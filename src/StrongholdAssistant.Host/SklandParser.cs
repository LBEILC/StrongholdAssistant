using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StrongholdAssistant.Host;

// 与界面 types.ts 一致；解析只处理响应正文，不接触登录凭据。
public sealed record SklandOperator(string Id, bool Elite,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Skill = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Module = null);
public sealed record SklandStage(string Type, int StartRound, int EndRound,
    SklandOperator[] Operators, string[] Equipment, string[] Covenants, string Notes);
public sealed record SklandGuide(string Url, string Title, string Author, string[] Strategies,
    string VersionId, SklandStage[] Stages);
public sealed record SklandCovenant(string Id, int Layer);
public sealed record SklandBoss(string Id, bool Killed);
public sealed record SklandRecord(string Id, string? StrategyId, SklandOperator[] Operators,
    string[] Equipment, bool Success, string Time, string StartTime, int CoinSpent, int Kills,
    SklandCovenant[] Covenants, SklandBoss[] Bosses, string Mode, string Difficulty, int PassRound, string VersionId);

public static class SklandParser
{
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
            var factions = tab.GetProperty("activeBonds");
            return new SklandStage(tabType == 1 ? "FORMATION" : tabType.ToString(CultureInfo.InvariantCulture),
                tab.GetProperty("startRound").GetInt32(),
                tab.GetProperty("endRound").GetInt32(), tab.GetProperty("recChars").EnumerateArray()
                    .Select(o => new SklandOperator(Text(o, "charId"), o.GetProperty("mergeLevel").GetInt32() == 1,
                        Option(o, "skillId"), Option(o, "moduleId"))).ToArray(),
                tab.GetProperty("recItems").EnumerateArray().Select(e => Text(e, "itemId")).ToArray(),
                Strings(factions, "seasonBondIds").Concat(Strings(factions, "regularBondIds"))
                    .Select(BondId).ToArray(),
                Text(tab, "operationIdea"));
        }).ToArray();
        if (stages.Length == 0) throw new ArgumentException("这篇攻略不存在或已失效。");
        return new(NormalizeGuideUrl(url), Text(guide, "title"), Text(wrapper.GetProperty("poster"), "nickname"),
            guide.TryGetProperty("strategyIds", out var strategies)
                ? strategies.EnumerateArray().Select(s => s.GetString()!).ToArray() : [],
            Text(guide, "versionId"), stages);
    }

    public static SklandRecord[]? ParseHistory(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.GetProperty("code").GetInt32() != 0) return null;
        var data = document.RootElement.GetProperty("data");
        return data.GetProperty("records").EnumerateArray().Select(record =>
        {
            var squad = record.GetProperty("chessSquad").EnumerateArray().ToArray();
            var strategy = Text(record, "bandId");
            return new SklandRecord(Text(record, "id"), strategy.Length == 0 ? null : strategy,
                squad.Select(o => new SklandOperator(Text(o, "charId"),
                    Text(o, "chessId") == Text(o.GetProperty("chessInfo"), "goldenChessId"),
                    Option(o, "skillId"), Option(o, "moduleId"))).ToArray(),
                squad.SelectMany(o => o.GetProperty("equips").EnumerateArray())
                    .Select(e => Text(e, "equipId")).ToArray(),
                record.GetProperty("completeState").GetInt32() == 1,
                Timestamp(record, "gameEndTs"), Timestamp(record, "gameStartTs"),
                record.GetProperty("coinCost").GetInt32(), record.GetProperty("killCnt").GetInt32(),
                record.GetProperty("bondInfo").EnumerateArray().Select(b =>
                    new SklandCovenant(BondId(Text(b, "bondId")), b.GetProperty("layer").GetInt32())).ToArray(),
                record.GetProperty("acBossInfo").EnumerateArray().Select(b =>
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

    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static string? Option(JsonElement value, string name) =>
        value.TryGetProperty(name, out var option) && option.GetString() is { Length: > 0 } id ? id : null;
    private static string[] Strings(JsonElement value, string name) => value.GetProperty(name)
        .EnumerateArray().Select(e => e.GetString()!).ToArray();
}
