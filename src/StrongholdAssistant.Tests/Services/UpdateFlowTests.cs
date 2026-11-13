using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests.Services;

[Collection("Update environment")]
public sealed class UpdateFlowTests
{
    [Fact]
    public async Task BlockedDownloadDoesNotHoldBridgeOrStartAnotherDownload()
    {
        var directory = Directory.CreateTempSubdirectory("StrongholdAssistant-BridgeTest-");
        using var handler = new BlockedDownloadHandler();
        using var http = new HttpClient(handler);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory.FullName);
            api.Updater = new UpdateService(new Version(1, 0, 0), directory.FullName,
                feedUrl: "http://localhost/feed", httpClient: http);
            api.Changed += (name, value) =>
            {
                if (name == "update" && JsonSerializer.SerializeToElement(value).GetProperty("state").GetString() == "failed")
                    failed.TrySetResult();
            };
            await api.InvokeAsync("checkUpdate", []);
            // 与宿主相同，按顺序等待每条消息完成。
            await api.InvokeAsync("applyUpdate", []).WaitAsync(TimeSpan.FromSeconds(2));
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await api.InvokeAsync("saveSettings", [JsonSerializer.SerializeToElement(api.Settings with { UpdateProxy = "" }, HostApi.JsonOptions)])
                .WaitAsync(TimeSpan.FromSeconds(2));
            await api.InvokeAsync("applyUpdate", []).WaitAsync(TimeSpan.FromSeconds(2));
            var status = JsonSerializer.SerializeToElement(await api.InvokeAsync("checkUpdate", []));
            Assert.Equal("downloading", status.GetProperty("state").GetString());
            Assert.Equal("2.0.0", status.GetProperty("version").GetString());
            Assert.Equal(0, status.GetProperty("progress").GetDouble());
            Assert.Equal(1, handler.Downloads);
            Assert.Equal(1, handler.Checks);
        }
        finally
        {
            handler.Release.TrySetException(new HttpRequestException("测试结束"));
            if (handler.Downloads > 0) await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseBodyStallTimesOut(bool download)
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = Task.Run(async () =>
        {
            using var client = await server.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 100\r\n\r\n"));
            await release.Task;
        });
        try
        {
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            var service = new UpdateService(new Version(1, 0, 0), feedUrl: url, httpClient: http,
                readTimeout: TimeSpan.FromMilliseconds(150));
            if (download)
            {
                var error = await Assert.ThrowsAsync<TaskCanceledException>(() => service.DownloadAndApplyUpdateAsync(
                    new UpdateInfo("v2.0.0", "2.0.0", "", url, "test.zip", null)).WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal("连接更新源超时", UpdateService.DescribeError(error));
            }
            else
            {
                Assert.Null(await service.CheckForUpdateAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal("连接更新源超时", service.LastError);
            }
        }
        finally
        {
            release.TrySetResult();
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class BlockedDownloadHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Downloads;
        public int Checks;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/package")
            {
                Interlocked.Increment(ref Downloads);
                Started.TrySetResult();
                return Release.Task;
            }
            Interlocked.Increment(ref Checks);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"tag_name":"v2.0.0","assets":[{"name":"StrongholdAssistant-v2.0.0-win-x64.zip","browser_download_url":"http://localhost/package"}]}
                    """)
            });
        }
    }
}
