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
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    public string DataDirectory { get; }
    public System.Windows.Window? DialogOwner { get; set; }
    private readonly LineupStore _lineups;
    private readonly Dictionary<string, string> _ids;
    private readonly Dictionary<string, string> _names;
    private readonly List<Operator> _operators;
    private readonly HashSet<string> _equipmentNames;
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
        _equipmentNames = catalog.GetProperty("equipment").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToHashSet();
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
        SetBanGroupOperator(name, banned);
        return RecomputeBans();
    }

    private MatchInfo RecomputeBans()
    {
        var incomplete = Match.Covenants.Where(c => c.Incomplete).Select(c => CovenantName(c.Id)).ToHashSet();
        var core = _covenants.Where(c => c.BondType == "SEASON" && incomplete.Contains(c.Name)).Select(c => c.Name).ToHashSet();
        var extra = incomplete.Except(core).ToHashSet();
        var banned = OperatorBanCalculator.Compute(_operators, core, extra).ToHashSet();
        banned.UnionWith(_avatarBans);
        var groups = RecomputeBanGroups();
        banned.UnionWith(_banGroups.Values.SelectMany(g => g.Slots.Values).Select(s => s.Name).OfType<string>());
        foreach (var (name, value) in _manualBans)
            if (value) banned.Add(name); else banned.Remove(name);
        Match = Match with { Source = "manual", BannedOperators = banned.ToArray(), BanGroups = groups };
        RecomputeTracking();
        return Match;
    }

    private LineupDto ToDto(Lineup lineup) => new(lineup.Id, lineup.Name,
        lineup.Slots.Select(s => new LineupMember(s.OperatorName,
            s.TransformedCovenant.Length == 0 ? null : _ids[s.TransformedCovenant], s.Key)).ToArray(),
        lineup.Source, lineup.Note, lineup.UpdatedAt)
        { Goals = lineup.Goals ?? InferGoals(lineup), Equipment = lineup.Equipment, StrategyId = lineup.StrategyId };

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
        lineup.Equipment = input.Equipment;
        lineup.Name = input.Name;
        lineup.StrategyId = input.StrategyId;
        lineup.Source = input.Source;
        lineup.Note = input.Note;
        await _lineups.SaveAsync(lineup);
        await RefreshTrackingAsync();
        return ToDto(lineup);
    }

    private void ValidateLineup(LineupDto input)
    {
        if (input.StrategyId is not null && !Catalog.GetProperty("strategies").EnumerateArray()
                .Any(s => s.GetProperty("id").GetString() == input.StrategyId))
            throw new ArgumentException("阵容策略无效。");
        ValidateEquipment(input.Equipment);
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

    public async Task<SessionDto> SessionAsync()
    {
        await _sessionGate.WaitAsync();
        try { return await ReadSessionAsync(); }
        finally { _sessionGate.Release(); }
    }

    private async Task<SessionDto> ReadSessionAsync()
    {
        if (!File.Exists(_sessionPath)) return new(null, [], []);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(_sessionPath));
        var session = json.RootElement.Deserialize<SessionDto>(JsonOptions)!;
        if (!json.RootElement.TryGetProperty("trackedEquipment", out _))
        {
            var target = (await LineupsAsync()).SingleOrDefault(l => l.Id == session.TargetLineupId);
            session = session with
            {
                Tracked = session.Tracked.Concat(target?.Members.Select(m => m.Name) ?? []).Distinct().ToArray(),
                TrackedEquipment = (target?.Equipment ?? []).Distinct().ToArray()
            };
            // 写入字段作为迁移标记，之后取消追踪不会重新合入目标名单。
            await WriteSessionAsync(session);
        }
        return session;
    }

    public async Task SaveSessionAsync(SessionDto session)
    {
        ValidateEquipment(session.TrackedEquipment);
        await _sessionGate.WaitAsync();
        try { await WriteSessionAsync(session); }
        finally { _sessionGate.Release(); }
        await RefreshTrackingAsync();
    }

    private async Task WriteSessionAsync(SessionDto session)
    {
        Directory.CreateDirectory(DataDirectory);
        await File.WriteAllTextAsync(_sessionPath + ".tmp", JsonSerializer.Serialize(session, JsonOptions));
        File.Move(_sessionPath + ".tmp", _sessionPath, overwrite: true);
    }

    public async Task<MatchInfo> ResetMatchAsync()
    {
        await _scanGate.WaitAsync();
        try
        {
            _manualBans.Clear();
            _avatarBans.Clear();
            _banGroups.Clear();
            Match = new("none", null, _covenants.Select(c => new CovenantState(_ids[c.Name], false)).ToArray(), [], null, []);
            RecomputeTracking();
            Changed?.Invoke("match", Match);
            return Match;
        }
        finally { _scanGate.Release(); }
    }

    public void CopyText(string text)
    {
        if (text is null || text.Length > 2000)
            throw new ArgumentException("复制文字无效或超过 2000 字。");
        var dispatcher = DialogOwner?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher
            ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(() =>
        {
            try { System.Windows.Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.COMException)
            {
                throw new ArgumentException("剪贴板被其他程序占用，稍后再试。");
            }
        });
    }

    private void ValidateEquipment(string[] names)
    {
        if (names is null || names.Any(n => !_equipmentNames.Contains(n)))
            throw new ArgumentException("装备不存在。"); // TODO(design): 确认无效装备的错误文案。
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
            case "resetMatch": return await ResetMatchAsync();
            case "copyText": CopyText(Read<string>(0)); return null;
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
