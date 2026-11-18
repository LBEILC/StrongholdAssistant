using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed partial class FieldTracker
{
    private Func<string, bool>? _usesPrototype;
    private Func<FieldSpritePatch, int, string[], SpriteCandidate[]>? _unknownPrototype;

    public void ConfigurePrototypes(Func<string, bool> usesPrototype,
        Func<FieldSpritePatch, int, string[], SpriteCandidate[]> unknownPrototype)
    { _usesPrototype = usesPrototype; _unknownPrototype = unknownPrototype; }
}
