using System.IO.Pipes;
using System.Text;

namespace StrongholdAssistant.Host;

public sealed class DeepLinkChannel(string name) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();

    public static bool IsValid(string? url) => url is { Length: <= 2000 }
        && url.StartsWith("stronghold://", StringComparison.Ordinal);

    public async Task ListenAsync(Action<string> receive)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                var buffer = new char[2001];
                var count = 0;
                try
                {
                    while (count < buffer.Length)
                    {
                        var read = await reader.ReadAsync(buffer.AsMemory(count), _stop.Token);
                        if (read == 0) break;
                        count += read;
                    }
                    var url = new string(buffer, 0, count);
                    if (IsValid(url)) receive(url);
                }
                catch (IOException) { /* 发送进程提前断开时丢弃本次消息。 */ }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async Task SendAsync(string url)
    {
        if (!IsValid(url)) return;
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
        await writer.WriteAsync(url);
    }

    public void Dispose() => _stop.Cancel();
}
