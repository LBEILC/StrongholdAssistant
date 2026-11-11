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
    public event Action? ShopChanged;

    public ShopReader CreateShopReader(IOcrEngine engine)
    {
        _shopReader = new ShopReader(engine, _operators.Select(o => o.Name).ToArray());
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

    public void SaveSettings(SettingsDto settings)
    {
        var scale = settings.UiScale;
        if (!(scale.ValueKind == System.Text.Json.JsonValueKind.String && scale.GetString() == "auto")
            && !(scale.ValueKind == System.Text.Json.JsonValueKind.Number
                && scale.TryGetDouble(out var number) && double.IsFinite(number) && number >= 1 && number <= 2))
            throw new ArgumentException("界面缩放无效。");
        var theme = Theme(settings.GameTheme);
        SettingsStore.SaveSettings(settings);
        Settings = settings;
        if (_shopReader is not null) _shopReader.Theme = theme;
        ShopChanged?.Invoke();
    }

    private async Task RefreshTrackingAsync()
    {
        var session = await SessionAsync();
        var target = (await LineupsAsync()).SingleOrDefault(l => l.Id == session.TargetLineupId);
        var names = (target?.Members.Select(m => m.Name) ?? []).Concat(session.Tracked)
            .Where(n => _operators.Any(o => o.Name == n)).Distinct().ToArray();
        Volatile.Write(ref _trackedNames, names);
        ShopChanged?.Invoke();
    }

    public void UpdateShopTracking(IReadOnlyList<ShopItem> items)
    {
        var names = TrackedNames;
        foreach (var item in items) item.IsTracked = names.Contains(item.Name);
    }

    private static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("只能打开网页链接。");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static object AppInfo() => new
    {
        version = typeof(HostApi).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+')[0]
    };
}
