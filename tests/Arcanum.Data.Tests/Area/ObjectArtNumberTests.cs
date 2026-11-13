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
        // An item's range depends on its subtype and is not modelled: the id is kept.
        Assert.IsFalse(ObjectArtNumber.ItemRangeKnown(6u << 28));
        Assert.AreEqual((6u << 28) | 0x40, ObjectArtNumber.WithNumber((6u << 28) | 0x40, 3, out _));
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
