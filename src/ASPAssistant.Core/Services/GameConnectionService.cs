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
    private (string Serial, int Display)? _loggedCapture;
    private MaaAdbController? _controller;
    private MumuDevice? _device;
    private IReadOnlyList<MumuDevice> _devices = [];
    private DateTime _discoverAfter, _retryAfter;
    private string _source = "auto", _serial = "";
    private bool _disposed;
    private GameConnectionState _state = new();
    public GameConnectionState State => Volatile.Read(ref _state);
    public bool IsAdb => State.Source == "mumu";
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

    public static IReadOnlyList<MumuDevice> DiscoverMumuDevices()
    {
        using var found = MaaToolkit.Shared.AdbDevice.Find();
        return found.Where(d => d.Name.Contains("mumu", StringComparison.OrdinalIgnoreCase))
            .Select(d => new MumuDevice(d.Name, d.AdbPath, d.AdbSerial, d.Config)).ToArray();
    }

    public static IReadOnlyList<MumuWindow> FindMumuWindows()
    {
        using var windows = MaaToolkit.Shared.Desktop.Window.Find();
        return windows.Where(w => User32.IsWindowVisible(w.Handle)
                && IsMumuGameWindow(w.Handle))
            .Select(w => new MumuWindow(w.Handle, w.Name)).ToArray();
    }

    private static bool IsMumuGameWindow(IntPtr window)
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
                FindMumuWindows, () =>
                {
                    // 设备发现会调用 ADB；窗口轮询不必每 100 毫秒重新发现。
                    if (DateTime.UtcNow >= _discoverAfter)
                    {
                        _discoverAfter = DateTime.UtcNow.AddSeconds(5);
                        try { _devices = DiscoverMumuDevices().Select(MumuDisplay.Inspect).ToArray(); }
                        catch { _devices = []; }
                    }
                    return _devices;
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
        var display = User32.FindChildWindow(window, "nemudisplay");
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
                        if (_controller.LinkStart().Wait() != MaaJobStatus.Succeeded) throw new IOException();
                        _controller.SetOption_ScreenshotUseRawSize(true);
                    }
                    if (_controller.Screencap().Wait() != MaaJobStatus.Succeeded
                        || !_controller.GetCachedImage(image) || image.IsEmpty) throw new IOException();
                }
                else if (!image.TrySetEncodedData(MumuDisplay.Capture(_device)) || image.IsEmpty)
                    throw new IOException();
                var capture = (Serial: _device.Serial, Display: _device.DisplayId!.Value);
                if (_loggedCapture != capture)
                {
                    var method = capture.Display == 0 ? "MaaFramework ADB auto" : "adb exec-out screencap -p -d";
                    AppLogger.Info("MuMu", $"serial={capture.Serial} display={capture.Display} capture={method}");
                    _loggedCapture = capture;
                }
                var width = image.Width;
                var height = image.Height;
                if (!WindowGeometry.IsSupportedGameAspectRatio(width, height))
                {
                    SetState(State with { Width = width, Height = height,
                        Problem = $"MuMu 画面是 {width}×{height}，请在模拟器设置里改成横屏 16:9（如 1920×1080）。" });
                    return null;
                }
                if (width > maxWidth)
                    image.TryResize(maxWidth, (int)Math.Round(height * (double)maxWidth / width));
                if (!image.TryGetEncodedData(out byte[]? png) || png is null) throw new IOException();
                SetState(State with { Width = width, Height = height, Problem = null });
                return png;
            }
            catch
            {
                _controller?.Dispose();
                _controller = null;
                _retryAfter = DateTime.UtcNow.AddSeconds(5);
                // TODO(design): 审阅 display 截图失败时的提示文案。
                SetState(State with { Problem = "MuMu 游戏画面截图失败，请提供诊断包以排查显示屏兼容性。", Width = 0, Height = 0 });
                return null;
            }
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
