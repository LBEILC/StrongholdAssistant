using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

// 独立绘制输入；任务 170 合入后在接线处转换，不共用推荐算法模型。
public sealed record OverlayPlacementMove(string Operator, FieldCell From, FieldCell To, string? Facing);
public sealed record OverlayPlacementPlan(string BasedOn, IReadOnlyList<OverlayPlacementMove> Moves);

public sealed class PlacementLayer : Canvas
{
    private sealed record Marker(string Operator, FieldCell From, FieldCell To, string? Facing, bool Leaving)
    {
        public FieldCell Cell => Leaving ? From : To;
    }

    public void Update(OverlayPlacementPlan? plan, FieldState? field, BoardProjection? projection,
        bool enabled, double width, double height)
    {
        Children.Clear();
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        // 新输入计算期间保留上一份推荐；离局、无棋盘和主动关闭仍由宿主清除。
        if (!enabled || plan is null || field is null || projection is null) return;
        // 撤退画在原格、上场画在目标格：一个下场的人和一个顶上来的人画在同一格时（换人最常见的样子），
        // 两者的矩形和标签坐标完全一致，先画撤退、上场后画并把它缩到左上角，否则「该放谁」会被整块盖住。
        var markers = plan.Moves.Where(m => m.From != m.To)
            .Select(m => new Marker(m.Operator, m.From, m.To, m.Facing, m.To.Zone == "bench"))
            .Where(m => m.Cell.Zone == "board")
            .ToList();
        var shared = markers.GroupBy(m => m.Cell).Where(g => g.Select(m => m.Leaving).Distinct().Count() > 1)
            .Select(g => g.Key).ToHashSet();
        foreach (var marker in markers.OrderByDescending(m => m.Leaving))
            Draw(marker, shared.Contains(marker.Cell), projection, width, height);
    }

    private void Draw(Marker marker, bool sharesCell, BoardProjection projection, double width, double height)
    {
        var cell = marker.Cell;
        Point Project(double col, double row)
        {
            var p = projection.Project(col, row);
            return new(p.X * width, p.Y * height);
        }
        var color = marker.Leaving ? Color.FromRgb(0xFF, 0x8A, 0x80) : Color.FromRgb(0x22, 0xE3, 0xB2);
        var brush = new SolidColorBrush(color);
        double stroke = Math.Max(1, height * .0015);
        if (marker.Leaving && sharesCell)
        {
            // 只占左上角这一小块，把整格留给要顶上来的人。
            Point[] tag = [Project(cell.Col + .08, cell.Row + .08), Project(cell.Col + .52, cell.Row + .08),
                Project(cell.Col + .52, cell.Row + .34), Project(cell.Col + .08, cell.Row + .34)];
            Children.Add(new Polygon { Points = new(tag), Stroke = brush, StrokeThickness = stroke,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * .18), color.R, color.G, color.B)),
                StrokeDashArray = new DoubleCollection { 4, 3 } });
            var tagLabel = Label("撤 " + marker.Operator, height * .011);
            SetLeft(tagLabel, tag[0].X + stroke);
            SetTop(tagLabel, tag[0].Y + stroke);
            Children.Add(tagLabel);
            return;
        }
        // 四个角沿格坐标各向中心缩 8%。
        Point[] points = [Project(cell.Col + .08, cell.Row + .08), Project(cell.Col + .92, cell.Row + .08),
            Project(cell.Col + .92, cell.Row + .92), Project(cell.Col + .08, cell.Row + .92)];
        Children.Add(new Polygon { Points = new(points), Stroke = brush, StrokeThickness = stroke,
            Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * .12), color.R, color.G, color.B)),
            StrokeDashArray = marker.Leaving ? new DoubleCollection { 4, 3 } : null });
        var label = Label(marker.Operator + (marker.Leaving ? " 下场" : ""), height * .013);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        SetLeft(label, (points[2].X + points[3].X - label.DesiredSize.Width) / 2);
        SetTop(label, (points[2].Y + points[3].Y) / 2);
        Children.Add(label);
        if (marker.Leaving || marker.Facing is not { } facing) return;
        int edge = facing switch { "up" => 0, "right" => 1, "down" => 2, "left" => 3, _ => -1 };
        if (edge < 0) return;
        var a = points[edge]; var b = points[(edge + 1) % 4];
        var middle = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var tangent = b - a; tangent.Normalize();
        var outward = new Vector(tangent.Y, -tangent.X);
        double size = height * .008;
        Children.Add(new Polygon { Fill = brush, Points = new PointCollection
            { middle + outward * size, middle - tangent * size / 2, middle + tangent * size / 2 } });
    }

    // 字号必须大于 0：v1.0.0 有过 FontSize 为 0 闪退（issue #5）。
    private static Border Label(string text, double fontSize)
    {
        double size = Math.Max(10, fontSize);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0)),
            Padding = new Thickness(Math.Max(2, size * .3), 0, Math.Max(2, size * .3), 0),
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = size },
        };
    }
}
