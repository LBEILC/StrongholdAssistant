using System.Text.Json;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _sessionPath;
    public string DataDirectory { get; }
    public System.Windows.Window? DialogOwner { get; set; }
    private readonly LineupStore _lineups;
    private readonly Dictionary<string, string> _ids;
    private readonly Dictionary<string, string> _names;
    private readonly List<Operator> _operators;
    private readonly List<CovenantInfo> _covenants;
    private readonly Dictionary<string, bool> _manualBans = [];
    private readonly HashSet<string> _avatarBans = [];
    public JsonElement Catalog { get; }
    public MatchInfo Match { get; private set; }

    private HostApi(string userDirectory, Dictionary<string, string> ids, List<Operator> operators,
        List<CovenantInfo> covenants, JsonElement catalog)
    {
        DataDirectory = Path.GetFullPath(userDirectory);
        _sessionPath = Path.Combine(DataDirectory, "session.json");
        _lineups = new LineupStore(Path.Combine(userDirectory, "lineups"));
        _ids = ids;
        _names = ids.ToDictionary(p => p.Value, p => p.Key);
        _operators = operators;
        _covenants = covenants;
        Catalog = catalog;
        Match = new("none", null, covenants.Select(c => new CovenantState(ids[c.Name], false)).ToArray(), [], null, []);
    }

    public static async Task<HostApi> CreateAsync(string dataDirectory, string userDirectory)
    {
        var store = new JsonDataStore(dataDirectory);
        var operators = await store.LoadOperatorsAsync();
        var (covenants, _) = await store.LoadCovenantsAsync();
        var (equipment, _) = await store.LoadEquipmentAsync();
        var strategies = await store.LoadStrategiesAsync();
        var ids = JsonSerializer.Deserialize<Dictionary<string, string>>(
            await File.ReadAllTextAsync(Path.Combine(dataDirectory, "covenant_ids.json")), JsonOptions)!;
        using var bosses = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dataDirectory, "bosses.json")));
        using var enemies = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dataDirectory, "enemy_types.json")));
        var catalog = JsonSerializer.SerializeToElement(new
        {
            covenants = covenants.Select(c => new
            {
                id = ids[c.Name], name = c.Name, kind = c.BondType == "SEASON" ? "core" : "extra",
                thresholds = LineupPlanner.HasSecondLevel(c) && !LineupPlanner.IsSecondLevelManual(c)
                    ? new[] { LineupPlanner.CreateGoal(c, 1).Count, LineupPlanner.CreateGoal(c, 2).Count }
                    : new[] { LineupPlanner.CreateGoal(c, 1).Count },
                effects = c.Effects.Select(e => new { condition = Clean(e.Condition), text = Clean(e.Text) })
            }),
            operators = operators.Select(o => new
            {
                name = o.Name, tier = o.Tier, obtain = o.Obtain,
                core = o.CoreCovenants.Select(c => ids[c]), extra = o.AdditionalCovenants.Select(c => ids[c]),
                normal = o.Normal.Traits.Select(t => new { type = t.TraitType, text = Clean(t.TraitDescription) }),
                elite = o.Elite.Traits.Select(t => new { type = t.TraitType, text = Clean(t.TraitDescription) })
            }),
            equipment = equipment.Select(e => new
            {
                name = e.Name, tier = e.Tier, categories = e.Categories,
                normal = Clean(e.Normal.EffectDescription), elite = Clean(e.Elite.EffectDescription)
            }),
            bosses = bosses.RootElement.GetProperty("bosses").EnumerateArray().Select(b => new
            {
                id = b.GetProperty("enemyId").GetString(), name = b.GetProperty("name").GetString(),
                abilities = b.GetProperty("abilityList").EnumerateArray().Select(a => Clean(a.GetString()!))
            }),
            enemyTypes = enemies.RootElement.GetProperty("types").EnumerateObject().Select(e => new
            {
                code = e.Value.GetProperty("typeCode").GetString(), name = e.Value.GetProperty("typeName").GetString()
            }),
            strategies = strategies.Select(s => new { id = s.Id, name = s.Name, initiator = s.Initiator, description = Clean(s.Description) })
        }, JsonOptions);
        var api = new HostApi(userDirectory, ids, operators, covenants, catalog);
        api.SettingsStore = new HostSettingsStore(Path.Combine(userDirectory, "settings.json"));
        api.Settings = api.SettingsStore.LoadSettings();
        await api.RefreshTrackingAsync();
        return api;
    }

    private static string Clean(string text) => Regex.Replace(text, @"<[@$][^>]*>|</>", "");

    private string CovenantName(string id) => _names.TryGetValue(id, out var name)
        ? name : throw new ArgumentException("盟约不存在。");

    public MatchInfo SetCovenantIncomplete(string id, bool incomplete)
    {
        _ = CovenantName(id);
        Match = Match with { Covenants = Match.Covenants.Select(c => c.Id == id ? c with { Incomplete = incomplete } : c).ToArray() };
        return RecomputeBans();
    }

    public MatchInfo SetOperatorBanned(string name, bool banned)
    {
        if (!_operators.Any(o => o.Name == name)) throw new ArgumentException("干员不存在。");
        _manualBans[name] = banned;
        return RecomputeBans();
    }

    private MatchInfo RecomputeBans()
    {
        var incomplete = Match.Covenants.Where(c => c.Incomplete).Select(c => CovenantName(c.Id)).ToHashSet();
        var core = _covenants.Where(c => c.BondType == "SEASON" && incomplete.Contains(c.Name)).Select(c => c.Name).ToHashSet();
        var extra = incomplete.Except(core).ToHashSet();
        var banned = OperatorBanCalculator.Compute(_operators, core, extra).ToHashSet();
        banned.UnionWith(_avatarBans);
        foreach (var (name, value) in _manualBans)
            if (value) banned.Add(name); else banned.Remove(name);
        Match = Match with { Source = "manual", BannedOperators = banned.ToArray() };
        RecomputeTracking();
        return Match;
    }

    private LineupDto ToDto(Lineup lineup) => new(lineup.Id, lineup.Name,
        lineup.Slots.Select(s => new LineupMember(s.OperatorName,
            s.TransformedCovenant.Length == 0 ? null : _ids[s.TransformedCovenant], s.Key)).ToArray(),
        lineup.Source, lineup.Note, lineup.UpdatedAt) { Goals = lineup.Goals ?? InferGoals(lineup) };

    private CovenantGoal[] InferGoals(Lineup lineup)
    {
        var counts = lineup.Slots.SelectMany(s =>
        {
            var op = _operators.Single(o => o.Name == s.OperatorName);
            return op.CoreCovenants.Concat(op.AdditionalCovenants)
                .Append(s.TransformedCovenant).Where(n => n.Length > 0).Distinct();
        }).GroupBy(n => n).ToDictionary(g => _ids[g.Key], g => g.Count());
        return Catalog.GetProperty("covenants").EnumerateArray().Select(c =>
        {
            var id = c.GetProperty("id").GetString()!;
            var level = c.GetProperty("thresholds").EnumerateArray()
                .Count(t => counts.GetValueOrDefault(id) >= t.GetInt32());
            return new CovenantGoal(id, level);
        }).Where(g => g.Level > 0).ToArray();
    }

    public async Task<LineupDto[]> LineupsAsync() => (await _lineups.LoadAllAsync()).Select(ToDto).ToArray();

    public async Task<LineupDto> SaveLineupAsync(LineupDto input)
    {
        ValidateLineup(input);
        var lineup = input.Id.Length == 0 ? new Lineup() :
            (await _lineups.LoadAllAsync()).SingleOrDefault(l => l.Id == input.Id)
            ?? throw new ArgumentException("要保存的阵容不存在。");
        var oldSlots = lineup.Slots.ToDictionary(s => s.OperatorName);
        lineup.Slots = input.Members.Select(m =>
        {
            var slot = oldSlots.TryGetValue(m.Name, out var old) ? old : new LineupSlot { OperatorName = m.Name };
            slot.Key = m.Key;
            slot.TransformedCovenant = m.Transform is null ? "" : CovenantName(m.Transform);
            return slot;
        }).ToList();
        lineup.Goals = input.Goals;
        lineup.Name = input.Name;
        lineup.Source = input.Source;
        lineup.Note = input.Note;
        await _lineups.SaveAsync(lineup);
        await RefreshTrackingAsync();
        return ToDto(lineup);
    }

    private void ValidateLineup(LineupDto input)
    {
        if (input.Id is null || input.Name is null || input.Members is null
            || input.Members.Any(m => m is null) || input.Goals?.Any(g => g is null) == true)
            throw new ArgumentException("阵容内容无效，请检查干员和来源。");
        ValidateId(input.Id, allowEmpty: true);
        if (input.Source is not ("user" or "planned") ||
            input.Members.Select(m => m.Name).Distinct().Count() != input.Members.Length ||
            input.Members.Any(m => !_operators.Any(o => o.Name == m.Name)))
            throw new ArgumentException("阵容内容无效，请检查干员和来源。");
        if (input.Goals is null || input.Goals.Any(g => !Catalog.GetProperty("covenants").EnumerateArray()
                .Any(c => c.GetProperty("id").GetString() == g.Id
                    && g.Level is 1 or 2 && g.Level <= c.GetProperty("thresholds").GetArrayLength())))
            throw new ArgumentException("阵容的目标盟约无效。");
        foreach (var member in input.Members)
            if (member.Transform is not null) _ = CovenantName(member.Transform);
    }

    private static void ValidateId(string id, bool allowEmpty = false)
    {
        if ((id.Length == 0 && !allowEmpty) || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("阵容编号无效。");
    }

    public async Task DeleteLineupAsync(string id)
    {
        ValidateId(id);
        await _lineups.DeleteAsync(id);
        await RefreshTrackingAsync();
    }

    public async Task<SessionDto> SessionAsync() => File.Exists(_sessionPath)
        ? JsonSerializer.Deserialize<SessionDto>(await File.ReadAllTextAsync(_sessionPath), JsonOptions)!
        : new(null, [], []);

    public async Task SaveSessionAsync(SessionDto session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        await File.WriteAllTextAsync(_sessionPath, JsonSerializer.Serialize(session, JsonOptions));
        await RefreshTrackingAsync();
    }

    public async Task<object?> InvokeAsync(string method, JsonElement[] args)
    {
        T Read<T>(int index) => args[index].Deserialize<T>(JsonOptions)!;
        switch (method)
        {
            case "catalog": return Catalog;
            case "settings": return Settings;
            case "saveSettings": SaveSettings(Read<SettingsDto>(0)); return null;
            case "appInfo": return AppInfo();
            case "openDataFolder": OpenDataFolder(); return null;
            case "exportData": return await ExportDataAsync();
            case "pickBackup": return await PickBackupAsync();
            case "importData": await ImportDataAsync(Read<string>(0), Read<string>(1)); return null;
            case "openUrl": OpenUrl(Read<string>(0)); return null;
            case "checkUpdate": return await CheckUpdateAsync();
            case "applyUpdate": await ApplyUpdateAsync(); return null;
            case "connection": return Connection;
            case "match": return Match;
            case "rescan":
                return await RecordInformationAsync();
            case "scanContinuous": await ScanContinuousAsync(Read<bool>(0)); return null;
            case "setCovenantIncomplete": return SetCovenantIncomplete(Read<string>(0), Read<bool>(1));
            case "setOperatorBanned": return SetOperatorBanned(Read<string>(0), Read<bool>(1));
            case "lineups": return await LineupsAsync();
            case "saveLineup": return await SaveLineupAsync(Read<LineupDto>(0));
            case "deleteLineup": await DeleteLineupAsync(Read<string>(0)); return null;
            case "session": return await SessionAsync();
            case "saveSession": await SaveSessionAsync(Read<SessionDto>(0)); return null;
            default: throw new ArgumentException("不支持的请求。");
        }
    }
}
