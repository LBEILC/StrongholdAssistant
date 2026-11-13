using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StrongholdAssistant.Host;

public sealed class SklandException(string message) : ArgumentException(message);

// 与界面 types.ts 一致；解析只处理响应正文，不接触登录凭据。
public sealed record SklandOperator(string Id, bool Elite,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Skill = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Module = null);
public sealed record SklandStage(string Type, int StartRound, int EndRound,
    SklandOperator[] Operators, string[] Equipment, string[] Covenants, string Notes);
public sealed record SklandGuide(string Url, string Title, string Author, string[] Strategies,
    string VersionId, SklandStage[] Stages);
public sealed record SklandCovenant(string Id, int Layer);
public sealed record SklandAccount(string Nickname);
public sealed record SklandBoss(string Id, bool Killed);
public sealed record SklandRecord(string Id, string? StrategyId, SklandOperator[] Operators,
    string[] Equipment, bool Success, string Time, string StartTime, int CoinSpent, int Kills,
    SklandCovenant[] Covenants, SklandBoss[] Bosses, string Mode, string Difficulty, int PassRound, string VersionId);

public static class SklandParser
{
    public static SklandAccount? ParseAccount(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("code").GetInt32() != 0) return null;
        var game = Elements(root.GetProperty("data"), "list")
            .FirstOrDefault(g => Option(g, "appCode") == "arknights");
        var bindings = Elements(game, "bindingList").ToArray();
        var role = bindings.FirstOrDefault(b => Optional(b, "isDefault").ValueKind == JsonValueKind.True);
        if (role.ValueKind == JsonValueKind.Undefined) role = bindings.FirstOrDefault();
        return Option(role, "nickname") is { } nickname ? new(nickname) : null;
    }

    // 登录表单出现时重置等待起点，登录完成后给页面完整的 15 秒加载时间。
    internal static bool HistoryWaitExpired(bool visible, bool loginForm, DateTime now, ref DateTime waitingSince)
    {
        if (visible && loginForm) waitingSince = now;
        return now - waitingSince >= TimeSpan.FromSeconds(15);
    }

    public static string NormalizeGuideUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "game.skland.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/arknights/stronghold-guide/detail")
            throw new ArgumentException("这不是森空岛的阵容攻略链接。");
        var ids = uri.Query.TrimStart('?').Split('&').Where(p => p.StartsWith("guideId=", StringComparison.Ordinal))
            .Select(p => p[8..]).ToArray();
        if (ids.Length != 1 || !Regex.IsMatch(ids[0], "^[0-9]+$"))
            throw new ArgumentException("这不是森空岛的阵容攻略链接。");
        return "https://game.skland.com/arknights/stronghold-guide/detail?guideId=" + ids[0];
    }

    public static SklandGuide ParseGuide(string json, string url)
        => ParseResponse(() => ParseGuideCore(json, url), "森空岛攻略的数据结构已变化，暂时无法读取。");

    private static SklandGuide ParseGuideCore(string json, string url)
    {
        using var document = JsonDocument.Parse(json);
        var data = ReadData(document.RootElement, "这篇攻略不存在或已失效。");
        var wrapper = data.GetProperty("guide");
        if (wrapper.GetProperty("invalid").GetInt32() != 0)
            throw new ArgumentException("这篇攻略不存在或已失效。");
        var guide = wrapper.GetProperty("guide");
        var stages = guide.GetProperty("tabs").EnumerateArray().Select(tab =>
        {
            var tabType = tab.GetProperty("tabType").GetInt32();
            var factions = Optional(tab, "activeBonds");
            return new SklandStage(tabType == 1 ? "FORMATION" : tabType.ToString(CultureInfo.InvariantCulture),
                tab.GetProperty("startRound").GetInt32(),
                tab.GetProperty("endRound").GetInt32(), Elements(tab, "recChars")
                    .Select(o => new SklandOperator(Text(o, "charId"), Optional(o, "mergeLevel") is
                        { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } level && level.GetInt32() == 1,
                        Option(o, "skillId"), Option(o, "moduleId"))).ToArray(),
                Elements(tab, "recItems").Select(e => Text(e, "itemId")).ToArray(),
                Strings(factions, "seasonBondIds").Concat(Strings(factions, "regularBondIds"))
                    .Select(BondId).ToArray(),
                Option(tab, "operationIdea") ?? "");
        }).ToArray();
        if (stages.Length == 0) throw new ArgumentException("这篇攻略不存在或已失效。");
        return new(NormalizeGuideUrl(url), Text(guide, "title"), Option(Optional(wrapper, "poster"), "nickname") ?? "",
            Strings(guide, "strategyIds"),
            Text(guide, "versionId"), stages);
    }

    public static SklandRecord[]? ParseHistory(string json)
        => ParseResponse(() => ParseHistoryCore(json), "历史战绩的数据结构已变化，暂时无法读取。");

    private static SklandRecord[]? ParseHistoryCore(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.GetProperty("code").GetInt32() != 0) return null;
        var data = document.RootElement.GetProperty("data");
        return data.GetProperty("records").EnumerateArray().Select(record =>
        {
            var squad = Elements(record, "chessSquad").ToArray();
            var strategy = Text(record, "bandId");
            return new SklandRecord(Text(record, "id"), strategy.Length == 0 ? null : strategy,
                squad.Select(o => new SklandOperator(Text(o, "charId"),
                    Text(o, "chessId") == Text(o.GetProperty("chessInfo"), "goldenChessId"),
                    Option(o, "skillId"), Option(o, "moduleId"))).ToArray(),
                squad.SelectMany(o => Elements(o, "equips"))
                    .Select(e => Text(e, "equipId")).ToArray(),
                record.GetProperty("completeState").GetInt32() == 1,
                Timestamp(record, "gameEndTs"), Timestamp(record, "gameStartTs"),
                record.GetProperty("coinCost").GetInt32(), record.GetProperty("killCnt").GetInt32(),
                Elements(record, "bondInfo").Select(b =>
                    new SklandCovenant(BondId(Text(b, "bondId")), b.GetProperty("layer").GetInt32())).ToArray(),
                Elements(record, "acBossInfo").Select(b =>
                    new SklandBoss(BossIds.GetValueOrDefault(Text(b, "bossId"), Text(b, "bossId")),
                        b.GetProperty("isKilled").GetBoolean())).ToArray(),
                Text(record, "modeType"), Text(record, "modeDifficulty"), record.GetProperty("passRound").GetInt32(),
                Text(record, "versionId"));
        }).OrderByDescending(r => r.Time, StringComparer.Ordinal).ToArray();
    }

    private static string Timestamp(JsonElement record, string name) => DateTimeOffset
        .FromUnixTimeSeconds(long.Parse(Text(record, name), CultureInfo.InvariantCulture))
        .UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static string BondId(string id) => id.EndsWith("Ship", StringComparison.Ordinal) ? id[..^4] : id;

    // 来源：activity_table.json 的 autoChessData.bossInfoDict.enemyId，对照 data/bosses.json。
    // 隐秘核心 _2 变体归到同一 Boss.id；boss_5 不在图鉴，保留原编号。
    private static readonly Dictionary<string, string> BossIds = new()
    {
        ["boss_1"] = "enemy_9013_acstmk",
        ["boss_2"] = "enemy_9017_achunt",
        ["boss_3"] = "enemy_9021_acduml",
        ["boss_4"] = "enemy_1521_dslily",
        ["boss_6"] = "enemy_9032_aclionk",
        ["boss_7"] = "enemy_9033_acdeer",
        ["boss_8"] = "enemy_9013_acstmk",
        ["boss_9"] = "enemy_9017_achunt",
        ["boss_10"] = "enemy_9021_acduml",
        ["boss_11"] = "enemy_9029_acfnc",
        ["boss_12"] = "enemy_9035_acspider",
        ["boss_13"] = "enemy_9037_aceel",
        ["boss_14"] = "enemy_9029_acfnc",
        ["boss_15"] = "enemy_9035_acspider",
        ["boss_16"] = "enemy_9037_aceel",
        ["boss_17"] = "enemy_2124_dyjsfg",
    };

    private static JsonElement ReadData(JsonElement root, string error)
    {
        if (root.GetProperty("code").GetInt32() != 0) throw new ArgumentException(error);
        return root.GetProperty("data");
    }

    private static T ParseResponse<T>(Func<T> parse, string message)
    {
        try { return parse(); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
            or FormatException or OverflowException)
        {
            Trace.WriteLine($"{message} {ex}");
            // 数字格式异常会回显原始值；不向界面返回该值或含本机路径的堆栈。
            var detail = ex is FormatException ? "数字字段格式无效。" : ex.Message;
            throw new SklandException($"{message}\n{ex.GetType().Name}: {detail}");
        }
    }

    private static JsonElement Optional(JsonElement value, string name) =>
        value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? default :
        value.TryGetProperty(name, out var property) ? property : default;
    private static IEnumerable<JsonElement> Elements(JsonElement value, string name) =>
        Optional(value, name) is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } array
            ? array.EnumerateArray() : [];
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static string? Option(JsonElement value, string name) =>
        Optional(value, name) is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } option
        && option.GetString() is { Length: > 0 } id ? id : null;
    private static string[] Strings(JsonElement value, string name) =>
        Elements(value, name).Select(e => e.GetString()!).ToArray();
}
