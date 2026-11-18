using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed partial class FieldTracker
{
    private Dictionary<FieldCell, int> _lastCells = [];
    private int? _observedMoney;
    private bool _pendingTrigger;

    private void ResetTriggers()
    {
        _lastCells.Clear(); _observedMoney = null; _pendingTrigger = false;
    }

    private bool ObserveTriggers(FieldObservation o)
    {
        bool moneyChanged = o.Money is not null && _observedMoney is not null && o.Money != _observedMoney;
        _observedMoney = o.Money ?? _observedMoney;
        _pendingTrigger |= moneyChanged || o.PanelOperator is not null || o.DragOperator is not null;
        if (!o.MainScreen || o.DragOperator is not null) { _lastCells.Clear(); return false; }
        bool stableChange = o.Cells.Any(p => p.Value != (_units.GetValueOrDefault(p.Key)?.Tier ?? 0)
            && _lastCells.TryGetValue(p.Key, out int previous) && previous == p.Value);
        _lastCells = new(o.Cells);
        bool run = State is null || _battle || o.Round != State.Round || _pendingTrigger || _drag is not null || stableChange;
        // 未触发身份处理时仍计算连续空扫描次数，避免旧的漏读身份永不过期。
        if (!run)
            foreach (var (cell, missing) in _missing.ToArray())
                if (o.Cells.GetValueOrDefault(cell, -1) == 0)
                    _missing[cell] = missing with { Scans = missing.Scans + 1 };
        if (run) _pendingTrigger = false;
        return run;
    }

    // 自动关闭时更新内部占用，保留手动指认，供下一次强制识别使用。
    private void ObserveOccupancy(FieldObservation o, bool triggered)
    {
        if (!o.MainScreen) return;
        if (triggered)
        {
            foreach (var (cell, tier) in o.Cells)
            {
                if (_manualOnly.Contains(cell) && tier == 0) continue;
                _manualOnly.Remove(cell);
                if (_units.TryGetValue(cell, out var manual) && manual.Source == "manual" && manual.Tier == tier) continue;
                if (tier == 0) _units.Remove(cell);
                else _units[cell] = new(cell.Zone, cell.Col, cell.Row, tier, null, null);
            }
            foreach (var (cell, unit) in _units.ToArray())
                if (unit.Source != "manual")
                    _units[cell] = unit with { Operator = null, Source = null, Status = "unknown", Candidates = [] };
            _battle = false;
        }
        _money = o.Money ?? _money;
    }
}
