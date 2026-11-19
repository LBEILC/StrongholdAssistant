using System.Drawing;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.Placement;
using JsonDataStore = ASPAssistant.Core.Data.JsonDataStore;
using ASPAssistant.Tests.Services;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class FieldReaderTests(EnemyRecognitionFixture fixture)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);
    private async Task<FieldReader> Reader() => new(Data, fixture.Engine,
        (await new JsonDataStore(Data).LoadOperatorsAsync()).Select(o => o.Name));

    [Theory]
    [InlineData("63", "m12", 6, "2,1=1 4,1=3 5,1=1 2,2=1 3,3=2 4,3=2 5,3=2")]
    [InlineData("64", "m01", 4, "2,0=1 3,0=3 4,0=1 5,0=1 1,1=1 5,2=2")]
    [InlineData("71", "m06", 5, "4,0=1 5,0=1 2,2=2 3,2=3 5,2=2 4,3=1 5,3=1")]
    [InlineData("73", "m12", 4, "2,0=3 1,1=1 2,1=1 4,1=1 5,1=1 0,2=1 1,2=1 2,2=1")]
    public async Task DiagnosticBoardMatchesVisibleOperators(string sample, string map, int round, string expected)
    {
        using var reader = await Reader();
        reader.KnownMap = map;
        var frame = File.ReadAllBytes(Sample($"field-368-{sample}.png"));
        var observation = await reader.ReadAsync(frame, DateTimeOffset.UnixEpoch,
            new(round, 0, 1, 0, true), true, true, []);
        var cells = observation.Cells.Where(p => p.Key.Zone == "board" && p.Value > 0)
            .OrderBy(p => p.Key.Row).ThenBy(p => p.Key.Col)
            .Select(p => $"{p.Key.Col},{p.Key.Row}={p.Value}");
        Assert.Equal(expected.Split(' '), cells);
    }

    [Theory]
    [InlineData("2131", 1, 0, 4)]
    [InlineData("2051", 3, 0, 2)]
    [InlineData("2051", 5, 0, 1)]
    [InlineData("1905", 2, 0, 1)]
    [InlineData("1805", 2, 3, 1)]
    public async Task PartiallyObscuredBoardUnitsKeepTheirTier(string game, int col, int row, int tier)
    {
        // 保留采集帧的棋盘区域和原坐标，遮去外围界面。
        var path = Sample($"field-badge-{game}.png");
        var board = new BoardRecognizer(Data).Recognize(File.ReadAllBytes(path));
        Assert.NotNull(board);
        using var reader = await Reader();
        using var frame = new Bitmap(path);
        var cells = reader.ReadCells(frame, board, board.Camera == BoardCamera.Expanded);
        Assert.Equal(tier, cells[new("board", col, row)]);
    }


    [Theory]
    [InlineData("p0139-219.4", false, false, "board", 6, 3, 1)]
    [InlineData("p1436-2946.7", false, true, "board", 3, 3, 5)]
    [InlineData("p1749-2401.4", true, false, "bench", 3, 0, 4)]
    [InlineData("p1905-1710.9", true, false, "board", 3, 3, 3)]
    [InlineData("p1905-2384.5", false, true, "board", 2, 3, 3)]
    [InlineData("p1905-2384.5", false, true, "board", 2, 0, 4)]
    public async Task ReadsTierOnCollectedRestFrame(string sample, bool shopVisible, bool boss,
        string zone, int col, int row, int tier)
    {
        using var reader = await Reader();
        reader.BossRound = boss;
        var result = await reader.ReadAsync(File.ReadAllBytes(Sample($"field-miss-{sample}.png")),
            DateTimeOffset.UtcNow, new(1, 0, 1, 0, true), true, shopVisible, []);
        Assert.Equal(tier, result.Cells.GetValueOrDefault(new(zone, col, row)));
        if (boss) Assert.Null(result.MapId);
    }

    [Theory]
    [InlineData("1749", 2560, true, "2,1 4,1 1,2 1,3 2,3 4,3")]
    [InlineData("1622", 1600, false, "0,2 2,2 3,2")]
    public async Task CameraFitFindsOccupiedCellsAtCaptureResolution(
        string pack, int width, bool expanded, string occupied)
    {
        using var reader = await Reader();
        using var saved = OpenCvSharp.Cv2.ImRead(Sample($"field-camera-{pack}.jpg"));
        using var raw = new OpenCvSharp.Mat();
        OpenCvSharp.Cv2.Resize(saved, raw, new OpenCvSharp.Size(width, width * 9 / 16),
            interpolation: OpenCvSharp.InterpolationFlags.Cubic);
        var frame = raw.ImEncode(".png");
        var result = await reader.ReadAsync(frame, DateTimeOffset.UtcNow, new(4, 0, 1, 0, true),
            true, expanded, []);
        if (pack == "1749") Assert.Equal(6, result.Cells.Count(p => p.Value > 0));
        foreach (string point in occupied.Split(' '))
        {
            var xy = point.Split(',');
            Assert.True(result.Cells.GetValueOrDefault(new("board", int.Parse(xy[0]), int.Parse(xy[1]))) > 0, point);
        }
    }
    [Theory]
    [InlineData(47, "bench:9:0,bench:10:0")]
    [InlineData(57, "board:4:2,board:5:3")]
    [InlineData(345, "bench:9:0,bench:10:0")]
    [InlineData(350, "board:5:3,board:6:3")]
    public async Task RecordedOccupancyAndTiersMatchAnswer(int second, string expected)
    {
        var reader = await Reader(); var bytes = File.ReadAllBytes(Sample($"field-{second}.jpg"));
        using var bitmap = new Bitmap(Sample($"field-{second}.jpg"));
        var board = new BoardRecognizer(Data).Recognize(bytes);
        Assert.NotNull(board);
        var cells = reader.ReadCells(bitmap, board, true).Where(p => p.Value > 0).ToArray();
        Assert.Equal(expected.Split(',').Order(), cells.Select(p => $"{p.Key.Zone}:{p.Key.Col}:{p.Key.Row}").Order());
        Assert.All(cells, p => Assert.Equal(1, p.Value));
    }

    [Fact]
    public async Task PreparationHasElevenOperatorsAndEquipmentDoesNotCount()
    {
        var reader = await Reader();
        using var prep = new Bitmap(Sample("prep-ready-1920x1080.png"));
        var cells = reader.ReadCells(prep, null, false).Where(p => p.Value > 0).ToArray();
        Assert.Equal(11, cells.Length);
        Assert.Equal(new[] { 1, 1, 2, 2, 2, 2, 3, 3, 3, 5, 5 }, cells.Select(p => p.Value).Order());
        using var shop = new Bitmap(Sample("shop-closure-li-user-1665x1012.jpg"));
        Assert.Equal(new[] { 1, 1, 3, 5 }, reader.ReadBadges(shop).Select(b => b.Tier).Order());
    }

    [Fact]
    public async Task EquipmentPanelWithVisibleReadyButtonDoesNotReplaceBoardOccupancy()
    {
        using var reader = await Reader();
        reader.KnownMap = "m10";
        var frame = File.ReadAllBytes(Sample("field-411-equipment-panel.png"));
        var observation = await reader.ReadAsync(frame, DateTimeOffset.UnixEpoch,
            new(4, null, null, null, true), true, false, []);
        Assert.False(observation.MainScreen, string.Join(" ", observation.Cells.Where(p => p.Value > 0)));
        Assert.Empty(observation.Cells);
    }

    [Fact]
    public async Task DragPanelReadsMousseDespiteHiddenReadyControl()
    {
        var reader = await Reader();
        var o = await reader.ReadAsync(File.ReadAllBytes(Sample("field-55.5.jpg")), DateTimeOffset.UtcNow,
            new(1, 0, 1, 0, true), false, false, []);
        Assert.Equal("慕斯", o.DragOperator); Assert.Empty(o.Cells);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EquipmentRowIsExcludedFromProductObservations(bool shopVisible)
    {
        var reader = await Reader();
        var bytes = File.ReadAllBytes(Sample("prep-ready-1920x1080.png"));
        using var bitmap = new Bitmap(Sample("prep-ready-1920x1080.png"));
        var cells = reader.ReadCells(bitmap, null, shopVisible);
        Assert.Contains(cells, p => p.Key.Zone == "board" && p.Key.Row == 3);
        Assert.DoesNotContain(cells.Keys, c => c.Zone == "board" && c.Row == 4);
        Assert.DoesNotContain(reader.OverlayGeometry!.Anchors.Keys, c => c.Zone == "board" && c.Row == 4);

        var observation = await reader.ReadAsync(bytes, DateTimeOffset.UtcNow,
            new(1, 0, 1, 0, true), true, shopVisible, []);
        Assert.NotEmpty(observation.Cells);
        Assert.DoesNotContain(observation.Cells.Keys, c => c.Zone == "board" && c.Row == 4);
        Assert.DoesNotContain(observation.Sprites!.Keys, c => c.Zone == "board" && c.Row == 4);
        Assert.NotNull(observation.Geometry);
        Assert.Equal(4, observation.Geometry.Corners.Length);
        Assert.Equal(10, observation.Geometry.BenchFeet.Count);
        Assert.All(observation.Sprites.Values, patch => Assert.True(patch.Geometry?.CellWidth > 0));
        var geometry = observation.Geometry;
        var corners = new[] { (0d, 0d), (7d, 0d), (7d, 5d), (0d, 5d) };
        var restored = new BoardProjection(corners.Select((p, i) =>
            (p, (geometry.Corners[i].X / 1920, geometry.Corners[i].Y / 1080))).ToArray());
        foreach (var (cell, patch) in observation.Sprites.Where(p => !p.Value.Bench))
        {
            var center = restored.Project(cell.Col + .5, cell.Row + .5);
            Assert.Equal(center.X * 1920, patch.Geometry!.FootX, 6);
            Assert.Equal(center.Y * 1080, patch.Geometry.FootY, 6);
        }
    }

    [Theory]
    [InlineData(1920, 0, 4)]
    [InlineData(1280, 0, 4)]
    [InlineData(1920, 60, 0)]
    public async Task RecordedFourthTierIsReadOnlyAtItsBenchAnchor(int width, int shift, int expected)
    {
        var reader = await Reader();
        using var sprite = new Bitmap(Sample("sprite-N29.png"));
        using var frame = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Black);
            // 已核实的四阶整备区图块；角标位于图块内约 (35, 49)。
            g.DrawImage(sprite, new Rectangle(219 + shift, 560, sprite.Width, sprite.Height), new Rectangle(0, 0, sprite.Width, sprite.Height), GraphicsUnit.Pixel);
        }
        using var scaled = new Bitmap(frame, width, width * 1080 / 1920);
        var cells = reader.ReadCells(scaled, null, true);
        Assert.Equal(expected, cells[new("bench", 1, 0)]);
        Assert.Equal(expected == 0 ? 0 : 1, cells.Count(p => p.Value > 0));
    }

    [Fact]
    public async Task RecordedBenchSummonCountDoesNotCountAsOperator()
    {
        var reader = await Reader();
        using var equipment = new Bitmap(Sample("field-equipment-tier4.png"));
        using var frame = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Black);
            // 历史文件名误称装备；实际是夜莺召唤物，角标旁有 X2 数量标签。
            g.DrawImage(equipment, new Rectangle(218, 560, equipment.Width, equipment.Height), new Rectangle(0, 0, equipment.Width, equipment.Height), GraphicsUnit.Pixel);
        }
        Assert.DoesNotContain(reader.ReadCells(frame, null, true), p => p.Value > 0);
    }

    [Theory]
    [InlineData(1920, "field-equipment-tier4.png")]
    [InlineData(1280, "field-equipment-tier4.png")]
    [InlineData(1920, "field-summon-side.png")]
    public async Task BoardSummonTriangleIsExcludedWithoutAQuantityLabel(int width, string sample)
    {
        var reader = await Reader();
        // 侧面令牌来自同一录屏 1404 秒；游戏画面归上海鹰角网络所有。
        using var summon = new Bitmap(Sample(sample));
        using var frame = new Bitmap(1920, 1080);
        reader.ReadCells(frame, null, true);
        var cell = new FieldCell("board", 5, 3);
        var anchor = reader.OverlayGeometry!.Anchors[cell];
        using (var g = Graphics.FromImage(frame))
        {
            // 仅保留角标与下方召唤物，遮去右侧数量标签，验证棋盘独立判据。
            int x = (int)(anchor.X * 1920) - 35, y = (int)(anchor.Y * 1080) - 48;
            g.DrawImage(summon, new Rectangle(x, y, summon.Width, summon.Height), new Rectangle(0, 0, summon.Width, summon.Height), GraphicsUnit.Pixel);
            g.FillRectangle(Brushes.Black, x + 57, y + 24, 71, 42);
        }
        using var scaled = new Bitmap(frame, width, width * 1080 / 1920);
        Assert.Equal(0, reader.ReadCells(scaled, null, true)[cell]);
        Assert.DoesNotContain(cell, reader.OverlayGeometry!.Badges.Keys);
    }

    [Fact]
    public async Task RecognizedMapOnlyReadsDeployableCellsAndUnknownMapKeepsAnchors()
    {
        var reader = await Reader();
        var bytes = File.ReadAllBytes(Sample("field-57.jpg"));
        using var bitmap = new Bitmap(Sample("field-57.jpg"));
        var board = new BoardRecognizer(Data).Recognize(bytes)!;
        var map = (await GarrisonBoardStore.LoadAsync(Data))!.Find(board.MapId)!;
        reader.ReadCells(bitmap, board, true);
        var known = reader.OverlayGeometry!.Anchors.Keys.Where(c => c.Zone == "board").ToArray();
        Assert.NotEmpty(known);
        Assert.All(known, c => Assert.True(GarrisonMap.CanDeploy(map.At(new(c.Col, c.Row)), false)));
        reader.ReadCells(bitmap, null, true);
        Assert.Equal(28, reader.OverlayGeometry!.Anchors.Keys.Count(c => c.Zone == "board"));
        Assert.True(known.Length < 28);
    }

}
