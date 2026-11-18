using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests;

public sealed class FieldIdentityTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("优等生", false)]
    public void UnknownStrategyIncludesIronhideGift(string? strategy, bool included)
    {
        string[] candidates = [];
        var t = new FieldTracker();
        t.ConfigureIdentity([
            new() { Name = "暴躁铁皮", Tier = 1, Obtain = "策略「暴躁铁皮」第 1 回合赠送" },
            new() { Name = "甲", Tier = 1 },
            new() { Name = "装备赠送", Tier = 1, Obtain = "装备赠送" }
        ], (_, _, names) =>
        {
            candidates = names;
            return new(names.Select(n => new SpriteCandidate(n, n == "暴躁铁皮" ? .95 : .4)).ToArray(), .55);
        });
        t.SeedRoster(strategy, [], 1, Start);
        t.Observe(Frame(0, (A, 1, Pic())), force: true);
        Assert.Equal(included, candidates.Contains("暴躁铁皮"));
        Assert.DoesNotContain("装备赠送", candidates);
        Assert.Equal(included ? "暴躁铁皮" : "甲", Assert.Single(t.State!.Units).Operator);
    }

    [Fact]
    public void UnknownStrategyKeepsPithTierAndPrototypeSeparation()
    {
        var t = new FieldTracker();
        var pools = new System.Collections.Concurrent.ConcurrentDictionary<int, string[]>();
        t.ConfigureIdentity([
            new() { Name = "Pith", Tier = 5, Diy = true },
            new() { Name = PithIdentity.Gift, Tier = 3, Obtain = "策略「优等生」第 1 回合赠送" }
        ], (_, tier, names) =>
        {
            pools[tier] = names;
            return new(names.Select(n => new SpriteCandidate(n, .95)).ToArray(), null);
        });
        t.SeedRoster(null, [], 1, Start);
        t.Observe(Frame(0, (A, 5, Pic()), (B, 3, Pic(1))), force: true);
        Assert.Equal(new[] { "Pith" }, pools[5]);
        Assert.Equal(new[] { PithIdentity.Gift }, pools[3]);
    }

    private static readonly FieldCell A = new("board", 1, 1), B = new("board", 2, 1), C = new("board", 3, 1), Bench = new("bench", 1, 0);
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static FieldSpritePatch Pic(int shift = 0, bool bench = false)
    {
        using var image = new OpenCvSharp.Mat(204, 180, OpenCvSharp.MatType.CV_8UC3);
        var pixels = new byte[204 * 180 * 3];
        new Random(shift).NextBytes(pixels);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, image.Data, pixels.Length);
        return FieldReader.CropSprite(image, 90, bench ? 180 : 156, bench)!;
    }
    private static FieldObservation Frame(double sec, params (FieldCell Cell, int Tier, FieldSpritePatch? Patch)[] cells)
        => new(Start.AddSeconds(sec), true, "m01", 1, cells.ToDictionary(p => p.Cell, p => p.Tier))
        { Sprites = cells.Where(p => p.Tier > 0 && p.Patch is not null).ToDictionary(p => p.Cell, p => p.Patch!) };
    private static void ObserveStable(FieldTracker t, FieldObservation o)
    {
        t.Observe(o);
        t.Observe(o with { At = o.At.AddMilliseconds(100) });
    }
    private static FieldTracker Tracker(Func<FieldSpritePatch, int, string[], SpriteMatch>? match = null)
    {
        var t = new FieldTracker();
        t.ConfigureIdentity([new() { Name = "甲", Tier = 1 }, new() { Name = "乙", Tier = 1 },
            new() { Name = "丁", Tier = 1 },
            new() { Name = "丙", Tier = 2, Obtain = "策略「赠送」第 2 回合赠送" }],
            match ?? ((_, _, names) => new(names.Select((n, i) => new SpriteCandidate(n, .8 - i * .1)).ToArray(), .1)));
        return t;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragInheritsStatusAndForceCanCorrectAutomaticOrigin(bool confirmed)
    {
        bool changed = false;
        var t = Tracker((_, _, names) => new(names.Select(n => new SpriteCandidate(n,
            n == (changed ? "乙" : "甲") ? .9 : .4)).ToArray(), .5));
        t.Observe(Frame(0, (A, 1, Pic())));
        if (confirmed) t.SetOperator(A, "甲", Start);
        t.Observe(Frame(1, (A, 1, Pic())) with { DragOperator = "甲" });
        var landed = Frame(2, (A, 0, null), (B, 1, Pic(20)));
        t.Observe(landed);
        Assert.Equal(confirmed ? "sure" : "auto", Assert.Single(t.State!.Units).Status);
        changed = true;
        t.Observe(landed with { At = Start.AddSeconds(3) }, force: true);
        Assert.Equal(confirmed ? "甲" : "乙", Assert.Single(t.State!.Units).Operator);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForceRechecksAutomaticIdentityButKeepsPlayerConfirmation(bool confirm)
    {
        bool changed = false;
        var t = Tracker((_, _, names) => new(names.Select(n =>
            new SpriteCandidate(n, n == (changed ? "乙" : "甲") ? .9 : .5)).ToArray(), .4));
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        var frame = Frame(0, (A, 1, Pic()));
        t.Observe(frame);
        if (confirm) t.SetOperator(A, "甲", Start);
        changed = true;
        t.Observe(frame with { At = Start.AddSeconds(1) }, force: true);
        var unit = Assert.Single(t.State!.Units);
        Assert.Equal(confirm ? "甲" : "乙", unit.Operator);
        Assert.Equal(confirm ? "sure" : "auto", unit.Status);
    }

    [Fact]
    public void ForceUsesCurrentFrameWithoutAveragingOldScores()
    {
        double a = .6, b = .58;
        var t = Tracker((_, _, names) => new(names.Select(n => new SpriteCandidate(n, n == "甲" ? a : b)).ToArray(), null));
        t.ConfigureCandidatePool(["丁"], null);
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        t.Observe(Frame(0, (A, 1, Pic(0))), force: true);
        t.Observe(Frame(1, (A, 1, Pic(1))), force: true);
        a = .58; b = .6;
        var third = Frame(2, (A, 1, Pic(2)));
        t.Observe(third, force: true);
        t.Observe(third, force: true);
        var unit = Assert.Single(t.State!.Units);
        Assert.Equal("乙", unit.Operator);
        Assert.Equal("pending", unit.Status);
        Assert.Equal(.6, unit.Candidates[0].Score, 6);
        // 空格后重新出现同样只使用当前帧。
        t.Observe(Frame(3, (A, 0, null)));
        a = .4; b = .8;
        t.Observe(Frame(4, (A, 1, Pic(4))), force: true);
        unit = Assert.Single(t.State!.Units);
        Assert.Equal("乙", unit.Operator);
        Assert.Equal("auto", unit.Status);
        Assert.Equal(.8, unit.Candidates[0].Score, 6);
    }

    [Fact]
    public void IdentityDoesNotMatchOnUntriggeredScans()
    {
        int calls = 0;
        var t = Tracker((_, _, names) => { calls++; return new(names.Select(n => new SpriteCandidate(n, .6)).ToArray(), 0); });
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        t.Observe(Frame(0, (A, 1, Pic(0))));
        int initial = calls;
        t.Observe(Frame(1, (A, 1, Pic(1))));
        t.Observe(Frame(2, (A, 1, Pic(2))));
        Assert.Equal(initial, calls);
    }

    [Fact]
    public void MoveObservedBeforeDragDoesNotAcquireLaterDragName()
    {
        var t = Tracker();
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        var pic = Pic();
        t.Observe(Frame(0, (A, 1, pic)));
        // 单帧换格尚未达到稳定触发，但它已发生在下一次拖动之前。
        t.Observe(Frame(1, (A, 0, null), (B, 1, pic)));
        t.Observe(Frame(2) with { DragOperator = "乙", MainScreen = false });
        t.Observe(Frame(3, (A, 0, null), (B, 1, pic)));
        var unit = Assert.Single(t.State!.Units);
        Assert.Equal(B, unit.Cell);
        Assert.Equal("甲", unit.Operator);
        Assert.NotEqual("drag", unit.Source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TierFlickerKeepsConfirmedAndAutoIdentity(bool confirm)
    {
        var t = Tracker();
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        t.Observe(Frame(0, (A, 1, Pic())));
        if (confirm) t.SetOperator(A, "甲", Start);
        var original = Assert.Single(t.State!.Units);
        t.Observe(Frame(1, (A, 2, Pic(1))));
        Assert.Equal(original, Assert.Single(t.State.Units));
        t.Observe(Frame(2, (A, 1, Pic(2))));
        t.Observe(Frame(3, (A, 2, Pic(3))));
        Assert.Equal(original, Assert.Single(t.State.Units));
        t.Observe(Frame(4, (A, 3, Pic(4))));
        Assert.Equal(original, Assert.Single(t.State.Units));
        t.Observe(Frame(5, (A, 2, Pic(5))));
        Assert.Equal(original, Assert.Single(t.State.Units));
        t.Observe(Frame(6, (A, 2, Pic(6))));
        var changed = Assert.Single(t.State.Units);
        Assert.Equal(2, changed.Tier); Assert.NotEqual("甲", changed.Operator);
    }

    [Fact]
    public void AutomaticLearningKeepsEarlierForceTemplate()
    {
        bool changed = false;
        var t = Tracker((_, _, names) => new(names.Select(n =>
            new SpriteCandidate(n, n == (changed ? "乙" : "甲") ? .9 : .5)).ToArray(), .4));
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        var original = Pic();
        t.Observe(Frame(0, (A, 1, original)) with { Money = 20 }, force: true);
        for (int i = 1; i <= 8; i++)
            t.Observe(Frame(i, (A, 1, Pic(i))) with { Money = 20 + i });
        changed = true;
        t.Observe(Frame(9, (A, 1, original)), force: true);
        Assert.Equal("甲", Assert.Single(t.State!.Units).Operator);
    }

    [Fact]
    public void UnconfirmedTierFrameDoesNotTeachOldIdentityToAnotherCell()
    {
        var t = Tracker();
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        ObserveStable(t, Frame(0, (A, 1, Pic()))); t.SetOperator(A, "甲", Start);
        var other = Pic(10);
        ObserveStable(t, Frame(1, (A, 2, other)));
        ObserveStable(t, Frame(2, (A, 1, Pic()), (B, 1, other)));
        // 新格应独立比对，不能直接承接上一帧未确认图块的错误姓名。
        Assert.Equal("sprite", t.State!.Units.Single(u => u.Cell == B).Source);
    }

    [Fact]
    public void EmptyOrInvisibleScanBreaksConsecutiveTierReadings()
    {
        var t = Tracker();
        t.Observe(Frame(0, (A, 1, Pic()))); t.SetOperator(A, "甲", Start);
        t.Observe(Frame(1, (A, 2, Pic(1))));
        t.Observe(Frame(2));
        t.Observe(Frame(3, (A, 2, Pic(1))));
        Assert.Equal(1, Assert.Single(t.State!.Units).Tier);
        t.Observe(Frame(4, (A, 0, null)));
        t.Observe(Frame(5, (A, 2, Pic(1))));
        t.Observe(Frame(6, (A, 2, Pic(1))));
        Assert.Equal(2, Assert.Single(t.State.Units).Tier);
    }

    [Fact]
    public void ManualIdentitySurvivesBattle()
    {
        int calls = 0;
        var t = Tracker((_, _, names) => { calls++; return new(names.Select((n, i) => new SpriteCandidate(n, .8 - i * .1)).ToArray(), .1); });
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        t.Observe(Frame(0, (A, 1, Pic())));
        t.SetOperator(A, "甲", Start);
        int initial = calls;
        t.Observe(Frame(.5) with { IsRest = false });
        t.Observe(Frame(1, (A, 1, Pic(100))));
        Assert.Equal(initial, calls); Assert.Equal("sure", t.State!.Units[0].Status);
        Assert.Equal("甲", t.State.Units[0].Operator);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoMissingScansKeepIdentityWithoutMatching(bool confirm)
    {
        int calls = 0;
        var t = Tracker((_, _, names) => { calls++; return new(names.Select(n => new SpriteCandidate(n, .5)).ToArray(), 0); });
        ObserveStable(t, Frame(0, (A, 1, Pic())));
        if (confirm) t.SetOperator(A, "甲", Start);
        var original = t.State!.Units[0];
        t.Observe(Frame(4, (A, 0, null)));
        t.Observe(Frame(8, (A, 0, null)));
        ObserveStable(t, Frame(12, (A, 1, Pic(100))));
        Assert.Equal(original, Assert.Single(t.State!.Units));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpiredOrSoldCellDoesNotRestoreIdentity(bool sold)
    {
        int calls = 0;
        var t = Tracker((_, _, names) => { calls++; return new(names.Select(n => new SpriteCandidate(n, .5)).ToArray(), 0); });
        ObserveStable(t, Frame(0, (A, 1, Pic())) with { Money = 4 });
        t.SetOperator(A, "乙", Start);
        t.AddRoster("甲", "manual", Start);
        t.AddRoster("丁", "manual", Start);
        if (sold) ObserveStable(t, Frame(1) with { DragOperator = "乙", Money = 4 });
        ObserveStable(t, Frame(4, (A, 0, null)) with { Money = sold ? 5 : 4 });
        if (!sold)
        {
            ObserveStable(t, Frame(8, (A, 0, null)));
            ObserveStable(t, Frame(12, (A, 0, null)));
        }
        ObserveStable(t, Frame(sold ? 5 : 16, (A, 1, Pic(100))));
        Assert.True(calls > 1);
        Assert.Equal("pending", Assert.Single(t.State!.Units).Status);
    }

    [Theory]
    [InlineData(false, "pending")]
    [InlineData(true, "sure")]
    public void SameTierMoveKeepsPreviousConfirmation(bool confirmed, string expected)
    {
        var t = Tracker((_, _, names) => new(names.Select((n, i) => new SpriteCandidate(n, .8 - i * .03)).ToArray(), .03));
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        ObserveStable(t, Frame(0, (A, 1, Pic())));
        if (confirmed) t.SetOperator(A, "甲", Start);
        ObserveStable(t, Frame(4, (A, 0, null), (B, 1, Pic(100))));
        Assert.Equal(expected, Assert.Single(t.State!.Units).Status);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.1)]
    public void PersonalTemplateSearchesPositionAndScale(double scale)
    {
        using var source = new OpenCvSharp.Mat(204, 180, OpenCvSharp.MatType.CV_8UC3, new OpenCvSharp.Scalar(20, 20, 20));
        for (int i = 0; i < 8; i++)
            OpenCvSharp.Cv2.Circle(source, new OpenCvSharp.Point(70 + i % 3 * 20, 60 + i * 13), 8,
                new OpenCvSharp.Scalar(40 + i * 20, 200 - i * 15, 80 + i * 10), -1);
        using var transform = new OpenCvSharp.Mat(2, 3, OpenCvSharp.MatType.CV_64FC1);
        transform.Set(0, 0, scale); transform.Set(0, 1, 0d); transform.Set(0, 2, 90 * (1 - scale) + 8);
        transform.Set(1, 0, 0d); transform.Set(1, 1, scale); transform.Set(1, 2, 108 * (1 - scale) - 6);
        using var moved = new OpenCvSharp.Mat();
        OpenCvSharp.Cv2.WarpAffine(source, moved, transform, source.Size());
        var a = FieldReader.CropSprite(source, 90, 156, false)!;
        var b = FieldReader.CropSprite(moved, 90, 156, false)!;
        Assert.True(SpriteMatcher.MatchPersonal(a, b) > .9);
        Assert.True(FieldTracker.SpriteSimilarity(a, b) < FieldTracker.SameSpriteScore);
    }

    [Fact]
    public void RosterDoesNotBoostVisualRanking()
    {
        var t = Tracker((_, _, names) => new(names.Select(n =>
            new SpriteCandidate(n, n == "甲" ? .59 : n == "乙" ? .60 : .2)).ToArray(), .01));
        t.AddRoster("甲", "manual", Start);
        t.Observe(Frame(0, (A, 1, Pic())));
        var unit = Assert.Single(t.State!.Units);
        Assert.Equal("乙", unit.Operator);
        Assert.Equal("pending", unit.Status);
        Assert.Contains(t.State.Roster, r => r.Operator == "甲" && r.Source == "manual");
    }

    [Fact]
    public void ShopDisappearanceDoesNotRecordAnOperator()
    {
        var t = Tracker();
        t.Observe(Frame(0, (A, 1, Pic()), (Bench, 0, null)) with { Money = 4, Shop = new Dictionary<int, string> { [1] = "乙" } });
        t.SetOperator(A, "甲", Start);
        t.Observe(Frame(.8, (A, 0, null), (Bench, 1, Pic())) with { Money = 3, Shop = new Dictionary<int, string>() });
        var u = Assert.Single(t.State!.Units);
        Assert.Equal("甲", u.Operator);
        Assert.DoesNotContain(t.State.Roster, r => r.Operator == "乙");
        Assert.DoesNotContain(t.State.Log, e => e.Kind == "buy");
    }

    [Theory]
    [InlineData(.59, "甲")]
    [InlineData(.63, "乙")]
    public void PreviousConfirmationDoesNotPromoteNameOrLowerAutoFillGap(double score, string expected)
    {
        var t = Tracker((_, _, names) => new(names.Select(n =>
            new SpriteCandidate(n, n == "甲" ? .60 : n == "乙" ? score : .20)).ToArray(), null));
        t.Observe(Frame(0, (A, 1, null)));
        t.SetOperator(A, "乙", Start);
        t.SetOperator(A, null, Start);
        t.Observe(Frame(1, (A, 1, null), (B, 1, Pic())), force: true);
        var unit = t.State!.Units.Single(u => u.Cell == B);
        Assert.Equal(expected, unit.Operator);
        Assert.Equal("pending", unit.Status);
    }

    [Theory]
    [InlineData(.1, "sure")]
    [InlineData(.01, "sure")]
    public void ConfirmedMoveWithoutInitialPictureKeepsSureRegardlessOfGap(double gap, string status)
    {
        var t = Tracker((_, _, names) => new(names.Select((n, i) => new SpriteCandidate(n, .8 - i * gap)).ToArray(), gap));
        ObserveStable(t, Frame(0, (A, 1, Pic())) with { Money = 4 }); t.SetOperator(A, "甲", Start);
        ObserveStable(t, Frame(1, (A, 0, null), (Bench, 1, null)) with { Money = 4 });
        ObserveStable(t, Frame(3, (Bench, 1, Pic(100, true))) with { Money = 3 });
        Assert.Equal("甲", t.State!.Units[0].Operator);
        Assert.Equal(status, t.State.Units[0].Status);
    }

    [Fact]
    public void MoveNeedsSameTierButNotSimilarPicture()
    {
        var t = Tracker(); ObserveStable(t, Frame(0, (A, 1, Pic()))); t.SetOperator(A, "甲", Start);
        ObserveStable(t, Frame(1, (A, 0, null), (B, 1, Pic(100))));
        Assert.Equal("manual", Assert.Single(t.State!.Units).Source);
        Assert.Equal("甲", t.State.Units[0].Operator);
        ObserveStable(t, Frame(2, (B, 0, null), (C, 2, Pic(90))));
        Assert.NotEqual("carry", Assert.Single(t.State.Units).Source);
    }

    [Theory]
    [InlineData("sure", false)]
    [InlineData("auto", false)]
    [InlineData("pending", true)]
    public void ConservationCarriesStatusCandidatesAndEquipmentAcrossScans(string status, bool delayed)
    {
        int calls = 0;
        var t = Tracker((_, _, names) =>
        {
            calls++;
            return new(names.Select((n, i) => new SpriteCandidate(n, .8 - i * (status == "pending" ? .01 : .1))).ToArray(), 0);
        });
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        t.Observe(Frame(0, (Bench, 1, Pic(0, true))));
        if (status == "sure") t.SetOperator(Bench, "甲", Start);
        t.Observe(Frame(.5, (Bench, 1, Pic(0, true))) with
            { PanelOperator = "甲", Equipment = [new("简易通讯机", false)] });
        var original = Assert.Single(t.State!.Units);
        int initial = calls;
        if (delayed) t.Observe(Frame(1, (Bench, 0, null)), force: true);
        t.Observe(Frame(2, (Bench, 0, null), (A, 1, Pic(100))), force: true);
        var moved = Assert.Single(t.State.Units);
        Assert.Equal(original with { Zone = A.Zone, Col = A.Col, Row = A.Row, Candidates = moved.Candidates }, moved);
        // 强制重认自动身份会重新计算分数，手动身份仍完整承接。
        Assert.Equal(original.Candidates.Select(c => c.Operator), moved.Candidates.Select(c => c.Operator));
        if (status == "sure") Assert.Equal(original.Candidates, moved.Candidates);
        Assert.Equal(initial + (status == "sure" ? 0 : 1), calls);
        Assert.Contains(t.State.Log, e => e.Kind == "move" && e.From == Bench && e.To == A);
        Assert.DoesNotContain(t.State.Log, e => e.Kind == "lost" || e.Kind == "gain" && e.To == A);
    }

    [Fact]
    public void MultipleMovesUseEachOriginOnceByRelativeSimilarity()
    {
        var t = Tracker();
        t.Observe(Frame(0, (A, 1, Pic()), (B, 1, Pic(100))));
        t.SetOperator(A, "甲", Start); t.SetOperator(B, "乙", Start);
        t.Observe(Frame(1, (A, 0, null), (B, 0, null), (C, 1, Pic(100)), (Bench, 1, Pic())), force: true);
        Assert.Equal("乙", t.State!.Units.Single(u => u.Cell == C).Operator);
        Assert.Equal("甲", t.State.Units.Single(u => u.Cell == Bench).Operator);
        Assert.Equal(2, t.State.Log.Count(e => e.Kind == "move"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoneyIncreaseSellsDisappearanceInsteadOfMovingIt(bool delayed)
    {
        var t = Tracker();
        t.Observe(Frame(0, (A, 1, Pic())) with { Money = 4 });
        t.SetOperator(A, "乙", Start);
        if (delayed) t.Observe(Frame(.5, (A, 0, null)) with { Money = 4 }, force: true);
        t.Observe(Frame(1, (A, 0, null), (Bench, 1, Pic(100, true))) with { Money = 5 });
        Assert.Contains(t.State!.Log, e => e.Kind == "sell" && e.Operator == "乙");
        Assert.DoesNotContain(t.State.Log, e => e.Kind == "move");
        Assert.Equal("sprite", Assert.Single(t.State.Units).Source);
    }

    [Fact]
    public void DragNameWithDifferentTierDoesNotBecomeSure()
    {
        var t = Tracker();
        t.Observe(Frame(0, (Bench, 2, Pic(0, true))));
        t.SetOperator(Bench, "丙", Start);
        t.Observe(Frame(1) with { DragOperator = "丙" });
        t.Observe(Frame(2, (Bench, 0, null), (A, 1, Pic(100))));
        var unit = Assert.Single(t.State!.Units);
        Assert.NotEqual("丙", unit.Operator);
        Assert.NotEqual("drag", unit.Source);
        Assert.DoesNotContain(t.State.Log, e => e.Kind == "move");
    }

    [Theory]
    [InlineData(.8, .6, "auto")]
    [InlineData(.8, .78, "pending")]
    [InlineData(.2, .1, "auto")]
    public void ScoreGapSelectsStatus(double first, double second, string expected)
    {
        var t = Tracker((_, _, names) => new(names.Select((n, i) => new SpriteCandidate(n, i == 0 ? first : second)).ToArray(), first - second));
        t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        t.Observe(Frame(0, (A, 1, Pic())));
        Assert.Equal(expected, t.State!.Units[0].Status);
        Assert.Equal(3, t.State.Units[0].Candidates.Length);
    }

    [Fact]
    public void ConfirmationLearnsPictureAndUntrackedGainRemainsPending()
    {
        var t = Tracker((_, _, names) => new(names.Select(n => new SpriteCandidate(n, .5)).ToArray(), 0)); t.AddRoster("甲", "manual", Start);
        t.Observe(Frame(0, (Bench, 1, Pic(10, true))) with { GainSource = "equipment" });
        t.Observe(Frame(2, (Bench, 1, Pic(10, true))));
        Assert.Equal("pending", t.State!.Units[0].Status);
        t.SetOperator(Bench, "乙", Start);
        Assert.Contains(t.State.Roster, r => r.Operator == "乙" && r.Source == "equipment");
        t.Observe(Frame(3, (Bench, 0, null)));
        t.Observe(Frame(4, (Bench, 1, Pic(10, true))));
        Assert.Equal("乙", t.State!.Units[0].Operator);
        t.Observe(Frame(6, (Bench, 1, Pic(10, true))));
        Assert.Equal("sure", t.State!.Units[0].Status);
        Assert.Equal("乙", t.State.Units[0].Operator);
        t.Reset(); Assert.Null(t.State);
        t.Observe(Frame(7)); Assert.Empty(t.State!.Roster);
    }

    [Fact]
    public void PendingSurvivesDelayedGainAndTemporaryMissingBadge()
    {
        int calls = 0;
        var t = Tracker((_, _, names) => { calls++; return new(names.Select(n => new SpriteCandidate(n, .5)).ToArray(), 0); });
        t.Observe(Frame(0, (Bench, 1, Pic(0, true))));
        var candidates = t.State!.Units[0].Candidates;
        t.Observe(Frame(2, (Bench, 1, Pic(0, true))));
        Assert.Same(candidates, t.State.Units[0].Candidates);
        t.Observe(Frame(3, (Bench, 0, null)));
        t.Observe(Frame(4, (Bench, 1, Pic(0, true))));
        Assert.Equal("pending", t.State.Units[0].Status);
        Assert.Same(candidates, t.State.Units[0].Candidates);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, "auto", false)]
    [InlineData(true, "sure", false)]
    [InlineData(false, "auto", true)]
    [InlineData(true, "sure", true)]
    public void MoveFromMissingCellPreservesIdentityAfterBattle(bool confirm, string status, bool bench)
    {
        var t = Tracker(); t.AddRoster("甲", "manual", Start); t.AddRoster("乙", "manual", Start);
        ObserveStable(t, Frame(0, (A, 1, Pic())));
        if (confirm) t.SetOperator(A, "甲", Start);
        ObserveStable(t, Frame(1, (A, 0, null)));
        ObserveStable(t, Frame(2) with { IsRest = false });
        ObserveStable(t, Frame(3, (bench ? Bench : B, 1, Pic(0, bench))) with { Round = 2 });
        var unit = Assert.Single(t.State!.Units);
        Assert.Equal("甲", unit.Operator); Assert.Equal(status, unit.Status); Assert.Equal(confirm ? "manual" : "sprite", unit.Source);
    }

    [Fact]
    public void SeedHonorsGiftRoundAndSellKeepsRoster()
    {
        var t = Tracker();
        t.SeedRoster("赠送", ["甲"], 1, Start); t.Observe(Frame(0, (A, 1, Pic())));
        Assert.DoesNotContain(t.State!.Roster, r => r.Operator == "丙");
        t.SeedRoster("赠送", ["甲"], 2, Start); t.Observe(Frame(.5, (A, 1, Pic())) with { Money = 4, Round = 2 });
        Assert.Contains(t.State.Roster, r => r.Operator == "丙" && r.Source == "strategy");
        t.Observe(Frame(1) with { DragOperator = "甲", Money = 4 });
        t.Observe(Frame(2, (A, 0, null)) with { Money = 5 });
        Assert.True(t.State.Roster.Single(r => r.Operator == "甲").Sold);
    }
    [Fact]
    public void AllAllowedNamesAreCandidatesWithoutExpandingPublicRoster()
    {
        string[] matched = [];
        var t = Tracker((_, _, names) =>
        {
            matched = names;
            return new(names.Select(n => new SpriteCandidate(n, n == "乙" ? .9 : .4)).ToArray(), .5);
        });
        t.AddRoster("甲", "manual", Start);
        t.Observe(Frame(0) with { Shop = new Dictionary<int, string> { [1] = "乙", [2] = "丙" } }, false);
        t.Observe(Frame(1, (A, 1, Pic())), force: true);
        Assert.Equal(new[] { "甲", "乙", "丁" }, matched);
        Assert.Equal("乙", Assert.Single(t.State!.Units).Operator);
        Assert.Equal("甲", Assert.Single(t.State.Roster).Operator);
        t.Reset();
        t.AddRoster("甲", "manual", Start);
        t.Observe(Frame(2, (A, 1, Pic())));
        Assert.Equal("sprite", Assert.Single(t.State!.Units).Source);
    }

}
