namespace ASPAssistant.Core.Services;

/// <summary>只有右上角就绪控件仍可见的主画面允许画路线，包含联机取消就绪状态。</summary>
public sealed class BoardSurfaceReader(IOcrEngine ocr)
{
    public async Task<bool> IsMainScreenAsync(byte[] frame, int width, int height)
    {
        double scale = Math.Min(width / 1920.0, height / 1080.0);
        var texts = await ocr.RecognizeRegionAsync(frame, (int)(width - 300 * scale),
            (int)(100 * scale), (int)(300 * scale), (int)(160 * scale));
        return texts.Any(t => t.Text.Replace(" ", "").Contains("就绪", StringComparison.Ordinal));
    }
}
