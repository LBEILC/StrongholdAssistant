using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

// 所有删除、覆盖都发生在新建的镜像目录中，校验失败不会写到当前安装。
internal static partial class UpdateImage
{
    private sealed record Changes(string[]? Added, string[]? Modified, string[]? Deleted,
        [property: JsonPropertyName("added_dir")] string[]? AddedDirs,
        [property: JsonPropertyName("deleted_dir")] string[]? DeletedDirs);

    private static string Relative(string name)
    {
        var parts = name.Split('/');
        if (parts.Any(p => p.Length == 0 || p.EndsWith('.') || p.EndsWith(' ')
            || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || DeviceName().IsMatch(p)))
            throw new InvalidDataException($"更新路径无效：{name}");
        return name;
    }

    [GeneratedRegex(@"^(?:con|prn|aux|nul|com[1-9¹²³]|lpt[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceName();

    private static void ReadTree(FileSystemInfo source, string? target)
    {
        if (source.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException($"更新路径包含链接：{source.Name}");
        if (source is DirectoryInfo dir)
        {
            if (target is not null) Directory.CreateDirectory(target);
            foreach (var child in dir.EnumerateFileSystemInfos())
                ReadTree(child, target is null ? null : Path.Combine(target, child.Name));
        }
        else if (target is not null) File.Copy(source.FullName, target);
    }

    internal static void Prepare(string package, string image, string installation, bool incremental, bool installed)
    {
        Directory.CreateDirectory(image);
        using var zip = ZipFile.OpenRead(package);
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var name = Relative(entry.FullName.TrimEnd('/'));
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 || !entries.TryAdd(name, entry))
                throw new InvalidDataException($"更新包包含链接或重名路径：{name}");
        }
        void Extract(string name)
        {
            if (!entries.TryGetValue(name, out var entry) || entry.FullName.EndsWith('/'))
                throw new InvalidDataException($"更新包缺少文件：{name}");
            var path = Path.Combine(image, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, overwrite: true);
        }
        // 全量更新也会清理旧备份。拒绝安装中的重解析点，避免清理链接目标。
        var baseDirectory = new DirectoryInfo(installation);
        if (baseDirectory.Exists)
        {
            if (baseDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("安装目录是链接");
            foreach (var child in baseDirectory.EnumerateFileSystemInfos())
                if (!installed || IsProductEntry(child.Name))
                    ReadTree(child, incremental ? Path.Combine(image, child.Name) : null);
        }

        if (!incremental)
        {
            if (entries.ContainsKey("changes.json")) throw new InvalidDataException("全量包包含增量清单");
            foreach (var (name, entry) in entries)
                if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(Path.Combine(image, name));
                else Extract(name);
            return;
        }

        if (!entries.TryGetValue("changes.json", out var manifest)) throw new InvalidDataException("增量包缺少 changes.json");
        Changes changes;
        try
        {
            using var stream = manifest.Open();
            changes = JsonSerializer.Deserialize<Changes>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("增量清单为空");
        }
        catch (JsonException ex) { throw new InvalidDataException("增量清单格式无效", ex); }

        foreach (var name in (changes.Added ?? []).Concat(changes.Modified ?? []).Concat(changes.Deleted ?? [])
            .Concat(changes.AddedDirs ?? []).Concat(changes.DeletedDirs ?? []))
        {
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException("增量清单包含空路径");
            Relative(name);
            if (installed && !IsProductEntry(name.Split('/')[0])) throw new InvalidDataException($"非产品路径：{name}");
        }
        foreach (var name in changes.Deleted ?? [])
        {
            var path = Path.Combine(image, name);
            if (File.Exists(path)) File.Delete(path);
        }
        foreach (var name in (changes.DeletedDirs ?? []).OrderByDescending(n => n.Count(c => c == '/')))
        {
            var path = Path.Combine(image, name);
            if (Directory.Exists(path)) Directory.Delete(path);
        }
        foreach (var name in changes.AddedDirs ?? []) Directory.CreateDirectory(Path.Combine(image, name));
        foreach (var name in (changes.Added ?? []).Concat(changes.Modified ?? [])) Extract(name);
        // 宿主退出后再探测待替换文件，避免把运行中的宿主 DLL 误判为占用。
        var affected = (changes.Deleted ?? []).Concat(changes.Modified ?? []).Distinct(StringComparer.OrdinalIgnoreCase);
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(image)!, "affected.txt"), affected);
    }

    internal static readonly string[] ProductEntries =
        ["app", "data", "ui", "StrongholdAssistant.exe", "LICENSE", "LICENSE.upstream", "NOTICE", "ASSETS.md", "runtimes", "plugins"];
    private static bool IsProductEntry(string name) => ProductEntries.Contains(name, StringComparer.OrdinalIgnoreCase);
}
