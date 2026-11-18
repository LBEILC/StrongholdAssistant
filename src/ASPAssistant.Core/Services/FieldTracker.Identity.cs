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

    public const double SameSpriteScore = .92;
    private readonly Dictionary<string, FieldRosterEntry> _roster = new(StringComparer.Ordinal);
    private readonly Dictionary<FieldCell, FieldSpritePatch> _pictures = [];
    private readonly List<(FieldUnit Unit, FieldSpritePatch Patch, bool Forced)> _learned = [];
    private readonly Dictionary<FieldCell, (FieldUnit Unit, FieldSpritePatch? Patch, DateTimeOffset At, int Scans)> _missing = [];
    private readonly HashSet<FieldCell> _returned = [];
    private readonly HashSet<FieldCell> _moved = [];
    private readonly Dictionary<FieldCell, string> _gainSources = [];
    private IReadOnlyList<Operator> _identityOperators = [];
    private Func<FieldSpritePatch, int, string[], SpriteMatch>? _matchSprite;
    private SpriteModelMatcher? _spriteModel;
    private Func<FieldSpritePatch, IEnumerable<(FieldUnit Unit, FieldSpritePatch Patch)>, FieldSpritePatch>? _occlusion;
    private Func<FieldSpritePatch, int, string[], FieldSpritePatch>? _modelRegion;
    private double IdentityAutoFillGap => _spriteModel is null ? SpriteMatcher.AutoFillGap : SpriteModelMatcher.AutoFillGap;
    private string? _strategyName;
    private (DateTimeOffset At, string Source)? _recentGift;

    public void ConfigureIdentity(IReadOnlyList<Operator> operators,
        Func<FieldSpritePatch, int, string[], SpriteMatch> match, SpriteModelMatcher? model = null,
        Func<FieldSpritePatch, IEnumerable<(FieldUnit Unit, FieldSpritePatch Patch)>, FieldSpritePatch>? occlusion = null,
        Func<FieldSpritePatch, int, string[], FieldSpritePatch>? modelRegion = null)
    { _identityOperators = operators; _matchSprite = match; _spriteModel = model; _occlusion = occlusion; _modelRegion = modelRegion; }

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
    { _bannedCandidates.Clear(); _customCandidates = null; _roster.Clear(); _pictures.Clear(); _learned.Clear(); _missing.Clear(); _returned.Clear(); _moved.Clear(); _gainSources.Clear(); _recentGift = null; _strategyName = null; }

    private void CaptureGainContext(FieldObservation o, Dictionary<FieldCell, FieldUnit> before, string? dragged)
    {
        var board = before.Values.Where(u => u.Zone == "board").ToArray();
        var removed = before.Values.Where(u => o.Cells.GetValueOrDefault(u.Cell, -1) == 0).ToArray();
        bool ethan = _log.Any(e => e.Kind == "sell" && e.Operator == "伊桑" && e.At == o.At.ToString("O"))
            || dragged == "伊桑" && o.Money > _dragMoney && removed.Any(u => u.Operator == "伊桑");
        bool mechanic = _battle && board.Any(u => u.Operator == "机械师"
            && (u.Elite == true || board.Count(v => v.Row == u.Row) >= 3));
        if (ethan || mechanic)
            _recentGift = (o.At, ethan ? "ethan" : "strategy");
        else
        {
            if (_battle && _strategyName == "得闲饮茶" && o.Round == 3) _recentGift = (o.At, "strategy");
            CaptureEquipmentGain(o, before.Values);
        }
        foreach (var unit in _units.Values.Where(u => u.Zone == "bench" && !before.ContainsKey(u.Cell)))
            _gainSources[unit.Cell] = o.GainSource ?? (_recentGift is {} recent && (o.At - recent.At).TotalSeconds <= 3
                ? recent.Source : StrategySource(o));
    }

    private void CaptureEquipmentGain(FieldObservation o, IEnumerable<FieldUnit> units)
    {
        var used = units.SelectMany(u => (u.Equipment ?? []).Where(e => e.Name is "简易通讯机" or "寻呼模块" or "信标")
            .Where(e => o.Cells.GetValueOrDefault(u.Cell, -1) == 0 || o.PanelOperator == u.Operator
                && o.Equipment is not null && !o.Equipment.Any(n => n.Name == e.Name))
            .Select(e => (Unit: u, Equipment: e))).FirstOrDefault();
        if (used.Unit is null) return;
        bool choice = used.Equipment.Name is "寻呼模块" or "信标";
        _recentGift = (o.At, choice ? "dispatch" : "equipment");
    }

    private string StrategySource(FieldObservation o)
    {
        if (_strategyName is "集结指示" or "协力共进") return "dispatch";
        if (_strategyName is "人才盲盒" or "团伙行动" or "猎人战法" or "骑士团长" or "流动乐章" or "无惧风雪" or "黑白相易"
            or "幻画为真" or "纸片替身" or "不稳定要素" or "最佳品味" or "打包速递" or "替身娃娃") return "strategy";
        return _battle && _identityOperators.Any(p => Regex.IsMatch(p.Obtain ?? "", $"策略「{Regex.Escape(_strategyName ?? "")}」第\\s*{o.Round}\\s*回合赠送"))
            ? "strategy" : "manual";
    }

    private void TrackRosterEvent(string kind, string? name)
    {
        if (name is null) return;
        if (kind == "sell" && _roster.TryGetValue(name, out var item))
            _roster[name] = item with { Sold = !_units.Values.Any(u => u.Operator == name) };
    }

    private void ConfirmIdentity(FieldCell cell, string? name, DateTimeOffset at)
    {
        if (name is null) return;
        _gains.RemoveAll(g => g.Cell == cell);
        AddRoster(name, _gainSources.GetValueOrDefault(cell, "manual"), at);
        _roster[name] = _roster[name] with { Sold = false };
        if (_pictures.TryGetValue(cell, out var patch))
        {
            // 手动纠正同时替换相似图块的旧自动身份。
            _learned.RemoveAll(p => p.Unit.Tier == _units[cell].Tier
                && SpriteSimilarity(p.Patch, patch) >= SameSpriteScore);
            Remember(_units[cell], patch);
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

    private void ResolveIdentity(FieldObservation o, Dictionary<FieldCell, FieldUnit> before, bool force, HashSet<FieldCell> merged)
    {
        if (o.Sprites is null || _matchSprite is null) return;
        var gone = before.Values.Where(u => o.Cells.GetValueOrDefault(u.Cell, -1) == 0 && !_manualOnly.Contains(u.Cell)).ToList();

        var ordered = o.Sprites.OrderByDescending(p => _occlusion is null ? 0 : p.Value.Geometry?.BadgeFootY ?? 0).ToArray();
        var personalScores = new Dictionary<(byte[], byte[]), double>();
        int rowEnd = 0;
        foreach (var (cell, rawPatch) in ordered)
        {
            int index = Array.FindIndex(ordered, p => p.Key == cell);
            if (force && index == rowEnd)
            {
                rowEnd = index + 1;
                while (rowEnd < ordered.Length && ordered[rowEnd].Key.Zone == cell.Zone && ordered[rowEnd].Key.Row == cell.Row) rowEnd++;
                PrepareRow(o, ordered[index..rowEnd], personalScores);
            }
            if (!_units.TryGetValue(cell, out var unit)) continue;
            var patch = _occlusion?.Invoke(rawPatch, _units.Values.Where(u => u.Cell != cell && o.Sprites.ContainsKey(u.Cell))
                .Select(u => (u, o.Sprites[u.Cell]))) ?? rawPatch;
            before.TryGetValue(cell, out var old);
            // 强制识别只保留直接证据的确定身份，其余重新使用冻结模板比较。
            bool reidentify = force && !(unit.Status == "sure" && unit.Source is "drag" or "manual");
            _pictures[cell] = patch;
            if (!reidentify && _moved.Contains(cell)) { SetIdentity(cell, unit, patch, force: force); continue; }
            bool arrival = old is null;
            if (arrival && cell.Zone != "bench") _gainSources[cell] = o.GainSource ?? "manual";
            // 2. 本次拖动面板已确定身份。
            bool direct = unit.Operator is not null && unit.Source == "drag"
                && (old?.Operator != unit.Operator || old.Source != unit.Source || arrival);
            if (!reidentify && direct)
            {
                SetIdentity(cell, unit, patch, force: force);
                AddRoster(unit.Operator!, "manual", o.At);
                continue;
            }
            // 两次空扫描或三秒内回到原格，连待确认候选也原样保留。
            if (!reidentify && _returned.Contains(cell) && old is not null && unit.Operator == old.Operator && unit.Source == old.Source)
            { SetIdentity(cell, old, patch, force: force); continue; }
            if (unit.Status == "sure" && unit.Source is "drag" or "manual")
            { SetIdentity(cell, unit, patch, force: force); continue; }
            var names = MatchCandidates()
                .Distinct().Where(n => HasTier(n, unit.Tier)).ToArray();
            var prototype = _unknownPrototype?.Invoke(patch, unit.Tier, names) ?? [];
            if (prototype.Length > 0)
            {
                SetIdentity(cell, unit with { Operator = prototype[0].Name, Source = "sprite", Status = "pending",
                    Candidates = prototype.Select(p => new FieldCandidate(p.Name, p.Score)).ToArray() }, patch, force: force);
                continue;
            }
            var ranked = Rank(patch, unit.Tier, reidentify, names, CurrentName(unit), personalScores, out var evidence);
            if (reidentify && unit.Operator is not null)
                ranked = ranked.OrderByDescending(c => c.Score + (c.Name == unit.Operator ? .01 : 0)).ToArray();
            var candidates = ranked.Take(3).Select(c => new FieldCandidate(c.Name, c.Score)).ToArray();
            // Rank 已逐名字取个人模板与冻结模板的高分，个人模板领先也用同一条分差判断。
            // 仅比模板时用模板门槛。模型也明确支持首名时，可用相同分差自动填入（346）。
            double gap = evidence.Logits is null || ranked.Length > 1
                && _spriteModel!.StronglySupports(evidence.Logits, evidence.Templates, ranked[0].Name)
                ? SpriteMatcher.AutoFillGap : IdentityAutoFillGap;
            string status = candidates.Length == 0 ? "unknown" : evidence.Confirmed || ranked.Length > 1
                && ranked[0].Score - ranked[1].Score >= gap ? "auto" : "pending";
            SetIdentity(cell, unit with { Operator = candidates.FirstOrDefault()?.Operator,
                Source = candidates.Length == 0 ? null : "sprite", Status = status, Candidates = candidates,
                Elite = unit.Operator == candidates.FirstOrDefault()?.Operator ? unit.Elite : null }, patch, force: force);
        }
        _preparedRanks.Clear();
        foreach (var u in gone)
        {
            bool consumed = _log.Any(e => e.At == o.At.ToString("O") && e.From == u.Cell && e.Kind is "move" or "sell")
                || merged.Contains(u.Cell);
            if (!consumed) _missing[u.Cell] = (u, _pictures.GetValueOrDefault(u.Cell), o.At, 1);
            _pictures.Remove(u.Cell); _gainSources.Remove(u.Cell);
        }
    }

    private bool DragMatchesTier(string name, int tier, Dictionary<FieldCell, FieldUnit> before)
    {
        var definition = _identityOperators.FirstOrDefault(p => p.Name == name);
        if (definition is not null && tier < definition.Tier) return false;
        return HasTier(name, tier) || before.Values.Any(u => u.Operator == name && u.Tier == tier && u.Status == "sure");
    }

    private HashSet<FieldCell> PairMoves(FieldObservation o, Dictionary<FieldCell, FieldUnit> before,
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
            _gainSources.Remove(to);
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
        return merged;
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
                if (missing.Patch is { } patch) _pictures[cell] = patch;
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

    private string[] CurrentName(FieldUnit unit) =>
        unit.Operator is { } name && HasTier(name, unit.Tier) ? [name] : [];

    private SpriteCandidate[] Rank(FieldSpritePatch patch, int tier, bool force, string[] names, string[] sourceNames,
        Dictionary<(byte[], byte[]), double> personalScores, out RankResult result)
    {
        if (names.Length == 0 && sourceNames.Length == 0) { result = new([], [], null); return []; }
        // 先逐名字取个人模板与冻结模板的高分，再统一融合模型概率。
        var personal = PersonalRanking(patch, tier, force, names, sourceNames, personalScores);
        var prepared = _preparedRanks.GetValueOrDefault(patch.Image);
        if (prepared is not null && prepared.Names.SequenceEqual(names) && prepared.Sources.SequenceEqual(sourceNames)
            && prepared.Personal.Count == personal.Count && personal.All(p => prepared.Personal.GetValueOrDefault(p.Key, double.NaN) == p.Value)
            && SameMask(prepared.Patch.OcclusionMask, patch.OcclusionMask))
            result = prepared.Result;
        else result = ComputeRank(patch, tier, names, sourceNames, personal);
        Compared?.Invoke(patch, result.Templates, result.Fused, result.Logits);
        return result.Fused.OrderByDescending(c => c.Score).ToArray();
    }

    private Dictionary<string, double> PersonalRanking(FieldSpritePatch patch, int tier, bool force,
        string[] names, string[] sources, Dictionary<(byte[], byte[]), double> scores)
    {
        // 一次识别中的预计算和顺序应用可共享相同两张图的五尺度分数；候选与已学习身份仍按当前状态取。
        double Compare(FieldSpritePatch sample)
        {
            var key = (sample.Image, patch.Image);
            if (!scores.TryGetValue(key, out double score))
                scores[key] = score = SpriteMatcher.MatchPersonal(sample, patch);
            return score;
        }

        return _learned.Where(p => (!force || p.Forced || p.Unit.Status == "sure" && p.Unit.Source is "drag" or "manual")
                && p.Unit.Tier == tier && (names.Contains(p.Unit.Operator!) || sources.Contains(p.Unit.Operator!)))
            .GroupBy(p => p.Unit.Operator!).ToDictionary(g => g.Key, g => g.Max(p => Compare(p.Patch)));
    }

    private sealed record RankResult(SpriteCandidate[] Templates, SpriteCandidate[] Fused, float[]? Logits, bool Confirmed = false);
    private sealed record PreparedRank(FieldSpritePatch Patch, string[] Names, string[] Sources,
        Dictionary<string, double> Personal, RankResult Result);
    private readonly Dictionary<byte[], PreparedRank> _preparedRanks = [];
    private static bool SameMask(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    // 同排只并行纯比对；邻人和个人图块若被前格更新，顺序应用时按实际输入重算。
    private void PrepareRow(FieldObservation o, KeyValuePair<FieldCell, FieldSpritePatch>[] row,
        Dictionary<(byte[], byte[]), double> personalScores)
    {
        _preparedRanks.Clear();
        var inputs = new List<(FieldSpritePatch Patch, int Tier, string[] Names, string[] Sources, Dictionary<string, double> Personal)>();
        for (int i = 0; i < row.Length; i++)
        {
            var (cell, raw) = row[i];
            if (!_units.TryGetValue(cell, out var unit)
                || unit.Status == "sure" && unit.Source is "drag" or "manual") continue;
            var patch = _occlusion?.Invoke(raw, _units.Values.Where(u => u.Cell != cell && o.Sprites!.ContainsKey(u.Cell))
                .Select(u => (u, o.Sprites![u.Cell]))) ?? raw;
            var names = MatchCandidates().Distinct().Where(n => HasTier(n, unit.Tier)).ToArray();
            var sources = CurrentName(unit);
            if (names.Length == 0 && sources.Length == 0) continue;
            var personal = PersonalRanking(patch, unit.Tier, true, names, sources, personalScores);
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
            var modelPatch = _modelRegion?.Invoke(patch, tier, names.Concat(sourceNames).Distinct().ToArray()) ?? patch;
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
            if (names.Length > 1 && patch.OcclusionRatio < .2)
            {
                var quick = _spriteModel.TemplateCandidates(logits, names, 1);
                if (_spriteModel.Confident(logits, names.Concat(sourceNames).Concat(personal.Keys).Distinct().ToArray(), quick[0]))
                {
                    var matches = _matchSprite!(patch, tier, quick).Candidates.OrderByDescending(c => c.Score).ToArray();
                    // 同局学到的自动模板可能来自另一名相似干员。只用冻结模板复核模型首名。
                    if (matches.Length > 0 && matches[0].Name == quick[0] && matches[0].Score >= .4
                        && (matches.Length == 1 || matches[0].Score - matches[1].Score >= SpriteMatcher.AutoFillGap))
                        return new(matches, _spriteModel.Fuse(logits, matches), logits, true);
                }
            }
            // 训练侧271条答案的最小完整召回K为4；当前名字和个人模板不被模型筛掉。
            // 原始遮挡率达到20%时保留该阶全部候选，避免模型预筛漏掉真名。
            if (patch.OcclusionRatio < .2) names = _spriteModel.TemplateCandidates(logits, names);
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
        if (unit.Operator is null || _usesPrototype?.Invoke(unit.Operator) == true || !(unit.Status == "auto" || unit.Status == "sure")) return;
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

    private void SetIdentity(FieldCell cell, FieldUnit identity, FieldSpritePatch patch, bool force = false)
    {
        // 装备字段归独立追踪器管理。
        var current = _units[cell];
        _units[cell] = current with { Operator = identity.Operator, Source = identity.Source,
            Status = identity.Status, Candidates = identity.Candidates, Elite = identity.Elite };
        Remember(_units[cell], patch, force);
    }
}
