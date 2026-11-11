using System.Text.Json;
using System.Text.Json.Nodes;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class EquipmentTrackingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);

    [Fact]
    public async Task LegacyFilesAndBackupReturnEmptyEquipmentArrays()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "lineups"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "lineups", "legacy.json"),
            """{"id":"legacy","name":"旧阵容","slots":[],"goals":[]}""");
        await File.WriteAllTextAsync(Path.Combine(_directory, "session.json"),
            """{"targetLineupId":"legacy","owned":[],"tracked":[]}""");
        var api = await CreateApi();
        var lineup = Assert.Single(await api.LineupsAsync());
        Assert.Empty(lineup.Equipment);
        Assert.Empty((await api.SessionAsync()).TrackedEquipment);
        Assert.Equal(JsonValueKind.Array, JsonSerializer.SerializeToElement(lineup, HostApi.JsonOptions).GetProperty("equipment").ValueKind);
        Assert.Equal(JsonValueKind.Array, JsonSerializer.SerializeToElement(await api.SessionAsync(), HostApi.JsonOptions).GetProperty("trackedEquipment").ValueKind);

        var path = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(path);
        var backup = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        backup["lineups"]![0]!.AsObject().Remove("equipment");
        await File.WriteAllTextAsync(path, backup.ToJsonString());
        var selection = await api.ReadBackupAsync(path);
        await api.ImportDataAsync(selection.Token, "replace");
        Assert.Empty(Assert.Single(await api.LineupsAsync()).Equipment);
    }

    [Fact]
    public async Task SaveRejectsUnknownEquipmentInLineupAndSession()
    {
        var api = await CreateApi();
        await Assert.ThrowsAsync<ArgumentException>(() => api.InvokeAsync("saveLineup",
            [JsonSerializer.SerializeToElement(new LineupDto("", "阵容", [], "user", null, default)
                { Equipment = ["不存在的装备"] }, HostApi.JsonOptions)]));
        await Assert.ThrowsAsync<ArgumentException>(() => api.InvokeAsync("saveSession",
            [JsonSerializer.SerializeToElement(new SessionDto(null, [], [])
                { TrackedEquipment = ["不存在的装备"] }, HostApi.JsonOptions)]));
        Assert.Empty(await api.LineupsAsync());
        Assert.False(File.Exists(Path.Combine(_directory, "session.json")));
    }

    [Fact]
    public async Task TrackingOnlyIncludesExplicitListsAfterReloadAndRefresh()
    {
        var api = await CreateApi();
        var lineup = await api.SaveLineupAsync(new("", "阵容", [new("卡达")], "user", null, default)
            { Equipment = ["虹彩活血饮料"] });
        await api.SaveSessionAsync(new(lineup.Id, [], ["慕斯"])
            { TrackedEquipment = ["随身身份牌"] });
        var restarted = await CreateApi();
        Assert.Equal(new[] { "慕斯", "随身身份牌" }, restarted.TrackedNames);
        Assert.Equal(lineup.Equipment, Assert.Single(await restarted.LineupsAsync()).Equipment);
        Assert.DoesNotContain("虹彩活血饮料", restarted.SecondaryNames);
        Assert.DoesNotContain("随身身份牌", restarted.SecondaryNames);
        await restarted.SaveSessionAsync(new(lineup.Id, [], []) { TrackedEquipment = ["随身身份牌"] });
        await restarted.SaveLineupAsync(lineup with { Equipment = [] });
        Assert.Equal(new[] { "随身身份牌" }, restarted.TrackedNames);
    }

    [Fact]
    public async Task LegacySessionMergesTargetOnlyOnce()
    {
        var api = await CreateApi();
        var lineup = await api.SaveLineupAsync(new("", "阵容", [new("卡达"), new("慕斯")], "user", null, default)
            { Equipment = ["虹彩活血饮料"] });
        var path = Path.Combine(_directory, "session.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            { targetLineupId = lineup.Id, owned = new[] { "卡达" }, tracked = new[] { "卡达", "角峰" } }));
        var migrated = await CreateApi();
        var session = await migrated.SessionAsync();
        Assert.Equal(new[] { "卡达", "角峰", "慕斯" }, session.Tracked);
        Assert.Equal(new[] { "虹彩活血饮料" }, session.TrackedEquipment);
        Assert.True(JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject().ContainsKey("trackedEquipment"));
        await migrated.SaveSessionAsync(session with { Tracked = ["角峰"], TrackedEquipment = [] });
        var saved = await File.ReadAllTextAsync(path);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var restarted = await CreateApi();
        Assert.Equal(new[] { "角峰" }, restarted.TrackedNames);
        Assert.Empty((await restarted.SessionAsync()).TrackedEquipment);
        Assert.Equal(saved, await File.ReadAllTextAsync(path));
        Assert.Equal(2020, File.GetLastWriteTimeUtc(path).Year);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
