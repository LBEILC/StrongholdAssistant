// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
// 2026-11-11：从上游 OverlayWindow 改编，仅保留商店标记，按任务 003 更新样式与移除行为。
// 2026-11-11：按上游补回同尺寸下的 6 DIP 位置防抖。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed class OverlayWindow : Window
{
    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Dictionary<string, FrameworkElement> _markers = [];
    private IReadOnlyList<ShopItem> _items = [];
    private (double Width, double Height) _markerViewport;

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
        var sameViewport = _markerViewport == (ActualWidth, ActualHeight);
        _markerViewport = (ActualWidth, ActualHeight);
        var tracked = items.Where(i => i.IsTracked || i.IsSecondary).ToDictionary(i => i.Id);
        foreach (var id in _markers.Keys.Except(tracked.Keys).ToArray())
        {
            _canvas.Children.Remove(_markers[id]);
            _markers.Remove(id);
        }
        var size = 28 * ActualHeight / 1080;
        foreach (var item in tracked.Values)
        {
            var secondary = !item.IsTracked;
            if (_markers.TryGetValue(item.Id, out var previous) && !Equals(previous.Tag, secondary))
            {
                _canvas.Children.Remove(previous);
                _markers.Remove(item.Id);
            }
            if (!_markers.TryGetValue(item.Id, out var marker))
            {
                marker = CreateMarker(secondary);
                _markers.Add(item.Id, marker);
                _canvas.Children.Add(marker);
                marker.BeginAnimation(OpacityProperty, new DoubleAnimation(0, secondary ? .75 : 1, TimeSpan.FromMilliseconds(120)));
            }
            marker.Width = marker.Height = size;
            var (x, y) = WindowGeometry.ShopMarkerPosition(item.OcrRegion, ActualWidth, ActualHeight, size);
            if (sameViewport && Math.Abs(Canvas.GetLeft(marker) - x) < 6
                && Math.Abs(Canvas.GetTop(marker) - y) < 6) continue;
            Canvas.SetLeft(marker, x);
            Canvas.SetTop(marker, y);
        }
    }

    private static Grid CreateMarker(bool secondary)
    {
        var grid = new Grid { Tag = secondary, Opacity = secondary ? .75 : 1 };
        var mint = new SolidColorBrush(Color.FromRgb(0x22, 0xE3, 0xB2));
        grid.Children.Add(new Ellipse
        {
            Fill = secondary ? null : new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromRgb(0x5E, 0xF5, 0xCF), 0),
                    new(Color.FromRgb(0x22, 0xE3, 0xB2), .55),
                    new(Color.FromRgb(0x10, 0xA9, 0x82), 1)
                }
            },
            Stroke = secondary ? mint : new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), StrokeThickness = 2,
            Effect = secondary ? null : new DropShadowEffect
                { Color = Color.FromRgb(0x22, 0xE3, 0xB2), BlurRadius = 10, ShadowDepth = 0, Opacity = .45 }
        });
        // Remix Icon star-fill：沿用界面已采用的图标路径。
        var star = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M12.0006 18.26L4.94715 22.2082L6.52248 14.2799L0.587891 8.7918L8.61493 7.84006L12.0006 0.5L15.3862 7.84006L23.4132 8.7918L17.4787 14.2799L19.054 22.2082L12.0006 18.26Z"),
            Fill = secondary ? null : new SolidColorBrush(Color.FromRgb(0x06, 0x23, 0x1B)),
            Stroke = secondary ? mint : null, StrokeThickness = 2, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        grid.SizeChanged += (_, _) => { star.Width = grid.ActualWidth * .55; star.Height = grid.ActualHeight * .55; };
        grid.Children.Add(star);
        return grid;
    }
}
