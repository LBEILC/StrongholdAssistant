using System.Drawing;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Control;

public sealed record MenuPage(string Page, int? Level);
internal sealed record LevelRow(int Number, TapStep Center, bool Locked);

public static class MenuPages
{
    internal sealed record Reading(MenuPage Page, LevelRow[] Rows, TapStep? Button, string? Label);

    public static async Task<MenuPage> IdentifyAsync(IOcrEngine ocr, byte[] image) =>
        (await ReadAsync(ocr, image)).Page;

    internal static async Task<Reading> ReadAsync(IOcrEngine ocr, byte[] image)
    {
        using var stream = new MemoryStream(image);
        using var bitmap = new Bitmap(stream);
        int w = bitmap.Width, h = bitmap.Height;
        var lines = (await ocr.RecognizeRegionAsync(image, 0, 0, w, h))
            .Select(l => l with { Text = l.Text.Replace(" ", "") }).ToArray();
        TapStep Center(OcrTextResult line) => new((line.Box.X + line.Box.W * .5) / w, (line.Box.Y + line.Box.H * .5) / h);
        var heading = string.Concat(lines.Where(l => Center(l).Y < .16).Select(l => l.Text));
        OcrTextResult? Button(string label) => lines.FirstOrDefault(l => l.Text.Contains(label) && Center(l) is { X: > .55, Y: > .76 });
        Reading Page(string name, OcrTextResult? button = null) => new(new(name, null), [], button == null ? null : Center(button), button?.Text);
        if (Button("独立模拟") is { } solo && Button("极速模拟") != null) return Page("lobby", solo);
        if (lines.Any(l => l.Text.Contains("物资调配处")) && lines.Any(l => l.Text.Contains("协议数据库")))
            return Page("lobby", lines.FirstOrDefault(l => l.Text == "个人模拟" && Center(l).Y > .7));

        if (heading.Contains("请选择模拟难度"))
        {
            var labels = lines.Select(l => (Line: l, Match: Regex.Match(l.Text, @"规(?:程等|桂寺)级(\d{1,2})(?!\d)")))
                .Where(p => p.Match.Success).Select(p => (p.Line, Number: int.Parse(p.Match.Groups[1].Value))).ToArray();
            var rows = new List<LevelRow>();
            // 左下常驻等级是已选值；右侧列表还可用「当前选择」箭头核对。
            int? level = labels.Where(p => Center(p.Line) is { X: < .3, Y: > .8 })
                .Select(p => (int?)p.Number).FirstOrDefault();
            var marker = lines.FirstOrDefault(l => l.Text.Contains("当前选择"));
            foreach (var (line, number) in labels.Where(p => Center(p.Line).X >= .65))
            {
                var c = Center(line);
                var nearby = lines.Where(l => Math.Abs(Center(l).Y - c.Y) < .04 && Center(l).X >= .65);
                if (marker != null && Math.Abs(Center(marker).Y - c.Y) < .055) level ??= number;
                // 未选中的行字本来就暗，不能当锁；真锁是图标上的挂锁，点了选不中会由核对报出。
                bool locked = nearby.Any(l => l.Text.Contains("未解锁") || l.Text.Contains("尚未开放"));
                rows.Add(new(number, c, locked));
            }
            var start = Button("开始模拟");
            return new(new("difficulty", level), rows.ToArray(), start == null ? null : Center(start), start?.Text);
        }
        var result = lines.Any(l => l.Text.Contains("模拟完成") || l.Text.Contains("模拟失败") ||
            l.Text.Contains("模拟结束") || l.Text == "模拟中断");
        if (result) return Page("result", Button("下一步") ?? Button("返回主页") ?? Button("返回主界面") ?? Button("返回"));
        if (heading.Contains("自选编队")) return Page("custom");
        if (heading.Contains("策略") && (heading.Contains("选择") || heading.Contains("选定"))) return Page("strategy");
        if (heading.Contains("机变")) return Page("adapt");
        if (heading.Contains("回合"))
        {
            if (heading.Contains("休整") || heading.Contains("休息")) return Page("rest");
            if (heading.Contains("作战") || heading.Contains("战斗")) return Page("battle");
        }
        var openingLevel = lines.Where(l => Center(l).Y < .16).Select(l => Regex.Match(l.Text, @"规程等级(\d{1,2})")).FirstOrDefault(m => m.Success);
        if (openingLevel != null && lines.Any(l => l.Text.Contains("核心盟约")))
            return new(new("opening", int.Parse(openingLevel.Groups[1].Value)), [], null, null);
        return Page("unknown");
    }

    private static bool DimLabel(Bitmap bitmap, OcrTextResult line)
    {
        var (x, y, w, h) = line.Box;
        int samples = 0, bright = 0;
        for (int j = Math.Max(y, 0); j < Math.Min(y + h, bitmap.Height); j += 3)
        for (int i = Math.Max(x, 0); i < Math.Min(x + w, bitmap.Width); i += 3)
        {
            var pixel = bitmap.GetPixel(i, j);
            if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) >= 100) bright++;
            samples++;
        }
        // 等级 1 原帧中，灰行字形亮度约 50～60；小面积装饰不计入亮字。
        return samples > 0 && bright * 20 < samples;
    }
}

public sealed partial class ActionCompiler
{
    private CompileResult CompileMenu(GameAction action, ControlFrame frame)
    {
        var menu = MenuPages.ReadAsync(_ocr, frame.Image).GetAwaiter().GetResult();
        CompileResult Step(string text, InputStep input, bool more, int settle = 750) =>
            new(new(action, text, [input], settle) { Continue = more }, null, null);
        if (action is LeaveResultAction)
        {
            if (menu.Page.Page != "result") return WrongScreen();
            return menu.Button == null ? Mismatch("结算页读不到返回或下一步") : Step($"离开结算页（{menu.Label}）", menu.Button, true, 2200);
        }
        int target = ((StartSoloAction)action).Level;
        if (target is < 1 or > 20) return Mismatch("规程等级应为 1～20");
        if (menu.Page.Page == "lobby")
        {
            if (frame.State?.ModeId.Value == null && menu.Label != "独立模拟")
                return Mismatch("读不到独立模拟入口");
            return menu.Button == null ? Mismatch("读不到个人模拟入口") : Step($"进入{menu.Label}", menu.Button, true, 3000);
        }
        if (menu.Page.Page != "difficulty") return WrongScreen();
        if (menu.Rows.Length == 0) return Mismatch("读不到列表中的规程等级");
        var row = menu.Rows.FirstOrDefault(r => r.Number == target);
        if (row is { Locked: true }) return Mismatch("该等级未解锁");
        if (menu.Page.Level == target && row != null)
            return menu.Button == null ? Mismatch("读不到开始模拟") : Step($"开始规程等级 {target}", menu.Button, false, 3500);
        if (row != null) return Step($"选择规程等级 {target}", row.Center, true);
        var first = menu.Rows.Min(r => r.Number);
        var last = menu.Rows.Max(r => r.Number);
        if (target >= first && target <= last) return Mismatch("列表中读不到目标等级");
        // 高等级在列表上方，手指向下拖才能把它带入视野。
        bool down = target > last;
        return Step($"滚动等级列表（当前 {first}～{last}）", new DragStep(.8, down ? .32 : .65, .8, down ? .65 : .32, 100, 550, 160), true);
    }

    private VerifyResult VerifyMenu(GameAction action, ControlFrame before, ControlFrame after)
    {
        var b = MenuPages.ReadAsync(_ocr, before.Image).GetAwaiter().GetResult();
        var a = MenuPages.ReadAsync(_ocr, after.Image).GetAwaiter().GetResult();
        bool ok;
        if (action is LeaveResultAction)
            ok = b.Page.Page == "result" && (a.Page.Page == "lobby" || a.Page.Page == "result" && b.Label != a.Label && a.Button != null);
        else
        {
            var target = ((StartSoloAction)action).Level;
            ok = b.Page.Page switch
            {
                "lobby" => a.Page.Page == "difficulty" || a.Page.Page == "lobby" && a.Label?.Contains("独立模拟") == true && b.Label == "个人模拟",
                "difficulty" when b.Page.Level == target && b.Rows.Any(r => r.Number == target) =>
                    a.Page.Page == "opening" ? a.Page.Level == target : a.Page.Page is "strategy" or "custom" or "rest",
                "difficulty" when b.Rows.Any(r => r.Number == target) => a.Page.Page == "difficulty" && a.Page.Level == target,
                "difficulty" => a.Page.Page == "difficulty" && a.Rows.Length > 0 && !b.Rows.Select(r => r.Number).SequenceEqual(a.Rows.Select(r => r.Number)),
                _ => false
            };
        }
        return new(ok, ok ? null : "菜单画面未出现预期变化")
        {
            Complete = action is LeaveResultAction && a.Page.Page == "lobby"
        };
    }
}
