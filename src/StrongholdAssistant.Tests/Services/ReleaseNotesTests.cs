using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Services;
using StrongholdAssistant.Host;

namespace ASPAssistant.Tests.Services;

[Collection("Update environment")]
public sealed class ReleaseNotesTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("version")]
    [InlineData("schema")]
    [InlineData("large")]
    [InlineData("missing")]
    [InlineData("failure")]
    public async Task NotesAreOptionalAndAvailableChecksRequestLatestAgain(string scenario)
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var feedRequests = 0;
        var serving = Task.Run(async () =>
        {
            var requests = scenario == "missing" ? 2 : 4;
            for (var i = 0; i < requests; i++)
            {
                using var client = await server.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var path = (await reader.ReadLineAsync(timeout.Token))!.Split(' ')[1];
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
                string body;
                if (path == "/")
                {
                    feedRequests++;
                    var version = $"99.0.{feedRequests}";
                    var assets = new List<object> { new { name = $"StrongholdAssistant-v{version}-win-x64.zip", browser_download_url = url + "package" } };
                    if (scenario != "missing") assets.Add(new { name = "release-notes.json", browser_download_url = url + "notes" });
                    body = JsonSerializer.Serialize(new { tag_name = "v" + version, assets });
                }
                else
                {
                    body = JsonSerializer.Serialize(new
                    {
                        schema = scenario == "schema" ? "wrong" : "sa-release-notes/1",
                        version = scenario == "version" ? "0.0.1" : $"99.0.{feedRequests}",
                        date = "2026-11-12",
                        items = new[] { new { kind = "fixed", title = "修复", detail = scenario == "large" ? new string('x', 65536) : "保存", highlight = true } }
                    });
                }
                var bytes = Encoding.UTF8.GetBytes(body);
                var status = path == "/notes" && scenario == "failure" ? "500 Internal Server Error" : "200 OK";
                // 不给长度，验证流式下载的 64 KB 上限。
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n"), timeout.Token);
                await stream.WriteAsync(bytes, timeout.Token);
            }
        });
        var previous = Environment.GetEnvironmentVariable(UpdateService.FeedOverrideVariable);
        var directory = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, url);
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory);
            api.Updater = new UpdateService(new Version(0, 4, 0));
            for (var i = 1; i <= 2; i++)
            {
                var result = JsonSerializer.SerializeToElement(await api.InvokeAsync("checkUpdate", []));
                Assert.Equal("available", result.GetProperty("state").GetString());
                Assert.Equal($"99.0.{i}", result.GetProperty("version").GetString());
                Assert.Equal(scenario == "valid", result.TryGetProperty("notes", out var notes));
                if (scenario == "valid") Assert.Equal($"99.0.{i}", notes.GetProperty("version").GetString());
                Assert.Null(api.Updater.LastError);
            }
            await serving;
            Assert.Equal(2, feedRequests);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, previous);
            timeout.Cancel();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
