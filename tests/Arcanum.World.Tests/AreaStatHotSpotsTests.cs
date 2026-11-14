using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-UI-1924 (ledger IW46): the hot spots of the inventory windows that open the weapon and armour stat lines (open 0x5727B0, handler 0x573850).</summary>
[TestClass]
public sealed class AreaStatHotSpotsTests
{
    private static readonly (int, int) Art = (40, 30);

    private static AreaStatTarget? Hit(AreaInventoryMode mode, bool hero, bool partner, float x, float y) => AreaStatHotSpots.Hit(mode, hero, partner, x, y, Art, Art);

    [TestMethod]
    public void The_own_window_has_the_hero_buttons_and_the_rectangles_over_the_numbers_all_the_time()
    {
        Assert.AreEqual(AreaStatTarget.HeroWeapon, Hit(AreaInventoryMode.Own, false, false, 29, 318), "the art button's corner");
        Assert.AreEqual(AreaStatTarget.HeroWeapon, Hit(AreaInventoryMode.Own, false, false, 68, 347), "the art button's last pixel");
        Assert.IsNull(Hit(AreaInventoryMode.Own, false, false, 69, 347), "past the art");
        Assert.AreEqual(AreaStatTarget.HeroWeapon, Hit(AreaInventoryMode.Own, false, false, 80, 358), "the rectangle over the damage number (0x5CAEF8)");
        Assert.AreEqual(AreaStatTarget.HeroWeapon, Hit(AreaInventoryMode.Own, false, false, 102, 392));
        Assert.IsNull(Hit(AreaInventoryMode.Own, false, false, 103, 358));
        Assert.AreEqual(AreaStatTarget.HeroArmour, Hit(AreaInventoryMode.Own, false, false, 254, 319));
        Assert.AreEqual(AreaStatTarget.HeroArmour, Hit(AreaInventoryMode.Own, false, false, 261, 358), "the rectangle over the armour number (0x5CAF08)");
        Assert.IsNull(Hit(AreaInventoryMode.Own, false, false, 29, 400));
    }

    [TestMethod]
    public void The_windows_of_modes_1_to_4_make_the_hero_buttons_hidden_and_show_them_with_the_hero_doll_toggle()
    {
        foreach (var mode in new[] { AreaInventoryMode.Barter, AreaInventoryMode.Loot, AreaInventoryMode.Steal, AreaInventoryMode.Contents })
        {
            Assert.IsFalse(AreaStatHotSpots.HeroShown(mode, false), mode.ToString());
            Assert.IsNull(Hit(mode, false, false, 374, 318), mode + ": hidden");
            Assert.IsNull(Hit(mode, false, false, 425, 358), mode + ": the rectangle too");
            Assert.AreEqual(AreaStatTarget.HeroWeapon, Hit(mode, true, false, 374, 318), mode.ToString());
            Assert.AreEqual(AreaStatTarget.HeroWeapon, Hit(mode, true, false, 425, 358), "0x5CAF18");
            Assert.AreEqual(AreaStatTarget.HeroArmour, Hit(mode, true, false, 599, 319), mode.ToString());
            Assert.AreEqual(AreaStatTarget.HeroArmour, Hit(mode, true, false, 609, 358), "0x5CAF28");
            Assert.IsNull(Hit(mode, true, false, 29, 318), "the own window's places are not made there");
        }
        Assert.IsTrue(AreaStatHotSpots.HeroShown(AreaInventoryMode.Own, false));
    }

    [TestMethod]
    public void The_other_partys_two_hot_spots_are_55_by_40_and_follow_its_doll_toggle()
    {
        Assert.IsNull(Hit(AreaInventoryMode.Loot, false, false, 75, 329), "hidden until its doll toggle is set");
        Assert.AreEqual(AreaStatTarget.PartnerWeapon, Hit(AreaInventoryMode.Loot, false, true, 75, 329));
        Assert.AreEqual(AreaStatTarget.PartnerWeapon, Hit(AreaInventoryMode.Loot, false, true, 129, 368));
        Assert.IsNull(Hit(AreaInventoryMode.Loot, false, true, 130, 329));
        Assert.IsNull(Hit(AreaInventoryMode.Loot, false, true, 75, 369));
        Assert.AreEqual(AreaStatTarget.PartnerArmour, Hit(AreaInventoryMode.Barter, false, true, 225, 329));
        Assert.AreEqual(AreaStatTarget.PartnerArmour, Hit(AreaInventoryMode.Barter, false, true, 279, 368));
        Assert.AreEqual((AreaStatTarget?)null, Hit(AreaInventoryMode.Barter, false, true, 280, 329));
    }

    [TestMethod]
    public void The_arts_are_676_and_677_and_the_places_are_the_executables()
    {
        CollectionAssert.AreEqual(new[] { 676, 677 }, AreaStatHotSpots.ArtNumbers.ToArray());
        Assert.AreEqual((29, 318), AreaStatHotSpots.ArtButton(AreaInventoryMode.Own, true));
        Assert.AreEqual((254, 319), AreaStatHotSpots.ArtButton(AreaInventoryMode.Own, false));
        Assert.AreEqual((374, 318), AreaStatHotSpots.ArtButton(AreaInventoryMode.Loot, true));
        Assert.AreEqual((599, 319), AreaStatHotSpots.ArtButton(AreaInventoryMode.Loot, false));
    }
}
