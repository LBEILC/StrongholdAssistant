// 2026-11-12：复用商店扫描截图读取回合与资金，通过事件交给宿主记录。
// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Threading.Channels;
// 2026-11-13：先发布商店，按名称指纹复用结果，辅助识别降频并支持热切换扫描速度。
using System.Diagnostics;
using System.Drawing;
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
    private bool _disposed;
    private byte[]? _combatRound;
    private readonly object _shopGate = new();
    private readonly ShopRefreshWindow _refreshWindow = new();
    private int _cacheVersion;
    private int _readVersion = -1;
    private ShopFrameFingerprint? _fingerprint;
    private (int Width, int Height, GameShopTheme Theme) _frameKey;
    private string[] _trackedNames = [];
    private IReadOnlyList<ShopItem> _cachedItems = [];
    private volatile EconomyReading? _lastEconomy;
    private EconomyReader? _economyReader;
    private long _lastTimingLog;
    public void InvalidateShop()
    {
        Interlocked.Increment(ref _cacheVersion);
        _shopReader.InvalidateNames();
    }
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
    private void SetIdleInterval(double interval)
    {
        _visibleRest = false;
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
    private readonly SemaphoreSlim _scanGate = new(1, 1);

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
    // 保留已验证的完整开局帧，自动录入不再等下一次截屏。
    public byte[]? OpeningScreenshot { get; private set; }
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
        _probeTimer.Elapsed += (_, _) => ProbeEnemyPanel();

        // Start the background consumer for button-triggered manual scans.
        _ = Task.Run(ProcessManualScansAsync);
    }

    // 宿主按覆盖层需求启用；复用这一轮截图与资金识别的休整期/回合读数。
    public Func<bool> BoardRoutesRequested { get; set; } = () => false;
    public event Action<byte[]?, EconomyReading?>? BoardFrameReady;
    public Action<byte[], DateTimeOffset>? CollectionFrame { get; set; }
    public Action<DateTimeOffset>? CollectionScanCompleted { get; set; }
    public bool BoardMainScreenVisible { get; private set; }
    private byte[]? _boardSurfacePixels;
    private (int, int) _boardSurfaceSize;
    public bool BoardShopVisible => _shopReader.HasVisibleCards;
    public bool BoardDetailVisible { get; private set; }
    public Func<byte[], DateTimeOffset, EconomyReading?, bool, bool, IReadOnlyList<ShopItem>, Task>? ObserveFieldAsync { get; set; }
    public Func<byte[], int, int, EconomyReading?, double, Task>? ObserveMatchFrameAsync { get; set; }

    // 敌人面板是悬停弹出的，只停 1～2 秒，而一轮扫描要 2～3 秒：主扫描忙时探针每 300 ms 只截面板那一小块看一眼，
    // 在就截整图存着（最多一张，下一轮读一次就丢）；本轮截图本身是面板时只读敌人，并很快再截一张。
    private readonly Timer _probeTimer = new(300);
    private byte[]? _pendingEnemyFrame;
    private int _probing;
    private bool _enemyPanelOpen, _enemyPanelFast;
    /// <summary>面板新弹出一次（上一次看到的截图里还没有）时触发，在扫描线程上。</summary>
    public event Action? EnemyPanelSeen;
    /// <summary>宿主给：屏幕上这块被助手自己的窗口（侧栏）盖着时返回 true，探针就不截。</summary>
    public Func<Rectangle, bool>? ProbeBlocked { get; set; }

    private int _manualFieldRequests;
    private bool ManualWaiting => Volatile.Read(ref _manualFieldRequests) != 0;

    // 与定时扫描共用占用标志，当前帧的商店、资金和棋盘判定不会与扫描交错。
    // capture：宿主给的截图方法（点助手里的「识别」时前台是助手，要和开局识别一样允许并在需要时先藏起侧栏）
    public async Task<FieldState> ReadCurrentFieldAsync(Func<Task<byte[]?>> capture,
        Func<byte[], DateTimeOffset, EconomyReading?, bool, bool, IReadOnlyList<ShopItem>, Task<FieldState>> recognize)
    {
        Interlocked.Increment(ref _manualFieldRequests);
        using (FieldRecognitionTiming.Measure("scanWait"))
            await _scanGate.WaitAsync();
        try
        {
            byte[]? frame;
            using (FieldRecognitionTiming.Measure("capture")) frame = await capture();
            if (frame is null) throw new InvalidOperationException("还没连上游戏");
            return await ReadFieldFrameAsync(frame, DateTimeOffset.UtcNow, recognize);
        }
        finally
        {
            Interlocked.Decrement(ref _manualFieldRequests);
            _scanGate.Release();
        }
    }

    public async Task<FieldState> ReadFieldFrameAsync(byte[] frame, DateTimeOffset at,
        Func<byte[], DateTimeOffset, EconomyReading?, bool, bool, IReadOnlyList<ShopItem>, Task<FieldState>> recognize)
    {
        CollectionFrame?.Invoke(frame, at);
        var (width, height) = GetFrameDimensions(frame);
        (bool Obscured, bool Detail, bool ConfirmationCandidate) surface;
        using (FieldRecognitionTiming.Measure("surface")) surface = FieldReader.InspectSurface(frame);
        if (surface.Obscured || surface.Detail)
            throw new InvalidOperationException("棋盘被面板挡住了，关掉面板再点");
        if (surface.ConfirmationCandidate)
        {
            double scale = Math.Min(width / 1920d, height / 1080d);
            var text = await _ocrEngine.RecognizeRegionAsync(frame, (int)(650 * scale), (int)(400 * scale),
                (int)(620 * scale), (int)(150 * scale));
            if (text.Any(t => t.Text.Replace(" ", "").Contains("确认准备", StringComparison.Ordinal)))
                throw new InvalidOperationException("棋盘被面板挡住了，关掉面板再点");
        }
        var boardSurface = new BoardSurfaceReader(_ocrEngine);
        bool main;
        using (FieldRecognitionTiming.Measure("mainScreen"))
            main = await boardSurface.IsMainScreenAsync(frame, width, height);
        EconomyReading economy;
        var economyReader = new EconomyReader(_ocrEngine, _shopReader.Theme);
        using (FieldRecognitionTiming.Measure("economy"))
            economy = await economyReader.ReadFieldAsync(frame, width, height, main);
        // 就绪区域可能只截到「资金不足」的后两个字；此时休整信号只能来自回合栏。
        if (!main && economy.IsRest && boardSurface.RawText.Replace(" ", "") is "不足" or "资金不足")
            main = true;
        if (!economy.IsRest || !main)
        {
            string detail = $"IsRest={economy.IsRest} main={main} round={economy.Round?.ToString() ?? "null"} finalRound={economy.IsFinalRound} roundOCR={economyReader.RoundRawText} readyOCR={boardSurface.RawText}";
            AppLogger.Warn("Field", detail);
            // 复用失败现场通道，保存本次手动识别的帧，不受路线或采集开关影响。
            RecognitionFailed?.Invoke(frame, "现在不在休整期的棋盘画面", null, [detail]);
            throw new InvalidOperationException("现在不在休整期的棋盘画面，先关掉干员详情等面板、回到棋盘再点");
        }
        // 手动认人只需卡身遮挡范围，不读商店文字，也不沿用上一帧卡片。
        using var stream = new MemoryStream(frame);
        using var bitmap = new System.Drawing.Bitmap(stream);
        double uiScale = GameUiScale.Shop(bitmap);
        bool visible = ShopLayout.HasCards(bitmap, uiScale, _shopReader.Theme);
        var cards = visible ? Enumerable.Range(0, 6).Select(slot =>
        {
            var r = ShopLayout.Card(slot, width, height, uiScale, _shopReader.Theme);
            return new ShopItem { SlotIndex = slot, CardRegion = (r.X / width, r.Y / height, r.Width / width, r.Height / height) };
        }).ToArray() : [];
        return await recognize(frame, at, economy, main, visible, cards);
    }

    public void Start()
    {
        _scanTimer.Start();
        _probeTimer.Start();
    }

    // 2026-11-11：StrongholdAssistant 录入及关闭前等待检测释放 OCR。
    public async Task WaitForIdleAsync()
    {
        await _scanGate.WaitAsync();
        _scanGate.Release();
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
        // 已排队的手动请求先取新帧，避免被下一轮定时扫描抢占。
        if (ManualWaiting) return;
        // Skip if a previous scan is still running.
        if (!_scanGate.Wait(0))
            return;

        // 普通扫描结束后再等一整个间隔。敌人面板保留原来的周期快扫。
        lock (_scanTimer)
        {
            if (_disposed) { _scanGate.Release(); return; }
            if (!_enemyPanelFast) _scanTimer.Stop();
        }
        byte[]? screenshot = null;
        _trace.Clear();
        try
        {
            var clock = Stopwatch.StartNew();
            var capturedAt = _refreshWindow.CaptureStarted();
            screenshot = _captureService.CaptureScreen(scanPixels: CollectionFrame is null);
            double captureMs = clock.Elapsed.TotalMilliseconds;
            if (screenshot == null)
            {
                _lastEconomy = null;
                SetIdleInterval(2000);
                _themeTracker.Observe(null);
                BoardFrameReady?.Invoke(null, null);
                InvalidateShop();
                _informationState.Suspend();
                PublishShopItems([], false, capturedAt);
                return;
            }

            // 所有坐标均基于缩放后的工作帧。
            var (imgWidth, imgHeight) = GetFrameDimensions(screenshot);
            if (imgWidth <= 0 || imgHeight <= 0)
                return;

            await ScanFrameAsync(screenshot, imgWidth, imgHeight, capturedAt, captureMs, null, 0);
        }
        catch (Exception ex)
        {
            // Swallow unexpected errors (e.g. capture failure during window state change)
            // so the timer-driven async void method never propagates an unhandled exception.
            AppLogger.Error("OcrScanner", "Scan tick failed", ex);
            RecognitionFailed?.Invoke(screenshot is null ? null : ScreenCaptureService.AsPng(screenshot), "商店识别失败。\n" + ex.Message, _shopReader.UiScale, _trace.Steps);
        }
        finally
        {
            lock (_scanTimer)
            {
                if (!_disposed)
                {
                    if (!_enemyPanelFast) _scanTimer.Stop();
                    _scanTimer.Start();
                }
                _scanGate.Release();
            }
        }
    }

    // 样图测试直接调用这一轮流程；截图仍只由计时器负责。
    private Task ScanFrameAsync(byte[] screenshot, int width, int height, DateTimeOffset capturedAt, double captureMs,
        ShopFrameFingerprint? fingerprint, double fingerprintMs)
        => CollectionFrame is null
            ? ScanFrameCoreAsync(screenshot, width, height, capturedAt, captureMs, fingerprint, fingerprintMs)
            : ScanCollectedFrameAsync(screenshot, width, height, capturedAt, captureMs, fingerprint, fingerprintMs);

    private async Task ScanCollectedFrameAsync(byte[] screenshot, int width, int height, DateTimeOffset capturedAt, double captureMs,
        ShopFrameFingerprint? fingerprint, double fingerprintMs)
    {
        CollectionFrame?.Invoke(screenshot, capturedAt);
        try { await ScanFrameCoreAsync(screenshot, width, height, capturedAt, captureMs, fingerprint, fingerprintMs); }
        finally { CollectionScanCompleted?.Invoke(capturedAt); }
    }

    private async Task ScanFrameCoreAsync(byte[] screenshot, int width, int height, DateTimeOffset capturedAt, double captureMs,
        ShopFrameFingerprint? fingerprint, double fingerprintMs)
    {
        lock (_shopGate)
            if (!_refreshWindow.Accept(capturedAt)) return;
        using var stream = new MemoryStream(screenshot);
        using var bitmap = new Bitmap(stream);
        bool panel = RoundEnemyReader.PanelVisible(bitmap);
        if (await ReadEnemyPanelAsync(screenshot, width, height, panel)) return;
        if (ManualWaiting) return;
        var theme = _shopReader.Theme;
        var key = (width, height, theme);
        if (!panel && !_banScreenActive && !_informationState.IsConsumed && !AwaitingDetail()
            && ScanHudPixels.ModeMenu(bitmap))
        {
            _combatRound = null;
            _fingerprint = null;
            _shopReader.ClearFrame();
            BoardMainScreenVisible = BoardDetailVisible = false;
            _boardSurfacePixels = null;
            _themeTracker.Observe(null);
            _lastEconomy = new(null, null, null, null, false);
            EconomyRead?.Invoke(_lastEconomy);
            BoardFrameReady?.Invoke(null, null);
            PublishShopItems([], false, capturedAt);
            SetIdleInterval(2000);
            return;
        }
        var previousRound = _combatRound;
        _combatRound = ScanHudPixels.CombatRound(bitmap, out bool hasRoundBadge);
        bool phaseChecked = false;
        bool knownCombat = key == _frameKey && _economyReader is not null && _lastEconomy is { Round: not null, IsRest: false };
        bool checkCombat = !panel && (knownCombat || _combatRound is not null) && !_banScreenActive && !_informationState.IsConsumed && !AwaitingDetail()
            && !_themeTracker.AwaitingConfirmation && !BoardShopVisible;
        var fieldSurface = checkCombat && ObserveFieldAsync is not null ? FieldReader.InspectSurface(bitmap) : default;
        if (checkCombat && !fieldSurface.Obscured && !fieldSurface.Detail && !fieldSurface.ConfirmationCandidate)
        {
            var phaseClock = Stopwatch.StartNew();
            EconomyReading phase;
            if (knownCombat && _combatRound is not null && previousRound is not null && ScanHudPixels.SameRound(_combatRound, previousRound))
            {
                phase = _lastEconomy! with { Money = null, Level = null, UpgradeCost = null, UpgradeButton = null };
            }
            else
            {
                await ReadBoardSurfaceAsync(screenshot, bitmap);
                if (ManualWaiting) return;
                phaseChecked = true;
                if (key != _frameKey || _economyReader is null) _economyReader = new EconomyReader(_ocrEngine, theme);
                phase = await _economyReader.ReadAsync(screenshot, bitmap, _shopReader.UiScale, false, BoardMainScreenVisible);
            }
            FieldRecognitionTiming.Active?.Record("phase", phaseClock.Elapsed.TotalMilliseconds);
            if (phase is { Round: not null, IsRest: false })
            {
                if (!knownCombat)
                {
                    _frameKey = key;
                    _fingerprint = null;
                    var combatTheme = await _themeDetector.ReadAsync(screenshot, bitmap);
                    if (_themeTracker.Observe(combatTheme)) ThemeDetected?.Invoke(combatTheme!);
                }
                SetIdleInterval(1200);
                BoardDetailVisible = false;
                _lastEconomy = phase;
                EconomyRead?.Invoke(phase);
                BoardFrameReady?.Invoke(screenshot, phase);
                if (!ManualWaiting && ObserveFieldAsync is { } field)
                    using (FieldRecognitionTiming.Measure("observeField"))
                        await field(screenshot, capturedAt, phase, false, false, []);
                if (!ManualWaiting && ObserveMatchFrameAsync is { } match)
                    using (FieldRecognitionTiming.Measure("observeMatch"))
                        await match(screenshot, width, height, phase, _shopReader.UiScale ?? 1);
                return;
            }
        }
        // 开局完整页可能只停一帧，先于棋盘遮挡判定。
        var informationClock = Stopwatch.StartNew();
        bool checkOpening = !hasRoundBadge || _banScreenActive || _informationState.IsConsumed || AwaitingDetail();
        bool opening = checkOpening && await DetectBanScreenAsync(screenshot, width, height, bitmap);
        double informationMs = informationClock.Elapsed.TotalMilliseconds;
        FieldRecognitionTiming.Active?.Record("information", informationMs);
        if (opening || _pauseBanScreenDetection)
        {
            _lastEconomy = null;
            BoardMainScreenVisible = false;
            _boardSurfacePixels = null;
            UpdateScanInterval(false);
            BoardFrameReady?.Invoke(null, null);
            PublishShopItems([], false, capturedAt);
            return;
        }
        if (ManualWaiting) return;
        if (!checkCombat && ObserveFieldAsync is not null) fieldSurface = FieldReader.InspectSurface(bitmap);
        BoardDetailVisible = fieldSurface.Detail;
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
        if (ManualWaiting) return;
        int version = Volatile.Read(ref _cacheVersion);
        var tracked = _getTrackedNames().ToArray();
        if (key != _frameKey) _economyReader = null;
        bool forced = version != _readVersion || key != _frameKey || !tracked.SequenceEqual(_trackedNames);
        var clock = Stopwatch.StartNew();
        fingerprint ??= ShopFrameFingerprint.Read(bitmap, theme);
        fingerprintMs += clock.Elapsed.TotalMilliseconds;
        FieldRecognitionTiming.Active?.Record("fingerprint", fingerprintMs);
        bool changed = _shopReader.NeedsEmptyConfirmation || forced || _fingerprint is null || !ShopFrameFingerprint.SameNames(_fingerprint.Names, fingerprint.Names);
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
        double? shopMs = null, economyMs = null;
        if (changed)
        {
            clock.Restart();
            using (FieldRecognitionTiming.Measure("shop"))
                _cachedItems = await _shopReader.ReadAsync(screenshot, width, height, tracked, bitmap);
            shopMs = clock.Elapsed.TotalMilliseconds;
            _fingerprint = fingerprint;
            _readVersion = version;
            _frameKey = key;
            _trackedNames = tracked;
        }
        clock.Restart();
        PublishShopItems(_cachedItems, _shopReader.HasVisibleCards, capturedAt);
        double publishMs = clock.Elapsed.TotalMilliseconds;
        FieldRecognitionTiming.Active?.Record("publishShop", publishMs);
        double captureToPublish = (DateTimeOffset.UtcNow - capturedAt).TotalMilliseconds;
        if (changed && _shopReader.Failure is { } failure)
            RecognitionFailed?.Invoke(ScreenCaptureService.AsPng(screenshot), failure, _shopReader.UiScale, _trace.Steps);

        // 商店先发布。确认新主题后宿主同步更新设置；本帧后续读数和下一帧商店用新主题。
        if (ManualWaiting) return;
        var detected = await _themeDetector.ReadAsync(screenshot, bitmap);
        if (_themeTracker.Observe(detected))
        {
            AppLogger.Info("GameTheme", $"theme={detected!.Theme} score={detected.Score:F4}");
            ThemeDetected?.Invoke(detected);
        }
        if (theme != _shopReader.Theme)
        {
            theme = _shopReader.Theme;
            _economyReader = null;
        }
        if (ManualWaiting) return;

        // 每帧确认读数；各数字区域由持久化 reader 独立比较，未变不调用 OCR。
        EconomyReading? boardEconomy = _lastEconomy;
        if (EconomyRead is not null || BoardRoutesRequested() || ObserveFieldAsync is not null)
        {
            clock.Restart();
            _economyReader ??= new EconomyReader(_ocrEngine, theme);
            if (!phaseChecked) await ReadBoardSurfaceAsync(screenshot, bitmap);
            if (ManualWaiting) return;
            var reading = await _economyReader.ReadAsync(screenshot, bitmap, _shopReader.UiScale, true, BoardMainScreenVisible);
            _lastEconomy = boardEconomy = reading;
            EconomyRead?.Invoke(reading);
            economyMs = clock.Elapsed.TotalMilliseconds;
            FieldRecognitionTiming.Active?.Record("economy", economyMs.Value);
        }
        if (_pauseBanScreenDetection || _banScreenActive) PublishShopItems([], false, capturedAt);
        // 商店已先发布，棋盘识别不会推迟本轮商店结果。
        if (BoardRoutesRequested() || ObserveFieldAsync is not null)
        {
            BoardFrameReady?.Invoke(_pauseBanScreenDetection || _banScreenActive ? null : screenshot, boardEconomy);
            // 本帧就绪控件可见时，资金降频不应跳过占用变化；战斗/弹窗仍不得拿旧读数更新占用。
            var fieldEconomy = boardEconomy ?? (BoardMainScreenVisible ? _lastEconomy : null);
            if (!_banScreenActive && boardEconomy is { IsRest: false } away)
                SetIdleInterval(away.Round is null ? 2000 : 1200);
            else
                UpdateScanInterval(!_pauseBanScreenDetection && !_banScreenActive && !fieldSurface.Detail
                    && BoardMainScreenVisible && fieldEconomy is { IsRest: true });
            if (!ManualWaiting && !_pauseBanScreenDetection && !_banScreenActive && ObserveFieldAsync is { } observe && fieldEconomy is not null)
                using (FieldRecognitionTiming.Measure("observeField"))
                    await observe(screenshot, capturedAt, fieldEconomy, BoardMainScreenVisible, BoardShopVisible, _shopReader.AllItems);
        }
        if (!ManualWaiting && !_pauseBanScreenDetection && ObserveMatchFrameAsync is { } observeMatch)
            using (FieldRecognitionTiming.Measure("observeMatch"))
                await observeMatch(screenshot, width, height, boardEconomy, _shopReader.UiScale ?? 1);
        double total = captureMs + fingerprintMs + (shopMs ?? 0) + publishMs + (economyMs ?? 0) + informationMs;
        // 有卡片时至多每两秒一行；慢轮（超过 800 ms）始终记录。
        if (total >= 800 || _shopReader.HasVisibleCards && Environment.TickCount64 - _lastTimingLog >= 2000)
        {
            _lastTimingLog = Environment.TickCount64;
            static string Ms(double? value) => value is { } ms ? $"{ms:F1}ms" : "skip";
            AppLogger.Info("ShopTiming", $"capture={Ms(captureMs)} fingerprint={Ms(fingerprintMs)} shop={Ms(shopMs)} publish={Ms(publishMs)} economy={Ms(economyMs)} information={Ms(informationMs)} captureToPublish={captureToPublish:F1}ms");
        }
    }

    private void ProbeEnemyPanel()
    {
        // 只在主扫描忙、休整期（已开局）、PC 截图时；截不到就跳过这一次，不重试也不记日志
        if (_scanGate.CurrentCount != 0 || ManualWaiting || _pauseBanScreenDetection || _lastEconomy is not { IsRest: true }
            || ObserveMatchFrameAsync is null) return;
        if (Interlocked.Exchange(ref _probing, 1) != 0) return;
        try
        {
            // 侧栏盖着敌人面板那一块就不截
            if (_captureService.CaptureRegion(RoundEnemyReader.PanelStrip, ProbeBlocked, RoundEnemyReader.PanelArea) is not { } strip)
                return;
            bool visible;
            using (strip.Image) visible = RoundEnemyReader.PanelVisible(strip.Image, strip.Scale);
            if (!visible || _captureService.CaptureRegion((w, h) => new(0, 0, w, h)) is not { } full) return;
            using (full.Image) Volatile.Write(ref _pendingEnemyFrame, _captureService.EncodeCapture(full.Image));
        }
        finally { Interlocked.Exchange(ref _probing, 0); }
    }

    private void ObserveEnemyPanel(bool visible)
    {
        bool appeared = visible && !_enemyPanelOpen;
        _enemyPanelOpen = visible;
        if (!appeared) return;
        AppLogger.Info("RoundEnemy", "看到敌人面板");
        EnemyPanelSeen?.Invoke();
    }

    /// <summary>先读探针存下的面板整图；本帧是面板时只读敌人（棋盘被挡着，跳过开局页、商店和场上认人），返回 true。</summary>
    private async Task<bool> ReadEnemyPanelAsync(byte[] screenshot, int width, int height, bool panel)
    {
        var observe = ObserveMatchFrameAsync;
        bool rest = !_pauseBanScreenDetection && _lastEconomy is { IsRest: true } && observe is not null;
        if (Interlocked.Exchange(ref _pendingEnemyFrame, null) is { } pending && rest && !panel)
        {
            ObserveEnemyPanel(true);
            var (w, h) = GetFrameDimensions(pending);
            await observe!(pending, w, h, _lastEconomy, _shopReader.UiScale ?? 1);
        }
        if (!rest || !panel)
        {
            ObserveEnemyPanel(false);
            if (_enemyPanelFast) { _enemyPanelFast = false; UpdateScanInterval(_visibleRest); }
            return false;
        }
        ObserveEnemyPanel(true);
        await observe!(screenshot, width, height, _lastEconomy, _shopReader.UiScale ?? 1);
        _enemyPanelFast = true;
        _scanTimer.Interval = 150;
        return true;
    }

    public DateTimeOffset ChangedShopCapturedAt { get; private set; }
    public bool RemovingChangedShopItems { get; private set; }

    private async Task ReadBoardSurfaceAsync(byte[] frame, Bitmap image)
    {
        int width = image.Width, height = image.Height;
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
                var (w, h) = GetFrameDimensions(screenshot);
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

    // 后台传未压缩 BMP，采集、ADB 和手动截图传 PNG。
    internal static (int Width, int Height) GetFrameDimensions(byte[] png)
    {
        if (png[0] == 'B' && png[1] == 'M')
            return (BitConverter.ToInt32(png, 18), BitConverter.ToInt32(png, 22));
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

    private async Task<bool> DetectBanScreenAsync(byte[] screenshot, int width, int height, Bitmap bitmap)
    {
        if (_pauseBanScreenDetection) return false;
        bool opening = await _informationDetector.IsOpeningScreenAsync(screenshot, width, height, bitmap);
        bool gameplay = !opening && _informationState.IsConsumed
            && await _informationDetector.IsGameplayAsync(screenshot, width, height);
        if (_pauseBanScreenDetection) return false;

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
            OpeningScreenshot = ScreenCaptureService.AsPng(screenshot);
            BanScreenDetected?.Invoke(solo);
        }
        if (!opening && AwaitingDetail()
            && await _informationDetector.IsInformationScreenAsync(screenshot, width, height))
            DetailScreenDetected?.Invoke();
        return opening;
    }

    public void Dispose()
    {
        lock (_scanTimer)
        {
            _disposed = true;
            _scanTimer.Dispose();
        }
        _probeTimer.Stop();
        _probeTimer.Dispose();
        _manualScanChannel.Writer.TryComplete();
    }
}
