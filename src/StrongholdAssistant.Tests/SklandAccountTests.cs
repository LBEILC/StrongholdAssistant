using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class SklandAccountTests
{
    [Theory]
    [InlineData("""{"code":0,"data":{"list":[{"appCode":"arknights","bindingList":[{"nickname":"甲#0001"},{"nickname":"乙#0002","isDefault":true}]}]}}""", "乙#0002")]
    [InlineData("""{"code":0,"data":{"list":[{"appCode":"endfield","bindingList":[{"nickname":"其他游戏","isDefault":true}]},{"appCode":"arknights","bindingList":[{"nickname":"甲#0001"},{"nickname":"乙#0002"}]}]}}""", "甲#0001")]
    [InlineData("""{"code":0,"data":{"list":[{"appCode":"arknights","bindingList":[]}]}}""", null)]
    [InlineData("""{"code":10001,"message":"未登录","data":null}""", null)]
    public void SelectsDefaultOrFirstArknightsNickname(string json, string? expected)
    {
        Assert.Equal(expected, SklandParser.ParseAccount(json)?.Nickname);
    }

    [Fact]
    public void LoginFormWaitsWithoutTimeoutThenAllowsPageToLoad()
    {
        var start = DateTime.UtcNow;
        var waitingSince = start;
        Assert.False(SklandParser.HistoryWaitExpired(true, true, start.AddSeconds(60), ref waitingSince));
        Assert.False(SklandParser.HistoryWaitExpired(true, false, start.AddSeconds(74), ref waitingSince));
        Assert.True(SklandParser.HistoryWaitExpired(true, false, start.AddSeconds(75), ref waitingSince));
    }

    [Fact]
    public void MissingFormAndButtonStillTimesOut()
    {
        var start = DateTime.UtcNow;
        var waitingSince = start;
        Assert.False(SklandParser.HistoryWaitExpired(true, false, start.AddSeconds(14), ref waitingSince));
        Assert.True(SklandParser.HistoryWaitExpired(true, false, start.AddSeconds(15), ref waitingSince));
        Assert.True(SklandParser.HistoryWaitExpired(false, true, start.AddSeconds(15), ref waitingSince));
    }
}
