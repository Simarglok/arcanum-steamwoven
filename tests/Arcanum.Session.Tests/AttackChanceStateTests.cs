using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.AttackStateTests;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-124 (ledger OH19): the hit chance the combat panel shows the hero for an NPC, read without a roll or a revision.
[TestClass]
public sealed class AttackChanceStateTests
{
    private const string Rat = "oid:rat";

    private static GameSessionOwner Owner(bool hero = true)
    {
        var result = GameSessionOwner.Create(Descriptor() with { Hero = hero ? HalfElf() : null, Experience = ExperienceStateTests.Rules() });
        Assert.AreEqual(GameSessionFailure.None, result.Failure);
        return result.Owner!;
    }

    private static SessionCritterCombat Combat(string entity = Rat)
    {
        var build = SessionCritterCombat.From(entity, Npc());
        Assert.AreEqual(SessionCritterCombatFailure.None, build.Failure);
        return build.Combat!;
    }

    [TestMethod]
    public void TheChanceIsTheAttackChecksEffectivenessLessItsDifficultyAndSpendsNothing()
    {
        using var owner = Owner();
        var rat = Combat();
        var version = owner.Version;
        var draws = owner.QueryRandom().Draws;
        var chance = owner.QueryHeroAttackChance(rat, 1, null);
        Assert.IsNotNull(chance);
        Assert.AreEqual(version, owner.Version, "a read: no revision");
        Assert.AreEqual(draws, owner.QueryRandom().Draws, "a read: no roll");
        Assert.AreEqual(SkillDifficultyReasons.None, chance.Reasons, "a bare-handed melee swing at one tile");
        Assert.IsFalse(chance.MagickTechPenalty);

        // The attack itself resolves the same check: its effectiveness less its difficulty is the chance shown.
        var check = owner.AttackCritter(owner.Version, new(rat)).Resolution!.AttackCheck!;
        Assert.AreEqual(Math.Max(0, check.Effectiveness - check.Difficulty), chance.Percent);
    }

    [TestMethod]
    public void TheDistanceAndTheLineOfFireReachTheChanceOnlyForTheSkillsThatReadThem()
    {
        using var owner = Owner();
        var rat = Combat();
        // Bare hands are melee: the distance terms are not read, a blocked line is no reason.
        var near = owner.QueryHeroAttackChance(rat, 1, null)!;
        var far = owner.QueryHeroAttackChance(rat, 30, new SessionLineOfFire(9, true))!;
        Assert.AreEqual(near, far);
    }

    [TestMethod]
    public void ARefusedDistanceOrNoHeroOrATargetOutsideTheSessionGivesNoChance()
    {
        using var owner = Owner();
        var rat = Combat();
        Assert.IsNull(owner.QueryHeroAttackChance(rat, -1, null), "a distance below 0 is refused by the shared check");
        Assert.IsNull(owner.QueryHeroAttackChance(rat, AttackResolver.MaxMagnitude + 1, null));
        using var heroless = Owner(hero: false);
        Assert.IsNull(heroless.QueryHeroAttackChance(rat, 1, null));
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.QueryHeroAttackChance(null!, 1, null));
    }

    [TestMethod]
    public void TheChanceDependsOnTheStoredValuesNotOnTheEntityName()
    {
        using var owner = Owner();
        Assert.AreEqual(owner.QueryHeroAttackChance(Combat("oid:rat"), 1, null), owner.QueryHeroAttackChance(Combat("oid:other"), 1, null),
            "the same stored values give the same chance");
    }

    // ARC-WORLD-1149 (FR-LIGHT-011): the light level at the target's cell reaches the darkness term of the check, the shown chance and the attack alike.
    [TestMethod]
    public void TheTargetsLightReachesTheDarknessTermOfTheChanceAndOfTheAttack()
    {
        using var owner = Owner();
        var rat = Combat();
        var bright = owner.QueryHeroAttackChance(rat, 1, null)!;
        Assert.AreEqual(bright, owner.QueryHeroAttackChance(rat, 1, null, AttackRules.MaxLightLevel), "full light is the default");
        var dark = owner.QueryHeroAttackChance(rat, 1, null, 0)!;
        Assert.IsTrue(dark.Reasons.HasFlag(SkillDifficultyReasons.Darkness), "no light at the target's cell is a reason");
        Assert.IsTrue(dark.Percent < bright.Percent);
        var dim = owner.QueryHeroAttackChance(rat, 1, null, 128)!;
        Assert.IsTrue(dim.Percent < bright.Percent && dim.Percent > dark.Percent, "half light costs about half the penalty");

        // The attack resolves the same term: its darkness difficulty is the penalty of the level it was given.
        var seen = owner.AttackCritter(owner.Version, new(rat)).Resolution!;
        using var darkOwner = Owner();
        var blind = darkOwner.AttackCritter(darkOwner.Version, new(rat) { TargetLight = 0 }).Resolution!;
        Assert.AreEqual(0, seen.Difficulty!.Light, "full light: no darkness");
        Assert.AreEqual(30, blind.Difficulty!.Light, "30 at no light for an untrained melee swing");
        Assert.AreEqual(seen.Difficulty.Total + 30, blind.Difficulty.Total);
    }

    [TestMethod]
    public void ALightLevelOutsideTheRangeRefusesTheChanceAndTheAttack()
    {
        using var owner = Owner();
        var rat = Combat();
        Assert.IsNull(owner.QueryHeroAttackChance(rat, 1, null, -1));
        Assert.IsNull(owner.QueryHeroAttackChance(rat, 1, null, AttackRules.MaxLightLevel + 1));
        var version = owner.Version;
        foreach (var light in new[] { -1, AttackRules.MaxLightLevel + 1 })
        {
            var result = owner.AttackCritter(owner.Version, new(rat) { TargetLight = light });
            Assert.AreEqual(GameSessionDisposition.Rejected, result.Disposition);
            Assert.AreEqual(GameSessionFailure.InvalidAttack, result.Failure);
        }
        Assert.AreEqual(version, owner.Version, "a refusal spends nothing");
    }
}
