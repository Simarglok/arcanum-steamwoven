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
    public void TheDyingAnimationIsTheFallDownWithTheFilterOnAndTheKillsOwnOtherwise()
    {
        foreach (var stored in new[] { 7, 17, 18, 19 })
        {
            Assert.AreEqual(stored, DyingAnimationRules.Chosen(stored, false));
            Assert.AreEqual(DyingAnimationRules.FallDown, DyingAnimationRules.Chosen(stored, true));
        }
        Assert.IsTrue(DyingAnimationRules.FilterRefusesMissingArt(true, false), "no repair to 18 with the filter on");
        Assert.IsFalse(DyingAnimationRules.FilterRefusesMissingArt(true, true));
        Assert.IsFalse(DyingAnimationRules.FilterRefusesMissingArt(false, false), "filter off: the original repairs to 18, the caller's step");
    }
}
