using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;
using Xunit.Abstractions;
using BoardCell = ASPAssistant.Core.Placement.BoardCell;

namespace StrongholdAssistant.Tests;

public class PlacementInputTests(ITestOutputHelper output)
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly Dictionary<string, (BoardCell, Facing)> Position = new() { ["银灰"] = (new(2, 1), Facing.Right) };

    private static PlacementInput Input() => new(new("m10", "test", [new("board", 2, 1, "银灰", "sure", true, [])]),
        5, "mode_multi_normal", false, null, ["SPECIAL", "ELEMENT", "DOT"], null, [], null, null,
        new Dictionary<string, string?>(), "balanced");

    private static async Task<(Dictionary<string, ASPAssistant.Core.Placement.Operator> Ops, PlacementPlanner Planner, GarrisonMap Map)> Fixture()
    {
        var store = new ASPAssistant.Core.Placement.JsonDataStore(Data);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        return (ops, new(ops, covenants), (await GarrisonBoardStore.LoadAsync(Data))!.Find("m10")!);
    }

    [Fact]
    public async Task OwnedCultivationChangesTheRecommendedOutcome()
    {
        var adapter = await PlacementAdapter.LoadAsync(Data);
        var input = Input() with
        {
            Field = new("m10", "test", [new("board", 2, 1, "斯卡蒂", "sure", true, [])]),
        };
        var owned = adapter.Compute(input)!;
        var standIn = adapter.Compute(input with
        {
            Roster = new("read", [new("斯卡蒂", null, null, null, null, "read", Prototype: "Sharp")], []),
        })!;
        Assert.Contains("按你的干员都练满估算（助手读不到精英化和等级）", owned.Assumptions);
        Assert.True(owned.Recommended!.Leaked < standIn.Recommended!.Leaked,
            $"自持有与补位的预计漏怪分别为 {owned.Recommended.Leaked} 和 {standIn.Recommended.Leaked}");
    }

    [Fact]
    public async Task SkillAndModuleSelectionsChangeUnitsAndRecommendation()
    {
        var (ops, planner, map) = await Fixture();
        var profile = ops["银灰"].Profile!;
        var options = new PlacementOptions { Potential = 0, FieldCount = 1 };
        SimUnit Unit(bool elite, string skill, string module) => Assert.Single(planner.BuildSimUnits(map,
            [new("银灰", elite) { Profile = profile.Select(skill, module) }], Position, options));
        foreach (bool elite in new[] { false, true })
        {
            var s1 = Unit(elite, "skchr_svrash_1", "");
            var s3 = Unit(elite, "skchr_svrash_3", "");
            Assert.NotEqual(s1.Skill, s3.Skill);
            Assert.NotEqual(s1.SkillRange?.Count, s3.SkillRange?.Count);
        }
        Assert.True(Unit(true, "skchr_svrash_3", "uniequip_002_svrash").Atk > Unit(true, "skchr_svrash_3", "").Atk);
        // 自选普通棋子模组等级为零；不能因为有选择就套用精锐模组面板。
        Assert.Equal(Unit(false, "skchr_svrash_3", "").Atk, Unit(false, "skchr_svrash_3", "uniequip_002_svrash").Atk);
        var adapter = await PlacementAdapter.LoadAsync(Data);
        PlacementResult Run(string skill, string module) => adapter.Compute(Input() with
        {
            // 满练度下第 6 回合的漏数相同，换一批敌人验证模组对推荐结果的影响。
            Round = 7,
            Skills = new Dictionary<string, string?> { ["银灰"] = skill },
            Modules = new Dictionary<string, string?> { ["银灰"] = module },
        })!;
        var before = Run("skchr_svrash_1", "");
        var skillOnly = Run("skchr_svrash_3", "");
        var after = Run("skchr_svrash_3", "uniequip_002_svrash");
        output.WriteLine($"S1无模组 / S3无模组 / S3模组：预计漏怪 {before.Recommended!.Leaked} / {skillOnly.Recommended!.Leaked} / {after.Recommended!.Leaked}");
        Assert.NotEqual(JsonSerializer.Serialize(before.Recommended), JsonSerializer.Serialize(skillOnly.Recommended));
        Assert.NotEqual(JsonSerializer.Serialize(skillOnly.Recommended), JsonSerializer.Serialize(after.Recommended));
    }

    [Fact]
    public async Task EquipmentNormalAndEliteValuesReachSimUnit()
    {
        var (ops, planner, map) = await Fixture();
        var effects = await CombatEffectStore.LoadAsync(Data);
        var adapter = await PlacementAdapter.LoadAsync(Data);
        var candidate = new PlacementCandidate("银灰", true) { Profile = ops["银灰"].Profile!.Select("skchr_svrash_3", "") };
        var options = new PlacementOptions { Potential = 0 };
        double Attack(PlacementCandidate c) => Assert.Single(planner.BuildSimUnits(map, [c], Position, options)).Atk;
        var atk = Attack(candidate);
        foreach (bool? elite in new bool?[] { false, true, null })
        {
            var unit = new FieldUnit("board", 2, 1, 3, "银灰", "manual")
                { Elite = true, Equipment = [new("维式重锤", elite)] };
            var field = new FieldState("m10", 5, "test", [unit], []);
            var json = JsonSerializer.SerializeToElement(field, HostApi.JsonOptions);
            var item = json.GetProperty("units")[0].GetProperty("equipment")[0];
            Assert.Equal("维式重锤", item.GetProperty("name").GetString());
            Assert.Equal(elite, item.GetProperty("elite").Deserialize<bool?>());
            var snapshot = json.Deserialize<PlacementField>(HostApi.JsonOptions)!;
            var effect = adapter.SelectEquipmentEffect(Assert.Single(snapshot.Units[0].Equipment!))!;
            Assert.Equal(atk * (elite == true ? 1.25 : 1.15), Attack(candidate with { Equipment = [effect] }), 6);
            var result = adapter.Compute(Input() with { Field = snapshot })!;
            Assert.Equal(elite is null, result.Assumptions.Contains("装备品质没读到，按普通算"));
        }
        Assert.Equal("noCombat", effects.Equipment["信标"].Normal.Status);
    }

    [Fact]
    public async Task AmiyaUsesCovenantsOfTheCurrentPlacement()
    {
        var (ops, _, map) = await Fixture();
        var effects = await CombatEffectStore.LoadAsync(Data);
        string[] covenants = ["测试盟约一", "测试盟约二", "测试盟约三", "测试盟约四", "测试盟约五"];
        var planner = new PlacementPlanner(ops, covenants.Select(n => new CovenantInfo { Name = n, ActivateCount = 1 }));
        var options = new PlacementOptions { Potential = 0 };
        foreach (int count in new[] { 2, 3, 4, 5 })
        {
            PlacementCandidate[] pool = [new("银灰", true) { Transforms = covenants.Take(count).ToArray() }];
            var plain = Assert.Single(planner.BuildSimUnits(map, pool, Position, options));
            var buff = Assert.Single(planner.BuildSimUnits(map, pool, Position, options with { Strategy = effects.Strategies["band_amiya"] }));
            double factor = count < 3 ? 1 : 1 + (count - 1) * .1;
            Assert.Equal(plain.Atk * factor, buff.Atk, 6);
            Assert.Equal(plain.Hp * factor, buff.Hp, 6);
        }
    }

    [Fact]
    public async Task RosterOverridesDefaultsAndMissingSelectionsAreReported()
    {
        var adapter = await PlacementAdapter.LoadAsync(Data);
        var selected = Input() with
        {
            Skills = new Dictionary<string, string?> { ["银灰"] = "skchr_svrash_1" },
            Modules = new Dictionary<string, string?> { ["银灰"] = "" },
        };
        var expected = adapter.Compute(selected)!;
        var fromRoster = adapter.Compute(selected with
        {
            Skills = new Dictionary<string, string?> { ["银灰"] = "skchr_svrash_3" },
            Modules = new Dictionary<string, string?> { ["银灰"] = "uniequip_002_svrash" },
            Roster = new("read", [new("银灰", null, "skchr_svrash_1", "", 1, "read")], []),
        })!;
        Assert.Equal(JsonSerializer.Serialize(expected.Recommended), JsonSerializer.Serialize(fromRoster.Recommended));
        Assert.Equal(expected.Moves, fromRoster.Moves);
        Assert.DoesNotContain(expected.Assumptions, a => a.Contains("技能按默认"));
        Assert.DoesNotContain(fromRoster.Assumptions, a => a.Contains("技能按默认"));
        var fallback = adapter.Compute(Input())!;
        Assert.Contains("1 人的技能按默认算", fallback.Assumptions);
        var unsupported = adapter.Compute(Input() with
        {
            StrategyId = "band_dance",
            Field = Input().Field with { Units = [new("board", 2, 1, "银灰", "sure", true, [new("伪装服", null)])] },
        })!;
        Assert.Contains("1 件装备的效果没算：伪装服", unsupported.Assumptions);
        Assert.Contains("本局策略「溃心领域」的效果没算", unsupported.Assumptions);
    }
}
