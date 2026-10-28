using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Tests.Combat.CombatFixtures;

namespace Arcanum.Simulation.Tests.Combat;

/// <summary>Each D2 fix (Steamwoven setting) against the original game's behaviour (Original setting).</summary>
[TestClass]
public sealed class CombatAnomalyProfileTests
{
    [TestMethod]
    public void TwoSettingsSteamwovenFirst()
    {
        var steamwoven = CombatAnomalyProfile.Steamwoven;
        Assert.AreEqual("steamwoven", steamwoven.Name);
        Assert.IsFalse(steamwoven.InvocationModifierCountedTwice || steamwoven.ArmorClassEffectsCountedTwice
            || steamwoven.CriticalSelfHitRolledTwice || steamwoven.BackstabDamageOnSelfHit);
        var original = CombatAnomalyProfile.Original;
        Assert.AreEqual("original", original.Name);
        Assert.IsTrue(original.InvocationModifierCountedTwice && original.ArmorClassEffectsCountedTwice
            && original.CriticalSelfHitRolledTwice && original.BackstabDamageOnSelfHit);
        // ARC-COMBAT-015: EX9's switch.
        Assert.AreEqual((false, true), (steamwoven.RangedApproachSkipsClearShot, original.RangedApproachSkipsClearShot));
        CollectionAssert.AreEqual(new[] { steamwoven, original }, CombatAnomalyProfile.All.ToArray());
        Assert.AreSame(original, CombatAnomalyProfile.FromName("original"));
        Assert.AreSame(steamwoven, CombatAnomalyProfile.FromName("steamwoven"));
        Assert.IsNull(CombatAnomalyProfile.FromName("Original"));
        Assert.IsNull(CombatAnomalyProfile.FromName(null));
    }

    [TestMethod]
    public void ResolverDefaultsToSteamwoven()
    {
        var request = Melee() with { SkillLevel = 0, Target = Npc() with { ArmorClass = 95 }, TargetWasInBed = true };
        var rolls = Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 100) });
        Assert.AreEqual(-30, Run(request, rolls, profile: null).Difficulty!.Invocation);
    }

    [TestMethod]
    public void A2BedModifierCountsOnceByDefault()
    {
        // Skill 0 against AC 95 keeps every roll a miss, so only the difficulty matters.
        var request = Melee() with { SkillLevel = 0, Target = Npc() with { ArmorClass = 95 }, TargetWasInBed = true };
        var rolls = Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 100) });
        Assert.AreEqual(-30, Run(request, rolls).Difficulty!.Invocation);
        Assert.AreEqual(-60, Run(request, rolls, profile: CombatAnomalyProfile.Original).Difficulty!.Invocation);
        Assert.AreEqual(0, Run(request with { TargetWasInBed = false }, rolls, profile: CombatAnomalyProfile.Original).Difficulty!.Invocation);
    }

    [TestMethod]
    public void A7CriticalSelfHitRollsItsEffectsOnceByDefault()
    {
        var criticalMiss = Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 1), (CriticalSelfHit, 1) });

        var effects = new FixedCriticalEffects();
        var fixedResult = Run(Melee(), Concat(criticalMiss, new[] { (Normal, 5) }, NoMultiplier()), effects);
        Assert.AreEqual(AttackOutcome.CriticalMiss, fixedResult.Outcome);
        Assert.IsTrue(fixedResult.CriticalSelfHit);
        Assert.AreEqual(DamageMultiplier.OneAndHalf, fixedResult.Damage!.Multiplier);
        Assert.AreEqual(7, fixedResult.Damage.HitPointDamage);
        Assert.AreEqual(1, effects.HitContexts.Count);
        Assert.IsTrue(effects.HitContexts[0].SelfHit);
        Assert.IsFalse(effects.HitContexts[0].Aimed);
        Assert.AreEqual(1, effects.MissContexts.Count);
        Assert.AreEqual(CriticalMissChart.Bludgeon, effects.MissContexts[0].Chart);

        // Original: the critical-miss branch rolls first (falls back to ×1.5), then the damage step rolls ×3; flags add up.
        var sourceEffects = new FixedCriticalEffects();
        var source = Run(Melee(), Concat(criticalMiss, NoMultiplier(), new[] { (Normal, 5), (Triple, 1) }), sourceEffects,
            CombatAnomalyProfile.Original);
        Assert.AreEqual(DamageMultiplier.Triple, source.Damage!.Multiplier);
        Assert.IsFalse(source.Damage.MultiplierFromFallback);
        Assert.AreEqual(15, source.Damage.HitPointDamage);
        Assert.AreEqual(2, sourceEffects.HitContexts.Count);

        // With a miss effect from the table, the fallback never fires.
        var lost = new FixedCriticalEffects { Miss = CriticalMissEffects.LoseAmmo };
        var withMissEffect = Run(Melee(), Concat(criticalMiss, new[] { (Normal, 5) }, NoMultiplier()), lost);
        Assert.AreEqual(DamageMultiplier.None, withMissEffect.Damage!.Multiplier);
        Assert.AreEqual(CriticalMissEffects.LoseAmmo, withMissEffect.Damage.MissEffects);
        Assert.AreEqual(5, withMissEffect.Damage.HitPointDamage);
    }

    [TestMethod]
    public void A8CancelledMagicGivesCriticalEffectChanceZeroInBothProfiles()
    {
        var request = Melee() with
        {
            Target = Npc() with { MagickTechAptitude = -60 },
            Weapon = Sword() with { MagickTechComplexity = 50, MagicCriticalEffectChance = 20 },
        };
        (CombatRollPurpose, int)[] Rolls(int cancel) => Concat(
            new[] { (LocationRoll, 50), (TechnologyFailure, 100), (MagickCancel, cancel) },
            new[] { (AttackCheck, 1), (AttackCritical, 1), (DodgeCheck, 100), (DodgeCritical, 100), (Normal, 5) },
            NoMultiplier());

        foreach (var profile in new[] { CombatAnomalyProfile.Steamwoven, CombatAnomalyProfile.Original })
        {
            var cancelled = new FixedCriticalEffects();
            Assert.IsTrue(Run(request, Rolls(60), cancelled, profile).WeaponMagicCancelled);
            Assert.AreEqual(0, cancelled.HitContexts.Single().Chance);

            var kept = new FixedCriticalEffects();
            Assert.IsFalse(Run(request, Rolls(61), kept, profile).WeaponMagicCancelled);
            Assert.AreEqual(20, kept.HitContexts.Single().Chance);
        }
    }

    [TestMethod]
    public void A10BackstabDamageSkipsTheSelfHitByDefault()
    {
        var request = Melee() with
        {
            Attacker = Npc() with { BackstabLevel = 4, BackstabTraining = SkillTraining.Apprentice },
            Backstab = BackstabKind.TrueBackstab,
        };
        var rolls = Concat(Opening(), new[] { (AttackCheck, 100), (AttackCritical, 1), (CriticalSelfHit, 100), (Normal, 5) });
        var fixedResult = Run(request, rolls);
        Assert.AreEqual(DamageRecipient.Attacker, fixedResult.Damage!.Recipient);
        Assert.AreEqual(5, fixedResult.Damage.HitPointDamage);
        Assert.AreEqual(25, Run(request, rolls, profile: CombatAnomalyProfile.Original).Damage!.HitPointDamage);
    }
}
