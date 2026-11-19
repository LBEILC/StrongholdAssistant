using ASPAssistant.Core.Placement;

namespace ASPAssistant.Tests.Services;

public class PlacementCalibrationTests
{
    private static readonly SimLane Lane = new([(4.5, 2.5), (0.5, 2.5)], 1, false);
    private static readonly SimUnit Caster = new("施术者", new(3, 2), [new(4, 2)], false, 0, true,
        100, 10, true, 2000, 100, false);
    private static readonly EnemyProfile Target = new(5000, 0, 0, 0, 1, 0);
    private static readonly WaveScenario Wave = new()
    {
        CollectDetails = true, TailSeconds = 4, Step = 0.2,
        Spawns = [new(0, false, false, Target with { Name = "重", Weight = 3 }),
            new(0, false, false, Target with { Name = "轻" })]
    };

    [Fact]
    public void PlacementFieldTicksAtLandingPointAndOnlyAddsHeavyDamageToHeavyEnemies()
    {
        var skill = new SimSkill(1, 30, 30, -1, SkillCharge.Auto)
        {
            GroundField = new(2, 1, 0.5, HeavyWeight: 3, HeavyDamage: 1)
        };
        var result = ClearSimulator.Run([Caster with { Skill = skill }], [Lane], Wave);
        var normal = ClearSimulator.Run([Caster], [Lane], Wave);
        var targets = result.EnemyDetails!.ToDictionary(e => e.Name);
        foreach (var e in normal.EnemyDetails!)
            Assert.Equal(e.Name == "重" ? 300 : 100, targets[e.Name].Taken - e.Taken, 6);
        Assert.Equal(1, result.Casts[Caster.Key]);
        Assert.Equal(result.Damage[Caster.Key], targets.Values.Sum(e => e.Taken), 6);
    }

    [Fact]
    public void PlacementSlashSeriesReplacesNormalStrikesAndFinishesOnce()
    {
        var skill = new SimSkill(1, 30, 30, 0, SkillCharge.Auto)
        {
            Slashes = new(2, 0.5, 1.8, 3), Targets = 2
        };
        var caster = Caster with { Skill = skill, Pattern = new AttackPattern { HitsPerAttack = 2 } };
        var result = ClearSimulator.Run([caster], [Lane], Wave with { TailSeconds = 1.1 });
        Assert.All(result.EnemyDetails!, e => Assert.Equal(660, e.Taken, 6));
        Assert.Equal(1, result.Casts[Caster.Key]);
    }

    [Fact]
    public void PlacementBurstHitsEveryTargetWhileTheNormalAttackIsCoolingDown()
    {
        var skill = new SimSkill(1, 30, 28, -1, SkillCharge.Auto) { AreaBurst = 1.3 };
        var result = ClearSimulator.Run([Caster with { Skill = skill }], [Lane], Wave);
        var normal = ClearSimulator.Run([Caster], [Lane], Wave);
        Assert.Equal(260, result.Damage[Caster.Key] - normal.Damage[Caster.Key], 6);
        Assert.Equal(1, result.Casts[Caster.Key]);
        Assert.Equal(0, result.UnitDetails![Caster.Key].Active);
    }
}
