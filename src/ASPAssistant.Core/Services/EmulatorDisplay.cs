using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public static class EmulatorDisplay
{
    // 按 WindowState 分块，避免把其他窗口的 display 或已退出的 Activity 关联到游戏。
    public static int? FindGameDisplay(string windows) => FindGameDisplay(windows, out _);

    public static int? FindGameDisplay(string windows, out string? gamePackage)
    {
        gamePackage = null;
        foreach (var block in Regex.Split(windows, @"(?m)^\s*Window #\d+ "))
        {
            var header = block.Split('\n')[0];
            var package = Regex.Match(header, @"\bu\d+ (com\.hypergryph\.arknights(?:\.bilibili)?)/");
            if (!package.Success) continue;
            if (!Regex.IsMatch(block, @"\bmHasSurface=true\b")) continue;
            var display = Regex.Match(block, @"\bmDisplayId=(\d+)\b");
            if (display.Success && int.TryParse(display.Groups[1].Value, out var id))
            {
                gamePackage = package.Groups[1].Value;
                return id;
            }
        }
        return null;
    }

    private static readonly ConcurrentDictionary<(string Serial, string Step, string Code, string Error), byte> LoggedFailures = new();

    public static EmulatorDevice Inspect(EmulatorDevice device) => Inspect(device, RunAdb);

    public static EmulatorDevice Inspect(EmulatorDevice device,
        Func<EmulatorDevice, string[], (int ExitCode, byte[] Output, string Error)> runAdb)
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
        string? gamePackage = null;
        return device with
        {
            DisplayId = windows is { ExitCode: 0 } result
                ? FindGameDisplay(Encoding.UTF8.GetString(result.Output), out gamePackage) : 0,
            GamePackage = gamePackage,
            ProbeFailed = false
        };
    }

    private static void LogProbeFailure(string serial, string step, string code, string error)
    {
        var excerpt = error[..Math.Min(error.Length, 200)].Replace('\r', ' ').Replace('\n', ' ');
        if (LoggedFailures.TryAdd((serial, step, code, excerpt), 0))
            AppLogger.Warn("Emulator", $"serial={serial} step={step} exit={code} stderr={excerpt}");
    }

    // Android 12 LogicalDisplay.dumpLocked 的 mBaseDisplayInfo.uniqueId 来自设备；
    // LocalDisplayAdapter 的 local: 后缀是 SurfaceFlinger 的物理 display ID。
    public static string? FindPhysicalDisplayId(string displays, int logicalId)
    {
        foreach (var block in Regex.Split(displays, @"(?m)^\s*(?=mDisplayId=)"))
        {
            var id = Regex.Match(block, @"\AmDisplayId=(\d+)\b");
            if (!id.Success || !int.TryParse(id.Groups[1].Value, out var value) || value != logicalId) continue;
            var physical = Regex.Match(block, "mBaseDisplayInfo=DisplayInfo\\{[^\\r\\n]*?uniqueId \"local:(\\d+)\"");
            if (physical.Success && ulong.TryParse(physical.Groups[1].Value, out var number))
                return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return null;
    }

    internal static (int ExitCode, byte[] Output, string Error) RunAdb(EmulatorDevice device, string[] arguments)
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
