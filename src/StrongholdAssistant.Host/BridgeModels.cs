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
public sealed record LineupGroupDto(int Need, LineupMember[] Members);
public sealed record LineupStageDto(string Name, int FromRound, int ToRound,
    ASPAssistant.Core.Models.CovenantGoal[] Goals, LineupMember[] Members, string[] Equipment)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LineupGroupDto[]? Groups { get; init; }
}
public sealed record LineupDto(string Id, string Name, LineupMember[] Members, string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Note, DateTime UpdatedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LineupGroupDto[]? Groups { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ASPAssistant.Core.Models.LineupUpgrade[]? Upgrades { get; init; }
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
    // TODO(design): 将 placementGoal 加入前端云同步 PREF_KEYS 和 CloudPrefsData。
    public string PlacementGoal { get; init; } = "balanced";
    public bool Collect { get; init; }
    public bool FieldAuto { get; init; }
    public ControlSettings Control { get; init; } = new();
    public string FieldHotkey { get; init; } = "Alt+F";
    public MarkerStyles MarkerStyles { get; init; } = new();
    public bool GameThemeAuto { get; init; } = true;
    public bool MultiInstance { get; init; }
    public bool RunAsAdmin { get; init; } = true;
    public RouteOverlaySettings Routes { get; init; } = new();
    public OverlayLayerSettings OverlayLayers { get; init; } = new();
    public string UiSkin { get; init; } = "follow";
    public string UiScheme { get; init; } = "system";
    private string _uiAccent = "";
    public string UiAccent { get => _uiAccent; init => _uiAccent = value?.ToLowerInvariant()!; }
    public string UiFont { get; init; } = "";
    public string UiFontWeight { get; init; } = "normal";
    public bool PlazaNotices { get; init; } = true;
    public string ShopScanSpeed { get; init; } = "normal";
    public string UpdateSource { get; init; } = "github";
    public string UpdateProxy { get; init; } = "";
    public bool AutoDownloadUpdate { get; init; } = true;
    public string MirrorChyanCdk { get; init; } = "";
    /// <summary>GitHub Token（可选，2026-11-16 加，对位 MAA 的 GitHubToken）。落盘加密，不进备份和诊断包</summary>
    [JsonPropertyName("githubToken")]
    public string GitHubToken { get; init; } = "";
    /// <summary>上次校验拿到的 Token 到期时间（ISO 8601）；空 = 永不过期或还没校验过</summary>
    [JsonPropertyName("githubTokenExpiresAt")]
    public string GitHubTokenExpiresAt { get; init; } = "";
    public Dictionary<string, OperatorDefaults> OperatorDefaults { get; init; } = [];
    public bool HideCaptureBorder { get; init; }
    public string GameSource { get; init; } = "auto";
    public string EmulatorSerial { get; init; } = "";
    public string AdbAddress { get; init; } = "";
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
    public StrategyAdvice? StrategyAdvice { get; init; }
    public BanGroup[] BanGroups { get; init; } = [];
    public bool BansScanned { get; init; }
    public bool Solo { get; init; }
    public bool AwaitingDetail { get; init; }
    public ASPAssistant.Core.Models.GameModeInfo? Mode { get; init; }
    public Economy? Economy { get; init; }
    public string? StrategySource { get; init; }
    public RoundEnemies? RoundEnemies { get; init; }
    public Dictionary<int, string[]>? CustomPicks { get; init; }
    public CovenantBarItem[] CovenantBar { get; init; } = [];
    public AdaptAdvice? AdaptAdvice { get; init; }
}
public sealed record CovenantBarItem(string Id, int? Level, int? Layers, int? Round, string At);
public sealed record RoundEnemy(string? Id, string Name, string Source = "read");
public sealed record RoundEnemies(int Round, string At, RoundEnemy[] Enemies)
{
    public RoundEnemy[] Removed { get; init; } = [];
}
public sealed record RoundEnemyOption(string? Id, string Name, int Count);
public sealed record RoundEnemyGroup(string Label, double Weight, RoundEnemyOption[] Enemies);
public sealed record RoundEnemyOptions(int Round, RoundEnemyGroup[] Groups);
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Ok = null);
