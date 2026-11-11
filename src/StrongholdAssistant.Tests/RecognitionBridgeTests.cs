using System.Text.Json;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class RecognitionBridgeTests(EnemyRecognitionFixture fixture) : IClassFixture<EnemyRecognitionFixture>
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    private async Task<HostApi> CreateApi(string image)
    {
        var api = await HostApi.CreateAsync(Data, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        await api.InitializeRecognitionAsync(Data, fixture.Engine,
            async () => await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", image)));
        return api;
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
            $"新识别到 {match.BannedOperators.Length} 名禁用干员，共 {match.BannedOperators.Length} 名。禁用列表没显示全时，往下滚动后再点「重新识别」。", true), events[2].Value);
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
        Assert.Equal("当前画面不是本局信息页，请切换到“确认本局信息”后重试。", error.Message);
    }
}
