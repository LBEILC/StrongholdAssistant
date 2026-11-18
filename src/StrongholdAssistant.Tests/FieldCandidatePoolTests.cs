using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Tests;

public sealed class FieldCandidatePoolTests
{
    private static readonly FieldCell Cell = new("board", 1, 1);
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BanAndKnownCustomSelectionFilterEvenLowScoresWithoutChangingRoster(bool knownCustom)
    {
        var tracker = new FieldTracker();
        string[] matched = [];
        tracker.ConfigureIdentity([
            new() { Name = "普通", Tier = 1 }, new() { Name = "禁用", Tier = 1 },
            new() { Name = "自选甲", Tier = 1, Diy = true }, new() { Name = "自选乙", Tier = 1, Diy = true },
            new() { Name = "赠送", Tier = 1, Obtain = "策略「赠送策略」第 1 回合赠送" }],
            (_, _, names) => { matched = names; return new(names.Select(n => new SpriteCandidate(n, .1)).ToArray(), 0); });
        tracker.ConfigureCandidatePool(["禁用"], knownCustom ? ["自选甲"] : null);
        tracker.SeedRoster("赠送策略", [], 1, At);
        tracker.Observe(Frame() with { Shop = new Dictionary<int, string> { [0] = "禁用", [1] = "自选乙" } }, force: true);
        Assert.Contains("普通", matched);
        Assert.Contains("自选甲", matched);
        Assert.Equal(!knownCustom, matched.Contains("自选乙"));
        Assert.Contains("赠送", matched);
        Assert.DoesNotContain("禁用", matched);
        Assert.Equal("赠送", Assert.Single(tracker.State!.Roster).Operator);
        Assert.Equal("pending", Assert.Single(tracker.State.Units).Status);
    }

    [Fact]
    public void OnlyCandidateUsesFilteredPoolAndResetClearsMatchRestrictions()
    {
        var tracker = new FieldTracker();
        int calls = 0;
        tracker.ConfigureIdentity([new() { Name = "甲", Tier = 1 }, new() { Name = "乙", Tier = 1 }],
            (_, _, names) => { calls++; return new(names.Select(n => new SpriteCandidate(n, .5)).ToArray(), 0); });
        tracker.ConfigureCandidatePool(["乙"], []);
        tracker.Observe(Frame(), force: true);
        Assert.Equal("甲", Assert.Single(tracker.State!.Units).Operator);
        Assert.Equal("sprite", tracker.State.Units[0].Source);
        Assert.Equal(1, calls);
        Assert.Equal("pending", tracker.State.Units[0].Status);
        Assert.Empty(tracker.State.Roster);
        tracker.Reset();
        tracker.Observe(Frame(), force: true);
        Assert.Equal(2, tracker.State!.Units[0].Candidates.Length);
        Assert.Equal("sprite", tracker.State.Units[0].Source);
    }

    private static FieldObservation Frame()
    {
        using var image = new OpenCvSharp.Mat(204, 180, OpenCvSharp.MatType.CV_8UC3, OpenCvSharp.Scalar.All(100));
        return new(At, true, "m01", 1, new Dictionary<FieldCell, int> { [Cell] = 1 })
        { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [Cell] = FieldReader.CropSprite(image, 90, 156, false)! } };
    }
}
