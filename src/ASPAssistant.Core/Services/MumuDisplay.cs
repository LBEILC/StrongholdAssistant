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

    public static MumuDevice Inspect(MumuDevice device)
    {
        try
        {
            var windows = Encoding.UTF8.GetString(RunAdb(device, "shell", "dumpsys", "window", "windows"));
            return device with { DisplayId = FindGameDisplay(windows), ProbeFailed = false };
        }
        catch
        {
            return device with { DisplayId = null, ProbeFailed = true };
        }
    }

    public static byte[] Capture(MumuDevice device) => RunAdb(device, "exec-out", "screencap", "-p", "-d",
        device.DisplayId!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static byte[] RunAdb(MumuDevice device, params string[] arguments)
    {
        var start = new ProcessStartInfo(device.AdbPath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-s");
        start.ArgumentList.Add(device.Serial);
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
        if (process.ExitCode != 0) throw new IOException(error.Result);
        return output.ToArray();
    }
}
