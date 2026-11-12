using System.Text.Json;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed record RoundMoney(int Round, int StartMoney);
public sealed record LocalMatchRecord(GameModeInfo? Mode, bool Solo, string? StrategyId, CovenantState[] Covenants,
    string[] BannedOperators, string? BossId, LineupDto? Lineup, RoundMoney[] Rounds, int? LastRound, string LastSeenAt);
public sealed record MatchRecord(string Id, string Time, LocalMatchRecord? Local, SklandRecord? Skland);

public sealed partial class HostApi
{
    private readonly object _historyGate = new();
    private List<MatchRecord> _history = [];
    private string? _activeRecord;

    private void LoadHistory()
    {
        var path = Path.Combine(DataDirectory, "history.json");
        if (File.Exists(path)) _history = JsonSerializer.Deserialize<List<MatchRecord>>(File.ReadAllText(path), JsonOptions)!;
        path = Path.Combine(DataDirectory, "base-money.json");
        if (File.Exists(path)) _baseMoney = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path), JsonOptions)!;
    }

    private void SaveHistoryFile<T>(string name, T value)
    {
        Directory.CreateDirectory(DataDirectory);
        var path = Path.Combine(DataDirectory, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, JsonOptions));
        File.Move(path + ".tmp", path, true);
    }

    private void BeginHistory()
    {
        CompleteHistory();
        ClearEconomy();
        Match = Match with { StrategyId = null };
        string now = DateTimeOffset.UtcNow.ToString("O");
        _activeRecord = Guid.NewGuid().ToString("N");
        // 序列化复制快照，之后编辑目标阵容不会修改历史。
        var lineup = _trackingTarget is null ? null : JsonSerializer.Deserialize<LineupDto>(JsonSerializer.Serialize(_trackingTarget, JsonOptions), JsonOptions);
        _history.Add(new(_activeRecord, now, new(Match.Mode, Match.Solo, Match.StrategyId,
            Match.Covenants.ToArray(), Match.BannedOperators.ToArray(), Match.BossId, lineup, [], null, now), null));
        SaveHistoryFile("history.json", _history);
    }

    private void RecordRecognizedMatch(bool newMatch, bool opening)
    {
        lock (_historyGate)
        {
            if (newMatch || _activeRecord is null) BeginHistory();
            _openingObserved |= opening;
            int index = _history.FindIndex(r => r.Id == _activeRecord);
            if (index < 0) return;
            var record = _history[index];
            _history[index] = record with { Local = record.Local! with { Mode = Match.Mode, Solo = Match.Solo,
                StrategyId = Match.StrategyId, Covenants = Match.Covenants.ToArray(), BannedOperators = Match.BannedOperators.ToArray(),
                BossId = Match.BossId, LastSeenAt = DateTimeOffset.UtcNow.ToString("O") } };
            SaveHistoryFile("history.json", _history);
        }
    }

    private void UpdateHistoryRound(int round, int? start, string now)
    {
        int index = _history.FindIndex(r => r.Id == _activeRecord);
        if (index < 0) return;
        var record = _history[index];
        var local = record.Local!;
        var rounds = local.Rounds;
        if (start is { } money && !rounds.Any(r => r.Round == round)) rounds = [.. rounds, new(round, money)];
        _history[index] = record with { Local = local with { Rounds = rounds, LastRound = round, LastSeenAt = now } };
        SaveHistoryFile("history.json", _history);
    }

    public MatchRecord[] History()
    {
        lock (_historyGate) return _history.OrderByDescending(r => DateTimeOffset.Parse(r.Time)).ToArray();
    }

    public void DeleteRecord(string id)
    {
        lock (_historyGate)
        {
            _history.RemoveAll(r => r.Id == id);
            SaveHistoryFile("history.json", _history);
        }
    }

    public MatchRecord[] SaveSklandRecords(SklandRecord[] records)
    {
        // TODO(design): 确认战绩数据无效的错误文案。
        if (records is null || records.Any(r => r is null || string.IsNullOrEmpty(r.Id)
            || !DateTimeOffset.TryParse(r.Time, out _) || !DateTimeOffset.TryParse(r.StartTime, out _)
            || r.Mode is not ("SINGLE" or "MULTI"))) throw new ArgumentException("战绩数据无效。");
        lock (_historyGate)
        {
            foreach (var skland in records.OrderBy(r => DateTimeOffset.Parse(r.Time)))
            {
                if (_history.Any(r => r.Skland?.Id == skland.Id)) continue;
                var end = DateTimeOffset.Parse(skland.Time);
                // TODO(design): 多条本地记录都满足时间窗时，优先匹配开始时间最近的一条。
                var match = _history.Where(r => r.Local is { } local && r.Skland is null
                    && local.Solo == (skland.Mode == "SINGLE") && end >= DateTimeOffset.Parse(r.Time)
                    && end <= DateTimeOffset.Parse(local.LastSeenAt).AddMinutes(30))
                    .OrderByDescending(r => DateTimeOffset.Parse(r.Time)).FirstOrDefault();
                if (match is not null) _history[_history.IndexOf(match)] = match with { Skland = skland };
                else _history.Add(new(Guid.NewGuid().ToString("N"), skland.StartTime, null, skland));
            }
            SaveHistoryFile("history.json", _history);
            return History();
        }
    }

    private void MergeHistory(MatchRecord[] records)
    {
        lock (_historyGate)
        {
            foreach (var record in records)
                if (!_history.Any(r => r.Id == record.Id)) _history.Add(record);
            SaveHistoryFile("history.json", _history);
        }
    }
}
