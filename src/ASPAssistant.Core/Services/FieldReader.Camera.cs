using System.Drawing;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed partial class FieldReader
{
    // 角标属于界面，格宽属于场景。用亮框两侧的内边距量出界面缩放，
    // 防止把角标相对人物的位移也算进镜头。基准画幅的内框宽为 34 像素。
    private static double BadgeScale(Bitmap frame, IReadOnlyList<FieldBadge> badges, double zoom)
    {
        var widths = new List<int>();
        foreach (var badge in badges)
        {
            int x = (int)badge.X, y = (int)badge.Y;
            int Edge(int side)
            {
                int strongest = 0, edge = 0;
                for (int d = 13; d < 24; d++)
                {
                    int contrast = 0;
                    for (int row = -4; row <= 4; row += 4)
                    {
                        var outer = frame.GetPixel(x + side * (d + 1), y + row);
                        var inner = frame.GetPixel(x + side * (d - 2), y + row);
                        contrast += outer.R + outer.G + outer.B - inner.R - inner.G - inner.B;
                    }
                    if (contrast <= strongest) continue;
                    strongest = contrast;
                    edge = d;
                }
                return edge;
            }
            int left = Edge(-1), right = Edge(1);
            if (left > 0 && right > 0) widths.Add(left + right);
        }
        if (widths.Count == 0) return 1;
        widths.Sort();
        double width = widths[widths.Count / 2];
        // 两边各有一个像素的取整误差。镜头本身放大时，不能重复放大角标偏移。
        double expected = 34 * zoom;
        return width > expected + 2 ? width / expected : 1;
    }

    // 地板颜色能辨别地图，但在同色格子内平移仍可能得到几乎相同的分数。
    // 在标准画幅中用多个可见阶数标记联合定位；单个角标不能移动棋盘。
    private static (double Scale, double X, double Y) FitFieldCamera(Bitmap bitmap, IReadOnlyList<FieldBadge> badges,
        Dictionary<FieldCell, (double X, double Y)> anchors)
    {
        var pairs = new List<(int Badge, double X, double Y, double Dx, double Dy)>();
        for (int i = 0; i < badges.Count; i++)
        {
            var badge = badges[i];
            foreach (var (cell, anchor) in anchors)
            {
                // 以画面中心为缩放原点；高台标记较平地上移 26 像素。
                double x = anchor.X * 1920 / bitmap.Width, y = anchor.Y * 1080 / bitmap.Height;
                double dx = badge.X - x;
                if (Math.Abs(dx) > 58) continue;
                for (int lift = 0; lift <= (cell.Zone == "board" ? 26 : 0); lift += 26)
                {
                    double dy = badge.Y - y + lift;
                    if (Math.Abs(dy) <= 45) pairs.Add((i, x - 960, y - 540, dx, dy));
                }
            }
        }
        var weights = new double[badges.Count];
        double Score(double scale, double dx, double dy)
        {
            Array.Clear(weights);
            foreach (var p in pairs)
            {
                double ex = p.Dx - p.X * scale - dx, ey = p.Dy - p.Y * scale - dy;
                double error = Math.Max(Math.Abs(ex), Math.Abs(ey));
                weights[p.Badge] = Math.Max(weights[p.Badge], 1 - error * error / 225);
            }
            return weights.Sum();
        }
        double initial = Score(0, 0, 0), best = initial;
        (double Scale, double X, double Y) fit = (1, 0, 0);
        for (int s = -20; s <= 30; s++)
        {
            double scale = s * .005;
            foreach (var p in pairs)
            {
                double dx = p.Dx - p.X * scale, dy = p.Dy - p.Y * scale;
                if (Math.Abs(dx) > 34 || Math.Abs(dy) > 34) continue;
                double score = Score(scale, dx, dy);
                if (score <= best) continue;
                best = score;
                fit = (1 + scale, dx / 1920, dy / 1080);
            }
        }
        return best >= 2.7 && best - initial > .65 ? fit : (1, 0, 0);
    }
}
