using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;

namespace ASPAssistant.Core.Services;

/// <summary>只消费观测，不接触截图、OCR、时钟或存储。</summary>
public sealed partial class FieldTracker
{
    private GarrisonBoardData? _maps;
    private string? _knownMap;
    public void ConfigureMaps(GarrisonBoardData maps) => _maps = maps;

    public FieldState? State { get; private set; }
    private readonly Dictionary<FieldCell, FieldUnit> _units = new();
    private readonly Dictionary<FieldCell, FieldSummon> _summons = new();
    private SummonDefinition[] _summonDefinitions = [];
    public void ConfigureSummons(SummonDefinition[] definitions) => _summonDefinitions = definitions;
    // 尚未被占用识别证实的手动添加，不参与漏读过期。
    private readonly HashSet<FieldCell> _manualOnly = [];
    private readonly List<FieldEvent> _log = [];
    private const double GainWindowSeconds = 6;
    private readonly List<(DateTimeOffset At, FieldCell Cell)> _gains = [];
    private string? _drag;
    private FieldEquipment[]? _dragEquipment;
    private Dictionary<FieldCell, FieldUnit>? _beforeDrag;
    private int? _money, _dragMoney;
    private bool _battle;

    public void Reset()
    {
        ResetIdentity();
        State = null; _knownMap = null; _units.Clear(); _log.Clear();
        _gains.Clear(); _drag = null; _dragEquipment = null; _beforeDrag = null;
        ResetTriggers();
        _money = _dragMoney = null; _battle = false;
        _manualOnly.Clear();
        _summons.Clear();
    }

    public bool SetOperator(FieldCell cell, string? name, DateTimeOffset at)
    {
        if (!(cell.Zone == "bench" && cell.Col is >= 1 and <= 10 && cell.Row == 0
            || cell.Zone == "board" && cell.Col is >= 0 and <= 6 && cell.Row is >= 0 and <= 4))
            throw new ArgumentException("格坐标无效。");
        if (cell.Zone == "board" && _maps?.Find(_knownMap ?? State?.MapId ?? "") is { } map
            && !GarrisonMap.CanDeploy(map.At(new(cell.Col, cell.Row)), false))
            throw new ArgumentException("这格不能放置干员。");
        if (name is null && _manualOnly.Remove(cell))
        {
            _units.Remove(cell);
            return Publish(State?.MapId, State?.Round, at);
        }
        if (!_units.TryGetValue(cell, out var unit))
        {
            if (name is null) throw new ArgumentException("这格还没有识别到干员。");
            unit = new(cell.Zone, cell.Col, cell.Row, _identityOperators.Single(o => o.Name == name).Tier, null, null);
            _manualOnly.Add(cell);
            _missing.Remove(cell); _pictures.Remove(cell);
            _gainSources.Remove(cell);
        }
        _units[cell] = unit with { Operator = name, Source = name is null ? null : "manual", Status = name is null ? "unknown" : "sure", Candidates = [] };
        ConfirmIdentity(cell, name, at);
        return Publish(State?.MapId, State?.Round, at);
    }

    public bool Observe(FieldObservation o, bool recognize = true, bool force = false)
    {
        if (!o.IsRest) { _battle = true; _drag = null; _dragEquipment = null; _beforeDrag = null; ResetTriggers(); _gains.Clear(); return false; }
        _knownMap = o.MapId ?? _knownMap;
        bool pruned = false;
        if (_knownMap is { } mapId && _maps?.Find(mapId) is { } map)
        {
            bool Blocked(FieldCell c) => c.Zone == "board" && !GarrisonMap.CanDeploy(map.At(new(c.Col, c.Row)), false);
            // 禁放格是确定的空格，不能按“本帧不可见”保留或恢复旧人。
            foreach (var cell in _units.Keys.Concat(_missing.Keys).Where(Blocked).Distinct().ToArray())
            {
                pruned |= _units.Remove(cell);
                _manualOnly.Remove(cell);
                _missing.Remove(cell); _pictures.Remove(cell);
                _beforeDrag?.Remove(cell);
                _gains.RemoveAll(g => g.Cell == cell);
            }
            foreach (var cell in _summons.Keys.Where(Blocked).ToArray()) pruned |= _summons.Remove(cell);
            o = o with { Cells = o.Cells.Where(p => !Blocked(p.Key)).ToDictionary(p => p.Key, p => p.Value) };
        }
        if (pruned) Publish(_knownMap, o.Round, o.At);
        var preDragCells = new Dictionary<FieldCell, int>(_lastCells);
        bool triggered = ObserveTriggers(o) | force | pruned;
        triggered |= _manualOnly.Any(c => o.Cells.GetValueOrDefault(c) > 0);
        if (!recognize)
        {
            ObserveOccupancy(o, triggered);
            return pruned;
        }
        if (o.DragOperator is { } drag)
        {

            foreach (var cell in _missing.Where(p => p.Value.Unit.Operator == drag).Select(p => p.Key).ToArray())
                _missing.Remove(cell);
            if (_drag is null)
            {
                _beforeDrag = new(_units);
                // 拖动前已观测到的占用变化不能归给随后开始的拖动。
                foreach (var (cell, tier) in preDragCells)
                {
                    if (tier == 0 && !_manualOnly.Contains(cell)) _beforeDrag.Remove(cell);
                    else if (tier == 0) continue;
                    else if (!_beforeDrag.TryGetValue(cell, out var unit) || unit.Tier != tier)
                        _beforeDrag[cell] = new(cell.Zone, cell.Col, cell.Row, tier, null, null);
                }
                _dragMoney = _money;
            }
            _drag = drag;
            if (o.Equipment is not null) _dragEquipment = o.Equipment;
            return pruned;
        }
        if (!o.MainScreen)
        {
            CaptureEquipmentGain(o, _units.Values);

            return (ApplyEquipment(o) && Publish(State?.MapId, o.Round, o.At)) || pruned;
        }
        if (o.Summons is not null)
        {
            foreach (var cell in o.Cells.Keys) _summons.Remove(cell);
            foreach (var summon in o.Summons)
                _summons[new(summon.Zone, summon.Col, summon.Row)] = summon;
        }
        if (!triggered)
        {
            return o.Summons is not null && Publish(o.MapId ?? State?.MapId, o.Round, o.At);
        }
        RestoreMissing(o);
        var before = _beforeDrag ?? new(_units);
        var dragged = _drag;
        var removed = before.Keys.Where(c => o.Cells.TryGetValue(c, out int t) && t == 0).ToList();
        var added = o.Cells.Where(p => p.Value > 0 && !before.ContainsKey(p.Key)).Select(p => p.Key).ToList();
        foreach (var (cell, tier) in o.Cells)
        {
            if (tier == 0) { if (!_manualOnly.Contains(cell)) _units.Remove(cell); }
            else
            {
                _manualOnly.Remove(cell);
                if (!_units.TryGetValue(cell, out var old) || old.Tier != tier)
                    _units[cell] = new(cell.Zone, cell.Col, cell.Row, tier, null, null);
            }
        }
        if (!_battle && _drag is { } name)
        {
            if (removed.Count == 1 && added.Count == 1
                && !(o.Money > _dragMoney)
                && before[removed[0]].Tier == _units[added[0]].Tier
                && DragMatchesTier(name, _units[added[0]].Tier, before))
            {
                var from = removed[0]; var to = added[0];
                _units[to] = _units[to] with { Operator = name, Source = "drag", Status = before[from].Status == "sure" ? "sure" : before[from].Status == "auto" ? "auto" : "pending", Candidates = before[from].Candidates,
                    Elite = before[from].Operator == name ? before[from].Elite : null, Equipment = _dragEquipment ?? before[from].Equipment };
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
                        _units[to] = _units[to] with { Operator = known ? u.Operator : null, Source = known ? u.Source : null, Status = known ? u.Status : "unknown", Candidates = known ? u.Candidates : [], Elite = known ? u.Elite : null, Equipment = known ? u.Equipment : null };
                        Add(o.At, "move", known ? u.Operator : null, from, to);
                    }
                    var origin = changed.Where(c => before[c].Operator == name).ToArray();
                    if (known && origin.Length == 1 && DragMatchesTier(name, before[origin[0]].Tier, before))
                    {
                        var to = origin[0] == a ? b : a;
                        _units[to] = _units[to] with { Operator = name, Source = "drag", Status = before[origin[0]].Status, Candidates = before[origin[0]].Candidates, Equipment = _dragEquipment ?? _units[to].Equipment };
                    }
                }
                else
                {
                    // 同阶换位与原位取消无法仅凭角标区分；清除可能被交换的身份。
                    var origins = before.Values.Where(u => u.Operator == name).ToArray();
                    if (origins.Length == 1)
                    {
                        var possible = before.Values.Where(u => u.Tier == origins[0].Tier).ToArray();
                        if (possible.Length == 1 && _dragEquipment is not null)
                            _units[origins[0].Cell] = _units[origins[0].Cell] with { Equipment = _dragEquipment };
                        else if (possible.Length == 2)
                        {
                            foreach (var u in possible)
                            {
                                _units[u.Cell] = _units[u.Cell] with { Operator = null, Source = null, Status = "unknown", Candidates = [], Equipment = null };
                                Add(o.At, "move", null, u.Cell, possible.Single(p => p.Cell != u.Cell).Cell);
                            }
                        }
                        // TODO(design): 三名以上同阶且占用不变时，角标无法定位交换双方；当前清除该阶身份，不编造移动事件。
                        else if (possible.Length > 2)
                            foreach (var u in possible) _units[u.Cell] = _units[u.Cell] with { Operator = null, Source = null, Status = "unknown", Candidates = [], Equipment = null };
                    }
                }
            }
        }
        _moved.Clear();
        CaptureGainContext(o, before, dragged);
        _drag = null; _dragEquipment = null; _beforeDrag = null;
        HashSet<FieldCell> merged;
        if (!_battle)
        {
            _gains.RemoveAll(g => !_units.ContainsKey(g.Cell));
            foreach (var c in added.Where(c => c.Zone == "bench"))
            {
                _gains.RemoveAll(g => g.Cell == c);
                _gains.Add((o.At, c));
            }

            merged = PairMoves(o, before, removed, added);
            foreach (var c in removed) Add(o.At, "lost", before[c].Operator, c, null);
            foreach (var gain in _gains.Where(g => State is not null || o.GainSource is not null).ToArray())
            {
                if (!_log.Any(e => e.Kind == "gain" && e.To == gain.Cell && e.At == gain.At.ToString("O")))
                    Add(gain.At, "gain", null, null, gain.Cell);
                if ((o.At - gain.At).TotalSeconds > GainWindowSeconds) _gains.Remove(gain);
            }
        }

        else merged = PairMoves(o, before, removed, added);

        // 仅在原格升一阶且同名两份消失时，沿用原格装备；不改变既有身份判定。
        foreach (var (cell, unit) in _units.ToArray())
            if (before.TryGetValue(cell, out var old) && old.Operator is not null && unit.Tier == old.Tier + 1
                && before.Values.Count(u => u.Cell != cell && u.Operator == old.Operator && u.Tier == old.Tier
                    && o.Cells.TryGetValue(u.Cell, out int tier) && tier == 0) == 2)
                _units[cell] = unit with { Equipment = old.Equipment };
        ResolveIdentity(o, before, force, merged);
        ApplyEquipment(o);
        _battle = false; _money = o.Money ?? _money;
        bool published = Publish(o.MapId ?? State?.MapId, o.Round, o.At);
        if (force && State is not null) State = State with { UpdatedAt = o.At.ToString("O") };
        return published || force || pruned;
    }

    private bool ApplyEquipment(FieldObservation o)
    {
        if (o.PanelOperator is null || o.Equipment is null) return false;
        var matches = _units.Values.Where(u => u.Operator == o.PanelOperator).ToArray();
        // 没有选中格的可靠证据时，同名多份不写。
        if (matches.Length != 1) return false;
        var unit = matches[0];
        if (unit.Equipment?.SequenceEqual(o.Equipment) == true) return false;
        _units[unit.Cell] = unit with { Equipment = o.Equipment };
        return true;
    }

    private void Add(DateTimeOffset at, string kind, string? name, FieldCell? from, FieldCell? to)
    {
        if (from is not null && kind is "move" or "sell")
        {
            _missing.Remove(from);
            if (_manualOnly.Remove(from)) _units.Remove(from);
        }
        TrackRosterEvent(kind, name);
        _log.Add(new(at.ToString("O"), kind, name, from, to));
        _log.Sort((a, b) => string.CompareOrdinal(b.At, a.At));
        if (_log.Count > 20) _log.RemoveRange(20, _log.Count - 20);
    }

    private static bool SameUnits(FieldUnit[] left, FieldUnit[] right) => left.Length == right.Length
        && left.Zip(right).All(p => (p.First with { Candidates = p.Second.Candidates, Equipment = p.Second.Equipment }) == p.Second
            && p.First.Candidates.SequenceEqual(p.Second.Candidates)
            && (p.First.Equipment ?? []).SequenceEqual(p.Second.Equipment ?? []));

    private bool Publish(string? map, int? round, DateTimeOffset at)
    {
        var units = _units.Values.OrderBy(u => u.Zone == "bench" ? 0 : 1).ThenBy(u => u.Row).ThenBy(u => u.Col).ToArray();
        var summons = _summons.Values.Where(s => !units.Any(u => u.Zone == s.Zone && u.Col == s.Col && u.Row == s.Row))
            .Select(s => {
                var owners = _summonDefinitions.Where(d => d.Name == s.Name).SelectMany(d => d.Owners)
                    .Where(name => units.Any(u => u.Operator == name && u.Status is "sure" or "auto")).Distinct().ToArray();
                return s with { Owner = owners.Length == 1 ? owners[0] : null };
            }).OrderBy(s => s.Zone).ThenBy(s => s.Row).ThenBy(s => s.Col).ToArray();
        if (State is { } old && old.MapId == map && old.Round == round && SameUnits(old.Units, units) && old.Log.SequenceEqual(_log) && old.Roster.SequenceEqual(_roster.Values)
            && (old.Summons ?? []).SequenceEqual(summons)) return false;
        State = new(map, round, at.ToString("O"), units, _log.ToArray()) { Roster = _roster.Values.ToArray(), Summons = summons };
        return true;
    }
}
