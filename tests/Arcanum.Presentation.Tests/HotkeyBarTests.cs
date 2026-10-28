using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-027: the hotkey bar rules against the behaviour recorded in docs/research/ledger/spell-runs.md HK10–HK16 (the slots,
/// the icons, the counts, the right-button drag and the clicks); values from the ledger, expected results worked by hand.</summary>
[TestClass]
public sealed class HotkeyBarTests
{
    [TestMethod]
    public void The_slots_take_the_pointer_over_their_32_pixels_inside_the_band()
    {
        Assert.AreEqual(0, HotkeyBarRules.EntryAt(198, 440), "the first slot's top-left corner");
        Assert.AreEqual(0, HotkeyBarRules.EntryAt(229, 476), "its last column, the band's last row");
        Assert.IsNull(HotkeyBarRules.EntryAt(230, 460), "the gap between slots 0 and 1");
        Assert.AreEqual(1, HotkeyBarRules.EntryAt(237, 460));
        Assert.AreEqual(4, HotkeyBarRules.EntryAt(385, 460));
        Assert.IsNull(HotkeyBarRules.EntryAt(400, 460), "the middle gap between keys 5 and 6");
        Assert.AreEqual(5, HotkeyBarRules.EntryAt(418, 460));
        Assert.AreEqual(9, HotkeyBarRules.EntryAt(604, 460), "the key 0's slot");
        Assert.IsNull(HotkeyBarRules.EntryAt(605, 460), "past the last slot");
        Assert.IsNull(HotkeyBarRules.EntryAt(198, 439), "above the band");
        Assert.IsNull(HotkeyBarRules.EntryAt(198, 477), "below the band");
        Assert.IsNull(HotkeyBarRules.EntryAt(192, 460), "left of the band");
        Assert.AreEqual(10, HotkeyBarRules.SlotX.Count);
    }

    [TestMethod]
    public void Icons_come_from_the_spell_and_skill_tables()
    {
        Assert.AreEqual(12, HotkeyBarRules.SpellIcon(0), "conveyance 1");
        Assert.AreEqual(45, HotkeyBarRules.SpellIcon(5));
        Assert.AreEqual(127, HotkeyBarRules.SpellIcon(79));
        Assert.IsNull(HotkeyBarRules.SpellIcon(80), "outside the college spells a record flag decides (not read)");
        Assert.IsNull(HotkeyBarRules.SpellIcon(-1));
        Assert.AreEqual(279, HotkeyBarRules.SkillIcon(0), "prowling");
        Assert.AreEqual(280, HotkeyBarRules.SkillIcon(1), "pick pocket");
        Assert.AreEqual(282, HotkeyBarRules.SkillIcon(2), "disarm traps");
        Assert.AreEqual(278, HotkeyBarRules.SkillIcon(3), "repair");
        Assert.AreEqual(279, HotkeyBarRules.SkillIcon(7), "outside the table: prowling's");
        CollectionAssert.AllItemsAreUnique(HotkeyBarRules.ArtNumbers.ToArray());
        CollectionAssert.Contains(HotkeyBarRules.ArtNumbers.ToArray(), HotkeyBarRules.SlotArt);
    }

    [TestMethod]
    public void An_icon_is_centred_in_its_slot_or_clipped_to_it()
    {
        Assert.AreEqual(new HotkeyIconPlace(198, 4, 32, 32), HotkeyBarRules.IconPlace(0, 32, 32));
        Assert.AreEqual(new HotkeyIconPlace(198 + 7, 4 + 8, 17, 16), HotkeyBarRules.IconPlace(0, 17, 16), "(32 − 17) / 2 = 7, (32 − 16) / 2 = 8");
        Assert.AreEqual(new HotkeyIconPlace(573, 4, 32, 32), HotkeyBarRules.IconPlace(9, 64, 96), "a large art is clipped from its top-left");
        Assert.AreEqual(new HotkeyIconPlace(418 + 4, 4, 24, 32), HotkeyBarRules.IconPlace(5, 24, 40));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HotkeyBarRules.IconPlace(10, 1, 1));
    }

    [TestMethod]
    public void A_count_prints_in_0_to_9999_and_small_inventory_art_is_the_icon()
    {
        Assert.AreEqual("3", HotkeyBarRules.CountText(3));
        Assert.AreEqual("9999", HotkeyBarRules.CountText(9999));
        Assert.IsNull(HotkeyBarRules.CountText(10000));
        Assert.IsNull(HotkeyBarRules.CountText(-1));
        Assert.IsTrue(HotkeyBarRules.ItemShowsInventoryArt(32, 32));
        Assert.IsFalse(HotkeyBarRules.ItemShowsInventoryArt(33, 32));
        Assert.IsFalse(HotkeyBarRules.ItemShowsInventoryArt(32, 64));
    }

    // ARC-UI-032: the type icons of 0x554C20 (ledger HK17); art numbers are interface.mes numbers.
    private static int? Icon(int type, int complexity = 0, int subtype = 0, int coverage = 0, bool identified = false, bool hexed = false,
        int ammo = 0, int written = 0, int discipline = 0) =>
        HotkeyBarRules.ItemTypeIcon(new(type, complexity, identified, hexed, subtype, coverage, ammo, written, discipline));

    [TestMethod]
    public void A_weapon_or_armour_icon_comes_from_its_table_one_step_for_magick_two_for_technology()
    {
        Assert.AreEqual(391, Icon(5), "general melee");
        Assert.AreEqual(392, Icon(5, complexity: 40), "magick melee");
        Assert.AreEqual(393, Icon(5, complexity: -40), "tech melee");
        Assert.AreEqual(395, Icon(5, subtype: 8), "bows");
        Assert.AreEqual(397, Icon(5, -5, subtype: 8), "tech bows");
        Assert.AreEqual(394, Icon(5, -1, subtype: 6), "subtype 6 starts at magick melee: tech guns");
        Assert.AreEqual(398, Icon(5, subtype: 15), "subtype 15 reads past the weapon table into the ammunition one");
        Assert.AreEqual(402, Icon(7), "clothes by subtype with coverage 0");
        Assert.AreEqual(408, Icon(7, subtype: 5), "robes");
        Assert.AreEqual(406, Icon(7, 10, subtype: 2), "magick shield");
        Assert.AreEqual(411, Icon(7, coverage: 5), "a ring by coverage");
        Assert.AreEqual(416, Icon(7, -3, coverage: 6), "tech medallion");
        Assert.AreEqual(405, Icon(7, subtype: 5, coverage: 1), "a coverage wins over the subtype");
        Assert.AreEqual(428, Icon(7, coverage: 7), "coverage 7 reads past the coverage table into the written one");
    }

    [TestMethod]
    public void The_other_types_have_their_own_icons()
    {
        Assert.AreEqual(399, Icon(6, ammo: 1), "bullets");
        Assert.AreEqual(401, Icon(6, 10, ammo: 3), "ammunition takes no complexity step");
        Assert.AreEqual(417, Icon(8), "coins");
        Assert.AreEqual(418, Icon(9, 1), "a magickal food is a potion");
        Assert.AreEqual(423, Icon(9), "plain food");
        Assert.AreEqual(419, Icon(9, -1, discipline: 7), "therapeutics");
        Assert.AreEqual(420, Icon(9, -1, discipline: 0), "herbal");
        Assert.AreEqual(421, Icon(9, -1, discipline: 1), "chemical");
        Assert.AreEqual(423, Icon(9, -1, discipline: 3), "another technology's food");
        Assert.AreEqual(425, Icon(10), "scroll");
        Assert.AreEqual(426, Icon(11), "key");
        Assert.AreEqual(427, Icon(12), "key ring");
        Assert.AreEqual(428, Icon(13), "book");
        Assert.AreEqual(431, Icon(13, written: 2), "newspaper");
        Assert.AreEqual(430, Icon(13, written: 3), "telegram");
        Assert.AreEqual(462, Icon(13, written: 5), "schematic");
        Assert.AreEqual(432, Icon(14), "a plain generic item");
        Assert.AreEqual(433, Icon(14, 2), "magick generic");
        Assert.AreEqual(434, Icon(14, -2), "tech generic");
        Assert.AreEqual(3, Icon(3), "a type outside the items returns itself");
        Assert.IsNull(Icon(6, ammo: 40), "past the tables read");
        Assert.AreEqual(414, Icon(13, written: -1), "a negative subtype reads the table before (coverage 6)");
        Assert.IsNull(Icon(13, written: -36), "before the first table");
    }

    [TestMethod]
    public void An_identified_hexed_item_shows_a_hexed_icon()
    {
        Assert.AreEqual(392, Icon(5, 10, hexed: true), "not identified: the plain table");
        Assert.AreEqual(792, Icon(5, 10, subtype: 8, identified: true, hexed: true), "hexed bow");
        Assert.AreEqual(791, Icon(5, 10, subtype: 13, identified: true, hexed: true), "hexed melee");
        Assert.AreEqual(440, Icon(5, 10, subtype: 0, identified: true, hexed: true), "subtype 0 gets the plain hexed icon");
        Assert.AreEqual(797, Icon(7, 10, subtype: 5, identified: true, hexed: true), "hexed robes");
        Assert.AreEqual(794, Icon(7, 10, subtype: 1, identified: true, hexed: true), "hexed clothes");
        Assert.AreEqual(793, Icon(7, 10, subtype: 0, identified: true, hexed: true), "subtype 0 with coverage 0: the hexed shield");
        Assert.AreEqual(796, Icon(7, 10, coverage: 5, identified: true, hexed: true), "hexed ring");
        Assert.AreEqual(795, Icon(7, 10, coverage: 6, identified: true, hexed: true), "hexed medallion");
        Assert.AreEqual(793, Icon(7, 10, coverage: 2, identified: true, hexed: true), "another coverage: the hexed shield");
        Assert.AreEqual(440, Icon(14, 10, identified: true, hexed: true), "another type: the plain hexed icon");
        CollectionAssert.IsSubsetOf(new[] { 391, 393, 397, 401, 404, 410, 413, 416, 417, 434, 440, 462, 791, 797 }, HotkeyBarRules.TypeIconNumbers.ToArray());
        CollectionAssert.IsSubsetOf(HotkeyBarRules.TypeIconNumbers.ToArray(), HotkeyBarRules.ArtNumbers.ToArray());
        CollectionAssert.DoesNotContain(HotkeyBarRules.TypeIconNumbers.ToArray(), 0);
    }

    [TestMethod]
    public void A_right_press_starts_a_drag_only_from_something_while_nothing_is_dragged_or_held()
    {
        Assert.IsTrue(HotkeyBarRules.StartsDrag(false, false, HotkeyDragSourceKind.Entry, false));
        Assert.IsFalse(HotkeyBarRules.StartsDrag(false, false, HotkeyDragSourceKind.Entry, true), "an empty entry");
        Assert.IsTrue(HotkeyBarRules.StartsDrag(false, false, HotkeyDragSourceKind.Spell, true), "the pane's spell (no entry)");
        Assert.IsFalse(HotkeyBarRules.StartsDrag(true, false, HotkeyDragSourceKind.Spell, false), "a drag goes on");
        Assert.IsFalse(HotkeyBarRules.StartsDrag(false, true, HotkeyDragSourceKind.SkillButton, false), "an item is held");
        Assert.IsFalse(HotkeyBarRules.StartsDrag(false, false, HotkeyDragSourceKind.HeldItem, false), "a held item is dropped by a click, not dragged");
    }

    [TestMethod]
    public void A_release_moves_puts_clears_or_ends_the_drag()
    {
        Assert.AreEqual(new HotkeyDrop(HotkeyDropKind.Move, 2, 7), HotkeyBarRules.Drop(HotkeyDragSourceKind.Entry, 2, 7, false, false));
        Assert.AreEqual(new HotkeyDrop(HotkeyDropKind.SameEntry, 2, 2), HotkeyBarRules.Drop(HotkeyDragSourceKind.Entry, 2, 2, false, false));
        Assert.AreEqual(new HotkeyDrop(HotkeyDropKind.Clear, 2), HotkeyBarRules.Drop(HotkeyDragSourceKind.Entry, 2, null, false, false),
            "off the bar the dragged entry empties (HK15's fix)");
        Assert.AreEqual(new HotkeyDrop(HotkeyDropKind.Put, -1, 4), HotkeyBarRules.Drop(HotkeyDragSourceKind.Spell, -1, 4, false, false));
        Assert.AreEqual(new HotkeyDrop(HotkeyDropKind.Put, -1, 0), HotkeyBarRules.Drop(HotkeyDragSourceKind.ItemSpell, -1, 0, false, false));
        Assert.AreEqual(new HotkeyDrop(HotkeyDropKind.End), HotkeyBarRules.Drop(HotkeyDragSourceKind.SkillButton, -1, null, false, false));
        Assert.AreEqual(new HotkeyDrop(HotkeyDropKind.Continue), HotkeyBarRules.Drop(HotkeyDragSourceKind.Spell, -1, 4, true, false),
            "a left release goes on with a right drag");
        Assert.AreEqual(new HotkeyDrop(HotkeyDropKind.Put, -1, 4), HotkeyBarRules.Drop(HotkeyDragSourceKind.HeldItem, -1, 4, true, true),
            "a held item is dropped by the left release");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HotkeyBarRules.Drop(HotkeyDragSourceKind.Entry, 10, null, false, false));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HotkeyBarRules.Drop(HotkeyDragSourceKind.Spell, -1, 10, false, false));
    }

    [TestMethod]
    public void A_left_click_on_an_entry_activates_it_or_drops_the_held_item()
    {
        Assert.AreEqual(HotkeyClickKind.Activate, HotkeyBarRules.ClickEntry(false, false, false));
        Assert.AreEqual(HotkeyClickKind.Nothing, HotkeyBarRules.ClickEntry(false, false, true), "an empty entry");
        Assert.AreEqual(HotkeyClickKind.Nothing, HotkeyBarRules.ClickEntry(true, false, false), "while a drag goes on");
        Assert.AreEqual(HotkeyClickKind.DropHeld, HotkeyBarRules.ClickEntry(false, true, true));
    }
}
