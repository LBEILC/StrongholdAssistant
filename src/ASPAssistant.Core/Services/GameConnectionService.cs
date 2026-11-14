// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
// 2026-11-12：MuMu 显示区域按实际截图比例映射。
// 2026-11-12：参考 PR #5，改为自动发现与来源选择，共享主窗口及画面区域；只保留截图控制器。
// 2026-11-14：按游戏所在 display 选择实例，非主屏显式指定 ADB 截图目标。
using ASPAssistant.Core.Interop;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

public sealed record GameConnectionState(string? Source = null, IntPtr Window = default,
    string? Problem = null, int Width = 0, int Height = 0);

public sealed class GameConnectionService : IDisposable
{
    private readonly object _gate = new();
    private readonly EmulatorCapture _capture = new();
    private (string Serial, int Display)? _loggedCapture;
    private MaaAdbController? _controller;
    private EmulatorDevice? _device;
    private IReadOnlyList<EmulatorDevice> _devices = [];
    private DateTime _discoverAfter, _retryAfter;
    private string _source = "auto", _serial = "";
    private bool _disposed;
    private GameConnectionState _state = new();
    public GameConnectionState State => Volatile.Read(ref _state);
    public bool IsAdb => State.Source is "mumu" or "ldplayer";
    public event Action<GameConnectionState>? Changed;

    public bool Configure(string source, string serial)
    {
        lock (_gate)
        {
            if (_source == source && _serial == serial) return false;
            _source = source;
            _serial = serial;
            _controller?.Dispose();
            _controller = null;
            _device = null;
            _loggedCapture = null;
            _discoverAfter = _retryAfter = default;
            SetState(new());
            return true;
        }
    }

    public static IReadOnlyList<EmulatorDevice> DiscoverEmulatorDevices()
    {
        using var found = MaaToolkit.Shared.AdbDevice.Find();
        var devices = found.Where(d => !d.Name.EndsWith("-LDPlayer", StringComparison.OrdinalIgnoreCase)
                && d.Name.Contains("mumu", StringComparison.OrdinalIgnoreCase))
            .Select(d => new EmulatorDevice(
                System.Text.RegularExpressions.Regex.Replace(d.Name, @"-MuMuPlayer\d*$", "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                d.AdbPath, d.AdbSerial, d.Config,
                "mumu")).ToArray();
        return MuMuFallback.UseIfMissing(devices, MuMuFallback.DiscoverDevices)
            .Concat(LdPlayerWindows.DiscoverDevices()).ToArray();
    }

    public static IReadOnlyList<EmulatorWindow> FindEmulatorWindows()
    {
        using var windows = MaaToolkit.Shared.Desktop.Window.Find();
        return windows.Where(w => User32.IsWindowVisible(w.Handle)
                && IsEmbeddedGameWindow(w.Handle))
            .Select(w => new EmulatorWindow(w.Handle, w.Name)).ToArray();
    }

    private static bool IsEmbeddedGameWindow(IntPtr window)
    {
        var display = User32.FindChildWindow(window, "nemudisplay");
        return display != IntPtr.Zero && User32.GetAncestor(display, 2) == window;
    }

    public IntPtr FindGameWindow()
    {
        lock (_gate)
        {
            if (_disposed) return IntPtr.Zero;
            var selected = GameSourceSelection.Find(_source, _serial, User32.FindArknightsWindow,
                FindEmulatorWindows, () =>
                {
                    // 设备发现会调用 ADB；窗口轮询不必每 100 毫秒重新发现。
                    if (DateTime.UtcNow >= _discoverAfter)
                    {
                        _discoverAfter = DateTime.UtcNow.AddSeconds(5);
                        try { _devices = DiscoverEmulatorDevices().Select(EmulatorDisplay.Inspect).ToArray(); }
                        catch { _devices = []; }
                    }
                    return _devices.Select(d => d.Kind == "ldplayer"
                        && !LdPlayerWindows.IsValid(d.Window, d.RenderWindow)
                        ? d with { Window = IntPtr.Zero, RenderWindow = IntPtr.Zero } : d).ToArray();
                });
            var previous = State;
            if (_device != selected.Device || previous.Source != selected.Source || previous.Window != selected.Window)
            {
                _controller?.Dispose();
                _controller = null;
                _device = selected.Device;
                _retryAfter = default;
                if (_device is null) _loggedCapture = null;
                SetState(new(selected.Source, selected.Window, selected.Problem));
            }
            else if (selected.Device is null)
                SetState(new(selected.Source, selected.Window, selected.Problem));
            return selected.Window;
        }
    }

    public RECT? GetViewport(IntPtr window)
    {
        if (window == IntPtr.Zero || User32.IsIconic(window)) return null;
        if (!IsAdb) return User32.GetClientRectScreen(window);
        var display = State.Source == "ldplayer" ? _device?.RenderWindow ?? IntPtr.Zero
            : User32.FindChildWindow(window, "nemudisplay");
        if (State.Source == "ldplayer" && !LdPlayerWindows.IsValid(window, display)) return null;
        var bounds = display == IntPtr.Zero ? null : User32.GetClientRectScreen(display);
        var state = State;
        return bounds is { Width: > 0, Height: > 0 } rect
            ? WindowGeometry.FitAspectRatio(rect, state.Width > 0 ? state.Width : 16,
                state.Height > 0 ? state.Height : 9) : null;
    }

    public byte[]? CaptureAdbScreen(int maxWidth)
    {
        lock (_gate)
        {
            if (_disposed || _device is null || !IsAdb || DateTime.UtcNow < _retryAfter) return null;
            var method = _device.DisplayId == 0 ? "MaaFramework ADB auto" : "ADB non-primary display";
            try
            {
                using var image = new MaaImageBuffer();
                if (_device.DisplayId == 0)
                {
                    // 已确认游戏在主屏，保留原有截图方法和性能。
                    if (_controller is null)
                    {
                        _controller = new MaaAdbController(_device.AdbPath, _device.Serial,
                            AdbScreencapMethods.Encode | AdbScreencapMethods.EncodeToFileAndPull
                                | AdbScreencapMethods.RawWithGzip | AdbScreencapMethods.RawByNetcat,
                            AdbInputMethods.AdbShell, config: _device.Config,
                            agentPath: Path.Combine(AppContext.BaseDirectory, "MaaAgentBinary"), link: LinkOption.None);
                        if (_controller.LinkStart().Wait() != MaaJobStatus.Succeeded)
                        {
                            _capture.LogFailure(_device, method, "n/a", "LinkStart failed");
                            _controller.Dispose();
                            _controller = null;
                            _retryAfter = DateTime.UtcNow.AddSeconds(5);
                            SetState(State with { Problem = _device.Kind == "ldplayer"
                                ? GameSourceSelection.LdAdbProblem : GameSourceSelection.AdbProblem, Width = 0, Height = 0 });
                            return null;
                        }
                        _controller.SetOption_ScreenshotUseRawSize(true);
                    }
                    var status = _controller.Screencap().Wait();
                    if (status != MaaJobStatus.Succeeded) throw new IOException($"Screencap status={status}");
                    if (!_controller.GetCachedImage(image) || image.IsEmpty) throw new IOException("Cached image is empty");
                }
                else
                {
                    var bytes = _capture.Capture(_device, EmulatorDisplay.RunAdb,
                        png => image.TrySetEncodedData(png) && !image.IsEmpty
                        && WindowGeometry.IsSupportedGameAspectRatio(image.Width, image.Height), out method, CaptureExtras);
                    if (bytes is null)
                    {
                        // 各候选方式已记录具体错误，不再追加一条笼统的失败日志。
                        CaptureFailed();
                        return null;
                    }
                }
                var capture = (Serial: _device.Serial, Display: _device.DisplayId!.Value);
                if (_loggedCapture != capture)
                {
                    AppLogger.Info(_device.DisplayName, $"serial={capture.Serial} display={capture.Display} capture={method}");
                    _loggedCapture = capture;
                }
                var width = image.Width;
                var height = image.Height;
                if (!WindowGeometry.IsSupportedGameAspectRatio(width, height))
                {
                    SetState(State with { Width = width, Height = height,
                        Problem = _device.Kind == "ldplayer"
                            ? $"雷电画面是 {width}×{height}，请在雷电设置 → 性能设置里把分辨率改成横屏 16:9（如 1920×1080）。"
                            : $"MuMu 画面是 {width}×{height}，请在模拟器设置里改成横屏 16:9（如 1920×1080）。" });
                    return null;
                }
                if (width > maxWidth)
                    image.TryResize(maxWidth, (int)Math.Round(height * (double)maxWidth / width));
                if (!image.TryGetEncodedData(out byte[]? png) || png is null || png.Length == 0)
                    throw new IOException("Encoded image is empty");
                SetState(State with { Width = width, Height = height, Problem = null });
                return png;
            }
            catch (Exception ex)
            {
                _capture.LogFailure(_device, method, "n/a", ex.Message);
                CaptureFailed();
                return null;
            }
            void CaptureFailed()
            {
                _controller?.Dispose();
                _controller = null;
                _retryAfter = DateTime.UtcNow.AddSeconds(5);
                SetState(State with { Problem = $"{_device.DisplayName}游戏画面截图失败，请在设置里导出诊断包反馈。", Width = 0, Height = 0 });
            }
        }
    }

    private byte[]? CaptureExtras()
    {
        try
        {
            if (_controller is null)
            {
                var settings = MuMuExtras.Resolve(_device!);
                if (settings is null) throw new IOException("未找到 MuMu SDK 安装目录、实例或游戏包名");
                var config = MuMuExtras.Prepare(_device!, settings);
                _controller = new MaaAdbController(_device!.AdbPath, _device.Serial,
                    AdbScreencapMethods.EmulatorExtras, AdbInputMethods.AdbShell, config: config,
                    agentPath: Path.Combine(AppContext.BaseDirectory, "MaaAgentBinary"), link: LinkOption.None);
                if (_controller.LinkStart().Wait() != MaaJobStatus.Succeeded)
                    throw new IOException("LinkStart failed");
                _controller.SetOption_ScreenshotUseRawSize(true);
            }
            var status = _controller.Screencap().Wait();
            if (status != MaaJobStatus.Succeeded) throw new IOException($"Screencap status={status}");
            using var image = new MaaImageBuffer();
            if (!_controller.GetCachedImage(image) || image.IsEmpty
                || !WindowGeometry.IsSupportedGameAspectRatio(image.Width, image.Height))
                throw new IOException("图像为空、无效或不是横屏 16:9");
            if (!image.TryGetEncodedData(out byte[]? png) || png is null || png.Length == 0)
                throw new IOException("Encoded image is empty");
            return png;
        }
        catch
        {
            _controller?.Dispose();
            _controller = null;
            throw;
        }
    }

    private void SetState(GameConnectionState state)
    {
        if (State == state) return;
        Volatile.Write(ref _state, state);
        Changed?.Invoke(state);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _controller?.Dispose();
            _controller = null;
        }
    }
}
