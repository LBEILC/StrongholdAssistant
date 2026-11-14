// 2026-11-12：高画面商店按宽度缩放并锚定底部；忽略单字名称旁被识别为 △ 的职业图标。
// 2026-11-11：从卡片间距测量 UI 缩放，以右下角为基准换算名称栏。
// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Drawing;
// 2026-11-12：输出商店识别失败步骤及测得缩放，供宿主保存诊断。
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Reads the shop's bottom name strip, independently of card colour and slot count.
/// Uses the selected game's name band and colour preprocessing.
/// Gold light on an operator is not evidence of an elite upgrade.
/// </summary>
public sealed class ShopReader(IOcrEngine engine, IReadOnlyList<string> catalogNames)
{
    private readonly string[] _names = catalogNames.OrderByDescending(n => n.Length).ToArray();
    private volatile GameShopTheme _theme = GameShopTheme.Terminal;
    public GameShopTheme Theme { get => _theme; set => _theme = value; }
    public bool HasVisibleCards { get; private set; }
    public string? Failure { get; private set; }
    public double? UiScale { get; private set; }
    public bool ReadUntracked { get; set; }
    public IReadOnlyList<ShopItem> AllItems { get; private set; } = [];

    public async Task<IReadOnlyList<ShopItem>> ReadAsync(
        byte[] screenshot, int width, int height, IReadOnlyList<string> trackedNames)
    {
        HasVisibleCards = false;
        Failure = null;
        UiScale = null;
        AllItems = [];
        if (trackedNames.Count == 0 && !ReadUntracked)
            return [];
        var theme = Theme;

        using var stream = new MemoryStream(screenshot);
        using var source = new Bitmap(stream);
        // 居中的 16:9 录屏黑边不属于游戏视口；实际宽屏商店则锚定视口右下角。
        int contentWidth = (int)Math.Round(height * 16.0 / 9);
        int padding = (width - contentWidth) / 2;
        if (padding > 0 && Enumerable.Range(0, 20).All(n =>
            source.GetPixel(padding / 2, n * height / 20).ToArgb() == Color.Black.ToArgb()
            && source.GetPixel(width - 1 - padding / 2, n * height / 20).ToArgb() == Color.Black.ToArgb()))
        {
            using var cropped = source.Clone(new Rectangle(padding, 0, contentWidth, height), PixelFormat.Format32bppArgb);
            using var croppedBytes = new MemoryStream();
            cropped.Save(croppedBytes, ImageFormat.Png);
            var croppedItems = await ReadAsync(croppedBytes.ToArray(), contentWidth, height, trackedNames);
            foreach (var item in AllItems.Concat(croppedItems).Distinct())
            {
                var ocr = item.OcrRegion;
                item.OcrRegion = ((padding + ocr.X * contentWidth) / width, ocr.Y, ocr.Width * contentWidth / width, ocr.Height);
                var card = item.CardRegion;
                item.CardRegion = ((padding + card.X * contentWidth) / width, card.Y, card.Width * contentWidth / width, card.Height);
            }
            return croppedItems;
        }
        // Small captures lose thin strokes in the frozen white labels. Normalize
        // their working height before cropping; output coordinates remain ratios.
        using var enlarged = height < 1080 ? new Bitmap((int)Math.Round(width * 1080.0 / height), 1080) : null;
        if (enlarged != null)
        {
            using var graphics = Graphics.FromImage(enlarged);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, enlarged.Width, enlarged.Height);
            width = enlarged.Width;
            height = enlarged.Height;
        }
        var workingImage = enlarged ?? source;
        double k = GameUiScale.Shop(workingImage);
        UiScale = k;
        double scale = Math.Min(width / 1920.0, height / 1080.0);
        HasVisibleCards = ShopLayout.HasCards(workingImage, k, theme);
        int ShopY(double y) => (int)Math.Round(height - (1080 - y) * scale * k);
        int ShopX(double offset) => (int)Math.Round(width - (960 - offset) * scale * k);
        int Size(double size) => (int)Math.Round(size * scale * k);
        // Isolate and enlarge small, dim text. Including the snowflakes below this
        // strip makes the detector merge or lose frozen card names.
        int top = ShopY(theme == GameShopTheme.Terminal ? 1014 : 990);
        int stripHeight = Size(theme == GameShopTheme.Terminal ? 34 : 50);
        int canvasTop = height * 3 / 4;
        int nameTop = ShopY(theme == GameShopTheme.Terminal ? 1018 : 1006);
        int nameHeight = Size(theme == GameShopTheme.Terminal ? 26 : 30);
        // Keep vertical padding: a very wide, 50px-high input makes PaddleOCR's
        // detector resize away the frozen text strokes.
        using var strip = new Bitmap(width * 2, (height - canvasTop) * 2, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(strip))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Black);
            graphics.DrawImage(workingImage, new Rectangle(0, (top - canvasTop) * 2, strip.Width, stripHeight * 2),
                0, top, width, stripHeight, GraphicsUnit.Pixel);
        }

        if (theme != GameShopTheme.Closure)
        using (var originalStrip = (Bitmap)strip.Clone())
        using (var graphics = Graphics.FromImage(strip))
        {
            // Terminal/Ursus white names become dim under the blue freezing overlay.
            // Red-channel contrast suppresses that blue background. Keep the original
            // band as well: the same enhancement can erase Closure's dark letters.
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix([
                [5, 5, 5, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 0, 0],
                [0, 0, 0, 1, 0],
                [theme == GameShopTheme.Terminal ? -1.3f : -1.72f,
                 theme == GameShopTheme.Terminal ? -1.3f : -1.72f,
                 theme == GameShopTheme.Terminal ? -1.3f : -1.72f, 0, 1]]));
            // These skins use up to six right-aligned 220px slots at 1080p.
            // Alternate rows prevent the detector joining neighbouring frozen labels.
            // Empty/sold slots are harmless; the original band remains layout-independent.
            for (int slot = 0; slot < 6; slot++)
            {
                int left = ShopX(slot * 220 - 600);
                int bandTop = (int)Math.Round(height * (slot % 2 == 0 ? 60 : 200) / 1080.0);
                int slotWidth = Size(220);
                graphics.DrawImage(originalStrip, new Rectangle(left * 2, bandTop, slotWidth * 2, nameHeight * 2),
                    left * 2, (nameTop - canvasTop) * 2, slotWidth * 2, nameHeight * 2, GraphicsUnit.Pixel, attributes);
            }
            // Isolate the shop control too: on wide captures it can merge with
            // the adjacent card and lose "收起", incorrectly hiding all markers.
            int controlLeft = ShopX(770);
            int controlWidth = Size(120);
            int controlHeight = Size(theme == GameShopTheme.Terminal ? 42 : 35);
            graphics.DrawImage(workingImage,
                new Rectangle(controlLeft * 2, (int)Math.Round(height * 60 / 1080.0), controlWidth * 2, controlHeight * 2),
                controlLeft, ShopY(theme == GameShopTheme.Terminal ? 1006 : 980),
                controlWidth, controlHeight, GraphicsUnit.Pixel);
        }
        using var encoded = new MemoryStream();
        strip.Save(encoded, ImageFormat.Png);
        var ocrInput = encoded.ToArray();
        // 高分辨率缩小后，空槽纹理可能被检测成横跨整张卡片的单个 W。
        // 只排除过宽的单个英文字符；「砾」这类单字干员名不受影响。
        var texts = (await engine.RecognizeRegionAsync(ocrInput, 0, 0, strip.Width, strip.Height))
            .Where(t => t.Text.Trim() is not [< (char)128] || t.Box.W <= t.Box.H * 2).ToList();
        if (!texts.Any(t => t.Text.Contains("收起", StringComparison.Ordinal) && t.Box.X + t.Box.W > width * 1.6))
            // 没找到「收起」时商店未打开，属于正常画面，不上报识别失败。
            return [];
        var originalTexts = texts.Where(t => t.Box.Y + t.Box.H / 2.0 >= height * 300 / 1080.0
            || t.Text.Contains("收起", StringComparison.Ordinal)).ToList();
        var items = MatchNames(originalTexts, width, height, canvasTop, _names).ToList();
        var enhancedTexts = texts.Where(t => t.Box.Y + t.Box.H / 2.0 < height * 300 / 1080.0)
            .Select(t => new OcrTextResult(t.Text, (t.Box.X,
                t.Box.Y + (nameTop - canvasTop) * 2
                    - (int)Math.Round(height * (t.Box.Y + t.Box.H / 2.0 < height * 170 / 1080.0 ? 60 : 200) / 1080.0),
                t.Box.W, t.Box.H))).ToList();
        foreach (var item in MatchNames(enhancedTexts, width, height, canvasTop, _names))
        {
            if (items.Any(i => i.Name == item.Name && Math.Abs(
                i.OcrRegion.X + i.OcrRegion.Width / 2 - item.OcrRegion.X - item.OcrRegion.Width / 2)
                < height * 110.0 / (1080 * width)))
                continue;
            items.Add(item);
        }
        if (theme == GameShopTheme.Terminal)
        {
            // Freezing can hide the entire word from the detector. Precisely read
            // the missing card's name line instead of guessing from a partial name.
            for (int slot = 0; slot < 6; slot++)
            {
                int left = ShopX(slot * 220 - 600);
                int slotWidth = Size(220);
                if (items.Any(i => (i.OcrRegion.X + i.OcrRegion.Width / 2) * width >= left
                    && (i.OcrRegion.X + i.OcrRegion.Width / 2) * width < left + slotWidth))
                    continue;
                int inset = Size(32);
                // The wide crop retains long names; the narrow crop removes
                // snowflake texture to the right of short, dim names.
                foreach (int lineWidthPx in new[] { 180, 110 })
                {
                    int lineWidth = Size(lineWidthPx);
                    var results = await engine.RecognizeLineAsync(ocrInput, (left + inset) * 2,
                        (nameTop - canvasTop) * 2, lineWidth * 2, nameHeight * 2);
                    // Observed on the 720p frozen sample; correct whole names,
                    // not individual glyphs or approximate catalogue neighbours.
                    var lines = results.Select(t => new OcrTextResult(
                        t.Text.Replace("斯卡菜", "斯卡蒂").Replace("复泰", "复奏"),
                        ((left + inset) * 2, (nameTop - canvasTop) * 2, lineWidth * 2, nameHeight * 2))).ToList();
                    var matches = MatchNames(lines, width, height, canvasTop, _names);
                    if (matches.Count == 0) continue;
                    items.AddRange(matches);
                    break;
                }
            }
        }
        if (HasVisibleCards && items.Count == 0) Failure = $"商店卡片的名字没读出来。\n{OcrSummary(texts)}";
        AppLogger.Info("Shop", $"UI 缩放={k:F3}; 可见卡片={HasVisibleCards}; 识别名称={string.Join('|', items.Select(i => i.Name))}");
        AllItems = ShopLayout.Snap(items, width, height, k, theme);
        return AllItems.Where(i => trackedNames.Contains(i.Name)).ToArray();
    }

    private static string OcrSummary(IReadOnlyList<OcrTextResult> texts)
    {
        var text = string.Join(" | ", texts.Select(t => t.Text));
        return $"OCR：{text[..Math.Min(text.Length, 240)]}";
    }

    public IReadOnlyList<ShopItem> Match(IReadOnlyList<OcrTextResult> texts,
        int width, int height, int stripTop, IReadOnlyList<string> trackedNames)
    {
        // Do not leave markers on menus/closed shops with unrelated bottom text.
        if (!texts.Any(t => t.Text.Contains("收起", StringComparison.Ordinal) && t.Box.X + t.Box.W > width * 1.6))
            return [];
        return ShopLayout.Snap(MatchNames(texts, width, height, stripTop, trackedNames), width, height, 1, Theme);
    }

    private IReadOnlyList<ShopItem> MatchNames(IReadOnlyList<OcrTextResult> texts,
        int width, int height, int stripTop, IReadOnlyList<string> trackedNames)
    {
        var items = new List<ShopItem>();
        var occurrences = new Dictionary<string, int>();
        foreach (var text in texts.OrderBy(t => t.Box.X))
        {
            var value = text.Text.Replace(" ", "").Replace("傑", "杰").Replace("奧", "奥")
                .Replace("卫成推荐信", "卫戍推荐信").Replace("野繫", "野鬃")
                .Replace("野紫", "野鬃").Replace("云边", "云迹");
            // Resolve against the whole catalogue before filtering tracking. Otherwise
            // tracking 维式重锤 would also match an untracked 终极维式重锤.
            var matches = new List<(string Name, int Start, int Length)>();
            foreach (var name in _names)
            {
                var candidate = name.Replace(" ", "");
                int start = value.IndexOf(candidate, StringComparison.Ordinal);
                while (start >= 0)
                {
                    if ((candidate.Length > 1 || value.Trim('M', 'X', '入', 'λ', '×', '△') == candidate)
                        && !matches.Any(m => start < m.Start + m.Length && start + candidate.Length > m.Start))
                        matches.Add((name, start, candidate.Length));
                    start = value.IndexOf(candidate, start + candidate.Length, StringComparison.Ordinal);
                }
            }

            // Wide images can merge neighbouring labels (or 鸿雪 + 收起). Split
            // their OCR bounds by character span instead of discarding a slot.
            foreach (var match in matches.OrderBy(m => m.Start))
            {
                if (!trackedNames.Contains(match.Name))
                    continue;
                // The shopkeeper panel can resemble a one-character operator
                // (e.g. "山"); only accept names within the merchandise area.
                if (text.Box.X + text.Box.W * (match.Start + match.Length / 2.0) / value.Length
                    > width + height * 1440 / 1080.0)
                    continue;
                occurrences.TryGetValue(match.Name, out int occurrence);
                occurrences[match.Name] = occurrence + 1;
                items.Add(new ShopItem
                {
                    Id = $"{match.Name}@{occurrence}",
                    Name = match.Name,
                    OcrRegion = ((text.Box.X + text.Box.W * match.Start / (double)value.Length) / (2 * width),
                        (stripTop + text.Box.Y / 2.0) / height,
                        text.Box.W * match.Length / (double)value.Length / (2 * width), text.Box.H / (2.0 * height)),
                });
            }
        }
        return items;
    }
}
