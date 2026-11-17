using System.IO.Compression;
using System.Reflection;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using Moq;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class RestBoardRecognitionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManualRestRejectionExportsExactFrameAndReadingRegardlessOfRoutes(bool routes)
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            api.BoardRoutesEnabled = routes;
            var ocr = new Mock<IOcrEngine>();
            ocr.Setup(o => o.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((byte[] _, int x, int y, int w, int h) => y == 0
                    ? new OcrTextResult[] { new("回合 3", (650, 10, 90, 30)) }
                    : new OcrTextResult[] { new("S.W.E.E.P.报告", (1700, 140, 180, 40)) });
            using var scanner = new OcrScannerService(new ScreenCaptureService(), new GarrisonOcrStrategy(),
                new ShopReader(ocr.Object, []), ocr.Object, new ASPAssistant.Core.GameState.GameState(), () => []);
            var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-ui100-2000x1125.png"));
            string[]? details = null;
            scanner.RecognitionFailed += (shot, reason, scale, steps) =>
            {
                Assert.Same(frame, shot);
                details = steps;
                api.ReportRecognitionFailure(shot, reason, scale, steps);
            };
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ReadFieldFrameAsync(frame,
                DateTimeOffset.UtcNow, (_, _, _, _, _, _) => throw new Exception("拒识后不应继续认人")));
            Assert.Contains("现在不在休整期", error.Message);
            string detail = Assert.Single(details!);
            Assert.Contains("IsRest=False main=False round=3", detail);
            Assert.Contains("roundOCR=回合 3", detail);
            Assert.Contains("readyOCR=S.W.E.E.P.报告", detail);
            string path = Path.Combine(directory, "diagnostics.zip");
            new DiagnosticsStore(directory).Export(path, api.Settings, api.Connection, new { version = "test" });
            using var zip = ZipFile.OpenRead(path);
            Assert.Single(zip.Entries, e => e.FullName.EndsWith(".png"));
            var metadata = Assert.Single(zip.Entries, e => e.FullName.StartsWith("diagnostics/") && e.FullName.EndsWith(".json"));
            using var reader = new StreamReader(metadata.Open());
            Assert.Contains("readyOCR", await reader.ReadToEndAsync());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoutesDisabledStillRecognizesFieldAndComputesPlacement(bool automatic)
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            string data = Path.Combine(AppContext.BaseDirectory, "data");
            var api = await HostApi.CreateAsync(data, directory);
            Assert.False(api.BoardRoutesEnabled);
            // 产品设置仍固定关闭自动认人；只在测试内打开保留的自动路径。
            typeof(HostApi).GetProperty("Settings")!.SetValue(api, api.Settings with { FieldAuto = automatic });
            var ocr = new Mock<IOcrEngine>();
            ocr.Setup(o => o.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(Array.Empty<OcrTextResult>());
            api.CreateShopReader(ocr.Object);
            api.InitializeField(data, ocr.Object);
            api.InitializeBoardRoutes(data);
            var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "field-57.jpg"));
            var rest = new EconomyReading(1, 0, 1, 0, true);
            api.ObserveEconomy(rest);
            api.ObserveEconomy(rest);
            int boards = 0;
            var recognize = api.RecognizeBoard;
            api.RecognizeBoard = (f, m, c) => { boards++; return recognize(f, m, c); };
            api.BoardRoutesChanged += routes => Assert.Null(routes);
            api.ObserveBoardFrame(frame, rest, true, true);
            Assert.Equal(1, boards);
            if (automatic) await api.ObserveFieldAsync(frame, DateTimeOffset.UtcNow, rest, true, true, []);
            else await api.RecognizeFieldFrameAsync(frame, DateTimeOffset.UtcNow, rest, true, true, []);
            Assert.NotNull(api.Field?.MapId);
            Assert.Contains(api.Field!.Units, unit => unit.Zone == "board");
            var work = (Task)typeof(HostApi).GetField("_placementWork", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(api)!;
            await work.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(api.Placement);
            Assert.Equal(api.Field.UpdatedAt, api.Placement!.BasedOn);
            Assert.Same(frame, api.GetDiagnosticBoardFrame());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
