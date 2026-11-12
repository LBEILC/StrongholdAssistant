using System.Buffers.Binary;
using System.Text.Json;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class CodexExtraTests(EnemyRecognitionFixture fixture) : IClassFixture<EnemyRecognitionFixture>
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public async Task GeneratedRelationshipsAndBossHealthReachCatalog()
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Data, "codex_extra.json")));
        var root = doc.RootElement;
        var equipment = root.GetProperty("equipment");
        var fuse = equipment.GetProperty("汲取者之喙").GetProperty("fuse");
        Assert.Equal("汲取者之心", fuse.GetProperty("with").GetString());
        Assert.Equal("汲取者之镰", fuse.GetProperty("result").GetString());
        var hammer = equipment.GetProperty("维式重锤");
        Assert.Equal("victoria", hammer.GetProperty("transformBond").GetString());
        Assert.Contains("凯瑟琳", hammer.GetProperty("sources").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("维式重锤", root.GetProperty("operators").GetProperty("凯瑟琳").GetProperty("givesEquipment").EnumerateArray().Select(x => x.GetString()));
        var bosses = root.GetProperty("bosses").EnumerateObject().ToArray();
        Assert.Equal(10, bosses.Length);
        string[] hiding = ["enemy_9013_acstmk", "enemy_9017_achunt", "enemy_9021_acduml", "enemy_9029_acfnc", "enemy_9035_acspider", "enemy_9037_aceel"];
        foreach (var boss in bosses)
        {
            Assert.Equal(hiding.Contains(boss.Name), boss.Value.GetProperty("hidingCore").GetBoolean());
            var hp = boss.Value.GetProperty("hp").EnumerateArray().ToArray();
            Assert.Equal(25, hp.Length);
            Assert.Equal("mode_single_funny", hp[0].GetProperty("mode").GetString());
            Assert.Equal("mode_multi_abyss", hp[^1].GetProperty("mode").GetString());
            Assert.All(hp, h => { Assert.True(h.GetProperty("hp").GetInt32() > 0); Assert.Equal(hiding.Contains(boss.Name), h.TryGetProperty("coreHp", out _)); });
        }
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Data, directory);
            var catalog = (JsonElement)(await api.InvokeAsync("catalog", []))!;
            var actual = catalog.GetProperty("equipment").EnumerateArray().Single(e => e.GetProperty("name").GetString() == "维式重锤");
            Assert.Equal("victoria", actual.GetProperty("transformBond").GetString());
            Assert.Contains("凯瑟琳", actual.GetProperty("sources").EnumerateArray().Select(x => x.GetString()));
            var boss = catalog.GetProperty("bosses").EnumerateArray().Single(b => b.GetProperty("id").GetString() == "enemy_9013_acstmk");
            Assert.True(boss.GetProperty("hidingCore").GetBoolean());
            Assert.Equal(25, boss.GetProperty("hp").GetArrayLength());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("solo-opening-level16-1920x1080.png", "mode_single_16")]
    [InlineData("mumu-solo-opening-1763x992.png", "mode_single_16")]
    [InlineData("ban-sample-1920x1080.png", "mode_multi_hard")]
    [InlineData("ban-sample-2-1920x1080.png", "mode_multi_hard")]
    [InlineData("user-ban-1-1920x1080.png", "mode_multi_funny")]
    [InlineData("user-ban-2-1920x1080.png", null)]
    [InlineData("user-ban-2-532x300.png", null)]
    [InlineData("user-ban-3-xianjing-1936x1119.png", "mode_multi_normal")]
    [InlineData("user-ban-4-1920x1080.png", null)]
    public async Task ReadsOpeningMode(string file, string? expected)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Data, "codex_extra.json")));
        var modes = doc.RootElement.GetProperty("modes").Deserialize<GameModeInfo[]>(HostApi.JsonOptions)!;
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        int w = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), h = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        var (png, width, height) = ScreenshotNormalizer.EnsureAnalysable(bytes, w, h);
        var mode = await new InformationScreenDetector(fixture.Engine, new GarrisonOcrStrategy()).ReadModeAsync(png, width, height, modes);
        Assert.Equal(expected, mode?.Id);
    }
}
