namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private CancellationTokenSource? _continuousStop;
    private Task _continuousTask = Task.CompletedTask;
    private string? _continuousReason;
    public bool IsContinuous => _continuousStop is not null;

    public Task ScanContinuousAsync(bool on)
    {
        if (!on) return StopContinuousAsync();
        if (IsContinuous) return Task.CompletedTask;
        _continuousReason = null;
        _continuousStop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        _continuousTask = RunContinuousAsync(_continuousStop);
        return Task.CompletedTask;
    }

    public Task StopContinuousAsync(bool windowLost = false)
    {
        if (_continuousStop is { } stop)
        {
            _continuousReason ??= windowLost ? "游戏窗口丢失，持续识别已结束。" : "持续识别已停止，";
            // 只中止两次识别之间的等待；正在处理的截图仍然完成。
            stop.Cancel();
        }
        return _continuousTask;
    }

    private async Task RunContinuousAsync(CancellationTokenSource stop)
    {
        int failures = 0;
        bool succeeded = false;
        var added = new HashSet<string>();
        while (!stop.IsCancellationRequested)
        {
            var previous = Match.BannedOperators.ToHashSet();
            try
            {
                await RecordInformationAsync(true);
                added.UnionWith(Match.BannedOperators.Except(previous));
                failures = 0;
                succeeded = true;
            }
            catch (Exception) { failures++; } // 截屏和识别边界的错误均计作本次失败。
            if (stop.IsCancellationRequested || failures == 3) break;
            try { await Task.Delay(500, stop.Token); }
            catch (OperationCanceledException) { break; }
        }

        bool ok = true;
        string summary = $"本次新识别到 {added.Count} 名禁用干员，共 {Match.BannedOperators.Length} 名。";
        string message;
        if (_continuousReason is { } reason) message = reason + summary;
        else if (stop.IsCancellationRequested) message = "持续识别满 90 秒，已自动结束。" + summary;
        else if (succeeded) message = "连续 3 次没找到开局信息页，持续识别已结束。" + summary;
        else
        {
            ok = false;
            message = "没找到开局信息页，持续识别已结束。请先打开本局信息页再点「持续识别」。";
        }
        _continuousStop = null;
        stop.Dispose();
        Changed?.Invoke("scan", new ScanStatus(false, message, ok, false));
    }
}
