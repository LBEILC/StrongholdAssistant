using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ASPAssistant.Core.Control;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Placement;
using StrongholdAssistant.Cli;
using StrongholdAssistant.Host;

namespace StrongholdAssistant.Tests;

public class LocalApiTests
{
    [Fact]
    public async Task AuthPermissionDispatchAndWaitUseTheSameListener()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var api = await HostApi.CreateAsync(Path.Combine(AppContext.BaseDirectory, "data"), directory.FullName);
            var enteredWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ControlJob? submitted = null;
            ControlJob Run(GameAction[] actions, string? mode, string source)
            {
                Assert.Equal(new BuyAction(5), Assert.Single(actions));
                submitted = new("batch", source, mode!, "running", [], null, null, DateTimeOffset.UtcNow, null);
                api.RecordControlJob(submitted);
                return submitted;
            }
            using var server = new LocalHttpServer(directory.FullName, "default", (method, uri, body, token) =>
            {
                var task = api.RouteLocalAsync(method, uri, body, token, Run);
                if (uri.Query.Contains("wait")) enteredWait.TrySetResult();
                return task;
            });
            using var client = new HttpClient { BaseAddress = new Uri(server.Address) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/info")).StatusCode);
            var descriptor = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory.FullName, "api.json")));
            client.DefaultRequestHeaders.Authorization = new("Bearer", descriptor.RootElement.GetProperty("token").GetString());
            api.SaveSettings(api.Settings with { Control = new(Enabled: true, Consent: ControlSettings.ConsentVersion) });
            var request = new { actions = new[] { new { slot = 5, type = "buy" } }, mode = "dryRun" };
            // 明确给出字节长度，和实际命令行一样。
            Task<HttpResponseMessage> Submit() => client.PostAsync("/v1/actions",
                new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Forbidden, (await Submit()).StatusCode);
            Assert.Null(submitted);
            api.SaveSettings(api.Settings with { Control = api.Settings.Control with { External = true } });
            var result = await Submit();
            result.EnsureSuccessStatusCode();
            Assert.Equal("api", submitted!.Source);
            var waiting = client.GetFromJsonAsync<ControlJob>("/v1/jobs/current?wait=120");
            await enteredWait.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(waiting.IsCompleted);
            api.RecordControlJob(submitted with { State = "done", EndedAt = DateTimeOffset.UtcNow });
            Assert.Equal("done", (await waiting.WaitAsync(TimeSpan.FromSeconds(5)))!.State);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void StalePlacementIsRejectedBeforeCompilingMoves()
    {
        var plan = new PlacementResult("old", "", "", [], 0, [], null, null, []);
        Assert.Equal("场上变了，等重新算完再摆", Assert.Throws<InvalidOperationException>(() =>
            HostApi.PlacementMoves(plan, new FieldState(null, 1, "new", [], []))).Message);
    }

    [Fact]
    public async Task CliConvertsCellsAndSlotsAndReportsExitCodes()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var sent = new List<JsonElement>();
            bool fail = false;
            using (var server = new LocalHttpServer(directory.FullName, "default", (_, _, bytes, _) =>
            {
                sent.Add(JsonSerializer.Deserialize<JsonElement>(bytes).GetProperty("actions")[0]);
                return Task.FromResult<object?>(new { id = "batch", state = fail ? "failed" : "done", steps = Array.Empty<object>() });
            }))
            {
                var output = new StringWriter();
                var error = new StringWriter();
                Task<int> Cli(params string[] args) => Program.RunAsync(args, directory.FullName, output, error);
                Assert.Equal(0, await Cli("buy", "6", "--wait"));
                Assert.Equal(5, sent[0].GetProperty("slot").GetInt32());
                Assert.Equal(0, await Cli("move", "bench:3", "D7"));
                Assert.Equal("""{"zone":"bench","col":3,"row":0}""", sent[1].GetProperty("from").GetRawText());
                Assert.Equal("""{"zone":"board","col":4,"row":3}""", sent[1].GetProperty("to").GetRawText());
                fail = true;
                Assert.Equal(1, await Cli("refresh", "--wait"));
                Assert.Equal(2, await Cli("buy", "0"));
            }
            Assert.Equal(2, await Program.RunAsync(["info"], directory.FullName, TextWriter.Null, TextWriter.Null));
        }
        finally { directory.Delete(true); }
    }
}

