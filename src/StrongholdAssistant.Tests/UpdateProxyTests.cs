using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public sealed class UpdateProxyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
    private Task<HostApi> CreateApi() => HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), _directory);

    [Fact]
    public async Task SettingsDefaultPersistAndRejectInvalidAddressesWithoutSaving()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, "{}");
        var api = await CreateApi();
        Assert.Equal("", api.Settings.UpdateProxy);
        foreach (var proxy in new[] { "http://127.0.0.1:7890", "SOCKS5://localhost:1080" })
        {
            api.SaveSettings(api.Settings with { UpdateProxy = proxy });
            Assert.Equal(proxy, api.SettingsStore.LoadSettings().UpdateProxy);
        }
        var saved = await File.ReadAllTextAsync(path);
        foreach (var proxy in new[] { "127.0.0.1:7890", "ftp://a:1", "http://u:p@a:1", "http://a:0", "http://a:1/x" })
        {
            var error = Assert.Throws<ArgumentException>(() => api.SaveSettings(api.Settings with { UpdateProxy = proxy }));
            Assert.Equal("代理地址格式不对。", error.Message);
            Assert.Equal(saved, await File.ReadAllTextAsync(path));
            Assert.Equal("SOCKS5://localhost:1080", api.Settings.UpdateProxy);
        }
    }

    [Fact]
    public async Task SameUpdaterUsesProxyThenSystemWithoutRestart()
    {
        using var origin = new LocalReleaseServer();
        using var proxy = new LocalReleaseServer();
        var api = await CreateApi();
        api.Updater = new UpdateService(new Version(1, 0, 0), feedUrl: origin.Url);
        api.SaveSettings(api.Settings with { UpdateProxy = proxy.Url.TrimEnd('/') });
        await api.CheckUpdateAsync();
        Assert.Null(api.Updater.LastError);
        Assert.Equal(1, proxy.Requests);
        Assert.StartsWith("GET " + origin.Url, proxy.LastRequest);
        Assert.Equal(0, origin.Requests);
        api.SaveSettings(api.Settings with { UpdateProxy = "" });
        await api.CheckUpdateAsync();
        Assert.Null(api.Updater.LastError);
        Assert.Equal(1, proxy.Requests);
        Assert.Equal(1, origin.Requests);
    }

    [Fact]
    public async Task UnavailableProxyReturnsAddressInFailure()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var api = await CreateApi();
        api.Updater = new UpdateService(new Version(1, 0, 0), feedUrl: "http://127.0.0.1:1/");
        api.SaveSettings(api.Settings with { UpdateProxy = $"http://127.0.0.1:{port}" });
        var result = JsonSerializer.SerializeToElement(await api.CheckUpdateAsync());
        Assert.Equal("failed", result.GetProperty("state").GetString());
        var message = result.GetProperty("message").GetString();
        Assert.Contains("通过代理", message);
        Assert.Contains($"127.0.0.1:{port}", message);
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("replace")]
    public async Task BackupOmitsProxyAndImportPreservesLocalProxy(string mode)
    {
        var api = await CreateApi();
        api.SaveSettings(api.Settings with { UpdateProxy = "http://127.0.0.1:7890" });
        var path = Path.Combine(_directory, "backup.json");
        await api.ExportBackupAsync(path);
        var backup = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Equal("", backup["settings"]!["updateProxy"]!.GetValue<string>());
        backup["settings"]!["updateProxy"] = "socks5://localhost:1080";
        await File.WriteAllTextAsync(path, backup.ToJsonString());
        var selected = await api.ReadBackupAsync(path);
        await api.ImportDataAsync(selected.Token, mode);
        Assert.Equal("http://127.0.0.1:7890", api.Settings.UpdateProxy);
        Assert.Equal(api.Settings.UpdateProxy, api.SettingsStore.LoadSettings().UpdateProxy);
    }

    [Theory]
    [InlineData("http://private-proxy:7890", "http")]
    [InlineData("SOCKS5://private-proxy:1080", "socks5")]
    [InlineData("", "system")]
    public void DiagnosticsContainOnlyProxyProtocol(string proxy, string expected)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "diagnostics.zip");
        new DiagnosticsStore(_directory).Export(path, new("terminal", true, true) { UpdateProxy = proxy },
            new("disconnected", ""), new { version = "1.0.0" });
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("settings.json")!.Open());
        var text = reader.ReadToEnd();
        Assert.DoesNotContain("private-proxy", text);
        using var json = JsonDocument.Parse(text);
        Assert.Equal(expected, json.RootElement.GetProperty("updateProxy").GetString());
    }

    private sealed class LocalReleaseServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public int Requests { get; private set; }
        public string? LastRequest { get; private set; }
        public string Url { get; }

        public LocalReleaseServer()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _loop = RunAsync();
        }

        private async Task RunAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    LastRequest = await reader.ReadLineAsync(_stop.Token);
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                    Requests++;
                    const string body = """{"tag_name":"v1.0.0","assets":[]}""";
                    var bytes = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                    await stream.WriteAsync(bytes, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _loop.GetAwaiter().GetResult();
            _listener.Stop();
            _stop.Dispose();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}