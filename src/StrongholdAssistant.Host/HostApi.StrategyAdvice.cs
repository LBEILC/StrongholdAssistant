using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed record StrategyAdviceItem(string StrategyId, double ClearRate, double ReachedRound);
public sealed record StrategyAdviceAverage(double ClearRate, double ReachedRound);
public sealed record StrategyAdvice(string Status, string? Reason, string Basis, StrategyAdviceItem[] Items,
    StrategyAdviceAverage Average, string[] Assumptions);

public sealed partial class HostApi
{
    private List<Strategy> _openingStrategies = [];
    private readonly object _adviceGate = new();
    private sealed record OpeningInput(string? Mode, string? Boss, string? Map, CovenantState[] Covenants, string[] Bans);
    private OpeningInput? _adviceInput;

    private void ResetStrategyAdvice()
    {
        lock (_adviceGate)
        {
            _adviceInput = null;
            _match = _match with { StrategyAdvice = null };
        }
    }

    private void ScheduleStrategyAdvice(bool mapOnly = false)
    {
        var map = Field?.MapId ?? _boardMap;
        lock (_adviceGate)
        {
            var last = _adviceInput;
            if (mapOnly && last is null) return;
            var match = _match;
            var covenants = match.Source == "none" ? [] : match.Covenants;
            if (last is not null && last.Mode == match.Mode?.Id && last.Map == map && last.Boss == match.BossId
                && last.Covenants.SequenceEqual(covenants) && last.Bans.SequenceEqual(match.BannedOperators)) return;

            var input = new OpeningInput(match.Mode?.Id, match.BossId, map, covenants, match.BannedOperators);
            _adviceInput = input;
            _match = match with { StrategyAdvice = null };
            _ = ComputeStrategyAdviceAsync(input);
        }
    }

    private async Task ComputeStrategyAdviceAsync(OpeningInput input)
    {
        // 连续补录只计算最后一个快照。模型可能在计算时换局，发布前再次核对。
        await Task.Delay(120).ConfigureAwait(false);
        lock (_adviceGate)
            if (!ReferenceEquals(input, _adviceInput)) return;
        var state = new MatchState { GameDataVersion = TextFact(_gameDataVersion), ModeId = TextFact(input.Mode),
            MapId = TextFact(input.Map), BossId = TextFact(input.Boss),
            Covenants = ListFact(input.Covenants.Select(c => new MatchCovenant
                { Id = TextFact(c.Id), Incomplete = BoolFact(c.Incomplete) }).ToArray()),
            BannedOperators = ListFact(input.Bans.Select(name => new MatchEntity { Name = TextFact(name) }).ToArray()) };
        var result = BuildStrategyAdvice(state);

        void Publish()
        {
            MatchInfo match;
            lock (_adviceGate)
            {
                if (!ReferenceEquals(input, _adviceInput)) return;
                match = _match = _match with { StrategyAdvice = result };
            }
            Changed?.Invoke("match", match);
        }
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher) await dispatcher.InvokeAsync(Publish);
        else Publish();
    }

    internal StrategyAdvice BuildStrategyAdvice(MatchState state)
    {
        var mode = state.ModeId.Value;
        var basis = StrategyBasis(mode) ?? "";
        if (mode?.StartsWith("mode_multi_", StringComparison.Ordinal) == true)
            return new("unavailable", "多人模式的推荐还在训练，暂不提供", basis, [], new(0, 0), []);

        var candidates = _openingStrategies.Where(s => !s.InactiveModes.Contains(mode!)).Select(s => s.Id).ToArray();
        var recommendation = RecommendOpeningStrategies(state, candidates);
        if (recommendation.Candidates.Length == 0)
            return new("unavailable", recommendation.Assumptions[0], basis, [], new(0, 0), []);
        var items = recommendation.Candidates.Select(s =>
            new StrategyAdviceItem(s.StrategyId, s.ClearProbability, s.ExpectedRound)).ToArray();
        return new("ok", null, basis, items, new(items.Average(s => s.ClearRate), items.Average(s => s.ReachedRound)),
            recommendation.Assumptions);
    }
}
