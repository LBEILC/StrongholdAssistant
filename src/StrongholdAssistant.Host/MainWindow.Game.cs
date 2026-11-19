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
    internal object ReadDiagnosticCapture() => new
    {
        method = _capture.Connection.WindowCapture.Method,
        lastFallbackReason = _capture.Connection.WindowCapture.LastFallbackReason
    };
    private MaaOcrEngine _ocr = null!;
    private MaaBanIconMatcher _iconMatcher = null!;
    private OcrScannerService _scanner = null!;
    private DockSettings _dock = new(380, null);
    private readonly OverlayWindow _overlay = new();
    private IReadOnlyList<ShopItem> _shopItems = [];
    private bool _capturingInformation;
    private bool _moving;
    private bool _resizingHeight;
    private bool _overlayMode;
    private string _side = "right";
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
        if (!double.IsFinite(_dock.Width) || new[] { _dock.FollowOffsetDip, _dock.FollowLeftOffsetDip,
                _dock.OverlayOffsetDip, _dock.OverlayRightOffsetDip }
            .Any(offset => offset is not null && (!double.IsFinite(offset.X) || !double.IsFinite(offset.Y))))
            throw new JsonException("侧栏设置无效。");
        if (new[] { _dock.FollowHeightDip, _dock.OverlayHeightDip }
            .Any(height => height is { } h && (!double.IsFinite(h) || h <= 0)))
            throw new JsonException("侧栏高度无效。");
        _dock = _dock.WithWidth(_dock.Width);
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
            if (name == "controlJob")
            {
                // 执行线程发布不可变快照；停止键和 UI 查询都不能等待 UI 线程回调。
                Dispatcher.BeginInvoke(() => Browser.CoreWebView2.PostWebMessageAsJson(
                    JsonSerializer.Serialize(new { @event = name, data = value }, HostApi.JsonOptions)));
                return;
            }
            if (value is ScanStatus status)
            {
                _scanner.SetBanScreenDetectionPaused(status.Busy || _closing);
                if (status.Ok == true) _scanner.MarkInformationRecorded();
                Dispatcher.Invoke(() =>
                {
                    bool recognizing = status.Busy;
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
        StateChanged += OnWindowStateChanged;
        if (_api.StartupWorkbench) SetWorkbench(true);
        LocationChanged += (_, _) => RememberWorkbenchBounds();
        SizeChanged += (_, _) => RememberWorkbenchBounds();
    }

    // OCR 加载和预热较慢，放在界面开始加载之后
    private async Task InitializeGameAsync()
    {
        var data = Path.Combine(ASPAssistant.Core.PackageLayout.Root, "data");
        _ocr = await Task.Run(() => new MaaOcrEngine(data));
        await _ocr.WarmUpAsync();
        _iconMatcher = new MaaBanIconMatcher(data);
        var scanOcr = new DiagnosticOcrEngine(_ocr);
        _scanner = new OcrScannerService(_capture, new GarrisonOcrStrategy(), _api.CreateShopReader(scanOcr),
            scanOcr, new ASPAssistant.Core.GameState.GameState(),
            () => _api.TrackedNames.Concat(_api.Settings.MarkerStyles.Pickable.Show ? _api.SecondaryNames : []).Distinct().ToArray(), iconMatcher: _iconMatcher,
            intervalMs: 800);
        _scanner.RecognitionFailed += (shot, reason, scale, steps) =>
        {
            var message = _api.ReportRecognitionFailure(shot, reason, scale, steps);
            Dispatcher.BeginInvoke(() => Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(
                new { @event = "scan", data = new ScanStatus(false, message, false) }, HostApi.JsonOptions)));
        };
        _scanner.GameStateUpdated += state =>
        {
            var items = state.ShopItems.ToArray();
            var generation = _scanner.ShopGeneration;
            // 卡槽变了立即移除（093）；淡出照常播（150 ms，从移除那一刻开始，不拖慢移除）
            bool removing = _scanner.RemovingChangedShopItems;
            var capturedAt = _scanner.ChangedShopCapturedAt;
            Dispatcher.BeginInvoke(() =>
            {
                if (generation != _scanner.ShopGeneration) return;
                _shopItems = items; RefreshShopOverlay();
                if (removing) ASPAssistant.Core.AppLogger.Info("ShopTiming", $"画面变化到覆盖层移除={(DateTimeOffset.UtcNow - capturedAt).TotalMilliseconds:F1}ms");
            });
        };
        _scanner.SetShopScanSpeed(_api.Settings.ShopScanSpeed);
        _overlay.PlacementChanged += () => RefreshShopOverlay();
        _api.ShopChanged += () => Dispatcher.BeginInvoke(() =>
        {
            _scanner.SetShopScanSpeed(_api.Settings.ShopScanSpeed);
            _overlay.UpdateRouteSettings(_api.Settings.Routes);
            RefreshShopOverlay();
        });
        _scanner.EconomyRead += reading => Dispatcher.Invoke(() => _api.ObserveEconomy(reading));
        _scanner.ThemeDetected += reading => Dispatcher.Invoke(() => _api.ObserveTheme(reading));
        _api.InitializeBoardRoutes(data);
        _api.InitializeField(data, scanOcr);
        _api.InitializeMatchReading(data, scanOcr);
        _scanner.ObserveAdaptFrameAsync = _api.ObserveAdaptFrameAsync;
        _scanner.ObserveMatchFrameAsync = _api.ObserveMatchFrameAsync;
        _scanner.ObserveEnemyFrameAsync = _api.ObserveEnemyFrameAsync;
        _scanner.EnemyPanelSeen += _api.CountEnemyPanel;
        // 敌人面板探针：侧栏盖着面板那一块时不截
        var sidebar = new WindowInteropHelper(this).EnsureHandle();
        _scanner.ProbeBlocked = screen => User32.IsWindowVisible(sidebar) && User32.ReadWindowRect(sidebar) is var r
            && r.Left < screen.Right && r.Right > screen.Left && r.Top < screen.Bottom && r.Bottom > screen.Top;
        _api.FieldCursorRegion = () =>
        {
            if (_tracker.CurrentViewport is not { } viewport || !User32.GetCursorPos(out var point)) return null;
            return new System.Drawing.RectangleF((point.X - viewport.Left - 8f) / viewport.Width,
                (point.Y - viewport.Top - 8f) / viewport.Height, 48f / viewport.Width, 64f / viewport.Height);
        };
        _scanner.ObserveFieldAsync = _api.ObserveFieldAsync;
        void ConfigureCollection()
        {
            _scanner.CollectionFrame = _api.Settings.Collect ? _api.ObserveCollectionFrame : null;
            _scanner.CollectionEnemyPanel = _api.Settings.Collect ? _api.CollectEnemyPanel : null;
            _scanner.CollectionScanCompleted = _api.Settings.Collect ? _api.CompleteCollectionScan : null;
            _capture.NativeFrame = _api.Settings.Collect ? _api.ObserveCollectionNative : null;
            _capture.NativeFrameRequested = () => _api.NeedsCollectionRaw;
        }
        _api.CollectionEnabledChanged += ConfigureCollection;
        ConfigureCollection();
        _api.CollectionEnvironment = () =>
        {
            var state = _capture.Connection.State;
            var rect = state.Window == 0 ? null : User32.GetClientRectScreen(state.Window);
            return new { source = state.Source, capture = _capture.Connection.IsAdb ? _capture.Connection.CaptureMethod : "BitBlt",
                rawWidth = state.Width > 0 ? state.Width : rect?.Width,
                rawHeight = state.Height > 0 ? state.Height : rect?.Height,
                windowWidth = rect?.Width, windowHeight = rect?.Height,
                dpi = state.Window == 0 ? 96 : User32.GetWindowDpiScale(state.Window) * 96 };
        };
        // 和开局识别同一个截图方法：点助手里的「识别」时前台是助手，也能截；侧栏挡着游戏时先藏一下（v1.0.2 玩家反馈）
        _api.InitializeControl(_capture, _scanner);
        _api.ReadCurrentField = () => _scanner.ReadCurrentFieldAsync(CaptureInformationScreenAsync, _api.RecognizeFieldFrameAsync);
        _scanner.BoardRoutesRequested = () => _api.BoardRoutesEnabled;
        _scanner.BoardFrameReady += (frame, reading) =>
        {
            var upgradeButton = frame is not null && _scanner.BoardMainScreenVisible
                && _scanner.BoardShopVisible && !_scanner.BoardDetailVisible
                && reading is { IsRest: true } ? reading.UpgradeButton : null;
            Dispatcher.BeginInvoke(() =>
            {
                _upgradeButton = upgradeButton;
                RefreshShopOverlay();
            });
            if (frame is null || !_scanner.BoardMainScreenVisible || reading is { IsRest: false })
            {
                _api.HideFieldOverlay();
            }
            _api.ObserveBoardFrame(frame, reading, _scanner.BoardShopVisible, _scanner.BoardMainScreenVisible);
        };
        _api.FieldOverlayChanged += (field, geometry) => Dispatcher.BeginInvoke(() =>
        {
            _overlay.UpdateField(field, geometry);
            RefreshShopOverlay();
        });
        _api.PlacementOverlayChanged += plan => Dispatcher.BeginInvoke(() =>
        {
            _overlay.UpdatePlacement(plan);
            RefreshShopOverlay();
        });
        _overlay.UpdatePlacement(HostApi.ToOverlayPlacement(_api.Placement));
        // 敌人路线：识别结果在扫描线程上来，交给界面线程画
        _api.BoardRoutesChanged += routes => Dispatcher.BeginInvoke(() =>
        {
            _overlay.UpdateRoutes(routes, _api.Settings.Routes);
            RefreshShopOverlay();
        });
        _api.RouteHotkeyChanged += down =>
        {
            ASPAssistant.Core.AppLogger.Info("Board", $"快捷键 MainWindow 收到 down={down}");
            _overlay.SetRouteHotkey(down);
        };
        await _api.InitializeRecognitionAsync(data, _ocr, async () =>
        {
            await _scanner.WaitForIdleAsync();
            if (_automaticOpeningScreenshot is { } opening)
            {
                _automaticOpeningScreenshot = null;
                return opening;
            }
            return await CaptureInformationScreenAsync();
        });
        _api.InitializeRoster(data, _ocr, _capture.Connection, async paused =>
        {
            _scanner.SetBanScreenDetectionPaused(paused || _closing);
            if (paused) await _scanner.WaitForIdleAsync();
            await Dispatcher.InvokeAsync(() =>
            {
                _capturingInformation = paused;
                if (paused) _overlay.Hide(); else RefreshShopOverlay();
            });
        });
        _scanner.AwaitingDetail = () => _api.Match.AwaitingDetail;
        _scanner.BanScreenDetected += solo => _automaticScan = Dispatcher.InvokeAsync(() => RecordAutomaticallyAsync(solo)).Task.Unwrap();
        _scanner.DetailScreenDetected += () => _automaticScan = Dispatcher.InvokeAsync(RecordDetailAutomaticallyAsync).Task.Unwrap();
    }

    // 跟随定位不等待 OCR 预热；首次发现和后续移动共用 UpdateGame。
    private void InitializeGameTracking()
    {
        _capture.Connection.Configure(_api.Settings.GameSource, _api.Settings.EmulatorSerial, _api.Settings.AdbAddress, _api.Settings.HideCaptureBorder);
        _capture.Connection.Changed += _ =>
        {
            _scanner?.InvalidateShop();
            Dispatcher.BeginInvoke(PublishConnection);
        };
        _tracker.GameWindowMoved += rect => Dispatcher.BeginInvoke(() => UpdateGame(rect));
        _tracker.GameWindowPolled += rect => Dispatcher.BeginInvoke(() => UpdateGame(rect));
        _tracker.GameWindowLost += () => Dispatcher.BeginInvoke(() =>
        {
            _scanner?.InvalidateShop();
            _lastGameRect = null;
            _lastClientRect = null;
            _overlayMode = false;
            ApplyScale(null);
            _shopItems = [];
            _overlay.UpdateShopItems([]);
            _overlay.Hide();
            PublishConnection();
        });
        // 游戏窗口自己也常是置顶窗口：点一下游戏，它就排到侧栏前面把侧栏盖住。
        // WPF 的 Topmost 只在设置时排一次，所以游戏切到前台时重新把侧栏和标记层提到最上面（v1.2.5）。
        _tracker.GameWindowFocusChanged += focused => Dispatcher.BeginInvoke(() =>
        {
            if (!focused || _closing) return;
            if (Topmost && IsVisible) User32.RaiseTopmost(new WindowInteropHelper(this).Handle);
            if (_overlay.IsVisible) User32.RaiseTopmost(new WindowInteropHelper(_overlay).Handle);
        });
        _tracker.GameWindowMinimizedChanged += minimized => Dispatcher.BeginInvoke(() =>
        {
            if (!_workbench && _api.Settings.FollowGame) WindowState = minimized ? WindowState.Minimized : WindowState.Normal;
            if (minimized) _overlay.Hide();
            if (!minimized && _tracker.CurrentGameRect is { } rect) UpdateGame(rect);
        });
    }

    private byte[]? _automaticOpeningScreenshot;

    private async Task RecordAutomaticallyAsync(bool solo)
    {
        _automaticOpeningScreenshot = _scanner!.OpeningScreenshot;
        try { await _api.HandleOpeningAsync(solo); }
        catch (RecognitionException) { /* 失败已由 scan 事件送达界面，忙碌时跳过自动请求。 */ }
        finally { _automaticOpeningScreenshot = null; }
    }

    // 单人对局详情的自动识别也要接住识别失败和「正在识别」：没接住时关闭助手会在等它时出错，
    // 窗口藏了进程却不退，像卡死（v1.2.0 玩家诊断包）
    private async Task RecordDetailAutomaticallyAsync()
    {
        try { await _api.HandleDetailAsync(); }
        catch (RecognitionException) { /* 失败已由 scan 事件送达界面，忙碌时跳过自动请求。 */ }
    }

    private async Task<byte[]?> CaptureInformationScreenAsync()
    {
        if (!_connectGame) return null;
        if (_capture.Connection.IsAdb)
            return _closing ? null : await Task.Run(() => _capture.CaptureScreen());
        if (_closing) return null;
        var windowFrame = await Task.Run(() => _capture.CaptureWindowScreen());
        if (windowFrame is not null) return windowFrame;
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
            return await Task.Run(() => _capture.CaptureBitBltScreen(before.Sidebar));
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
        if (!_lastGameRect.Equals(game)) _scanner?.InvalidateShop();
        _lastGameRect = game;
        RefreshShopOverlay();
        var client = _tracker.CurrentViewport;
        if (client is null) return;
        _lastClientRect = client;
        if (_workbench || !_api.Settings.FollowGame)
        {
            _overlayMode = false;
            var bounds = User32.ReadWindowRect(new WindowInteropHelper(this).Handle);
            // TODO(design): 自由窗口和工作台暂按窗口中心判断所在侧，待明确这两种形态的 side 语义。
            _side = (long)bounds.Left + bounds.Right < (long)game.Left + game.Right ? "left" : "right";
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
        var selection = WindowGeometry.SelectSide(game, gameMonitor.WorkArea, scale,
            _dock.Width * desiredScale, _tracker.IsFullscreen, _api.Settings.DockSide, _api.Settings.OverlaySide);
        var offset = selection.Overlay ? null : selection.Side == "left" ? _dock.FollowLeftOffsetDip : _dock.FollowOffsetDip;
        var origin = selection.Side == "left" ? game.Left - _dock.Width * desiredScale * scale : game.Right;
        var monitor = offset is null
            ? User32.GetMonitorGeometry(game.Right - 1, game.Top + game.Height / 2)
            : User32.GetMonitorGeometry(
                (int)Math.Clamp(origin + offset.X * scale + current.Width / 2, int.MinValue, int.MaxValue),
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
        (_overlayMode, _side) = WindowGeometry.SelectSide(game, gameMonitor.WorkArea, scale,
            _dock.Width * _uiScale, _tracker.IsFullscreen, _api.Settings.DockSide, _api.Settings.OverlaySide);
        offset = _side == "left" ? _dock.FollowLeftOffsetDip : _dock.FollowOffsetDip;
        // 跟随位置和尺寸只在下方统一应用，避免先写旧的展开宽度再写停靠宽度。
        ApplyScale(client.Value.Height, updateWindowSize: false);
        PublishConnection();
        var expandedWidth = _dock.Width * _uiScale;
        var target = _overlayMode
            ? WindowGeometry.OverlayBounds(client.Value, scale, expandedWidth,
                _side == "right" ? _dock.OverlayRightOffsetDip : _dock.OverlayOffsetDip, _side, _dock.OverlayHeightDip)
            : offset is null
            ? WindowGeometry.SidePanelBounds(game, gameMonitor.WorkArea, scale, expandedWidth, _dock.FollowHeightDip, false, _side)
            : WindowGeometry.FollowBounds(game, monitor.WorkArea, scale, expandedWidth, _dock.FollowHeightDip, false, offset, _side);
        if (_collapsed)
            target = WindowGeometry.CollapseBounds(target, (int)Math.Round(48 * _uiScale * scale),
                _collapseUpHeight is { } height ? (int)Math.Round(height * _uiScale * scale) : null,
                WindowGeometry.AnchorsRight(_overlayMode, _side));
        User32.SetWindowBounds(hwnd, target);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (HandleWorkbenchMessage(hwnd, message, wParam, lParam, ref handled)) return IntPtr.Zero;
        if (message == User32.WM_HOTKEY && wParam.ToInt32() == 4)
        {
            handled = true;
            _fieldSystemHotkey.Tap(_api.PublishFieldHotkey);
            return IntPtr.Zero;
        }
        if (message == User32.WM_HOTKEY && wParam.ToInt32() == 3)
        {
            handled = true;
            _routeSystemHotkey.Tap(_overlay.TapRouteHotkey);
            return IntPtr.Zero;
        }
        if (message == User32.WM_HOTKEY && _hotkeyId != 0 && wParam.ToInt32() == _hotkeyId)
        {
            handled = true;
            ToggleFromHotkey();
            return IntPtr.Zero;
        }
        if (message == User32.WM_ENTERSIZEMOVE) { _moving = true; _resizingHeight = false; }
        if (message == 0x0214 && wParam.ToInt32() is >= 3 and <= 8) _resizingHeight = true; // WM_SIZING
        if (message == User32.WM_EXITSIZEMOVE)
        {
            _moving = false;
            if (_workbench) { RememberWorkbenchBounds(); return IntPtr.Zero; }
            var rect = User32.ReadWindowRect(hwnd);
            var scale = User32.GetWindowDpiScale(hwnd);
            var placement = new SidePanelPlacement(rect.Left, rect.Top, rect.Width / scale, rect.Height / scale);
            if (!_collapsed) _dock = _dock.WithWidth(placement.WidthDip / _uiScale);
            if (!_api.Settings.FollowGame) RememberFreeBounds();
            else if (_overlayMode && _lastClientRect is { } client)
            {
                if (!_collapsed && _resizingHeight)
                    _dock = _dock with { OverlayHeightDip = WindowGeometry.SnapHeight(placement.HeightDip,
                        WindowGeometry.OverlayBounds(client, scale, _dock.Width * _uiScale, null, _side).Height / scale) };
                var offset = WindowGeometry.SnapOverlayOffset(new SidePanelOffset(
                    (_side == "right" ? rect.Right - client.Right : rect.Left - client.Left) / scale,
                    (rect.Top - client.Top) / scale), client, scale, _dock.Width * _uiScale, _side);
                _dock = _side == "right" ? _dock with { OverlayRightOffsetDip = offset } : _dock with { OverlayOffsetDip = offset };
            }
            else if (_lastGameRect is { } game)
            {
                if (!_collapsed && _resizingHeight)
                    _dock = _dock with { FollowHeightDip = WindowGeometry.SnapHeight(placement.HeightDip, game.Height / scale) };
                var offset = WindowGeometry.SnapOffset(WindowGeometry.CaptureFollowOffset(game, placement, scale, _side));
                _dock = _side == "left" ? _dock with { FollowLeftOffsetDip = offset } : _dock with { FollowOffsetDip = offset };
            }
            _api.SettingsStore.SaveDock(_dock);
            if (_api.Settings.FollowGame && _lastGameRect is { } currentGame) UpdateGame(currentGame);
        }
        if (message == 0x02E0) // WM_DPICHANGED：等 WPF 应用新的 DPI 后重新计算。
            Dispatcher.BeginInvoke(() =>
            {
                // 拖动时不重定位，但跨屏仍要更新缩放；普通轮询不干预用户调整宽度。
                if (_moving) ApplyScale(_lastClientRect?.Height);
                else RefreshScale();
            });
        return IntPtr.Zero;
    }

    private void ApplyScale(int? clientHeight, bool updateWindowSize = true)
    {
        _uiScale = _workbench ? _api.Settings.ScaleValue ?? _uiScale : WindowGeometry.UiScale(_api.Settings.ScaleValue, clientHeight,
            User32.GetWindowDpiScale(new WindowInteropHelper(this).Handle));
        MinWidth = 0;
        if (_workbench)
        {
            MaxWidth = double.PositiveInfinity;
            var minimum = WorkbenchGeometry.MinimumSize(CurrentWorkArea(), _uiScale);
            MinWidth = minimum.Width; MinHeight = minimum.Height;
            ResizeMode = ResizeMode.CanResize;
            if (WindowState == WindowState.Normal)
            {
                Width = Math.Max(Width, MinWidth);
                Height = Math.Max(Height, MinHeight);
            }
            if (Browser.CoreWebView2 is not null) Browser.ZoomFactor = _uiScale;
            Topmost = _api.Settings.AlwaysOnTop;
            return;
        }
        MinHeight = 0;
        var work = CurrentWorkArea();
        MaxWidth = Math.Min(720 * _uiScale, work.Width);
        MinWidth = Math.Min((_collapsed && _collapseUpHeight is null ? 48 : 320) * _uiScale, work.Width);
        if (_api.Settings.FollowGame && updateWindowSize)
        {
            Width = _collapsed && _collapseUpHeight != null ? _expandedWidth
                : WindowGeometry.PanelWidth(_dock.Width, _uiScale, _collapsed);
            if (_collapsed && _collapseUpHeight is { } height) Height = height * _uiScale;
        }
        ResizeMode = _collapsed ? ResizeMode.CanMinimize : ResizeMode.CanResize;
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

    private void Redock()
    {
        _dock = _dock.Redock();
        _api.SettingsStore.SaveDock(_dock);
        if (!_workbench && _api.Settings.FollowGame && _lastGameRect is { } game) UpdateGame(game);
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
        if (_api.Settings.FollowGame && _lastGameRect is { } game)
        {
            UpdateGame(game);
            return;
        }
        ApplyScale(_lastClientRect?.Height);
        (Width, Height) = WindowGeometry.CollapsedSize(_expandedWidth, _expandedHeight, _uiScale, collapsed, upHeight);
    }

    private void RememberFreeBounds()
    {
        _dock = _dock with { FreeBounds = new(Left, Top,
            _collapsed ? _expandedWidth : Width, _collapsed ? _expandedHeight : Height) };
    }

    private async Task RefreshSettingsAsync(bool followedBefore)
    {
        _refreshKey = HotkeyParser.Parse(_api.Settings.RefreshKey);
        RefreshRouteHotkey(_api.Connection.GameElevated == true);
        if (_connectGame)
        {
            await Task.Run(() =>
            {
                if (_capture.Connection.Configure(_api.Settings.GameSource, _api.Settings.EmulatorSerial, _api.Settings.AdbAddress, _api.Settings.HideCaptureBorder))
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
        if (state.Source == "pc" && User32.IsIconic(state.Window)) _api.StopControl("lost");
        bool connected = state.Source == "pc" ? viewport is not null
            : _capture.Connection.IsAdb && state.Width > 0 && state.Problem is null;
        string? resolution = !connected ? null : _capture.Connection.IsAdb
            ? $"{state.Width}×{state.Height}" : $"{viewport!.Value.Width}×{viewport.Value.Height}";
        bool? gameElevated = connected ? state.Source == "pc" && ProcessElevation.ForWindow(state.Window) : null;
        RefreshRouteHotkey(gameElevated == true);
        _api.UpdateConnection(new(connected ? "connected" : "searching", resolution,
            connected ? _tracker.IsFullscreen : null, connected && _overlayMode,
            connected ? state.Source : null, state.Problem, connected ? _side : null, gameElevated));
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _api?.CloseLocalApi();
        if (_api is not null) await _api.StopControlAsync();
        _keyboardHook?.Dispose();
        if (_api is not null)
        {
            _dock = _dock with { LastWorkbench = _workbench };
            _api.SettingsStore.SaveDock(_dock);
        }
        if (_api is not null) await _api.StopRosterScanAsync();
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
        await _api.FinishCollectionsAsync();
        _routeSystemHotkey.Dispose(() => User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, 3));
        _fieldSystemHotkey.Dispose(() => User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, 4));
        UnregisterHotkey();
        _source?.RemoveHook(OnWindowMessage);
        _overlay.Close();
        _canClose = true;
        _ = Dispatcher.BeginInvoke(Close);
    }

    private System.Drawing.RectangleF? _upgradeButton;

    private void RefreshShopOverlay(bool animate = true)
    {
        _api.UpdateShopTracking(_shopItems);
        var items = _api.Settings.ShopMarkers ? _shopItems : [];
        _overlay.UpdateShopItems(items, animate, _api.Settings.MarkerStyles);
        _overlay.UpdateLayerSettings(_api.Settings.OverlayLayers);
        _overlay.UpdateUpgrade(_upgradeButton, _api.UpgradeTarget);
        if (!_connectGame || _closing || _capturingInformation || _lastGameRect is null
            || (!items.Any(i => i.IsTracked || i.IsSecondary) && !_overlay.HasOverlayContent))
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
