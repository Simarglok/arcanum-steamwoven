using Arcanum.Simulation.Hotkeys;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Hotkeys;

// ARC-UI-022: the hotkey bar's keys, the activation's plans and the recent actions (ledger HK rows, IP7–IP9).
[TestClass]
public sealed class HotkeyRulesTests
{
    private const uint CanUse = HotkeyRules.CanUseFlag, Thrown = HotkeyRules.UseIsThrowFlag;

    private static HotkeyItemFacts Item(string entity, int type, uint flags = 0, int wear = -1) => new(entity, type, flags, wear);

    [TestMethod]
    public void The_keys_1_to_0_are_the_entries_0_to_9()
    {
        Assert.AreEqual(0, HotkeyRules.EntryOfScancode(2), "the key 1");
        Assert.AreEqual(9, HotkeyRules.EntryOfScancode(11), "the key 0");
        Assert.IsNull(HotkeyRules.EntryOfScancode(1), "Escape");
        Assert.IsNull(HotkeyRules.EntryOfScancode(12), "the minus key");
        var spell = HotkeyEntry.ForSpell(62);
        Assert.IsTrue(HotkeyRules.KeyActivates(true, false, false, spell, false));
        Assert.IsFalse(HotkeyRules.KeyActivates(false, false, false, spell, false), "a release");
        Assert.IsFalse(HotkeyRules.KeyActivates(true, true, false, spell, false), "another critter's turn");
        Assert.IsFalse(HotkeyRules.KeyActivates(true, false, true, spell, false), "0x566EF0");
        Assert.IsFalse(HotkeyRules.KeyActivates(true, false, false, HotkeyEntry.Empty, false), "an empty entry");
        Assert.IsFalse(HotkeyRules.KeyActivates(true, false, false, spell, true), "an entry being dragged");
    }

    [TestMethod]
    public void A_pc_that_cannot_act_activates_nothing()
    {
        Assert.IsTrue(HotkeyRules.PcCanActivate(false, true, false, false, false));
        Assert.IsFalse(HotkeyRules.PcCanActivate(true, true, false, false, false), "dragging");
        Assert.IsFalse(HotkeyRules.PcCanActivate(false, false, false, false, false), "no PC");
        Assert.IsFalse(HotkeyRules.PcCanActivate(false, true, true, false, false), "dead");
        Assert.IsFalse(HotkeyRules.PcCanActivate(false, true, false, true, false), "unconscious");
        Assert.IsFalse(HotkeyRules.PcCanActivate(false, true, false, false, true), "critter flags 0x20 or 0x40");
    }

    [TestMethod]
    public void An_item_that_can_be_used_is_used_and_another_of_its_prototype_goes_first()
    {
        var potion = Item("potion", 9, CanUse);
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Nothing), HotkeyRules.PlanItem(potion, null, false, false, null), "not the PC's");
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Use, "potion"), HotkeyRules.PlanItem(potion, null, true, false, null));
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Use, "potion2", TargetSelf: true),
            HotkeyRules.PlanItem(potion, Item("potion2", 9, CanUse), true, true, null), "the other one first; Shift: on the PC");
        // A written item that can be used keeps its own text; one that cannot is swapped like any item.
        var book = Item("book", 13, CanUse);
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Use, "book"), HotkeyRules.PlanItem(book, Item("book2", 13, CanUse), true, false, null));
        var note = Item("note", 13);
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Hold, "note2"), HotkeyRules.PlanItem(note, Item("note2", 13), true, false, null));
    }

    [TestMethod]
    public void Weapons_and_armour_are_worn_ammunition_and_gold_do_nothing_and_the_rest_is_held()
    {
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Wear, "sword", WearSlot: 1004),
            HotkeyRules.PlanItem(Item("sword", 5), Item("sword2", 5), true, false, null), "the entry's own weapon");
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Wear, "dagger", WearSlot: 1004),
            HotkeyRules.PlanItem(Item("dagger", 5, CanUse | Thrown), null, true, false, null), "a thrown item that can be used is worn, not used");
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Wear, "helm", WearSlot: 1000),
            HotkeyRules.PlanItem(Item("helm", 7, wear: 1000), Item("helm2", 7, wear: 1000), true, false, null));
        Assert.AreEqual(HotkeyItemAction.Nothing, HotkeyRules.PlanItem(Item("helm", 7), null, true, false, null).Action, "no wear location");
        Assert.AreEqual(HotkeyItemAction.Nothing, HotkeyRules.PlanItem(Item("arrows", 6), null, true, false, null).Action);
        Assert.AreEqual(HotkeyItemAction.Nothing, HotkeyRules.PlanItem(Item("coins", 8), null, true, false, null).Action);
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Hold, "key"), HotkeyRules.PlanItem(Item("key", 11), null, true, false, null));
        Assert.AreEqual(HotkeyItemAction.Nothing, HotkeyRules.PlanItem(Item("key", 11), null, true, false, "key").Action, "held already");
        Assert.AreEqual(new HotkeyItemPlan(HotkeyItemAction.Hold, "key2"), HotkeyRules.PlanItem(Item("key", 11), Item("key2", 11), true, false, "key2"),
            "the held-item test reads the entry's own item");
    }

    [TestMethod]
    public void An_item_and_spell_entry_wields_its_item_first_and_tests_the_spell()
    {
        Assert.AreEqual(HotkeyItemSpellAction.Nothing, HotkeyRules.PlanItemSpell(false, false));
        Assert.AreEqual(HotkeyItemSpellAction.Activate, HotkeyRules.PlanItemSpell(true, true));
        Assert.AreEqual(HotkeyItemSpellAction.WieldThenActivate, HotkeyRules.PlanItemSpell(true, false));
        static bool? Triggers(int spell) => spell is >= 0 and < 223 ? spell == 87 : null;
        Assert.IsTrue(HotkeyRules.SpellActivates(62, Triggers));
        Assert.IsFalse(HotkeyRules.SpellActivates(87, Triggers), "item triggers");
        Assert.IsFalse(HotkeyRules.SpellActivates(-1, Triggers));
        Assert.IsFalse(HotkeyRules.SpellActivates(300, Triggers), "outside the table");
    }

    [TestMethod]
    public void A_new_action_becomes_the_latest_and_the_same_one_changes_nothing()
    {
        IReadOnlyList<HotkeyEntry> none = [HotkeyEntry.Empty, HotkeyEntry.Empty];
        var flame = HotkeyEntry.ForSpell(62);
        var prowl = HotkeyEntry.ForSkill(SkillBarButton.Prowling);
        var once = HotkeyRules.RecordAction(none, flame);
        CollectionAssert.AreEqual(new[] { flame, HotkeyEntry.Empty }, once.ToArray());
        Assert.AreSame(once, HotkeyRules.RecordAction(once, HotkeyEntry.ForSpell(62)), "equal by type and index");
        var twice = HotkeyRules.RecordAction(once, prowl);
        CollectionAssert.AreEqual(new[] { prowl, flame }, twice.ToArray());
        CollectionAssert.AreEqual(new[] { flame, prowl }, HotkeyRules.RecordAction(twice, flame).ToArray());
        Assert.AreSame(twice, HotkeyRules.RecordAction(twice, HotkeyEntry.Empty));
        Assert.IsFalse(HotkeyRules.SameAction(HotkeyEntry.ForSkill(SkillBarButton.Repair), HotkeyEntry.ForSpell(3)), "another type");
        Assert.IsTrue(HotkeyRules.SameAction(HotkeyEntry.ForItem("rock"), HotkeyEntry.ForItem("rock")));
        Assert.IsFalse(HotkeyRules.SameAction(HotkeyEntry.ForItemSpell("rock", 1), HotkeyEntry.ForItemSpell("rock", 2)));
    }

    [TestMethod]
    public void Entries_fit_their_kind()
    {
        Assert.IsTrue(HotkeyEntry.Empty.IsWellFormed);
        Assert.IsFalse(new HotkeyEntry(HotkeyKind.None, null, 0).IsWellFormed);
        Assert.IsFalse(new HotkeyEntry(HotkeyKind.Item, "").IsWellFormed);
        Assert.IsFalse(new HotkeyEntry(HotkeyKind.Item, "rock", 3).IsWellFormed);
        Assert.IsFalse(new HotkeyEntry(HotkeyKind.Skill, null, 4).IsWellFormed);
        Assert.IsFalse(new HotkeyEntry(HotkeyKind.Spell, null, -1).IsWellFormed);
        Assert.IsFalse(new HotkeyEntry(HotkeyKind.ItemSpell, null, 3).IsWellFormed);
        Assert.IsFalse(new HotkeyEntry((HotkeyKind)5, null, -1).IsWellFormed);
    }
}
