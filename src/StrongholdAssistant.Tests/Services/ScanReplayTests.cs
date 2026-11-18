using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests.Services;

[Collection("Shop OCR")]
public sealed class ScanReplayTests(ShopReaderFixture catalog)
{
    // SCAN_REPLAY 指向 JSON 数组；每段给出 Phase、Theme、Paths（依次采样的 PNG）。
    // SCAN_RESULTS 接收逐帧 JSONL；不启动扫描器的截屏计时器。
    public sealed record Clip(string Phase, GameShopTheme Theme, string[] Paths, bool Adb = false);

    [Fact]
    public async Task Replay()
    {
        if (Environment.GetEnvironmentVariable("SCAN_REPLAY") is not { } manifest) return;
        var clips = JsonSerializer.Deserialize<Clip[]>(File.ReadAllText(manifest))!;
        string results = Environment.GetEnvironmentVariable("SCAN_RESULTS")!;
        using var output = File.CreateText(results);
        using var process = Process.GetCurrentProcess();
        var scan = typeof(OcrScannerService).GetMethod("ScanFrameAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string data = Path.Combine(AppContext.BaseDirectory, "data");
        var session = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(results))!, Guid.NewGuid().ToString("N"));
        var api = await HostApi.CreateAsync(data, session);
        // 性能回放走宿主的原生 OCR。测试夹具的整图散列和跨帧结果缓存会改变计时。
        using var native = NativeOcrEngines.Create();
        var profile = Environment.GetEnvironmentVariable("SCAN_OCR_PROFILE") == "1"
            ? new ReplayOcrProfile(native, process) : null;
        IOcrEngine ocr = (IOcrEngine?)profile ?? native;
        var reader = api.CreateShopReader(ocr);
        api.InitializeBoardRoutes(data);
        api.InitializeField(data, ocr);
        api.InitializeMatchReading(data, ocr);
        BoardRoutes? routes = null;
        api.BoardRoutesChanged += value => routes = value;
        api.BoardRoutesEnabled = true;
        foreach (var clip in clips)
        {
            var state = new ASPAssistant.Core.GameState.GameState();
            await api.ResetMatchAsync();
            routes = null;
            reader.Theme = clip.Theme;
            using var scanner = new OcrScannerService(new(), new GarrisonOcrStrategy(), reader,
                ocr, state, () => catalog.Names);
            var timer = (System.Timers.Timer)typeof(OcrScannerService).GetField("_scanTimer",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scanner)!;
            var clock = new Stopwatch();
            double? publish = null, remove = null;
            EconomyReading? economy = null;
            GameThemeReading? theme = null;
            bool opening = false, panel = false;
            scanner.GameStateUpdated += _ =>
            {
                if (scanner.RemovingChangedShopItems) remove = clock.Elapsed.TotalMilliseconds;
                else publish ??= clock.Elapsed.TotalMilliseconds;
            };
            scanner.EconomyRead += value => { economy = value; api.ObserveEconomy(value); };
            scanner.ThemeDetected += value => theme = value;
            scanner.BanScreenDetected += _ => opening = true;
            scanner.BanScreenLost += () => opening = false;
            scanner.EnemyPanelSeen += () => panel = true;
            scanner.BoardFrameReady += (frame, value) => api.ObserveBoardFrame(frame, value,
                scanner.BoardShopVisible, scanner.BoardMainScreenVisible);
            scanner.EnemyPanelSeen += api.CountEnemyPanel;
            scanner.ObserveFieldAsync = api.ObserveFieldAsync;
            scanner.ObserveMatchFrameAsync = api.ObserveMatchFrameAsync;
            foreach (string path in clip.Paths)
            {
                using var source = new Bitmap(path);
                var encoded = clip.Adb ? File.ReadAllBytes(path) : null;
                publish = remove = null;
                panel = false;
                profile?.Calls.Clear();
                var cpu = process.TotalProcessorTime;
                clock.Restart();
                var at = DateTimeOffset.UtcNow;
                var bytes = encoded ?? new ScreenCaptureService().EncodeCapture(source, scanPixels: true);
                var (w, h) = OcrScannerService.GetFrameDimensions(bytes);
                double capture = clock.Elapsed.TotalMilliseconds;
                using var stages = new FieldRecognitionTiming();
                await (Task)scan.Invoke(scanner, [bytes, w, h, at, capture, null, 0d])!;
                double elapsed = clock.Elapsed.TotalMilliseconds;
                var period = timer.Interval == 150 ? Math.Ceiling(elapsed / timer.Interval) * timer.Interval
                    : elapsed + timer.Interval;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, period - elapsed)));
                process.Refresh();
                output.WriteLine(JsonSerializer.Serialize(new { clip.Phase, frame = Path.GetFileName(path),
                    capture, elapsed, stages = stages.Snapshot(), period = clock.Elapsed.TotalMilliseconds, publish, remove,
                    calls = profile?.Calls,
                    cpu = (process.TotalProcessorTime - cpu).TotalMilliseconds, memory = process.WorkingSet64,
                    result = new { economy, theme, opening, panel, scanner.BoardMainScreenVisible,
                        scanner.BoardDetailVisible, scanner.BoardShopVisible, state.ShopItems, api.Match.CustomPicks,
                        routes, enemies = api.Match.RoundEnemies?.Enemies } }));
            }
            // 每段最后一帧另测一次竞争；在后台已进入时请求手动截图，只计到取帧入口。
            using var last = new Bitmap(clip.Paths[^1]);
            var input = new ScreenCaptureService().EncodeCapture(last);
            var size = OcrScannerService.GetFrameDimensions(input);
            var gate = (SemaphoreSlim)typeof(OcrScannerService).GetField("_scanGate",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(scanner)!;
            await gate.WaitAsync();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var background = Task.Run(async () =>
            {
                started.SetResult();
                try { await (Task)scan.Invoke(scanner, [input, size.Width, size.Height, DateTimeOffset.UtcNow, 0d, null, 0d])!; }
                finally { gate.Release(); }
            });
            await started.Task;
            await Task.Delay(100);
            using var timing = new FieldRecognitionTiming();
            await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.ReadCurrentFieldAsync(
                () => Task.FromResult<byte[]?>(null), api.RecognizeFieldFrameAsync));
            await background;
            double? refresh = null;
            if (state.ShopItems.Count != 0)
            {
                clock.Restart();
                scanner.RefreshShop();
                refresh = clock.Elapsed.TotalMilliseconds;
                Assert.Empty(state.ShopItems);
            }
            output.WriteLine(JsonSerializer.Serialize(new { clip.Phase, scanWait = timing.Snapshot()["scanWait"], refresh }));
        }
    }
}

