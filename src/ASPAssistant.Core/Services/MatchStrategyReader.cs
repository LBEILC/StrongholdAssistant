using System.Drawing;
using System.Text.Json;

namespace ASPAssistant.Core.Services;

/// <summary>圆内头像相关性匹配，以及选定策略页的名称读取。</summary>
public sealed class MatchStrategyReader(IOcrEngine ocr, string dataDirectory, IReadOnlyDictionary<string, string> names)
{
    private readonly Dictionary<string, double[]> _portraits = JsonSerializer.Deserialize<Dictionary<string, double[]>>(
        File.ReadAllText(Path.Combine(dataDirectory, "strategy_portraits.json")))!
        .ToDictionary(p => p.Key, p => Normalize(p.Value));

    private static double[] Normalize(double[] values)
    {
        double mean = values.Average();
        var result = values.Select(v => v - mean).ToArray();
        double length = Math.Sqrt(result.Sum(v => v * v));
        return length == 0 ? result : result.Select(v => v / length).ToArray();
    }

    public (string? Id, double Score, double Margin) ReadPortrait(byte[] frame, double uiScale = 1)
    {
        using var stream = new MemoryStream(frame);
        using var image = new Bitmap(stream);
        double baseScale = Math.Min(image.Width / 1920d, image.Height / 1080d);
        // 收店或部分卡位已空时，卡距估出的缩放会漂移；头像始终锚在右上。
        return new[] { uiScale, 1d, 100d / 90 }.Distinct().Select(k =>
        {
            double scale = baseScale * k;
            return Portrait(image, new RectangleF((float)(image.Width - 124 * scale), (float)(259 * scale),
                (float)(112 * scale), (float)(112 * scale)));
        }).OrderByDescending(m => m.Id != null).ThenByDescending(m => m.Score).First();
    }

    private (string? Id, double Score, double Margin) Portrait(Bitmap image, RectangleF bounds, bool grid = false, string? expected = null)
    {
        using var small = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, new Rectangle(0, 0, 32, 32), bounds, GraphicsUnit.Pixel);
        }
        var pixels = new List<double>();
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            if ((x - 15.5) * (x - 15.5) + (y - 15.5) * (y - 15.5) >= 144) continue;
            var c = small.GetPixel(x, y);
            pixels.Add(.299 * c.R + .587 * c.G + .114 * c.B);
        }
        var sample = Normalize(pixels.ToArray());
        var scores = _portraits.Select(p => (Id: p.Key, Score: p.Value.Zip(sample, (a, b) => a * b).Sum()))
            .OrderByDescending(p => p.Score).Take(2).ToArray();
        double margin = scores[0].Score - scores[1].Score;
        bool matched = scores[0].Score >= (grid ? .68 : .65) && margin >= (grid ? .1 : .15);
        if (grid && scores[0].Id == expected && scores[0].Score >= .58 && margin >= .15) matched = true;
        return (matched ? scores[0].Id : null, scores[0].Score, margin);
    }

    public sealed record Option(string? Id, string? Name);

    public async Task<Option[]?> ReadOptionsAsync(byte[] frame, int width, int height)
    {
        double s = Math.Min(width / 1920d, height / 1080d);
        var title = await ocr.RecognizeRegionAsync(frame, 0, 0, width, (int)(125 * s));
        string heading = string.Concat(title.Select(t => t.Text.Replace(" ", "")));
        if (!heading.Contains("选择策略") && !heading.Contains("请选择策略")) return null;
        var detail = await ocr.RecognizeRegionAsync(frame, (int)(width * .75), (int)(height * .13),
            width - (int)(width * .75), (int)(height * .86));
        bool expanded = detail.Any(t => t.Text.Replace(" ", "").Contains("初始生命值"));
        return (await VisibleOptions(frame, width, height, expanded)).Select(slot => slot.Option).ToArray();
    }

    internal async Task<(Option Option, double X, double Y)[]> VisibleOptions(byte[] frame, int width, int height, bool expanded)
    {
        using var stream = new MemoryStream(frame);
        using var image = new Bitmap(stream);
        double scale = Math.Min(width / 1920d, height / 1080d);
        var order = names.Keys.ToArray();
        List<(Option Option, double X, double Y)> best = [];
        int most = -1;
        // UI90 首屏五列，UI100 首屏四列；用实际头像匹配数选择布局。
        foreach (var (zoom, columns) in new[] { (1d, 5), (100d / 90, 4) })
        {
            double s = scale * zoom;
            List<(Option Option, double X, double Y)> slots = [];
            for (int row = 0; row < 5; row++)
            for (int col = 0; col < columns; col++)
            {
                double x = (width - 1920 * scale) / 2 + ((expanded ? 610 : 966) + 189 * col) * s;
                double y = (height - 1080 * scale) / 2 + (332 + 189 * row) * s;
                if (y - 58 * s >= height || x >= width) continue;
                var match = new[] { 58, 68, 76, 84 }.Where(r => y + r * s < height)
                    .Select(r => Portrait(image, new((float)(x - r * s), (float)(y - r * s),
                        (float)(2 * r * s), (float)(2 * r * s)), grid: true, expected: order.ElementAtOrDefault(row * columns + col)))
                    .Where(m => m.Id != null).OrderByDescending(m => m.Score).FirstOrDefault();
                slots.Add((new(match.Id, match.Id == null ? null : names[match.Id]), x / width, Math.Min(y, height - 12 * s) / height));
            }
            int found = slots.Count(slot => slot.Option.Id != null);
            if (found > most) { most = found; best = slots; }
        }
        if (best.Where((slot, i) => i < order.Length && slot.Option.Id == order[i]).Count() >= 2)
        {
            var group = await ocr.RecognizeRegionAsync(frame, (int)(width * .25), (int)(height * .12),
                (int)(width * .5), (int)(height * .12));
            if (group.Any(t => t.Text.Replace(" ", "").Contains("泛用型策略")))
                for (int i = 0; i < Math.Min(order.Length, best.Count); i++)
                    if (best[i].Option.Id == null) best[i] = (new(order[i], names[order[i]]), best[i].X, best[i].Y);
        }
        return best.ToArray();
    }

    public async Task<string?> ReadSelectionAsync(byte[] frame, int width, int height)
    {
        double s = Math.Min(width / 1920d, height / 1080d);
        var lines = await ocr.RecognizeRegionAsync(frame, (int)(1100 * s), (int)(420 * s), (int)(720 * s), (int)(400 * s));
        string text = string.Concat(lines.Select(l => l.Text.Replace(" ", "")));
        if (!text.Contains("已选定策略")) return null;
        var detail = await ocr.RecognizeRegionAsync(frame, (int)(width * .75), (int)(height * .36),
            width - (int)(width * .75), (int)(height * .45));
        text += string.Concat(detail.Select(l => l.Text.Replace(" ", "")));
        var matches = names.Where(p => text.Contains(p.Value, StringComparison.Ordinal)).Select(p => p.Key).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
