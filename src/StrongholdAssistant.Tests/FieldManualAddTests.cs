using System.Reflection;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.Placement;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class FieldManualAddTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
    private static readonly FieldCell Board = new("board", 2, 2);
    private static FieldTracker Tracker(Action<string>? log = null)
    {
        var tracker = new FieldTracker { RecognitionLog = log };
        tracker.ConfigureIdentity([new() { Name = "甲", Tier = 2 }, new() { Name = "乙", Tier = 3 }],
            (_, tier, _) => new([new(tier == 2 ? "甲" : "乙", .9)], .9));
        return tracker;
    }
    private static FieldObservation Frame(FieldCell cell, int tier, int seconds = 0) =>
        new(Start.AddSeconds(seconds), true, "m01", 1, new Dictionary<FieldCell, int> { [cell] = tier });

    private static FieldObservation Recognized(params FieldCell[] cells)
    {
        using var image = new OpenCvSharp.Mat(204, 180, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(40, 80, 120));
        return Frame(cells[0], 2, 1) with
        {
            Cells = cells.ToDictionary(c => c, _ => 2),
            Sprites = cells.ToDictionary(c => c, c => FieldReader.CropSprite(image, 90,
                c.Zone == "bench" ? 180 : 156, c.Zone == "bench")!)
        };
    }

    [Fact]
    public void ManualAdditionMovesToPreviouslyUnidentifiedOccupiedCell()
    {
        var tracker = Tracker();
        var bench = new FieldCell("bench", 1, 0);
        var scan = Recognized(bench);
        var cells = new Dictionary<FieldCell, int> { [bench] = 2, [Board] = 0 };
        tracker.Observe(scan with { At = Start, Cells = cells, Sprites = null });
        tracker.SetOperator(Board, "甲", Start);
        tracker.Observe(scan with { Cells = cells }, force: true);
        var unit = Assert.Single(tracker.State!.Units);
        Assert.Equal((bench, "甲", "manual"), (unit.Cell, unit.Operator, unit.Source));
        Assert.Contains(tracker.State.Log, e => e.Kind == "move" && e.From == Board && e.To == bench);
    }

    [Fact]
    public void TwoVisibleCopiesReplaceTheUnseenPreviousPosition()
    {
        var logs = new List<string>();
        var tracker = Tracker(logs.Add);
        var bench = new FieldCell("bench", 1, 0);
        var other = new FieldCell("board", 3, 2);
        tracker.Observe(Recognized(bench, Board), force: true);
        tracker.SetOperator(Board, "甲", Start.AddSeconds(2));
        var seen = Recognized(Board, other);
        var scan = seen with { At = Start.AddSeconds(3), Cells = new Dictionary<FieldCell, int>(seen.Cells) { [bench] = 0 } };
        tracker.Observe(scan, force: true);
        var units = tracker.State!.Units;
        Assert.Equal(new[] { Board, other }, units.Select(u => u.Cell));
        Assert.All(units, u => Assert.Equal("甲", u.Operator));
        Assert.Equal("manual", units[0].Source);
        Assert.Contains("场上(2,2)=甲(手动, —)", logs[^1]);
        output.WriteLine("[Field] " + logs[^1]);
        var score = units[1].Candidates.Single(c => c.Operator == "甲").Score;
        Assert.Contains(FormattableString.Invariant($"场上(3,2)=甲(识别, {score:F3})"), logs[^1]);
        Assert.Contains("整备(1)=空", logs[^1]);
        tracker.Observe(scan with { At = Start.AddSeconds(4) }, force: true);
        Assert.Equal(2, logs.Count);
        // 只看到整备区是空的、场上两格看不到时，场上两份照留
        tracker.Observe(Frame(bench, 0, 5), force: true);
        Assert.Equal(2, tracker.State.Units.Count());
    }

    [Fact]
    public void CopyInHiddenCellIsKept()
    {
        var bench = new FieldCell("bench", 1, 0);
        var other = new FieldCell("board", 3, 2);
        var tracker = Tracker(_ => { });
        tracker.Observe(Recognized(bench, Board), force: true);
        // 整备区被商店面板挡住时不在 Cells 里，看不到不等于空，同名的那份要留着
        tracker.Observe(Recognized(Board, other) with { At = Start.AddSeconds(3) }, force: true);
        Assert.Contains(tracker.State!.Units, u => u.Cell == bench && u.Operator == "甲");
    }

    [Theory]
    [InlineData("board", 2, 2)]
    [InlineData("bench", 1, 0)]
    public void EmptyCellUsesCatalogTierAndSurvivesEmptyScansAndBattle(string zone, int col, int row)
    {
        var cell = new FieldCell(zone, col, row);
        var tracker = Tracker();
        tracker.Observe(Frame(cell, 0));
        Assert.True(tracker.SetOperator(cell, "甲", Start));
        var unit = Assert.Single(tracker.State!.Units);
        Assert.Equal(2, unit.Tier);
        Assert.Equal("manual", unit.Source);
        Assert.Equal("sure", unit.Status);
        Assert.Contains(tracker.State.Roster, r => r.Operator == "甲" && r.Source == "manual");
        for (int i = 1; i <= 8; i++) tracker.Observe(Frame(cell, 0, i * 10), force: true);
        tracker.Observe(Frame(cell, 0, 90) with { IsRest = false });
        tracker.Observe(Frame(cell, 0, 100) with { Round = 2 });
        tracker.Observe(Frame(cell, 0, 110), recognize: false);
        tracker.Observe(Frame(cell, 0, 120), force: true);
        Assert.Equal(unit, Assert.Single(tracker.State.Units));
        tracker.SetOperator(cell, null, Start.AddSeconds(121));
        Assert.Empty(tracker.State.Units);
    }

    [Theory]
    [InlineData(2, "甲", "sure")]
    [InlineData(3, "乙", "pending")]
    public void RealOccupancyKeepsSameTierNameOrRecognizesReplacement(int tier, string name, string status)
    {
        var tracker = Tracker();
        tracker.Observe(Frame(Board, 0));
        tracker.SetOperator(Board, "甲", Start);
        using var image = new OpenCvSharp.Mat(204, 180, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(40, 80, 120));
        var patch = FieldReader.CropSprite(image, 90, 180, true)!;
        tracker.Observe(Frame(Board, tier, 1) with { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [Board] = patch } }, force: true);
        var unit = Assert.Single(tracker.State!.Units);
        Assert.Equal(tier, unit.Tier);
        Assert.Equal(name, unit.Operator);
        Assert.Equal(status, unit.Status);
        tracker.SetOperator(Board, null, Start.AddSeconds(2));
        unit = Assert.Single(tracker.State.Units);
        Assert.Null(unit.Operator);
        Assert.Equal("unknown", unit.Status);
    }

    [Fact]
    public void ManualOccupantFollowsDragAndIsClearedByBlockedMapOrReset()
    {
        var tracker = Tracker();
        var bench = new FieldCell("bench", 1, 0);
        tracker.Observe(Frame(bench, 0));
        tracker.SetOperator(bench, "甲", Start);
        tracker.Observe(Frame(bench, 0, 1));
        tracker.Observe(Frame(bench, 0, 2) with { DragOperator = "甲" });
        tracker.Observe(Frame(bench, 0, 3) with { Cells = new Dictionary<FieldCell, int> { [bench] = 0, [Board] = 2 } });
        Assert.Equal(Board, Assert.Single(tracker.State!.Units).Cell);
        Assert.Equal("甲", tracker.State.Units[0].Operator);
        tracker.SetOperator(bench, "甲", Start);
        tracker.ConfigureMaps(new() { Maps = [new("m01", "测试", 0, new BoardCellType[5, 7], [])] });
        tracker.Observe(Frame(Board, 0, 4), force: true);
        Assert.DoesNotContain(tracker.State.Units, u => u.Cell == Board);
        Assert.Throws<ArgumentException>(() => tracker.SetOperator(Board, "甲", Start));
        Assert.Throws<ArgumentException>(() => tracker.SetOperator(new("board", 7, 0), "甲", Start));
        tracker.Reset();
        tracker.Observe(Frame(bench, 0, 5));
        Assert.Empty(tracker.State!.Units);
    }

    [Fact]
    public async Task ManualAddPublishesPlacementWithAutoRecognitionDisabled()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var data = Path.Combine(AppContext.BaseDirectory, "data");
            var api = await HostApi.CreateAsync(data, directory);
            var tracker = (FieldTracker)typeof(HostApi).GetField("_fieldTracker", flags)!.GetValue(api)!;
            var operators = await new ASPAssistant.Core.Data.JsonDataStore(data).LoadOperatorsAsync();
            tracker.ConfigureIdentity(operators, (_, _, _) => new([], 0));
            tracker.Observe(Frame(Board, 0));
            Assert.False(api.Settings.FieldAuto);
            var placement = new TaskCompletionSource<PlacementResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            int fields = 0;
            api.Changed += (kind, value) =>
            {
                if (kind == "field") fields++;
                if (kind == "placement" && value is PlacementResult result) placement.TrySetResult(result);
            };
            var state = api.SetFieldOperator(Board, "慕斯");
            var result = await placement.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, fields);
            Assert.Equal(state.UpdatedAt, result.BasedOn);
            Assert.Equal(operators.Single(o => o.Name == "慕斯").Tier, Assert.Single(state.Units).Tier);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
