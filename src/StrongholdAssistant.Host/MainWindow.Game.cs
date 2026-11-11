// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
// 2026-11-11：从 ASPAssistant.App 的截图和窗口停靠流程改编，推送改为 WebView2 消息桥；见 NOTICE。
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public partial class MainWindow
{
    private readonly WindowTrackerService _tracker = new();
    private readonly ScreenCaptureService _capture = new();
    private MaaOcrEngine _ocr = null!;
    private MaaBanIconMatcher _iconMatcher = null!;
    private OcrScannerService _scanner = null!;
    private DockSettings _dock = new(380, null);
    private readonly OverlayWindow _overlay = new();
    private IReadOnlyList<ShopItem> _shopItems = [];
    private bool _capturingInformation;
    private bool _moving;
    private bool _overlayMode;
    private bool _collapsed;
    private double _uiScale = 1;
    private RECT? _lastClientRect;
    private bool _closing;
    private bool _canClose;
    private RECT? _lastGameRect;
    private Task _automaticScan = Task.CompletedTask;
    private HwndSource? _source;

    // 界面加载前要就绪的部分：侧栏尺寸、事件推送、窗口消息。都很快，不等 OCR。
    private void InitializeWindow()
    {
        _dock = _api.SettingsStore.LoadDock();
        if (!double.IsFinite(_dock.Width) || _dock.FollowOffsetDip is { } offset
            && (!double.IsFinite(offset.X) || !double.IsFinite(offset.Y)))
            throw new JsonException("侧栏设置无效。");
        _dock = _dock with { Width = Math.Clamp(_dock.Width, 320, 460) };
        ApplyScale(null);
        // 识别事件只会在 InitializeGameAsync 完成后出现，那时 _scanner 已就绪
        _api.Changed += (name, value) =>
        {
            if (value is ScanStatus status)
            {
                _scanner.SetBanScreenDetectionPaused(status.Busy || status.Continuous == true || _closing);
                if (status.Ok == true) _scanner.MarkInformationRecorded();
            }
            Dispatcher.Invoke(() => Browser.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new { @event = name, data = value }, HostApi.JsonOptions)));
        };
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source.AddHook(OnWindowMessage);
    }

    // OCR 加载和预热较慢，放在界面开始加载之后
    private async Task InitializeGameAsync()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        _ocr = await Task.Run(() => new MaaOcrEngine(data));
        await _ocr.WarmUpAsync();
        _iconMatcher = new MaaBanIconMatcher(data);
        _scanner = new OcrScannerService(_capture, new GarrisonOcrStrategy(), _api.CreateShopReader(_ocr),
            _ocr, new ASPAssistant.Core.GameState.GameState(), () => _api.TrackedNames, iconMatcher: _iconMatcher,
            intervalMs: 500);
        _scanner.GameStateUpdated += state =>
        {
            var items = state.ShopItems.ToArray();
            Dispatcher.BeginInvoke(() => { _shopItems = items; RefreshShopOverlay(); });
        };
        _api.ShopChanged += RefreshShopOverlay;
        await _api.InitializeRecognitionAsync(data, _ocr, async () =>
        {
            await _scanner.WaitForIdleAsync();
            return await CaptureInformationScreenAsync();
        });
        _scanner.BanScreenDetected += () => _automaticScan = Dispatcher.InvokeAsync(RecordAutomaticallyAsync).Task.Unwrap();
        _tracker.GameWindowMoved += rect => Dispatcher.BeginInvoke(() => UpdateGame(rect));
        _tracker.GameWindowPolled += rect => Dispatcher.BeginInvoke(() => UpdateGame(rect));
        _tracker.GameWindowLost += () => Dispatcher.BeginInvoke(() =>
        {
            _ = _api.StopContinuousAsync(windowLost: true);
            _lastGameRect = null;
            _lastClientRect = null;
            _overlayMode = false;
            _collapsed = false;
            ApplyScale(null);
            _shopItems = [];
            _overlay.UpdateShopItems([]);
            _overlay.Hide();
            _api.UpdateConnection(new("searching"));
        });
        _tracker.GameWindowMinimizedChanged += minimized => Dispatcher.BeginInvoke(() =>
        {
            WindowState = minimized ? WindowState.Minimized : WindowState.Normal;
            if (minimized) _overlay.Hide();
            if (!minimized && _tracker.CurrentGameRect is { } rect) UpdateGame(rect);
        });
    }

    private async Task RecordAutomaticallyAsync()
    {
        if (_api.IsContinuous) return;
        try { await _api.RecordInformationAsync(); }
        catch (RecognitionException) { /* 失败已由 scan 事件送达界面，忙碌时跳过自动请求。 */ }
    }

    private async Task<byte[]?> CaptureInformationScreenAsync()
    {
        if (!_connectGame) return null;
        var before = await Dispatcher.InvokeAsync(() =>
        {
            var game = User32.FindArknightsWindow();
            var foreground = User32.GetForegroundWindow();
            if (_closing || game == IntPtr.Zero || User32.IsIconic(game)
                || foreground != game && foreground != new WindowInteropHelper(this).Handle)
                return (Allowed: false, Hidden: false, Sidebar: IntPtr.Zero);
            var sidebar = new WindowInteropHelper(this).Handle;
            var client = User32.GetClientRectScreen(game);
            if (client is null) return (Allowed: false, Hidden: false, Sidebar: sidebar);
            bool hide = IsVisible && WindowGeometry.ShouldHideForCapture(
                User32.ReadWindowRect(sidebar), client.Value, _collapsed);
            _capturingInformation = true;
            _overlay.Hide();
            if (hide)
            {
                Hide();
                User32.SetForegroundWindow(game);
            }
            return (Allowed: true, Hidden: hide, Sidebar: sidebar);
        });
        if (!before.Allowed) return null;
        try
        {
            if (before.Hidden) await Task.Delay(150);
            return await Task.Run(() => _capture.CaptureScreen(before.Sidebar));
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _capturingInformation = false;
                RefreshShopOverlay();
                if (!before.Hidden || _closing) return;
                var activated = ShowActivated;
                ShowActivated = false;
                Show();
                ShowActivated = activated;
            });
        }
    }

    private void UpdateGame(RECT game)
    {
        if (_closing) return;
        _lastGameRect = game;
        RefreshShopOverlay();
        var client = User32.GetClientRectScreen(User32.FindArknightsWindow());
        if (client is null) return;
        _lastClientRect = client;
        if (_moving || WindowState != WindowState.Normal) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var current = User32.ReadWindowRect(hwnd);
        var scale = User32.GetWindowDpiScale(hwnd);
        var gameMonitor = User32.GetMonitorGeometry(game.Right - 1, game.Top + game.Height / 2);
        var desiredScale = WindowGeometry.UiScale(_api.Settings.ScaleValue, client.Value.Height, scale);
        var needsOverlay = WindowGeometry.NeedsOverlay(game, gameMonitor.WorkArea, scale,
            _dock.Width * desiredScale, _tracker.IsFullscreen);
        var offset = needsOverlay ? null : _dock.FollowOffsetDip;
        var monitor = offset is null
            ? User32.GetMonitorGeometry(game.Right - 1, game.Top + game.Height / 2)
            : User32.GetMonitorGeometry(
                (int)Math.Clamp(game.Right + offset.X * scale + current.Width / 2, int.MinValue, int.MaxValue),
                (int)Math.Clamp(game.Top + offset.Y * scale + current.Height / 2, int.MinValue, int.MaxValue));
        var centerX = current.Left + current.Width / 2;
        var centerY = current.Top + current.Height / 2;
        if (centerX < monitor.Bounds.Left || centerX >= monitor.Bounds.Right
            || centerY < monitor.Bounds.Top || centerY >= monitor.Bounds.Bottom)
        {
            var left = monitor.Bounds.Left + (monitor.Bounds.Width - current.Width) / 2;
            var top = monitor.Bounds.Top + (monitor.Bounds.Height - current.Height) / 2;
            User32.SetWindowBounds(hwnd, new RECT
                { Left = left, Top = top, Right = left + current.Width, Bottom = top + current.Height });
        }
        scale = User32.GetWindowDpiScale(hwnd);
        _uiScale = WindowGeometry.UiScale(_api.Settings.ScaleValue, client.Value.Height, scale);
        _overlayMode = WindowGeometry.NeedsOverlay(game, monitor.WorkArea, scale, _dock.Width * _uiScale, _tracker.IsFullscreen);
        if (!_overlayMode) _collapsed = false;
        ApplyScale(client.Value.Height);
        _api.UpdateConnection(new("connected", $"{client.Value.Width}×{client.Value.Height}", _tracker.IsFullscreen, _overlayMode));
        var target = _overlayMode
            ? WindowGeometry.OverlayBounds(client.Value, scale, Width, _dock.OverlayOffsetDip)
            : offset is null
            ? WindowGeometry.SidePanelBounds(game, monitor.WorkArea, scale, Width, null, false)
            : WindowGeometry.FollowBounds(game, monitor.WorkArea, scale, Width, null, false, offset);
        User32.SetWindowBounds(hwnd, target);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == User32.WM_ENTERSIZEMOVE) _moving = true;
        if (message == User32.WM_EXITSIZEMOVE)
        {
            _moving = false;
            var rect = User32.ReadWindowRect(hwnd);
            var scale = User32.GetWindowDpiScale(hwnd);
            var placement = new SidePanelPlacement(rect.Left, rect.Top, rect.Width / scale, rect.Height / scale);
            _dock = _dock with { Width = _collapsed ? _dock.Width : Math.Clamp(placement.WidthDip / _uiScale, 320, 460) };
            if (_overlayMode && _lastClientRect is { } client)
                _dock = _dock with { OverlayOffsetDip = new((rect.Left - client.Left) / scale, (rect.Top - client.Top) / scale) };
            else if (_lastGameRect is { } game)
                _dock = _dock with { FollowOffsetDip = WindowGeometry.CaptureFollowOffset(game, placement, scale) };
            _api.SettingsStore.SaveDock(_dock);
        }
        if (message == 0x02E0) // WM_DPICHANGED：等 WPF 应用新的 DPI 后重新计算。
            Dispatcher.BeginInvoke(RefreshScale);
        return IntPtr.Zero;
    }

    private void ApplyScale(int? clientHeight)
    {
        _uiScale = WindowGeometry.UiScale(_api.Settings.ScaleValue, clientHeight,
            User32.GetWindowDpiScale(new WindowInteropHelper(this).Handle));
        MinWidth = 0;
        MaxWidth = 460 * _uiScale;
        MinWidth = (_collapsed ? 48 : 320) * _uiScale;
        Width = WindowGeometry.PanelWidth(_dock.Width, _uiScale, _collapsed);
        ResizeMode = _collapsed ? ResizeMode.NoResize : ResizeMode.CanResize;
        if (Browser.CoreWebView2 is not null) Browser.ZoomFactor = _uiScale;
        Splash.LayoutTransform = new System.Windows.Media.ScaleTransform(_uiScale, _uiScale);
        // 覆盖模式（全屏或旁边放不下）盖在游戏上，不置顶会被游戏挡住，所以总是置顶；其余按用户的置顶设置
        Topmost = _overlayMode || _api.Settings.AlwaysOnTop;
    }

    private void RefreshScale()
    {
        if (_lastGameRect is { } game) UpdateGame(game);
        else ApplyScale(null);
    }

    private void SetCollapsed(bool collapsed)
    {
        if (collapsed && !_overlayMode) return;
        _collapsed = collapsed;
        ApplyScale(_lastClientRect?.Height);
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        if (_api is not null) await _api.StopContinuousAsync();
        // 网页先播退场（内容淡出到底色），再藏起窗口等后台收尾，收尾和释放 WebView2 时不会露出白底
        if (Browser.Visibility == Visibility.Visible)
        {
            PostWindowEvent("closing");
            await Task.Delay(170);
        }
        Hide();
        _overlay.Hide();
        await _initialization;
        _tracker.Stop();
        _scanner.SetBanScreenDetectionPaused(true);
        await _scanner.WaitForIdleAsync();
        await _automaticScan;
        await _api!.WaitForRecordingAsync();
        _source?.RemoveHook(OnWindowMessage);
        _overlay.Close();
        _canClose = true;
        _ = Dispatcher.BeginInvoke(Close);
    }

    private void RefreshShopOverlay()
    {
        _api.UpdateShopTracking(_shopItems);
        var items = _api.Settings.ShopMarkers ? _shopItems : [];
        _overlay.UpdateShopItems(items);
        if (!_connectGame || _closing || _capturingInformation || _lastGameRect is null
            || !items.Any(i => i.IsTracked || i.IsSecondary))
        {
            _overlay.Hide();
            return;
        }
        var game = User32.FindArknightsWindow();
        if (game == IntPtr.Zero || User32.IsIconic(game) || User32.GetClientRectScreen(game) is not { } client)
        {
            _overlay.Hide();
            return;
        }
        _overlay.UpdatePosition(client);
        if (!_overlay.IsVisible) _overlay.Show();
    }
}
