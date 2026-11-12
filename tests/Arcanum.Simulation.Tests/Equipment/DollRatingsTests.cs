using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Equipment;

// ARC-UI-1121 (ledger IW25, IW29): the two numbers under the doll, the original's displayed damage rating (0x464630) and armour rating (0x464700).
// Every value is invented; the expected numbers are worked out by hand from the tables the functions read (weights 15 | 12 12 18 15 12 and 24 | 25 15 10 16 15).
[TestClass]
public sealed class DollRatingsTests
{
    [TestMethod]
    public void TheWeightsAddToTheOriginalsDivisors()
    {
        Assert.AreEqual(90, DollRatings.SkillWeight + DollRatings.DamageTypeWeights.Sum() + DollRatings.DivisorOffset, "84 + 6");
        Assert.AreEqual(111, DollRatings.ArmorClassWeight + DollRatings.ResistanceWeights.Sum() + DollRatings.DivisorOffset, "105 + 6");
    }

    [TestMethod]
    public void BareHandsWeighNormalAndFatigueAndTheSkill()
    {
        // Bare hands: normal and fatigue 1..5 (average 3 each) at weights 12 and 12; melee level 4 is an effectiveness of 45: (15 × 45 + 36 + 36) / 90 = 747 / 90 = 8.
        Assert.AreEqual(8, DollRatings.Damage(CombatWeapon.Unarmed(), 0, CombatSkill.Melee, 45));
        // A gauntlet's unarmed bonus 30 gives 5..35 (average 20): (675 + 240 + 240) / 90 = 12.
        Assert.AreEqual(12, DollRatings.Damage(CombatWeapon.Unarmed(30), 0, CombatSkill.Melee, 45));
    }

    [TestMethod]
    public void AWeaponWeighsEachOfItsFiveTypesAtItsOwnWeight()
    {
        var weapon = new CombatWeapon
        {
            Kind = WeaponKind.Weapon,
            Damage = new DamageRanges(new(10, 20), new(2, 4), new(0, 0), new(8, 10), new(0, 0)),
        };
        // Averages 15, 3, 0, 9, 0 at weights 12, 12, 18, 15, 12: 180 + 36 + 0 + 135 + 0 = 351; plus 15 × 60 = 900: 1251 / 90 = 13.
        Assert.AreEqual(13, DollRatings.Damage(weapon, 0, CombatSkill.Bow, 60));
        // A melee damage bonus of 4 lifts normal to 14..24 (average 19): 12 × 4 = 48 more: 1299 / 90 = 14. The bonus reads for melee only: a bow takes none.
        Assert.AreEqual(14, DollRatings.Damage(weapon, 4, CombatSkill.Melee, 60));
        Assert.AreEqual(13, DollRatings.Damage(weapon, 4, CombatSkill.Bow, 60));
    }

    [TestMethod]
    public void TheArmourRatingDividesBeforeItDoubles()
    {
        // 24 × 10 + 25 × 20 + 15 × 0 + 10 × 0 + 16 × 5 + 15 × 10 = 970; 970 / 111 = 8, doubled 16 (doubling first would give 17).
        Assert.AreEqual(16, DollRatings.Armour(10, 20, 0, 0, 5, 10));
        Assert.AreEqual(0, DollRatings.Armour(0, 0, 0, 0, 0, 0));
        // 24 × 95 + 95 × 81 = 9975; 9975 / 111 = 89, doubled 178: a fully armoured critter's top value.
        Assert.AreEqual(178, DollRatings.Armour(95, 95, 95, 95, 95, 95));
    }

    [TestMethod]
    public void AWeaponIsTooHeavyOnlyAboveTheWearersStrength()
    {
        Assert.IsTrue(DollRatings.TooHeavy(9, 8));
        Assert.IsFalse(DollRatings.TooHeavy(8, 8));
        Assert.IsFalse(DollRatings.TooHeavy(1, 8));
    }

    [TestMethod]
    public void TheDisplayedMagicResistanceAddsWornAndEffectsAndClamps()
    {
        Assert.AreEqual(30, CombatantRules.MagicResistance(10, 20, false, []));
        Assert.AreEqual(95, CombatantRules.MagicResistance(90, 40, false, []), "cut to 95");
        Assert.AreEqual(0, CombatantRules.MagicResistance(10, -40, false, []), "and not below 0");
        Assert.AreEqual(130, CombatantRules.MagicResistance(90, 40, true, []), "a monstrous NPC keeps its sum");
    }
}
