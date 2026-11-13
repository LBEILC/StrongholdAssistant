using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class SklandLoginTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("""{"nickName":"新昵称","roles":[]}""", "新昵称")]
    [InlineData("""{"nickname":"新昵称","roles":[]}""", "新昵称")]
    [InlineData("""{"roles":[]}""", "明日方舟角色")]
    public async Task LoggedInReturnsFreshRoleWithoutOpeningWindow(string binding, string expected)
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "skland-account.json"), """{"nickname":"旧昵称"}""");
        var calls = new List<bool>();
        api.SklandLoginSessionReader = visible =>
        {
            calls.Add(visible);
            return Task.FromResult<string?>(
                """{"code":0,"data":{"list":[{"appCode":"arknights","bindingList":[""" + binding + "]}]}}");
        };

        var result = Assert.IsType<SklandAccount>(await api.InvokeAsync("sklandLogin", []));

        Assert.Equal(expected, result.Nickname);
        Assert.Equal(new[] { false }, calls);
        Assert.Equal(result, Assert.IsType<SklandAccount>(await api.InvokeAsync("sklandAccount", [])));
    }

    [Fact]
    public async Task ClosingLoginWindowReturnsNullAndKeepsNickname()
    {
        var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "skland-account.json");
        const string saved = """{"nickname":"原昵称"}""";
        File.WriteAllText(path, saved);
        var calls = new List<bool>();
        var closed = new TaskCompletionSource<string?>();
        api.SklandLoginSessionReader = visible =>
        {
            calls.Add(visible);
            return visible ? closed.Task : Task.FromResult<string?>(null);
        };

        var login = api.InvokeAsync("sklandLogin", []);
        Assert.False(login.IsCompleted);
        closed.SetResult(null);

        Assert.Null(await login);
        Assert.Equal(new[] { false, true }, calls);
        Assert.Equal(saved, File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
