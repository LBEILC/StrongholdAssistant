using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class HostCompletionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);

    [Fact]
    public async Task CopyTextRejectsOverlongText()
    {
        var api = await CreateApi();
        await Assert.ThrowsAsync<ArgumentException>(() => api.InvokeAsync("copyText", [JsonSerializer.SerializeToElement(new string('a', 2001))]));
    }

    [Fact]
    public async Task ResetClearsManualAndAccumulatedBansAndPreservesTrackingAndLineups()
    {
        var api = await CreateApi();
        var lineup = await api.SaveLineupAsync(new("", "测试", [new("初雪")], "user", null, default));
        await api.SaveSessionAsync(new(lineup.Id, [], ["初雪"]));
        var before = JsonSerializer.Serialize(await api.SessionAsync());
        api.ApplyRecognizedMatch(api.Match with { StrategyId = "test", BossId = "boss", EnemyTypes = ["enemy"] }, ["卡达"]);
        api.SetCovenantIncomplete("siracusa", true);
        api.SetOperatorBanned("初雪", true);
        MatchInfo? pushed = null;
        api.Changed += (name, value) => { if (name == "match") pushed = (MatchInfo)value; };
        await api.InvokeAsync("resetMatch", []);
        var match = Assert.IsType<MatchInfo>(await api.InvokeAsync("match", []));
        Assert.Equal("none", match.Source);
        Assert.Null(match.StrategyId);
        Assert.Null(match.BossId);
        Assert.Null(match.RecognizedAt);
        Assert.Empty(match.EnemyTypes);
        Assert.Empty(match.BannedOperators);
        Assert.All(match.Covenants, c => Assert.False(c.Incomplete));
        Assert.Same(match, pushed);
        Assert.Equal(before, JsonSerializer.Serialize(await api.SessionAsync()));
        Assert.Equal(lineup.Id, Assert.Single(await api.LineupsAsync()).Id);
        Assert.Equal(new[] { "初雪" }, api.TrackedNames);
        Assert.False(File.Exists(Path.Combine(_directory, "match.json")));
        api.SetOperatorBanned("夜莺", false);
        Assert.Empty(api.Match.BannedOperators);
    }

    [Fact]
    public async Task StrategyDefaultsToNullRejectsInvalidAndSurvivesBackup()
    {
        var store = new LineupStore(Path.Combine(_directory, "lineups"));
        Directory.CreateDirectory(Path.Combine(_directory, "lineups"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "lineups", "old.json"), "{\"id\":\"old\",\"name\":\"旧阵容\",\"slots\":[]}");
        var api = await CreateApi();
        var old = Assert.Single(await api.LineupsAsync());
        Assert.Null(old.StrategyId);
        Assert.Null(Assert.Single(await store.LoadAllAsync()).StrategyId);
        await Assert.ThrowsAsync<ArgumentException>(() => api.SaveLineupAsync(old with { StrategyId = "invalid" }));
        var strategy = api.Catalog.GetProperty("strategies")[0].GetProperty("id").GetString();
        await api.SaveLineupAsync(old with { StrategyId = strategy });
        var backup = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(backup);
        var selection = await api.ReadBackupAsync(backup);
        await api.SaveLineupAsync(old);
        await api.ImportDataAsync(selection.Token, "replace");
        Assert.Equal(strategy, Assert.Single(await api.LineupsAsync()).StrategyId);
    }

    [Fact]
    public async Task ConcurrentLineupAndSessionReadsAndWritesKeepCompleteFiles()
    {
        var api = await CreateApi();
        var lineup = await api.SaveLineupAsync(new("", "测试", [new("初雪")], "user", null, default));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            await api.SaveLineupAsync(lineup with { Note = new string((char)('a' + i), 10000) });
            Assert.Single(await api.LineupsAsync());
            await api.SaveSessionAsync(new(lineup.Id, [], ["初雪"]));
            Assert.Equal(lineup.Id, (await api.SessionAsync()).TargetLineupId);
        }));
        var saved = Assert.Single(await api.LineupsAsync());
        Assert.Equal(10000, saved.Note!.Length);
        Assert.Single(saved.Note.Distinct());
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_directory, "lineups", lineup.Id + ".json")));
        Assert.Equal(saved.Note, json.RootElement.GetProperty("note").GetString());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
