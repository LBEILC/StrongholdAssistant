using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StrongholdAssistant.Host;

public sealed record MarkerStyle(bool Show, string Color);

[JsonConverter(typeof(MarkerStylesConverter))]
public sealed record MarkerStyles
{
    public MarkerStyle Key { get; init; } = new(true, "#F3B322");
    public MarkerStyle Tracked { get; init; } = new(true, "#3CF0C2");
    public MarkerStyle Pickable { get; init; } = new(true, "#A98BFF");
    public bool Labels { get; init; } = true;

    public static MarkerStyles Load(JsonNode? node)
    {
        var defaults = new MarkerStyles();
        if (node is not JsonObject json) return defaults;
        MarkerStyle Read(string name, MarkerStyle fallback)
        {
            if (json[name] is not JsonObject style) return fallback;
            return new(style["show"] is JsonValue show && show.TryGetValue<bool>(out var visible) ? visible : fallback.Show,
                style["color"] is JsonValue color && color.TryGetValue<string>(out var text)
                    && Regex.IsMatch(text, @"\A#[0-9a-fA-F]{6}\z") ? text : fallback.Color);
        }
        return new()
        {
            Key = Read("key", defaults.Key), Tracked = Read("tracked", defaults.Tracked),
            Pickable = Read("pickable", defaults.Pickable),
            Labels = json["labels"] is JsonValue labels && labels.TryGetValue<bool>(out var value) ? value : true
        };
    }
}

// 设置文件、备份和消息桥共用同一套缺项补全规则。
public sealed class MarkerStylesConverter : JsonConverter<MarkerStyles>
{
    public override bool HandleNull => true;
    public override MarkerStyles Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => MarkerStyles.Load(JsonNode.Parse(ref reader));

    public override void Write(Utf8JsonWriter writer, MarkerStyles value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new { value.Key, value.Tracked, value.Pickable, value.Labels }, options);
}
