namespace ASPAssistant.Core.Models;

public sealed record FieldCandidate(string Operator, double Score);
public sealed record FieldRosterEntry(string Operator, string Source, string At, bool Sold);

public sealed partial record FieldUnit
{
    public string Status { get; init; } = Operator is null ? "unknown" : "sure";
    public FieldCandidate[] Candidates { get; init; } = [];
    public bool? Elite { get; init; }
}

public sealed partial record FieldState
{
    public FieldRosterEntry[] Roster { get; init; } = [];
}

// 图块只在本局内存中使用，不是消息桥接口的一部分。
public sealed record FieldSpritePatch(byte[] Image, byte[] Pixels, bool Bench)
{
    public FieldSpriteGeometry? Geometry { get; init; }
    public FieldSpriteRegion? TemplateRegion { get; init; }
    // 模板区域内的遮挡蒙版（PNG）；不进入消息桥，也不写回读取缓存。
    public byte[]? OcclusionMask { get; init; }
    public double OcclusionRatio { get; init; }
}

public sealed record FieldSpriteRegion(byte[] Image, int OffsetX, int OffsetY);

// 内部识别证据，坐标统一为1920×1080，不进入消息桥。
public sealed record FieldScreenPoint(double X, double Y);
public sealed record FieldSpriteGeometry(double CellWidth, double FootX, double FootY,
    double BadgeFootX, double BadgeFootY);
public sealed record FieldFrameGeometry(BoardCamera Camera, string? PlayerSide, bool BossRound,
    bool BoardMatched, FieldScreenPoint[] Corners, IReadOnlyDictionary<FieldCell, FieldScreenPoint> BenchFeet);

public sealed partial record FieldObservation
{
    public IReadOnlyDictionary<FieldCell, FieldSpritePatch>? Sprites { get; init; }
    public string? GainSource { get; init; }
    public FieldFrameGeometry? Geometry { get; init; }
}
