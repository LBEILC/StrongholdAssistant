using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ASPAssistant.Core.Models;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public sealed record EconomyReading(int? Round, int? Money, int? Level, int? UpgradeCost, bool IsRest)
{
    public bool IsFinalRound { get; init; }
    public RectangleF? UpgradeButton { get; init; }
}

/// <summary>复用商店截图；顶部读回合，右下读资金，LEVEL 标签定位升级面板。</summary>
public sealed class EconomyReader(IOcrEngine ocr, GameShopTheme theme = GameShopTheme.Terminal)
{
    public string RoundRawText { get; private set; } = "";
    private readonly Dictionary<(Rectangle Region, bool Line), (byte[] Pixels, IReadOnlyList<OcrTextResult> Text)> _regions = [];
    private OcrTextResult? _levelLabel;
    private Rectangle? _upgradeLabelRegion;
    private (int Width, int Height, double Scale) _layout;
    // 「回合」标签的右边界：取本布局下见过的最大值。换回合的动画里标签框偶尔量窄几个像素，
    // 数字框跟着左移会把「合」字最右一竖读成 1，第 2 回合读成 12（v1.0.4 玩家诊断包）
    private int? _roundDigitsX;

    public Task<EconomyReading> ReadAsync(byte[] png, int width, int height, double? uiScale = null)
        => ReadCoreAsync(png, width, height, uiScale, true, null);

    // 手动认人只消费回合和阶段；等级与升级价格仍由正常经济扫描读取。
    public Task<EconomyReading> ReadFieldAsync(byte[] png, int width, int height, bool mainScreen)
        => ReadCoreAsync(png, width, height, null, false, mainScreen);

    private async Task<EconomyReading> ReadCoreAsync(byte[] png, int width, int height, double? uiScale, bool readUpgrade, bool? mainScreen)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        double scale = Math.Min(width / 1920.0, height / 1080.0) * (uiScale ?? GameUiScale.Shop(bitmap));
        var layout = (width, height, Math.Round(scale, 2));
        if (_layout != layout)
        {
            _regions.Clear();
            _levelLabel = null;
            _upgradeLabelRegion = null;
            _roundDigitsX = null;
            _layout = layout;
        }
        Task<IReadOnlyList<OcrTextResult>> Read(int x, int y, int w, int h, bool line = false)
            => ReadRegionAsync(bitmap, png, new(x, y, w, h), line);
        // 阶段标牌的动画会改变 OCR 包围框；固定读取顶部区域，不复用某一帧的文字裁框。
        var top = await Read((int)(width * .32), 0, (int)(width * .25), (int)(height * .09));
        string heading = string.Join("", top.OrderBy(t => t.Box.X).Select(t => t.Text));
        var roundLabel = top.FirstOrDefault(t => t.Text.Trim() == "回合");
        var combinedRound = top.Select(t => Regex.Match(t.Text, @"^回合\s*([\d?？﹖\s]+)$")).FirstOrDefault(m => m.Success);
        string roundText = combinedRound?.Groups[1].Value ?? "";
        if (roundLabel is not null)
        {
            _roundDigitsX = Math.Max(_roundDigitsX ?? 0, roundLabel.Box.X + roundLabel.Box.W + 5);
            var digits = await Read(_roundDigitsX.Value, roundLabel.Box.Y, (int)(52 * scale), roundLabel.Box.H, true);
            roundText = string.Join("", digits.Select(t => t.Text));
        }
        int? round = Number(roundText);
        RoundRawText = string.Join(" | ", top.Select(t => t.Text)) + $"; digits={roundText}";
        if (round > 20) round = null;
        // 动画标牌会让顶部文字漏检；只用本帧就绪控件补证，不沿用上一帧阶段。
        bool isRest = heading.Contains("休息") || heading.Contains("休整")
            || (mainScreen ?? await new BoardSurfaceReader(ocr).IsMainScreenAsync(png, width, height));
        bool isFinalRound = isRest && Regex.IsMatch(roundText.Trim(), @"^[?？﹖](\s*[?？﹖])?$");
        if (!readUpgrade) return new(round, null, null, null, isRest) { IsFinalRound = isFinalRound };
        var moneyText = await Read((int)(width - 130 * scale),
            (int)(height - 273 * scale), (int)(65 * scale), (int)(38 * scale), true);
        int? money = Number(string.Join("", moneyText.Select(t => t.Text)));
        // 卡位数量变化会移动升级面板；使用本帧的标签，像素未变时仍复用区域 OCR 缓存。
        var panel = await Read(0, (int)(height * .72), (int)(width * .85), (int)(height * .28));
        _levelLabel = panel.FirstOrDefault(t => t.Text.Contains("LEVEL", StringComparison.OrdinalIgnoreCase) || t.Text == "LEUEL")
            ?? panel.FirstOrDefault(t => t.Text is "升级" or "已满级");
        var label = _levelLabel;
        int? level = null, cost = null;
        RectangleF? button = null;
        if (label is not null)
        {
            int center = label.Box.X + label.Box.W / 2;
            // 三主题均为 150px 宽面板；可露希尔上缘比另两款低 12px。
            int buttonTop = theme == GameShopTheme.Closure ? 817 : 805;
            button = new RectangleF((float)((center - 75 * scale) / width),
                (float)((height - (1080 - buttonTop) * scale) / height),
                (float)(150 * scale / width), (float)((1070 - buttonTop) * scale / height));
            var maxLabel = panel.FirstOrDefault(t => t.Text == "已满级" && Math.Abs(t.Box.X + t.Box.W / 2 - center) < 60 * scale);
            var upgradeLabel = maxLabel ?? panel.FirstOrDefault(t => t.Text == "升级" && Math.Abs(t.Box.X + t.Box.W / 2 - center) < 60 * scale);
            if (upgradeLabel is not null)
                _upgradeLabelRegion = new(center - (int)(55 * scale), upgradeLabel.Box.Y - 4, (int)(110 * scale), upgradeLabel.Box.H + 8);
            if (maxLabel is not null || (_upgradeLabelRegion is { } max &&
                (await Read(max.X, max.Y, max.Width, max.Height)).Any(t => t.Text.Contains("已满级"))))
                return new(round, money, 6, null, isRest) { IsFinalRound = isFinalRound, UpgradeButton = button };
            bool closure = theme == GameShopTheme.Closure;
            // 可露希尔未检出 LEVEL 时，从底部定位完整等级字形，并避开两侧箭头。
            bool bottomLabel = label.Text is "升级" or "已满级";
            bool closureUpgrade = closure && bottomLabel;
            int levelWidth = closureUpgrade ? 48 : closure ? 74 : 96;
            var levelText = await Read(center - (int)(levelWidth / 2 * scale),
                (bottomLabel ? (int)(height - (closure ? 199 : 183) * scale) : label.Box.Y + label.Box.H + (closure ? 5 : 0)), (int)(levelWidth * scale), (int)((closure ? 48 : 58) * scale), true);
            string text = string.Join("", levelText.Select(t => t.Text)).Trim().ToUpperInvariant();
            level = text switch { "I" or "Ⅰ" or "1" => 1, "II" or "Ⅱ" => 2,
                "III" or "Ⅲ" => 3, "IV" or "Ⅳ" => 4, "V" or "Ⅴ" => 5, "VI" or "Ⅵ" => 6, _ => null };
            var price = await Read(center - (int)(26 * scale),
                (int)(height - 273 * scale), (int)(52 * scale), (int)(38 * scale), true);
            cost = level == 6 ? null : Number(string.Join("", price.Select(t => t.Text)));
        }
        return new(round, money, level, cost, isRest) { IsFinalRound = isFinalRound, UpgradeButton = button };
    }

    private async Task<IReadOnlyList<OcrTextResult>> ReadRegionAsync(Bitmap bitmap, byte[] png, Rectangle region, bool line)
    {
        region = Rectangle.Intersect(region, new(0, 0, bitmap.Width, bitmap.Height));
        using var crop = bitmap.Clone(region, PixelFormat.Format32bppArgb);
        var bits = crop.LockBits(new(0, 0, crop.Width, crop.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[bits.Stride * bits.Height];
        Marshal.Copy(bits.Scan0, pixels, 0, pixels.Length);
        crop.UnlockBits(bits);
        var key = (region, line);
        if (_regions.TryGetValue(key, out var cached) && SameTextPixels(pixels, cached.Pixels, crop.Width, crop.Height)) return cached.Text;
        var text = line
            ? await ocr.RecognizeLineAsync(png, region.X, region.Y, region.Width, region.Height)
            : await ocr.RecognizeRegionAsync(png, region.X, region.Y, region.Width, region.Height);
        _regions[key] = (pixels, text);
        return text;
    }

    private static bool SameTextPixels(byte[] current, byte[] previous, int width, int height)
    {
        // 局部小块比较亮度，容忍录屏压缩和底色微动；单个数字变化不能被整片平均值稀释。
        for (int y = 0; y < height; y += 8)
        for (int x = 0; x < width; x += 8)
        {
            int difference = 0, count = 0;
            for (int row = y; row < Math.Min(y + 8, height); row++)
            for (int col = x; col < Math.Min(x + 8, width); col++)
            {
                int i = (row * width + col) * 4;
                int a = (current[i] * 114 + current[i + 1] * 587 + current[i + 2] * 299) / 1000;
                int b = (previous[i] * 114 + previous[i + 1] * 587 + previous[i + 2] * 299) / 1000;
                difference += Math.Abs(a - b);
                count++;
            }
            if (difference > count * 10) return false;
        }
        return true;
    }

    private static int? Number(string text) => Regex.IsMatch(text.Trim(), @"^\d{1,3}$")
        && int.TryParse(text.Trim(), out int value) ? value : null;
}
