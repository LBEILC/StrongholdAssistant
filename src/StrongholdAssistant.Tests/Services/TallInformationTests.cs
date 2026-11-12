using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests.Services;

[Collection("Native OCR")]
public class TallInformationTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
    : IClassFixture<EnemyRecognitionFixture>
{
    [Theory]
    [InlineData("top")]
    [InlineData("bans")]
    public async Task ReadsFullHeightInformationPage(string page)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var store = new JsonDataStore(data);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var avatars = new BanAvatarRecognizer(fixture.Engine, await store.LoadOperatorsAsync(), Path.Combine(data, "operator_avatars"));
        var strategy = new GarrisonOcrStrategy();
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", $"info-1610-{page}-1600x1024.png"));
        var (png, w, h) = ScreenshotNormalizer.EnsureAnalysable(bytes, 1600, 1024);
        var read = await BanCovenantReader.ReadAsync(fixture.Engine, strategy, png, w, h,
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToArray(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToArray());
        var enemy = await new EnemyRecognizer(fixture.Engine, strategy, fixture.Bosses).RecognizeAsync(png, w, h);
        var bans = await avatars.RecognizeAsync(png);
        output.WriteLine($"{page}: 核心={read.Core.Count} 附加={read.Additional.Count} 标题={read.SawCoreHeader}/{read.SawAdditionalHeader} 首领={enemy?.BossName} 特训={string.Join('|', enemy?.EnemyTypes ?? [])}");
        output.WriteLine(string.Join('\n', bans.Slots.Select(s => $"{s.Covenant}/{s.SlotIndex}: {s.Name ?? "拒识"} ({s.Candidate}, {s.Score:F3})")));
        Assert.True(WindowGeometry.IsSupportedGameAspectRatio(w, h));
        Assert.Equal("假想敌：管", enemy?.BossName);
        Assert.Equal(new[] { "特训敌人·持续", "特训敌人·隐匿" }, enemy?.EnemyTypes);
        string[] yan = ["夕", "黍", "老鲤", "余", "仇白", "风絮", "乌有", "矩", "拜松"];
        if (page == "top")
        {
            Assert.Equal(new[] { "维多利亚", "谢拉格", "叙拉古", "乌萨斯", "炎", "萨尔贡", "伊比利亚", "莱塔尼亚" }, read.Core.Select(c => c.Name));
            Assert.Equal(new[] { "炎", "萨尔贡", "伊比利亚", "莱塔尼亚" }, read.Core.Where(c => c.IsBanned).Select(c => c.Name));
            Assert.Equal(new[] { "精准", "迅捷", "坚守", "助力", "远见", "不屈", "调和", "协防干员", "聚合", "奥术" }, read.Additional.Select(c => c.Name));
            Assert.Equal(new[] { "聚合", "奥术" }, read.Additional.Where(c => c.IsBanned).Select(c => c.Name));
            Assert.True(read.SawCoreHeader && read.SawAdditionalHeader);
            Assert.Empty(read.UnnamedCore);
            Assert.Empty(read.UnnamedAdditional);
            Assert.Equal(yan, bans.Slots.Select(s => s.Name));
            Assert.False(bans.IsScrolledList);
        }
        else
        {
            Assert.False(read.SawCoreHeader || read.SawAdditionalHeader);
            // 异客这格与现有模板的得分低于阈值，保留拒识，不能因比例适配而放宽阈值。
            Assert.Equal(yan.Concat(new[] { "真言", "佩佩", "鸿雪", "森蚺", null, "卡涅利安", "至简", "泡泡", "莎草", "艾丝黛尔", "引星棘刺", "艾丽妮", "棘刺", "温蒂", "流明", "海沫", "旅骨" }), bans.Slots.Select(s => s.Name));
            Assert.Equal(Enumerable.Repeat("炎", 9).Concat(Enumerable.Repeat("萨尔贡", 10)).Concat(Enumerable.Repeat("伊比利亚", 7)), bans.Slots.Select(s => s.Covenant));
            Assert.True(bans.IsScrolledList);
        }
        Assert.Equal(Enumerable.Range(0, 9), bans.Slots.Where(s => s.Covenant == "炎").Select(s => s.SlotIndex!.Value));
    }
}
