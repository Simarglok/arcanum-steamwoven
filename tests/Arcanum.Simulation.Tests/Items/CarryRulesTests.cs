using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Items;

/// <summary>ARC-WORLD-085: the original's item weight (0x461370), total weight (0x461410), insert weight test (0x466510), encumbrance
/// level (0x45F790) and its consequences' numbers (0x45F820, 0x45E610), ledger WE1–WE9.</summary>
[TestClass]
public sealed class CarryRulesTests
{
    private const int Weapon = 5, Ammo = CarryRules.AmmoType, Gold = CarryRules.GoldType;

    [TestMethod]
    public void GoldIsWeightlessAndAmmunitionWeighsItsUnitsOverFour()
    {
        Assert.AreEqual(0, CarryRules.ItemWeight(new(Gold, 500, 40, 0, 9_000)), "gold weighs nothing, whatever its fields");
        Assert.AreEqual(120, CarryRules.ItemWeight(new(Weapon, 120)));
        Assert.AreEqual(7, CarryRules.ItemWeight(new(Ammo, 3, 0, 0, 10)), "3 × 10 / 4 truncated");
        Assert.AreEqual(-7, CarryRules.ItemWeight(new(Ammo, -3, 0, 0, 10)), "toward zero");
        Assert.AreEqual(0, CarryRules.ItemWeight(new(Ammo, 3, 0, 0, 0)), "an empty stack");
        Assert.AreEqual(unchecked(1_000_000 * 5_000) / 4, CarryRules.ItemWeight(new(Ammo, 1_000_000, 0, 0, 5_000)), "a 32-bit product");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CarryRules.ItemWeight(new(Weapon, 1, 0, 0, 2)), "only stacks carry units");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CarryRules.ItemWeight(new(4, 1)), "not an item type");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CarryRules.ItemWeight(new(Weapon, CarryLimits.MaxWeight + 1)));
    }

    [TestMethod]
    public void TheMagicAdjustmentNeedsAHolderAndFollowsItsPower()
    {
        // Without a holder the adjustment is left out.
        Assert.AreEqual(100, CarryRules.ItemWeight(new(Weapon, 100, -40, 50)));
        // A neutral item keeps the whole adjustment with any holder.
        Assert.AreEqual(60, CarryRules.ItemWeight(new(Weapon, 100, -40, 0), CarryHolder.Critter, -100));
        Assert.AreEqual(60, CarryRules.ItemWeight(new(Weapon, 100, -40, 0), CarryHolder.Container));
        // A magickal item (complexity 50): power (50 + 30) / 2 = 40, so −40 × 40 / 50 = −32.
        Assert.AreEqual(68, CarryRules.ItemWeight(new(Weapon, 100, -40, 50), CarryHolder.Critter, 30));
        // A technologist gets nothing from it (power clamped to 0).
        Assert.AreEqual(100, CarryRules.ItemWeight(new(Weapon, 100, -40, 50), CarryHolder.Critter, -60));
        // A technological item (complexity −50): a mage's power is 0, so the whole adjustment; a strong technologist's −50 leaves none.
        Assert.AreEqual(60, CarryRules.ItemWeight(new(Weapon, 100, -40, -50), CarryHolder.Critter, 60));
        Assert.AreEqual(100, CarryRules.ItemWeight(new(Weapon, 100, -40, -50), CarryHolder.Critter, -50));
        // A container has the item's full power: a magickal item keeps the adjustment, a technological one loses it.
        Assert.AreEqual(60, CarryRules.ItemWeight(new(Weapon, 100, -40, 50), CarryHolder.Container));
        Assert.AreEqual(100, CarryRules.ItemWeight(new(Weapon, 100, -40, -50), CarryHolder.Container));
        // The adjusted weight of ammunition is multiplied by its units.
        Assert.AreEqual((10 - 2) * 9 / 4, CarryRules.ItemWeight(new(Ammo, 10, -2, 0, 9), CarryHolder.Critter));
        // The power truncates toward zero: (7 + 0) / 2 = 3, then 10 × 3 / 7 = 4.
        Assert.AreEqual(4, CarryRules.HolderMagicWeight(10, 7, CarryHolder.Critter, 0));
        Assert.AreEqual(-4, CarryRules.HolderMagicWeight(-10, 7, CarryHolder.Critter, 0));
        Assert.AreEqual(0, CarryRules.HolderMagicWeight(10, 7, CarryHolder.None, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CarryRules.HolderMagicWeight(10, 7, CarryHolder.Critter, 101));
    }

    [TestMethod]
    public void TheTotalSumsEveryItemWithTheHolderAndWraps()
    {
        CarriedItem[] items = [new(Weapon, 100, -40, 50), new(Gold, 9), new(Ammo, 4, 0, 0, 10)];
        Assert.AreEqual(68 + 0 + 10, CarryRules.TotalWeight(items, CarryHolder.Critter, 30));
        Assert.AreEqual(0, CarryRules.TotalWeight([], CarryHolder.Critter));
        // Four stacks of 536 750 000 and one item of 1 000 000 pass 2^31 and wrap.
        Assert.AreEqual(unchecked(4 * 536_750_000 + 1_000_000),
            CarryRules.TotalWeight([.. Enumerable.Repeat(new CarriedItem(Ammo, 1_000_000, 0, 0, 2_147), 4), new(Weapon, 1_000_000)], CarryHolder.Critter));
    }

    [TestMethod]
    public void AnItemFitsUpToTheCarryWeightInclusive()
    {
        Assert.IsTrue(CarryRules.Fits(4_900, 100, 5_000), "exactly the carry weight");
        Assert.IsFalse(CarryRules.Fits(4_900, 101, 5_000), "one above");
        Assert.IsFalse(CarryRules.Fits(5_001, 0, 5_000), "a weightless item still needs the load within the carry weight");
        Assert.IsTrue(CarryRules.Fits(int.MaxValue, 1, -1), "the sum wraps at 32 bits as the original's does");
    }

    [TestMethod]
    public void TheLevelIsTheFirstThresholdAboveTheLoad()
    {
        // Carry weight 5 000: thresholds 750, 1 500, 2 250, 3 000, 3 750, 4 500 (and 5 000).
        int[] expected = [0, 0, 1, 1, 2, 3, 4, 5, 5, 6, 6, 6];
        int[] loads = [0, 749, 750, 1_499, 1_500, 2_250, 3_000, 3_750, 4_499, 4_500, 5_000, 99_999];
        for (var i = 0; i < loads.Length; i++) Assert.AreEqual(expected[i], CarryRules.Level(loads[i], 5_000), $"load {loads[i]}");
        Assert.AreEqual(3, CarryRules.Level(299, 500), "60 % of 500 is 300");
        Assert.AreEqual(6, CarryRules.Level(0, 1), "every threshold below 100 % of 1 truncates to 0, which no load is below");
        Assert.AreEqual(1, CarryRules.Level(0, 4), "15 % of 4 is 0, 30 % is 1");
        Assert.AreEqual(6, CarryRules.Level(0, 0));
        Assert.AreEqual(2_250, CarryRules.Threshold(2, 5_000));
        Assert.AreEqual(unchecked(15 * int.MaxValue) / 100, CarryRules.Threshold(0, int.MaxValue), "the product wraps");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CarryRules.Threshold(7, 5_000));
    }

    [TestMethod]
    public void TheConsequencesNumbersByLevel()
    {
        int[] entries = [0, 1, 2, 3, 4, 5, 8];
        int?[] intervals = [null, null, null, null, 600_000, 60_000, 5_000];
        for (var level = 0; level < CarryRules.Levels; level++)
        {
            Assert.AreEqual(entries[level], CarryRules.EffectEntries(level), $"entries at {level}");
            Assert.AreEqual(intervals[level], CarryRules.DrainIntervalMs(level), $"interval at {level}");
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CarryRules.EffectEntries(-1));
        Assert.AreEqual(0, CarryRules.Drain(4, 1), "below 5 nothing drains");
        Assert.AreEqual(7, CarryRules.Drain(30, 7));
        Assert.AreEqual(1, CarryRules.Drain(11, 7), "it would leave less than 5: one point");
        Assert.AreEqual(1, CarryRules.Drain(5, 1), "5 − 1 is 4, so one point all the same");
        Assert.AreEqual(2, CarryRules.Drain(7, 2), "exactly 5 left");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CarryRules.Drain(30, 0));
    }
}
