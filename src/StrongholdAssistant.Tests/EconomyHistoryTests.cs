using System.Text.Json;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class EconomyHistoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
    private static void Open(HostApi api, string? strategy = null)
    {
        api.ApplyRecognizedMatch(api.Match with { Mode = new("mode_multi_hard", "联机困难") }, [], opening: true);
        api.SetMatchStrategy(strategy);
    }
    private static void Read(HostApi api, int round, int? money) => api.ObserveEconomy(new(round, money, 1, 5, true));

    private static SklandRecord Skland(string id) => new(id, null, [], [], true,
        "2026-11-12T01:00:00Z", "2026-11-12T00:00:00Z", 0, 0, [], [], "SINGLE", "HARD", 15, "1");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoryReadsBothFormatsAndSavesArrayWithSeparateDeletedIds(bool objectFormat)
    {
        Directory.CreateDirectory(_directory);
        var record = new MatchRecord("kept", "2026-11-12T00:00:00Z", null, Skland("kept"));
        var path = Path.Combine(_directory, "history.json");
        var deletedPath = Path.Combine(_directory, "history-deleted-skland.json");
        var records = new[] { record };
        await File.WriteAllTextAsync(path, objectFormat
            ? JsonSerializer.Serialize(new { records, deletedSklandIds = new[] { "deleted" } }, HostApi.JsonOptions)
            : JsonSerializer.Serialize(records, HostApi.JsonOptions));
        if (!objectFormat) await File.WriteAllTextAsync(deletedPath, "[\"deleted\"]");

        var api = await CreateApi();
        Assert.Equal("kept", Assert.Single(api.History()).Id);
        Assert.Equal("kept", Assert.Single(api.SaveSklandRecords([Skland("deleted")])).Id);
        Assert.Equal("kept", Assert.Single(JsonSerializer.Deserialize<MatchRecord[]>(await File.ReadAllTextAsync(path), HostApi.JsonOptions)!).Id);
        Assert.Equal(new[] { "deleted" }, JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(deletedPath)));
        api.DeleteRecord("kept");
        Assert.Equal(new[] { "deleted", "kept" }, JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(deletedPath))!.Order().ToArray());
        Assert.Empty((await CreateApi()).SaveSklandRecords([Skland("deleted"), Skland("kept")]));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(deletedPath + ".tmp"));
    }

    [Fact]
    public async Task DeletedSklandRecordStaysDeletedAfterReloadAndBackupImport()
    {
        var api = await CreateApi();
        var record = Skland("deleted");
        api.DeleteRecord(Assert.Single(api.SaveSklandRecords([record])).Id);
        api = await CreateApi();
        Assert.Empty(api.SaveSklandRecords([record]));
        string backup = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(backup);
        var other = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), Path.Combine(_directory, "other"));
        other.SaveSklandRecords([record]);
        await other.ImportDataAsync((await other.ReadBackupAsync(backup)).Token, "merge");
        Assert.Empty(other.SaveSklandRecords([record]));
    }

    [Fact]
    public async Task BackupMergeUsesSklandIdentityAndFillsMissingLocalData()
    {
        var api = await CreateApi();
        var local = Assert.Single(api.SaveSklandRecords([Skland("same")]));
        var details = new LocalMatchRecord(null, true, null, [], [], null, null, [], 15, local.Time);
        string backup = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(backup);
        var root = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(backup))!;
        root["history"] = JsonSerializer.SerializeToNode(new[] { local with { Id = "another-device", Local = details } }, HostApi.JsonOptions);
        await File.WriteAllTextAsync(backup, root.ToJsonString());
        // 原数组格式仍能读取，下一次保存时迁移。
        await File.WriteAllTextAsync(Path.Combine(_directory, "history.json"), JsonSerializer.Serialize(new[] { local }, HostApi.JsonOptions));
        api = await CreateApi();
        await api.ImportDataAsync((await api.ReadBackupAsync(backup)).Token, "merge");
        var merged = Assert.Single((await CreateApi()).History());
        Assert.Equal(local.Id, merged.Id);
        Assert.Equal(local.Time, merged.Time);
        Assert.Equal("same", merged.Skland!.Id);
        Assert.Equal(15, merged.Local!.LastRound);
    }

    [Fact]
    public async Task LearnsMinimumSubtractsIncomeExcludesEconomicStrategiesAndPersists()
    {
        var api = await CreateApi();
        Open(api);
        Read(api, 1, 4);
        api.AddIncome("精打细算玩偶", false);
        api.AddIncome("见钱眼开玩偶", false);
        Read(api, 2, 10);
        Assert.Equal(10, api.Match.Economy!.StartMoney);
        Read(api, 2, 2);
        Assert.Equal(10, api.Match.Economy.StartMoney);
        Open(api);
        Read(api, 1, 3);
        Assert.Equal(7, api.Match.Economy!.Next);
        Read(api, 2, 6);
        foreach (var strategy in new[] { "band_cannot", "band_lmlee", "band_mlynar", "band_fund", "band_protect", "band_treasure", "band_ducklord", "band_siracusa" })
        {
            Open(api, strategy);
            Read(api, 1, 0);
            Read(api, 2, 0);
        }
        api = await CreateApi();
        Open(api);
        Read(api, 1, 8);
        Assert.Equal("own", api.Match.Economy!.Basis);
        Assert.Equal(5, api.Match.Economy.Extra);
        Assert.Equal(11, api.Match.Economy.Next);
        Assert.Empty(api.Match.Economy.Incomes);
    }

    [Fact]
    public async Task MidRoundAttachDoesNotLearnAndFirstMoneyAfterTransitionIsRecorded()
    {
        var api = await CreateApi();
        api.ApplyRecognizedMatch(api.Match with { Mode = new("mode_single_1", "规程等级1") }, []);
        Read(api, 8, 2);
        Assert.Null(api.Match.Economy!.StartMoney);
        Assert.Equal("none", api.Match.Economy.Basis);
        Read(api, 9, null);
        Read(api, 9, 12);
        Assert.Equal(12, api.Match.Economy.StartMoney);
        Assert.Equal(new RoundMoney(9, 12), Assert.Single(Assert.Single(api.History()).Local!.Rounds));
        api.ObserveEconomy(new(10, 5, null, null, false));
        Assert.Equal(9, api.Match.Economy.Round);
    }

    [Fact]
    public void AllRulesUseBaseIncomeExtraAndCarryUpdatesWithMoney()
    {
        int? Basis(int round) => round switch { 1 => 4, 2 => 6, 3 => 8, _ => null };
        var e = new Economy(1, 5, 7, 1, 5, "", null, 0, [], null, "none");
        Assert.Equal(9, HostApi.Predict(e, null, Basis).Next);
        Assert.Equal(15, HostApi.Predict(e, "band_cannot", Basis).Next);
        Assert.Equal(13, HostApi.Predict(e with { Money = 4 }, "band_cannot", Basis).Next);
        Assert.Null(HostApi.Predict(e, "band_lmlee", Basis).Next);
        Assert.Equal(18, HostApi.Predict(e with { Round = 2, StartMoney = null }, "band_lmlee", Basis).Next);
        foreach (var strategy in new[] { "band_mlynar", "band_fund" })
            Assert.Equal(9, HostApi.Predict(e, strategy, Basis).Next);
        Assert.Equal("kazimier", HostApi.Predict(e, "band_mlynar", Basis).Rule);
        Assert.Equal("random", HostApi.Predict(e, "band_fund", Basis).Rule);
        Assert.Null(HostApi.Predict(e with { Round = 3 }, null, Basis).Next);
    }

    [Fact]
    public async Task CatalogAndIncomeCommandsHaveCorrectAmountsTimingAndEvents()
    {
        var api = await CreateApi();
        Assert.Equal(new[] {
            new IncomeSource("精打细算玩偶", "equipment", "each", 1, 2),
            new IncomeSource("见钱眼开玩偶", "equipment", "once", 2, 4),
            new IncomeSource("波登可", "sell", "once", 1, 2)
        }, api.Catalog.GetProperty("incomeSources").Deserialize<IncomeSource[]>(HostApi.JsonOptions));
        Open(api);
        Read(api, 1, 4);
        int events = 0;
        api.Changed += (name, _) => { if (name == "match") events++; };
        await api.InvokeAsync("addIncome", [JsonSerializer.SerializeToElement("精打细算玩偶"), JsonSerializer.SerializeToElement(true)]);
        api.AddIncome("波登可", true);
        var incomes = api.Match.Economy!.Incomes;
        Assert.Equal(0, HostApi.IncomeAt(incomes, 1));
        Assert.Equal(4, HostApi.IncomeAt(incomes, 2));
        Assert.Equal(2, HostApi.IncomeAt(incomes, 3));
        await api.InvokeAsync("removeIncome", [JsonSerializer.SerializeToElement(incomes[1].Id)]);
        Assert.Single(api.Match.Economy.Incomes);
        Assert.Equal(3, events);
        Read(api, 2, null);
        api.AddIncome("波登可", false);
        Assert.Equal(2, api.Match.Economy.Incomes.Last().Round);
    }

    [Fact]
    public async Task HistorySnapshotsRoundsResetBackupMergeAndDelete()
    {
        var api = await CreateApi();
        var lineup = await api.SaveLineupAsync(new("", "目标甲", [], "user", null, DateTime.UtcNow));
        await api.SaveSessionAsync(new(lineup.Id, [], []));
        Open(api);
        Read(api, 1, 4);
        Read(api, 2, 8);
        await api.SaveLineupAsync(lineup with { Name = "目标乙" });
        var first = Assert.Single(api.History());
        Assert.Equal("目标甲", first.Local!.Lineup!.Name);
        Assert.Equal(2, first.Local.LastRound);
        Assert.Equal(new[] { new RoundMoney(1, 4), new RoundMoney(2, 8) }, first.Local.Rounds);
        Read(api, 1, 4);
        Assert.Equal(2, api.History().Length);
        Assert.Equal(4, api.Match.Economy!.StartMoney);
        await api.ResetMatchAsync();
        Assert.Equal(2, api.History().Length);
        string backup = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(backup);
        api.DeleteRecord(first.Id);
        var selected = await api.ReadBackupAsync(backup);
        await api.ImportDataAsync(selected.Token, "merge");
        Assert.Equal(2, api.History().Length);
        Assert.Equal(2, (await CreateApi()).History().Length);
        Assert.True(DateTimeOffset.Parse(api.History()[0].Time) >= DateTimeOffset.Parse(api.History()[1].Time));
    }

    [Fact]
    public async Task SklandMergesByTimeAndModeOnlyOnceAndDeduplicates()
    {
        var api = await CreateApi();
        Open(api);
        Read(api, 1, 4);
        var local = Assert.Single(api.History());
        var time = DateTimeOffset.Parse(local.Time);
        SklandRecord Record(string id, string mode, int minutes) => new(id, null, [], [], true,
            time.AddMinutes(minutes).ToString("O"), time.AddMinutes(-1).ToString("O"), 0, 0, [], [], mode, "HARD", 15, "1");
        var matching = Record("match", "MULTI", 1);
        var records = api.SaveSklandRecords([Record("wrong-mode", "SINGLE", 1), Record("too-late", "MULTI", 31),
            Record("too-early", "MULTI", -1), matching, Record("second", "MULTI", 2), matching]);
        Assert.Equal(5, records.Length);
        Assert.Equal("match", Assert.Single(records, r => r.Local is not null).Skland!.Id);
        Assert.All(records.Where(r => r.Local is null), r => Assert.Equal(r.Skland!.StartTime, r.Time));
        Assert.Equal(5, api.SaveSklandRecords([matching]).Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
