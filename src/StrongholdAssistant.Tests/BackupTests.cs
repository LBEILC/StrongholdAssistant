using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Models;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class BackupTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
    private string BackupPath => Path.Combine(_directory, "export.json");
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, HostApi.JsonOptions);
    private static Task<LineupDto> AddLineup(HostApi api, string name) => api.SaveLineupAsync(
        new("", name, [new("折光", "yan", true)], "user", "备注", DateTime.UtcNow)
        { Goals = [new("yan", 1)], Equipment = ["虹彩活血饮料"] });

    [Fact]
    public async Task ReplaceRoundTripPreservesLineupsSettingsSessionAndDock()
    {
        var api = await CreateApi();
        var lineup = await AddLineup(api, "原阵容");
        var settings = new SettingsDto("closure", false, false)
        {
            AlwaysOnTop = true, UiScale = JsonSerializer.SerializeToElement(1.5),
            FollowGame = false, CollapseMode = "up",
            FocusCovenants = ["yan"], FocusOperators = ["折光"]
        };
        api.SaveSettings(settings);
        api.SettingsStore.SaveDock(new(420, new SidePanelOffset(12, 34), new SidePanelOffset(56, 78)));
        await api.SaveSessionAsync(new(lineup.Id, ["折光"], ["车尔尼"]));
        var session = await File.ReadAllTextAsync(Path.Combine(_directory, "session.json"));
        var dock = api.SettingsStore.LoadDock();
        await api.ExportBackupAsync(BackupPath);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(BackupPath))!.AsObject();
        Assert.Equal(new[] { "format", "version", "appVersion", "exportedAt", "settings", "lineups", "history" }, root.Select(p => p.Key));
        Assert.Equal("stronghold-assistant-backup", root["format"]!.GetValue<string>());
        Assert.Equal(1, root["version"]!.GetValue<int>());
        Assert.Equal(15, root["settings"]!.AsObject().Count);
        var selection = await api.ReadBackupAsync(BackupPath);
        Assert.Equal(1, selection.Summary.Lineups);
        Assert.Equal(1, selection.Summary.FocusCovenants);
        Assert.Equal(1, selection.Summary.FocusOperators);
        api.SaveSettings(new("terminal", true, true));
        await api.SaveLineupAsync(lineup with { Name = "修改过" });
        await api.ImportDataAsync(selection.Token, "replace");
        Assert.Equal(Json(lineup), Json(Assert.Single(await api.LineupsAsync())));
        Assert.Equal(Json(settings), Json(api.Settings));
        Assert.Equal(session, await File.ReadAllTextAsync(Path.Combine(_directory, "session.json")));
        Assert.Equal(dock, api.SettingsStore.LoadDock());
    }

    [Fact]
    public async Task MergeOverwritesSameIdAddsNewIdAndUnionsFocusWithoutChangingPreferences()
    {
        var api = await CreateApi();
        var same = await AddLineup(api, "备份名称");
        var added = await AddLineup(api, "备份新增");
        api.SaveSettings(new("closure", false, false)
        { FocusCovenants = ["yan"], FocusOperators = ["折光"] });
        await api.ExportBackupAsync(BackupPath);
        var selection = await api.ReadBackupAsync(BackupPath);
        await api.DeleteLineupAsync(added.Id);
        await api.SaveLineupAsync(same with { Name = "本地名称" });
        var retained = await AddLineup(api, "本地保留");
        var current = new SettingsDto("ursus", true, true)
        {
            UiScale = JsonSerializer.SerializeToElement(2), AlwaysOnTop = true,
            FocusCovenants = ["yan", "siracusa"], FocusOperators = ["车尔尼"]
        };
        api.SaveSettings(current);
        await api.SaveSessionAsync(new(same.Id, ["折光"], ["车尔尼"]));
        var session = Json(await api.SessionAsync());
        await api.ImportDataAsync(selection.Token, "merge");
        var lineups = await api.LineupsAsync();
        Assert.Equal(3, lineups.Length);
        Assert.Equal(Json(same), Json(lineups.Single(l => l.Id == same.Id)));
        Assert.Contains(lineups, l => l.Id == added.Id);
        Assert.Contains(lineups, l => l.Id == retained.Id);
        Assert.Equal(Json(current with { FocusOperators = ["车尔尼", "折光"] }), Json(api.Settings));
        Assert.Equal(session, Json(await api.SessionAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidFormatOrUnknownOperatorUsesSpecifiedError(bool unknownOperator)
    {
        var api = await CreateApi();
        await AddLineup(api, "阵容");
        await api.ExportBackupAsync(BackupPath);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(BackupPath))!;
        if (unknownOperator) root["lineups"]![0]!["members"]![0]!["name"] = "不存在的干员";
        else root["format"] = "other";
        await File.WriteAllTextAsync(BackupPath, root.ToJsonString());
        var error = await Assert.ThrowsAsync<ArgumentException>(() => api.ReadBackupAsync(BackupPath));
        Assert.Equal("这不是有效的备份文件，或者来自不兼容的版本。", error.Message);
    }

    [Fact]
    public async Task ImportBacksUpCurrentDataAndClearsOnlyMissingTarget()
    {
        var api = await CreateApi();
        api.SaveSettings(new("terminal", true, true));
        await api.ExportBackupAsync(BackupPath);
        var selection = await api.ReadBackupAsync(BackupPath);
        var old = await AddLineup(api, "导入前阵容");
        api.SaveSettings(new("ursus", false, false) { FocusOperators = ["折光"] });
        var settings = Json(api.Settings);
        await api.SaveSessionAsync(new(old.Id, ["折光"], ["车尔尼"]));
        await api.ImportDataAsync(selection.Token, "replace");
        var safetyPath = Assert.Single(Directory.GetFiles(Path.Combine(_directory, "backups"), "导入前-*.json"));
        var root = JsonNode.Parse(await File.ReadAllTextAsync(safetyPath))!;
        Assert.Equal(settings, root["settings"]!.ToJsonString());
        Assert.Equal(Json(old), root["lineups"]![0]!.ToJsonString());
        Assert.Empty(await api.LineupsAsync());
        var session = await api.SessionAsync();
        Assert.Null(session.TargetLineupId);
        Assert.Equal(new[] { "折光" }, session.Owned);
        Assert.Equal(new[] { "车尔尼" }, session.Tracked);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
