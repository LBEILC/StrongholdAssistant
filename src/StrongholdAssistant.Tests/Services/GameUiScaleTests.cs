using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.Services;

[Collection("Native OCR")]
public sealed class GameUiScaleTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void IntermediateScaleIsMeasuredFromGeometry()
    {
        const double k = 95.0 / 90;
        using var bitmap = new Bitmap(1920, 1080);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.FromArgb(61, 61, 61)))
        {
            graphics.Clear(Color.Black);
            graphics.FillRectangle(brush, (int)Math.Round(960 - 852 * k), (int)(162 * k),
                (int)(1188 * k), (int)(60 * k));
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        double measured = GameUiScale.Information(stream.ToArray());
        output.WriteLine($"合成标题条 UI95 k={measured:F5}");
        Assert.InRange(measured, k - .001, k + .001);
    }

    [Fact]
    public async Task Ui100InformationReadsAllCovenantsAndEnemyWithoutPartialAvatars()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var store = new JsonDataStore(data);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var avatars = new BanAvatarRecognizer(fixture.Engine, await store.LoadOperatorsAsync(), Path.Combine(data, "operator_avatars"));
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "info-ui100-2000x1125.png"));
        await fixture.Engine.WarmUpAsync();
        var timer = Stopwatch.StartNew();
        double k = GameUiScale.Information(bytes);
        var strategy = new GarrisonOcrStrategy();
        var result = await BanCovenantReader.ReadAsync(fixture.Engine, strategy, bytes, 2000, 1125,
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToArray(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToArray());
        var enemy = await new EnemyRecognizer(fixture.Engine, strategy, fixture.Bosses).RecognizeAsync(bytes, 2000, 1125);
        var bans = await avatars.RecognizeAsync(bytes);
        output.WriteLine($"info-ui100-2000x1125.png k={k:F5} ms={timer.Elapsed.TotalMilliseconds:F1}");
        output.WriteLine($"核心：{string.Join('|', result.Core.Select(c => $"{c.Name}:{c.IsBanned}"))}");
        output.WriteLine($"附加：{string.Join('|', result.Additional.Select(c => $"{c.Name}:{c.IsBanned}"))}");
        output.WriteLine($"敌人：{enemy?.BossName} {string.Join('|', enemy?.EnemyTypes ?? [])}");
        Assert.InRange(k, 1.10, 100.0 / 90);
        Assert.Equal(new[] { "炎", "萨尔贡", "伊比利亚", "莱塔尼亚", "维多利亚", "谢拉格", "叙拉古", "乌萨斯" }, result.Core.Select(c => c.Name));
        Assert.Equal(new[] { "维多利亚", "谢拉格", "叙拉古", "乌萨斯" }, result.Core.Where(c => c.IsBanned).Select(c => c.Name));
        Assert.Equal(new[] { "精准", "迅捷", "坚守", "远见", "不屈", "调和", "协防干员", "奥术", "助力", "聚合" }, result.Additional.Select(c => c.Name));
        Assert.Equal(new[] { "助力", "聚合" }, result.Additional.Where(c => c.IsBanned).Select(c => c.Name));
        Assert.True(result.SawCoreHeader && result.SawAdditionalHeader);
        Assert.Empty(result.UnnamedCore);
        Assert.Empty(result.UnnamedAdditional);
        Assert.Equal("阿利斯泰尔，帝国余晖", enemy?.BossName);
        Assert.Equal(new[] { "特训敌人·隐匿", "特训敌人·持续" }, enemy?.EnemyTypes);
        Assert.Empty(bans.Slots);
    }

    [Fact]
    public async Task Ui100BanListLocatesNineteenCompleteSlotsAndTheirGroupsAndTiers()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var store = new JsonDataStore(data);
        var reader = new BanAvatarRecognizer(fixture.Engine, await store.LoadOperatorsAsync(), Path.Combine(data, "operator_avatars"));
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "ban-ui100-2000x1125.png"));
        await fixture.Engine.WarmUpAsync();
        var timer = Stopwatch.StartNew();
        var result = await reader.RecognizeAsync(bytes);
        double ms = timer.Elapsed.TotalMilliseconds;
        double k = GameUiScale.Information(bytes);
        output.WriteLine($"ban-ui100-2000x1125.png k={k:F5} ms={ms:F1}");
        using var bitmap = new Bitmap(new MemoryStream(bytes));
        var readTier = typeof(BanAvatarRecognizer).GetMethod("ReadTier", BindingFlags.NonPublic | BindingFlags.Static)!;
        var slots = result.Slots.OrderBy(s => Math.Round(s.Box.Y / (10.0 * bitmap.Height / 1080))).ThenBy(s => s.Box.X).ToArray();
        var tiers = slots.Select(s => (int)readTier.Invoke(null, [bitmap, s.Box, bitmap.Height / 1080.0 * k])!).ToArray();
        for (int i = 0; i < result.Slots.Count; i++)
        {
            var s = slots[i];
            output.WriteLine($"{i + 1} {s.Covenant} {tiers[i]}阶 {s.Name ?? "拒识"} candidate={s.Candidate} score={s.Score:F3} margin={s.Margin:F3} box={s.Box}");
        }
        Assert.Equal(19, result.Slots.Count);
        Assert.Equal(new[] { "酒神", "维娜·维多利亚", "涤火杰西卡", "白铁", "水灯心", "凯瑟琳", "Miss.Christine", "哈洛德", "慕斯",
            "锏", "圣聆初雪", "凛御银灰", "灵知", "极光", "耶拉", "初雪", "雪猎", "哈洛德", "角峰" }, slots.Select(s => s.Name));
        Assert.Equal(Enumerable.Repeat("维多利亚", 9).Concat(Enumerable.Repeat("谢拉格", 10)), slots.Select(s => s.Covenant));
        Assert.Equal(new[] { 6, 5, 4, 4, 3, 3, 2, 2, 1, 6, 5, 5, 4, 3, 3, 3, 2, 2, 1 }, tiers);
    }
}
