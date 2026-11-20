using System.Drawing;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Control;

public sealed partial class ActionCompiler
{
    private static bool StrategyPage(View v) => v.Heading.Contains("选择策略") || v.Heading.Contains("请选择策略");
    private static bool AdaptPage(View v) => new[] { "机变", "道具补给", "机密商店", "悬赏决策", "战术决策" }.Any(v.Heading.Contains);
    private TapStep? AdaptConfirm(View v, int index)
    {
        var lines = Region(v, 0, (int)(v.H * .49), v.W, (int)(v.H * .29));
        var label = lines.FirstOrDefault(t => t.Text.Replace(" ", "").Contains("确认选择")
            && Math.Abs((t.Box.X + t.Box.W * .5) / v.W - (.25 + .25 * index)) < .11);
        return label == null ? null : new((label.Box.X + label.Box.W * .5) / v.W, (label.Box.Y + label.Box.H * .5) / v.H);
    }
    private static bool AdaptMarked(View v, int index)
    {
        // 单人选择成功后，目标卡右上角会出现青色圆框的玩家头像。
        using var stream = new MemoryStream(v.Frame.Image);
        using var image = new Bitmap(stream);
        int marked = 0;
        for (int y = 322; y < 408; y += 4)
        for (int x = 573; x < 655; x += 4)
        {
            int px = (int)((.25 + index * .25) * v.W + (x - 480) * v.S);
            var c = image.GetPixel(px, (int)(v.H * .5 + (y - 540) * v.S));
            if (c.G > 135 && c.B > 100 && c.G > c.R * 1.6 && c.B > c.R * 1.6) marked++;
        }
        return marked >= 20;
    }

    private CompileResult CompileOpening(GameAction a, View v, List<InputStep> steps, Func<string, CompileResult> plan)
    {
        switch (a)
        {
            case PickStrategyAction strategy:
                if (!StrategyPage(v)) return WrongScreen();
                if (!_strategies.TryGetValue(strategy.StrategyId, out var initiator)) return Mismatch("没有这个策略");
                if (!v.Heading.Contains("请选择策略")) return Mismatch("当前没有轮到选择策略");
                var details = Join(Region(v, (int)(v.W * .75), (int)(v.H * .13), (int)(v.W * .25), (int)(v.H * .86)));
                string confirm = Join(Region(v, (int)(v.W * .75), (int)(v.H * .86), (int)(v.W * .25), (int)(v.H * .13)));
                bool detailOpen = details.Contains("初始生命值") && confirm.Contains("确认");
                if (!(detailOpen && details.Contains(initiator)))
                {
                    var icon = StrategyIcon(v, strategy.StrategyId, detailOpen);
                    if (icon is null) return Mismatch("当前可见的策略图标中没有找到目标");
                    steps.Add(icon);
                    steps.Add(new WaitStep(400));
                }
                steps.Add(v.Center(1700, 997));
                return plan($"选择策略（{initiator}）");
            case PickAdaptAction adapt:
                if (!AdaptPage(v)) return WrongScreen();
                if (adapt.Index is < 0 or > 2) return Mismatch("机变选项超出范围");
                if (AdaptConfirm(v, adapt.Index) is { } button)
                {
                    steps.Add(button);
                    return plan($"确认第 {adapt.Index + 1} 项机变");
                }
                steps.Add(new TapStep(.25 + .25 * adapt.Index, .5));
                var choice = plan($"选择第 {adapt.Index + 1} 项机变");
                return choice with { Plan = choice.Plan! with { Continue = true } };
            case PickCustomAction custom:
                if (v.Custom is null) return WrongScreen();
                if (v.Custom.IsFinal || custom.Tier is not (5 or 6) || custom.Operators is null ||
                    custom.Operators.Length > 2 || custom.Operators.Distinct().Count() != custom.Operators.Length)
                    return Mismatch("自选编队已结束或选择人数、阶级不符");
                if (custom.Operators.Length == 0)
                {
                    var ready = Region(v, (int)(v.W * .73), (int)(v.H * .84), (int)(v.W * .27), (int)(v.H * .16))
                        .FirstOrDefault(t => t.Text.Replace(" ", "").Contains("准备就绪"));
                    if (ready == null) return Mismatch("读不到自选编队的准备就绪按钮");
                    steps.Add(new TapStep((ready.Box.X + ready.Box.W * .5) / v.W, (ready.Box.Y + ready.Box.H * .5) / v.H));
                    return plan("放弃自选编队");
                }
                var row = v.Custom.Cards.Where(c => c.Tier == custom.Tier).ToArray();
                var available = row.Where(c => c.State != CustomPickCardState.Banned).ToArray();
                if (custom.Operators.Any(n => !available.Any(c => c.Name == n))) return Mismatch("找不到可选的目标干员");
                var chosen = new List<CustomPickCard>();
                if (available.Any(c => c.State == CustomPickCardState.Unknown))
                {
                    using var stream = new MemoryStream(v.Frame.Image);
                    using var bitmap = new Bitmap(stream);
                    chosen.AddRange(available.Where(c => SelectedBorder(bitmap, v.S, v.Custom, c)));
                    string count = v.Custom.CountAsync(_ocr, v.Frame.Image, v.S, custom.Tier).GetAwaiter().GetResult();
                    if (count != $"{chosen.Count}/2") return Mismatch("自选人数和卡片选中框不一致");
                }
                var wanted = custom.Operators.ToHashSet();
                var remove = chosen.Where(c => c.Name is null || !wanted.Contains(c.Name));
                var add = available.Where(c => c.Name is not null && wanted.Contains(c.Name) && !chosen.Contains(c));
                foreach (var card in remove.Concat(add))
                    steps.Add(new TapStep(v.Custom.X(810 + 177 * card.Slot) * v.S / v.W, v.Custom.Y(290 + (custom.Tier - 5) * 400, custom.Tier) * v.S / v.H));
                steps.Add(v.Center(1715, 995));
                return plan($"确认 {custom.Tier} 阶自选编队（{string.Join("、", custom.Operators)}）");
            default: throw new ArgumentOutOfRangeException(nameof(a));
        }
    }

    // 选择中的卡左侧有贯穿卡身的金/橙色竖边；用右侧人数读数复核，避免重复点已选卡。
    private static bool SelectedBorder(Bitmap image, double scale, CustomPickReading layout, CustomPickCard card)
    {
        int top = 180 + (card.Tier - 5) * 400;
        for (int x = (int)(layout.X(715 + 177 * card.Slot) * scale); x <= layout.X(735 + 177 * card.Slot) * scale; x++)
        {
            int warm = 0;
            for (int y = top; y < top + 260; y += 10)
            {
                var c = image.GetPixel(x, (int)(layout.Y(y, card.Tier) * scale));
                if (c.R > 90 && c.R > c.G * 1.15 && c.G > c.B * 1.2) warm++;
            }
            if (warm >= 22) return true;
        }
        return false;
    }

    private TapStep? StrategyIcon(View v, string id, bool detail)
    {
        var reader = new MatchStrategyReader(_ocr, _data, _strategies);
        var found = reader.VisibleOptions(v.Frame.Image, v.W, v.H, detail).GetAwaiter().GetResult()
            .FirstOrDefault(s => s.Option.Id == id);
        return found.Option == null ? null : new(found.X, found.Y);
    }
}
