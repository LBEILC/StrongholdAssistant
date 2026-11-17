using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private readonly object _fieldGate = new();
    private readonly SemaphoreSlim _fieldScanGate = new(1, 1);
    public Func<Task<FieldState>>? ReadCurrentField { get; set; }

    public async Task<FieldState> RecognizeFieldAsync()
    {
        using var timing = Settings.Collect ? new FieldRecognitionTiming() : null;
        var clock = Settings.Collect ? System.Diagnostics.Stopwatch.StartNew() : null;
        try
        {
            if (Connection.State != "connected" || ReadCurrentField is null) throw new RecognitionException("还没连上游戏");
            var result = await ReadCurrentField();
            timing?.Mark("namesReady");
            if (clock is not null) CollectRecognition(clock.Elapsed.TotalMilliseconds, result, null, timing!);
            return result;
        }
        catch (Exception e)
        {
            if (clock is not null) CollectRecognition(clock.Elapsed.TotalMilliseconds, null, e.Message, timing!);
            // 保留核心的画面校验提示；采集只旁观，不改变异常处理结果。
            if (e is InvalidOperationException && (e.Message == "还没连上游戏" || e.Message.EndsWith("再点")))
                throw new RecognitionException(e.Message);
            throw;
        }
    }

    public void PublishFieldHotkey() => Changed?.Invoke("fieldHotkey", null!);
    private readonly FieldTracker _fieldTracker = new();
    private FieldReader? _fieldReader;
    private int _fieldGeneration;
    private int _fieldReaderGeneration;
    private Task _fieldIdentityWork = Task.CompletedTask;
    private int? _fieldRestRound;

    private void ObserveFieldPhase(EconomyReading? reading)
    {
        if (reading is null) return;
        lock (_collectionGate)
        {
            if (!reading.IsRest || reading.Round is { } round && _fieldRestRound is { } previous && round != previous)
                _confirmedFieldSource = null;
            _fieldRestRound = reading.IsRest ? reading.Round ?? _fieldRestRound : null;
        }
    }
    private SpriteMatcher? _fieldSprites;
    private SpriteModelMatcher? _fieldSpriteModel;
    public event Action<FieldState?, FieldOverlayGeometry?>? FieldOverlayChanged;
    private FieldOverlayGeometry? _fieldOverlayGeometry;

    internal void HideFieldOverlay()
    {
        _fieldOverlayGeometry = null;
        FieldOverlayChanged?.Invoke(Field, null);
    }

    public Func<System.Drawing.RectangleF?> FieldCursorRegion { get; set; } = () => null;

    public FieldState? Field { get { lock (_fieldGate) return _fieldTracker.State; } }

    public void InitializeField(string directory, IOcrEngine ocr)
    {
        _fieldReader?.Dispose();
        _fieldReader = new(directory, ocr, _operators.Select(o => o.Name));
        _fieldTracker.ConfigureSummons(SummonCatalog.Load(directory));
        using var maps = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "garrison_maps.json")));
        _fieldTracker.ConfigureMaps(ASPAssistant.Core.Placement.GarrisonBoardStore.Parse(maps.RootElement));
        _fieldSprites?.Dispose();
        _fieldSpriteModel?.Dispose();
        _fieldSpriteModel = new(Path.Combine(directory, "sprite-model.onnx"), Path.Combine(directory, "sprite-model-classes.json"));
        _fieldSprites = new(Path.Combine(directory, "sprite-templates.bin.gz"));
        _fieldTracker.ConfigureIdentity(_operators, (patch, tier, names) =>
        {
            var skins = FieldAppearances();
            return _fieldSprites.MatchCellWidth(patch, tier, names, skins);
        }, _fieldSpriteModel, (patch, foreground) =>
        {
            var skins = FieldAppearances();
            return _fieldSprites.WithNeighbors(_fieldSprites.WithOcclusion(patch, foreground, skins), foreground, skins);
        }, (patch, tier, names) => _fieldSprites.MaskNeighbors(patch, tier, names,
            FieldAppearances()));
        InitializeFieldPrototypes(directory);
        _shopReader!.ReadUntracked = true;
    }

    public async Task ObserveFieldAsync(byte[] frame, DateTimeOffset at, EconomyReading? economy,
        bool mainScreen, bool shopVisible, IReadOnlyList<ShopItem> shop)
    {
        if (!await _fieldScanGate.WaitAsync(0)) return;
        try { await ReadFieldCoreAsync(frame, at, economy, mainScreen, shopVisible, shop, false); }
        finally { _fieldScanGate.Release(); }
    }

    public async Task<FieldState> RecognizeFieldFrameAsync(byte[] frame, DateTimeOffset at, EconomyReading? economy,
        bool mainScreen, bool shopVisible, IReadOnlyList<ShopItem> shop)
    {
        if (economy is not { IsRest: true } || !mainScreen)
            throw new RecognitionException("现在不在休整期的棋盘画面，先关掉干员详情等面板、回到棋盘再点");
        using (FieldRecognitionTiming.Measure("fieldWait")) await _fieldScanGate.WaitAsync();
        try
        {
            await ReadFieldCoreAsync(frame, at, economy, mainScreen, shopVisible, shop, true);
            return Field!;
        }
        finally { _fieldScanGate.Release(); }
    }

    private async Task ReadFieldCoreAsync(byte[] frame, DateTimeOffset at, EconomyReading? economy,
        bool mainScreen, bool shopVisible, IReadOnlyList<ShopItem> shop, bool force)
    {
        var collectionClock = Settings.Collect ? System.Diagnostics.Stopwatch.StartNew() : null;
        ObserveFieldPhase(economy);
        if (economy is { IsRest: true, Round: null })
            economy = economy with { Round = _fieldRestRound };
        int generation;
        lock (_fieldGate) generation = _fieldGeneration;
        if (_fieldReaderGeneration != generation) { _fieldReader!.Reset(); _fieldReaderGeneration = generation; }
        _fieldReader!.Theme = Theme(Settings.GameTheme);
        _fieldReader.KnownMap = Field?.MapId;
        _fieldReader.HasPithStrategy = Match.StrategyId is { } strategyId ? strategyId == "band_pith" : null;
        _fieldReader.BossRound = economy?.IsFinalRound == true || economy?.Round is { } round
            && _boardRouteStore?.IsBossRound(round, Match.Solo, Match.Mode?.Id) == true;
        var cursor = FieldCursorRegion();
        var observation = await _fieldReader!.ReadAsync(frame, at, economy, mainScreen, shopVisible, shop, cursor, readShop: !force);
        var match = Match;
        string? strategy = Catalog.GetProperty("strategies").EnumerateArray()
            .FirstOrDefault(s => s.GetProperty("id").GetString() == match.StrategyId) is var selected
            && selected.ValueKind == System.Text.Json.JsonValueKind.Object ? selected.GetProperty("name").GetString() : null;
        var custom = match.CustomPicks?.Values.SelectMany(v => v).ToArray() ?? [];
        // 169：问号和推荐框的锚点用这一帧读到的角标和投影，随定人结果一起推给覆盖层
        var geometry = _fieldReader.OverlayGeometry;
        _fieldIdentityWork = Task.Run(() =>
        {
            FieldState? changed = null;
            lock (_fieldGate)
            {
                if (generation != _fieldGeneration) return;
                _fieldTracker.ConfigureCandidatePool(match.BannedOperators, match.CustomPicks is null ? null : custom);
                _fieldTracker.SeedRoster(strategy, custom, observation.Round, at);
                var before = Settings.Collect ? _fieldTracker.State : null;
                _comparingObservation = observation;
                _collectionComparisons = Settings.Collect ? [] : null;
                using (FieldRecognitionTiming.Measure("tracker"))
                    if (_fieldTracker.Observe(observation, Settings.FieldAuto || force, force)) changed = _fieldTracker.State;
                if (Settings.Collect) CollectObservation(frame, observation, before, _fieldTracker.State,
                    _collectionComparisons?.ToArray() ?? [], force, collectionClock?.Elapsed.TotalMilliseconds ?? 0);
                _collectionComparisons = null; _comparingObservation = null;
                if (Settings.FieldAuto || force) _fieldOverlayGeometry = geometry;
            }
            using var publishTiming = FieldRecognitionTiming.Measure("publish");
            if (Settings.FieldAuto || force) FieldOverlayChanged?.Invoke(Field, geometry);
            if (changed is not null && (Settings.FieldAuto || force))
            {
                RecomputeTracking();
                _publishingManualField = force;
                try { Changed?.Invoke("field", changed); }
                finally { _publishingManualField = false; }
            }
            if (force) SchedulePlacementCore(force: true);
        });
        await _fieldIdentityWork;
    }

    public FieldState SetFieldOperator(FieldCell cell, string? name)
    {
        if (!(cell.Zone == "bench" && cell.Col is >= 1 and <= 10 && cell.Row == 0
            || cell.Zone == "board" && cell.Col is >= 0 and <= 6 && cell.Row is >= 0 and <= 4))
            throw new ArgumentException("格坐标无效。");
        if (name is not null && !_operators.Any(o => o.Name == name)) throw new ArgumentException("干员名无效。");
        FieldState state; bool changed;
        lock (_fieldGate)
        {
            var before = _fieldTracker.State?.Units.FirstOrDefault(u => u.Cell == cell)?.Operator;
            changed = _fieldTracker.SetOperator(cell, name, DateTimeOffset.UtcNow);
            CollectAnswer("answer", cell, before, name, _fieldTracker.State);
            state = _fieldTracker.State!;
        }
        if (changed)
        {
            RecomputeTracking();
            FieldOverlayChanged?.Invoke(state, _fieldOverlayGeometry);
            _publishingManualField = true;
            try { Changed?.Invoke("field", state); }
            finally { _publishingManualField = false; }
            SchedulePlacementCore(force: true);
        }
        return state;
    }

    private void ResetField()
    {
        InvalidateCollectionFrame();
        _fieldRestRound = null;
        ResetPlacement();
        bool hadState;
        lock (_fieldGate)
        {
            _fieldGeneration++;
            hadState = _fieldTracker.State is not null;
            _fieldTracker.Reset();
        }
        HideFieldOverlay();
        RecomputeTracking();
        // 接口事件不允许 null，用空状态通知清除；field() 仍返回未识别的 null。
        if (hadState) Changed?.Invoke("field", new FieldState(null, null, DateTimeOffset.UtcNow.ToString("O"), [], []));
    }
}
