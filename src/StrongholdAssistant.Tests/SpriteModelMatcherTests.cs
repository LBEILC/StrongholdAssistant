using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using OpenCvSharp;

namespace StrongholdAssistant.Tests;

public sealed class SpriteModelMatcherTests
{
    private static string Data(string file) => Path.Combine(AppContext.BaseDirectory, "data", file);
    private static SpriteModelMatcher Model() => new(Data("sprite-model.onnx"), Data("sprite-model-classes.json"));
    private static Mat Crop()
    {
        using var original = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "TestData", "sprite-N30.png"));
        var crop = new Mat();
        Cv2.Resize(original, crop, new Size(180, 204));
        return crop;
    }

    [Fact]
    public void TemplateCandidatesRecallLookalikesWithinEligiblePool()
    {
        using var model = Model();
        var classes = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Data("sprite-model-classes.json")))!;
        var logits = new float[classes.Length];
        logits[Array.IndexOf(classes, "风絮")] = 5;
        string[] eligible = ["风絮", "水灯心", "青枳", "车尔尼", "凯瑟琳", "罗比菈塔"];
        Assert.Equal(eligible, model.TemplateCandidates(logits, eligible));
        Assert.DoesNotContain("凯瑟琳", model.TemplateCandidates(logits, eligible.Where(n => n != "凯瑟琳").ToArray()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ModerateGapAcceptsTemplateOnlyOrStrongModelEvidence(bool templateOnly)
    {
        using var model = Model();
        using var crop = Crop();
        var patch = FieldReader.CropSprite(crop, 90, 156, false)!;
        using var image = Cv2.ImDecode(patch.Image, ImreadModes.Color);
        var classes = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Data("sprite-model-classes.json")))!;
        var byModel = model.Shortlist(model.Predict(image), classes, classes.Length);
        var first = templateOnly ? PithIdentity.Gift : byModel[0];
        var second = byModel[^1];
        var tracker = new FieldTracker();
        tracker.ConfigureIdentity([new() { Name = first, Tier = 2 }, new() { Name = second, Tier = 2 }],
            (_, _, _) => new([new(first, templateOnly ? .57 : .50), new(second, templateOnly ? .50 : .54)], null), model);
        var cell = new FieldCell("board", 1, 1);
        tracker.Observe(new(DateTimeOffset.UnixEpoch, true, "m01", 1, new Dictionary<FieldCell, int> { [cell] = 2 })
            { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [cell] = patch } }, force: true);
        var unit = Assert.Single(tracker.State!.Units);
        Assert.Equal(first, unit.Operator);
        Assert.Equal("auto", unit.Status);
        Assert.InRange(unit.Candidates[0].Score - unit.Candidates[1].Score, SpriteMatcher.AutoFillGap, SpriteModelMatcher.AutoFillGap);
    }

    [Fact]
    public void ShortlistKeepsConfirmedPersonalTemplateOutsideTopFourAndReusesPrediction()
    {
        using var model = Model(); using var crop = Crop();
        var logits = model.Predict(crop);
        var classes = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Data("sprite-model-classes.json")))!;
        var names = model.Shortlist(logits, classes, 6);
        string[] matched = [];
        var tracker = new FieldTracker();
        tracker.ConfigureIdentity(names.Select(n => new Operator { Name = n, Tier = 2 }).ToArray(),
            (_, _, pool) => { matched = pool; return new(pool.Select(n => new SpriteCandidate(n, .5)).ToArray(), 0); }, model);
        var patch = FieldReader.CropSprite(crop, 90, 156, false)!;
        var cell = new FieldCell("board", 1, 1); var at = DateTimeOffset.UnixEpoch;
        tracker.Observe(new(at, true, "m01", 1, new Dictionary<FieldCell, int> { [cell] = 2 })
            { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [cell] = patch } });
        tracker.SetOperator(cell, names[5], at);
        var target = new FieldCell("board", 2, 1);
        tracker.Observe(new(DateTimeOffset.UnixEpoch.AddSeconds(1), true, "m01", 1, new Dictionary<FieldCell, int> { [target] = 2 })
            { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [target] = patch } }, force: true);
        Assert.Contains(names[5], matched); Assert.InRange(matched.Length, 4, 5);
        var templates = names.Select(n => new SpriteCandidate(n, .5)).ToArray();
        Assert.Equal(model.Fuse(crop, templates), model.Fuse(logits, templates));
    }

    [Theory]
    [InlineData(.1999)]
    [InlineData(.2)]
    [InlineData(.4)]
    public void OccludedCellsKeepAllCandidatesOnlyAtTwentyPercent(double ratio)
    {
        using var model = Model(); using var crop = Crop();
        var classes = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Data("sprite-model-classes.json")))!;
        var names = model.Shortlist(model.Predict(crop), classes, 6);
        string[] matched = [];
        var tracker = new FieldTracker();
        tracker.ConfigureIdentity(names.Select(n => new Operator { Name = n, Tier = 2 }).ToArray(),
            (_, _, pool) => { matched = pool; return new(pool.Select(n => new SpriteCandidate(n, .5)).ToArray(), 0); }, model);
        var patch = FieldReader.CropSprite(crop, 90, 156, false)! with { OcclusionRatio = ratio };
        var target = new FieldCell("board", 2, 1);
        tracker.Observe(new(DateTimeOffset.UnixEpoch.AddSeconds(1), true, "m01", 1, new Dictionary<FieldCell, int> { [target] = 2 })
            { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [target] = patch } }, force: true);
        using var actualCrop = Cv2.ImDecode(patch.Image, ImreadModes.Color);
        Assert.Equal(ratio >= .2 ? names : model.Shortlist(model.Predict(actualCrop), names, 4), matched);
    }

    [Fact]
    public void DnnMatchesOnnxRuntimeWithTrainingPreprocessing()
    {
        using var model = Model();
        using var crop = Crop();
        var logits = model.Predict(crop);
        Assert.Equal(217, logits.Length);
        // 由相同模型的 ONNX Runtime CPU 推理保存，覆盖通道、尺寸及归一化。
        Assert.InRange(Math.Abs(logits[25] + 1.0640063285827637), 0, 1e-4);
        Assert.InRange(Math.Abs(logits[93] - 0.5580694675445557), 0, 1e-4);
        Assert.InRange(Math.Abs(logits[126] + 4.483623504638672), 0, 1e-4);
    }

    [Fact]
    public void TrackerUsesFusedGapAndKeepsPersonalTemplate()
    {
        using var model = Model();
        using var crop = Crop();
        var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Data("sprite-model-classes.json")))!;
        string first = names[25], second = names[93];
        var tracker = new FieldTracker();
        tracker.ConfigureIdentity([new() { Name = first, Tier = 2 }, new() { Name = second, Tier = 2 }],
            // 第三轮模型对本图更偏向second；模板仍让first领先，融合分差落在两个阈值之间。
            (_, _, pool) => new(pool.Select(n => new SpriteCandidate(n, n == first ? .76 : .6)).ToArray(), .16), model);
        var at = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var cell = new FieldCell("board", 1, 1);
        var patch = FieldReader.CropSprite(crop, 90, 156, false)!;
        var observation = new FieldObservation(at, true, "m01", 1, new Dictionary<FieldCell, int> { [cell] = 2 })
            { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [cell] = patch } };
        tracker.Observe(observation, force: true);
        var unit = Assert.Single(tracker.State!.Units);
        Assert.Equal(first, unit.Operator);
        Assert.Equal("pending", unit.Status);
        double gap = unit.Candidates[0].Score - unit.Candidates[1].Score;
        Assert.True(gap > SpriteMatcher.AutoFillGap && gap < SpriteModelMatcher.AutoFillGap);

        tracker.SetOperator(cell, second, at);
        var target = new FieldCell("board", 2, 1);
        tracker.Observe(new(DateTimeOffset.UnixEpoch.AddSeconds(1), true, "m01", 1, new Dictionary<FieldCell, int> { [target] = 2 })
            { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [target] = patch } }, force: true);
        var learned = tracker.State!.Units.Single(u => u.Cell == target).Candidates;
        Assert.Equal(second, learned[0].Operator);
        Assert.True(learned[0].Score > .9);
    }
}
