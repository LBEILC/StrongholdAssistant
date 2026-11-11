// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
// 2026-11-11：StrongholdAssistant 新增阵容来源和备注，兼容旧阵容文件。
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>
/// 阵容槽位标签：玩家手动给某个干员标记，仅作分类/筛选/可视化用，不影响盟约激活计算。
/// </summary>
public enum LineupTag
{
    叠层,
    输出,
    控场,
    经济,
    阵容核心,
}

public enum LineupPhase
{
    前期,
    中期,
    后期,
    挂件,
}

// 2026-11-11：新增目标盟约与关键干员，兼容旧阵容文件。
public sealed record CovenantGoal(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("level")] int Level);

public class LineupSlot
{
    [JsonPropertyName("operatorName")]
    public string OperatorName { get; set; } = "";

    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Key { get; set; }

    /// <summary>规划器为该干员临时追加的转职盟约。</summary>
    [JsonPropertyName("transformedCovenant")]
    public string TransformedCovenant { get; set; } = "";

    /// <summary>装备名列表，最多 2 个。</summary>
    [JsonPropertyName("equipments")]
    public List<string> Equipments { get; set; } = [];

    [JsonPropertyName("coreEquipments")]
    public List<string> CoreEquipments { get; set; } = [];

    /// <summary>标签列表，最多 2 个。</summary>
    [JsonPropertyName("tags")]
    public List<LineupTag> Tags { get; set; } = [];
}

public class Lineup
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "user";

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // null 表示旧文件尚未迁移；空数组表示用户没有选择目标。
    [JsonPropertyName("goals")]
    public CovenantGoal[]? Goals { get; set; }

    /// <summary>想抓的干员池，也是列表预览和追踪使用的阵容。</summary>
    [JsonPropertyName("slots")]
    public List<LineupSlot> Slots { get; set; } = [];

    [JsonPropertyName("earlySlots")]
    public List<LineupSlot> EarlySlots { get; set; } = [];

    [JsonPropertyName("midSlots")]
    public List<LineupSlot> MidSlots { get; set; } = [];

    [JsonPropertyName("supportSlots")]
    public List<LineupSlot> SupportSlots { get; set; } = [];

    [JsonPropertyName("strategies")]
    public List<LineupStrategySlot> Strategies { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum StrategyTag
{
    阵容核心,
    优选,
    次选,
}

public class LineupStrategySlot
{
    [JsonPropertyName("strategyId")]
    public string StrategyId { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<StrategyTag> Tags { get; set; } = [];
}

/// <summary>
/// 单条盟约定义（来自 data/covenants.json，可被 covenants.user.json 覆盖）。
/// </summary>
public class CovenantInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>"SEASON" = 核心盟约（赛季限定）；"REGULAR" = 普通附加盟约。</summary>
    [JsonPropertyName("bondType")]
    public string BondType { get; set; } = "REGULAR";

    /// <summary>激活该盟约所需的同盟约干员数；null 表示数据缺失，应在 user 配置中补齐。</summary>
    [JsonPropertyName("activateCount")]
    public int? ActivateCount { get; set; }

    /// <summary>效果说明，来自手工维护的 data/covenant_effects.json（不写回 covenants.json）。</summary>
    [JsonIgnore]
    public List<CovenantEffect> Effects { get; set; } = [];
}

/// <summary>
/// 一条盟约效果。<see cref="Condition"/> 为空表示激活后的基础效果，否则是游戏内 &lt;…&gt; 里的额外条件，
/// 例如「在场6名不同的【炎】干员」「每叠加25层」。
/// </summary>
public class CovenantEffect
{
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonIgnore]
    public bool HasCondition => !string.IsNullOrWhiteSpace(Condition);
}

/// <summary>
/// 阵容某盟约的实时统计：用于 UI 展示「已激活/层数/阈值」。
/// </summary>
public record CovenantStat(
    string Name,
    string BondType,
    int Count,
    int? ActivateCount,
    bool Activated);
