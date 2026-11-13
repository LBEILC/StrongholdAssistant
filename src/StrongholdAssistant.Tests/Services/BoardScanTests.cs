using System.Diagnostics;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests.Services;

[Collection("Shop OCR")]
public sealed class BoardScanTests(ShopReaderFixture fixture, EnemyRecognitionFixture recognition, ITestOutputHelper output)
{
    [Fact]
    public async Task ReusesFrameAndPublishesShopBeforeBoardOnlyWhenRequested()
    {
        var reader = new ShopReader(recognition.Engine, fixture.Names) { Theme = GameShopTheme.Closure };
        using var scanner = new OcrScannerService(new ScreenCaptureService(), new GarrisonOcrStrategy(),
            reader, recognition.Engine, new ASPAssistant.Core.GameState.GameState(), () => fixture.Names);
        var recognizer = new BoardRecognizer(Path.Combine(AppContext.BaseDirectory, "data"));
        var order = new List<string>();
        scanner.GameStateUpdated += _ => order.Add("shop");
        scanner.EconomyRead += _ => { };
        scanner.BoardFrameReady += (frame, reading) =>
        {
            order.Add("board");
            Assert.NotNull(frame); Assert.NotNull(reading);
            Assert.True(scanner.BoardShopVisible);
            Assert.True(reading.IsRest); Assert.Equal(1, reading.Round);
            var timer = Stopwatch.StartNew();
            Assert.Equal("m01", recognizer.Recognize(frame)!.MapId);
            output.WriteLine($"棋盘识别耗时 {timer.Elapsed.TotalMilliseconds:F1} ms；同一张扫描截图。");
        };
        var sample = Path.Combine(AppContext.BaseDirectory, "TestData", "shop-closure-round1-1600x1024.png");
        await ShopScanTests.Frame(scanner, sample);
        Assert.DoesNotContain("board", order);
        // 静帧商店事件会去重；保留已经发布的记录，核对棋盘在它之后到达。
        scanner.BoardRoutesRequested = () => true;
        var clock = Stopwatch.StartNew();
        await ShopScanTests.Frame(scanner, sample);
        output.WriteLine($"开启棋盘后的静帧扫描耗时 {clock.Elapsed.TotalMilliseconds:F1} ms。");
        Assert.Equal(new[] { "shop", "board" }, order);
    }
}
