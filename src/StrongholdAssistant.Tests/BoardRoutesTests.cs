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
                        foreach (var point in segment) Check(point, route);
                        // 共线合并后，每段必须是横线或竖线，中间的格子仍必须可走。
                        for (int i = 1; i < segment.Length; i++)
                        {
                            var a = segment[i - 1]; var b = segment[i];
                            Assert.True(a.Col == b.Col || a.Row == b.Row, $"{map.Name}/{level.Name}: {a} → {b}");
                            int dc = Math.Sign(b.Col - a.Col), dr = Math.Sign(b.Row - a.Row);
                            int steps = (int)Math.Max(Math.Abs(b.Col - a.Col), Math.Abs(b.Row - a.Row));
                            for (int n = 1; n <= steps; n++)
                            {
                                Check(new(a.Col + dc * n, a.Row + dr * n), route);
                            }
                        }
                    }
                    if (!boss)
                    {
                        Assert.Contains(segments[0][0], new[] { new RoutePoint(7.5, .5), new RoutePoint(7.5, 3.5) });
                        Assert.Equal(new RoutePoint(-.5, 3.5), segments[^1][^1]);
                    }
                }
                void Check(RoutePoint p, JsonElement route)
                {
                    int row = boss ? (int)(p.Row + .5) : (int)Math.Floor(p.Row);
                    int col = boss ? (int)(p.Col + .5) : (int)Math.Floor(p.Col) + 2;
                    // 右侧路线存储时镜像到左侧；校验时还原到真实棋盘格。
                    if (boss && route.GetProperty("side").GetString() == "right") col = 16 - col;
                    Assert.True(grid[row][col].GetBoolean(), $"{map.Name}/{level.Name}: {p}");
                }
            }
    }

    [Theory]
    [InlineData("m09", .5)]
    [InlineData("m02", .5)]
    [InlineData("m01", 3.5)]
    public void RoundFiveGroundRoutesUseUpdatedTerrainAndTieOrder(string mapId, double startRow)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_routes.json")));
        var routes = doc.RootElement.GetProperty("maps").GetProperty(mapId).GetProperty("levels")
            .GetProperty("05").GetProperty("routes").Deserialize<EnemyRoute[]>(HostApi.JsonOptions)!;
        var route = Assert.Single(routes, r => r.Motion == "walk" && r.Segments[0][0] == new RoutePoint(7.5, startRow));
        RoutePoint[] expected = mapId == "m01"
            ? [new(7.5, 3.5), new(6.5, 3.5), new(6.5, .5), new(1.5, .5), new(1.5, 3.5), new(-.5, 3.5)]
            : [new(7.5, .5), new(.5, .5), new(.5, 3.5), new(-.5, 3.5)];
        Assert.Equal(expected, Assert.Single(route.Segments));
    }

    [Fact]
    public void MultiplayerBossPatrolsAndAllPatrolClosuresMatchCheckpointsOnEveryMap()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_routes.json")));
        foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
        {
            var levels = map.Value.GetProperty("levels");
            foreach (string id in new[] { "h07_02", "h07_10", "h07_11", "h08_02", "h08_06" })
            {
                var routes = levels.GetProperty(id).GetProperty("routes").Deserialize<EnemyRoute[]>(HostApi.JsonOptions)!;
                var right = Assert.Single(routes, r => r.Boss && r.Side == "right");
                Assert.Equal("walk", right.Motion);
                Assert.True(right.Patrol);
            }
            foreach (var level in levels.EnumerateObject())
            {
                var patrols = level.Value.GetProperty("routes").Deserialize<EnemyRoute[]>(HostApi.JsonOptions)!
                    .Where(r => r.Patrol).ToArray();
                foreach (var route in patrols)
                {
                    var points = route.Segments.SelectMany(s => s).ToArray();
                    Assert.DoesNotContain(new RoutePoint(-.5, 3.5), points);
                    if (route.Boss)
                    {
                        // 模板的首个巡逻点：左侧 (7,4)，右侧铳/鳍 (9,2)、矩兽 (9,4)。
                        var first = new RoutePoint(6.5, route.Side == "right" && level.Name != "h07_11" ? 2.5 : .5);
                        Assert.Equal(first, points[^1]);
                    }
                }
                // 铳的三个随从也有巡逻路线，按模板出生动作顺序核对首个巡逻点。
                var escorts = patrols.Where(r => !r.Boss).ToArray();
                if (escorts.Length > 0)
                {
                    Assert.Contains(level.Name, new[] { "h08_02", "h08_02_s" });
                    RoutePoint[] first = level.Name.EndsWith("_s")
                        ? [new(6.5, 3.5), new(6.5, .5), new(.5, 3.5)]
                        : [new(.5, 3.5), new(7.5, 2.5), new(.5, 3.5)];
                    Assert.Equal(first, escorts.Select(r => r.Segments.Last().Last()).ToArray());
                }
            }
        }
    }


    // 从 docs/research/141-video-paths.json 的 114 个 observed 小段提取折点。
    // 内嵌观测值使公开导出的测试不依赖调研文件；38 个未展示的出生段不作观测。
    // 当前例外为空：补算障碍后的 RULD 吻合全部可读小段。
    public static IEnumerable<object[]> VideoPatrolCases()
    {
        (string Clip, string Map, string Level, string Side, string Paths)[] observations =
        [
            ("01", "m02", "h07_02", "left", "7,1 1,1 1,4;1,4 4,4 4,2 7,2 7,3;7,3 7,1"),
            ("01", "m02", "h07_02", "right", "9,3 9,2 12,2 12,4 15,4;15,4 15,1 9,1;9,1 9,3"),
            ("02", "m03", "h08_02", "left", "7,1 4,1 4,3 3,3 3,4 1,4;1,4 7,4 7,3;7,3 7,1"),
            ("02", "m03", "h08_02", "right", "9,3 9,4 15,4;15,4 13,4 13,3 12,3 12,1 9,1;9,1 9,3"),
            ("03", "m04", "h07_02", "left", "7,1 7,2 2,2 2,4 1,4;1,4 2,4 2,3 7,3;7,3 7,1"),
            ("03", "m04", "h07_02", "right", "9,3 14,3 14,4 15,4;15,4 14,4 14,2 9,2 9,1;9,1 9,3"),
            ("04", "m05", "h08_02", "left", "7,1 7,4 5,4 5,1 3,1 3,4 1,4;1,4 3,4 3,1 5,1 5,4 7,4 7,3;7,3 7,1"),
            ("04", "m05", "h08_02", "right", "9,3 9,4 11,4 11,1 13,1 13,4 15,4;15,4 13,4 13,1 11,1 11,4 9,4 9,1;9,1 9,3"),
            ("05", "m06", "h07_02", "left", "7,1 5,1 5,4 1,4;1,4 7,4 7,3;7,3 7,1"),
            ("05", "m06", "h07_02", "right", "9,3 9,4 15,4;15,4 11,4 11,1 9,1;9,1 9,3"),
            ("06", "m09", "h08_02", "left", "7,1 1,1 1,4;1,4 3,4 3,2 5,2 5,1 7,1 7,3;7,3 7,1"),
            ("06", "m09", "h08_02", "right", "9,3 9,1 15,1 15,4;15,4 15,1 9,1;9,1 9,3"),
            ("07", "m10", "h07_02", "left", "7,1 2,1 2,4 1,4;1,4 4,4 4,2 7,2 7,3;7,3 7,1"),
            ("07", "m10", "h07_02", "right", "9,3 10,3 10,2 12,2 12,4 15,4;15,4 14,4 14,1 9,1;9,1 9,3"),
            ("08", "m11", "h08_02", "left", "7,1 4,1 4,2 1,2 1,4;1,4 4,4 4,3 7,3;7,3 7,1"),
            ("08", "m11", "h08_02", "right", "9,3 12,3 12,4 15,4;15,4 15,2 12,2 12,1 9,1;9,1 9,3"),
            ("09", "m12", "h07_02", "left", "7,1 7,2 5,2 5,1 3,1 3,2 1,2 1,4;1,4 1,3 3,3 3,4 7,4 7,3;7,3 7,1"),
            ("09", "m12", "h07_02", "right", "9,3 9,4 13,4 13,3 15,3 15,4;15,4 15,2 13,2 13,1 11,1 11,2 9,2 9,1;9,1 9,3"),
            ("10", "m01", "h08_02", "left", "7,1 2,1 2,4 1,4;1,4 2,4 2,1 7,1 7,3;7,3 7,1"),
            ("10", "m01", "h08_02", "right", "9,3 10,3 10,1 14,1 14,4 15,4;15,4 14,4 14,1 9,1;9,1 9,3"),
            ("11", "m02", "h07_11", "left", "7,1 1,1 1,4;1,4 4,4 4,2 7,2 7,3;7,3 7,1"),
            ("11", "m02", "h07_11", "right", "9,1 15,1 15,4;15,4 12,4 12,2 9,2 9,3;9,3 9,1"),
            ("12", "m03", "h07_11", "left", "7,1 4,1 4,3 3,3 3,4 1,4;1,4 7,4 7,3;7,3 7,1"),
            ("12", "m03", "h07_11", "right", "9,1 12,1 12,3 13,3 13,4 15,4;15,4 9,4 9,3;9,3 9,1"),
            ("13", "m05", "h07_11", "left", "7,1 7,4 5,4 5,1 3,1 3,4 1,4;1,4 3,4 3,1 5,1 5,4 7,4 7,3;7,3 7,1"),
            ("13", "m05", "h07_11", "right", "9,1 9,4 11,4 11,1 13,1 13,4 15,4;15,4 13,4 13,1 11,1 11,4 9,4 9,3;9,3 9,1"),
            ("14", "m09", "h07_11", "left", "7,1 1,1 1,4;1,4 3,4 3,2 5,2 5,1 7,1 7,3;7,3 7,1"),
            ("14", "m09", "h07_11", "right", "9,1 15,1 15,4;15,4 15,1 9,1 9,3;9,3 9,1"),
            ("15", "m06", "h07_11", "left", "7,1 5,1 5,4 1,4;1,4 7,4 7,3;7,3 7,1"),
            ("15", "m06", "h07_11", "right", "9,1 11,1 11,4 15,4;15,4 9,4 9,3;9,3 9,1"),
            ("16", "m10", "h07_11", "left", "7,1 2,1 2,4 1,4;1,4 4,4 4,2 7,2 7,3;7,3 7,1"),
            ("16", "m10", "h07_11", "right", "9,1 14,1 14,4 15,4;15,4 12,4 12,2 9,2 9,3;9,3 9,1"),
            ("17", "m11", "h07_11", "left", "7,1 4,1 4,2 1,2 1,4;1,4 4,4 4,3 7,3;7,3 7,1"),
            ("17", "m11", "h07_11", "right", "9,1 12,1 12,2 15,2 15,4;15,4 12,4 12,3 9,3;9,3 9,1"),
            ("18", "m12", "h07_11", "left", "7,1 7,2 5,2 5,1 3,1 3,2 1,2 1,4;1,4 1,3 3,3 3,4 7,4 7,3;7,3 7,1"),
            ("18", "m12", "h07_11", "right", "9,1 9,2 11,2 11,1 13,1 13,2 15,2 15,4;15,4 15,3 13,3 13,4 9,4 9,3;9,3 9,1"),
            ("19", "m01", "h07_11", "left", "7,1 2,1 2,4 1,4;1,4 2,4 2,1 7,1 7,3;7,3 7,1"),
            ("19", "m01", "h07_11", "right", "9,1 14,1 14,4 15,4;15,4 14,4 14,1 9,1 9,3;9,3 9,1"),
        ];
        foreach (var item in observations)
            foreach (var (points, index) in item.Paths.Split(';').Select((p, i) => (p, i)))
                yield return [ $"{item.Clip}-{item.Side[0]}{index + 1}{(index + 1) % 3 + 1}", item.Map, item.Level, item.Side, points ];
    }

    [Theory]
    [MemberData(nameof(VideoPatrolCases))]
    public void GeneratedPatrolCellsMatchEveryObservedVideoSegment(string id, string mapId, string levelId, string side, string corners)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_routes.json")));
        var routes = doc.RootElement.GetProperty("maps").GetProperty(mapId).GetProperty("levels")
            .GetProperty(levelId).GetProperty("routes").Deserialize<EnemyRoute[]>(HostApi.JsonOptions)!;
        var route = Assert.Single(routes, r => r.Boss && r.Side == side);
        var actual = ExpandCells(Assert.Single(route.Segments).Select(p =>
            (side == "right" ? 16 - (int)(p.Col + .5) : (int)(p.Col + .5), (int)(p.Row + .5))));
        var expected = ExpandCells(corners.Split(' ').Select(p =>
        {
            var xy = p.Split(',').Select(int.Parse).ToArray();
            return (xy[0], xy[1]);
        }));
        Assert.True(Enumerable.Range(0, Math.Max(0, actual.Count - expected.Count + 1))
            .Any(i => actual.Skip(i).Take(expected.Count).SequenceEqual(expected)), id);
    }

    [Fact]
    public void PredefinedCrateIsBlockedAndNoGroundRouteCrossesIt()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_routes.json")));
        var map = doc.RootElement.GetProperty("maps").GetProperty("m03");
        Assert.False(map.GetProperty("bossWalkable")[2][3].GetBoolean());
        foreach (var level in map.GetProperty("levels").EnumerateObject()
            .Where(l => l.Name.StartsWith("h07_") || l.Name.StartsWith("h08_")))
            foreach (var route in level.Value.GetProperty("routes").Deserialize<EnemyRoute[]>(HostApi.JsonOptions)!
                .Where(r => r.Motion == "walk"))
                foreach (var segment in route.Segments)
                    Assert.DoesNotContain((3, 2), ExpandCells(segment.Select(p =>
                        (route.Side == "right" ? 16 - (int)(p.Col + .5) : (int)(p.Col + .5), (int)(p.Row + .5)))));
    }

    private static List<(int Col, int Row)> ExpandCells(IEnumerable<(int Col, int Row)> corners)
    {
        var points = corners.ToArray();
        var cells = new List<(int Col, int Row)> { points[0] };
        foreach (var end in points.Skip(1))
        {
            var start = cells[^1];
            Assert.True(start.Col == end.Col || start.Row == end.Row);
            while (start != end)
            {
                start = (start.Col + Math.Sign(end.Col - start.Col), start.Row + Math.Sign(end.Row - start.Row));
                cells.Add(start);
            }
        }
        return cells;
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
        Assert.All(multi.Routes.Where(r => r.Boss), r => Assert.True(r.Patrol));
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
