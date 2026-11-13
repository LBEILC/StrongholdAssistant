using System.Drawing;
using System.IO.Compression;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

public sealed class BoardDiagnosticsTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SamplesLogOnlyChangesAndThrottleFailedCandidates()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var data = Path.Combine(AppContext.BaseDirectory, "data");
            var api = await HostApi.CreateAsync(data, directory);
            api.InitializeBoardRoutes(data);
            var lines = new List<string>();
            api.BoardLog = message => { lines.Add(message); output.WriteLine("[Board] " + message); };
            var time = DateTimeOffset.UtcNow;
            api.BoardClock = () => time;
            byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", name));
            var sample = Sample("shop-ui100-2000x1125.png");
            for (int i = 0; i < 3; i++) Assert.NotNull(api.RecognizeBoard(sample, null, null));
            Assert.Single(lines);
            var bad = Sample("board-map_intro.bmp");
            Assert.Null(api.RecognizeBoard(bad, null, null));
            Assert.Equal(2, lines.Count);
            Assert.Contains("score=", lines[1]);
            Assert.Contains("gap=", lines[1]);
            Assert.Contains("map=", lines[1]);
            Assert.Contains("reason=score>11", lines[1]);
            api.RecognizeBoard(bad, null, null);
            var failed = new BoardRecognizer(data).RecognizeDetailed(bad);
            api.LogBoardRecognition(failed with { Camera = ASPAssistant.Core.Models.BoardCamera.Expanded });
            Assert.Equal(2, lines.Count);
            time += TimeSpan.FromSeconds(10);
            api.LogBoardRecognition(failed with { Camera = ASPAssistant.Core.Models.BoardCamera.Expanded });
            Assert.Equal(3, lines.Count);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ExportIncludesLatestRecognizedFrameOnlyWhileEnabled()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var data = Path.Combine(AppContext.BaseDirectory, "data");
            var api = await HostApi.CreateAsync(data, directory);
            api.InitializeBoardRoutes(data);
            api.BoardRoutesEnabled = true;
            var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-ui100-2000x1125.png"));
            api.ObserveBoardFrame(frame, new(1, null, null, null, true));
            Assert.Same(frame, api.GetDiagnosticBoardFrame());
            var path = Path.Combine(directory, "diagnostics.zip");
            new DiagnosticsStore(directory).Export(path, api.Settings, api.Connection, new { version = "test" }, api.GetDiagnosticBoardFrame());
            using (var zip = ZipFile.OpenRead(path))
            using (var input = zip.GetEntry("diagnostics/board-latest.png")!.Open())
            using (var image = new Bitmap(input)) Assert.Equal((1920, 1080), (image.Width, image.Height));
            api.BoardRoutesEnabled = false;
            Assert.Null(api.GetDiagnosticBoardFrame());
        }
        finally { Directory.Delete(directory, true); }
    }
}
