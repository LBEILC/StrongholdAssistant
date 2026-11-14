using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public static class MumuDisplay
{
    // 按 WindowState 分块，避免把其他窗口的 display 或已退出的 Activity 关联到游戏。
    public static int? FindGameDisplay(string windows)
    {
        foreach (var block in Regex.Split(windows, @"(?m)^\s*Window #\d+ "))
        {
            var header = block.Split('\n')[0];
            if (!Regex.IsMatch(header, @"\bu\d+ com\.hypergryph\.arknights(?:\.bilibili)?/")) continue;
            if (!Regex.IsMatch(block, @"\bmHasSurface=true\b")) continue;
            var display = Regex.Match(block, @"\bmDisplayId=(\d+)\b");
            if (display.Success && int.TryParse(display.Groups[1].Value, out var id)) return id;
        }
        return null;
    }

    private static readonly ConcurrentDictionary<(string Serial, string Step, string Code, string Error), byte> LoggedFailures = new();

    public static MumuDevice Inspect(MumuDevice device) => Inspect(device, RunAdb);

    public static MumuDevice Inspect(MumuDevice device,
        Func<MumuDevice, string[], (int ExitCode, byte[] Output, string Error)> runAdb)
    {
        (int ExitCode, byte[] Output, string Error)? Probe(string step, params string[] arguments)
        {
            try
            {
                var result = runAdb(device, arguments);
                if (result.ExitCode != 0)
                    LogProbeFailure(device.Serial, step, result.ExitCode.ToString(), result.Error);
                return result;
            }
            catch (Exception ex)
            {
                // 启动失败或超时没有可用退出码，仍交给后续连接处理。
                LogProbeFailure(device.Serial, step, "n/a", ex.Message);
                return null;
            }
        }

        if (Regex.IsMatch(device.Serial, @"^\S+:\d+$"))
            Probe("connect", "connect", device.Serial);
        var windows = Probe("dumpsys", "-s", device.Serial, "shell", "dumpsys", "window", "windows");
        return device with
        {
            DisplayId = windows is { ExitCode: 0 } result
                ? FindGameDisplay(Encoding.UTF8.GetString(result.Output)) : 0,
            ProbeFailed = false
        };
    }

    private static void LogProbeFailure(string serial, string step, string code, string error)
    {
        var excerpt = error[..Math.Min(error.Length, 200)].Replace('\r', ' ').Replace('\n', ' ');
        if (LoggedFailures.TryAdd((serial, step, code, excerpt), 0))
            AppLogger.Warn("MuMu", $"serial={serial} step={step} exit={code} stderr={excerpt}");
    }

    public static byte[] Capture(MumuDevice device)
    {
        var result = RunAdb(device, ["-s", device.Serial, "exec-out", "screencap", "-p", "-d",
            device.DisplayId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (result.ExitCode != 0) throw new IOException(result.Error);
        return result.Output;
    }

    private static (int ExitCode, byte[] Output, string Error) RunAdb(MumuDevice device, string[] arguments)
    {
        var start = new ProcessStartInfo(device.AdbPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        // 外部 ADB 进程不能无限占住连接锁。
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            Task.WhenAll(copy, error).GetAwaiter().GetResult();
            throw new IOException("ADB 请求超时");
        }
        Task.WhenAll(copy, error).GetAwaiter().GetResult();
        return (process.ExitCode, output.ToArray(), error.Result);
    }
}
