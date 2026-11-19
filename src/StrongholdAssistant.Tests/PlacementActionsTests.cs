using ASPAssistant.Core.Control;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;

namespace StrongholdAssistant.Tests;

public class PlacementActionsTests
{
    private static FieldCell Board(int col) => new("board", col, 0);
    private static FieldCell Bench(int col) => new("bench", col, 0);
    private static FieldState Field(params (FieldCell C, string Name)[] units) =>
        new(null, 1, "", units.Select(x => new FieldUnit(x.C.Zone, x.C.Col, x.C.Row, 1, x.Name, null)).ToArray(), []);
    private static PlacementResult Plan(params PlacementMove[] moves) => new("", "", "", moves, 0, [], null, null, []);

    [Fact]
    public void ChainMovesTheBlockerFirstAndOmitsPendingIdentity()
    {
        var field = Field((Board(1), "慕斯"), (Board(2), "深靛"));
        field.Units[1] = field.Units[1] with { Status = "pending" };
        var plan = Plan(new("慕斯", Board(1), Board(2), "right"), new("深靛", Board(2), Board(3), "left"));
        var result = PlacementActions.From(plan, field);
        Assert.Equal(new GameAction[] { new MoveAction(Board(2), Board(3), "left"), new MoveAction(Board(1), Board(2), "right", "慕斯") }, result);
    }

    [Fact]
    public void CycleBorrowsEmptyBenchAndSkipsOnlyUnsolvableRing()
    {
        var field = Field((Board(1), "甲"), (Board(2), "乙"));
        var plan = Plan(new("甲", Board(1), Board(2), "right"), new("乙", Board(2), Board(1), "down"));
        Assert.Equal(new GameAction[] {new MoveAction(Board(1),Bench(1),null,"甲"),new MoveAction(Board(2),Board(1),"down","乙"),
            new MoveAction(Bench(1),Board(2),"right","甲")}, PlacementActions.From(plan, field));
        var all = field with { Units = field.Units.Concat(Enumerable.Range(1, 10).Select(i => new FieldUnit("bench", i, 0, 1, "丙", null))).ToArray() };
        Assert.Empty(PlacementActions.From(plan, all, out var notes));
        Assert.Single(notes);
    }

    [Fact]
    public void LeavesBoardBeforeDeployingFromBench()
    {
        var field = Field((Bench(1), "甲"), (Board(2), "乙"));
        var plan = Plan(new("甲", Bench(1), Board(1), "up"), new("乙", Board(2), Bench(2), null));
        Assert.Equal(new GameAction[] {new MoveAction(Board(2),Bench(2),null,"乙"),
            new MoveAction(Bench(1),Board(1),"up","甲")}, PlacementActions.From(plan, field));
    }
}
