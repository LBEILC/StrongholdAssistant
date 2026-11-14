using System.Drawing;
using System.Text.Json;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class RecognitionBridgeTests(EnemyRecognitionFixture fixture)
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    private async Task<HostApi> CreateApi(string image)
    {
        var api = await HostApi.CreateAsync(Data, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        await api.InitializeRecognitionAsync(Data, fixture.Engine,
            async () => await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", image)));
        return api;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScrolledSampleRecordsEnemiesAndOnlyPreservesSameMatch(bool same)
    {
        // 保留已有滚动页的换局与同局合并覆盖。
        var api = await CreateApi("info-1610-bans-1600x1024.png");
        api.ApplyRecognizedMatch(api.Match with { BossId = "enemy_9021_acduml",
            EnemyTypes = same ? ["INVISIBLE", "DOT"] : ["CRITIC", "DOT", "REFLECTION"] }, ["初雪"]);
        api.SetCovenantIncomplete("kjerag", true);
        var before = api.Match;

        var match = await api.RecordInformationAsync();

        Assert.Equal("enemy_9021_acduml", match.BossId);
        Assert.Equal(new[] { "DOT", "INVISIBLE" }, match.EnemyTypes);
        Assert.Contains("佩佩", match.BannedOperators);
        if (same)
        {
            Assert.Equal(before.Covenants, match.Covenants);
            Assert.All(before.BannedOperators, name => Assert.Contains(name, match.BannedOperators));
        }
        else
        {
            Assert.Empty(match.Covenants);
            Assert.DoesNotContain("初雪", match.BannedOperators);
            // 25 个直接命中头像，加上既有格位顺序规则补出的异客。
            Assert.Equal(26, match.BannedOperators.Length);
        }
    }

    [Fact]
    public async Task NewMatchSampleClearsPreviousBansAndSlots()
    {
        var api = await CreateApi("info-scrolled-new-match-1920x1080.png");
        var expected = await api.RecordInformationAsync();
        Assert.NotEmpty(expected.BannedOperators);
        Assert.Equal("enemy_9021_acduml", expected.BossId);
        Assert.Equal(new[] { "DOT", "REFLECTION", "ELEMENT" }.Order(), expected.EnemyTypes.Order());
        Assert.Empty(expected.Covenants);
        var oldNames = api.Catalog.GetProperty("operators").EnumerateArray()
            .Select(o => o.GetProperty("name").GetString()!)
            .Except(expected.BannedOperators).Take(2).ToArray();
        var grid = new BanAvatarResult(
            [new(new Rectangle(0, 200, 100, 100), oldNames[0], "萨尔贡", oldNames[0], .9, .2)
                { SlotIndex = 0, VisibleIndex = 0, Tier = 6 }], 1920, 1080, 0, 0, 0);
        api.ApplyRecognizedMatch(api.Match with { EnemyTypes = ["CRITIC", "DOT", "REFLECTION"] }, [], grid);
        api.SetOperatorBanned(oldNames[1], true);
        api.SetCovenantIncomplete("kjerag", true);
        Assert.All(oldNames, name => Assert.Contains(name, api.Match.BannedOperators));
        Assert.NotEmpty(api.Match.BanGroups);

        var match = await api.RecordInformationAsync();

        Assert.Equal(expected.BossId, match.BossId);
        Assert.Equal(expected.EnemyTypes, match.EnemyTypes);
        Assert.Empty(match.Covenants);
        Assert.Equal(expected.BannedOperators.Order(), match.BannedOperators.Order());
        Assert.Equal(JsonSerializer.Serialize(expected.BanGroups), JsonSerializer.Serialize(match.BanGroups));
        Assert.All(oldNames, name => Assert.DoesNotContain(name, match.BannedOperators));
    }

    [Fact]
    public async Task InGameSampleReplacesMatchWithRecognizedIdsAndClearsManualBans()
    {
        var api = await CreateApi("info-ingame-1920x1080.png");
        var operators = api.Catalog.GetProperty("operators").EnumerateArray()
            .Select(o => o.GetProperty("name").GetString()!).ToHashSet();
        foreach (var name in operators) api.SetOperatorBanned(name, false);
        var events = new List<(string Name, object Value)>();
        api.Changed += (name, value) => events.Add((name, value));

        var match = Assert.IsType<MatchInfo>(await api.InvokeAsync("rescan", []));

        var coreIds = api.Catalog.GetProperty("covenants").EnumerateArray()
            .Where(c => c.GetProperty("kind").GetString() == "core")
            .Select(c => c.GetProperty("id").GetString()).ToHashSet();
        Assert.Equal(10, match.Covenants.Count(c => coreIds.Contains(c.Id)));
        Assert.Equal(16, match.Covenants.Count(c => !coreIds.Contains(c.Id)));
        Assert.Equal(new[] { "victoria", "kjerag", "egir", "kazimierz", "ursus", "preci", "skillful", "stead", "visi", "mira" }.Order(),
            match.Covenants.Where(c => c.Incomplete).Select(c => c.Id).Order());
        Assert.Equal("enemy_1521_dslily", match.BossId);
        Assert.Equal(new[] { "REBORN", "INVISIBLE", "DOT" }.Order(), match.EnemyTypes.Order());
        Assert.NotEmpty(match.BannedOperators);
        Assert.All(match.BannedOperators, name => Assert.Contains(name, operators));
        Assert.Equal("recognized", match.Source);
        Assert.NotNull(match.RecognizedAt);
        Assert.True(DateTimeOffset.TryParse(match.RecognizedAt, out _));
        Assert.Null(match.StrategyId);
        Assert.Same(match, api.Match);
        Assert.Equal(new[] { "scan", "match", "scan" }, events.Select(e => e.Name));
        Assert.Equal(new ScanStatus(true), events[0].Value);
        Assert.Equal(new ScanStatus(false,
            $"新识别到 {match.BannedOperators.Length} 名禁用干员，共 {match.BannedOperators.Length} 名。", true), events[2].Value);
        var covenant = match.Covenants[0];
        Assert.Equal(match.BannedOperators.Order(), api.SetCovenantIncomplete(covenant.Id, covenant.Incomplete).BannedOperators.Order());
    }

    [Fact]
    public async Task ShopSamplePreservesMatchAndPublishesAnalyzerFailure()
    {
        var api = await CreateApi("shop-tier2-color.png");
        api.SetCovenantIncomplete("victoria", true);
        var before = api.Match;
        var events = new List<(string Name, object Value)>();
        api.Changed += (name, value) => events.Add((name, value));

        var error = await Assert.ThrowsAsync<RecognitionException>(() => api.InvokeAsync("rescan", []));

        Assert.Same(before, api.Match);
        Assert.Equal(new[] { "scan", "scan" }, events.Select(e => e.Name));
        Assert.Equal(new ScanStatus(true), events[0].Value);
        Assert.Equal(new ScanStatus(false, error.Message, false), events[1].Value);
        Assert.StartsWith("当前画面不是本局信息页，请打开开局信息页或对局详情后重试。\n", error.Message);
        Assert.Contains("OCR (", error.Message);
        Assert.Contains("UI 缩放", error.Message);
        Assert.Single(Directory.GetFiles(Path.Combine(api.DataDirectory, "diagnostics"), "*.png"));
        Assert.Single(Directory.GetFiles(Path.Combine(api.DataDirectory, "diagnostics"), "*.json"));
    }
}
