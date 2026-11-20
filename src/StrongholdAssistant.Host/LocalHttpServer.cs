using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ASPAssistant.Core;

namespace StrongholdAssistant.Host;

internal sealed class LocalApiError(int status, string message, int? index = null) : Exception(message)
{
    public int? Index => index;
    public int Status => status;
}

// 每个连接只处理一个 HTTP/1.1 请求。直接绑定回环套接字，无需注册 HTTP.sys URL。
internal sealed class LocalHttpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private readonly string _file;
    private readonly Func<string, Uri, byte[], CancellationToken, Task<object?>> _route;
    internal string Address { get; }

    internal LocalHttpServer(string directory, string profile,
        Func<string, Uri, byte[], CancellationToken, Task<object?>> route)
    {
        _route = route;
        _file = Path.Combine(directory, "api.json");
        _listener.Start();
        try
        {
            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Address = $"http://127.0.0.1:{port}";
            File.WriteAllText(_file + ".tmp", JsonSerializer.Serialize(new { port, token = _token, pid = Environment.ProcessId, profile }));
            File.Move(_file + ".tmp", _file, true);
            _ = AcceptAsync(_stop.Token);
        }
        catch { _listener.Stop(); throw; }
    }

    private async Task AcceptAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = ServeAsync(client, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken token)
    {
        using var connection = client;
        using var stream = client.GetStream();
        var start = Stopwatch.GetTimestamp();
        string method = "-", path = "-";
        int status = 200;
        try
        {
            object? result;
            try
            {
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);
                reading.CancelAfter(TimeSpan.FromSeconds(30));
                byte[] buffer = new byte[16384];
                int used = 0, end;
                while ((end = buffer.AsSpan(0, used).IndexOf("\r\n\r\n"u8)) < 0)
                {
                    if (used == buffer.Length) throw new LocalApiError(431, "请求头太长");
                    int n = await stream.ReadAsync(buffer.AsMemory(used), reading.Token);
                    if (n == 0) return;
                    used += n;
                }
                var lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
                var first = lines[0].Split(' ');
                if (first.Length != 3 || !first[1].StartsWith('/') || !Uri.TryCreate(Address + first[1], UriKind.Absolute, out var uri))
                    throw new LocalApiError(400, "请求路径无效");
                method = first[0];
                path = uri.AbsolutePath;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines.Skip(1))
                {
                    int i = line.IndexOf(':');
                    if (i < 1 || !headers.TryAdd(line[..i], line[(i + 1)..].Trim()))
                        throw new LocalApiError(400, "请求头无效");
                }
                if (headers.GetValueOrDefault("Authorization") != "Bearer " + _token)
                    throw new LocalApiError(401, "本机接口口令不正确");
                if (headers.ContainsKey("Transfer-Encoding"))
                    throw new LocalApiError(400, "请使用 Content-Length 指定请求体长度");
                if (!int.TryParse(headers.GetValueOrDefault("Content-Length", "0"), out var length) || length < 0 || length > 1024 * 1024)
                    throw new LocalApiError(400, "请求体长度无效或超过 1 MiB");
                byte[] body = new byte[length];
                int available = Math.Min(length, used - end - 4);
                buffer.AsSpan(end + 4, available).CopyTo(body);
                await stream.ReadExactlyAsync(body.AsMemory(available), reading.Token);
                token.ThrowIfCancellationRequested();
                result = await _route(method, uri, body, token);
            }
            catch (Exception e) when (e is not (OperationCanceledException or IOException or SocketException))
            {
                status = e switch
                {
                    LocalApiError error => error.Status,
                    JsonException or ArgumentException => 400,
                    InvalidOperationException or RecognitionException => 409,
                    _ => 500
                };
                string message = e switch
                {
                    JsonException => "请求 JSON 无效，请检查字段和类型",
                    LocalApiError or InvalidOperationException or RecognitionException or ArgumentException => e.Message,
                    _ => "助手处理请求失败"
                };
                result = e is LocalApiError { Index: int index } ? new { error = message, index } : new { error = message };
            }
            var png = result as byte[];
            byte[] bytes = png ?? JsonSerializer.SerializeToUtf8Bytes(result, HostApi.JsonOptions);
            var source = path == "/v1/schedule" && status == 200 ? "X-Data-Source: inferred\r\n" : "";
            var header = $"HTTP/1.1 {status} {((HttpStatusCode)status)}\r\n{source}Content-Type: {(png is null ? "application/json; charset=utf-8" : "image/png")}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), token);
            await stream.WriteAsync(bytes, token);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException) { status = 499; }
        finally { AppLogger.Info("LocalApi", $"{method} {path} {status} {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0}ms"); }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        File.Delete(_file);
        _stop.Dispose();
    }
}

