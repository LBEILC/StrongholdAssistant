using ASPAssistant.Core.Services;
using ASPAssistant.Core.Models;
using Moq;

namespace StrongholdAssistant.Tests;

public sealed class PithIdentityTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    [Theory]
    [InlineData(3, false, "Pith（优等生）")]
    [InlineData(5, true, "Pith")]
    [InlineData(null, true, "Pith（优等生）")]
    [InlineData(null, false, "Pith")]
    [InlineData(null, null, null)]
    [InlineData(6, true, null)]
    public void TierPrecedesStrategyAndUnknownIdentityIsNotGuessed(int? tier, bool? strategy, string? expected)
        => Assert.Equal(expected, PithIdentity.Resolve(tier, strategy));

    [Theory]
    [InlineData("III", "Pith（优等生）")]
    [InlineData("V", "Pith")]
    public async Task DetailPanelReadsPithTier(string tier, string expected)
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(o => o.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int w, int h) => Task.FromResult<IReadOnlyList<OcrTextResult>>(
                y == 285 ? [new("Pith", (x, y, w, h))] : y == 340 ? [new(tier, (x, y, w, h))] : []));
        using var reader = new FieldReader(Data, ocr.Object, ["Pith", PithIdentity.Gift]);
        var result = await reader.ReadAsync(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "field-55.5.jpg")),
            DateTimeOffset.UnixEpoch, new(1, 0, 1, 0, true), false, false, []);
        Assert.Equal(expected, result.DragOperator);
    }

    [Theory]
    [InlineData("Pith", 5)]
    [InlineData("Pith（优等生）", 3)]
    public void TrackerDoesNotUseOldModelClassForEitherPith(string name, int tier)
    {
        using var model = new SpriteModelMatcher(Path.Combine(Data, "sprite-model.onnx"), Path.Combine(Data, "sprite-model-classes.json"));
        using var image = OpenCvSharp.Cv2.ImDecode(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "field-47.jpg")), OpenCvSharp.ImreadModes.Color);
        var patch = FieldReader.CropSprite(image, 90, 156, false)!;
        var tracker = new FieldTracker();
        tracker.ConfigureIdentity([new() { Name = name, Tier = tier }],
            (_, _, names) => new(names.Select(n => new SpriteCandidate(n, .9)).ToArray(), null), model);
        var cell = new FieldCell("board", 1, 1);
        tracker.Observe(new(DateTimeOffset.UnixEpoch, true, "m01", 1, new Dictionary<FieldCell, int> { [cell] = tier })
            { Sprites = new Dictionary<FieldCell, FieldSpritePatch> { [cell] = patch } }, force: true);
        Assert.Equal(new FieldCandidate(name, .9), Assert.Single(Assert.Single(tracker.State!.Units).Candidates));
    }
}
