using System.Buffers.Binary;
using System.Reflection;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Moq;

namespace StrongholdAssistant.Tests.Services;

[Collection("Shop OCR")]
public sealed class ShopScanTests(ShopReaderFixture fixture, EnemyRecognitionFixture recognition)
{
    [Theory]
    [InlineData("shop-tier4-gray.png", "shop-tier4-gray.png")]
    [InlineData("shop-tier6-glow-a.png", "shop-tier6-glow-b.png")]
    public async Task ReusesStaticShopAndRereadsChangesAndRefresh(string first, string same)
    {
        int reads = 0;
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] bytes, int x, int y, int w, int h) =>
            {
                if (w == 3840) reads++;
                return recognition.Engine.RecognizeRegionAsync(bytes, x, y, w, h);
            });
        var reader = new ShopReader(ocr.Object, fixture.Names) { Theme = GameShopTheme.Closure };
        var state = new ASPAssistant.Core.GameState.GameState();
        using var scanner = new OcrScannerService(new ScreenCaptureService(), new GarrisonOcrStrategy(),
            reader, ocr.Object, state, () => fixture.Names);
        await Frame(scanner, fixture.Sample(first));
        var original = state.ShopItems.ToArray();
        Assert.NotEmpty(original);
        await Frame(scanner, fixture.Sample(same));
        Assert.Equal(1, reads);
        Assert.Equal(original, state.ShopItems);
        await Frame(scanner, fixture.Sample("shop-tier2-color.png"));
        Assert.Equal(2, reads);
        scanner.RefreshShop();
        await Frame(scanner, fixture.Sample("shop-tier2-color.png"), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(3, reads);
        scanner.InvalidateShop();
        await Frame(scanner, fixture.Sample("shop-tier2-color.png"), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(4, reads);
        reader.Theme = GameShopTheme.Ursus;
        await Frame(scanner, fixture.Sample("shop-tier2-color.png"), DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(5, reads);
    }

    [Fact]
    public void SpeedChangesTimerImmediately()
    {
        using var scanner = new OcrScannerService(new ScreenCaptureService(), new GarrisonOcrStrategy(),
            fixture.Reader, recognition.Engine, new ASPAssistant.Core.GameState.GameState(), () => fixture.Names);
        var timer = (System.Timers.Timer)typeof(OcrScannerService).GetField("_scanTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scanner)!;
        Assert.Equal(800, timer.Interval);
        scanner.SetShopScanSpeed("fast");
        Assert.Equal(250, timer.Interval);
        scanner.SetShopScanSpeed("normal");
        Assert.Equal(800, timer.Interval);
    }

    internal static async Task Frame(OcrScannerService scanner, string path, DateTimeOffset? capturedAt = null)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        int w = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        int h = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        await (Task)typeof(OcrScannerService).GetMethod("ScanFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scanner, [bytes, w, h, capturedAt ?? DateTimeOffset.UtcNow, 0d, null, 0d])!;
    }
}
