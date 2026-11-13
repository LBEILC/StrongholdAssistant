using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests.Services;

[Collection("Update environment")]
public sealed class MirrorChyanUpdateTests
{
    [Fact]
    public async Task RefreshSameVersionFailsWithoutDownloading()
    {
        using var fixture = new MirrorFixture(refreshSameVersion: true);
        var api = await fixture.CreateApi();
        var failed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Changed += (name, value) =>
        {
            var status = JsonSerializer.SerializeToElement(value);
            if (name == "update" && status.GetProperty("state").GetString() == "failed") failed.TrySetResult(status);
        };
        await api.CheckUpdateAsync();
        await api.ApplyUpdateAsync();
        var result = await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Mirror酱 上的版本不比当前新，已取消更新", result.GetProperty("message").GetString());
        Assert.Equal(2, fixture.ApiRequests.Count);
        Assert.DoesNotContain(fixture.Requests, r => r.StartsWith("/package-"));
        Assert.False(api.UpdatePrepared);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task ValidCdkRefreshesUrlAndVerifiesPackage(bool installed, bool wrongHash)
    {
        using var fixture = new MirrorFixture(installed, wrongHash);
        var api = await fixture.CreateApi();
        var events = new List<JsonElement>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Changed += (name, value) =>
        {
            if (name != "update") return;
            var status = JsonSerializer.SerializeToElement(value);
            events.Add(status);
            if (status.GetProperty("state").GetString() is "ready" or "failed") finished.TrySetResult();
        };
        var status = JsonSerializer.SerializeToElement(await api.CheckUpdateAsync());
        Assert.Equal("available", status.GetProperty("state").GetString());
        Assert.Equal("mirrorchyan", status.GetProperty("source").GetString());
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(2000000000), DateTimeOffset.Parse(status.GetProperty("cdkExpiresAt").GetString()!));
        await api.ApplyUpdateAsync();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(wrongHash ? "failed" : "ready", events.Last().GetProperty("state").GetString());
        Assert.Equal(!wrongHash, api.UpdatePrepared);
        Assert.Equal(2, fixture.ApiRequests.Count);
        Assert.All(fixture.ApiRequests, request =>
        {
            Assert.Contains(installed ? "arch=x64&" : "arch=&", request);
            Assert.Contains("user_agent=StrongholdAssistant", request);
            Assert.Contains("cdk=test-cdk", request);
            Assert.DoesNotContain("current_version", request);
            Assert.DoesNotContain("channel=", request);
        });
        Assert.Contains("/package-2", fixture.Requests);
        Assert.DoesNotContain("/package-1", fixture.Requests);
        Assert.DoesNotContain(fixture.Requests, r => r.Contains("SHA256SUMS"));
        if (wrongHash) Assert.Null(api.Updater.PendingScriptPath);
        else Assert.True(File.Exists(api.Updater.PendingScriptPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoCdkBuildsGitHubAssetUrls(bool installed)
    {
        using var fixture = new MirrorFixture(installed);
        var service = new UpdateService(new Version(1, 0, 0), fixture.DirectoryPath, mirrorChyanUrl: fixture.Endpoint, httpClient: fixture.Http);
        var info = await service.CheckMirrorChyanAsync("  ");
        Assert.NotNull(info);
        Assert.Equal("github", info.Source);
        var root = "https://github.com/LBEILC/StrongholdAssistant/releases/download/v99.0.0/";
        Assert.Equal(root + "StrongholdAssistant-v99.0.0-" + (installed ? "setup.exe" : "win-x64.zip"), info.DownloadUrl);
        Assert.Equal(root + "SHA256SUMS.txt", info.ChecksumsUrl);
        Assert.DoesNotContain("cdk=", Assert.Single(fixture.ApiRequests));
    }

    [Fact]
    public async Task InvalidCdkReadsHttpErrorBodyWithoutFallback()
    {
        using var fixture = new MirrorFixture(errorCode: 7002);
        var api = await fixture.CreateApi();
        var status = JsonSerializer.SerializeToElement(await api.CheckUpdateAsync());
        Assert.Equal("failed", status.GetProperty("state").GetString());
        Assert.True(status.GetProperty("cdkProblem").GetBoolean());
        Assert.Equal("Mirror酱 CDK 不对，请检查有没有填错。", status.GetProperty("message").GetString());
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task LatestStillIncludesCdkExpiry()
    {
        using var fixture = new MirrorFixture();
        var api = await fixture.CreateApi();
        api.Updater = new UpdateService(new Version(99, 0, 0), fixture.DirectoryPath, mirrorChyanUrl: fixture.Endpoint, httpClient: fixture.Http);
        var status = JsonSerializer.SerializeToElement(await api.CheckUpdateAsync());
        Assert.Equal("latest", status.GetProperty("state").GetString());
        Assert.True(status.TryGetProperty("cdkExpiresAt", out _));
    }

    // 更新说明走固定 GitHub 地址；测试不依赖公网可达性。
    private sealed class LocalUpdateHandler : HttpClientHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => request.RequestUri!.IsLoopback ? base.SendAsync(request, ct)
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class MirrorFixture : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly HashSet<string> _oldJobs = System.IO.Directory.GetDirectories(Path.GetTempPath(), "StrongholdAssistant-Update-*").ToHashSet();
        private readonly Task _server;
        public string Endpoint { get; }
        public HttpClient Http { get; } = new(new LocalUpdateHandler());
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        public List<string> Requests { get; } = [];
        public List<string> ApiRequests { get; } = [];

        public MirrorFixture(bool installed = false, bool wrongHash = false, int errorCode = 0, bool refreshSameVersion = false)
        {
            System.IO.Directory.CreateDirectory(DirectoryPath);
            if (installed) File.WriteAllText(Path.Combine(DirectoryPath, "installed.json"), "{}");
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(zip.CreateEntry("StrongholdAssistant.exe").Open()))
                writer.Write("测试包，不执行");
            var package = memory.ToArray();
            var hash = wrongHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(package));
            _listener.Start();
            var root = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            Endpoint = root + "/latest";
            _server = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        await using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                        var request = (await reader.ReadLineAsync(_stop.Token))!.Split(' ')[1];
                        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                        Requests.Add(request);
                        var isApi = request.StartsWith("/latest?");
                        if (isApi) ApiRequests.Add(request);
                        var body = isApi ? JsonSerializer.SerializeToUtf8Bytes(new
                        {
                            code = errorCode, msg = "fixture", data = new
                            {
                                version_name = refreshSameVersion && ApiRequests.Count > 1 ? "v1.0.0" : "v99.0.0", url = request.Contains("cdk=") ? root + "/package-" + ApiRequests.Count : null,
                                sha256 = hash, filesize = package.Length, cdk_expired_time = 2000000000
                            }
                        }) : package;
                        var status = isApi && errorCode != 0 ? "403 Forbidden" : "200 OK";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                        await stream.WriteAsync(body);
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        public async Task<HostApi> CreateApi()
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), DirectoryPath);
            api.SaveSettings(api.Settings with { UpdateSource = "mirrorchyan", MirrorChyanCdk = "  test-cdk  " });
            api.Updater = new UpdateService(new Version(1, 0, 0), DirectoryPath, mirrorChyanUrl: Endpoint, httpClient: Http);
            return api;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _server.GetAwaiter().GetResult();
            _listener.Stop();
            _stop.Dispose();
            Http.Dispose();
            System.IO.Directory.Delete(DirectoryPath, true);
            foreach (var job in System.IO.Directory.GetDirectories(Path.GetTempPath(), "StrongholdAssistant-Update-*").Except(_oldJobs))
            {
                Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\'), Path.GetDirectoryName(Path.GetFullPath(job))!.TrimEnd('\\'));
                System.IO.Directory.Delete(job, true);
            }
        }
    }
}
