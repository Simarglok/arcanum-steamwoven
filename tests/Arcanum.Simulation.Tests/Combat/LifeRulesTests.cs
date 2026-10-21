using System.Collections.Immutable;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Combat.CombatFixtures;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-COMBAT-002. Facts are invented; numbers are the formulas of docs/world/life-state-runtime.md worked by hand.
[TestClass]
public sealed class LifeRulesTests
{
    private const CombatRollPurpose Knockout = CombatRollPurpose.KnockoutFatigue;
    private const CombatRollPurpose SecondArm = CombatRollPurpose.CrippleBothArms;

    // ST 8, CN 8, WP 8, level 1, no pools: 8 + 2 × (8 + 1) + 4 = 30 hit points, 2 × (1 + 8) + 8 + 4 = 30 fatigue.
    private static readonly LifeFacts Plain = new();
    private static readonly ScriptedRolls None = new([]);

    private static LifeState Hurt(int hitPoints = 0, int fatigue = 0, int poison = 0, LifeConditions conditions = LifeConditions.None, int scars = 0) =>
        new(hitPoints, fatigue, poison, conditions, scars);

    private static LifeOutcome Damage(LifeState state, LifeDamage damage, LifeFacts? facts = null, params (CombatRollPurpose, int)[] rolls)
    {
        var scripted = new ScriptedRolls(rolls);
        var outcome = LifeRules.ApplyDamage(state, facts ?? Plain, damage, scripted);
        scripted.AssertConsumed();
        return outcome;
    }

    [TestMethod]
    public void AFatigueChargeAddsToTheDamageWhateverTheImmunities()
    {
        // ARC-COMBAT-004: the run charge in combat mode is a plain fatigue write, not a damage step.
        Assert.AreEqual(new LifeOutcome(Hurt(fatigue: 3), Hurt(fatigue: 4), LifeEvents.None, 0, DeathAnimation.None), LifeRules.ChargeFatigue(Hurt(fatigue: 3), Plain, 1));
        Assert.AreEqual(1, LifeRules.ChargeFatigue(Hurt(), Plain with { Traits = LifeTraits.FatigueImmune | LifeTraits.Invulnerable }, 1).After.FatigueDamage);
        Assert.AreEqual(LifeEvents.LostConsciousness, LifeRules.ChargeFatigue(Hurt(fatigue: 29), Plain, 1).Events, "30 fatigue: the 30th point knocks out");
        Assert.AreEqual(LifeEvents.None, LifeRules.ChargeFatigue(Hurt(fatigue: 29), Plain with { Traits = LifeTraits.Undead }, 1).Events);
        Assert.AreEqual(LifeLimits.MaxDamage, LifeRules.ChargeFatigue(Hurt(fatigue: LifeLimits.MaxDamage), Plain, 1).After.FatigueDamage);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LifeRules.ChargeFatigue(Hurt(), Plain, 0));
    }

    [TestMethod]
    public void MaximaFollowTheSourceFormulas()
    {
        Assert.AreEqual((30, 30), (LifeRules.MaxHitPoints(Plain, 0), LifeRules.MaxFatigue(Plain)));
        var built = Plain with { HitPointPoints = 2, HitPointAdjustment = 3, FatiguePoints = 1, FatigueAdjustment = -2, Level = 5, Strength = 12, Constitution = 10, Willpower = 9 };
        // 4 × 2 + 3 + 9 + 2 × (12 + 5) + 4 = 58; 4 × 1 − 2 + 2 × (5 + 10) + 9 + 4 = 45.
        Assert.AreEqual((58, 45), (LifeRules.MaxHitPoints(built, 0), LifeRules.MaxFatigue(built)));
        Assert.AreEqual(new LifeStatus(30, 18, 30, 25, false, false, true), LifeRules.Evaluate(Hurt(12, 5), Plain));
    }

    [TestMethod]
    public void EffectsOnAMaximumApplyToTheWholeSum()
    {
        // ARC-WORLD-029 (object_hp_max 0x43D5A0, critter_fatigue_max 0x45D670): +10 %, then +5 over 4 × 2 + 3 + 30 = 41.
        var built = Plain with
        {
            HitPointPoints = 2, HitPointAdjustment = 3,
            MaxHitPointEffects = [new(StatModifierOperator.Add, 5), new(StatModifierOperator.Percent, 10)],
            MaxFatigueEffects = [new(StatModifierOperator.Maximum, 12), new(StatModifierOperator.Maximum, 20)],
        };
        Assert.AreEqual((41 * 110 / 100 + 5, 12), (LifeRules.MaxHitPoints(built, 0), LifeRules.MaxFatigue(built)));
        // Poison lowers the strength inside the sum: 39 × 110 % + 5.
        Assert.AreEqual(39 * 110 / 100 + 5, LifeRules.MaxHitPoints(built, 100));
        Assert.IsTrue(LifeRules.IsValid(built));
        // A maximum at the pinned death damage, in either direction, or a default list is out of range.
        Assert.IsFalse(LifeRules.IsValid(Plain with { MaxHitPointEffects = [new(StatModifierOperator.Add, LifeLimits.DeathHitPointDamage - 30)] }));
        Assert.IsTrue(LifeRules.IsValid(Plain with { MaxHitPointEffects = [new(StatModifierOperator.Add, LifeLimits.DeathHitPointDamage - 31)] }));
        Assert.IsFalse(LifeRules.IsValid(Plain with { MaxFatigueEffects = [new(StatModifierOperator.Add, -LifeLimits.DeathHitPointDamage - 30)] }));
        Assert.IsFalse(LifeRules.IsValid(Plain with { MaxFatigueEffects = default }));
        Assert.IsFalse(LifeRules.IsValid(Plain with { MaxFatigueEffects = [new(StatModifierOperator.Divide, 0)] }));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(), Plain with { MaxHitPointEffects = [new((StatModifierOperator)6, 1)] }));
    }

    [TestMethod]
    public void PoisonLowersStrengthAndSoTheHitPointMaximum()
    {
        Assert.AreEqual((0, 0, 1, 2, 3, 3), (LifeRules.PoisonPenalty(0), LifeRules.PoisonPenalty(99), LifeRules.PoisonPenalty(100),
            LifeRules.PoisonPenalty(250), LifeRules.PoisonPenalty(399), LifeRules.PoisonPenalty(1000)));
        Assert.AreEqual(26, LifeRules.MaxHitPoints(Plain, 250));
        Assert.AreEqual(1, LifeRules.StrengthAt(Plain with { Strength = 2 }, 300));
        // Given strengths win: an effect can keep strength up under poison.
        var steady = Plain with { PoisonedStrength = [8, 8, 7] };
        Assert.AreEqual((30, 30, 28), (LifeRules.MaxHitPoints(steady, 100), LifeRules.MaxHitPoints(steady, 200), LifeRules.MaxHitPoints(steady, 300)));
    }

    [TestMethod]
    public void DeathIsHitPointsAtZeroAndPinsTheDamage()
    {
        var alive = Damage(Hurt(), new LifeDamage { HitPoints = 29 });
        Assert.AreEqual(Hurt(29), alive.After);
        Assert.AreEqual((LifeEvents.None, DeathAnimation.None, 29), (alive.Events, alive.Death, alive.HitPointDamage));
        Assert.IsFalse(LifeRules.Evaluate(alive.After, Plain).IsDead);

        var dead = Damage(Hurt(29), new LifeDamage { HitPoints = 1 });
        Assert.AreEqual(Hurt(LifeLimits.DeathHitPointDamage), dead.After);
        Assert.AreEqual((LifeEvents.Died, DeathAnimation.Plain), (dead.Events, dead.Death));
        var status = LifeRules.Evaluate(dead.After, Plain);
        Assert.IsTrue(status.IsDead);
        Assert.IsFalse(status.IsActive);

        // More than 20 damage picks the animation by location.
        Assert.AreEqual(DeathAnimation.Head, Damage(Hurt(10), new LifeDamage { HitPoints = 21, Location = HitLocation.Head }).Death);
        Assert.AreEqual(DeathAnimation.Leg, Damage(Hurt(10), new LifeDamage { HitPoints = 21, Location = HitLocation.Leg }).Death);
        Assert.AreEqual(DeathAnimation.Body, Damage(Hurt(10), new LifeDamage { HitPoints = 21, Location = HitLocation.Arm }).Death);
        Assert.AreEqual(DeathAnimation.Plain, Damage(Hurt(10), new LifeDamage { HitPoints = 20, Location = HitLocation.Head }).Death);

        // A dead critter takes nothing and rolls nothing.
        var again = Damage(dead.After, new LifeDamage { HitPoints = 5, Effects = CriticalHitEffects.Knockout | CriticalHitEffects.CrippleArm });
        Assert.AreEqual((dead.After, LifeEvents.AlreadyDead, false), (again.After, again.Events, again.Changed));
    }

    [TestMethod]
    public void AKnockoutLeavesTheCritterUnconsciousButAlive()
    {
        var outcome = Damage(Hurt(10), new LifeDamage { HitPoints = 3, Effects = CriticalHitEffects.Knockout }, null, (Knockout, 15));
        Assert.AreEqual(Hurt(13, 45), outcome.After);
        Assert.AreEqual(LifeEvents.KnockedDown | LifeEvents.KnockedOut | LifeEvents.LostConsciousness, outcome.Events);
        var status = LifeRules.Evaluate(outcome.After, Plain);
        Assert.AreEqual((false, true, false, -15), (status.IsDead, status.IsUnconscious, status.IsActive, status.Fatigue));

        // Already unconscious: no roll; fatigue damage still adds.
        var down = Damage(outcome.After, new LifeDamage { Fatigue = 2, Effects = CriticalHitEffects.Knockout });
        Assert.AreEqual((Hurt(13, 47), LifeEvents.KnockedDown), (down.After, down.Events));

        // A knockdown alone is animation only.
        var knockdown = Damage(Hurt(), new LifeDamage { Effects = CriticalHitEffects.Knockdown });
        Assert.AreEqual((Hurt(), LifeEvents.KnockedDown, false), (knockdown.After, knockdown.Events, knockdown.Changed));
    }

    [TestMethod]
    public void UndeadNeverFallUnconsciousAndTakeNoPoison()
    {
        var undead = Plain with { Traits = LifeTraits.Undead };
        var outcome = Damage(Hurt(), new LifeDamage { Fatigue = 100, Poison = 50 }, undead);
        Assert.AreEqual(Hurt(0, 100), outcome.After);
        Assert.AreEqual(LifeEvents.None, outcome.Events);
        Assert.IsFalse(LifeRules.Evaluate(outcome.After, undead).IsUnconscious);
        Assert.IsTrue(LifeRules.Evaluate(outcome.After, undead).IsActive);
        Assert.AreEqual(0, Damage(Hurt(), new LifeDamage { Poison = 50 }, Plain with { Traits = LifeTraits.Mechanical }).After.PoisonLevel);
        Assert.AreEqual(0, Damage(Hurt(), new LifeDamage { Fatigue = 9 }, Plain with { Traits = LifeTraits.FatigueImmune }).After.FatigueDamage);
    }

    [TestMethod]
    public void AStonedCritterTakesNoDamageButStillTheCriticalEffects()
    {
        var stoned = Plain with { Traits = LifeTraits.Stoned };
        var outcome = Damage(Hurt(), new LifeDamage { HitPoints = 50, Fatigue = 5, Poison = 5, Effects = CriticalHitEffects.Blind }, stoned);
        Assert.AreEqual(Hurt(conditions: LifeConditions.Blinded), outcome.After);
        Assert.AreEqual((LifeEvents.Blinded, 0), (outcome.Events, outcome.HitPointDamage));
        Assert.IsFalse(LifeRules.Evaluate(outcome.After, stoned).IsActive);
    }

    [TestMethod]
    public void StunWaitsForTheAnimationAndYieldsToAKnockdown()
    {
        var stunned = Damage(Hurt(), new LifeDamage { Effects = CriticalHitEffects.Stun });
        Assert.AreEqual((Hurt(conditions: LifeConditions.Stunned), LifeEvents.Stunned), (stunned.After, stunned.Events));
        Assert.IsFalse(LifeRules.Evaluate(stunned.After, Plain).IsActive);
        Assert.AreEqual(LifeEvents.None, Damage(stunned.After, new LifeDamage { Effects = CriticalHitEffects.Stun }).Events);
        Assert.AreEqual(Hurt(), Damage(Hurt(), new LifeDamage { Effects = CriticalHitEffects.Stun | CriticalHitEffects.Knockdown }).After);

        // ARC-COMBAT-009: the stun starts only on a critter that can act (0x45D790): not unconscious, paralysed or stoned.
        var stun = new LifeDamage { Effects = CriticalHitEffects.Stun };
        Assert.AreEqual(LifeEvents.None, Damage(Hurt(fatigue: LifeRules.MaxFatigue(Plain)), stun).Events & LifeEvents.Stunned, "unconscious");
        Assert.AreEqual(Hurt(), Damage(Hurt(), stun, Plain with { Traits = LifeTraits.Paralyzed }).After, "paralysed");
        Assert.AreEqual(Hurt(), Damage(Hurt(), stun, Plain with { Traits = LifeTraits.Stoned }).After, "stoned");
        var ended = LifeRules.ClearStun(stunned.After, Plain);
        Assert.AreEqual((Hurt(), LifeEvents.StunEnded), (ended.After, ended.Events));
        Assert.AreEqual(LifeEvents.None, LifeRules.ClearStun(Hurt(), Plain).Events);
    }

    [TestMethod]
    public void ArmsAreCrippledOneAfterTheOther()
    {
        var arm = new LifeDamage { Effects = CriticalHitEffects.CrippleArm };
        var first = Damage(Hurt(), arm);
        Assert.AreEqual((LifeConditions.CrippledArmOne, LifeEvents.CrippledArm | LifeEvents.ArmHitDropsItems), (first.After.Conditions, first.Events));
        var spared = Damage(first.After, arm, null, (SecondArm, 51));
        Assert.AreEqual((LifeConditions.CrippledArmOne, LifeEvents.ArmHitDropsItems), (spared.After.Conditions, spared.Events));
        var both = Damage(first.After, arm, null, (SecondArm, 50));
        Assert.AreEqual(LifeConditions.CrippledArmOne | LifeConditions.CrippledArmsBoth, both.After.Conditions);
        Assert.AreEqual(LifeEvents.CrippledBothArms | LifeEvents.ArmHitDropsItems, both.Events);
        // Both arms crippled: no roll, but the wielded items still drop.
        Assert.AreEqual(LifeEvents.ArmHitDropsItems, Damage(both.After, arm).Events);

        var legs = Damage(Hurt(), new LifeDamage { Effects = CriticalHitEffects.CrippleLeg | CriticalHitEffects.Scar });
        Assert.AreEqual(Hurt(conditions: LifeConditions.CrippledLegsBoth, scars: 1), legs.After);
        Assert.AreEqual(LifeEvents.CrippledLegs | LifeEvents.Scarred, legs.Events);
        var scarred = Damage(Hurt(scars: LifeLimits.MaxScars), new LifeDamage { Effects = CriticalHitEffects.Scar });
        Assert.AreEqual((LifeLimits.MaxScars, LifeEvents.ScarLimitReached), (scarred.After.Scars, scarred.Events));
    }

    [TestMethod]
    public void PoisonIsBoundedAndExtraordinaryConstitutionClearsIt()
    {
        Assert.AreEqual(150, Damage(Hurt(poison: 100), new LifeDamage { Poison = 50 }).After.PoisonLevel);
        Assert.AreEqual(1000, Damage(Hurt(poison: 990), new LifeDamage { Poison = 50 }).After.PoisonLevel);
        Assert.AreEqual(0, Damage(Hurt(poison: 30), new LifeDamage { Poison = -50 }).After.PoisonLevel);
        // The source writes the poison level on every damage step: extraordinary constitution resets it even without poison damage.
        Assert.AreEqual(0, Damage(Hurt(poison: 300), new LifeDamage { HitPoints = 1 }, Plain with { Constitution = 20 }).After.PoisonLevel);
        // A resistance above 100 % makes the packet negative: it heals, never below 0.
        Assert.AreEqual(Hurt(2), Damage(Hurt(5), new LifeDamage { HitPoints = -3 }).After);
        Assert.AreEqual(Hurt(), Damage(Hurt(1), new LifeDamage { HitPoints = -3 }).After);
    }

    [TestMethod]
    public void TheDeathCheckReadsTheNewPoisonLevel()
    {
        // 29 damage at poison 99 leaves 1 HP; one more poison makes strength 7, the maximum 28 and the critter dead.
        var outcome = Damage(Hurt(29, poison: 99), new LifeDamage { Poison = 1 });
        Assert.AreEqual((LifeEvents.Died, DeathAnimation.Plain), (outcome.Events, outcome.Death));
        Assert.AreEqual(Hurt(LifeLimits.DeathHitPointDamage, poison: 100), outcome.After);
    }

    [TestMethod]
    public void HealingRemovesDamageWhileThereIsSome()
    {
        var heal = LifeRules.Heal(Hurt(10, 8, 40), Plain, new LifeHeal { HitPoints = 4, Fatigue = 20, Poison = 15 });
        Assert.AreEqual((Hurt(6, 0, 25), LifeEvents.None), (heal.After, heal.Events));
        var awake = LifeRules.Heal(Hurt(0, 35), Plain, new LifeHeal { Fatigue = 10 });
        Assert.AreEqual((Hurt(0, 25), LifeEvents.RegainedConsciousness), (awake.After, awake.Events));

        var all = LifeConditions.Blinded | LifeConditions.CrippledArmOne | LifeConditions.CrippledArmsBoth | LifeConditions.CrippledLegsBoth | LifeConditions.Stunned;
        var full = LifeRules.Heal(Hurt(20, 12, 70, all, 7), Plain, new LifeHeal { Full = true, Fatigue = 1, Scars = true });
        Assert.AreEqual(Hurt(0, 0, 70, LifeConditions.Stunned, 2), full.After);
        Assert.AreEqual(12, LifeRules.Heal(Hurt(20, 12), Plain, new LifeHeal { Full = true }).After.FatigueDamage, "fatigue only when asked");

        // Single cures: legs heal legs, as in the original game (anomaly A5).
        Assert.AreEqual(LifeConditions.Blinded, LifeRules.Heal(Hurt(conditions: all & ~LifeConditions.Stunned), Plain,
            new LifeHeal { CrippledArms = true, CrippledLegs = true }).After.Conditions);
        Assert.AreEqual(LifeConditions.CrippledLegsBoth, LifeRules.Heal(Hurt(conditions: LifeConditions.Blinded | LifeConditions.CrippledLegsBoth), Plain,
            new LifeHeal { Blindness = true }).After.Conditions);
        Assert.AreEqual(1, LifeRules.Heal(Hurt(scars: 6), Plain, new LifeHeal { Scars = true }).After.Scars);
        Assert.AreEqual(0, LifeRules.Heal(Hurt(poison: 30), Plain with { Constitution = 20 }, new LifeHeal { Poison = 1 }).After.PoisonLevel);
    }

    [TestMethod]
    public void TheDeadNeedAResurrection()
    {
        var dead = Hurt(LifeLimits.DeathHitPointDamage, 3, scars: 6, conditions: LifeConditions.Stunned);
        // Scars go before the dead check, as in the source.
        var plain = LifeRules.Heal(dead, Plain, new LifeHeal { HitPoints = 50, Scars = true });
        Assert.AreEqual((dead with { Scars = 1 }, LifeEvents.AlreadyDead), (plain.After, plain.Events));

        var refused = LifeRules.Heal(dead, Plain with { Traits = LifeTraits.Unresurrectable }, new LifeHeal { Resurrect = true, Full = true });
        Assert.AreEqual((dead, LifeEvents.ResurrectionRefused), (refused.After, refused.Events));
        Assert.AreEqual(LifeEvents.ResurrectionRefused,
            LifeRules.Heal(dead, Plain with { Traits = LifeTraits.Unrevivifiable }, new LifeHeal { Resurrect = true, Reanimate = true }).Events);
        Assert.AreEqual(LifeEvents.Resurrected,
            LifeRules.Heal(dead, Plain with { Traits = LifeTraits.Unrevivifiable }, new LifeHeal { Resurrect = true }).Events);

        // Without a hit-point heal the resurrected critter is still dead; fatigue damage is 10 either way.
        var still = LifeRules.Heal(dead, Plain, new LifeHeal { Resurrect = true });
        Assert.AreEqual(Hurt(LifeLimits.DeathHitPointDamage, 10, scars: 6), still.After);
        Assert.IsTrue(LifeRules.Evaluate(still.After, Plain).IsDead);
        var back = LifeRules.Heal(dead, Plain, new LifeHeal { Resurrect = true, Full = true });
        Assert.AreEqual((Hurt(0, 10, scars: 1), LifeEvents.Resurrected), (back.After, back.Events));
        Assert.IsTrue(LifeRules.Evaluate(back.After, Plain).IsActive);
    }

    [TestMethod]
    public void FatigueRecoversByHealRatePerEightySeconds()
    {
        Assert.AreEqual(1, LifeRules.FatigueRecoverySteps(0, 0));
        Assert.AreEqual(1, LifeRules.FatigueRecoverySteps(1_000, 80_999));
        Assert.AreEqual(2, LifeRules.FatigueRecoverySteps(0, 160_000));
        // Whole game seconds of both times, as the source counts them.
        Assert.AreEqual(2, LifeRules.FatigueRecoverySteps(999, 160_998), "160 − 0 = 160 s");
        Assert.AreEqual(1, LifeRules.FatigueRecoverySteps(1_000, 160_999), "160 − 1 = 159 s");
        Assert.AreEqual((1, 2), (LifeRules.RestingHours(0, 3_599_999), LifeRules.RestingHours(0, 7_200_000)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LifeRules.FatigueRecoverySteps(5, 4));

        var recovered = LifeRules.RecoverFatigue(Hurt(0, 35), Plain, 2);
        Assert.AreEqual((Hurt(0, 29), LifeEvents.RegainedConsciousness), (recovered.After, recovered.Events));
        Assert.AreEqual(Hurt(0, 0), LifeRules.RecoverFatigue(Hurt(0, 4), Plain, 9).After);
        var dead = Hurt(LifeLimits.DeathHitPointDamage, 35);
        Assert.AreEqual((dead, LifeEvents.AlreadyDead), (LifeRules.RecoverFatigue(dead, Plain, 1).After, LifeRules.RecoverFatigue(dead, Plain, 1).Events));
    }

    [TestMethod]
    public void RestingHealsHitPointsAndThreeTimesTheFatigue()
    {
        Assert.AreEqual(Hurt(4, 2), LifeRules.RestHeal(Hurt(10, 20), Plain, 2).After);
        Assert.AreEqual(Hurt(), LifeRules.RestHeal(Hurt(1, 1), Plain, 1).After);
        Assert.AreEqual(Hurt(10, 20), LifeRules.RestHeal(Hurt(10, 20), Plain, 0).After);
        Assert.AreEqual(LifeEvents.AlreadyDead, LifeRules.RestHeal(Hurt(LifeLimits.DeathHitPointDamage), Plain, 5).Events);
    }

    [TestMethod]
    public void PoisonTicksDamageAndRecover()
    {
        Assert.AreEqual(1, LifeRules.PoisonDamage(Hurt(poison: 199), Plain).After.HitPointDamage);
        Assert.AreEqual(2, LifeRules.PoisonDamage(Hurt(poison: 200), Plain).After.HitPointDamage);
        Assert.AreEqual(2, LifeRules.PoisonDamage(Hurt(poison: 549), Plain).After.HitPointDamage);
        Assert.AreEqual(3, LifeRules.PoisonDamage(Hurt(poison: 550), Plain).After.HitPointDamage);
        Assert.AreEqual(2, LifeRules.PoisonDamage(Hurt(poison: 550), Plain with { NormalResistance = 50 }).After.HitPointDamage, "3 − 3 × 50 / 100");
        Assert.AreEqual(Hurt(), LifeRules.PoisonDamage(Hurt(), Plain).After);
        Assert.AreEqual(Hurt(poison: 600), LifeRules.PoisonDamage(Hurt(poison: 600), Plain with { Traits = LifeTraits.NighInvulnerable }).After);
        Assert.AreEqual(Hurt(poison: 600), LifeRules.PoisonDamage(Hurt(poison: 600), Plain with { Traits = LifeTraits.Invulnerable }).After);
        // Poison 199 costs one strength: maximum 28, so 27 damage leaves 1 hit point for the tick to take.
        var killed = LifeRules.PoisonDamage(Hurt(27, poison: 199), Plain);
        Assert.AreEqual((LifeEvents.Died, DeathAnimation.Plain), (killed.Events, killed.Death));

        Assert.AreEqual(Hurt(poison: 12), LifeRules.PoisonRecoveryTick(Hurt(poison: 20), Plain).After);
        Assert.AreEqual(Hurt(), LifeRules.PoisonRecoveryTick(Hurt(poison: 5), Plain).After);
        // The recovery tick does not check for death.
        Assert.AreEqual(12, LifeRules.PoisonRecoveryTick(Hurt(LifeLimits.DeathHitPointDamage, poison: 20), Plain).After.PoisonLevel);
    }

    [TestMethod]
    public void AKillPinsTheDamageEvenOnTheInvulnerable()
    {
        var kill = LifeRules.Kill(Hurt(10), Plain);
        Assert.AreEqual((Hurt(LifeLimits.DeathHitPointDamage), LifeEvents.Died, DeathAnimation.Plain, 20),
            (kill.After, kill.Events, kill.Death, kill.HitPointDamage));
        var invulnerable = LifeRules.Kill(Hurt(), Plain with { Traits = LifeTraits.Invulnerable });
        Assert.AreEqual((Hurt(LifeLimits.DeathHitPointDamage), LifeEvents.None), (invulnerable.After, invulnerable.Events));
        Assert.IsTrue(LifeRules.Evaluate(invulnerable.After, Plain).IsDead);
        var again = LifeRules.Kill(kill.After, Plain);
        Assert.AreEqual((LifeEvents.AlreadyDead, false), (again.Events, again.Changed));
    }

    [TestMethod]
    public void AnAttackBecomesAPacketForItsRecipient()
    {
        var hit = new AttackResolution(AttackRefusal.None, AttackOutcome.CriticalHit, AttackMissCause.None, HitLocation.Head, true, false,
            null, null, null, FateUse.None, false,
            new AttackDamage(DamageRecipient.Target, DamageByType.Zero, DamageByType.Zero, 14, 3, 6, DamageMultiplier.Double, false,
                CriticalHitEffects.Stun | CriticalHitEffects.DropHelmet, CriticalMissEffects.None, false, DamagePrevention.None), []);
        Assert.AreEqual(new LifeDamage { HitPoints = 14, Fatigue = 3, Poison = 6, Effects = CriticalHitEffects.Stun | CriticalHitEffects.DropHelmet, Location = HitLocation.Head },
            LifeDamage.FromAttack(hit));
        Assert.IsNull(LifeDamage.FromAttack(hit with { Damage = hit.Damage! with { Prevented = DamagePrevention.Invulnerable } }));
        Assert.IsNull(LifeDamage.FromAttack(hit with { Outcome = AttackOutcome.Miss, Damage = null }));

        // The item effects are not the life state's.
        var outcome = Damage(Hurt(), LifeDamage.FromAttack(hit)!);
        Assert.AreEqual((Hurt(14, 3, 6, LifeConditions.Stunned), LifeEvents.Stunned), (outcome.After, outcome.Events));
    }

    [TestMethod]
    public void AnInvulnerableCritterTakesNothingFromAnyPacket()
    {
        var invulnerable = Plain with { Traits = LifeTraits.Invulnerable };
        var outcome = Damage(Hurt(), new LifeDamage { HitPoints = 50, Poison = 9, Effects = CriticalHitEffects.Knockout | CriticalHitEffects.Blind }, invulnerable);
        Assert.AreEqual((Hurt(), LifeEvents.None, false), (outcome.After, outcome.Events, outcome.Changed));
    }

    [TestMethod]
    public void AKillWhosePoisonResetLiftsStrengthSkipsTheDeathStep()
    {
        // Poison 300 costs 3 strength (maximum 24); the kill takes those 24, then the extraordinary-constitution reset lifts
        // the maximum back to 30, so the critter is not dead at the death check: no death step, but the pin still follows.
        var hardy = Plain with { Constitution = 20 };
        var kill = LifeRules.Kill(Hurt(poison: 300), hardy);
        Assert.AreEqual((Hurt(LifeLimits.DeathHitPointDamage), LifeEvents.None, DeathAnimation.None), (kill.After, kill.Events, kill.Death));
        Assert.IsTrue(LifeRules.Evaluate(kill.After, hardy).IsDead);
    }

    [TestMethod]
    public void ACorpseNeitherFallsUnconsciousNorGetsUp()
    {
        var knockedOut = Hurt(LifeLimits.DeathHitPointDamage, 45);
        var resurrected = LifeRules.Heal(knockedOut, Plain, new LifeHeal { Resurrect = true });
        Assert.AreEqual((10, LifeEvents.Resurrected), (resurrected.After.FatigueDamage, resurrected.Events));
        var killedAwake = Damage(Hurt(10), new LifeDamage { HitPoints = 40, Fatigue = 40 });
        Assert.AreEqual(LifeEvents.Died, killedAwake.Events);
        Assert.AreEqual(LifeLimits.MaxRestHours, LifeRules.RestingHours(0, long.MaxValue / 2));
    }

    [TestMethod]
    public void OutOfRangeInputsThrow()
    {
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(-1), Plain));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(poison: 1001), Plain));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(conditions: (LifeConditions)64), Plain));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(scars: LifeLimits.MaxScars + 1), Plain));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(), Plain with { HitPointPoints = LifeLimits.MaxPoolPoints + 1 }));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(), Plain with { Level = 52 }));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(), Plain with { PoisonedStrength = [7, 6] }));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Evaluate(Hurt(), Plain with { Traits = (LifeTraits)512 }));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.ApplyDamage(Hurt(), Plain, new LifeDamage { HitPoints = LifeLimits.MaxAmount + 1 }, None));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.ApplyDamage(Hurt(), Plain, new LifeDamage { Effects = (CriticalHitEffects)(1 << 11) }, None));
        Assert.ThrowsExactly<ArgumentException>(() => LifeRules.Heal(Hurt(), Plain, new LifeHeal { HitPoints = -1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LifeRules.RestHeal(Hurt(), Plain, LifeLimits.MaxRestHours + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LifeRules.RecoverFatigue(Hurt(), Plain, 0));
        Assert.IsTrue(LifeRules.IsValid(Plain with { PoisonedStrength = ImmutableArray<int>.Empty }));
        // Every maximum stays below the pinned death damage.
        var largest = Plain with { HitPointPoints = LifeLimits.MaxPoolPoints, HitPointAdjustment = LifeLimits.MaxPoolAdjustment,
            Level = LifeLimits.MaxLevel, Strength = LifeLimits.MaxStat, Willpower = LifeLimits.MaxStat };
        Assert.IsTrue(LifeRules.MaxHitPoints(largest, 0) < LifeLimits.DeathHitPointDamage);
    }
}
