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

    [Fact]
    public async Task PartialCovenantsPreserveMatchAndTitlelessAvatarsJoinExistingGroup()
    {
        var api = await CreateApi();
        // 数据顺序为 10 个核心盟约、16 个附加盟约。
        var full = api.Match with { BossId = "enemy_9021_acduml" };
        Assert.Equal(26, full.Covenants.Length);
        api.ApplyRecognizedMatch(full, [], Grid(0, "森蚺"));
        api.ApplyRecognizedMatch(full, ["芬"]);
        api.SetOperatorBanned("泡泡", true);
        var before = api.Match;
        var partial = full with { Covenants = full.Covenants.Take(20).ToArray() };

        api.ApplyRecognizedMatch(partial, []);

        Assert.Equal(full.Covenants, api.Match.Covenants);
        Assert.Equal(before.BannedOperators.Order(), api.Match.BannedOperators.Order());
        Assert.Equal("sargon", Assert.Single(api.Match.BanGroups).Covenant);
        Assert.Equal("森蚺", Assert.Single(api.Match.BanGroups[0].Slots).Name);
        Assert.Single(api.History());

        var names = new[] { "森蚺", "佩佩", "至简" };
        for (int count = 2; count <= names.Length; count++)
        {
            var grid = Grid(0, "森蚺") with
            {
                Slots = names.Take(count).Select((name, index) =>
                    Grid(index, name).Slots[0] with { Covenant = null, SlotIndex = null }).ToArray()
            };
            api.ApplyRecognizedMatch(full with { Covenants = [] }, names.Take(count), grid);
            var group = Assert.Single(api.Match.BanGroups);
            Assert.Equal("sargon", group.Covenant);
            Assert.Equal(names.Take(count), group.Slots.Select(s => s.Name));
        }

        api.ApplyRecognizedMatch(full, []);
        Assert.Equal(full.Covenants, api.Match.Covenants);
        Assert.All(names.Concat(["芬", "泡泡"]), name => Assert.Contains(name, api.Match.BannedOperators));
        Assert.Equal(names, Assert.Single(api.Match.BanGroups).Slots.Select(s => s.Name));
        Assert.Single(api.History());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewCovenantOrChangedStateStillStartsNewMatch(bool changedState)
    {
        var api = await CreateApi();
        var full = api.Match with { BossId = "enemy_9021_acduml" };
        var old = full with { Covenants = full.Covenants.Take(20).ToArray() };
        api.ApplyRecognizedMatch(old, [], Grid(0, "森蚺"), opening: true);
        api.ApplyRecognizedMatch(old, ["芬"]);
        api.SetOperatorBanned("泡泡", true);
        var covenant = changedState
            ? old.Covenants[0] with { Incomplete = !old.Covenants[0].Incomplete }
            : full.Covenants[20];

        api.ApplyRecognizedMatch(full with { Covenants = [covenant] }, []);

        Assert.Equal(new[] { covenant }, api.Match.Covenants);
        Assert.Empty(api.Match.BanGroups);
        Assert.DoesNotContain("芬", api.Match.BannedOperators);
        Assert.DoesNotContain("泡泡", api.Match.BannedOperators);
        Assert.Equal(2, api.History().Length);
    }

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
    public async Task MissedOpeningThenDetailRecordingLeavesModeUnknown()
    {
        var state = new InformationScreenState();
        // 日志时序：一帧完整开局，随后滚动失去标题，最后在局内手动录入。
        Assert.False(state.Observe(true, false));
        Assert.False(state.Observe(false, false));
        Assert.False(state.IsConsumed);
        var api = await CreateApi();
        api.ReadInformation = () => Task.FromResult((api.Match with { Mode = null }, Array.Empty<string>()));
        await api.RecordInformationAsync();
        state.MarkRecorded();
        Assert.Null(api.Match.Mode);
        Assert.Null(Assert.Single(api.History()).Local!.Mode);
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
