using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class AdaptAdviceTests
{
    [Fact]
    public async Task AdvicePreservesSlotsAndExpiresOnNextConfirmedRoundOrNewGame()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            var state = api.GetMatchState() with { ModeId = MatchFact<string?>.Of("mode_multi_hard") };
            AdaptChoice card = new("chess_item_5_05_e_a", "天马之盔", "生命上限+60%", 5);
            var advice = api.BuildAdaptAdvice(state, new("shop", [card, null, card, null, null, null]), 9);
            Assert.Equal("按单人规程等级 9～14 平均估", advice.Basis);
            Assert.Equal(new int[] {0,1,2,3,4,5}, advice.Cards.Select(c => c.Index));
            Assert.Null(advice.Cards[1].Rank);
            Assert.Equal(card.Id, advice.Cards[2].Id);
            using var model = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "data", "decision", "adapt.json")));
            if (model.RootElement.TryGetProperty("accepted", out var accepted) && accepted.GetBoolean())
            {
                Assert.Equal("ok", advice.Status);
                Assert.Equal(1, advice.Cards[0].Rank);
            }
            else
            {
                Assert.Equal("unavailable", advice.Status);
                Assert.Equal("推荐还在调", advice.Reason);
                Assert.All(advice.Cards, c => { Assert.Null(c.Rank); Assert.Null(c.ClearRate); Assert.Null(c.ReachedRound); });
            }
            var pushed = new List<AdaptAdvice?>();
            api.Changed += (name, value) => { if (name == "match") pushed.Add(((MatchInfo)value).AdaptAdvice); };
            api.PublishAdaptAdvice(advice);
            Assert.Equal(advice, Assert.Single(pushed));
            void Rest(int round) => api.ObserveEconomy(new(round, 8, 3, null, true));
            Rest(9); Rest(9);
            Assert.Equal(advice, api.Match.AdaptAdvice);
            Rest(10); Rest(10);
            Assert.Null(api.Match.AdaptAdvice);
            api.PublishAdaptAdvice(advice);
            await api.ResetMatchAsync();
            Assert.Null(api.Match.AdaptAdvice);
        }
        finally { Directory.Delete(directory, true); }
    }
}
