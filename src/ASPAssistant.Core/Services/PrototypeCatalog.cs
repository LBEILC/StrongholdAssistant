using System.Text.Json;

namespace ASPAssistant.Core.Services;

public sealed record PrototypeAppearance(string Name, string CharId, string Profession, string Branch, int[] Tiers);
public sealed record PrototypeSlot(string Name, int Tier, int Order, string Profession, string Branch,
    string? Prototype, string? PrototypeId, int SkillIndex, string[] Bonds);

/// <summary>赛季逐棋子替代关系；职业和分支由游戏表记录，不用职业近似推断。</summary>
public sealed class PrototypeCatalog
{
    public PrototypeAppearance[] Prototypes { get; }
    public PrototypeSlot[] Slots { get; }
    public PrototypeCatalog(string data)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "prototypes.json")));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Prototypes = doc.RootElement.GetProperty("prototypes").Deserialize<PrototypeAppearance[]>(options)!;
        Slots = doc.RootElement.GetProperty("slots").Deserialize<PrototypeSlot[]>(options)!;
    }

    public bool IsPrototype(string name) => Prototypes.Any(p => p.Name == name);
    public string[] Candidates(string prototype, int tier, IEnumerable<string> allowed) => Slots
        .Where(s => s.Prototype == prototype && s.Tier == tier && allowed.Contains(s.Name))
        .Select(s => s.Name).Distinct().ToArray();

    public PrototypeSlot? Resolve(string prototype, int? tier, int? order, IReadOnlyCollection<string> bonds)
    {
        // 图标或阶数缺读时不凭排序猜名字。重复外观必须仍能唯一落到原棋子。
        if (tier is null || bonds.Count == 0) return null;
        var candidates = Slots.Where(s => s.Prototype == prototype && s.Tier == tier
            && (order is null || s.Order == order) && bonds.All(s.Bonds.Contains)).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public SpriteCandidate[] MatchUnknown(SpriteMatcher matcher, ASPAssistant.Core.Models.FieldSpritePatch patch,
        int tier, string[] names, IReadOnlyDictionary<string, string?>? appearances = null)
    {
        var defaults = Prototypes.Where(p => p.Tiers.Contains(tier) && Candidates(p.Name, tier, names).Length > 0)
            .Select(p => p.Name).ToArray();
        var prototypeSkins = defaults.ToDictionary(n => n, n => (string?)("prototype:" + n));
        var ranked = matcher.MatchCellWidth(patch, tier, defaults, prototypeSkins).Candidates;
        if (ranked.Count == 0 || ranked[0].Score < .65) return [];
        var ordinary = matcher.MatchCellWidth(patch, tier, names, appearances).Candidates.FirstOrDefault()?.Score ?? -2;
        if (ranked[0].Score - ordinary < SpriteMatcher.AutoFillGap) return [];
        return ranked.Where(p => ranked[0].Score - p.Score < SpriteMatcher.AutoFillGap)
            .SelectMany(p => Candidates(p.Name, tier, names).Select(n => new SpriteCandidate(n, p.Score)))
            .DistinctBy(p => p.Name).ToArray();
    }
}
