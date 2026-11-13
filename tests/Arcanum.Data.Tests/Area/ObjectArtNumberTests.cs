using Arcanum.Data.Area;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Area;

/// <summary>ARC-SCRIPT-1735 (ledger NS5, NS9): the number and frame setters of the original's art id helpers (0x502780, 0x502C40) on invented art ids; no original data.</summary>
[TestClass]
public sealed class ObjectArtNumberTests
{
    [TestMethod]
    public void The_number_bits_depend_on_the_class_and_the_other_bits_stay()
    {
        // Scenery (class 4): 9 bits at bit 19 (kind bits 6..10, frame bits 14..18 stay).
        var scenery = (4u << 28) | (300u << 19) | (7u << 14) | (3u << 6) | 2u;
        Assert.AreEqual((4u << 28) | (12u << 19) | (7u << 14) | (3u << 6) | 2u, ObjectArtNumber.WithNumber(scenery, 12, out var outOfRange));
        Assert.IsFalse(outOfRange);
        // Portal, container and the other classes of the 9-bit rule.
        Assert.AreEqual((3u << 28) | (511u << 19), ObjectArtNumber.WithNumber(3u << 28, 511, out _));
        Assert.AreEqual((7u << 28) | (5u << 19), ObjectArtNumber.WithNumber(7u << 28, 5, out _));
        // Unique NPC (13): 8 bits at bit 20. Interface (5): 12 bits at bit 16.
        Assert.AreEqual((13u << 28) | (255u << 20), ObjectArtNumber.WithNumber(13u << 28, 255, out _));
        Assert.AreEqual((5u << 28) | (4095u << 16) | 0x34, ObjectArtNumber.WithNumber((5u << 28) | 0x34, 4095, out _));
    }

    [TestMethod]
    public void A_number_past_the_range_keeps_the_id_and_the_critter_classes_answer_zero()
    {
        var scenery = (4u << 28) | (9u << 19);
        Assert.AreEqual(scenery, ObjectArtNumber.WithNumber(scenery, 512, out var outOfRange));
        Assert.IsTrue(outOfRange);
        Assert.AreEqual((13u << 28) | (1u << 20), ObjectArtNumber.WithNumber((13u << 28) | (1u << 20), 256, out outOfRange));
        Assert.IsTrue(outOfRange);
        Assert.AreEqual(scenery, ObjectArtNumber.WithNumber(scenery, unchecked((uint)-1), out outOfRange), "a negative number is a huge one");
        foreach (var cls in new[] { 0u, 1u, 2u, 12u })
        {
            Assert.AreEqual(0u, ObjectArtNumber.WithNumber((cls << 28) | 0x1234, 5, out _), $"class {cls}");
            Assert.IsTrue(ObjectArtNumber.IsCritterClass(cls << 28));
        }
    }

    [TestMethod]
    public void An_item_number_has_11_bits_at_bit_17_and_a_range_by_its_type_and_coverage()
    {
        // Ranges (0x502830, 0x504550, 0x504570): types 0 and 1 and an armour (2) of coverage 0 allow 20 numbers, every other type 1,000; a non-item art id has none.
        Assert.AreEqual(20u, ObjectArtNumber.ItemRange((6u << 28) | 0u));
        Assert.AreEqual(20u, ObjectArtNumber.ItemRange((6u << 28) | 1u | (3u << 6)));
        Assert.AreEqual(20u, ObjectArtNumber.ItemRange((6u << 28) | 2u));
        Assert.AreEqual(1000u, ObjectArtNumber.ItemRange((6u << 28) | 2u | (5u << 14)), "an armour with coverage");
        foreach (var type in new[] { 3u, 4u, 5u, 9u, 15u }) Assert.AreEqual(1000u, ObjectArtNumber.ItemRange((6u << 28) | type), $"type {type}");
        Assert.AreEqual(0u, ObjectArtNumber.ItemRange(4u << 28));
        Assert.AreEqual(0, ObjectArtNumber.ItemType(4u << 28 | 3));
        Assert.AreEqual(3, ObjectArtNumber.ItemType((6u << 28) | 0x1243));
        // The setter replaces bits 17..27 and keeps the type, subtype, place, coverage and destroyed bits.
        var weapon = (6u << 28) | (7u << 17) | (2u << 14) | 0x400 | (5u << 6) | 1u;
        Assert.AreEqual((6u << 28) | (19u << 17) | (2u << 14) | 0x400 | (5u << 6) | 1u, ObjectArtNumber.WithNumber(weapon, 19, out var outOfRange));
        Assert.IsFalse(outOfRange);
        // 20 is past a 20-number range and the id is kept; the same number on a type with 1,000 numbers is taken.
        Assert.AreEqual(weapon, ObjectArtNumber.WithNumber(weapon, 20, out outOfRange));
        Assert.IsTrue(outOfRange);
        var other = (6u << 28) | (7u << 17) | 4u;
        Assert.AreEqual((6u << 28) | (999u << 17) | 4u, ObjectArtNumber.WithNumber(other, 999, out outOfRange));
        Assert.IsFalse(outOfRange);
        Assert.AreEqual(other, ObjectArtNumber.WithNumber(other, 1000, out outOfRange));
        Assert.IsTrue(outOfRange);
        Assert.AreEqual(other, ObjectArtNumber.WithNumber(other, unchecked((uint)-1), out outOfRange), "a negative number is a huge one");
        // Frame 0 keeps an item id, so the change is the number setter alone.
        Assert.AreEqual((6u << 28) | (19u << 17) | (2u << 14) | 0x400 | (5u << 6) | 1u, ObjectArtNumber.Changed(weapon, 19, out _));
    }

    [TestMethod]
    public void Frame_zero_clears_the_frame_bits_of_the_class()
    {
        Assert.AreEqual((4u << 28) | (9u << 19) | 0x40, ObjectArtNumber.WithFrameZero((4u << 28) | (9u << 19) | (31u << 14) | 0x40), "scenery: bits 14..18");
        Assert.AreEqual((9u << 28) | 0xFFF, ObjectArtNumber.WithFrameZero((9u << 28) | (0x7Fu << 12) | 0xFFF), "light: bits 12..18");
        Assert.AreEqual(0xE000_0000u | 0xFFF, ObjectArtNumber.WithFrameZero(0xE000_0000u | (0x7Fu << 12) | 0xFFF), "eye candy: bits 12..18");
        Assert.AreEqual(0x5000_00FFu, ObjectArtNumber.WithFrameZero(0x5000_FFFFu), "interface: bits 8..15");
        Assert.AreEqual(0xB000_0801u, ObjectArtNumber.WithFrameZero(0xB000_0FFFu | 0x0), "facade: bits 1..10");
        // Tile, wall and item keep the id.
        foreach (var cls in new[] { 0u, 1u, 6u }) Assert.AreEqual((cls << 28) | 0x7FFFF, ObjectArtNumber.WithFrameZero((cls << 28) | 0x7FFFF));
    }

    [TestMethod]
    public void Changed_is_the_number_setter_then_frame_zero()
    {
        var framed = (4u << 28) | (9u << 19) | (3u << 14) | (3u << 6);
        Assert.AreEqual((4u << 28) | (4u << 19) | (3u << 6), ObjectArtNumber.Changed(framed, 4, out var outOfRange));
        Assert.IsFalse(outOfRange);
        Assert.AreEqual((4u << 28) | (9u << 19) | (3u << 6), ObjectArtNumber.Changed(framed, 9999, out outOfRange), "out of range: the id is kept and its frame reset, as the original writes it");
        Assert.IsTrue(outOfRange);
    }
}
