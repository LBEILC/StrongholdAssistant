using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

public sealed class BoardRouteStore
{
    private sealed record Battle(string? BossId, string Level);
    private sealed record Level(EnemyRoute[] Routes, RoutePoint[] BossSpots);
    private sealed record Map(Dictionary<string, Level> Levels);
    private sealed record Data(Dictionary<string, Dictionary<string, Battle[]>> Battles,
        Dictionary<string, string> BossIds, Dictionary<string, Map> Maps);
    private readonly Data _data;

    public BoardRouteStore(string directory) => _data = JsonSerializer.Deserialize<Data>(
        File.ReadAllText(Path.Combine(directory, "garrison_routes.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public BoardRoutes? Find(BoardMatch board, int round, bool solo, string? bossId, string? mode)
        => Find(board, round, solo, bossId, mode, out _);

    // battles 由 activity_table 的模式回合列表生成。
    public int? LastRound(bool solo, string? mode) =>
        _data.Battles.TryGetValue(mode ?? (solo ? "mode_single_20" : "mode_multi_normal"), out var rounds)
            ? rounds.Keys.Select(int.Parse).Max() : null;

    public BoardRoutes? Find(BoardMatch board, int round, bool solo, string? bossId, string? mode, out string? reason)
    {
        reason = null;
        string key = mode ?? (solo ? "mode_single_20" : "mode_multi_normal");
        if (!_data.Battles.TryGetValue(key, out var rounds)) { reason = "缺模式"; return null; }
        if (!rounds.TryGetValue(round.ToString(), out var battles)) { reason = "关卡没找到"; return null; }
        string? level;
        if (battles.Any(b => !string.IsNullOrEmpty(b.BossId)))
        {
            if (bossId is null) { reason = "首领还没识别"; return null; }
            // 首领由识别的敌人 id 与单人/联机确定，不受默认模式的首领池限制。
            level = _data.Battles.Values.SelectMany(r => r.GetValueOrDefault(round.ToString()) ?? [])
                .FirstOrDefault(b => b.BossId is not null && _data.BossIds.GetValueOrDefault(b.BossId) == bossId
                    && b.Level.EndsWith("_s", StringComparison.Ordinal) == solo)?.Level;
        }
        else level = battles.FirstOrDefault()?.Level;
        if (level is null || !_data.Maps.TryGetValue(board.MapId, out var map)
            || !map.Levels.TryGetValue(level, out var routes)) { reason = "关卡没找到"; return null; }
        return new(board.MapId, round, solo, bossId, routes.Routes, routes.BossSpots, board.Camera, board.Projection);
    }
}
