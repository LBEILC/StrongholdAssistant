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
    private string[] _trackedNames = [];
    public IReadOnlyList<string> TrackedNames => Volatile.Read(ref _trackedNames);
    private string[] _secondaryNames = [];
    public IReadOnlyList<string> SecondaryNames => Volatile.Read(ref _secondaryNames);
    private SessionDto _trackingSession = new(null, [], []);
    private LineupDto? _trackingTarget;
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
        if (settings.FocusCovenants is null || settings.FocusOperators is null
            || settings.FocusCovenants.Any(id => !_names.ContainsKey(id))
            || settings.FocusOperators.Any(name => !_operators.Any(o => o.Name == name)))
            throw new ArgumentException("关注的盟约或干员无效。");
        var scale = settings.UiScale;
        if (!(scale.ValueKind == System.Text.Json.JsonValueKind.String && scale.GetString() == "auto")
            && !(scale.ValueKind == System.Text.Json.JsonValueKind.Number
                && scale.TryGetDouble(out var number) && double.IsFinite(number) && number >= 1 && number <= 2))
            throw new ArgumentException("界面缩放无效。");
        _ = Theme(settings.GameTheme);
    }

    public void SaveSettings(SettingsDto settings)
    {
        ValidateSettings(settings);
        var theme = Theme(settings.GameTheme);
        SettingsStore.SaveSettings(settings);
        Settings = settings;
        if (_shopReader is not null) _shopReader.Theme = theme;
        ShopChanged?.Invoke();
    }

    private async Task RefreshTrackingAsync()
    {
        _trackingSession = await SessionAsync();
        _trackingTarget = (await LineupsAsync()).SingleOrDefault(l => l.Id == _trackingSession.TargetLineupId);
        RecomputeTracking();
    }

    private void RecomputeTracking()
    {
        var names = _trackingSession.Tracked
            .Where(n => _operators.Any(o => o.Name == n && o.Obtain is null))
            .Concat(_trackingSession.TrackedEquipment)
            .Distinct().ToArray();
        var goals = (_trackingTarget?.Goals ?? []).Select(g => CovenantName(g.Id)).ToHashSet();
        var secondary = _operators.Where(o => o.Obtain is null
            && o.CoreCovenants.Concat(o.AdditionalCovenants).Any(goals.Contains)
            && !Match.BannedOperators.Contains(o.Name)
            && !names.Contains(o.Name)).Select(o => o.Name).ToArray();
        Volatile.Write(ref _trackedNames, names);
        Volatile.Write(ref _secondaryNames, secondary);
        ShopChanged?.Invoke();
    }

    public void UpdateShopTracking(IReadOnlyList<ShopItem> items)
    {
        var names = TrackedNames;
        var secondary = SecondaryNames;
        foreach (var item in items)
        {
            item.IsTracked = names.Contains(item.Name);
            item.IsKey = item.IsTracked && (_trackingTarget?.Members.Any(m => m.Name == item.Name && m.Key == true) ?? false);
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

    private object AppInfo() => new
    {
        dataDir = DataDirectory,
        version = typeof(HostApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0]
    };
}

