using System.Diagnostics;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private string _phase = "unknown";
    private string? _turnId;
    private (int? Round, string Phase)? _decision;
    private long _decisionSerial;
    private object? _options;
    private CollectedResult? _settlement;

    private object GetSchedule()
    {
        var match = Match;
        if (match.Mode?.Id is not { } mode) return Array.Empty<object>();
        bool Adapt(int round) => mode switch
        {
            "mode_single_funny" => false,
            "mode_multi_funny" => round is 3 or 9,
            "mode_multi_normal" => round is 6 or 9,
            _ when mode.StartsWith("mode_single_") && int.TryParse(mode.AsSpan(12), out int n) && n <= 8 => round is 6 or 9,
            _ => round is 3 or 9 or 11
        };
        return _boardRouteStore.Schedule(mode).Select(r => new { round = r.Round, boss = r.Bosses.Length > 0,
            adapt = Adapt(r.Round), enemies = _placementAdapter.PossibleEnemies(r.SpawnRound, mode, match.EnemyTypes,
                r.Bosses, match.BossId) }).ToArray();
    }

    private sealed record LastRound(int Round, int? HpChange, int? Leaks, bool? Cleared);
    private LastRound? _lastRound;
    private RestReading? _previousShop;
    private readonly ShopStateStabilizer _turnShop = new();
    private int? _battleRound;
    private TaskCompletionSource _turnChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SetPhase(string phase, int? round = null, object? options = null, CollectedResult? result = null)
    {
        lock (_fieldGate)
        {
            if (phase != "rest") _previousShop = null;
            if (phase != "unknown" && phase != "rest") _turnShop.Reset();
            if (phase == "battle") _battleRound = round;
            if (_turnId != null && phase is "adapt" or "over" && (_battleRound ?? _matchHp?.Round) is int completed)
                _lastRound = new(completed, null, result?.RoundLeaks ?? (_lastRound?.Round == completed ? _lastRound.Leaks : null),
                    result?.Cleared ?? (_lastRound?.Round == completed ? _lastRound.Cleared : null));
            if (phase == "rest") _battleRound = null;
            var priorResult = _decision?.Phase == "over" ? _settlement : null;
            bool decision = phase is not ("unknown" or "battle");
            if (decision && (_decision != (round, phase) || _phase != phase))
            {
                if (_decision != (round, phase))
                    _turnId = (round.HasValue ? $"r{round}-" : "") + $"{phase}-{++_decisionSerial}";
                _decision = (round, phase);
                _turnChanged.TrySetResult();
                _turnChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            if (phase == "battle") _decision = null;
            _settlement = result is null ? (phase == "unknown" ? priorResult : null) : result with
            {
                Cleared = result.Cleared ?? priorResult?.Cleared, FinalHp = result.FinalHp ?? priorResult?.FinalHp,
                ReachedRound = result.ReachedRound ?? priorResult?.ReachedRound
            };
            _phase = phase;
            if (phase != "rest") LeaveAdviceShop();
            _options = options;
        }
    }

    private async Task<object> ReadTurnAsync(Uri uri, CancellationToken token)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (!int.TryParse(query["wait"] ?? "0", out int seconds) || seconds < 0)
            throw new LocalApiError(400, "等待秒数无效");
        var deadline = TimeSpan.FromSeconds(Math.Min(seconds, 600));
        var clock = Stopwatch.StartNew();
        bool waitedForField = false;
        while (true)
        {
            Task changed;
            TimeSpan left;
            lock (_fieldGate)
            {
                left = deadline - clock.Elapsed;
                bool decision = _phase is not ("unknown" or "battle");
                if (left <= TimeSpan.Zero || decision && _turnId != query["after"])
                {
                    var state = GetMatchState();
                    if (_phase != "rest" || state.Units.Value != null || waitedForField)
                        return new { turnId = decision ? _turnId : null, phase = _phase, state,
                            fieldReason = _phase == "rest" && state.Units.Value == null ? FieldTimeout : null,
                            lastRound = _lastRound, options = _options,
                            result = _phase != "over" || _settlement is null ? null : new { _settlement.Cleared, _settlement.ReachedRound, hp = _settlement.FinalHp } };
                    waitedForField = true;
                    var budget = seconds == 0 || left > FirstFieldWait ? FirstFieldWait : TimeSpan.FromTicks(Math.Max(0, left.Ticks));
                    changed = WaitForFieldAsync(state.Round.Value, budget, token);
                }
                else changed = _turnChanged.Task.WaitAsync(left, token);
            }
            try { await changed; }
            catch (TimeoutException) { }
        }
    }

    internal async Task ObserveBoardTurn(byte[] frame, DateTimeOffset at, EconomyReading? economy, bool main, bool visible, IReadOnlyList<ShopItem> shop)
    {
        var cards = visible ? shop.Where(c => c.SlotIndex is >= 0 and <= 5 && c.Name.Length > 0)
            .OrderBy(c => c.SlotIndex).Select(c => (c.SlotIndex, c.Name, c.Price)).ToArray() : [];
        var rest = main && economy is { IsRest: true }
            ? new RestReading(economy, cards, visible ? _shopReader?.EmptySlots ?? [] : []) : null;
        bool stable;
        lock (_fieldGate)
        {
            // 截图可能早于刚结束的动作，不能用迟到的旧帧恢复已购卡或原格。
            if (at < _actionsAppliedAt) return;
            _coveredHud = null;
            _matchRest = rest;
            if (rest?.Economy.Round is not int round)
            {
                LeaveAdviceShop();
                if (economy is { IsRest: false, Round: not null }) SetPhase("battle", economy.Round);
                else if (economy is { IsRest: true }) SetPhase("unknown");
                return;
            }
            _turnShop.TryUpdate(shop, out var stabilized, visible);
            var previous = _previousShop;
            stable = visible && stabilized.Count == cards.Length && rest.Economy.Money != null
                && previous?.Economy.Round == round && previous.Economy.Money == rest.Economy.Money
                && previous.Cards.SequenceEqual(cards) && cards.Length + rest.Empty.Length == 6;
            if (!stable) SetPhase("unknown");
            _shopEconomyStable = stable && _priorAdviceMode == Match.Mode?.Id && previous != null && ReferenceEquals(_priorAdviceRest, previous) && previous.Economy.Level == rest.Economy.Level
                && previous.Economy.UpgradeCost == rest.Economy.UpgradeCost && previous.Economy.Frozen == rest.Economy.Frozen
                && previous.Economy.RefreshCost == rest.Economy.RefreshCost && previous.Empty.SequenceEqual(rest.Empty);
            if (!_shopEconomyStable) ClearShopAdvice();
            _priorAdviceRest = rest;
            _priorAdviceMode = Match.Mode?.Id;
            _previousShop = rest;
            if (_matchHp?.Round == round && _matchHp.Value.Hp != null)
            {
                if (stable) SetPhase("rest", round);
                return;
            }
        }

        int current = rest.Economy.Round.Value;
        var hp = _collectionHud is null ? null :
            (await _collectionHud.ReadVitals(frame, rest.Economy, Theme(Settings.GameTheme))).Hp;
        lock (_fieldGate)
        {
            if (!ReferenceEquals(rest, _matchRest)) return;
            if (_matchHp?.Round != current)
                _lastRound = _turnId != null && _matchHp is { } old ? new(current - 1, current == old.Round + 1 ? hp - old.Hp : null, null, null) : null;
            if (hp.HasValue) _matchHp = (current, hp);
            if (stable) SetPhase("rest", current);
        }
    }

    internal async Task ObserveOtherTurn(byte[] frame, int width, int height)
    {
        var generation = _matchReadingGeneration;
        var picks = await _customPickReader.ReadAsync(frame, width, height);
        await ObserveOtherTurn(frame, width, height, picks, generation);
    }

    private async Task ObserveOtherTurn(byte[] frame, int width, int height, CustomPickReading? picks, int generation)
    {
        var result = await _collectionHud!.ReadResult(frame);
        if (generation != _matchReadingGeneration) return;
        if (result is not null) { SetPhase("over", result: result); return; }
        if (picks is { IsFinal: false })
        {
            SetPhase("custom", options: new { custom = picks.Cards.Where(c => c.State != CustomPickCardState.Banned)
                .GroupBy(c => c.Tier).Select(g => new { tier = g.Key, operators = g.Select(c => c.Name).ToArray() }).ToArray() });
            return;
        }
        // 开局快路径也要记录已选定的策略；等待跳页期间不再发布可操作的选择点。
        if (await _strategyReader.ReadSelectionAsync(frame, width, height) is { } selected)
        {
            if (generation != _matchReadingGeneration) return;
            if (Match.StrategySource == null)
            {
                ApplyRecognizedStrategy(selected);
                CollectOpening(frame, "info");
            }
            SetPhase("unknown");
            return;
        }
        var strategies = await _strategyReader.ReadOptionsAsync(frame, width, height);
        if (generation != _matchReadingGeneration) return;
        if (strategies != null) SetPhase("strategy", options: new { strategies });
        else SetPhase("unknown");
    }
}
