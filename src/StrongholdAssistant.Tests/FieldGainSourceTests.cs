using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests;

public sealed class FieldGainSourceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly FieldCell A = new("board", 1, 1), B = new("board", 2, 1), C = new("board", 3, 1), Bench = new("bench", 1, 0);
    private static readonly Operator[] Operators = [
        new() { Name = "伊桑", Tier = 3, AdditionalCovenants = ["突袭"] },
        new() { Name = "甲", Tier = 1, CoreCovenants = ["炎"] },
        new() { Name = "乙", Tier = 1, CoreCovenants = ["炎"] },
        new() { Name = "丙", Tier = 1, CoreCovenants = ["维多利亚"] },
        new() { Name = "丁", Tier = 2, CoreCovenants = ["炎"] }
    ];
    private static FieldObservation Frame(int sec, params (FieldCell Cell, int Tier)[] cells) =>
        new(Start.AddSeconds(sec), true, "m01", 3, cells.ToDictionary(p => p.Cell, p => p.Tier));
    private static FieldSpritePatch Patch => new([], Enumerable.Range(0, 2040).Select(i => (byte)(i % 251)).ToArray(), true);
    private static FieldObservation Picture(FieldObservation o) => o with { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [Bench] = Patch } };

    [Fact]
    public void EthanSaleRecordsSourceOnConfirmation()
    {
        string[] candidates = [];
        var t = new FieldTracker();
        t.ConfigureIdentity(Operators, (_, _, names) => { candidates = names; return new(names.Select((n,i) => new SpriteCandidate(n,.8-i*.01)).ToArray(),.01); });
        var ethan = new FieldCell("bench",2,0);
        t.Observe(Frame(0,(A,1),(B,1),(C,1),(ethan,3)) with { Money=5 });
        t.SetOperator(A,"甲",Start);t.SetOperator(B,"甲",Start);t.SetOperator(C,"丙",Start);t.SetOperator(ethan,"伊桑",Start);
        t.Observe(Frame(1) with { DragOperator="伊桑",Money=5 });
        t.Observe(Frame(2,(ethan,0)) with { Money=6 });
        t.Observe(Picture(Frame(3,(Bench,1))));
        t.Observe(Picture(Frame(4,(Bench,1))));
        Assert.Equal(new[]{"甲","乙","丙"}.Order(),candidates.Order());
        t.SetOperator(Bench,"乙",Start.AddSeconds(3));
        Assert.Contains(t.State!.Roster,r=>r.Operator=="乙"&&r.Source=="ethan");
    }

    [Theory]
    [InlineData("黑白相易", "auto", "乙")]
    [InlineData("人才盲盒", "auto", "丙")]
    public void StrategyKeepsAllowedPoolAndConfirmationRecordsSource(string strategy,string status,string answer)
    {
        string[] candidates=[];
        var t=new FieldTracker();
        t.ConfigureIdentity(Operators,(_,_,names)=>{candidates=names;return new(names.Select((n,i)=>new SpriteCandidate(n,.8-i*.1)).ToArray(),.1);});
        t.SeedRoster(strategy,[],3,Start);
        t.Observe(Frame(-1));
        t.Observe(Picture(Frame(0,(Bench,1))));
        t.Observe(Picture(Frame(7,(Bench,1))));
        Assert.Equal(new[]{"甲","乙","丙"},candidates);
        Assert.Equal(status,Assert.Single(t.State!.Units).Status);
        t.SetOperator(Bench,answer,Start);
        Assert.Contains(t.State.Roster,r=>r.Operator==answer&&r.Source=="strategy");
    }

    [Theory]
    [InlineData(.4, "乙", 3)]
    [InlineData(.2, "乙", 3)]
    public void UnknownGainUsesAllowedTierRegardlessOfRosterScore(double rosterScore, string expected, int count)
    {
        var t=new FieldTracker();
        t.ConfigureIdentity(Operators,(_,_,names)=>new(names.Select(n=>new SpriteCandidate(n,n=="乙"?.9:rosterScore)).ToArray(),.5));
        t.AddRoster("甲","manual",Start);
        t.Observe(Frame(-1));
        t.Observe(Picture(Frame(0,(Bench,1))));
        t.Observe(Picture(Frame(7,(Bench,1))));
        Assert.Equal(expected,Assert.Single(t.State!.Units).Operator);
        Assert.Equal(count,t.State.Units[0].Candidates.Length);
        Assert.Equal("auto",t.State.Units[0].Status);
    }

    [Fact]
    public void StrategySourceDoesNotExcludeSessionCandidates()
    {
        string[] candidates = [];
        var t = new FieldTracker();
        t.ConfigureIdentity(Operators, (_, _, names) =>
        {
            candidates = names;
            return new(names.Select(n => new SpriteCandidate(n, n == "乙" ? .9 : .5)).ToArray(), .4);
        });
        t.SeedRoster("人才盲盒", ["乙"], 3, Start);
        t.Observe(Frame(-1));
        t.Observe(Picture(Frame(0, (Bench, 1))));
        t.Observe(Picture(Frame(7, (Bench, 1))));
        Assert.Contains("乙", candidates);
        Assert.Contains("丙", candidates);
        Assert.Equal("乙", Assert.Single(t.State!.Units).Operator);
    }

    [Fact]
    public void ConsumedRadioOnDetailPanelKeepsEquipmentSourceForArrival()
    {
        string[] candidates=[];
        var t=new FieldTracker();
        t.ConfigureIdentity(Operators,(_,_,names)=>{candidates=names;return new(names.Select(n=>new SpriteCandidate(n,.5)).ToArray(),0);});
        t.Observe(Frame(0,(A,1)));t.SetOperator(A,"甲",Start);
        t.Observe(Frame(1) with { MainScreen=false,PanelOperator="甲",Equipment=[new("简易通讯机",false)] });
        t.Observe(Frame(2) with { MainScreen=false,PanelOperator="甲",Equipment=[] });
        t.Observe(Picture(Frame(3,(Bench,1))));
        t.Observe(Picture(Frame(10,(Bench,1))));
        Assert.Equal(new[]{"甲","乙","丙"},candidates);
        t.SetOperator(Bench,"乙",Start.AddSeconds(3));
        Assert.Contains(t.State!.Roster,r=>r.Operator=="乙"&&r.Source=="equipment");
    }
}
