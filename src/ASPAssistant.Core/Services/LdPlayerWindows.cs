using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

public sealed record LdPlayerWindow(int Index, string Name, IntPtr Window, IntPtr RenderWindow);

public static class LdPlayerWindows
{
    public static Encoding OutputEncoding { get; } = GetEncoding();
    private static Encoding GetEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    public static IReadOnlyList<LdPlayerWindow> Parse(byte[] output)
    {
        var windows = new List<LdPlayerWindow>();
        foreach (var line in OutputEncoding.GetString(output).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Trim().Split(',');
            if (fields.Length < 7 || !int.TryParse(fields[0], out var index)
                || !long.TryParse(fields[2], out var window) || !long.TryParse(fields[3], out var render)
                || !int.TryParse(fields[5], out var pid) || pid <= 0
                || !int.TryParse(fields[6], out var vboxPid) || vboxPid <= 0) continue;
            windows.Add(new(index, fields[1], (IntPtr)window, (IntPtr)render));
        }
        return windows;
    }

    public static bool IsValid(IntPtr window, IntPtr render) =>
        User32.IsWindow(window) && User32.IsWindowVisible(window)
        && User32.GetWindowClass(window) == "LDPlayerMainFrame"
        && User32.IsWindow(render) && User32.IsWindowVisible(render)
        && User32.GetWindowClass(render) == "RenderWindow";

    [DllImport("kernel32.dll")]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags,
        StringBuilder path, ref uint size);

    public static IReadOnlyList<EmulatorDevice> FromListOutput(string directory, byte[] output) =>
        Parse(output).Select(window =>
        {
            var valid = IsValid(window.Window, window.RenderWindow);
            return new EmulatorDevice(window.Name, Path.Combine(directory, "adb.exe"),
                $"emulator-{5554 + window.Index * 2}", "{}", "ldplayer")
            {
                Window = valid ? window.Window : IntPtr.Zero,
                RenderWindow = valid ? window.RenderWindow : IntPtr.Zero
            };
        }).ToArray();

    public static IReadOnlyList<EmulatorDevice> DiscoverDevices()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcessesByName("dnplayer"))
        {
            using (process)
            {
                // 有限查询权限可读取管理员进程的路径，不读取进程内存。
                using var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, process.Id);
                if (handle.IsInvalid) continue;
                var path = new StringBuilder(32768);
                var size = (uint)path.Capacity;
                if (QueryFullProcessImageName(handle, 0, path, ref size))
                    directories.Add(Path.GetDirectoryName(path.ToString())!);
            }
        }

        var devices = new List<EmulatorDevice>();
        foreach (var directory in directories)
        {
            try
            {
                var start = new ProcessStartInfo(Path.Combine(directory, "ldconsole.exe"), "list2")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
                };
                using var process = Process.Start(start)!;
                using var output = new MemoryStream();
                process.StandardOutput.BaseStream.CopyTo(output);
                process.WaitForExit();
                if (process.ExitCode == 0)
                    devices.AddRange(FromListOutput(directory, output.ToArray()));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                // 外部控制台不可用时，本轮跳过这个安装目录。
            }
        }
        return devices;
    }
}
