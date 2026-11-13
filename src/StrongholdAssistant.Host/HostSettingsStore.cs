using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed record FreeWindowBounds(double Left, double Top, double Width, double Height);
public sealed record DockSettings(double Width = 380, SidePanelOffset? FollowOffsetDip = null,
    SidePanelOffset? OverlayOffsetDip = null, FreeWindowBounds? FreeBounds = null,
    FreeWindowBounds? WorkbenchBounds = null, bool LastWorkbench = false,
    SidePanelOffset? FollowLeftOffsetDip = null, SidePanelOffset? OverlayRightOffsetDip = null);

// 同步合并写入，让窗口位置与界面设置共用文件而不相互覆盖。
public sealed class HostSettingsStore(string path)
{
    private JsonObject Read() => File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new();

    public SettingsDto LoadSettings()
    {
        var json = Read();
        var uiSkin = json["uiSkin"] is JsonValue value && value.TryGetValue<string>(out var skin)
            && skin is ("follow" or "terminal" or "ursus" or "closure") ? skin : "follow";
        var settings = new SettingsDto(json["gameTheme"]?.GetValue<string>() ?? "terminal",
            json["shopMarkers"]?.GetValue<bool>() ?? true,
            json["autoCheckUpdate"]?.GetValue<bool>() ?? true)
        {
            UiSkin = uiSkin,
            UpdateSource = json["updateSource"]?.GetValue<string>() ?? "github",
            UpdateProxy = json["updateProxy"]?.GetValue<string>() ?? "",
            MirrorChyanCdk = json["mirrorChyanCdk"]?.GetValue<string>() ?? "",
            UiScale = JsonSerializer.SerializeToElement(json["uiScale"] ?? JsonValue.Create("auto")),
            AlwaysOnTop = json["alwaysOnTop"]?.GetValue<bool>() ?? false,
            GameSource = json["gameSource"]?.GetValue<string>() ?? "auto",
            MumuSerial = json["mumuSerial"]?.GetValue<string>() ?? "",
            FollowGame = json["followGame"]?.GetValue<bool>() ?? true,
            DockSide = json["dockSide"]?.GetValue<string>() ?? "auto",
            OverlaySide = json["overlaySide"]?.GetValue<string>() ?? "left",
            ToggleHotkey = json["toggleHotkey"]?.GetValue<string>() ?? "Alt+S",
            RefreshKey = json["refreshKey"]?.GetValue<string>() ?? "D",
            StartupMode = json["startupMode"]?.GetValue<string>() ?? "remember",
            CloudSync = json["cloudSync"]?.Deserialize<CloudSyncState>(HostApi.JsonOptions) ?? new(false, null, null, null, []),
            ShareIncome = json["shareIncome"]?.GetValue<bool>() ?? false,
            CollapseMode = json["collapseMode"]?.GetValue<string>() ?? "strip",
            FocusCovenants = json["focusCovenants"]?.Deserialize<string[]>() ?? [],
            FocusOperators = json["focusOperators"]?.Deserialize<string[]>() ?? [],
            OperatorDefaults = json["operatorDefaults"]?.Deserialize<Dictionary<string, OperatorDefaults>>(HostApi.JsonOptions) ?? []
        };
        ValidateSides(settings);
        return settings;
    }

    public DockSettings LoadDock() => Read().Deserialize<DockSettings>(HostApi.JsonOptions)!;

    public bool StartInWorkbench() => LoadSettings().StartupMode switch
    {
        "workbench" => true, "sidebar" => false, _ => LoadDock().LastWorkbench
    };

    public static void ValidateSides(SettingsDto settings)
    {
        if (settings.DockSide is not ("auto" or "left" or "right"))
            throw new ArgumentException("侧栏贴靠边无效。");
        if (settings.OverlaySide is not ("left" or "right"))
            throw new ArgumentException("侧栏覆盖边无效。");
    }

    public void SaveSettings(SettingsDto settings)
    {
        ValidateSides(settings);
        if (settings.UiSkin is not ("follow" or "terminal" or "ursus" or "closure"))
            throw new ArgumentException("界面主题无效。");
        var proxy = settings.UpdateProxy;
        if (proxy != "" && (proxy is null
            || !System.Text.RegularExpressions.Regex.IsMatch(proxy, @"\A(?:http|socks5)://(?:\[[^\]]+\]|[^\s/:@?#\\]+):[0-9]+\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || !Uri.TryCreate(proxy, UriKind.Absolute, out var uri)
            || uri.Host.Length == 0 || uri.Port is < 1 or > 65535))
            throw new ArgumentException("代理地址格式不对。");
        Merge(settings);
    }
    public void SaveDock(DockSettings dock) => Merge(dock);

    private void Merge<T>(T value)
    {
        var json = Read();
        foreach (var property in JsonSerializer.SerializeToNode(value, HostApi.JsonOptions)!.AsObject())
            json[property.Key] = property.Value?.DeepClone();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", json.ToJsonString(HostApi.JsonOptions));
        File.Move(path + ".tmp", path, true);
    }
}
