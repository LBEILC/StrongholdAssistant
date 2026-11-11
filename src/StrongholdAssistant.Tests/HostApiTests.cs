using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class HostApiTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);

    [Fact]
    public async Task SpecialOperatorAppearsInCatalogButIsNotAPlanningCandidate()
    {
        var api = await CreateApi();
        var special = Assert.Single(api.Catalog.GetProperty("operators").EnumerateArray(),
            o => o.GetProperty("name").GetString() == "暴躁铁皮");
        Assert.Equal("策略「暴躁铁皮」第 1 回合赠送", special.GetProperty("obtain").GetString());
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var operators = await store.LoadOperatorsAsync();
        var (covenants, _) = await store.LoadCovenantsAsync();
        var planner = new LineupPlanner(operators, covenants);
        var request = new PlannerRequest
        {
            Goals = [LineupPlanner.CreateGoal(covenants.Single(c => c.Name == "协防干员"), 1)],
            PeopleOptions = [8], TransformOptions = [0]
        };
        var result = planner.Plan(request);
        Assert.Equal(operators.Count(o => o.Obtain is null), result.CandidateCount);
        Assert.NotEmpty(result.AllLineups);
        Assert.All(result.AllLineups, lineup => Assert.DoesNotContain(lineup.Operators, o => o.Name == "暴躁铁皮"));
        var locked = planner.Plan(new PlannerRequest
        {
            Goals = request.Goals, PeopleOptions = [8], TransformOptions = [0],
            LockedOperators = new HashSet<string> { "暴躁铁皮" }
        });
        Assert.NotNull(locked.Error);
        Assert.Empty(locked.AllLineups);
    }

    [Fact]
    public async Task SaveLineupPreservesOtherPhasesAndMemberDetailsAndConvertsTransforms()
    {
        var api = await CreateApi();
        var store = new LineupStore(Path.Combine(_directory, "lineups"));
        var names = api.Catalog.GetProperty("operators").EnumerateArray().Take(3).Select(o => o.GetProperty("name").GetString()!).ToArray();
        var original = new Lineup
        {
            Name = "原阵容",
            EarlySlots = [new() { OperatorName = names[0], Equipments = ["早期装备"] }],
            MidSlots = [new() { OperatorName = names[1], Tags = [LineupTag.经济] }],
            SupportSlots = [new() { OperatorName = names[2], CoreEquipments = ["挂件装备"] }],
            Strategies = [new() { StrategyId = "strategy", Tags = [StrategyTag.优选] }],
            Slots =
            [
                new() { OperatorName = names[0], Equipments = ["装备甲"], CoreEquipments = ["装备乙"], Tags = [LineupTag.阵容核心], TransformedCovenant = "炎" },
                new() { OperatorName = names[1], Equipments = ["装备丙"], Tags = [LineupTag.输出], TransformedCovenant = "叙拉古" }
            ]
        };
        await store.SaveAsync(original);
        // 模拟旧版文件没有来源与备注字段。
        var path = Path.Combine(_directory, "lineups", original.Id + ".json");
        var oldJson = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        oldJson.AsObject().Remove("source");
        oldJson.AsObject().Remove("note");
        await File.WriteAllTextAsync(path, oldJson.ToJsonString());
        var loaded = Assert.Single(await api.LineupsAsync());
        Assert.Equal("user", loaded.Source);
        Assert.Equal("siracusa", loaded.Members[1].Transform);
        var input = loaded with
        {
            Name = "改名", Source = "planned", Note = "备注",
            Members = [new(names[1], "yan"), new(names[0], "siracusa"), new(names[2])]
        };
        var response = await api.InvokeAsync("saveLineup", [JsonSerializer.SerializeToElement(input, HostApi.JsonOptions)]);
        var saved = Assert.IsType<LineupDto>(response);
        var persisted = Assert.Single(await store.LoadAllAsync());
        Assert.Equal(original.Id, persisted.Id);
        Assert.Equal(original.CreatedAt, persisted.CreatedAt);
        Assert.Equal("改名", persisted.Name);
        Assert.Equal("planned", persisted.Source);
        Assert.Equal("备注", persisted.Note);
        Assert.True(persisted.UpdatedAt >= original.UpdatedAt);
        Assert.Equal(JsonSerializer.Serialize(original.EarlySlots), JsonSerializer.Serialize(persisted.EarlySlots));
        Assert.Equal(JsonSerializer.Serialize(original.MidSlots), JsonSerializer.Serialize(persisted.MidSlots));
        Assert.Equal(JsonSerializer.Serialize(original.SupportSlots), JsonSerializer.Serialize(persisted.SupportSlots));
        Assert.Equal(JsonSerializer.Serialize(original.Strategies), JsonSerializer.Serialize(persisted.Strategies));
        Assert.Equal(input.Members.Select(m => m.Name), persisted.Slots.Select(s => s.OperatorName));
        foreach (var previous in original.Slots)
        {
            var current = persisted.Slots.Single(s => s.OperatorName == previous.OperatorName);
            Assert.Equal(previous.Equipments, current.Equipments);
            Assert.Equal(previous.CoreEquipments, current.CoreEquipments);
            Assert.Equal(previous.Tags, current.Tags);
        }
        Assert.Equal(new[] { "炎", "叙拉古", "" }, persisted.Slots.Select(s => s.TransformedCovenant));
        Assert.Equal(new string?[] { "yan", "siracusa", null }, saved.Members.Select(m => m.Transform));
        var reloaded = Assert.Single(await api.LineupsAsync());
        Assert.Equal(saved.Members, reloaded.Members);
        Assert.Equal(saved.Source, reloaded.Source);
        Assert.Equal(saved.Note, reloaded.Note);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
