using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class LineupGoalsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);

    [Fact]
    public async Task LegacyNineMembersInferGoalsIncludingTransformAndPersistOnSave()
    {
        var store = new LineupStore(Path.Combine(_directory, "lineups"));
        var old = new Lineup
        {
            Slots = new[] { "奥斯塔", "忍冬", "伺夜", "铃兰", "贝洛内", "卡达", "初雪", "塞雷娅", "夜莺" }
                .Select(n => new LineupSlot { OperatorName = n, TransformedCovenant = n == "卡达" ? "叙拉古" : "" }).ToList()
        };
        await store.SaveAsync(old);
        var path = Path.Combine(_directory, "lineups", old.Id + ".json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        json.Remove("goals");
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var api = await CreateApi();
        var loaded = Assert.Single(await api.LineupsAsync());
        Assert.Equal(new[] { new CovenantGoal("siracusa", 2), new CovenantGoal("empty", 1) }, loaded.Goals);
        Assert.False(JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject().ContainsKey("goals"));
        await api.SaveLineupAsync(loaded);
        Assert.Equal(loaded.Goals, Assert.Single(await store.LoadAllAsync()).Goals);
    }

    [Fact]
    public async Task MoreThanNineMembersPreserveGoalsAndOptionalKey()
    {
        var api = await CreateApi();
        var members = api.Catalog.GetProperty("operators").EnumerateArray().Take(12)
            .Select((o, i) => new LineupMember(o.GetProperty("name").GetString()!, Key: i == 0 ? true : i == 1 ? false : null)).ToArray();
        var saved = await api.SaveLineupAsync(new("", "干员池", members, "user", null, default)
            { Goals = [new("siracusa", 2)] });
        var loaded = Assert.Single(await (await CreateApi()).LineupsAsync());
        Assert.Equal(saved.Members, loaded.Members);
        Assert.Equal(saved.Goals, loaded.Goals);
    }

    [Fact]
    public async Task MarkerSetsExcludeBannedOwnedPrimaryAndSpecialOperatorsAndRefresh()
    {
        var api = await CreateApi();
        var lineup = await api.SaveLineupAsync(new("", "协防", [new("初雪")], "user", null, default)
            { Goals = [new("empty", 1)] });
        await api.SaveSessionAsync(new(lineup.Id, ["塞雷娅"], ["夜莺"]));
        api.SetOperatorBanned("Mon3tr", true);
        Assert.Equal(new[] { "初雪", "夜莺" }, api.TrackedNames);
        Assert.Equal(new[] { "机械师", "塔露拉", "缪尔赛思" }.Order(), api.SecondaryNames.Order());
        foreach (var name in new[] { "Mon3tr", "塞雷娅", "初雪", "夜莺", "暴躁铁皮" })
            Assert.DoesNotContain(name, api.SecondaryNames);
        var items = new[] { new ShopItem { Name = "初雪" }, new ShopItem { Name = "机械师" } };
        api.UpdateShopTracking(items);
        Assert.True(items[0].IsTracked);
        Assert.False(items[0].IsSecondary);
        Assert.True(items[1].IsSecondary);
        Assert.False(items[1].IsTracked);
        api.SetOperatorBanned("Mon3tr", false);
        Assert.Contains("Mon3tr", api.SecondaryNames);
        api.ApplyRecognizedMatch(api.Match, ["机械师"]);
        Assert.DoesNotContain("机械师", api.SecondaryNames);
        await api.SaveSessionAsync(new(lineup.Id, [], []));
        Assert.Contains("塞雷娅", api.SecondaryNames);
        Assert.Contains("夜莺", api.SecondaryNames);
        await api.SaveLineupAsync(lineup with { Members = [], Goals = [] });
        Assert.Empty(api.TrackedNames);
        Assert.Empty(api.SecondaryNames);
    }

    [Fact]
    public async Task FocusSettingsRoundTripAndRejectInvalidIds()
    {
        var api = await CreateApi();
        Assert.Empty(api.Settings.FocusCovenants);
        Assert.Empty(api.Settings.FocusOperators);
        var settings = api.Settings with { FocusCovenants = ["siracusa"], FocusOperators = ["卡达"] };
        await api.InvokeAsync("saveSettings", [JsonSerializer.SerializeToElement(settings, HostApi.JsonOptions)]);
        var loaded = Assert.IsType<SettingsDto>(await (await CreateApi()).InvokeAsync("settings", []));
        Assert.Equal(settings.FocusCovenants, loaded.FocusCovenants);
        Assert.Equal(settings.FocusOperators, loaded.FocusOperators);
        foreach (var invalid in new[]
        {
            settings with { FocusCovenants = ["不存在"] },
            settings with { FocusOperators = ["不存在"] }
        })
            Assert.Equal("关注的盟约或干员无效。", Assert.Throws<ArgumentException>(() => api.SaveSettings(invalid)).Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
