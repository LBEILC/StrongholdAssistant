using ASPAssistant.Core.Services;
using ASPAssistant.Core;
using System.Net.Http;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    public UpdateService Updater { get; set; } = new();
    private UpdateInfo? _availableUpdate;
    public bool UpdatePrepared { get; private set; }
    private object? _downloadStatus;
    // 玩家点过「更新」「安装并重启」：下完就装。自动下载的（v1.0.4）下完等玩家点
    private bool _installWhenReady;
    public event Action? UpdateReady;

    private object ReportUpdate(object status)
    {
        Changed?.Invoke("update", status);
        return status;
    }

    internal async Task RecoverUpdateOnStartupAsync()
    {
        if (AssistantProfile.Current.Name is not null) return;
        if (Updater.PreviousInstallFailure is { } reason)
            ReportUpdate(UpdateFailure("上次更新没装上：" + reason));
        if (!Updater.RetryFullOnStartup) return;
        AppLogger.Info("Update", "开始全量回退检查");
        await CheckUpdateAsync();
        if (_availableUpdate is not null) await ApplyUpdateAsync();
    }

    public async Task<object> CheckUpdateAsync()
    {
        if (AssistantProfile.Current.Name is not null) return new { state = "latest" };
        if (Volatile.Read(ref _downloadStatus) is { } downloading) return downloading;
        if (UpdatePrepared && _availableUpdate is { } prepared) return ReportUpdate(new { state = "ready", version = prepared.Version });
        Updater.GetUpdateProxy = () => Settings.UpdateProxy;
        Updater.GetGithubToken = () => Settings.GitHubToken;
        if (Updater.IsDevBuild) return new { state = "latest" };
        ReportUpdate(new { state = "checking" });
        _availableUpdate = Settings.UpdateSource == "mirrorchyan"
            ? await Updater.CheckMirrorChyanAsync(Settings.MirrorChyanCdk)
            : await Updater.CheckForUpdateAsync();
        if (Updater.LastError is { } message) return ReportUpdate(UpdateFailure(message));
        var status = new Dictionary<string, object> { ["state"] = _availableUpdate is null ? "latest" : "available" };
        if (Updater.CdkExpiresAt is { } expires) status["cdkExpiresAt"] = expires;
        SetGitHubTokenExpiry(Updater.TokenExpiresAt);
        if (Settings.GitHubTokenExpiresAt is { Length: > 0 } tokenExpiry) status["githubTokenExpiresAt"] = tokenExpiry;
        if (_availableUpdate is not { } info) return ReportUpdate(status);
        status["version"] = info.Version;
        status["notesUrl"] = info.NotesUrl;
        status["source"] = info.Source;
        if (info.Notes is { } notes) status["notes"] = notes;
        return ReportUpdate(status);
    }

    /// <summary>
    /// 记下 GitHub 给的 Token 到期时间：下次启动直接显示，不用再问一次。
    /// GitHub 只在响应的 Token 永不过期、或这次校验没带 Token 时不发这个头，两种情况都按「永不过期」记。
    /// </summary>
    private void SetGitHubTokenExpiry(string? expiresAt)
    {
        var expiry = expiresAt ?? "";
        if (Settings.GitHubTokenExpiresAt == expiry) return;
        SettingsStore.SaveGitHubTokenExpiry(expiry);
        lock (_fieldGate) Settings = Settings with { GitHubTokenExpiresAt = expiry };
    }

    /// <summary>校验用户填的 GitHub Token：有效时返回到期时间，无效时给出原因。</summary>
    public async Task<object> ValidateGithubTokenAsync(string token)
    {
        Updater.GetUpdateProxy = () => Settings.UpdateProxy;
        var check = await Updater.CheckGitHubTokenAsync(token);
        if (check.Valid) SetGitHubTokenExpiry(check.ExpiresAt);
        return new { valid = check.Valid, expiresAt = check.ExpiresAt, message = check.Message };
    }

    /// <summary>玩家点「更新」：没下就下、在下就等，下好了马上安装并重启。</summary>
    public Task ApplyUpdateAsync()
    {
        if (AssistantProfile.Current.Name is not null) throw new ArgumentException("请在默认配置中安装更新。");
        _installWhenReady = true;
        if (UpdatePrepared) { UpdateReady?.Invoke(); return Task.CompletedTask; }
        return DownloadUpdateAsync();
    }

    /// <summary>后台下载新版本，下好推送 ready，等玩家点安装（v1.0.4）。</summary>
    public Task DownloadUpdateAsync()
    {
        if (_availableUpdate is not { } info || UpdatePrepared) return Task.CompletedTask;
        var status = new { state = "downloading", version = info.Version, progress = 0d };
        if (Interlocked.CompareExchange(ref _downloadStatus, status, null) is not null) return Task.CompletedTask;
        Updater.GetUpdateProxy = () => Settings.UpdateProxy;
        UpdatePrepared = false;
        ReportUpdate(status);
        _ = Task.Run(() => PrepareUpdateAsync(info));
        return Task.CompletedTask;
    }

    public void ClearPreparedUpdate()
    {
        UpdatePrepared = false;
        Updater.ClearPendingUpdate();
    }

    private async Task PrepareUpdateAsync(UpdateInfo info)
    {
        var lastProgress = -0.05;
        try
        {
            AppLogger.Info("Update", $"开始下载 {info.AssetName}");
            if (info.Source == "mirrorchyan")
            {
                var refreshed = await Updater.ReadMirrorChyanAsync(Settings.MirrorChyanCdk);
                if (refreshed is null)
                {
                    ReportUpdate(UpdateFailure(Updater.LastError!));
                    return;
                }
                if (UpdateService.TryParseVersion(refreshed.Version)! <= Updater.CurrentVersion)
                {
                    ReportUpdate(UpdateFailure("Mirror酱 上的版本不比当前新，已取消更新"));
                    return;
                }
                info = refreshed;
            }
            await Updater.DownloadAndApplyUpdateAsync(info, new UpdateProgress(value =>
            {
                if (value < 1 && value - lastProgress < 0.05) return;
                lastProgress = value;
                var status = new { state = "downloading", version = info.Version, progress = value };
                Volatile.Write(ref _downloadStatus, status);
                ReportUpdate(status);
            }));
            UpdatePrepared = true;
            AppLogger.Info("Update", $"更新已准备好 v{info.Version}");
            ReportUpdate(new { state = "ready", version = info.Version });
            if (_installWhenReady) UpdateReady?.Invoke();
        }
        catch (Exception ex) when (ex is UpdateIntegrityException or IOException or HttpRequestException or TaskCanceledException
                                   or UnauthorizedAccessException or InvalidDataException)
        {
            var message = UpdateService.DescribeError(ex, Updater.GetUpdateProxy());
            if (info.Source == "mirrorchyan") message = message.Replace("更新源", "Mirror酱");
            ReportUpdate(UpdateFailure(message));
        }
        finally { Volatile.Write(ref _downloadStatus, null); }
    }

    private Dictionary<string, object> UpdateFailure(string message)
    {
        AppLogger.Warn("Update", message);
        var status = new Dictionary<string, object> { ["state"] = "failed", ["message"] = message };
        if (Updater.CdkProblem) status["cdkProblem"] = true;
        if (Updater.TokenProblem) status["githubTokenProblem"] = true;
        if (Updater.RateLimited) status["githubRateLimited"] = true;
        return status;
    }

    private sealed class UpdateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
