using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace ASPAssistant.Core.Services;

public static class MuMuFallback
{
    private static readonly object Gate = new();
    private static long _discoverAfter;
    private static IReadOnlyList<EmulatorDevice> _devices = [];

    [DllImport("kernel32.dll")]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags,
        StringBuilder path, ref uint size);

    public static IReadOnlyList<EmulatorDevice> UseIfMissing(IReadOnlyList<EmulatorDevice> devices,
        Func<IReadOnlyList<EmulatorDevice>> discover) => devices.Count > 0 ? devices : discover();

    // 桥接网络下 MaaToolkit 给的是「模拟器局域网 IP:16384」，但 16384 只在本机转发；桥接时 ADB 要连「模拟器 IP:5555」（诊断包 41，用户实测）
    public static string BridgedSerial(string serial) =>
        Regex.Replace(serial, @"^(?!127\.)(\d{1,3}(?:\.\d{1,3}){3}):\d+$", "$1:5555");

    /// <summary>emulator-5554 这类本机串号和 127.x 地址算本机；局域网 IP、主机名不算</summary>
    public static bool IsLoopback(string serial) => !Regex.IsMatch(serial, @"^(?!127\.)[^:]+:\d+$") || serial.StartsWith("localhost:");

    public static string? FindAdb(string processPath) => FindFile(processPath, "adb.exe", Path.Combine("shell", "adb.exe"));

    public static EmulatorDevice? FindRunningAdb(string source)
    {
        foreach (var (name, kind) in new[] { ("MuMuNxMain", "mumu"), ("MuMuNxDevice", "mumu"),
            ("MuMuNxService", "mumu"), ("MuMuPlayer", "mumu"), ("dnplayer", "ldplayer") })
        {
            if (source != "auto" && source != kind) continue;
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    using var handle = OpenProcess(0x1000, false, process.Id);
                    if (handle.IsInvalid) continue;
                    var path = new StringBuilder(32768);
                    var size = (uint)path.Capacity;
                    if (!QueryFullProcessImageName(handle, 0, path, ref size)) continue;
                    var adb = FindAdb(path.ToString());
                    if (adb is not null)
                    {
                        var window = kind == "ldplayer" ? process.MainWindowHandle : IntPtr.Zero;
                        var render = window == IntPtr.Zero ? IntPtr.Zero
                            : ASPAssistant.Core.Interop.User32.FindChildWindow(window, "RenderWindow");
                        var valid = kind == "ldplayer" && LdPlayerWindows.IsValid(window, render);
                        return new(name, adb, "", "{}", kind)
                        {
                            Window = valid ? window : IntPtr.Zero,
                            RenderWindow = valid ? render : IntPtr.Zero
                        };
                    }
                }
            }
        }
        return null;
    }

    private static string? FindFile(string processPath, params string[] names)
    {
        var directory = Path.GetDirectoryName(processPath);
        for (var level = 0; level <= 2 && directory is not null; level++)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(directory, name);
                if (File.Exists(path)) return path;
            }
            directory = Path.GetDirectoryName(directory);
        }
        return null;
    }

    public static IReadOnlyList<string> ParseDevices(string output) => Regex.Matches(output,
            @"(?m)^\s*(127\.0\.0\.1:(\d+))\s+device(?:\s|$)")
        .Where(m => int.TryParse(m.Groups[2].Value, out var port) && port is > 0 and <= 65535)
        .Select(m => m.Groups[1].Value).Distinct().ToArray();

    private static IReadOnlyList<string> ParseManager(string output) => Regex.Matches(output,
            "\"adb_port\"\\s*:\\s*\"?(\\d+)")
        .Where(m => int.TryParse(m.Groups[1].Value, out var port) && port is > 0 and <= 65535)
        .Select(m => $"127.0.0.1:{m.Groups[1].Value}").Distinct().ToArray();

    public static IReadOnlyList<EmulatorDevice> DiscoverDevices()
    {
        lock (Gate)
        {
            if (Environment.TickCount64 < _discoverAfter) return _devices;
            _discoverAfter = Environment.TickCount64 + 5000;
            // 共用总预算，包含进程与文件发现；留少量时间用于退出清理。
            using var budget = new CancellationTokenSource(TimeSpan.FromMilliseconds(2800));
            var installations = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in new[] { "MuMuNxMain", "MuMuNxDevice", "MuMuNxService", "MuMuPlayer" })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (budget.IsCancellationRequested) continue;
                        using var handle = OpenProcess(0x1000, false, process.Id);
                        if (handle.IsInvalid) continue;
                        var path = new StringBuilder(32768);
                        var size = (uint)path.Capacity;
                        if (!QueryFullProcessImageName(handle, 0, path, ref size)) continue;
                        var adb = FindAdb(path.ToString());
                        if (adb is not null)
                            installations.TryAdd(adb, FindFile(path.ToString(), "MuMuManager.exe",
                                Path.Combine("shell", "MuMuManager.exe")));
                    }
                }
            }
            _devices = DiscoverAsync(installations, budget.Token).GetAwaiter().GetResult();
            return _devices;
        }
    }

    private static async Task<IReadOnlyList<EmulatorDevice>> DiscoverAsync(
        Dictionary<string, string?> installations, CancellationToken budget)
    {
        var devices = new List<EmulatorDevice>();
        foreach (var (adb, manager) in installations)
        {
            if (budget.IsCancellationRequested) break;
            IReadOnlyList<string> serials;
            if (manager is not null)
                serials = ParseManager(await RunAsync(manager, ["info", "-v", "all"], budget));
            else
            {
                // 并发探测八个端口，避免一个未启动的实例耗尽整个发现预算。
                await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
                    RunAsync(adb, ["connect", $"127.0.0.1:{16384 + 32 * i}"], budget)));
                serials = ParseDevices(await RunAsync(adb, ["devices"], budget));
            }
            devices.AddRange(serials.Select(serial =>
                new EmulatorDevice($"MuMu {serial.Split(':')[1]}", adb, serial, "{}", "mumu")));
        }
        return devices.DistinctBy(d => d.Serial).ToArray();
    }

    private static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken budget)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(budget);
        timeout.CancelAfter(1000);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error).WaitAsync(timeout.Token);
            return process.ExitCode == 0 ? await output : "";
        }
        catch (OperationCanceledException)
        {
            // 只终止本次启动的命令，不终止 ADB 服务或模拟器。
            try { process.Kill(); }
            catch (InvalidOperationException) { }
            return "";
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // 外部命令不可用，本轮没有可采信的输出。
            return "";
        }
    }
}
