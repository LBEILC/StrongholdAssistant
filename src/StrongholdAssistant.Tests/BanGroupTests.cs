using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class BanGroupTests(ITestOutputHelper output)
{
    [Fact]
    public async Task CompleteSamplesHaveGameOrderAndSlotsMergeInferConfirmRejectAndReset()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        using var ocr = new MaaOcrEngine(data);
        await ocr.WarmUpAsync();
        var operators = await new JsonDataStore(data).LoadOperatorsAsync();
        var reader = new BanAvatarRecognizer(ocr, operators, Path.Combine(data, "operator_avatars"));
        BanAvatarResult userFour = null!;
        int audited = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestData"), "*.png")
                     .Where(p => Path.GetFileName(p).StartsWith("ban-sample-")
                         || Path.GetFileName(p).StartsWith("user-ban-") || Path.GetFileName(p).StartsWith("ban-ui100-")).Order())
        {
            var result = await reader.RecognizeAsync(await File.ReadAllBytesAsync(path));
            var complete = result.Slots.GroupBy(s => s.Covenant)
                .Where(g => g.Key != null && g.First().SlotIndex == 0 && g.Last().EndsGroup).ToArray();
            if (complete.Length == 0) output.WriteLine($"{Path.GetFileName(path)}：没有标题、完整格位和结尾同时可见的组。");
            foreach (var group in complete)
            {
                var slots = group.ToArray();
                Assert.Equal(Enumerable.Range(0, slots.Length), slots.Select(s => s.SlotIndex!.Value));
                Assert.All(slots, s => Assert.NotNull(s.Name));
                Assert.Equal(slots.Select(s => s.Tier).OrderDescending(), slots.Select(s => s.Tier));
                var names = slots.Select(s => s.Name).ToHashSet();
                Assert.Equal(operators.Where(o => names.Contains(o.Name)).OrderByDescending(o => o.Tier).Select(o => o.Name),
                    slots.Select(s => s.Name));
                output.WriteLine($"{Path.GetFileName(path)} / {group.Key}：{slots.Length} 格，星级降序、同星级数据顺序均符合；{string.Join('、', slots.Select(s => $"{s.Name}({s.Tier})"))}");
                audited++;
            }
            if (Path.GetFileName(path) == "user-ban-4-1920x1080.png") userFour = result;
        }
        Assert.Equal(4, audited);
        try
        {
            var api = await HostApi.CreateAsync(data, directory);
            var match = api.Match with { BossId = "enemy_1521_dslily" };
            api.ApplyRecognizedMatch(match, [], userFour);
            Assert.Equal(new[] { "sargon", "kjerag" }, api.Match.BanGroups.Select(g => g.Covenant));
            Assert.All(api.Match.BanGroups, g => { Assert.True(g.Complete); Assert.Equal(9, g.Slots.Length); });
            // 第三组只有顶部残片，不能作为完整格位，也不能误报 complete。
            Assert.DoesNotContain(api.Match.BanGroups, g => g.Covenant != "sargon" && g.Covenant != "kjerag" && g.Complete);
            output.WriteLine("实际 MatchInfo JSON：" + JsonSerializer.Serialize(api.Match, HostApi.JsonOptions));

            await api.ResetMatchAsync();
            Assert.Empty(api.Match.BanGroups);
            var missing = userFour with { Slots = userFour.Slots.Select(s => s.Name is "至简" or "佩佩" ? s with { Name = null } : s).ToArray() };
            api.ApplyRecognizedMatch(match, [], missing);
            var sargon = api.Match.BanGroups.Single(g => g.Covenant == "sargon");
            Assert.Null(sargon.Slots[5].Name);
            Assert.Contains("至简", sargon.Slots[5].Candidates);
            Assert.Equal("佩佩", sargon.Slots[1].Name);
            Assert.True(sargon.Slots[1].Inferred);
            Assert.Contains("佩佩", api.Match.BannedOperators);
            api.SetOperatorBanned("佩佩", true);
            Assert.Null(api.Match.BanGroups[0].Slots[1].Inferred);
            api.SetOperatorBanned("佩佩", false);
            Assert.Null(api.Match.BanGroups[0].Slots[1].Name);
            Assert.DoesNotContain("佩佩", api.Match.BanGroups[0].Slots[1].Candidates);
            Assert.DoesNotContain("佩佩", api.Match.BannedOperators);

            var withoutTitle = userFour with { Slots = userFour.Slots.Where(s => s.Covenant == "萨尔贡")
                .Select(s => s with { Covenant = null, SlotIndex = null }).ToArray() };
            Assert.True(withoutTitle.IsScrolledList);
            api.ApplyRecognizedMatch(match, [], withoutTitle);
            Assert.Equal("至简", api.Match.BanGroups[0].Slots[5].Name);
            Assert.Null(api.Match.BanGroups[0].Slots[5].Inferred);
            api.ApplyRecognizedMatch(match with { BossId = "another-boss" }, []);
            Assert.Empty(api.Match.BanGroups);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
