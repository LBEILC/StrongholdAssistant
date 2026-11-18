using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private IReadOnlyDictionary<string, string?> FieldAppearances() => SpriteMatcher.RosterAppearances(ReadRoster().Operators);
}
