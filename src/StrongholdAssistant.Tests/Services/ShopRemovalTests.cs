using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Moq;

namespace StrongholdAssistant.Tests.Services;

[Collection("Shop OCR")]
public sealed class ShopRemovalTests(ShopReaderFixture fixture, EnemyRecognitionFixture recognition,
    Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task ChangedSlotRemovedBeforeOcrAndStaticMissKeepsMarkers()
    {
        bool miss = false;
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] bytes, int x, int y, int w, int h) => miss
                ? Task.FromResult<IReadOnlyList<OcrTextResult>>([])
                : recognition.Engine.RecognizeRegionAsync(bytes, x, y, w, h));
        var reader = new ShopReader(ocr.Object, fixture.Names) { Theme = GameShopTheme.Closure };
        var state = new ASPAssistant.Core.GameState.GameState();
        using var scanner = new OcrScannerService(new ScreenCaptureService(), new GarrisonOcrStrategy(),
            reader, ocr.Object, state, () => fixture.Names);
        var source = fixture.Sample("shop-tier4-gray.png");
        await ShopScanTests.Frame(scanner, source);
        var original = state.ShopItems.ToArray();
        Assert.NotEmpty(original);
        miss = true;
        scanner.InvalidateShop();
        await ShopScanTests.Frame(scanner, source);
        Assert.Equal(original, state.ShopItems);
        miss = false;
        scanner.InvalidateShop();
        await ShopScanTests.Frame(scanner, source);

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            var removed = original[0];
            using (var bitmap = new Bitmap(source))
            {
                var r = removed.CardRegion;
                using var graphics = Graphics.FromImage(bitmap);
                graphics.FillRectangle(Brushes.Black, (float)(r.X * bitmap.Width), (float)(r.Y * bitmap.Height),
                    (float)(r.Width * bitmap.Width), (float)(r.Height * bitmap.Height));
                bitmap.Save(path, ImageFormat.Png);
            }
            double? removedMs = null;
            var clock = Stopwatch.StartNew();
            scanner.GameStateUpdated += value =>
            {
                if (scanner.RemovingChangedShopItems)
                {
                    Assert.DoesNotContain(value.ShopItems, item => item.Id == removed.Id);
                    Assert.Equal(original.Length - 1, value.ShopItems.Count);
                    removedMs = clock.Elapsed.TotalMilliseconds;
                }
            };
            await ShopScanTests.Frame(scanner, path);
            Assert.NotNull(removedMs);
            Assert.DoesNotContain(state.ShopItems, item => item.Id == removed.Id);

            // 对照旧流程：同一张改图、真实 OCR，普通档两次读数相隔 800 ms。
            var old = new ShopStateStabilizer();
            old.TryUpdate(original, out _);
            var bytes = File.ReadAllBytes(path);
            using var image = new Bitmap(path);
            var baseline = Stopwatch.StartNew();
            var detected = await reader.ReadAsync(bytes, image.Width, image.Height, fixture.Names);
            old.TryUpdate(detected, out var first);
            Assert.Contains(first, item => item.Id == removed.Id);
            await Task.Delay(800);
            old.TryUpdate(detected, out var second);
            Assert.DoesNotContain(second, item => item.Id == removed.Id);
            output.WriteLine($"同样图，截图已取得→移除结果：旧流程 {baseline.Elapsed.TotalMilliseconds:F1} ms（800 ms 扫描间隔）；新流程 {removedMs:F1} ms（OCR 前）。不含桌面渲染。" );
        }
        finally { File.Delete(path); }
    }
}
