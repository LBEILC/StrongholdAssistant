using System.Text.Json.Nodes;

namespace StrongholdAssistant.Host;

public sealed record RouteOverlaySettings(bool Enabled = false, string Display = "always",
    double PeekSeconds = 6, string Hotkey = "Alt+R", string Overlap = "cycle",
    double CycleSeconds = 2.2, string Fly = "main", string Side = "auto")
{
    public static RouteOverlaySettings Load(JsonNode? node)
    {
        var json = node as JsonObject;
        T Field<T>(string name, T fallback, Func<T, bool> valid) =>
            json?[name] is JsonValue value && value.TryGetValue<T>(out var result) && valid(result)
                ? result : fallback;
        return new(
            Field("enabled", false, _ => true),
            Field("display", "always", ValidDisplay),
            Field("peekSeconds", 6d, ValidPeek),
            Field("hotkey", "Alt+R", ValidHotkey),
            Field("overlap", "cycle", ValidOverlap),
            Field("cycleSeconds", 2.2, ValidCycle),
            Field("fly", "main", ValidFly),
            Field("side", "auto", ValidSide));
    }

    public void Validate()
    {
        if (!ValidDisplay(Display) || !ValidPeek(PeekSeconds) || !ValidHotkey(Hotkey)
            || !ValidOverlap(Overlap) || !ValidCycle(CycleSeconds) || !ValidFly(Fly) || !ValidSide(Side))
            // TODO(design): 确认非法路线设置的错误文案。
            throw new ArgumentException("敌人路线设置无效。");
    }

    private static bool ValidDisplay(string value) => value is "always" or "hold" or "peek";
    private static bool ValidPeek(double value) => value is >= 3 and <= 15;
    private static bool ValidHotkey(string value) => HotkeyParser.Parse(value) is not null;
    private static bool ValidOverlap(string value) => value is "cycle" or "all";
    private static bool ValidCycle(double value) => value is >= 1 and <= 5;
    private static bool ValidFly(string value) => value is "main" or "all" or "off";
    private static bool ValidSide(string value) => value is "auto" or "left" or "right";
}
