using ASPAssistant.Core.Placement;
using FluentAssertions;

namespace StrongholdAssistant.Tests;

public class PlacementDuplicateTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    // 单路、两名额，候选均未锁定；用明确的面板差距隔离随机搜索与真实数据调整。
    private static readonly GarrisonMap Map = new("duplicates", "同名副本测试", 0,
        new[,] { { BoardCellType.Ground, BoardCellType.Ground, BoardCellType.Ground, BoardCellType.Ground } },
        [new BoardCell[] { new(3, 0), new(2, 0), new(1, 0), new(0, 0) }]);

    private static PlacementOptions Options => new()
    {
        FieldCount = 2, Seed = 184, Iterations = 3000, Restarts = 3,
        Simulate = false, ExpectFlying = false, Potential = 0
    };

    private static Operator Piece(string name, double attack, int block = 2, double hp = 3000)
    {
        var range = new OperatorRangeRef { BaseRange = new AttackRange("test", [(0, 0), (1, 0), (2, 0), (3, 0)]) };
        return new Operator
        {
            Name = name, Tier = 1,
            Profile = new OperatorProfile
            {
                Position = "近战", Profession = "近卫", BlockCount = block,
                NormalRange = range, EliteRange = range,
                NormalStats = new ChessStats { Atk = attack, Dps = attack, Hp = hp, Def = 300, Block = block },
                EliteStats = new ChessStats { Atk = attack * 2, Dps = attack * 2, Hp = hp * 2, Def = 300, Block = block }
            }
        };
    }

    [Fact]
    public void Plan_SelectsBothStrongCopiesOverWeakAlternativesAndKeepsEliteStats()
    {
        var operators = new[] { Piece("强输出", 2000), Piece("弱甲", 10), Piece("弱乙", 20) };
        var planner = new PlacementPlanner(operators.ToDictionary(o => o.Name), []);
        PlacementCandidate[] pool = [new("强输出", false), new("强输出", true, 1), new("弱甲", false), new("弱乙", false)];

        var plan = planner.Plan(Map, pool, Options);

        plan.Placed.Select(p => p.Key).Should().BeEquivalentTo("强输出", "强输出#2");
        plan.Placed.Select(p => p.Cell).Should().OnlyHaveUniqueItems();
        plan.Bench.Select(p => p.Name).Should().BeEquivalentTo("弱甲", "弱乙");
        // 输出没有精锐标志，验证推荐摆法生成的战斗单位确实使用各自普通/精锐面板。
        var units = planner.BuildSimUnits(Map, pool,
            plan.Placed.ToDictionary(p => p.Key, p => (p.Cell, p.Facing)), Options);
        units.Single(u => u.Key == "强输出").Atk.Should().Be(2000);
        units.Single(u => u.Key == "强输出").Hp.Should().Be(3000);
        units.Single(u => u.Key == "强输出#2").Atk.Should().Be(4000);
        units.Single(u => u.Key == "强输出#2").Hp.Should().Be(6000);
    }

    [Fact]
    public void Plan_PrefersNeededBlockerOverSecondDamageCopy()
    {
        var operators = new[] { Piece("强输出", 2000, block: 0), Piece("阻挡手", 100, block: 3, hp: 20000), Piece("弱甲", 10, block: 0) };
        var planner = new PlacementPlanner(operators.ToDictionary(o => o.Name), []);
        PlacementCandidate[] pool = [new("强输出", false), new("强输出", true, 1), new("阻挡手", false), new("弱甲", false)];

        var plan = planner.Plan(Map, pool, Options);

        plan.Placed.Should().HaveCount(2);
        plan.Placed.Should().ContainSingle(p => p.Name == "强输出");
        plan.Placed.Should().ContainSingle(p => p.Name == "阻挡手");
    }

    [Fact]
    public async Task Adapter_DeploysBothStrongCopiesWithTheirOwnOrigins()
    {
        var adapter = await PlacementAdapter.LoadAsync(Data);
        PlacementUnit[] units =
        [
            new("board", 3, 1, "塔露拉", "sure", false, []),
            new("board", 3, 3, "深靛", "sure", false, []),
            new("bench", 1, 0, "塔露拉", "sure", true, []),
            new("bench", 2, 0, "灰喉", "sure", false, [])
        ];
        var input = new PlacementInput(new("m10", "2026-11-15T00:00:00Z", units),
            5, "mode_multi_normal", false, null, ["SPECIAL", "ELEMENT", "DOT"],
            null, null, null, null, new Dictionary<string, string?>(), "balanced");

        // 适配器使用 PlacementOptions 默认固定种子 1，并按场上两个格子决定名额。
        var result = adapter.Compute(input)!;

        result.Should().NotBeNull();
        var reversed = adapter.Compute(input with { Field = input.Field with { Units = units.Reverse().ToArray() } })!;
        // 计算完成时间记录真实时钟；其余字段包括逐个副本的移动、模拟结果及顺序都应相同。
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(result),
            System.Text.Json.JsonSerializer.Serialize(reversed with { ComputedAt = result.ComputedAt }));
        var copies = result.Moves.Where(m => m.Operator == "塔露拉").ToArray();
        copies.Should().HaveCount(2).And.OnlyContain(m => m.To.Zone == "board");
        copies.Should().ContainSingle(m => m.From == units[0].Cell);
        copies.Should().ContainSingle(m => m.From == units[2].Cell);
        copies.Select(m => m.To).Should().OnlyHaveUniqueItems();
        result.Moves.Count(m => m.To.Zone == "board").Should().Be(2);
    }

    [Fact]
    public async Task Covenants_TwoCopiesDoNotActivateButTwoDifferentMembersDo()
    {
        var store = new JsonDataStore(Data);
        var operators = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        covenants.Single(c => c.Name == "聚合").ActivateCount.Should().Be(2);
        var planner = new PlacementPlanner(operators, covenants);
        var map = (await GarrisonBoardStore.LoadAsync(Data))!.Find("m10")!;

        var duplicates = planner.Evaluate(map, [new("乌啾", false), new("乌啾", true, 1)],
            new Dictionary<string, (BoardCell, Facing)>
            {
                ["乌啾"] = (new(2, 2), Facing.Down), ["乌啾#2"] = (new(3, 2), Facing.Down)
            }, Options);
        var distinct = planner.Evaluate(map, [new("乌啾", false), new("风絮", true)],
            new Dictionary<string, (BoardCell, Facing)>
            {
                ["乌啾"] = (new(2, 2), Facing.Down), ["风絮"] = (new(3, 2), Facing.Down)
            }, Options);

        duplicates.Placed.Should().HaveCount(2);
        duplicates.ActiveCovenants.Should().NotContain("聚合");
        distinct.Placed.Should().HaveCount(2);
        distinct.ActiveCovenants.Should().Contain("聚合");
    }
}
