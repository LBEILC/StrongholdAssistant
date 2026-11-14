using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class HostSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ShopScanSpeedDefaultsAndSurvivesReload()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{}");
        var store = new HostSettingsStore(path);
        Assert.Equal("normal", store.LoadSettings().ShopScanSpeed);
        store.SaveSettings(store.LoadSettings() with { ShopScanSpeed = "fast" });
        Assert.Equal("fast", new HostSettingsStore(path).LoadSettings().ShopScanSpeed);
        Assert.Contains("\"shopScanSpeed\":\"fast\"", File.ReadAllText(path));
        Assert.Throws<ArgumentException>(() => store.SaveSettings(store.LoadSettings() with { ShopScanSpeed = "invalid" }));
        File.WriteAllText(path, """{"shopScanSpeed":123}""");
        Assert.Equal("normal", store.LoadSettings().ShopScanSpeed);
        File.WriteAllText(path, """{"shopScanSpeed":"invalid"}""");
        Assert.Equal("normal", store.LoadSettings().ShopScanSpeed);
    }

    [Fact]
    public void PlazaNoticesDefaultsAndSurvivesReload()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{}");
        var store = new HostSettingsStore(path);
        Assert.True(store.LoadSettings().PlazaNotices);
        store.SaveSettings(store.LoadSettings() with { PlazaNotices = false });
        Assert.False(new HostSettingsStore(path).LoadSettings().PlazaNotices);
        store.SaveSettings(store.LoadSettings() with { PlazaNotices = true });
        Assert.True(new HostSettingsStore(path).LoadSettings().PlazaNotices);
    }

    [Fact]
    public void UiSkinDefaultsAndSurvivesReload()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"gameTheme":"closure"}""");
        var store = new HostSettingsStore(path);
        Assert.Equal("follow", store.LoadSettings().UiSkin);
        store.SaveSettings(store.LoadSettings() with { UiSkin = "ursus" });
        Assert.Equal("ursus", new HostSettingsStore(path).LoadSettings().UiSkin);
    }

    [Fact]
    public void ClassicUiSkinSurvivesReload()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new HostSettingsStore(path);
        store.SaveSettings(store.LoadSettings() with { UiSkin = "classic" });
        Assert.Equal("classic", new HostSettingsStore(path).LoadSettings().UiSkin);
    }

    [Fact]
    public void InvalidUiSkinIsRejectedOnSave()
    {
        var store = new HostSettingsStore(Path.Combine(_directory, "settings.json"));
        var error = Assert.Throws<ArgumentException>(() =>
            store.SaveSettings(store.LoadSettings() with { UiSkin = "invalid" }));
        Assert.Equal("界面主题无效。", error.Message);
    }

    [Fact]
    public void CloudAccountSurvivesAtomicSettingsAndDockWrites()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"cloudSync":{"enabled":false,"since":null,"pushedAt":null,"prefsAt":null,"deleted":[]}}""");
        var store = new HostSettingsStore(path);
        Assert.Null(store.LoadSettings().CloudSync.Account);
        store.SaveSettings(store.LoadSettings() with
        { CloudSync = new(true, null, null, null, []) { Account = "account-1" } });
        store.SaveDock(new(Width: 420));
        Assert.Equal("account-1", new HostSettingsStore(path).LoadSettings().CloudSync.Account);
        Assert.Equal(new[] { path }, Directory.GetFiles(_directory));
    }

    [Fact]
    public void SideSettingsDefaultAndSurviveBridgeAndDockWrites()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"followGame":true}""");
        var store = new HostSettingsStore(path);
        Assert.Equal(("auto", "left"), (store.LoadSettings().DockSide, store.LoadSettings().OverlaySide));
        store.SaveSettings(store.LoadSettings() with { DockSide = "left", OverlaySide = "right" });
        var dock = new DockSettings(FollowOffsetDip: new(1, 2), FollowLeftOffsetDip: new(3, 4),
            OverlayOffsetDip: new(5, 6), OverlayRightOffsetDip: new(7, 8));
        store.SaveDock(dock);
        Assert.Equal(("left", "right"), (store.LoadSettings().DockSide, store.LoadSettings().OverlaySide));
        Assert.Equal(dock, store.LoadDock());
    }

    [Theory]
    [InlineData("invalid", "left")]
    [InlineData("auto", "auto")]
    public async Task InvalidSidesAreRejectedOnSaveAndLoad(string dockSide, string overlaySide)
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        Assert.Throws<ArgumentException>(() => api.SaveSettings(api.Settings with { DockSide = dockSide, OverlaySide = overlaySide }));
        var path = Path.Combine(_directory, "settings.json");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { dockSide, overlaySide }));
        Assert.Throws<ArgumentException>(() => new HostSettingsStore(path).LoadSettings());
    }

    [Fact]
    public void NewAndMissingHotkeySettingsDefaultToAltS()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new HostSettingsStore(path);
        Assert.Equal("Alt+S", store.LoadSettings().ToggleHotkey);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, """{"followGame":true,"alwaysOnTop":true,"collapseMode":"up"}""");
        Assert.Equal("Alt+S", store.LoadSettings().ToggleHotkey);
        Assert.Equal("Alt+S", new SettingsDto("terminal", true, true).ToggleHotkey);
        Assert.Empty(store.LoadSettings().OperatorDefaults);
        Assert.Equal("D", store.LoadSettings().RefreshKey);
        Assert.Equal("remember", store.LoadSettings().StartupMode);
        Assert.False(store.StartInWorkbench());
        Assert.Equal("github", store.LoadSettings().UpdateSource);
        Assert.Equal("", store.LoadSettings().MirrorChyanCdk);
        store.SaveSettings(store.LoadSettings() with { UpdateSource = "mirrorchyan", MirrorChyanCdk = "test-cdk" });
        Assert.Equal("mirrorchyan", store.LoadSettings().UpdateSource);
        Assert.Equal("test-cdk", store.LoadSettings().MirrorChyanCdk);
    }

    [Fact]
    public void StartupChoiceAndLastLayoutSurviveSettingsWrites()
    {
        var store = new HostSettingsStore(Path.Combine(_directory, "settings.json"));
        store.SaveDock(new(LastWorkbench: true));
        Assert.True(store.StartInWorkbench());
        store.SaveSettings(store.LoadSettings() with { StartupMode = "sidebar", RefreshKey = "Tab" });
        Assert.False(store.StartInWorkbench());
        Assert.Equal("Tab", store.LoadSettings().RefreshKey);
        store.SaveSettings(store.LoadSettings() with { StartupMode = "workbench", RefreshKey = "" });
        store.SaveDock(new(LastWorkbench: false));
        Assert.True(store.StartInWorkbench());
        Assert.Equal("", store.LoadSettings().RefreshKey);
        store.SaveSettings(store.LoadSettings() with { StartupMode = "remember" });
        Assert.False(store.StartInWorkbench());
    }

    [Fact]
    public async Task OperatorDefaultsSurviveBridgeSaveReloadAndBackup()
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        var defaults = new Dictionary<string, OperatorDefaults>
        {
            ["艾丝黛尔"] = new("skchr_estell_2", "uniequip_002_estell"),
            ["凯尔希·思衡托"] = new(Skill: "skchr_kalts2_3")
        };
        var settings = api.Settings with { OperatorDefaults = defaults };
        var input = System.Text.Json.JsonSerializer.SerializeToElement(new[] { settings }, HostApi.JsonOptions);
        await api.InvokeAsync("saveSettings", input.EnumerateArray().ToArray());
        api.SettingsStore.SaveDock(new(Width: 420));
        var restored = api.SettingsStore.LoadSettings();
        Assert.Equal(defaults, restored.OperatorDefaults);
        var backupPath = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(backupPath);
        using var backup = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(backupPath));
        Assert.Equal("uniequip_002_estell", backup.RootElement.GetProperty("settings")
            .GetProperty("operatorDefaults").GetProperty("艾丝黛尔").GetProperty("module").GetString());
    }

    [Theory]
    [InlineData("Alt+Q")]
    [InlineData("")]
    [InlineData("Ctrl+Shift+F2")]
    public void SavedHotkeySurvivesSettingsAndDockWrites(string combo)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { toggleHotkey = combo }));
        var store = new HostSettingsStore(path);
        var settings = store.LoadSettings();
        Assert.Equal(combo, settings.ToggleHotkey);
        store.SaveSettings(settings with { AlwaysOnTop = true });
        store.SaveDock(new(FreeBounds: new(100, 200, 380, 900), OverlayOffsetDip: new(12, 34)));
        Assert.Equal(combo, new HostSettingsStore(path).LoadSettings().ToggleHotkey);
    }

    [Theory]
    [InlineData("sidebar", true, false)]
    [InlineData("workbench", false, true)]
    [InlineData("remember", true, true)]
    [InlineData("remember", false, false)]
    public async Task AppInfoReturnsStartupLayout(string mode, bool lastWorkbench, bool expected)
    {
        var store = new HostSettingsStore(Path.Combine(_directory, "settings.json"));
        store.SaveSettings(store.LoadSettings() with { StartupMode = mode });
        store.SaveDock(new(LastWorkbench: lastWorkbench));
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        // 修改下次启动设置，不应改变本次启动结果。
        api.SaveSettings(api.Settings with { StartupMode = expected ? "sidebar" : "workbench" });
        var info = System.Text.Json.JsonSerializer.SerializeToElement(await api.InvokeAsync("appInfo", []));
        Assert.Equal(expected, info.GetProperty("workbench").GetBoolean());
        Assert.Equal(expected, api.StartupWorkbench);
    }

    [Fact]
    public async Task HalfUiScaleSurvivesSaveAndReload()
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        api.SaveSettings(api.Settings with { UiScale = System.Text.Json.JsonSerializer.SerializeToElement(0.5) });

        var restored = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        Assert.Equal(0.5, restored.Settings.UiScale.GetDouble());
    }

    [Fact]
    public async Task UiScaleBelowHalfIsRejected()
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        var settings = api.Settings with { UiScale = System.Text.Json.JsonSerializer.SerializeToElement(0.4) };

        var error = Assert.Throws<ArgumentException>(() => api.SaveSettings(settings));
        Assert.Equal("界面缩放无效。", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
