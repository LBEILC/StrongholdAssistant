using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using StrongholdAssistant.Host;

// 直接调用宿主消息桥的数据入口，不另排顺序；临时用户目录不会影响助手配置。
var temporary = Path.Combine(Path.GetTempPath(), "StrongholdAssistant.Catalog", Guid.NewGuid().ToString("N"));
try
{
    var api = await HostApi.CreateAsync(args[0], temporary);
    var fields = new[] { ("covenants", "id"), ("operators", "name"), ("equipment", "name"), ("strategies", "id") };
    var minimal = fields.ToDictionary(f => f.Item1, f => api.Catalog.GetProperty(f.Item1).EnumerateArray()
        .Select(x =>
        {
            var entry = new Dictionary<string, object?> { [f.Item2] = x.GetProperty(f.Item2).GetString() };
            if (f.Item1 == "operators" && x.GetProperty("diy").GetBoolean()) entry["diy"] = true;
            if (f.Item1 == "operators")
                foreach (var key in new[] { "skills", "modules" })
                    entry[key] = x.TryGetProperty(key, out var items)
                        ? items.EnumerateArray().Select(item => new { id = item.GetProperty("id").GetString() }).ToArray()
                        : [];
            return entry;
        }).ToArray());
    var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(minimal, options) + "\n");
    // 模式白名单来自与宿主相同的当期补充数据，只收单人与联机模式。
    using var extra = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(args[0], "codex_extra.json")));
    var modes = extra.RootElement.GetProperty("modes").EnumerateArray().Select(x => x.GetProperty("id").GetString()!)
        .Where(id => id.StartsWith("mode_single_") || id.StartsWith("mode_multi_")).ToArray();
    await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "income-modes.json"),
        JsonSerializer.Serialize(modes, options) + "\n");
    // 验收可同时输出完整宿主 catalog，用同一分享码编码器比较指纹。
    if (args.Length > 2) await File.WriteAllTextAsync(args[2], api.Catalog.GetRawText());
    Console.WriteLine("已导出宿主 catalog：" + string.Join("，", minimal.Select(x => $"{x.Key}={x.Value.Length}")));
}
finally
{
    if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
}
