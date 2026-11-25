using System.Text.Json;
using ASPAssistant.Core.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace StrongholdAssistant.Host;

internal sealed record ShopAction(string Kind, int? ShopSlot = null, string? TargetId = null);
internal sealed record ShopPreference(double TopShare, double? LogitGap, int LegalCount);
internal sealed record ShopAdvice(long StateRevision, string ModelId, string Status, string[] MissingInputs,
    ShopAction? Action, ShopPreference? Preference);

internal sealed record ShopContext(int? InitialHp, string? Main, string? Second,
    int Actions = 0, int Buys = 0, int Refreshes = 0, int? DeployCap = null, int?[]? Losses = null, int?[]? Leaks = null,
    int? EquipmentSlot = null);

internal sealed class ShopObservation
{
    public HashSet<string> Missing { get; } = ["hand", "offer", "pendingFunds", "command", "preview", "roundActionHistory", "disabledCovenants"];
    public List<ShopAction> Actions { get; } = [];
    public List<NamedOnnxValue> Inputs { get; } = [];
    public bool Available { get; set; } = true;

    public T[] Tensor<T>(string name, T fill, params int[] shape)
    {
        var data = new T[shape.Aggregate(1, (a, b) => a * b)];
        Array.Fill(data, fill);
        Inputs.Add(NamedOnnxValue.CreateFromTensor(name, new DenseTensor<T>(data, shape)));
        return data;
    }
}

internal sealed class ShopEncoder
{
    private sealed record Operator(string Id, string? CharId, int[] Covenants);
    private sealed record Bond(string Id, string ProductId, int[] Thresholds, bool CountsHand, bool CountsGoldenOnly);
    private sealed record Slots(int Chess);
    private sealed record Mode(int MaxLevel, string[] Disabled, Dictionary<int, Slots> Shop);
    private sealed record Spec(Operator[] Operators, Bond[] Covenants, JsonElement[] Items, string[] EnemyTypes,
        string[] BossKeys, string[] Maps, string[] Kinds, Dictionary<string, int> InitialHp, Dictionary<string, Mode> Modes);
    private readonly Spec _spec;
    private readonly Dictionary<string, int> _ops;
    private readonly Dictionary<string, int> _items;
    public ShopEncoder(string directory)
    {
        _spec = JsonSerializer.Deserialize<Spec>(File.ReadAllText(Path.Combine(directory, "catalog.json")), HostApi.JsonOptions)!;
        _ops = _spec.Operators.Select((o, i) => (o, i)).Where(x => x.o.CharId != null).ToDictionary(x => x.o.CharId!, x => x.i);
        _items = _spec.Items.Select((o, i) => (o, i)).ToDictionary(x => x.o.GetProperty("id").GetString()!, x => x.i);
    }

    private int OperatorIndex(MatchState state, string? id, int? tier)
    {
        if (id == null) return -1;
        var choices = state.CustomPicks.Value?.Where(p => !tier.HasValue || p.Key == tier)
            .SelectMany(p => p.Value.Select((op, i) => (op, key: $"chess_char_{p.Key}_diy{i + 1}_a")))
            .Where(p => p.op.Id.Value == id).ToArray();
        if (choices is { Length: 1 }) return Array.FindIndex(_spec.Operators, o => o.Id == choices[0].key);
        return choices is { Length: > 1 } ? -1 : _ops.GetValueOrDefault(id, -1);
    }

    public ShopObservation Encode(MatchState s, ShopContext ctx)
    {
        var o = new ShopObservation();
        void Block(string name) { o.Available = false; o.Missing.Add(name); }
        if (!_spec.Modes.TryGetValue(s.ModeId.Value ?? "", out var mode)) { Block("mode"); return o; }
        if (s.GameDataVersion.Value != "act3autochess@V3_1") Block("gameDataVersion");
        var round = s.Round.Value;
        int money = s.Money.Value ?? -1, level = s.Level.Value ?? 0;
        if (round is not > 0 || money < 0 || !mode.Shop.TryGetValue(level, out _)) Block("economy");
        int hp0 = ctx.InitialHp ?? _spec.InitialHp.GetValueOrDefault(s.StrategyId.Value ?? "");
        if (ctx.InitialHp == null) o.Missing.Add("initialHp");
        if (hp0 <= 0 || s.Hp.Value == null) Block("hp");
        var units = s.Units.Value ?? [];
        if (s.Units.Value == null || units.Length > 20) Block("units");
        var ids = units.Select(u => OperatorIndex(s, u.Operator.Value?.Id.Value, u.Tier.Value)).ToArray();
        if (ids.Contains(-1) || units.Any(u => u.Zone.Value is not ("board" or "bench"))) Block("unitIdentity");
        int board = units.Count(u => u.Zone.Value == "board");
        var g = o.Tensor("g", 0f, 1, 17);
        g[0] = (round ?? 0) / 15f; g[1] = (s.Hp.Value ?? 0) / (float)Math.Max(1, hp0);
        g[2] = money / 10f; g[3] = level / 6f; g[4] = Math.Max(0, s.UpgradeCost.Value ?? 0) / 10f;
        g[5] = (ctx.DeployCap ?? 8) - 8; g[6] = units.Length / 18f;
        if (ctx.DeployCap == null) o.Missing.Add("deployCap");
        g[8] = ctx.Actions / 20f; g[9] = ctx.Buys / 5f; g[10] = ctx.Refreshes / 5f;
        g[11] = s.RefreshCost.Value == 0 ? 1f / 3 : 0; g[12] = board / 8f;
        if (s.RefreshCost.Value == 0) o.Missing.Add("freeRefreshCount");
        g[16] = s.Frozen.Value == true ? 1 : 0;
        var op = o.Tensor("unit_op", -1L, 1, 20);
        var uf = o.Tensor("unit_f", 0f, 1, 20, 6);
        var equipment = o.Tensor("unit_items", -1L, 1, 20, 2);
        for (int i = 0; i < Math.Min(20, units.Length); i++)
        {
            var u = units[i]; op[i] = ids[i];
            var worn = u.Equipment.Value ?? [];
            if (u.Equipment.Value == null) o.Missing.Add("equipment");
            if (u.Elite.Value == null) o.Missing.Add("elite");
            uf[i * 6] = u.Elite.Value == true ? 1 : 0;
            uf[i * 6 + 1] = worn.Length / 2f; uf[i * 6 + 2] = ids.Count(x => x == ids[i]) / 3f;
            uf[i * 6 + 3] = 1; uf[i * 6 + 4] = u.Zone.Value == "board" ? 1 : 0;
            for (int j = 0; j < Math.Min(2, worn.Length); j++)
            {
                equipment[i * 2 + j] = _items.GetValueOrDefault(worn[j].Id.Value ?? "", -1);
                if (equipment[i * 2 + j] < 0) o.Missing.Add("equipment");
            }
        }
        o.Tensor("offer", -1L, 1, 3); o.Tensor("hand", -1L, 1, 5);
        o.Tensor("hand_adv", 0f, 1, 5); o.Tensor("preview", 0f, 1, 12);
        o.Tensor("level", 0f, 1, 1)[0] = int.Parse(s.ModeId.Value![12..]) / 20f;
        var run = o.Tensor("run", 0f, 1, _spec.EnemyTypes.Length + _spec.BossKeys.Length + _spec.Maps.Length + 10);
        int offset = 0;
        void OneHot(string name, string[] catalog, string? value)
        {
            int i = Array.IndexOf(catalog, value);
            if (i >= 0) run[offset + i] = 1; else o.Missing.Add(name);
            offset += catalog.Length;
        }
        OneHot("strategy", _spec.EnemyTypes, s.StrategyId.Value);
        OneHot("boss", _spec.BossKeys, s.BossId.Value);
        OneHot("map", _spec.Maps, "act3autochess_" + s.MapId.Value);
        for (int i = 0; i < 3 && i + 1 < round; i++)
        {
            if (ctx.Losses?.ElementAtOrDefault(i) is int loss) run[offset + 2 * i] = loss / (float)Math.Max(1, hp0);
            else o.Missing.Add("battleHpHistory");
            if (ctx.Leaks?.ElementAtOrDefault(i) is int leaks) run[offset + 2 * i + 1] = Math.Min(1, leaks / 20f);
            else o.Missing.Add("leakHistory");
        }
        int n = _spec.Covenants.Length, activeLayers = 0;
        var cov = o.Tensor("cov", 0f, 1, n, 5);
        var planCov = o.Tensor("plan_cov", 0f, 1, n, 1);
        var pg = o.Tensor("plan_g", 0f, 1, 4);
        var want = o.Tensor("want", 0f, 1, _spec.Operators.Length + 1, 2);
        var main = Array.FindIndex(_spec.Covenants, c => c.ProductId == ctx.Main);
        var second = Array.FindIndex(_spec.Covenants, c => c.ProductId == ctx.Second);
        if (main < 0) Block("targetCovenant");
        if (second < 0) o.Missing.Add("secondaryCovenant");
        if (s.StrategyId.Value is not (null or "band_amiya" or "band_takecare" or "band_bonus"))
            o.Missing.Add("strategyOutsideTrainingPool");
        for (int c = 0; c < n; c++)
        {
            var bond = _spec.Covenants[c];
            var read = s.Covenants.Value?.FirstOrDefault(b => b.Id.Value == bond.ProductId);
            var members = Enumerable.Range(0, units.Length).Where(i => ids[i] >= 0 && _spec.Operators[ids[i]].Covenants.Contains(c)).ToArray();
            int count = read?.Count.Value ?? members.Where(i => (units[i].Zone.Value == "board" || bond.CountsHand)
                && (!bond.CountsGoldenOnly || units[i].Elite.Value == true)).Select(i => ids[i]).Distinct().Count();
            if (read?.Count.Value == null) o.Missing.Add("covenantCounts");
            int layers = read?.Layers.Value ?? 0;
            if (read?.Layers.Value == null) o.Missing.Add("covenantLayers");
            bool disabled = mode.Disabled.Contains(bond.Id);
            int tier = read?.Level.Value ?? (disabled ? 0 : bond.Thresholds.Count(t => t <= count));
            if (read?.Level.Value == null) o.Missing.Add("covenantTiers");
            cov[c * 5] = Math.Min(3, count / (float)bond.Thresholds[0]); cov[c * 5 + 1] = layers / 100f;
            cov[c * 5 + 2] = tier > 0 ? 1 : 0; cov[c * 5 + 3] = tier / (float)bond.Thresholds.Length; cov[c * 5 + 4] = disabled ? 1 : 0;
            if (tier > 0) activeLayers += layers;
            if (c == main)
            {
                pg[0] = 1; int top = bond.Thresholds[^1];
                pg[1] = Math.Min(1, members.Select(i => ids[i]).Distinct().Count() / (float)top);
                pg[2] = Math.Min(1, members.Where(i => units[i].Elite.Value == true).Select(i => ids[i]).Distinct().Count() / (float)top);
                pg[3] = Math.Min(3, layers / 300f);
            }
            planCov[c] = c == main ? 1 : c == second && main >= 0 ? 0.5f : 0;
        }
        if (main >= 0)
            for (int i = 0; i < _spec.Operators.Length; i++)
            {
                var bonds = _spec.Operators[i].Covenants;
                want[i * 2 + 1] = bonds.Contains(main) ? 1 : 0;
                want[i * 2] = bonds.Contains(main) ? 1 : bonds.Contains(second) ? 0.5f : 0;
            }
        run[^4] = units.Count(u => u.Elite.Value == true) / 8f; run[^3] = Math.Min(2, activeLayers / 500f);
        run[^2] = hp0 / 45f; run[^1] = Math.Min(2, activeLayers / 350f);
        var shop = o.Tensor("shop", -1L, 1, 26);
        var item = o.Tensor("item", -1L, 1, 1);
        var kind = o.Tensor("kind", 0L, 1, 200);
        var ptr = o.Tensor("ptr", 0L, 1, 200, 2);
        var mask = o.Tensor("amask", false, 1, 200);
        void Add(string name, int token = 0, int? slot = null, string? target = null)
        {
            int j = o.Actions.Count;
            kind[j] = Array.IndexOf(_spec.Kinds, name); ptr[2 * j] = token; mask[j] = true;
            o.Actions.Add(new(char.ToLowerInvariant(name[0]) + name[1..], slot, target));
        }
        bool capped = ctx.Actions >= 40;
        // 实机靠右排列，装备在干员左侧。作者的 shop 只保留干员，已售位置不能左移。
        int equipmentSlot = Array.FindIndex(s.Shop, c => c.Kind.Value == "equipment");
        int firstOperator;
        if (equipmentSlot >= 0) firstOperator = equipmentSlot + 1;
        else if (ctx.EquipmentSlot.HasValue) firstOperator = ctx.EquipmentSlot.Value + 1;
        else
        {
            firstOperator = 6 - (mode.Shop.GetValueOrDefault(level)?.Chess ?? 0);
            // 升级后的第一批货可能仍用旧布局。装备已售且无上一张快照时，槽位不能唯一还原。
            if (level > 1 && mode.Shop.TryGetValue(level - 1, out var old) && 6 - old.Chess != firstOperator)
                Block("shopLayout");
        }
        for (int i = 0; i < s.Shop.Length; i++)
        {
            var card = s.Shop[i];
            if (card.Bought.Value == true) continue;
            if (card.Bought.Value != false) { Block("shopSlots"); continue; }
            var id = card.Card.Value?.Id.Value;
            if (card.Kind.Value == "equipment")
            {
                if (i != firstOperator - 1) Block("shopLayout");
                item[0] = _items.GetValueOrDefault(id ?? "", -1);
                if (item[0] < 0) o.Missing.Add("itemShop");
                continue;
            }
            if (card.Kind.Value != "operator" || i < firstOperator)
            { Block("shopLayout"); continue; }
            int token = i - firstOperator;
            shop[token] = OperatorIndex(s, id, card.Tier.Value);
            if (shop[token] < 0 || card.Price.Value == null) { Block("shopCard"); continue; }
            if (!capped && money >= card.Price.Value && units.Length - board < 10) Add("Buy", 21 + token, i, id);
        }
        if (!capped)
        {
            for (int i = 0; i < units.Length && i < 20; i++) Add("Sell", 1 + i);
            if (s.RefreshCost.Value == null) Block("refreshCost");
            else if (money >= s.RefreshCost.Value) Add("Refresh");
            if (level < mode.MaxLevel && s.UpgradeCost.Value == null) Block("upgradeCost");
            else if (level < mode.MaxLevel && money >= s.UpgradeCost.Value) Add("Upgrade");
            if (s.Frozen.Value == null) o.Missing.Add("frozen");
            if (s.Frozen.Value == false && s.Shop.Any(c => c.Bought.Value == false)) Add("Freeze");
        }
        Add("EndRound");
        return o;
    }
}

internal sealed class ShopPolicy : IDisposable
{
    internal const string ModelId = "garrison-sp3-v0.1-06d6bf9";
    internal ShopEncoder Encoder { get; }
    private readonly InferenceSession _session;
    public ShopPolicy(string directory)
    {
        Encoder = new(directory);
        using var options = new SessionOptions { IntraOpNumThreads = 1, InterOpNumThreads = 1 };
        _session = new(Path.Combine(directory, "sp3.onnx"), options);
    }
    internal float[] Logits(ShopObservation o)
    {
        using var result = _session.Run(o.Inputs);
        return result.First().AsTensor<float>().ToArray();
    }
    public ShopAdvice Predict(long revision, MatchState state, ShopContext context)
    {
        var o = Encoder.Encode(state, context);
        ShopAdvice Empty() => new(revision, ModelId, "unavailable", o.Missing.ToArray(), null, null);
        if (!o.Available) return Empty();
        if (o.Actions.Count < 2) { o.Missing.Add("onlyEndRound"); return Empty(); }
        var logits = Logits(o);
        var order = Enumerable.Range(0, o.Actions.Count).OrderByDescending(i => logits[i]).ToArray();
        var action = o.Actions[order[0]];
        // TODO(design): 冻结、出售和结束的展示策略留待复核，保留其评分以免把次选当作首选。
        if (action.Kind is not ("buy" or "refresh" or "upgrade")) { o.Missing.Add("unsupportedTopAction"); return Empty(); }
        double top = logits[order[0]];
        return new(revision, ModelId, "ready", o.Missing.ToArray(), action,
            new(1 / order.Sum(i => Math.Exp(logits[i] - top)), top - logits[order[1]], order.Length));
    }
    public void Dispose() => _session.Dispose();
}

