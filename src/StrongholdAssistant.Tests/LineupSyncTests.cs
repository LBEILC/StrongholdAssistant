using System.Text.Json;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class LineupSyncTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, HostApi.JsonOptions);

    [Fact]
    public async Task FullLineupAndCloudStateSurviveSaveReloadAndBackup()
    {
        var api = await CreateApi();
        LineupMember[] members = [new("卡达", "siracusa", true)
            { Elite = true, Skill = "skchr_cuttle_2", Module = "", Equipment = [new("精打细算玩偶", true)] }, new("初雪")];
        var saved = await api.SaveLineupAsync(new("", "分阶段", members, "user", null, default)
        {
            Stages = [new("开局", 1, 4, [new("siracusa", 1)], members, ["精打细算玩偶"])],
            Description = "阶段和养成", Goals = [new("empty", 1)]
        });
        var cloud = new CloudSyncState(true, "2026-11-12T00:00:00Z", "2026-11-12T01:00:00Z",
            "2026-11-12T02:00:00Z", [new("removed", "2026-11-12T03:00:00Z")]);
        api.SaveSettings(api.Settings with { CloudSync = cloud });
        api = await CreateApi();
        Assert.Equal(Json(saved), Json(Assert.Single(await api.LineupsAsync())));
        Assert.Equal(Json(cloud), Json(api.Settings.CloudSync));
        Assert.Contains("\"module\":\"\"", Json(saved));
        Assert.DoesNotContain("module", Json(saved.Members[1]));
        await api.SaveSessionAsync(new(saved.Id, [], ["卡达"]) { Stage = 0 });
        Assert.Equal(new[] { "卡达" }, api.TrackedNames);
        Assert.Contains("伺夜", api.SecondaryNames);
        Assert.DoesNotContain("卡达", api.SecondaryNames);
        Assert.Equal(0, (await api.SessionAsync()).Stage);
        string backup = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(backup);
        await api.DeleteLineupAsync(saved.Id);
        api.SaveSettings(api.Settings with { CloudSync = new(false, null, null, null, []) });
        await api.ImportDataAsync((await api.ReadBackupAsync(backup)).Token, "replace");
        var imported = Assert.Single(await api.LineupsAsync());
        Assert.True(imported.UpdatedAt > saved.UpdatedAt);
        Assert.Equal(Json(saved with { UpdatedAt = imported.UpdatedAt }), Json(imported));
        Assert.Equal(Json(new CloudSyncState(false, null, null, null, [])), Json(api.Settings.CloudSync));
        var badStage = saved.Stages![0] with { Members = [new("卡达") { Equipment = [new("不存在", false)] }] };
        await Assert.ThrowsAsync<ArgumentException>(() => api.SaveLineupAsync(saved with { Stages = [badStage] }));
        await Assert.ThrowsAsync<ArgumentException>(() => api.SaveLineupAsync(saved with
            { Stages = [badStage with { Members = [new("卡达", "不存在")] }] }));
    }

    [Fact]
    public async Task LegacyFilesAndPutPreserveIdentityTimeAndRemoveTarget()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "lineups"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "settings.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_directory, "lineups", "legacy.json"),
            """{"id":"legacy","name":"旧阵容","goals":[],"slots":[{"operatorName":"初雪"}]}""");
        var api = await CreateApi();
        var old = Assert.Single(await api.LineupsAsync());
        Assert.Null(old.Stages);
        Assert.Null(old.Members[0].Elite);
        Assert.Null(old.Members[0].Skill);
        Assert.Null(old.Members[0].Module);
        Assert.Equal("{\"enabled\":false,\"since\":null,\"pushedAt\":null,\"prefsAt\":null,\"deleted\":[],\"account\":null}", Json(api.Settings.CloudSync));
        await api.SaveSessionAsync(new(old.Id, [], ["初雪"]));
        var time = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var incoming = old with { Id = "cloud", UpdatedAt = time };
        var result = Assert.IsType<LineupDto[]>(await api.InvokeAsync("putLineups",
            [JsonSerializer.SerializeToElement(new[] { incoming }, HostApi.JsonOptions), JsonSerializer.SerializeToElement(new[] { old.Id })]));
        Assert.Equal(Json(incoming), Json(Assert.Single(result)));
        Assert.Null((await api.SessionAsync()).TargetLineupId);
        await api.PutLineupsAsync([incoming with { Name = "云端更新" }], []);
        var reloaded = Assert.Single(await (await CreateApi()).LineupsAsync());
        Assert.Equal(time, reloaded.UpdatedAt);
        Assert.Equal("云端更新", reloaded.Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
