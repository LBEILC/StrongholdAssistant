using System.Text.Json;

namespace ASPAssistant.Core.Placement;

// 只用于内部计算，不改变消息桥的输入输出协议。
public sealed class CombatEffect
{
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public double Atk { get; set; }
    public double Hp { get; set; }
    public double Def { get; set; }
    public double Res { get; set; }
    public double Aspd { get; set; }
    public int Block { get; set; }
    public double Regen { get; set; }
    public double RegenFlat { get; set; }
    public double Sp { get; set; }
    public double BurnRatio { get; set; }
    public double ExtraArts { get; set; }
    public double ExtraTrue { get; set; }
    public double Crit { get; set; }
    public double LossFlat { get; set; }
    public double LossRatio { get; set; }
    public bool NonlethalLoss { get; set; }
    public bool NoHeal { get; set; }
    public bool Weakness { get; set; }
    public double Lifesteal { get; set; }
    public double DistantDamage { get; set; }
    public double CastAtk { get; set; }
    public double SideAspd { get; set; }
    public double FrontHp { get; set; }
    public double SharedInitSp { get; set; }
    public double PerCovenant { get; set; }

    // 多栏装备的同类加成先相加，不能逐件乘百分比造成额外放大。
    public static CombatEffect Combine(IReadOnlyList<CombatEffect> effects) => new()
    {
        Atk = effects.Sum(e => e.Atk), Hp = effects.Sum(e => e.Hp), Def = effects.Sum(e => e.Def),
        Res = effects.Sum(e => e.Res), Aspd = effects.Sum(e => e.Aspd), Block = effects.Sum(e => e.Block),
        Regen = effects.Sum(e => e.Regen), RegenFlat = effects.Sum(e => e.RegenFlat), Sp = effects.Sum(e => e.Sp),
        BurnRatio = effects.Sum(e => e.BurnRatio),
        ExtraArts = effects.Sum(e => e.ExtraArts), ExtraTrue = effects.Sum(e => e.ExtraTrue), Crit = effects.Sum(e => e.Crit),
        LossFlat = effects.Sum(e => e.LossFlat), LossRatio = effects.Sum(e => e.LossRatio),
        NonlethalLoss = effects.Any(e => e.NonlethalLoss), NoHeal = effects.Any(e => e.NoHeal), Weakness = effects.Any(e => e.Weakness),
        Lifesteal = effects.Sum(e => e.Lifesteal), DistantDamage = effects.Sum(e => e.DistantDamage),
        CastAtk = effects.Sum(e => e.CastAtk), SideAspd = effects.Sum(e => e.SideAspd),
        FrontHp = effects.Sum(e => e.FrontHp), SharedInitSp = effects.Sum(e => e.SharedInitSp), PerCovenant = effects.Sum(e => e.PerCovenant),
    };

    public SimUnit Apply(SimUnit unit, int activeCovenants, int sharedAllies, bool emptyFront)
    {
        double countBonus = PerCovenant * Math.Min(8, activeCovenants);
        double hp = unit.Hp * (1 + Hp + countBonus + (emptyFront ? FrontHp : 0));
        return unit with
        {
            Atk = unit.Atk * (1 + Atk + countBonus) * (1 + Crit), Hp = hp, Def = unit.Def * (1 + Def),
            Res = unit.Res + Res, Block = Math.Max(0, unit.Block + Block),
            Interval = unit.BaseAttackInterval / Math.Max(.1, unit.BaseAttackInterval / unit.Interval + Aspd / 100),
            Regen = unit.Regen + hp * Regen + RegenFlat, SpPerSecond = unit.SpPerSecond + Sp,
            BurnRatio = unit.BurnRatio + BurnRatio,
            EquipmentExtraArts = unit.EquipmentExtraArts + ExtraArts, ExtraTrue = unit.ExtraTrue + ExtraTrue,
            NoHeal = unit.NoHeal || NoHeal, Weakness = unit.Weakness || Weakness,
            LossPerSecond = unit.LossPerSecond + LossFlat + (NonlethalLoss ? 0 : hp * LossRatio),
            NonlethalLossPerSecond = unit.NonlethalLossPerSecond + (NonlethalLoss ? hp * LossRatio : 0),
            Lifesteal = unit.Lifesteal + Lifesteal, DistantDamage = unit.DistantDamage + DistantDamage,
            CastAtk = unit.CastAtk + CastAtk,
            Skill = unit.Skill is { } s ? s with { Init = Math.Min(s.Cost, s.Init + SharedInitSp * (1 + sharedAllies)) } : null,
        };
    }
}

public sealed class StrategyEffect
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Kind { get; set; } = "";
    public int[] Thresholds { get; set; } = [];
    public double[] Bonuses { get; set; } = [];
    public double PerElite { get; set; }
    public double EliteAtk { get; set; }
    public double EliteAspd { get; set; }

    public SimUnit Apply(SimUnit unit, int activeCovenants, int eliteCount, bool elite)
    {
        double bonus = Kind == "eliteCount" && elite ? PerElite * eliteCount : 0;
        if (Kind == "activeCovenants")
            for (int i = 0; i < Thresholds.Length; i++)
                if (activeCovenants >= Thresholds[i]) bonus = Bonuses[i];
        return unit with
        {
            Atk = unit.Atk * (1 + bonus + (elite ? EliteAtk : 0)), Hp = unit.Hp * (1 + bonus),
            Interval = unit.BaseAttackInterval / (unit.BaseAttackInterval / unit.Interval + (elite ? EliteAspd : 0) / 100),
            Weakness = unit.Weakness || Kind == "weakness",
        };
    }
}

public sealed class CombatEffectStore
{
    public Dictionary<string, ProfileChoices<CombatEffect>> Equipment { get; private init; } = [];
    public Dictionary<string, StrategyEffect> Strategies { get; private init; } = [];
    public static async Task<CombatEffectStore> LoadAsync(string directory)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return new()
        {
            Equipment = JsonSerializer.Deserialize<Dictionary<string, ProfileChoices<CombatEffect>>>(
                await File.ReadAllTextAsync(Path.Combine(directory, "equipment_effects.json")), options)!,
            Strategies = JsonSerializer.Deserialize<Dictionary<string, StrategyEffect>>(
                await File.ReadAllTextAsync(Path.Combine(directory, "strategy_effects.json")), options)!,
        };
    }
}
