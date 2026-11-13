using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class BoardRoutesTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void GroundRoutesStayWalkableAndNormalRoundsConnectSpawnToDefense()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_routes.json")));
        foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
            foreach (var level in map.Value.GetProperty("levels").EnumerateObject())
            {
                bool boss = level.Name.StartsWith("h07_") || level.Name.StartsWith("h08_");
                var grid = map.Value.GetProperty(boss ? "bossWalkable" : "walkable");
                foreach (var route in level.Value.GetProperty("routes").EnumerateArray().Where(r => r.GetProperty("motion").GetString() == "walk"))
                {
                    var segments = route.GetProperty("segments").Deserialize<RoutePoint[][]>(HostApi.JsonOptions)!;
                    foreach (var segment in segments)
                    {
                        foreach (var point in segment) Check(point);
                        // 共线合并后，线段中间的格子及斜走两侧仍必须可走。
                        for (int i = 1; i < segment.Length; i++)
                        {
                            var a = segment[i - 1]; var b = segment[i];
                            int dc = Math.Sign(b.Col - a.Col), dr = Math.Sign(b.Row - a.Row);
                            int steps = (int)Math.Max(Math.Abs(b.Col - a.Col), Math.Abs(b.Row - a.Row));
                            for (int n = 1; n <= steps; n++)
                            {
                                Check(new(a.Col + dc * n, a.Row + dr * n));
                                if (dc != 0 && dr != 0)
                                {
                                    Check(new(a.Col + dc * (n - 1), a.Row + dr * n));
                                    Check(new(a.Col + dc * n, a.Row + dr * (n - 1)));
                                }
                            }
                        }
                    }
                    if (!boss)
                    {
                        Assert.Contains(segments[0][0], new[] { new RoutePoint(7.5, .5), new RoutePoint(7.5, 3.5) });
                        Assert.Equal(new RoutePoint(-.5, 3.5), segments[^1][^1]);
                    }
                }
                void Check(RoutePoint p)
                {
                    int row = boss ? (int)(p.Row + .5) : (int)Math.Floor(p.Row);
                    int col = boss ? (int)(p.Col + .5) : (int)Math.Floor(p.Col) + 2;
                    Assert.True(grid[row][col].GetBoolean(), $"{map.Name}/{level.Name}: {p}");
                }
            }
    }

    [Fact]
    public void BossRouteMarkersMatchLevelAndMode()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_routes.json")));
        var maps = doc.RootElement.GetProperty("maps");
        var levels = maps.GetProperty("m01").GetProperty("levels");
        var multi = levels.GetProperty("h07_02").GetProperty("routes").Deserialize<EnemyRoute[]>(HostApi.JsonOptions)!;
        Assert.True(Assert.Single(multi, r => r.Boss && r.Side == "left").Patrol);
        Assert.Single(multi, r => r.Boss && r.Side == "right");
        var solo = levels.GetProperty("h07_02_s").GetProperty("routes").Deserialize<EnemyRoute[]>(HostApi.JsonOptions)!;
        Assert.Null(Assert.Single(solo, r => r.Boss).Side);
        foreach (var map in maps.EnumerateObject())
            foreach (var level in map.Value.GetProperty("levels").EnumerateObject()
                .Where(l => !l.Name.StartsWith("h07_") && !l.Name.StartsWith("h08_")))
                foreach (var route in level.Value.GetProperty("routes").Deserialize<EnemyRoute[]>(HostApi.JsonOptions)!)
                {
                    Assert.Null(route.Side);
                    Assert.False(route.Patrol);
                }
    }

    [Fact]
    public void BossBranchesTeleportsSpotsAndRoundSelection()
    {
        var store = new BoardRouteStore(Data);
        var projection = new BoardProjection([((0, 0), (0, 0)), ((6, 0), (1, 0)), ((0, 4), (0, 1)), ((6, 4), (1, 1))]);
        var board = new BoardMatch("m01", BoardCamera.Expanded, projection, 0);
        var multi = store.Find(board, 14, false, "enemy_9017_achunt", null)!;
        Assert.Equal(2, multi.Routes.Count(r => r.Boss));
        Assert.True(Assert.Single(multi.Routes, r => r.Boss && r.Side == "left").Patrol);
        Assert.Single(multi.Routes, r => r.Boss && r.Side == "right");
        Assert.Contains(multi.Routes, r => r.Boss && r.Segments.Count == 2);
        Assert.Single(store.Find(board, 14, true, "enemy_9017_achunt", null)!.Routes, r => r.Boss);
        var stationary = store.Find(board, 14, false, "enemy_9013_acstmk", null)!;
        Assert.NotEmpty(stationary.BossSpots);
        Assert.DoesNotContain(stationary.Routes, r => r.Boss);
        Assert.NotNull(store.Find(board, 15, true, "enemy_9017_achunt", null));
        Assert.Null(store.Find(board, 14, true, null, null));
        Assert.NotNull(store.Find(board, 1, true, null, null));
        Assert.NotNull(store.Find(board, 1, false, null, "mode_multi_normal"));
    }

    [Fact]
    public async Task EventsOnlyRecognizeWhenEnabledAndPublishChangesOnce()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Data, directory);
            api.InitializeBoardRoutes(Data);
            int calls = 0;
            var projection = new BoardProjection([((0, 0), (0, 0)), ((6, 0), (1, 0)), ((0, 4), (0, 1)), ((6, 4), (1, 1))]);
            BoardMatch? board = new("m01", BoardCamera.Expanded, projection, 0);
            api.RecognizeBoard = (_, _, _) => { calls++; return board; };
            var events = new List<BoardRoutes?>();
            api.BoardRoutesChanged += events.Add;
            var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-ui100-2000x1125.png"));
            var rest = new EconomyReading(1, null, null, null, true);
            api.ObserveBoardFrame(frame, rest);
            Assert.Equal(0, calls);
            api.BoardRoutesEnabled = true;
            api.ObserveBoardFrame(frame, rest); api.ObserveBoardFrame(frame, rest);
            Assert.Single(events);
            board = board with { MapId = "m02" };
            api.ObserveBoardFrame(frame, rest); api.ObserveBoardFrame(frame, rest);
            Assert.Single(events);
            Assert.Equal(1, calls);
            rest = rest with { Round = 2 };
            api.ObserveBoardFrame(frame, rest); api.ObserveBoardFrame(frame, rest);
            Assert.Equal(2, events.Count);
            api.ApplyRecognizedMatch(api.Match with { BossId = "enemy_9017_achunt" }, []);
            rest = rest with { Round = 14 };
            api.ObserveBoardFrame(frame, rest); api.ObserveBoardFrame(frame, rest);
            Assert.Equal(4, events.Count); // 换局先清除旧路线。
            api.ApplyRecognizedMatch(api.Match with { BossId = "enemy_9013_acstmk" }, []);
            api.ObserveBoardFrame(frame, rest); api.ObserveBoardFrame(frame, rest);
            Assert.Equal(6, events.Count);
            board = null;
            rest = rest with { Round = 15 };
            api.ObserveBoardFrame(frame, rest); api.ObserveBoardFrame(frame, rest);
            Assert.Equal(7, events.Count); Assert.Null(events[^1]);
            int before = calls;
            api.ObserveBoardFrame(frame, rest with { IsRest = false });
            api.BoardRoutesEnabled = false;
            api.ObserveBoardFrame(frame, rest);
            Assert.Equal(before, calls);
        }
        finally { Directory.Delete(directory, true); }
    }
}
