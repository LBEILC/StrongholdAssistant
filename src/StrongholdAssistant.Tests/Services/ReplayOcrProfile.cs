using System.Diagnostics;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests.Services;

// 只在离线回放中记录；原生推理可能并行，重叠调用的进程 CPU 不能相加。
internal sealed class ReplayOcrProfile(IOcrEngine engine, Process process) : IOcrEngine
{
    internal sealed record Call(string Reader, string Method, int X, int Y, int Width, int Height,
        double StartMs, double WallMs, double CpuMs);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    public List<Call> Calls { get; } = [];

    private async Task<T> Measure<T>(string method, int x, int y, int w, int h, Func<Task<T>> action)
    {
        var reader = new StackTrace().GetFrames().Select(frame => frame.GetMethod()?.DeclaringType)
            .Select(type => type?.DeclaringType ?? type)
            .FirstOrDefault(type => type?.Namespace == typeof(IOcrEngine).Namespace && type != typeof(DiagnosticOcrEngine));
        var cpu = process.TotalProcessorTime;
        double start = _clock.Elapsed.TotalMilliseconds;
        var result = await action();
        var call = new Call(reader?.Name ?? "unknown", method, x, y, w, h, start,
            _clock.Elapsed.TotalMilliseconds - start, (process.TotalProcessorTime - cpu).TotalMilliseconds);
        lock (Calls) Calls.Add(call);
        return result;
    }

    public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] bytes, int x, int y, int w, int h)
        => Measure("region", x, y, w, h, () => engine.RecognizeRegionAsync(bytes, x, y, w, h));

    public Task<IReadOnlyList<OcrTextResult>> RecognizeLineAsync(byte[] bytes, int x, int y, int w, int h)
        => Measure("line", x, y, w, h, () => engine.RecognizeLineAsync(bytes, x, y, w, h));

    public Task<string?> FindCandidateInRegionAsync(byte[] bytes, int x, int y, int w, int h, IReadOnlyList<string> candidates)
        => Measure("candidate", x, y, w, h, () => engine.FindCandidateInRegionAsync(bytes, x, y, w, h, candidates));
}
