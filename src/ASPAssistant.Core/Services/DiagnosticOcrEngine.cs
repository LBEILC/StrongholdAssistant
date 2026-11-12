using System.Collections.Concurrent;

namespace ASPAssistant.Core.Services;

// 每次识别只保留有限的区域摘要，关键结果同时进入滚动日志。
public sealed class DiagnosticOcrEngine(IOcrEngine inner) : IOcrEngine
{
    private readonly ConcurrentQueue<string> _steps = new();
    public void Clear() => _steps.Clear();
    public string[] Steps => _steps.ToArray();
    private void Record(int x, int y, int width, int height, IEnumerable<string> texts)
    {
        var text = string.Join(" | ", texts);
        var step = $"OCR ({x},{y},{width},{height}): {text[..Math.Min(text.Length, 240)]}";
        _steps.Enqueue(step);
        while (_steps.Count > 24) _steps.TryDequeue(out _);
        AppLogger.Info("OCR", step);
    }
    public async Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height)
    {
        var result = await inner.RecognizeRegionAsync(pngBytes, x, y, width, height);
        Record(x, y, width, height, result.Select(t => t.Text));
        return result;
    }
    public async Task<IReadOnlyList<OcrTextResult>> RecognizeLineAsync(byte[] pngBytes, int x, int y, int width, int height)
    {
        var result = await inner.RecognizeLineAsync(pngBytes, x, y, width, height);
        Record(x, y, width, height, result.Select(t => t.Text));
        return result;
    }
    public async Task<string?> FindCandidateInRegionAsync(byte[] pngBytes, int x, int y, int width, int height, IReadOnlyList<string> candidates)
    {
        var result = await inner.FindCandidateInRegionAsync(pngBytes, x, y, width, height, candidates);
        Record(x, y, width, height, [result ?? ""]);
        return result;
    }
}
