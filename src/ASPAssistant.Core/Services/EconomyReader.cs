using System.Drawing;
using ASPAssistant.Core.Models;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public sealed record EconomyReading(int? Round, int? Money, int? Level, int? UpgradeCost, bool IsRest);

/// <summary>复用商店截图；顶部读回合，右下读资金，LEVEL 标签定位升级面板。</summary>
public sealed class EconomyReader(IOcrEngine ocr, GameShopTheme theme = GameShopTheme.Terminal)
{
    public async Task<EconomyReading> ReadAsync(byte[] png, int width, int height)
    {
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);
        double scale = height / 1080.0 * GameUiScale.Shop(bitmap);
        var top = await ocr.RecognizeRegionAsync(png, (int)(width * .32), 0, (int)(width * .25), (int)(height * .09));
        string heading = string.Join("", top.OrderBy(t => t.Box.X).Select(t => t.Text));
        var roundLabel = top.FirstOrDefault(t => t.Text.Trim() == "回合");
        var combinedRound = top.Select(t => Regex.Match(t.Text, @"^回合\s*(\d+)$")).FirstOrDefault(m => m.Success);
        int? round = combinedRound is null ? null : Number(combinedRound.Groups[1].Value);
        if (roundLabel is not null)
        {
            var digits = await ocr.RecognizeLineAsync(png, roundLabel.Box.X + roundLabel.Box.W + 5,
                roundLabel.Box.Y, (int)(52 * scale), roundLabel.Box.H);
            round = Number(string.Join("", digits.Select(t => t.Text)));
        }
        var moneyText = await ocr.RecognizeLineAsync(png, (int)(width - 130 * scale),
            (int)(height - 273 * scale), (int)(65 * scale), (int)(38 * scale));
        int? money = Number(string.Join("", moneyText.Select(t => t.Text)));
        var panel = await ocr.RecognizeRegionAsync(png, 0, (int)(height * .72), (int)(width * .85), (int)(height * .28));
        var label = panel.FirstOrDefault(t => t.Text.Contains("LEVEL", StringComparison.OrdinalIgnoreCase) || t.Text == "LEUEL")
            ?? panel.FirstOrDefault(t => t.Text is "升级" or "已满级");
        int? level = null, cost = null;
        if (label is not null)
        {
            int center = label.Box.X + label.Box.W / 2;
            if (panel.Any(t => t.Text == "已满级" && Math.Abs(t.Box.X + t.Box.W / 2 - center) < 60 * scale))
                return new(round, money, 6, null, heading.Contains("休息") || heading.Contains("休整"));
            bool closure = theme == GameShopTheme.Closure;
            var levelText = await ocr.RecognizeLineAsync(png, center - (int)((closure ? 37 : 48) * scale),
                (label.Text == "升级" ? (int)(height - 183 * scale) : label.Box.Y + label.Box.H + (closure ? 5 : 0)), (int)((closure ? 74 : 96) * scale), (int)((closure ? 48 : 58) * scale));
            string text = string.Join("", levelText.Select(t => t.Text)).Trim().ToUpperInvariant();
            level = text switch { "I" or "Ⅰ" or "1" => 1, "II" or "Ⅱ" => 2,
                "III" or "Ⅲ" => 3, "IV" or "Ⅳ" => 4, "V" or "Ⅴ" => 5, "VI" or "Ⅵ" => 6, _ => null };
            var price = await ocr.RecognizeLineAsync(png, center - (int)(26 * scale),
                (int)(height - 273 * scale), (int)(52 * scale), (int)(38 * scale));
            cost = level == 6 ? null : Number(string.Join("", price.Select(t => t.Text)));
        }
        return new(round, money, level, cost, heading.Contains("休息") || heading.Contains("休整"));
    }

    private static int? Number(string text) => Regex.IsMatch(text.Trim(), @"^\d{1,3}$")
        && int.TryParse(text.Trim(), out int value) ? value : null;
}
