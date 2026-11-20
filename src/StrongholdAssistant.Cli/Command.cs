using System.Text.Json;
using System.Text.Json.Nodes;

namespace StrongholdAssistant.Cli;

internal sealed record Command(string Method, string Path, object? Body = null, string? FileName = null, bool Wait = false)
{
    internal static readonly Dictionary<string, string> Usage = new()
    {
        ["page"] = "page                         读取当前页面和选中的规程等级",
        ["start"] = "start <等级>                 在规程主界面或难度页开始单人独立模拟（1～20）",
        ["leave"] = "leave                        从结算页回到规程主界面",
        ["info"] = "info                         读取实例、连接和操作设置",
        ["state"] = "state                        读取当前局面",
        ["turn"] = "turn [--wait 秒] [--after turnId]  读取决策点，可等待与给定编号不同的决策点（最多 600 秒）",
        ["adapt"] = "adapt <1～3>                  选择从左往右第 1～3 张机变卡",
        ["strategy"] = "strategy <id 或名字>          选择策略",
        ["custom"] = "custom <阶> <名字>…           确认 5 或 6 阶自选编队，最多两人",
        ["schedule"] = "schedule                     读取按游戏数据推算的回合与可能敌人",
        ["field"] = "field [--recognize]          读取场上，或重新识别一次",
        ["placement"] = "placement                    读取站位推荐",
        ["screenshot"] = "screenshot <文件>            保存当前游戏画面为 PNG",
        ["do"] = "do <JSON 或 @文件>           下发一个动作或动作数组",
        ["buy"] = "buy <卡位>                   购买从左往右第 1～6 张卡",
        ["sell"] = "sell <格>                    卖出指定格的干员",
        ["move"] = "move <起点> <终点> [--facing up|down|left|right] [--expect 名字]  移动干员",
        ["refresh"] = "refresh                      刷新商店",
        ["freeze"] = "freeze on|off                冻结或解冻商店",
        ["upgrade"] = "upgrade                      升级",
        ["ready"] = "ready [on|off]               准备或取消准备，默认 on",
        ["place"] = "place                        执行当前站位推荐",
        ["job"] = "job [id] [--wait]            查询指定批次或最近一批",
        ["stop"] = "stop                         停止当前批次"
    };

    internal static string Help(string? name)
    {
        IEnumerable<string> lines = name == null ? Usage.Values : Usage.TryGetValue(name, out var line)
            ? [line] : throw new ArgumentException("未知命令");
        if (name is "turn" or "adapt")
            return "用法：sa [--profile 配置名] " + Usage[name]
                + (name == "adapt" ? "\n可加 --mode dryRun|confirm|auto 和 --wait 等到结束。" : "");
        return "用法：sa [--profile 配置名] <命令> [参数]\n" + string.Join('\n', lines)
            + "\n操作命令可加 --mode dryRun|confirm|auto 和 --wait 等到结束。逐步确认请在助手界面完成。"
            + "\n棋盘格写作 D7（行字母、列数字），整备区写作 bench:3（第 3 格）。卡位从左到右为 1～6。"
            + "\n示例：sa --profile agent field --recognize\n      sa move bench:3 D7 --facing right --mode dryRun --wait\n      sa do @actions.json --wait";
    }

    internal static string? Option(List<string> args, string key)
    {
        int index = args.IndexOf(key);
        if (index < 0) return null;
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--")) throw new ArgumentException($"{key} 缺少参数");
        var result = args[index + 1];
        args.RemoveRange(index, 2);
        return result;
    }

    internal static object Cell(string text)
    {
        if (text.StartsWith("bench:") && int.TryParse(text.AsSpan(6), out int bench) && bench is >= 1 and <= 10)
            return new { zone = "bench", col = bench, row = 0 };
        if (text.Length >= 2 && char.ToUpperInvariant(text[0]) is >= 'A' and <= 'E' &&
            int.TryParse(text.AsSpan(1), out int column) && column is >= 1 and <= 9)
            return new { zone = "board", col = column - 3, row = char.ToUpperInvariant(text[0]) - 'A' };
        throw new ArgumentException("格子写作 D7 或 bench:3");
    }

    private static Command ParseMenu(string name, List<string> args)
    {
        if (name == "page" && args.Count == 0) return new("GET", "page");
        if (name == "page") throw new ArgumentException($"用法：sa {Usage[name]}");
        var mode = Option(args, "--mode");
        if (mode is not (null or "dryRun" or "confirm" or "auto")) throw new ArgumentException("执行模式无效");
        bool wait = args.Remove("--wait");
        object action = name switch
        {
            "start" when args.Count == 1 && int.TryParse(args[0], out int level) && level is >= 1 and <= 20
                => new { type = "startSolo", level },
            "leave" when args.Count == 0 => new { type = "leaveResult" },
            _ => throw new ArgumentException($"用法：sa {Usage[name]}")
        };
        var body = new Dictionary<string, object> { ["actions"] = new[] { action } };
        if (mode != null) body["mode"] = mode;
        return new("POST", "actions", body, Wait: wait);
    }

    internal static Command Parse(List<string> args)
    {
        if (args.Count == 0 || !Usage.ContainsKey(args[0])) throw new ArgumentException("请用 sa --help 查看命令");
        var name = args[0];
        args.RemoveAt(0);
        if (name is "start" or "leave" or "page") return ParseMenu(name, args);
        bool action = name is "adapt" or "custom" or "strategy" or "do" or "buy" or "sell" or "move" or "refresh" or "freeze" or "upgrade" or "ready" or "place";
        string? mode = action ? Option(args, "--mode") : null;
        if (mode is not (null or "dryRun" or "confirm" or "auto")) throw new ArgumentException("执行模式无效");
        bool wait = (action || name == "job") && args.Remove("--wait");
        var facing = name == "move" ? Option(args, "--facing") : null;
        if (facing is not (null or "up" or "down" or "left" or "right")) throw new ArgumentException("朝向无效");
        var expect = name == "move" ? Option(args, "--expect") : null;
        bool recognize = name == "field" && args.Remove("--recognize");
        if (name == "turn")
        {
            var seconds = Option(args, "--wait") ?? "0";
            var after = Option(args, "--after");
            if (args.Count != 0 || !int.TryParse(seconds, out var n) || n < 0)
                throw new ArgumentException($"用法：sa {Usage[name]}");
            return new("GET", $"turn?wait={Math.Min(n, 600)}" + (after == null ? "" : "&after=" + Uri.EscapeDataString(after)));
        }
        int count = args.Count;
        if (name == "job" && count <= 1 && (count == 0 || !args[0].StartsWith("--")))
            return new("GET", "jobs/" + (count == 0 ? "current" : Uri.EscapeDataString(args[0])) + (wait ? "?wait=120" : ""), Wait: wait);
        if (count == 0 && name is "schedule" or "info" or "state" or "field" or "placement")
            return recognize ? new("POST", "field/recognize") : new("GET", name);
        if (name == "screenshot" && count == 1) return new("GET", name, FileName: args[0]);
        if (name == "stop" && count == 0) return new("POST", "stop");
        var body = new Dictionary<string, object>();
        if (mode != null) body["mode"] = mode;
        if (name == "place" && count == 0) return new("POST", "placement/run", body, Wait: wait);
        object single = name switch
        {
            "buy" when count == 1 && int.TryParse(args[0], out int n) && n is >= 1 and <= 6 => new { type = name, slot = n - 1 },
            "adapt" when count == 1 && int.TryParse(args[0], out int i) && i is >= 1 and <= 3 => new { type = "pickAdapt", index = i - 1 },
            "strategy" when count == 1 => new { type = "pickStrategy", strategyId = args[0] },
            "custom" when count is >= 1 and <= 3 && int.TryParse(args[0], out int tier) && tier is 5 or 6 =>
                new { type = "pickCustom", tier, operators = args.Skip(1).ToArray() },
            "sell" when count == 1 => new { type = name, from = Cell(args[0]) },
            "move" when count == 2 => new { type = name, from = Cell(args[0]), to = Cell(args[1]), facing, expect },
            "refresh" or "upgrade" when count == 0 => new { type = name },
            "ready" when count == 0 => new { type = name, on = true },
            "freeze" or "ready" when count == 1 && args[0] is "on" or "off" => new { type = name, on = args[0] == "on" },
            "do" when count == 1 => JsonNode.Parse(args[0].StartsWith('@') ? File.ReadAllText(args[0][1..]) : args[0])
                ?? throw new ArgumentException("请提供动作 JSON"),
            _ => throw new ArgumentException($"用法：sa {Usage[name]}")
        };
        if (name == "do" && single is not (JsonArray or JsonObject)) throw new ArgumentException("动作应为对象或数组");
        body["actions"] = single is JsonArray ? single : new[] { single };
        return new("POST", "actions", body, Wait: wait);
    }
}

