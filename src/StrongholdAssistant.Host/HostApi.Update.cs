using ASPAssistant.Core.Services;
using System.Net.Http;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    public UpdateService Updater { get; set; } = new();
    private UpdateInfo? _availableUpdate;
    public bool UpdatePrepared { get; private set; }

    private object ReportUpdate(object status)
    {
        Changed?.Invoke("update", status);
        return status;
    }

    public async Task<object> CheckUpdateAsync()
    {
        if (Updater.IsDevBuild) return new { state = "latest" };
        ReportUpdate(new { state = "checking" });
        _availableUpdate = await Updater.CheckForUpdateAsync();
        if (Updater.LastError is { } message) return ReportUpdate(new { state = "failed", message });
        return _availableUpdate is { } info
            ? ReportUpdate(new { state = "available", version = info.Version, notesUrl = info.NotesUrl })
            : ReportUpdate(new { state = "latest" });
    }

    public async Task ApplyUpdateAsync()
    {
        if (_availableUpdate is not { } info) return;
        UpdatePrepared = false;
        var lastProgress = -0.05;
        try
        {
            await Updater.DownloadAndApplyUpdateAsync(info, new UpdateProgress(value =>
            {
                if (value < 1 && value - lastProgress < 0.05) return;
                lastProgress = value;
                ReportUpdate(new { state = "downloading", version = info.Version, progress = value });
            }));
            ReportUpdate(new { state = "ready", version = info.Version });
            UpdatePrepared = true;
        }
        catch (Exception ex) when (ex is UpdateIntegrityException or IOException or HttpRequestException or TaskCanceledException
                                   or UnauthorizedAccessException or InvalidDataException)
        {
            ReportUpdate(new { state = "failed", message = UpdateService.DescribeError(ex) });
        }
    }

    private sealed class UpdateProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
