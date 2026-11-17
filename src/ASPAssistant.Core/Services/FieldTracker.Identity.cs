using ASPAssistant.Core.Models;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

public sealed partial class FieldTracker
{
    private HashSet<string> _bannedCandidates = [];
    private HashSet<string>? _customCandidates;

    // 仅控制身份候选，不向公开的本局名单添加干员。
    public void ConfigureCandidatePool(IEnumerable<string> banned, IEnumerable<string>? custom)
    { _bannedCandidates = banned.ToHashSet(); _customCandidates = custom?.ToHashSet(); }

    private IEnumerable<string> MatchCandidates() => _identityOperators
        .Where(p => (p.Obtain is null || _strategyName is null
                && Regex.IsMatch(p.Obtain, "策略「(.+?)」第\\s*(\\d+)\\s*回合赠送"))
            && !_bannedCandidates.Contains(p.Name)
            && (!p.Diy || _customCandidates is null || _customCandidates.Contains(p.Name)))
        .Select(p => p.Name).Concat(_roster.Where(r => r.Value.Source is "strategy" or "equipment" or "ethan" or "dispatch"
            || !_bannedCandidates.Contains(r.Key) && (_customCandidates is null
                || !_identityOperators.Any(p => p.Name == r.Key && p.Diy) || _customCandidates.Contains(r.Key)))
            .Select(r => r.Key));

    public const double MinimumRosterScore = .35;
    public const double SameSpriteScore = .92;
    private readonly Dictionary<string, FieldRosterEntry> _roster = new(StringComparer.Ordinal);
    private readonly Dictionary<FieldCell, FieldSpritePatch> _pictures = [];
    private SpriteCandidate[] RankCell(FieldCell cell, FieldSpritePatch patch, int tier, bool force, string[] names)
    {
        var sources = (_gainContexts.GetValueOrDefault(cell)?.Names ?? [])
            .Concat(_purchaseCandidates.GetValueOrDefault(cell) ?? [])
            .Concat(_units.GetValueOrDefault(cell)?.Operator is { } name ? [name] : []);
        return Rank(patch, tier, force, names, sources);
    }
    private readonly Dictionary<FieldCell, FieldSpritePatch> _anchors = [];
    private readonly List<(FieldUnit Unit, FieldSpritePatch Patch, bool Forced)> _learned = [];
    private readonly HashSet<(string Name, int Tier)> _confirmed = [];
    private readonly HashSet<string> _playerConfirmed = [];
    private readonly Dictionary<FieldCell, (FieldUnit Unit, FieldSpritePatch? Patch, DateTimeOffset At, int Scans)> _missing = [];
    private readonly HashSet<FieldCell> _returned = [];
    private readonly HashSet<FieldCell> _moved = [];
    private readonly Dictionary<FieldCell, (FieldUnit Unit, FieldSpritePatch Patch)> _history = [];
    private readonly Dictionary<FieldCell, string> _gainSources = [];
    private readonly HashSet<FieldCell> _untrackedGains = [];
    private IReadOnlyList<Operator> _identityOperators = [];
    private Func<FieldSpritePatch, int, string[], SpriteMatch>? _matchSprite;
    private SpriteModelMatcher? _spriteModel;
    private Func<FieldSpritePatch, IEnumerable<(FieldUnit Unit, FieldSpritePatch Patch)>, FieldSpritePatch>? _occlusion;
    private Func<FieldSpritePatch, int, string[], FieldSpritePatch>? _neighborMask;
    private double IdentityAutoFillGap => _spriteModel is null ? SpriteMatcher.AutoFillGap : SpriteModelMatcher.AutoFillGap;
    private string? _strategyName;
    private sealed record GainContext(string Source, string[]? Names);
    private readonly Dictionary<FieldCell, GainContext> _gainContexts = [];
    private (DateTimeOffset At, GainContext Context)? _recentGift;

    public void ConfigureIdentity(IReadOnlyList<Operator> operators,
        Func<FieldSpritePatch, int, string[], SpriteMatch> match, SpriteModelMatcher? model = null,
        Func<FieldSpritePatch, IEnumerable<(FieldUnit Unit, FieldSpritePatch Patch)>, FieldSpritePatch>? occlusion = null,
        Func<FieldSpritePatch, int, string[], FieldSpritePatch>? neighborMask = null)
    { _identityOperators = operators; _matchSprite = match; _spriteModel = model; _occlusion = occlusion; _neighborMask = neighborMask; }

    public void SeedRoster(string? strategyName, IEnumerable<string> custom, int? round, DateTimeOffset at)
    {
        _strategyName = strategyName;
        foreach (var op in _identityOperators)
        {
            var gift = Regex.Match(op.Obtain ?? "", "策略「(.+?)」第\\s*(\\d+)\\s*回合赠送");
            if (gift.Success && gift.Groups[1].Value == strategyName && round >= int.Parse(gift.Groups[2].Value))
                AddRoster(op.Name, "strategy", at);
        }
        foreach (string name in custom) AddRoster(name, "custom", at);
    }

    public void AddRoster(string name, string source, DateTimeOffset at)
    {
        if (!_roster.ContainsKey(name)) _roster[name] = new(name, source, at.ToString("O"), false);
    }

    private void ResetIdentity()
    { _bannedCandidates.Clear(); _customCandidates = null; _roster.Clear(); _pictures.Clear(); _anchors.Clear(); _learned.Clear(); _confirmed.Clear(); _playerConfirmed.Clear(); _missing.Clear(); _returned.Clear(); _moved.Clear(); _history.Clear(); _gainSources.Clear(); _untrackedGains.Clear(); _gainContexts.Clear(); _recentGift = null; _strategyName = null; }

    // 在购买配对前保存来源证据；购买记录和场上身份分别处理。
    private void CaptureGainContext(FieldObservation o, Dictionary<FieldCell, FieldUnit> before, string? dragged)
    {
        var board = before.Values.Where(u => u.Zone == "board").ToArray();
        var removed = before.Values.Where(u => o.Cells.GetValueOrDefault(u.Cell, -1) == 0).ToArray();
        bool ethan = _log.Any(e => e.Kind == "sell" && e.Operator == "伊桑" && e.At == o.At.ToString("O"))
            || dragged == "伊桑" && o.Money > _dragMoney && removed.Any(u => u.Operator == "伊桑");
        bool mechanic = _battle && board.Any(u => u.Operator == "机械师"
            && (u.Elite == true || board.Count(v => v.Row == u.Row) >= 3));
        if (ethan || mechanic)
        {
            var names = board.Where(u => !ethan || !removed.Contains(u)).Select(u => u.Operator).Distinct().ToHashSet();
            var counts = _identityOperators.Where(p => names.Contains(p.Name))
                .SelectMany(p => p.CoreCovenants.Concat(p.AdditionalCovenants).Distinct())
                .GroupBy(c => c).ToArray();
            var covenants = counts.Where(g => g.Count() == counts.Max(c => c.Count())).Select(g => g.Key).ToHashSet();
            var candidates = _identityOperators.Where(p => (!ethan || p.Name != "伊桑")
                && p.CoreCovenants.Concat(p.AdditionalCovenants).Any(covenants.Contains)).Select(p => p.Name).ToArray();
            // 没有读到场上盟约时保留同阶全集，不把空证据当成排除所有人。
            // TODO(design): 机械师没有独立来源值，暂归 strategy；需要区分时扩展来源定义。
            _recentGift = (o.At, new(ethan ? "ethan" : "strategy", candidates.Length == 0 ? null : candidates));
        }
        else if (_battle && _strategyName == "得闲饮茶" && o.Round == 3)
            _recentGift = (o.At, new("strategy", _identityOperators.Where(p => p.Tier is 2 or 4).Select(p => p.Name).ToArray()));

        if (!ethan && !mechanic) CaptureEquipmentGain(o, before.Values);

        foreach (var unit in _units.Values.Where(u => u.Zone == "bench" && !before.ContainsKey(u.Cell)))
        {
            GainContext context = _recentGift is {} recent && (o.At - recent.At).TotalSeconds <= 3
                ? recent.Context : StrategyGain(o, before, removed);
            if (o.GainSource is not null) context = context with { Source = o.GainSource };
            _gainContexts[unit.Cell] = context;
            _gainSources[unit.Cell] = context.Source;
            if (_battle) _untrackedGains.Add(unit.Cell);
        }
    }

    private void CaptureEquipmentGain(FieldObservation o, IEnumerable<FieldUnit> units)
    {
        var used = units.SelectMany(u => (u.Equipment ?? []).Where(e => e.Name is "简易通讯机" or "寻呼模块" or "信标")
            .Where(e => o.Cells.GetValueOrDefault(u.Cell, -1) == 0 || o.PanelOperator == u.Operator
                && o.Equipment is not null && !o.Equipment.Any(n => n.Name == e.Name))
            .Select(e => (Unit: u, Equipment: e))).FirstOrDefault();
        if (used.Unit is null) return;
        bool choice = used.Equipment.Name is "寻呼模块" or "信标";
        var op = _identityOperators.FirstOrDefault(p => p.Name == used.Unit.Operator);
        string? covenant = op?.CoreCovenants.Concat(op.AdditionalCovenants).FirstOrDefault();
        var names = o.Shop?.Values.Distinct().ToArray();
        if (!choice || names is not { Length: > 0 })
            names = covenant is null || used.Equipment.Name == "信标" ? null
                : _identityOperators.Where(p => p.CoreCovenants.Contains(covenant) || p.AdditionalCovenants.Contains(covenant)).Select(p => p.Name).ToArray();
        _recentGift = (o.At, new(choice ? "dispatch" : "equipment", names));
    }

    private GainContext StrategyGain(FieldObservation o, Dictionary<FieldCell, FieldUnit> before, FieldUnit[] removed)
    {
        // TODO(design): 观测没有调度等级和供选模式；目前按已读阶数、卡名和策略缩候选。
        string? covenant = _strategyName switch
        {
            "人才盲盒" => "维多利亚", "团伙行动" => "叙拉古", "猎人战法" => "阿戈尔",
            "骑士团长" => "卡西米尔", "流动乐章" => "莱塔尼亚", "无惧风雪" => "乌萨斯", "黑白相易" => "炎", _ => null
        };
        if (covenant is not null)
            return new("strategy", _identityOperators.Where(p => (p.CoreCovenants.Contains(covenant) || p.AdditionalCovenants.Contains(covenant))
                && (_strategyName != "黑白相易" || removed.Length == 0 || p.Tier <= removed.Max(u => u.Tier))).Select(p => p.Name).ToArray());
        if (_strategyName == "幻画为真") return new("strategy", before.Values.Where(u => u.Operator is not null).Select(u => u.Operator!).Distinct().ToArray());
        if (_strategyName is "纸片替身" or "不稳定要素")
            return new("strategy", removed.Length == 0 ? null : _identityOperators.Where(p => removed.Any(u => p.Tier == u.Tier + (_strategyName == "不稳定要素" ? 1 : 0))).Select(p => p.Name).ToArray());
        if (_strategyName is "最佳品味" or "打包速递" or "替身娃娃")
        {
            var cards = _shop?.Where(p => o.Shop is not null && !o.Shop.ContainsKey(p.Key)).Select(p => p.Value).Distinct().ToArray();
            return new("strategy", cards is { Length: > 0 } ? cards : null);
        }
        if (_strategyName is "集结指示" or "协力共进")
        {
            var cards = o.Shop?.Values.Distinct().ToArray();
            return new("dispatch", cards is { Length: > 0 } ? cards : null);
        }
        // 固定赠送也属于来源候选，只在本回合进入休整期时使用。
        var gifts = _identityOperators.Where(p => Regex.IsMatch(p.Obtain ?? "", $"策略「{Regex.Escape(_strategyName ?? "")}」第\\s*{o.Round}\\s*回合赠送")).Select(p => p.Name).ToArray();
        return _battle && gifts.Length > 0 ? new("strategy", gifts) : new("manual", null);
    }

    private void TrackRosterEvent(DateTimeOffset at, string kind, string? name, FieldCell? to)
    {
        if (kind == "gain" && to is not null)
        {
            _untrackedGains.Add(to);
            // 未知来源保留待确认锚点；有来源范围或本阶唯一推断时重新核实。
            if (_units.GetValueOrDefault(to)?.Source == "only"
                || _gainContexts.GetValueOrDefault(to)?.Names is not null) _anchors.Remove(to);
        }
        if (name is null) return;
        if (kind == "buy")
        {
            AddRoster(name, "purchase", at);
            _roster[name] = _roster[name] with { Sold = false };
        }
        if (kind == "sell" && _roster.TryGetValue(name, out var item))
            _roster[name] = item with { Sold = !_units.Values.Any(u => u.Operator == name) };
    }

    private void ConfirmIdentity(FieldCell cell, string? name, DateTimeOffset at)
    {
        if (name is null)
        {
            if (_pictures.TryGetValue(cell, out var unknown)) _anchors[cell] = unknown;
            return;
        }
        _untrackedGains.Remove(cell);
        _playerConfirmed.Add(name);
        _gains.RemoveAll(g => g.Cell == cell);
        AddRoster(name, _gainSources.GetValueOrDefault(cell, "manual"), at);
        _roster[name] = _roster[name] with { Sold = false };
        if (_pictures.TryGetValue(cell, out var patch))
        {
            _anchors[cell] = patch;
            // 手动纠正同时替换相似图块的旧自动身份。
            _learned.RemoveAll(p => p.Unit.Tier == _units[cell].Tier
                && SpriteSimilarity(p.Patch, patch) >= SameSpriteScore);
            Remember(_units[cell], patch);
            _history[cell] = (_units[cell], patch);
        }
    }

    public static double SpriteSimilarity(FieldSpritePatch a, FieldSpritePatch b)
    {
        double ma = a.Pixels.Average(x => (double)x), mb = b.Pixels.Average(x => (double)x);
        double xy = 0, xx = 0, yy = 0, delta = 0;
        for (int i = 0; i < a.Pixels.Length; i++)
        {
            double x = a.Pixels[i] - ma, y = b.Pixels[i] - mb;
            xy += x * y; xx += x * x; yy += y * y;
            delta += Math.Abs(a.Pixels[i] - b.Pixels[i]);
        }
        double color = 1 - delta / (a.Pixels.Length * 255);
        double corr = xx * yy > 0 ? xy / Math.Sqrt(xx * yy) : color;
        return .75 * corr + .25 * color;
    }

    private void ResolveIdentity(FieldObservation o, Dictionary<FieldCell, FieldUnit> before, bool force)
    {
        if (o.Sprites is null || _matchSprite is null) return;
        var gone = before.Values.Where(u => o.Cells.GetValueOrDefault(u.Cell, -1) == 0 && !_manualOnly.Contains(u.Cell)).ToList();

        var ordered = o.Sprites.OrderByDescending(p => _occlusion is null ? 0 : p.Value.Geometry?.BadgeFootY ?? 0).ToArray();
        int rowEnd = 0;
        foreach (var (cell, rawPatch) in ordered)
        {
            int index = Array.FindIndex(ordered, p => p.Key == cell);
            if (force && index == rowEnd)
            {
                rowEnd = index + 1;
                while (rowEnd < ordered.Length && ordered[rowEnd].Key.Zone == cell.Zone && ordered[rowEnd].Key.Row == cell.Row) rowEnd++;
                PrepareRow(o, ordered[index..rowEnd]);
            }
            if (!_units.TryGetValue(cell, out var unit)) continue;
            // 异阶尚未确认时不学习这一帧，避免把换入者的图块记到旧身份下。
            if (_tierChanges.ContainsKey(cell)) continue;
            var patch = _occlusion?.Invoke(rawPatch, _units.Values.Where(u => u.Cell != cell && o.Sprites.ContainsKey(u.Cell))
                .Select(u => (u, o.Sprites[u.Cell]))) ?? rawPatch;
            before.TryGetValue(cell, out var old);
            // 强制识别只保留直接证据的确定身份，其余重新使用冻结模板比较。
            bool reidentify = force && !(unit.Status == "sure" && unit.Source is "purchase" or "drag" or "manual");
            _pictures[cell] = patch;
            if (!reidentify && _moved.Contains(cell)) { SetIdentity(cell, unit, patch, force: force); continue; }
            bool arrival = old is null;
            bool unexplainedGain = !reidentify && _untrackedGains.Contains(cell);
            var context = reidentify ? null : _gainContexts.GetValueOrDefault(cell);
            if (arrival && !_gainContexts.ContainsKey(cell)) _gainSources[cell] = o.GainSource ?? "manual";
            // 2. 本次事件已由商店卡或拖动面板确定身份。
            bool direct = unit.Operator is not null && (unit.Source == "purchase" || unit.Source == "drag")
                && (old?.Operator != unit.Operator || old.Source != unit.Source || arrival);
            if (!reidentify && direct)
            {
                SetIdentity(cell, unit, patch, force: force);
                AddRoster(unit.Operator!, unit.Source == "purchase" ? "purchase" : "manual", o.At);
                continue;
            }
            // 两次空扫描或三秒内回到原格，连待确认候选也原样保留。
            if (!reidentify && _returned.Contains(cell) && old is not null && unit.Operator == old.Operator && unit.Source == old.Source)
            { SetIdentity(cell, old, patch, force: force); continue; }
            // 同格连续占用且没有事件改写身份时，动画和镜头变化不使确定身份降级。
            // 延迟发现的无卡新增仍需推翻“本阶唯一”的推断。
            if (!reidentify && old is not null && old.Tier == unit.Tier && unit.Operator == old.Operator && unit.Source == old.Source
                && old.Status is "sure" or "auto" && !(_untrackedGains.Contains(cell)
                    && (old.Source == "only" || !_anchors.ContainsKey(cell))))
            { SetIdentity(cell, old, patch, force: force); continue; }
            // 待确认仍使用固定锚点，避免候选顺序随待机动画抖动。
            if (!reidentify && old is not null && old.Tier == unit.Tier && unit.Operator == old.Operator && unit.Source == old.Source
                && _anchors.TryGetValue(cell, out var anchor) && SpriteSimilarity(anchor, patch) >= SameSpriteScore)
            { SetIdentity(cell, old, patch, false, force); continue; }
            // 4. 原格升阶且同名同阶的两份同时消失。
            if (!reidentify && old?.Operator is not null && old.Status is "sure" or "auto" && unit.Tier == old.Tier + 1
                && gone.Count(u => u.Operator == old.Operator && u.Status is "sure" or "auto" && u.Tier == old.Tier) == 2)
            {
                SetIdentity(cell, unit with { Operator = old.Operator, Source = "merge", Status = old.Status == "sure" && gone.Where(u => u.Operator == old.Operator && u.Tier == old.Tier).All(u => u.Status == "sure") ? "sure" : "auto", Candidates = old.Candidates, Elite = true }, patch, force: force);
                continue;
            }
            var names = MatchCandidates()
                .Distinct().Where(n => HasTier(n, unit.Tier)).OrderBy(n => _roster.GetValueOrDefault(n)?.Sold ?? false).ToArray();
            var prototype = _unknownPrototype?.Invoke(patch, unit.Tier, names) ?? [];
            if (prototype.Length > 0)
            {
                SetIdentity(cell, unit with { Operator = prototype[0].Name, Source = "sprite", Status = "pending",
                    Candidates = prototype.Select(p => new FieldCandidate(p.Name, p.Score)).ToArray() }, patch, save: false, force: force);
                continue;
            }
            // 跨扫描挪格、短暂漏读及换镜头后，先找本局已确认或自动认出的个人图块。
            var remembered = _learned.Where(p => p.Patch.OcclusionRatio < SpriteMatcher.OcclusionLearningThreshold
                && patch.OcclusionRatio < SpriteMatcher.OcclusionLearningThreshold && p.Unit.Tier == unit.Tier
                && SpriteMatcher.MatchPersonal(p.Patch, patch) >= SameSpriteScore).ToArray();
            if (!reidentify && !_purchaseCandidates.ContainsKey(cell) && remembered.Select(p => p.Unit.Operator).Distinct().Count() == 1)
            {
                var known = remembered.OrderByDescending(p => p.Unit.Status == "sure").First();
                SetIdentity(cell, known.Unit with { Source = "carry" }, patch, force: force);
                _untrackedGains.Remove(cell); _gains.RemoveAll(g => g.Cell == cell);
                continue;
            }
            if (!reidentify && old is null && _history.TryGetValue(cell, out var previous) && previous.Unit.Status == "pending"
                && previous.Unit.Tier == unit.Tier && SpriteSimilarity(previous.Patch, patch) >= SameSpriteScore)
            { SetIdentity(cell, previous.Unit, previous.Patch, force: force); continue; }
            // 无卡赠送新格不能被“本阶唯一”强填；它可能根本不在本局名单里。
            var active = names.Where(n => !(_roster.GetValueOrDefault(n)?.Sold ?? false)).ToArray();
            // 本阶唯一仍需图像支持；训练侧真名最低分约0.336，门槛取0.30留余量。
            if (!unexplainedGain && !_purchaseCandidates.ContainsKey(cell) && active.Length == 1)
            {
                var score = Rank(patch, unit.Tier, reidentify, active).FirstOrDefault()?.Score ?? 0;
                SetIdentity(cell, unit with { Operator = active[0], Source = "only", Status = score >= .30 ? "auto" : "pending", Candidates = [new(active[0], score)],
                    Elite = unit.Operator == active[0] ? unit.Elite : null }, patch, force: force);
                continue;
            }
            // 赠送等来源照旧扩充候选；低分不重新放入禁用或未选自选干员。
            var pool = names.Concat(context?.Names ?? []).Distinct().Where(n => HasTier(n, unit.Tier)).ToArray();
            var ranked = RankCell(cell, patch, unit.Tier, reidentify, pool);
            bool open = ranked.Length == 0 || ranked.Max(c => c.Score) < MinimumRosterScore;
            // 保留赠送来源和重认当前名字的排序偏好。
            ranked = ranked.OrderByDescending(c => c.Score
                    + (context?.Names?.Contains(c.Name) == true ? .02 : 0)
                    + (reidentify && c.Name == unit.Operator ? .01 : 0)).ToArray();
            var preferred = unexplainedGain ? null : ranked.Take(3).FirstOrDefault(c => (reidentify ? _playerConfirmed.Contains(c.Name) : _confirmed.Contains((c.Name, unit.Tier)))
                && !_units.Values.Any(u => u.Cell != cell && u.Operator == c.Name));
            if (preferred is not null) ranked = new[] { preferred }.Concat(ranked.Where(c => c.Name != preferred.Name)).ToArray();
            var candidates = ranked.Take(3).Select(c => new FieldCandidate(c.Name, c.Score)).ToArray();
            bool confirmedFirst = ranked.Length > 0 && _playerConfirmed.Contains(ranked[0].Name)
                && !_units.Values.Any(u => u.Cell != cell && u.Operator == ranked[0].Name);
            // Rank 已逐名字取个人模板与冻结模板的高分，个人模板领先也用同一条分差判断。
            double gap = confirmedFirst ? IdentityAutoFillGap / 2 : IdentityAutoFillGap;
            bool sourceKnown = unexplainedGain && context?.Names is not null;
            string status = candidates.Length == 0 ? "unknown" : (!open || sourceKnown || preferred is not null || confirmedFirst) && ranked.Length > 1
                && ranked[0].Score - ranked[1].Score >= gap && !FieldConfusionRules.NeedsConfirmation(ranked) ? "auto" : "pending";
            SetIdentity(cell, unit with { Operator = candidates.FirstOrDefault()?.Operator,
                Source = candidates.Length == 0 ? null : "sprite", Status = status, Candidates = candidates,
                Elite = unit.Operator == candidates.FirstOrDefault()?.Operator ? unit.Elite : null }, patch, force: force);
        }
        _preparedRanks.Clear();
        foreach (var u in gone)
        {
            bool consumed = _log.Any(e => e.At == o.At.ToString("O") && e.From == u.Cell && e.Kind is "move" or "sell")
                || before.Values.Any(v => v.Operator == u.Operator && v.Tier == u.Tier
                    && _units.TryGetValue(v.Cell, out var merged) && merged.Source == "merge" && merged.Tier == u.Tier + 1);
            if (!consumed) _missing[u.Cell] = (u, _pictures.GetValueOrDefault(u.Cell), o.At, 1);
            _pictures.Remove(u.Cell); _anchors.Remove(u.Cell); _gainSources.Remove(u.Cell); _gainContexts.Remove(u.Cell); _untrackedGains.Remove(u.Cell);
        }
    }

    private bool DragMatchesTier(string name, int tier, Dictionary<FieldCell, FieldUnit> before)
    {
        var definition = _identityOperators.FirstOrDefault(p => p.Name == name);
        if (definition is not null && tier < definition.Tier) return false;
        return HasTier(name, tier) || before.Values.Any(u => u.Operator == name && u.Tier == tier && u.Status == "sure");
    }

    private void PairMoves(FieldObservation o, Dictionary<FieldCell, FieldUnit> before,
        List<FieldCell> removed, List<FieldCell> added)
    {
        // 三合一消耗的两份不能再分给其他新格。
        var merged = removed.Where(c => before[c].Operator is { } name && before.Values.Any(v =>
            v.Operator == name && v.Tier == before[c].Tier
            && o.Cells.GetValueOrDefault(v.Cell) == v.Tier + 1
            && removed.Count(r => before[r].Operator == name && before[r].Tier == v.Tier) == 2)).ToHashSet();
        // 资金增加提供卖出证据，不把这些消失重新解释为挪格。
        if (!_battle && o.Money > _money)
        {
            foreach (var from in removed.Where(c => !merged.Contains(c) && !_manualOnly.Contains(c)).ToArray())
            {
                Add(o.At, "sell", before[from].Operator, from, null);
                removed.Remove(from);
            }
            foreach (var missing in _missing.Values.ToArray())
                Add(o.At, "sell", missing.Unit.Operator, missing.Unit.Cell, null);
        }
        var origins = removed.Where(c => !merged.Contains(c)).Select(c => before[c])
            .Concat(_missing.Values.Select(m => m.Unit)).DistinctBy(u => u.Cell).ToArray();
        // 同阶全部边按相似度排序，每个起点和终点只用一次；单对无需图像门槛。
        var pairs = (from origin in origins
                     from to in added
                     where origin.Tier == _units[to].Tier
                     let picture = _pictures.GetValueOrDefault(origin.Cell) ?? _missing.GetValueOrDefault(origin.Cell).Patch
                     let target = o.Sprites?.GetValueOrDefault(to)
                     let score = picture is not null && target is not null ? SpriteSimilarity(picture, target) : -2
                     orderby score descending
                     select (From: origin, To: to)).ToArray();
        var used = new HashSet<FieldCell>();
        foreach (var pair in pairs)
        {
            if (used.Contains(pair.From.Cell) || !added.Contains(pair.To)) continue;
            var from = pair.From; var to = pair.To;
            used.Add(from.Cell);
            _units[to] = pair.From with { Zone = to.Zone, Col = to.Col, Row = to.Row };
            _moved.Add(to);
            _purchaseCandidates.Remove(from.Cell);
            _untrackedGains.Remove(to); _gainContexts.Remove(to); _gainSources.Remove(to);
            _gains.RemoveAll(g => g.Cell == from.Cell || g.Cell == to);
            var lostAt = _missing.TryGetValue(from.Cell, out var missing) ? missing.At : o.At;
            _log.RemoveAll(e => e.Kind == "lost" && e.From == from.Cell && e.At == lostAt.ToString("O"));
            Add(o.At, "move", from.Operator, from.Cell, to);
            removed.Remove(from.Cell); added.Remove(to);
        }
        removed.RemoveAll(c => _manualOnly.Contains(c));
        // 没有图块的观测同样保留短暂漏读身份。
        if (o.Sprites is null || _matchSprite is null)
            foreach (var cell in removed.Where(c => !merged.Contains(c)))
                _missing[cell] = (before[cell], _pictures.GetValueOrDefault(cell), o.At, 1);
    }

    private void RestoreMissing(FieldObservation o)
    {
        _returned.Clear();
        foreach (var (cell, missing) in _missing.ToArray())
        {
            int tier = o.Cells.GetValueOrDefault(cell, -1);
            bool within = missing.Scans <= 2 || (o.At - missing.At).TotalSeconds <= 3;
            if (tier > 0 && tier == missing.Unit.Tier && within)
            {
                _units[cell] = missing.Unit;
                if (missing.Patch is { } patch) { _pictures[cell] = patch; _anchors[cell] = patch; }
                _returned.Add(cell);
            }
            if (tier > 0 || !within) _missing.Remove(cell);
            else _missing[cell] = missing with { Scans = missing.Scans + 1 };
        }
    }

    private bool HasTier(string name, int tier) => _identityOperators.Any(o => o.Name == name && o.Tier == tier)
        || _units.Values.Any(u => u.Operator == name && u.Tier == tier && u.Status == "sure")
        || _learned.Any(p => p.Unit.Operator == name && p.Unit.Tier == tier);

    // 只读采集钩子；不参与候选排序或判定。未订阅时不创建采集对象。
    public Action<FieldSpritePatch, SpriteCandidate[], SpriteCandidate[], float[]?>? Compared { get; set; }

    private SpriteCandidate[] Rank(FieldSpritePatch patch, int tier, string[] names) => Rank(patch, tier, false, names);

    private SpriteCandidate[] Rank(FieldSpritePatch patch, int tier, bool force, string[] names)
        => Rank(patch, tier, force, names, []);

    private SpriteCandidate[] Rank(FieldSpritePatch patch, int tier, bool force, string[] names, IEnumerable<string> sources)
    {
        var sourceNames = sources.Where(n => HasTier(n, tier)).Distinct().ToArray();
        if (names.Length == 0 && sourceNames.Length == 0) return [];
        // 先逐名字取个人模板与冻结模板的高分，再统一融合模型概率。
        var personal = _learned.Where(p => p.Patch.OcclusionRatio < SpriteMatcher.OcclusionLearningThreshold
            && patch.OcclusionRatio < SpriteMatcher.OcclusionLearningThreshold && (!force || p.Forced || p.Unit.Status == "sure" && p.Unit.Source is "purchase" or "drag" or "manual") && p.Unit.Tier == tier && (names.Contains(p.Unit.Operator!) || sourceNames.Contains(p.Unit.Operator!)))
            .GroupBy(p => p.Unit.Operator!).ToDictionary(g => g.Key,
                g => g.Max(p => SpriteMatcher.MatchPersonal(p.Patch, patch)));
        var prepared = _preparedRanks.GetValueOrDefault(patch.Image);
        RankResult result;
        if (prepared is not null && prepared.Names.SequenceEqual(names) && prepared.Sources.SequenceEqual(sourceNames)
            && prepared.Personal.Count == personal.Count && personal.All(p => prepared.Personal.GetValueOrDefault(p.Key, double.NaN) == p.Value)
            && SameMask(prepared.Patch.OcclusionMask, patch.OcclusionMask) && SameMask(prepared.Patch.NeighborMask, patch.NeighborMask)
            && (prepared.Patch.NeighborNames ?? []).SequenceEqual(patch.NeighborNames ?? []))
            result = prepared.Result;
        else result = ComputeRank(patch, tier, names, sourceNames, personal);
        Compared?.Invoke(patch, result.Templates, result.Fused, result.Logits);
        return result.Fused.OrderBy(c => _roster.GetValueOrDefault(c.Name)?.Sold ?? false).ThenByDescending(c => c.Score).ToArray();
    }

    private sealed record RankResult(SpriteCandidate[] Templates, SpriteCandidate[] Fused, float[]? Logits);
    private sealed record PreparedRank(FieldSpritePatch Patch, string[] Names, string[] Sources,
        Dictionary<string, double> Personal, RankResult Result);
    private readonly Dictionary<byte[], PreparedRank> _preparedRanks = [];
    private static bool SameMask(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    // 同排只并行纯比对；邻人和个人图块若被前格更新，顺序应用时按实际输入重算。
    private void PrepareRow(FieldObservation o, KeyValuePair<FieldCell, FieldSpritePatch>[] row)
    {
        _preparedRanks.Clear();
        var inputs = new List<(FieldSpritePatch Patch, int Tier, string[] Names, string[] Sources, Dictionary<string, double> Personal)>();
        for (int i = 0; i < row.Length; i++)
        {
            var (cell, raw) = row[i];
            if (!_units.TryGetValue(cell, out var unit) || _tierChanges.ContainsKey(cell)
                || unit.Status == "sure" && unit.Source is "purchase" or "drag" or "manual") continue;
            var patch = _occlusion?.Invoke(raw, _units.Values.Where(u => u.Cell != cell && o.Sprites!.ContainsKey(u.Cell))
                .Select(u => (u, o.Sprites![u.Cell]))) ?? raw;
            var names = MatchCandidates().Distinct().Where(n => HasTier(n, unit.Tier))
                .OrderBy(n => _roster.GetValueOrDefault(n)?.Sold ?? false).ToArray();
            var active = names.Where(n => !(_roster.GetValueOrDefault(n)?.Sold ?? false)).ToArray();
            bool only = !_purchaseCandidates.ContainsKey(cell) && active.Length == 1;
            if (only) names = active;
            var sources = only ? [] : (_gainContexts.GetValueOrDefault(cell)?.Names ?? [])
                .Concat(_purchaseCandidates.GetValueOrDefault(cell) ?? [])
                .Concat(unit.Operator is { } name ? [name] : []).Where(n => HasTier(n, unit.Tier)).Distinct().ToArray();
            if (names.Length == 0 && sources.Length == 0) continue;
            var personal = _learned.Where(p => p.Patch.OcclusionRatio < SpriteMatcher.OcclusionLearningThreshold
                && patch.OcclusionRatio < SpriteMatcher.OcclusionLearningThreshold
                && (p.Forced || p.Unit.Status == "sure" && p.Unit.Source is "purchase" or "drag" or "manual")
                && p.Unit.Tier == unit.Tier && (names.Contains(p.Unit.Operator!) || sources.Contains(p.Unit.Operator!)))
                .GroupBy(p => p.Unit.Operator!).ToDictionary(g => g.Key, g => g.Max(p => SpriteMatcher.MatchPersonal(p.Patch, patch)));
            inputs.Add((patch, unit.Tier, names, sources, personal));
        }
        var results = new PreparedRank[inputs.Count];
        Parallel.For(0, inputs.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
        {
            var p = inputs[i];
            results[i] = new(p.Patch, p.Names, p.Sources, p.Personal, ComputeRank(p.Patch, p.Tier, p.Names, p.Sources, p.Personal));
        });
        foreach (var result in results) if (result is not null) _preparedRanks[result.Patch.Image] = result;
    }

    private RankResult ComputeRank(FieldSpritePatch patch, int tier, string[] names, string[] sourceNames, Dictionary<string, double> personal)
    {
        // 模型的旧 Pith 类属于原型，且没有优等生训练类；和已知原型一样只用模板，避免误筛或索引不存在的类。
        if (names.Concat(sourceNames).Any(n => n is "Pith" or PithIdentity.Gift || _usesPrototype?.Invoke(n) == true))
        {
            var rank = _matchSprite!(patch, tier, names.Concat(sourceNames).Distinct().ToArray()).Candidates.ToArray();
            return new(rank, rank, null);
        }
        float[]? logits = null;
        if (_spriteModel is not null)
        {
            var modelPatch = _neighborMask?.Invoke(patch, tier, names.Concat(sourceNames).Distinct().ToArray()) ?? patch;
            using var crop = OpenCvSharp.Cv2.ImDecode(patch.Image, OpenCvSharp.ImreadModes.Color);
            if (modelPatch.OcclusionMask is { } maskBytes)
            {
                using var mask = OpenCvSharp.Cv2.ImDecode(maskBytes, OpenCvSharp.ImreadModes.Grayscale);
                using var blocked = new OpenCvSharp.Mat(mask, new OpenCvSharp.Rect(
                    patch.TemplateRegion?.OffsetX ?? 0, patch.TemplateRegion?.OffsetY ?? 0, crop.Width, crop.Height));
                using var visible = new OpenCvSharp.Mat();
                OpenCvSharp.Cv2.BitwiseNot(blocked, visible);
                crop.SetTo(OpenCvSharp.Cv2.Mean(crop, visible), blocked);
            }
            logits = _spriteModel.Predict(crop);
            // 训练侧271条答案的最小完整召回K为4；来源和个人模板不被模型筛掉。
            // 原始遮挡率达到20%时保留该阶全部候选，避免模型预筛漏掉真名。
            if (patch.OcclusionRatio < .2) names = _spriteModel.Shortlist(logits, names, 4);
            names = names.Concat(sourceNames).Concat(personal.Keys).Distinct().ToArray();
        }
        else names = names.Concat(sourceNames).Distinct().ToArray();
        var templates = _matchSprite!(patch, tier, names).Candidates
            .Concat(personal.Select(p => new SpriteCandidate(p.Key, p.Value)))
            .GroupBy(c => c.Name).Select(g => g.MaxBy(c => c.Score)!).ToArray();
        var templateScores = templates;
        if (_spriteModel is not null)
        {
            templates = _spriteModel.Fuse(logits!, templates);
        }
        return new(templateScores, templates, logits);
    }

    private void Remember(FieldUnit unit, FieldSpritePatch patch, bool force = false)
    {
        if (patch.OcclusionRatio >= SpriteMatcher.OcclusionLearningThreshold) return;
        if (unit.Operator is null || _usesPrototype?.Invoke(unit.Operator) == true || !(unit.Status == "auto" || unit.Status == "sure" && unit.Source != "only")) return;
        if (unit.Status == "sure") _confirmed.Add((unit.Operator, unit.Tier));
        int existing = _learned.FindIndex(p => p.Unit.Operator == unit.Operator && p.Unit.Tier == unit.Tier
            && SpriteSimilarity(p.Patch, patch) >= SameSpriteScore);
        if (existing >= 0)
        {
            if (force) _learned[existing] = (unit, patch, true);
            return;
        }
        var same = _learned.Where(p => p.Unit.Operator == unit.Operator).ToArray();
        if (same.Length == 6)
        {
            // 自动扫描不能挤掉此前手动重认的模板，否则开关自动识别会改变下一次重认的依据。
            var replace = same.FirstOrDefault(p => !p.Forced);
            if (replace.Unit is not null) _learned.Remove(replace);
            else if (force) _learned.Remove(same[0]);
            else return;
        }
        _learned.Add((unit, patch, force));
    }

    private void SetIdentity(FieldCell cell, FieldUnit identity, FieldSpritePatch patch, bool save = true, bool force = false)
    {
        // 图像身份严重遮挡时只保留待确认，直接购买、拖动和手动证据仍可信。
        if ((patch.OcclusionRatio >= SpriteMatcher.OcclusionPendingThreshold
            || patch.OcclusionRatio >= SpriteMatcher.NeighborOcclusionThreshold
                && (patch.NeighborNames.Contains(identity.Operator)
                    || identity.Candidates.FirstOrDefault()?.Score < SpriteMatcher.NeighborMinimumScore))
            && !(identity.Status == "sure" && identity.Source is "purchase" or "drag" or "manual"))
            identity = identity with { Status = "pending" };
        // 装备字段归独立追踪器管理。
        var current = _units[cell];
        _units[cell] = current with { Operator = identity.Operator, Source = identity.Source,
            Status = identity.Status, Candidates = identity.Candidates, Elite = identity.Elite };
        if (save)
        {
            _anchors[cell] = patch;
            _history[cell] = (_units[cell], patch);
            Remember(_units[cell], patch, force);
        }
    }
}
