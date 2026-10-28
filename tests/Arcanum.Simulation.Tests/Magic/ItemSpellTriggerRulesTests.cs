using Arcanum.Simulation.Magic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Simulation.Magic.ItemSpellTriggerRules;

namespace Arcanum.Simulation.Tests.Magic;

// ARC-MAGIC-020: the item trigger executors (0x4CB830, 0x4CBFF0; ledger IT1–IT6). Spell numbers and masks are invented.
[TestClass]
public sealed class ItemSpellTriggerRulesTests
{
    private sealed class Rolls(params int[] values) : ISpellRollSource
    {
        private int _next;
        public List<(SpellRollPurpose, int, int)> Asked { get; } = [];

        public int Roll(SpellRollPurpose purpose, int min, int max)
        {
            Asked.Add((purpose, min, max));
            return values[_next++];
        }
    }

    private static Func<int, uint> Masks(params (int Value, uint Mask)[] masks) => value => masks.FirstOrDefault(m => m.Value == value).Mask;

    [TestMethod]
    public void The_walk_ends_at_the_first_empty_slot_and_skips_records_whose_mask_misses_the_event()
    {
        var masks = Masks((90, Wear), (91, Wear | Unwear), (92, Pickup));
        Assert.AreEqual(0, Fire([NoSpell, 90, 91, NoSpell, NoSpell], masks, Wear, new Rolls()).Count, "an empty slot 0 fires nothing");
        CollectionAssert.AreEqual(new[] { new ItemSpellFiring(0, 90, ItemSpellAim.Parent), new ItemSpellFiring(2, 91, ItemSpellAim.Parent) },
            Fire([90, 92, 91, NoSpell, 90], masks, Wear, new Rolls()).ToArray(), "slot 4 lies past the empty slot 3");
        Assert.AreEqual(0, Fire([93, NoSpell, NoSpell, NoSpell, NoSpell], masks, Wear, new Rolls()).Count, "a record without triggers never fires");
    }

    [TestMethod]
    public void The_mask_is_read_for_the_whole_value_and_the_run_takes_its_low_byte()
    {
        var firing = Fire([256 + 90, NoSpell, NoSpell, NoSpell, NoSpell], Masks((256 + 90, Wear)), Wear, new Rolls()).Single();
        Assert.AreEqual(90, firing.Spell);
        Assert.AreEqual(0, Fire([256 + 90, NoSpell, NoSpell, NoSpell, NoSpell], Masks((90, Wear)), Wear, new Rolls()).Count);
    }

    [TestMethod]
    public void A_chance_flag_rolls_d100_and_fires_at_or_below_the_first_flag_in_the_original_order()
    {
        Assert.AreEqual((10, 20, 40, 66, (int?)null), (Chance(Rare), Chance(Uncommon), Chance(Common), Chance(Frequent), Chance(Wear)));
        Assert.AreEqual(10, Chance(Rare | Frequent), "Rare is tested first");
        Assert.AreEqual(20, Chance(Uncommon | Common | Frequent));
        var rolls = new Rolls(66, 67);
        var masks = Masks((90, Wear | Frequent));
        Assert.AreEqual(1, Fire([90, NoSpell, NoSpell, NoSpell, NoSpell], masks, Wear, rolls).Count, "66 fires");
        Assert.AreEqual(0, Fire([90, NoSpell, NoSpell, NoSpell, NoSpell], masks, Wear, rolls).Count, "67 does not");
        CollectionAssert.AreEqual(new[] { (SpellRollPurpose.ItemTriggerChance, 1, 100), (SpellRollPurpose.ItemTriggerChance, 1, 100) }, rolls.Asked);
        Assert.AreEqual(1, Withdraw([90, NoSpell, NoSpell, NoSpell, NoSpell], Masks((90, Unwear | Rare)), Unwear).Count,
            "the withdrawal has no chance roll");
    }

    [TestMethod]
    public void The_aim_follows_the_record_mask()
    {
        Assert.AreEqual(ItemSpellAim.Parent, Aim(Wear | Unwear));
        Assert.AreEqual(ItemSpellAim.Other, Aim(0x3000), "Item_Used holds the attacker bit");
        Assert.AreEqual(ItemSpellAim.Other, Aim(ParentHit | Other | WeaponMelee));
        Assert.AreEqual(ItemSpellAim.OtherWeapon, Aim(OtherWeapon | OtherArmor));
        Assert.AreEqual(ItemSpellAim.OtherArmor, Aim(OtherArmor));
        Assert.AreEqual(ItemSpellAim.None, Aim(ParentHit | WeaponMelee), "the melee bit alone sets no target");
        Assert.AreEqual(ItemSpellAim.Location, Aim(ParentAttacksLocation));
        Assert.AreEqual(ItemSpellAim.Other, Aim(ParentAttacksLocation | Other), "an aim bit wins over the location");
    }

    [TestMethod]
    public void A_withdrawal_casts_again_at_the_parent_with_the_casting_goal()
    {
        var masks = Masks((90, Wear | Unwear | Rare), (91, Drop | Other));
        CollectionAssert.AreEqual(new[] { new ItemSpellFiring(0, 90, ItemSpellAim.Parent, true) },
            Withdraw([90, 91, NoSpell, NoSpell, NoSpell], masks, Unwear).ToArray());
        CollectionAssert.AreEqual(new[] { new ItemSpellFiring(1, 91, ItemSpellAim.Parent, true) },
            Withdraw([90, 91, NoSpell, NoSpell, NoSpell], masks, Drop).ToArray(), "the aim bits are not read");
        Assert.AreEqual(0, Withdraw([NoSpell, 91], masks, Drop).Count);
    }
}
