using ASPAssistant.Core.Data;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace StrongholdAssistant.Host;

internal sealed record AssistantProfile(string Root, string? Name)
{
    internal const string TestRootVariable = "STRONGHOLD_STARTUP_CHECK_ROOT";
    public static AssistantProfile Current { get; set; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StrongholdAssistant"), null);
    public string DirectoryPath => Name is null ? Root : Path.Combine(Root, "profiles", Name);
    public string KernelName(string purpose) => @"Local\StrongholdAssistant" + purpose
        + (Name is null ? "" : ".Profile." + Name.ToUpperInvariant());

    private static bool Valid(string? name) => name is not null && Regex.IsMatch(name, @"\A[A-Za-z0-9-]{1,24}\z")
        && !Regex.IsMatch(name, @"\A(?:CON|AUX|NUL|PRN|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public AssistantProfile Named(string? name) => Valid(name) ? this with { Name = name!.ToLowerInvariant() }
        : throw new ArgumentException("配置名限 1 至 24 个英文字母、数字或短横线，请避开系统保留名。");

    public string[] List()
    {
        var parent = Path.Combine(Root, "profiles");
        return Directory.Exists(parent) ? Directory.EnumerateDirectories(parent).Select(Path.GetFileName)
            .OfType<string>().Where(Valid).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    }

    public void Create(bool allowExisting)
    {
        if (Name is null || allowExisting && Directory.Exists(DirectoryPath)) return;
        var parent = Path.Combine(Root, "profiles");
        var staging = Path.Combine(parent, "." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var source = Path.Combine(Root, "settings.json");
            var settings = UserJsonFile.Read(source, json => System.Text.Json.JsonSerializer.Deserialize<JsonObject>(json)) ?? new();
            foreach (var key in new[] { "toggleHotkey", "refreshKey", "fieldHotkey" }) settings[key] = "";
            if (settings["routes"] is not JsonObject) settings["routes"] = new JsonObject();
            settings["routes"]!["hotkey"] = "";
            File.WriteAllText(Path.Combine(staging, "settings.json"), settings.ToJsonString());
            try { Directory.Move(staging, DirectoryPath); }
            catch (IOException) when (Directory.Exists(DirectoryPath))
            {
                if (!allowExisting) throw new ArgumentException("这个配置名已经存在。");
            }
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    public void Launch(string? name)
    {
        var target = name is null ? this with { Name = null } : Named(name);
        if (name is not null && !Directory.Exists(target.DirectoryPath)) throw new ArgumentException("这个配置不存在。");
        var start = new ProcessStartInfo(ASPAssistant.Core.PackageLayout.Launcher) { UseShellExecute = false };
        if (name is not null) { start.ArgumentList.Add("--profile"); start.ArgumentList.Add(target.Name!); }
        else start.ArgumentList.Add("--default-profile");
        if (Environment.GetCommandLineArgs().Contains("--no-game"))
        {
            start.ArgumentList.Add("--no-game");
            start.Environment[TestRootVariable] = Root;
        }
        using var process = Process.Start(start);
    }
}
