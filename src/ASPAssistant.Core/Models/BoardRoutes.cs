namespace ASPAssistant.Core.Models;

// 格 (c,r) 的中心是 (c+0.5,r+0.5)，投影结果为客户区 0..1 坐标。
public sealed record RoutePoint(double Col, double Row);
public sealed record EnemyRoute(string Motion, bool Boss, bool Approx, int Count,
    IReadOnlyList<IReadOnlyList<RoutePoint>> Segments, string? Side, bool Patrol);
public sealed record BoardRoutes(string MapId, int Round, bool Solo, string? BossId,
    IReadOnlyList<EnemyRoute> Routes, IReadOnlyList<RoutePoint> BossSpots,
    BoardCamera Camera, BoardProjection Projection);
public sealed record BoardMatch(string MapId, BoardCamera Camera, BoardProjection Projection, double Score);
