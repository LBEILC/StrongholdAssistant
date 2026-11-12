using System.Collections.Concurrent;
using System.Security.Cryptography;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

// 只在一次测试运行内复用同图同区域的原生结果；不同截图、区域和识别方式分别缓存。
public sealed class CachedTestOcrEngine : IOcrEngine, IDisposable
{
    private readonly MaaOcrEngine _engine = NativeOcrEngines.Create();
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<OcrTextResult>>>> _results = new();

    private Task<IReadOnlyList<OcrTextResult>> Read(byte[] png, int x, int y, int w, int h, bool line)
    {
        var key = $"{Convert.ToHexString(SHA256.HashData(png))}:{x}:{y}:{w}:{h}:{line}";
        return _results.GetOrAdd(key, _ => new(() => line
            ? _engine.RecognizeLineAsync(png, x, y, w, h)
            : _engine.RecognizeRegionAsync(png, x, y, w, h))).Value;
    }

    public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] png, int x, int y, int w, int h)
        => Read(png, x, y, w, h, false);
    public Task<IReadOnlyList<OcrTextResult>> RecognizeLineAsync(byte[] png, int x, int y, int w, int h)
        => Read(png, x, y, w, h, true);
    public Task<string?> FindCandidateInRegionAsync(byte[] png, int x, int y, int w, int h, IReadOnlyList<string> candidates)
        => _engine.FindCandidateInRegionAsync(png, x, y, w, h, candidates);
    public Task WarmUpAsync() => _engine.WarmUpAsync();
    public void Dispose() => _engine.Dispose();
}
