namespace ASPAssistant.Core.Services;

public sealed record EmulatorDevice(string Name, string AdbPath, string Serial, string Config, string Kind = "mumu")
{
    public int? DisplayId { get; init; }
    public bool ProbeFailed { get; init; }
    public IntPtr Window { get; init; }
    public IntPtr RenderWindow { get; init; }
    public string DisplayName => Kind == "ldplayer" ? "雷电" : "MuMu";
}
public sealed record EmulatorWindow(IntPtr Handle, string Name);
public sealed record GameSourceSelection(string? Source, IntPtr Window, EmulatorDevice? Device, string? Problem)
{
    public const string AdbProblem = "MuMu 已打开，但连不上它的 ADB。请确认模拟器设置里开启了 ADB 调试，或重启模拟器后再试。";
    public const string LdAdbProblem = "雷电已打开，但连不上它的 ADB。请在雷电设置 → 其他设置里把「ADB 调试」改成「开启本地连接」，然后重启模拟器。";
    public const string GameMissingProblem = "MuMu里没找到正在运行的明日方舟";

    public static GameSourceSelection Find(string source, string serial, Func<IntPtr> findPc,
        Func<IReadOnlyList<EmulatorWindow>> findMumu, Func<IReadOnlyList<EmulatorDevice>> findDevices)
    {
        if (source is "auto" or "pc")
        {
            var pc = findPc();
            if (pc != IntPtr.Zero) return new("pc", pc, null, null);
            if (source == "pc") return new(null, IntPtr.Zero, null, null);
        }
        var windows = source == "ldplayer" ? [] : findMumu();
        var devices = findDevices().Where(d => source == "auto" || d.Kind == source).ToArray();
        if (windows.Count == 0 && devices.Length == 0) return new(null, IntPtr.Zero, null, null);
        var candidates = serial.Length == 0
            ? devices.Where(d => d.DisplayId is not null).ToArray()
            : devices.Where(d => d.Serial == serial).ToArray();
        var device = candidates.FirstOrDefault();
        var kind = device?.Kind ?? devices.FirstOrDefault()?.Kind ?? "mumu";
        var window = device is { Kind: "ldplayer" } ? device.Window
            : kind == "ldplayer" ? devices[0].Window
            // TODO(design): MuMu 多开窗口对应
            : (windows.FirstOrDefault(w => w.Name == device?.Name) ?? windows.FirstOrDefault())?.Handle ?? IntPtr.Zero;
        if (serial.Length == 0 && candidates.Length > 1)
            return new(kind, window, null, $"找到 {candidates.Length} 个模拟器实例，请在设置里选一个");
        var name = kind == "ldplayer" ? "雷电" : "MuMu";
        if (source == "auto" && devices.Any(d => d.Kind == "ldplayer")
            && (windows.Count > 0 || devices.Any(d => d.Kind == "mumu"))) name = "模拟器";
        var missing = $"{name}里没找到正在运行的明日方舟";
        var adbProblem = kind == "ldplayer" ? LdAdbProblem : AdbProblem;
        if (serial.Length == 0 && device is null && devices.Length > 0 && !devices.Any(d => d.ProbeFailed))
            return new(kind, window, null, missing);
        if (device is null || device.ProbeFailed) return new(kind, window, null, adbProblem);
        if (device.DisplayId is null) return new(kind, window, null, missing);
        return new(kind, window, device, null);
    }
}
