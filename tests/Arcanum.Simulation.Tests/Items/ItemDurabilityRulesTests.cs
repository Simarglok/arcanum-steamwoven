using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Items.ItemDurabilityRules;

namespace Arcanum.Simulation.Tests.Items;

// ARC-WORLD-053: the item durability rules of ledger DU1–DU8. Values are invented.
[TestClass]
public sealed class ItemDurabilityRulesTests
{
    private static readonly ItemCondition Sword = new(40, 10, 0, false);

    [TestMethod]
    public void Maximum_is_points_plus_adjustment_and_current_is_maximum_less_damage_without_a_clamp()
    {
        Assert.AreEqual((50, 50), (Sword.MaximumHitPoints, Sword.HitPoints));
        var worn = Sword with { Damage = 20 };
        Assert.AreEqual((50, 30), (MaximumHitPoints(worn), HitPoints(worn)));
        Assert.AreEqual(-10, HitPoints(Sword with { Damage = 60 }), "no clamp");
        Assert.AreEqual(30, MaximumHitPoints(Sword with { Adjustment = -10 }), "a negative adjustment lowers the maximum");
    }

    [TestMethod]
    public void The_setter_stores_a_negative_damage_as_zero_and_never_breaks()
    {
        Assert.AreEqual(0, SetDamage(Sword, -5).Damage);
        Assert.AreEqual(70, SetDamage(Sword, 70).Damage);
        Assert.IsFalse(SetDamage(Sword, 70).Broken, "the setter does not break; the callers do");
    }

    [TestMethod]
    public void Damage_breaks_at_zero_hit_points_and_resets_the_damage_then_a_second_break_deletes()
    {
        var hit = AddDamage(Sword, 30);
        Assert.AreEqual((ItemBreakStep.None, 30, 20), (hit.Break, hit.Condition.Damage, hit.Condition.HitPoints));
        Assert.IsFalse(hit.TakeOffAndRunBust);
        Assert.AreEqual(ItemBreakStep.None, AddDamage(hit.Condition, 19).Break, "one hit point left");

        var broken = AddDamage(hit.Condition, 20);
        Assert.AreEqual(ItemBreakStep.Broken, broken.Break);
        Assert.IsTrue(broken.TakeOffAndRunBust);
        Assert.AreEqual((0, true, true, 50), (broken.Condition.Damage, broken.Condition.Broken, broken.Condition.InventoryArtBroken, broken.Condition.HitPoints),
            "a broken item reads as healthy");

        var again = AddDamage(broken.Condition, 50);
        Assert.AreEqual(ItemBreakStep.Deleted, again.Break);
        Assert.AreEqual(ItemBreakStep.None, AddDamage(broken.Condition, 49).Break, "a broken item with hit points left stays");
        Assert.AreEqual(ItemBreakStep.Broken, AddDamage(new(0, 0, 0, false), 0).Break, "an item with no hit points breaks on any step");

        Assert.AreEqual(ItemBreakStep.Broken, Break(Sword).Break);
        Assert.AreEqual(ItemBreakStep.Deleted, Break(Sword with { Broken = true }).Break);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AddDamage(Sword, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AddDamage(Sword, ItemDurabilityLimits.MaxDamageStep + 1));
    }

    [TestMethod]
    public void Broken_blocks_wearing_selling_and_schematic_use_but_not_use_or_item_spells()
    {
        var broken = Sword with { Broken = true };
        Assert.IsTrue(BlocksWear(broken) && BlocksSchematicUse(broken) && SaleRefused(broken, 100));
        Assert.IsFalse(BlocksUse(broken), "TC9: the original's behaviour in both modes");
        Assert.IsFalse(BlocksWear(Sword) || BlocksSchematicUse(Sword) || SaleRefused(Sword, 100));
        Assert.IsTrue(SaleRefused(Sword, 0), "a worth of 0 is refused");
    }

    [TestMethod]
    public void Only_the_pcs_sale_price_scales_with_damage_truncated_then_bounded()
    {
        var damaged = Sword with { Damage = 20 };
        Assert.AreEqual(60, ScalePrice(100, damaged, pcSells: true), "30 / 50 of 100");
        Assert.AreEqual(100, ScalePrice(100, damaged, pcSells: false), "buying from an NPC ignores damage");
        Assert.AreEqual(100, ScalePrice(100, Sword, pcSells: true));
        Assert.AreEqual(33, ScalePrice(100, new(3, 0, 2, false), pcSells: true), "1 × 100 / 3, truncated");
        Assert.AreEqual(2, ScalePrice(1, Sword, pcSells: true), "a PC sale price of exactly 1 is 2");
        Assert.AreEqual(1, ScalePrice(1, Sword, pcSells: false), "the buy branch's bounds are the trade owner's");
        Assert.AreEqual(0, ScalePrice(0, Sword, pcSells: true));
        Assert.AreEqual(0, ScalePrice(100, Sword with { Damage = 60 }, pcSells: true), "a negative product is 0");
    }

    [TestMethod]
    public void The_quote_counts_damage_twice_in_the_original_and_once_in_steamwoven_TC7()
    {
        var damaged = Sword with { Damage = 20 };
        // Original: P = 100 × 30 / 50 = 60; 30 × 20 / 50 = 12. Steamwoven: 50 × 20 / 50 = 20.
        Assert.AreEqual(new ItemRepairQuote(ItemQuoteStatus.Quoted, 12), RepairQuote(damaged, 100, RepairTraining.None, ItemRuleProfile.Original));
        Assert.AreEqual(new ItemRepairQuote(ItemQuoteStatus.Quoted, 20), RepairQuote(damaged, 100, RepairTraining.None, ItemRuleProfile.Steamwoven));
        // Original: the dearest repair is half damaged; Steamwoven grows with the damage.
        int Quote(int damage, ItemRuleProfile p) => RepairQuote(Sword with { Damage = damage }, 1000, RepairTraining.None, p).Cost;
        Assert.IsTrue(Quote(25, ItemRuleProfile.Original) > Quote(45, ItemRuleProfile.Original));
        Assert.IsTrue(Quote(45, ItemRuleProfile.Steamwoven) > Quote(25, ItemRuleProfile.Steamwoven));
        Assert.AreEqual(new ItemRepairQuote(ItemQuoteStatus.Quoted, 2), RepairQuote(Sword with { Damage = 1 }, 100, RepairTraining.None, ItemRuleProfile.Steamwoven), "1 at least 2");
        Assert.AreEqual((9, 125, 490), (Quote(49, ItemRuleProfile.Original), Quote(25, ItemRuleProfile.Original), Quote(49, ItemRuleProfile.Steamwoven)), "nearly destroyed: almost free in the original");

        foreach (var profile in ItemRuleProfile.All)
        {
            Assert.AreEqual(new ItemRepairQuote(ItemQuoteStatus.NotNeeded, 0), RepairQuote(Sword, 100, RepairTraining.Master, profile));
            var broken = Sword with { Broken = true };
            Assert.AreEqual(new ItemRepairQuote(ItemQuoteStatus.BrokenNeedsMaster, 0), RepairQuote(broken, 100, RepairTraining.Expert, profile));
            Assert.AreEqual(new ItemRepairQuote(ItemQuoteStatus.Quoted, 50), RepairQuote(broken, 100, RepairTraining.Master, profile),
                "a broken item: damage taken as the maximum, P / 2");
        }
        Assert.IsTrue(ItemRuleProfile.Original.RepairQuoteCountsDamageTwice);
        Assert.IsFalse(ItemRuleProfile.Steamwoven.RepairQuoteCountsDamageTwice);
        Assert.AreSame(ItemRuleProfile.Original, ItemRuleProfile.FromName("original"));
        Assert.IsNull(ItemRuleProfile.FromName("Original"));
    }

    [TestMethod]
    public void A_repair_lowers_damage_and_the_adjustment_and_a_master_clears_the_current_art_only()
    {
        var damaged = Sword with { Damage = 30 };
        Assert.AreEqual(25, RepairAmount(damaged, 50, fullRepair: false), "maximum × effectiveness / 100");
        Assert.AreEqual(50, RepairAmount(damaged, 50, fullRepair: true));
        Assert.AreEqual(1, RepairAmount(damaged, 0, fullRepair: false), "at least 1");
        Assert.AreEqual((10, 5, 1, 0, 5), (RepairAdjustmentPercent(RepairTraining.None, false), RepairAdjustmentPercent(RepairTraining.Apprentice, false),
            RepairAdjustmentPercent(RepairTraining.Expert, false), RepairAdjustmentPercent(RepairTraining.Master, false),
            RepairAdjustmentPercent(RepairTraining.Master, true)));
        // ARC-SKILLS-007 (ledger RP2): the 5 % of a broken item is a master's; anyone else keeps the training table.
        Assert.AreEqual((10, 1), (RepairAdjustmentPercent(RepairTraining.None, true), RepairAdjustmentPercent(RepairTraining.Expert, true)));
        // The skill's own run is not forced: effectiveness 0 restores nothing, anything above restores at least 1.
        Assert.AreEqual((25, 50, 0, 1), (SkillRepairAmount(damaged, 50, false), SkillRepairAmount(damaged, 0, true), SkillRepairAmount(damaged, 0, false),
            SkillRepairAmount(damaged, 1, false)));

        var repaired = Repair(damaged, 25, 10, RepairTraining.None);
        Assert.AreEqual(ItemRepairStatus.Repaired, repaired.Status);
        // Damage 30 − 25 = 5; hp_adj 10 − 50 × 10 / 100 = 5 (the maximum before the repair).
        Assert.AreEqual((5, 5, 45), (repaired.Condition.Damage, repaired.Condition.Adjustment, repaired.Condition.MaximumHitPoints));
        Assert.AreEqual(0, Repair(damaged, 100, 0, RepairTraining.Master).Condition.Damage, "not below 0");
        Assert.AreEqual(ItemRepairStatus.NothingToRepair, Repair(Sword, 10, 0, RepairTraining.Master).Status);

        var broken = Break(Sword).Condition;
        Assert.AreEqual(ItemRepairStatus.NothingToRepair, Repair(broken, 10, 5, RepairTraining.Expert).Status, "no damage, not a master");
        // A broken item that took damage again: any repairer lowers the damage; only a master clears the bit.
        var hurt = broken with { Damage = 20 };
        var expert = Repair(hurt, 10, 1, RepairTraining.Expert);
        Assert.AreEqual((ItemRepairStatus.Repaired, 10, 10, true), (expert.Status, expert.Condition.Damage, expert.Condition.Adjustment, expert.Condition.Broken),
            "an expert's 1 % of 50 truncates to 0");
        Assert.IsFalse(Repair(hurt, 10, 5, RepairTraining.Master).Condition.Broken);
        var mended = Repair(broken, 10, 5, RepairTraining.Master);
        Assert.AreEqual(ItemRepairStatus.Repaired, mended.Status);
        Assert.AreEqual((false, true, 0, 8), (mended.Condition.Broken, mended.Condition.InventoryArtBroken, mended.Condition.Damage, mended.Condition.Adjustment),
            "the current art's bit only; hp_adj 10 − 50 × 5 / 100");
        var nothing = Repair(damaged, 0, 10, RepairTraining.None);
        Assert.AreEqual((ItemRepairStatus.Repaired, 30, 5), (nothing.Status, nothing.Condition.Damage, nothing.Condition.Adjustment), "no hit points, the share still taken");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Repair(damaged, -1, 0, RepairTraining.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Repair(damaged, 1, 101, RepairTraining.None));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Repair(damaged, 1, 0, (RepairTraining)9));
    }

    [TestMethod]
    public void Values_outside_the_bounds_are_not_valid()
    {
        Assert.IsTrue(IsValid(Sword));
        Assert.IsFalse(IsValid(Sword with { Damage = -1 }));
        Assert.IsFalse(IsValid(Sword with { Points = ItemDurabilityLimits.MaxMagnitude + 1 }));
        Assert.IsFalse(IsValid(Sword with { Adjustment = -ItemDurabilityLimits.MaxMagnitude - 1 }));
        Assert.IsFalse(IsValid(Sword with { Damage = ItemDurabilityLimits.MaxDamage + 1 }));
    }
}
