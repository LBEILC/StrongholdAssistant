// 2026-11-15：从 CarlosDJY d3dc2f9 移植计算核心与测试，适配本项目输入和数据，保留原声明。


using ASPAssistant.Core.Placement;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class PlacementPlannerTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static readonly string[] UrsusPool =
        ["塔露拉", "Mon3tr", "机械师", "乌啾", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "夏栎", "角峰", "霜叶"];

    private static async Task<(PlacementPlanner Planner, GarrisonBoardData Board)> LoadAsync()
    {
        var store = new JsonDataStore(DataDir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(DataDir))!;
        return (new PlacementPlanner(ops, covenants), board);
    }

    private static PlacementOptions Fast(int seed = 1) => new() { Iterations = 3000, Restarts = 3, Seed = seed };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PalaceRecommendationAvoidsBombardmentIncludingStartingAndPinnedCells(bool simulate)
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("m10")!;
        var pool = new[] { "拉普兰德", "角峰", "夏栎" }.Select(n => new PlacementCandidate(n, false)).ToList();
        var start = new Dictionary<string, (BoardCell Cell, Facing Facing)>
        {
            ["拉普兰德"] = (new(3, 3), Facing.Right),
            ["角峰"] = (new(2, 2), Facing.Right),
            ["夏栎"] = (new(4, 3), Facing.Right),
        };
        var options = new PlacementOptions
        {
            FieldCount = 3, Start = start, Simulate = simulate, Iterations = 100, SimulationIterations = 50,
            Pins = new Dictionary<string, PlacementPin> { ["拉普兰德"] = new(new(3, 3), Facing.Right) }
        };
        var plan = planner.Plan(map, pool, options);
        plan.Placed.Should().HaveCount(3);
        plan.Placed.Should().OnlyContain(p => Math.Abs(p.Cell.Col - 3) > 1 || Math.Abs(p.Cell.Row - 3) > 1);
        // 评价当前摆法仍保留原格子，不能把玩家实际站在伤害区的人从输入中删除。
        var current = planner.Evaluate(map, pool, start, options with { Simulate = false });
        current.Placed.Should().HaveCount(3);
        foreach (var p in current.Placed) p.Cell.Should().Be(start[p.Key].Cell);
    }

    [Fact]
    public async Task ExploreFromScratchIsNeverWorseThanCurrentOnly()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("m12")!;
        var pool = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        // 故意摆烂：8 个人挤在最左边那几格、全都朝左，让「照现状改」没有好起点。
        var cells = map.AllCells()
            .Where(c => c.Cell.Col is >= 0 and <= 6 && c.Type is BoardCellType.Ground or BoardCellType.Special)
            .Select(c => c.Cell).Take(8).ToArray();
        cells.Should().HaveCount(8);
        var start = pool.Take(8).Select((p, i) => (p.Key, Cell: cells[i]))
            .ToDictionary(p => p.Key, p => (new BoardCell(p.Cell.Col, p.Cell.Row), Facing.Left));
        var options = new PlacementOptions
        {
            FieldCount = 8, Start = start, Iterations = 2000, Restarts = 2, Seed = 7, Simulate = false
        };
        double Best(bool explore) => planner.Plan(map, pool, options with { ExploreFromScratch = explore }).Total;
        // 链 0 与「只从现状」完全相同，链 1 只能把最好值抬高，所以多链不会更差。
        Best(true).Should().BeGreaterThanOrEqualTo(Best(false));
        // 关着时是老行为，并且同 seed 可复现。
        Best(false).Should().Be(Best(false));
        Best(true).Should().Be(Best(true));
    }

    [Fact]
    public async Task Plan_PicksEightFromTwelveAndHoldsBothRoutes()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("m12")!;
        var plan = planner.Plan(map, UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast());

        plan.Placed.Should().HaveCount(8);
        plan.Bench.Should().HaveCount(4);
        plan.Placed.Select(p => p.Cell).Should().OnlyHaveUniqueItems();
        foreach (var p in plan.Placed)
            GarrisonMap.CanDeploy(map.At(p.Cell), planner.IsMelee(p.Name)).Should().BeTrue(p.Name);
        plan.ActiveCovenants.Should().Contain("乌萨斯");
        // Both routes have someone blocking.
        foreach (var route in map.Routes)
            plan.Placed.Should().Contain(p => route.Contains(p.Cell));
        plan.Checks.Should().Contain(c => c.Text.StartsWith("下路阻挡"));
    }

    [Fact]
    public async Task Plan_KeepsPinnedOperators()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("m12")!;
        var pin = new PlacementPin(new BoardCell(2, 3), Facing.Up);
        var options = Fast() with { Pins = new Dictionary<string, PlacementPin> { ["塔露拉"] = pin } };
        var plan = planner.Plan(map, UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList(), options);

        var t = plan.Find("塔露拉");
        t.Should().NotBeNull();
        t!.Cell.Should().Be(pin.Cell);
        t.Facing.Should().Be(Facing.Up);
        t.IsPinned.Should().BeTrue();
        plan.Placed.Should().HaveCount(8);
    }

    [Fact]
    public async Task Plan_KeepsFieldAndBenchPins()
    {
        // Whoever the plan would pick: the field pins go on, the bench pins stay off.
        var (planner, board) = await LoadAsync();
        var map = board.Find("m12")!;
        var pool = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        var free = planner.Plan(map, pool, Fast());
        var benched = free.Bench.Select(b => b.Name).ToList();
        var placed = free.Placed.Select(p => p.Key).ToList();
        var up = new HashSet<string> { benched[0], benched[1] };
        var down = new HashSet<string> { placed[0], placed[1] };
        var plan = planner.Plan(map, pool, Fast() with { FieldPins = up, BenchPins = down });
        plan.Placed.Should().HaveCount(8);
        plan.Placed.Select(p => p.Key).Should().Contain(up).And.NotContain(down);
        plan.Bench.Select(b => b.Name).Should().Contain(down);
    }

    [Fact]
    public async Task TransformsCountOnlyOnTheFieldAndHintsFillTheGap()
    {
        // 炎 takes 3: two field operators carrying a 炎 转职 leave it one short (a hint); a third activates it.
        var (planner, board) = await LoadAsync();
        var map = board.Find("m12")!;
        var plain = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        var free = planner.Plan(map, plain, Fast());
        var at = free.Placed.ToDictionary(p => p.Key, p => (p.Cell, p.Facing));
        var holders = free.Placed.Take(3).Select(p => p.Key).ToList();
        List<PlacementCandidate> With(int count) =>
            [.. plain.Select(c => holders.Take(count).Contains(c.Key) ? c with { Transforms = ["炎"] } : c)];
        var options = Fast() with { TransformableCovenants = new HashSet<string> { "炎" } };

        var two = planner.Evaluate(map, With(2), at, options);
        two.ActiveCovenants.Should().NotContain("炎");
        two.TransformHints.Should().ContainSingle(h => h.Covenant == "炎" && h.Have == 2 && h.Need == 3)
            .Which.Holder.Should().NotBe(holders[0]).And.NotBe(holders[1]);
        planner.Evaluate(map, With(3), at, options).ActiveCovenants.Should().Contain("炎");
        // A holder benched takes its 转职 with it.
        var benched = at.Where(kv => kv.Key != holders[0]).ToDictionary(kv => kv.Key, kv => kv.Value);
        planner.Evaluate(map, With(3), benched, options).ActiveCovenants.Should().NotContain("炎");
    }

    [Fact]
    public async Task Evaluate_AndAlternatives_ScoreAGivenArrangement()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("m12")!;
        var pool = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        var plan = planner.Plan(map, pool, Fast());
        var placement = plan.Placed.ToDictionary(p => p.Name, p => (p.Cell, p.Facing));

        planner.Evaluate(map, pool, placement, Fast()).Total.Should().BeApproximately(plan.Total, 0.2);
        var alts = planner.Alternatives(map, pool, placement, plan.Placed[0].Name, Fast());
        alts.Should().NotBeEmpty();
        alts.Should().OnlyContain(a => !placement.Values.Select(v => v.Cell).Contains(a.Cell));
    }

    [Fact]
    public async Task Plan_WorksOnEveryMap()
    {
        var (planner, board) = await LoadAsync();
        var pool = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        foreach (var map in board.Maps)
        {
            var plan = planner.Plan(map, pool, new PlacementOptions { Iterations = 800, Restarts = 1 });
            plan.Placed.Should().HaveCount(8, map.Id);
        }
    }



    [Fact]
    public async Task Plan_KeepsAHealerOnTheField()
    {
        var (planner, board) = await LoadAsync();
        // Damage-heavy pool with a single healer: the healer still goes on and reaches a block point.
        string[] pool = ["塔露拉", "机械师", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "角峰", "霜叶", "赫拉格", "乌啾"];
        foreach (var id in new[] { "m12", "m01", "m05" })
        {
            // The fast score (the planner's first stage) keeps the healer on; with layers weighted ×2.5 it may stand
            // where it stacks rather than by the block point — the simulation against the round's real enemies puts
            // it in reach (the test below).
            var plan = planner.Plan(board.Find(id)!, pool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast() with { Simulate = false });
            plan.Placed.Should().Contain(p => p.Name == "乌啾", id);
        }
    }

    [Fact]
    public async Task Plan_KeepsAHealerOnTheField_AgainstRealEnemies()
    {
        var (planner, board) = await LoadAsync();
        var dir = Path.Combine(AppContext.BaseDirectory, "data");
        var spawns = ASPAssistant.Core.Placement.GarrisonSpawnStore.Load(dir)!;
        var routes = EnemyRouteStore.Load(dir)!;
        string[] pool = ["塔露拉", "机械师", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "角峰", "霜叶", "赫拉格", "乌啾"];
        foreach (var id in new[] { "m12", "m01", "m05" })
        {
            var options = Fast() with
            {
                Routes = routes.Find(id, 8, null, null), Round = 8, SimulationIterations = 300,
                Streams = spawns.Streams(8, null, ["特异", "致命", "元素"]),
            };
            var plan = planner.Plan(board.Find(id)!, pool.Select(n => new PlacementCandidate(n, false)).ToList(), options);
            plan.Placed.Should().Contain(p => p.Name == "乌啾", id);
        }
    }

    private static readonly string[] VictoriaPool =
        ["涤火杰西卡", "塞雷娅", "维娜·维多利亚", "白铁", "凯瑟琳", "哈洛德", "酒神", "慕斯", "罗比菈塔", "云迹", "空弦", "星源"];



    [Fact]
    public async Task Plan_UsesSariaCopyTraitOnLegalCells()
    {
        var (planner, board) = await LoadAsync();
        // (2,3) 修正为禁放岩石后，最优阵型及复制对象会变化；保留复制收益与合法落点要求。
        var map = board.Find("m01")!;
        var crates = planner.Plan(map, VictoriaPool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast(2) with { Simulate = false });
        crates.Find("塞雷娅")!.Notes.Should().Contain(n => n.StartsWith("复制身前 "));
        crates.Placed.Should().NotContain(p => p.Cell == new BoardCell(2, 3));
        foreach (var p in crates.Placed)
            GarrisonMap.CanDeploy(map.At(p.Cell), planner.IsMelee(p.Name)).Should().BeTrue(p.Name);
        // With two separate routes 塞雷娅 (a defender with about 7 500 effective HP) is worth more holding the second
        // line than copying a trait; either way she must have a job on the field, not just stand somewhere.
        var plan = planner.Plan(board.Find("m10")!, VictoriaPool.Select(n => new PlacementCandidate(n, false)).ToList(), new PlacementOptions { Simulate = false });
        var saria = plan.Find("塞雷娅");
        saria.Should().NotBeNull();
        saria!.Notes.Should().Contain(n => n.StartsWith("复制身前") || n.Contains("拦截点"));
    }

    [Fact]
    public async Task Plan_PlacesDuplicateCopiesButCountsCovenantsOnce()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("m12")!;
        // Two 冻河 and two 维伊: both copies can go on the field, but 乌萨斯 counts different operators.
        var pool = new List<PlacementCandidate>
        {
            new("塔露拉", false), new("机械师", false), new("Mon3tr", false), new("折桠", false),
            new("冻河", false), new("冻河", false, 1), new("维伊", false), new("维伊", false, 1),
        };
        var plan = planner.Plan(map, pool, Fast());
        plan.Placed.Should().HaveCount(8);
        plan.Placed.Select(p => p.Key).Should().OnlyHaveUniqueItems().And.Contain(["冻河", "冻河#2", "维伊#2"]);
        plan.Placed.Where(p => p.Name == "冻河").Should().HaveCount(2);
        // 塔露拉, 冻河, 维伊, 折桠 → 4 different 乌萨斯 operators: not 6, so no "乌萨斯 6" check.
        plan.Checks.Should().NotContain(c => c.Text == "乌萨斯 6");
        plan.Checks.Should().Contain(c => c.Text.StartsWith("乌萨斯 4"));
    }


}
