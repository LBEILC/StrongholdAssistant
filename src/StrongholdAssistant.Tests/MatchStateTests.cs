using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StrongholdAssistant.Tests;

public class MatchStateTests
{
    [Fact]
    public async Task HostSnapshotRoundTripsBothEdgesOfShopWithoutChangingRecognition()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            api.ApplyCustomPicks([], ["凯尔希"]);
            await api.ObserveFieldAsync([], DateTimeOffset.UtcNow, new EconomyReading(11, 0, 4, null, true), true, true,
                [new() { SlotIndex = 0, Name = "雪祀之衣" }, new() { SlotIndex = 3, Name = "凯尔希" },
                 new() { SlotIndex = 5, Name = "杰克" }]);
            var json = JsonSerializer.Serialize(api.GetMatchState(), HostApi.JsonOptions);
            var copy = JsonSerializer.Deserialize<MatchState>(json, HostApi.JsonOptions)!;
            Assert.Equal("equipment", copy.Shop[0].Kind.Value);
            Assert.Equal("chess_item_1_07_e_a", copy.Shop[0].Card.Value!.Id.Value);
            Assert.Equal("杰克", copy.Shop[5].Card.Value!.Name.Value);
            Assert.Equal(6, copy.Shop[3].Tier.Value);
            Assert.Equal(copy.CustomPicks.Value![6][0].Id, copy.Shop[3].Card.Value!.Id);
            Assert.Null(copy.Shop[1].Bought.Value);
            Assert.Equal(0, copy.Money.Value);
            Assert.Null(copy.Units.Value);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task StrategyRecommendationRanksCandidatesAndRequiresMatchingVersion()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            var state = new MatchState {
                GameDataVersion = api.GetMatchState().GameDataVersion, ModeId = MatchFact<string?>.Of("mode_single_funny"),
                MapId = MatchFact<string?>.Of("m04"), BossId = MatchFact<string?>.Of("enemy_9035_acspider"),
                Covenants = MatchFact<MatchCovenant[]?>.Of([
                    new() { Id = MatchFact<string?>.Of("yan"), Incomplete = MatchFact<bool?>.Of(true) }])
            };
            string[] ids = ["band_yan", "band_sarkazb", "band_ioleta"];
            var result = api.RecommendOpeningStrategies(state, ids);
            Assert.Equal(ids.Order(), result.Candidates.Select(c => c.StrategyId).Order());
            Assert.Equal(new[] { 1, 2, 3 }, result.Candidates.Select(c => c.Rank));
            Assert.Equal(result.Candidates.OrderByDescending(c => c.ClearProbability).ThenByDescending(c => c.ExpectedRound), result.Candidates);
            Assert.All(result.Candidates, c => {
                Assert.InRange(c.ClearProbability, 0.0, 1.0);
                Assert.InRange(c.ExpectedRound, 1.0, 9.0);
            });
            // 同一开局由训练库直接计算的参考值，覆盖 JSON 导出和宿主求值。
            var yan = Assert.Single(result.Candidates, c => c.StrategyId == "band_yan");
            Assert.Equal(0.934319235049708, yan.ClearProbability, 9);
            Assert.Equal(8.93052584830345, yan.ExpectedRound, 9);
            var multi = api.RecommendOpeningStrategies(state with { ModeId = MatchFact<string?>.Of("mode_multi_hard") }, ids);
            Assert.Equal(3, multi.Candidates.Length);
            var multiYan = Assert.Single(multi.Candidates, c => c.StrategyId == "band_yan");
            Assert.Equal(0.852837785772377, multiYan.ClearProbability, 9);
            Assert.Equal(14.3513891798496, multiYan.ExpectedRound, 9);
            Assert.Contains(multi.Assumptions, a => a.Contains("真人队友"));
            Assert.Empty(api.RecommendOpeningStrategies(state with { GameDataVersion = MatchFact<string?>.Of("other") }, ids).Candidates);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RoundTripPreservesUnknownConfirmedAndZero()
    {
        var state = new MatchState { Round = MatchFact<int?>.Of(3), Money = MatchFact<int?>.Of(0),
            Units = MatchFact<MatchUnit[]?>.Of([new MatchUnit { Operator = MatchFact<MatchEntity?>.Of(
                new() { Id = MatchFact<string?>.Of("char_140_whitew", "confirmed") }, "confirmed") }]) };
        MatchStep[] steps = [new(state, new() { Upgraded = MatchFact<bool?>.Of(false, "inferred") }, null)];
        var json = JsonSerializer.Serialize(steps, HostApi.JsonOptions);
        var copy = JsonSerializer.Deserialize<MatchStep[]>(json, HostApi.JsonOptions)!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonSerializer.SerializeToNode(copy, HostApi.JsonOptions)));
        Assert.Null(copy[0].State.Hp.Value);
        Assert.Equal("confirmed", copy[0].State.Units.Value![0].Operator.Source);
        Assert.Equal(6, copy[0].State.Shop.Length);
    }
}
