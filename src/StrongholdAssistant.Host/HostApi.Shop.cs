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
            .Concat(_equipmentNames).ToArray());
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
        settings = settings with { ToggleHotkey = Settings.ToggleHotkey, RunAsAdmin = Settings.RunAsAdmin };
        ValidateSettings(settings);
        var theme = Theme(settings.GameTheme);
        SettingsStore.SaveSettings(settings);
        Settings = settings;
        BoardRoutesEnabled = settings.Routes.Enabled;
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
        var names = _trackingSession.Tracked
            .Where(n => _operators.Any(o => o.Name == n && o.Obtain is null))
            .Concat(_trackingSession.TrackedEquipment)
            .Distinct().ToArray();
        var goals = (_trackingStage?.Goals ?? []).Select(g => CovenantName(g.Id)).ToHashSet();
        var secondary = _operators.Where(o => o.Obtain is null
            && o.CoreCovenants.Concat(o.AdditionalCovenants).Any(goals.Contains)
            && !Match.BannedOperators.Contains(o.Name)
            && !names.Contains(o.Name)).Select(o => o.Name).ToArray();
        Volatile.Write(ref _trackedNames, names);
        Volatile.Write(ref _secondaryNames, secondary);
        ShopChanged?.Invoke();
    }

    // 与界面 stages.ts 的 currentStage、stageView 保持一致。
    private static LineupDto TrackingStageView(LineupDto lineup, int? fixedStage, int? round)
    {
        var stages = lineup.Stages ?? [];
        int index = fixedStage is { } stage && stage <= stages.Length ? stage
            : round is { } value ? Array.FindIndex(stages, s => value >= s.FromRound && value <= s.ToRound) : -1;
        if (index < 0) index = stages.Length;
        var all = stages.Append(new LineupStageDto("", 0, 0, lineup.Goals, lineup.Members, lineup.Equipment)).ToArray();
        var current = all[index];
        var have = current.Members.Select(m => m.Name).ToHashSet();
        var laterKeys = all.Skip(index + 1).SelectMany(s => s.Members)
            .Where(m => m.Key == true && have.Add(m.Name))
            .Select(m => new LineupMember(m.Name, m.Transform, true));
        return lineup with { Goals = current.Goals, Members = [.. current.Members, .. laterKeys], Equipment = current.Equipment };
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
        Settings = settings;
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

