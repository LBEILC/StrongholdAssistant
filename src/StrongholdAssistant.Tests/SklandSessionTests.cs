using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class SklandSessionTests
{
    [Theory]
    [InlineData("""{"code":0,"data":{"list":[{"appCode":"arknights","bindingList":[{"uid":"sample-role"}]}]}}""", true)]
    [InlineData("""{"code":0,"data":{"list":[{"appCode":"arknights","bindingList":[]}]}}""", false)]
    [InlineData("""{"code":0,"data":{"list":[{"appCode":"endfield","bindingList":[{}]}]}}""", false)]
    [InlineData("""{"code":10001,"message":"expired","data":{}}""", null)]
    public void BindingDistinguishesRoleEmptyAndBusinessFailure(string json, bool? expected)
    {
        Assert.Equal(expected, HostApi.SklandHasRole(json));
    }

    [Fact]
    public void ApiLogContainsOnlyStatusCodeAndListCounts()
    {
        const string json = """
            {"code":10001,"message":"secret-cred secret-token secret-cookie",
             "cred":"secret-cred","token":"secret-token","Cookie":"secret-cookie",
             "data":{"list":[{"appCode":"arknights","bindingList":[{"uid":"private-uid",
               "cred":"secret-cred","token":"secret-token"}]}],"records":[]}}
            """;
        using var writtenLog = new StringWriter();
        writtenLog.WriteLine(HostApi.SklandResponseSummary("GET",
            "https://zonai.skland.com/api/v1/game/player/binding?cred=secret-cred&token=secret-token",
            200, json));
        var log = writtenLog.ToString();
        Assert.Contains("GET /api/v1/game/player/binding HTTP 200 code=10001", log);
        Assert.Contains("$.data.list=1", log);
        Assert.Contains("$.data.list[].bindingList=1", log);
        Assert.Contains("$.data.records=0", log);
        foreach (var secret in new[] { "secret-cred", "secret-token", "secret-cookie", "private-uid" })
            Assert.DoesNotContain(secret, log);
        Assert.DoesNotContain("?", log);
    }
}
