using System.Drawing;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class NewMatchTests
{
    private static Task<HostApi> CreateApi() => HostApi.CreateAsync(
        Path.Combine(AppContext.BaseDirectory, "data"), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

    private static BanAvatarResult Grid(int index, string name, double score = .9) => new(
        [new(new Rectangle(0, 200, 100, 100), name, "萨尔贡", name, score, .2)
            { SlotIndex = index, VisibleIndex = index, Tier = 6 }], 1920, 1080, 0, 0, 0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScrolledEnemyResultResetsChangedMatchAndMergesSameMatch(bool same)
    {
        var api = await CreateApi();
        var old = api.Match with { BossId = "enemy_9021_acduml",
            EnemyTypes = same ? ["ELEMENT", "REFLECTION", "DOT"] : ["CRITIC", "DOT", "REFLECTION"] };
        api.ApplyRecognizedMatch(old, [], Grid(0, "森蚺"));
        api.SetCovenantIncomplete("kjerag", true);
        api.SetOperatorBanned("泡泡", true);
        var before = api.Match;
        var next = new MatchInfo("recognized", null, [], [], "enemy_9021_acduml", ["DOT", "REFLECTION", "ELEMENT"]);
        api.ReadInformation = () => Task.FromResult((next, new[] { "佩佩" }));

        // 持续识别与手动识别使用同一合并入口。
        await api.ScanContinuousAsync(true);
        await api.StopContinuousAsync();

        Assert.Equal("enemy_9021_acduml", api.Match.BossId);
        Assert.Equal(next.EnemyTypes, api.Match.EnemyTypes);
        if (same)
        {
            Assert.Equal(before.Covenants, api.Match.Covenants);
            Assert.Equal(before.BanGroups.SelectMany(g => g.Slots).Select(s => s.Name),
                api.Match.BanGroups.SelectMany(g => g.Slots).Select(s => s.Name));
            Assert.Equal(before.BannedOperators.Append("佩佩").Order(), api.Match.BannedOperators.Order());
        }
        else
        {
            Assert.Empty(api.Match.Covenants);
            Assert.Empty(api.Match.BanGroups);
            Assert.Equal(new[] { "佩佩" }, api.Match.BannedOperators);
            Assert.Null(api.Match.StrategyId);
            Assert.False(api.Match.Solo);
        }
    }

    [Fact]
    public async Task EnteringOpeningAgainClearsOldBansEvenWithIdenticalEnemies()
    {
        var api = await CreateApi();
        var next = api.Match with { BossId = "enemy_9021_acduml", EnemyTypes = ["DOT", "REFLECTION", "ELEMENT"] };
        api.ApplyRecognizedMatch(next, [], Grid(0, "森蚺"));
        api.SetOperatorBanned("泡泡", true);
        api.ReadInformation = () => Task.FromResult((next, new[] { "佩佩" }));
        await api.HandleOpeningAsync(false);
        Assert.Equal(new[] { "佩佩" }, api.Match.BannedOperators);
        Assert.Empty(api.Match.BanGroups);
        Assert.Equal(2, api.History().Length);
        await api.RecordInformationAsync();
        Assert.Equal(2, api.History().Length);
    }

    [Fact]
    public async Task LatestSlotWinsAndOldDuplicateStaysUnknown()
    {
        var api = await CreateApi();
        var match = api.Match;
        api.ApplyRecognizedMatch(match, [], Grid(0, "森蚺"));
        api.ApplyRecognizedMatch(match, [], Grid(1, "佩佩"));
        api.ApplyRecognizedMatch(match, [], Grid(1, "森蚺", .8));
        var slots = Assert.Single(api.Match.BanGroups).Slots;
        Assert.Null(slots[0].Name);
        Assert.Equal("森蚺", slots[1].Name);
        Assert.Equal(slots.Count(s => s.Name != null), slots.Select(s => s.Name).OfType<string>().Distinct().Count());
    }
}
