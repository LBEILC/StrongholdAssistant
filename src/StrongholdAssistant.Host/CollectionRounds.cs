using System.Text.Json;
using ASPAssistant.Core.Models;
using OpenCvSharp;

namespace StrongholdAssistant.Host;

internal sealed record RoundVitals(int? Hp, int? Money, int? Level);
internal sealed record CollectedResult(int? ReachedRound, bool? Cleared, int? FinalHp, int? Trophies,
    int? Score = null, int? Rank = null)
{
    internal int? RoundLeaks { get; init; }
}
internal sealed record RoundUnit(string Zone, int Col, int Row, string? Operator, int Tier, bool? Elite,
    FieldEquipment[]? Equipment, bool Confirmed);

internal sealed class CollectedRound(int round, RoundVitals start, DateTimeOffset at)
{
    public int Round { get; } = round;
    public GameModeInfo? Mode { get; set; }
    public string? Map { get; set; }
    public string? Boss { get; set; }
    public RoundEnemy[]? Enemies { get; set; }
    public string? Strategy { get; set; }
    public CovenantState[] Covenants { get; set; } = [];
    public string[] Banned { get; set; } = [];
    public RoundVitals Start { get; } = start;
    public DateTimeOffset StartAt { get; } = at;
    public DateTimeOffset EndAt { get; set; } = at;
    public RoundVitals End { get; set; } = start;
    public int? HpChange { get; set; }
    public int? Leaks => null;
    public IReadOnlyDictionary<int, string>? OpeningShop { get; set; }
    public DateTimeOffset? OpeningShopAt { get; set; }
    public int? Refreshes { get; set; }
    public List<string>? Purchases { get; set; }
    public bool? Upgraded { get; set; }
    public RoundUnit[]? Units { get; set; }
    public string? RestStart { get; set; }
    public string? RestEnd { get; set; }
    public string? EnemyPanel { get; set; }
}

internal sealed partial class CollectionStore
{
    internal sealed partial class Batch
    {
        public bool HasPendingRound => _session.RoundEndFrame is not null;
        private string DirectoryPath => Path.Combine(_owner._root, _session.Id);
        private CollectedRound? Current => _session.RoundRows.LastOrDefault();
        private string KeyFrame(byte[] frame, int round, string kind)
        {
            var relative = $"round-frames/{round:D2}-{kind}.jpg";
            Directory.CreateDirectory(Path.Combine(DirectoryPath, "round-frames"));
            using var image = Cv2.ImDecode(frame, ImreadModes.Color);
            using var scaled = new Mat();
            Cv2.Resize(image, scaled, new Size(1280, (int)Math.Round(image.Height * 1280d / image.Width)));
            File.WriteAllBytes(Path.Combine(DirectoryPath, relative), scaled.ImEncode(".jpg",
                new ImageEncodingParam(ImwriteFlags.JpegQuality, 85)));
            return relative;
        }

        public void RestFrame(int round, RoundVitals vitals, MatchInfo match, FieldObservation observation,
            IReadOnlyDictionary<int, string>? cards, FieldUnit[]? units, byte[] frame)
        {
            if (_session.Result is not null || Current is { } previous && round < previous.Round) return;
            bool first = Current?.Round != round;
            if (first)
            {
                if (Current is { } last)
                {
                    FlushRound();
                    if (round == last.Round + 1) last.HpChange = vitals.Hp - last.End.Hp;
                }
                var row = new CollectedRound(round, vitals, _at) { RestStart = KeyFrame(frame, round, "rest-start") };
                _session.WaitingForOpeningShop = true;
                _session.RoundRows.Add(row);
                _session.LastShop = null;
            }
            var current = Current!;
            current.Mode = match.Mode;
            current.Boss = match.BossId;
            current.Strategy = match.StrategyId;
            current.Covenants = match.Covenants;
            current.Banned = match.BannedOperators;
            if (match.RoundEnemies?.Round == round) current.Enemies = match.RoundEnemies.Enemies;
            current.Map = observation.MapId ?? current.Map;
            if (_session.WaitingForOpeningShop)
            {
                if (!first && (vitals.Money is null || vitals.Money != current.Start.Money))
                    _session.WaitingForOpeningShop = false;
                else if (cards is { Count: > 0 })
                {
                    current.OpeningShop = cards;
                    current.OpeningShopAt = _at;
                    _session.WaitingForOpeningShop = false;
                }
            }
            ObserveShop(current, observation.Shop, cards, vitals);
            current.End = vitals;
            current.EndAt = _at;
            if (units is not null) SetUnits(units);
            _session.RoundEndFrame = frame;
            if (first) SaveRows();
        }

        private void ObserveShop(CollectedRound row, IReadOnlyDictionary<int, string>? operators,
            IReadOnlyDictionary<int, string>? cards, RoundVitals now)
        {
            if (row.Upgraded != true)
            {
                if (now.Level > row.Start.Level || now.Level > row.End.Level) row.Upgraded = true;
                else row.Upgraded = now.Level is not null && now.Level == row.Start.Level ? false : null;
            }
            if (cards is not null)
            {
                // 对比相邻帧时使用场上读取器已用卡面复核的占位，避免卡名瞬时漏读变成买入。
                var visible = operators?.ToDictionary() ?? [];
                foreach (var card in cards) visible[card.Key] = card.Value;
                cards = visible;
                row.Refreshes ??= 0;
                row.Purchases ??= [];
                if (_session.LastShop is { } old && row.End.Money is int money && now.Money < money)
                {
                    int changed = cards.Count(p => old.TryGetValue(p.Key, out var name) && p.Value != name);
                    var gone = old.Where(p => !cards.ContainsKey(p.Key)).ToArray();
                    if (changed >= 2) row.Refreshes++;
                    else if (changed == 0 && gone.Length == 1 && cards.Count + 1 == old.Count
                        && _session.LastOperators?.ContainsKey(gone[0].Key) == true && !(now.Level > row.End.Level))
                        row.Purchases.Add(gone[0].Value);
                }
            }
            _session.LastShop = cards;
            _session.LastOperators = operators;
        }

        private void SetUnits(FieldUnit[] units)
        {
            foreach (var p in _session.Confirmed.ToArray())
                if (!units.Any(u => u.Cell == p.Key && u.Operator == p.Value)) _session.Confirmed.Remove(p.Key);
            Current!.Units = units.Select(u => new RoundUnit(u.Zone, u.Col, u.Row, u.Operator, u.Tier,
                u.Elite, u.Equipment, u.Operator is not null && (u.Source == "manual" ||
                _session.Confirmed.GetValueOrDefault(u.Cell) == u.Operator))).ToArray();
        }

        public void ManualSnapshot(int? round, FieldUnit[] units)
        {
            if (Current is { } row && row.Round == round) SetUnits(units);
        }

        public void ConfirmUnits(FieldCell? cell, FieldState? state, bool all)
        {
            if (Current is not { } row || state is null || (state.Round is int round && round != row.Round)) return;
            foreach (var u in state.Units.Where(u => u.Operator is not null && (all || u.Cell == cell)))
                if (!all || row.Units?.Any(last => last.Zone == u.Zone && last.Col == u.Col &&
                    last.Row == u.Row && last.Operator == u.Operator) == true)
                    _session.Confirmed[u.Cell] = u.Operator!;
            if (!all) SetUnits(state.Units);
            else if (row.Units is not null) row.Units = row.Units.Select(u => u with { Confirmed = u.Confirmed ||
                u.Operator is not null && _session.Confirmed.GetValueOrDefault(new(u.Zone, u.Col, u.Row)) == u.Operator }).ToArray();
        }

        public void Panel(int round, byte[] frame, RoundEnemy[]? enemies)
        {
            if (Current is not { } row || row.Round != round) return;
            row.EnemyPanel ??= KeyFrame(frame, round, "enemies");
            if (enemies is not null) row.Enemies = enemies;
        }

        public void FlushRound()
        {
            if (Current is { } row && _session.RoundEndFrame is { } frame)
            {
                row.RestEnd = KeyFrame(frame, row.Round, "rest-end");
                _session.RoundEndFrame = null;
            }
            SaveRows();
        }

        private void SaveRows()
        {
            if (Current is null) return;
            string file = Path.Combine(DirectoryPath, "rounds.jsonl");
            File.WriteAllLines(file + ".tmp", _session.RoundRows.Select(r => JsonSerializer.Serialize(r, HostApi.JsonOptions)));
            File.Move(file + ".tmp", file, true);
        }

        public void Settlement(CollectedResult reading)
        {
            var old = _session.Result;
            // 失败时结算显示的是通过回合，可能比已经到达的棋盘回合少一。
            int? reached = old?.ReachedRound ?? Current?.Round;
            if (reading.ReachedRound is int passed && (reached is null || passed > reached)) reached = passed;
            _session.Result = reading with { ReachedRound = reached,
                Cleared = reading.Cleared ?? old?.Cleared, FinalHp = reading.FinalHp ?? old?.FinalHp,
                Trophies = reading.Trophies ?? old?.Trophies, Score = reading.Score ?? old?.Score, Rank = reading.Rank ?? old?.Rank };
            FlushRound();
            var path = Path.Combine(DirectoryPath, "result.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(_session.Result, HostApi.JsonOptions));
            File.Move(path + ".tmp", path, true);
        }
    }
}
