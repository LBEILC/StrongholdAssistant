using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Moq;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class RoundProgressTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static byte[] Frame => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-ui100-2000x1125.png"));

    private async Task<HostApi> Create()
    {
        var api = await HostApi.CreateAsync(Data, _directory);
        api.InitializeBoardRoutes(Data);
        var projection = new BoardProjection([((0, 0), (0, 0)), ((6, 0), (1, 0)), ((0, 4), (0, 1)), ((6, 4), (1, 1))]);
        api.RecognizeBoard = (_, _, _) => new("m01", BoardCamera.Expanded, projection, 0);
        api.BoardRoutesEnabled = true;
        return api;
    }

    [Theory]
    [InlineData(true, "mode_single_1", "？？")]
    [InlineData(false, "mode_multi_funny", "??")]
    public async Task QuestionMarksQueryModeFinalRound(bool solo, string mode, string question)
    {
        var api = await Create();
        if (solo)
        {
            api.ReadOpeningMode = () => Task.FromResult<GameModeInfo?>(new(mode, mode));
            await api.HandleOpeningAsync(true);
        }
        api.ApplyRecognizedMatch(api.Match with { Mode = new(mode, mode), BossId = "enemy_9017_achunt" }, [], opening: !solo);
        var ocr = new Mock<IOcrEngine>();
        ocr.SetupSequence(o => o.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync([new("回合", (800, 20, 50, 30)), new("休息一下", (950, 20, 100, 30))])
            .ReturnsAsync([]);
        ocr.SetupSequence(o => o.RecognizeLineAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync([new(question, (855, 20, 50, 30))]).ReturnsAsync([]);
        var frame = Frame;
        var reading = await new EconomyReader(ocr.Object).ReadAsync(frame, 2000, 1125);
        Assert.True(reading.IsFinalRound);
        Assert.Null(reading.Round);
        var events = new List<BoardRoutes?>();
        api.BoardRoutesChanged += events.Add;
        api.ObserveBoardFrame(frame, new(13, null, null, null, true));
        api.ObserveBoardFrame(frame, reading);
        var routes = Assert.IsType<BoardRoutes>(events[^1]);
        Assert.Equal(14, routes.Round);
        Assert.Equal(solo ? 1 : 2, routes.Routes.Count(r => r.Boss));
        if (!solo) Assert.Single(routes.Routes, r => r.Boss && r.Side == "right");
    }

    [Theory]
    [InlineData(new[] { 1, 2, 12, 12, 3 }, new[] { 1, 2, 3 })]
    [InlineData(new[] { 5, 15, 15 }, new[] { 5 })]
    public async Task RejectsJumpsUntilNextRoundAndResetsOnNewMatch(int[] readings, int[] expected)
    {
        var api = await Create();
        api.ApplyRecognizedMatch(api.Match, [], opening: true);
        var accepted = new List<int>();
        var logs = new List<string>();
        api.BoardLog = logs.Add;
        api.BoardRoutesChanged += routes => { if (routes is not null) accepted.Add(routes.Round); };
        var frame = Frame;
        foreach (var round in readings)
        {
            var reading = new EconomyReading(round, 4, 1, 5, true);
            // 每个候选连续读两帧，验证两帧误读也不能绕过顺序限制。
            for (int i = 0; i < 2; i++)
            {
                api.ObserveEconomy(reading);
                api.ObserveBoardFrame(frame, reading);
            }
        }
        Assert.Equal(expected, accepted);
        Assert.Equal(expected[^1], api.Match.Economy!.Round);
        Assert.Single(logs, l => l.Contains("拒绝回合"));
        Assert.DoesNotContain(logs, l => l.StartsWith("查询 ") && (l.Contains("round=12 ") || l.Contains("round=15 ")));
        api.ApplyRecognizedMatch(api.Match, [], opening: true);
        var first = new EconomyReading(1, 4, 1, 5, true);
        api.ObserveEconomy(first);
        api.ObserveEconomy(first);
        api.ObserveBoardFrame(frame, first);
        Assert.Equal(1, accepted[^1]);
        Assert.Equal(1, api.Match.Economy!.Round);
    }

    [Fact]
    public async Task SustainedJumpRecoversEconomyAndRoutes()
    {
        var api = await Create();
        var now = DateTimeOffset.UtcNow;
        api.BoardClock = () => now;
        var accepted = new List<int>();
        api.BoardRoutesChanged += routes => { if (routes is not null) accepted.Add(routes.Round); };
        var frame = Frame;
        void Observe(int round)
        {
            var reading = new EconomyReading(round, 4, 1, 5, true);
            api.ObserveEconomy(reading);
            api.ObserveBoardFrame(frame, reading);
        }
        Observe(2); Observe(2);
        for (int i = 0; i < 4; i++) { Observe(4); now = now.AddSeconds(1); }
        Assert.Equal(2, accepted[^1]);
        Assert.Equal(2, api.Match.Economy!.Round);
        Observe(4); Observe(4);
        Assert.Equal(4, accepted[^1]);
        Assert.Equal(4, api.Match.Economy!.Round);
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
