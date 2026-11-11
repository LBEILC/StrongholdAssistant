using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;
using Xunit.Abstractions;
using System.Diagnostics;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class BanAvatarTests(ITestOutputHelper output)
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public async Task RemainingSamplesKeepRejectingIncompleteAvatars()
    {
        using var ocr = new MaaOcrEngine(Data);
        await ocr.WarmUpAsync();
        var operators = await new JsonDataStore(Data).LoadOperatorsAsync();
        var reader = new BanAvatarRecognizer(ocr, operators, Path.Combine(Data, "operator_avatars"));
        var samples = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.png")
            .Where(p => Path.GetFileName(p).StartsWith("ban-sample-") || Path.GetFileName(p).StartsWith("user-ban-"))
            .Where(p => Path.GetFileName(p) != "user-ban-4-1920x1080.png").Order().ToArray();
        Assert.Equal(6, samples.Length);
        foreach (var path in samples)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var result = await reader.RecognizeAsync(bytes);
            output.WriteLine($"{Path.GetFileName(path)} k={GameUiScale.Information(bytes):F5}");
            output.WriteLine($"{Path.GetFileName(path)}：定位 {result.LocateMs:F1} ms，OCR {result.OcrMs:F1} ms，比对 {result.CompareMs:F1} ms；识别 {result.Names.Length} 人");
            Assert.Empty(result.Names);
        }
    }

    [Fact]
    public async Task CompleteVisibleAvatarsRecognizeAllEighteenWithinOneSecond()
    {
        using var ocr = new MaaOcrEngine(Data);
        await ocr.WarmUpAsync();
        var operators = await new JsonDataStore(Data).LoadOperatorsAsync();
        var timer = Stopwatch.StartNew();
        var reader = new BanAvatarRecognizer(ocr, operators, Path.Combine(Data, "operator_avatars"));
        output.WriteLine($"模板加载 {timer.Elapsed.TotalMilliseconds:F1} ms");
        var screenshot = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "user-ban-4-1920x1080.png"));
        timer.Restart();
        var result = await reader.RecognizeAsync(screenshot);
        double elapsedMs = timer.Elapsed.TotalMilliseconds;
        output.WriteLine($"user-ban-4-1920x1080.png k={GameUiScale.Information(screenshot):F5} ms={elapsedMs:F1}");
        string[] truth = ["真言", "佩佩", "鸿雪", "森蚺", "异客", "至简", "泡泡", "莎草", "艾丝黛尔",
            "锏", "圣聆初雪", "凛御银灰", "灵知", "极光", "耶拉", "初雪", "雪猎", "角峰"];
        output.WriteLine($"定位 {result.LocateMs:F1} ms，OCR {result.OcrMs:F1} ms，比对 {result.CompareMs:F1} ms；{string.Join('、', result.Names)}");
        foreach (var slot in result.Slots)
            output.WriteLine($"{slot.Box} {slot.Name ?? "拒识"}（候选 {slot.Candidate}，分数 {slot.Score:F3}，分差 {slot.Margin:F3}）");
        Assert.Equal(truth, result.Slots.Select(s => s.Name));
        // 016 修改前的 18 格基线；单一候选沿用识别器以得分作为分差的定义。
        var margins = result.Slots.Select(s => s.Margin).Order().ToArray();
        Assert.True(margins[0] >= .1013398550586983);
        Assert.True((margins[8] + margins[9]) / 2 >= .3466745831556446);
        Assert.InRange(elapsedMs, 0, 1000);
    }

    [Fact]
    public async Task RescansAccumulatePreserveManualOverridesAndResetOnNewMatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Data, directory);
            var first = api.Match with { BossId = "enemy_1521_dslily" };
            Assert.Equal(2, api.ApplyRecognizedMatch(first, ["真言", "泡泡"]));
            api.SetOperatorBanned("真言", false);
            api.SetOperatorBanned("佩佩", true);
            Assert.Equal(1, api.ApplyRecognizedMatch(first with { Covenants = first.Covenants.Reverse().ToArray() }, ["真言", "莎草"]));
            Assert.Equal(new[] { "泡泡", "佩佩", "莎草" }.Order(), api.Match.BannedOperators.Order());
            Assert.Equal(0, api.ApplyRecognizedMatch(first, ["真言", "泡泡", "莎草"]));

            // 修正盟约只改变规则项，头像累计与手动解除仍保留。
            api.SetCovenantIncomplete("sargon", true);
            Assert.Contains("莎草", api.Match.BannedOperators);
            Assert.DoesNotContain("真言", api.Match.BannedOperators);
            api.SetCovenantIncomplete("sargon", false);
            Assert.Equal(new[] { "泡泡", "佩佩", "莎草" }.Order(), api.Match.BannedOperators.Order());

            api.ApplyRecognizedMatch(first with { BossId = "enemy_9029_acfnc" }, ["真言"]);
            Assert.Equal(new[] { "真言" }, api.Match.BannedOperators);
            api.SetOperatorBanned("泡泡", true);
            var differentCovenants = api.Match with { Covenants = api.Match.Covenants.Where(c => c.Id != "sargon").ToArray() };
            api.ApplyRecognizedMatch(differentCovenants, ["锏"]);
            Assert.Equal(new[] { "锏" }, api.Match.BannedOperators);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
