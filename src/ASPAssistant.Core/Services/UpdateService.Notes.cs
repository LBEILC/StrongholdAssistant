using System.Net.Http;
using System.Text.Json;

namespace ASPAssistant.Core.Services;

public partial class UpdateService
{
    private async Task<JsonElement?> ReadReleaseNotesAsync(string? url, string version, CancellationToken ct)
    {
        if (url is null) return null;
        const int limit = 64 * 1024;
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            // 未提供 Content-Length 时也限制实际读取量。
            var bytes = new byte[limit + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), ct);
                if (read == 0) break;
                count += read;
            }
            if (count > limit) return null;
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            var notes = document.RootElement;
            if (!ValidReleaseNotes(notes, version)) return null;
            return notes.Clone();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or UriFormatException)
        {
            return null;
        }
    }

    private static bool ValidReleaseNotes(JsonElement notes, string version)
    {
        static bool Text(JsonElement value, string name) => value.TryGetProperty(name, out var field)
            && field.ValueKind == JsonValueKind.String;
        if (notes.ValueKind != JsonValueKind.Object || !Text(notes, "schema") || !Text(notes, "version")
            || notes.GetProperty("schema").GetString() != "sa-release-notes/1"
            || notes.GetProperty("version").GetString() != version || !Text(notes, "date")
            || !notes.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return false;
        return items.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object
            && Text(item, "kind") && item.GetProperty("kind").GetString() is "new" or "improved" or "fixed"
            && Text(item, "title") && Text(item, "detail")
            && (!item.TryGetProperty("highlight", out var highlight) || highlight.ValueKind is JsonValueKind.True or JsonValueKind.False));
    }
}
