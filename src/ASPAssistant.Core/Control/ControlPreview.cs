using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ASPAssistant.Core.Control;

public static class ControlPreview
{
    public static byte[] Render(ControlFrame frame, CompiledAction plan)
    {
        using var input = new MemoryStream(frame.Image);
        using var source = new Bitmap(input);
        using var image = new Bitmap(source);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = Math.Min(image.Width / 1920f, image.Height / 1080f);
        using var pen = new Pen(Color.FromArgb(0xF3, 0xB3, 0x22), 4 * s);
        using var arrow = new AdjustableArrowCap(5, 6);
        using var arrowPen = (Pen)pen.Clone();
        arrowPen.CustomEndCap = arrow;
        using var font = new Font("Microsoft YaHei", Math.Max(9, 24 * s), FontStyle.Bold, GraphicsUnit.Pixel);
        using var back = new SolidBrush(Color.FromArgb(215, 0, 0, 0));
        void Label(string text, float x, float y)
        {
            var size = g.MeasureString(text, font);
            x = Math.Clamp(x, 0, Math.Max(0, image.Width - size.Width - 8 * s));
            y = Math.Clamp(y, 0, Math.Max(0, image.Height - size.Height - 4 * s));
            g.FillRectangle(back, x, y, size.Width + 8 * s, size.Height + 4 * s);
            g.DrawString(text, font, Brushes.White, x + 4 * s, y + 2 * s);
        }
        PointF Point(double x, double y) => new((float)(x * image.Width), (float)(y * image.Height));
        float r = 18 * s;
        void Circle(PointF p) => g.DrawEllipse(pen, p.X - r, p.Y - r, r * 2, r * 2);
        Label(plan.Text, 12 * s, 12 * s);
        int number = 0;
        var labels = new Dictionary<PointF, List<int>>();
        foreach (var step in plan.Steps)
        {
            if (step is WaitStep) continue;
            number++;
            PointF point;
            switch (step)
            {
                case TapStep t:
                    point = Point(t.X, t.Y);
                    Circle(point);
                    break;
                case DragStep d:
                    point = Point(d.X1, d.Y1);
                    var end = Point(d.X2, d.Y2);
                    Circle(point);
                    g.DrawLine(arrowPen, point, end);
                    g.DrawRectangle(pen, end.X - r, end.Y - r, r * 2, r * 2);
                    break;
                case KeyStep k:
                    Label($"{number} 按 {k.Key}", image.Width - 165 * s, 12 * s + (number - 1) * 42 * s);
                    continue;
                default: throw new ArgumentOutOfRangeException(nameof(step));
            }
            if (!labels.TryGetValue(point, out var numbers)) labels[point] = numbers = [];
            numbers.Add(number);
        }
        foreach (var (point, numbers) in labels)
            Label(string.Join("、", numbers), point.X + r, point.Y - r);
        using var output = new MemoryStream();
        image.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
