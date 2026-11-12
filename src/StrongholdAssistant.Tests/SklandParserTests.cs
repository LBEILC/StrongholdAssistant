using System.Text.Json.Nodes;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class SklandParserTests
{
    private const string Url = "https://game.skland.com/arknights/stronghold-guide/detail?header=0&guideId=123";
    // 手写样本，不保存真实响应或账号资料。
    private const string GuideJson = """
        {"code":0,"data":{
          "guide":{"invalid":0,"poster":{"nickname":"测试作者"},
          "guide":{"title":"测试攻略","strategyIds":["band_bonus"],"versionId":"V3_1","tabs":[
            {"tabType":1,"startRound":0,"endRound":0,
             "recChars":[{"charId":"char_171_bldsk","mergeLevel":1,"skillId":"skchr_bldsk_2","moduleId":"uniequip_002_bldsk"}],
             "recItems":[{"itemId":"chess_item_6_03_e_a"}],
             "activeBonds":{"seasonBondIds":["kjeragShip"],"regularBondIds":["docShip"]},
             "operationIdea":"第一行\n第二行"},
            {"tabType":2,"startRound":1,"endRound":5,
             "recChars":[{"charId":"char_1027_greyy2","mergeLevel":0,"skillId":"","moduleId":""}],"recItems":[],
             "activeBonds":{"seasonBondIds":[],"regularBondIds":[]},"operationIdea":""}
          ]}}}}
        """;

    [Fact]
    public void GuidePreservesStagesAndAllowsMissingStrategies()
    {
        var guide = SklandParser.ParseGuide(GuideJson, Url);
        Assert.Equal("https://game.skland.com/arknights/stronghold-guide/detail?guideId=123", guide.Url);
        Assert.Equal("测试攻略", guide.Title);
        Assert.Equal("测试作者", guide.Author);
        Assert.Equal("V3_1", guide.VersionId);
        Assert.Equal(["band_bonus"], guide.Strategies);
        Assert.Equal(2, guide.Stages.Length);
        Assert.Equal(["kjerag", "doc"], guide.Stages[0].Covenants);
        Assert.Equal(["chess_item_6_03_e_a"], guide.Stages[0].Equipment);
        Assert.Equal("第一行\n第二行", guide.Stages[0].Notes);
        Assert.True(Assert.Single(guide.Stages[0].Operators).Elite);
        Assert.False(Assert.Single(guide.Stages[1].Operators).Elite);
        Assert.Equal("skchr_bldsk_2", guide.Stages[0].Operators[0].Skill);
        Assert.Equal("uniequip_002_bldsk", guide.Stages[0].Operators[0].Module);
        var empty = System.Text.Json.JsonSerializer.SerializeToElement(guide.Stages[1].Operators[0], HostApi.JsonOptions);
        Assert.False(empty.TryGetProperty("skill", out _));
        Assert.False(empty.TryGetProperty("module", out _));
        Assert.Equal("FORMATION", guide.Stages[0].Type);
        Assert.Equal("2", guide.Stages[1].Type);
        Assert.Equal(1, guide.Stages[1].StartRound);
        Assert.Equal(5, guide.Stages[1].EndRound);
        var missing = JsonNode.Parse(GuideJson)!;
        missing["data"]!["guide"]!["guide"]!.AsObject().Remove("strategyIds");
        Assert.Empty(SklandParser.ParseGuide(missing.ToJsonString(), Url).Strategies);
    }

    [Fact]
    public void GuideRejectsInvalidOrNonzeroCode()
    {
        var invalid = JsonNode.Parse(GuideJson)!;
        invalid["data"]!["guide"]!["invalid"] = 1;
        foreach (var json in new[] { invalid.ToJsonString(), "{\"code\":1}" })
            Assert.Equal("这篇攻略不存在或已失效。",
                Assert.Throws<ArgumentException>(() => SklandParser.ParseGuide(json, Url)).Message);
    }

    [Fact]
    public void HistoryReturnsNullForNonzeroCode()
    {
        Assert.Null(SklandParser.ParseHistory("{\"code\":1}"));
    }

    [Fact]
    public void HistoryConvertsSecondsAndEliteLevelAndSortsNewestFirst()
    {
        const string json = """
            {"code":0,"data":{"records":[
              {"id":"older","bandId":"","chessSquad":[
                {"charId":"char_171_bldsk","chessId":"chess_a","chessInfo":{"goldenChessId":"chess_b"},"equips":[]}],
               "completeState":0,"gameStartTs":"0","gameEndTs":"0","coinCost":0,"killCnt":0,
               "bondInfo":[],"acBossInfo":[],"modeType":"SINGLE","modeDifficulty":"NORMAL","passRound":8,"versionId":"V3_1"},
              {"id":"newer","bandId":"band_mech","chessSquad":[
                {"charId":"char_1027_greyy2","chessId":"chess_b","chessInfo":{"goldenChessId":"chess_b"},
                 "skillId":"skchr_greyy2_2","moduleId":"uniequip_002_greyy2",
                 "equips":[{"equipId":"equip_1"},{"equipId":"equip_2"}]},
                {"charId":"char_171_bldsk","chessId":"other_a","chessInfo":{"goldenChessId":"other_b"},
                 "equips":[{"equipId":"equip_3"}]}],
               "completeState":1,"gameStartTs":"30","gameEndTs":"60","coinCost":95,"killCnt":92,
               "bondInfo":[{"bondId":"skillfulShip","layer":32,"activate":1}],
               "acBossInfo":[{"bossId":"boss_2","isKilled":true},{"bossId":"boss_9","isKilled":false},
                 {"bossId":"boss_5","isKilled":false},{"bossId":"unknown","isKilled":true}],
               "modeType":"MULTI","modeDifficulty":"ABYSS","passRound":15,"versionId":"V3_1"}
            ]}}
            """;
        var records = SklandParser.ParseHistory(json);
        Assert.NotNull(records);
        Assert.Equal(["newer", "older"], records.Select(r => r.Id));
        Assert.Equal("1970-01-01T00:01:00.0000000Z", records[0].Time);
        Assert.Equal("1970-01-01T00:00:00.0000000Z", records[1].Time);
        Assert.True(records[0].Operators[0].Elite);
        Assert.Equal("skchr_greyy2_2", records[0].Operators[0].Skill);
        Assert.Equal("uniequip_002_greyy2", records[0].Operators[0].Module);
        Assert.Null(records[1].Operators[0].Skill);
        Assert.Null(records[1].Operators[0].Module);
        Assert.False(records[0].Operators[1].Elite);
        Assert.False(Assert.Single(records[1].Operators).Elite);
        Assert.Equal("band_mech", records[0].StrategyId);
        Assert.Null(records[1].StrategyId);
        Assert.Equal(["equip_1", "equip_2", "equip_3"], records[0].Equipment);
        Assert.True(records[0].Success);
        Assert.False(records[1].Success);
        Assert.Equal("1970-01-01T00:00:30.0000000Z", records[0].StartTime);
        Assert.Equal(95, records[0].CoinSpent);
        Assert.Equal(92, records[0].Kills);
        Assert.Equal(new SklandCovenant("skillful", 32), Assert.Single(records[0].Covenants));
        Assert.Equal([new SklandBoss("enemy_9017_achunt", true), new SklandBoss("enemy_9017_achunt", false),
            new SklandBoss("boss_5", false), new SklandBoss("unknown", true)], records[0].Bosses);
        Assert.Equal("MULTI", records[0].Mode);
        Assert.Equal("ABYSS", records[0].Difficulty);
        Assert.Equal(15, records[0].PassRound);
    }
}
