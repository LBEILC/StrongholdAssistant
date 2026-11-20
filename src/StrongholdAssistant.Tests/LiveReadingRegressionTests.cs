using System.Drawing;
using System.Text.Json;
using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using StrongholdAssistant.Host;
using Xunit.Abstractions;

namespace StrongholdAssistant.Tests;

[Collection("Shop OCR")]
public sealed class LiveReadingRegressionTests(EnemyRecognitionFixture fixture, ShopReaderFixture shop, ITestOutputHelper output)
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestData", "live398");
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");

    private static (string? Name, int? Number) ParseLabel(string text)
    {
        var match = Regex.Match(text, @"^(.*?)(\d*)$");
        string name = match.Groups[1].Value;
        return (name is "-" or "?" ? null : name, int.TryParse(match.Groups[2].Value, out int n) ? n : null);
    }

    [Fact]
    public async Task ReadsVisibleFacts()
    {
        var ids = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Data, "covenant_ids.json")))!;
        var names = ids.ToDictionary(p => p.Value, p => p.Key);
        var reader = new CovenantBarReader(fixture.Engine, Data, names);
        var truth = File.ReadAllLines(Path.Combine(Root, "truth.tsv")).Skip(1).Select(line => line.Split('\t')).ToArray();
        Assert.Equal(truth.Select(row => row[0] + ".jpg").Order(), Directory.GetFiles(Root, "*.jpg").Select(Path.GetFileName).Order());
        var errors = new List<string>();
        foreach (var row in truth)
        {
            string id = row[0], file = Path.Combine(Root, id + ".jpg");
            var bytes = File.ReadAllBytes(file);
            using var bitmap = new Bitmap(file);
            double scale = GameUiScale.Shop(bitmap);
            var economy = await new EconomyReader(fixture.Engine, GameShopTheme.Closure).ReadAsync(bytes, bitmap.Width, bitmap.Height, scale);
            var vitals = await new CollectionHudReader(fixture.Engine).ReadVitals(bytes, economy, GameShopTheme.Closure);
            var shopReader = new ShopReader(fixture.Engine, shop.Names) { Theme = GameShopTheme.Closure, ReadPrices = true };
            var cards = await shopReader.ReadAsync(bytes, bitmap.Width, bitmap.Height, shop.Names);
            var covenants = await reader.ReadAsync(bytes, scale);
            void Check(string field, object? expected, object? actual)
            {
                if (actual != null && !Equals(expected, actual)) errors.Add($"{id} {field}：应为 {expected ?? "未知"}，读到 {actual}");
            }
            int?[] actual = [economy.Round, vitals.Hp, economy.Money, economy.Level, economy.UpgradeCost, economy.RefreshCost];
            string[] labels = ["回合", "血量", "资金", "商店等级", "升级费", "刷新费"];
            for (int i = 0; i < actual.Length; i++)
                Check(labels[i], int.TryParse(row[2 + i], out int n) ? n : null, actual[i]);
            var expectedCards = row[8].Split(' ').Select(ParseLabel).ToArray();
            foreach (var card in cards)
            {
                var expected = expectedCards[card.SlotIndex];
                Check($"商品{card.SlotIndex + 1}", expected.Name, card.Name);
                Check($"价格{card.SlotIndex + 1}", expected.Number, card.Price > 0 ? card.Price : null);
            }
            var expectedBar = row[9].Split(' ').Where(s => s != "-").Select(ParseLabel).ToDictionary(p => p.Name!, p => p.Number);
            foreach (var item in covenants.Items)
            {
                string name = names[item.Id];
                if (!expectedBar.TryGetValue(name, out int? number)) errors.Add($"{id} 多读盟约 {name}");
                Check(name, number, item.Layers);
            }

            // 主要修正必须实际读出；遮挡、无卡、没有层数或局部残缺才允许未知。
            if (row[5] != "-" && economy.Level == null) errors.Add($"{id} 可见升级卡未读到等级");
            if (row[6] != "-" && economy.UpgradeCost == null) errors.Add($"{id} 可见升级卡未读到费用");
            if (id == "02" && covenants.Items.Count(c => c.Layers.HasValue) != 3) errors.Add("02 灵巧、助力和卡西米尔未读全");
            output.WriteLine(JsonSerializer.Serialize(new { frame = id, economy.Round, vitals.Hp, economy.Money, economy.Level, economy.UpgradeCost, economy.RefreshCost,
                cards = cards.Select(c => new { c.SlotIndex, c.Name, c.Price }), covenants = covenants.Items.Select(c => new { name = names[c.Id], c.Layers }) }));
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
