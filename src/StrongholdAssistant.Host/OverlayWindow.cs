// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
// 2026-11-11：整卡标记改用共享绘制层，按固定卡位定位。
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed class OverlayWindow : Window
{
    private readonly ShopMarkerLayer _canvas = new();
    private IReadOnlyList<ShopItem> _items = [];
    public bool HasMarkers => _canvas.Children.Count > 0;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Content = _canvas;
        _canvas.Emptied += Hide;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            User32.SetWindowLong(hwnd, User32.GWL_EXSTYLE,
                User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE)
                | User32.WS_EX_TRANSPARENT | User32.WS_EX_TOOLWINDOW | User32.WS_EX_NOACTIVATE);
        };
        SizeChanged += (_, _) => UpdateShopItems(_items);
    }

    public void UpdatePosition(RECT client) =>
        User32.SetWindowBounds(new WindowInteropHelper(this).EnsureHandle(), client);

    public void UpdateShopItems(IReadOnlyList<ShopItem> items)
    {
        _items = items;
        _canvas.Update(items, ActualWidth, ActualHeight);
    }
}
