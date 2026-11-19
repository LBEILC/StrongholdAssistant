using System.IO.Compression;
using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Tests.Services;
using OpenCvSharp;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

[Collection("Native OCR")]
public sealed class CollectionRoundTests(EnemyRecognitionFixture fixture)
{
    [Theory]
    [InlineData("zero", 0)]
    [InlineData("one", 1)]
    public async Task CollapsedShopReadsTheGoldBadge(string sample, int money)
    {
        var reader = new CollectionHudReader(fixture.Engine);
        var frame = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", $"collection-money-{sample}.png"));
        var result = await reader.ReadVitals(frame, new(3, null, null, null, true), GameShopTheme.Terminal);
        Assert.Equal(new RoundVitals(29, money, null), result);
    }

    [Fact]
    public async Task SettlementReadsVictoryAndSummaryFromDifferentFrames()
    {
        var reader = new CollectionHudReader(fixture.Engine);
        byte[] Frame(string kind) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
            "TestData", $"collection-{kind}.png"));
        Assert.Equal(new CollectedResult(null, true, 29, null), await reader.ReadResult(Frame("victory")));
        Assert.Equal(new CollectedResult(15, null, null, 5), await reader.ReadResult(Frame("summary")));
    }

    [Fact]
    public async Task ExportUsesAdjacentRoundsAndPlayerConfirmedUnits()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var store = new CollectionStore(directory.FullName);
            var at = DateTimeOffset.UtcNow;
            using var picture = new Mat(800, 1280, MatType.CV_8UC3, Scalar.Black);
            var frame = picture.ImEncode(".png");
            var match = new MatchInfo("test", null, [], [], null, []);
            var units = new[] { new FieldUnit("bench", 4, 0, 2, "杰克", "sprite") };
            var first = new Dictionary<int, string> { [2] = "褐果", [4] = "杰克" };
            var left = new Dictionary<int, string> { [2] = "褐果" };
            var state = new FieldState("map", 1, at.ToString("O"), units, []);
            store.Enable(true);
            store.Add("one", at.ToString("O"), null, at, new { }, batch =>
            {
                void Rest(int round, int? hp, int money, int level, Dictionary<int, string> cards)
                {
                    var observation = new FieldObservation(at, true, "map", round, new Dictionary<FieldCell, int>(), cards);
                    batch.RestFrame(round, new(hp, money, level), match, observation, cards, units, frame);
                }
                Rest(1, 28, 7, 1, first);
                batch.Panel(1, frame, [new(null, "敌人")]);
                Rest(1, 28, 5, 1, left);
                batch.ConfirmUnits(null, state, true);
                Rest(1, 28, 0, 2, left);
                Rest(2, 25, 5, 2, first);
                Rest(4, null, 6, 2, first);
                batch.Settlement(new(3, false, 0, null));
            });
            string package = Path.Combine(directory.FullName, "match.zip");
            await store.ExportAsync("one", package);
            using var zip = ZipFile.OpenRead(package);
            using var rounds = new StreamReader(zip.GetEntry("rounds.jsonl")!.Open());
            var rows = (await rounds.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => JsonSerializer.Deserialize<JsonElement>(s)).ToArray();
            Assert.Equal(new[] { 1, 2, 4 }, rows.Select(r => r.GetProperty("round").GetInt32()));
            Assert.Equal(-3, rows[0].GetProperty("hpChange").GetInt32());
            Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("hpChange").ValueKind);
            Assert.Equal("杰克", rows[0].GetProperty("purchases")[0].GetString());
            Assert.True(rows[0].GetProperty("upgraded").GetBoolean());
            Assert.True(rows[0].GetProperty("units")[0].GetProperty("confirmed").GetBoolean());
            foreach (string field in new[] { "restStart", "restEnd", "enemyPanel" })
            {
                using var stream = zip.GetEntry(rows[0].GetProperty(field).GetString()!)!.Open();
                using var bitmap = new System.Drawing.Bitmap(stream);
                Assert.Equal(1280, bitmap.Width);
                Assert.Equal(800, bitmap.Height);
            }
            Assert.Null(rows[1].GetProperty("enemyPanel").GetString());
            using var result = JsonDocument.Parse(zip.GetEntry("result.json")!.Open());
            Assert.Equal(4, result.RootElement.GetProperty("reachedRound").GetInt32());
            Assert.False(result.RootElement.GetProperty("cleared").GetBoolean());
        }
        finally { directory.Delete(true); }
    }
}
