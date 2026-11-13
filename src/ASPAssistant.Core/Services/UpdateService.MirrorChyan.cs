using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Services;

public partial class UpdateService
{
    public const string MirrorChyanOverrideVariable = "STRONGHOLD_MIRRORCHYAN_API";
    public string? CdkExpiresAt { get; private set; }
    public bool CdkProblem { get; private set; }

    public async Task<UpdateInfo?> CheckMirrorChyanAsync(string cdk, CancellationToken ct = default)
    {
        var info = await ReadMirrorChyanAsync(cdk, ct);
        if (info is null || TryParseVersion(info.Version)! <= CurrentVersion) return null;
        var notesUrl = $"https://github.com/LBEILC/StrongholdAssistant/releases/download/{info.TagName}/release-notes.json";
        return info with { Notes = await ReadReleaseNotesAsync(notesUrl, info.Version, ct) };
    }

    // 下载前再次请求，避免沿用检查时取得的限时地址。
    public async Task<UpdateInfo?> ReadMirrorChyanAsync(string cdk, CancellationToken ct = default)
    {
        LastError = null;
        CdkExpiresAt = null;
        CdkProblem = false;
        cdk = cdk.Trim();
        var endpoint = mirrorChyanUrl ?? Environment.GetEnvironmentVariable(MirrorChyanOverrideVariable);
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = "https://mirrorchyan.com/api/resources/StrongholdAssistant/latest";
        var url = endpoint + (endpoint.Contains('?') ? "&" : "?")
            + $"os=win&arch={(IsInstalled ? "x64" : "")}&user_agent=StrongholdAssistant"
            + (cdk.Length == 0 ? "" : "&cdk=" + Uri.EscapeDataString(cdk));
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            // 错误响应也有业务码，必须先读取 JSON。
            var result = JsonSerializer.Deserialize<MirrorResponse>(await ReadBodyAsync(response.Content, ct),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (result is null) throw new JsonException();
            if (result.Code != 0)
            {
                CdkProblem = result.Code is >= 7001 and <= 7005;
                LastError = result.Code switch
                {
                    7001 => "Mirror酱 CDK 已过期。",
                    7002 => "Mirror酱 CDK 不对，请检查有没有填错。",
                    7003 => "这个 CDK 今天的下载次数用完了，明天再试或先改用 GitHub。",
                    7004 => "这个 CDK 不能用来下载卫戍协议助手。",
                    7005 => "这个 CDK 已被封禁。",
                    8001 => "Mirror酱 上还没有这个版本的包。",
                    _ => "Mirror酱 返回错误：" + result.Msg
                };
                if (cdk.Length > 0) LastError = LastError.Replace(cdk, "[已隐藏]");
                return null;
            }
            response.EnsureSuccessStatusCode();
            var data = result.Data;
            var version = TryParseVersion(data?.VersionName ?? "");
            if (version is null) { LastError = "Mirror酱 返回的数据不完整"; return null; }
            if (data!.CdkExpiredTime is { } expires)
                CdkExpiresAt = DateTimeOffset.FromUnixTimeSeconds(expires).ToString("O");
            var tag = "v" + version;
            var asset = $"StrongholdAssistant-{tag}-{(IsInstalled ? "setup.exe" : "win-x64.zip")}";
            var root = $"https://github.com/LBEILC/StrongholdAssistant/releases/download/{tag}/";
            var mirror = !string.IsNullOrWhiteSpace(data.Url);
            return new(tag, version.ToString(), "", mirror ? data.Url! : root + asset,
                asset, mirror ? null : root + ChecksumsAssetName,
                $"https://github.com/LBEILC/StrongholdAssistant/releases/tag/{tag}")
            { Source = mirror ? "mirrorchyan" : "github", Sha256 = data.Sha256 };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or ArgumentOutOfRangeException)
        {
            LastError = ex is JsonException ? "Mirror酱 返回的不是有效 JSON"
                : ex is ArgumentOutOfRangeException ? "Mirror酱 返回的数据不完整"
                : DescribeError(ex, GetUpdateProxy()).Replace("更新源", "Mirror酱");
            return null;
        }
    }

    private record MirrorResponse(int Code, string? Msg, MirrorData? Data);
    private record MirrorData(
        [property: JsonPropertyName("version_name")] string? VersionName,
        string? Url, string? Sha256, long? Filesize,
        [property: JsonPropertyName("cdk_expired_time")] long? CdkExpiredTime);
}
