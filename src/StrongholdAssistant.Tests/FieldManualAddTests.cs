using System.Reflection;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.Placement;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class FieldManualAddTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-17T00:00:00Z");
    private static readonly FieldCell Board = new("board", 2, 2);
    private static FieldTracker Tracker()
    {
        var tracker = new FieldTracker();
        tracker.ConfigureIdentity([new() { Name = "甲", Tier = 2 }, new() { Name = "乙", Tier = 3 }],
            (_, tier, _) => new([new(tier == 2 ? "甲" : "乙", .9)], .9));
        return tracker;
    }
    private static FieldObservation Frame(FieldCell cell, int tier, int seconds = 0) =>
        new(Start.AddSeconds(seconds), true, "m01", 1, new Dictionary<FieldCell, int> { [cell] = tier });

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
