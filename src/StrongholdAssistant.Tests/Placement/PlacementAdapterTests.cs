using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;
using StrongholdAssistant.Host;
using Xunit.Abstractions;
using BoardCell = ASPAssistant.Core.Placement.BoardCell;

namespace StrongholdAssistant.Tests;

public class PlacementAdapterTests(ITestOutputHelper output)
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    private static async Task<(PlacementInput Input, GarrisonMap Map, PlacementPlanner Planner)> Fixture()
    {
        var store = new ASPAssistant.Core.Placement.JsonDataStore(Data);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var planner = new PlacementPlanner(ops, covenants);
        var map = (await GarrisonBoardStore.LoadAsync(Data))!.Find("m10")!;
        string?[] names = ["塔露拉", "Mon3tr", "机械师", "乌啾", "维伊", "冻河", "冻河", null, "折桠", "奥尔佳"];
        var occupied = new HashSet<BoardCell>();
        var units = names.Select((n, i) =>
        {
            var cell = i >= 8 ? new BoardCell(i - 7, 0) : map.AllCells().First(c => c.Cell.Col is >= 0 and <= 6
                && !occupied.Contains(c.Cell) && GarrisonMap.CanDeploy(c.Type, n is null || planner.IsMelee(n))).Cell;
            if (i < 8) occupied.Add(cell);
            return new FieldUnit(i < 8 ? "board" : "bench", cell.Col, cell.Row, 3, n, n is null ? null : "manual");
        }).ToArray();
        var field = new FieldState("m10", 5, "2026-11-15T00:00:00Z", units, []);
        var match = new MatchInfo("manual", null, [], [], null, ["SPECIAL", "ELEMENT", "DOT"])
            { Mode = new("mode_multi_normal", "险境模拟") };
        var json = JsonSerializer.SerializeToNode(field, HostApi.JsonOptions)!;
        for (int i = 0; i < units.Length; i++) json["units"]![i]!["status"] = i == 7 ? "unknown" : i == 6 ? "pending" : "sure";
        return (new(json.Deserialize<PlacementField>(HostApi.JsonOptions)!, 5, match.Mode.Id, match.Solo,
            match.BossId, match.EnemyTypes, null, null, null, null, new Dictionary<string, string?>(), "balanced"), map, planner);
    }

    [Fact]
    public async Task FieldCopiesGoalsAndOutcomes()
    {
        var (input, map, planner) = await Fixture();
        var adapter = await PlacementAdapter.LoadAsync(Data);
        var results = new Dictionary<string, PlacementResult>();
        foreach (var goal in new[] { "balanced", "leak", "layers" })
        {
            var watch = Stopwatch.StartNew();
            var plan = adapter.Compute(input with { Goal = goal });
            watch.Stop();
            Assert.NotNull(plan);
            results[goal] = plan;
            output.WriteLine($"{goal}: {watch.Elapsed.TotalMilliseconds:F0} ms; 漏怪 {plan.Recommended?.Leaked}; 叠层 {plan.Recommended?.Layers.Sum(l => l.Layers):F2}");
            Assert.Equal(goal, plan.Goal);
            Assert.Equal(input.Field.UpdatedAt, plan.BasedOn);
            Assert.Equal(9, plan.Moves.Length);
            Assert.Equal(9, plan.Moves.Select(m => m.From).Distinct().Count());
            Assert.All(plan.Moves, m => Assert.Contains(input.Field.Units, u => u.Cell == m.From && u.Operator == m.Operator));
            var deployed = plan.Moves.Where(m => m.To.Zone == "board").ToArray();
            Assert.Equal(8, deployed.Length);
            Assert.Equal(8, deployed.Select(m => m.To).Distinct().Count());
            Assert.All(deployed, m => Assert.True(GarrisonMap.CanDeploy(map.At(new(m.To.Col, m.To.Row)), planner.IsMelee(m.Operator))));
            Assert.All(plan.Moves.Where(m => m.To.Zone == "bench"), m =>
            {
                Assert.InRange(m.To.Col, 1, 10); Assert.Equal(0, m.To.Row); Assert.Null(m.Facing);
                if (m.From.Zone == "bench") Assert.Equal(m.From, m.To);
            });
            Assert.Contains(input.Field.Units[7].Cell, plan.Skipped);
            Assert.Equal(1, plan.Guessed);
            Assert.Contains(plan.Assumptions, a => a.Contains("潜能按 1"));
            Assert.Contains("盟约层数按 0 算", plan.Assumptions);
            Assert.NotNull(plan.Recommended); Assert.NotNull(plan.Current);
            Assert.InRange(plan.Recommended.Streams.Sum(s => s.Chance), .999999, 1.000001);
            Assert.InRange(plan.Current.Streams.Sum(s => s.Chance), .999999, 1.000001);
        }
        var leak = results["leak"].Recommended!;
        var layers = results["layers"].Recommended!;
        // 退火结果不保证跨目标单调；按验收要求输出实测值，不调整搜索凑断言。
        output.WriteLine($"目标比较：叠层单调={layers.Layers.Sum(l => l.Layers) >= leak.Layers.Sum(l => l.Layers)}；漏怪单调={leak.Leaked <= layers.Leaked}");
    }

    [Fact]
    public async Task HostEquipmentAndCovenantLayersReachAdapter()
    {
        var (input, _, _) = await Fixture();
        var field = new FieldState(input.Field.MapId, 5, input.Field.UpdatedAt,
            input.Field.Units.Select(u => new FieldUnit(u.Zone, u.Col, u.Row, 3, u.Operator, "manual")
            {
                Equipment = u.Operator is null ? [] : [new("变形同构体", null), new("炎国短刀", null)]
            }).ToArray(), []);
        var match = new MatchInfo("manual", null, [], [], null, [])
        {
            CovenantBar = [new("yan", 1, 80, 5, input.Field.UpdatedAt)]
        };
        // 与宿主相同的协议快照路径，覆盖真实 C# 属性名和 JSON 格式。
        var snapshot = JsonSerializer.SerializeToElement(field, HostApi.JsonOptions).Deserialize<PlacementField>(HostApi.JsonOptions)!;
        var bar = JsonSerializer.SerializeToElement(match, HostApi.JsonOptions).GetProperty("covenantBar")
            .Deserialize<PlacementLayers[]>(HostApi.JsonOptions)!;
        Assert.Equal(80, Assert.Single(bar).Layers);
        Assert.Equal(new[] { new FieldEquipment("变形同构体", null), new("炎国短刀", null) }, snapshot.Units[0].Equipment);
        var result = (await PlacementAdapter.LoadAsync(Data)).Compute(input with { Field = snapshot, CovenantBar = bar })!;
        Assert.DoesNotContain(result.Assumptions, a => a.Contains("按 0 算") || a.Contains("装备的效果没算") || a.Contains("没读到的装备"));
        Assert.Contains("装备品质没读到，按普通算", result.Assumptions);
        Assert.Contains("现在的朝向按最好的算", result.Assumptions);
        Assert.NotNull(result.Current);
    }

    [Fact]
    public async Task ProfilesCoverCatalogIncludingDiyAndHiddenPiece()
    {
        var ops = await new ASPAssistant.Core.Placement.JsonDataStore(Data).LoadOperatorsAsync();
        var planner = new PlacementPlanner(ops.ToDictionary(o => o.Name), []);
        Assert.Equal(218, ops.Count);
        Assert.All(ops, op => Assert.True(planner.CanPlace(op.Name), op.Name));
        foreach (var name in new[] { "凯尔希", "陈", "银灰", "能天使", "暴躁铁皮", "Pith（优等生）" })
        {
            var profile = ops.Single(o => o.Name == name).Profile!;
            Assert.Equal(2, profile.NormalStats!.Phase);
            Assert.Equal(1, profile.NormalStats.Level);
            Assert.Equal(60, profile.EliteStats!.Level);
            Assert.True(profile.NormalStats.Atk > 0);
        }
    }

    [Fact]
    public async Task PithGiftComputesAndReportsElementalDamageAssumption()
    {
        var (input, _, _) = await Fixture();
        var unit = input.Field.Units.First(u => u.Operator == "机械师") with { Operator = "Pith（优等生）" };
        var field = input.Field with { Units = [unit] };
        var result = (await PlacementAdapter.LoadAsync(Data)).Compute(input with { Field = field, StrategyId = "band_pith" });
        Assert.NotNull(result);
        Assert.Empty(result.Skipped);
        Assert.Contains(result.Moves, m => m.Operator == "Pith（优等生）");
        Assert.Contains(result.Assumptions, a => a.Contains("神经、灼燃、凋亡损伤"));
    }

    [Fact]
    public async Task CurrentFacingSearchKeepsCopiesAndCells()
    {
        var (input, map, planner) = await Fixture();
        var pool = input.Field.Units.Where(u => u.Operator is not null)
            .Select((u, i) => new PlacementCandidate(u.Operator!, false, i)).ToArray();
        var current = pool.Take(7).Select((p, i) => (p.Key, Cell: input.Field.Units[i].Cell))
            .ToDictionary(p => p.Key, p => (new BoardCell(p.Cell.Col, p.Cell.Row), Facing.Right));
        var options = new PlacementOptions { Simulate = false, FieldCount = 7 };
        var before = planner.Evaluate(map, pool, current, options);
        var after = planner.Evaluate(map, pool, current, options, optimiseFacing: true);
        Assert.Equal(current.Count, after.Placed.Count);
        Assert.All(after.Placed, p => Assert.Equal(current[p.Key].Item1, p.Cell));
        Assert.True(after.Total >= before.Total);
    }

    // 场上 n 人 + 整备区 m 人，都是能计算的干员：验「按部署上限补满」用。
    private static async Task<PlacementInput> BoardAndBenchFixture(int board, int bench)
    {
        var store = new ASPAssistant.Core.Placement.JsonDataStore(Data);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var planner = new PlacementPlanner(ops, covenants);
        var map = (await GarrisonBoardStore.LoadAsync(Data))!.Find("m10")!;
        string[] names = ["塔露拉", "Mon3tr", "机械师", "乌啾", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "夏栎"];
        Assert.True(board + bench <= names.Length);
        var occupied = new HashSet<BoardCell>();
        var units = new List<FieldUnit>();
        for (int i = 0; i < board; i++)
        {
            var cell = map.AllCells().First(c => c.Cell.Col is >= 0 and <= 6 && !occupied.Contains(c.Cell)
                && GarrisonMap.CanDeploy(c.Type, planner.IsMelee(names[i]))).Cell;
            occupied.Add(cell);
            units.Add(new FieldUnit("board", cell.Col, cell.Row, 3, names[i], "manual"));
        }
        for (int i = 0; i < bench; i++)
            units.Add(new FieldUnit("bench", i + 1, 0, 3, names[board + i], "manual"));
        var field = new FieldState("m10", 5, "2026-11-20T00:00:00Z", units.ToArray(), []);
        var json = JsonSerializer.SerializeToNode(field, HostApi.JsonOptions)!;
        for (int i = 0; i < units.Count; i++) json["units"]![i]!["status"] = "sure";
        return new(json.Deserialize<PlacementField>(HostApi.JsonOptions)!, 5, "mode_multi_normal", false,
            null, ["SPECIAL", "ELEMENT", "DOT"], null, null, null, null, new Dictionary<string, string?>(), "balanced");
    }

    [Fact]
    public async Task PlanFillsToDeployLimitNotTheCurrentCount()
    {
        var input = await BoardAndBenchFixture(board: 4, bench: 5);
        var adapter = await PlacementAdapter.LoadAsync(Data);
        var plan = adapter.Compute(input);
        Assert.NotNull(plan);
        // 卫戍协议·规程 部署上限 8：场上 4 人时推荐要把整备区的人补到 8 人，而不是只排场上那 4 个。
        var deployed = plan.Moves.Where(m => m.To.Zone == "board").ToArray();
        Assert.Equal(PlacementAdapter.DeployLimit, deployed.Length);
        Assert.Equal(deployed.Length, deployed.Select(m => m.To).Distinct().Count());
        Assert.True(plan.Moves.Count(m => m.From.Zone == "bench" && m.To.Zone == "board") >= 4);
        Assert.Contains(plan.Assumptions, a => a.Contains("部署上限"));
        // 同一输入算两次结果一致（固定种子）。
        var again = adapter.Compute(input)!;
        Assert.Equal(JsonSerializer.Serialize(plan with { ComputedAt = "" }, HostApi.JsonOptions),
            JsonSerializer.Serialize(again with { ComputedAt = "" }, HostApi.JsonOptions));
    }

    [Fact]
    public async Task FullFieldKeepsTheOldPlacementContract()
    {
        var (input, _, _) = await Fixture();
        var plan = (await PlacementAdapter.LoadAsync(Data)).Compute(input);
        Assert.NotNull(plan);
        Assert.Equal(9, plan.Moves.Length);
        Assert.Equal(8, plan.Moves.Count(m => m.To.Zone == "board"));
        Assert.DoesNotContain(plan.Assumptions, a => a.Contains("部署上限"));
    }

    [Fact]
    public async Task SoloStillComputesWithExplicitAssumption()
    {
        var (input, _, _) = await Fixture();
        var adapter = await PlacementAdapter.LoadAsync(Data);
        var result = adapter.Compute(input with { Solo = true, Mode = "mode_single_20" });
        Assert.NotNull(result);
        Assert.Contains("单人模式的敌人加成没有数据，按联机标准算", result.Assumptions);
        Assert.NotNull(result.Current);
        Assert.NotNull(result.Recommended);
    }
}
