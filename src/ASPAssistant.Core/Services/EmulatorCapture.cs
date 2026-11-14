using System.Text;

namespace ASPAssistant.Core.Services;

// 由连接服务的锁保护；成功方法与失败去重在发现刷新时保留。
public sealed class EmulatorCapture
{
    private readonly Dictionary<(string Serial, int Display), (string Method, string Id)> _methods = new();
    private readonly HashSet<(string Serial, int Display, string Method, string Code, string Error)> _failures = [];
    private readonly Action<string> _warn;

    public EmulatorCapture(Action<string>? warn = null) =>
        _warn = warn ?? (message => AppLogger.Warn("Emulator", message));

    public void LogFailure(EmulatorDevice device, string method, string code, string error)
    {
        if (!_failures.Add((device.Serial, device.DisplayId!.Value, method, code, error))) return;
        var excerpt = error[..Math.Min(error.Length, 200)].Replace('\r', ' ').Replace('\n', ' ');
        _warn($"serial={device.Serial} display={device.DisplayId} capture={method} exit={code} stderr={excerpt}");
    }

    public byte[]? Capture(EmulatorDevice device, Func<byte[], bool> valid, out string method) =>
        Capture(device, EmulatorDisplay.RunAdb, valid, out method);

    public byte[]? Capture(EmulatorDevice device,
        Func<EmulatorDevice, string[], (int ExitCode, byte[] Output, string Error)> runAdb,
        Func<byte[], bool> valid, out string method, Func<byte[]?>? captureExtras = null)
    {
        var key = (device.Serial, device.DisplayId!.Value);
        if (key.Value == 0) throw new ArgumentException("此路径仅用于非主屏截图", nameof(device));
        byte[]? TryCapture((string Method, string Id) candidate)
        {
            var code = "n/a";
            try
            {
                var result = candidate.Method == "MuMu extras"
                    ? (ExitCode: 0, Output: captureExtras!() ?? [], Error: "")
                    : runAdb(device, ["-s", device.Serial, "exec-out", "screencap", "-p", "-d", candidate.Id]);
                code = result.ExitCode.ToString();
                if (result.ExitCode != 0) LogFailure(device, candidate.Method, code, result.Error);
                else if (!valid(result.Output)) LogFailure(device, candidate.Method, code, "图像为空、无效或不是横屏 16:9");
                else
                {
                    _methods[key] = candidate;
                    return result.Output;
                }
            }
            catch (Exception ex) { LogFailure(device, candidate.Method, code, ex.Message); }
            return null;
        }

        if (_methods.TryGetValue(key, out var cached))
        {
            var png = TryCapture(cached);
            if (png is not null) { method = cached.Method; return png; }
            _methods.Remove(key);
        }

        if (device.Kind == "mumu" && captureExtras is not null && cached.Method != "MuMu extras")
        {
            var png = TryCapture(("MuMu extras", ""));
            if (png is not null) { method = "MuMu extras"; return png; }
        }

        const string mappedMethod = "ADB SurfaceFlinger display";
        string? physicalId = null;
        try
        {
            var result = runAdb(device, ["-s", device.Serial, "shell", "dumpsys", "display"]);
            if (result.ExitCode != 0) LogFailure(device, mappedMethod, result.ExitCode.ToString(), result.Error);
            else
            {
                physicalId = EmulatorDisplay.FindPhysicalDisplayId(Encoding.UTF8.GetString(result.Output), key.Value);
                if (physicalId is null) LogFailure(device, mappedMethod, "0", "未找到逻辑 display 对应的 local: 物理 ID");
            }
        }
        catch (Exception ex) { LogFailure(device, mappedMethod, "n/a", ex.Message); }

        if (physicalId is not null)
        {
            var png = TryCapture((mappedMethod, physicalId));
            if (png is not null) { method = mappedMethod; return png; }
        }
        method = "ADB logical display";
        return TryCapture((method, key.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
