using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StrongholdAssistant.Cli;

internal static class Program
{
    private const string Offline = "助手没开，或设置里的本机接口没打开";
    private sealed record Endpoint(int Port, string Token);

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        return await RunAsync(args, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StrongholdAssistant"), Console.Out, Console.Error);
    }

    internal static async Task<int> RunAsync(string[] args, string root, TextWriter output, TextWriter error)
    {
        try
        {
            var parts = args.ToList();
            var profile = Command.Option(parts, "--profile") ?? "default";
            if (string.IsNullOrWhiteSpace(profile) || profile != Path.GetFileName(profile) || profile.EndsWith('.') ||
                profile.EndsWith(' ') || profile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("配置名无效");
            if (parts.Remove("--help"))
            {
                if (parts.Count > 1) throw new ArgumentException("用法：sa <命令> --help");
                await output.WriteLineAsync(Command.Help(parts.FirstOrDefault()));
                return 0;
            }
            var command = Command.Parse(parts);
            var directory = profile == "default" ? root : Path.Combine(root, "profiles", profile);
            Endpoint? endpoint;
            try
            {
                endpoint = JsonSerializer.Deserialize<Endpoint>(await File.ReadAllBytesAsync(Path.Combine(directory, "api.json")),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (Exception e) when (e is IOException or JsonException) { endpoint = null; }
            if (endpoint == null || endpoint.Port is < 1 or > 65535 || string.IsNullOrEmpty(endpoint.Token))
                throw new HttpRequestException(Offline);

            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
            {
                BaseAddress = new Uri($"http://127.0.0.1:{endpoint.Port}/v1/"),
                Timeout = TimeSpan.FromSeconds(command.Path.StartsWith("turn?") ? 615 : 135)
            };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
            while (true)
            {
                using var request = new HttpRequestMessage(new HttpMethod(command.Method), command.Path);
                if (command.Body != null)
                    request.Content = new StringContent(JsonSerializer.Serialize(command.Body), Encoding.UTF8, "application/json");
                using var response = await http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var message = await response.Content.ReadAsStringAsync();
                    using var problem = JsonDocument.Parse(message);
                    await error.WriteLineAsync((int)response.StatusCode == 422 ? message : problem.RootElement.GetProperty("error").GetString());
                    return 1;
                }
                if (command.FileName != null)
                {
                    await File.WriteAllBytesAsync(command.FileName, await response.Content.ReadAsByteArrayAsync());
                    return 0;
                }
                string json = await response.Content.ReadAsStringAsync();
                using var data = JsonDocument.Parse(json);
                var job = data.RootElement;
                if (job.ValueKind == JsonValueKind.Object && job.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String &&
                    job.TryGetProperty("steps", out _))
                {
                    if (state.GetString() is "failed" or "stopped")
                    {
                        await error.WriteLineAsync(json);
                        return 1;
                    }
                    if (command.Wait && state.GetString() != "done")
                    {
                        command = new("GET", "jobs/" + job.GetProperty("id").GetString() + "?wait=120", Wait: true);
                        continue;
                    }
                }
                if (command.Wait && job.ValueKind == JsonValueKind.Null)
                {
                    await error.WriteLineAsync("还没有操作批次");
                    return 1;
                }
                await output.WriteLineAsync(json);
                return 0;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            await error.WriteLineAsync(Offline);
            return 2;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            await error.WriteLineAsync(e is JsonException ? "JSON 格式无效" : e.Message);
            return 2;
        }
    }
}

