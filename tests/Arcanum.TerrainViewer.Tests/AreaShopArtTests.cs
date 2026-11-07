using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

// ARC-UI-069: the opening shop's original look (window 15, ledger MW30). Synthetic art only.
[TestClass]
public sealed class AreaShopArtTests
{
    private static AreaPremadeBrowserArtContent Content(IEnumerable<int> numbers) => new(
        numbers.ToDictionary(number => number,
            number => new AreaInterfaceArtLoad(number, AreaInterfaceArtStatus.Ready, null, [new AreaInterfaceFrame(5, 19, new byte[5 * 19 * 4], 4)], "synthetic")),
        null, null, null, null, "synthetic");

    [TestMethod]
    public void The_shop_s_buttons_stand_in_the_bottom_strip_as_window_12_s()
    {
        // Table 0x5C5408: Accept 321 at (675, 55) and Back 323 at (69, 55), flag 1 (from the bottom strip's top, 441).
        Assert.AreEqual((321, 675, 496), AreaShopArtLayout.Button(AreaShopArtButton.Accept));
        Assert.AreEqual((323, 69, 496), AreaShopArtLayout.Button(AreaShopArtButton.Back));
        Assert.AreEqual((7003, 7002), (AreaShopArtLayout.Hint(AreaShopArtButton.Accept), AreaShopArtLayout.Hint(AreaShopArtButton.Back)));
        Assert.AreEqual(AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.Accept), AreaShopArtLayout.Button(AreaShopArtButton.Accept));
        Assert.AreEqual(AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.Cancel), AreaShopArtLayout.Button(AreaShopArtButton.Back));
        Assert.AreEqual((0, 41, 800, 400), AreaShopArtLayout.Window, "the barter window is a game window at (0, 41)");
        CollectionAssert.AreEqual(new[] { 336, 335, 321, 323 }, AreaShopArtLayout.ArtNumbers.ToArray(), "no background of its own (MW1)");
    }

    [TestMethod]
    public void The_focus_rows_light_accept_then_back()
    {
        Assert.AreEqual(AreaShopArtButton.Accept, AreaShopArtLayout.Focused(0));
        Assert.AreEqual(AreaShopArtButton.Back, AreaShopArtLayout.Focused(1));
        Assert.IsNull(AreaShopArtLayout.Focused(2));
        Assert.IsNull(AreaShopArtLayout.Focused(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaShopArtLayout.Button((AreaShopArtButton)7));
    }

    [TestMethod]
    public void The_shop_s_art_is_ready_with_its_strips_and_buttons_alone()
    {
        Assert.IsTrue(Content(AreaShopArtLayout.ArtNumbers).ShopArtReady, "the shop draws no text: no tables needed");
        Assert.IsFalse(Content(AreaShopArtLayout.ArtNumbers.Where(number => number != 323)).ShopArtReady, "without Back's art the shop keeps its diagnostic look");
        Assert.IsFalse(AreaPremadeBrowserArtContent.Empty("none").ShopArtReady);
        Assert.IsTrue(AreaShopArtLayout.ArtNumbers.All(AreaPremadeBrowserArtContent.AllArtNumbers.Contains), "one read serves the shop too");
    }
}
