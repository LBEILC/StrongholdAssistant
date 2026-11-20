using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;

namespace ASPAssistant.Core.Services;

// 可露希尔升级卡的黑色 LEVEL 标签有左下尖角；以标签宽度量字形，不依赖商品数量。
internal sealed record ClosureUpgradeCard(int Center, double Scale, int Level, Rectangle PriceArea, Rectangle Caption)
{
    public static ClosureUpgradeCard? Locate(Bitmap image)
    {
        double s = Math.Min(image.Width / 1920d, image.Height / 1080d);
        var area = new Rectangle(0, image.Height - (int)(250 * s), (int)(image.Width * .65), (int)(66 * s));
        using var crop = image.Clone(area, PixelFormat.Format24bppRgb);
        using var encoded = new MemoryStream();
        crop.Save(encoded, ImageFormat.Bmp);
        using var gray = Cv2.ImDecode(encoded.ToArray(), ImreadModes.Grayscale);
        using var ink = gray.Threshold(82, 255, ThresholdTypes.BinaryInv);
        Cv2.FindContours(ink, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var box in contours.Select(Cv2.BoundingRect))
        {
            if (box.Width < 53 * s || box.Width > 73 * s || box.Height < box.Width * .27 || box.Height > box.Width * .40) continue;
            int center = box.X + box.Width / 2, top = box.Top + area.Top;
            double scale = box.Width / 60d;
            bool Dark(int x, int y)
            {
                var p = image.GetPixel(x, y);
                return (p.R * 299 + p.G * 587 + p.B * 114) / 1000 < 82;
            }
            // 尖角只在标签左边，右侧和标签到数字之间有亮底。
            int tip = top + box.Height - Math.Max(2, (int)(2 * scale));
            if (!Enumerable.Range(box.Left + box.Width / 8, box.Width / 5).Any(x => Dark(x, tip))
                || Dark(center, tip) || Dark(center, top + box.Height)) continue;
            List<double> Strokes(double offset)
            {
                int y = top + (int)Math.Round(offset * box.Width);
                var strokes = new List<double>();
                int start = -1;
                for (int x = box.Left; x <= box.Right; x++)
                {
                    if (Dark(x, y)) { if (start < 0) start = x; continue; }
                    if (start < 0) continue;
                    if (x - start >= 4 * scale)
                    {
                        if (x - start > 14 * scale) return [];
                        strokes.Add((x + start - 1) / 2d);
                    }
                    start = -1;
                }
                return strokes;
            }
            var upper = Strokes(.45);
            var lower = Strokes(.62);
            if (upper.Count is < 1 or > 3 || upper.Count != lower.Count
                || Math.Abs((upper[0] + upper[^1]) / 2 - center) > 4 * scale) continue;
            var slopes = upper.Zip(lower, (a, b) => (b - a) / scale).Select(d =>
                Math.Abs(d) <= 1.5 ? 'I' : d is >= 2 and <= 8 ? '/' : d is <= -2 and >= -8 ? '\\' : '?').ToArray();
            int level = new string(slopes) switch { "I" => 1, "II" => 2, "III" => 3, "I/\\" => 4,
                "/\\" => 5, "/\\I" => 6, _ => 0 };
            if (level == 0 || center < 34 * scale) continue;
            return new(center, scale, level,
                new(center - (int)(34 * scale), top - (int)(68 * scale), (int)(68 * scale), (int)(52 * scale)),
                new(center - (int)(63 * scale), top + (int)(111 * scale), (int)(126 * scale), (int)(45 * scale)));
        }
        return null;
    }
}
