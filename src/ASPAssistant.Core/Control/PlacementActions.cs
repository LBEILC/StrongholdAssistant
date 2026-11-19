using ASPAssistant.Core;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;

namespace ASPAssistant.Core.Control;

public static class PlacementActions
{
    public static IReadOnlyList<GameAction> From(PlacementResult plan, FieldState field) =>
        From(plan, field, out _);

    public static IReadOnlyList<GameAction> From(PlacementResult plan, FieldState field, out IReadOnlyList<string> notes)
    {
        var skipped = new List<string>();
        notes = skipped;
        var occupied = field.Units.ToDictionary(u => u.Cell);
        var summons = (field.Summons ?? []).Select(s => new FieldCell(s.Zone, s.Col, s.Row)).ToHashSet();
        List<MoveAction> pending = [];
        List<GameAction> result = [];
        foreach (var move in plan.Moves.Where(m => m.From != m.To))
        {
            if (!occupied.TryGetValue(move.From, out var unit) || summons.Contains(move.To))
            {
                skipped.Add($"{move.Operator} 的起点或目标格不可用");
                continue;
            }
            pending.Add(new(move.From, move.To, move.Facing, unit.Status == "pending" ? null : move.Operator));
        }

        // 链尾若站着推荐之外的人，把他安排到链首最终腾出的格子。
        foreach (var end in pending.ToArray())
        {
            if (!occupied.TryGetValue(end.To, out var blocker) || pending.Any(m => m.From == end.To)) continue;
            var start = end.From;
            while (pending.FirstOrDefault(m => m.To == start) is { } previous) start = previous.From;
            pending.Add(new(end.To, start, null, blocker.Status == "pending" ? null : blocker.Operator));
        }
        void Move(MoveAction action)
        {
            var unit = occupied[action.From];
            occupied.Remove(action.From);
            occupied.Add(action.To, unit);
            result.Add(action);
        }
        var bench = Enumerable.Range(1, 10).Select(i => new FieldCell("bench", i, 0)).ToArray();
        var board = new Lazy<FieldCell[]>(() =>
        {
            if (field.MapId is null) return [];
            var maps = GarrisonBoardStore.LoadAsync(Path.Combine(PackageLayout.Root, "data")).GetAwaiter().GetResult();
            return maps?.Find(field.MapId)?.AllCells().Where(c => c.Cell.Col is >= 0 and <= 6 && c.Cell.Row is >= 0 and < 4
                && GarrisonMap.CanDeploy(c.Type, true)).Select(c => new FieldCell("board", c.Cell.Col, c.Cell.Row)).ToArray() ?? [];
        });
        int startCount = occupied.Keys.Count(c => c.Zone == "board");
        int finalCount = startCount + pending.Count(m => m.From.Zone == "bench" && m.To.Zone == "board")
            - pending.Count(m => m.From.Zone == "board" && m.To.Zone == "bench");
        int limit = Math.Max(startCount, finalCount);
        while (pending.Count != 0)
        {
            bool full = occupied.Keys.Count(c => c.Zone == "board") >= limit;
            var next = pending.Where(m => !occupied.ContainsKey(m.To) &&
                (!full || m.From.Zone == "board" || m.To.Zone == "bench")).OrderBy(m =>
                m.To.Zone == "bench" ? 0 : m.From.Zone == "board" ? 1 : 2).FirstOrDefault();
            if (next is not null)
            {
                pending.Remove(next);
                Move(next);
                continue;
            }

            // 待实机核实占用格交换。先选环中的场上干员下场，为后续上场腾出名额。
            var cycle = new List<MoveAction>();
            var cursor = pending.FirstOrDefault(m => m.From.Zone == "board" && m.To.Zone == "bench") ?? pending[0];
            while (!cycle.Contains(cursor))
            {
                cycle.Add(cursor);
                var blocker = pending.FirstOrDefault(m => m.From == cursor.To);
                if (blocker is null) break;
                cursor = blocker;
            }
            var first = cycle.FirstOrDefault(m => m.From.Zone == "board") ?? cycle[0];
            bool Free(FieldCell c) => !occupied.ContainsKey(c) && !summons.Contains(c) && !pending.Any(m => m.To == c);
            var spare = full && cycle.All(m => m.From.Zone == "bench") && !occupied.ContainsKey(cycle[^1].To)
                ? null : bench.FirstOrDefault(Free);
            if (spare is null && (!full || first.From.Zone == "board" && cycle.All(m => m.To.Zone == "board")))
                spare = board.Value.FirstOrDefault(Free);
            if (spare is null)
            {
                foreach (var m in cycle) pending.Remove(m);
                skipped.Add($"没有可借的空格或上场名额，跳过 {cycle.Count} 步换位");
                continue;
            }
            Move(new(first.From, spare, null, first.Expect));
            pending[pending.IndexOf(first)] = first with { From = spare };
        }
        return result;
    }
}
