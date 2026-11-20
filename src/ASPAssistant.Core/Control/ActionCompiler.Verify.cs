using ASPAssistant.Core.Services;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Control;

public sealed partial class ActionCompiler
{
    public VerifyResult Verify(GameAction action, ControlFrame before, ControlFrame after)
    {
        if (action is StartSoloAction or LeaveResultAction) return VerifyMenu(action, before, after);
        var b = Read(action, before);
        var a = Read(action, after);
        if (action is ReadyAction { On: true })
        {
            if (a.ReadyConfirmation != null) return new(b.Board && b.ReadyConfirmation == null, "游戏正在等待确认进入战斗");
            if (a.Battle || a.Ready == true) return new(true, null) { Complete = true };
            return new(false, "未确认进入战斗或已就绪");
        }
        if (action is UpgradeAction && a.Board &&
            Join(Region(a, 0, (int)(a.H * .85), (int)(a.W * .65), (int)(a.H * .12))).Contains("确认升级"))
            return new(false, "游戏正在等待确认升级");
        if (action is PickAdaptAction adapt)
        {
            if (!AdaptPage(b)) return new(false, "操作前未认出机变页");
            if (!AdaptPage(a)) return new(a.Heading.Contains("回合") ? true : null, null) { Complete = true };
            if (AdaptConfirm(b, adapt.Index) == null) return new(AdaptConfirm(a, adapt.Index) != null, null);
            return new(AdaptConfirm(a, adapt.Index) == null && AdaptMarked(a, adapt.Index), null) { Complete = true };
        }
        bool? ok = Check();
        return new(ok, ok switch { true => null, false => "前后画面的变化与动作不符", null => "未能从画面核对结果" });

        bool? Check()
        {
            if (action is PickStrategyAction strategy)
            {
                if (!StrategyPage(b)) return false;
                var reader = new MatchStrategyReader(_ocr, _data, _strategies);
                string? id = reader.ReadSelectionAsync(after.Image, a.W, a.H).GetAwaiter().GetResult();
                if (id is not null) return id == strategy.StrategyId;
                if (StrategyPage(a)) return false;
                return after.State?.StrategyId.Value is { } current ? current == strategy.StrategyId : null;
            }
            if (action is PickCustomAction custom)
            {
                if (b.Custom is null || b.Custom.IsFinal) return false;
                bool ContainsRequested(IEnumerable<string?> names)
                {
                    var selected = names.ToHashSet();
                    return custom.Operators.Length == 0 ? selected.Count == 0 : custom.Operators.All(selected.Contains);
                }
                if (a.Custom is { IsFinal: true } final)
                    return ContainsRequested(final.Cards.Where(c => c.Tier == custom.Tier).Select(c => c.Name));
                if (a.Custom is not null) return false;
                if (after.State?.CustomPicks.Value is not { } picks) return null;
                return ContainsRequested((picks.GetValueOrDefault(custom.Tier) ?? []).Select(c => c.Name.Value));
            }
            if (!b.Board || !a.Board) return false;
            switch (action)
            {
                case ReadyAction ready:
                    return a.Ready.HasValue ? a.Ready == ready.On : null;
                case FreezeAction freeze:
                    return a.Frozen.HasValue ? a.Frozen == freeze.On : null;
                case MoveAction move:
                    var original = b.Unit(move.From);
                    if (before.Field is null || after.Field is null) return null;
                    if (original is null || move.Expect is not null && original.Operator != move.Expect) return false;
                    var dest = a.Unit(move.To);
                    var oldDest = b.Unit(move.To);
                    var src = a.Unit(move.From);
                    bool Matches(FieldUnit? unit, FieldUnit? expected) => expected is null ? unit is null :
                        unit is not null && unit.Tier == expected.Tier && (expected.Operator is null || unit.Operator == expected.Operator);
                    return Matches(dest, original) && Matches(src, oldDest) && move.From != move.To &&
                        !(Matches(original, oldDest) && Matches(oldDest, original));
                case FaceAction face:
                    if (before.Field != null && after.Field != null && b.Unit(face.At) != null && a.Unit(face.At) == null)
                        return false;
                    string? dir = after.State?.Units.Value?.FirstOrDefault(u =>
                        u.Zone.Value == face.At.Zone && u.Col.Value == face.At.Col && u.Row.Value == face.At.Row)?.Dir.Value;
                    return dir is null ? null : dir == face.Facing;
                case SellAction sell:
                    if (before.Field is null || after.Field is null || !b.Money.HasValue || !a.Money.HasValue) return null;
                    return b.Unit(sell.From) is { } sold && (sell.Expect is null || sold.Operator == sell.Expect)
                        && a.Unit(sell.From) is null && a.Money > b.Money;
                case BuyAction buy:
                    if (buy.Slot is < 0 or > 5) return false;
                    string? name = Card(b, buy.Slot);
                    if (name is null || buy.Expect is not null && name != buy.Expect) return false;
                    bool? empty = a.Cards.ContainsKey(buy.Slot) ? false : a.EmptySlots.Contains(buy.Slot) ? true :
                        StateCard(a, buy.Slot)?.Bought.Value == true ? true : null;
                    bool? spent = a.Money.HasValue && b.Money.HasValue ? a.Money < b.Money : null;
                    bool? gained = before.Field != null && after.Field != null ?
                        after.Field.Units.Count(u => u.Zone == "bench") > before.Field.Units.Count(u => u.Zone == "bench") : null;
                    bool?[] signals = [empty, spent, gained];
                    return signals.Count(v => v == true) >= 2 ? true : signals.Count(v => v == false) >= 2 ? false : null;
                case RefreshAction:
                    if (!a.Shop) return null;
                    bool changed = Enumerable.Range(0, 6).Any(i => Card(a, i) is { } card && card != Card(b, i));
                    if (!changed) return false;
                    if (a.Free < b.Free) return true;
                    if (!b.Money.HasValue || !a.Money.HasValue) return null;
                    return before.State?.RefreshCost.Value is > 0 and var fee
                        ? b.Money - a.Money == fee : a.Money < b.Money;
                case UpgradeAction:
                    var level = b.Economy?.Level ?? before.State?.Level.Value;
                    var next = a.Economy?.Level ?? after.State?.Level.Value;
                    if (level.HasValue && next == level + 1 || a.Money < b.Money) return true;
                    return level.HasValue && next.HasValue || a.Money.HasValue && b.Money.HasValue ? false : null;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }
}
