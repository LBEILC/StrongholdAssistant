using System.Text.Json;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class RouteSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));

    public RouteSettingsTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void LoadDefaultsPerFieldAndRejectInvalidSaves()
    {
        var path = Path.Combine(_directory, "settings.json");
        var store = new HostSettingsStore(path);
        File.WriteAllText(path, "{}");
        Assert.Equal(new RouteOverlaySettings(), store.LoadSettings().Routes);
        File.WriteAllText(path, """{"routes":{"enabled":true,"display":"hold"}}""");
        Assert.Equal(new RouteOverlaySettings(true, "hold"), store.LoadSettings().Routes);
        File.WriteAllText(path, """{"routes":{"enabled":true,"display":"bad","peekSeconds":16,"hotkey":"Unknown","overlap":7,"cycleSeconds":0,"fly":"bad","side":"right"}}""");
        Assert.Equal(new RouteOverlaySettings(true, Side: "right"), store.LoadSettings().Routes);
        File.WriteAllText(path, """{"routes":{"enabled":"yes","display":"peek","peekSeconds":"bad","hotkey":"Unknown","overlap":"all","cycleSeconds":3,"fly":"off","side":"bad"}}""");
        var valid = new RouteOverlaySettings(Display: "peek", Overlap: "all", CycleSeconds: 3, Fly: "off");
        Assert.Equal(valid, store.LoadSettings().Routes);
        var settings = store.LoadSettings();
        store.SaveSettings(settings with { Routes = valid with { Hotkey = "F2" } });
        Assert.Equal(valid with { Hotkey = "F2" }, store.LoadSettings().Routes);
        foreach (var invalid in new[] { valid with { Display = "bad" }, valid with { PeekSeconds = 2 },
            valid with { Hotkey = "Unknown" }, valid with { Overlap = "bad" }, valid with { CycleSeconds = 6 },
            valid with { Fly = "bad" }, valid with { Side = "bad" } })
            Assert.Throws<ArgumentException>(() => store.SaveSettings(settings with { Routes = invalid }));
    }

    [Fact]
    public async Task EnableTracksSettingsAndCatalogKeepsAllVariantsInOrder()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var api = await HostApi.CreateAsync(data, _directory);
        Assert.False(api.BoardRoutesEnabled);
        api.SaveSettings(api.Settings with { Routes = new(Enabled: true) });
        Assert.True(api.BoardRoutesEnabled);
        Assert.True((await HostApi.CreateAsync(data, _directory)).BoardRoutesEnabled);
        api.SaveSettings(api.Settings with { Routes = new(Enabled: false) });
        Assert.False(api.BoardRoutesEnabled);
        api.RecognizeBoard = (_, _, _) => throw new Exception("关闭后不应识别棋盘");
        api.ObserveBoardFrame([], null);
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "enemy_types.json")));
        var types = source.RootElement.GetProperty("types");
        Assert.Equal(types.EnumerateObject().Count(), api.Catalog.GetProperty("enemyTypes").GetArrayLength());
        foreach (var type in api.Catalog.GetProperty("enemyTypes").EnumerateArray())
            foreach (var half in new[] { "firstHalf", "secondHalf" })
            {
                var expected = types.GetProperty(type.GetProperty("code").GetString()!).GetProperty(half + "Variants");
                var actual = type.GetProperty(half);
                Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
                for (int i = 0; i < expected.GetArrayLength(); i++)
                {
                    Assert.Equal(expected[i].GetProperty("specialEnemyId").GetString(), actual[i].GetProperty("id").GetString());
                    Assert.Equal(expected[i].GetProperty("enemyName").GetString(), actual[i].GetProperty("name").GetString());
                    Assert.Equal(expected[i].GetProperty("weight").GetDouble(), actual[i].GetProperty("weight").GetDouble());
                }
            }
    }

    [Theory]
    [InlineData("Alt", 0xA4u)]
    [InlineData("R", 0x52u)]
    [InlineData("Mouse5", 0x06u)]
    public async Task HotkeyPublishesOnceOnPressAndRelease(string name, uint key)
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        var events = new List<bool>();
        api.RouteHotkeyChanged += events.Add;
        var router = new RouteHotkeyRouter();
        var hotkey = HotkeyParser.Parse(name)!.Value;
        Assert.Equal(0u, hotkey.Modifiers);
        foreach (var foreground in new nint[] { 1, 2, 3 })
            foreach (bool down in new[] { true, true, false, false })
                if (router.Observe(key, down, foreground, 1, 2, hotkey) is { } state)
                    api.PublishRouteHotkey(state);
        Assert.Equal(new[] { true, false, true, false }, events);
        Assert.Equal(new Hotkey(0, 0x11), HotkeyParser.Parse("Ctrl"));
        Assert.Equal(new Hotkey(0, 0x10), HotkeyParser.Parse("Shift"));
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
