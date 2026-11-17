using System.Drawing;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;
using Moq;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class BoardSurfaceTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.1)]
    [InlineData(100.0 / 90)]
    public async Task ReadyRegionReadsAtSimulatedUiScales(double uiScale)
    {
        // 原图为 UI100；先归一化到 1920，再按右上锚点模拟 UI90～100。
        using var source = new Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-ui100-2000x1125.png"));
        using var image = new Bitmap(1920, 1080);
        double ratio = 1920d / source.Width * uiScale / (100d / 90);
        using (var graphics = Graphics.FromImage(image))
            graphics.DrawImage(source, new RectangleF((float)(1920 - source.Width * ratio), 0,
                (float)(source.Width * ratio), (float)(source.Height * ratio)));
        using var encoded = new MemoryStream();
        image.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
        var reader = new BoardSurfaceReader(fixture.Engine);
        bool main = await reader.IsMainScreenAsync(encoded.ToArray(), 1920, 1080);
        output.WriteLine($"UI scale={uiScale:F3} main={main} OCR={reader.RawText}");
        Assert.True(main);
    }

    [Fact]
    public async Task EnemyPanelClearsRoutesAndClosingItRestoresSameRound()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var data = Path.Combine(AppContext.BaseDirectory, "data");
            var api = await HostApi.CreateAsync(data, directory);
            api.InitializeBoardRoutes(data);
            api.BoardRoutesEnabled = true;
            var reader = new BoardSurfaceReader(fixture.Engine);
            var events = new List<BoardRoutes?>();
            api.BoardRoutesChanged += events.Add;
            var logs = new List<string>();
            api.BoardLog = logs.Add;
            var rest = new EconomyReading(1, null, null, null, true);
            int calls = 0;
            var recognize = api.RecognizeBoard;
            api.RecognizeBoard = (f, m, c) => { calls++; return recognize(f, m, c); };
            foreach (var name in new[] { "shop-ui100-2000x1125.png", "board-enemy-panel.png", "board-enemy-panel.png", "shop-ui100-2000x1125.png" })
            {
                var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));
                using var stream = new MemoryStream(frame);
                using var image = new Bitmap(stream);
                bool main = await reader.IsMainScreenAsync(frame, image.Width, image.Height);
                Assert.Equal(!name.Contains("enemy"), main);
                api.ObserveBoardFrame(frame, rest, true, main);
            }
            Assert.Equal(3, events.Count);
            Assert.NotNull(events[0]);
            Assert.Null(events[1]);
            Assert.Equal(events[0], events[2]);
            Assert.Equal(1, calls);
            Assert.Equal(3, logs.Count(l => l.StartsWith("休整期主画面=")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("准备就绪")]
    [InlineData("取消就绪")]
    public async Task BothReadyStatesAllowRoutes(string label)
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new OcrTextResult[] { new(label, (1700, 140, 180, 40)) });
        Assert.True(await new BoardSurfaceReader(ocr.Object).IsMainScreenAsync([], 1920, 1080));
    }
}
