using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests.Services;

[Collection("Native OCR")]
public class DiagnosticInformationTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
{
    // 玩家图只从环境变量指定的绝对路径读取，不随测试发布。
    public sealed class DiagnosticTheoryAttribute : TheoryAttribute
    {
        public DiagnosticTheoryAttribute()
        {
            var directory = Environment.GetEnvironmentVariable("STRONGHOLD_INFO_293");
            if (directory is null || !Path.IsPathFullyQualified(directory) || !Directory.Exists(directory)
                || new[] { "052655423", "052717398", "054100828" }.Any(id =>
                    !Directory.EnumerateFiles(directory, $"20261117-{id}-*.png").Any()))
                Skip = "未提供 293 诊断样图绝对目录（STRONGHOLD_INFO_293）";
        }
    }

    [DiagnosticTheory]
    [InlineData("052655423", false)]
    [InlineData("052717398", false)]
    [InlineData("054100828", true)]
    public async Task ReadsEveryVisibleCovenantAndEnemy(string id, bool smallPool)
    {
        var path = Directory.GetFiles(Environment.GetEnvironmentVariable("STRONGHOLD_INFO_293")!, $"20261117-{id}-*.png").Single();
        var png = File.ReadAllBytes(path);
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var store = new JsonDataStore(data);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var strategy = new GarrisonOcrStrategy();
        var read = await BanCovenantReader.ReadAsync(fixture.Engine, strategy, png, 1920, 1200,
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToArray(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToArray());
        var enemy = await new EnemyRecognizer(fixture.Engine, strategy, fixture.Bosses).RecognizeAsync(png, 1920, 1200);
        output.WriteLine($"{id}: 核心={read.Core.Count}/{read.CoreSlots} 附加={read.Additional.Count}/{read.AdditionalSlots} 首领={enemy?.BossName}");
        output.WriteLine(string.Join('|', read.Core.Concat(read.Additional).Select(c => $"{c.Name}:{c.IsBanned}")));
        // 真值来自逐项看图，见 docs/research/truth/293-information.md。
        string[] coreEnabled = smallPool ? ["叙拉古", "莱塔尼亚", "谢拉格", "炎"] : ["炎", "谢拉格", "莱塔尼亚", "乌萨斯", "叙拉古"];
        string[] coreBanned = smallPool ? ["乌萨斯", "维多利亚", "伊比利亚", "萨尔贡"] : ["阿戈尔", "萨尔贡", "卡西米尔", "伊比利亚", "维多利亚"];
        string[] additionalEnabled = smallPool ? ["精准", "助力", "远见", "不屈", "调和", "协防干员", "聚合", "奥术"] : ["精准", "迅捷", "坚守", "奇迹", "投资人", "不屈", "调和", "协防干员", "独行", "绝技", "奥术"];
        string[] additionalBanned = smallPool ? ["迅捷", "坚守"] : ["灵巧", "助力", "远见", "突袭", "聚合"];
        Assert.Equal(coreEnabled.Order(), read.Core.Where(c => !c.IsBanned).Select(c => c.Name).Order());
        Assert.Equal(coreBanned.Order(), read.Core.Where(c => c.IsBanned).Select(c => c.Name).Order());
        Assert.Equal(additionalEnabled.Order(), read.Additional.Where(c => !c.IsBanned).Select(c => c.Name).Order());
        Assert.Equal(additionalBanned.Order(), read.Additional.Where(c => c.IsBanned).Select(c => c.Name).Order());
        Assert.Equal(read.Core.Count, read.CoreSlots);
        Assert.Equal(read.Additional.Count, read.AdditionalSlots);
        Assert.Empty(read.UnnamedCore);
        Assert.Empty(read.UnnamedAdditional);
        Assert.True(read.SawCoreHeader && read.SawAdditionalHeader);
        Assert.Equal(smallPool ? "假想敌：鳍" : "矩兽", enemy?.BossName);
        Assert.Equal(new[] { smallPool ? "特训敌人·致命" : "特训敌人·元素", "特训敌人·复苏" }, enemy?.EnemyTypes);
        Assert.True(await new InformationScreenDetector(fixture.Engine, strategy).IsSoloOpeningAsync(png, 1920, 1200));
    }
}
