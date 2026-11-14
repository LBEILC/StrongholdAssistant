using System.Diagnostics;
using ASPAssistant.Core;

namespace StrongholdAssistant.Host;

internal static class InstanceRecovery
{
    internal static bool ShouldAcknowledgeActivation(bool uiReady, bool windowVisible) =>
        uiReady && windowVisible;

    internal static bool ShouldStop(int processId, int sessionId, string? imagePath,
        int currentProcessId, int currentSessionId, string currentImagePath) =>
        processId != currentProcessId && sessionId == currentSessionId
        && string.Equals(imagePath, currentImagePath, StringComparison.OrdinalIgnoreCase);

    internal static Task<bool> WaitForResponseAsync(Func<TimeSpan, Task<bool>> wait) =>
        wait(TimeSpan.FromSeconds(3));

    internal static void StopOtherInstances()
    {
        using var current = Process.GetCurrentProcess();
        var processes = Process.GetProcessesByName("StrongholdAssistant");
        var stopped = new List<(Process Process, TimeSpan Uptime)>();
        try
        {
            foreach (var process in processes)
            {
                if (process.Id == current.Id || process.HasExited || process.SessionId != current.SessionId) continue;
                if (!ShouldStop(process.Id, process.SessionId, process.MainModule?.FileName,
                        current.Id, current.SessionId, Environment.ProcessPath!)) continue;
                var uptime = DateTime.Now - process.StartTime;
                process.Kill();
                stopped.Add((process, uptime));
            }
            // 所有目标共用五秒退出期限，不逐个累计等待。
            var elapsed = Stopwatch.StartNew();
            foreach (var (process, uptime) in stopped)
            {
                var remaining = Math.Max(0, 5000 - (int)elapsed.ElapsedMilliseconds);
                if (!process.WaitForExit(remaining)) throw new TimeoutException();
                AppLogger.Info("Instance", $"已结束进程 {process.Id}，已运行 {uptime.TotalSeconds:F0} 秒。");
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }
}
