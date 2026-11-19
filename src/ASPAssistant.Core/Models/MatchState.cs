namespace ASPAssistant.Core.Models;

// 未知读数保留 null；空数组表示明确观察到没有条目。
public sealed record MatchFact<T>(string Source, T Value)
{
    public static MatchFact<T> Unknown { get; } = new("unknown", default!);
    public static MatchFact<T> Of(T value, string source = "read") =>
        value is null ? Unknown : new(source, value);
}

public sealed record MatchEntity
{
    public MatchFact<string?> Id { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<string?> Name { get; init; } = MatchFact<string?>.Unknown;
}

public sealed record MatchCovenant
{
    public MatchFact<string?> Id { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<string?> Kind { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<bool?> Incomplete { get; init; } = MatchFact<bool?>.Unknown;
    public MatchFact<int?> Level { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> Layers { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> Count { get; init; } = MatchFact<int?>.Unknown;
}

public sealed record MatchEquipment
{
    public MatchFact<string?> Id { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<string?> Name { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<bool?> Elite { get; init; } = MatchFact<bool?>.Unknown;
}

public sealed record MatchUnit
{
    public MatchFact<string?> Zone { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<int?> Col { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> Row { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<MatchEntity?> Operator { get; init; } = MatchFact<MatchEntity?>.Unknown;
    public MatchFact<int?> Tier { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<bool?> Elite { get; init; } = MatchFact<bool?>.Unknown;
    public MatchFact<int?> SkillIndex { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<string?> ModuleId { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<int?> Potential { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<MatchEquipment[]?> Equipment { get; init; } = MatchFact<MatchEquipment[]?>.Unknown;
    public MatchFact<string?> Dir { get; init; } = MatchFact<string?>.Unknown;
}

public sealed record MatchCard
{
    public MatchFact<string?> Kind { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<MatchEntity?> Card { get; init; } = MatchFact<MatchEntity?>.Unknown;
    public MatchFact<int?> Tier { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> Price { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<bool?> Bought { get; init; } = MatchFact<bool?>.Unknown;
}

public sealed record MatchState
{
    public MatchFact<string?> GameDataVersion { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<string?> ModeId { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<string?> Difficulty { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<string?> MapId { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<int?> Round { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<string?> BossId { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<MatchEntity[]?> Enemies { get; init; } = MatchFact<MatchEntity[]?>.Unknown;
    public MatchFact<string?> StrategyId { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<Dictionary<int, MatchEntity[]>?> CustomPicks { get; init; } = MatchFact<Dictionary<int, MatchEntity[]>?>.Unknown;
    public MatchFact<MatchEntity[]?> BannedOperators { get; init; } = MatchFact<MatchEntity[]?>.Unknown;
    public MatchFact<MatchCovenant[]?> Covenants { get; init; } = MatchFact<MatchCovenant[]?>.Unknown;
    public MatchFact<int?> Hp { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> Money { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> Level { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> UpgradeCost { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> UpgradeProgress { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> RefreshCost { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<bool?> Frozen { get; init; } = MatchFact<bool?>.Unknown;
    public MatchFact<MatchUnit[]?> Units { get; init; } = MatchFact<MatchUnit[]?>.Unknown;
    public MatchCard[] Shop { get; init; } = Enumerable.Range(0, 6).Select(_ => new MatchCard()).ToArray();
}

public sealed record MatchMove
{
    public MatchFact<MatchPosition?> From { get; init; } = MatchFact<MatchPosition?>.Unknown;
    public MatchFact<MatchPosition?> To { get; init; } = MatchFact<MatchPosition?>.Unknown;
    public MatchFact<MatchEntity?> Operator { get; init; } = MatchFact<MatchEntity?>.Unknown;
}

public sealed record MatchPosition
{
    public MatchFact<string?> Zone { get; init; } = MatchFact<string?>.Unknown;
    public MatchFact<int?> Col { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> Row { get; init; } = MatchFact<int?>.Unknown;
}

public sealed record MatchDecision
{
    public MatchFact<MatchEntity[]?> Purchases { get; init; } = MatchFact<MatchEntity[]?>.Unknown;
    public MatchFact<MatchEntity[]?> Sales { get; init; } = MatchFact<MatchEntity[]?>.Unknown;
    public MatchFact<int?> Refreshes { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<bool?> Frozen { get; init; } = MatchFact<bool?>.Unknown;
    public MatchFact<bool?> Upgraded { get; init; } = MatchFact<bool?>.Unknown;
    public MatchFact<MatchMove[]?> Moves { get; init; } = MatchFact<MatchMove[]?>.Unknown;
    public MatchFact<int?> HpChange { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> Leaks { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<int?> ReachedRound { get; init; } = MatchFact<int?>.Unknown;
    public MatchFact<bool?> Cleared { get; init; } = MatchFact<bool?>.Unknown;
}

// State 是期初可知的输入。休整末帧单独保存，避免动作和结算信息进入期初输入。
public sealed record MatchStep(MatchState State, MatchDecision Decision, MatchState? EndState)
{
    // 采集溯源信息只供还原 v2 行，模型不读取。
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MatchCapture? Capture { get; init; }
}

public sealed record MatchCapture(string? StartAt, string? EndAt, string? OpeningShopAt, string? RestStart,
    string? RestEnd, string? EnemyPanel, bool OpeningShopSeen);
