using ASPAssistant.Core.Services;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Control;

public sealed partial class ActionCompiler
{
    public VerifyResult Verify(GameAction action, ControlFrame before, ControlFrame after)
    {
        var b = Read(action, before);
        var a = Read(action, after);
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
            if (action is PickAdaptAction)
                return !AdaptPage(b) ? false : AdaptPage(a) ? false : a.Heading.Length > 0 ? true : null;
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
                    string? dir = after.State?.Units.Value?.FirstOrDefault(u =>
                        u.Zone.Value == face.At.Zone && u.Col.Value == face.At.Col && u.Row.Value == face.At.Row)?.Dir.Value;
                    return dir is null ? null : dir == face.Facing;
                case SellAction sell:
                    if (before.Field is null || after.Field is null || !b.Money.HasValue || !a.Money.HasValue) return null;
                    return b.Unit(sell.From) is { } sold && (sell.Expect is null || sold.Operator == sell.Expect)
                        && a.Unit(sell.From) is null && a.Money > b.Money;
                case BuyAction buy:
                    if (buy.Slot is < 0 or > 5 || !a.Shop) return null;
                    string? name = Card(b, buy.Slot);
                    if (name is null || buy.Expect is not null && name != buy.Expect) return false;
                    if (StateCard(a, buy.Slot)?.Bought.Value == true && StateCard(b, buy.Slot)?.Bought.Value != true) return true;
                    if (Card(a, buy.Slot) is not null) return false;
                    bool gained = after.Field?.Units.Any(u => u.Zone == "bench" && u.Operator == name && b.Unit(u.Cell)?.Operator != name) == true;
                    if (gained) return true;
                    if (before.Field is not null && after.Field is not null)
                    {
                        int Equipment(FieldState field) => field.Units.Sum(u => u.Equipment?.Count(e => e.Name == name) ?? 0);
                        if (Equipment(after.Field) > Equipment(before.Field)) return true;
                    }
                    return b.Money.HasValue && a.Money.HasValue ? a.Money < b.Money : null;
                case RefreshAction:
                    if (!a.Shop) return null;
                    bool changed = Enumerable.Range(0, 6).Any(i => Card(a, i) is { } card && card != Card(b, i));
                    if (!changed) return false;
                    if (a.Free < b.Free) return true;
                    if (!b.Money.HasValue || !a.Money.HasValue) return null;
                    return before.State?.RefreshCost.Value is > 0 and var fee
                        ? b.Money - a.Money == fee : a.Money < b.Money;
                case UpgradeAction:
                    var level = before.State?.Level.Value ?? b.Economy?.Level;
                    var next = after.State?.Level.Value ?? a.Economy?.Level;
                    var progress = before.State?.UpgradeProgress.Value;
                    var nextProgress = after.State?.UpgradeProgress.Value;
                    if ((!level.HasValue || !next.HasValue) && (!progress.HasValue || !nextProgress.HasValue)) return null;
                    if (!a.Money.HasValue || !b.Money.HasValue) return null;
                    return (next > level || nextProgress > progress) && a.Money < b.Money;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }
    }
}
