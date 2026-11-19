using ASPAssistant.Core.Data;
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
    private HashSet<string> _deletedSklandIds = [];
    private sealed record HistoryDocument(List<MatchRecord> Records, HashSet<string> DeletedSklandIds);

    private void SaveHistory()
    {
        SaveHistoryFile("history-deleted-skland.json", _deletedSklandIds);
        SaveHistoryFile("history.json", _history);
    }

    private string[] DeletedSklandIds()
    {
        lock (_historyGate) return _deletedSklandIds.ToArray();
    }
    private string? _activeRecord;

    private void LoadHistory()
    {
        var history = UserJsonFile.Read(Path.Combine(DataDirectory, "history.json"), text =>
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.ValueKind == JsonValueKind.Array
                ? new HistoryDocument(json.RootElement.Deserialize<List<MatchRecord>>(JsonOptions)!, [])
                : json.RootElement.Deserialize<HistoryDocument>(JsonOptions);
        });
        if (history is not null)
        {
            _history = history.Records;
            _deletedSklandIds = history.DeletedSklandIds;
        }
        _deletedSklandIds.UnionWith(UserJsonFile.Read(Path.Combine(DataDirectory, "history-deleted-skland.json"),
            text => JsonSerializer.Deserialize<string[]>(text, JsonOptions)) ?? []);
        _baseMoney = UserJsonFile.Read(Path.Combine(DataDirectory, "base-money.json"),
            text => JsonSerializer.Deserialize<Dictionary<string, int>>(text, JsonOptions)) ?? [];
    }

    private void SaveHistoryFile<T>(string name, T value) =>
        UserJsonFile.Write(Path.Combine(DataDirectory, name), JsonSerializer.Serialize(value, JsonOptions));

    private void BeginHistory()
    {
        CompleteHistory();
        ClearEconomy();
        ResetMatchReading();
        ResetStrategyAdvice();
        Match = Match with { StrategyId = null, StrategySource = null, RoundEnemies = null, CustomPicks = null };
        string now = DateTimeOffset.UtcNow.ToString("O");
        _activeRecord = Guid.NewGuid().ToString("N");
        // 序列化复制快照，之后编辑目标阵容不会修改历史。
        var lineup = _trackingTarget is null ? null : JsonSerializer.Deserialize<LineupDto>(JsonSerializer.Serialize(_trackingTarget, JsonOptions), JsonOptions);
        _history.Add(new(_activeRecord, now, new(Match.Mode, Match.Solo, Match.StrategyId,
            Match.Covenants.ToArray(), Match.BannedOperators.ToArray(), Match.BossId, lineup, [], null, now), null));
        SaveHistory();
    }

    private void RecordRecognizedMatch(bool newMatch, bool opening)
    {
        if (newMatch || opening && !_openingObserved) ResetField();
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
            SaveHistory();
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
        SaveHistory();
    }

    public MatchRecord[] History()
    {
        lock (_historyGate) return _history.OrderByDescending(r => DateTimeOffset.Parse(r.Time))
            .Select(r => r.Local is { } local ? r with { Local = local with
            {
                Rounds = local.Rounds.Where(s => s.Round is >= 1 and <= 15).ToArray(),
                LastRound = local.LastRound is >= 1 and <= 15 ? local.LastRound : null
            } } : r).ToArray();
    }

    public void DeleteRecord(string id)
    {
        lock (_historyGate)
        {
            foreach (var record in _history.Where(r => r.Id == id && r.Skland is not null))
                _deletedSklandIds.Add(record.Skland!.Id);
            _history.RemoveAll(r => r.Id == id);
            SaveHistory();
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
                if (_deletedSklandIds.Contains(skland.Id) || _history.Any(r => r.Skland?.Id == skland.Id)) continue;
                var end = DateTimeOffset.Parse(skland.Time);
                // TODO(design): 多条本地记录都满足时间窗时，优先匹配开始时间最近的一条。
                var match = _history.Where(r => r.Local is { } local && r.Skland is null
                    && local.Solo == (skland.Mode == "SINGLE") && end >= DateTimeOffset.Parse(r.Time)
                    && end <= DateTimeOffset.Parse(local.LastSeenAt).AddMinutes(30))
                    .OrderByDescending(r => DateTimeOffset.Parse(r.Time)).FirstOrDefault();
                if (match is not null) _history[_history.IndexOf(match)] = match with { Skland = skland };
                else _history.Add(new(Guid.NewGuid().ToString("N"), skland.StartTime, null, skland));
            }
            SaveHistory();
            return History();
        }
    }

    private void MergeHistory(MatchRecord[] records, string[] deletedSklandIds)
    {
        lock (_historyGate)
        {
            _deletedSklandIds.UnionWith(deletedSklandIds);
            _history.RemoveAll(r => r.Skland is { } skland && _deletedSklandIds.Contains(skland.Id));
            foreach (var record in records)
            {
                if (record.Skland is { } skland && _deletedSklandIds.Contains(skland.Id)) continue;
                int index = _history.FindIndex(r => r.Id == record.Id
                    || record.Skland is not null && r.Skland?.Id == record.Skland.Id);
                if (index < 0) _history.Add(record);
                else
                {
                    var local = _history[index];
                    _history[index] = local with { Local = local.Local ?? record.Local, Skland = local.Skland ?? record.Skland };
                }
            }
            SaveHistory();
        }
    }
}
