using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class BanAvatarTests(ITestOutputHelper output)
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public async Task CompleteVisibleAvatarsAreCorrectAndAtLeastFifteenAreRecognized()
    {
        using var ocr = new MaaOcrEngine(Data);
        await ocr.WarmUpAsync();
        var operators = await new JsonDataStore(Data).LoadOperatorsAsync();
        var reader = new BanAvatarRecognizer(ocr, operators, Path.Combine(Data, "operator_avatars"));
        var result = await reader.RecognizeAsync(await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "user-ban-4-1920x1080.png")));
        string[] truth = ["真言", "佩佩", "鸿雪", "森蚺", "异客", "至简", "泡泡", "莎草", "艾丝黛尔",
            "锏", "圣聆初雪", "凛御银灰", "灵知", "极光", "耶拉", "初雪", "雪猎", "角峰"];
        output.WriteLine($"定位 {result.LocateMs:F1} ms，OCR {result.OcrMs:F1} ms，比对 {result.CompareMs:F1} ms；{string.Join('、', result.Names)}");
        foreach (var slot in result.Slots)
            output.WriteLine($"{slot.Box} {slot.Name ?? "拒识"}（候选 {slot.Candidate}，分数 {slot.Score:F3}，分差 {slot.Margin:F3}）");
        Assert.All(result.Names, name => Assert.Contains(name, truth));
        Assert.True(result.Names.Length >= 15, $"仅认出 {result.Names.Length} 人");
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
