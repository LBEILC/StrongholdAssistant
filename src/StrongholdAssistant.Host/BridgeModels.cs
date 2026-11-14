using System.Text.Json.Serialization;

namespace StrongholdAssistant.Host;

// 对应 src/ui/src/lib/api/types.ts；协议仍由界面侧定义。
public sealed record LineupMember(string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Transform = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Key = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Elite { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Skill { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Module { get; init; }
    public ASPAssistant.Core.Models.MemberEquip[] Equipment { get; init; } = [];
}
public sealed record LineupStageDto(string Name, int FromRound, int ToRound,
    ASPAssistant.Core.Models.CovenantGoal[] Goals, LineupMember[] Members, string[] Equipment);
public sealed record LineupDto(string Id, string Name, LineupMember[] Members, string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note, DateTime UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LineupStageDto[]? Stages { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
    public string? StrategyId { get; init; }
    public ASPAssistant.Core.Models.CovenantGoal[] Goals { get; init; } = [];
    public string[] Equipment { get; init; } = [];
}
public sealed record SessionDto(string? TargetLineupId, string[] Owned, string[] Tracked)
{
    public int? Stage { get; init; }
    public string[] TrackedEquipment { get; init; } = [];
}
public sealed record CloudDeletedLineup(string Id, string At);
public sealed record CloudSyncState(bool Enabled, string? Since, string? PushedAt, string? PrefsAt, CloudDeletedLineup[] Deleted)
{
    public string? Account { get; init; }
}
public sealed record SettingsDto(string GameTheme, bool ShopMarkers, bool AutoCheckUpdate)
{
    public bool GameThemeAuto { get; init; } = true;
    public bool RunAsAdmin { get; init; }
    public RouteOverlaySettings Routes { get; init; } = new();
    public string UiSkin { get; init; } = "follow";
    public bool PlazaNotices { get; init; } = true;
    public string ShopScanSpeed { get; init; } = "normal";
    public string UpdateSource { get; init; } = "github";
    public string UpdateProxy { get; init; } = "";
    public string MirrorChyanCdk { get; init; } = "";
    public Dictionary<string, OperatorDefaults> OperatorDefaults { get; init; } = [];
    public string GameSource { get; init; } = "auto";
    public string EmulatorSerial { get; init; } = "";
    public bool AlwaysOnTop { get; init; }
    public bool FollowGame { get; init; } = true;
    public string DockSide { get; init; } = "auto";
    public string OverlaySide { get; init; } = "left";
    public string ToggleHotkey { get; init; } = "Alt+S";
    public string RefreshKey { get; init; } = "D";
    public string StartupMode { get; init; } = "remember";
    public bool ShareIncome { get; init; }
    public CloudSyncState CloudSync { get; init; } = new(false, null, null, null, []);
    public string CollapseMode { get; init; } = "strip";
    public string[] FocusCovenants { get; init; } = [];
    public string[] FocusOperators { get; init; } = [];
    public System.Text.Json.JsonElement UiScale { get; init; } = System.Text.Json.JsonSerializer.SerializeToElement("auto");
    [JsonIgnore]
    public double? ScaleValue => UiScale.ValueKind == System.Text.Json.JsonValueKind.Number ? UiScale.GetDouble() : null;
}
public sealed record OperatorDefaults(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Skill = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Module = null);
public sealed record CovenantState(string Id, bool Incomplete);
public sealed record MatchInfo(string Source, string? StrategyId, CovenantState[] Covenants,
    string[] BannedOperators, string? BossId, string[] EnemyTypes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RecognizedAt = null)
{
    public BanGroup[] BanGroups { get; init; } = [];
    public bool BansScanned { get; init; }
    public bool Solo { get; init; }
    public bool AwaitingDetail { get; init; }
    public ASPAssistant.Core.Models.GameModeInfo? Mode { get; init; }
    public Economy? Economy { get; init; }
}
public sealed record BanGroup(string Covenant, BanSlot[] Slots, bool Complete);
public sealed record BanSlot(string? Name, int Tier, string[] Candidates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Inferred = null);

public sealed record Connection(string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Resolution = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Fullscreen = null, bool Overlay = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Problem = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Side = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? GameElevated = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Theme { get; init; }
}
public sealed record EmulatorInstance(string Kind, string Serial, string Name);
public sealed record ScanStatus(bool Busy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Ok = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Continuous = null);
