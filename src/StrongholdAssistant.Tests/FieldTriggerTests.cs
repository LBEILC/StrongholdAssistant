using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests;

public sealed class FieldTriggerTests
{
    private static readonly FieldCell Bench = new("bench", 1, 0);
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static FieldObservation Frame(int second, int tier, int money = 10, Dictionary<int, string>? shop = null)
        => new(Start.AddSeconds(second), true, "m01", 1, new Dictionary<FieldCell, int> { [Bench] = tier }, shop, money);

    [Theory]
    [InlineData(.1, "auto")]
    [InlineData(.03, "pending")]
    public void ShopRowDoesNotOverrideVisualIdentity(double gap, string status)
    {
        var t = new FieldTracker();
        t.ConfigureIdentity([new() { Name = "甲", Tier = 1 }, new() { Name = "乙", Tier = 1 },
            new() { Name = "丙", Tier = 1 }, new() { Name = "丁", Tier = 1 }, new() { Name = "戊", Tier = 2 }],
            (_, _, names) => new(names.Select(n => new SpriteCandidate(n, n == "乙" ? .8 : n == "甲" ? .8 - gap : .3)).ToArray(), 0));
        Dictionary<int, string> shop = new() { [1] = "甲", [2] = "戊" };
        t.Observe(Frame(0, 0, shop: shop));
        t.Observe(Frame(1, 1, 9, shop) with { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [Bench] = new([], [1, 2, 3], true) } });
        Assert.Equal("乙", Assert.Single(t.State!.Units).Operator);
        Assert.Equal(status, t.State.Units[0].Status);
    }

    [Fact]
    public void IdleFramesFreezePendingAndRequireTwoConsecutiveOccupancyChanges()
    {
        int calls = 0;
        var t = new FieldTracker();
        t.ConfigureIdentity([new() { Name = "甲", Tier = 1 }, new() { Name = "乙", Tier = 1 }],
            (_, _, names) => { calls++; return new(names.Select(n => new SpriteCandidate(n, .5)).ToArray(), 0); });
        var first = Frame(0, 1) with { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [Bench] = new([], [1, 2, 3], true) } };
        t.Observe(first);
        var state = t.State;
        for (int second = 1; second <= 5; second++)
            Assert.False(t.Observe(first with { At = Start.AddSeconds(second), Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [Bench] = new([], [9, 1, 8], true) } }));
        Assert.Same(state, t.State); Assert.Equal(1, calls);
        Assert.False(t.Observe(Frame(6, 0)));
        Assert.False(t.Observe(Frame(7, 1)));
        Assert.False(t.Observe(Frame(8, 0)));
        Assert.True(t.Observe(Frame(9, 0)));
        Assert.Empty(t.State!.Units);
    }

    [Theory]
    [InlineData(.1, "auto")]
    [InlineData(.01, "pending")]
    public void NewUnitRanksAllOperatorsAtObservedTier(double gap, string status)
    {
        string[] matched = [];
        var t = new FieldTracker();
        t.ConfigureIdentity([new() { Name = "甲", Tier = 1 }, new() { Name = "乙", Tier = 1 },
            new() { Name = "丙", Tier = 2 }, new() { Name = "丁", Tier = 1 }],
            (_, _, names) => { matched = names; return new(names.Select((n, i) => new SpriteCandidate(n, .8 - i * gap)).ToArray(), gap); });
        Dictionary<int, string> shop = new() { [1] = "甲", [2] = "乙", [3] = "丙" };
        t.Observe(Frame(0, 0, shop: shop));
        t.Observe(Frame(1, 1, 9, shop) with { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [Bench] = new([], [1, 2, 3], true) } });
        Assert.Equal(new[] { "甲", "乙", "丁" }, matched);
        var unit = Assert.Single(t.State!.Units);
        Assert.Equal(status, unit.Status);
        Assert.Equal(new[] { "甲", "乙", "丁" }, unit.Candidates.Select(c => c.Operator));
        Assert.DoesNotContain(t.State.Roster, r => r.Operator == "乙");
    }

    [Fact]
    public void FreeThreeChoiceDoesNotRecordPurchase()
    {
        var t = new FieldTracker();
        t.ConfigureIdentity([new() { Name = "甲", Tier = 1 }, new() { Name = "乙", Tier = 2 }, new() { Name = "丙", Tier = 3 }],
            (_, _, _) => throw new Exception("没有小人图块时不调用匹配"));
        t.Observe(Frame(0, 0, shop: new() { [1] = "甲", [2] = "乙", [3] = "丙" }));
        t.Observe(Frame(1, 1, shop: new()));
        t.Observe(Frame(2, 1, shop: new()));
        var unit = Assert.Single(t.State!.Units);
        Assert.Null(unit.Operator); Assert.Equal("unknown", unit.Status);
        Assert.Empty(t.State.Roster);
        Assert.DoesNotContain(t.State.Log, e => e.Kind == "buy");
    }
}
