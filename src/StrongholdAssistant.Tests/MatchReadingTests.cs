using System.Diagnostics;
using System.Text.Json;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;
using Moq;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class MatchReadingTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static byte[] Frame(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));
    private static Task<HostApi> Api() => HostApi.CreateAsync(Data, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task CovenantBarMergesReadCellsAndResetsForNewMatch()
    {
        var api = await Api();
        api.ApplyCovenantBar([new("yan", null, 8), new("preci", 1, 2)], 4);
        api.ApplyCovenantBar([new("preci", 2, 12)], 5);
        Assert.Equal(2, api.Match.CovenantBar.Length);
        Assert.Contains(api.Match.CovenantBar, c => c.Id == "yan" && c.Layers == 8 && c.Round == 4);
        Assert.Contains(api.Match.CovenantBar, c => c.Id == "preci" && c.Level == 2 && c.Layers == 12 && c.Round == 5);
        Assert.All(api.Match.CovenantBar, c => Assert.True(DateTimeOffset.TryParse(c.At, out _)));
        api.ApplyRecognizedMatch(api.Match, [], opening: true);
        Assert.Empty(api.Match.CovenantBar);
        api.ApplyCovenantBar([new("yan", null, 2)], 1);
        await api.ResetMatchAsync();
        Assert.Empty(api.Match.CovenantBar);
    }

    [Fact]
    public async Task BothStrategySourcesReadMech()
    {
        var api = await Api();
        var reader = new MatchStrategyReader(fixture.Engine, Data, api.Catalog.GetProperty("strategies").EnumerateArray()
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("name").GetString()!));
        var frame = Frame("strategy-board.jpg");
        var clock = Stopwatch.StartNew();
        var portrait = reader.ReadPortrait(frame);
        output.WriteLine($"头像 {clock.Elapsed.TotalMilliseconds:F1} ms；score={portrait.Score:F3} margin={portrait.Margin:F3}");
        Assert.Equal("band_mech", portrait.Id);
        clock.Restart();
        var selection = await reader.ReadSelectionAsync(Frame("strategy-selection.jpg"), 1920, 1080);
        output.WriteLine($"选择页 OCR {clock.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal("band_mech", selection);
    }

    [Fact]
    public async Task EnemyPanelReadsNamesAndCachesUnchangedFrame()
    {
        int calls = 0;
        var measured = new Mock<IOcrEngine>();
        measured.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(async (byte[] bytes, int x, int y, int w, int h) =>
            {
                var timer = Stopwatch.StartNew();
                var result = await fixture.Engine.RecognizeRegionAsync(bytes, x, y, w, h);
                output.WriteLine($"敌人 OCR #{++calls} {timer.Elapsed.TotalMilliseconds:F1} ms");
                return result;
            });
        var reader = new RoundEnemyReader(measured.Object);
        var frame = Frame("board-enemy-panel.png");
        var clock = Stopwatch.StartNew();
        var names = await reader.ReadAsync(frame);
        output.WriteLine($"敌人首次检测及 OCR {clock.Elapsed.TotalMilliseconds:F1} ms；{JsonSerializer.Serialize(names)}");
        Assert.Equal(new[] { "源石虫", "宿主士兵" }, names);
        clock.Restart();
        Assert.Null(await reader.ReadAsync(frame));
        Assert.Equal(1, calls);
        output.WriteLine($"敌人静帧检测 {clock.Elapsed.TotalMilliseconds:F1} ms");
        var api = await Api();
        api.ApplyRoundEnemies(1, names!);
        Assert.All(api.Match.RoundEnemies!.Enemies, e => Assert.NotNull(e.Id));
        api.ApplyRoundEnemies(1, ["宿主士兵", "未知敌人"]);
        Assert.Equal(new[] { "源石虫", "宿主士兵", "未知敌人" }, api.Match.RoundEnemies.Enemies.Select(e => e.Name));
        Assert.Null(api.Match.RoundEnemies.Enemies[^1].Id);
    }

    [Fact]
    public async Task StrategyUpdatesHistoryManualWinsAndNewMatchResets()
    {
        var api = await Api();
        api.ApplyRecognizedMatch(api.Match, [], opening: true);
        api.ObserveEconomy(new(1, 10, 1, 4, true));
        api.ObserveEconomy(new(1, 10, 1, 4, true));
        api.ApplyRecognizedStrategy("band_mech");
        Assert.Equal("recognized", api.Match.StrategySource);
        Assert.Equal("band_mech", api.History()[0].Local!.StrategyId);
        api.SetMatchStrategy("band_cannot");
        Assert.Equal("carry", api.Match.Economy!.Rule);
        api.ApplyRecognizedStrategy("band_mech");
        Assert.Equal("band_cannot", api.Match.StrategyId);
        Assert.Equal("manual", api.Match.StrategySource);
        api.ApplyRoundEnemies(1, ["源石虫"]);
        api.ApplyRecognizedMatch(api.Match, [], opening: true);
        Assert.Null(api.Match.StrategyId);
        Assert.Null(api.Match.StrategySource);
        Assert.Null(api.Match.RoundEnemies);
        api.SetMatchStrategy("band_mech");
        await api.ResetMatchAsync();
        Assert.Null(api.Match.StrategySource);
        Assert.Null(api.Match.RoundEnemies);
        api.SetMatchStrategy(null);
        Assert.Null(api.Match.StrategySource);
    }
}
