// 2026-11-12：复用商店扫描截图读取回合与资金，通过事件交给宿主记录。
// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Threading.Channels;
// 2026-11-13：先发布商店，按名称指纹复用结果，辅助识别降频并支持热切换扫描速度。
using System.Diagnostics;
// 2026-11-12：刷新键清空商店状态并过滤旧截图；收集失败现场和 OCR 步骤。
using System.Timers;
using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Models;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class OcrScannerService : IDisposable
{
    private readonly ScreenCaptureService _captureService;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly ShopReader _shopReader;
    private readonly IOcrEngine _ocrEngine;
    private readonly DiagnosticOcrEngine _trace;
    private readonly GameThemeDetector _themeDetector;
    private readonly GameThemeTracker _themeTracker = new();
    public event Action<GameThemeReading>? ThemeDetected;
    public event Action<byte[]?, string, double?, string[]>? RecognitionFailed;
    private readonly GameState.GameState _gameState;
    private readonly Func<IReadOnlyList<string>> _getTrackedNames;
    private readonly Func<IReadOnlyList<(string Name, IReadOnlyList<string> CoreCovenants, IReadOnlyList<string> AdditionalCovenants, IReadOnlyList<string> TemplateNames)>>? _getAllOperators;
    private readonly MaaBanIconMatcher? _iconMatcher;
    private readonly Timer _scanTimer;
    private readonly object _shopGate = new();
    private readonly ShopRefreshWindow _refreshWindow = new();
    private int _cacheVersion;
    private int _readVersion = -1;
    private ShopFrameFingerprint? _fingerprint;
    private (int Width, int Height, GameShopTheme Theme) _frameKey;
    private string[] _trackedNames = [];
    private IReadOnlyList<ShopItem> _cachedItems = [];
    private EconomyReading? _lastEconomy;
    private bool _economyStable;
    private bool _skipEconomy;
    private byte[]? _economyContext;
    private byte[]? _informationContext;
    private long _lastTimingLog;
    public void InvalidateShop() => Interlocked.Increment(ref _cacheVersion);
    private double _configuredInterval;
    private bool _visibleRest;
    public void SetShopScanSpeed(string speed)
    {
        _configuredInterval = speed == "fast" ? 250 : 800;
        UpdateScanInterval(_visibleRest);
    }
    private void UpdateScanInterval(bool visibleRest)
    {
        _visibleRest = visibleRest;
        double interval = visibleRest ? Math.Min(_configuredInterval, 400) : _configuredInterval;
        if (_scanTimer.Interval != interval) _scanTimer.Interval = interval;
    }
    public int ShopGeneration { get { lock (_shopGate) return _refreshWindow.Generation; } }
    public void RefreshShop()
    {
        lock (_shopGate)
        {
            _refreshWindow.Refresh();
            InvalidateShop();
            _gameState.ShopItems = [];
        }
    }
    private int _scanning; // re-entry guard (0=idle, 1=busy)

    // Accumulated ban set for manual screenshot scans.
    private readonly HashSet<string> _accumulatedBans = [];

    // Manual screenshot scan queue — button-triggered scans execute sequentially.
    private readonly Channel<byte[]> _manualScanChannel = Channel.CreateUnbounded<byte[]>();
    private int _pendingManualScans;

    // Ban screen detection state machine.
    private bool _banScreenActive;
    private readonly InformationScreenState _informationState = new();
    private readonly InformationScreenDetector _informationDetector;
    private int _banScreenAbsentTicks;
    private volatile bool _pauseBanScreenDetection;
    private const int BanLostThreshold   = 3;

    public event Action<GameState.GameState>? GameStateUpdated;
    public event Action<EconomyReading>? EconomyRead;

    /// <summary>
    /// Raised when the periodic scan first detects that the game is on the ban selection screen.
    /// Fires once per entry; resets after the screen is left.
    /// </summary>
    public event Action<bool>? BanScreenDetected;
    public Func<bool> AwaitingDetail { get; set; } = () => false;
    public event Action? DetailScreenDetected;

    /// <summary>
    /// Raised when the ban selection screen is no longer detected.
    /// </summary>
    public event Action? BanScreenLost;

    /// <summary>
    /// Raised when manual screenshot matching finds newly banned operators.
    /// The argument is the full accumulated list of banned operator names.
    /// </summary>
    public event Action<IReadOnlyList<string>>? BansDetected;

    /// <summary>
    /// Raised when the number of pending manual screenshot scans changes.
    /// Argument is the new pending count (0 = all done).
    /// </summary>
    public event Action<int>? ManualScanQueueChanged;

    public OcrScannerService(
        ScreenCaptureService captureService,
        IOcrStrategy ocrStrategy,
        ShopReader shopReader,
        IOcrEngine ocrEngine,
        GameState.GameState gameState,
        Func<IReadOnlyList<string>> getTrackedNames,
        Func<IReadOnlyList<(string Name, IReadOnlyList<string> CoreCovenants, IReadOnlyList<string> AdditionalCovenants, IReadOnlyList<string> TemplateNames)>>? getAllOperators = null,
        MaaBanIconMatcher? iconMatcher = null,
        int intervalMs = 800)
    {
        _captureService = captureService;
        _ocrStrategy = ocrStrategy;
        _shopReader = shopReader;
        _trace = ocrEngine as DiagnosticOcrEngine ?? new DiagnosticOcrEngine(ocrEngine);
        _ocrEngine = _trace;
        _themeDetector = new GameThemeDetector(_trace);
        _informationDetector = new InformationScreenDetector(_trace, ocrStrategy);
        _gameState = gameState;
        _getTrackedNames = getTrackedNames;
        _getAllOperators = getAllOperators;
        _iconMatcher = iconMatcher;
        _scanTimer = new Timer(intervalMs);
        _configuredInterval = intervalMs;
        _scanTimer.Elapsed += OnScanTick;

        // Start the background consumer for button-triggered manual scans.
        _ = Task.Run(ProcessManualScansAsync);
    }

    // 宿主按覆盖层需求启用；复用这一轮截图与资金识别的休整期/回合读数。
    public Func<bool> BoardRoutesRequested { get; set; } = () => false;
    public event Action<byte[]?, EconomyReading?>? BoardFrameReady;
    public bool BoardMainScreenVisible { get; private set; }
    private byte[]? _boardSurfacePixels;
    private (int, int) _boardSurfaceSize;
    public bool BoardShopVisible => _shopReader.HasVisibleCards;
    public Func<byte[], DateTimeOffset, EconomyReading?, bool, bool, IReadOnlyList<ShopItem>, Task>? ObserveFieldAsync { get; set; }

    public void Start() => _scanTimer.Start();

    // 2026-11-11：StrongholdAssistant 录入及关闭前等待检测释放 OCR。
    public async Task WaitForIdleAsync()
    {
        while (Volatile.Read(ref _scanning) != 0) await Task.Delay(20);
    }

    /// <summary>
    /// Temporarily pauses ban-screen state-machine OCR checks. This is used while
    /// app-controlled fullscreen overlays are shown, so those overlays do not
    /// accidentally trigger BanScreenLost/BanScreenDetected flapping.
    /// </summary>
    public void SetBanScreenDetectionPaused(bool paused)
    {
        _pauseBanScreenDetection = paused;
        if (paused)
        {
            UpdateScanInterval(false);
            _themeTracker.Observe(null);
            BoardFrameReady?.Invoke(null, null);
            // Freeze state transitions while overlays cover the game window.
            // Keep _banScreenActive as-is, but clear accumulated edge counters
            // to avoid immediate false lost/detected transitions after resume.
            _informationState.Suspend();
            _banScreenAbsentTicks = 0;
        }
        AppLogger.Info("BanScreen", $"Ban screen detection paused={paused}");
    }

    private async void OnScanTick(object? sender, ElapsedEventArgs e)
    {
        if (_pauseBanScreenDetection) return;
        // Skip if a previous scan is still running.
        if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0)
            return;

        byte[]? screenshot = null;
        _trace.Clear();
        try
        {
            var clock = Stopwatch.StartNew();
            var capturedAt = _refreshWindow.CaptureStarted();
            ShopFrameFingerprint? fingerprint = null;
            double fingerprintMs = 0;
            var theme = _shopReader.Theme;
            screenshot = _captureService.CaptureScreen(inspect: bitmap =>
            {
                var comparison = Stopwatch.StartNew();
                fingerprint = ShopFrameFingerprint.Read(bitmap, theme);
                fingerprintMs = comparison.Elapsed.TotalMilliseconds;
            });
            double captureMs = clock.Elapsed.TotalMilliseconds - fingerprintMs;
            if (screenshot == null)
            {
                UpdateScanInterval(false);
                _themeTracker.Observe(null);
                BoardFrameReady?.Invoke(null, null);
                InvalidateShop();
                _informationState.Suspend();
                PublishShopItems([], false, capturedAt);
                return;
            }

            // Derive dimensions from the actual PNG bytes so that every downstream
            // coordinate (ROI, card boxes, normalization) lives in the same pixel
            // space as the image MAA will process. Using GetClientRect here was
            // wrong when ScreenCaptureService downscales the capture to MaxCaptureWidth.
            var (imgWidth, imgHeight) = GetPngDimensions(screenshot);
            if (imgWidth <= 0 || imgHeight <= 0)
                return;

            if (theme != _shopReader.Theme) fingerprint = null;
            await ScanFrameAsync(screenshot, imgWidth, imgHeight, capturedAt, captureMs, fingerprint, fingerprintMs);
        }
        catch (Exception ex)
        {
            // Swallow unexpected errors (e.g. capture failure during window state change)
            // so the timer-driven async void method never propagates an unhandled exception.
            AppLogger.Error("OcrScanner", "Scan tick failed", ex);
            RecognitionFailed?.Invoke(screenshot, "商店识别失败。\n" + ex.Message, _shopReader.UiScale, _trace.Steps);
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    // 样图测试直接调用这一轮流程；截图仍只由计时器负责。
    private async Task ScanFrameAsync(byte[] screenshot, int width, int height, DateTimeOffset capturedAt, double captureMs,
        ShopFrameFingerprint? fingerprint, double fingerprintMs)
    {
        lock (_shopGate)
            if (!_refreshWindow.Accept(capturedAt)) return;
        var fieldSurface = ObserveFieldAsync is not null ? FieldReader.InspectSurface(screenshot) : default;
        bool confirmation = false;
        if (!fieldSurface.Obscured && fieldSurface.ConfirmationCandidate)
        {
            double scale = Math.Min(width / 1920d, height / 1080d);
            var text = await _ocrEngine.RecognizeRegionAsync(screenshot, (int)(650 * scale), (int)(400 * scale),
                (int)(620 * scale), (int)(150 * scale));
            confirmation = text.Any(t => t.Text.Replace(" ", "").Contains("确认准备", StringComparison.Ordinal));
        }
        if (fieldSurface.Obscured || confirmation)
        {
            BoardMainScreenVisible = false;
            _boardSurfacePixels = null;
            UpdateScanInterval(false);
            BoardFrameReady?.Invoke(null, null);
            return;
        }
        int version = Volatile.Read(ref _cacheVersion);
        var theme = _shopReader.Theme;
        var tracked = _getTrackedNames().ToArray();
        var key = (width, height, theme);
        bool forced = version != _readVersion || key != _frameKey || !tracked.SequenceEqual(_trackedNames);
        var clock = Stopwatch.StartNew();
        // ADB 返回编码图；样图测试也走这一入口。PC 截图已经在编码前生成了指纹。
        fingerprint ??= ShopFrameFingerprint.Read(screenshot, theme);
        fingerprintMs += clock.Elapsed.TotalMilliseconds;
        bool changed = forced || _fingerprint is null || !ShopFrameFingerprint.SameNames(_fingerprint.Names, fingerprint.Names);
        if (_fingerprint is { } previous && changed)
        {
            lock (_shopGate)
            {
                if (_refreshWindow.RemoveChanged(item => key != _frameKey || previous.ChangedSlot(fingerprint, item), capturedAt, out var remaining))
                {
                    _gameState.ShopItems = remaining.ToList();
                    ChangedShopCapturedAt = capturedAt;
                    RemovingChangedShopItems = true;
                    try { GameStateUpdated?.Invoke(_gameState); }
                    finally { RemovingChangedShopItems = false; }
                    AppLogger.Info("ShopTiming", $"画面变化清除标记 captureToRemove={(DateTimeOffset.UtcNow - capturedAt).TotalMilliseconds:F1}ms（发布到覆盖层）");
                }
            }
        }
        double? shopMs = null, economyMs = null, informationMs = null;
        if (changed)
        {
            clock.Restart();
            _cachedItems = await _shopReader.ReadAsync(screenshot, width, height, tracked);
            shopMs = clock.Elapsed.TotalMilliseconds;
            _fingerprint = fingerprint;
            _readVersion = version;
            _frameKey = key;
            _trackedNames = tracked;
            _economyStable = _skipEconomy = false;
        }
        clock.Restart();
        PublishShopItems(_cachedItems, _shopReader.HasVisibleCards, capturedAt);
        double publishMs = clock.Elapsed.TotalMilliseconds;
        if (changed && _shopReader.Failure is { } failure)
            RecognitionFailed?.Invoke(screenshot, failure, _shopReader.UiScale, _trace.Steps);

        // 商店先发布。确认新主题后宿主同步更新设置；本帧后续读数和下一帧商店用新主题。
        var detected = await _themeDetector.ReadAsync(screenshot, width, height);
        if (_themeTracker.Observe(detected))
        {
            AppLogger.Info("GameTheme", $"theme={detected!.Theme} score={detected.Score:F4}");
            ThemeDetected?.Invoke(detected);
        }
        if (theme != _shopReader.Theme)
        {
            theme = _shopReader.Theme;
            _economyStable = _skipEconomy = false;
            _economyContext = null;
        }

        // 静止画面不读资金；画面变化但资金稳定时隔轮读。刷新和名称变化立即恢复。
        bool economyChanged = _economyContext is null || !_economyContext.AsSpan().SequenceEqual(fingerprint.Context);
        EconomyReading? boardEconomy = _lastEconomy;
        if ((EconomyRead is not null || BoardRoutesRequested() || ObserveFieldAsync is not null) && (changed || economyChanged))
        {
            if (!_skipEconomy || changed)
            {
                clock.Restart();
                var reading = await new EconomyReader(_ocrEngine, theme).ReadAsync(screenshot, width, height);
                _economyStable = reading == _lastEconomy;
                _lastEconomy = reading;
                boardEconomy = reading;
                _economyContext = fingerprint.Context;
                EconomyRead?.Invoke(reading);
                economyMs = clock.Elapsed.TotalMilliseconds;
                _skipEconomy = _economyStable;
            }
            else
            {
                _skipEconomy = false;
                // 顶栏有变化但本轮资金 OCR 降频时，不用旧的休整期状态启动棋盘识别。
                boardEconomy = null;
            }
        }
        // 状态机尚未稳定、等待对局详情时继续确认；稳定的商店静帧不再做信息页 OCR。
        bool informationChanged = _informationContext is null || !_informationContext.AsSpan().SequenceEqual(fingerprint.Context);
        if (!_pauseBanScreenDetection && (forced || informationChanged || _banScreenActive
            || _informationState.IsConsumed || AwaitingDetail() || !_shopReader.HasVisibleCards))
        {
            clock.Restart();
            await DetectBanScreenAsync(screenshot, width, height);
            _informationContext = fingerprint.Context;
            informationMs = clock.Elapsed.TotalMilliseconds;
        }
        if (_pauseBanScreenDetection || _banScreenActive) PublishShopItems([], false, capturedAt);
        // 商店已先发布，棋盘识别不会推迟本轮商店结果。
        if (BoardRoutesRequested() || ObserveFieldAsync is not null)
        {
            await ReadBoardSurfaceAsync(screenshot, width, height);
            BoardFrameReady?.Invoke(_pauseBanScreenDetection || _banScreenActive ? null : screenshot, boardEconomy);
            // 本帧就绪控件可见时，资金降频不应跳过占用变化；战斗/弹窗仍不得拿旧读数更新占用。
            var fieldEconomy = boardEconomy ?? (BoardMainScreenVisible ? _lastEconomy : null);
            UpdateScanInterval(!_pauseBanScreenDetection && !_banScreenActive && !fieldSurface.Detail
                && BoardMainScreenVisible && fieldEconomy is { IsRest: true });
            if (!_pauseBanScreenDetection && !_banScreenActive && ObserveFieldAsync is { } observe && fieldEconomy is not null)
                await observe(screenshot, capturedAt, fieldEconomy, BoardMainScreenVisible, BoardShopVisible, _shopReader.AllItems);
        }
        double total = captureMs + fingerprintMs + (shopMs ?? 0) + publishMs + (economyMs ?? 0) + (informationMs ?? 0);
        // 有卡片时至多每两秒一行；慢轮（超过 800 ms）始终记录。
        if (total >= 800 || _shopReader.HasVisibleCards && Environment.TickCount64 - _lastTimingLog >= 2000)
        {
            _lastTimingLog = Environment.TickCount64;
            static string Ms(double? value) => value is { } ms ? $"{ms:F1}ms" : "skip";
            AppLogger.Info("ShopTiming", $"capture={Ms(captureMs)} fingerprint={Ms(fingerprintMs)} shop={Ms(shopMs)} publish={Ms(publishMs)} economy={Ms(economyMs)} information={Ms(informationMs)} captureToPublish={captureMs + fingerprintMs + (shopMs ?? 0) + publishMs:F1}ms");
        }
    }

    public DateTimeOffset ChangedShopCapturedAt { get; private set; }
    public bool RemovingChangedShopItems { get; private set; }

    private async Task ReadBoardSurfaceAsync(byte[] frame, int width, int height)
    {
        using var stream = new MemoryStream(frame);
        using var image = new System.Drawing.Bitmap(stream);
        double scale = Math.Min(width / 1920d, height / 1080d);
        var pixels = new byte[32 * 20 * 3];
        int index = 0;
        for (int row = 0; row < 20; row++) for (int col = 0; col < 32; col++)
        {
            var c = image.GetPixel((int)(width - (300 - (col + .5) * 300 / 32) * scale),
                (int)((100 + (row + .5) * 160 / 20) * scale));
            pixels[index++] = c.R; pixels[index++] = c.G; pixels[index++] = c.B;
        }
        // 只复用就绪控件这一小块画面；弹窗遮盖或明暗变化会重新 OCR，不能用整局状态代替当前画面。
        bool same = _boardSurfaceSize == (width, height) && _boardSurfacePixels is { } old
            && pixels.Zip(old, (a, b) => Math.Abs(a - b)).Average() < 2;
        if (same) return;
        BoardMainScreenVisible = await new BoardSurfaceReader(_ocrEngine).IsMainScreenAsync(frame, width, height);
        _boardSurfacePixels = pixels; _boardSurfaceSize = (width, height);
    }

    private void PublishShopItems(IReadOnlyList<ShopItem> items, bool shopVisible, DateTimeOffset capturedAt)
    {
        lock (_shopGate)
        {
            if (!_refreshWindow.TryUpdate(items, capturedAt, shopVisible, out var stableItems)) return;
            _gameState.ShopItems = stableItems.ToList();
            GameStateUpdated?.Invoke(_gameState);
        }
    }

    /// <summary>
    /// Captures the current game window screenshot and enqueues it for ban-screen matching.
    /// The scan runs on a background consumer thread so multiple calls queue up sequentially.
    /// </summary>
    public void EnqueueManualScan()
    {
        var screenshot = _captureService.CaptureScreen();
        if (screenshot == null)
        {
            AppLogger.Warn("OcrScanner", "EnqueueManualScan: screenshot capture returned null");
            return;
        }
        var pending = Interlocked.Increment(ref _pendingManualScans);
        AppLogger.Info("OcrScanner", $"Manual scan enqueued (pending={pending}, png={screenshot.Length} bytes)");
        ManualScanQueueChanged?.Invoke(pending);
        _manualScanChannel.Writer.TryWrite(screenshot);
    }

    private async Task ProcessManualScansAsync()
    {
        var regions = _ocrStrategy.GetScanRegions();
        await foreach (var screenshot in _manualScanChannel.Reader.ReadAllAsync())
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var (w, h) = GetPngDimensions(screenshot);
                AppLogger.Info("OcrScanner", $"Manual scan started (img={w}x{h})");
                await RunBanMatchCoreAsync(screenshot, regions, w, h);
                AppLogger.Info("OcrScanner", $"Manual scan completed in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                AppLogger.Error("OcrScanner", "Manual scan failed", ex);
            }
            finally
            {
                var remaining = Interlocked.Decrement(ref _pendingManualScans);
                ManualScanQueueChanged?.Invoke(remaining);
            }
        }
    }

    /// <summary>
    /// Reads width and height from a PNG file's IHDR chunk (bytes 16–23).
    /// No dependencies beyond the raw byte array.
    /// </summary>
    private static (int Width, int Height) GetPngDimensions(byte[] png)
    {
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    /// <summary>
    /// Core ban-screen matching: faction OCR → candidate filter → batch FeatureMatch.
    /// Accumulates hits in <see cref="_accumulatedBans"/> and fires <see cref="BansDetected"/>.
    /// </summary>
    private async Task RunBanMatchCoreAsync(
        byte[] screenshot,
        IReadOnlyList<OcrRegionDefinition> regions,
        int imgWidth,
        int imgHeight)
    {
        // ── Faction OCR: detect which covenant groups are visible in the left panel ──────────
        var allOperators = _getAllOperators!();
        var allCovenants = allOperators
            .SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet();

        var visibleFactions = new HashSet<string>();
        var factionRegion = regions.FirstOrDefault(r => r.Name == "BanFactionPanel");
        if (factionRegion != null)
        {
            int fx = (int)(factionRegion.XPercent * imgWidth);
            int fy = (int)(factionRegion.YPercent * imgHeight);
            int fw = (int)(factionRegion.WidthPercent * imgWidth);
            int fh = (int)(factionRegion.HeightPercent * imgHeight);
            var factionOcr = await _ocrEngine.RecognizeRegionAsync(screenshot, fx, fy, fw, fh);
            var ocrTexts = factionOcr.Select(r => r.Text).ToList();
            AppLogger.Info("BanMatch", $"Faction OCR ({ocrTexts.Count} results): [{string.Join(", ", ocrTexts)}]");
            foreach (var result in factionOcr)
                foreach (var covenant in allCovenants)
                    if (result.Text.Contains(covenant))
                        visibleFactions.Add(covenant);
            AppLogger.Info("BanMatch", $"Visible factions: [{string.Join(", ", visibleFactions)}]");
        }
        else
        {
            AppLogger.Warn("BanMatch", "BanFactionPanel region not found, no faction pre-filter applied");
        }

        // Build candidate list, skipping already-identified bans and filtering by visible faction.
        var buildSw = System.Diagnostics.Stopwatch.StartNew();
        var totalOperators = allOperators.Count;
        var alreadyBanned = allOperators.Count(x => _accumulatedBans.Contains(x.Name));
        var candidateSnapshot = allOperators
            .Where(x => !_accumulatedBans.Contains(x.Name))
            .Where(x => visibleFactions.Count == 0
                || x.CoreCovenants.Any(c => visibleFactions.Contains(c))
                || x.AdditionalCovenants.Any(c => visibleFactions.Contains(c)))
            .Select(x => (x.Name, x.TemplateNames))
            .ToList();
        buildSw.Stop();
        AppLogger.Info("BanMatch",
            $"Candidates: {candidateSnapshot.Count} built in {buildSw.ElapsedMilliseconds}ms " +
            $"(total={totalOperators}, already-banned={alreadyBanned}, " +
            $"templates={candidateSnapshot.Sum(c => c.TemplateNames.Count)})");

        bool anyNewMatch = false;
        var newlyDetected = new List<string>();
        var matchSw = System.Diagnostics.Stopwatch.StartNew();
        var candidateSw = System.Diagnostics.Stopwatch.StartNew();
        long minCandidateMs = long.MaxValue, maxCandidateMs = 0, sumCandidateMs = 0;
        int candidateIndex = 0;
        foreach (var candidate in candidateSnapshot)
        {
            candidateSw.Restart();
            var tuple = await _iconMatcher!.FindBestInSlotAsync(screenshot, candidate, imgWidth, imgHeight);
            var candidateMs = candidateSw.ElapsedMilliseconds;
            sumCandidateMs += candidateMs;
            if (candidateMs < minCandidateMs) minCandidateMs = candidateMs;
            if (candidateMs > maxCandidateMs) maxCandidateMs = candidateMs;
            candidateIndex++;

            // Log a progress heartbeat every 20 candidates so we can see scan is alive.
            if (candidateIndex % 20 == 0)
                AppLogger.Info("BanMatch",
                    $"Progress: {candidateIndex}/{candidateSnapshot.Count} — " +
                    $"elapsed={matchSw.ElapsedMilliseconds}ms avg={sumCandidateMs/candidateIndex}ms/op max={maxCandidateMs}ms/op");

            if (tuple == null)
                continue;

            var (name, hit, hitBox) = tuple.Value;
            if (hit && !_accumulatedBans.Contains(name))
            {
                _accumulatedBans.Add(name);
                newlyDetected.Add(name);
                anyNewMatch = true;
            }
        }
        matchSw.Stop();
        long avgMs = candidateSnapshot.Count > 0 ? sumCandidateMs / candidateSnapshot.Count : 0;
        AppLogger.Info("BanMatch",
            $"Match done: elapsed={matchSw.ElapsedMilliseconds}ms " +
            $"candidates={candidateSnapshot.Count} avg={avgMs}ms/op min={minCandidateMs}ms/op max={maxCandidateMs}ms/op — " +
            $"new={newlyDetected.Count} [{string.Join(", ", newlyDetected)}], " +
            $"accumulated={_accumulatedBans.Count} [{string.Join(", ", _accumulatedBans)}]");

        if (anyNewMatch)
            BansDetected?.Invoke(_accumulatedBans.ToList());
    }

    /// <summary>
    /// Mirrors a manual ban/unban into <see cref="_accumulatedBans"/> so subsequent scans
    /// don't re-detect (and re-ban) an operator the user has deliberately unbanned.
    /// </summary>
    public void SetManualBan(string name, bool banned)
    {
        if (banned) _accumulatedBans.Add(name);
        else _accumulatedBans.Remove(name);
        AppLogger.Info("OcrScanner", $"Manual ban: {name} -> {(banned ? "banned" : "unbanned")} (accumulated={_accumulatedBans.Count})");
    }

    /// <summary>Successful manual recording also consumes this opening screen.</summary>
    public void MarkInformationRecorded() => _informationState.MarkRecorded();

    private async Task DetectBanScreenAsync(byte[] screenshot, int width, int height)
    {
        bool opening = await _informationDetector.IsOpeningScreenAsync(screenshot, width, height);
        bool gameplay = !opening && _informationState.IsConsumed
            && await _informationDetector.IsGameplayAsync(screenshot, width, height);
        if (_pauseBanScreenDetection) return;

        if (opening)
        {
            _banScreenActive = true;
            _banScreenAbsentTicks = 0;
        }
        else if (_banScreenActive && ++_banScreenAbsentTicks >= BanLostThreshold)
        {
            _banScreenActive = false;
            BanScreenLost?.Invoke();
        }
        if (_informationState.Observe(opening, gameplay))
        {
            AppLogger.Info("BanScreen", "Validated opening info screen detected");
            bool solo = await _informationDetector.HasSoloPromptAsync(screenshot, width, height);
            BanScreenDetected?.Invoke(solo);
        }
        if (!opening && AwaitingDetail()
            && await _informationDetector.IsInformationScreenAsync(screenshot, width, height))
            DetailScreenDetected?.Invoke();
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _scanTimer.Dispose();
        _manualScanChannel.Writer.TryComplete();
    }
}
