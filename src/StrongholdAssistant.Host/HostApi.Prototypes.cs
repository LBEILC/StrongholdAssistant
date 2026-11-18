using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private IReadOnlyDictionary<string, string?> FieldAppearances() => SpriteMatcher.RosterAppearances(ReadRoster().Operators);

    private void InitializeFieldPrototypes(string data)
    {
        var catalog = new PrototypeCatalog(data);
        _fieldTracker.ConfigurePrototypes(name => ReadRoster().Operators.Any(o => o.Name == name && o.Prototype is not null),
            (patch, tier, names) =>
            {
                if (ReadRoster().ReadAt is not null) return [];
                return catalog.MatchUnknown(_fieldSprites!, patch, tier, names, FieldAppearances());
            });
    }

}
