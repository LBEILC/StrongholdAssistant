using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests.Services;

[Collection("Native OCR")]
public class CursorInformationTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
{
    // 玩家图只读环境变量给定的绝对目录，不随仓库发布。
    public sealed class CursorTheoryAttribute : TheoryAttribute
    {
        public CursorTheoryAttribute()
        {
            var root = Environment.GetEnvironmentVariable("STRONGHOLD_INFO_310");
            if (root is null || !Path.IsPathFullyQualified(root) || !Directory.Exists(root)
                || new[] { "054211609", "070238374", "070557339", "074612318", "113202658", "071013353",
                    "062155205", "062310307", "062323839" }.Any(id => !Directory.EnumerateFiles(root, $"*-{id}-*.png", SearchOption.AllDirectories).Any()))
                Skip = "未提供 310/312 诊断图绝对父目录（STRONGHOLD_INFO_310）";
        }
    }

    [CursorTheory]
    [InlineData("054211609", "炎,维多利亚,叙拉古", "灵巧,坚守,助力,不屈,聚合", "矩兽", "元素,复苏,频次", true)]
    [InlineData("070238374", "炎,维多利亚,卡西米尔,乌萨斯,伊比利亚", "迅捷,灵巧,坚守,奇迹,突袭", "", "", true)]
    [InlineData("074612318", "炎,萨尔贡,阿戈尔,卡西米尔,乌萨斯", "精准,灵巧,助力,远见,聚合", "假想敌：铳", "致命,持续,隐匿", true)]
    [InlineData("070557339", "炎,维多利亚,卡西米尔,乌萨斯,伊比利亚", "迅捷,灵巧,坚守,奇迹,突袭", "假想敌：冑", "飞行,频次,折射", false)]
    [InlineData("113202658", "维多利亚,谢拉格,阿戈尔,乌萨斯,莱塔尼亚", "精准,助力,远见,聚合,奥术", "假想敌：茧", "持续,元素", false)]
    [InlineData("071013353", "维多利亚,谢拉格,阿戈尔,叙拉古,卡西米尔", "迅捷,灵巧,坚守,突袭,聚合", "假想敌：鳍", "飞行,元素,持续", false)]
    [InlineData("062155205", "炎,谢拉格,阿戈尔,叙拉古,卡西米尔", "迅捷,灵巧,助力,不屈,奥术", "“萨米的意志”", "折射,隐匿", false)]
    [InlineData("062310307", "炎,谢拉格,阿戈尔,叙拉古,卡西米尔", "迅捷,灵巧,助力,不屈,奥术", "“萨米的意志”", "折射,隐匿", false)]
    [InlineData("062323839", "炎,谢拉格,阿戈尔,叙拉古,卡西米尔", "迅捷,灵巧,助力,不屈,奥术", "“萨米的意志”", "折射,隐匿", false)]
    public async Task ReadsCursorOcclusionWithoutInventingHiddenInformation(string id, string coreBanned,
        string additionalBanned, string boss, string types, bool panelOccluded)
    {
        var path = Directory.EnumerateFiles(Environment.GetEnvironmentVariable("STRONGHOLD_INFO_310")!,
            $"*-{id}-*.png", SearchOption.AllDirectories).Single();
        var png = File.ReadAllBytes(path);
        using var pixels = BanScreenPixels.Load(png)!;
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var strategy = new GarrisonOcrStrategy();
        var read = await BanCovenantReader.ReadAsync(fixture.Engine, strategy, png, pixels.Width, pixels.Height,
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToArray(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToArray());
        var enemy = await new EnemyRecognizer(fixture.Engine, strategy, fixture.Bosses)
            .RecognizeAsync(png, pixels.Width, pixels.Height);
        output.WriteLine($"{id}: 核心={read.Core.Count}/{read.CoreSlots} 附加={read.Additional.Count}/{read.AdditionalSlots} 未命名={read.UnnamedCore.Count}/{read.UnnamedAdditional.Count} 标题={read.SawCoreHeader}/{read.SawAdditionalHeader} 首领={enemy?.BossName} 特训={string.Join(',', enemy?.EnemyTypes ?? [])}");
        output.WriteLine(string.Join('|', read.Core.Concat(read.Additional).Select(c => $"{c.Name}:{c.IsBanned}")));
        if (panelOccluded)
        {
            Assert.True(read.Core.Count < 10 || read.Additional.Count < 16 || enemy is null);
            // 只断言可见且已命名的禁用项，不用另一张图替遮挡帧补答案。
            if (coreBanned.Length > 0)
                Assert.All(read.Core.Where(c => c.IsBanned), c => Assert.Contains(c.Name, coreBanned.Split(',')));
            Assert.All(read.Additional.Where(c => c.IsBanned), c => Assert.Contains(c.Name, additionalBanned.Split(',')));
            return;
        }
        Assert.Equal(10, read.Core.Count);
        Assert.Equal(16, read.Additional.Count);
        Assert.Equal(10, read.Core.Select(c => c.Name).Distinct().Count());
        Assert.Equal(16, read.Additional.Select(c => c.Name).Distinct().Count());
        Assert.Equal(10, read.CoreSlots);
        Assert.Equal(16, read.AdditionalSlots);
        Assert.Empty(read.UnnamedCore);
        Assert.Empty(read.UnnamedAdditional);
        Assert.True(read.SawCoreHeader && read.SawAdditionalHeader);
        Assert.Equal(coreBanned.Split(',').Order(), read.Core.Where(c => c.IsBanned).Select(c => c.Name).Order());
        Assert.Equal(additionalBanned.Split(',').Order(), read.Additional.Where(c => c.IsBanned).Select(c => c.Name).Order());
        Assert.Equal(boss, enemy?.BossName);
        Assert.Equal(types.Split(',').Select(t => $"特训敌人·{t}"), enemy?.EnemyTypes);
    }
}
