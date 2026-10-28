using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Equipment;

// The body-armour art (ARC-WORLD-112, ledger BA1–BA6). Art ids are built from their bit fields; none is an original object's.
[TestClass]
public sealed class BodyArmorArtRulesTests
{
    // A critter art id: type 2, body 0 (human), male (bit 27), armour part, animation 0, weapon 0, palette.
    private static uint Critter(int armor, int palette = 0, uint body = 0) => 0x20000000u | 1u << 27 | body << 24 | (uint)armor << 20 | (uint)palette << 4;
    // An item use art: type 6 with an armour subtype and a palette.
    private static uint Use(int subtype, int palette = 0) => 0x60000000u | (uint)subtype << 6 | (uint)palette << 4;

    [TestMethod]
    public void WearingTakesTheItemsSubtypeAndPalette()
    {
        // Leather (2) in palette 1 on a bare human man: only the armour part and the palette change.
        Assert.AreEqual(Critter(2, 1), BodyArmorArtRules.WornArt(Critter(0), Critter(0), Use(2, 1)));
        // Clothes (8) replace chain (3); the body bits stay.
        Assert.AreEqual(Critter(8, 0, body: 2), BodyArmorArtRules.WornArt(Critter(3, 2, body: 2), Critter(0, 0, body: 2), Use(8)));
    }

    [TestMethod]
    public void TakingOffLeavesUnderwearInTheBasePalette()
    {
        Assert.AreEqual(Critter(0, 3), BodyArmorArtRules.WornArt(Critter(4, 1), Critter(0, 3), null));
    }

    [TestMethod]
    public void APartTheArtCannotHoldFailsTheWear()
    {
        // Subtype 9..15 does not fit a critter's parts 0..8: the setter writes 0, the check fails.
        Assert.IsNull(BodyArmorArtRules.WornArt(Critter(0), Critter(0), Use(9)));
        Assert.AreEqual(Critter(0), BodyArmorArtRules.WithArmor(Critter(2), 12));
        // A monster holds 0..7.
        const uint monster = 0xC0000000u;
        Assert.AreEqual(monster | 7u << 20, BodyArmorArtRules.WornArt(monster, monster, Use(7)));
        Assert.IsNull(BodyArmorArtRules.WornArt(monster, monster, Use(8)));
        // A unique NPC has no armour part: only part 0 stands.
        const uint unique = 0xD0000000u;
        Assert.IsNull(BodyArmorArtRules.WornArt(unique, unique, Use(1)));
        Assert.AreEqual(unique | 2u << 4, BodyArmorArtRules.WornArt(unique, unique, Use(0, 2)));
    }

    [TestMethod]
    public void ArtFieldsReadAndWriteTheirBits()
    {
        Assert.AreEqual(5, BodyArmorArtRules.ItemArmor(Use(5, 3)));
        Assert.AreEqual(0, BodyArmorArtRules.ItemArmor(Critter(5)), "not an item art");
        Assert.AreEqual(2, BodyArmorArtRules.Palette(Critter(1, 2)));
        Assert.AreEqual(0, BodyArmorArtRules.Palette(0x90000030u), "type 9 has no palette");
        Assert.AreEqual(Critter(1, 0), BodyArmorArtRules.WithPalette(Critter(1, 3), 4), "a palette past 3 sets 0");
        Assert.AreEqual(0xB0000000u, BodyArmorArtRules.WithPalette(0xB0000000u, 2), "type 11 is unchanged");
        Assert.AreEqual(0x60000000u, BodyArmorArtRules.WithArmor(0x60000000u, 3), "an item art has no armour part");
    }
}
