namespace ASPAssistant.Core.Services;

public sealed record MumuDevice(string Name, string AdbPath, string Serial, string Config);
public sealed record MumuWindow(IntPtr Handle, string Name);
public sealed record GameSourceSelection(string? Source, IntPtr Window, MumuDevice? Device, string? Problem)
{
    public const string AdbProblem = "MuMu 已打开，但连不上它的 ADB。请确认模拟器设置里开启了 ADB 调试，或重启模拟器后再试。";

    public static GameSourceSelection Find(string source, string serial, Func<IntPtr> findPc,
        Func<IReadOnlyList<MumuWindow>> findMumu, Func<IReadOnlyList<MumuDevice>> findDevices)
    {
        if (source != "mumu")
        {
            var pc = findPc();
            if (pc != IntPtr.Zero) return new("pc", pc, null, null);
            if (source == "pc") return new(null, IntPtr.Zero, null, null);
        }
        var windows = findMumu();
        if (windows.Count == 0) return new(null, IntPtr.Zero, null, null);
        var devices = findDevices();
        if (serial.Length == 0 && devices.Count > 1)
            return new("mumu", windows[0].Handle, null, $"找到 {devices.Count} 个 MuMu 实例，请在设置里选一个");
        var device = serial.Length == 0 ? devices.SingleOrDefault() : devices.FirstOrDefault(d => d.Serial == serial);
        if (device is null) return new("mumu", windows[0].Handle, null, AdbProblem);
        // TODO(design): 多开窗口对应
        var window = windows.FirstOrDefault(w => w.Name == device.Name) ?? windows[0];
        return new("mumu", window.Handle, device, null);
    }
}
