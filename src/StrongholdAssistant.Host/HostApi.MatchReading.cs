using ASPAssistant.Core;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private MatchStrategyReader _strategyReader = null!;
    private CustomPickReader _customPickReader = null!;
    private RoundEnemyReader _roundEnemyReader = null!;
    private CovenantBarReader _covenantBarReader = null!;
    private DateTimeOffset _covenantReadAt = DateTimeOffset.MinValue;
    private (string? Match, int? Round) _enemyReadKey;
    private int _matchReadingGeneration;
    // 诊断包用：本局敌人面板弹出几次、其中读到名字几次
    private int _enemyPanelSeen, _enemyPanelRead;
    private bool _enemyPanelCounted;

    /// <summary>扫描看到敌人面板新弹出一次：清掉读取器的画面缓存，这一次一定重读。</summary>
    public void CountEnemyPanel()
    {
        _enemyPanelSeen++;
        _enemyPanelCounted = false;
        _roundEnemyReader.Reset();
    }

    internal object EnemyPanelStats => new { seen = _enemyPanelSeen, read = _enemyPanelRead };

    public void InitializeMatchReading(string data, IOcrEngine ocr)
    {
        _strategyReader = new(ocr, data, Catalog.GetProperty("strategies").EnumerateArray()
            .ToDictionary(s => s.GetProperty("id").GetString()!, s => s.GetProperty("name").GetString()!));
        _roundEnemyReader = new(ocr);
        _covenantBarReader = new(ocr, data, Catalog.GetProperty("covenants").EnumerateArray()
            .ToDictionary(c => c.GetProperty("id").GetString()!, c => c.GetProperty("name").GetString()!));
        _customPickReader = new(ocr, _operators.Where(o => o.Diy).Select(o => o.Name).ToArray());
    }

    public async Task ObserveMatchFrameAsync(byte[] frame, int width, int height, EconomyReading? reading, double uiScale)
    {
        // 战斗中盟约读数延至下次休整，保留上一条记录。
        if (reading is { IsRest: false, Round: not null }) return;
        string? match = _activeRecord;
        int generation = _matchReadingGeneration;
        if (reading is not null)
        {
            bool read;
            lock (_historyGate)
            {
                var now = DateTimeOffset.UtcNow;
                read = now - _covenantReadAt >= TimeSpan.FromSeconds(3);
                if (read) _covenantReadAt = now;
            }
            if (read)
            {
                int? round = Match.Economy?.Round;
                var result = await _covenantBarReader.ReadAsync(frame, uiScale);
                lock (_historyGate)
                {
                    if (generation == _matchReadingGeneration && match == _activeRecord && Match.Economy?.Round == round
                        && !result.Skipped && result.Items.Length > 0)
                        ApplyCovenantBar(result.Items, round);
                }
            }
        }
        // 经济扫描在非棋盘页也返回空字段对象，不能用 null 判断是否读取自选页。
        if (reading is not { IsRest: true })
        {
            var picks = await _customPickReader.ReadAsync(frame, width, height);
            if (generation == _matchReadingGeneration && match == _activeRecord && picks is { IsFinal: true })
            {
                ApplyCustomPicks(picks.Cards.Where(c => c.Tier == 5).Select(c => c.Name!).ToArray(),
                    picks.Cards.Where(c => c.Tier == 6).Select(c => c.Name!).ToArray());
                CollectOpening(frame, "info");
            }
        }
        if (Match.StrategySource is null)
        {
            string? id = reading is { IsRest: true } ? _strategyReader.ReadPortrait(frame, uiScale).Id
                : await _strategyReader.ReadSelectionAsync(frame, width, height);
            if (generation == _matchReadingGeneration && match == _activeRecord && id is not null)
            {
                ApplyRecognizedStrategy(id);
                if (reading is not { IsRest: true }) CollectOpening(frame, "info");
            }
        }
        if (reading is not { IsRest: true } || Match.Economy is not { } economy || reading.Round != economy.Round) return;
        var key = (match, (int?)economy.Round);
        if (_enemyReadKey != key) { _roundEnemyReader.Reset(); _enemyReadKey = key; }
        var names = await _roundEnemyReader.ReadAsync(frame);
        if (names is not null)
        {
            AppLogger.Info("RoundEnemy", $"敌人名单读到 {names.Length} 个：{string.Join("、", names)}");
            if (names.Length > 0 && !_enemyPanelCounted) { _enemyPanelRead++; _enemyPanelCounted = true; }
        }
        if (generation == _matchReadingGeneration && match == _activeRecord && Match.Economy?.Round == economy.Round && names is { Length: > 0 })
            ApplyRoundEnemies(economy.Round, names);
    }

    private void ResetMatchReading()
    {
        _matchReadingGeneration++;
        _enemyReadKey = default;
        _roundEnemyOrder.Clear();
        _covenantReadAt = DateTimeOffset.MinValue;
        _enemyPanelSeen = _enemyPanelRead = 0;
        Match = Match with { CovenantBar = [] };
    }

    internal void ApplyCovenantBar(CovenantBarReading[] items, int? round)
    {
        lock (_historyGate)
        {
            string at = DateTimeOffset.UtcNow.ToString("O");
            var merged = Match.CovenantBar.ToDictionary(c => c.Id);
            foreach (var item in items) merged[item.Id] = new(item.Id, item.Level, item.Layers, round, at);
            Match = Match with { CovenantBar = merged.Values.ToArray() };
            Changed?.Invoke("match", Match);
        }
    }

    internal void ApplyRecognizedStrategy(string id) => UpdateMatchStrategy(id, "recognized");

    // 每次读到可信最终状态均覆盖，空数组表示读到了但未选择。
    internal void ApplyCustomPicks(string[] tier5, string[] tier6)
    {
        lock (_historyGate)
        {
            Match = Match with { CustomPicks = new() { [5] = tier5, [6] = tier6 } };
            Changed?.Invoke("match", Match);
        }
    }

    internal void ApplyRoundEnemies(int round, IEnumerable<string> names)
    {
        lock (_historyGate)
        {
            var previous = RoundEnemiesFor(round);
            var enemies = previous.Enemies.ToList();
            var catalog = EnemyIdsByName();
            foreach (var name in names)
            {
                if (previous.Removed.Any(e => e.Name == name)) continue;
                int index = enemies.FindIndex(e => e.Name == name);
                if (index >= 0) enemies[index] = enemies[index] with { Source = "read" };
                else
                {
                    int insert = enemies.FindLastIndex(e => e.Source == "read") + 1;
                    InsertRoundEnemyOrder(enemies, insert, name);
                    enemies.Insert(insert, new(catalog.GetValueOrDefault(name), name));
                }
            }
            Match = Match with { RoundEnemies = previous with
                { At = DateTimeOffset.UtcNow.ToString("O"), Enemies = enemies.ToArray() } };
            Changed?.Invoke("match", Match);
        }
    }
}
