using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-COMBAT-1738 (ledger OH3 case c, damage step 0x4B4390 messages 0x13, 0x16, 0x17): which attacks that struck the hero took its weapon.</summary>
[TestClass]
public sealed class AreaHeroWeaponLossTests
{
    private static SessionCriticalStep Critical(SessionCriticalStepKind kind, SessionCriticalStepOutcome outcome, DamageRecipient whose = DamageRecipient.Target) =>
        new(kind, whose, EquipmentSlot.Weapon, "kit:1", outcome, null);

    private static SessionAttackWearResult Wear(SessionWearStepKind kind, ItemBreakStep step, DamageRecipient wearer = DamageRecipient.Target,
        SessionWearSkip skip = SessionWearSkip.None, EquipmentSlot slot = EquipmentSlot.Weapon) =>
        new(GameSessionDisposition.Changed, GameSessionFailure.None,
            [new SessionWearStep(kind, wearer, slot, "kit:1", 10, skip, new ItemDurabilityResult(GameSessionDisposition.Changed, GameSessionFailure.None, null, step, slot, default), null)],
            GameSessionFailure.None, default);

    [TestMethod]
    public void A_dropped_destroyed_or_broken_weapon_of_the_struck_side_counts()
    {
        Assert.IsTrue(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [Critical(SessionCriticalStepKind.DropWeapon, SessionCriticalStepOutcome.Dropped)], null), "message 0x13");
        Assert.IsTrue(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [], Wear(SessionWearStepKind.WeaponDestroy, ItemBreakStep.Deleted)), "message 0x16");
        Assert.IsTrue(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [], Wear(SessionWearStepKind.WeaponDamage, ItemBreakStep.Broken)), "message 0x17");
    }

    [TestMethod]
    public void Other_steps_and_other_sides_do_not_count()
    {
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [], null));
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [Critical(SessionCriticalStepKind.ArmHitWeapon, SessionCriticalStepOutcome.Dropped)], null), "an arm hit is another branch");
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [Critical(SessionCriticalStepKind.DropWeapon, SessionCriticalStepOutcome.Kept)], null), "no drop happened");
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [Critical(SessionCriticalStepKind.DropWeapon, SessionCriticalStepOutcome.Dropped, DamageRecipient.Attacker)], null), "the attacker's own");
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Attacker, [Critical(SessionCriticalStepKind.DropWeapon, SessionCriticalStepOutcome.Dropped)], null), "the struck side is the target of this hit");
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [], Wear(SessionWearStepKind.WeaponDamage, ItemBreakStep.None)), "damaged, not broken");
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [], Wear(SessionWearStepKind.WeaponDestroy, ItemBreakStep.Deleted, skip: SessionWearSkip.Invulnerable)), "an invulnerable weapon is skipped");
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [], Wear(SessionWearStepKind.AttackerWeapon, ItemBreakStep.Broken, DamageRecipient.Attacker)), "the attacker's wear is case b");
        Assert.IsFalse(AreaHeroWeaponLoss.TookStruckWeapon(DamageRecipient.Target, [], Wear(SessionWearStepKind.Armor, ItemBreakStep.Broken, slot: EquipmentSlot.Armor)), "armour is no weapon");
    }
}
