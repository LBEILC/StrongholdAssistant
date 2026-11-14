using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private readonly object _fieldGate = new();
    private readonly FieldTracker _fieldTracker = new();
    private FieldReader? _fieldReader;
    private int _fieldGeneration;
    private int _fieldReaderGeneration;
    public Func<System.Drawing.RectangleF?> FieldCursorRegion { get; set; } = () => null;

    public FieldState? Field { get { lock (_fieldGate) return _fieldTracker.State; } }

    public void InitializeField(string directory, IOcrEngine ocr)
    {
        _fieldReader = new(directory, ocr, _operators.Select(o => o.Name));
        _shopReader!.ReadUntracked = true;
    }

    public async Task ObserveFieldAsync(byte[] frame, DateTimeOffset at, EconomyReading? economy,
        bool mainScreen, bool shopVisible, IReadOnlyList<ShopItem> shop)
    {
        int generation;
        lock (_fieldGate) generation = _fieldGeneration;
        if (_fieldReaderGeneration != generation) { _fieldReader!.Reset(); _fieldReaderGeneration = generation; }
        _fieldReader!.Theme = Theme(Settings.GameTheme);
        var cursor = FieldCursorRegion();
        var observation = await _fieldReader!.ReadAsync(frame, at, economy, mainScreen, shopVisible, shop, cursor);
        FieldState? changed = null;
        lock (_fieldGate)
            if (generation == _fieldGeneration && _fieldTracker.Observe(observation)) changed = _fieldTracker.State;
        if (changed is not null) Changed?.Invoke("field", changed);
    }

    public FieldState SetFieldOperator(FieldCell cell, string? name)
    {
        if (!(cell.Zone == "bench" && cell.Col is >= 1 and <= 10 && cell.Row == 0
            || cell.Zone == "board" && cell.Col is >= 0 and <= 6 && cell.Row is >= 0 and <= 4))
            throw new ArgumentException("格坐标无效。");
        if (name is not null && !_operators.Any(o => o.Name == name)) throw new ArgumentException("干员名无效。");
        FieldState state; bool changed;
        lock (_fieldGate)
        {
            changed = _fieldTracker.SetOperator(cell, name, DateTimeOffset.UtcNow);
            state = _fieldTracker.State!;
        }
        if (changed) Changed?.Invoke("field", state);
        return state;
    }

    private void ResetField()
    {
        bool hadState;
        lock (_fieldGate)
        {
            _fieldGeneration++;
            hadState = _fieldTracker.State is not null;
            _fieldTracker.Reset();
        }
        // 接口事件不允许 null，用空状态通知清除；field() 仍返回未识别的 null。
        if (hadState) Changed?.Invoke("field", new FieldState(null, null, DateTimeOffset.UtcNow.ToString("O"), [], []));
    }
}
