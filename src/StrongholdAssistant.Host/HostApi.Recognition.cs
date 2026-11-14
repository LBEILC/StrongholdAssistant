using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private BanScreenAnalyzer _analyzer = null!;
    private InformationScreenDetector _informationDetector = null!;
    private BanAvatarRecognizer _avatars = null!;
    private EnemyRecognizer _enemyRecognizer = null!;
    private Func<Task<byte[]?>> _captureInformation = null!;
    internal Func<Task<(MatchInfo Match, string[] Avatars)>> ReadInformation { get; set; } = null!;
    internal Func<Task<GameModeInfo?>> ReadOpeningMode { get; set; } = null!;
    private byte[]? _scanShot;
    private BanAvatarResult? _scanAvatars;
    private BanScreenResult? _scanResult;
    private EnemyResult? _scanEnemy;
    private string? _scanFailure;
    private bool _scanOpening;
    private bool _pendingOpening;
    private DiagnosticOcrEngine? _scanOcr;
    private double? _scanScale;
    private string _scanStep = "截取游戏画面";
    private void ScanStep(string step)
    {
        _scanStep = step;
        ASPAssistant.Core.AppLogger.Info("Information", step);
    }
    private readonly BossDataService _bosses = new();
    private readonly EnemyTypeDataService _enemyTypes = new();
    public Connection Connection { get; private set; } = new("searching");
    public event Action<string, object>? Changed;

    public async Task InitializeRecognitionAsync(string dataDirectory, IOcrEngine ocr, Func<Task<byte[]?>> capture)
    {
        _scanOcr = new DiagnosticOcrEngine(ocr);
        ocr = _scanOcr;
        await Task.WhenAll(_bosses.LoadAsync(dataDirectory), _enemyTypes.LoadAsync(dataDirectory));
        _avatars = await Task.Run(() => new BanAvatarRecognizer(ocr, _operators,
            Path.Combine(dataDirectory, "operator_avatars")));
        var strategy = new GarrisonOcrStrategy();
        _informationDetector = new InformationScreenDetector(ocr, strategy);
        _captureInformation = capture;
        ReadInformation = ReadInformationAsync;
        ReadOpeningMode = ReadOpeningModeAsync;
        _enemyRecognizer = new EnemyRecognizer(ocr, strategy, _bosses);
        _analyzer = new BanScreenAnalyzer(() => Task.FromResult(_scanShot), ocr, strategy,
            _enemyRecognizer, _operators,
            _covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToArray(),
            _covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToArray());
        _analyzer.AnalysisCompleted += result => _scanResult = result;
        _analyzer.EnemyDetected += enemy => _scanEnemy = enemy;
        _analyzer.AnalysisFailed += message => _scanFailure = message;
    }

    public void UpdateConnection(Connection connection)
    {
        connection = connection with { Theme = connection.State == "connected" ? _detectedTheme : null };
        if (Connection == connection) return;
        Connection = connection;
        if (connection.State != "connected") ObserveBoardFrame(null, null);
        if (connection.State != "connected")
            lock (_historyGate)
            {
                ResetBoardRound();
                _observedRound = null;
                _pendingEconomyRound = null;
                _pendingStartMoney = null;
                _openingObserved = false;
                _canReadStart = false;
            }
        Changed?.Invoke("connection", connection);
    }

    public Task<MatchInfo> RecordInformationAsync()
    {
        if (IsContinuous) throw new RecognitionException("正在持续识别，请先停止。");
        return RecordInformationAsync(false);
    }

    private async Task<MatchInfo> RecordInformationAsync(bool continuous, bool opening = false)
    {
        if (continuous) await _scanGate.WaitAsync();
        else if (!_scanGate.Wait(0)) throw new RecognitionException("正在识别，请稍候。");
        try
        {
            Changed?.Invoke("scan", new ScanStatus(true, Continuous: continuous ? true : null));
            _scanAvatars = null;
            _scanOpening = false;
            _scanShot = null;
            _scanScale = null;
            _scanOcr?.Clear();
            ScanStep("截取游戏画面");
            var (next, avatars) = await ReadInformation();
            int added = ApplyRecognizedMatch(next, avatars, _scanAvatars,
                opening || _pendingOpening || (_scanOpening && _observedRound > 1));
            _pendingOpening = false;
            _openingObserved |= _scanOpening;
            if (continuous) Match = Match with { BansScanned = true };
            Changed?.Invoke("match", Match);
            if (!continuous)
                Changed?.Invoke("scan", new ScanStatus(false, added > 0
                    ? $"新识别到 {added} 名禁用干员，共 {Match.BannedOperators.Length} 名。"
                    : $"没有新识别到禁用干员，共 {Match.BannedOperators.Length} 名。", true));
            return Match;
        }
        catch (Exception ex)
        {
            var message = ReportRecognitionFailure(_scanShot, ex.Message, _scanScale,
                ["失败步骤: " + _scanStep, .. _scanOcr?.Steps ?? []]);
            if (!continuous) Changed?.Invoke("scan", new ScanStatus(false, message, false));
            throw new RecognitionException(message);
        }
        finally
        {
            if (continuous) Changed?.Invoke("scan", new ScanStatus(false, Continuous: true));
            _scanGate.Release();
        }
    }

    private async Task<(MatchInfo Match, string[] Avatars)> ReadInformationAsync()
    {
        _scanResult = null;
        _scanEnemy = null;
        _scanShot = null;
        _scanFailure = null;
        var mode = Match.Mode;
        _scanShot = await _captureInformation();
        if (_scanShot is not null)
        {
            int rawWidth = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(_scanShot.AsSpan(16, 4));
            int rawHeight = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(_scanShot.AsSpan(20, 4));
            var (png, width, height) = ScreenshotNormalizer.EnsureAnalysable(_scanShot, rawWidth, rawHeight);
            _scanScale = GameUiScale.Information(png);
            ScanStep($"画面 {rawWidth}×{rawHeight}; UI 缩放 {_scanScale:F3}; 检查信息页标题");
            if (await _informationDetector.IsSoloOpeningAsync(png, width, height))
                throw new RecognitionException("这是单人模式的开局页：选好盟约进入对局后，点顶部回合数左边的放大镜打开对局详情再识别。");
            _scanOpening = await _informationDetector.IsOpeningScreenAsync(png, width, height);
            if (_scanOpening)
                mode = await _informationDetector.ReadModeAsync(png, width, height, _modes);
        }
        ScanStep("定位禁用区并识别头像");
        var avatars = _scanShot == null ? null : await _avatars.RecognizeAsync(_scanShot);
        _scanAvatars = avatars;
        // 轻微滚动时盟约标题已离开画面，头像却还没进入上半区；同样按滚动页补录。
        bool supplement = avatars is { Slots.Count: > 0 }
            && (avatars.IsScrolledList || !await _informationDetector.IsInformationScreenAsync(
                _scanShot!, avatars.Width, avatars.Height));
        if (supplement)
        {
            _scanEnemy = await _enemyRecognizer.RecognizeAsync(_scanShot!, avatars!.Width, avatars.Height);
            if (_scanEnemy is null)
                _scanFailure = "未找到完整的核心/附加盟约区域，请打开本局信息页并保持面板顶部可见。";
        }
        else
        {
            ScanStep("读取核心/附加盟约标题、槽位及敌方领袖");
            await Task.Run(_analyzer.RunAnalysisAsync);
        }
        if (_scanFailure is { } failure) throw new RecognitionException(failure);

        var result = _scanResult;
        ScanStep($"识别完成: 核心 {result?.CoreCovenants.Count}; 附加 {result?.AdditionalCovenants.Count}; 禁用头像 {avatars?.Names.Length}; 首领 {_scanEnemy?.BossName}");
        var next = new MatchInfo("recognized", null,
            (result is null ? [] : result.CoreCovenants.Concat(result.AdditionalCovenants))
                .Select(c => new CovenantState(_ids[c.Name], c.IsBanned)).ToArray(),
            [], _scanEnemy is null ? null : _bosses.Lookup(_scanEnemy.BossName)?.EnemyId,
            _scanEnemy?.EnemyTypes.Select(name => _enemyTypes.LookupByTypeName(
                    name.StartsWith("特训敌人·") ? name[5..] : name)?.TypeCode)
                .OfType<string>().Distinct().ToArray() ?? [], DateTimeOffset.UtcNow.ToString("O"));
        return (next with { Mode = mode }, avatars?.Names ?? []);
    }

    internal int ApplyRecognizedMatch(MatchInfo next, IEnumerable<string> avatars, BanAvatarResult? grid = null, bool opening = false)
    {
        // 未读到的区域不参与换局比较；敌人类型按集合比较，忽略 OCR 顺序。
        bool enemyChanged = (next.BossId is not null && next.BossId != Match.BossId)
            || (next.EnemyTypes.Length > 0 && Match.EnemyTypes.Length > 0
                && !next.EnemyTypes.ToHashSet().SetEquals(Match.EnemyTypes));
        var currentCovenants = Match.Covenants.ToDictionary(c => c.Id);
        // 滚动只会减少可见盟约；仅新增盟约或已读状态变化才参与换局判断。
        bool covenantsChanged = currentCovenants.Count > 0 && next.Covenants.Any(c =>
            !currentCovenants.TryGetValue(c.Id, out var current) || current.Incomplete != c.Incomplete);
        bool newMatch = opening || enemyChanged || covenantsChanged;
        var previous = newMatch ? new HashSet<string>() : Match.BannedOperators.ToHashSet();
        if (newMatch)
        {
            lock (_historyGate) CompleteHistory();
            _avatarBans.Clear();
            _manualBans.Clear();
            _banGroups.Clear();
        }
        if (grid is null) _avatarBans.UnionWith(avatars);
        else MergeBanGroups(grid);
        Match = next with
        {
            Covenants = newMatch ? next.Covenants : Match.Covenants.Concat(next.Covenants)
                .GroupBy(c => c.Id).Select(g => g.Last()).ToArray(),
            BossId = next.BossId ?? (newMatch ? null : Match.BossId),
            EnemyTypes = next.EnemyTypes.Length > 0 ? next.EnemyTypes : newMatch ? [] : Match.EnemyTypes,
            StrategyId = newMatch ? null : Match.StrategyId,
            // 单人开局清空后首次录入详情，保留刚确认的单人模式。
            Solo = opening || (newMatch && Match.RecognizedAt is not null) ? false : Match.Solo,
            AwaitingDetail = false,
            Economy = newMatch ? null : Match.Economy,
            BansScanned = !newMatch && Match.BansScanned
        };
        RecomputeBans();
        Match = Match with { Source = "recognized", RecognizedAt = DateTimeOffset.UtcNow.ToString("O") };
        RecordRecognizedMatch(newMatch, opening);
        return Match.BannedOperators.Except(previous).Count();
    }

    public async Task WaitForRecordingAsync()
    {
        await _scanGate.WaitAsync();
        _scanGate.Release();
    }

    public async Task HandleOpeningAsync(bool solo)
    {
        if (IsContinuous)
        {
            if (!solo)
            {
                _pendingOpening = true;
                return;
            }
            await StopContinuousAsync();
        }
        if (solo)
        {
            lock (_historyGate) CompleteHistory();
            await ResetMatchAsync(true);
            Match = Match with { Mode = await ReadOpeningMode() };
            Changed?.Invoke("match", Match);
            return;
        }
        Match = Match with { Solo = false, AwaitingDetail = false, Mode = null };
        Changed?.Invoke("match", Match);
        await RecordInformationAsync(false, opening: true);
    }

    public Task HandleDetailAsync()
    {
        if (!Match.AwaitingDetail) return Task.CompletedTask;
        Match = Match with { AwaitingDetail = false };
        Changed?.Invoke("match", Match);
        return ScanContinuousAsync(true);
    }

    private async Task<GameModeInfo?> ReadOpeningModeAsync()
    {
        var shot = await _captureInformation();
        if (shot == null) return null;
        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(shot.AsSpan(16, 4));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(shot.AsSpan(20, 4));
        var (png, w, h) = ScreenshotNormalizer.EnsureAnalysable(shot, width, height);
        return await _informationDetector.ReadModeAsync(png, w, h, _modes);
    }
}

public sealed class RecognitionException(string message) : Exception(message);
