using System.Text.Json;
using ASPAssistant.Core.Models;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class MemberEquipmentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
    private static LineupDto Input() => new("", "候选装备阵容", [new("卡达")
    {
        Equipment = [new("虹彩活血饮料", true), new("随身身份牌", false), new("虹彩活血饮料", false)]
    }], "user", null, default);

    [Fact]
    public async Task SaveReloadAndBackupKeepFirstEquipmentAndPriority()
    {
        var api = await CreateApi();
        var saved = Assert.IsType<LineupDto>(await api.InvokeAsync("saveLineup",
            [JsonSerializer.SerializeToElement(Input(), HostApi.JsonOptions)]));
        MemberEquip[] expected = [new("虹彩活血饮料", true), new("随身身份牌", false)];
        Assert.Equal(expected, saved.Members[0].Equipment);
        var restarted = await CreateApi();
        Assert.Equal(expected, Assert.Single(await restarted.LineupsAsync()).Members[0].Equipment);
        var path = Path.Combine(_directory, "backup.json");
        await restarted.ExportBackupAsync(path);
        await restarted.DeleteLineupAsync(saved.Id);
        var selection = await restarted.ReadBackupAsync(path);
        await restarted.ImportDataAsync(selection.Token, "replace");
        Assert.Equal(expected, Assert.Single(await restarted.LineupsAsync()).Members[0].Equipment);
    }

    [Fact]
    public async Task UnknownEquipmentIsRejectedAndLegacyMemberReturnsEmptyArray()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "lineups"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "lineups", "legacy.json"),
            """{"id":"legacy","name":"旧阵容","slots":[{"operatorName":"卡达"}],"goals":[]}""");
        var api = await CreateApi();
        var legacy = Assert.Single(await api.LineupsAsync());
        Assert.Empty(legacy.Members[0].Equipment);
        Assert.Equal(0, JsonSerializer.SerializeToElement(legacy, HostApi.JsonOptions)
            .GetProperty("members")[0].GetProperty("equipment").GetArrayLength());
        await Assert.ThrowsAsync<ArgumentException>(() => api.SaveLineupAsync(Input() with
        {
            Members = [new("卡达") { Equipment = [new("不存在的装备", true)] }]
        }));
        Assert.Single(await api.LineupsAsync());
    }

    [Fact]
    public async Task CatalogHasEquipmentSlotsAndBothKindsOfEquipmentSources()
    {
        var api = await CreateApi();
        var catalog = (JsonElement)(await api.InvokeAsync("catalog", []))!;
        var operators = catalog.GetProperty("operators").EnumerateArray().ToDictionary(o => o.GetProperty("name").GetString()!);
        foreach (var (name, normal, elite) in new[] { ("暴躁铁皮", 4, 6), ("卡达", 2, 2) })
        {
            var slots = operators[name].GetProperty("equipSlots");
            Assert.Equal(normal, slots.GetProperty("normal").GetInt32());
            Assert.Equal(elite, slots.GetProperty("elite").GetInt32());
        }
        var equipment = catalog.GetProperty("equipment").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
        foreach (var (name, gift) in new[] { ("承曦格雷伊", "精准狙击镜"), ("奥尔佳", "乌萨斯呼吸器") })
        {
            Assert.Contains(name, equipment[gift].GetProperty("sources").EnumerateArray().Select(x => x.GetString()));
            Assert.Contains(gift, operators[name].GetProperty("givesEquipment").EnumerateArray().Select(x => x.GetString()));
        }
        Assert.Equal(new[] { "维式重锤" }, operators["凯瑟琳"].GetProperty("givesEquipment").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(new[] { "盟约之币", "萨尔贡浓茶", "拟态物质" }, operators["异客"].GetProperty("givesEquipment").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task ShopMarksOnlyTrackedPriorityEquipmentInTargetLineup()
    {
        var api = await CreateApi();
        var lineup = await api.SaveLineupAsync(Input());
        await api.SaveSessionAsync(new(lineup.Id, [], []) { TrackedEquipment = ["虹彩活血饮料", "随身身份牌"] });
        var priority = new ShopItem { Name = "虹彩活血饮料" };
        var candidate = new ShopItem { Name = "随身身份牌" };
        api.UpdateShopTracking([priority, candidate]);
        Assert.True(priority.IsTracked);
        Assert.True(candidate.IsTracked);
        Assert.True(priority.IsKey);
        Assert.False(candidate.IsKey);
        await api.SaveSessionAsync(new(lineup.Id, [], []) { TrackedEquipment = ["随身身份牌"] });
        api.UpdateShopTracking([priority]);
        Assert.False(priority.IsKey);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
