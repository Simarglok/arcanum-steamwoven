using Arcanum.Data.Combat;
using Arcanum.Presentation.Hud;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-032: the fields the type icon (0x554C20, ledger HK17) reads from an item's stored fields; invented items.</summary>
[TestClass]
public sealed class AreaItemIconsTests
{
    private static CombatFields Item(int type, int complexity, uint flags, uint inventoryArt = 0, uint useArt = 0) =>
        new(CombatFieldOrigin.Prototype, new CombatObjectFields(type, 15000, 0, 0, 0, 0, 0, 10, 0, 0, 4, [0, 0, 0, 0, 0], 0),
            new CombatItemFields(flags, 1, 1, 0, complexity, 3, 0, [], 0, 0, inventoryArt, useArt), null, null, null, null, null, null);

    [TestMethod]
    public void Identified_is_complexity_zero_or_less_the_stored_flag_or_the_session_mark()
    {
        Assert.IsTrue(AreaItemIcons.Facts(Item(5, 0, 0), false).Identified, "neutral items are always identified");
        Assert.IsTrue(AreaItemIcons.Facts(Item(5, -5, 0), false).Identified, "technological items too");
        Assert.IsFalse(AreaItemIcons.Facts(Item(5, 5, 0x40), false).Identified, "a magickal item needs the flag");
        Assert.IsTrue(AreaItemIcons.Facts(Item(5, 5, 0x1), false).Identified, "the stored flag 0x1");
        Assert.IsTrue(AreaItemIcons.Facts(Item(5, 5, 0), true).Identified, "the session's identification mark");
        Assert.IsTrue(AreaItemIcons.Facts(Item(5, 5, 0x40), false).Hexed, "item flag 0x40");
        // Not identified: a magickal hexed weapon shows the plain magick icon; identified, the hexed one.
        Assert.AreEqual(392, HotkeyBarRules.ItemTypeIcon(AreaItemIcons.Facts(Item(5, 5, 0x40), false)));
        Assert.AreEqual(HotkeyBarRules.HexedIcon, HotkeyBarRules.ItemTypeIcon(AreaItemIcons.Facts(Item(5, 5, 0x40), true)));
        // ARC-UI-035: the item window's name colour (0x554F10).
        Assert.AreEqual(AreaMessageColour.Hexed, AreaItemIcons.NameColour(AreaItemIcons.Facts(Item(5, 5, 0x40), true)), "identified and hexed: orange");
        Assert.AreEqual(AreaMessageColour.Magick, AreaItemIcons.NameColour(AreaItemIcons.Facts(Item(5, 5, 0x40), false)), "hexed but not identified: magick blue");
        Assert.AreEqual(AreaMessageColour.Magick, AreaItemIcons.NameColour(AreaItemIcons.Facts(Item(5, 5, 0), false)));
        Assert.AreEqual(AreaMessageColour.Hexed, AreaItemIcons.NameColour(AreaItemIcons.Facts(Item(5, -5, 0x40), false)), "a technological item counts as identified");
        Assert.AreEqual(AreaMessageColour.White, AreaItemIcons.NameColour(AreaItemIcons.Facts(Item(5, -5, 0), false)));
    }

    [TestMethod]
    public void The_subtype_and_coverage_come_from_the_item_art_ids()
    {
        // An item art id (class 6): the use-art fragment's subtype is bits 6..9, the inventory art's coverage bits 14..16.
        var facts = AreaItemIcons.Facts(Item(7, 0, 0, inventoryArt: 0x60000000u | (5u << 14) | (1u << 12), useArt: 0x60000000u | (8u << 6)), false);
        Assert.AreEqual((8, 5, 3), (facts.UseArtSubtype, facts.Coverage, facts.Discipline));
        Assert.AreEqual(411, HotkeyBarRules.ItemTypeIcon(facts), "a ring");
        var other = AreaItemIcons.Facts(Item(5, 0, 0, inventoryArt: 0x40000000u | (5u << 14), useArt: 0x40000000u | (8u << 6)), false);
        Assert.AreEqual((0, 0), (other.UseArtSubtype, other.Coverage), "an art id of another class reads 0 (0x5044F0, 0x504570)");
    }
}
