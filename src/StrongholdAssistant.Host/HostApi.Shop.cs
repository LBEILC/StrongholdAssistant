using System.Diagnostics;
using System.Reflection;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    public HostSettingsStore SettingsStore { get; private set; } = null!;
    public SettingsDto Settings { get; private set; } = new("terminal", true, true);
    private ShopReader? _shopReader;
    private string? _detectedTheme;
    private string[] _trackedNames = [];
    public IReadOnlyList<string> TrackedNames => Volatile.Read(ref _trackedNames);
    private string[] _secondaryNames = [];
    public IReadOnlyList<string> SecondaryNames => Volatile.Read(ref _secondaryNames);
    private SessionDto _trackingSession = new(null, [], []);
    private LineupDto? _trackingTarget;
    private LineupDto? _trackingStage;
    public event Action? ShopChanged;

    public ShopReader CreateShopReader(IOcrEngine engine)
    {
        _shopReader = new ShopReader(engine, _operators.Where(o => o.Obtain is null).Select(o => o.Name)
            .Concat(_equipmentNames).ToArray()) { ReadPrices = true };
        _shopReader.Theme = Theme(Settings.GameTheme);
        return _shopReader;
    }

    private static GameShopTheme Theme(string theme) => theme switch
    {
        "terminal" => GameShopTheme.Terminal,
        "ursus" => GameShopTheme.Ursus,
        "closure" => GameShopTheme.Closure,
        _ => throw new ArgumentException("商店主题无效。")
    };

    private void ValidateSettings(SettingsDto settings)
    {
        HostSettingsStore.ValidateSides(settings);
        if (settings.FieldHotkey != "" && HotkeyParser.Parse(settings.FieldHotkey) is null)
            throw new ArgumentException("这个按键用不了，请换一个试试。");
        if (settings.StartupMode is not ("remember" or "sidebar" or "workbench"))
            throw new ArgumentException("启动形态无效。");
        if (settings.RefreshKey != "" && HotkeyParser.Parse(settings.RefreshKey) is null)
            throw new ArgumentException("刷新键无效。");
        if (settings.CollapseMode is not ("strip" or "up")) throw new ArgumentException("收起方式无效。");
        if (settings.FocusCovenants is null || settings.FocusOperators is null
            || settings.FocusCovenants.Any(id => !_names.ContainsKey(id))
            || settings.FocusOperators.Any(name => !_operators.Any(o => o.Name == name)))
            throw new ArgumentException("关注的盟约或干员无效。");
        var scale = settings.UiScale;
        if (!(scale.ValueKind == System.Text.Json.JsonValueKind.String && scale.GetString() == "auto")
            && !(scale.ValueKind == System.Text.Json.JsonValueKind.Number
                && scale.TryGetDouble(out var number) && double.IsFinite(number) && number >= 0.5 && number <= 2))
            throw new ArgumentException("界面缩放无效。");
        _ = Theme(settings.GameTheme);
    }

    public void SaveSettings(SettingsDto settings)
    {
        if (settings.GameThemeAuto && _detectedTheme is { } detected)
            settings = settings with { GameTheme = detected };
        settings = settings with { ToggleHotkey = Settings.ToggleHotkey, RunAsAdmin = Settings.RunAsAdmin, FieldAuto = false };
        if (settings.Control is null || settings.Control.Mode is not ("dryRun" or "confirm" or "auto")
            || settings.Control.StopHotkey != "" && HotkeyParser.Parse(settings.Control.StopHotkey) is null)
            throw new ArgumentException("自动操作设置无效。");
        settings = settings with { Control = settings.Control.Local() };
        ValidateSettings(settings);
        var theme = Theme(settings.GameTheme);
        SettingsStore.SaveSettings(settings);
        lock (_fieldGate) Settings = settings;
        if (!settings.Control.Enabled) StopControl("disabled");
        ConfigureLocalApi();
        ConfigureCollectionEnabled();
        if (!settings.FieldAuto)
            lock (_placementGate) { _placementStop?.Cancel(); _placementGeneration++; }
        SchedulePlacement();
        BoardRoutesEnabled = settings.Routes.Enabled;
        PublishBoardView();
        if (_shopReader is not null) _shopReader.Theme = theme;
        ShopChanged?.Invoke();
    }

    internal void ObserveTheme(GameThemeReading reading)
    {
        string theme = reading.Theme.ToString().ToLowerInvariant();
        if (_detectedTheme == theme) return;
        _detectedTheme = theme;
        Connection = Connection with { Theme = Connection.State == "connected" ? theme : null };
        if (Settings.GameThemeAuto && Settings.GameTheme != theme)
            SaveSettings(Settings with { GameTheme = theme });
        Changed?.Invoke("connection", Connection);
    }

    private async Task RefreshTrackingAsync()
    {
        _trackingSession = await SessionAsync();
        _trackingTarget = (await LineupsAsync()).SingleOrDefault(l => l.Id == _trackingSession.TargetLineupId);
        RecomputeTracking();
    }

    private void RecomputeTracking()
    {
        _trackingStage = _trackingTarget is { } target
            ? TrackingStageView(target, _trackingSession.Stage, Match.Economy?.Round) : null;
        var groupMembers = TrackingGroupMembers(_trackingStage?.Groups, Field, Match.BannedOperators);
        var allGroupMembers = (_trackingTarget?.Groups ?? []).Concat(
            (_trackingTarget?.Stages ?? []).SelectMany(s => s.Groups ?? [])).SelectMany(g => g.Members).ToArray();
        var groupNames = allGroupMembers.Select(m => m.Name).ToHashSet();
        var groupEquipment = allGroupMembers.SelectMany(m => m.Equipment).Select(e => e.Name).ToHashSet();
        var fixedNames = (_trackingStage?.Members ?? []).Select(m => m.Name).ToHashSet();
        var fixedEquipment = (_trackingStage?.Equipment ?? []).Concat(
            (_trackingStage?.Members ?? []).SelectMany(m => m.Equipment).Select(e => e.Name)).ToHashSet();
        var names = _trackingSession.Tracked
            .Where(n => !groupNames.Contains(n) || fixedNames.Contains(n))
            .Where(n => _operators.Any(o => o.Name == n && o.Obtain is null))
            .Concat(groupMembers.Where(m => _operators.Any(o => o.Name == m.Name && o.Obtain is null)).Select(m => m.Name))
            .Concat(_trackingSession.TrackedEquipment.Where(n => !groupEquipment.Contains(n) || fixedEquipment.Contains(n)))
            .Concat(groupMembers.SelectMany(m => m.Equipment).Select(e => e.Name))
            .Distinct().ToArray();
        if (_trackingStage is { } view) _trackingStage = view with { Members = [.. view.Members, .. groupMembers] };
        var goals = (_trackingStage?.Goals ?? []).Select(g => CovenantName(g.Id)).ToHashSet();
        var secondary = _operators.Where(o => o.Obtain is null
            && o.CoreCovenants.Concat(o.AdditionalCovenants).Any(goals.Contains)
            && !Match.BannedOperators.Contains(o.Name)
            && !names.Contains(o.Name)).Select(o => o.Name).ToArray();
        Volatile.Write(ref _trackedNames, names);
        Volatile.Write(ref _secondaryNames, secondary);
        ShopChanged?.Invoke();
    }

    internal static LineupMember[] TrackingGroupMembers(LineupGroupDto[]? groups, FieldState? field, string[] banned)
    {
        var owned = (field?.Roster ?? []).Where(r => !r.Sold).Select(r => r.Operator).ToHashSet();
        return (groups ?? []).Where(g => g.Members.Count(m => owned.Contains(m.Name)) < g.Need)
            .SelectMany(g => g.Members).Where(m => !owned.Contains(m.Name) && !banned.Contains(m.Name)).ToArray();
    }

    public int? UpgradeTarget => Match.Economy is { Level: { } level } economy
        ? _trackingTarget?.Upgrades?.FirstOrDefault(u => u.Round == economy.Round && level < u.Level)?.Level : null;

    // 与界面 stages.ts 的 currentStage、stageView 保持一致。
    private static LineupDto TrackingStageView(LineupDto lineup, int? fixedStage, int? round)
    {
        var stages = lineup.Stages ?? [];
        int index = fixedStage is { } stage && stage <= stages.Length ? stage
            : round is { } value ? Array.FindIndex(stages, s => value >= s.FromRound && value <= s.ToRound) : -1;
        if (index < 0) index = stages.Length;
        var all = stages.Append(new LineupStageDto("", 0, 0, lineup.Goals, lineup.Members, lineup.Equipment)
            { Groups = lineup.Groups }).ToArray();
        var current = all[index];
        var have = current.Members.Select(m => m.Name).ToHashSet();
        var laterKeys = all.Skip(index + 1).SelectMany(s => s.Members)
            .Where(m => m.Key == true && have.Add(m.Name))
            .Select(m => new LineupMember(m.Name, m.Transform, true));
        return lineup with { Goals = current.Goals, Members = [.. current.Members, .. laterKeys],
            Equipment = current.Equipment, Groups = current.Groups };
    }

    public void UpdateShopTracking(IReadOnlyList<ShopItem> items)
    {
        var names = TrackedNames;
        var secondary = SecondaryNames;
        foreach (var item in items)
        {
            item.IsTracked = names.Contains(item.Name);
            item.IsKey = item.IsTracked && (_trackingStage?.Members.Any(m =>
                (m.Name == item.Name && m.Key == true)
                || m.Equipment.Any(e => e.Name == item.Name && e.Priority)) ?? false);
            item.IsSecondary = secondary.Contains(item.Name);
        }
    }

    private static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("只能打开网页链接。");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void SaveRunAsAdmin(bool on)
    {
        SettingsStore.SaveRunAsAdmin(on);
        Settings = Settings with { RunAsAdmin = on };
    }

    public bool HotkeyFailed { get; set; }
    public bool StartupWorkbench { get; private set; }

    public void SaveHotkey(string combo)
    {
        var settings = Settings with { ToggleHotkey = combo };
        SettingsStore.SaveSettings(settings);
        lock (_fieldGate) Settings = settings;
        if (!settings.FieldAuto)
            lock (_placementGate) { _placementStop?.Cancel(); _placementGeneration++; }
        HotkeyFailed = false;
    }

    private object AppInfo() => new
    {
        elevated = ProcessElevation.Current,
        dataDir = DataDirectory,
        hotkeyFailed = HotkeyFailed,
        workbench = StartupWorkbench,
        version = typeof(HostApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0]
    };
}

