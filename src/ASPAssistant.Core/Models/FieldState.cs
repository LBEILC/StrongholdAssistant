namespace ASPAssistant.Core.Models;

public sealed record FieldCell(string Zone, int Col, int Row);
public sealed record FieldUnit(string Zone, int Col, int Row, int Tier, string? Operator, string? Source)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public FieldCell Cell => new(Zone, Col, Row);
}
public sealed record FieldEvent(string At, string Kind, string? Operator, FieldCell? From, FieldCell? To);
public sealed record FieldState(string? MapId, int? Round, string UpdatedAt, FieldUnit[] Units, FieldEvent[] Log);

// 缺失的格子代表不可见；明确的 0 才代表空格。拖动期间冻结占用。
public sealed record FieldObservation(DateTimeOffset At, bool IsRest, string? MapId, int? Round,
    IReadOnlyDictionary<FieldCell, int> Cells, IReadOnlyDictionary<int, string>? Shop = null,
    int? Money = null, string? DragOperator = null, bool MainScreen = true);
