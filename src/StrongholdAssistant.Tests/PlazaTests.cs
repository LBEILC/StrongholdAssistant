using System.Text;
using System.Text.Json;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class PlazaTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);

    [Fact]
    public async Task TokenIsEncryptedAndLogoutRemovesIt()
    {
        var api = await CreateApi();
        var store = new PlazaTokenStore(_directory);
        const string token = "test-plaza-token-凭据-12345";
        Assert.Null(await api.InvokeAsync("plazaToken", []));
        store.Save(token);
        Assert.Equal(token, await api.InvokeAsync("plazaToken", []));
        Assert.DoesNotContain(token, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(_directory, "plaza-token.bin"))));
        var backup = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(backup);
        Assert.DoesNotContain(token, await File.ReadAllTextAsync(backup));
        await api.InvokeAsync("plazaLogout", []);
        Assert.Null(await api.InvokeAsync("plazaToken", []));
        await File.WriteAllTextAsync(Path.Combine(_directory, "plaza-token.bin"), "invalid ciphertext");
        Assert.Null(store.Read());
    }

    [Fact]
    public void LoginMessageRequiresPlazaOriginTypeAndStringToken()
    {
        const string valid = "{\"type\":\"plaza-login\",\"token\":\"test-token\"}";
        Assert.Equal("test-token", HostApi.ParsePlazaLogin(HostApi.PlazaOrigin + "/login?app=1", valid));
        Assert.Null(HostApi.ParsePlazaLogin("https://example.com", valid));
        Assert.Null(HostApi.ParsePlazaLogin(HostApi.PlazaOrigin + ".example.com", valid));
        Assert.Null(HostApi.ParsePlazaLogin(HostApi.PlazaOrigin, "{\"type\":\"other\",\"token\":\"x\"}"));
        Assert.Null(HostApi.ParsePlazaLogin(HostApi.PlazaOrigin, "{\"type\":\"plaza-login\"}"));
        Assert.Null(HostApi.ParsePlazaLogin(HostApi.PlazaOrigin, "{\"type\":\"plaza-login\",\"token\":123}"));
    }

    [Fact]
    public void DeepLinkRequiresPrefixAndAtMost2000Characters()
    {
        Assert.True(DeepLinkChannel.IsValid("stronghold://import?post=123"));
        Assert.True(DeepLinkChannel.IsValid("stronghold://".PadRight(2000, 'x')));
        Assert.False(DeepLinkChannel.IsValid("stronghold://".PadRight(2001, 'x')));
        Assert.False(DeepLinkChannel.IsValid("https://example.com"));
        Assert.False(DeepLinkChannel.IsValid(null));
    }

    [Fact]
    public async Task DescriptionSurvivesStorageAndBackupAndRejects501Characters()
    {
        var api = await CreateApi();
        var input = new LineupDto("", "阵容", [new("折光")], "user", null, DateTime.UtcNow)
        { Description = new string('简', 500) };
        var saved = await api.SaveLineupAsync(input);
        api = await CreateApi();
        Assert.Equal(input.Description, Assert.Single(await api.LineupsAsync()).Description);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => api.SaveLineupAsync(saved with { Description = new string('简', 501) }));
        Assert.Equal("简介最多 500 字。", error.Message);
        var path = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(path);
        var backup = await api.ReadBackupAsync(path);
        await api.SaveLineupAsync(saved with { Description = "修改后" });
        await api.ImportDataAsync(backup.Token, "replace");
        Assert.Equal(input.Description, Assert.Single(await api.LineupsAsync()).Description);
        await api.SaveLineupAsync(saved with { Description = null });
        var legacy = Assert.Single(await api.LineupsAsync());
        Assert.DoesNotContain("description", JsonSerializer.Serialize(legacy, HostApi.JsonOptions));
        Assert.DoesNotContain("description", await File.ReadAllTextAsync(Path.Combine(_directory, "lineups", saved.Id + ".json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
