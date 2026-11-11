using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Models;

namespace StrongholdAssistant.Host;

public sealed record DockSettings(double Width = 380, SidePanelOffset? FollowOffsetDip = null, SidePanelOffset? OverlayOffsetDip = null);

// 同步合并写入，让窗口位置与界面设置共用文件而不相互覆盖。
public sealed class HostSettingsStore(string path)
{
    private JsonObject Read() => File.Exists(path)
        ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : new();

    public SettingsDto LoadSettings()
    {
        var json = Read();
        return new(json["gameTheme"]?.GetValue<string>() ?? "terminal",
            json["shopMarkers"]?.GetValue<bool>() ?? true,
            json["autoCheckUpdate"]?.GetValue<bool>() ?? true)
        {
            UiScale = JsonSerializer.SerializeToElement(json["uiScale"] ?? JsonValue.Create("auto")),
            FocusCovenants = json["focusCovenants"]?.Deserialize<string[]>() ?? [],
            FocusOperators = json["focusOperators"]?.Deserialize<string[]>() ?? []
        };
    }

    public DockSettings LoadDock() => Read().Deserialize<DockSettings>(HostApi.JsonOptions)!;

    public void SaveSettings(SettingsDto settings) => Merge(settings);
    public void SaveDock(DockSettings dock) => Merge(dock);

    private void Merge<T>(T value)
    {
        var json = Read();
        foreach (var property in JsonSerializer.SerializeToNode(value, HostApi.JsonOptions)!.AsObject())
            json[property.Key] = property.Value?.DeepClone();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToJsonString(HostApi.JsonOptions));
    }
}
