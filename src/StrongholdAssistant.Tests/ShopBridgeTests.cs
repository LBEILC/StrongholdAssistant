using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class ShopBridgeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
    private static Task<object?> Call(HostApi api, string method, object value) =>
        api.InvokeAsync(method, [JsonSerializer.SerializeToElement(value, HostApi.JsonOptions)]);

    [Fact]
    public async Task TrackingFollowsSessionAndTargetLineupChanges()
    {
        var api = await CreateApi();
        var lineup = Assert.IsType<LineupDto>(await Call(api, "saveLineup",
            new LineupDto("", "测试", [new("卡达"), new("慕斯")], "user", null, default)));
        await Call(api, "saveSession", new SessionDto(lineup.Id, ["卡达"], ["角峰", "卡达"]));
        Assert.Equal(new[] { "卡达", "慕斯", "角峰" }, api.TrackedNames);
        await Call(api, "saveLineup", lineup with { Members = [new("地灵")] });
        Assert.Equal(new[] { "地灵", "角峰", "卡达" }, api.TrackedNames);
        var other = Assert.IsType<LineupDto>(await Call(api, "saveLineup",
            new LineupDto("", "其他", [new("陨星")], "user", null, default)));
        await Call(api, "saveSession", new SessionDto(other.Id, [], ["褐果"]));
        Assert.Equal(new[] { "陨星", "褐果" }, api.TrackedNames);
        Assert.Equal(api.TrackedNames, (await CreateApi()).TrackedNames);
        await Call(api, "deleteLineup", other.Id);
        Assert.Equal(new[] { "褐果" }, api.TrackedNames);
    }

    [Fact]
    public async Task TerminalSettingsProduceTrackedItemWithValidRegion()
    {
        var api = await CreateApi();
        using var engine = new MaaOcrEngine(Path.Combine(AppContext.BaseDirectory, "data"));
        var reader = api.CreateShopReader(engine);
        foreach (var (theme, expected) in new[] { ("closure", GameShopTheme.Closure), ("ursus", GameShopTheme.Ursus), ("terminal", GameShopTheme.Terminal) })
        {
            await Call(api, "saveSettings", new SettingsDto(theme, true, true));
            Assert.Equal(expected, reader.Theme);
        }
        await Call(api, "saveSession", new SessionDto(null, [], ["卡达"]));
        var items = await reader.ReadAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,
            "TestData", "shop-terminal-tier1-color.png")), 1920, 1080, api.TrackedNames);
        api.UpdateShopTracking(items);
        var item = Assert.Single(items);
        Assert.Equal("卡达", item.Name);
        Assert.True(item.IsTracked);
        Assert.InRange(item.OcrRegion.X, 0, 1 - item.OcrRegion.Width);
        Assert.InRange(item.OcrRegion.Y, .91, .97);
        Assert.True(item.OcrRegion.Width > 0 && item.OcrRegion.Height > 0);
    }

    [Fact]
    public async Task SettingsRoundTripPreservesDockAndDefaultsForOldFiles()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{"width":421,"followOffsetDip":{"x":-32,"y":71}}""");
        var api = await CreateApi();
        Assert.Equal(JsonSerializer.Serialize(new SettingsDto("terminal", true, true)),
            JsonSerializer.Serialize(await api.InvokeAsync("settings", [])));
        var settings = new SettingsDto("ursus", false, false) { UiScale = JsonSerializer.SerializeToElement(1.5) };
        await Call(api, "saveSettings", settings);
        Assert.Equal(new DockSettings(421, new(-32, 71)), api.SettingsStore.LoadDock());
        Assert.Equal(JsonSerializer.Serialize(settings), JsonSerializer.Serialize((await CreateApi()).Settings));
        api.SettingsStore.SaveDock(new(360, new(12, 24)));
        var restarted = await CreateApi();
        Assert.Equal(JsonSerializer.Serialize(settings), JsonSerializer.Serialize(restarted.Settings));
        Assert.Equal(new DockSettings(360, new(12, 24)), restarted.SettingsStore.LoadDock());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
