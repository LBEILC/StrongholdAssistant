using System.IO.Compression;
using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using OpenCvSharp;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class CollectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BackgroundFramePreservesCapturePixelsWhenSaved()
    {
        using var image = new System.Drawing.Bitmap(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-ui100-2000x1125.png"));
        var capture = new ScreenCaptureService();
        using var original = Cv2.ImDecode(capture.EncodeCapture(image), ImreadModes.Color);
        var frame = capture.EncodeCapture(image, scanPixels: true);
        Assert.Equal((original.Width, original.Height), OcrScannerService.GetFrameDimensions(frame));
        using var diagnostic = Cv2.ImDecode(ScreenCaptureService.AsPng(frame), ImreadModes.Color);
        Assert.Equal(0d, Cv2.Norm(original, diagnostic));
    }

    [Fact]
    public async Task DisabledDoesNotWriteAndEnabledExportsClearsAndKeepsThreeMatches()
    {
        var root = Path.Combine(_directory, "collect");
        var store = new CollectionStore(root);
        var at = DateTimeOffset.UtcNow;
        using var native = new System.Drawing.Bitmap(2400, 1350);
        byte[] raw = [];
        var capture = new ScreenCaptureService { NativeFrame = image =>
        {
            using var stream = new MemoryStream();
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            raw = stream.ToArray();
        } };
        byte[] png = capture.EncodeCapture(native);
        using (var scaled = Cv2.ImDecode(png, ImreadModes.Color)) Assert.Equal(1920, scaled.Width);
        void Add(string id, DateTimeOffset time) => store.Add(id, time.ToString("O"), "测试", time, new { source = "test" }, batch =>
        {
            batch.Log("scan", new { round = 1 });
            batch.Round(1);
            batch.Image(raw, "first-rest", "raw", once: true);
            batch.Observe(png, true, true);
            batch.Observe(png, false, false);
            batch.Answers(["board-1-1", "board-1-1"]);
        });
        Add("off", at);
        await store.DrainAsync();
        Assert.False(Directory.Exists(root));
        store.Enable(true);
        for (int i = 0; i < 4; i++) Add(i.ToString(), at.AddSeconds(i));
        var matches = await store.ListAsync();
        Assert.Equal(new[] { "3", "2", "1" }, matches.Select(m => m.Id));
        Assert.True(matches[0].Ongoing);
        Assert.Equal(1, matches[0].Answers);
        Assert.Equal(1, matches[0].Frames);
        Assert.False(Directory.Exists(Path.Combine(root, "0")));
        string zipPath = Path.Combine(_directory, "export.zip");
        await store.ExportAsync("3", zipPath);
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            Assert.NotNull(zip.GetEntry("说明.txt"));
            using var gzip = new GZipStream(zip.GetEntry("log.jsonl.gz")!.Open(), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            Assert.Contains("scan", reader.ReadToEnd());
            var rawEntry = Assert.Single(zip.Entries, e => e.FullName.StartsWith("raw/"));
            using var buffer = new MemoryStream();
            using (var stream = rawEntry.Open()) stream.CopyTo(buffer);
            using var decoded = Cv2.ImDecode(buffer.ToArray(), ImreadModes.Color);
            Assert.Equal(2400, decoded.Width);
            Assert.Equal(1350, decoded.Height);
        }
        await store.ClearAsync();
        Assert.Empty(await store.ListAsync());
        Add("3", at.AddSeconds(5));
        Assert.Single(await store.ListAsync());
        store.Add("3", at.AddSeconds(5).ToString("O"), "测试", at.AddSeconds(6), new { },
            batch => batch.Observe(png, true, true));
        store.End("3");
        var completed = Assert.Single(await store.ListAsync());
        Assert.False(completed.Ongoing);
        Assert.Equal(2, completed.Frames);
        store.Enable(false);
        await store.DrainAsync();
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.GetLastWriteTimeUtc);
        Add("off", at.AddSeconds(10));
        await store.DrainAsync();
        Assert.Equal(files, Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.GetLastWriteTimeUtc));
    }

    [Fact]
    public async Task SettingsDefaultAndConfirmationErrorsFollowBridgeContract()
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        Assert.False(api.Settings.Collect);
        Assert.Equal("先打开设置里的「帮助改进识别」", Assert.Throws<RecognitionException>(api.ConfirmField).Message);
        api.SaveSettings(api.Settings with { Collect = true });
        Assert.True(new HostSettingsStore(Path.Combine(_directory, "settings.json")).LoadSettings().Collect);
        Assert.Equal("现在不在休整期的棋盘画面，先关掉干员详情等面板、回到棋盘再点", Assert.Throws<RecognitionException>(api.ConfirmField).Message);
        Assert.Empty(Assert.IsType<CollectionMatch[]>(await api.InvokeAsync("collections", [])));
        await api.InvokeAsync("clearCollections", []);
        await api.FinishCollectionsAsync();
        var collection = Path.Combine(_directory, "collect", "example");
        Directory.CreateDirectory(collection);
        File.WriteAllText(Path.Combine(collection, "meta.json"), "collection-private");
        var diagnostic = Path.Combine(_directory, "diagnostic.zip");
        new DiagnosticsStore(_directory).Export(diagnostic, api.Settings, api.Connection, new { version = "test" });
        using var zip = ZipFile.OpenRead(diagnostic);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("collect/"));
        using var settings = JsonDocument.Parse(zip.GetEntry("settings.json")!.Open());
        Assert.True(settings.RootElement.GetProperty("collect").GetBoolean());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
