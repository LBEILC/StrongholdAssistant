using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace ASPAssistant.Core.Services;

public sealed record CovenantBarReading(string Id, int? Level, int? Layers);
public sealed record CovenantBarResult(bool Skipped, CovenantBarReading[] Items);
// 仅供离线探针和测试使用，不随宿主消息发送。
public sealed record CovenantBarDiagnostic(int Center, string NameText, string? TightNameText, string? Rejection, double[] DigitDifferences, double[] DigitMargins);

/// <summary>外圈定位、逐项名字 OCR、罗马数字竖线计数和层数字形匹配。</summary>
public sealed class CovenantBarReader(IOcrEngine ocr, string dataDirectory, IReadOnlyDictionary<string, string> names)
{
    private sealed record Digit(int DigitValue, string Pixels);
    private readonly Digit[] _digits = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "covenant_bar_digits.json")))
        .RootElement.EnumerateArray().Select(d => new Digit(d.GetProperty("digit").GetInt32(), d.GetProperty("pixels").GetString()!)).ToArray();

    public async Task<CovenantBarResult> ReadAsync(byte[] frame, double uiScale = 1)
    {
        using var stream = new MemoryStream(frame);
        using var image = new Bitmap(stream);
        double s = BarScale(image, uiScale);
        var area = new RectangleF((float)(image.Width / 2d - 420 * s), (float)(70 * s), (float)(860 * s), (float)(125 * s));
        if (area.Left < 0 || area.Right > image.Width || area.Bottom > image.Height) return new(false, []);
        using var bar = new Bitmap(860, 125);
        using (var g = Graphics.FromImage(bar))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, new Rectangle(0, 0, 860, 125), area, GraphicsUnit.Pixel);
        }
        return await ReadBarAsync(bar);
    }

    // 收店或商品很少时，卡距测不准。用盟约外圈左上弧的蓝边和外侧暗底量顶栏自己的缩放。
    private static double BarScale(Bitmap image, double shopScale)
    {
        double unit = image.Height / 1080d, best = 0, result = unit * shopScale;
        bool Blue(int x, int y)
        {
            var p = image.GetPixel(x, y);
            return p.B > 100 && p.B > p.R * 1.4 && p.B > p.G * 1.05;
        }
        var angles = Enumerable.Range(0, 10).Select(i => (174 + i * 8) * Math.PI / 180)
            .Select(a => (X: Math.Cos(a), Y: Math.Sin(a))).ToArray();
        foreach (double scale in new[] { 1, 100 / 90d, shopScale }.Concat(Enumerable.Range(1, 4).Select(i => (90 + 2 * i) / 90d)).Distinct())
        {
            double s = unit * scale;
            int y = (int)Math.Round(119 * s);
            int half = Math.Min(image.Width / 2 - (int)Math.Ceiling(42 * s), (int)(380 * s));
            for (int x = image.Width / 2 - half; x <= image.Width / 2 + half; x += Math.Max(1, (int)s))
            {
                if (!Blue(x - (int)Math.Round(32 * s), y)) continue;
                int hits = 0;
                foreach (var a in angles)
                {
                    bool At(double radius) => Blue(x + (int)Math.Round(radius * s * a.X), y + (int)Math.Round(radius * s * a.Y));
                    if (At(39) || !At(32)) continue;
                    hits += 1 + (At(31) ? 1 : 0) + (At(34) ? 1 : 0);
                }
                if (hits == 30) return s;
                if (hits >= 24 && hits > best) { best = hits; result = s; }
            }
        }
        return result;
    }

    // 顶栏标准裁片入口，离线测试与整帧读取共用同一算法。
    public async Task<CovenantBarResult> ReadBarAsync(Bitmap bar, Action<CovenantBarDiagnostic>? diagnose = null)
    {
        var columns = new List<int>();
        for (int x = 0; x < 860; x++)
        {
            int blue = 0;
            for (int y = 22; y < 61; y++)
            {
                var p = bar.GetPixel(x, y);
                if (p.B > 100 && p.B > p.R * 1.4 && p.B > p.G * 1.05) blue++;
            }
            if (blue >= 5) columns.Add(x);
        }
        var rings = Runs(columns, 15).Where(r => r[^1] - r[0] >= 12).ToArray();
        if (rings.Length == 0) return new(false, []);
        // 联防提示位于顶栏中央，优先检查，避免把队友读数写入本局。
        string notice = await ReadText(bar, new(335, 33, 220, 34));
        if (notice.Contains("叠加") || notice.Contains("禁用")) return new(true, []);
        var items = new List<CovenantBarReading>();
        for (int c = rings[0][0] + 34; c < Math.Min(rings[^1][0] + 35, 825); c += 108)
        {
            // 右端裁切边界在标准图的约 x=820，名字与外圈必须完整。
            if (c - 51 < 0 || c + 52 > 820) continue;
            string text = await ReadText(bar, new(c - 51, 90, 103, 27));
            var matches = names.Where(n => text.Contains(n.Value, StringComparison.Ordinal)).ToArray();
            string? tightText = null;
            if (matches.Length != 1)
            {
                // 去掉名字下方背景和两侧边缘，仍只接受包含完整目录名的结果。
                tightText = await ReadText(bar, new(c - 47, 90, 95, 18));
                matches = names.Where(n => tightText.Contains(n.Value, StringComparison.Ordinal)).ToArray();
            }
            string? rejection = matches.Length != 1 ? "名字" : null;
            var tier = White(bar, new(c - 12, 10, 25, 18));
            var lines = Runs(Enumerable.Range(0, 25).Where(x => Enumerable.Range(0, 12).Count(y => tier[x, y]) >= 9), 1);
            if (lines.Any(l => l.Count > 6) || lines.Count > 3) rejection ??= "档位";
            int? level = lines.Count == 0 ? null : lines.Count;
            var mask = White(bar, new(c - 28, 64, 57, 20));
            var boxes = Components(mask);
            int? layers = null;
            bool rejected = false;
            var differences = new List<double>();
            var margins = new List<double>();
            foreach (var box in boxes)
            {
                string pixels = Vector(mask, box);
                var scores = _digits.GroupBy(d => d.DigitValue)
                    .Select(g => (Digit: g.Key, Score: g.Min(d => pixels.Zip(d.Pixels, (a, b) => a == b ? 0 : 1).Sum() / 896d)))
                    .OrderBy(d => d.Score).ToArray();
                differences.Add(scores[0].Score);
                margins.Add(scores[1].Score - scores[0].Score);
                if (scores[0].Score > .20 || scores[1].Score - scores[0].Score < .025) rejected = true;
                layers = (layers ?? 0) * 10 + scores[0].Digit;
            }
            if (rejected) rejection ??= "数字";
            diagnose?.Invoke(new(c, text, tightText, rejection, differences.ToArray(), margins.ToArray()));
            if (rejection is null) items.Add(new(matches[0].Key, level, layers));
        }
        return new(false, items.ToArray());
    }

    private async Task<string> ReadText(Bitmap bar, Rectangle area)
    {
        using var crop = bar.Clone(area, PixelFormat.Format24bppRgb);
        using var big = new Bitmap(crop.Width * 3, crop.Height * 3);
        using (var graphics = Graphics.FromImage(big))
            graphics.DrawImage(crop, new Rectangle(0, 0, big.Width, big.Height),
                new Rectangle(0, 0, crop.Width, crop.Height), GraphicsUnit.Pixel);
        using var stream = new MemoryStream();
        big.Save(stream, ImageFormat.Png);
        var result = await ocr.RecognizeLineAsync(stream.ToArray(), 0, 0, big.Width, big.Height);
        return string.Concat(result.Select(r => r.Text.Replace(" ", "")));
    }

    private static List<List<int>> Runs(IEnumerable<int> positions, int gap)
    {
        var runs = new List<List<int>>();
        foreach (int x in positions)
        {
            if (runs.Count == 0 || x - runs[^1][^1] > gap) runs.Add([]);
            runs[^1].Add(x);
        }
        return runs;
    }

    private static bool[,] White(Bitmap image, Rectangle area)
    {
        var mask = new bool[area.Width, area.Height];
        for (int y = 0; y < area.Height; y++) for (int x = 0; x < area.Width; x++)
        {
            var p = image.GetPixel(area.X + x, area.Y + y);
            int min = Math.Min(p.R, Math.Min(p.G, p.B)), max = Math.Max(p.R, Math.Max(p.G, p.B));
            mask[x, y] = min >= 145 && max - min < 80;
        }
        return mask;
    }

    private static List<Rectangle> Components(bool[,] mask)
    {
        int w = mask.GetLength(0), h = mask.GetLength(1);
        var seen = new bool[w, h];
        var boxes = new List<Rectangle>();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            if (!mask[x, y] || seen[x, y]) continue;
            var queue = new Stack<Point>();
            queue.Push(new(x, y)); seen[x, y] = true;
            int left = x, right = x, top = y, bottom = y, count = 0;
            while (queue.TryPop(out var p))
            {
                count++; left = Math.Min(left, p.X); right = Math.Max(right, p.X);
                top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int a = p.X + dx, b = p.Y + dy;
                    if (a < 0 || b < 0 || a >= w || b >= h || seen[a, b] || !mask[a, b]) continue;
                    seen[a, b] = true; queue.Push(new(a, b));
                }
            }
            if (bottom - top + 1 >= 8 && count >= 15 && right - left + 1 is >= 4 and <= 17)
                boxes.Add(Rectangle.FromLTRB(left, top, right + 1, bottom + 1));
        }
        return boxes.OrderBy(b => b.X).ToList();
    }

    private static string Vector(bool[,] mask, Rectangle box)
    {
        int width = (int)Math.Round(box.Width * 24d / box.Height), offset = (32 - width) / 2;
        var pixels = Enumerable.Repeat('0', 896).ToArray();
        for (int y = 0; y < 24; y++) for (int x = 0; x < width; x++)
            if (mask[box.X + Math.Min(box.Width - 1, (int)((x + .5) * box.Width / width)),
                box.Y + Math.Min(box.Height - 1, (int)((y + .5) * box.Height / 24))]) pixels[(y + 2) * 32 + x + offset] = '1';
        return new(pixels);
    }
}
