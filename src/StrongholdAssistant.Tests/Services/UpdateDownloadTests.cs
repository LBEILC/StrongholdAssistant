// 2026-11-11：适配本项目的更新包与环境变量。
// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ASPAssistant.Core.Services;
using FluentAssertions;
using StrongholdAssistant.Host;
using System.Text.Json;

namespace ASPAssistant.Tests.Services;

[Collection("Update environment")]
public class UpdateDownloadTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task LocalFeed_ChecksDownloadsVerifiesAndPreparesWithoutLaunching(bool wrapped, bool wrongHash)
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var prefix = wrapped ? "win-x64/" : "";
            foreach (var name in new[] { "StrongholdAssistant.exe", "data/test.json", "runtimes/test.txt" })
            {
                await using var writer = zip.CreateEntry(prefix + name).Open();
                await writer.WriteAsync(Encoding.UTF8.GetBytes("isolated download fixture; never executed"));
            }
        }
        var zipBytes = zipStream.ToArray();
        const string asset = "StrongholdAssistant-v99.0.0-win-x64.zip";
        var hash = wrongHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/";
        var release = Encoding.UTF8.GetBytes($$"""
            {"tag_name":"v99.0.0","html_url":"https://example.org/releases/v99.0.0","body":"isolated test","assets":[
              {"name":"{{asset}}","browser_download_url":"{{url}}package"},
              {"name":"SHA256SUMS.txt","browser_download_url":"{{url}}checksums"}]}
            """);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var serving = Task.Run(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                using var client = await server.AcceptTcpClientAsync(timeout.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = (await reader.ReadLineAsync(timeout.Token))!.Split(' ')[1];
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
                var body = request switch
                {
                    "/" => release,
                    "/package" => zipBytes,
                    "/checksums" => Encoding.ASCII.GetBytes($"{hash}  {asset}\n"),
                    _ => throw new InvalidOperationException(request),
                };
                var contentType = request == "/" ? "application/json" : "application/octet-stream";
                // 不发送包的 Content-Length，同时覆盖未知下载长度的进度路径。
                var length = request == "/package" ? "" : $"Content-Length: {body.Length}\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\n{length}Connection: close\r\n\r\n"), timeout.Token);
                await stream.WriteAsync(body, timeout.Token);
            }
        });
        string? job = null;
        var beforeJobs = Directory.GetDirectories(Path.GetTempPath(), "StrongholdAssistant-Update-*").ToHashSet();
        try
        {
            var service = new UpdateService(new Version(0, 1, 0), feedUrl: url);
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"),
                Path.Combine(Path.GetTempPath(), "StrongholdAssistant-BridgeTest-" + Guid.NewGuid().ToString("N")));
            api.Updater = service;
            var events = new List<JsonElement>();
            api.Changed += (name, value) => { if (name == "update") events.Add(JsonSerializer.SerializeToElement(value)); };
            var status = JsonSerializer.SerializeToElement(await api.CheckUpdateAsync());
            status.GetProperty("state").GetString().Should().Be("available");
            status.GetProperty("notesUrl").GetString().Should().Be("https://example.org/releases/v99.0.0");
            await api.ApplyUpdateAsync();
            if (wrongHash)
            {
                events.Last().GetProperty("state").GetString().Should().Be("failed");
                api.UpdatePrepared.Should().BeFalse();
                service.PendingScriptPath.Should().BeNull();
                service.LastError.Should().Contain("校验不通过");
            }
            else if (wrapped)
            {
                events.Last().GetProperty("state").GetString().Should().Be("failed");
                api.UpdatePrepared.Should().BeFalse();
                service.PendingScriptPath.Should().BeNull();
                service.LastError.Should().Contain("缺少主程序");
            }
            else
            {
                events.Last().GetProperty("state").GetString().Should().Be("ready");
                api.UpdatePrepared.Should().BeTrue();
                job = Path.GetDirectoryName(service.PendingScriptPath)!;
                File.Exists(service.PendingScriptPath).Should().BeTrue();
                Directory.Exists(service.PendingStagingDir).Should().BeFalse("preparation must not replace the running installation");
                var script = await File.ReadAllTextAsync(service.PendingScriptPath!);
                script.Should().Contain("$startupTimeout = 60").And.Contain("STRONGHOLD_UPDATE_STARTUP_RESULT");
                var source = Path.Combine(job, "extracted");
                File.Exists(Path.Combine(source, "StrongholdAssistant.exe")).Should().BeTrue();
                File.ReadAllBytes(service.PendingScriptPath!).Take(3).Should().Equal(new byte[] { 0xef, 0xbb, 0xbf });
            }
            await serving;
        }
        finally
        {
            timeout.Cancel();
            server.Stop();
            foreach (var directory in Directory.GetDirectories(Path.GetTempPath(), "StrongholdAssistant-Update-*").Except(beforeJobs))
            {
                Path.GetDirectoryName(Path.GetFullPath(directory))!.TrimEnd('\\')
                    .Should().Be(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\'));
                Directory.Delete(directory, true);
            }
        }
    }
}
