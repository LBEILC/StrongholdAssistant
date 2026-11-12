using System.Reflection;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Moq;

namespace StrongholdAssistant.Tests.Services;

[Collection("Native OCR")]
public class ShopFailureTests(EnemyRecognitionFixture fixture)
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "商店卡片的名字没读出来。")]
    public async Task OnlyOpenShopWithUnreadableNamesReportsFailure(bool hasControl, string? firstLine)
    {
        var ocr = new Mock<IOcrEngine>();
        var unreadable = new string('错', 300);
        List<OcrTextResult> texts = [new(unreadable, (740, 390, 220, 40))];
        if (hasControl) texts.Add(new("收起", (3500, 370, 160, 60)));
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(texts);
        var reader = new ShopReader(ocr.Object, ["山"]) { Theme = GameShopTheme.Closure };
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-tier4-gray.png"));
        var failures = await ScanShopAsync(reader, ocr.Object, bytes);
        Assert.True(reader.HasVisibleCards);
        if (!hasControl)
        {
            Assert.Null(reader.Failure);
            Assert.Empty(failures);
            return;
        }
        Assert.Equal(reader.Failure, Assert.Single(failures));
        var lines = reader.Failure!.Split('\n');
        Assert.Equal(firstLine, lines[0]);
        Assert.Contains("OCR：" + unreadable[..240], lines[1]);
        Assert.DoesNotContain(unreadable, lines[1]);
    }

    [Fact]
    public async Task PrepReadySampleDoesNotReportFailure()
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "prep-ready-1920x1080.png"));
        var reader = new ShopReader(fixture.Engine, ["山"]);
        Assert.Empty(await ScanShopAsync(reader, fixture.Engine, bytes));
        Assert.Null(reader.Failure);
    }

    private static async Task<List<string>> ScanShopAsync(ShopReader reader, IOcrEngine engine, byte[] bytes)
    {
        var state = new ASPAssistant.Core.GameState.GameState();
        using var scanner = new OcrScannerService(new ScreenCaptureService(), new GarrisonOcrStrategy(),
            reader, engine, state, () => ["山"]);
        var failures = new List<string>();
        scanner.RecognitionFailed += (_, failure, _, _) => failures.Add(failure);
        // 直接运行计时器使用的商店步骤，不启动计时器或真实截屏。
        await (Task)typeof(OcrScannerService).GetMethod("ScanShopAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scanner, [bytes, 1920, 1080, DateTimeOffset.UtcNow])!;
        Assert.Empty(state.ShopItems);
        return failures;
    }
}
