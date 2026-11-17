namespace ASPAssistant.Core.Services;

public sealed partial class SpriteMatcher
{
    private const string PrototypePrefix = "prototype:";
    public static IReadOnlyDictionary<string, string?> RosterAppearances(IEnumerable<RosterOperator> operators) =>
        operators.ToDictionary(o => o.Name, o => o.Prototype is { } p ? PrototypePrefix + p : o.Skin, StringComparer.Ordinal);

    private static string AppearanceName(string name, IReadOnlyDictionary<string, string?>? skins) =>
        skins?.GetValueOrDefault(name) is { } value && value.StartsWith(PrototypePrefix, StringComparison.Ordinal)
            ? value[PrototypePrefix.Length..] : name == "Pith" ? "char_509_acast" : name;

    private static bool AllowsSkin(string template, string? skin) => skin is null
        || (skin.StartsWith(PrototypePrefix, StringComparison.Ordinal) ? template == "default" : template == "default" || template == skin);
}
