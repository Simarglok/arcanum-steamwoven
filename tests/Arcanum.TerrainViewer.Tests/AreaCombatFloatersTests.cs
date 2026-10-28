using Arcanum.Presentation.Hud;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-021 viewer core: an attack's session results as its floaters read them (the original's message conditions, ledger CH12,
// CW2, CW3, CW7) and the combat.mes texts. Results are built by hand.
[TestClass]
public sealed class AreaCombatFloatersTests
{
    private static AttackResolution Resolution(AttackOutcome outcome, CriticalHitEffects effects = default, DamageMultiplier multiplier = DamageMultiplier.None,
        DamageRecipient recipient = DamageRecipient.Target, AttackMissCause cause = AttackMissCause.None, int fatigue = 0, int poison = 0) =>
        new(AttackRefusal.None, outcome, cause, HitLocation.Torso, false, false, null, null, null, FateUse.None, recipient == DamageRecipient.Attacker,
            outcome is AttackOutcome.Hit or AttackOutcome.CriticalHit or AttackOutcome.CriticalMiss
                ? new AttackDamage(recipient, DamageByType.Zero, DamageByType.Zero, 6, fatigue, poison, multiplier, false, effects, default, false, DamagePrevention.None)
                : null, []);

    private static LifeOutcome Life(LifeEvents events, LifeConditions before = LifeConditions.None) =>
        new(new(0, 0, 0, before, 0), new(6, 0, 0, before, 0), events, 6, DeathAnimation.None);

    private static SessionAttackResult Result(AttackResolution resolution, LifeOutcome? life, DamageRecipient recipient = DamageRecipient.Target,
        IReadOnlyList<SessionCriticalStep>? criticals = null, IReadOnlyList<SessionWearStep>? wear = null) =>
        new(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, life is null ? null : recipient, life, null, null, GameSessionFailure.None, default)
        {
            Criticals = criticals ?? [],
            Wear = wear is null ? null : new(GameSessionDisposition.Changed, GameSessionFailure.None, wear, GameSessionFailure.None, default),
        };

    private static SessionWearStep Wear(SessionWearStepKind kind, DamageRecipient wearer, ItemBreakStep broken = ItemBreakStep.None, SessionWearSkip skip = SessionWearSkip.None) =>
        new(kind, wearer, null, "item", 10, skip, new ItemDurabilityResult(GameSessionDisposition.Changed, GameSessionFailure.None, null, broken, null, default), null);

    private static SessionCriticalStep Step(SessionCriticalStepKind kind, SessionCriticalStepOutcome outcome, DamageRecipient whose = DamageRecipient.Target) =>
        new(kind, whose, EquipmentSlot.Weapon, "item", outcome, null);

    [TestMethod]
    public void A_critical_hit_reads_its_effects_items_and_numbers()
    {
        var resolution = Resolution(AttackOutcome.CriticalHit, CriticalHitEffects.Stun | CriticalHitEffects.Scar | CriticalHitEffects.CrippleArm | CriticalHitEffects.DamageArmor,
            DamageMultiplier.Triple, fatigue: 2, poison: 4);
        var result = Result(resolution, Life(LifeEvents.ScarLimitReached | LifeEvents.CrippledBothArms | LifeEvents.ArmHitDropsItems), DamageRecipient.Target,
            [
                Step(SessionCriticalStepKind.ArmHitWeapon, SessionCriticalStepOutcome.Refused),
                Step(SessionCriticalStepKind.ArmHitShield, SessionCriticalStepOutcome.Kept),
                Step(SessionCriticalStepKind.DropWeapon, SessionCriticalStepOutcome.NoItem),
                Step(SessionCriticalStepKind.LoseAmmo, SessionCriticalStepOutcome.NoAmmunition),
            ],
            [
                Wear(SessionWearStepKind.Armor, DamageRecipient.Target, ItemBreakStep.Broken),
                Wear(SessionWearStepKind.WeaponDamage, DamageRecipient.Target, skip: SessionWearSkip.Invulnerable),
                Wear(SessionWearStepKind.AttackerWeapon, DamageRecipient.Attacker),
            ]);
        var hit = AreaCombatFloaters.Of(result, FloaterSide.Pc, FloaterSide.PcFollower)!;
        Assert.IsTrue(hit.CritterStep);
        Assert.AreEqual(FloaterSide.PcFollower, hit.Struck);
        Assert.AreEqual(FloaterCritical.Hit, hit.Critical);
        Assert.AreEqual(FloaterMultiplier.Triple, hit.Multiplier);
        Assert.IsTrue(hit.Stunned, "the stun bit on a critter that was not stunned, whether or not its animation could start");
        Assert.IsTrue(hit.Scarred, "the scar message floats at the scar limit too");
        Assert.IsTrue(hit.CrippledArm, "the second arm");
        Assert.IsTrue(hit.ArmDropsWeapon, "a refused drop still floats");
        Assert.IsFalse(hit.ArmDropsShield, "a kept item is not dropped");
        Assert.IsFalse(hit.WeaponDropped, "no weapon to drop");
        Assert.IsTrue(hit.AmmunitionLost, "the step ran on the weapon's ammunition");
        Assert.AreEqual(FloaterItem.Broken, hit.Armor);
        Assert.AreEqual(FloaterItem.None, hit.Weapon, "an invulnerable weapon passes without a message");
        Assert.AreEqual(FloaterItem.Damaged, hit.AttackerWeapon);
        Assert.AreEqual((6, 2, 4), (hit.HitPoints, hit.Fatigue, hit.Poison));
        Assert.IsFalse(hit.KnockedDown || hit.KnockedOut || hit.Blinded || hit.CrippledLegs || hit.HelmetDropped || hit.WeaponDestroyed);
    }

    [TestMethod]
    public void The_stun_and_the_fall_follow_the_originals_tests()
    {
        var stunnedBefore = AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.CriticalHit, CriticalHitEffects.Stun), Life(LifeEvents.None, LifeConditions.Stunned)),
            FloaterSide.Pc, FloaterSide.Other)!;
        Assert.IsFalse(stunnedBefore.Stunned, "already stunned");
        var down = AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.CriticalHit, CriticalHitEffects.Stun | CriticalHitEffects.Knockdown), Life(LifeEvents.KnockedDown)),
            FloaterSide.Pc, FloaterSide.Other)!;
        Assert.IsFalse(down.Stunned, "a knockdown bit stops the stun");
        Assert.IsTrue(down.KnockedDown);
        var unconscious = AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.CriticalHit, CriticalHitEffects.Knockout | CriticalHitEffects.Knockdown),
            Life(LifeEvents.KnockedDown | LifeEvents.KnockedOut | LifeEvents.LostConsciousness)), FloaterSide.Pc, FloaterSide.Other)!;
        Assert.IsTrue(unconscious.KnockedOut);
        Assert.IsFalse(unconscious.KnockedDown, "a knockout bit is never the knockdown message");
        var already = AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.CriticalHit, CriticalHitEffects.Knockout), Life(LifeEvents.KnockedDown)), FloaterSide.Pc, FloaterSide.Other)!;
        Assert.IsFalse(already.KnockedOut || already.KnockedDown, "a knockout on a critter already unconscious floats nothing");
    }

    [TestMethod]
    public void Misses_dodges_self_hits_and_the_dead()
    {
        Assert.IsTrue(AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.Miss, cause: AttackMissCause.Dodged), null), FloaterSide.Pc, FloaterSide.Other)!.Dodged);
        Assert.IsNull(AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.Miss, cause: AttackMissCause.SkillCheck), null), FloaterSide.Pc, FloaterSide.Other));
        var self = AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.CriticalMiss, recipient: DamageRecipient.Attacker, multiplier: DamageMultiplier.OneAndHalf),
            Life(LifeEvents.None), DamageRecipient.Attacker, [Step(SessionCriticalStepKind.DestroyAmmo, SessionCriticalStepOutcome.AmmunitionSpent, DamageRecipient.Attacker)],
            [Wear(SessionWearStepKind.WeaponDestroy, DamageRecipient.Attacker)]), FloaterSide.PcFollower, FloaterSide.Pc)!;
        Assert.AreEqual((FloaterCritical.Miss, FloaterSide.PcFollower, FloaterMultiplier.OneAndHalf), (self.Critical, self.Struck, self.Multiplier),
            "a critical self-hit is the attacker's step");
        Assert.IsTrue(self.WeaponDestroyed && self.AmmunitionLost);
        var dead = AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.Hit), Life(LifeEvents.AlreadyDead)), FloaterSide.Pc, FloaterSide.Other)!;
        Assert.IsFalse(dead.CritterStep, "a target dead before the hit");
        var refused = Result(Resolution(AttackOutcome.Hit), null) with { Disposition = GameSessionDisposition.Rejected };
        Assert.IsNull(AreaCombatFloaters.Of(refused, FloaterSide.Pc, FloaterSide.Other));
        Assert.IsNull(AreaCombatFloaters.Of(Result(Resolution(AttackOutcome.Hit) with { Refusal = AttackRefusal.AttackerInactive }, null), FloaterSide.Pc, FloaterSide.Other));
    }

    [TestMethod]
    public void An_object_floats_its_damage_and_the_attackers_hands()
    {
        var resolution = Resolution(AttackOutcome.Hit);
        var hands = new SessionWearStep(SessionWearStepKind.AttackerHands, DamageRecipient.Attacker, null, null, 3, SessionWearSkip.None, null,
            new LifeOutcome(LifeState.Unharmed, new(2, 0, 0, LifeConditions.None, 0), LifeEvents.None, 2, DeathAnimation.None));
        var result = new SessionObjectAttackResult(GameSessionDisposition.Changed, GameSessionFailure.None, resolution, new ObjectHitOutcome(0, 5, false, default, 0),
            null, null, new(GameSessionDisposition.Changed, GameSessionFailure.None, [hands], GameSessionFailure.None, default), default, GameSessionFailure.None, default);
        var hit = AreaCombatFloaters.Of(result, FloaterSide.Pc)!;
        Assert.IsTrue(hit.ObjectStep && !hit.CritterStep);
        Assert.AreEqual(5, hit.HitPoints);
        Assert.AreEqual(2, hit.AttackerHands, "the capped loss the session applied");
        var negative = result with { Object = new ObjectHitOutcome(0, -4, false, default, 0) };
        Assert.AreEqual(0, AreaCombatFloaters.Of(negative, FloaterSide.Pc)!.HitPoints, "never below 0");
    }

    [TestMethod]
    public void The_texts_are_the_games_with_the_projects_where_it_has_none()
    {
        var texts = AreaCombatTexts.From(new Dictionary<int, string> { [11] = "Ausgewichen!" }, "test");
        Assert.AreEqual("Ausgewichen!", texts.Text(CombatFloaters.DodgeLine));
        Assert.AreEqual("Critical hit", texts.Text(CombatFloaters.CriticalHitLine), "the project's text");
        Assert.AreEqual("[25]", texts.Text(25));
        Assert.AreEqual(CombatFloaters.LastLine + 1, AreaCombatTexts.Count, "every message the floaters read");
        Assert.IsFalse(AreaCombatTexts.Project("none").FromGame);
    }
}
