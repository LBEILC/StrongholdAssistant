// 敌人路线覆盖的绘制层（设计）：样式、依次循环、三种显示方式。参数和原型 src/ui/dev/route-overlay.html 一致；
// 数据来自 HostApi.BoardRoutesChanged，快捷键来自 HostApi.RouteHotkeyChanged。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed class RouteLayer : Canvas
{
    private static readonly Color WalkCore = Color.FromRgb(0xFF, 0x5A, 0x4E), WalkUnder = Color.FromRgb(0x1B, 0x08, 0x06);
    private static readonly Color FlyCore = Color.FromRgb(0xC7, 0x9B, 0xFF), FlyUnder = Color.FromRgb(0x14, 0x08, 0x1F);
    private static readonly Color BossCore = Color.FromRgb(0xF3, 0xB3, 0x22), BossUnder = Color.FromRgb(0x2A, 0x1D, 0x02);
    private static readonly FontFamily Font = new("Microsoft YaHei UI");
    private const double Shown = .85, Dim = .12;
    // 三种线错开：飞行往右上、首领往左下（单位：格）
    private static readonly Vector FlyShift = new(.2, -.26), BossShift = new(-.17, .2);

    private BoardRoutes? _routes;
    // 上一次有路线时的「地图|回合」：路线中途被清空又发回来（读不到休整期、打开信息页）不算新回合
    private string? _lastRound;
    private RouteOverlaySettings _settings = new();
    private Size _size;
    private readonly List<(UIElement Element, string Label)> _cycleItems = [];
    private TextBlock? _tagSub;
    private int _cycle;
    private bool _hotkeyDown, _hiddenByKey;
    private readonly DispatcherTimer _cycleTimer = new(), _peekTimer = new();

    /// <summary>有路线数据（不论此刻是否淡出）。覆盖层窗口据此决定要不要留着。</summary>
    public bool HasRoutes => _routes is not null;

    public RouteLayer()
    {
        IsHitTestVisible = false;
        Opacity = 0;
        _cycleTimer.Tick += (_, _) =>
        {
            _cycle = (_cycle + 1) % Math.Max(1, _cycleItems.Count);
            ShowCycle();
        };
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            if (!_hotkeyDown) Fade(false, 1200);
        };
    }

    public void Update(BoardRoutes? routes, RouteOverlaySettings settings, double width, double height)
    {
        string? key = routes is null ? null : $"{routes.MapId}|{routes.Round}";
        bool newRound = key is not null && key != _lastRound;
        if (key is not null) _lastRound = key;
        bool reappeared = routes is not null && _routes is null && !newRound;
        bool settingsChanged = settings != _settings;
        if (newRound) _hiddenByKey = false;
        if (newRound || settingsChanged || routes?.BossId != _routes?.BossId) _cycle = 0;
        _routes = routes;
        _settings = settings;
        _size = new Size(width, height);
        Rebuild();
        if (routes is null) Fade(false, 0);
        else if (newRound || settingsChanged || reappeared) ApplyDisplay(newRound);
    }

    public void Resize(double width, double height)
    {
        _size = new Size(width, height);
        Rebuild();
    }

    /// <summary>路线快捷键按下 / 松开。</summary>
    public void SetHotkey(bool down)
    {
        _hotkeyDown = down;
        if (_routes is null) return;
        if (_settings.Display == "always")
        {
            if (!down) return;
            _hiddenByKey = !_hiddenByKey;
            Fade(!_hiddenByKey, 160);
            return;
        }
        _peekTimer.Stop();
        Fade(down, down ? 120 : 220);
    }

    private void ApplyDisplay(bool newRound)
    {
        _peekTimer.Stop();
        switch (_settings.Display)
        {
            case "always":
                Fade(!_hiddenByKey, 180);
                break;
            case "hold":
                Fade(_hotkeyDown, 160);
                break;
            default: // peek：新回合先显示几秒再淡出
                if (newRound)
                {
                    Fade(true, 180);
                    _peekTimer.Interval = TimeSpan.FromSeconds(_settings.PeekSeconds);
                    _peekTimer.Start();
                }
                else Fade(_hotkeyDown, 160);
                break;
        }
    }

    private void Fade(bool show, int ms) =>
        BeginAnimation(OpacityProperty, new DoubleAnimation(show ? Shown : 0, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });

    // ── 绘制 ───────────────────────────────────────────────────────────

    private void Rebuild()
    {
        Children.Clear();
        _cycleItems.Clear();
        _tagSub = null;
        _cycleTimer.Stop();
        if (_routes is not { } data || _size.Width <= 0 || _size.Height <= 0) return;
        double k = _size.Height / 1080; // 线宽等按 1080p 设计，随画面缩放
        Point P(Point grid)
        {
            var (x, y) = data.Projection.Project(grid.X, grid.Y);
            return new Point(x * _size.Width, y * _size.Height);
        }

        // 按设置筛：飞行三档；联机首领只看自己一侧（自动时两边都画）
        var mainFly = data.Routes.Where(r => r.Motion == "fly" && !r.Boss).MaxBy(r => r.Count);
        var shown = data.Routes
            .Where(r => r.Boss || r.Motion != "fly" || _settings.Fly == "all" || (_settings.Fly == "main" && r == mainFly))
            .Where(r => r.Side is null || _settings.Side == "auto" || r.Side == _settings.Side)
            .ToList();

        if (_settings.Overlap == "cycle" && shown.Count > 1)
        {
            foreach (var route in shown)
            {
                var group = new Canvas { Opacity = Dim };
                Draw(group, [route], P, k);
                Children.Add(group);
                _cycleItems.Add((group, Label(route)));
            }
        }
        else
        {
            var group = new Canvas();
            Draw(group, shown, P, k);
            Children.Add(group);
        }
        foreach (var spot in data.BossSpots) DrawSpot(new Point(spot.Col, spot.Row), P, k);
        DrawTag(data, shown, P, k);

        if (_cycleItems.Count > 0)
        {
            _cycle %= _cycleItems.Count;
            ShowCycle();
            _cycleTimer.Interval = TimeSpan.FromSeconds(_settings.CycleSeconds);
            _cycleTimer.Start();
        }
    }

    private void ShowCycle()
    {
        for (int i = 0; i < _cycleItems.Count; i++)
            _cycleItems[i].Element.BeginAnimation(OpacityProperty,
                new DoubleAnimation(i == _cycle ? 1 : Dim, TimeSpan.FromMilliseconds(400)));
        if (_tagSub is not null)
            _tagSub.Text = $"{_cycle + 1}/{_cycleItems.Count} · {_cycleItems[_cycle].Label}";
    }

    private static string Label(EnemyRoute r)
    {
        string side = r.Side switch { "left" => " · 左侧", "right" => " · 右侧", _ => "" };
        if (r.Boss) return (r.Patrol ? "首领 · 巡逻" : "首领") + side;
        if (r.Motion == "fly") return $"飞行 {r.Count} 只";
        return $"地面 · {(r.Segments[0][0].Row < 2 ? "上路" : "下路")} {r.Count} 只";
    }

    private static void Draw(Canvas canvas, IReadOnlyList<EnemyRoute> routes, Func<Point, Point> P, double k)
    {
        // 地面：重合的单位格边合并成一条，按经过的敌人总数加粗
        var ground = routes.Where(r => !r.Boss && r.Motion != "fly").ToList();
        var groundEdges = Merge(ground, default);
        foreach (var e in groundEdges)
            canvas.Children.Add(Seg(P(e.A), P(e.B), WalkUnder, LineWidth(e.Count, k) + 3.5 * k, e.Approx ? .45 : .75));
        foreach (var e in groundEdges)
            canvas.Children.Add(Seg(P(e.A), P(e.B), WalkCore, LineWidth(e.Count, k), e.Approx ? .55 : 1));
        foreach (var r in ground)
            foreach (var seg in r.Segments) Chevrons(canvas, Units(seg, default), P, k, Color.FromRgb(0xFF, 0xE3, 0xDF), WalkUnder, 3);

        // 飞行：往右上偏，紫色虚线，同样合并
        var fly = routes.Where(r => !r.Boss && r.Motion == "fly").ToList();
        var flyEdges = Merge(fly, FlyShift);
        foreach (var e in flyEdges)
        {
            double w = LineWidth(e.Count, k) * .5;
            canvas.Children.Add(Seg(P(e.A), P(e.B), FlyUnder, w + 2.5 * k, .55, Dash(8 * k, 7 * k, w + 2.5 * k)));
        }
        foreach (var e in flyEdges)
        {
            double w = LineWidth(e.Count, k) * .5;
            canvas.Children.Add(Seg(P(e.A), P(e.B), FlyCore, w, e.Approx ? .5 : .9, Dash(8 * k, 7 * k, w)));
        }
        foreach (var r in fly)
            foreach (var seg in r.Segments) Chevrons(canvas, Units(seg, FlyShift), P, k, Color.FromRgb(0xF1, 0xE6, 0xFF), FlyUnder, 4);

        // 首领：往左下偏，金色粗线；巡逻画虚线；消失 / 出现处断开并画虚线圈
        foreach (var r in routes.Where(r => r.Boss))
        {
            var segments = r.Segments.Select(s => s.Select(p => new Point(p.Col, p.Row) + BossShift).ToList()).ToList();
            for (int i = 0; i < segments.Count; i++)
            {
                var screen = segments[i].Select(P).ToList();
                double w = 5 * k;
                canvas.Children.Add(Path(screen, BossUnder, w + 4 * k, .8, r.Patrol ? Dash(14 * k, 6 * k, w + 4 * k) : null));
                canvas.Children.Add(Path(screen, BossCore, w, r.Approx ? .6 : 1, r.Patrol ? Dash(14 * k, 6 * k, w) : null));
                Chevrons(canvas, Units(segments[i].Select(p => new RoutePoint(p.X, p.Y)).ToList(), default), P, k,
                    Color.FromRgb(0xFF, 0xF3, 0xCF), BossUnder, 2);
                if (segments.Count > 1)
                {
                    if (i < segments.Count - 1) Ring(canvas, P(segments[i][^1]), k, "消失");
                    if (i > 0) Ring(canvas, P(segments[i][0]), k, "出现");
                }
            }
            Badge(canvas, P(segments[0][0]), k);
        }
    }

    private sealed record Edge(Point A, Point B, int Count, bool Approx);

    private static List<Edge> Merge(IEnumerable<EnemyRoute> routes, Vector shift)
    {
        var edges = new Dictionary<(Point, Point), Edge>();
        foreach (var r in routes)
            foreach (var seg in r.Segments)
            {
                var units = Units(seg, shift);
                for (int i = 1; i < units.Count; i++)
                {
                    var (a, b) = (units[i - 1], units[i]);
                    var key = a.X < b.X || (a.X == b.X && a.Y <= b.Y) ? (a, b) : (b, a);
                    edges[key] = edges.TryGetValue(key, out var e)
                        ? e with { Count = e.Count + r.Count, Approx = e.Approx || r.Approx }
                        : new Edge(a, b, r.Count, r.Approx);
                }
            }
        return [.. edges.Values];
    }

    /// <summary>折线拆成相邻格中心之间的单位步（斜走也算一步），方便按格边合并。</summary>
    private static List<Point> Units(IReadOnlyList<RoutePoint> seg, Vector shift)
    {
        var points = new List<Point> { new Point(seg[0].Col, seg[0].Row) + shift };
        for (int i = 1; i < seg.Count; i++)
        {
            var a = new Point(seg[i - 1].Col, seg[i - 1].Row) + shift;
            var b = new Point(seg[i].Col, seg[i].Row) + shift;
            int n = Math.Max(1, (int)Math.Round(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y))));
            for (int t = 1; t <= n; t++) points.Add(a + (b - a) * t / n);
        }
        return points;
    }

    private static double LineWidth(int count, double k) => (3.5 + 2.6 * Math.Log2(1 + count / 2.0)) * k;

    // WPF 的虚线长度以线宽为单位，这里按像素给
    private static DoubleCollection Dash(double on, double off, double width) => [on / width, off / width];

    private static Line Seg(Point a, Point b, Color color, double width, double opacity, DoubleCollection? dash = null) => new()
    {
        X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
        Stroke = new SolidColorBrush(color) { Opacity = opacity },
        StrokeThickness = width,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeDashCap = PenLineCap.Round,
        StrokeDashArray = dash,
    };

    private static Polyline Path(IEnumerable<Point> points, Color color, double width, double opacity, DoubleCollection? dash) => new()
    {
        Points = new PointCollection(points),
        Stroke = new SolidColorBrush(color) { Opacity = opacity },
        StrokeThickness = width,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeDashCap = PenLineCap.Round,
        StrokeDashArray = dash,
    };

    /// <summary>沿路线每隔 every 个单位步放一个方向箭头（放在那一步的中点）。</summary>
    private static void Chevrons(Canvas canvas, List<Point> units, Func<Point, Point> P, double k, Color core, Color under, int every)
    {
        double s = 1.9 * k;
        for (int i = 1; i < units.Count; i++)
        {
            if (i % every != 0) continue;
            var (a, b) = (units[i - 1], units[i]);
            var mid = P(a + (b - a) * .5);
            var ahead = P(a + (b - a) * .55);
            var u = ahead - mid;
            if (u.Length == 0) continue;
            u.Normalize();
            var n = new Vector(-u.Y, u.X);
            var pts = new[] { mid - u * 3 * s - n * 4 * s, mid + u * 2 * s, mid - u * 3 * s + n * 4 * s };
            canvas.Children.Add(Path(pts, under, 3 * s, 1, null));
            canvas.Children.Add(Path(pts, core, 1.6 * s, 1, null));
        }
    }

    private static void Ring(Canvas canvas, Point at, double k, string text)
    {
        double r = 9 * k;
        var ring = new Ellipse
        {
            Width = 2 * r, Height = 2 * r,
            Fill = new SolidColorBrush(BossUnder) { Opacity = .55 },
            Stroke = new SolidColorBrush(BossCore), StrokeThickness = 2.5 * k,
            StrokeDashArray = Dash(4 * k, 3 * k, 2.5 * k),
        };
        SetLeft(ring, at.X - r);
        SetTop(ring, at.Y - r);
        canvas.Children.Add(ring);
        Place(canvas, Text(text, 15 * k, Color.FromRgb(0xFF, 0xF3, 0xCF), true), at.X, at.Y - 14 * k, .5, 1);
    }

    /// <summary>首领起点的标签（不画图标：覆盖层没有 webp 解码，按约定也不自己画图标）。</summary>
    private static void Badge(Canvas canvas, Point at, double k)
    {
        var tag = new Border
        {
            Background = new SolidColorBrush(BossCore),
            CornerRadius = new CornerRadius(2 * k),
            Padding = new Thickness(6 * k, 1 * k, 6 * k, 1 * k),
            Child = Text("首领", 13 * k, BossUnder, false),
        };
        Place(canvas, tag, at.X, at.Y - 10 * k, .5, 1);
    }

    private void DrawSpot(Point grid, Func<Point, Point> P, double k)
    {
        var at = P(grid + BossShift);
        double r = 30 * k;
        var ring = new Ellipse
        {
            Width = 2 * r, Height = 2 * r,
            Stroke = new SolidColorBrush(BossCore), StrokeThickness = 3 * k,
            RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = new ScaleTransform(),
        };
        SetLeft(ring, at.X - r);
        SetTop(ring, at.Y - r);
        Children.Add(ring);
        var pulse = new DoubleAnimation(.93, 1.35, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        ring.BeginAnimation(OpacityProperty, new DoubleAnimation(.9, .2, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        var canvas = new Canvas();
        Badge(canvas, at, k);
        Children.Add(canvas);
    }

    /// <summary>回合标签：贴在棋盘右上角（出生点上方），说明这是哪一回合的预测。</summary>
    private void DrawTag(BoardRoutes data, List<EnemyRoute> shown, Func<Point, Point> P, double k)
    {
        int walk = shown.Where(r => !r.Boss && r.Motion != "fly").Sum(r => r.Count);
        int fly = shown.Where(r => !r.Boss && r.Motion == "fly").Sum(r => r.Count);
        string sub = string.Join(" · ", new[]
        {
            walk > 0 ? $"地面 {walk}" : null,
            fly > 0 ? $"飞行 {fly}" : null,
            shown.Any(r => r.Boss) || data.BossSpots.Count > 0 ? "首领" : null,
        }.OfType<string>());
        _tagSub = Text(sub, 12.5 * k, Color.FromRgb(0xAE, 0xB6, 0xC0), false);
        var panel = new StackPanel();
        panel.Children.Add(Text($"第 {data.Round} 回合路线", 15 * k, Color.FromRgb(0xF2, 0xF4, 0xF6), true));
        panel.Children.Add(_tagSub);
        var tag = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x15)) { Opacity = .82 },
            BorderBrush = new SolidColorBrush(WalkCore) { Opacity = .7 },
            BorderThickness = new Thickness(1.5 * k),
            CornerRadius = new CornerRadius(4 * k),
            Padding = new Thickness(10 * k, 4 * k, 12 * k, 5 * k),
            MinWidth = 150 * k,
            Child = panel,
        };
        var anchor = P(new Point(7.2, -.35));
        Place(this, tag, anchor.X, anchor.Y, 1, 1);
    }

    private static TextBlock Text(string text, double size, Color color, bool bold) => new()
    {
        Text = text,
        FontFamily = Font,
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = new SolidColorBrush(color),
        Effect = bold ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = .9 } : null,
    };

    /// <summary>按锚点放元素：ax / ay 是元素自身的对齐比例（.5,1 = 底边中点对准锚点）。</summary>
    private static void Place(Canvas canvas, FrameworkElement element, double x, double y, double ax, double ay)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(element, x - element.DesiredSize.Width * ax);
        SetTop(element, y - element.DesiredSize.Height * ay);
        canvas.Children.Add(element);
    }
}
