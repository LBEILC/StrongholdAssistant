using System.Diagnostics;

namespace ASPAssistant.Core.Services;

/// <summary>手动认人的分段耗时；随异步调用传递，不改识别数据和界面协议。</summary>
public sealed class FieldRecognitionTiming : IDisposable
{
    private static readonly AsyncLocal<FieldRecognitionTiming?> Current = new();
    public static FieldRecognitionTiming? Active => Current.Value;
    public string RequestId { get; } = Guid.NewGuid().ToString("N");
    private readonly long _started = Stopwatch.GetTimestamp();
    public void Mark(string stage) { lock (_milliseconds) _milliseconds[stage] = Stopwatch.GetElapsedTime(_started).TotalMilliseconds; }
    private readonly FieldRecognitionTiming? _previous;
    private readonly Dictionary<string, double> _milliseconds = [];

    public FieldRecognitionTiming()
    {
        _previous = Current.Value;
        Current.Value = this;
    }

    public Dictionary<string, double> Snapshot()
    {
        lock (_milliseconds) return new(_milliseconds);
    }

    public static IDisposable? Measure(string stage) => Current.Value is { } timing ? new Segment(timing, stage) : null;

    internal void Record(string stage, double milliseconds)
    {
        lock (_milliseconds) _milliseconds[stage] = _milliseconds.GetValueOrDefault(stage) + milliseconds;
    }

    public void Dispose() => Current.Value = _previous;

    private sealed class Segment(FieldRecognitionTiming owner, string stage) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        public void Dispose()
        {
            double elapsed = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            owner.Record(stage, elapsed);
        }
    }
}
