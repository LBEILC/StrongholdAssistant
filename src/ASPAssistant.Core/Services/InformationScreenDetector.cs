using ASPAssistant.Core.GameModes;

namespace ASPAssistant.Core.Services;

/// <summary>Positive visual evidence for the opening, in-game info and gameplay pages.</summary>
public sealed class InformationScreenDetector(IOcrEngine ocr, IOcrStrategy strategy)
{
    // 开局左上角模式名；仅去掉空白，不纠错或模糊匹配。
    public async Task<ASPAssistant.Core.Models.GameModeInfo?> ReadModeAsync(byte[] screenshot, int width, int height,
        IReadOnlyList<ASPAssistant.Core.Models.GameModeInfo> modes)
    {
        var texts = await ScanAsync(screenshot, width, height, (.02, .015, .40, .12));
        static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        return modes.FirstOrDefault(m => texts.Any(t => Compact(t.Text) == Compact(m.Name)));
    }

    /// <summary>
    /// 标题带的位置（比例）。标准模拟与险境两种页面实测都在这个范围：
    /// 核心盟约标题 y≈0.185，附加盟约标题 y≈0.45。只扫这两条窄带比扫整块面板
    /// （1190×410）快约 3–6 倍，识别结论相同。
    /// </summary>
    private static readonly (double X, double Y, double W, double H) CoreHeaderBand = (0.02, 0.13, 0.70, 0.13);
    private static readonly (double X, double Y, double W, double H) AdditionalHeaderBand = (0.02, 0.38, 0.70, 0.14);

    /// <summary>兜底：整块面板一次扫（两条窄带都没命中时才会用到）。</summary>
    private static readonly (double X, double Y, double W, double H) WholePanel = (0.05, 0.13, 0.62, 0.38);

    public async Task<bool> IsInformationScreenAsync(byte[] screenshot, int width, int height)
    {
        if (await HasBothHeadersAsync(screenshot, width, height, CoreHeaderBand, AdditionalHeaderBand))
            return true;

        // 页面可能有轻微位移或不同分辨率下的界面上移，退回整块面板再确认一次。
        return await HasBothHeadersAsync(screenshot, width, height, WholePanel, WholePanel, sameBand: true);
    }

    private async Task<bool> HasBothHeadersAsync(
        byte[] screenshot, int width, int height,
        (double X, double Y, double W, double H) coreBand,
        (double X, double Y, double W, double H) additionalBand,
        bool sameBand = false)
    {
        if (sameBand)
        {
            var texts = await ScanAsync(screenshot, width, height, coreBand);
            var normalized = texts.Select(t => Normalize(t.Text)).ToList();
            return normalized.Any(t => t.Contains("核心盟约")) && normalized.Any(t => t.Contains("附加盟约"));
        }

        var coreTask = ScanAsync(screenshot, width, height, coreBand);
        var additionalTask = ScanAsync(screenshot, width, height, additionalBand);
        await Task.WhenAll(coreTask, additionalTask);

        return (await coreTask).Select(t => Normalize(t.Text)).Any(t => t.Contains("核心盟约"))
            && (await additionalTask).Select(t => Normalize(t.Text)).Any(t => t.Contains("附加盟约"));
    }

    private Task<IReadOnlyList<OcrTextResult>> ScanAsync(
        byte[] screenshot, int width, int height, (double X, double Y, double W, double H) band) =>
        ocr.RecognizeRegionAsync(screenshot,
            (int)(width * band.X), (int)(height * band.Y),
            (int)(width * band.W), (int)(height * band.H));

    /// <summary>标题里的「盟」在低分辨率/抗锯齿下常被读成「明」，统一归一化后再比较。</summary>
    private static string Normalize(string text) => text.Replace(" ", "").Replace("明", "盟").Replace("約", "约");

    public async Task<bool> IsOpeningScreenAsync(byte[] screenshot, int width, int height)
    {
        var region = strategy.GetScanRegions().Single(r => r.Name == "BanConfirmText");
        var texts = await ocr.RecognizeRegionAsync(screenshot,
            (int)(width * region.XPercent), (int)(height * region.YPercent),
            (int)(width * region.WidthPercent), (int)(height * region.HeightPercent));
        return texts.Any(t => t.Text.Replace(" ", "").Contains("确认本局信息"))
            && (await IsInformationScreenAsync(screenshot, width, height)
                // 单人核心盟约占两行，附加盟约标题比联机页更靠下。
                || await HasBothHeadersAsync(screenshot, width, height, CoreHeaderBand, (.02, .60, .70, .16)));
    }

    public async Task<bool> IsGameplayAsync(byte[] screenshot, int width, int height)
    {
        // Both cues are left of the attached panel. Neither exists on the info modal.
        var round = await ocr.RecognizeRegionAsync(screenshot,
            (int)(width * .34), 0, (int)(width * .13), (int)(height * .10));
        if (!round.Any(t => t.Text.Contains("回合"))) return false;
        var upgrade = await ocr.RecognizeRegionAsync(screenshot,
            (int)(width * .10), (int)(height * .88), (int)(width * .35), (int)(height * .09));
        return upgrade.Any(t => t.Text.Contains("升级") || t.Text.Contains("已满级"));
    }

    public async Task<bool> IsSoloOpeningAsync(byte[] screenshot, int width, int height)
    {
        if (!await IsOpeningScreenAsync(screenshot, width, height)) return false;
        return await HasSoloPromptAsync(screenshot, width, height);
    }

    // 调用方已经确认是开局页时，直接检查提示带，避免重复标题 OCR。
    public async Task<bool> HasSoloPromptAsync(byte[] screenshot, int width, int height)
    {
        var band = GameUiScale.InformationRegion(
            new OcrRegionDefinition("SoloPrompt", .07, .225, .63, .035), GameUiScale.Information(screenshot));
        var texts = await ocr.RecognizeRegionAsync(screenshot,
            (int)(width * band.XPercent), (int)(height * band.YPercent),
            (int)(width * band.WidthPercent), (int)(height * band.HeightPercent));
        return texts.Any(t => Normalize(t.Text).Contains("请选择一组"));
    }
}
