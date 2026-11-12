using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private BanScreenAnalyzer _analyzer = null!;
    private BanAvatarRecognizer _avatars = null!;
    private EnemyRecognizer _enemyRecognizer = null!;
    private Func<Task<byte[]?>> _captureInformation = null!;
    internal Func<Task<(MatchInfo Match, string[] Avatars)>> ReadInformation { get; set; } = null!;
    private byte[]? _scanShot;
    private BanAvatarResult? _scanAvatars;
    private BanScreenResult? _scanResult;
    private EnemyResult? _scanEnemy;
    private string? _scanFailure;
    private readonly BossDataService _bosses = new();
    private readonly EnemyTypeDataService _enemyTypes = new();
    public Connection Connection { get; private set; } = new("searching");
    public event Action<string, object>? Changed;

    public async Task InitializeRecognitionAsync(string dataDirectory, IOcrEngine ocr, Func<Task<byte[]?>> capture)
    {
        await Task.WhenAll(_bosses.LoadAsync(dataDirectory), _enemyTypes.LoadAsync(dataDirectory));
        _avatars = await Task.Run(() => new BanAvatarRecognizer(ocr, _operators,
            Path.Combine(dataDirectory, "operator_avatars")));
        var strategy = new GarrisonOcrStrategy();
        _captureInformation = capture;
        ReadInformation = ReadInformationAsync;
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
        if (Connection == connection) return;
        Connection = connection;
        Changed?.Invoke("connection", connection);
    }

    public Task<MatchInfo> RecordInformationAsync()
    {
        if (IsContinuous) throw new RecognitionException("正在持续识别，请先停止。");
        return RecordInformationAsync(false);
    }

    private async Task<MatchInfo> RecordInformationAsync(bool continuous)
    {
        if (continuous) await _scanGate.WaitAsync();
        else if (!_scanGate.Wait(0)) throw new RecognitionException("正在识别，请稍候。");
        try
        {
            Changed?.Invoke("scan", new ScanStatus(true, Continuous: continuous ? true : null));
            _scanAvatars = null;
            var (next, avatars) = await ReadInformation();
            int added = ApplyRecognizedMatch(next, avatars, _scanAvatars);
            Changed?.Invoke("match", Match);
            if (!continuous)
                Changed?.Invoke("scan", new ScanStatus(false, added > 0
                    ? $"新识别到 {added} 名禁用干员，共 {Match.BannedOperators.Length} 名。禁用列表没显示全时，点「持续识别」再往下滚动。"
                    : $"没有新识别到禁用干员，共 {Match.BannedOperators.Length} 名。", true));
            return Match;
        }
        catch (Exception ex)
        {
            if (!continuous) Changed?.Invoke("scan", new ScanStatus(false, ex.Message, false));
            throw;
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
        _scanShot = await _captureInformation();
        var avatars = _scanShot == null ? null : await _avatars.RecognizeAsync(_scanShot);
        _scanAvatars = avatars;
        // TODO(design): 盟约已滚出画面时沿用已录入的盟约；首领须仍可读且相同，无法据此区分同首领的新局。
        bool supplement = Match.RecognizedAt != null && avatars is { IsScrolledList: true };
        if (supplement)
        {
            var enemy = await _enemyRecognizer.RecognizeAsync(_scanShot!, avatars!.Width, avatars.Height);
            if (Match.BossId == null || enemy == null || _bosses.Lookup(enemy.BossName)?.EnemyId != Match.BossId)
                _scanFailure = "未找到完整的核心/附加盟约区域，请打开本局信息页并保持面板顶部可见。";
        }
        else await Task.Run(_analyzer.RunAnalysisAsync);
        if (_scanFailure is { } failure) throw new RecognitionException(failure);

        var result = _scanResult;
        var next = supplement ? Match : new MatchInfo("recognized", null,
            result!.CoreCovenants.Concat(result.AdditionalCovenants)
                .Select(c => new CovenantState(_ids[c.Name], c.IsBanned)).ToArray(),
            [], _scanEnemy is null ? null : _bosses.Lookup(_scanEnemy.BossName)?.EnemyId,
            _scanEnemy?.EnemyTypes.Select(name => _enemyTypes.LookupByTypeName(
                    name.StartsWith("特训敌人·") ? name[5..] : name)?.TypeCode)
                .OfType<string>().Distinct().ToArray() ?? [], DateTimeOffset.UtcNow.ToString("O"));
        return (next, avatars?.Names ?? []);
    }

    internal int ApplyRecognizedMatch(MatchInfo next, IEnumerable<string> avatars, BanAvatarResult? grid = null)
    {
        bool sameMatch = Match.BossId == next.BossId
            && Match.Covenants.OrderBy(c => c.Id).SequenceEqual(next.Covenants.OrderBy(c => c.Id));
        var previous = sameMatch ? Match.BannedOperators.ToHashSet() : [];
        if (!sameMatch)
        {
            _avatarBans.Clear();
            _manualBans.Clear();
            _banGroups.Clear();
        }
        if (grid is null) _avatarBans.UnionWith(avatars);
        else MergeBanGroups(grid);
        Match = next;
        RecomputeBans();
        Match = Match with { Source = "recognized", RecognizedAt = DateTimeOffset.UtcNow.ToString("O") };
        return Match.BannedOperators.Except(previous).Count();
    }

    public async Task WaitForRecordingAsync()
    {
        await _scanGate.WaitAsync();
        _scanGate.Release();
    }
}

public sealed class RecognitionException(string message) : Exception(message);
