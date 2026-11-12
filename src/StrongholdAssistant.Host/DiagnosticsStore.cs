using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StrongholdAssistant.Host;

public sealed class DiagnosticsStore(string directory)
{
    private readonly object _gate = new();
    public string SaveFailure(byte[]? screenshot, string reason, string? source, double? scale, string[] steps)
    {
        int? width = null, height = null;
        byte[]? png = null;
        if (screenshot is not null)
        {
            using var stream = new MemoryStream(screenshot);
            using var image = new Bitmap(stream);
            width = image.Width; height = image.Height;
            double ratio = Math.Min(1, 1920.0 / Math.Max(image.Width, image.Height));
            using var small = new Bitmap(image, Math.Max(1, (int)(image.Width * ratio)), Math.Max(1, (int)(image.Height * ratio)));
            using var encoded = new MemoryStream();
            small.Save(encoded, ImageFormat.Png);
            png = encoded.ToArray();
        }
        var details = $"画面: {width?.ToString() ?? "未知"}×{height?.ToString() ?? "未知"}; 来源: {source ?? "未连接"}; UI 缩放: {scale?.ToString("F3") ?? "未测得"}\n步骤: {reason}\n{string.Join('\n', steps)}";
        var message = Redact(reason + "\n" + details);
        lock (_gate)
        {
            var folder = Path.Combine(directory, "diagnostics");
            Directory.CreateDirectory(folder);
            var name = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}";
            if (png is not null) File.WriteAllBytes(Path.Combine(folder, name + ".png"), png);
            File.WriteAllText(Path.Combine(folder, name + ".json"), SanitizeJson(
                new { time = DateTimeOffset.UtcNow, reason, width, height, source, scale, steps }));
            foreach (var old in Directory.GetFiles(folder, "*.json").OrderDescending().Skip(10))
            {
                File.Delete(old);
                File.Delete(Path.ChangeExtension(old, ".png"));
            }
        }
        ASPAssistant.Core.AppLogger.Warn("Recognition", message);
        return message;
    }

    public void Export(string path, SettingsDto settings, Connection connection, object system)
    {
        lock (_gate)
        {
            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create);
            foreach (var folder in new[] { "logs", "diagnostics" })
            {
                var root = Path.Combine(directory, folder);
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.GetFiles(root).Where(f => folder == "logs"
                    ? Path.GetExtension(f) == ".log" : Path.GetExtension(f) is ".json" or ".png"))
                {
                    var entry = zip.CreateEntry(folder + "/" + Path.GetFileName(file));
                    using var output = entry.Open();
                    if (Path.GetExtension(file) == ".png")
                    {
                        using var input = File.OpenRead(file);
                        input.CopyTo(output);
                    }
                    else
                    {
                        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(input);
                        using var writer = new StreamWriter(output);
                        writer.Write(Redact(reader.ReadToEnd()));
                    }
                }
            }
            var all = JsonSerializer.SerializeToNode(settings, HostApi.JsonOptions)!.AsObject();
            // 白名单避免将未来增加的云端或账号字段带入诊断包。
            string[] allowed = ["gameTheme", "shopMarkers", "autoCheckUpdate", "updateSource", "uiScale", "alwaysOnTop", "gameSource",
                "followGame", "toggleHotkey", "refreshKey", "startupMode", "collapseMode", "focusCovenants", "focusOperators"];
            var safe = new JsonObject();
            foreach (var key in allowed) safe[key] = all[key]?.DeepClone();
            WriteJson(zip, "settings.json", safe);
            WriteJson(zip, "connection.json", connection);
            WriteJson(zip, "system.json", system);
        }
    }

    private static void WriteJson(ZipArchive zip, string name, object value)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(SanitizeJson(value));
    }
    private static string SanitizeJson(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, HostApi.JsonOptions)!;
        JsonNode? Clean(JsonNode? item)
        {
            if (item is JsonValue v && v.TryGetValue<string>(out var text)) return JsonValue.Create(Redact(text));
            if (item is JsonObject obj)
                foreach (var key in obj.Select(p => p.Key).ToArray()) obj[key] = Clean(obj[key]?.DeepClone());
            if (item is JsonArray array)
                for (int i = 0; i < array.Count; i++) array[i] = Clean(array[i]?.DeepClone());
            return item;
        }
        return Clean(node)!.ToJsonString(HostApi.JsonOptions);
    }
    public static string Redact(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        text = Regex.Replace(text, @"(?i)([a-z]:[\\/]+Users[\\/]+)[^\\/\r\n""]+", "$1[用户]");
        text = Regex.Replace(text, @"(?i)(/(?:home|Users)/)[^/\r\n""]+", "$1[用户]");
        text = Regex.Replace(text, @"(?i)(Bearer\s+)[^\s""\\,;]+", "$1[已隐藏]");
        return Regex.Replace(text, @"(?i)((?:[\w-]*(?:token|secret|password|credential|cdk|cookie|authorization|accesskey)[\w-]*|cred|sign)\s*""?\s*[:=]\s*""?)[^""\r\n,;&}]+", "$1[已隐藏]");
    }
}
