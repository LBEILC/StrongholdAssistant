using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

/// <summary>覆盖层和离屏验收共用的卡身标记画布。</summary>
public sealed class ShopMarkerLayer : Canvas
{
    private readonly Dictionary<string, FrameworkElement> _markers = [];
    private (double Width, double Height) _viewport;
    public event Action? Emptied;

    public ShopMarkerLayer() => IsHitTestVisible = false;

    public void Update(IReadOnlyList<ShopItem> items, double width, double height, bool animate = true)
    {
        // 首次显示前 WPF 尚未给出客户区尺寸，SizeChanged 后再排版。
        if (width <= 0 || height <= 0) return;
        bool resized = _viewport != (width, height);
        _viewport = (width, height);
        if (resized)
        {
            foreach (FrameworkElement child in Children) Stop(child);
            Children.Clear();
            _markers.Clear();
        }
        var tracked = items.Where(i => i.IsTracked || i.IsSecondary).ToDictionary(i => i.Id);
        foreach (var id in _markers.Keys.Except(tracked.Keys).ToArray()) Retire(id, animate && !resized);
        foreach (var item in tracked.Values)
        {
            var r = item.CardRegion;
            double w = r.Width * width, h = r.Height * height;
            int kind = item.IsTracked ? (item.IsKey ? 2 : 1) : 0;
            if (_markers.TryGetValue(item.Id, out var old) &&
                (!Equals(old.Tag, kind) || old.Width != w || old.Height != h))
                Retire(item.Id, animate && !resized);
            if (!_markers.TryGetValue(item.Id, out var marker))
            {
                marker = Create(w, h, kind, animate && !resized);
                _markers.Add(item.Id, marker);
                Children.Add(marker);
                if (animate && !resized) Appear(marker);
            }
            SetLeft(marker, r.X * width);
            SetTop(marker, r.Y * height);
        }
    }

    private void Retire(string id, bool animate)
    {
        var marker = _markers[id];
        _markers.Remove(id);
        void Remove()
        {
            Stop(marker);
            Children.Remove(marker);
            if (Children.Count == 0) Emptied?.Invoke();
        }
        if (!animate) { Remove(); return; }
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) => Remove();
        marker.BeginAnimation(OpacityProperty, fade);
    }

    private static void Appear(FrameworkElement marker)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);
        marker.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        var scale = (ScaleTransform)marker.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.04, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.04, 1, duration) { EasingFunction = ease });
    }

    private static void Stop(FrameworkElement marker)
    {
        marker.BeginAnimation(OpacityProperty, null);
        var scale = (ScaleTransform)marker.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        foreach (var shape in ((Canvas)marker).Children.OfType<Shape>())
            if (shape.Effect is DropShadowEffect glow)
            {
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
            }
    }


    private static Canvas Create(double w, double h, int kind, bool animate)
    {
        bool secondary = kind == 0, key = kind == 2;
        var mint = new SolidColorBrush(Color.FromRgb(0x3C, 0xF0, 0xC2));
        var gold = Color.FromRgb(0xF3, 0xB3, 0x22);
        var dark = new SolidColorBrush(key ? Color.FromRgb(0x2A, 0x1D, 0x02) : Color.FromRgb(0x06, 0x23, 0x1B));
        double offset = .012 * w, fw = w + 2 * offset, fh = h + 2 * offset;
        double thickness = (secondary ? .012 : .017) * w;
        var canvas = new Canvas
        {
            Width = w, Height = h, Tag = kind,
            RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1)
        };
        void Add(FrameworkElement element, double x, double y)
        {
            SetLeft(element, x); SetTop(element, y); canvas.Children.Add(element);
        }
        Add(new Rectangle
        {
            Width = fw + 4, Height = fh + 4, StrokeThickness = 2,
            Stroke = new SolidColorBrush(Color.FromArgb(secondary ? (byte)166 : (byte)191, 0, 0, 0))
        }, -offset - 2, -offset - 2);
        Add(new Rectangle
        {
            Width = fw - 2 * thickness, Height = fh - 2 * thickness, StrokeThickness = .012 * w,
            Stroke = new SolidColorBrush(Color.FromArgb(secondary ? (byte)179 : (byte)204, 0, 0, 0))
        }, -offset + thickness, -offset + thickness);
        var frame = new Rectangle
        {
            Width = fw, Height = fh, Stroke = mint, StrokeThickness = thickness,
            StrokeDashArray = secondary ? new DoubleCollection { 4, 3 } : null
        };
        if (!secondary)
        {
            var glow = new DropShadowEffect
            {
                Color = Color.FromRgb(0x22, 0xE3, 0xB2), ShadowDepth = 0, BlurRadius = .075 * w, Opacity = .5
            };
            frame.Effect = glow;
            // 入场结束后 1.1 秒闪两次，之后恢复基值；缩放重排和静态叠图不闪。
            DoubleAnimation Pulse(double from, double to) => new(from, to, TimeSpan.FromMilliseconds(275))
            {
                BeginTime = TimeSpan.FromMilliseconds(180), AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2), FillBehavior = FillBehavior.Stop,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            if (animate)
            {
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, Pulse(.5, .95));
                glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, Pulse(.075 * w, .14 * w));
            }
        }
        Add(frame, -offset, -offset);
        if (!secondary)
        {
            double stroke = .029 * w, arm = .115 * w;
            double left = -offset + stroke / 2, top = -offset + stroke / 2;
            double right = w + offset - stroke / 2, bottom = h + offset - stroke / 2;
            var corners = new PathGeometry([
                new PathFigure(new Point(left, top + arm - stroke / 2),
                    [new LineSegment(new Point(left, top), true), new LineSegment(new Point(left + arm - stroke / 2, top), true)], false),
                new PathFigure(new Point(right - arm + stroke / 2, bottom),
                    [new LineSegment(new Point(right, bottom), true), new LineSegment(new Point(right, bottom - arm + stroke / 2), true)], false)
            ]);
            canvas.Children.Add(new System.Windows.Shapes.Path
                { Data = corners, StrokeThickness = stroke + 3, Stroke = Brushes.Black });
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = corners, StrokeThickness = stroke,
                Stroke = new SolidColorBrush(key ? Color.FromRgb(0xFF, 0xD3, 0x6A) : Color.FromRgb(0x7D, 0xFA, 0xDB))
            });
        }
        var tag = new Border
        {
            Height = .13 * w, Padding = new Thickness(.05 * w, 0, .05 * w, 0),
            Background = secondary ? new SolidColorBrush(Color.FromArgb(235, 0x06, 0x14, 0x10))
                : key ? new SolidColorBrush(gold) : mint,
            BorderBrush = secondary ? mint : null, BorderThickness = new Thickness(secondary ? .008 * w : 0)
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (!secondary)
            content.Children.Add(new System.Windows.Shapes.Path
            {
                // Remix Icon star-fill：沿用现有路径。
                Data = Geometry.Parse("M12.0006 18.26L4.94715 22.2082L6.52248 14.2799L0.587891 8.7918L8.61493 7.84006L12.0006 0.5L15.3862 7.84006L23.4132 8.7918L17.4787 14.2799L19.054 22.2082L12.0006 18.26Z"),
                Fill = dark, Stretch = Stretch.Uniform, Width = .075 * w, Height = .075 * w,
                Margin = new Thickness(0, 0, .025 * w, 0), VerticalAlignment = VerticalAlignment.Center
            });
        content.Children.Add(new TextBlock
        {
            Text = secondary ? "可抓" : key ? "关键" : "追踪", Foreground = secondary ? mint : dark,
            FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = .08 * w,
            FontWeight = secondary ? FontWeights.SemiBold : FontWeights.Bold
        });
        tag.Child = content;
        var label = new Border
        {
            Child = tag, BorderBrush = new SolidColorBrush(Color.FromArgb(191, 0, 0, 0)), BorderThickness = new Thickness(2),
            Effect = secondary ? null : new DropShadowEffect
            {
                Color = key ? gold : Color.FromRgb(0x22, 0xE3, 0xB2), ShadowDepth = 0, BlurRadius = .05 * w, Opacity = .55
            }
        };
        Add(label, -offset - 2, -offset - .159 * w - 4);
        return canvas;
    }
}
