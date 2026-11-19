using System.Drawing;
using System.Text.Json;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Control;

public sealed partial class ActionCompiler
{
    private static bool StrategyPage(View v) => v.Heading.Contains("选择策略") || v.Heading.Contains("请选择策略");
    private static bool AdaptPage(View v) => v.Heading.Contains("机变");
    private CompileResult CompileOpening(GameAction a, View v, List<InputStep> steps, Func<string, CompileResult> plan)
    {
        switch (a)
        {
            case PickStrategyAction strategy:
                if (!StrategyPage(v)) return WrongScreen();
                if (!_strategies.TryGetValue(strategy.StrategyId, out var initiator)) return Mismatch("没有这个策略");
                if (!v.Heading.Contains("请选择策略")) return Mismatch("当前没有轮到选择策略");
                var details = Join(Region(v, (int)(v.W * .78), (int)(v.H * .13), (int)(v.W * .22), (int)(v.H * .86)));
                string confirm = Join(Region(v, (int)(v.W * .78), (int)(v.H * .86), (int)(v.W * .22), (int)(v.H * .13)));
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
                // TODO(design): 三选一卡面与确认键的中心位置待实机核实。
                steps.Add(v.Center(550 + 410 * adapt.Index, 540));
                steps.Add(new WaitStep(350));
                steps.Add(v.Center(1700, 997));
                return plan($"选择第 {adapt.Index + 1} 项机变");
            case PickCustomAction custom:
                if (v.Custom is null) return WrongScreen();
                if (v.Custom.IsFinal || custom.Tier is not (5 or 6) || custom.Operators is null ||
                    custom.Operators.Length > 2 || custom.Operators.Distinct().Count() != custom.Operators.Length)
                    return Mismatch("自选编队已结束或选择人数、阶级不符");
                var row = v.Custom.Cards.Where(c => c.Tier == custom.Tier).ToArray();
                var available = row.Where(c => c.State != CustomPickCardState.Banned).ToArray();
                if (custom.Operators.Any(n => !available.Any(c => c.Name == n))) return Mismatch("找不到可选的目标干员");
                var chosen = new List<CustomPickCard>();
                if (available.Any(c => c.State == CustomPickCardState.Unknown))
                {
                    using var stream = new MemoryStream(v.Frame.Image);
                    using var bitmap = new Bitmap(stream);
                    chosen.AddRange(available.Where(c => SelectedBorder(bitmap, v.S, c)));
                    int off = (custom.Tier - 5) * 400;
                    string count = Join(_ocr.RecognizeLineAsync(v.Frame.Image, (int)(1832 * v.S), (int)((300 + off) * v.S),
                        (int)(60 * v.S), (int)(38 * v.S)).GetAwaiter().GetResult());
                    if (count != $"{chosen.Count}/2") return Mismatch("自选人数和卡片选中框不一致");
                }
                var wanted = custom.Operators.ToHashSet();
                var remove = chosen.Where(c => c.Name is null || !wanted.Contains(c.Name));
                var add = available.Where(c => c.Name is not null && wanted.Contains(c.Name) && !chosen.Contains(c));
                foreach (var card in remove.Concat(add))
                    steps.Add(new TapStep((810 + 177 * card.Slot) * v.S / v.W, (290 + (custom.Tier - 5) * 400) * v.S / v.H));
                steps.Add(v.Center(1715, 995));
                return plan($"确认 {custom.Tier} 阶自选编队（{string.Join("、", custom.Operators)}）");
            default: throw new ArgumentOutOfRangeException(nameof(a));
        }
    }

    // 选择中的卡左侧有贯穿卡身的金/橙色竖边；用右侧人数读数复核，避免重复点已选卡。
    private static bool SelectedBorder(Bitmap image, double scale, CustomPickCard card)
    {
        int top = 180 + (card.Tier - 5) * 400;
        for (int x = (int)((715 + 177 * card.Slot) * scale); x <= (735 + 177 * card.Slot) * scale; x++)
        {
            int warm = 0;
            for (int y = top; y < top + 260; y += 10)
            {
                var c = image.GetPixel(x, (int)(y * scale));
                if (c.R > 90 && c.R > c.G * 1.15 && c.G > c.B * 1.2) warm++;
            }
            if (warm >= 22) return true;
        }
        return false;
    }

    // 圆内头像沿用策略识别的32×32灰度特征。详情打开后列表向左平移。
    private TapStep? StrategyIcon(View v, string id, bool detail)
    {
        var portraits = JsonSerializer.Deserialize<Dictionary<string, double[]>>(
            File.ReadAllBytes(Path.Combine(_data, "strategy_portraits.json")))!;
        if (!portraits.ContainsKey(id)) return null;
        static double[] Normalize(double[] x)
        {
            double mean = x.Average();
            double length = Math.Sqrt(x.Sum(a => (a - mean) * (a - mean)));
            return x.Select(a => (a - mean) / Math.Max(1, length)).ToArray();
        }
        var templates = portraits.ToDictionary(p => p.Key, p => Normalize(p.Value));
        using var stream = new MemoryStream(v.Frame.Image);
        using var bitmap = new Bitmap(stream);
        TapStep? best = null;
        double bestScore = .68;
        var order = _strategies.Keys.ToArray();
        HashSet<int> matchingSlots = [];
        for (int row = 0; row < 5; row++)
            for (int col = 0; col < 5; col++)
            {
                var center = v.Center((detail ? 610 : 966) + col * 189, 332 + row * 189);
                int x = (int)(center.X * v.W), y = (int)(center.Y * v.H);
                int slot = row * 5 + col;
                foreach (int radius in new[] { 58, 68, 76, 84 })
                {
                    int r = (int)(radius * v.S);
                    if (y + r >= v.H || y - r < 0) continue;
                    using var small = new Bitmap(32, 32);
                    using (var g = Graphics.FromImage(small))
                        g.DrawImage(bitmap, new Rectangle(0, 0, 32, 32), new Rectangle(x - r, y - r, 2 * r, 2 * r), GraphicsUnit.Pixel);
                    List<double> values = [];
                    for (int j = 0; j < 32; j++)
                        for (int i = 0; i < 32; i++)
                        {
                            if ((i - 15.5) * (i - 15.5) + (j - 15.5) * (j - 15.5) >= 144) continue;
                            var c = small.GetPixel(i, j);
                            values.Add(.299 * c.R + .587 * c.G + .114 * c.B);
                        }
                    var sample = Normalize(values.ToArray());
                    double Score(double[] t) => t.Zip(sample, (a, b) => a * b).Sum();
                    if (templates.TryGetValue(order[slot], out var ordered) && Score(ordered) > .67 &&
                        templates.All(t => t.Key == order[slot] || Score(t.Value) < Score(ordered) - .08)) matchingSlots.Add(slot);
                    double score = Score(templates[id]);
                    if (score <= bestScore || templates.Any(t => t.Key != id && Score(t.Value) > score - .1)) continue;
                    best = center;
                    bestScore = score;
                }
            }
        if (best is not null) return best;
        // 首屏的通用型列表按目录顺序排列。至少两枚独立头像和分组标题吻合才借用顺序，
        // 最下一排只露出顶部时也能点击可见部分；滚动后的列表不按序号盲点。
        int index = Array.IndexOf(order, id);
        var group = Join(Region(v, (int)(v.W * .25), (int)(v.H * .12), (int)(v.W * .5), (int)(v.H * .12)));
        if (matchingSlots.Count < 2 || !group.Contains("泛用型策略") || index is < 0 or >= 25) return null;
        return v.Center((detail ? 610 : 966) + index % 5 * 189, Math.Min(1040, 332 + index / 5 * 189));
    }
}
