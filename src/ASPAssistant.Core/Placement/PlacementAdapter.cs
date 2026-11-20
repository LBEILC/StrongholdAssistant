// 转职判定改编自 CarlosDJY d3dc2f9 的 LineupEquipmentService（Apache-2.0）；改为读取本局各副本装备。
using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Placement;

// 计算边界快照与宿主识别模型分开，允许尚未合入的可选协议字段缺省。
public sealed record PlacementUnit(string Zone, int Col, int Row, string? Operator,
    string? Status = null, bool? Elite = null, FieldEquipment[]? Equipment = null)
{
    public FieldCell Cell => new(Zone, Col, Row);
}
public sealed record PlacementField(string? MapId, string UpdatedAt, PlacementUnit[] Units)
{
    public FieldSummon[]? Summons { get; init; }
}
public sealed record PlacementLayers(string Id, int? Layers);
public sealed record PlacementInput(PlacementField Field, int? Round, string? Mode, bool Solo,
    string? BossId, string[] EnemyTypes, string[]? RoundEnemyNames, PlacementLayers[]? CovenantBar,
    string? StrategyId, Roster? Roster, IReadOnlyDictionary<string, string?> Skills, string Goal)
{
    public IReadOnlyDictionary<string, string?> Modules { get; init; } = new Dictionary<string, string?>();
}
public sealed record PlacementMove(string Operator, FieldCell From, FieldCell To, string? Facing);
public sealed record PlacementStream(string Label, double Chance, double Enemies, double Leaked);
public sealed record PlacementLayer(string Covenant, double Layers);
public sealed record PlacementOutcome(double Enemies, double Leaked, PlacementStream[] Streams, PlacementLayer[] Layers);
public sealed record PlacementResult(string BasedOn, string ComputedAt, string Goal, PlacementMove[] Moves,
    int Guessed, FieldCell[] Skipped, PlacementOutcome? Recommended, PlacementOutcome? Current, string[] Assumptions);

public sealed class PlacementAdapter
{
    /// <summary>部署上限：卫戍协议·规程 共通「部署上限：8」（PRTS 战场一览），联机同值。</summary>
    public const int DeployLimit = 8;

    public static PlacementOptions ProductBudget => new()
    {
        // 快搜跑两条链（现状 / 从零，各 3500 步），模拟轮仍是 1 条：模拟一步就是一整场战斗。
        Iterations = 3500, Restarts = 2, SimulationIterations = 1000, MaxStreams = 6, Simulate = true,
        ExploreFromScratch = true
    };
    private readonly Dictionary<string, Operator> _operators;
    private readonly Dictionary<string, string> _ids;
    private readonly Dictionary<string, string> _transforms;
    private readonly CombatEffectStore _effects;
    private readonly GarrisonBoardData _board;
    private readonly EnemyRouteStore _routes;
    private readonly GarrisonSpawnStore _spawns;
    private readonly PlacementPlanner _planner;
    private SummonDefinition[] _summons = [];

    public string[]? GetCells(string mapId) => _board.Find(mapId)?.CellRows;

    public IReadOnlyList<RoundStream> RoundStreams(int round, string? mode, bool solo, string[] enemyTypes, string? bossId)
        => solo ? SoloRoundStreams(_spawns, round, enemyTypes, bossId) : _spawns.Streams(round, mode, enemyTypes, bossId);

    public static IReadOnlyList<RoundStream> SoloRoundStreams(GarrisonSpawnStore spawns, int round, string[] enemyTypes, string? bossId)
        => spawns.Streams(round, "mode_multi_funny", enemyTypes, bossId);

    public CombatEffect? SelectEquipmentEffect(FieldEquipment item)
    {
        if (!_effects.Equipment.TryGetValue(item.Name, out var versions)) return null;
        return item.Elite == true && versions.Elite.Status == "simulated" ? versions.Elite : versions.Normal;
    }

    private PlacementAdapter(Dictionary<string, Operator> operators, Dictionary<string, string> ids,
        Dictionary<string, string> transforms, CombatEffectStore effects,
        GarrisonBoardData board, EnemyRouteStore routes, GarrisonSpawnStore spawns, PlacementPlanner planner)
        => (_operators, _ids, _transforms, _effects, _board, _routes, _spawns, _planner)
            = (operators, ids, transforms, effects, board, routes, spawns, planner);

    public static async Task<PlacementAdapter> LoadAsync(string directory)
    {
        var store = new JsonDataStore(directory);
        var operators = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, transforms) = await store.LoadCovenantsAsync();
        var ids = JsonSerializer.Deserialize<Dictionary<string, string>>(
            await File.ReadAllTextAsync(Path.Combine(directory, "covenant_ids.json")))!;
        return new(operators, ids, transforms, await CombatEffectStore.LoadAsync(directory), (await GarrisonBoardStore.LoadAsync(directory))!,
            EnemyRouteStore.Load(directory), GarrisonSpawnStore.Load(directory)!, new(operators, covenants))
            { _summons = SummonCatalog.Load(directory) };
    }

    public PlacementResult? Compute(PlacementInput input, CancellationToken cancel = default)
    {
        var field = input.Field;
        int count = field.Units.Count(u => u.Zone == "board");
        if (field.MapId is null || _board.Find(field.MapId) is not { } map || input.Round is not > 0 || count == 0) return null;
        var pool = new List<PlacementCandidate>();
        var origins = new Dictionary<string, PlacementUnit>();
        var potentials = new Dictionary<string, int>();
        var skipped = new List<FieldCell>();
        var assumptions = new List<string>();
        int guessed = 0, missingPotential = 0, defaultSkill = 0;
        var ignoredEquipment = new List<string>();
        bool missingElite = false, missingEquipment = false, missingEquipmentQuality = false;
        // 同一盘的枚举顺序不参与副本编号、候选顺序和固定种子搜索。
        foreach (var unit in field.Units.OrderBy(u => u.Zone, StringComparer.Ordinal).ThenBy(u => u.Row).ThenBy(u => u.Col))
        {
            if (unit.Status == "unknown" || unit.Operator is not { } name || !_planner.CanPlace(name))
            {
                skipped.Add(unit.Cell);
                continue;
            }
            var equipment = unit.Equipment?.OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Elite).ToArray() ?? [];
            // TODO(design): 现有模拟器没有元素损伤累计、爆发和对应选敌，按既有假设列表说明。
            if (name == PithIdentity.Gift && !assumptions.Any(a => a.StartsWith(PithIdentity.Gift, StringComparison.Ordinal)))
                assumptions.Add("Pith（优等生）的神经、灼燃、凋亡损伤及优先攻击未爆发目标没算");
            var equipmentNames = equipment.Select(e => e.Name).ToArray();
            var transforms = equipmentNames.Contains("变形同构体")
                ? equipmentNames.Where(_transforms.ContainsKey).Select(e => _transforms[e]).Distinct().ToArray() : [];
            var roster = input.Roster?.Operators.FirstOrDefault(o => o.Name == name);
            var profile = _operators[name].Profile!;
            string? skill = roster?.Skill ?? input.Skills.GetValueOrDefault(name);
            string? module = roster?.Module ?? input.Modules.GetValueOrDefault(name);
            if (skill is null) defaultSkill++;
            else if (!profile.Skills.ContainsKey(skill))
            {
                assumptions.Add($"{name}所选技能没有计算资料，按默认算");
                skill = null;
            }
            if (module is not null && !profile.Modules.ContainsKey(module))
            {
                assumptions.Add($"{name}所选模组没有计算资料，按默认算");
                module = null;
            }
            var effects = new List<CombatEffect>();
            if (name == PithIdentity.Gift)
            {
                var variant = unit.Elite == true ? _operators[name].Elite : _operators[name].Normal;
                var trait = string.Join(" ", variant.Traits.Select(t => t.TraitDescription));
                var bonus = System.Text.RegularExpressions.Regex.Match(trait, @"攻击力和生命上限\+(\d+)%");
                double amount = double.Parse(bonus.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100;
                effects.Add(new() { Status = "simulated", Atk = amount, Hp = amount });
            }
            foreach (var item in equipment)
            {
                var effect = SelectEquipmentEffect(item);
                if (effect is null || effect.Status == "unsupported")
                    ignoredEquipment.Add(item.Name);
                else if (effect.Status == "simulated")
                {
                    effects.Add(effect);
                    missingEquipmentQuality |= item.Elite is null;
                }
            }
            var candidate = new PlacementCandidate(name, unit.Elite ?? false, pool.Count(c => c.Name == name))
            {
                Transforms = transforms, Profile = profile.Select(skill, module), Equipment = effects,
            };
            pool.Add(candidate);
            origins[candidate.Key] = unit;
            if (unit.Status == "pending") guessed++;
            missingElite |= unit.Elite is null;
            missingEquipment |= unit.Equipment is null;
            potentials[candidate.Key] = (roster?.Potential ?? 1) - 1;
            if (roster?.Potential is null) missingPotential++;
        }
        if (pool.Count == 0) return null;
        if (missingElite) assumptions.Add("精锐按没有算");
        if (missingPotential > 0) assumptions.Add($"{missingPotential} 人潜能按 1 算（没读过我的干员）");
        if (defaultSkill > 0) assumptions.Add($"{defaultSkill} 人的技能按默认算");
        if (missingEquipment) assumptions.Add("没读到的装备按没有算");
        if (missingEquipmentQuality) assumptions.Add("装备品质没读到，按普通算");
        if (ignoredEquipment.Count > 0) assumptions.Add($"{ignoredEquipment.Count} 件装备的效果没算：{string.Join("、", ignoredEquipment.Distinct())}");
        var strategy = input.StrategyId is { } sid ? _effects.Strategies.GetValueOrDefault(sid) : null;
        if (input.StrategyId is not null && (strategy is null || strategy.Status == "unsupported"))
            assumptions.Add($"本局策略「{strategy?.Name ?? input.StrategyId}」的效果没算");
        if (input.Solo) assumptions.Add("单人模式的敌人加成没有数据，按联机标准算");
        var layers = new Dictionary<string, int>();
        if (input.CovenantBar is not { Length: > 0 }) assumptions.Add("盟约层数按 0 算");
        else
        {
            foreach (var item in input.CovenantBar.Where(c => c.Layers is not null))
                if (_ids.FirstOrDefault(p => p.Value == item.Id).Key is { } name) layers[name] = item.Layers!.Value;
            int missing = input.CovenantBar.Count(c => c.Layers is null);
            if (missing > 0) assumptions.Add($"{missing} 个盟约层数没读到，按 0 算");
        }
        var streams = RoundStreams(input.Round.Value, input.Mode, input.Solo, input.EnemyTypes, input.BossId);
        if (input.RoundEnemyNames is { Length: > 0 } names)
        {
            var available = streams.SelectMany(s => s.Spawns).Select(e => e.Profile.Name).ToHashSet();
            var ignored = names.Where(n => !available.Contains(n)).Distinct().ToArray();
            if (ignored.Length > 0) assumptions.Add($"{string.Join("、", ignored)} 不在本回合怪组里，没算进模拟");
            var included = names.Where(available.Contains).ToArray();
            if (included.Length > 0)
            {
                var matching = streams.Where(s => included.All(n => s.Spawns.Any(e => e.Profile.Name == n))).ToArray();
                if (matching.Length > 0) streams = matching;
                else assumptions.Add("本回合敌人名单没匹配到怪组，按本局敌人类型算");
            }
        }
        // 两种摆法使用同一批怪组，输出保持真实强度。
        // 模式未识别时沿用产品 BoardRouteStore 的单人默认模式。
        var routeMode = input.Mode ?? (input.Solo ? "mode_single_20" : null);
        var routes = _routes.Find(map.Id, input.Round.Value, routeMode, input.BossId);
        if (routes is null) assumptions.Add("本回合路线没查到，按地图基础路线算");
        var current = origins.Where(p => p.Value.Zone == "board").ToDictionary(p => p.Key,
            p => (Cell: new BoardCell(p.Value.Col, p.Value.Row), Facing: Facing.Right));
        assumptions.Add("现在的朝向按最好的算");
        // TODO(design): 候选总数不足原上场人数时，先算可用的人；确认是否应整次不推荐。
        if (pool.Count < count) assumptions.Add("有场上干员没认出或缺少计算资料，上场人数按可计算人数算");
        // 按部署上限补满，不跟着「现在站了几个」走：场上不满 8 人时也会建议把整备区的人补上场。
        int fieldLimit = Math.Min(DeployLimit, pool.Count);
        if (fieldLimit > count) assumptions.Add($"部署上限 {DeployLimit} 人：推荐把整备区的 {fieldLimit - count} 人补上场");
        var options = ProductBudget with
        {
            FieldCount = Math.Max(count, fieldLimit), Mode = input.Goal switch { "leak" => PlacementMode.Combat, "layers" => PlacementMode.Stacking, _ => PlacementMode.Balanced },
            Round = input.Round, TotalRounds = _routes.RoundCount(routeMode), Routes = routes,
            Streams = streams, CovenantLayers = layers, Potentials = potentials, Potential = 0, Start = current,
            Strategy = strategy,
            OccupiedCells = (field.Summons ?? []).Where(s => s.Zone == "board")
                .Select(s => new BoardCell(s.Col, s.Row)).ToHashSet(),
            FixedSummonNames = (field.Summons ?? []).Where(s => s.Zone == "board").Select(s => s.Name).ToHashSet(),
            FixedSummons = (field.Summons ?? []).Where(s => s.Zone == "board")
                .OrderBy(s => s.Row).ThenBy(s => s.Col)
                .Select(s => (Summon: s, Definition: _summons.FirstOrDefault(d => d.Name == s.Name)))
                .Where(p => p.Definition is not null).Select(p => {
                    var d = p.Definition!;
                    // TODO(design): 动态阻挡缺少实例状态字段；暂用基础值，狼影数量和流形复制对象需另定协议。
                    return new SimUnit($"summon:{p.Summon.Col},{p.Summon.Row}", new(p.Summon.Col, p.Summon.Row), [],
                        d.BaseBlock > 0, d.BaseBlock, false, 0, 1, false, d.Hp, d.Defense, false) { Res = d.Resistance };
                }).ToArray(),
        };
        cancel.ThrowIfCancellationRequested();
        var recommended = _planner.Plan(map, pool, options, cancel);
        cancel.ThrowIfCancellationRequested();
        var now = _planner.Evaluate(map, pool, current, options with { StreamFactor = recommended.StreamFactor },
            optimiseFacing: true, cancel: cancel);
        cancel.ThrowIfCancellationRequested();
        var placed = recommended.Placed.ToDictionary(p => p.Key);
        var occupiedBench = field.Units.Where(u => u.Zone == "bench" && !origins.Any(p => p.Value == u && placed.ContainsKey(p.Key)))
            .Select(u => u.Col).ToHashSet();
        occupiedBench.UnionWith((field.Summons ?? []).Where(s => s.Zone == "bench").Select(s => s.Col));
        var moves = pool.Select(c =>
        {
            var from = origins[c.Key].Cell;
            if (placed.TryGetValue(c.Key, out var to))
                return new PlacementMove(c.Name, from, new("board", to.Cell.Col, to.Cell.Row), to.Facing.ToString().ToLowerInvariant());
            if (from.Zone == "bench") return new PlacementMove(c.Name, from, from, null);
            int col = Enumerable.Range(1, 10).First(c => !occupiedBench.Contains(c));
            occupiedBench.Add(col);
            return new PlacementMove(c.Name, from, new("bench", col, 0), null);
        }).ToArray();
        return new(field.UpdatedAt, DateTimeOffset.UtcNow.ToString("O"), input.Goal, moves, guessed,
            skipped.ToArray(), Outcome(recommended), Outcome(now), assumptions.ToArray());
    }

    private PlacementOutcome? Outcome(PlacementPlan plan)
    {
        if (plan.StreamResults.Count == 0) return null;
        double total = plan.StreamResults.Sum(s => s.Weight);
        var streams = plan.StreamResults.OrderByDescending(s => s.Weight)
            .Select(s => new PlacementStream(s.Label, s.Weight / total, s.Result.Spawned, s.Result.Leaked)).ToArray();
        return new(Math.Round(streams.Sum(s => s.Chance * s.Enemies), 1), Math.Round(streams.Sum(s => s.Chance * s.Leaked), 1),
            streams, plan.LayerGains.Where(p => p.Value != 0 && _ids.ContainsKey(p.Key))
                .Select(p => new PlacementLayer(_ids[p.Key], p.Value)).ToArray());
    }
}
