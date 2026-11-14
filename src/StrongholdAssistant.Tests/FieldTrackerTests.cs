using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests;

public sealed class FieldTrackerTests
{
    private static readonly FieldCell Bench = new("bench", 10, 0), Other = new("bench", 9, 0), Board = new("board", 4, 2);
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static FieldObservation O(double seconds, int bench = 0, int board = 0,
        Dictionary<int, string>? shop = null, string? drag = null, int? money = 4, bool rest = true)
        => new(Start.AddSeconds(seconds), rest, "m01", 1, new Dictionary<FieldCell, int> { [Bench] = bench, [Board] = board }, shop, money, drag);

    [Fact]
    public void MissingPanelMovePreservesIdentityAndSourceAcrossZones()
    {
        var t = new FieldTracker(); t.Observe(O(0, board: 2));
        t.SetOperator(Board, "砾", Start);
        t.Observe(O(.8, bench: 2)); t.Observe(O(2.5, bench: 2));
        var unit = Assert.Single(t.State!.Units);
        Assert.Equal("砾", unit.Operator); Assert.Equal("manual", unit.Source);
        var e = Assert.Single(t.State.Log);
        Assert.Equal("move", e.Kind); Assert.Equal("砾", e.Operator);
        Assert.Equal(Board, e.From); Assert.Equal(Bench, e.To);
    }

    [Fact]
    public void MissingPanelDifferentTiersDoNotInferMove()
    {
        var t = new FieldTracker(); t.Observe(O(0, board: 1));
        t.SetOperator(Board, "慕斯", Start);
        t.Observe(O(.8, bench: 2)); t.Observe(O(2.5, bench: 2));
        Assert.Null(Assert.Single(t.State!.Units).Operator);
        Assert.Contains(t.State.Log, e => e.Kind == "lost");
        Assert.Contains(t.State.Log, e => e.Kind == "gain");
        Assert.DoesNotContain(t.State.Log, e => e.Kind == "move");
    }

    [Fact]
    public void PurchaseTakesPriorityOverSameTierMove()
    {
        var t = new FieldTracker(); t.Observe(O(0, board: 1, shop: new() { [2] = "灰喉" }));
        t.SetOperator(Board, "慕斯", Start);
        t.Observe(O(.8, bench: 1, shop: new()));
        Assert.Equal("灰喉", Assert.Single(t.State!.Units).Operator);
        Assert.Equal("purchase", t.State.Units[0].Source);
        Assert.Contains(t.State.Log, e => e.Kind == "buy");
        Assert.DoesNotContain(t.State.Log, e => e.Kind == "move");
    }

    [Fact]
    public void BuyUsesDisappearingSlotAmongDuplicateNames()
    {
        var t = new FieldTracker();
        t.Observe(O(0, shop: new() { [2] = "慕斯", [3] = "慕斯" }));
        t.Observe(O(.8, bench: 1, shop: new() { [3] = "慕斯" }));
        Assert.Equal("purchase", Assert.Single(t.State!.Units).Source);
        var e = Assert.Single(t.State.Log); Assert.Equal("buy", e.Kind); Assert.Equal(Bench, e.To); Assert.Equal("慕斯", e.Operator);
        Assert.False(t.Observe(O(1.6, bench: 1, shop: new() { [3] = "慕斯" })));
    }

    [Fact]
    public void PurchaseCanFollowOccupancyOrMergeWithoutNewCell()
    {
        var t = new FieldTracker();
        t.Observe(O(0, shop: new() { [2] = "慕斯" }));
        t.Observe(O(.8, bench: 1, shop: new() { [2] = "慕斯" }));
        t.Observe(O(1.6, bench: 1, shop: new()));
        Assert.Equal("buy", Assert.Single(t.State!.Log).Kind);
        t.Observe(O(2, bench: 1, shop: new() { [1] = "灰喉" }));
        t.Observe(O(2.8, bench: 1, shop: new()));
        t.Observe(O(4.5, bench: 1, shop: new()));
        Assert.Null(t.State!.Log[0].To); Assert.Equal("buy", t.State.Log[0].Kind);
    }

    [Fact]
    public void UnexplainedBenchArrivalIsUnknownGain()
    {
        var t = new FieldTracker(); t.Observe(O(0)); t.Observe(O(.8, bench: 2)); t.Observe(O(2.5, bench: 2));
        Assert.Null(Assert.Single(t.State!.Units).Operator);
        Assert.Equal("gain", Assert.Single(t.State.Log).Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("角峰")]
    [InlineData("慕斯")]
    public void DragOverridesUnknownOrConflictingIdentityAndCarriesManualAssignment(string? original)
    {
        var t = new FieldTracker(); t.Observe(O(0, bench: 1));
        if (original is not null) t.SetOperator(Bench, original, Start);
        t.Observe(O(.8, drag: "慕斯"));
        t.Observe(O(1.6, board: 1));
        var unit = Assert.Single(t.State!.Units); Assert.Equal("慕斯", unit.Operator); Assert.Equal("drag", unit.Source); Assert.Equal(Board, unit.Cell);
        var e = Assert.Single(t.State.Log); Assert.Equal("move", e.Kind); Assert.Equal(Bench, e.From);
    }

    [Fact]
    public void DragWithMoneyIncreaseIsSellOtherwiseDisappearanceIsLost()
    {
        var t = new FieldTracker(); t.Observe(O(0, bench: 1)); t.SetOperator(Bench, "慕斯", Start);
        t.Observe(O(.8, drag: "慕斯")); t.Observe(O(1.6, money: 5));
        Assert.Equal("sell", Assert.Single(t.State!.Log).Kind);
        t.Reset(); t.Observe(O(0, board: 1)); t.SetOperator(Board, "灰喉", Start); t.Observe(O(.8));
        var lost = Assert.Single(t.State!.Log); Assert.Equal("lost", lost.Kind); Assert.Equal("灰喉", lost.Operator);
    }

    [Fact]
    public void DistinctTiersIdentifyBothSidesOfSwap()
    {
        var t = new FieldTracker(); t.Observe(O(0, bench: 1, board: 2));
        t.SetOperator(Bench, "慕斯", Start); t.SetOperator(Board, "砾", Start);
        t.Observe(O(.8, drag: "慕斯")); t.Observe(O(1.6, bench: 2, board: 1));
        Assert.Equal("砾", t.State!.Units.Single(u => u.Cell == Bench).Operator);
        Assert.Equal("慕斯", t.State.Units.Single(u => u.Cell == Board).Operator);
        Assert.Equal(2, t.State.Log.Count(e => e.Kind == "move"));
    }

    [Fact]
    public void UnobservableSameTierSwapClearsIdentity()
    {
        var t = new FieldTracker(); t.Observe(O(0, bench: 1, board: 1));
        t.SetOperator(Bench, "慕斯", Start); t.SetOperator(Board, "灰喉", Start);
        t.Observe(O(.8, drag: "慕斯")); t.Observe(O(1.6, bench: 1, board: 1));
        Assert.All(t.State!.Units, u => Assert.Null(u.Operator));
    }

    [Fact]
    public void InvisibleCellsAndBattleAreFrozenThenChangedTierLosesIdentity()
    {
        var t = new FieldTracker(); t.Observe(O(0, board: 1)); t.SetOperator(Board, "慕斯", Start);
        t.Observe(new(Start.AddSeconds(.8), true, "m01", 1, new Dictionary<FieldCell, int>()));
        Assert.Equal("慕斯", Assert.Single(t.State!.Units).Operator);
        t.Observe(O(1.6, rest: false)); Assert.Single(t.State.Units);
        t.Observe(O(2.4, board: 2)); Assert.Null(Assert.Single(t.State.Units).Operator); Assert.Empty(t.State.Log);
    }

    [Fact]
    public void ResetClearsManualIdentityEventsAndPendingPurchases()
    {
        var t = new FieldTracker(); t.Observe(O(0, shop: new() { [1] = "慕斯" })); t.Observe(O(.8, bench: 1, shop: new()));
        t.SetOperator(Bench, "灰喉", Start); t.Reset(); Assert.Null(t.State);
        t.Observe(O(3)); Assert.Empty(t.State!.Units); Assert.Empty(t.State.Log);
    }
}
