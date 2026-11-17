using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;
using StrongholdAssistant.Host;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

public sealed class BoardRouteFilterTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private static readonly BoardMatch Board = new("m01", BoardCamera.Expanded,
        new([((0, 0), (0, 0)), ((6, 0), (1, 0)), ((0, 4), (0, 1)), ((6, 4), (1, 1))]), 0);

    private async Task<HostApi> Create(string[] types, string? boss = null)
    {
        var api = await HostApi.CreateAsync(Data, _directory);
        api.ApplyRecognizedMatch(api.Match with { EnemyTypes = types, BossId = boss }, []);
        return api;
    }

    private static BoardRoutes Routes(int round = 5, string? boss = null) =>
        new BoardRouteStore(Data).Find(Board, round, false, boss, null)!;

    [Fact]
    public async Task NonFlyingTrainingKeepsOnlyGroundRoutes()
    {
        var api = await Create(["DOT", "TIMES", "ELEMENT"]);
        var before = Routes();
        Assert.Contains(before.Routes, r => r.Motion == "fly");
        var after = api.FilterBoardRoutes(before);
        Assert.Equal(before.Routes.Where(r => r.Motion == "walk"), after.Routes);
        Assert.NotEmpty(after.Routes);
    }

    [Fact]
    public async Task ReadGroundGroupRemovesFlyingAndUnmatchedNameRestoresPossibilities()
    {
        var api = await Create(["FLY", "DOT"]);
        var streams = (await PlacementAdapter.LoadAsync(Data)).RoundStreams(5, null, false, api.Match.EnemyTypes, null);
        var ground = streams.First(s => s.Spawns.All(e => !e.Flying));
        api.ApplyRoundEnemies(5, ground.Spawns.Select(e => e.Profile.Name).Distinct());
        var before = Routes();
        Assert.DoesNotContain(api.FilterBoardRoutes(before).Routes, r => r.Motion == "fly");
        var logs = new List<string>();
        api.BoardLog = logs.Add;
        api.ApplyRoundEnemies(5, ["不存在的敌人"]);
        Assert.Equal(before.Routes, api.FilterBoardRoutes(before).Routes);
        Assert.Contains(logs, l => l.Contains("名单未匹配"));
    }

    [Fact]
    public async Task MissingStreamsKeepRoutesAndBossSpotsUnchanged()
    {
        var api = await Create([]);
        var before = Routes() with { Round = 0 };
        var after = api.FilterBoardRoutes(before);
        Assert.Same(before.Routes, after.Routes);
        Assert.Same(before.BossSpots, after.BossSpots);
    }

    [Theory]
    [InlineData("enemy_9017_achunt")]
    [InlineData("enemy_9013_acstmk")]
    public async Task BossRoutesAndSpotsArePreserved(string boss)
    {
        var api = await Create(["DOT"], boss);
        var before = Routes(14, boss);
        var after = api.FilterBoardRoutes(before);
        Assert.Equal(before.Routes.Where(r => r.Boss), after.Routes.Where(r => r.Boss));
        Assert.Same(before.BossSpots, after.BossSpots);
        Assert.True(before.Routes.Any(r => r.Boss) || before.BossSpots.Count > 0);
    }

    [Fact]
    public async Task CachedBoardRefreshesOnReadManualEditsTrainingAndRoundWithoutRecognition()
    {
        var api = await Create([]);
        api.InitializeBoardRoutes(Data);
        api.BoardRoutesEnabled = true;
        var events = new List<BoardRoutes?>();
        api.BoardRoutesChanged += events.Add;
        api.BoardLog = output.WriteLine;
        var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-ui100-2000x1125.png"));
        api.ObserveEconomy(new(5, 10, 1, 4, true));
        api.ObserveEconomy(new(5, 10, 1, 4, true));
        // 首帧真实识别后，后续变化必须完全使用缓存。
        api.ObserveBoardFrame(frame, new(5, 10, 1, 4, true));
        var first = Assert.IsType<BoardRoutes>(Assert.Single(events));
        api.RecognizeBoard = (_, _, _) => throw new InvalidOperationException("信息更新不应重新识别棋盘");
        Assert.Contains(first.Routes, r => r.Motion == "fly");
        api.ApplyRecognizedMatch(api.Match with { EnemyTypes = ["FLY", "DOT"] }, []);
        var streams = (await PlacementAdapter.LoadAsync(Data)).RoundStreams(5, null, false, api.Match.EnemyTypes, null);
        var ground = streams.First(s => s.Spawns.All(e => !e.Flying));
        var names = ground.Spawns.Select(e => e.Profile.Name).Distinct().ToArray();
        api.ApplyRoundEnemies(5, names);
        Assert.DoesNotContain(events[^1]!.Routes, r => r.Motion == "fly");
        Describe(first, events[^1]!);
        api.SetRoundEnemy("不存在的敌人", true);
        Assert.Contains(events[^1]!.Routes, r => r.Motion == "fly");
        api.SetRoundEnemy("不存在的敌人", false);
        Assert.DoesNotContain(events[^1]!.Routes, r => r.Motion == "fly");
        foreach (var name in names) api.SetRoundEnemy(name, false);
        Assert.Contains(events[^1]!.Routes, r => r.Motion == "fly");
        api.ApplyRoundEnemies(5, names);
        api.ObserveEconomy(new(6, 10, 1, 4, true));
        api.ObserveEconomy(new(6, 10, 1, 4, true));
        Assert.Equal(6, events[^1]!.Round);
        Assert.Contains(events[^1]!.Routes, r => r.Motion == "fly");
        var round6 = events[^1]!;
        var nextStreams = (await PlacementAdapter.LoadAsync(Data)).RoundStreams(6, null, false, api.Match.EnemyTypes, null);
        api.ApplyRoundEnemies(6, nextStreams.First(s => s.Spawns.All(e => !e.Flying)).Spawns.Select(e => e.Profile.Name).Distinct());
        Assert.DoesNotContain(events[^1]!.Routes, r => r.Motion == "fly");
        Describe(round6, events[^1]!);
    }

    [Fact]
    public async Task TrainingReadImmediatelyRefreshesCachedRoutes()
    {
        var api = await Create([]);
        api.InitializeBoardRoutes(Data);
        api.RecognizeBoard = (_, _, _) => Board;
        api.BoardRoutesEnabled = true;
        var events = new List<BoardRoutes?>();
        api.BoardRoutesChanged += events.Add;
        var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-ui100-2000x1125.png"));
        api.ObserveBoardFrame(frame, new(5, null, null, null, true));
        Assert.Contains(events[^1]!.Routes, r => r.Motion == "fly");
        api.ApplyRecognizedMatch(api.Match with { EnemyTypes = ["DOT"] }, []);
        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events[^1]!.Routes, r => r.Motion == "fly");
    }

    private void Describe(BoardRoutes before, BoardRoutes after)
    {
        static string Summary(BoardRoutes r) => string.Join("; ", r.Routes.Select((p, i) =>
            $"#{i + 1}:{p.Motion},boss={p.Boss},起点={p.Segments[0][0]}"));
        output.WriteLine($"缓存样图 map={before.MapId} round={before.Round} before=[{Summary(before)}] after=[{Summary(after)}]");
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
