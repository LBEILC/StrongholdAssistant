using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using Moq;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class GameThemeSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigratesOldSettingsAndHonorsManualTheme()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"gameTheme":"closure"}""");
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        Assert.True(api.Settings.GameThemeAuto);
        api.UpdateConnection(new("connected"));
        var reader = api.CreateShopReader(Mock.Of<IOcrEngine>());
        int events = 0;
        api.Changed += (name, _) => { if (name == "connection") events++; };
        api.ObserveTheme(new(GameShopTheme.Terminal, .1));
        Assert.Equal("terminal", api.Settings.GameTheme);
        Assert.Equal(GameShopTheme.Terminal, reader.Theme);
        Assert.Equal("terminal", api.SettingsStore.LoadSettings().GameTheme);
        Assert.True(api.SettingsStore.LoadSettings().GameThemeAuto);
        api.ObserveTheme(new(GameShopTheme.Terminal, .1));
        Assert.Equal(1, events);
        api.SaveSettings(api.Settings with { GameThemeAuto = false, GameTheme = "closure" });
        api.ObserveTheme(new(GameShopTheme.Ursus, .1));
        Assert.Equal("closure", api.Settings.GameTheme);
        Assert.Equal(GameShopTheme.Closure, reader.Theme);
        Assert.Equal("ursus", api.Connection.Theme);
        Assert.False(api.SettingsStore.LoadSettings().GameThemeAuto);
        Assert.Equal("closure", api.SettingsStore.LoadSettings().GameTheme);
        api.UpdateConnection(new("connected"));
        Assert.Equal("ursus", api.Connection.Theme);
        api.UpdateConnection(new("searching"));
        Assert.Null(api.Connection.Theme);
        api.UpdateConnection(new("connected"));
        Assert.Equal("ursus", api.Connection.Theme);
        api.SaveSettings(api.Settings with { GameThemeAuto = true });
        Assert.Equal("ursus", api.Settings.GameTheme);
        Assert.Equal(GameShopTheme.Ursus, reader.Theme);
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
