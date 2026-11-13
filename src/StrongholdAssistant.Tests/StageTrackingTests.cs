using System.Text.Json;
using ASPAssistant.Core.Models;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class StageTrackingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));

    private async Task<HostApi> CreateApi()
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        var lineup = await api.SaveLineupAsync(new("", "阶段标记", [new("卡达", Key: true)], "user", null, default)
        {
            Goals = [new("yan", 1)],
            Stages =
            [
                new("前期", 1, 4, [new("empty", 1)],
                    [new("初雪", Key: true) { Equipment = [new("虹彩活血饮料", true)] }, new("卡达")], []),
                new("中期", 5, 8, [new("siracusa", 1)],
                    [new("慕斯", Key: true) { Equipment = [new("随身身份牌", true)] }], [])
            ]
        });
        await api.SaveSessionAsync(new(lineup.Id, [], ["初雪", "慕斯", "卡达"])
            { TrackedEquipment = ["虹彩活血饮料", "随身身份牌"] });
        return api;
    }

    private static void AssertStage(HostApi api, string goal, params string[] keys)
    {
        var expected = api.Catalog.GetProperty("operators").EnumerateArray()
            .Where(o => o.GetProperty("obtain").ValueKind == JsonValueKind.Null
                && o.GetProperty("core").EnumerateArray().Concat(o.GetProperty("extra").EnumerateArray())
                    .Any(c => c.GetString() == goal))
            .Select(o => o.GetProperty("name").GetString()!).Except(api.TrackedNames);
        Assert.Equal(expected.Order(), api.SecondaryNames.Order());
        var items = api.TrackedNames.Concat(api.SecondaryNames).Select(n => new ShopItem { Name = n }).ToArray();
        api.UpdateShopTracking(items);
        Assert.Equal(keys.Order(), items.Where(i => i.IsKey).Select(i => i.Name).Order());
        Assert.All(items, i => Assert.Equal(api.SecondaryNames.Contains(i.Name), i.IsSecondary));
    }

    [Fact]
    public async Task AutomaticStagesRefreshCandidatesAndKeysWhenRoundChanges()
    {
        var api = await CreateApi();
        AssertStage(api, "yan", "卡达");
        int changed = 0;
        api.ShopChanged += () => changed++;
        api.ObserveEconomy(new(4, 10, 1, 5, true));
        api.ObserveEconomy(new(4, 10, 1, 5, true));
        Assert.Equal(1, changed);
        // 本阶段已有的卡达不被后续关键标记覆盖；慕斯提前标记，但装备不提前带入。
        AssertStage(api, "empty", "初雪", "慕斯", "虹彩活血饮料");
        api.ObserveEconomy(new(4, 9, 1, 5, true));
        api.ObserveEconomy(new(4, 9, 1, 5, true));
        Assert.Equal(1, changed);
        api.ObserveEconomy(new(5, 10, 1, 5, true));
        api.ObserveEconomy(new(5, 10, 1, 5, true));
        Assert.Equal(2, changed);
        AssertStage(api, "siracusa", "慕斯", "卡达", "随身身份牌");
        api.ObserveEconomy(new(9, 10, 1, 5, true));
        api.ObserveEconomy(new(9, 10, 1, 5, true));
        Assert.Equal(3, changed);
        AssertStage(api, "yan", "卡达");
    }

    [Fact]
    public async Task FixedStageOverridesRoundAndSessionChangesRefreshMarkers()
    {
        var api = await CreateApi();
        api.ObserveEconomy(new(9, 10, 1, 5, true));
        api.ObserveEconomy(new(9, 10, 1, 5, true));
        var session = await api.SessionAsync();
        await api.SaveSessionAsync(session with { Stage = 0 });
        AssertStage(api, "empty", "初雪", "慕斯", "虹彩活血饮料");
        api.ObserveEconomy(new(10, 10, 1, 5, true));
        api.ObserveEconomy(new(10, 10, 1, 5, true));
        AssertStage(api, "empty", "初雪", "慕斯", "虹彩活血饮料");
        await api.SaveSessionAsync(session with { Stage = 1 });
        AssertStage(api, "siracusa", "慕斯", "卡达", "随身身份牌");
        await api.SaveSessionAsync(session with { Stage = 2 });
        AssertStage(api, "yan", "卡达");
        api.ObserveEconomy(new(5, 10, 1, 5, true));
        api.ObserveEconomy(new(5, 10, 1, 5, true));
        await api.SaveSessionAsync(session with { Stage = 3 });
        AssertStage(api, "siracusa", "慕斯", "卡达", "随身身份牌");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
