// 2026-11-15：从 CarlosDJY d3dc2f9 移植计算核心与测试，适配本项目输入和数据，保留原声明。
// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Placement;

namespace ASPAssistant.Core.Placement;

public sealed record EnemyAttackClip(double Seconds, double Hit);

/// <summary>A kind of enemy in the simulated stream (speed in cells per second, range in cells; 0 = melee).</summary>
public sealed record EnemyProfile(double Hp, double Def, double Res, double Atk, double Interval, double Speed)
{
    public string Name { get; init; } = "";
    /// <summary>Attack radius in cells; ranged enemies shoot operators in reach while they walk.</summary>
    public double Range { get; init; }
    /// <summary>Its attacks deal arts damage (reduced by the operator's arts resistance).</summary>
    public bool Arts { get; init; }
    public bool NoAttack { get; init; }
    public EnemyAttackClip? AttackClip { get; init; }
    public string Rank { get; init; } = "NORMAL";
    // 死亡生成物是否计入结算漏怪，与本体的生命点参数独立。
    public bool CountsOnLeak { get; init; } = true;
    /// <summary>Protection-point HP lost when it gets through.</summary>
    public int LifePoints { get; init; } = 1;
    /// <summary>Weight (massLevel); 3 or more sinks twice as fast in 沼泽地段.</summary>
    public int Weight { get; init; } = 1;
    /// <summary>Its special mechanics (none for the generic stream).</summary>
    public EnemyTraits Traits { get; init; } = EnemyTraits.None;

    /// <summary>
    /// The enemy <paramref name="factor"/> times as tough: HP × factor and attack × √factor (harder enemies also hit
    /// harder, or blockers and healers would never matter); what it spawns on death too.
    /// </summary>
    public EnemyProfile Tougher(double factor) => this with
    {
        Hp = Hp * factor,
        Atk = Atk * Math.Sqrt(factor),
        Traits = Traits.OnDeath.Count == 0 ? Traits
            : Traits with { OnDeath = [.. Traits.OnDeath.Select(d => d with { Profile = d.Profile.Tougher(factor) })] },
    };
}

public sealed record DefenseDecay(int Hits, double Def, double Res);

public enum ElementKind { None, Burning, Apoptosis, Neural, Erosion }

/// <summary>
/// An enemy's special mechanics, read from its talents (garrison_spawns.json enemies.traits). Rules from the game's
/// terms: 隐匿 = cannot be targeted until blocked; 折射 = +70 arts resistance (on for the whole fight here); element
/// injury on operators bursts at 1000 (神经: 1000 true damage + 10 s stun; 侵蚀: −100 defence + 800 physical; 灼燃:
/// 1200 arts and −20 resistance for 10 s; 凋亡: no skills and 100 arts damage per second for 15 s).
/// </summary>
public sealed record EnemyTraits
{
    public static readonly EnemyTraits None = new();
    public bool Invisible { get; init; }
    public double RefractionRes { get; init; }
    /// <summary>Hits it shrugs off whatever their damage (damage-count shield).</summary>
    public int Shield { get; init; }
    /// <summary>On death the enemies around it get this many shield hits.</summary>
    public int DeathShieldAura { get; init; }
    /// <summary>Every fall: an ember (unblockable, still) that needs this many hits, or it comes back at full HP (and falls into an ember again).</summary>
    public int EmberHits { get; init; }
    public double EmberSeconds { get; init; }
    public DefenseDecay? DefenseDecay { get; init; }
    /// <summary>First death: untargetable for this long, then back at full HP.</summary>
    public double RebornSeconds { get; init; }
    public IReadOnlyList<DeathSpawn> OnDeath { get; init; } = [];
    public ElementKind Element { get; init; }
    /// <summary>Element injury per hit as a share of its attack.</summary>
    public double ElementRatio { get; init; }
    public double BleedDps { get; init; }
    public double BleedSeconds { get; init; }
    /// <summary>A cloud left on death: true damage per second (or attack share) to operators within range.</summary>
    public double CloudDps { get; init; }
    public double CloudAtkScale { get; init; }
    public double CloudRange { get; init; }
    public double CloudSeconds { get; init; }
    /// <summary>The cloud lands on whoever killed it rather than where it fell.</summary>
    public bool CloudAtSource { get; init; }
    /// <summary>First hit on each operator × this, and that operator cannot be healed for <see cref="CritNoHeal"/> s.</summary>
    public double CritScale { get; init; } = 1;
    public double CritNoHeal { get; init; }
    /// <summary>Defence / arts resistance it gives the enemies within <see cref="AuraRange"/>.</summary>
    public double AuraDef { get; init; }
    public double AuraRes { get; init; }
    public double AuraRange { get; init; }
    /// <summary>Attack-speed multiplier on operators within <see cref="SlowRange"/> (0.5 = half as fast).</summary>
    public double SlowFactor { get; init; } = 1;
    public double SlowRange { get; init; }
    /// <summary>Damage it takes while not blocked (0.6 = 40 % less).</summary>
    public double UnblockedTaken { get; init; } = 1;
    /// <summary>Block slots it takes, and the least block count that can stop it.</summary>
    public int BlockCost { get; init; } = 1;
    public int MinBlock { get; init; }
    /// <summary>HP regenerated per second.</summary>
    public double Regen { get; init; }
    /// <summary>
    /// Every this many attacks the hit also stuns for <see cref="StunSeconds"/>: its target, or every operator within
    /// <see cref="StunRadius"/> of the enemy.
    /// </summary>
    public int StunEvery { get; init; }
    public double StunSeconds { get; init; }
    public double StunRadius { get; init; }
    /// <summary>
    /// Its hits also land (× <see cref="SplashScale"/>) on the operators within this radius of the target: on every
    /// attack, every <see cref="SplashEvery"/>-th, or only the first (<see cref="SplashFirst"/>).
    /// </summary>
    public double SplashRadius { get; init; }
    public int SplashEvery { get; init; } = 1;
    public bool SplashFirst { get; init; }
    public double SplashScale { get; init; } = 1;
    /// <summary>Each hit jumps on to the nearest operator until this many are hit, × <see cref="ChainScale"/> per jump.</summary>
    public int ChainTargets { get; init; }
    public double ChainScale { get; init; } = 1;
    /// <summary>Every <see cref="LineCooldown"/> s an attack sweeps the line through its target (× <see cref="LineScale"/> each).</summary>
    public double LineCooldown { get; init; }
    public double LineScale { get; init; }
}

/// <summary>Enemies that appear where one died (after <see cref="Delay"/> s).</summary>
public sealed record DeathSpawn(EnemyProfile Profile, int Count, double Delay);

/// <summary>One enemy of a real stream: when it appears, at which gate, flying or not, and what it is.</summary>
public sealed record SimSpawn(double Time, bool Upper, bool Flying, EnemyProfile Profile);

/// <summary>
/// A steady stream of enemies: <see cref="GroundCount"/> walkers and <see cref="FlyCount"/> flyers spread evenly
/// over <see cref="SpawnSeconds"/>, then <see cref="TailSeconds"/> to finish them.
/// </summary>
public sealed record WaveScenario
{
    public bool CollectDetails { get; init; }
    public EnemyProfile Ground { get; init; } = new(4000, 250, 20, 450, 1.6, 0.85);
    public EnemyProfile Flying { get; init; } = new(2200, 120, 20, 0, 0, 1.0);
    public int GroundCount { get; init; } = 24;
    public int FlyCount { get; init; }
    public double SpawnSeconds { get; init; } = 45;
    // 普通战场的倒计时按二倍战速换成游戏秒；首领场有独立的加时规则。
    public static double? RoundLimit(int? round) => round switch
    {
        1 or 2 => 90, 3 or 4 or 5 => 110, 6 => 140, 7 or 8 => 170, 9 or 10 => 190,
        11 or 12 or 13 => 230, _ => null
    };
    public double? TimeLimit { get; init; }
    public double TailSeconds { get; init; } = 25;
    public double Step { get; init; } = 0.2;
    /// <summary>A real stream (round data): replaces the counts and the two profiles above when set.</summary>
    public IReadOnlyList<SimSpawn>? Spawns { get; init; }

    /// <summary>
    /// The same stream with enemies <paramref name="factor"/> times as tough: HP × factor and attack × √factor
    /// (harder enemies also hit harder, or blockers and healers would never matter).
    /// </summary>
    public WaveScenario Tougher(double factor) => this with
    {
        Ground = Ground with { Hp = Ground.Hp * factor, Atk = Ground.Atk * Math.Sqrt(factor) },
        Flying = Flying with { Hp = Flying.Hp * factor, Atk = Flying.Atk * Math.Sqrt(factor) },
        Spawns = Spawns?.Select(s => s with { Profile = s.Profile.Tougher(factor) }).ToList(),
    };
}

/// <summary>How a skill gains SP: one per second, per attack, or per hit taken.</summary>
public enum SkillCharge { Auto, Attack, Hit }

/// <summary>What damage a skill's hits deal while it runs: the operator's own type, arts, or true damage.</summary>
public enum SkillDamage { Same, Arts, True }

public sealed record SlashSeries(int Count, double Interval, double Damage, double Finisher);

public sealed record GroundField(double Seconds, double Radius, double Damage, double Drift = 0,
    double Growth = 0, int HeavyWeight = 0, double HeavyDamage = 0, double HeavySlow = 0);

/// <summary>
/// A skill on its SP cycle: SP from <see cref="Init"/> up to <see cref="Cost"/>, then attack (or healing) × <see cref="Gain"/>
/// for <see cref="Duration"/> s (0: one strong action). Auto skills wait for a target; no SP is gained while one runs.
/// While it runs the operator uses its skill range (<see cref="SimUnit.SkillRange"/>) and the effects below.
/// </summary>
public sealed record SimSkill(double Gain, double Cost, double Init, double Duration, SkillCharge Charge)
{
    public SlashSeries? Slashes { get; init; }
    public GroundField? GroundField { get; init; }
    public double AreaBurst { get; init; }
    /// <summary>Enemies hit per attack (99: every enemy in range).</summary>
    public int Targets { get; init; } = 1;
    /// <summary>Strikes per skill action ("持续进行五次").</summary>
    public int Hits { get; init; } = 1;
    /// <summary>Hits every enemy it blocks.</summary>
    public bool HitsBlocked { get; init; }
    public SkillDamage Damage { get; init; }
    /// <summary>Arts resistance its hits ignore.</summary>
    public double ResIgnore { get; init; }
    /// <summary>Enemies in range: defence × this and arts resistance + <see cref="EnemyResAdd"/> (a debuff).</summary>
    public double EnemyDefScale { get; init; } = 1;
    public double EnemyResAdd { get; init; }
    /// <summary>On cast, allies in range healed by this × attack; while it runs, this × attack per second.</summary>
    public double HealBurst { get; init; }
    public double HealPerSecond { get; init; }
    public bool Debuffs => EnemyDefScale < 1 || EnemyResAdd < 0;
    /// <summary>Its hits stun (no moving or attacking) or stop (no moving) enemies for this long, at this chance.</summary>
    public bool ControlStuns { get; init; }
    public double ControlSeconds { get; init; }
    public double ControlChance { get; init; } = 1;
    /// <summary>What it gives itself while it runs: defence × (1 + this), max HP × (1 + ratio) or + flat, block + this.</summary>
    public double SelfDef { get; init; }
    public double SelfHp { get; init; }
    public int SelfBlock { get; init; }
    /// <summary>While it runs: damage per second (× attack, arts) to every ground enemy in reach, and their slow.</summary>
    public bool Liberator { get; init; }
    public double InitialRamp { get; init; }
    public double MarkDamage { get; init; }
    public double Reflect { get; init; }
    public double ArtsEcho { get; init; }
    public double ArtsTrait { get; init; }
    public bool CastOnDamage { get; init; }
    public double BurnBonus { get; init; }
    public double BurstElement { get; init; }
    public double BurstHeal { get; init; }
    public double BurstDot { get; init; }
    public double GroupBoost { get; init; }
    public bool Snow { get; init; }
    public double FireWave { get; init; }
    public double FireGauge { get; init; }
    public double SkillIntervalDelta { get; init; }
    public bool CastInSkillRange { get; init; }
    public bool OnlySkillAttacks { get; init; }
    public double SkillAspd { get; init; }
    public double FieldHeal { get; init; }
    public double FieldSp { get; init; }
    public int FieldExtension { get; init; }
    public double InspireHp { get; init; }
    public bool BlockCharge { get; init; }
    public double IdleSpRate { get; init; }
    public double SelfRegen { get; init; }
    public double EndStun { get; init; }
    public int Ammo { get; init; }
    public double FrozenGain { get; init; }
    public bool StopsAttack { get; init; }
    public double AtkPerHit { get; init; }
    public double EndBurst { get; init; }
    public double StanceAspd { get; init; }
    public double StanceDodge { get; init; }
    public double DecayingAspd { get; init; }
    public double RemnantDamage { get; init; }
    public int Charges { get; init; } = 1;
    public int ReconstructHp { get; init; }
    public double ChainFalloff { get; init; }
    public bool MeleeDuringSkill { get; init; }
    public double BardZone { get; init; }
    public double Inspire { get; init; }
    public double BardHeal { get; init; }
    public bool ShareHp { get; init; }
    public double Zone { get; init; }
    public double Slow { get; init; }
    /// <summary>Its hits push enemies away or drag them towards the operator.</summary>
    public PushData? Push { get; init; }
}

/// <summary>A summon's time on the field: first appears at <see cref="Appear"/> s, stays <see cref="Duration"/> s
/// (0: until beaten), comes back <see cref="Cooldown"/> s after it leaves or falls.</summary>
public sealed record SummonTiming(double Appear, double Duration, double Cooldown);

/// <summary>
/// How an operator's normal attack lands (its branch's trait): on one target, on several, on everything it blocks,
/// splashing around the target, jumping between enemies, or twice per attack; healers healing several at once;
/// operators that only act through their skill; or a healing aura instead of attacks.
/// </summary>
public sealed record AttackPattern
{
    public static readonly AttackPattern Single = new();
    /// <summary>Enemies hit per attack (99: every enemy in range).</summary>
    public int Targets { get; init; } = 1;
    public bool HitsBlocked { get; init; }
    public double SplashRadius { get; init; }
    /// <summary>Hits jump on to this many enemies in all (× 0.85 per jump).</summary>
    public int ChainTargets { get; init; }
    public int HitsPerAttack { get; init; } = 1;
    public double HealFromDamage { get; init; }
    public int HealTargets { get; init; } = 1;
    /// <summary>Does not attack unless its skill runs (阵法术师).</summary>
    public bool SkillOnly { get; init; }
    /// <summary>No attacks; heals everyone in range by this × attack per second (吟游者).</summary>
    public double HealAura { get; init; }
    /// <summary>Attacks normally, heals instead while its skill runs (护佑者).</summary>
    public bool HealsDuringSkill { get; init; }
}

/// <summary>An operator in the simulation: where it stands, what it reaches and its combat numbers.</summary>
public sealed record SimUnit(
    string Key, BoardCell Cell, IReadOnlyCollection<BoardCell> Range, bool CanBlock, int Block, bool AntiAir,
    double Atk, double Interval, bool Arts, double Hp, double Def, bool Healer)
{
    internal string? ReserveFor { get; init; }
    internal string? ReservePiece { get; init; }
    public int RedeployCost { get; init; }
    public int RedeploySeconds { get; init; }
    public string? FixedSummonName { get; init; }
    public string? FixedOwner { get; init; }
    public BoardCell? ReconstructAt { get; init; }
    public BoardCell? SeabornAt { get; init; }
    /// <summary>法术抗性（0～100）。</summary>
    public double Res { get; init; }
    /// <summary>How much of <see cref="Atk"/> comes from the skill (lost while 凋亡 locks skills).</summary>
    public double SkillFactor { get; init; } = 1;
    /// <summary>
    /// The carried skill on its SP cycle; <see cref="Atk"/> ÷ <see cref="SkillFactor"/> is then the attack without it.
    /// Null: <see cref="Atk"/> is the skill's average over a fight.
    /// </summary>
    public SimSkill? Skill { get; init; }
    /// <summary>Enemies in its range lose invisibility (奥术 with 3 different 奥术 operators on the field).</summary>
    public bool Reveals { get; init; }
    /// <summary>Casting its skill strips invisibility from the enemies in its range for this long (凛御银灰).</summary>
    public double SkillRevealSeconds { get; init; }
    /// <summary>The cells it reaches while its skill runs (null: its normal range).</summary>
    public IReadOnlyCollection<BoardCell>? SkillRange { get; init; }
    public AttackPattern Pattern { get; init; } = AttackPattern.Single;

    // Talents (data/talent_effects.py), already scaled by how often they hold.
    /// <summary>Defence ignored: flat, then a share; arts resistance ignored (flat).</summary>
    public double DefIgnore { get; init; }
    public double DefIgnoreRatio { get; init; }
    public double ResIgnore { get; init; }
    /// <summary>Damage dealt × this.</summary>
    public double OutputScale { get; init; } = 1;
    public double DamageScale { get; init; } = 1;
    /// <summary>Extra arts damage per hit, × the hit's attack.</summary>
    public double ExtraArts { get; init; }
    public double ExtraTrue { get; init; }
    public BoardCell? ThrowCell { get; init; }
    public Facing Facing { get; init; }
    public (int Left, int Right, int Bottom) FieldBounds { get; init; }
    public IReadOnlyList<BoardCell> RemnantCells { get; init; } = [];
    public double IberiaDot { get; init; }
    public double UrsusBlockDamage { get; init; }
    public double SiracusaAspd { get; init; }
    public double SiracusaWindow { get; init; }
    public double SiracusaPulse { get; init; }
    public double SiracusaStatusSeconds { get; init; }
    public double DocHeal { get; init; }
    public double DocElement { get; init; }
    public double DocShelter { get; init; }
    public bool KazimierzAlly { get; init; }
    public bool KazimierzUpper { get; init; }
    public bool IberiaUpper { get; init; }
    public double BurnRatio { get; init; }
    public double EquipmentExtraArts { get; init; }
    public bool Weakness { get; init; }
    public bool NoHeal { get; init; }
    public double LossPerSecond { get; init; }
    public double NonlethalLossPerSecond { get; init; }
    public double Lifesteal { get; init; }
    public double DistantDamage { get; init; }
    public double CastAtk { get; init; }
    public double BaseAttackInterval { get; init; } = 1;
    /// <summary>Poison its hits leave: arts damage per second for <see cref="DotSeconds"/>.</summary>
    public double DotDps { get; init; }
    public double DotSeconds { get; init; }
    /// <summary>Its hits stun (or stop) enemies for this long, at this chance.</summary>
    public bool HitStuns { get; init; }
    public double HitControlSeconds { get; init; }
    public double HitControlChance { get; init; }
    /// <summary>Enemies in its range take this much more damage and move this much slower.</summary>
    public double Fragile { get; init; }
    public double EnemySlow { get; init; }
    /// <summary>Damage it takes × (1 − this); share of physical hits dodged.</summary>
    public double Sanctuary { get; init; }
    public double Dodge { get; init; }
    /// <summary>HP it regains per second; SP it gains per second on top of its skill's charging.</summary>
    public double Regen { get; init; }
    public double SpPerSecond { get; init; }
    /// <summary>Its normal attacks push (or drag) their target.</summary>
    public PushData? Push { get; init; }
    /// <summary>A summon: when it is on the field (null: an operator, there from the start).</summary>
    public SummonTiming? Summon { get; init; }
}

/// <summary>An enemy path: points in grid coordinates (cell centres at +0.5), spawn → defence point.</summary>
public sealed record SimLane(IReadOnlyList<(double Col, double Row)> Points, double Weight, bool Flying)
{
    /// <summary>Starts at the upper gate (spawn row 0–1) rather than the lower one.</summary>
    public bool Upper => Points.Count > 0 && Points[0].Row < 2;
}

public sealed record SimUnitDetail(double Seconds, double Active, double Atk, double Speed);
public sealed record SimEnemyDetail(string Name, double Hp, double Taken, double Seconds, bool Killed);

public sealed record SimResult(
    int Spawned, int Killed, int Leaked, int Alive,
    IReadOnlyDictionary<string, int> Kills, IReadOnlyDictionary<string, double> Damage,
    IReadOnlyList<string> Fallen, IReadOnlyDictionary<int, int> LeaksByLane)
{
    public IReadOnlyDictionary<string, SimUnitDetail>? UnitDetails { get; init; }
    public IReadOnlyList<SimEnemyDetail>? EnemyDetails { get; init; }
    /// <summary>Enemies that got through, by name (what the line could not handle).</summary>
    public IReadOnlyDictionary<string, int> LeakedByName { get; init; } = new Dictionary<string, int>();
    /// <summary>Skills cast per operator (only for operators given a <see cref="SimUnit.Skill"/>).</summary>
    public IReadOnlyDictionary<string, int> Casts { get; init; } = new Dictionary<string, int>();
    /// <summary>Per operator: stuns (enemies or operators) in the eight cells around it.</summary>
    public IReadOnlyDictionary<string, int> StunsAround { get; init; } = new Dictionary<string, int>();
    /// <summary>Per operator: enemies that fell inside its attack range (whoever killed them).</summary>
    public IReadOnlyDictionary<string, int> KillsInRange { get; init; } = new Dictionary<string, int>();
    /// <summary>Per operator (summons apart): damage it took, and HP it restored to others or itself through healing.</summary>
    public IReadOnlyDictionary<string, double> Taken { get; init; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> Healed { get; init; } = new Dictionary<string, double>();
    /// <summary>Bosses (rank BOSS) in the stream.</summary>
    public int Bosses { get; init; }
    /// <summary>
    /// How far the bosses were beaten, summed: 1 per boss killed, otherwise the share of its HP (all its lives)
    /// taken off before it got through or the fight ended.
    /// </summary>
    public double BossProgress { get; init; }
    /// <summary>Bosses killed (also counted in <see cref="Killed"/>).</summary>
    public int BossesKilled { get; init; }
    /// <summary>
    /// What a boss is worth in enemies handled: the round is about the boss, so it counts as much as everything else
    /// in its stream put together (at least <see cref="MinBossWorth"/>).
    /// </summary>
    public double BossWorth => Bosses == 0 ? 0 : Math.Max(MinBossWorth, (double)(Spawned - Bosses) / Bosses);
    public const double MinBossWorth = 8;
    /// <summary>
    /// Enemies handled with the bosses at their worth: kills of the others, plus each boss's worth times how far
    /// it was beaten (so damage on a boss that survives still counts).
    /// </summary>
    public double Handled => Killed - ChargedChildLeaks - BossesKilled + BossWorth * BossProgress;
    // 子代不占波次名额，漏出去仍会扣血的子代在站位评分里单独扣除。
    public int ChargedChildLeaks { get; init; }
    /// <summary>The most <see cref="Handled"/> can be.</summary>
    public double Worth => Spawned - Bosses + BossWorth * Bosses;
    public double KillRate => Spawned == 0 ? 1 : (double)Killed / Spawned;

    /// <summary>The weighted average of several fights (counts rounded; fallen = down in at least half of them).</summary>
    public static SimResult Average(IEnumerable<(SimResult Result, double Weight)> runs)
    {
        var list = runs.ToList();
        double total = list.Sum(r => r.Weight);
        if (list.Count == 0 || total <= 0) return new SimResult(0, 0, 0, 0, new Dictionary<string, int>(), new Dictionary<string, double>(), [], new Dictionary<int, int>());
        int Avg(Func<SimResult, double> f) => (int)Math.Round(list.Sum(r => r.Weight * f(r.Result)) / total);
        Dictionary<TKey, double> Sum<TKey>(Func<SimResult, IEnumerable<KeyValuePair<TKey, double>>> f) where TKey : notnull =>
            list.SelectMany(r => f(r.Result).Select(kv => (kv.Key, V: r.Weight * kv.Value / total)))
                .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.V));
        var kills = Sum(r => r.Kills.Select(kv => new KeyValuePair<string, double>(kv.Key, kv.Value)));
        var leaks = Sum(r => r.LeaksByLane.Select(kv => new KeyValuePair<int, double>(kv.Key, kv.Value)));
        var names = Sum(r => r.LeakedByName.Select(kv => new KeyValuePair<string, double>(kv.Key, kv.Value)));
        var fallen = list.SelectMany(r => r.Result.Fallen.Select(k => (k, r.Weight / total))).GroupBy(x => x.k)
            .Where(g => g.Sum(x => x.Item2) >= 0.5).Select(g => g.Key).ToList();
        return new SimResult(Avg(r => r.Spawned), Avg(r => r.Killed), Avg(r => r.Leaked), Avg(r => r.Alive),
            kills.Where(kv => kv.Value >= 0.5).ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value)),
            Sum(r => r.Damage), fallen,
            leaks.Where(kv => kv.Value >= 0.5).ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value)))
        {
            ChargedChildLeaks = Avg(r => r.ChargedChildLeaks),
            LeakedByName = names.Where(kv => kv.Value >= 0.5).ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value)),
        };
    }
}

/// <summary>
/// Time-step simulation of an enemy stream against a placement: walkers follow their lane and stop at the first
/// operator with free block capacity on their cell, fight it, and leak when they reach the end; flyers are never
/// blocked and only anti-air operators reach them. Operators attack every <see cref="SimUnit.Interval"/> (enemies
/// they block first, then the one furthest along), healers heal the most hurt ally in range. The enemies' special
/// mechanics (<see cref="EnemyTraits"/>) are played out: invisibility, refraction, damage-count shields, embers and
/// rebirth, death spawns, element injury with its bursts, bleeding, death clouds, first-hit crits, auras, block
/// requirements, regeneration, stuns every few attacks, splash, chains and line attacks. Operators' skills run on
/// their SP cycle when given (<see cref="SimUnit.Skill"/>), and the map's special tiles (<see cref="TerrainKind"/>)
/// act on whoever stands on or walks over them.
/// </summary>
public static class ClearSimulator
{
    private const double BurstAt = 1000;
    private const double ElementCooldown = 10;
    // Tiles (level blackboards): 活性源石 attack +20 %, attack speed +20, 70 HP lost per second (the rate is assumed);
    // 优化模块 operators' attack speed +25, enemies' move speed × 1.5. 沼泽地段 (陷入沼泽): each trigger adds a stack of
    // −5 % attack speed (enemies also −5 % move speed; weight ≥ 3 adds two stacks), up to 10. Assumed: it triggers on
    // stepping in and every second on it, and the stacks stay.
    private const double InfectionAtk = 1.2, InfectionSpeed = 1.2, InfectionDps = 70;
    private const double SpeedupSpeed = 1.25, SpeedupMove = 1.5;
    private const double MireStack = 0.05, MireInterval = 1;
    private const int MireMaxStacks = 10, MireHeavy = 3;

    private sealed record Field(int Owner, GroundField Spec, double Start, double X, double Y,
        double Vx, double Vy, double Atk)
    {
        public int Ticks;
    }

    private sealed class Aftertone(int owner, double damage, double next, HashSet<Enemy> targets)
    {
        public readonly int Owner = owner;
        public readonly double Damage = damage;
        public readonly HashSet<Enemy> Targets = targets;
        public double Next = next;
        public int Left = 6;
    }

    private sealed class FireShot(int owner, double x, double y, int dx, int dy, double reach, double attack, HashSet<Enemy> hit)
    {
        public readonly int Owner = owner, Dx = dx, Dy = dy;
        public double X = x, Y = y, Left = reach;
        public readonly double Attack = attack;
        public readonly HashSet<Enemy> Hit = hit;
    }

    private sealed class Enemy
    {
        public required EnemyProfile Profile;
        public int Lane;
        public double S;
        public double Hp;
        public double MaxHp;
        public int BlockedBy = -1;
        public double Cooldown;
        public double StandUntil;
        public int Aim = -1;
        public bool Flying;
        public bool Done;
        public int Shield;
        public bool Ember, UsedReborn;
        public int EmberHits;
        public double StateTimer;   // ember / rebirth countdown
        public bool Reviving;
        public double X, Y;
        public BoardCell Cell;      // the cell under it (kept with X, Y)
        public int CellBit = -1;
        public double AuraDef, AuraRes;
        public double DefScale = 1, ResAdd;   // skill debuffs on it this step
        public double StunUntil = -1, StopUntil = -1;   // controlled by an operator's skill
        public HashSet<int>? Struck; // operators already hit once (first-hit crits)
        public int Attacks;
        private int _decayHits;
        public double Def => Profile.Def + _decayHits * (T.DefenseDecay?.Def ?? 0);
        public double Res => Profile.Res + _decayHits * (T.DefenseDecay?.Res ?? 0);
        public void Damaged()
        {
            if (T.DefenseDecay is { } decay && _decayHits < decay.Hits) _decayHits++;
        }
        public bool Infected;       // walked over 活性源石: stronger, losing HP
        public TerrainKind Tile;
        public double LineReady;
        public double RevealedUntil = -1;
        public int MireStacks;
        public double MireNext = double.NegativeInfinity;   // next 沼泽 trigger while on it
        public int IberiaStacks, IberiaSlow, IberiaOwner;
        public double IberiaTick, IberiaEnds = -1, IberiaDps;
        public Dictionary<int, double>? IberiaLastHit;
        public double[]? SiracusaStatus;
        public double LureUntil, LurePosition, TrailUntil;
        public double AttackSlowUntil = -1;
        public double BurnGauge, BurnEnds = -1, HealCutUntil = -1;
        public double ColdUntil = -1, FrozenUntil = -1;
        public bool Killed;
        public bool Child;
        public double Taken, Seconds;
        public int LivesLost;       // embers and rebirths it went through
        public double Fragile, SlowBy;   // from operators' talents and zones this step
        public double PoisonDps, PoisonUntil = -1;
        public int PoisonBy = -1;
        public double PushedAt = double.NegativeInfinity;
        public double Mire => 1 - MireStack * MireStacks;
        public double Atk => Profile.Atk * (Infected ? InfectionAtk : 1);
        public EnemyTraits T => Profile.Traits;
        public bool Still => Ember || Reviving;
    }

    private sealed class Unit
    {
        public required SimUnit Spec;
        public required HashSet<BoardCell> Range;
        public UInt128 Mask;        // Range as cell bits
        public HashSet<BoardCell> SkillRange = [];
        public UInt128 SkillMask;
        public int[] SkillAllies = [];  // allies in the skill range (its healing)
        public double HealUntil = -1;
        public int[] Patients = []; // healers: the allies in range
        public double Hp;
        public double Cooldown;
        public int Blocking;
        public bool Down;
        public int Kills;
        public int Casts, StunsSeen, KillsSeen;
        public double MaxHp;
        public int BlockCap;
        public bool BuffOn;          // the skill's self buffs applied
        public double Damage, Taken, Healed;
        public double Seconds, Active, AtkSum, SpeedSum;
        public double StunUntil = -1, NoHealUntil = -1, SkillLockUntil = -1, BurnUntil = -1, ApoptosisUntil = -1;
        public double BleedDps, BleedUntil = -1;
        public double DefLoss;
        public double Slow = 1;
        public readonly double[] Gauge = new double[5];
        public readonly double[] GaugeReady = new double[5];
        public TerrainKind Tile;
        public double Sp, ActiveUntil = -1;
        public bool Empowered;
        public int SlashIndex = -1;
        public double Ramp, InspiredHp, NewInspireHp, FieldSpUntil;
        public int KillsAtCast;
        public int Stance = -1;
        public double DeployedAt;
        public int Ammo;
        public int HitsTaken;
        public int StoredEnergy;
        public double RemnantDamage;
        public int RemnantKills;
        public List<(BoardCell Cell, double Expires, double Trigger)>? Remnants;
        public double DocShelter;
        public double NextSlash;
        public double ConstructHp, ConstructReady, ChainHasteUntil;
        public double NextPulse, InspiredAtk;
        public bool Overdrive, BlessingUsed;
        public double SnowClock, DownShield, FireTick;
        public bool Recovering;
        public Dictionary<BoardCell, double>? FireTiles;
        public Dictionary<BoardCell, int>? SnowTiles;
        public Dictionary<Enemy, BoardCell>? SnowPrevious;
        public double NextRage = 3;
        public int Rage;
        public double NextUp = double.PositiveInfinity, UpUntil = double.PositiveInfinity;   // summons
        public double ImmortalUntil = -1;
        public BoardCell Cell => BuffOn && Spec.ReconstructAt is { } token ? token : Spec.Cell;
        public double X => Cell.Col + 0.5;
        public double Y => Cell.Row + 0.5;
        public bool Stunned(double t) => StunUntil > t;
        public double Res(double t) => Math.Max(0, Spec.Res - (BurnUntil > t ? 20 : 0));
        public double Def => Math.Max(0, Spec.Def * (BuffOn ? 1 + (Spec.Skill?.SelfDef ?? 0) : 1) - DefLoss);
    }

    private sealed record Cloud(double X, double Y, double Range, double Dps, double Until);

    private sealed class Path
    {
        public required (double X, double Y)[] Points;
        public required double[] Cumulative;
        public double Length => Cumulative[^1];

        public (double X, double Y) At(double s)
        {
            if (s <= 0) return Points[0];
            int i = Array.BinarySearch(Cumulative, s);
            if (i < 0) i = ~i;
            if (i >= Points.Length) return Points[^1];
            if (i == 0) return Points[0];
            double seg = Cumulative[i] - Cumulative[i - 1];
            double t = seg <= 0 ? 0 : (s - Cumulative[i - 1]) / seg;
            return (Points[i - 1].X + (Points[i].X - Points[i - 1].X) * t, Points[i - 1].Y + (Points[i].Y - Points[i - 1].Y) * t);
        }

        /// <summary>The distance along the path of its point nearest to (x, y).</summary>
        public double Nearest(double x, double y)
        {
            double best = double.MaxValue, at = 0;
            for (int i = 1; i < Points.Length; i++)
            {
                var (ax, ay) = Points[i - 1];
                double dx = Points[i].X - ax, dy = Points[i].Y - ay, len2 = dx * dx + dy * dy;
                double f = len2 <= 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
                double px = ax + f * dx - x, py = ay + f * dy - y, d = px * px + py * py;
                if (d < best) { best = d; at = Cumulative[i - 1] + f * (Cumulative[i] - Cumulative[i - 1]); }
            }
            return at;
        }

        public static Path Of(IReadOnlyList<(double Col, double Row)> points)
        {
            var pts = points.Select(p => (p.Col, p.Row)).ToArray();
            var cum = new double[pts.Length];
            for (int i = 1; i < pts.Length; i++)
                cum[i] = cum[i - 1] + Math.Sqrt(Math.Pow(pts[i].Col - pts[i - 1].Col, 2) + Math.Pow(pts[i].Row - pts[i - 1].Row, 2));
            return new Path { Points = pts, Cumulative = cum };
        }
    }

    private const int MaskCol0 = -4, MaskCols = 16, MaskRow0 = -2, MaskRows = 8;

    /// <summary>The cell's bit in a range mask, or −1 outside the masked area.</summary>
    private static int Bit(BoardCell c)
    {
        int col = c.Col - MaskCol0, row = c.Row - MaskRow0;
        return col < 0 || col >= MaskCols || row < 0 || row >= MaskRows ? -1 : row * MaskCols + col;
    }

    private static UInt128 MaskOf(IEnumerable<BoardCell> cells)
    {
        UInt128 m = 0;
        foreach (var c in cells) if (Bit(c) is int b and >= 0) m |= UInt128.One << b;
        return m;
    }

    private static bool Has(UInt128 mask, int bit) => ((mask >> bit) & UInt128.One) != UInt128.Zero;

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>Fights simulated so far in this process (for profiling the planner).</summary>
    public static long RunCount => Interlocked.Read(ref _runs);
    private static long _runs;

    public static SimResult Run(IReadOnlyList<SimUnit> specs, IReadOnlyList<SimLane> lanes, WaveScenario w,
        IReadOnlyDictionary<BoardCell, TerrainKind>? terrain = null)
    {
        Interlocked.Increment(ref _runs);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return RunCore(specs, lanes, w, terrain); }
        finally { Interlocked.Add(ref _ticks, System.Diagnostics.Stopwatch.GetTimestamp() - t0); }
    }

    /// <summary>Time spent simulating so far in this process, summed over threads.</summary>
    public static TimeSpan RunTime => TimeSpan.FromSeconds((double)Interlocked.Read(ref _ticks) / System.Diagnostics.Stopwatch.Frequency);
    private static long _ticks;

    private static SimResult RunCore(IReadOnlyList<SimUnit> specs, IReadOnlyList<SimLane> lanes, WaveScenario w,
        IReadOnlyDictionary<BoardCell, TerrainKind>? terrain)
    {
        var paths = lanes.Select(l => Path.Of(l.Points)).ToArray();
        TerrainKind TileAt(BoardCell c) => terrain is not null && terrain.TryGetValue(c, out var k) ? k : TerrainKind.None;
        var units = specs.Select(s => new Unit
        {
            Spec = s, Range = [.. s.Range], Hp = s.Hp, Tile = TileAt(s.Cell), Sp = s.Skill?.Init ?? 0,
        }).ToList();
        foreach (var u in units)
        {
            if (u.Spec.ReservePiece is not null) u.Down = true;
            if (u.Spec.Summon is { } sm) { u.Down = true; u.NextUp = sm.Appear; }
            u.ConstructHp = u.Spec.Skill?.ReconstructHp ?? 0;
            u.MaxHp = u.Spec.Hp;
            u.Ramp = u.Spec.Skill?.InitialRamp ?? 0;
            u.BlockCap = u.Spec.Skill is { Liberator: true } ? 0 : u.Spec.Block;
            u.Mask = MaskOf(u.Range);
            u.SkillRange = u.Spec.SkillRange is { } sr ? [.. sr] : u.Range;
            if (u.Spec.ReconstructAt is { } token)
                u.SkillRange = [.. u.SkillRange.Select(cell => new BoardCell(cell.Col + token.Col - u.Spec.Cell.Col,
                    cell.Row + token.Row - u.Spec.Cell.Row))];
            u.SkillMask = MaskOf(u.SkillRange);
            if (u.Spec.Skill is { } hs && (hs.HealBurst > 0 || hs.HealPerSecond > 0) || u.Spec.Pattern.HealAura > 0)
                u.SkillAllies = [.. Enumerable.Range(0, units.Count).Where(j => u.SkillRange.Contains(units[j].Spec.Cell))];
            if (u.Spec.Healer || u.Spec.Pattern.HealsDuringSkill || u.Spec.Pattern.HealFromDamage > 0)
                u.Patients = [.. Enumerable.Range(0, units.Count).Where(j => u.Range.Contains(units[j].Spec.Cell))];
        }
        UInt128 revealedMask = 0;
        var sources = new List<Enemy>();
        var hits = new List<(double Priority, Enemy Enemy)>();
        var revealed = new HashSet<BoardCell>();
        bool anyReveal = units.Any(u => u.Spec.Reveals);
        bool anyDebuff = units.Any(u => u.Spec.Skill is { Debuffs: true });
        var snowCasters = units.Select((u, i) => (u, i)).Where(pair => pair.u.Spec.Skill is { Snow: true }).ToArray();
        bool anyBardSkill = units.Any(u => u.Spec.Skill is { BardZone: > 0 } or { ShareHp: true });
        int siracusa = units.FindIndex(u => u.Spec.SiracusaPulse > 0);
        var liberators = units.Select((u, i) => (u, i)).Where(v => v.u.Spec.Skill is { Liberator: true }).ToArray();
        bool anyBondPulse = siracusa >= 0 || units.Any(u => u.Spec.UrsusBlockDamage > 0);
        double bondTick = 1;
        bool anyDoc = units.Any(u => u.Spec.DocHeal > 0);
        double docTick = 1;
        bool anyHot = units.Any(u => u.Spec.Skill is { HealPerSecond: > 0 } || u.Spec.Pattern.HealAura > 0);
        bool anyField = snowCasters.Length > 0 || units.Any(u => u.Spec.Fragile > 0 || u.Spec.EnemySlow > 0 || u.Spec.Skill is { Zone: > 0 } or { Slow: > 0 } or { GroundField.HeavySlow: > 0 });
        bool anyRegen = units.Any(u => u.Spec.Regen > 0);
        bool anyLoss = units.Any(u => u.Spec.LossPerSecond > 0 || u.Spec.NonlethalLossPerSecond > 0);
        bool anySummon = units.Any(u => u.Spec.Summon is not null);
        var rng = new Random(20261114);   // control chances: fixed, so a fight plays the same every time
        var byCell = new Dictionary<BoardCell, List<int>>();
        for (int i = 0; i < units.Count; i++)
        {
            var spec = units[i].Spec;
            if ((!spec.CanBlock && spec.ReconstructAt is null) || spec.Block + (spec.Skill?.SelfBlock ?? 0) <= 0) continue;
            var cell = spec.ReconstructAt ?? spec.Cell;
            if (!byCell.TryGetValue(cell, out var list)) byCell[cell] = list = [];
            list.Add(i);
        }
        var spawns = Schedule(lanes, w);
        var enemies = new List<Enemy>(spawns.Count);
        var pending = new List<(double Time, int Lane, double S, EnemyProfile Profile)>();
        var clouds = new List<Cloud>();
        var leaksByLane = new Dictionary<int, int>();
        var leakedByName = new Dictionary<string, int>();
        var bosses = new List<Enemy>();
        int next = 0, killed = 0, leaked = 0, chargedChildLeaks = 0;
        double end = w.TimeLimit ?? ((w.Spawns is { Count: > 0 } real ? real.Max(s => s.Time) : w.SpawnSeconds) + w.TailSeconds);
        bool anyAura = spawns.Any(s => HasAura(s.Profile)), anySlow = spawns.Any(s => s.Profile.Traits.SlowFactor < 1);
        double t = 0, dp = 10;
        int downOperators = 0;
        var usedReserves = new HashSet<string>();

        var fields = new List<Field>();
        var aftertones = new List<Aftertone>();
        var fireShots = new List<FireShot>();
        var blazeCasters = units.Select((u, i) => (u, i)).Where(v => v.u.Spec.Skill is { BurnBonus: > 0 }).ToArray();
        var fireCasters = units.Where(u => u.Spec.Skill is { FireWave: > 0 }).ToArray();
        List<Enemy>? observed = w.CollectDetails ? [] : null;

        Enemy Add(EnemyProfile p, int lane, double s)
        {
            var e = new Enemy
            {
                Profile = p, Lane = lane, S = s, Hp = p.Hp, MaxHp = p.Hp, Flying = lanes[lane].Flying,
                Cooldown = p.Interval, Shield = p.Traits.Shield,
            };
            (e.X, e.Y) = paths[lane].At(s);
            Locate(e);
            enemies.Add(e);
            observed?.Add(e);
            if (p.Rank == "BOSS") bosses.Add(e);
            return e;
        }

        static void Locate(Enemy e)
        {
            e.Cell = new BoardCell((int)Math.Floor(e.X), (int)Math.Floor(e.Y));
            e.CellBit = Bit(e.Cell);
        }

        void Release(Enemy e)
        {
            if (e.BlockedBy >= 0) units[e.BlockedBy].Blocking = Math.Max(0, units[e.BlockedBy].Blocking - e.T.BlockCost);
            e.BlockedBy = -1;
        }

        void Fall(Unit u, int index)
        {
            u.Down = true;
            if (u.Spec.Summon is { } sm) { u.NextUp = t + sm.Cooldown; u.UpUntil = double.PositiveInfinity; }
            else if (u.Spec.KazimierzUpper && u.Spec.CanBlock &&
                units.FirstOrDefault(candidate => candidate.Spec.ReserveFor == (u.Spec.ReserveFor ?? u.Spec.Key)
                    && !usedReserves.Contains(candidate.Spec.ReservePiece!)) is { } reserve)
            {
                usedReserves.Add(reserve.Spec.ReservePiece!);
                reserve.Down = false;
                reserve.DeployedAt = t;
            }
            else if (w.TimeLimit.HasValue && u.Spec.RedeploySeconds > 0)
            {
                u.NextUp = t + u.Spec.RedeploySeconds;
                downOperators++;
            }
            u.Blocking = 0;
            foreach (var e in enemies)
                if (e.BlockedBy == index) e.BlockedBy = -1;
        }

        // A stun at (x, y): every operator whose eight surrounding cells include it saw one.
        void SawStun(double x, double y)
        {
            int col = (int)Math.Floor(x), row = (int)Math.Floor(y);
            foreach (var o in units)
                if (!o.Down && Math.Abs(o.Spec.Cell.Col - col) <= 1 && Math.Abs(o.Spec.Cell.Row - row) <= 1) o.StunsSeen++;
        }

        void Stun(Unit u, int index, double seconds)
        {
            SawStun(u.X, u.Y);
            u.StunUntil = Math.Max(u.StunUntil, t + seconds);
            u.Blocking = 0;
            foreach (var e in enemies)
                if (e.BlockedBy == index) e.BlockedBy = -1;
        }

        void Hurt(Unit u, int index, double damage)
        {
            if (u.Recovering)
            {
                double absorbed = Math.Min(damage, u.DownShield);
                u.DownShield -= absorbed; damage -= absorbed;
            }
            u.Taken += Math.Max(0, Math.Min(damage, u.Hp));
            u.Hp -= damage;
            if (u.Hp > 0 || u.Down) return;
            if (u.Spec.ReconstructAt is not null && u.ActiveUntil > t)
            {
                u.ActiveUntil = t;
                u.ImmortalUntil = t + 1;
            }
            if (u.Spec.Skill is { BurnBonus: > 0 } && !u.Recovering)
            {
                u.Recovering = true; u.DownShield = 6000; u.Hp = 1;
                u.NoHealUntil = u.SkillLockUntil = double.PositiveInfinity;
                u.ActiveUntil = t; u.BlockCap = 0; u.Blocking = 0;
                foreach (var e in enemies) if (e.BlockedBy == index) e.BlockedBy = -1;
                return;
            }
            if (u.Spec.Skill is { Snow: true } && !u.BlessingUsed)
            {
                u.BlessingUsed = true; u.Hp = u.MaxHp;
                Stun(u, index, 4);
                foreach (var e in enemies)
                    if (!e.Done && InRange(u, e)) e.StunUntil = e.FrozenUntil = Math.Max(e.StunUntil, t + 8);
                return;
            }
            if (u.ImmortalUntil > t) u.Hp = 1;
            else Fall(u, index);
        }

        void Burst(Unit u, int index, ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Neural: Hurt(u, index, 1000); if (!u.Down) Stun(u, index, 10); break;
                case ElementKind.Erosion: u.DefLoss += 100; Hurt(u, index, Math.Max(800 - u.Def, 40)); break;
                case ElementKind.Burning: u.BurnUntil = t + 10; Hurt(u, index, 1200 * (1 - u.Res(t) / 100)); break;
                case ElementKind.Apoptosis: u.SkillLockUntil = t + 15; u.ApoptosisUntil = t + 15; break;
            }
        }

        // One enemy hit on an operator, with its crit, element injury and bleeding.
        void Strike(Enemy e, int index, double scale = 1)
        {
            var u = units[index];
            var p = e.Profile;
            double atk = e.Atk * scale;
            double dmg = p.Arts ? Math.Max(atk * (1 - u.Res(t) / 100), 0.05 * atk) : Math.Max(atk - u.Def, 0.05 * atk);
            if (!p.Arts) dmg *= (1 - u.Spec.Dodge) * (SkillOn(u) && u.Stance == 2 ? 1 - u.Spec.Skill!.StanceDodge : 1);
            dmg *= 1 - Math.Min(1, Math.Max(u.DocShelter, u.Spec.Sanctuary));
            if (u.Spec.Skill is { Charge: SkillCharge.Hit } hs && u.ActiveUntil <= t && !u.Empowered) u.Sp = Math.Min(hs.Cost, u.Sp + 1);
            if (e.T.CritScale > 1 && (e.Struck ??= []).Add(index))
            {
                dmg *= e.T.CritScale;
                u.NoHealUntil = Math.Max(u.NoHealUntil, t + e.T.CritNoHeal);
            }
            if (u.Spec.Skill is { ArtsEcho: > 0 } echo)
            {
                double amount = (p.Arts ? echo.ArtsEcho : 0) + (SkillOn(u) ? echo.ArtsTrait : 0);
                if (amount > 0) Wear(e, index, ArtsOn(e, PanelAttack(u) * (1 + u.HitsTaken * echo.AtkPerHit) * amount, u.Spec.ResIgnore), arts: true);
            }
            if (u.Spec.Skill is { AtkPerHit: > 0 } && u.ActiveUntil > t) u.HitsTaken = Math.Min(10, u.HitsTaken + 1);
            Hurt(u, index, dmg);
            if (u.Spec.KazimierzAlly)
                foreach (var (source, owner) in liberators)
                    if (!source.Down) { Wear(e, owner, PanelAttack(source) * source.Spec.Skill!.Reflect); break; }
            if (u.Down) return;
            if (u.Spec.Skill is { CastOnDamage: true } && Ready(u) && !u.Stunned(t)) Cast(u);
            if (e.T.Element != ElementKind.None && e.T.ElementRatio > 0)
            {
                int k = (int)e.T.Element;
                if (u.GaugeReady[k] <= t)
                {
                    u.Gauge[k] += atk * e.T.ElementRatio;
                    if (u.Gauge[k] >= BurstAt)
                    {
                        u.Gauge[k] = 0;
                        u.GaugeReady[k] = t + ElementCooldown;
                        Burst(u, index, e.T.Element);
                    }
                }
            }
            if (e.T.BleedDps > 0)
            {
                u.BleedDps = Math.Max(u.BleedDps, e.T.BleedDps);
                u.BleedUntil = t + e.T.BleedSeconds;
            }
        }

        // One enemy attack on an operator: the hit, then its splash, chain and stun; or, when ready, its line sweep.
        void Attack(Enemy e, int index)
        {
            var T = e.T;
            e.Attacks++;
            var target = units[index];
            double tx = target.X, ty = target.Y;
            if (T.LineCooldown > 0 && e.LineReady <= t)
            {
                e.LineReady = t + T.LineCooldown;
                double dx = tx - e.X, dy = ty - e.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) { dx = -1; dy = 0; len = 1; }  // on its blocker's tile: towards the defence point
                dx /= len; dy /= len;
                for (int j = 0; j < units.Count; j++)
                {
                    if (units[j].Down) continue;
                    double px = units[j].X - e.X, py = units[j].Y - e.Y;
                    double along = px * dx + py * dy, off = Math.Abs(px * dy - py * dx);
                    if (along >= -0.5 && along <= 4 && off <= 0.5) Strike(e, j, T.LineScale);
                }
                return;
            }
            Strike(e, index);
            if (T.SplashRadius > 0 && (T.SplashFirst ? e.Attacks == 1 : T.SplashEvery <= 1 || e.Attacks % T.SplashEvery == 0))
                for (int j = 0; j < units.Count; j++)
                    if (j != index && !units[j].Down && Dist(units[j].X, units[j].Y, tx, ty) <= T.SplashRadius)
                        Strike(e, j, T.SplashScale);
            if (T.ChainTargets > 1)
            {
                var hit = new HashSet<int> { index };
                double scale = 1, cx = tx, cy = ty;
                for (int k = 1; k < T.ChainTargets; k++)
                {
                    int jump = -1;
                    double best = 1.6;
                    for (int j = 0; j < units.Count; j++)
                    {
                        if (units[j].Down || hit.Contains(j)) continue;
                        double d = Dist(units[j].X, units[j].Y, cx, cy);
                        if (d <= best) { best = d; jump = j; }
                    }
                    if (jump < 0) break;
                    hit.Add(jump);
                    scale *= T.ChainScale;
                    Strike(e, jump, scale);
                    (cx, cy) = (units[jump].X, units[jump].Y);
                }
            }
            if (T.StunEvery > 0 && e.Attacks % T.StunEvery == 0)
            {
                if (T.StunRadius > 0)
                {
                    for (int j = 0; j < units.Count; j++)
                        if (!units[j].Down && Dist(units[j].X, units[j].Y, e.X, e.Y) <= T.StunRadius) Stun(units[j], j, T.StunSeconds);
                }
                else if (!target.Down) Stun(target, index, T.StunSeconds);
            }
        }

        // While a skill runs (or a strong action is armed) the operator reaches its skill range.
        bool SkillOn(Unit u) => u.ActiveUntil > t || u.Empowered;
        bool InRange(Unit u, Enemy e)
        {
            bool on = SkillOn(u);
            return e.CellBit >= 0 ? Has(on ? u.SkillMask : u.Mask, e.CellBit) : (on ? u.SkillRange : u.Range).Contains(e.Cell);
        }
        bool SeabornCovers(Unit u, BoardCell cell) => u.Spec.SeabornAt is { } token && t % 55 < 25
            && Math.Abs(cell.Col - token.Col) <= 1 && Math.Abs(cell.Row - token.Row) <= 1;
        bool Revealed(Enemy e) => e.CellBit >= 0 ? Has(revealedMask, e.CellBit) : revealed.Contains(e.Cell);

        // Skills: ready once SP reaches the cost; casting starts the boosted window (or arms one strong action).
        bool Ready(Unit u) => u.Spec.Skill is { } sk && u.ActiveUntil <= t && !u.Empowered && u.Sp >= sk.Cost && u.SkillLockUntil <= t;

        double PanelAttack(Unit u)
        {
            double atk = u.Spec.Atk;
            if (u.Spec.Skill is not null) atk /= Math.Max(1e-9, u.Spec.SkillFactor);
            else if (u.SkillLockUntil > t) atk /= Math.Max(1, u.Spec.SkillFactor);
            atk = (u.Tile == TerrainKind.Infection ? atk * InfectionAtk : atk) + u.InspiredAtk;
            if (u.Spec.Skill is { Liberator: true })
                atk *= 1 + (SkillOn(u) ? Math.Max(0, 2 * u.Ramp - .1 * (u.Kills - u.KillsAtCast)) : u.Ramp);
            return atk;
        }

        void Cast(Unit u)
        {
            var sk = u.Spec.Skill!;
            u.Sp = Math.Max(0, u.Sp - sk.Cost);
            u.Casts++;
            u.KillsAtCast = u.Kills;
            if (sk.FireWave > 0) { u.NextPulse = t; u.Overdrive = false; }
            if (sk.StanceAspd > 0) u.Stance = rng.Next(3);
            u.HitsTaken = 0;
            if (sk.FireWave == 0) u.NextPulse = t + (sk.ShareHp ? 2 : 1);
            if (sk.Ammo > 0) { u.Ammo = sk.Ammo; u.ActiveUntil = double.PositiveInfinity; }
            else if (sk.Slashes is { } series)
            {
                u.SlashIndex = 0;
                u.NextSlash = t;
                u.ActiveUntil = t + series.Count * series.Interval;
            }
            else if (sk.Duration > 0) u.ActiveUntil = t + sk.Duration; else u.Empowered = true;
            double baseAtk = u.Spec.Atk / Math.Max(1e-9, u.Spec.SkillFactor) * (1 + u.Spec.CastAtk * Math.Min(10, u.Casts));
            if (sk.Snow)
                foreach (var e in enemies)
                    if (Visible(e) && !e.Flying && e.Profile.Rank != "BOSS" && InRange(u, e))
                    {
                        Release(e);
                        e.LureUntil = t + 10;
                        // 几何路线内取最靠近施术者的可达点，沿路往返。
                        e.LurePosition = paths[e.Lane].Nearest(u.X, u.Y);
                    }
            if (sk.HealBurst > 0)
                foreach (int j in u.SkillAllies)
                    if (!units[j].Down) Heal(u, units[j], sk.HealBurst * baseAtk);
            if (sk.HealPerSecond > 0) u.HealUntil = t + (sk.Duration > 0 ? sk.Duration : 10);
            if (u.Spec.SkillRevealSeconds > 0)
                foreach (var e in enemies)
                    if (!e.Done && InRange(u, e)) e.RevealedUntil = t + u.Spec.SkillRevealSeconds;
        }

        // The skill's multiplier on this action (casting first if ready); attack-charged skills gain SP otherwise.
        // skillHit: this action is a skill action (the one that casts included), so the skill's effects apply.
        double SkillBoost(Unit u, out bool skillHit)
        {
            skillHit = false;
            if (u.Spec.Skill is not { } sk) return 1;
            if (!sk.CastOnDamage && Ready(u)) Cast(u);
            skillHit = true;
            if (u.ActiveUntil > t) return sk.Gain;
            if (u.Empowered) { u.Empowered = false; return sk.Gain; }
            skillHit = false;
            if (sk.Charge == SkillCharge.Attack) u.Sp = Math.Min(sk.Cost, u.Sp + 1);
            return 1;
        }

        // One operator hit on an enemy: embers take hits, shields absorb them, else damage by type.
        void Hit(Unit u, int i, Enemy target, double atk, bool skillHit)
        {
            if (target.Done) return;
            if (target.Ember)
            {
                // An ember takes a number of hits, whatever their damage.
                if (--target.EmberHits <= 0) { target.Ember = false; Die(target, i, final: true); }
                return;
            }
            if (target.Shield > 0) { target.Shield--; return; }
            var sk = skillHit ? u.Spec.Skill : null;
            if (sk is { BurnBonus: > 0 } && target.BurnEnds > t)
            {
                Wear(target, i, atk * sk.BurnBonus);
                if (target.Done || target.Still) return;
            }
            var kind = sk is { Damage: not SkillDamage.Same } ? sk.Damage : u.Spec.Arts ? SkillDamage.Arts : SkillDamage.Same;
            double dmg;
            if (kind == SkillDamage.True) dmg = atk;
            else if (kind == SkillDamage.Arts)
            {
                double res = Math.Clamp(target.Res + target.T.RefractionRes + target.AuraRes + target.ResAdd - (target.BurnEnds > t ? 20 : 0) - (sk?.ResIgnore ?? 0) - u.Spec.ResIgnore, 0, 100);
                dmg = Math.Max(atk * (1 - res / 100), 0.05 * atk);
            }
            else
            {
                double def = Math.Max(0, (target.Def + target.AuraDef) * target.DefScale * (1 - u.Spec.DefIgnoreRatio) - u.Spec.DefIgnore);
                dmg = Math.Max(atk - def, 0.05 * atk);
            }
            double arts = kind == SkillDamage.Arts ? dmg : 0;
            if (sk is { StanceAspd: > 0 } && u.Stance == 0) target.AttackSlowUntil = t + 5;
            if (u.Spec.Weakness && kind != SkillDamage.True)
            {
                double physical = Math.Max(atk - Math.Max(0, (target.Def + target.AuraDef) * target.DefScale
                    * (1 - u.Spec.DefIgnoreRatio) - u.Spec.DefIgnore), .05 * atk);
                dmg = Math.Max(physical, ArtsOn(target, atk, (sk?.ResIgnore ?? 0) + u.Spec.ResIgnore));
            }
            // 附加伤害以面板攻击力为基数，不重复乘技能单次伤害倍率。
            double panelAtk = u.Spec.Atk / Math.Max(1e-9, u.Spec.SkillFactor) * (1 + u.Spec.CastAtk * Math.Min(10, u.Casts));
            double artsRider = sk is { ArtsTrait: > 0 } ? ArtsOn(target, atk * sk.ArtsTrait, u.Spec.ResIgnore) : 0;
            if (u.Spec.ExtraArts > 0) artsRider += ArtsOn(target, u.Spec.ExtraArts * atk, u.Spec.ResIgnore);
            if (u.Spec.EquipmentExtraArts > 0) artsRider += ArtsOn(target, u.Spec.EquipmentExtraArts * panelAtk, u.Spec.ResIgnore);
            dmg += artsRider;
            arts += artsRider;
            dmg += u.Spec.ExtraTrue * panelAtk;
            if (u.Spec.KazimierzUpper && (t <= u.DeployedAt + 30 || SkillOn(u))) dmg += .65 * panelAtk;
            if (u.Spec.DistantDamage > 0 && Dist(u.X, u.Y, target.X, target.Y) >= 3) dmg *= 1 + u.Spec.DistantDamage;
            double artsShare = dmg > 0 ? arts / dmg : 0;
            dmg *= u.Spec.OutputScale * u.Spec.DamageScale * (1 + target.Fragile);
            if (target.BlockedBy < 0) dmg *= target.T.UnblockedTaken;
            if (u.Spec.Pattern.HealFromDamage > 0)
            {
                Unit? patient = null;
                foreach (int j in u.Patients)
                {
                    var ally = units[j];
                    if (!ally.Down && !ally.Spec.NoHeal && ally.NoHealUntil <= t && ally.Hp < ally.MaxHp &&
                        (patient is null || ally.Hp / ally.MaxHp < patient.Hp / patient.MaxHp)) patient = ally;
                }
                if (patient is not null) Heal(u, patient, dmg * u.Spec.Pattern.HealFromDamage);
            }
            dmg = Math.Min(dmg, target.Hp);
            target.Hp -= dmg;
            if (w.CollectDetails) target.Taken += dmg;
            if (dmg > 0) target.Damaged();
            u.Damage += dmg;
            if (u.Spec.Lifesteal > 0 && !u.Spec.NoHeal) Heal(u, u, dmg * u.Spec.Lifesteal);
            if (target.Hp <= 0) { Die(target, i); return; }
            ApplySiracusa(target, u);
            if (u.Spec.KazimierzAlly)
                foreach (var (source, owner) in liberators)
                    if (!source.Down && SkillOn(source) && InRange(source, target))
                        Wear(target, owner, PanelAttack(source) * source.Spec.Skill!.MarkDamage);
            if (u.Spec.IberiaDot > 0) ApplyIberia(target, i);
            if (u.Spec.BurnRatio > 0 && artsShare > 0) Burn(target, i, dmg * artsShare);
            if (target.Done || target.Still) return;
            if (u.Spec.DotDps > 0)
            {
                target.PoisonDps = Math.Max(target.PoisonDps, u.Spec.DotDps);
                target.PoisonUntil = t + u.Spec.DotSeconds;
                target.PoisonBy = i;
            }
            if (u.Spec.HitControlSeconds > 0 && rng.NextDouble() < u.Spec.HitControlChance)
            {
                if (u.Spec.HitStuns)
                {
                    if (target.StunUntil <= t) SawStun(target.X, target.Y);
                    target.StunUntil = Math.Max(target.StunUntil, t + u.Spec.HitControlSeconds);
                }
                else target.StopUntil = Math.Max(target.StopUntil, t + u.Spec.HitControlSeconds);
            }
            if ((sk?.Push ?? (skillHit ? null : u.Spec.Push)) is { } push) Displace(target, u, push);
            if (sk is { ControlSeconds: > 0 } c && (c.ControlChance >= 1 || rng.NextDouble() < c.ControlChance))
            {
                if (c.ControlStuns)
                {
                    if (target.StunUntil <= t) SawStun(target.X, target.Y);
                    target.StunUntil = Math.Max(target.StunUntil, t + c.ControlSeconds);
                }
                else target.StopUntil = Math.Max(target.StopUntil, t + c.ControlSeconds);
            }
        }

        // Healing by an operator: what actually filled the patient's HP counts as its healing.
        void Heal(Unit by, Unit to, double amount)
        {
            if (to.Spec.NoHeal || to.NoHealUntil > t) return;
            double before = to.Hp;
            to.Hp = Math.Min(to.MaxHp, to.Hp + amount);
            by.Healed += Math.Max(0, to.Hp - before);
        }

        // Arts damage on an enemy after its resistance (talent riders, poison, zones).
        double ArtsOn(Enemy e, double amount, double ignore)
        {
            double res = Math.Clamp(e.Res + e.T.RefractionRes + e.AuraRes + e.ResAdd - (e.BurnEnds > t ? 20 : 0) - ignore, 0, 100);
            return Math.Max(amount * (1 - res / 100), 0.05 * amount);
        }

        bool Visible(Enemy e) => !e.Done && !e.Still && (!(e.T.Invisible || e.Tile == TerrainKind.Hide) ||
            e.Ember || e.BlockedBy >= 0 || e.RevealedUntil > t || Revealed(e));

        void FireVolley(Unit u, int owner, int half)
        {
            bool horizontal = u.Spec.Facing is Facing.Left or Facing.Right;
            int sign = u.Spec.Facing is Facing.Right or Facing.Down ? 1 : -1;
            var bounds = u.Spec.FieldBounds;
            int low = horizontal ? 0 : bounds.Left, high = horizontal ? bounds.Bottom : bounds.Right;
            int width = Math.Min(high - low + 1, 2 * half + 1);
            int first = Math.Clamp((horizontal ? u.Cell.Row : u.Cell.Col) - half, low, high - width + 1);
            int back = horizontal ? bounds.Left : 0, front = horizontal ? bounds.Right : bounds.Bottom;
            int start = Math.Clamp((horizontal ? u.Cell.Col : u.Cell.Row) - sign * 11, back, front);
            double reach = Math.Abs((sign > 0 ? front : back) - start) + 5;
            double attack = PanelAttack(u) * u.Spec.Skill!.Gain * u.Spec.Skill.FireWave;
            var hit = new HashSet<Enemy>();
            for (int lane = first; lane < first + width; lane++)
                fireShots.Add(new(owner, (horizontal ? start : lane) + .5, (horizontal ? lane : start) + .5,
                    horizontal ? sign : 0, horizontal ? 0 : sign, reach, attack, hit));
        }

        void MoveFireShots()
        {
            for (int j = fireShots.Count - 1; j >= 0; j--)
            {
                var s = fireShots[j];
                double step = Math.Min(s.Left, 2.3 * w.Step), first = double.PositiveInfinity;
                Enemy? target = null;
                foreach (var e in enemies)
                {
                    if (!Visible(e)) continue;
                    double along = (e.X - s.X) * s.Dx + (e.Y - s.Y) * s.Dy;
                    double across = Math.Abs((e.X - s.X) * s.Dy - (e.Y - s.Y) * s.Dx);
                    if (across > .5 || along < -.5 || along > step + .5 || along >= first) continue;
                    target = e; first = along;
                }
                if (target is null)
                {
                    s.X += step * s.Dx; s.Y += step * s.Dy; s.Left -= step;
                    if (s.Left <= 0) fireShots.RemoveAt(j);
                    continue;
                }
                foreach (var e in enemies)
                {
                    if (!Visible(e) || Dist(e.X, e.Y, target.X, target.Y) > 1.5) continue;
                    double damage = s.Attack * (s.Hit.Add(e) ? 1 : .15) * (1 + .03 * units[s.Owner].Rage);
                    bool arts = e.BurnEnds <= t;
                    Wear(e, s.Owner, arts ? ArtsOn(e, damage, 0) : damage, arts);
                }
                fireShots.RemoveAt(j);
            }
        }

        void ChainHeal(Unit source, Unit? first, BoardCell cell, double amount, bool free)
        {
            var seen = new HashSet<Unit>();
            void Link(Unit ally)
            {
                Heal(source, ally, amount);
                ally.ChainHasteUntil = source.ChainHasteUntil = t + 10;
                seen.Add(ally);
            }
            if (first is not null) Link(first);
            int remaining = free ? 3 : 2;
            for (int j = 0; j < remaining; j++)
            {
                Unit? next = null;
                foreach (var ally in units)
                    if (!ally.Down && !ally.Spec.NoHeal && ally.NoHealUntil <= t && !seen.Contains(ally) &&
                        Math.Abs(ally.Cell.Col - cell.Col) <= 1 && Math.Abs(ally.Cell.Row - cell.Row) <= 1 &&
                        (next is null || ally.Hp / ally.MaxHp <= next.Hp / next.MaxHp)) next = ally;
                if (next is null) break;
                if (j > 0 || !free) amount *= source.Spec.Skill!.ChainFalloff;
                Link(next);
                cell = next.Cell;
            }
        }

        void ApplySiracusa(Enemy target, Unit u)
        {
            if (u.Spec.SiracusaPulse > 0 && rng.NextDouble() < .3)
            {
                // 六种异常分别记时；停顿不在叙拉古的触发集合内。
                var states = target.SiracusaStatus ??= new double[6];
                int status = rng.Next(states.Length);
                states[status] = t + u.Spec.SiracusaStatusSeconds;
                if (status == 0) target.StunUntil = Math.Max(target.StunUntil, states[status]);
                if (status == 3) target.ColdUntil = Math.Max(target.ColdUntil, states[status]);
                if (status == 5) target.StopUntil = Math.Max(target.StopUntil, states[status]);
            }
        }

        void ApplyIberia(Enemy e, int owner)
        {
            var u = units[owner];
            if (u.Spec.IberiaDot == 0 || e.Done || e.Still || e.Shield > 0) return;
            var last = e.IberiaLastHit ??= [];
            if (last.TryGetValue(owner, out double at) && t - at < .2 - 1e-6) return;
            last[owner] = t;
            if (t > e.IberiaEnds)
            {
                e.IberiaStacks = e.IberiaSlow = 0;
                e.IberiaTick = t + 1;
                e.IberiaOwner = owner;
            }
            e.IberiaEnds = t + 10;
            e.IberiaStacks = Math.Min(10, e.IberiaStacks + 1);
            double panel = PanelAttack(u);
            e.IberiaDps = e.IberiaStacks * u.Spec.IberiaDot * panel * (u.Spec.IberiaUpper ? 2 : 1);
            e.IberiaSlow = Math.Min(20, e.IberiaSlow + 1);
            if (rng.NextDouble() >= .2) return;
            foreach (var other in enemies)
                if (other != e && Dist(other.X, other.Y, e.X, e.Y) <= 1.7)
                    Wear(other, owner, ArtsOn(other, 3 * panel, u.Spec.ResIgnore), arts: true);
        }

        void Burn(Enemy e, int owner, double arts)
        {
            if (e.BurnEnds > t || e.Done || e.Still) return;
            e.BurnGauge += units[owner].Spec.BurnRatio * arts;
            if (e.BurnGauge < (e.Profile.Rank == "BOSS" ? 2000 : 1000)) return;
            e.BurnGauge = 0;
            e.BurnEnds = t + 10;
            e.TrailUntil = t + 5;
            double burst = Math.Min(7000, e.Hp);
            e.Hp -= burst;
            units[owner].Damage += burst;
            if (w.CollectDetails) e.Taken += burst;
            if (e.Hp <= 0) Die(e, owner);
            foreach (var (caster, index) in blazeCasters)
            {
                if (caster.Down || caster.Recovering) continue;
                var skill = caster.Spec.Skill!;
                double panel = PanelAttack(caster);
                if (SkillOn(caster)) { panel *= skill.Gain; caster.Ammo = Math.Min(skill.Ammo, caster.Ammo + 2); }
                if (!e.Done && !e.Still) Wear(e, index, panel * skill.BurstElement);
                Heal(caster, caster, skill.BurstHeal * caster.MaxHp);
            }
        }

        // Damage over time from an operator (poison, zones): no hit, just HP lost.
        void Wear(Enemy e, int by, double dmg, bool arts = false)
        {
            if (e.Done || e.Still || e.Shield > 0) return;
            dmg = Math.Min(dmg * (by >= 0 ? units[by].Spec.OutputScale : 1) * (1 + e.Fragile), e.Hp);
            e.Hp -= dmg;
            if (w.CollectDetails) e.Taken += dmg;
            if (dmg > 0) e.Damaged();
            if (by >= 0) units[by].Damage += dmg;
            if (e.Hp <= 0) Die(e, by);
            else if (arts && by >= 0 && units[by].Spec.BurnRatio > 0) Burn(e, by, dmg);
        }

        // Pushed away from the operator or dragged towards it, less the heavier it is; at most once a second.
        void Displace(Enemy e, Unit u, PushData push)
        {
            if (e.Done || e.Still || t - e.PushedAt < 1) return;
            double cells = push.Cells * Math.Max(0, 1 - 0.15 * (e.Profile.Weight - 1));
            if (cells < 0.05) return;
            e.PushedAt = t;
            var path = paths[e.Lane];
            double s = e.S;
            if (push.Pull)
            {
                double to = path.Nearest(u.X, u.Y);
                s = to > s ? Math.Min(to, s + cells) : Math.Max(to, s - cells);
            }
            else s = Math.Max(0, s - cells);
            if (Math.Abs(s - e.S) < 1e-6) return;
            Release(e);
            e.S = s;
            (e.X, e.Y) = path.At(s);
            Locate(e);
        }

        // An enemy brought to 0 HP: it may turn into an ember (every time) or be reborn (once) first; otherwise it
        // dies and its death effects play out. An ember beaten down by its hits is final.
        void Die(Enemy e, int killer, bool final = false)
        {
            if (e.T.EmberHits > 0 && !final)
            {
                if (!e.Ember) e.LivesLost++;
                e.Ember = true;
                e.EmberHits = e.T.EmberHits;
                e.StateTimer = e.T.EmberSeconds;
                // A blocked enemy stands on its blocker's tile, so the ember lies there, within the blocker's reach.
                if (e.BlockedBy >= 0)
                {
                    var cell = units[e.BlockedBy].Cell;
                    e.X = cell.Col + 0.5;
                    e.Y = cell.Row + 0.5;
                    Locate(e);
                }
                Release(e);
                return;
            }
            if (e.T.RebornSeconds > 0 && !e.UsedReborn)
            {
                e.UsedReborn = true;
                e.LivesLost++;
                e.Reviving = true;
                e.StateTimer = e.T.RebornSeconds;
                Release(e);
                return;
            }
            e.Done = true;
            e.Ember = false;
            e.Killed = true;
            if (!e.Child) killed++;
            foreach (var o in units)
                if (!o.Down && (e.CellBit >= 0 ? Has(o.Mask, e.CellBit) : o.Range.Contains(e.Cell))) o.KillsSeen++;
            if (killer >= 0) units[killer].Kills++;
            Release(e);
            foreach (var child in e.T.OnDeath)
                for (int k = 0; k < child.Count; k++)
                    pending.Add((t + child.Delay, e.Lane, e.S, child.Profile));
            if (e.T.CloudSeconds > 0)
            {
                bool atSource = e.T.CloudAtSource && killer >= 0;
                double dps = e.T.CloudDps > 0 ? e.T.CloudDps : e.T.CloudAtkScale * e.Profile.Atk;
                clouds.Add(new Cloud(atSource ? units[killer].X : e.X, atSource ? units[killer].Y : e.Y, e.T.CloudRange, dps, t + e.T.CloudSeconds));
            }
            if (e.T.DeathShieldAura > 0)
                foreach (var o in enemies)
                    if (!o.Done && o != e && Dist(o.X, o.Y, e.X, e.Y) <= 1.5) o.Shield = Math.Max(o.Shield, e.T.DeathShieldAura);
        }

        for (; t < end; t += w.Step)
        {
            dp = Math.Min(99, dp + w.Step);
            if (downOperators > 0)
                foreach (var u in units)
                {
                    if (!u.Down || u.Spec.RedeploySeconds <= 0 || u.NextUp > t || u.Spec.RedeployCost > dp) continue;
                    dp -= u.Spec.RedeployCost;
                    downOperators--;
                    u.Down = false;
                    u.DeployedAt = t;
                    u.Hp = u.MaxHp = u.Spec.Hp;
                    u.BlockCap = u.Spec.Skill is { Liberator: true } ? 0 : u.Spec.Block;
                    u.Cooldown = 0;
                    u.Sp = u.Spec.Skill?.Init ?? 0;
                    u.ActiveUntil = u.HealUntil = u.StunUntil = u.NoHealUntil = u.SkillLockUntil = -1;
                    u.BurnUntil = u.ApoptosisUntil = u.BleedUntil = u.ImmortalUntil = -1;
                    u.BuffOn = u.Empowered = false;
                    u.SlashIndex = -1;
                    u.Ramp = u.Spec.Skill?.InitialRamp ?? 0;
                    u.InspiredHp = u.DefLoss = 0;
                    u.StoredEnergy = u.HitsTaken = 0;
                    u.Recovering = false; u.FireTiles?.Clear();
                    u.ConstructHp = u.Spec.Skill?.ReconstructHp ?? 0; u.ConstructReady = t; u.ChainHasteUntil = 0;
                    u.BlessingUsed = false; u.SnowTiles?.Clear(); u.SnowPrevious?.Clear(); u.SnowClock = 0;
                    u.Stance = -1; u.Rage = 0; u.NextRage = t + 3;
                    Array.Clear(u.Gauge);
                    Array.Clear(u.GaugeReady);
                }
            if (anySummon)
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u.Spec.Summon is not { } sm) continue;
                    if (u.Down && t >= u.NextUp)
                    {
                        u.Down = false;
                        u.Hp = u.MaxHp;
                        u.Cooldown = 0;
                        u.UpUntil = sm.Duration > 0 ? t + sm.Duration : double.PositiveInfinity;
                    }
                    else if (!u.Down && t >= u.UpUntil) Fall(u, i);
                }
            while (next < spawns.Count && spawns[next].Time <= t)
            {
                var (_, lane, p) = spawns[next++];
                Add(p, lane, 0);
            }
            for (int k = pending.Count - 1; k >= 0; k--)
                if (pending[k].Time <= t)
                {
                    Add(pending[k].Profile, pending[k].Lane, pending[k].S).Child = true;
                    pending.RemoveAt(k);
                }

            if (w.CollectDetails) foreach (var e in enemies) e.Seconds += w.Step;

            if (anyReveal)
            {
                revealed.Clear();
                revealedMask = 0;
                foreach (var u in units)
                    if (u.Spec.Reveals && !u.Down) { revealed.UnionWith(u.Range); revealedMask |= u.Mask; }
            }

            // Auras: enemies buff the enemies around them and slow the operators around them.
            // (The few enemies that carry one are listed first, so each enemy checks only those.)
            if (anyAura)
            {
                sources.Clear();
                foreach (var s in enemies)
                    if (!s.Done && !s.Still && HasAura(s.Profile)) sources.Add(s);
                foreach (var e in enemies)
                {
                    if (e.Done) continue;
                    e.AuraDef = e.AuraRes = 0;
                    foreach (var s in sources)
                        if (s != e && Dist(s.X, s.Y, e.X, e.Y) <= s.T.AuraRange)
                        {
                            e.AuraDef = Math.Max(e.AuraDef, s.T.AuraDef);
                            e.AuraRes = Math.Max(e.AuraRes, s.T.AuraRes);
                        }
                }
            }
            if (anySlow)
            {
                sources.Clear();
                foreach (var s in enemies)
                    if (!s.Done && !s.Still && s.T.SlowFactor < 1) sources.Add(s);
                foreach (var u in units)
                {
                    u.Slow = 1;
                    foreach (var s in sources)
                        if (Dist(s.X, s.Y, u.X, u.Y) <= s.T.SlowRange) u.Slow = Math.Min(u.Slow, s.T.SlowFactor);
                }
            }

            // Enemies move, get blocked, fight their blocker, or leak.
            foreach (var e in enemies)
            {
                if (e.Done) continue;
                var prof = e.Profile;
                if (e.Still)
                {
                    // Embers and the reborn wait where they fell; an ember left alone comes back at full HP.
                    e.StateTimer -= w.Step;
                    if (e.StateTimer <= 0)
                    {
                        e.Ember = e.Reviving = false;
                        e.Hp = e.MaxHp;
                    }
                    continue;
                }
                if (e.T.Regen > 0) e.Hp = Math.Min(e.MaxHp, e.Hp + e.T.Regen * w.Step * (t <= e.HealCutUntil ? .5 : 1));
                if (e.IberiaEnds >= t && t + 1e-6 >= e.IberiaTick)
                {
                    e.IberiaTick += 1;
                    Wear(e, e.IberiaOwner, ArtsOn(e, e.IberiaDps, 0), arts: true);
                    if (e.Done || e.Still) continue;
                }
                if (e.PoisonUntil > t)
                {
                    Wear(e, e.PoisonBy, ArtsOn(e, e.PoisonDps * w.Step, 0), arts: true);
                    if (e.Done) continue;
                }
                e.Tile = e.Flying ? TerrainKind.None : TileAt(e.Cell);
                if (e.Tile == TerrainKind.Infection) e.Infected = true;
                if (e.Tile == TerrainKind.Mire)
                {
                    if (t >= e.MireNext)
                    {
                        e.MireStacks = Math.Min(MireMaxStacks, e.MireStacks + (prof.Weight >= MireHeavy ? 2 : 1));
                        e.MireNext = t + MireInterval;
                    }
                }
                else e.MireNext = double.NegativeInfinity;
                if (e.Infected)
                {
                    if (w.CollectDetails) e.Taken += Math.Min(e.Hp, InfectionDps * w.Step);
                    e.Hp -= InfectionDps * w.Step;
                    if (e.Hp <= 0) { Die(e, -1); continue; }
                }
                double haste = (e.Infected ? InfectionSpeed : 1) * e.Mire * (e.AttackSlowUntil > t ? .75 : 1);
                bool stunned = e.StunUntil > t;
                if (e.BlockedBy >= 0)
                {
                    var u = units[e.BlockedBy];
                    if (u.Down || u.Stunned(t)) e.BlockedBy = -1;
                    else
                    {
                        if (stunned) continue;
                        e.Cooldown -= w.Step * haste;
                        if (e.Cooldown <= 0 && prof.Atk > 0 && !prof.NoAttack)
                        {
                            Attack(e, e.BlockedBy);
                            e.Cooldown += prof.Interval;
                        }
                        continue;
                    }
                }
                var path = paths[e.Lane];
                if (stunned) continue;
                // Ranged enemies shoot the nearest operator in reach while they walk (the back line too).
                if (prof.Range > 0 && prof.Atk > 0 && !prof.NoAttack)
                {
                    var clip = prof.AttackClip;
                    double length = Math.Min(prof.Interval / haste, clip?.Seconds ?? 0);
                    double wind = clip is null ? 0 : length * clip.Hit / clip.Seconds;
                    e.Cooldown -= w.Step * haste;
                    if (e.Aim < 0 && e.Cooldown <= wind * haste)
                    {
                        int bestTaunt = int.MinValue;
                        for (int i = 0; i < units.Count; i++)
                        {
                            var ally = units[i];
                            if (ally.Down || ally.Tile == TerrainKind.Hide || Dist(ally.X, ally.Y, e.X, e.Y) > prof.Range + .25) continue;
                            int taunt = ally.Spec.Skill is { Liberator: true } || ally.BuffOn && ally.Spec.Skill is { AtkPerHit: > 0 } ? 1 : 0;
                            if (taunt >= bestTaunt) { bestTaunt = taunt; e.Aim = i; }
                        }
                        if (e.Aim >= 0) e.Cooldown = wind * haste;
                    }
                    if (e.Aim >= 0 && e.Cooldown <= 1e-6)
                    {
                        if (!units[e.Aim].Down) Attack(e, e.Aim);
                        e.Aim = -1;
                        e.Cooldown += prof.Interval;
                        e.StandUntil = t + length - wind;
                    }
                }
                if (e.StopUntil > t || e.Aim >= 0 || e.StandUntil > t) continue;
                double move = e.Mire * (e.Tile == TerrainKind.Speedup ? SpeedupMove : 1) * (1 - Math.Min(0.8, e.SlowBy)) * (e.IberiaEnds >= t ? 1 - .04 * e.IberiaSlow : 1);
                double travel = prof.Speed * move * w.Step;
                e.S += e.LureUntil > t ? Math.Clamp(e.LurePosition - e.S, -travel, travel) : travel;
                if (e.S >= path.Length)
                {
                    e.Done = true;
                    if (!prof.CountsOnLeak) continue;
                    leaked++;
                    if (e.Child) chargedChildLeaks++;
                    leaksByLane[e.Lane] = leaksByLane.GetValueOrDefault(e.Lane) + 1;
                    leakedByName[prof.Name] = leakedByName.GetValueOrDefault(prof.Name) + 1;
                    continue;
                }
                (e.X, e.Y) = path.At(e.S);
                Locate(e);
                if (!e.Flying && e.LureUntil <= t)
                {
                    var cell = e.Cell;
                    e.Tile = TileAt(cell);   // the tile it now stands on (hidden there before anyone aims)
                    if (byCell.TryGetValue(cell, out var blockers))
                        foreach (var i in blockers)
                        {
                            var u = units[i];
                            if (u.Down || u.Cell != cell || u.Stunned(t) || u.BlockCap < e.T.MinBlock || u.Blocking + e.T.BlockCost > u.BlockCap) continue;
                            u.Blocking += e.T.BlockCost;
                            e.BlockedBy = i;
                            e.Aim = -1;
                            e.Cooldown = prof.Interval;
                            break;
                        }
                }
            }

            // Damage over time on operators: bleeding (cured by healing), apoptosis, death clouds.
            clouds.RemoveAll(c => c.Until <= t);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Down) continue;
                double dot = 0;
                if (u.BleedUntil > t) dot += u.BleedDps * (1 - u.Res(t) / 100);
                if (u.ApoptosisUntil > t) dot += 100 * (1 - u.Res(t) / 100);
                if (u.Tile == TerrainKind.Infection) dot += InfectionDps;
                foreach (var c in clouds)
                    if (Dist(c.X, c.Y, u.X, u.Y) <= c.Range) dot += c.Dps;
                if (dot > 0) Hurt(u, i, dot * w.Step);
            }

            if (anyDebuff)
            {
                foreach (var e in enemies) { e.DefScale = 1; e.ResAdd = 0; }
                foreach (var u in units)
                {
                    if (u.Down || u.Spec.Skill is not { Debuffs: true } ds || !SkillOn(u)) continue;
                    foreach (var e in enemies)
                        if (!e.Done && InRange(u, e))
                        {
                            e.DefScale = Math.Min(e.DefScale, ds.EnemyDefScale);
                            e.ResAdd = Math.Min(e.ResAdd, ds.EnemyResAdd);
                        }
                }
            }
            if (anyField)
            {
                foreach (var e in enemies) { e.Fragile = 0; e.SlowBy = 0; }
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u.Down) continue;
                    var zs = u.Spec.Skill is { } zk && SkillOn(u) ? zk : null;
                    double zone = zs?.Zone ?? 0, slow = Math.Max(u.Spec.EnemySlow, zs?.Slow ?? 0);
                    if (zone <= 0 && slow <= 0 && u.Spec.Fragile <= 0) continue;
                    double perStep = zone * u.Spec.Atk / Math.Max(1e-9, u.Spec.SkillFactor) * w.Step;
                    foreach (var e in enemies)
                    {
                        if (e.Done || e.Still || !InRange(u, e)) continue;
                        e.Fragile = Math.Max(e.Fragile, u.Spec.Fragile);
                        if (e.Flying) continue;
                        e.SlowBy = Math.Max(e.SlowBy, slow);
                        if (zone > 0) Wear(e, i, ArtsOn(e, perStep, u.Spec.ResIgnore), arts: true);
                    }
                }
            }
            foreach (var (caster, owner) in snowCasters)
            {
                if (caster.Down) continue;
                var snow = caster.SnowTiles ??= [];
                var previous = caster.SnowPrevious ??= [];
                caster.SnowClock += w.Step;
                double period = SkillOn(caster) ? 1.5 : 5.5;
                if (caster.SnowClock >= period)
                {
                    caster.SnowClock -= period;
                    foreach (var cell in SkillOn(caster) ? caster.SkillRange : caster.Range)
                        snow[cell] = Math.Min(5, snow.GetValueOrDefault(cell) + 1);
                }
                foreach (var e in enemies)
                {
                    if (e.Done || e.Still) continue;
                    bool known = previous.TryGetValue(e, out var old);
                    if (!known || old != e.Cell)
                    {
                        if (known && !e.Flying) snow.Remove(old);
                        previous[e] = e.Cell;
                        if (!e.Flying && snow.ContainsKey(e.Cell))
                            Wear(e, owner, ArtsOn(e, .8 * PanelAttack(caster), caster.Spec.ResIgnore), arts: true);
                    }
                    e.SlowBy = Math.Max(e.SlowBy, .12 * snow.GetValueOrDefault(e.Cell));
                }
            }
            for (int j = aftertones.Count - 1; j >= 0; j--)
            {
                var echo = aftertones[j];
                if (units[echo.Owner].Down) { aftertones.RemoveAt(j); continue; }
                if (t + 1e-6 < echo.Next) continue;
                foreach (var e in echo.Targets)
                    if (!e.Done && !e.Still) Wear(e, echo.Owner, ArtsOn(e, echo.Damage, 0), arts: true);
                echo.Next++;
                if (--echo.Left == 0) aftertones.RemoveAt(j);
            }
            for (int i = 0; i < fireCasters.Length; i++)
            {
                var caster = fireCasters[i];
                if (caster.Down) { caster.FireTiles?.Clear(); continue; }
                var tiles = caster.FireTiles ??= [];
                if (caster.FireTick > t) continue;
                caster.FireTick = Math.Max(t, caster.FireTick + .3);
                foreach (var expired in tiles.Where(pair => pair.Value <= t).Select(pair => pair.Key).ToArray()) tiles.Remove(expired);
                foreach (var e in enemies)
                    if (!e.Done && !e.Flying && e.TrailUntil > t && (tiles.Count < 25 || tiles.ContainsKey(e.Cell)))
                        tiles[e.Cell] = t + 4;
                if (tiles.Count == 0) continue;
                int owner = units.IndexOf(caster);
                double damage = PanelAttack(caster) * (SkillOn(caster) ? caster.Spec.Skill!.Gain : 1) * .15 * (1 + .03 * caster.Rage);
                foreach (var e in enemies)
                    if (Visible(e) && !e.Flying && tiles.ContainsKey(e.Cell))
                        Wear(e, owner, ArtsOn(e, damage, caster.Spec.ResIgnore), arts: true);
            }
            if (fireShots.Count > 0) MoveFireShots();
            for (int j = fields.Count - 1; j >= 0; j--)
            {
                var f = fields[j];
                double age = t - f.Start;
                if (age > f.Spec.Seconds + 1e-6) { fields.RemoveAt(j); continue; }
                bool tick = age + 1e-6 >= f.Ticks + 1;
                if (tick) f.Ticks++;
                double radius = f.Spec.Radius + age * f.Spec.Growth;
                double x = f.X + age * f.Vx, y = f.Y + age * f.Vy;
                if (f.Spec.Drift > 0)
                {
                    var bounds = units[f.Owner].Spec.FieldBounds;
                    x = Math.Clamp(x, bounds.Left + .5, bounds.Right + .5);
                    y = Math.Clamp(y, .5, bounds.Bottom + .5);
                }
                var owner = units[f.Owner];
                if (tick && owner.Spec.Skill is { FieldHeal: > 0 } alchemy)
                    foreach (var ally in units)
                        if (!ally.Down && Dist(ally.X, ally.Y, x, y) <= radius)
                        {
                            double amount = Math.Min(ally.MaxHp - ally.Hp, alchemy.FieldHeal * PanelAttack(owner));
                            ally.Hp += amount;
                            owner.Healed += amount;
                        }
                foreach (var e in enemies)
                {
                    if (e.Done || e.Still || e.Flying || Dist(e.X, e.Y, x, y) > radius) continue;
                    if (f.Spec.Drift > 0) e.HealCutUntil = t + w.Step;
                    bool heavy = f.Spec.HeavyWeight > 0 && e.Profile.Weight >= f.Spec.HeavyWeight;
                    if (heavy) e.SlowBy = Math.Max(e.SlowBy, f.Spec.HeavySlow);
                    if (tick)
                    {
                        Wear(e, f.Owner, ArtsOn(e, f.Atk * (f.Spec.Damage + (heavy ? f.Spec.HeavyDamage : 0)),
                            units[f.Owner].Spec.ResIgnore), arts: true);
                        if (!e.Done) { ApplyIberia(e, f.Owner); ApplySiracusa(e, owner); }
                    }
                }
            }
            if (anyLoss) for (int i = 0; i < units.Count; i++)
                if (!units[i].Down && (units[i].Spec.LossPerSecond > 0 || units[i].Spec.NonlethalLossPerSecond > 0))
                {
                    var u = units[i];
                    if (u.Spec.NonlethalLossPerSecond > 0) u.Hp = Math.Max(1, u.Hp - u.Spec.NonlethalLossPerSecond * w.Step);
                    u.Hp = Math.Max(0, u.Hp - u.Spec.LossPerSecond * w.Step);
                    if (u.Hp <= 0) Fall(u, i);
                }
            if (anyRegen)
                foreach (var u in units)
                    if (!u.Down && u.Spec.Regen > 0) u.Hp = Math.Min(u.MaxHp, u.Hp + u.Spec.Regen * w.Step);
            if (anyBondPulse && t + 1e-6 >= bondTick)
            {
                bondTick += 1;
                foreach (var e in enemies)
                {
                    if (e.Done || e.Still) continue;
                    if (e.BlockedBy >= 0 && units[e.BlockedBy] is var member && member.Spec.UrsusBlockDamage > 0)
                        Wear(e, e.BlockedBy, ArtsOn(e, member.Spec.UrsusBlockDamage * PanelAttack(member), member.Spec.ResIgnore), arts: true);
                    if (siracusa < 0 || e.Done || e.Still) continue;
                    int count = (e.StunUntil > t ? 1 : 0) + (e.ColdUntil > t ? 1 : 0);
                    if (e.SiracusaStatus is { } statuses)
                        for (int k = 1; k < statuses.Length; k++)
                            if (k != 3 && statuses[k] > t) count++;
                    if (count < 2) continue;
                    // 版本结算把无来源的盟约伤害记给第一名成员，调和也可获得这笔伤害。
                    foreach (var target in enemies)
                        if (Dist(target.X, target.Y, e.X, e.Y) <= 1.1) Wear(target, siracusa, units[siracusa].Spec.SiracusaPulse);
                }
            }
            if (anyDoc && t + 1e-6 >= docTick)
            {
                docTick += 1;
                foreach (var ally in units) ally.DocShelter = 0;
                foreach (var u in units)
                {
                    if (u.Down || u.Spec.DocHeal <= 0) continue;
                    double panel = PanelAttack(u);
                    foreach (var ally in units)
                    {
                        if (ally.Down || ally.Spec.Summon is not null || !(SkillOn(u) ? u.SkillRange : u.Range).Contains(ally.Spec.Cell)) continue;
                        if (!ally.Spec.NoHeal && ally.NoHealUntil <= t) Heal(u, ally, u.Spec.DocHeal * panel);
                        for (int k = 1; k < ally.Gauge.Length; k++)
                            if (ally.GaugeReady[k] <= t) ally.Gauge[k] = Math.Max(0, ally.Gauge[k] - u.Spec.DocElement * panel);
                        ally.DocShelter = Math.Max(ally.DocShelter, u.Spec.DocShelter);
                    }
                }
            }
            if (anyBardSkill)
            {
                foreach (var ally in units) { ally.InspiredAtk = 0; ally.NewInspireHp = 0; }
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u.Down || u.Spec.Skill is not { } sk || (sk.BardZone <= 0 && !sk.ShareHp)) continue;
                    if (Ready(u) && !u.Stunned(t)) Cast(u);
                    if (!SkillOn(u)) continue;
                    if (sk.InspireHp > 0)
                        foreach (int j in u.SkillAllies)
                            if (!units[j].Down && units[j].Spec.Pattern.HealAura == 0)
                                units[j].NewInspireHp = Math.Max(units[j].NewInspireHp, sk.InspireHp * u.MaxHp);
                    if (sk.Inspire > 0)
                        foreach (var ally in units)
                            if (!ally.Down && ally.Spec.Pattern.HealAura == 0 && (u.SkillRange.Contains(ally.Spec.Cell) || SeabornCovers(u, ally.Spec.Cell)))
                                ally.InspiredAtk = Math.Max(ally.InspiredAtk, sk.Inspire * PanelAttack(u));
                    if (t + 1e-6 < u.NextPulse) continue;
                    u.NextPulse += sk.ShareHp ? 2 : 1;
                    if (sk.ShareHp)
                    {
                        double hp = 0, maxHp = 0;
                        foreach (int j in u.SkillAllies)
                            if (!units[j].Down) { hp += units[j].Hp; maxHp += units[j].MaxHp; }
                        foreach (int j in u.SkillAllies)
                            if (!units[j].Down) units[j].Hp = units[j].MaxHp * hp / maxHp;
                    }
                    else
                    {
                        foreach (var e in enemies)
                        {
                            int pulses = (InRange(u, e) ? 1 : 0) + (SeabornCovers(u, e.Cell) ? 1 : 0);
                            if (pulses > 0) Wear(e, i, PanelAttack(u) * sk.BardZone * pulses);
                        }
                        u.Hp = Math.Max(1, u.Hp - u.MaxHp * .05);
                    }
                }
                foreach (var ally in units)
                {
                    if (ally.NewInspireHp == ally.InspiredHp) continue;
                    double nextMax = ally.MaxHp - ally.InspiredHp + ally.NewInspireHp;
                    ally.Hp *= nextMax / ally.MaxHp;
                    ally.MaxHp = nextMax;
                    ally.InspiredHp = ally.NewInspireHp;
                }
            }
            if (anyHot)
                foreach (var u in units)
                {
                    if (u.Down) continue;
                    double rate = (SkillOn(u) && u.Spec.Skill is { BardZone: > 0 } ? 0
                        : SkillOn(u) && u.Spec.Skill is { BardHeal: > 0 } bard ? bard.BardHeal : u.Spec.Pattern.HealAura)
                        + (u.HealUntil > t && u.Spec.Skill is { } hs2 ? hs2.HealPerSecond : 0);
                    if (rate <= 0) continue;
                    double heal = rate * u.Spec.Atk / Math.Max(1e-9, u.Spec.SkillFactor) * w.Step;
                    foreach (int j in u.SkillAllies)
                        if (!units[j].Down) Heal(u, units[j], heal);
                    if (u.Spec.SeabornAt is not null)
                        foreach (var ally in units)
                            if (!ally.Down && !u.SkillRange.Contains(ally.Spec.Cell) && SeabornCovers(u, ally.Spec.Cell)) Heal(u, ally, heal);
                }

            UInt128 groundAt = 0, airAt = 0;
            bool stray = false;     // an enemy outside the masked area: no shortcut this step
            foreach (var e in enemies)
            {
                if (e.Done || e.Reviving) continue;
                if (e.CellBit < 0) { stray = true; continue; }
                if (e.Flying) airAt |= UInt128.One << e.CellBit; else groundAt |= UInt128.One << e.CellBit;
            }

            // Operators act.
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Down) continue;
                var sk = u.Spec.Skill;
                if (sk is not null)
                {
                    // 凋亡 cuts a running skill short; auto skills gain SP every second while none runs.
                    if (u.SkillLockUntil > t) { u.SlashIndex = -1; u.ActiveUntil = Math.Min(u.ActiveUntil, t); u.Empowered = false; }
                    if (sk.Charge == SkillCharge.Auto && u.ActiveUntil <= t && !u.Empowered) u.Sp = Math.Min(sk.Cost * sk.Charges, u.Sp + w.Step * (sk.BlockCharge && u.Blocking == 0 ? sk.IdleSpRate : 1));
                    double extraSp = u.Spec.SpPerSecond + (u.FieldSpUntil > t ? sk.FieldSp : 0);
                    if (extraSp > 0 && u.ActiveUntil <= t && !u.Empowered) u.Sp = Math.Min(sk.Cost * sk.Charges, u.Sp + extraSp * w.Step);
                }
                // The skill's self buffs come and go with it (extra block released when it ends).
                bool buff = sk is not null && SkillOn(u);
                if (buff != u.BuffOn)
                {
                    u.BuffOn = buff;
                    double hp = u.InspiredHp + (sk!.SelfHp > 10 ? u.Spec.Hp + sk.SelfHp : u.Spec.Hp * (1 + sk.SelfHp));
                    if (buff) { u.Hp *= hp / u.MaxHp; u.MaxHp = hp; u.BlockCap = u.Spec.Block + sk.SelfBlock + (u.Stance == 2 ? 1 : 0); }
                    else
                    {
                        if (sk.EndBurst > 0)
                        {
                            double burst = PanelAttack(u) * (1 + sk.AtkPerHit * u.HitsTaken) * sk.EndBurst;
                            foreach (var e in enemies)
                                if (!e.Done && Math.Abs(e.Cell.Col - u.Cell.Col) <= 1 && Math.Abs(e.Cell.Row - u.Cell.Row) <= 1)
                                    Wear(e, i, ArtsOn(e, burst, u.Spec.ResIgnore), arts: true);
                        }
                        if (sk.MeleeDuringSkill)
                        {
                            u.ImmortalUntil = t + 1; u.Sp = 0;
                            u.ConstructHp = 0; u.ConstructReady = t;
                        }
                        if (sk.EndStun > 0) Stun(u, i, sk.EndStun);
                        u.Hp = Math.Max(1, (u.Spec.Hp + u.InspiredHp) * u.Hp / u.MaxHp);
                        u.MaxHp = u.Spec.Hp + u.InspiredHp;
                        u.Hp = Math.Min(u.Hp, u.MaxHp);
                        u.BlockCap = sk.Liberator ? 0 : u.Spec.Block;
                        if (sk.Liberator) u.Ramp = 0;
                        foreach (var e in enemies)
                            if (u.Blocking > u.BlockCap && e.BlockedBy == i) Release(e);
                    }
                }
                if (u.Spec.ReconstructAt is not null && !buff && sk is not null)
                {
                    if (u.ConstructHp <= 0 && u.ConstructReady <= t && dp >= 3)
                    {
                        dp -= 3; u.ConstructHp = sk.ReconstructHp; u.ConstructReady = t;
                    }
                    if (u.ConstructHp > 0 && t >= u.ConstructReady + 1)
                    {
                        u.ConstructHp -= 80 * w.Step;
                        if (u.ConstructHp <= 0) u.ConstructReady = t + 15;
                    }
                }
                if (buff && sk is { SelfRegen: > 0 }) u.Hp = Math.Min(u.MaxHp, u.Hp + sk.SelfRegen * u.MaxHp * w.Step);
                if (sk is { MeleeDuringSkill: true } && buff) Hurt(u, i, 80 * w.Step);
                // 眩晕、缴械和停止攻击期间只等到普攻就绪，不能积攒普攻次数。
                u.Cooldown = Math.Max(-w.Step, u.Cooldown - w.Step);
                bool canAct = u.Cooldown <= 0 && !u.Stunned(t);
                var pat = u.Spec.Pattern;
                if (sk is { BurnBonus: > 0 })
                {
                    if (u.Recovering)
                    {
                        u.Hp = Math.Min(u.MaxHp, u.Hp + .035 * u.MaxHp * w.Step);
                        if (u.Hp < u.MaxHp) continue;
                        u.Recovering = false; u.DownShield = 0; u.NoHealUntil = u.SkillLockUntil = -1; u.BlockCap = u.Spec.Block;
                        foreach (var e in enemies) if (Visible(e) && Dist(u.X, u.Y, e.X, e.Y) <= 1.7)
                            e.StunUntil = Math.Max(e.StunUntil, t + 5);
                    }
                    if (SkillOn(u))
                    {
                        Hurt(u, i, u.MaxHp * .03 * w.Step);
                        if (u.Down || u.Recovering) continue;
                    }
                }
                if (sk is { BurstDot: > 0 } && Ready(u) && !u.Stunned(t))
                {
                    var centers = enemies.Where(e => Visible(e) && InRange(u, e)).OrderByDescending(e => e.S).Take(3).ToArray();
                    if (centers.Length > 0)
                    {
                        Cast(u);
                        u.Empowered = false;
                        double panel = PanelAttack(u);
                        var struck = new HashSet<Enemy>();
                        foreach (var center in centers)
                        {
                            if (center.Done || center.Still) continue;
                            var group = enemies.Where(e => Visible(e) && Dist(center.X, center.Y, e.X, e.Y) <= 1.7).ToArray();
                            double damage = panel * sk.Gain * (group.Length >= 3 ? sk.GroupBoost : 1);
                            foreach (var e in group)
                            {
                                Wear(e, i, ArtsOn(e, damage, u.Spec.ResIgnore), arts: true);
                                struck.Add(e);
                            }
                        }
                        aftertones.Add(new(i, panel * sk.BurstDot, t + 1, struck));
                    }
                }
                if (sk is { FireWave: > 0 })
                {
                    if (t >= u.NextRage) { u.Rage = Math.Min(6, u.Rage + 1); u.NextRage += .5; }
                    if (Ready(u) && !u.Stunned(t) && enemies.Any(Visible)) Cast(u);
                    if (SkillOn(u))
                    {
                        canAct = false;
                        if (!u.Overdrive && u.ActiveUntil - t <= sk.Duration / 2) { u.Overdrive = true; u.NextPulse = t; }
                        if (t + 1e-6 >= u.NextPulse && fireCasters.FirstOrDefault(a => !a.Down && SkillOn(a)) == u)
                        {
                            FireVolley(u, i, u.Overdrive ? 6 : 2);
                            u.NextPulse += u.Overdrive ? 2 : 2.67;
                        }
                    }
                }
                if (sk is { CastInSkillRange: true } && !SkillOn(u))
                {
                    if (Ready(u) && !u.Stunned(t) && enemies.Any(e => !e.Done && !e.Reviving && u.SkillRange.Contains(e.Cell))) Cast(u);
                    if (sk.OnlySkillAttacks) canAct &= SkillOn(u);
                }
                if (sk is { Liberator: true } && !SkillOn(u))
                {
                    u.Ramp = Math.Min(2, u.Ramp + .05 * w.Step);
                    if (Ready(u) && !u.Stunned(t) && enemies.Any(e => !e.Done && !e.Reviving && u.SkillRange.Contains(e.Cell))) Cast(u);
                    canAct &= SkillOn(u);
                }
                if (sk is { FrozenGain: > 0, Ammo: > 0, BurnBonus: 0 } && !SkillOn(u) && u.Sp <= .5 * sk.Cost)
                {
                    canAct = false;
                    u.Hp = Math.Min(u.MaxHp, u.Hp + .035 * u.MaxHp * w.Step);
                }
                if (sk is { StopsAttack: true })
                {
                    if (Ready(u) && !u.Stunned(t) && enemies.Any(e => !e.Done && InRange(u, e))) Cast(u);
                    if (SkillOn(u)) canAct = false;
                }
                if (sk?.GroundField is { Drift: > 0 } tide && Ready(u) && !u.Stunned(t))
                {
                    var targetCell = enemies.Where(e => !e.Done && !e.Still && InRange(u, e) &&
                        (!(e.T.Invisible || e.Tile == TerrainKind.Hide) || e.BlockedBy >= 0 || Revealed(e)))
                        .OrderBy(e => e.Flying).ThenByDescending(e => e.S).FirstOrDefault();
                    var cell = u.Spec.ThrowCell;
                    if (targetCell is not null || cell.HasValue)
                    {
                        Cast(u);
                        u.Empowered = false;
                        double x = targetCell?.X ?? cell!.Value.Col + .5, y = targetCell?.Y ?? cell!.Value.Row + .5;
                        double len = Math.Max(.000001, Dist(u.X, u.Y, x, y));
                        bool extended = units.Any(a => a != u && !a.Down && a.Spec.Summon is null && u.Range.Contains(a.Spec.Cell));
                        if (extended) tide = tide with { Seconds = tide.Seconds + sk.FieldExtension };
                        u.FieldSpUntil = Math.Max(u.FieldSpUntil, t + tide.Seconds);
                        fields.Add(new(i, tide, t, x, y,
                            tide.Drift * (x - u.X) / len, tide.Drift * (y - u.Y) / len,
                            PanelAttack(u) * (1 + u.Spec.CastAtk * Math.Min(10, u.Casts))));
                    }
                }
                if (sk is { RemnantDamage: > 0 } && !u.Stunned(t))
                {
                    var traps = u.Remnants ??= [];
                    if (Ready(u))
                    {
                        // 优先敌人所在或最近的可部署空格。同距离时沿用范围格顺序。
                        var free = u.Spec.RemnantCells.Where(cell => !traps.Any(a => a.Cell == cell))
                            .OrderBy(cell => enemies.Where(e => !e.Flying && !e.Done && !e.Still)
                                .Select(e => Dist(cell.Col + .5, cell.Row + .5, e.X, e.Y)).DefaultIfEmpty(double.MaxValue).Min())
                            .Take(1 + u.StoredEnergy).ToArray();
                        if (free.Length > 0)
                        {
                            Cast(u);
                            u.Empowered = false;
                            u.StoredEnergy -= free.Length - 1;
                            foreach (var cell in free) traps.Add((cell, t + 30, double.PositiveInfinity));
                        }
                    }
                    for (int j = traps.Count - 1; j >= 0; j--)
                    {
                        var trap = traps[j];
                        bool Near(Enemy e) => !e.Done && !e.Still && !e.Flying &&
                            Dist(e.X, e.Y, trap.Cell.Col + .5, trap.Cell.Row + .5) <= 1.35;
                        if (t >= trap.Expires) traps.RemoveAt(j);
                        else if (t >= trap.Trigger)
                        {
                            double before = u.Damage;
                            int kills = u.Kills;
                            foreach (var e in enemies)
                                if (Near(e))
                                {
                                    Wear(e, i, ArtsOn(e, sk.RemnantDamage * PanelAttack(u), u.Spec.ResIgnore), arts: true);
                                    if (!e.Done && !e.Still && e.BlockedBy < 0)
                                    {
                                        var path = paths[e.Lane];
                                        double to = path.Nearest(trap.Cell.Col + .5, trap.Cell.Row + .5);
                                        double reach = .8 * Math.Max(0, 1 - .15 * (e.Profile.Weight - 1));
                                        e.S += Math.Clamp(to - e.S, -reach, reach);
                                        (e.X, e.Y) = path.At(e.S);
                                        Locate(e);
                                    }
                                }
                            u.RemnantDamage += u.Damage - before;
                            u.RemnantKills += u.Kills - kills;
                            traps.RemoveAt(j);
                        }
                        else if (double.IsPositiveInfinity(trap.Trigger) && enemies.Any(Near))
                            traps[j] = (trap.Cell, trap.Expires, t + .93);
                    }
                }
                if (sk?.Slashes is { } series)
                {
                    if (u.SlashIndex < 0 && Ready(u) && !u.Stunned(t)) Cast(u);
                    if (u.SlashIndex >= 0)
                    {
                        canAct = false;
                        while (u.SlashIndex >= 0 && u.NextSlash <= t + 1e-6)
                        {
                            bool final = u.SlashIndex == series.Count;
                            double atkScale = final ? series.Finisher : series.Damage;
                            double damage = PanelAttack(u) * (1 + u.Spec.CastAtk * Math.Min(10, u.Casts)) * atkScale;
                            var targets = enemies.Where(e => !e.Done && !e.Reviving && u.SkillRange.Contains(e.Cell)
                                && (!(e.T.Invisible || e.Tile == TerrainKind.Hide) || e.Ember || e.BlockedBy >= 0 ||
                                    e.RevealedUntil > t || Revealed(e))).OrderByDescending(e => e.S).Take(sk.Targets).ToArray();
                            foreach (var e in targets) Hit(u, i, e, damage, true);
                            u.SlashIndex = final ? -1 : u.SlashIndex + 1;
                            u.NextSlash += series.Interval;
                        }
                    }
                }
                if (sk is { AreaBurst: > 0 } && Ready(u) && !u.Stunned(t) &&
                    enemies.Any(e => !e.Done && !e.Still && InRange(u, e) && (!e.Flying || u.Spec.AntiAir)))
                {
                    Cast(u);
                    u.Empowered = false;
                    double damage = sk.AreaBurst * PanelAttack(u) * (1 + u.Spec.CastAtk * Math.Min(10, u.Casts));
                    foreach (var e in enemies)
                        if (!e.Done && !e.Still && InRange(u, e) && (!e.Flying || u.Spec.AntiAir))
                            Hit(u, i, e, damage, true);
                }
                if (!w.CollectDetails && (!canAct || pat.HealAura > 0)) continue;   // heals through its aura, never attacks
                double tileSpeed = u.Tile switch
                {
                    TerrainKind.Infection => InfectionSpeed,
                    TerrainKind.Speedup => SpeedupSpeed,
                    TerrainKind.Mire => 1 - MireStack * Math.Min(MireMaxStacks, 1 + Math.Floor(t / MireInterval)),
                    _ => 1,
                };
                double interval = u.Spec.Interval / u.Slow / tileSpeed;
                if (u.ChainHasteUntil > t)
                    interval = u.Spec.BaseAttackInterval / (u.Spec.BaseAttackInterval / interval + .22);
                if (u.Spec.SiracusaAspd > 0 && t < u.DeployedAt + u.Spec.SiracusaWindow)
                    interval = u.Spec.BaseAttackInterval / (u.Spec.BaseAttackInterval / interval + u.Spec.SiracusaAspd / 100);
                if (sk is { DecayingAspd: > 0 } && SkillOn(u))
                    interval /= 1 + sk.DecayingAspd / 100 * Math.Clamp((u.ActiveUntil - t) / sk.Duration, 0, 1)
                        * interval / u.Spec.BaseAttackInterval;
                double atk = PanelAttack(u);
                if (sk is { AtkPerHit: > 0 } && SkillOn(u)) atk *= 1 + sk.AtkPerHit * u.HitsTaken;
                if (sk is not null && SkillOn(u) && (sk.SkillAspd > 0 || u.Stance == 1))
                    interval = u.Spec.BaseAttackInterval / (u.Spec.BaseAttackInterval / interval + (sk.SkillAspd + (u.Stance == 1 ? sk.StanceAspd : 0)) / 100);
                if (sk is { SkillIntervalDelta: not 0 } && SkillOn(u))
                    interval *= (u.Spec.BaseAttackInterval + sk.SkillIntervalDelta) / u.Spec.BaseAttackInterval;
                if (w.CollectDetails)
                {
                    u.Seconds += w.Step;
                    if (u.ActiveUntil > t) u.Active += w.Step;
                    u.AtkSum += atk * (1 + u.Spec.CastAtk * Math.Min(10, u.Casts)) * w.Step;
                    u.SpeedSum += w.Step / interval;
                    if (!canAct || pat.HealAura > 0) continue;
                }
                if (u.Spec.ReconstructAt is not null && u.ConstructHp > 0 && Ready(u) && enemies.Any(e => !e.Done && u.Range.Contains(e.Cell))) Cast(u);
                if (sk is { MeleeDuringSkill: true } && SkillOn(u)) interval *= (u.Spec.BaseAttackInterval - 1.5) / u.Spec.BaseAttackInterval;
                if (u.Spec.Healer && !(sk is { MeleeDuringSkill: true } && SkillOn(u)) || pat.HealsDuringSkill && SkillOn(u))
                {
                    Unit? patient = null;
                    foreach (int j in u.Patients)
                    {
                        var o = units[j];
                        if (!o.Down && !o.Spec.NoHeal && o.Hp < o.MaxHp && o.NoHealUntil <= t
                            && (patient is null || o.Hp / o.MaxHp < patient.Hp / patient.MaxHp))
                            patient = o;
                    }
                    if (sk is { MeleeDuringSkill: true } && u.Spec.ReconstructAt is { } token && u.Range.Contains(token) &&
                        u.ConstructHp > 0 && u.ConstructHp < sk.ReconstructHp &&
                        (patient is null || u.ConstructHp / sk.ReconstructHp < patient.Hp / patient.MaxHp))
                    {
                        u.Healed += Math.Min(atk, sk.ReconstructHp - u.ConstructHp);
                        u.ConstructHp = Math.Min(sk.ReconstructHp, u.ConstructHp + atk);
                        u.ChainHasteUntil = t + 10;
                        ChainHeal(u, null, token, atk, true);
                        u.Sp = Math.Min(sk.Cost, u.Sp + 1);
                        u.Cooldown += interval;
                        continue;
                    }
                    if (patient is null) { u.Cooldown = 0.2; continue; }
                    if (sk is { MeleeDuringSkill: true }) u.Sp = Math.Min(sk.Cost, u.Sp + 1);
                    else atk *= SkillBoost(u, out _);
                    atk *= 1 + u.Spec.CastAtk * Math.Min(10, u.Casts);
                    if (sk is { MeleeDuringSkill: true })
                    {
                        ChainHeal(u, patient, patient.Cell, atk, false);
                        u.Cooldown += interval;
                        continue;
                    }
                    Heal(u, patient, atk);
                    patient.BleedUntil = -1;
                    // Healers that heal several at once: the next most hurt too (each once).
                    var healed = pat.HealTargets > 1 ? new List<Unit> { patient } : null;
                    for (int extra = 1; extra < pat.HealTargets; extra++)
                    {
                        Unit? other = null;
                        foreach (int j in u.Patients)
                        {
                            var o = units[j];
                            if (!o.Down && !o.Spec.NoHeal && o.Hp < o.MaxHp && o.NoHealUntil <= t && !healed!.Contains(o) && (other is null || o.Hp / o.MaxHp < other.Hp / other.MaxHp))
                                other = o;
                        }
                        if (other is null) break;
                        healed!.Add(other);
                        Heal(u, other, atk);
                        other.BleedUntil = -1;
                    }
                    u.Cooldown += interval;
                    continue;
                }
                // Nothing it blocks and nothing in reach: no target (the same outcome as the scan below).
                if (!stray && u.Blocking == 0 && ((SkillOn(u) ? u.SkillMask : u.Mask) & (groundAt | (u.Spec.AntiAir ? airAt : UInt128.Zero))) == UInt128.Zero)
                {
                    if (sk is { RemnantDamage: > 0 })
                    {
                        u.StoredEnergy = Math.Min(3, u.StoredEnergy + 1);
                        u.Cooldown += interval;
                    }
                    else u.Cooldown = 0;
                    continue;
                }
                // A revealing skill is cast as soon as anything (seen or not) is in range.
                if (u.Spec.SkillRevealSeconds > 0 && Ready(u) && enemies.Any(e => !e.Done && !e.Reviving && InRange(u, e))) Cast(u);
                // Skill-only attackers stay idle until their skill runs (cast as soon as anything is in reach).
                if (pat.SkillOnly && !SkillOn(u))
                {
                    if (Ready(u) && enemies.Any(e => !e.Done && !e.Reviving && InRange(u, e))) Cast(u);
                    else { u.Cooldown = 0; continue; }
                }
                Enemy? target = null;
                double best = double.MinValue;
                int ownBit = Bit(u.Cell);
                bool on = sk is not null && SkillOn(u);
                int take = on && sk!.Targets > 1 ? sk.Targets : pat.Targets;
                bool allBlocked = on && sk!.HitsBlocked || pat.HitsBlocked;
                bool multi = take > 1 || allBlocked;
                hits.Clear();
                foreach (var e in enemies)
                {
                    if (e.Done || e.Reviving || e.Flying && (!u.Spec.AntiAir || sk is { MeleeDuringSkill: true })) continue;
                    // Invisible enemies (and those on 隐匿模块) can only be hit once blocked or revealed; embers always.
                    if ((e.T.Invisible || e.Tile == TerrainKind.Hide) && !e.Ember && e.BlockedBy < 0 && e.RevealedUntil <= t
                        && !Revealed(e)) continue;
                    double priority;
                    // What it blocks stands on its own cell: first in line, unless the range it uses now leaves
                    // that cell out (a skill that only strikes the cell in front).
                    if (e.BlockedBy == i && (!SkillOn(u) || Has(u.SkillMask, ownBit))) priority = 1e6 + e.S;
                    else
                    {
                        if (!InRange(u, e)) continue;
                        priority = e.S;
                    }
                    if (multi) hits.Add((priority, e));
                    if (priority > best) { best = priority; target = e; }
                }
                if (target is null)
                {
                    // An armed strike with nothing where it lands (a wall in front) is spent on nothing.
                    if (u.Empowered && sk is not null) { u.Empowered = false; u.Cooldown += interval; }
                    else u.Cooldown = 0;
                    continue;
                }
                u.Cooldown += interval;
                if (sk is { FireWave: > 0 }) { atk *= 1 + .03 * u.Rage; u.Rage = Math.Max(0, u.Rage - 1); u.NextRage = t + 3; }
                if (sk?.GroundField is { Drift: 0 } field && Ready(u) && !target.Flying)
                {
                    Cast(u);
                    u.Empowered = false;
                    double dx = target.X - u.X, dy = target.Y - u.Y;
                    double len = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
                    fields.Add(new(i, field, t, target.X, target.Y, field.Drift * dx / len, field.Drift * dy / len,
                        atk * (1 + u.Spec.CastAtk * Math.Min(10, u.Casts))));
                }
                bool skillHit = false;
                if (sk?.GroundField is null && !(sk?.BurstDot > 0) && !(sk?.AreaBurst > 0) && !(sk?.RemnantDamage > 0)) atk *= SkillBoost(u, out skillHit);
                atk *= 1 + u.Spec.CastAtk * Math.Min(10, u.Casts);
                if (skillHit && sk is { BurnBonus: > 0 } && --u.Ammo == 0) u.ActiveUntil = t;
                if (skillHit && sk is { BurnBonus: 0, Ammo: > 0 })
                {
                    u.Cooldown += .25 * interval / u.Spec.BaseAttackInterval;
                    if (target.FrozenUntil > t) atk *= sk.FrozenGain / sk.Gain;
                    else if (target.ColdUntil > t)
                    {
                        target.FrozenUntil = t + 2.5;
                        target.StunUntil = Math.Max(target.StunUntil, target.FrozenUntil);
                        target.ColdUntil = -1;
                    }
                    else target.ColdUntil = t + 2.5;
                    if (--u.Ammo == 0) u.ActiveUntil = t;
                }
                if (skillHit && sk is { MeleeDuringSkill: true }) ChainHeal(u, u, u.Cell, atk * .5, true);
                int strikes = Math.Max(1, pat.HitsPerAttack) * (skillHit && sk is { Hits: > 1 } ? sk.Hits : 1);
                if (sk is { RemnantDamage: > 0 })
                {
                    for (int shot = 0; shot < u.StoredEnergy && !target.Done; shot++) Hit(u, i, target, atk * 1.35, false);
                    u.StoredEnergy = 0;
                }
                if (sk is { GroupBoost: > 0 } && enemies.Count(e => Visible(e) && Dist(target.X, target.Y, e.X, e.Y) <= pat.SplashRadius) >= 3)
                    atk *= sk.GroupBoost;
                for (int rep = 0; rep < strikes; rep++)
                {
                    if (!multi) Hit(u, i, target, atk, skillHit);
                    else
                    {
                        // Several at once: the furthest-along enemies in reach (and every enemy it blocks, if it says so).
                        hits.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                        for (int h = 0; h < hits.Count; h++)
                        {
                            var (_, e) = hits[h];
                            if (h < take || allBlocked && e.BlockedBy == i) Hit(u, i, e, atk, skillHit);
                        }
                    }
                }
                // Splash around the target, or hits jumping on from it.
                double splash = skillHit && sk is { BurnBonus: > 0 } ? 1.7 : pat.SplashRadius;
                if (splash > 0)
                    foreach (var e in enemies)
                        if (e != target && !e.Done && !e.Reviving && (!e.Flying || u.Spec.AntiAir)
                            && Dist(e.X, e.Y, target.X, target.Y) <= splash) Hit(u, i, e, atk, skillHit);
                if (pat.ChainTargets > 1)
                {
                    var chained = new HashSet<Enemy> { target };
                    double scale = 1, cx = target.X, cy = target.Y;
                    for (int k = 1; k < pat.ChainTargets; k++)
                    {
                        Enemy? jump = null;
                        double nearest = 1.6;
                        foreach (var e in enemies)
                        {
                            if (e.Done || e.Reviving || chained.Contains(e) || e.Flying && !u.Spec.AntiAir) continue;
                            double d = Dist(e.X, e.Y, cx, cy);
                            if (d <= nearest) { nearest = d; jump = e; }
                        }
                        if (jump is null) break;
                        chained.Add(jump);
                        scale *= 0.85;
                        Hit(u, i, jump, atk * scale, skillHit);
                        (cx, cy) = (jump.X, jump.Y);
                    }
                }
            }

            // Finished enemies leave the list (order kept, so targeting ties break the same way).
            enemies.RemoveAll(e => e.Done);
            // Nothing left to come or fight and nothing still hurting an operator: the rest of the clock changes nothing.
            if (next == spawns.Count && pending.Count == 0 && enemies.Count == 0 && (w.TimeLimit.HasValue || clouds.Count == 0
                && !units.Any(u => !u.Down && (u.Tile == TerrainKind.Infection || u.BleedUntil > t || u.ApoptosisUntil > t))))
                break;
        }

        if (w.TimeLimit.HasValue && t >= end)
            foreach (var enemy in enemies.Where(e => !e.Done && e.Profile.Rank != "BOSS"))
            {
                enemy.Done = true;
                if (!enemy.Profile.CountsOnLeak) continue;
                leaked++;
                if (enemy.Child) chargedChildLeaks++;
                leaksByLane[enemy.Lane] = leaksByLane.GetValueOrDefault(enemy.Lane) + 1;
                leakedByName[enemy.Profile.Name] = leakedByName.GetValueOrDefault(enemy.Profile.Name) + 1;
            }
        int unspawned = w.TimeLimit.HasValue ? spawns.Count - next : 0;
        int alive = enemies.Count(e => !e.Done && !e.Child) + (spawns.Count - next - unspawned);
        // A boss not yet spawned counts as untouched; one with a second life (rebirth) has twice the HP to beat.
        int bossCount = bosses.Count + spawns.Skip(next).Count(s => s.Profile.Rank == "BOSS");
        double bossProgress = bosses.Sum(b =>
        {
            if (b.Killed) return 1.0;
            int lives = 1 + (b.T.RebornSeconds > 0 ? 1 : 0) + (b.T.EmberHits > 0 ? 1 : 0);
            double part = b.Still ? 0 : 1 - Math.Clamp(b.Hp / Math.Max(1, b.MaxHp), 0, 1);
            return Math.Min(1, (b.LivesLost + part) / lives);
        });
        var damageByUnit = units.ToDictionary(u => u.Spec.Key, u => u.Damage - u.RemnantDamage);
        var killsByUnit = units.ToDictionary(u => u.Spec.Key, u => u.Kills - u.RemnantKills);
        foreach (var u in units.Where(u => u.RemnantDamage > 0))
        {
            damageByUnit[u.Spec.Key + "·旧日残影"] = u.RemnantDamage;
            killsByUnit[u.Spec.Key + "·旧日残影"] = u.RemnantKills;
        }
        return new SimResult(spawns.Count - unspawned, killed, leaked, alive, killsByUnit, damageByUnit,
            [.. units.Where(u => u.Down && u.Spec.Summon is null && u.Spec.ReservePiece is null).Select(u => u.Spec.Key)], leaksByLane)
        {
            UnitDetails = w.CollectDetails ? units.ToDictionary(u => u.Spec.Key, u => new SimUnitDetail(
                u.Seconds, u.Active, u.Seconds > 0 ? u.AtkSum / u.Seconds : 0, u.Seconds > 0 ? u.SpeedSum / u.Seconds : 0)) : null,
            EnemyDetails = observed?.Select(e => new SimEnemyDetail(e.Profile.Name, e.MaxHp, e.Taken, e.Seconds, e.Killed)).ToList(),
            ChargedChildLeaks = chargedChildLeaks,
            LeakedByName = leakedByName,
            Casts = units.Where(u => u.Spec.Skill is not null).ToDictionary(u => u.Spec.Key, u => u.Casts),
            StunsAround = units.ToDictionary(u => u.Spec.Key, u => u.StunsSeen),
            KillsInRange = units.ToDictionary(u => u.Spec.Key, u => u.KillsSeen),
            Bosses = bossCount, BossProgress = bossProgress, BossesKilled = bosses.Count(b => b.Killed),
            Taken = units.ToDictionary(u => u.Spec.Key, u => u.Taken),
            Healed = units.ToDictionary(u => u.Spec.Key, u => u.Healed),
        };
    }

    private static bool HasAura(EnemyProfile p) => p.Traits.AuraRange > 0 && (p.Traits.AuraDef > 0 || p.Traits.AuraRes > 0);

    /// <summary>Spawn times and lanes: each kind spread evenly over the spawn window, lanes in proportion to their weight.</summary>
    private static List<(double Time, int Lane, EnemyProfile Profile)> Schedule(IReadOnlyList<SimLane> lanes, WaveScenario w)
    {
        if (w.Spawns is { } real)
        {
            // A real stream keeps its times; each enemy takes a lane of its own gate and motion, shared by weight.
            var share = new double[lanes.Count];
            var list = new List<(double, int, EnemyProfile)>();
            foreach (var s in real)
            {
                var idx = Enumerable.Range(0, lanes.Count).Where(i => lanes[i].Flying == s.Flying && lanes[i].Upper == s.Upper && lanes[i].Weight > 0).ToList();
                if (idx.Count == 0) idx = [.. Enumerable.Range(0, lanes.Count).Where(i => lanes[i].Flying == s.Flying && lanes[i].Weight > 0)];
                if (idx.Count == 0) idx = [.. Enumerable.Range(0, lanes.Count).Where(i => !lanes[i].Flying)];
                if (idx.Count == 0) continue;
                double total = idx.Sum(i => lanes[i].Weight);
                foreach (var i in idx) share[i] += lanes[i].Weight / total;
                int lane = idx.MaxBy(i => share[i]);
                share[lane] -= 1;
                list.Add((s.Time, lane, s.Profile));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }
        var result = new List<(double, int, EnemyProfile)>();
        foreach (bool flying in new[] { false, true })
        {
            int count = flying ? w.FlyCount : w.GroundCount;
            var idx = Enumerable.Range(0, lanes.Count).Where(i => lanes[i].Flying == flying && lanes[i].Weight > 0).ToList();
            if (count <= 0 || idx.Count == 0) continue;
            double total = idx.Sum(i => lanes[i].Weight);
            var credit = idx.ToDictionary(i => i, _ => 0.0);
            for (int k = 0; k < count; k++)
            {
                // Largest remainder: the lane furthest behind its share gets the next enemy.
                foreach (var i in idx) credit[i] += lanes[i].Weight / total;
                int lane = idx.MaxBy(i => credit[i]);
                credit[lane] -= 1;
                result.Add((w.SpawnSeconds * (k + 0.5) / count, lane, flying ? w.Flying : w.Ground));
            }
        }
        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }
}
