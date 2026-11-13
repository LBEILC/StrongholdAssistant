using System.Diagnostics;
using System.Drawing;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Xunit.Abstractions;
using Moq;

namespace StrongholdAssistant.Tests.Services;

[Collection("Shop OCR")]
public sealed class GameThemeTests(ShopReaderFixture fixture, EnemyRecognitionFixture recognition, ITestOutputHelper output)
{
    [Theory]
    [InlineData("shop-terminal-ui100-2000x1125.png", GameShopTheme.Terminal)]
    [InlineData("shop-ursus-ui100-2000x1125.png", GameShopTheme.Ursus)]
    [InlineData("shop-ui100-2000x1125.png", GameShopTheme.Closure)]
    [InlineData("shop-closure-round1-1600x1024.png", GameShopTheme.Closure)]
    public async Task ReadsGameplayTheme(string file, GameShopTheme expected)
    {
        var png = File.ReadAllBytes(fixture.Sample(file));
        using var bitmap = new Bitmap(fixture.Sample(file));
        var clock = Stopwatch.StartNew();
        var result = await new GameThemeDetector(recognition.Engine).ReadAsync(png, bitmap.Width, bitmap.Height);
        output.WriteLine($"{file}: {result}, {clock.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(expected, result?.Theme);
    }

    [Theory]
    [InlineData("info-ingame-1920x1080.png")]
    [InlineData("board-map_intro.bmp")]
    public async Task NonGameplayDoesNotChangeTheme(string file)
    {
        var tracker = new GameThemeTracker();
        for (int i = 0; i < 3; i++) tracker.Observe(new(GameShopTheme.Terminal, .1));
        using var bitmap = new Bitmap(fixture.Sample(file));
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        var reading = await new GameThemeDetector(recognition.Engine).ReadAsync(stream.ToArray(), bitmap.Width, bitmap.Height);
        Assert.Null(reading);
        for (int i = 0; i < 3; i++) Assert.False(tracker.Observe(reading));
        Assert.Equal(GameShopTheme.Terminal, tracker.Current);
    }

    [Fact]
    public async Task MenuAndSettlementTextCannotConfirmThemeEvenWithThemeColors()
    {
        var png = File.ReadAllBytes(fixture.Sample("shop-terminal-ui100-2000x1125.png"));
        var ocr = new Mock<IOcrEngine>();
        var tracker = new GameThemeTracker();
        for (int i = 0; i < 3; i++) tracker.Observe(new(GameShopTheme.Closure, .1));
        foreach (string heading in new[] { "卫戍协议 开始行动", "回合8 战斗结算" })
        {
            ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new OcrTextResult[] { new(heading, (0, 0, 100, 30)) });
            var reading = await new GameThemeDetector(ocr.Object).ReadAsync(png, 2000, 1125);
            Assert.Null(reading);
            for (int i = 0; i < 3; i++) Assert.False(tracker.Observe(reading));
            Assert.Equal(GameShopTheme.Closure, tracker.Current);
        }
    }

    [Fact]
    public void RequiresThreeConsecutiveFramesAndOnlyPublishesChanges()
    {
        var tracker = new GameThemeTracker();
        var terminal = new GameThemeReading(GameShopTheme.Terminal, .1);
        var closure = new GameThemeReading(GameShopTheme.Closure, .1);
        Assert.False(tracker.Observe(terminal));
        Assert.False(tracker.Observe(terminal));
        Assert.True(tracker.Observe(terminal));
        Assert.False(tracker.Observe(terminal));
        for (int i = 0; i < 4; i++)
        {
            Assert.False(tracker.Observe(closure));
            Assert.False(tracker.Observe(terminal));
        }
        Assert.False(tracker.Observe(closure));
        Assert.False(tracker.Observe(null));
        Assert.False(tracker.Observe(closure));
        Assert.False(tracker.Observe(closure));
        Assert.True(tracker.Observe(closure));
        Assert.False(tracker.Observe(closure));
    }
}
