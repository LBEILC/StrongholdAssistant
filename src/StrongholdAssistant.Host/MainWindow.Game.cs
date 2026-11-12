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
    private readonly WindowTrackerService _tracker;
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
    private double? _collapseUpHeight;
    private double _expandedWidth;
    private double _expandedHeight;
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
        if (_dock.WorkbenchBounds is { } workbench
            && (!double.IsFinite(workbench.Left) || !double.IsFinite(workbench.Top)
                || !double.IsFinite(workbench.Width) || !double.IsFinite(workbench.Height)
                || workbench.Width <= 0 || workbench.Height <= 0))
            throw new JsonException("工作台位置无效。");
        ApplyScale(null);
        if (!_api.Settings.FollowGame && _dock.FreeBounds is { } free)
        {
            if (!double.IsFinite(free.Left) || !double.IsFinite(free.Top)
                || !double.IsFinite(free.Width) || !double.IsFinite(free.Height) || free.Width <= 0 || free.Height <= 0)
                throw new JsonException("侧栏位置无效。");
            Left = free.Left; Top = free.Top; Width = free.Width; Height = free.Height;
            var hwnd = new WindowInteropHelper(this).Handle;
            var dpi = User32.GetWindowDpiScale(hwnd);
            var bounds = new RECT { Left = (int)(Left * dpi), Top = (int)(Top * dpi),
                Right = (int)((Left + Width) * dpi), Bottom = (int)((Top + Height) * dpi) };
            if (!User32.IntersectsMonitor(bounds))
            {
                var primary = User32.GetMonitorGeometry(0, 0).WorkArea;
                (Left, Top) = WindowGeometry.CenterFreePosition(Width, Height, primary, dpi);
            }
        }
        // 识别事件只会在 InitializeGameAsync 完成后出现，那时 _scanner 已就绪
        _api.Changed += (name, value) =>
        {
            if (value is ScanStatus status)
            {
                _scanner.SetBanScreenDetectionPaused(status.Busy || status.Continuous == true || _closing);
                if (status.Ok == true) _scanner.MarkInformationRecorded();
                Dispatcher.Invoke(() =>
                {
                    bool recognizing = status.Busy || status.Continuous == true;
                    if (_capturingInformation == recognizing) return;
                    _capturingInformation = recognizing;
                    if (recognizing)
                    {
                        _overlay.Hide();
                        // 开始识别时等待覆盖层消失，持续识别期间不再逐帧隐藏。
                        User32.DwmFlush();
                    }
                    else RefreshShopOverlay();
                });
            }
            Dispatcher.Invoke(() => Browser.CoreWebView2.PostWebMessageAsJson(
                JsonSerializer.Serialize(new { @event = name, data = value }, HostApi.JsonOptions)));
        };
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source.AddHook(OnWindowMessage);
        InitializeHotkey();
        LocationChanged += (_, _) => RememberWorkbenchBounds();
        SizeChanged += (_, _) => RememberWorkbenchBounds();
    }

    // OCR 加载和预热较慢，放在界面开始加载之后
    private async Task InitializeGameAsync()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        _ocr = await Task.Run(() => new MaaOcrEngine(data));
        await _ocr.WarmUpAsync();
        _iconMatcher = new MaaBanIconMatcher(data);
        _scanner = new OcrScannerService(_capture, new GarrisonOcrStrategy(), _api.CreateShopReader(_ocr),
            _ocr, new ASPAssistant.Core.GameState.GameState(),
            () => _api.TrackedNames.Concat(_api.SecondaryNames).Distinct().ToArray(), iconMatcher: _iconMatcher,
            intervalMs: 800);
        _scanner.GameStateUpdated += state =>
        {
            var items = state.ShopItems.ToArray();
            Dispatcher.BeginInvoke(() => { _shopItems = items; RefreshShopOverlay(); });
        };
        _api.ShopChanged += RefreshShopOverlay;
        _scanner.EconomyRead += reading => Dispatcher.Invoke(() => _api.ObserveEconomy(reading));
        await _api.InitializeRecognitionAsync(data, _ocr, async () =>
        {
            await _scanner.WaitForIdleAsync();
            return await CaptureInformationScreenAsync();
        });
        _scanner.AwaitingDetail = () => _api.Match.AwaitingDetail;
        _scanner.BanScreenDetected += solo => _automaticScan = Dispatcher.InvokeAsync(() => RecordAutomaticallyAsync(solo)).Task.Unwrap();
        _scanner.DetailScreenDetected += () => _automaticScan = Dispatcher.InvokeAsync(_api.HandleDetailAsync).Task.Unwrap();
    }

    // 跟随定位不等待 OCR 预热；首次发现和后续移动共用 UpdateGame。
    private void InitializeGameTracking()
    {
        _capture.Connection.Configure(_api.Settings.GameSource, _api.Settings.MumuSerial);
        _capture.Connection.Changed += _ => Dispatcher.BeginInvoke(PublishConnection);
        _tracker.GameWindowMoved += rect => Dispatcher.BeginInvoke(() => UpdateGame(rect));
        _tracker.GameWindowPolled += rect => Dispatcher.BeginInvoke(() => UpdateGame(rect));
        _tracker.GameWindowLost += () => Dispatcher.BeginInvoke(() =>
        {
            _ = _api.StopContinuousAsync(windowLost: true);
            _lastGameRect = null;
            _lastClientRect = null;
            _overlayMode = false;
            ApplyScale(null);
            _shopItems = [];
            _overlay.UpdateShopItems([]);
            _overlay.Hide();
            PublishConnection();
        });
        _tracker.GameWindowMinimizedChanged += minimized => Dispatcher.BeginInvoke(() =>
        {
            if (!_workbench && _api.Settings.FollowGame) WindowState = minimized ? WindowState.Minimized : WindowState.Normal;
            if (minimized) _overlay.Hide();
            if (!minimized && _tracker.CurrentGameRect is { } rect) UpdateGame(rect);
        });
    }

    private async Task RecordAutomaticallyAsync(bool solo)
    {
        if (_api.IsContinuous) return;
        try { await _api.HandleOpeningAsync(solo); }
        catch (RecognitionException) { /* 失败已由 scan 事件送达界面，忙碌时跳过自动请求。 */ }
    }

    private async Task<byte[]?> CaptureInformationScreenAsync()
    {
        if (!_connectGame) return null;
        if (_capture.Connection.IsAdb)
            return _closing ? null : await Task.Run(() => _capture.CaptureScreen());
        var before = await Dispatcher.InvokeAsync(() =>
        {
            var game = _tracker.CurrentGameWindowHandle;
            var foreground = User32.GetForegroundWindow();
            if (_closing || game == IntPtr.Zero || User32.IsIconic(game)
                || foreground != game && foreground != new WindowInteropHelper(this).Handle)
                return (Allowed: false, Hidden: false, Sidebar: IntPtr.Zero);
            var sidebar = new WindowInteropHelper(this).Handle;
            var client = User32.GetClientRectScreen(game);
            if (client is null) return (Allowed: false, Hidden: false, Sidebar: sidebar);
            bool hide = IsVisible && WindowGeometry.ShouldHideForCapture(
                User32.ReadWindowRect(sidebar), client.Value, _collapsed);
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
        var client = _tracker.CurrentViewport;
        if (client is null) return;
        _lastClientRect = client;
        if (_workbench || !_api.Settings.FollowGame)
        {
            _overlayMode = false;
            ApplyScale(client.Value.Height);
            PublishConnection();
            return;
        }
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
        ApplyScale(client.Value.Height);
        PublishConnection();
        var target = _overlayMode
            ? WindowGeometry.OverlayBounds(client.Value, scale, Width, _dock.OverlayOffsetDip)
            : offset is null
            ? WindowGeometry.SidePanelBounds(game, monitor.WorkArea, scale, Width, null, false)
            : WindowGeometry.FollowBounds(game, monitor.WorkArea, scale, Width, null, false, offset);
        if (_collapsed && _collapseUpHeight is { } height)
            target.Bottom = target.Top + (int)Math.Round(height * _uiScale * scale);
        User32.SetWindowBounds(hwnd, target);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == User32.WM_HOTKEY && _hotkeyId != 0 && wParam.ToInt32() == _hotkeyId)
        {
            handled = true;
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Activate();
                User32.SetForegroundWindow(hwnd);
            }
            else Browser.CoreWebView2?.PostWebMessageAsJson("{\"event\":\"hotkey\",\"data\":null}");
            return IntPtr.Zero;
        }
        if (message == User32.WM_ENTERSIZEMOVE) _moving = true;
        if (message == User32.WM_EXITSIZEMOVE)
        {
            _moving = false;
            if (_workbench) { RememberWorkbenchBounds(); return IntPtr.Zero; }
            var rect = User32.ReadWindowRect(hwnd);
            var scale = User32.GetWindowDpiScale(hwnd);
            var placement = new SidePanelPlacement(rect.Left, rect.Top, rect.Width / scale, rect.Height / scale);
            _dock = _dock with { Width = _collapsed ? _dock.Width : Math.Clamp(placement.WidthDip / _uiScale, 320, 460) };
            if (!_api.Settings.FollowGame) RememberFreeBounds();
            else if (_overlayMode && _lastClientRect is { } client)
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
        _uiScale = _workbench ? _api.Settings.ScaleValue ?? _uiScale : WindowGeometry.UiScale(_api.Settings.ScaleValue, clientHeight,
            User32.GetWindowDpiScale(new WindowInteropHelper(this).Handle));
        MinWidth = 0;
        if (_workbench)
        {
            MaxWidth = double.PositiveInfinity;
            var minimum = WorkbenchGeometry.MinimumSize(CurrentWorkArea());
            MinWidth = minimum.Width; MinHeight = minimum.Height;
            ResizeMode = ResizeMode.CanResize;
            if (Browser.CoreWebView2 is not null) Browser.ZoomFactor = _uiScale;
            Topmost = _api.Settings.AlwaysOnTop;
            return;
        }
        MinHeight = 0;
        MaxWidth = 460 * _uiScale;
        MinWidth = (_collapsed && _collapseUpHeight is null ? 48 : 320) * _uiScale;
        if (_api.Settings.FollowGame)
        {
            Width = _collapsed && _collapseUpHeight != null ? _expandedWidth
                : WindowGeometry.PanelWidth(_dock.Width, _uiScale, _collapsed);
            if (_collapsed && _collapseUpHeight is { } height) Height = height * _uiScale;
        }
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
        SetCollapse(collapsed, null);
    }

    private void SetCollapse(bool collapsed, double? upHeight)
    {
        if (_workbench) return;
        if (!_collapsed && collapsed)
        {
            _expandedWidth = Width;
            _expandedHeight = Height;
        }
        if (!_collapsed && !collapsed) return;
        _collapsed = collapsed;
        _collapseUpHeight = upHeight;
        ApplyScale(_lastClientRect?.Height);
        (Width, Height) = WindowGeometry.CollapsedSize(_expandedWidth, _expandedHeight, _uiScale, collapsed, upHeight);
        if (!collapsed && _api.Settings.FollowGame && _lastGameRect is { } game) UpdateGame(game);
    }

    private void RememberFreeBounds()
    {
        _dock = _dock with { FreeBounds = new(Left, Top,
            _collapsed ? _expandedWidth : Width, _collapsed ? _expandedHeight : Height) };
    }

    private async Task RefreshSettingsAsync(bool followedBefore)
    {
        if (_connectGame)
        {
            await Task.Run(() =>
            {
                if (_capture.Connection.Configure(_api.Settings.GameSource, _api.Settings.MumuSerial))
                    _capture.Connection.FindGameWindow();
            });
        }
        if (!_workbench && followedBefore && !_api.Settings.FollowGame)
        {
            RememberFreeBounds();
            _api.SettingsStore.SaveDock(_dock);
            _overlayMode = false;
            _api.UpdateConnection(_api.Connection with { Overlay = false });
        }
        RefreshScale();
    }

    private void PublishConnection()
    {
        if (_closing || !_connectGame) return;
        var state = _capture.Connection.State;
        var viewport = _capture.Connection.GetViewport(state.Window);
        bool connected = state.Source == "pc" ? viewport is not null
            : state.Source == "mumu" && state.Width > 0 && state.Problem is null;
        string? resolution = !connected ? null : state.Source == "mumu"
            ? $"{state.Width}×{state.Height}" : $"{viewport!.Value.Width}×{viewport.Value.Height}";
        _api.UpdateConnection(new(connected ? "connected" : "searching", resolution,
            connected ? _tracker.IsFullscreen : null, connected && _overlayMode,
            connected ? state.Source : null, state.Problem));
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
        UnregisterHotkey();
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
            || (!items.Any(i => i.IsTracked || i.IsSecondary) && !_overlay.HasMarkers))
        {
            _overlay.Hide();
            return;
        }
        var game = _tracker.CurrentGameWindowHandle;
        var foreground = User32.GetForegroundWindow();
        if (game == IntPtr.Zero || User32.IsIconic(game) || _tracker.CurrentViewport is not { } client
            || foreground != game && foreground != new WindowInteropHelper(this).Handle
            || _capture.Connection.State.Problem is not null)
        {
            _overlay.Hide();
            return;
        }
        _overlay.UpdatePosition(client);
        if (!_overlay.IsVisible) _overlay.Show();
    }
}
