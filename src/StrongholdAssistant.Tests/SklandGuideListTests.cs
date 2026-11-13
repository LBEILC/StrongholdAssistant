using System.Text.Json;
using System.Text.Json.Nodes;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class SklandGuideListTests
{
    // 096 匿名首屏的十条阵容结构；作者、标题、正文、标识和时间已替换，不保存账号资料。
    private static JsonNode Sample() => JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestData", "skland-guide-list.json")))!;

    [Fact]
    public void AnonymousSampleAndDecodedFeedShareGuideRules()
    {
        var sample = Sample();
        var raw = SklandParser.ParseGuidePage(sample["raw"]!.ToJsonString(), null, false, decoded: false);
        var decoded = SklandParser.ParseGuidePage(sample["decoded"]!.ToJsonString(), null, false);
        Assert.Equal(JsonSerializer.Serialize(raw), JsonSerializer.Serialize(decoded));
        Assert.Equal(10, decoded.Items.Length);
        Assert.Equal("next-page", decoded.PageToken);
        Assert.Equal("https://game.skland.com/arknights/stronghold-guide/detail?guideId=100", decoded.Items[0].Url);
        var first = decoded.Items[0].Guide!;
        Assert.Equal("FORMATION", Assert.Single(first.Stages).Type);
        Assert.Equal(new SklandOperator("char_4133_logos", true, "skchr_logos_3", "uniequip_003_logos"), first.Stages[0].Operators[0]);
        Assert.Equal(new[] { "chess_item_2_04_e_a", "chess_item_6_03_e_a", "chess_item_3_04_e_a", "chess_item_5_08_e_a" }, first.Stages[0].Equipment);
        Assert.Equal(5, decoded.Items[1].Guide!.Stages.Length);
        Assert.Contains(decoded.Items[1].Guide!.Stages, s => s.Type == "2" && s.StartRound > 0);
        Assert.Equal("序列I", decoded.Items[0].PhaseName);
        Assert.Equal(new SklandGuideStats(3, 2, 12), decoded.Items[0].Stats);
        Assert.Equal("2023-11-14T22:13:20.0000000Z", decoded.Items[0].EditedAt);
        Assert.Null(SklandParser.ParseGuidePage(sample["decoded"]!.ToJsonString(), "next-page", false).PageToken);
        var anonymous = SklandParser.ParseGuidePage(sample["raw"]!.ToJsonString(), null, true, decoded: false);
        Assert.True(anonymous.LoginRequired);
        Assert.Null(anonymous.PageToken);
        Assert.All(anonymous.Items, item => Assert.False(item.Collected));
    }

    [Fact]
    public void InvalidAndIncompleteGuidesKeepCardsWithoutImportContent()
    {
        var feed = Sample()["decoded"]!;
        feed["list"]![0]!["isInvalid"] = true;
        feed["list"]![1]!["guide"]!.AsObject().Remove("tabs");
        feed["list"]![2]!["relation"]!["collected"] = true;
        feed["hasMore"] = false;
        var page = SklandParser.ParseGuidePage(feed.ToJsonString(), null, false);
        Assert.Equal(10, page.Items.Length);
        Assert.Null(page.Items[0].Guide);
        Assert.Null(page.Items[1].Guide);
        Assert.True(page.Items[2].Collected);
        Assert.Null(page.PageToken);
    }

    [Fact]
    public void QueryMapsSectionsNamesAndKnownStrategies()
    {
        var resources = JsonSerializer.Deserialize<JsonElement>("""
            {"seasonId":"current-season","factions":[{"id":"differentShip","name":"助力"}],"strategyIds":["band_bonus"]}
            """);
        var catalog = JsonSerializer.Deserialize<JsonElement>("""
            {"covenants":[{"id":"deput","name":"助力"}],"strategies":[{"id":"band_bonus"},{"id":"retired"}]}
            """);
        var request = SklandParser.GuideQuery(new("all", "latest", "  标题  ", "deput", ["deput", "unknown"], "band_bonus"), resources, catalog);
        Assert.Equal("getGuideList", request.Method);
        Assert.Equal(2, request.Parameters["sortType"]);
        Assert.Equal("current-season", request.Parameters["seasonId"]);
        Assert.Equal("differentShip", request.Parameters["seasonBondId"]);
        Assert.Equal(new[] { "differentShip" }, request.Parameters["regularBondIds"]);
        Assert.Equal("band_bonus", request.Parameters["strategyId"]);
        Assert.Equal("标题", request.Parameters["titleKeyword"]);
        var ignored = SklandParser.GuideQuery(new("all", Keyword: "  ", Core: "unknown", Extras: ["unknown"], StrategyId: "retired"), resources, catalog);
        Assert.Equal(4, ignored.Parameters.Count);
        Assert.Equal(1, ignored.Parameters["sortType"]);
        var collected = SklandParser.GuideQuery(new("collected", "latest", Core: "deput"), resources, catalog);
        Assert.Equal("getMyCollectedList", collected.Method);
        Assert.Equal(1, collected.Parameters["sortType"]);
        Assert.False(collected.Parameters.ContainsKey("seasonBondId"));
        var published = SklandParser.GuideQuery(new("published", Keyword: new string('文', 45)), resources, catalog);
        Assert.Equal("getMyGuideList", published.Method);
        Assert.False(published.Parameters.ContainsKey("sortType"));
        Assert.Equal(40, ((string)published.Parameters["titleKeyword"]).Length);
    }

    [Fact]
    public async Task SecondCallWaitsForFirstCall()
    {
        var queue = new SklandGuideQueue(() => { }, new ManualClock());
        var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();
        var one = queue.RunAsync(async () => { order.Add(1); return await first.Task; });
        var two = queue.RunAsync(() => { order.Add(2); return Task.FromResult(2); });
        Assert.Equal(new[] { 1 }, order);
        Assert.False(two.IsCompleted);
        first.SetResult(1);
        Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(one, two));
        Assert.Equal(new[] { 1, 2 }, order);
        await queue.CloseAsync();
    }

    [Fact]
    public async Task ClosesAfterFiveIdleMinutesAndResetsOnCall()
    {
        var clock = new ManualClock();
        var closed = 0;
        var queue = new SklandGuideQueue(() => closed++, clock);
        await queue.RunAsync(() => Task.FromResult(1));
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, closed);
        await queue.RunAsync(() => Task.FromResult(2));
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, closed);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, closed);
        await queue.RunAsync(() => Task.FromResult(3));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(2, closed);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        private readonly List<ManualTimer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, _now + dueTime);
            _timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan time)
        {
            _now += time;
            foreach (var timer in _timers.ToArray())
                if (!timer.Disposed && timer.Due <= _now) { timer.Dispose(); timer.Callback(timer.State); }
        }
        private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            public TimerCallback Callback { get; } = callback;
            public object? State { get; } = state;
            public DateTimeOffset Due { get; } = due;
            public bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
