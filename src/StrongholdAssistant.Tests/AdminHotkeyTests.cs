using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class AdminHotkeyTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void KeysBlocked(bool app, bool game, bool expected) =>
        Assert.Equal(expected, ProcessElevation.KeysBlocked(app, game));

    [Fact]
    public async Task MigrationIsOnceAndOrdinarySaveCannotChangeAdmin()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, """{"routes":{"hotkey":"Alt"}}""");
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            Assert.Equal("Alt+R", api.Settings.Routes.Hotkey);
            api.SaveSettings(api.Settings with { RunAsAdmin = true });
            Assert.False(api.Settings.RunAsAdmin);
            Assert.False(api.SettingsStore.LoadSettings().RunAsAdmin);
            api.SaveRunAsAdmin(true);
            api.SaveSettings(api.Settings with { RunAsAdmin = false, Routes = new(Hotkey: "Alt") });
            Assert.True(api.Settings.RunAsAdmin);
            Assert.True(api.SettingsStore.LoadSettings().RunAsAdmin);
            Assert.Equal("Alt", api.SettingsStore.LoadSettings().Routes.Hotkey);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void BlockedCombinationRegistersAndDispatchesTapOnly()
    {
        var router = new RouteSystemHotkey();
        int registrations = 0, removals = 0, taps = 0;
        bool tapOnly = false;
        bool Register(Hotkey key) { Assert.Equal(new Hotkey(1, 0x52), key); registrations++; return true; }
        void Configure(bool blocked, string key) => router.Configure(blocked, HotkeyParser.Parse(key)!.Value,
            Register, () => removals++, value => tapOnly = value);
        Configure(false, "Alt+R");
        router.Tap(() => taps++);
        Assert.Equal(0, registrations);
        Configure(true, "Alt+R");
        Configure(true, "Alt+R");
        router.Tap(() => taps++);
        Assert.True(tapOnly);
        Assert.Equal(1, registrations);
        Assert.Equal(1, taps);
        Configure(false, "Alt+R");
        Assert.False(tapOnly);
        Assert.Equal(1, removals);
        Configure(true, "Alt");
        router.Tap(() => taps++);
        Assert.Equal(1, registrations);
        Assert.Equal(1, taps);
    }

    [Fact]
    public void RawCombinationReleasesWhenModifierIsReleased()
    {
        var router = new RouteHotkeyRouter();
        var key = HotkeyParser.Parse("Alt+R")!.Value;
        Assert.Null(router.Observe(0x52, true, 1, 1, 2, key));
        router.Observe(0x52, false, 1, 1, 2, key);
        Assert.Null(router.Observe(0xA4, true, 1, 1, 2, key, 1));
        Assert.True(router.Observe(0x52, true, 1, 1, 2, key, 1));
        Assert.Null(router.Observe(0x52, true, 1, 1, 2, key, 1));
        Assert.False(router.Observe(0xA4, false, 1, 1, 2, key));
        Assert.Null(router.Observe(0x52, false, 1, 1, 2, key));
    }
}
