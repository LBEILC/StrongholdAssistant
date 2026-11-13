using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class BoardRouteFrameTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static byte[] Sample(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));

    [Theory]
    [InlineData("shop-ui100-2000x1125.png")]
    [InlineData("board-desert_camo_expanded.bmp")]
    public async Task PaintedFramesKeepMapAndProjectionUntilRoundChanges(string file)
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var api = await HostApi.CreateAsync(Data, directory);
            api.InitializeBoardRoutes(Data);
            api.ApplyRecognizedMatch(api.Match with { Solo = false, BossId = "enemy_9017_achunt" }, []);
            api.BoardRoutesEnabled = true;
            var events = new List<BoardRoutes?>();
            api.BoardRoutesChanged += events.Add;
            var recognize = api.RecognizeBoard;
            int calls = 0;
            api.RecognizeBoard = (frame, map, camera) => { calls++; return recognize(frame, map, camera); };
            var clean = Sample(file);
            var rest = new EconomyReading(14, null, null, null, true);
            api.ObserveBoardFrame(clean, rest, true);
            var first = Assert.IsType<BoardRoutes>(Assert.Single(events));
            var painted = Paint(clean, first);
            for (int i = 0; i < 5; i++) api.ObserveBoardFrame(painted, rest, true);
            api.ObserveBoardFrame(painted, null, true);
            Assert.Single(events);
            Assert.Equal(1, calls);
            api.ObserveBoardFrame(clean, rest with { Round = 15 }, true);
            Assert.Equal(2, calls);
            Assert.Equal(15, events[^1]!.Round);

            api.ObserveBoardFrame(clean, rest with { IsRest = false }, true);
            api.ObserveBoardFrame(clean, rest with { IsRest = false }, true);
            Assert.NotNull(events[^1]);
            api.ObserveBoardFrame(clean, rest with { Round = 15 }, true);
            Assert.Equal(2, calls);
            for (int i = 0; i < 3; i++) api.ObserveBoardFrame(clean, rest with { IsRest = false }, true);
            Assert.Null(events[^1]);
            api.ObserveBoardFrame(clean, rest, true);
            Assert.NotNull(events[^1]);
            api.UpdateConnection(new("disconnected"));
            Assert.Null(events[^1]);
            api.ObserveBoardFrame(clean, rest, true);
            Assert.Equal(4, calls);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task CameraSwitchFitsPaintedCollapsedSampleAndReusesEachProjection()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var api = await HostApi.CreateAsync(Data, directory);
            api.InitializeBoardRoutes(Data);
            api.BoardRoutesEnabled = true;
            var events = new List<BoardRoutes?>();
            api.BoardRoutesChanged += events.Add;
            var clean = Sample("board-snow_collapsed.bmp");
            var recognizer = new BoardRecognizer(Data);
            var collapsed = recognizer.Recognize(clean)!;
            // 只有收起雪地实拍样图：展开状态用同地图的参考投影注入，切换后实际拟合样图。
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Data, "garrison_board_refs.json")));
            var expanded = new BoardProjection(doc.RootElement.GetProperty("cameras").GetProperty("expanded")
                .EnumerateArray().Select(v => ((v[0][0].GetDouble(), v[0][1].GetDouble()),
                    (v[1][0].GetDouble(), v[1][1].GetDouble()))).ToArray());
            int calls = 0;
            api.RecognizeBoard = (frame, map, camera) => ++calls == 1
                ? new("m09", BoardCamera.Expanded, expanded, 0) : recognizer.Recognize(frame, map, camera);
            var rest = new EconomyReading(6, null, null, null, true);
            api.ObserveBoardFrame(clean, rest, true);
            var first = events[^1]!;
            var painted = Paint(clean, first);
            api.ObserveBoardFrame(painted, rest, false);
            var next = Assert.IsType<BoardRoutes>(events[^1]);
            Assert.Equal(BoardCamera.Collapsed, next.Camera);
            Assert.Equal("m09", next.MapId);
            foreach (var point in new[] { (0d, 0d), (6d, 4d) })
            {
                var a = collapsed.Projection.Project(point.Item1, point.Item2);
                var b = next.Projection.Project(point.Item1, point.Item2);
                Assert.InRange(Math.Abs(a.X - b.X), 0, .012);
                Assert.InRange(Math.Abs(a.Y - b.Y), 0, .012);
            }
            api.ObserveBoardFrame(painted, rest, true);
            Assert.Same(first.Projection, events[^1]!.Projection);
            api.ObserveBoardFrame(painted, rest, false);
            Assert.Same(next.Projection, events[^1]!.Projection);
            Assert.Equal(2, calls);
        }
        finally { Directory.Delete(directory, true); }
    }

    // 离屏按实际路线叠加地面、飞行和两侧首领线，不操作游戏或桌面。
    private static byte[] Paint(byte[] frame, BoardRoutes routes)
    {
        using var input = new MemoryStream(frame);
        using var bitmap = new Bitmap(input);
        using (var graphics = Graphics.FromImage(bitmap))
            foreach (var route in routes.Routes)
            {
                var color = route.Boss ? Color.FromArgb(243, 179, 34)
                    : route.Motion == "fly" ? Color.FromArgb(199, 155, 255) : Color.FromArgb(255, 90, 78);
                using var pen = new Pen(Color.FromArgb(217, color), (route.Boss ? 5 : 8) * bitmap.Height / 1080f);
                foreach (var segment in route.Segments.Where(s => s.Count > 1))
                    graphics.DrawLines(pen, segment.Select(p =>
                    {
                        var (x, y) = routes.Projection.Project(p.Col + (route.Boss ? -.17 : route.Motion == "fly" ? .2 : 0),
                            p.Row + (route.Boss ? .2 : route.Motion == "fly" ? -.26 : 0));
                        return new PointF((float)(x * bitmap.Width), (float)(y * bitmap.Height));
                    }).ToArray());
            }
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }
}
