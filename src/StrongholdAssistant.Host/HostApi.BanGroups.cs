using ASPAssistant.Core.Services;

namespace StrongholdAssistant.Host;

public sealed partial class HostApi
{
    private sealed class ObservedBanSlot
    {
        public string? Name;
        public int Tier;
        public double Score;
        public bool Inferred;
        public string[] Candidates = [];
        public HashSet<string> Rejected = [];
    }

    private sealed class ObservedBanGroup
    {
        public SortedDictionary<int, ObservedBanSlot> Slots = [];
        public int? End;
    }

    private readonly Dictionary<string, ObservedBanGroup> _banGroups = [];

    private void MergeBanGroups(BanAvatarResult result)
    {
        foreach (var visible in result.Slots.GroupBy(s => s.Covenant))
        {
            string? covenant = visible.Key;
            int offset = 0;
            if (covenant is null)
            {
                // 标题不在画面内，只接受已知名字给出的一致行偏移。
                HashSet<(string Covenant, int Offset)>? anchors = null;
                foreach (var slot in visible.Where(s => s.Name != null))
                {
                    var possible = (from existing in _banGroups
                        from known in existing.Value.Slots
                        where known.Value.Name == slot.Name && !known.Value.Inferred
                        let shift = known.Key - slot.VisibleIndex
                        where shift % 7 == 0
                        select (existing.Key, shift)).ToHashSet();
                    if (possible.Count == 0) continue;
                    if (anchors is null) anchors = possible;
                    else anchors.IntersectWith(possible);
                }
                if (anchors?.Count != 1)
                {
                    _avatarBans.UnionWith(visible.Select(s => s.Name).OfType<string>());
                    continue;
                }
                (covenant, offset) = anchors.Single();
            }
            if (!_banGroups.TryGetValue(covenant, out var target))
                _banGroups.Add(covenant, target = new());
            foreach (var match in visible)
            {
                int index = match.SlotIndex ?? match.VisibleIndex + offset;
                if (index < 0) continue;
                if (!target.Slots.TryGetValue(index, out var slot))
                    target.Slots.Add(index, slot = new() { Tier = match.Tier });
                if (match.Name is { } name && !slot.Rejected.Contains(name)
                    && (slot.Name is null || slot.Inferred || match.Score > slot.Score))
                {
                    slot.Name = name;
                    slot.Tier = match.Tier;
                    slot.Score = match.Score;
                    slot.Inferred = false;
                }
                if (match.EndsGroup) target.End = index + 1;
            }
        }
    }

    private void SetBanGroupOperator(string name, bool banned)
    {
        foreach (var group in _banGroups.Values)
        {
            var named = group.Slots.Values.Where(s => s.Name == name).ToArray();
            if (banned)
            {
                var slot = named.FirstOrDefault() ?? group.Slots.Values.FirstOrDefault(s => s.Name == null && s.Candidates.Contains(name));
                if (slot is null) continue;
                slot.Name = name;
                slot.Inferred = false;
                slot.Score = double.PositiveInfinity;
            }
            else foreach (var slot in named)
            {
                slot.Name = null;
                slot.Inferred = false;
                slot.Score = 0;
                slot.Rejected.Add(name);
            }
        }
    }

    private BanGroup[] RecomputeBanGroups()
    {
        var result = new List<BanGroup>();
        foreach (var (covenant, group) in _banGroups)
        {
            var ordered = _operators.Where(o => o.Obtain is null
                    && (o.CoreCovenants.Contains(covenant) || o.AdditionalCovenants.Contains(covenant)))
                .OrderByDescending(o => o.Tier).ToArray();
            var ranks = ordered.Select((o, i) => (o.Name, i)).ToDictionary(p => p.Name, p => p.i);
            foreach (var slot in group.Slots.Values)
            {
                if (slot.Inferred) slot.Name = null;
                slot.Inferred = false;
                slot.Candidates = [];
            }
            bool changed;
            do
            {
                changed = false;
                foreach (var (index, slot) in group.Slots.Where(p => p.Value.Name is null))
                {
                    var used = group.Slots.Values.Select(s => s.Name).OfType<string>().ToHashSet();
                    // 完整样图核对通过：星级降序，同星级按 operators.json 顺序。
                    var before = group.Slots.LastOrDefault(p => p.Key < index && p.Value.Name != null).Value?.Name;
                    var after = group.Slots.FirstOrDefault(p => p.Key > index && p.Value.Name != null).Value?.Name;
                    var candidates = ordered.Where(o => o.Tier == slot.Tier && !used.Contains(o.Name) && !slot.Rejected.Contains(o.Name)
                        && (before is null || ranks[o.Name] > ranks[before])
                        && (after is null || ranks[o.Name] < ranks[after])).Select(o => o.Name).ToArray();
                    slot.Candidates = candidates.Take(6).ToArray();
                    if (candidates.Length != 1) continue;
                    slot.Name = candidates[0];
                    slot.Inferred = true;
                    changed = true;
                }
            } while (changed);
            var slots = new List<BanSlot>();
            for (int i = 0; group.Slots.TryGetValue(i, out var slot); i++)
                slots.Add(new(slot.Name, slot.Tier, slot.Candidates, slot.Inferred ? true : null));
            result.Add(new(_ids[covenant], slots.ToArray(), group.End == slots.Count));
        }
        return result.ToArray();
    }
}
