using ASPAssistant.Core.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed record FreeWindowBounds(double Left, double Top, double Width, double Height, bool Maximized = false);
public sealed record DockSettings(double Width = 380, SidePanelOffset? FollowOffsetDip = null,
    SidePanelOffset? OverlayOffsetDip = null, FreeWindowBounds? FreeBounds = null,
    FreeWindowBounds? WorkbenchBounds = null, bool LastWorkbench = false,
    SidePanelOffset? FollowLeftOffsetDip = null, SidePanelOffset? OverlayRightOffsetDip = null,
    double? FollowHeightDip = null, double? OverlayHeightDip = null)
{
    public DockSettings WithWidth(double width) => this with { Width = Math.Clamp(width, 320, 720) };

    public DockSettings Redock() => this with
    {
        FollowOffsetDip = null, FollowLeftOffsetDip = null,
        OverlayOffsetDip = null, OverlayRightOffsetDip = null,
        FollowHeightDip = null, OverlayHeightDip = null
    };
}

// 同步合并写入，让窗口位置与界面设置共用文件而不相互覆盖。
public sealed class HostSettingsStore(string path)
{
    private JsonObject Read() => UserJsonFile.Read(path, json => JsonSerializer.Deserialize<JsonObject>(json)) ?? new();

    public SettingsDto LoadSettings()
    {
        var json = Read();
        if (json["routeHotkeyMigrated"] is null)
        {
            if (json["routes"] is JsonObject routes && routes["hotkey"] is JsonValue hotkey
                && hotkey.TryGetValue<string>(out var oldHotkey) && oldHotkey == "Alt")
                routes["hotkey"] = "Alt+R";
            json["routeHotkeyMigrated"] = true;
            Write(json);
        }
        var uiSkin = json["uiSkin"] is JsonValue value && value.TryGetValue<string>(out var skin)
            && skin is ("follow" or "terminal" or "ursus" or "closure" or "classic" or "glass" or "round") ? skin : "follow";
        var settings = new SettingsDto(json["gameTheme"]?.GetValue<string>() ?? "terminal",
            json["shopMarkers"]?.GetValue<bool>() ?? true,
            json["autoCheckUpdate"]?.GetValue<bool>() ?? true)
        {
            PlacementGoal = json["placementGoal"]?.GetValue<string>() is "leak" or "layers" ? json["placementGoal"]!.GetValue<string>() : "balanced",
            GameThemeAuto = json["gameThemeAuto"]?.GetValue<bool>() ?? true,
            MultiInstance = json["multiInstance"]?.GetValue<bool>() ?? false,
            Collect = json["collect"]?.GetValue<bool>() ?? false,
            // 场上自动识别暂时关掉（v1.2.4，用户 2026-11-16）：一拖人就重认、推荐位一直变，有人看不到开关老来问；
            // 设置里的入口也去掉了，只能点「识别」或按快捷键。旧设置里存的 true 一律不认
            FieldAuto = false,
            Control = (json["control"]?.Deserialize<ControlSettings>(HostApi.JsonOptions) ?? new()).Local(),
            FieldHotkey = json["fieldHotkey"]?.GetValue<string>() ?? "Alt+F",
            RunAsAdmin = json["runAsAdmin"]?.GetValue<bool>() ?? true, // v1.0.5 起默认打开：玩家的游戏多是管理员身份，不同意授权就按普通身份运行
            UiSkin = uiSkin,
            UiScheme = json["uiScheme"] is JsonValue schemeValue && schemeValue.TryGetValue<string>(out var scheme)
                && scheme is ("system" or "dark" or "light") ? scheme : "system",
            UiAccent = json["uiAccent"] is JsonValue accentValue && accentValue.TryGetValue<string>(out var accent)
                && ValidAccent(accent) ? accent : "",
            UiFont = json["uiFont"]?.GetValue<string>() ?? "",
            UiFontWeight = json["uiFontWeight"] is JsonValue weightValue
                && weightValue.TryGetValue<string>(out var weight) && weight is ("normal" or "medium" or "bold") ? weight : "normal",
            PlazaNotices = json["plazaNotices"]?.GetValue<bool>() ?? true,
            Routes = RouteOverlaySettings.Load(json["routes"]),
            OverlayLayers = json["overlayLayers"]?.Deserialize<OverlayLayerSettings>(HostApi.JsonOptions) ?? new(),
            MarkerStyles = MarkerStyles.Load(json["markerStyles"]),
            ShopScanSpeed = json["shopScanSpeed"] is JsonValue speedValue
                && speedValue.TryGetValue<string>(out var speed) && speed is ("normal" or "fast") ? speed : "normal",
            UpdateSource = json["updateSource"]?.GetValue<string>() ?? "github",
            UpdateProxy = json["updateProxy"]?.GetValue<string>() ?? "",
            AutoDownloadUpdate = json["autoDownloadUpdate"]?.GetValue<bool>() ?? true,
            MirrorChyanCdk = json["mirrorChyanCdk"]?.GetValue<string>() ?? "",
            GitHubToken = UnprotectSecret(json["githubToken"]?.GetValue<string>()),
            GitHubTokenExpiresAt = json["githubTokenExpiresAt"]?.GetValue<string>() ?? "",
            UiScale = JsonSerializer.SerializeToElement(json["uiScale"] ?? JsonValue.Create("auto")),
            AlwaysOnTop = json["alwaysOnTop"]?.GetValue<bool>() ?? false,
            HideCaptureBorder = json["hideCaptureBorder"]?.GetValue<bool>() ?? false,
            GameSource = json["gameSource"]?.GetValue<string>() ?? "auto",
            EmulatorSerial = json["emulatorSerial"]?.GetValue<string>() ?? json["mumuSerial"]?.GetValue<string>() ?? "",
            AdbAddress = json["adbAddress"]?.GetValue<string>() ?? "",
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
        if (settings.PlacementGoal is not ("leak" or "balanced" or "layers"))
            throw new ArgumentException("站位目标无效。");
        ValidateSides(settings);
        if (settings.Routes is null) throw new ArgumentException("敌人路线设置无效。");
        var address = settings.AdbAddress;
        if (address != "" && (address is null ||
            !(System.Text.RegularExpressions.Regex.IsMatch(address, @"\Aemulator-[0-9]+\z") ||
              System.Text.RegularExpressions.Regex.IsMatch(address, @"\A[\w.-]+:[0-9]{1,5}\z")
              && int.TryParse(address[(address.LastIndexOf(':') + 1)..], out var port) && port is >= 1 and <= 65535)))
            throw new ArgumentException("ADB 地址格式不对。");
        settings.Routes.Validate();
        if (settings.OverlayLayers is null) throw new ArgumentException("覆盖层设置无效。");
        if (settings.ShopScanSpeed is not ("normal" or "fast"))
            throw new ArgumentException("商店识别速度无效。");
        if (settings.UiSkin is not ("follow" or "terminal" or "ursus" or "closure" or "classic" or "glass" or "round"))
            throw new ArgumentException("界面主题无效。");
        if (settings.UiScheme is not ("system" or "dark" or "light"))
            throw new ArgumentException("界面配色无效。");
        if (!ValidAccent(settings.UiAccent)) throw new ArgumentException("主题色无效。");
        var proxy = settings.UpdateProxy;
        if (proxy != "" && (proxy is null
            || !System.Text.RegularExpressions.Regex.IsMatch(proxy, @"\A(?:http|socks5)://(?:\[[^\]]+\]|[^\s/:@?#\\]+):[0-9]+\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || !Uri.TryCreate(proxy, UriKind.Absolute, out var uri)
            || uri.Host.Length == 0 || uri.Port is < 1 or > 65535))
            throw new ArgumentException("代理地址格式不对。");
        var token = (settings.GitHubToken ?? "").Trim();
        if (token != "" && (token.Length is < 20 or > 255 || !System.Text.RegularExpressions.Regex.IsMatch(token, @"\A[A-Za-z0-9_-]+\z")))
            throw new ArgumentException("GitHub Token 格式不对，应该是一串字母和数字。");
        // Token 只加密落盘：文件被拷走或同步到别处时拿不到原文，换电脑也解不开（DPAPI 按用户加密）。
        // 值没变就沿用原来的密文，免得改任何一个设置都把 Token 重新加密一遍、文件每次都变。
        var storedToken = Read()["githubToken"]?.GetValue<string>();
        var persistToken = token.Length == 0 ? ""
            : storedToken is not null && string.Equals(UnprotectSecret(storedToken), token, StringComparison.Ordinal) ? storedToken
            : ProtectSecret(token);
        Merge(settings with
        {
            RunAsAdmin = Read()["runAsAdmin"]?.GetValue<bool>() ?? true,
            GitHubToken = persistToken
        });
    }

    /// <summary>加密凭据（DPAPI 当前用户）；解不开就当作没填，不让设置读失败。</summary>
    private static string ProtectSecret(string value)
        => "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));

    private static string UnprotectSecret(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        if (!stored.StartsWith("dpapi:", StringComparison.Ordinal)) return stored; // 手改过的旧文件按原文读
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(stored["dpapi:".Length..]), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return "";
        }
    }
    private static bool ValidAccent(string? accent) => accent is not null && (accent == ""
        || System.Text.RegularExpressions.Regex.IsMatch(accent, @"\A#[0-9a-fA-F]{6}\z"));

    public void SaveRunAsAdmin(bool on) => Merge(new { runAsAdmin = on });
    /// <summary>只记 Token 到期时间，不动其他设置（检查更新拿到响应头时顺手存）。</summary>
    public void SaveGitHubTokenExpiry(string expiresAt) => Merge(new { githubTokenExpiresAt = expiresAt ?? "" });
    public void SaveDock(DockSettings dock) => Merge(dock);

    private void Merge<T>(T value)
    {
        var json = Read();
        foreach (var property in JsonSerializer.SerializeToNode(value, HostApi.JsonOptions)!.AsObject())
            json[property.Key] = property.Value?.DeepClone();
        Write(json);
    }

    private void Write(JsonObject json)
    {
        if (!json.ContainsKey("emulatorSerial") && json["mumuSerial"] is { } legacySerial)
            json["emulatorSerial"] = legacySerial.DeepClone();
        json.Remove("mumuSerial");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        UserJsonFile.Write(path, json.ToJsonString(HostApi.JsonOptions));
    }
}
