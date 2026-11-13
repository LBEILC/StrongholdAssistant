// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
// 2026-11-11：整卡标记改用共享绘制层，按固定卡位定位。
// 2026-11-13：加敌人路线绘制层（RouteLayer），画在商店标记下面。
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed class OverlayWindow : Window
{
    private readonly ShopMarkerLayer _canvas = new();
    private readonly RouteLayer _routes = new();
    private BoardRoutes? _boardRoutes;
    private RouteOverlaySettings _routeSettings = new();
    private IReadOnlyList<ShopItem> _items = [];
    private RECT? _client;
    public bool HasMarkers => _canvas.Children.Count > 0;
    public bool HasRoutes => _routes.HasRoutes;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Content = new System.Windows.Controls.Grid { Children = { _routes, _canvas } };
        // 商店标记清空时，还有路线就留着窗口
        _canvas.Emptied += () => { if (!HasRoutes) Hide(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
                User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE)
                | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) =>
        {
            UpdateShopItems(_items);
            _routes.Resize(ActualWidth, ActualHeight);
        };
    }

    public void UpdatePosition(RECT client)
    {
        _client = client;
        User32.SetWindowBounds(new WindowInteropHelper(this).EnsureHandle(), client);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // 等 WPF 应用建议矩形后，以最近的游戏客户区恢复物理尺寸。
        Dispatcher.BeginInvoke(() =>
        {
            if (_client is { } client) UpdatePosition(client);
            UpdateShopItems(_items);
            _routes.Resize(ActualWidth, ActualHeight);
        });
    }

    public void UpdateShopItems(IReadOnlyList<ShopItem> items, bool animate = true)
    {
        _items = items;
        _canvas.Update(items, ActualWidth, ActualHeight, animate);
    }

    public void UpdateRoutes(BoardRoutes? routes, RouteOverlaySettings settings)
    {
        _boardRoutes = routes;
        _routeSettings = settings;
        _routes.Update(routes, settings, ActualWidth, ActualHeight);
    }

    public void UpdateRouteSettings(RouteOverlaySettings settings)
    {
        if (settings != _routeSettings) UpdateRoutes(_boardRoutes, settings);
    }

    public void SetRouteTapOnly(bool value)
    {
        _routes.SetHotkey(false);
        _routes.TapOnly = value;
    }
    public void TapRouteHotkey()
    {
        ASPAssistant.Core.AppLogger.Info("Board", "系统热键 OverlayWindow → RouteLayer.TapHotkey");
        _routes.TapHotkey();
    }

    public void SetRouteHotkey(bool down)
    {
        ASPAssistant.Core.AppLogger.Info("Board", $"快捷键 OverlayWindow → RouteLayer.SetHotkey down={down} routes={HasRoutes} visible={IsVisible} display={_routeSettings.Display}");
        _routes.SetHotkey(down);
        ASPAssistant.Core.AppLogger.Info("Board", $"快捷键 RouteLayer.SetHotkey 已返回 down={down}");
    }
}
