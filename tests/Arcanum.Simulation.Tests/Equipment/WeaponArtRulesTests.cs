using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Equipment;

// The weapon art and the attack animation (ARC-RENDER-037, ledger WA1–WA8; ARC-RENDER-039 WA9). Art ids are built from their bit fields; none is an
// original object's.
[TestClass]
public sealed class WeaponArtRulesTests
{
    // A human man's critter art id with a weapon part and shield bit.
    private static uint Critter(int weapon = 0, int shield = 0) => 0x28000000u | (uint)shield << 19 | (uint)weapon;
    // An item use art with a weapon subtype.
    private static uint Use(int subtype) => 0x60000000u | (uint)subtype << 6;
    // A monster art id of a species (bits 23..27).
    private static uint Monster(int species) => 0xC0000000u | (uint)species << 23;

    [TestMethod]
    public void OutOfCombatModeNoWeaponOrShieldShows()
    {
        Assert.AreEqual(Critter(), WeaponArtRules.WieldedArt(Critter(2, 1), false, Use(2), WeaponArtRules.ArmorItemType));
    }

    [TestMethod]
    public void InCombatModeTheWieldedWeaponAndAnArmourShieldShow()
    {
        Assert.AreEqual(Critter(2, 1), WeaponArtRules.WieldedArt(Critter(), true, Use(2), WeaponArtRules.ArmorItemType));
        // Bare hands without a weapon, and for a subtype of 0 (logged by the original); a non-armour shield-slot item shows nothing.
        Assert.AreEqual(Critter(1), WeaponArtRules.WieldedArt(Critter(), true, null, null));
        Assert.AreEqual(Critter(1), WeaponArtRules.WieldedArt(Critter(), true, Use(0), 5));
        // Any item type's use art gives the part (the slot's item is not type-checked); a non-item art gives 0, so bare hands.
        Assert.AreEqual(13, WeaponArtRules.WeaponPart(true, Use(13)));
        Assert.AreEqual(1, WeaponArtRules.WeaponPart(true, 0x20000080u));
    }

    [TestMethod]
    public void TheSettersKeepTheirRangesAndArtTypes()
    {
        Assert.AreEqual(Critter(14), WeaponArtRules.WithWeapon(Critter(3), 14));
        Assert.AreEqual(Critter(), WeaponArtRules.WithWeapon(Critter(3), 15), "15 or more logs and sets 0");
        Assert.AreEqual(Critter(0, 0), WeaponArtRules.WithShield(Critter(0, 1), 2), "2 or more logs and sets 0");
        Assert.AreEqual(Monster(3) | 5u, WeaponArtRules.WithWeapon(Monster(3), 5));
        Assert.AreEqual(0xD0000000u | 1u << 19, WeaponArtRules.WithShield(0xD0000000u, 1), "a unique NPC holds both");
        // An item or scenery art is returned unchanged.
        Assert.AreEqual(Use(4), WeaponArtRules.WithWeapon(Use(4), 3));
        Assert.AreEqual(0x30000000u, WeaponArtRules.WithShield(0x30000000u, 1));
        Assert.AreEqual((7, 1), (WeaponArtRules.Weapon(Critter(7, 1)), WeaponArtRules.Shield(Critter(7, 1))));
    }

    [TestMethod]
    public void HeightIsMiddleForEveryCritterAndTheSpeciesTableForMonsters()
    {
        // The critter branch reads the body table's first entry for every body (dwarf body 1 included).
        Assert.AreEqual(1, WeaponArtRules.Height(0x21000000u));
        Assert.AreEqual(1, WeaponArtRules.Height(0x23000000u));
        Assert.AreEqual((0, 1, 2), (WeaponArtRules.Height(Monster(0)), WeaponArtRules.Height(Monster(2)), WeaponArtRules.Height(Monster(3))));
        Assert.AreEqual((2, 1), (WeaponArtRules.Height(Monster(28)), WeaponArtRules.Height(Monster(31))));
        Assert.AreEqual(1, WeaponArtRules.Height(0xD0000000u), "a unique NPC");
    }

    [TestMethod]
    public void TheAttackAnimationFollowsTheWeaponAndTheTarget()
    {
        var standing = Critter();
        Assert.AreEqual(WeaponArtRules.AttackAnimation, WeaponArtRules.Animation(0, 0, 0, standing));
        // A returning weapon throws, whatever the target.
        Assert.AreEqual(WeaponArtRules.ThrowAnimation, WeaponArtRules.Animation(WeaponArtRules.ReturningWeaponFlag, WeaponArtRules.ShrunkFlag, 7, Monster(0)));
        // A shrunk target, one falling, lying or getting up (7..11), or a short monster takes the low attack.
        Assert.AreEqual(WeaponArtRules.LowAttackAnimation, WeaponArtRules.Animation(0, WeaponArtRules.ShrunkFlag, 0, standing));
        Assert.AreEqual(WeaponArtRules.LowAttackAnimation, WeaponArtRules.Animation(0x200, 0, 7, standing));
        Assert.AreEqual(WeaponArtRules.LowAttackAnimation, WeaponArtRules.Animation(0, 0, 11, standing));
        Assert.AreEqual(WeaponArtRules.AttackAnimation, WeaponArtRules.Animation(0, 0, 12, standing));
        Assert.AreEqual(WeaponArtRules.AttackAnimation, WeaponArtRules.Animation(0, 0, 6, standing));
        Assert.AreEqual(WeaponArtRules.LowAttackAnimation, WeaponArtRules.Animation(0, 0, 0, Monster(9)));
        Assert.AreEqual(WeaponArtRules.AttackAnimation, WeaponArtRules.Animation(0, 0, 0, Monster(8)));
    }

    /// <summary>ARC-RENDER-039: an art's animation (0x503E20) is bits 6..10 of a critter, monster or unique NPC art id, 0 for another type.</summary>
    [TestMethod]
    public void TheAnimationOfAnArtIsReadOnlyFromCritterMonsterAndUniqueNpcArts()
    {
        Assert.AreEqual(7, WeaponArtRules.AnimationOf(Critter(2, 1) | 7u << 6));
        Assert.AreEqual(31, WeaponArtRules.AnimationOf(Monster(3) | 31u << 6));
        Assert.AreEqual(21, WeaponArtRules.AnimationOf(0xD0000000u | 21u << 6), "a unique NPC art");
        Assert.AreEqual(0, WeaponArtRules.AnimationOf(0x50000000u | 7u << 6), "a scenery art has no animation");
        Assert.AreEqual(0, WeaponArtRules.AnimationOf(0x10000000u | 9u << 6), "a wall art has no animation");
    }
}
