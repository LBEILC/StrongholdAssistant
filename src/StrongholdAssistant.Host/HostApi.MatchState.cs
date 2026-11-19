using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    // 保存扫描器已有的休整期读数；取快照时无需访问游戏。
    private sealed record RestReading(EconomyReading Economy, (int Slot, string Name, int Price)[] Cards);
    private RestReading? _matchRest;
    private (int Round, int? Hp)? _matchHp;

    private static MatchFact<string?> TextFact(string? value, string source = "read") =>
        MatchFact<string?>.Of(value, source);
    private static MatchFact<int?> NumberFact(int? value, string source = "read") =>
        MatchFact<int?>.Of(value, source);
    private static MatchFact<bool?> BoolFact(bool? value, string source = "read") =>
        MatchFact<bool?>.Of(value, source);
    private static MatchFact<T[]?> ListFact<T>(T[]? values, string source = "read") =>
        MatchFact<T[]?>.Of(values, source);

    private static string? Difficulty(string? id)
    {
        if (id == "mode_single_funny") return "FAST";
        if (id == "mode_training_2") return "TRAINING";
        if (id?.StartsWith("mode_multi_") == true) return id[11..].ToUpperInvariant();
        if (id?.StartsWith("mode_single_") != true || !int.TryParse(id[12..], out int level)) return null;
        return level switch { >= 1 and <= 4 => "FUNNY", >= 5 and <= 8 => "NORMAL",
            >= 9 and <= 14 => "HARD", >= 15 and <= 20 => "ABYSS", _ => null };
    }

    public MatchState GetMatchState()
    {
        var match = Match;
        FieldState? field;
        RestReading? rest;
        (int Round, int? Hp)? hp;
        lock (_fieldGate) { field = _fieldTracker.State; rest = _matchRest; hp = _matchHp; }
        var round = rest?.Economy.Round ?? match.Economy?.Round ?? field?.Round;
        if (field?.Round != round) field = null;
        if (rest?.Economy.Round != round) rest = null;
        var economy = match.Economy?.Round == round ? match.Economy : null;
        var ops = Catalog.GetProperty("operators").EnumerateArray().ToDictionary(o => o.GetProperty("name").GetString()!);
        var items = Catalog.GetProperty("equipment").EnumerateArray().ToDictionary(o => o.GetProperty("name").GetString()!);
        MatchEntity Entity(string name, string source = "read")
        {
            bool op = ops.TryGetValue(name, out var row);
            if (!op) items.TryGetValue(name, out row);
            var id = row.ValueKind == JsonValueKind.Object && row.TryGetProperty(op ? "charId" : "itemId", out var key)
                ? key.GetString() : null;
            return new() { Id = TextFact(id, source), Name = TextFact(name, source) };
        }
        MatchUnit Unit(FieldUnit u)
        {
            string source = u.Source switch { "manual" => "confirmed", "panel" => "read", _ => "inferred" };
            var op = u.Operator is not null && u.Status is "sure" or "auto" ? Entity(u.Operator, source) : null;
            return new() { Zone = TextFact(u.Zone), Col = NumberFact(u.Col), Row = NumberFact(u.Row),
                Operator = MatchFact<MatchEntity?>.Of(op, source), Tier = NumberFact(u.Tier), Elite = BoolFact(u.Elite),
                Equipment = ListFact(u.Equipment?.Select(e => new MatchEquipment { Id = Entity(e.Name).Id,
                    Name = TextFact(e.Name), Elite = BoolFact(e.Elite) }).ToArray()) };
        }
        var cards = new MatchState().Shop;
        foreach (var (slot, name, price) in rest?.Cards ?? [])
        {
            bool op = ops.TryGetValue(name, out var row);
            bool item = !op && items.TryGetValue(name, out row);
            int? tier = (op || item) ? row.GetProperty("tier").GetInt32() : null;
            if (op && row.GetProperty("diy").GetBoolean())
            {
                var tiers = match.CustomPicks?.Where(p => p.Value.Contains(name)).Select(p => p.Key).ToArray();
                tier = tiers is { Length: 1 } ? tiers[0] : null;
            }
            var card = new MatchCard { Card = MatchFact<MatchEntity?>.Of(Entity(name)),
                Kind = TextFact(op ? "operator" : item ? "equipment" : null, "inferred"),
                Tier = NumberFact(tier, "inferred"),
                Price = NumberFact(price > 0 ? price : null), Bought = BoolFact(false) };
            cards[slot] = card;
        }
        var kinds = Catalog.GetProperty("covenants").EnumerateArray().ToDictionary(
            c => c.GetProperty("id").GetString()!, c => c.GetProperty("kind").GetString());
        var selection = match.Source == "none" ? [] : match.Covenants;
        var bar = match.CovenantBar.Where(c => c.Round == round && round.HasValue).ToDictionary(c => c.Id);
        var covenants = selection.Select(c => c.Id).Union(bar.Keys).Select(id =>
        {
            var ban = selection.FirstOrDefault(c => c.Id == id);
            var reading = bar.GetValueOrDefault(id);
            return new MatchCovenant { Id = TextFact(id), Kind = TextFact(kinds.GetValueOrDefault(id), "inferred"),
                Incomplete = BoolFact(ban?.Incomplete, match.Source == "manual" ? "inferred" : "read"),
                Level = NumberFact(reading?.Level), Layers = NumberFact(reading?.Layers) };
        }).ToArray();
        return new() { GameDataVersion = TextFact(_gameDataVersion, "inferred"), ModeId = TextFact(match.Mode?.Id),
            Difficulty = TextFact(Difficulty(match.Mode?.Id), "inferred"), Round = NumberFact(round),
            MapId = TextFact(field?.MapId ?? _boardMap), BossId = TextFact(match.BossId),
            StrategyId = TextFact(match.StrategyId, match.StrategySource == "manual" ? "confirmed" : "read"),
            Enemies = ListFact(match.RoundEnemies is { } enemies && enemies.Round == round ? enemies.Enemies.Select(e =>
            { var source = e.Source == "manual" ? "confirmed" : "read"; return new MatchEntity
                { Id = TextFact(e.Id, source), Name = TextFact(e.Name, source) }; }).ToArray() : null),
            BannedOperators = ListFact(match.Source == "none" && match.BannedOperators.Length == 0 ? null :
                match.BannedOperators.Select(n => Entity(n, _manualBans.ContainsKey(n) ? "confirmed" :
                    _avatarBans.Contains(n) ? "read" : "inferred")).ToArray(), "inferred"),
            CustomPicks = MatchFact<Dictionary<int, MatchEntity[]>?>.Of(match.CustomPicks?.ToDictionary(
                p => p.Key, p => p.Value.Select(n => Entity(n)).ToArray())),
            Covenants = ListFact(covenants.Length == 0 ? null : covenants),
            Hp = NumberFact(hp?.Round == round ? hp?.Hp : null),
            Money = NumberFact(rest?.Economy.Money ?? economy?.Money), Level = NumberFact(rest?.Economy.Level ?? economy?.Level),
            UpgradeCost = NumberFact(rest?.Economy.UpgradeCost ?? economy?.UpgradeCost),
            Units = ListFact(field?.Units.Select(Unit).ToArray()), Shop = cards };
    }
}
