using System.Text.Json.Serialization;

namespace StrongholdAssistant.Host;

// 对应 src/ui/src/lib/api/types.ts；协议仍由界面侧定义。
public sealed record LineupMember(string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Transform = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Key = null);
public sealed record LineupDto(string Id, string Name, LineupMember[] Members, string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note, DateTime UpdatedAt)
{
    public ASPAssistant.Core.Models.CovenantGoal[] Goals { get; init; } = [];
}
public sealed record SessionDto(string? TargetLineupId, string[] Owned, string[] Tracked);
public sealed record SettingsDto(string GameTheme, bool ShopMarkers, bool AutoCheckUpdate)
{
    public bool AlwaysOnTop { get; init; }
    public string[] FocusCovenants { get; init; } = [];
    public string[] FocusOperators { get; init; } = [];
    public System.Text.Json.JsonElement UiScale { get; init; } = System.Text.Json.JsonSerializer.SerializeToElement("auto");
    [JsonIgnore]
    public double? ScaleValue => UiScale.ValueKind == System.Text.Json.JsonValueKind.Number ? UiScale.GetDouble() : null;
}
public sealed record CovenantState(string Id, bool Incomplete);
public sealed record MatchInfo(string Source, string? StrategyId, CovenantState[] Covenants,
    string[] BannedOperators, string? BossId, string[] EnemyTypes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RecognizedAt = null);

public sealed record Connection(string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Resolution = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Fullscreen = null, bool Overlay = false);
public sealed record ScanStatus(bool Busy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Ok = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Continuous = null);
