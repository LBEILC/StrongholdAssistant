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
    public void Update(OverlayPlacementPlan? plan, FieldState? field, BoardProjection? projection,
        bool enabled, double width, double height)
    {
        Children.Clear();
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return;
        // 新输入计算期间保留上一份推荐；离局、无棋盘和主动关闭仍由宿主清除。
        if (!enabled || plan is null || field is null || projection is null) return;
        var moves = plan.Moves.Where(m => m.From != m.To).ToArray();
        var arrivals = moves.Select(m => m.To).Where(cell => cell.Zone == "board").ToHashSet();
        foreach (var move in moves.OrderByDescending(m => m.To.Zone == "bench"))
        {
            bool leaving = move.To.Zone == "bench";
            var cell = leaving ? move.From : move.To;
            if (cell.Zone != "board") continue;
            Point Project(double col, double row)
            {
                var p = projection.Project(col, row);
                return new(p.X * width, p.Y * height);
            }
            // 四个角沿格坐标各向中心缩 8%。
            Point[] points = [Project(cell.Col + .08, cell.Row + .08), Project(cell.Col + .92, cell.Row + .08),
                Project(cell.Col + .92, cell.Row + .92), Project(cell.Col + .08, cell.Row + .92)];
            var color = leaving ? Color.FromRgb(0xFF, 0x8A, 0x80) : Color.FromRgb(0x22, 0xE3, 0xB2);
            var brush = new SolidColorBrush(color);
            if (leaving && arrivals.Contains(cell))
            {
                var badge = new Border { Background = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0)),
                    Padding = new Thickness(height * .004, 0, height * .004, 0),
                    Child = new TextBlock { Text = "撤 " + move.Operator, Foreground = brush, FontSize = height * .013 } };
                SetLeft(badge, points[0].X);
                SetTop(badge, points[0].Y);
                Children.Add(badge);
                continue;
            }
            Children.Add(new Polygon { Points = new(points), Stroke = brush, StrokeThickness = height * .0015,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(255 * .12), color.R, color.G, color.B)),
                StrokeDashArray = leaving ? new DoubleCollection { 4, 3 } : null });
            var label = new Border { Background = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0)),
                Padding = new Thickness(height * .004, 0, height * .004, 0),
                Child = new TextBlock { Text = move.Operator + (leaving ? " 下场" : ""), Foreground = Brushes.White, FontSize = height * .013 } };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(label, (points[2].X + points[3].X - label.DesiredSize.Width) / 2);
            SetTop(label, (points[2].Y + points[3].Y) / 2);
            Children.Add(label);
            if (!leaving && move.Facing is { } facing)
            {
                int edge = facing switch { "up" => 0, "right" => 1, "down" => 2, "left" => 3, _ => -1 };
                if (edge < 0) continue;
                var a = points[edge]; var b = points[(edge + 1) % 4];
                var middle = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                var tangent = b - a; tangent.Normalize();
                var outward = new Vector(tangent.Y, -tangent.X);
                // TODO(design): 方向三角尺寸暂取客户区高度 0.8%，待预览验收。
                double size = height * .008;
                Children.Add(new Polygon { Fill = brush, Points = new PointCollection
                    { middle + outward * size, middle - tangent * size / 2, middle + tangent * size / 2 } });
            }
        }
    }
}
