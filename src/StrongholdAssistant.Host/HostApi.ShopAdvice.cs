using System.Text.Json;
using ASPAssistant.Core.Models;
using Microsoft.ML.OnnxRuntime;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private Lazy<ShopPolicy> _shopPolicy = null!;
    private readonly object _shopModelGate = new();
    private string? _shopAdviceKey;
    private long _shopRevision;
    private bool _shopClosed;
    private bool _shopEconomyStable;
    private RestReading? _priorAdviceRest;
    private string? _priorAdviceMode;
    private int? _equipmentSlot;
    private int? _shopInitialHp;
    private MatchState? _countedShop;
    private int _shopActions, _shopBuys, _shopRefreshes;
    private readonly Dictionary<int, (int? Loss, int? Leaks)> _shopHistory = [];
    private bool AdviceMode => Settings.ShopAdvice && Match.Mode?.Id is string id
        && id.StartsWith("mode_single_", StringComparison.Ordinal) && int.TryParse(id.AsSpan(12), out int n) && n is >= 1 and <= 20;

    // 清除也是带版本的消息，订阅端能忽略排队中的旧建议。
    internal void LeaveAdviceShop()
    {
        lock (_fieldGate)
        {
            _priorAdviceRest = null;
            _shopEconomyStable = false;
            ClearShopAdvice();
        }
    }

    internal void ClearShopAdvice()
    {
        lock (_fieldGate)
        {
            if (_shopAdviceKey == null) return;
            _shopAdviceKey = null;
            Changed?.Invoke("shopAdvice", new ShopAdvice(++_shopRevision, ShopPolicy.ModelId, "unavailable", [], null, null));
        }
    }

    private void ResetShopHistory()
    {
        _countedShop = null; _shopInitialHp = null; _shopHistory.Clear();
        LeaveAdviceShop();
    }

    private ShopContext ShopInputs(MatchState state)
    {
        int round = state.Round.Value!.Value;
        if (round == 1 && state.Hp.Value > 0) _shopInitialHp ??= state.Hp.Value;
        if (_lastRound is { } last)
            _shopHistory[last.Round] = (last.HpChange is int delta ? Math.Max(0, -delta) : null, last.Leaks);
        if (_countedShop?.Round.Value != round)
        {
            _equipmentSlot = null;
            _shopBuys = _shopActions = _shopRefreshes = 0;
        }
        else
        {
            var prev = _countedShop;
            int bought = prev.Shop.Zip(state.Shop).Count(p => p.First.Bought.Value == false && p.Second.Bought.Value == true);
            bool upgraded = state.Level.Value > prev.Level.Value;
            bool refreshed = !upgraded && prev.Shop.Zip(state.Shop).Any(p => p.Second.Bought.Value == false
                && (p.First.Bought.Value == true || p.First.Card.Value?.Id.Value != p.Second.Card.Value?.Id.Value));
            if (!refreshed && !upgraded) { _shopBuys += bought; _shopActions += bought; }
            if (refreshed) { _shopRefreshes++; _shopActions++; _equipmentSlot = null; }
            if (upgraded) _shopActions++;
            if (prev.Frozen.Value == false && state.Frozen.Value == true) _shopActions++;
        }
        _countedShop = state;
        int equipmentSlot = Array.FindIndex(state.Shop, c => c.Kind.Value == "equipment");
        if (equipmentSlot >= 0) _equipmentSlot = equipmentSlot;
        // 只接受正在追踪的阵容。多个核心目标没有主次标注时，不按数组顺序替玩家决定。
        var goals = _trackingStage?.Goals.Select(g => g.Id).Distinct().ToArray() ?? [];
        var core = goals.Where(id => _covenants.Any(c => c.BondType == "SEASON" && _ids[c.Name] == id)).ToArray();
        var secondary = goals.Except(core).ToArray();
        int? cap = state.Units.Value?.Count(u => u.Zone.Value == "board") + _matchRest?.Economy.RemainingDeploy;
        var history = Enumerable.Range(1, 3).Select(k => _shopHistory.GetValueOrDefault(round - k)).ToArray();
        return new(_shopInitialHp, core.Length == 1 ? core[0] : null, secondary.Length == 1 ? secondary[0] : null,
            _shopActions, _shopBuys, _shopRefreshes, cap, history.Select(h => h.Loss).ToArray(), history.Select(h => h.Leaks).ToArray(), _equipmentSlot);
    }

    internal Task UpdateShopAdviceAsync()
    {
        lock (_fieldGate)
        {
            if (_shopClosed || !AdviceMode || _priorAdviceMode != Match.Mode?.Id || !_shopEconomyStable || _phase != "rest" || _matchRest?.Economy.Round == null)
            { ClearShopAdvice(); return Task.CompletedTask; }
            var state = GetMatchState();
            var context = ShopInputs(state);
            var key = JsonSerializer.Serialize(new { state, context }, JsonOptions);
            if (_shopAdviceKey == key) return Task.CompletedTask;
            ClearShopAdvice();
            _shopAdviceKey = key;
            long revision = ++_shopRevision;
            return Task.Run(() =>
            {
                ShopAdvice advice;
                lock (_shopModelGate)
                {
                    lock (_fieldGate) if (revision != _shopRevision || _shopClosed) return;
                    try { advice = _shopPolicy.Value.Predict(revision, state, context); }
                    catch (Exception ex) when (ex is IOException or JsonException or OnnxRuntimeException)
                    { advice = new(revision, ShopPolicy.ModelId, "unavailable", ["model"], null, null); }
                }
                lock (_fieldGate)
                    if (revision == _shopRevision && !_shopClosed) Changed?.Invoke("shopAdvice", advice);
            });
        }
    }

    internal void CloseShopAdvice()
    {
        lock (_fieldGate) { _shopClosed = true; _shopRevision++; }
        lock (_shopModelGate) if (_shopPolicy.IsValueCreated) _shopPolicy.Value.Dispose();
    }
}

