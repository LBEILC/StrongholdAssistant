using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Microsoft.Win32;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private CollectionStore? _collections;
    private CollectionHudReader? _collectionHud;
    private string _collectionData = "";
    private readonly object _collectionGate = new();
    private byte[]? _collectionFrame;
    private DateTimeOffset _collectionAt;
    private FieldObservation? _collectionObservation;
    private sealed record ConfirmedFieldSource(byte[] Frame, DateTimeOffset At, FieldState? State);
    private ConfirmedFieldSource? _confirmedFieldSource;
    private Task<byte[]>? _collectionRaw;
    private string? _rawCollectedFor;
    private Lazy<object> _collectionAssets = null!;
    private List<object>? _collectionComparisons;
    private FieldObservation? _comparingObservation;
    public Func<object>? CollectionEnvironment { get; set; }
    public event Action? CollectionEnabledChanged;

    private void InitializeCollections(string data)
    {
        _collectionData = data;
        _collections = new(Path.Combine(DataDirectory, "collect"));
        _collectionAssets = new(() =>
        {
            string? Hash(string name)
            {
                var path = Path.Combine(_collectionData, name);
                return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() : null;
            }
            return new { model = Hash("sprite-model.onnx"), classes = Hash("sprite-model-classes.json"), templates = Hash("sprite-templates.bin.gz") };
        });
        ConfigureCollectionEnabled();
    }

    private void ConfigureCollectionEnabled()
    {
        _collections?.Enable(Settings.Collect);
        _fieldTracker.Compared = Settings.Collect ? CollectComparison : null;
        if (!Settings.Collect)
            lock (_collectionGate) { _collectionFrame = null; _collectionObservation = null; _collectionRaw = null; _confirmedFieldSource = null; }
        CollectionEnabledChanged?.Invoke();
    }

    public bool NeedsCollectionRaw => Settings.Collect && (_rawCollectedFor is null || _rawCollectedFor != _activeRecord);

    public void ObserveCollectionNative(Bitmap bitmap)
    {
        if (!NeedsCollectionRaw) return;
        var copy = (Bitmap)bitmap.Clone();
        var work = Task.Run(() =>
        {
            using (copy)
            using (var stream = new MemoryStream())
            { copy.Save(stream, ImageFormat.Png); return stream.ToArray(); }
        });
        lock (_collectionGate) _collectionRaw = work;
    }

    // 每次扫描入口都会经过这里，包括尚不能读出阶段的画面。
    public void ObserveCollectionFrame(byte[] frame, DateTimeOffset at)
    {
        if (!Settings.Collect) return;
        lock (_collectionGate)
        { _collectionFrame = frame; _collectionAt = at; _collectionObservation = null; }

    }

    public async Task CompleteCollectionScan(DateTimeOffset at)
    {
        if (!Settings.Collect) return;
        byte[]? frame; FieldObservation? observation;
        lock (_collectionGate)
        { frame = _collectionAt == at ? _collectionFrame : null; observation = _collectionObservation; }
        var id = _activeRecord;
        var result = frame is not null && id is not null && _collectionHud is not null &&
            observation is not { IsRest: true, MainScreen: true }
            ? await _collectionHud.ReadResult(frame) : null;
        if (id != _activeRecord) return;
        Collect(at, batch =>
        {
            if (result is not null) batch.Settlement(result);
            if (observation?.At != at) batch.Log("scan", new { observed = false });
            if (frame is not null) batch.Timer(frame);
        });
    }

    internal void CollectEnemyPanel(byte[] frame, EconomyReading reading)
    {
        int? round = reading.IsFinalRound ? _boardRouteStore?.LastRound(Match.Solo, Match.Mode?.Id) : reading.Round;
        if (round is null || round != _acceptedBoardRound) return;
        var enemies = Match.RoundEnemies?.Round == round ? Match.RoundEnemies.Enemies : null;
        Collect(DateTimeOffset.UtcNow, b => b.Panel(round.Value, frame, enemies));
    }

    private void Collect(DateTimeOffset at, Action<CollectionStore.Batch> write)
    {
        if (!Settings.Collect || _activeRecord is not { } id) return;
        var record = _history.FirstOrDefault(r => r.Id == id);
        if (record is null) return;
        var match = Match;
        var roster = ReadRoster();
        var environment = CollectionEnvironment?.Invoke();
        // 哈希与 CPU 查询延迟到后台；上下文只带明确允许的字段，不序列化设置或账号。
        var context = new CollectionContext(
            typeof(HostApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
            _collectionAssets, environment, Settings.GameTheme, match.Mode, match.Solo, match.Covenants.ToArray(),
            match.BannedOperators.ToArray(), match.StrategyId, match.CustomPicks, match.BossId,
            roster.ReadAt is null ? null : roster.Operators.Select(o => new { o.Name, o.Skin }).ToArray(), _shopReader?.UiScale ?? _scanScale);
        _collections!.Add(id, record.Time, match.Mode?.Name, at, context, write);
    }

    private sealed record CollectionContext(string ProductVersion, [property: System.Text.Json.Serialization.JsonIgnore] Lazy<object> Assets, object? Environment,
        string Theme, GameModeInfo? Mode, bool Solo, CovenantState[] Covenants, string[] BannedOperators,
        string? StrategyId, Dictionary<int, string[]>? CustomPicks, string? BossId, object[]? Skins, double? GameUiScale)
    {
        public object AssetHashes => Assets.Value;
        public string? Cpu => CollectionCpu.Value;
    }
    private static readonly Lazy<string?> CollectionCpu = new(() =>
        Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string);

    private void CollectComparison(FieldSpritePatch patch, SpriteCandidate[] templates, SpriteCandidate[] fused, float[]? logits)
    {
        if (_collectionComparisons is null) return;
        var cell = _comparingObservation?.Sprites?.FirstOrDefault(p => ReferenceEquals(p.Value, patch)).Key;
        var probabilities = logits is null ? null : _fieldSpriteModel!.Probabilities(logits, templates);
        _collectionComparisons.Add(new
        {
            cell, geometry = patch.Geometry,
            candidates = fused.OrderByDescending(c => c.Score).Take(5).Select(c =>
            {
                int i = Array.FindIndex(templates, t => t.Name == c.Name);
                return new { name = c.Name, template = templates[i].Score, model = probabilities?[i], fused = c.Score };
            }).ToArray()
        });
    }

    private sealed record RestSample(int Round, RoundVitals Vitals, MatchInfo Match, IReadOnlyDictionary<int, string>? Shop);

    private async Task<RestSample?> ReadCollectionRest(byte[] frame, EconomyReading? reading,
        bool shopVisible, IReadOnlyList<ShopItem> shop, MatchInfo match)
    {
        if (reading is null || _collectionHud is null || _activeRecord is null) return null;
        int? round = reading.IsFinalRound && match.Mode is not null
            ? _boardRouteStore?.LastRound(match.Solo, match.Mode.Id) : reading.Round;
        // 顶栏文字可能把“合”的右侧笔画读成十位数；只在有分歧时单读青色数字牌。
        if (round != _acceptedBoardRound) round = await _collectionHud.ReadRoundBadge(frame);
        if (round is not (>= 1 and <= 20) || round != _acceptedBoardRound) return null;
        var vitals = await _collectionHud.ReadVitals(frame, reading, Theme(Settings.GameTheme));
        Dictionary<int, string>? cards = null;
        if (shopVisible)
        {
            // 场上读取器可能保留上回合的卡片；开店快照只取商店本帧读出的卡。
            cards = shop.GroupBy(i => i.SlotIndex).ToDictionary(g => g.Key, g => g.First().Name);
        }
        return new(round.Value, vitals, match, cards);
    }

    private void CollectObservation(byte[] frame, FieldObservation observation, FieldState? before, FieldState? after,
        object[] comparisons, bool force, double milliseconds, RestSample? rest)
    {
        if (!Settings.Collect) return;
        Task<byte[]>? raw = null;
        lock (_collectionGate)
        {
            _collectionFrame = frame; _collectionAt = observation.At; _collectionObservation = observation;
            if (force && observation is { IsRest: true, MainScreen: true })
                _confirmedFieldSource = new(frame, observation.At, after);
            if (observation is { IsRest: true, MainScreen: true } && NeedsCollectionRaw)
            {
                raw = _collectionRaw ?? Task.FromResult(frame);
                _rawCollectedFor = _activeRecord;
                _collectionRaw = null;
            }
        }
        var added = after?.Log.Except(before?.Log ?? []).ToArray() ?? [];
        var units = rest is null ? null : _fieldTracker.SnapshotUnits();
        var crops = observation.Sprites?.Where(p => after?.Units.Any(u => u.Cell == p.Key) == true
            && (before?.Units.FirstOrDefault(u => u.Cell == p.Key) is not { } old
                || after.Units.First(u => u.Cell == p.Key) is var next && (old.Operator != next.Operator || old.Tier != next.Tier)
                || added.Any(e => e.To == p.Key && e.Kind is "move" or "buy" or "merge"))).ToArray() ?? [];
        Collect(observation.At, batch =>
        {
            batch.Round(observation.Round);
            if (rest is not null)
                batch.RestFrame(rest.Round, rest.Vitals, rest.Match, observation, rest.Shop, units, frame);
            else if (force && after is not null)
                batch.ManualSnapshot(observation.Round, after.Units);
            batch.Log("scan", new
            {
                observed = true, observation.IsRest, observation.MainScreen, observation.Round, observation.MapId,
                cells = observation.Cells.Select(p => new { cell = p.Key, tier = p.Value }).ToArray(),
                observation.Shop, observation.Money, observation.DragOperator, observation.PanelOperator, observation.Equipment,
                observation.GainSource,
                geometry = observation.Geometry is { } g ? new { g.Camera, g.PlayerSide, g.BossRound, g.BoardMatched, g.Corners,
                    benchFeet = g.BenchFeet.Select(p => new { cell = p.Key, foot = p.Value }).ToArray() } : null,
                feet = observation.Sprites?.Select(p => new { cell = p.Key, geometry = p.Value.Geometry }).ToArray()
            });
            foreach (var comparison in comparisons) batch.Log("comparison", comparison);
            foreach (var entry in added) batch.Log("event", entry);
            batch.Log("decisions", new { units = after?.Units });
            batch.Observe(frame, observation.IsRest, observation.MainScreen);
            if (!observation.IsRest && observation.Round.HasValue && batch.HasPendingRound) batch.FlushRound();
            if (raw is not null) batch.Image(raw.GetAwaiter().GetResult(), "first-rest", "raw", once: true);
            if (force)
                batch.Log("recognizeFrame", new { milliseconds, result = after });
            if (observation.PanelOperator is { } panel)
            {
                var cells = after?.Units.Where(u => u.Operator == panel).Select(u => CellKey(u.Cell)).ToArray() ?? [];
                if (cells.Length != 1) cells = []; // 面板本身没有格坐标，同名多格不能伪造位置答案。
                batch.Answers(cells);
                batch.Log("panel", new { name = panel, observation.Equipment, cells, frame = batch.Image(frame, "panel") });
            }
            foreach (var crop in crops)
                batch.Log("crop", new { cell = crop.Key, geometry = crop.Value.Geometry,
                    file = batch.Image(crop.Value.Image, $"{CellKey(crop.Key)}", "crops") });
        });
    }

    private void CollectRecognition(double milliseconds, FieldState? result, string? error, FieldRecognitionTiming timing)
    {
        byte[]? frame; DateTimeOffset frameAt;
        lock (_collectionGate) { frame = _collectionFrame; frameAt = _collectionAt; }
        Collect(DateTimeOffset.UtcNow, b => b.Log("recognize", new
        {
            milliseconds, stages = timing.Snapshot(), requestId = timing.RequestId, phase = "names", result, error, frameAt,
            frame = frame is null ? null : b.Image(frame, "recognize", capturedAt: frameAt)
        }));
    }

    private static string CellKey(FieldCell cell) => $"{cell.Zone}-{cell.Col}-{cell.Row}";

    internal void InvalidateCollectionFrame()
    {
        lock (_collectionGate) { _collectionObservation = null; _confirmedFieldSource = null; }
    }

    public void ConfirmField()
    {
        if (!Settings.Collect) throw new RecognitionException("先打开设置里的「帮助改进识别」");
        ConfirmedFieldSource? source;
        lock (_collectionGate)
        {
            source = _confirmedFieldSource;
            if (source is null)
                throw new RecognitionException("现在不在休整期的棋盘画面，先关掉干员详情等面板、回到棋盘再点");
        }
        CollectAnswer("confirmField", null, null, null, source.State, source);
    }

    private void CollectAnswer(string kind, FieldCell? cell, string? from, string? to, FieldState? state, ConfirmedFieldSource? source = null)
    {
        if (!Settings.Collect) return;
        byte[]? frame; DateTimeOffset at;
        lock (_collectionGate) { frame = source?.Frame ?? _collectionFrame; at = source?.At ?? _collectionAt; }
        if (frame is null) return;
        Collect(DateTimeOffset.UtcNow, batch =>
        {
            batch.ConfirmUnits(cell, state, kind == "confirmField");
            batch.Answers(cell is not null ? [CellKey(cell)] : state?.Units.Select(u => CellKey(u.Cell)) ?? []);
            batch.Log(kind, new { cell, from, to, state, frameAt = at, frame = batch.Image(frame, "answer", capturedAt: at) });
        });
    }

    private void CollectOpening(byte[]? frame, string reason)
    {
        if (frame is null || !Settings.Collect) return;
        DateTimeOffset at;
        lock (_collectionGate) at = ReferenceEquals(frame, _collectionFrame) ? _collectionAt : DateTimeOffset.UtcNow;
        Collect(at, b => b.Image(frame, reason, once: true));
    }

    public Task<CollectionMatch[]> CollectionsAsync() => _collections!.ListAsync();
    public Task DrainCollectionsAsync() => _collections!.DrainAsync();
    public Task ClearCollectionsAsync()
    {
        lock (_collectionGate)
        {
            _rawCollectedFor = null; _collectionRaw = null;
            _collectionFrame = null; _collectionObservation = null;
            _confirmedFieldSource = null;
        }
        return _collections!.ClearAsync();
    }
    public async Task FinishCollectionsAsync()
    {
        _collections!.End(_activeRecord);
        await _collections.DrainAsync();
    }

    // 验收可直接指定路径，产品入口仍使用保存对话框。
    internal Task ExportCollectionToAsync(string id, string path) => _collections!.ExportAsync(id, path);
    public async Task<string?> ExportCollectionAsync(string id)
    {
        var match = (await CollectionsAsync()).Single(m => m.Id == id);
        var dialog = new SaveFileDialog
        {
            Filter = "ZIP (*.zip)|*.zip", DefaultExt = ".zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            FileName = $"高效雅雅-采集-{DateTimeOffset.Parse(match.StartedAt).LocalDateTime:yyyyMMdd-HHmm}.zip"
        };
        if (dialog.ShowDialog(DialogOwner) != true) return null;
        await ExportCollectionToAsync(id, dialog.FileName);
        return dialog.FileName;
    }
}
