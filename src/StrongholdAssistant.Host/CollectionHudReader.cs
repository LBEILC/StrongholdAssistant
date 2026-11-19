using ASPAssistant.Core.Services;
using OpenCvSharp;
using ASPAssistant.Core.Models;
using System.Text.RegularExpressions;

namespace StrongholdAssistant.Host;

// 只在采集开启时使用；所有坐标以 1280×720 的录屏为基准。
internal sealed class CollectionHudReader(IOcrEngine ocr)
{
    private static Rect Region(Mat image, int x, int y, int w, int h)
    {
        double s = Math.Min(image.Width / 1280d, image.Height / 720d);
        return new((int)((image.Width - 1280 * s) / 2 + x * s), (int)(y * s),
            Math.Max(1, (int)(w * s)), Math.Max(1, (int)(h * s)));
    }

    private async Task<string> Line(Mat image, Rect bounds)
    {
        using var crop = new Mat(image, bounds);
        using var gray = new Mat();
        Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.Normalize(gray, gray, 0, 255, NormTypes.MinMax);
        Cv2.Resize(gray, gray, new Size(0, 0), 2, 2);
        var lines = await ocr.RecognizeLineAsync(gray.ImEncode(".png"), 0, 0, gray.Width, gray.Height);
        return string.Concat(lines.Select(l => l.Text)).Replace(" ", "").Trim();
    }

    private static int? Number(string text, int max) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, null, out int n) && n <= max ? n : null;

    public async Task<int?> ReadRoundBadge(byte[] frame)
    {
        using var image = Cv2.ImDecode(frame, ImreadModes.Color);
        var area = Region(image, 465, 12, 91, 34);
        using var strip = new Mat(image, area);
        using var mask = new Mat();
        Cv2.InRange(strip, new Scalar(145, 145, 0), new Scalar(255, 255, 116), mask);
        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        double s = area.Width / 91d;
        var badge = contours.Select(Cv2.BoundingRect).FirstOrDefault(b =>
            b.Width >= 20 * s && b.Width <= 39 * s && b.Height >= 13 * s && b.Height <= 25 * s);
        return badge.Width == 0 ? null : Number(await Line(strip, badge), 20);
    }

    private async Task<int?> Health(Mat image, bool dimmed = false)
    {
        // 血量塔图标在回合标牌右侧；终局弹层压暗画面，两种亮度都保留颜色比例。
        var region = Region(image, 748, 16, 58, 28);
        using var strip = new Mat(image, region);
        using var hsv = new Mat();
        Cv2.CvtColor(strip, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(80, 140, dimmed ? 22 : 125), new Scalar(103, 255, 255), mask);
        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        double s = region.Height / 28d;
        var towers = contours.Select(Cv2.BoundingRect).Where(b => b.Height >= 10 * s && b.Height <= 23 * s &&
            b.Width >= 5 * s && b.Width <= 17 * s).OrderByDescending(b => b.Height).ToArray();
        if (towers.Length == 0) return null;
        var tower = towers[0];
        var digits = new Rect(region.X + tower.Right, region.Y + tower.Y - (int)(2 * s),
            (int)(38 * s), (int)(24 * s));
        return Number(await Line(image, digits), 999);
    }

    public async Task<RoundVitals> ReadVitals(byte[] frame, EconomyReading reading, GameShopTheme theme)
    {
        using var image = Cv2.ImDecode(frame, ImreadModes.Color);
        var level = reading.Level;
        if (level is null && theme == GameShopTheme.Terminal && reading.UpgradeButton is { } button)
            level = TerminalBars(image, button);
        int? money = reading.Money;
        if (money is null && theme == GameShopTheme.Terminal)
        {
            // 收店时卡距测不到，资金牌仍在右下。以金色外框实测位置裁数字。
            double scale = Math.Min(image.Width / 1280d, image.Height / 720d);
            var area = new Rect(image.Width - (int)(154 * scale), image.Height - (int)(219 * scale),
                (int)(135 * scale), (int)(73 * scale));
            using var strip = new Mat(image, area);
            using var hsv = new Mat();
            Cv2.CvtColor(strip, hsv, ColorConversionCodes.BGR2HSV);
            using var gold = new Mat();
            Cv2.InRange(hsv, new Scalar(12, 105, 85), new Scalar(38, 255, 255), gold);
            Cv2.FindContours(gold, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
            var box = contours.Select(Cv2.BoundingRect).FirstOrDefault(b => b.Width > 66 * scale &&
                b.Width < 105 * scale && b.Width > 2.5 * b.Height && b.Width < 3.6 * b.Height);
            if (box.Width > 0)
            {
                var digits = new Rect(box.X + (int)(box.Width * .47), box.Y + (int)(box.Height * .18),
                    (int)(box.Width * .36), (int)(box.Height * .66));
                money = Number(await Line(strip, digits), 999);
            }
        }
        return new(await Health(image), money, level);
    }

    // 终端主题的 I～III 是等宽垂直黑条。只接受上下两行同宽同位的笔画，避开 V 的斜线。
    private static int? TerminalBars(Mat image, System.Drawing.RectangleF button)
    {
        double width = button.Width * image.Width;
        int center = (int)((button.X + button.Width / 2) * image.Width);
        int left = center - (int)(width * .20);
        int right = center + (int)(width * .24);
        if (left < 0 || right >= image.Width) return null;
        List<(int Left, int Width)> Runs(double height)
        {
            int y = (int)((button.Y + button.Height * height) * image.Height);
            var runs = new List<(int, int)>();
            int start = left;
            for (int x = left; x <= right; x++)
            {
                var p = image.At<Vec3b>(y, x);
                if (Math.Max(p.Item0, Math.Max(p.Item1, p.Item2)) < 85) continue;
                if (x > start) runs.Add((start, x - start));
                start = x + 1;
            }
            return runs;
        }
        var upper = Runs(.319);
        var lower = Runs(.351);
        if (upper.Count is < 1 or > 3 || upper.Count != lower.Count) return null;
        for (int i = 0; i < upper.Count; i++)
        {
            var bar = upper[i];
            if (bar.Width < width * .035 || bar.Width > width * .09 ||
                Math.Abs(bar.Left - lower[i].Left) > width * .012 ||
                Math.Abs(bar.Width - lower[i].Width) > width * .012) return null;
        }
        return Math.Abs((upper[0].Left + upper[^1].Left + upper[^1].Width) / 2d - center) <= width * .065
            ? upper.Count : null;
    }

    private static bool ColoredText(Mat image, Rect area, bool orange)
    {
        using var crop = new Mat(image, area);
        using var hsv = new Mat();
        Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
        using var mask = new Mat();
        Cv2.InRange(hsv, new Scalar(orange ? 0 : 70, 95, 95), new Scalar(orange ? 28 : 100, 255, 255), mask);
        return Cv2.CountNonZero(mask) > area.Width * area.Height * .09;
    }

    public async Task<CollectedResult?> ReadResult(byte[] frame)
    {
        using var image = Cv2.ImDecode(frame, ImreadModes.Color);
        var title = Region(image, 143, 207, 215, 61);
        var rounds = Region(image, 36, 280, 97, 27);
        if (ColoredText(image, title, false) || ColoredText(image, title, true))
        {
            var label = await Line(image, title);
            if (label is ("模拟完成" or "模拟失败" or "模拟结束") && (await Line(image, rounds)).Contains("回合"))
            {
                // “下一步”出现时，通过回合的计数动画已经结束。
                int? reached = ColoredText(image, Region(image, 1020, 638, 224, 55), false)
                    ? Number(await Line(image, Region(image, 36, 307, 99, 70)), 20) : null;
                var trophy = await Line(image, Region(image, 228, 484, 76, 23));
                var stats = Region(image, 30, 270, 710, 290);
                var lines = await ocr.RecognizeRegionAsync(frame, stats.X, stats.Y, stats.Width, stats.Height);
                int? Stat(string name)
                {
                    foreach (var line in lines)
                    {
                        var match = Regex.Match(line.Text.Replace(" ", ""), $"^(?:本局)?{name}[：:]?([0-9]+)$");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int value)) return value;
                    }
                    return null;
                }
                return new(reached, label == "模拟失败" ? false : null, null,
                    trophy.Contains("奖杯") ? Number(await Line(image, Region(image, 211, 506, 32, 35)), 999) : null,
                    Stat("评分"), Stat("(?:名次|排名)"));
            }
        }
        var victory = Region(image, 523, 419, 235, 41);
        if (ColoredText(image, victory, true))
        {
            var label = await Line(image, victory);
            if (label.Contains("全部领袖") || label.Contains("卫戍完成") || label.Contains("卫戍失败"))
                return new(null, !label.Contains("失败"), await Health(image, dimmed: true), null);
        }
        return null;
    }
}
