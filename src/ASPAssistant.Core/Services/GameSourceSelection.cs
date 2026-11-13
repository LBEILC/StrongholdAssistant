namespace ASPAssistant.Core.Services;

public sealed record MumuDevice(string Name, string AdbPath, string Serial, string Config)
{
    public int? DisplayId { get; init; }
    public bool ProbeFailed { get; init; }
}
public sealed record MumuWindow(IntPtr Handle, string Name);
public sealed record GameSourceSelection(string? Source, IntPtr Window, MumuDevice? Device, string? Problem)
{
    public const string AdbProblem = "MuMu 已打开，但连不上它的 ADB。请确认模拟器设置里开启了 ADB 调试，或重启模拟器后再试。";
    public const string GameMissingProblem = "MuMu 里没找到正在运行的明日方舟";

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
        var candidates = serial.Length == 0
            ? devices.Where(d => d.DisplayId is not null).ToArray()
            : devices.Where(d => d.Serial == serial).ToArray();
        if (serial.Length == 0 && candidates.Length > 1)
            return new("mumu", windows[0].Handle, null, $"找到 {candidates.Length} 个 MuMu 实例，请在设置里选一个");
        var device = candidates.SingleOrDefault();
        if (serial.Length == 0 && device is null && devices.Count > 0 && !devices.Any(d => d.ProbeFailed))
            return new("mumu", windows[0].Handle, null, GameMissingProblem);
        if (device is null) return new("mumu", windows[0].Handle, null, AdbProblem);
        if (device.ProbeFailed) return new("mumu", windows[0].Handle, null, AdbProblem);
        if (device.DisplayId is null) return new("mumu", windows[0].Handle, null, GameMissingProblem);
        // TODO(design): 多开窗口对应
        var window = windows.FirstOrDefault(w => w.Name == device.Name) ?? windows[0];
        return new("mumu", window.Handle, device, null);
    }
}
