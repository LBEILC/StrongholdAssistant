using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>只消费观测，不接触截图、OCR、时钟或存储。</summary>
public sealed class FieldTracker
{
    public FieldState? State { get; private set; }
    private readonly Dictionary<FieldCell, FieldUnit> _units = new();
    private readonly List<FieldEvent> _log = [];
    private IReadOnlyDictionary<int, string>? _shop;
    private readonly List<(DateTimeOffset At, int Slot, string Name)> _buys = [];
    private readonly List<(DateTimeOffset At, FieldCell Cell)> _gains = [];
    private string? _drag;
    private Dictionary<FieldCell, FieldUnit>? _beforeDrag;
    private int? _money, _dragMoney;
    private bool _battle;

    public void Reset()
    {
        State = null; _units.Clear(); _log.Clear(); _shop = null;
        _buys.Clear(); _gains.Clear(); _drag = null; _beforeDrag = null;
        _money = _dragMoney = null; _battle = false;
    }

    public bool SetOperator(FieldCell cell, string? name, DateTimeOffset at)
    {
        // TODO(design): 确认未识别到占用的格子能否手动建人；当前只允许给已有占用指定身份。
        if (!_units.TryGetValue(cell, out var unit)) throw new ArgumentException("这格还没有识别到干员。");
        _units[cell] = unit with { Operator = name, Source = name is null ? null : "manual" };
        return Publish(State!.MapId, State.Round, at);
    }

    public bool Observe(FieldObservation o)
    {
        if (!o.IsRest) { _battle = true; _drag = null; _beforeDrag = null; _shop = null; _buys.Clear(); _gains.Clear(); return false; }
        if (o.DragOperator is { } drag)
        {
            if (_drag is null) { _beforeDrag = new(_units); _dragMoney = _money; }
            _drag = drag;
            return false;
        }
        if (!o.MainScreen) { _shop = null; return false; }
        var before = _beforeDrag ?? new(_units);
        var removed = before.Keys.Where(c => o.Cells.TryGetValue(c, out int t) && t == 0).ToList();
        var added = o.Cells.Where(p => p.Value > 0 && !before.ContainsKey(p.Key)).Select(p => p.Key).ToList();
        foreach (var (cell, tier) in o.Cells)
        {
            if (tier == 0) _units.Remove(cell);
            else if (!_units.TryGetValue(cell, out var old) || old.Tier != tier)
                _units[cell] = new(cell.Zone, cell.Col, cell.Row, tier, null, null);
        }
        if (!_battle && _drag is { } name)
        {
            if (removed.Count == 1 && added.Count == 1)
            {
                var from = removed[0]; var to = added[0];
                _units[to] = _units[to] with { Operator = name, Source = "drag" };
                Add(o.At, "move", name, from, to); removed.Clear(); added.Clear();
            }
            else if (removed.Count == 1 && added.Count == 0 && o.Money > _dragMoney)
            {
                Add(o.At, "sell", name, removed[0], null); removed.Clear();
            }
            else if (removed.Count == 0 && added.Count == 0)
            {
                var changed = o.Cells.Where(p => p.Value > 0 && before.TryGetValue(p.Key, out var u)
                    && u.Tier != p.Value).Select(p => p.Key).ToArray();
                if (changed.Length == 2)
                {
                    var a = changed[0]; var b = changed[1];
                    bool known = before[a].Tier == _units[b].Tier && before[b].Tier == _units[a].Tier;
                    foreach (var (from, to) in new[] { (a, b), (b, a) })
                    {
                        var u = before[from];
                        _units[to] = _units[to] with { Operator = known ? u.Operator : null, Source = known ? u.Source : null };
                        Add(o.At, "move", known ? u.Operator : null, from, to);
                    }
                    var origin = changed.Where(c => before[c].Operator == name).ToArray();
                    if (known && origin.Length == 1)
                    {
                        var to = origin[0] == a ? b : a;
                        _units[to] = _units[to] with { Operator = name, Source = "drag" };
                    }
                }
                else
                {
                    // 同阶换位与原位取消无法仅凭角标区分；清除可能被交换的身份。
                    var origins = before.Values.Where(u => u.Operator == name).ToArray();
                    if (origins.Length == 1)
                    {
                        var possible = before.Values.Where(u => u.Tier == origins[0].Tier).ToArray();
                        if (possible.Length == 2)
                        {
                            foreach (var u in possible)
                            {
                                _units[u.Cell] = _units[u.Cell] with { Operator = null, Source = null };
                                Add(o.At, "move", null, u.Cell, possible.Single(p => p.Cell != u.Cell).Cell);
                            }
                        }
                        // TODO(design): 三名以上同阶且占用不变时，角标无法定位交换双方；当前清除该阶身份，不编造移动事件。
                        else if (possible.Length > 2)
                            foreach (var u in possible) _units[u.Cell] = _units[u.Cell] with { Operator = null, Source = null };
                    }
                }
            }
        }
        _drag = null; _beforeDrag = null;
        if (!_battle)
        {
            foreach (var c in removed) Add(o.At, "lost", before[c].Operator, c, null);
            foreach (var c in added.Where(c => c.Zone == "bench")) _gains.Add((o.At, c));
            if (o.Shop is { } shop && _shop is { } previous)
                foreach (var (slot, cardName) in previous)
                    if (!shop.ContainsKey(slot)) _buys.Add((o.At, slot, cardName));
            foreach (var buy in _buys.ToArray())
            {
                if (o.Shop?.GetValueOrDefault(buy.Slot) == buy.Name)
                { _buys.Remove(buy); continue; }
                var candidates = _gains.Where(g => Math.Abs((g.At - buy.At).TotalSeconds) <= 1.6 && _units.ContainsKey(g.Cell)).ToArray();
                int i = candidates.Length == 1 ? _gains.IndexOf(candidates[0]) : -1;
                if (i >= 0)
                {
                    var c = _gains[i].Cell;
                    _units[c] = _units[c] with { Operator = buy.Name, Source = "purchase" };
                    Add(buy.At, "buy", buy.Name, null, c); _gains.RemoveAt(i); _buys.Remove(buy);
                }
                else if ((o.At - buy.At).TotalSeconds > 1.6)
                { Add(buy.At, "buy", buy.Name, null, null); _buys.Remove(buy); }
            }
            foreach (var gain in _gains.Where(g => (o.At - g.At).TotalSeconds > 1.6).ToArray())
            { Add(gain.At, "gain", null, null, gain.Cell); _gains.Remove(gain); }
        }
        _battle = false; _shop = o.Shop; _money = o.Money ?? _money;
        return Publish(o.MapId ?? State?.MapId, o.Round, o.At);
    }

    private void Add(DateTimeOffset at, string kind, string? name, FieldCell? from, FieldCell? to)
    {
        _log.Add(new(at.ToString("O"), kind, name, from, to));
        _log.Sort((a, b) => string.CompareOrdinal(b.At, a.At));
        if (_log.Count > 20) _log.RemoveRange(20, _log.Count - 20);
    }

    private bool Publish(string? map, int? round, DateTimeOffset at)
    {
        var units = _units.Values.OrderBy(u => u.Zone == "bench" ? 0 : 1).ThenBy(u => u.Row).ThenBy(u => u.Col).ToArray();
        if (State is { } old && old.MapId == map && old.Round == round && old.Units.SequenceEqual(units) && old.Log.SequenceEqual(_log)) return false;
        State = new(map, round, at.ToString("O"), units, _log.ToArray());
        return true;
    }
}
