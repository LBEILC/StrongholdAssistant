using System.Text.Json;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed record IncomeSource(string Name, string Trigger, string Kind, int Amount, int EliteAmount);
public sealed record Income(string Id, string Source, bool Elite, int Amount, string Kind, int Round);
public sealed record Economy(int Round, int Money, int? StartMoney, int? Level, int? UpgradeCost,
    string ReadAt, int? Next, int Extra, Income[] Incomes, string? Rule, string Basis);

public sealed partial class HostApi
{
    private Dictionary<string, int> _baseMoney = [];
    private int? _observedRound;
    private int? _startMoney;
    private bool _canReadStart;
    private bool _openingObserved;

    internal static IncomeSource[] BuildIncomeSources(IEnumerable<Equipment> equipment, IEnumerable<Operator> operators)
    {
        var sources = new List<IncomeSource>();
        void Add(string name, string trigger, string normal, string elite)
        {
            const string pattern = @"(之后每回合开始时|下回合)额外获得(\d+)资金";
            var a = Regex.Match(normal, pattern);
            var b = Regex.Match(elite, pattern);
            if (a.Success && b.Success)
                sources.Add(new(name, trigger, a.Groups[1].Value == "下回合" ? "once" : "each",
                    int.Parse(a.Groups[2].Value), int.Parse(b.Groups[2].Value)));
        }
        foreach (var e in equipment) Add(e.Name, "equipment", Clean(e.Normal.EffectDescription), Clean(e.Elite.EffectDescription));
        foreach (var o in operators)
            Add(o.Name, "sell", string.Join("", o.Normal.Traits.Where(t => t.TraitDescription.Contains("<售出时>")).Select(t => Clean(t.TraitDescription))),
                string.Join("", o.Elite.Traits.Where(t => t.TraitDescription.Contains("<售出时>")).Select(t => Clean(t.TraitDescription))));
        return sources.ToArray();
    }

    internal static string? EconomyRule(string? strategy) => strategy switch
    {
        "band_cannot" => "carry", "band_lmlee" => "deferred", "band_mlynar" => "kazimier",
        "band_fund" or "band_protect" or "band_treasure" or "band_ducklord" or "band_siracusa" => "random",
        _ => null
    };

    internal static int IncomeAt(IEnumerable<Income> incomes, int round) => incomes
        .Where(i => round > i.Round && (i.Kind == "each" || round == i.Round + 1)).Sum(i => i.Amount);

    private string MoneyKey(int round) => $"{Match.Mode!.Id}|{Match.Solo}|{round}";
    private (int? Amount, string Basis) BaseMoneySource(int round)
    {
        if (Match.Mode is null) return (null, "none");
        int? own = _baseMoney.TryGetValue(MoneyKey(round), out int amount) ? amount : null;
        int? shared = _sharedMoney.GetValueOrDefault(MoneyKey(round));
        return shared is not null && (own is null || shared < own) ? (shared, "shared")
            : own is not null ? (own, "own") : (null, "none");
    }

    private int? BaseMoney(int round) => BaseMoneySource(round).Amount;

    private Economy PredictCurrent(Economy economy)
    {
        var prediction = Predict(economy, Match.StrategyId, BaseMoney);
        var rounds = prediction.Rule == "deferred" && economy.Round == 2
            ? new[] { 1, 2, 3 } : new[] { economy.Round, economy.Round + 1 };
        return prediction with { Basis = rounds.Any(r => BaseMoneySource(r).Basis == "shared") ? "shared" : prediction.Basis };
    }

    public MatchInfo SetMatchStrategy(string? id)
    {
        if (id is not null && !Catalog.GetProperty("strategies").EnumerateArray()
            .Any(s => s.GetProperty("id").GetString() == id)) throw new ArgumentException("策略不存在。");
        lock (_historyGate)
        {
            Match = Match with { StrategyId = id };
            if (Match.Economy is { } economy) Match = Match with { Economy = PredictCurrent(economy) };
            int index = _history.FindIndex(r => r.Id == _activeRecord);
            if (index >= 0)
            {
                var record = _history[index];
                _history[index] = record with { Local = record.Local! with { StrategyId = id } };
                SaveHistoryFile("history.json", _history);
            }
            Changed?.Invoke("match", Match);
            return Match;
        }
    }

    internal static Economy Predict(Economy economy, string? strategy, Func<int, int?> basis)
    {
        var rule = EconomyRule(strategy);
        int extra = economy.StartMoney is { } start && basis(economy.Round) is { } current
            ? Math.Max(0, start - current - IncomeAt(economy.Incomes, economy.Round)) : 0;
        int? next = basis(economy.Round + 1);
        if (rule == "deferred")
        {
            // TODO(design): 044 同时写了第 2 回合 next=null 和预测第 3 回合合计；采用后一条明确公式。
            if (economy.Round == 1) next = null;
            if (economy.Round == 2) next = basis(1) + basis(2) + basis(3);
        }
        next += IncomeAt(economy.Incomes, economy.Round + 1) + extra;
        if (rule == "carry") next += economy.Money + (economy.Money >= 5 ? 1 : 0);
        return economy with { Next = next, Extra = extra, Rule = rule,
            Basis = basis(economy.Round) is not null || basis(economy.Round + 1) is not null ? "own" : "none" };
    }

    internal void ObserveEconomy(EconomyReading reading)
    {
        if (!reading.IsRest || reading.Round is not > 0) return;
        lock (_historyGate)
        {
            int round = reading.Round.Value;
            if (round == 1 && _observedRound > 1)
            {
                BeginHistory();
                _openingObserved = true;
            }
            if (_observedRound != round)
            {
                _canReadStart = _observedRound is not null || (_openingObserved && round == 1);
                _startMoney = null;
                _observedRound = round;
            }
            if (_canReadStart && _startMoney is null && reading.Money is { } first)
            {
                _startMoney = first;
                _canReadStart = false;
                _roundSamples[round] = first - IncomeAt(Match.Economy?.Incomes ?? [], round);
            }
            string now = DateTimeOffset.UtcNow.ToString("O");
            UpdateHistoryRound(round, _startMoney, now);
            // TODO(design): 协议 round/money 必填；任一未读到时保留最近一次完整读数，待确认是否改为可空。
            if (reading.Money is not { } money) return;
            bool roundChanged = Match.Economy?.Round != round;
            Match = Match with { Economy = PredictCurrent(new(round, money, _startMoney, reading.Level,
                reading.UpgradeCost, now, null, 0, Match.Economy?.Incomes ?? [], null, "none")) };
            if (roundChanged) RecomputeTracking();
            Changed?.Invoke("match", Match);
        }
    }

    public MatchInfo AddIncome(string source, bool elite)
    {
        lock (_historyGate)
        {
            // TODO(design): 确认收入来源无效、尚未读到回合的错误文案。
            var definition = Catalog.GetProperty("incomeSources").Deserialize<IncomeSource[]>(JsonOptions)!
                .SingleOrDefault(i => i.Name == source) ?? throw new ArgumentException("收入来源不存在。");
            var economy = Match.Economy ?? throw new ArgumentException("尚未读到当前回合。");
            return PublishIncome(economy with { Incomes = [.. economy.Incomes,
                new(Guid.NewGuid().ToString("N"), source, elite, elite ? definition.EliteAmount : definition.Amount, definition.Kind, _observedRound ?? economy.Round)] });
        }
    }

    public MatchInfo RemoveIncome(string id)
    {
        lock (_historyGate)
        {
            if (Match.Economy is { } economy) return PublishIncome(economy with { Incomes = economy.Incomes.Where(i => i.Id != id).ToArray() });
            Changed?.Invoke("match", Match);
            return Match;
        }
    }

    private MatchInfo PublishIncome(Economy economy)
    {
        Match = Match with { Economy = PredictCurrent(economy) };
        Changed?.Invoke("match", Match);
        return Match;
    }

    private void ClearEconomy()
    {
        _roundSamples.Clear();
        _observedRound = null;
        _startMoney = null;
        _canReadStart = false;
        _openingObserved = false;
        Match = Match with { Economy = null };
    }
}
