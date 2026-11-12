using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record BanAvatarMatch(Rectangle Box, string? Name, string? Covenant, string Candidate, double Score, double Margin)
{
    public int Tier { get; init; }
    public int? SlotIndex { get; init; }
    // 标题滚出画面时，以首个完整可见行作为临时原点，交给宿主按已知名字对齐。
    public int VisibleIndex { get; init; }
    public bool EndsGroup { get; init; }
}
public sealed record BanAvatarResult(IReadOnlyList<BanAvatarMatch> Slots, int Width, int Height,
    double LocateMs, double OcrMs, double CompareMs)
{
    public string[] Names => Slots.Select(s => s.Name).OfType<string>().Distinct().ToArray();
    // 完整头像已经进入原盟约面板所在的上半区，说明列表发生了滚动。
    public bool IsScrolledList => Slots.Any(s => s.Box.Y < Height * .43);
}

/// <summary>逐个定位禁用角标，在同星级、已知盟约的成员中比较小图相关性；标题不可见时由宿主对齐。</summary>
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
        _operators = operators.Where(o => o.Obtain is null).ToArray();
        foreach (var op in _operators)
        {
            var variants = new List<double[]>();
            var extraDirectory = Path.Combine(avatarDirectory, op.Name);
            var paths = new[] { Path.Combine(avatarDirectory, op.Name + ".png") }
                .Concat(Directory.Exists(extraDirectory) ? Directory.GetFiles(extraDirectory, "*.png") : []);
            foreach (var path in paths)
            {
                using var bitmap = new Bitmap(path);
                using var transformed = new Bitmap(128, 128);
                using (var g = Graphics.FromImage(transformed))
                {
                    // 已确认的 17 格均匹配居中缩小 6% 的取景，统一保留头像周围的留白。
                    g.Clear(Color.White);
                    g.TranslateTransform(64, 64);
                    g.ScaleTransform(.94f, .94f);
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
        double k = GameUiScale.Information(screenshot);
        double scale = source.Height / 1080.0 * k;
        double offsetX = source.Width / 2.0 * (1 - k);
        var boxes = Locate(source, scale, offsetX);
        double locateMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var groups = boxes.Count == 0 ? [] : await _ocr.RecognizeRegionAsync(screenshot,
            Math.Max(0, (int)(offsetX + 150 * scale)), boxes.Min(b => b.Y), (int)(135 * scale),
            (int)(985 * source.Height / 1080.0) - boxes.Min(b => b.Y));
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
            var scores = _operators.Where(o => o.Tier == tier && (group.Name == null || o.CoreCovenants.Contains(group.Name) || o.AdditionalCovenants.Contains(group.Name)))
                .Select(o => (o.Name, Score: _templates[o.Name].Max(t => vector.Zip(t, (a, b) => a * b).Sum())))
                .OrderByDescending(s => s.Score).ToArray();
            double score = scores.Length == 0 ? 0 : scores[0].Score;
            double margin = scores.Length < 2 ? score : score - scores[1].Score;
            double threshold = scores.Length == 1 ? UniqueTierMinimumScore : MinimumScore;
            matches.Add(new(box, score >= threshold && margin >= MinimumMargin ? scores[0].Name : null,
                group.Name, scores.Length == 0 ? "" : scores[0].Name, score, margin)
            {
                Tier = tier,
                // 90 档样图：首列 x=332，列距 136.5，行距 172；盟约文字顶边比首行低约 120。
                SlotIndex = group.Name == null ? null :
                    (int)Math.Round((box.Y - group.Text.Box.Y + 120 * scale) / (172 * scale)) * 7
                    + (int)Math.Round((box.X - offsetX - 332 * scale) / (136.5 * scale)),
                VisibleIndex = (int)Math.Round((box.Y - boxes[0].Y) / (172 * scale)) * 7
                    + (int)Math.Round((box.X - offsetX - 332 * scale) / (136.5 * scale))
            });
        }
        foreach (var group in matches.GroupBy(m => m.Covenant))
        {
            var last = group.Last();
            int column = (int)Math.Round((last.Box.X - offsetX - 332 * scale) / (136.5 * scale));
            double nextY = last.Box.Y + (column == 6 ? 172 * scale : 0);
            bool visible = nextY > 155 * scale && nextY + last.Box.Height <= 985 * source.Height / 1080.0;
            if (visible) matches[matches.IndexOf(last)] = last with { EndsGroup = true };
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

    private static List<Rectangle> Locate(Bitmap bitmap, double scale, double offsetX)
    {
        // 角标红色连通块的外框约 40×40，头像约 117×117；坐标按画面高度缩放。
        int left = Math.Max(0, (int)(offsetX + 320 * scale));
        int right = Math.Min(bitmap.Width, (int)(offsetX + 1295 * scale));
        // 列表内容随 UI 放大，但底部可视窗口仍在原处裁切；不能把露出上沿的头像当完整格。
        int top = (int)(155 * scale), bottom = Math.Min(bitmap.Height, (int)(985 * bitmap.Height / 1080.0));
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
        if (boxes.Count == 0) return boxes;
        int firstRow = boxes.Min(b => b.Y);
        // 同一行的红色角标可能相差一个像素，先归到几何行，再按列排序。
        return boxes.OrderBy(b => Math.Round((b.Y - firstRow) / (172 * scale))).ThenBy(b => b.X).ToList();
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
        using var small = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(small))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(source, new Rectangle(0, 0, 32, 32), rect, GraphicsUnit.Pixel);
        }
        var values = new List<double>();
        // 沿用原 24×24 的边缘与角标遮罩比例，边界向上取整到 32×32 像素。
        for (int y = 2; y < 31; y++)
        for (int x = 2; x < 31; x++)
        {
            if ((x >= 22 && y < 12) || (x < 12 && y >= 22)) continue;
            var c = small.GetPixel(x, y);
            values.Add(c.R); values.Add(c.G); values.Add(c.B);
        }
        double mean = values.Average();
        var centered = values.Select(v => v - mean).ToArray();
        double norm = Math.Sqrt(centered.Sum(v => v * v));
        return centered.Select(v => norm == 0 ? 0 : v / norm).ToArray();
    }
}
