using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// 从仓库根目录运行，第二个参数可指定换季后的版本路径。
var root = JsonNode.Parse(File.ReadAllText(args[0]))!;
var season = args.Length > 1 ? args[1] : "act3autochess/V3_1";
var data = root["activity"]!["AUTOCHESS"]!;
foreach (var part in season.Split('/')) data = data[part]!;
JsonNode Read(string file) => JsonNode.Parse(File.ReadAllText($"data/{file}.json"))!;
string Text(JsonNode? node) => node!.GetValue<string>();
string Clean(string text) => Regex.Replace(text, @"<[@$][^>]*>|</>", "").Replace("\\n", "\n");
var equipment = Read("equipment")["equipment"]!.AsArray();
var operators = Read("operators")["operators"]!.AsArray();
var previousOperators = Read("codex_extra")["operators"]!;
var names = equipment.Select(e => Text(e!["name"])).ToHashSet();
var ids = Read("covenant_ids").AsObject().Select(p => Text(p.Value)).ToHashSet();
var skins = Read("skin_avatar_map");
// 皮肤映射只覆盖部分干员；完整头像目录的文件名也带角色编号。
foreach (var directory in Directory.GetDirectories("data/operator_avatars"))
    foreach (var file in Directory.GetFiles(directory, "*.png"))
        skins[Path.GetFileName(file)] = Path.GetFileName(directory);
// 策略赠送棋子没有头像模板，使用数据表中的角色编号。
skins["char_618_acrbot.png"] = "暴躁铁皮";
var traps = data["trapChessDataDict"]!.AsObject();
var effects = data["effectInfoDataDict"]!;
string EquipmentName(string id) => Text(effects[Text(traps[id]!["effectId"])]!["effectName"]);
var extraEquipment = new JsonObject();
var extraOperators = new JsonObject();
var extraBosses = new JsonObject();
var unmatched = new HashSet<string>();
var patched = new List<string>();
foreach (var (id, trap) in traps)
{
    var name = EquipmentName(id);
    if (!names.Contains(name)) { unmatched.Add(name); continue; }
    var extra = (JsonObject)(extraEquipment[name] ??= new JsonObject());
    // 道具编号就是棋子编号；同名装备优先普通版，其余编号保留作导入别名。
    var itemId = Text(trap!["chessId"]);
    if (extra["itemId"] is null) extra["itemId"] = itemId;
    else
    {
        var aliases = (JsonArray)(extra["itemAliases"] ??= new JsonArray());
        if (!trap["isGolden"]!.GetValue<bool>())
        {
            aliases.Add(extra["itemId"]!.DeepClone());
            extra["itemId"] = itemId;
        }
        else aliases.Add(itemId);
    }
    if (trap!["isGolden"]!.GetValue<bool>())
    {
        var original = equipment.Single(e => Text(e!["name"]) == name)!;
        if (Text(original["elite"]!["effectDescription"]) == "")
        {
            extra["elite"] = Clean(Text(effects[Text(trap["effectId"])]!["effectDesc"]));
            patched.Add(name);
        }
        continue;
    }
    if (trap["canFuse"]!.GetValue<bool>())
        extra["fuse"] = new JsonObject { ["with"] = EquipmentName(Text(trap["fuseWith"])), ["result"] = EquipmentName(Text(trap["fuseResult"])) };
    if (trap["giveBondId"] is { } bond && Text(bond) != "")
    {
        var bondId = Text(bond).Replace("Ship", "");
        if (!ids.Contains(bondId)) throw new InvalidDataException($"未知盟约：{bondId}");
        extra["transformBond"] = bondId;
    }
}
var unmatchedSlots = new List<string>();
foreach (var op in operators)
{
    var name = Text(op!["name"]);
    // 自选名单来自攻略目录，游戏表只有空格子；保留已录入的角色编号与装备栏。
    if (op["diy"]?.GetValue<bool>() == true)
    {
        extraOperators[name] = previousOperators[name]!.DeepClone();
        continue;
    }
    var slots = new JsonObject { ["normal"] = 2, ["elite"] = 2 };
    var matched = new HashSet<string>();
    string? operatorCharId = null;
    foreach (var (_, shop) in data["charShopChessDatas"]!.AsObject())
    {
        if (shop!["charId"] is not { } charId || skins[Text(charId) + ".png"]?.GetValue<string>() != name) continue;
        operatorCharId = Text(charId);
        foreach (var chessId in new[] { Text(shop["chessId"]), Text(shop["goldenChessId"]) })
        {
            var chess = data["charChessDataDict"]![chessId]!;
            var kind = chess["isGolden"]!.GetValue<bool>() ? "elite" : "normal";
            slots[kind] = chess["equipSlotNum"]!.DeepClone();
            matched.Add(kind);
        }
    }
    if (matched.Count < 2) unmatchedSlots.Add(name);
    extraOperators[name] = new JsonObject { ["equipSlots"] = slots };
    if (operatorCharId is not null) extraOperators[name]!["charId"] = operatorCharId;
}
var unreadable = new List<string>();
var unmatchedOperators = new HashSet<string>();
foreach (var (id, ability) in data["garrisonDataDict"]!.AsObject())
{
    if (Text(ability!["effectType"]) is not ("SERVER_POOL_EQUIP" or "SERVER_GAIN_EQUIP")) continue;
    var description = Clean(Text(ability["description"]));
    var gifts = names.Where(description.Contains).ToArray();
    // 特殊维式重锤是同一系列，接口以基础件表示。
    if (description.Contains("维式重锤")) gifts = ["维式重锤"];
    if (gifts.Length == 0) { unreadable.Add(id + ": " + description); continue; }
    foreach (var (_, shop) in data["charShopChessDatas"]!.AsObject())
    {
        var chessIds = new[] { Text(shop!["chessId"]), Text(shop["goldenChessId"]) };
        if (!chessIds.Any(c => data["charChessDataDict"]![c]?["garrisonIds"]?.AsArray().Any(g => Text(g) == id) == true)) continue;
        var charId = Text(shop["charId"]);
        var name = skins[charId + ".png"]?.GetValue<string>();
        if (name == null || !operators.Any(o => Text(o!["name"]) == name)) { unmatchedOperators.Add(charId); continue; }
        var op = (JsonObject)(extraOperators[name] ??= new JsonObject { ["givesEquipment"] = new JsonArray() });
        foreach (var gift in gifts)
        {
            var list = (JsonArray)(op["givesEquipment"] ??= new JsonArray());
            if (!list.Any(x => Text(x) == gift)) list.Add(gift);
            foreach (var equipmentName in names.Where(n => n.Contains(gift)))
            {
                var e = (JsonObject)(extraEquipment[equipmentName] ??= new JsonObject());
                var sources = (JsonArray)(e["sources"] ??= new JsonArray());
                if (!sources.Any(x => Text(x) == name)) sources.Add(name);
            }
        }
    }
}
var allModes = data["modeDataDict"]!.AsObject().Select(p => p.Value!).OrderBy(m => m["sortId"]!.GetValue<int>()).ToArray();
var modes = allModes.Where(m => Text(m["modeId"]).StartsWith("mode_single_") || Text(m["modeId"]).StartsWith("mode_multi_")).ToArray();
var bossMap = root["autoChessData"]!["bossInfoDict"]!.AsObject();
var bossData = data["bossInfoDict"]!.AsObject();
var used = new HashSet<string>();
var unmatchedBosses = new List<string>();
foreach (var boss in Read("bosses")["bosses"]!.AsArray())
{
    var enemyId = Text(boss!["enemyId"]);
    var normal = bossMap.SingleOrDefault(p => Text(p.Value!["enemyId"]) == enemyId && bossData.ContainsKey(p.Key));
    if (normal.Key == null) { unmatchedBosses.Add(enemyId); continue; }
    var core = bossMap.SingleOrDefault(p => Text(p.Value!["enemyId"]) == enemyId + "_2" && bossData.ContainsKey(p.Key));
    used.Add(normal.Key);
    if (core.Key != null) used.Add(core.Key);
    var hp = new JsonArray();
    foreach (var mode in modes)
    {
        var modeId = Text(mode["modeId"]);
        var entry = new JsonObject { ["mode"] = modeId, ["name"] = Text(mode["name"]),
            ["group"] = modeId.StartsWith("mode_single_") ? "single" : "multi",
            ["hp"] = bossData[normal.Key]!["bloodDataDict"]![modeId]!.DeepClone() };
        if (core.Key != null) entry["coreHp"] = bossData[core.Key]!["bloodDataDict"]![modeId]!.DeepClone();
        hp.Add(entry);
    }
    extraBosses[enemyId] = new JsonObject { ["hidingCore"] = bossData[core.Key ?? normal.Key]!["isHidingBoss"]!.DeepClone(), ["hp"] = hp };
}
var output = new JsonObject { ["equipment"] = extraEquipment, ["operators"] = extraOperators, ["bosses"] = extraBosses,
    ["modes"] = new JsonArray(allModes.Select(m => (JsonNode)new JsonObject { ["id"] = Text(m["modeId"]), ["name"] = Text(m["name"]) }).ToArray()) };
// 技能与模组由 generate-operator-options.mjs 从攻略资源生成，本脚本保留这些字段。
foreach (var (name, op) in extraOperators)
    foreach (var field in new[] { "skills", "modules", "defaultSkill", "defaultModule" })
        if (previousOperators[name] is JsonObject previous && previous.ContainsKey(field))
            op![field] = previous[field]?.DeepClone();
File.WriteAllText("data/codex_extra.json", output.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
// 预览目录保留既有内容和顺序，只同步本次新增的编号。
var mockPath = "src/ui/src/lib/mock/catalog.json";
var mock = JsonNode.Parse(File.ReadAllText(mockPath))!;
foreach (var (category, field) in new[] { ("operators", "charId"), ("equipment", "itemId") })
    foreach (var entry in mock[category]!.AsArray())
    {
        entry![field] = output[category]![Text(entry["name"])]![field]!.DeepClone();
        if (category != "equipment") continue;
        entry.AsObject().Remove("iconId");
        entry.AsObject().Remove("itemAliases");
        if (output[category]![Text(entry["name"])]!["itemAliases"] is { } aliases)
            entry["itemAliases"] = aliases.DeepClone();
    }
var mockJson = mock.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
File.WriteAllText(mockPath, Regex.Replace(mockJson, @"(?m)^( +)", m => new string(' ', m.Length / 2)) + "\n");
Console.WriteLine($"装备未匹配：{string.Join('、', unmatched)}\n补进阶：{string.Join('、', patched.Distinct())}\n来源未读出：{string.Join('、', unreadable)}\n干员未匹配：{string.Join('、', unmatchedOperators)}\n其他首领：{string.Join('、', bossMap.Where(p => !used.Contains(p.Key)).Select(p => p.Key + ":" + Text(p.Value!["enemyId"])))}\n输出：{extraBosses.Count} 个首领，每个 {modes.Length} 档；{extraOperators.Count(p => p.Value!["givesEquipment"] is not null)} 名装备来源干员。");
Console.WriteLine($"本地装备未在表中找到：{string.Join('、', names.Except(extraEquipment.Select(p => p.Key)))}\n首领未匹配：{string.Join('、', unmatchedBosses)}");
Console.WriteLine($"装备栏未匹配（默认 2 / 2）：{string.Join('、', unmatchedSlots)}");
Console.WriteLine($"干员编号未匹配：{string.Join('、', operators.Select(o => Text(o!["name"])).Where(n => extraOperators[n]?["charId"] is null))}");
Console.WriteLine($"装备道具未匹配：{string.Join('、', names.Where(n => extraEquipment[n]?["itemId"] is null))}；带别名：{extraEquipment.Count(p => p.Value!["itemAliases"] is not null)} 件");
