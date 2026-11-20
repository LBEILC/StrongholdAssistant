using System.Drawing;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Control;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using OpenCvSharp;
using StrongholdAssistant.Host;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public class TurnStateTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    private static async Task<JsonElement> Turn(HostApi api, string query = "")
    {
        var response = await api.RouteLocalAsync("GET", new Uri("http://localhost/v1/turn" + query),
            [], default, (_, _, _) => throw new InvalidOperationException());
        return JsonSerializer.SerializeToElement(response, HostApi.JsonOptions);
    }

    private async Task Scan(HostApi api, byte[] frame, int repeat = 1)
    {
        var reader = api.CreateShopReader(fixture.Engine);
        reader.ReadUntracked = true;
        using var scanner = new OcrScannerService(new(), new GarrisonOcrStrategy(), reader, fixture.Engine,
            new ASPAssistant.Core.GameState.GameState(), () => [])
        {
            ObserveFieldAsync = api.ObserveFieldAsync, ObserveOtherTurn = api.ObserveOtherTurn,
            ObserveAdaptFrameAsync = api.ObserveAdaptFrameAsync, ObserveMatchFrameAsync = api.ObserveMatchFrameAsync,
            HudObscured = () => api.CoverMatchHud()
        };
        using var bitmap = new Bitmap(new MemoryStream(frame));
        var scan = typeof(OcrScannerService).GetMethod("ScanFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (int i = 0; i < repeat; i++)
            await (Task)scan.Invoke(scanner, [frame, bitmap.Width, bitmap.Height, DateTimeOffset.UtcNow, 0d, null, 0d])!;
    }

    [Fact]
    public async Task CoveredHudReadsOnlyThisFramesMoney()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Data, dir.FullName);
            api.SaveSettings(api.Settings with { GameTheme = "closure" });
            api.InitializeMatchReading(Data, fixture.Engine);
            var spent = new EconomyReading(2, 0, 1, 4, true);
            api.ObserveEconomy(spent);
            api.ObserveEconomy(spent);
            Assert.Equal(0, api.GetMatchState().Money.Value);
            string folder = Path.Combine(AppContext.BaseDirectory, "TestData", "live398");
            await Scan(api, File.ReadAllBytes(Path.Combine(folder, "03.jpg")));
            var adapt = await Turn(api);
            Assert.Equal("adapt", adapt.GetProperty("phase").GetString());
            Assert.Equal(6, api.GetMatchState().Money.Value);
            Assert.Equal(3, api.GetMatchState().Round.Value);
            Assert.Null(api.GetMatchState().Hp.Value);
            Assert.Null(api.GetMatchState().RefreshCost.Value);
            await Scan(api, File.ReadAllBytes(Path.Combine(folder, "32.jpg")));
            var confirm = api.GetMatchState();
            Assert.All(new[] { confirm.Round, confirm.Hp, confirm.Money, confirm.RefreshCost }, fact => Assert.Equal("unknown", fact.Source));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task FirstFieldWaitDistinguishesTimeoutAndObservedEmptyBoard()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Data, dir.FullName);
            api.SaveSettings(api.Settings with { GameTheme = "closure" });
            api.InitializeMatchReading(Data, fixture.Engine);
            api.CreateShopReader(fixture.Engine);
            api.InitializeField(Data, fixture.Engine);
            api.InitializeBoardRoutes(Data);
            var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "live398", "01.jpg"));
            await Scan(api, frame, 2);
            Assert.Null(api.Field);
            Task<object?> Field() => api.RouteLocalAsync("GET", new Uri("http://localhost/v1/field"), [],
                default, (_, _, _) => throw new InvalidOperationException());
            var expired = Field();
            var expiredTurn = Turn(api);
            Assert.Equal("unknown", JsonSerializer.SerializeToElement(await expired).GetProperty("source").GetString());
            Assert.Contains("超时", (await expiredTurn).GetProperty("fieldReason").GetString());
            var pending = Field();
            var turn = Turn(api);
            Assert.False(pending.IsCompleted);
            Assert.False(turn.IsCompleted);
            await Scan(api, frame);
            Assert.Empty(Assert.IsType<FieldState>(await pending).Units);
            var units = (await turn).GetProperty("state").GetProperty("units");
            Assert.Equal("read", units.GetProperty("source").GetString());
            Assert.Empty(units.GetProperty("value").EnumerateArray());
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task RestWaitsForStableShopAndReadsHealthWithoutCollection()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Data, dir.FullName);
            api.InitializeMatchReading(Data, fixture.Engine);
            Assert.False(api.Settings.Collect);
            var waiting = Turn(api, "?wait=30&after=previous");
            var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "shop-terminal-ui100-2000x1125.png"));
            await Scan(api, frame, 2);
            var rest = await Turn(api);
            output.WriteLine(rest.GetRawText());
            Assert.Equal("rest", rest.GetProperty("phase").GetString());
            Assert.Equal(22, api.GetMatchState().Hp.Value);
            Assert.Equal(1, api.GetMatchState().RefreshCost.Value);
            Assert.Equal(rest.GetProperty("turnId").GetString(), (await waiting).GetProperty("turnId").GetString());
            var timed = Turn(api, "?wait=1&after=" + rest.GetProperty("turnId").GetString());
            Assert.False(timed.IsCompleted);
            Assert.Equal("rest", (await timed).GetProperty("phase").GetString());

            var next = Turn(api, "?wait=20&after=" + rest.GetProperty("turnId").GetString());
            await Scan(api, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "custom-picks-selection.png")));
            Assert.Equal("custom", (await next).GetProperty("phase").GetString());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData("collection-victory.png", "over")]
    [InlineData("collection-summary.png", "over")]
    [InlineData("custom-picks-solo-selecting-2v2.png", "custom")]
    public async Task PagesFromSamples(string sample, string phase)
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Data, dir.FullName);
            api.InitializeMatchReading(Data, fixture.Engine);
            await Scan(api, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", sample)));
            var turn = await Turn(api);
            output.WriteLine(turn.GetRawText());
            Assert.Equal(phase, turn.GetProperty("phase").GetString());
        }
        finally { dir.Delete(true); }
    }

    [Theory]
    [InlineData("rec-2026-11-15-hard-full.mp4", 50, "strategy")]
    [InlineData("rec-2026-11-15-hard-full.mp4", 230, "battle")]
    [InlineData("rec-2026-11-14-full-game.mp4", 850, "adapt")]
    public async Task PagesFromRecordings(string file, int seconds, string phase)
    {
        if (Environment.GetEnvironmentVariable("TURN_RECORDINGS") is not { } root) return;
        using var video = new VideoCapture(Path.Combine(root, file));
        video.PosMsec = seconds * 1000;
        using var image = new Mat();
        Assert.True(video.Read(image));
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Data, dir.FullName);
            api.InitializeMatchReading(Data, fixture.Engine);
            await Scan(api, image.ImEncode(".png"));
            var turn = await Turn(api);
            output.WriteLine(turn.GetRawText());
            if (phase == "strategy") Assert.Contains(turn.GetProperty("options").GetProperty("strategies").EnumerateArray(),
                s => s.GetProperty("id").ValueKind == JsonValueKind.String);
            Assert.Equal(phase, turn.GetProperty("phase").GetString());
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public async Task BatchRejectsSecondUnfundedBuyAndMoveUpdatesFieldOnDone()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Data, dir.FullName);
            api.SaveSettings(api.Settings with { Control = new(true, ControlSettings.ConsentVersion, true) });
            await api.ObserveFieldAsync([], DateTimeOffset.UtcNow, new(1, 5, 2, 6, true), true, true,
                [new() { SlotIndex = 1, Name = "杰克", Price = 2 }, new() { SlotIndex = 3, Name = "灰喉", Price = 5 }]);
            var body = Encoding.UTF8.GetBytes("""{"actions":[{"type":"buy","slot":1},{"type":"buy","slot":3}]}""");
            var error = await Assert.ThrowsAsync<LocalApiError>(() => api.RouteLocalAsync("POST",
                new("http://localhost/v1/actions"), body,
                default, (_, _, _) => throw new InvalidOperationException()));
            Assert.Equal(422, error.Status);
            Assert.Equal(1, error.Index);
            Assert.Contains("资金 3", error.Message);
            using var listener = new LocalHttpServer(dir.FullName, "default", (method, uri, bytes, token) =>
                api.RouteLocalAsync(method, uri, bytes, token, (_, _, _) => throw new InvalidOperationException()));
            var stderr = new StringWriter();
            var stdout = new StringWriter();
            var code = await StrongholdAssistant.Cli.Program.RunAsync(
                ["do", """[{"type":"buy","slot":1},{"type":"buy","slot":3}]"""], dir.FullName, stdout, stderr);
            Assert.Equal(1, code);
            Assert.Empty(stdout.ToString());
            var problem = JsonSerializer.Deserialize<JsonElement>(stderr.ToString());
            Assert.Equal(error.Message, problem.GetProperty("error").GetString());
            Assert.Equal(1, problem.GetProperty("index").GetInt32());

            await api.ResetMatchAsync();
            api.CreateShopReader(fixture.Engine);
            api.InitializeField(Data, fixture.Engine);
            var from = new FieldCell("bench", 9, 0);
            var to = new FieldCell("board", 5, 3);
            api.SetFieldOperator(from, "杰克");
            var step = new ControlStep(new MoveAction(from, to));
            var job = new ControlJob("move", "api", "auto", "running", [step], null, null, DateTimeOffset.UtcNow, null);
            api.RecordControlJob(job);
            api.RecordControlJob(job with { State = "done", Steps = [step with { State = "done" }], EndedAt = DateTimeOffset.UtcNow });
            var moved = Assert.Single(api.Field!.Units);
            Assert.Equal((to, "杰克", "inferred"), (moved.Cell, moved.Operator, moved.Source));
            Assert.Equal("inferred", api.GetMatchState().Units.Source);
        }
        finally { dir.Delete(true); }
    }
}
