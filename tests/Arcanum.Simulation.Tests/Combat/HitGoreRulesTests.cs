using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-UI-1737 (ledger OH2): the damage effect's blood eye candy index (0x433020, kind 1) and the violence filter; the dying animation's choice (0x42BC10).
[TestClass]
public sealed class HitGoreRulesTests
{
    [TestMethod]
    public void WithTheFilterOffAnOrdinaryCritterTakesTheDamageTierAndOthersTheirFlagsEffect()
    {
        Assert.AreEqual(0, HitGoreRules.Index(1, false, 0, 0, 0), "a graze keeps index 0");
        Assert.AreEqual(0, HitGoreRules.Index(1, false, 0, 0, 5), "5 and below: no tier");
        Assert.AreEqual(8, HitGoreRules.Index(1, false, 0, 0, 6));
        Assert.AreEqual(8, HitGoreRules.Index(1, false, 0, 0, 9));
        Assert.AreEqual(9, HitGoreRules.Index(1, false, 0, 0, 10));
        Assert.AreEqual(9, HitGoreRules.Index(1, false, 0, 0, 14));
        Assert.AreEqual(10, HitGoreRules.Index(1, false, 0, 0, 15));
        Assert.AreEqual(6, HitGoreRules.Index(1, false, BloodPoolRules.UndeadFlag, 0, 40), "critter flag 0x4 takes 6 and no tier");
        Assert.AreEqual(7, HitGoreRules.Index(1, false, BloodPoolRules.MechanicalFlag, 0, 40), "critter flag 0x20000000 takes 7 and no tier");
        Assert.AreEqual(6, HitGoreRules.Index(1, false, BloodPoolRules.UndeadFlag | BloodPoolRules.MechanicalFlag, 0, 0), "0x4 is tested first");
    }

    [TestMethod]
    public void WithTheFilterOnOrAStonedCritterTheEffectIsAlwaysTheCleanSevenAndTheTiersAreNeverRead()
    {
        foreach (var damage in new[] { 0, 6, 12, 30 })
        {
            Assert.AreEqual(HitGoreRules.CleanIndex, HitGoreRules.Index(1, true, 0, 0, damage));
            Assert.AreEqual(HitGoreRules.CleanIndex, HitGoreRules.Index(1, true, BloodPoolRules.UndeadFlag, 0, damage), "the filter beats the flag effect");
            Assert.AreEqual(HitGoreRules.CleanIndex, HitGoreRules.Index(1, false, 0, HitGoreRules.StonedSpellFlag, damage), "a stoned critter takes it with the filter off too");
        }
    }

    [TestMethod]
    public void AnotherKindStartsAtItsOwnNumberAndTheNoBloodFlagStopsTheEffectBeforeTheChoice()
    {
        Assert.AreEqual(1, HitGoreRules.Index(2, true, 0, 0, 20), "kind 2 is index 1 whatever the filter and the damage");
        Assert.AreEqual(2, HitGoreRules.Index(3, false, 0, 0, 20));
        Assert.IsTrue(HitGoreRules.Refused(0x100));
        Assert.IsFalse(HitGoreRules.Refused(0x400 | 0x200));
    }

    [TestMethod]
    public void TheAttackEndTakesTheKindOfTheLargestPositiveSlotAndNoneForACriticalOrNoDamage()
    {
        // ARC-RENDER-1794 (0x4B6930, ER5, TT14): 1 + the type of the largest positive slot among normal, poison, electrical and fire; the first on a tie; none when nothing is positive.
        Assert.AreEqual(1, HitGoreRules.AttackEndKind(false, new(5, 0, 0, 0, 0)));
        Assert.AreEqual(2, HitGoreRules.AttackEndKind(false, new(5, 6, 0, 0, 0)));
        Assert.AreEqual(3, HitGoreRules.AttackEndKind(false, new(5, 6, 9, 0, 0)));
        Assert.AreEqual(4, HitGoreRules.AttackEndKind(false, new(5, 6, 9, 10, 0)));
        Assert.AreEqual(1, HitGoreRules.AttackEndKind(false, new(5, 5, 5, 5, 0)), "the first on a tie");
        Assert.AreEqual(3, HitGoreRules.AttackEndKind(false, new(0, 0, 2, 2, 7)), "fatigue is not a slot of the loop");
        Assert.IsNull(HitGoreRules.AttackEndKind(false, DamageByType.Zero));
        Assert.IsNull(HitGoreRules.AttackEndKind(false, new(0, 0, 0, 0, 12)));
        // The packet's critical flag skips the loop: the damage effect (kind 6, index 5) is never run for a critical hit.
        Assert.IsNull(HitGoreRules.AttackEndKind(true, new(30, 0, 0, 0, 0)));
    }

    [TestMethod]
    public void TheDyingAnimationIsTheFallDownWithTheFilterOnAndTheKillsOwnOtherwise()
    {
        foreach (var stored in new[] { 7, 17, 18, 19 })
        {
            Assert.AreEqual(stored, DyingAnimationRules.Chosen(stored, false));
            Assert.AreEqual(DyingAnimationRules.FallDown, DyingAnimationRules.Chosen(stored, true));
        }
    }
}
