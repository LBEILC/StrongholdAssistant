using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Moq;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class FieldRestTimingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static byte[] Frame()
    {
        using var bitmap = new System.Drawing.Bitmap(1920, 1080);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    [Fact]
    public async Task ConfirmationKeepsManualFrameAcrossScansButExpiresAtBattleOrNewRound()
    {
        var api = await HostApi.CreateAsync(Data, _directory);
        api.SaveSettings(api.Settings with { Collect = true });
        typeof(HostApi).GetMethod("BeginHistory", Private)!.Invoke(api, null);
        var at = DateTimeOffset.UtcNow;
        var frame = Frame();
        var state = new FieldState("m01", 9, at.ToString("O"), [new("board", 1, 3, 4, "黑键", "sprite")], []);
        void Recognize(int round)
        {
            api.ObserveEconomy(new(round, null, null, null, true));
            var observation = new FieldObservation(at, true, "m01", round, new Dictionary<FieldCell, int>());
            typeof(HostApi).GetMethod("CollectObservation", Private)!.Invoke(api, [frame, observation, null, state, Array.Empty<object>(), true, 0d, null]);
        }
        Recognize(9);
        api.ObserveCollectionFrame(Frame(), at.AddSeconds(3));
        api.ObserveEconomy(new(null, null, null, null, true));
        await api.CompleteCollectionScan(at.AddSeconds(3));
        api.ConfirmField();
        await api.DrainCollectionsAsync();
        string log = Directory.GetFiles(Path.Combine(_directory, "collect"), "log.jsonl.gz", SearchOption.AllDirectories).Single();
        using (var stream = new GZipStream(File.OpenRead(log), CompressionMode.Decompress))
        using (var reader = new StreamReader(stream))
        {
            var rows = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonDocument.Parse(s)).ToArray();
            var answer = rows.Single(r => r.RootElement.GetProperty("kind").GetString() == "confirmField").RootElement.GetProperty("data");
            Assert.Equal(at, answer.GetProperty("frameAt").GetDateTimeOffset());
            Assert.Equal("黑键", answer.GetProperty("state").GetProperty("units")[0].GetProperty("operator").GetString());
            foreach (var row in rows) row.Dispose();
        }
        api.ObserveEconomy(new(9, null, null, null, false));
        Assert.Throws<RecognitionException>(api.ConfirmField);
        Recognize(9);
        api.ObserveEconomy(new(10, null, null, null, true));
        Assert.Throws<RecognitionException>(api.ConfirmField);
        await api.FinishCollectionsAsync();
    }

    [Fact]
    public async Task EveryManualSuccessPublishesPlacementAndMissingRoundStaysWithinRestPeriod()
    {
        var api = await HostApi.CreateAsync(Data, _directory);
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(o => o.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<OcrTextResult>());
        api.CreateShopReader(ocr.Object);
        api.InitializeField(Data, ocr.Object);
        var pushes = new List<object?>();
        api.Changed += (kind, value) => { if (kind == "placement") pushes.Add(value); };
        var frame = Frame();
        var at = DateTimeOffset.UtcNow;
        api.ObserveEconomy(new(9, null, null, null, true));
        var reading = new EconomyReading(null, null, null, null, true);
        var first = await api.RecognizeFieldFrameAsync(frame, at, reading, true, false, []);
        var second = await api.RecognizeFieldFrameAsync(frame, at, reading, true, false, []);
        Assert.Equal(9, first.Round);
        Assert.Equal(9, second.Round);
        Assert.Equal(2, pushes.Count);
        Assert.All(pushes, Assert.Null);
        api.ObserveEconomy(new(9, null, null, null, false));
        var next = await api.RecognizeFieldFrameAsync(frame, at.AddSeconds(2), reading, true, false, []);
        Assert.Null(next.Round);
        Assert.Equal(3, pushes.Count);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
