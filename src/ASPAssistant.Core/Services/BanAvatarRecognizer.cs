using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record BanAvatarMatch(Rectangle Box, string? Name, string? Covenant, string Candidate, double Score, double Margin);
public sealed record BanAvatarResult(IReadOnlyList<BanAvatarMatch> Slots, int Width, int Height,
    double LocateMs, double OcrMs, double CompareMs)
{
    public string[] Names => Slots.Select(s => s.Name).OfType<string>().Distinct().ToArray();
    // 完整头像已经进入原盟约面板所在的上半区，说明列表发生了滚动。
    public bool IsScrolledList => Slots.Any(s => s.Covenant != null && s.Box.Y < Height * .43);
}

/// <summary>逐个定位禁用角标，只在所在盟约的成员中比较小图相关性。</summary>
public sealed class BanAvatarRecognizer
{
    public const double MinimumScore = .58;
    public const double UniqueTierMinimumScore = .50;
    public const double MinimumMargin = .08;
    private readonly IOcrEngine _ocr;
    private readonly IReadOnlyList<Operator> _operators;
    private readonly Dictionary<string, double[][]> _templates = [];

    public BanAvatarRecognizer(IOcrEngine ocr, IReadOnlyList<Operator> operators, string avatarDirectory)
    {
        _ocr = ocr;
        _operators = operators;
        foreach (var op in operators)
        {
            using var bitmap = new Bitmap(Path.Combine(avatarDirectory, op.Name + ".png"));
            var variants = new List<double[]>();
            foreach (bool flip in new[] { false, true })
            foreach (int angle in new[] { -20, 0, 20 })
            {
                using var transformed = new Bitmap(128, 128);
                using (var g = Graphics.FromImage(transformed))
                {
                    g.Clear(Color.White);
                    g.TranslateTransform(64, 64);
                    g.RotateTransform(angle);
                    g.ScaleTransform(flip ? -1 : 1, 1);
                    g.DrawImage(bitmap, -64, -64, 128, 128);
                }
                variants.Add(Vector(transformed, new Rectangle(0, 0, 128, 128)));
            }
            _templates.Add(op.Name, variants.ToArray());
        }
    }

    public Task<BanAvatarResult> RecognizeAsync(byte[] screenshot) => Task.Run(async () =>
    {
        var timer = Stopwatch.StartNew();
        using var stream = new MemoryStream(screenshot);
        using var source = new Bitmap(stream);
        double scale = source.Height / 1080.0;
        var boxes = Locate(source, scale);
        double locateMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var groups = boxes.Count == 0 ? [] : await _ocr.RecognizeRegionAsync(screenshot,
            (int)(150 * scale), boxes.Min(b => b.Y), (int)(135 * scale),
            (int)(985 * scale) - boxes.Min(b => b.Y));
        double ocrMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var matches = new List<BanAvatarMatch>();
        var covenantNames = _operators.SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants)).Distinct().ToArray();
        var labels = groups.Select(g => (Text: g, Name: covenantNames.FirstOrDefault(n => g.Text.Contains(n))))
            .Where(g => g.Name != null).ToArray();
        foreach (var box in boxes)
        {
            var group = labels.Where(g => g.Text.Box.Y - 130 * scale <= box.Y)
                .OrderByDescending(g => g.Text.Box.Y).FirstOrDefault();
            var vector = Vector(source, box);
            int tier = ReadTier(source, box, scale);
            var scores = _operators.Where(o => o.Tier == tier && (o.CoreCovenants.Contains(group.Name!) || o.AdditionalCovenants.Contains(group.Name!)))
                .Select(o => (o.Name, Score: _templates[o.Name].Max(t => vector.Zip(t, (a, b) => a * b).Sum())))
                .OrderByDescending(s => s.Score).ToArray();
            double score = scores.Length == 0 ? 0 : scores[0].Score;
            double margin = scores.Length < 2 ? score : score - scores[1].Score;
            double threshold = scores.Length == 1 ? UniqueTierMinimumScore : MinimumScore;
            matches.Add(new(box, score >= threshold && margin >= MinimumMargin ? scores[0].Name : null,
                group.Name, scores.Length == 0 ? "" : scores[0].Name, score, margin));
        }
        // 同一盟约列表里每名干员只出现一次；冲突时只有明显更好的格子可以保留名字。
        foreach (var duplicate in matches.Where(m => m.Name != null).GroupBy(m => (m.Covenant, m.Name)).Where(g => g.Count() > 1))
        {
            var ordered = duplicate.OrderByDescending(m => m.Score).ToArray();
            bool clearWinner = ordered[0].Score - ordered[1].Score >= MinimumMargin;
            foreach (var item in ordered.Skip(clearWinner ? 1 : 0))
                matches[matches.IndexOf(item)] = item with { Name = null };
        }
        return new BanAvatarResult(matches, source.Width, source.Height, locateMs, ocrMs, timer.Elapsed.TotalMilliseconds);
    });

    private static List<Rectangle> Locate(Bitmap bitmap, double scale)
    {
        // 角标红色连通块的外框约 40×40，头像约 117×117；坐标按画面高度缩放。
        int left = (int)(320 * scale), right = Math.Min(bitmap.Width, (int)(1295 * scale));
        int top = (int)(155 * scale), bottom = Math.Min(bitmap.Height, (int)(985 * scale));
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[data.Stride * bitmap.Height];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        bitmap.UnlockBits(data);
        bool IsRed(int x, int y)
        {
            int i = y * data.Stride + x * 4;
            return bytes[i + 2] > 70 && bytes[i + 2] > bytes[i + 1] * 1.45 && bytes[i + 2] > bytes[i] * 1.25;
        }
        var red = new bool[bitmap.Width * bitmap.Height];
        for (int y = top; y < bottom; y++)
        for (int x = left; x < right; x++)
        {
            red[y * bitmap.Width + x] = IsRed(x, y);
        }
        var boxes = new List<Rectangle>();
        var queue = new Queue<int>();
        for (int y = top; y < bottom; y++)
        for (int x = left; x < right; x++)
        {
            int index = y * bitmap.Width + x;
            if (!red[index]) continue;
            red[index] = false;
            queue.Enqueue(index);
            int minX = x, maxX = x, minY = y, maxY = y, count = 0;
            while (queue.TryDequeue(out int p))
            {
                int px = p % bitmap.Width, py = p / bitmap.Width;
                minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                minY = Math.Min(minY, py); maxY = Math.Max(maxY, py); count++;
                foreach (int next in new[] { p - 1, p + 1, p - bitmap.Width, p + bitmap.Width })
                    if (next >= 0 && next < red.Length && red[next]) { red[next] = false; queue.Enqueue(next); }
            }
            if (count < 180 * scale * scale || maxX - minX < 25 * scale || maxX - minX > 48 * scale
                || maxY - minY < 25 * scale || maxY - minY > 48 * scale) continue;
            int edgeWidth = Math.Max(1, (int)(6 * scale));
            int topEdge = Enumerable.Range(minY, edgeWidth).Max(yy => Enumerable.Range(minX, maxX - minX + 1).Count(xx => IsRed(xx, yy)));
            int rightEdge = Enumerable.Range(maxX - edgeWidth + 1, edgeWidth).Max(xx => Enumerable.Range(minY, maxY - minY + 1).Count(yy => IsRed(xx, yy)));
            if (topEdge < (maxX - minX) * .65 || rightEdge < (maxY - minY) * .65) continue;
            int size = (int)Math.Round(117 * scale);
            var box = new Rectangle(maxX + 1 - size, minY, size, size);
            if (box.Left >= left && box.Top > top && box.Bottom <= bottom) boxes.Add(box);
        }
        return boxes.OrderBy(b => b.Y).ThenBy(b => b.X).ToList();
    }

    private static int ReadTier(Bitmap bitmap, Rectangle box, double scale)
    {
        // 独立读取等阶角标以约束候选；头像相似度仍遮掉角标，避免拿角标本身当头像。
        var counts = new int[7];
        for (int y = box.Bottom - (int)(34 * scale); y < box.Bottom; y++)
        for (int x = box.Left; x < box.Left + (int)(32 * scale); x++)
        {
            var c = bitmap.GetPixel(x, y);
            if (c.GetSaturation() < .45 || c.GetBrightness() < .3) continue;
            float hue = c.GetHue();
            int tier = hue is >= 15 and < 40 ? 6 : hue is >= 40 and < 65 ? 5 : hue is >= 175 and < 200 ? 4 : hue is >= 125 and < 175 ? 3 : 0;
            counts[tier]++;
        }
        int best = Enumerable.Range(3, 4).MaxBy(t => counts[t]);
        if (counts[best] > 40 * scale * scale) return best;
        int white = 0;
        for (int y = box.Bottom - (int)(24 * scale); y < box.Bottom - (int)(5 * scale); y++)
        for (int x = box.Left + (int)(5 * scale); x < box.Left + (int)(25 * scale); x++)
        {
            var c = bitmap.GetPixel(x, y);
            if (Math.Min(c.R, Math.Min(c.G, c.B)) > 200) white++;
        }
        return white > 35 * scale * scale ? 2 : 1;
    }

    private static double[] Vector(Bitmap source, Rectangle rect)
    {
        using var small = new Bitmap(24, 24);
        using (var g = Graphics.FromImage(small))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(source, new Rectangle(0, 0, 24, 24), rect, GraphicsUnit.Pixel);
        }
        var values = new List<double>();
        for (int y = 1; y < 23; y++)
        for (int x = 1; x < 23; x++)
        {
            if ((x >= 16 && y < 9) || (x < 9 && y >= 16)) continue;
            var c = small.GetPixel(x, y);
            values.Add(c.R); values.Add(c.G); values.Add(c.B);
        }
        double mean = values.Average();
        var centered = values.Select(v => v - mean).ToArray();
        double norm = Math.Sqrt(centered.Sum(v => v * v));
        return centered.Select(v => norm == 0 ? 0 : v / norm).ToArray();
    }
}
