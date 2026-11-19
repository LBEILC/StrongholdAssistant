using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.Data;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class BoardBossRecognitionTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static byte[] Sample(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));

    [Fact]
    public void M10RightDefenseGateCorrectsPreviouslyInferredLeftSide()
    {
        // 包 74 第一次展开只靠地板判左，随后两种镜头都沿用左侧。
        Assert.False(BoardRecognizer.CanConfirmBossSide(26.018, 28.071));
        var frame = Sample("board-411-m10-boss-right.png");
        var result = new BoardRecognizer(Data).RecognizeDetailed(frame, "m10", BoardCamera.Collapsed, true, "left");
        Assert.Equal("right", result.Match?.PlayerSide);
        var routes = new BoardRouteStore(Data).Find(result.Match!, 15, false, "enemy_9021_acduml", "mode_multi_hard")!;
        var spot = Assert.Single(routes.BossSpots);
        Assert.Equal((6.5, 2.5), (spot.Col, spot.Row));
        Assert.True(routes.Projection.Project(spot.Col, spot.Row).X < routes.Projection.Project(-.5, 3.5).X);
    }

    [Theory]
    [InlineData("expanded", BoardCamera.Expanded)]
    [InlineData("collapsed", BoardCamera.Collapsed)]
    public void KnownBossMapChoosesRightCameraAndSkipsChangedReferences(string name, BoardCamera camera)
    {
        var image = Sample($"board-boss-right-{name}-r14.jpg");
        var recognizer = new BoardRecognizer(Data);
        var result = recognizer.RecognizeDetailed(image, "m05", camera, true);
        Assert.NotNull(result.Match);
        Assert.Equal("m05", result.Match.MapId);
        Assert.Equal(camera, result.Match.Camera);
        Assert.Equal("right", result.Match.PlayerSide);
        Assert.True(result.RightScore < result.LeftScore);
        Assert.True(result.Score < 40);
        Assert.True(result.Match.Projection.Project(0, 0).X > result.Match.Projection.Project(6, 0).X);
        Assert.Null(recognizer.RecognizeDetailed(image, null, camera, true).Match);
        Assert.Null(recognizer.RecognizeDetailed(image, "m05", camera).Match);

        // 改掉变化格的参考色；如果仍参与打分，候选分数或投影就会改变。
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(Data, "garrison_board_refs.json")))!;
            var colors = root["maps"]!["m05"]![name]!;
            foreach (var (col, row) in new[] { (7, 1), (7, 2), (0, 4) })
                for (int offset = 0; offset < 2; offset++)
                    colors[(row * 9 + col + 1) * 2 + offset] = new JsonArray(255, 0, 255);
            File.WriteAllText(Path.Combine(directory, "garrison_board_refs.json"), root.ToJsonString());
            var changed = new BoardRecognizer(directory).RecognizeDetailed(image, "m05", camera, true);
            Assert.Equal(result.LeftScore, changed.LeftScore);
            Assert.Equal(result.RightScore, changed.RightScore);
            Assert.Equal(result.Match.Projection.Project(0, 0), changed.Match!.Projection.Project(0, 0));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExpandedSideSurvivesCollapsedCameraAndNextRoundButResetsWithMatch()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var api = await HostApi.CreateAsync(Data, directory);
            api.InitializeBoardRoutes(Data);
            api.ApplyRecognizedMatch(api.Match with { Solo = false, BossId = "enemy_9037_aceel" }, []);
            api.BoardRoutesEnabled = true;
            var logs = new List<string>();
            api.BoardLog = logs.Add;
            BoardRoutes? routes = null;
            api.BoardRoutesChanged += value => routes = value;
            var rest = new EconomyReading(13, null, null, null, true);
            var normal = Sample("board-city-r13-expanded.jpg");
            var expanded = Sample("board-boss-right-expanded-r14.jpg");
            var collapsed = Sample("board-boss-right-collapsed-r14.jpg");
            api.ObserveBoardFrame(normal, rest, true);
            api.ObserveBoardFrame(collapsed, rest with { Round = 14 }, false);
            Assert.DoesNotContain("left=n/a", logs.Last(s => s.StartsWith("识别 ")));
            api.ObserveBoardFrame(expanded, rest with { Round = 14 }, true);
            Assert.Equal("right", routes!.PlayerSide);
            // 展开确认后，先前缓存的收起镜头也必须重算且不再比较左侧。
            api.ObserveBoardFrame(collapsed, rest with { Round = 14 }, false);
            Assert.Equal("right", routes!.PlayerSide);
            Assert.Contains("left=n/a", logs.Last(s => s.StartsWith("识别 ")));
            api.ObserveBoardFrame(collapsed, rest with { Round = 15 }, false);
            Assert.Equal("right", routes!.PlayerSide);
            await api.ResetMatchAsync();
            api.ApplyRecognizedMatch(api.Match with { Solo = false, BossId = "enemy_9037_aceel" }, []);
            api.ObserveBoardFrame(normal, rest, true);
            api.ObserveBoardFrame(collapsed, rest with { Round = 14 }, false);
            Assert.DoesNotContain("left=n/a", logs.Last(s => s.StartsWith("识别 ")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("expanded", BoardCamera.Expanded)]
    [InlineData("collapsed", BoardCamera.Collapsed)]
    public void BossProjectionOnlyTranslatesBaseCalibration(string name, BoardCamera camera)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_board_refs.json")));
        var image = Sample($"board-boss-right-{name}-r14.jpg");
        var result = new BoardRecognizer(Data).RecognizeDetailed(image, "m05", camera, true, "right");
        Assert.NotNull(result.Match);
        Assert.Equal("right", result.Match.PlayerSide);
        Assert.Null(result.LeftScore);
        var corners = doc.RootElement.GetProperty("bossCameras").GetProperty(name);
        (double X, double Y)? translation = null;
        foreach (var corner in corners.EnumerateArray())
        {
            var point = result.Match.Projection.Project(corner[0][0].GetDouble(), corner[0][1].GetDouble());
            var dx = point.X - corner[1][0].GetDouble();
            var dy = point.Y - corner[1][1].GetDouble();
            Assert.InRange(Math.Abs(dx), 0, .005 / 3 + 1e-10);
            Assert.InRange(Math.Abs(dy), 0, .005 / 3 + 1e-10);
            translation ??= (dx, dy);
            Assert.Equal(translation.Value.X, dx, 10);
            Assert.Equal(translation.Value.Y, dy, 10);
        }
    }

    [Fact]
    public void CityTerrainHasFixedChangesOnBothSides()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_board_refs.json")));
        var terrain = doc.RootElement.GetProperty("bossTerrain").GetProperty("m05");
        (int, int)[] expected = [(2, -1), (3, -1), (5, -1), (6, -1), (7, 1), (7, 2), (0, 4)];
        foreach (var side in new[] { "left", "right" })
            Assert.True(BoardRecognizer.ChangedCells(terrain, side).SetEquals(expected));
    }

    [Fact]
    public async Task MapSurvivesRoundAndCombatButResetsWithMatch()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var api = await HostApi.CreateAsync(Data, directory);
            api.InitializeBoardRoutes(Data);
            api.BoardRoutesEnabled = true;
            var image = Sample("board-city-r13-expanded.jpg");
            var board = new BoardRecognizer(Data).Recognize(image)!;
            var maps = new List<string?>();
            api.RecognizeBoard = (_, map, _) => { maps.Add(map); return board; };
            var rest = new EconomyReading(13, null, null, null, true);
            api.ObserveBoardFrame(image, rest, true);
            for (int i = 0; i < 3; i++) api.ObserveBoardFrame(image, rest with { IsRest = false }, true);
            api.ObserveBoardFrame(image, rest with { Round = 14 }, true);
            await api.ResetMatchAsync();
            api.ObserveBoardFrame(image, rest with { Round = 1 }, true);
            Assert.Equal(new string?[] { null, "m05", null }, maps);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void AutoSideUsesRecognitionAndManualSideOverridesIt()
    {
        var image = Sample("board-boss-right-expanded-r14.jpg");
        var board = new BoardRecognizer(Data).RecognizeDetailed(image, "m05", BoardCamera.Expanded, true).Match!;
        var store = new BoardRouteStore(Data);
        Assert.True(store.IsBossRound(14, false, null));
        Assert.False(store.IsBossRound(13, false, null));
        var routes = store.Find(board, 14, false, "enemy_9017_achunt", null)!;
        Assert.Equal("right", routes.PlayerSide);
        var bosses = routes.Routes.Where(r => r.Boss && r.Side is not null).ToArray();
        Assert.Contains(bosses, r => r.Side == "left");
        Assert.Contains(bosses, r => r.Side == "right");
        Assert.All(bosses.Where(r => RouteLayer.MatchesSide(r, "auto", routes.PlayerSide)), r => Assert.Equal("right", r.Side));
        Assert.All(bosses, r => Assert.True(RouteLayer.MatchesSide(r, "auto", null)));
        Assert.All(bosses.Where(r => RouteLayer.MatchesSide(r, "left", "right")), r => Assert.Equal("left", r.Side));
    }
}
