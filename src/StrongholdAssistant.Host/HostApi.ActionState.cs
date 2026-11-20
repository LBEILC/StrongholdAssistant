using ASPAssistant.Core.Control;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    // 预检和完成回写使用同一套格子变化规则，跳过没有观测依据的落点。
    private sealed class ActionState(MatchState state, FieldState? field, int? remaining)
    {
        private readonly Dictionary<FieldCell, FieldUnit>? _units = field?.Units.ToDictionary(u => u.Cell);
        private readonly MatchCard[] _shop = state.Shop.ToArray();
        private int? _money = state.Money.Value, _upgrade = state.UpgradeCost.Value, _level = state.Level.Value;
        private int? _remaining = remaining;
        private bool _uncertainOccupancy;
        public FieldUnit[]? Units => _units?.Values.OrderBy(u => u.Zone).ThenBy(u => u.Row).ThenBy(u => u.Col).ToArray();

        public string? Apply(GameAction action, string phase, bool check)
        {
            string required = action switch { PickAdaptAction => "adapt", PickStrategyAction => "strategy",
                PickCustomAction => "custom", _ => "rest" };
            if (check && phase != "unknown" && phase != required) return $"当前阶段 {phase}，此动作需要 {required}";
            var from = action switch { MoveAction m => m.From, SellAction s => s.From, FaceAction f => f.At, _ => null };
            if (check && from != null && !_uncertainOccupancy && _units != null && !_units.ContainsKey(from))
                return "起点格没有干员";
            switch (action)
            {
                case BuyAction buy:
                    if (buy.Slot is < 0 or > 5) return "卡位超出范围";
                    var card = _shop[buy.Slot];
                    if (check && card.Bought.Value == true) return $"{buy.Slot + 1} 号卡位已买或为空";
                    if (check && _money < card.Price.Value) return $"资金 {_money}，买 {buy.Slot + 1} 号卡位要 {card.Price.Value}";
                    if (check && !_uncertainOccupancy && _units != null && card.Kind.Value == "operator"
                        && card.Card.Value?.Name.Value is { } target && _units.Values.Count(u => u.Zone == "bench") == 10
                        && _units.Values.All(u => u.Status is "sure" or "auto" && u.Operator != target))
                        return "整备区已满";
                    _money -= card.Price.Value;
                    _shop[buy.Slot] = new() { Bought = BoolFact(true, "inferred") };
                    if (_units == null || card.Kind.Value == "equipment") break;
                    if (card.Card.Value?.Name.Value is not { } name || card.Tier.Value is not int tier
                        || _units.Values.Count(u => u.Operator == name && u.Elite != true) >= 2)
                    { _uncertainOccupancy = true; break; }
                    var empty = Enumerable.Range(1, 10).Reverse().Select(c => new FieldCell("bench", c, 0))
                        .FirstOrDefault(c => !_units.ContainsKey(c));
                    if (empty != null && !_uncertainOccupancy) _units[empty] = new("bench", empty.Col, 0, tier, name, "inferred");
                    break;
                case SellAction sell:
                    if (sell.From.Zone == "board") _remaining++;
                    _units?.Remove(sell.From);
                    _money = null;
                    break;
                case MoveAction move:
                    if (check && move.From.Zone == "bench" && move.To.Zone == "board" && _remaining <= 0)
                        return "场上没有剩余部署名额";
                    if (_units == null) { _remaining = null; break; }
                    bool occupied = _units.TryGetValue(move.To, out var other);
                    if (!occupied && move.From.Zone != move.To.Zone) _remaining += move.To.Zone == "board" ? -1 : 1;
                    if (_units.Remove(move.From, out var unit))
                    {
                        if (other != null) _units[move.From] = At(other, move.From);
                        _units[move.To] = At(unit, move.To);
                    }
                    break;
                case RefreshAction:
                    if (check && _money < state.RefreshCost.Value) return $"资金 {_money}，刷新要 {state.RefreshCost.Value}";
                    _money -= state.RefreshCost.Value;
                    Array.Fill(_shop, new MatchCard());
                    break;
                case UpgradeAction:
                    if (check && _level >= 6) return "调度中心已满级";
                    if (check && _money < _upgrade) return $"资金 {_money}，升级要 {_upgrade}";
                    _money -= _upgrade;
                    _upgrade = _level = null;
                    break;
            }
            return null;
        }

        private static FieldUnit At(FieldUnit u, FieldCell cell) =>
            u with { Zone = cell.Zone, Col = cell.Col, Row = cell.Row, Source = "inferred" };
    }

    private FieldState? _inferredState, _beforeActions;
    private MatchState? _beforeState;
    private int _actionGeneration;
    private DateTimeOffset _actionsAppliedAt;

    private void BeginFieldActions()
    {
        lock (_fieldGate)
        {
            _beforeActions = Field;
            _beforeState = GetMatchState();
            _actionGeneration = _fieldGeneration;
        }
    }

    private void CompleteFieldActions(ControlJob job)
    {
        lock (_fieldGate)
        {
            if (_beforeState == null || _actionGeneration != _fieldGeneration) return;
            var done = job.Steps.Where(s => s.State == "done").Select(s => s.Action).ToArray();
            if (!done.Any(a => a is BuyAction or MoveAction or SellAction)) return;
            _actionsAppliedAt = job.EndedAt!.Value;
            if (_matchRest is { } rest && rest.Economy.Round == _beforeState.Round.Value)
            {
                // 刷新会换一批商品，刷新前的购买记录不可套在新卡上。
                var buys = done.Skip(Array.FindLastIndex(done, a => a is RefreshAction) + 1)
                    .OfType<BuyAction>().Select(b => b.Slot).ToHashSet();
                var purchased = rest.Cards.Where(c => buys.Contains(c.Slot)).Select(c => c.Slot).ToArray();
                if (purchased.Length > 0)
                    _matchRest = rest with { Cards = rest.Cards.Where(c => !buys.Contains(c.Slot)).ToArray(),
                        Purchased = rest.Purchased.Concat(purchased).Distinct().ToArray() };
            }
            if (_beforeActions is not { } before
                || Field is not { } current || current.Round != before.Round) return;
            var projection = new ActionState(_beforeState, before, null);
            foreach (var action in done) projection.Apply(action, "unknown", false);
            if (_fieldTracker.ApplyInferredChanges(before, projection.Units!, job.EndedAt!.Value))
                _inferredState = _fieldTracker.State;
        }
    }
}
