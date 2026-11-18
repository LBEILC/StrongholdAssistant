using ASPAssistant.Core.Services;
using OpenCvSharp;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class CollectionFrameTests
{
    [Fact]
    public async Task CollectionPreservesSixteenByTenFrame()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var store = new CollectionStore(directory.FullName);
            store.Enable(true);
            var at = DateTimeOffset.UtcNow;
            using var image = new Mat(1600, 2560, MatType.CV_8UC3, Scalar.Black);
            store.Add("match", at.ToString("O"), null, at, new { }, batch => batch.Image(image.ImEncode(".png"), "rest"));
            await store.DrainAsync();
            var path = Assert.Single(Directory.GetFiles(Path.Combine(directory.FullName, "match", "frames")));
            using var stored = Cv2.ImRead(path);
            Assert.Equal(new Size(1920, 1200), stored.Size());
        }
        finally { directory.Delete(true); }
    }

    [Theory]
    [InlineData(1080, 2560, 1600, true)]
    [InlineData(1027, 2558, 1369, false)]
    public void ImportRestoresOnlyDistortedFrames(int height, int rawWidth, int rawHeight, bool distorted)
    {
        using var stored = new Mat(height, 1920, MatType.CV_8UC3, Scalar.Black);
        byte[] jpeg = stored.ImEncode(".jpg");
        var frame = CollectionFrameImage.RestoreAspect(jpeg, rawWidth, rawHeight, out _);
        using var decoded = Cv2.ImDecode(frame, ImreadModes.Color);
        Assert.Equal(distorted ? new Size(rawWidth, rawHeight) : stored.Size(), decoded.Size());
        if (!distorted) Assert.Same(jpeg, frame);
    }
}
