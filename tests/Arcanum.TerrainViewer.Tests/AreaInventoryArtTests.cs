using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-051: the inventory window's original layout (ledger IW20–IW25) and its lines with the project's texts (no game data).</summary>
[TestClass]
public sealed class AreaInventoryArtTests
{
    [TestMethod]
    public void TheWindowIsTheDollAndPackArtsWithACoverForEverySlot()
    {
        Assert.AreEqual((800f, 400f), (AreaInventoryDrag.FrameWidth, AreaInventoryDrag.FrameHeight));
        CollectionAssert.AreEquivalent(Enum.GetValues<EquipmentSlot>(), AreaInventoryArtLayout.Covers.Keys.ToArray());
        CollectionAssert.AreEquivalent(new[] { 241, 242, 243, 244, 245, 246, 247, 248, 249 }, AreaInventoryArtLayout.Covers.Values.ToArray());
        CollectionAssert.AllItemsAreUnique(AreaInventoryArtLayout.ArtNumbers.ToArray());
        Assert.AreEqual(15, AreaInventoryArtLayout.ArtNumbers.Count);
    }

    [TestMethod]
    public void TheBoxesTakeTheArtsSizeAndLightUpOnlyUnderAHeldItem()
    {
        Assert.AreEqual(new AreaInventoryHit(null, null, AreaInventoryIcon.Drop), AreaInventoryDrag.HitTest(703 + 79.9f, 344 + 42.9f));
        Assert.AreEqual(new AreaInventoryHit(null, null, AreaInventoryIcon.Use), AreaInventoryDrag.HitTest(703 + 79.9f, 290 + 42.9f));
        Assert.AreEqual(AreaInventoryHit.None, AreaInventoryDrag.HitTest(703 + 80, 344));
        Assert.AreEqual(AreaInventoryHit.None, AreaInventoryDrag.HitTest(703, 290 + 43), "between the boxes");
        Assert.AreEqual(AreaInventoryArtLayout.DropBox, AreaInventoryArtLayout.Box(AreaInventoryIcon.Drop));
        Assert.AreEqual(((int)AreaInventoryDrag.IconX, (int)AreaInventoryDrag.UseIconY), AreaInventoryArtLayout.Box(AreaInventoryIcon.Use));
        Assert.AreEqual(224, AreaInventoryArtLayout.BoxArt(AreaInventoryIcon.Drop, heldOver: false));
        Assert.AreEqual(225, AreaInventoryArtLayout.BoxArt(AreaInventoryIcon.Drop, heldOver: true));
        Assert.AreEqual(342, AreaInventoryArtLayout.BoxArt(AreaInventoryIcon.Use, heldOver: false));
        Assert.AreEqual(343, AreaInventoryArtLayout.BoxArt(AreaInventoryIcon.Use, heldOver: true));
    }

    [TestMethod]
    public void AnItemsArtIsCentredAndALargerOneShrinksToFit()
    {
        Assert.AreEqual((376f, 16f, 16f, 16f), AreaInventoryArtLayout.Place((368, 8, 32, 32), 16, 16), "(cells × 32 − art) / 2");
        Assert.AreEqual((23f + 16, 170f, 64f, 128f), AreaInventoryArtLayout.Place((23, 170, 96, 128), 64, 128));
        Assert.AreEqual((368f + 8, 8f, 16f, 32f), AreaInventoryArtLayout.Place((368, 8, 32, 32), 32, 64), "PROJECT: half size");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaInventoryArtLayout.Place((0, 0, 32, 32), 0, 5));
    }

    [TestMethod]
    public void TheLinesAreWeightEncumbranceAndSpeed()
    {
        var texts = AreaInventoryTexts.Project("test");
        CollectionAssert.AreEqual(new[] { "Total weight: 120 stone", "Encumbrance: level 1 (75)", "Speed: 9" },
            texts.Lines(new SessionHeroCarry(120, 250, 1), 9).ToArray(), "the bound is the carry weight times 30 / 100");
        CollectionAssert.AreEqual(new[] { "", "", "" }, texts.Lines(null, null).ToArray());
        Assert.AreEqual(3, AreaInventoryArtLayout.Lines.Count);
        Assert.AreEqual(5, AreaInventoryArtLayout.Counts.Count);
    }

    [TestMethod]
    public void TheRoundViewIsAnHalfScaleSquareOfTheGameViewAroundTheHolder()
    {
        // ARC-UI-054 (IW33, IW34): (11, 9) with art 233 in the player's own window, (16, 17) with art 232 in a loot window, 89 × 89;
        // ARC-UI-060: (16, 17) with art 231 in the barter window and the loot window's in the steal window.
        Assert.AreEqual((11, 9), AreaInventoryHole.Corner(AreaInventoryMode.Own));
        foreach (var mode in new[] { AreaInventoryMode.Barter, AreaInventoryMode.Loot, AreaInventoryMode.Steal })
            Assert.AreEqual((16, 17), AreaInventoryHole.Corner(mode), mode.ToString());
        CollectionAssert.AreEqual(new[] { 233, 231, 232, 232 }, Enum.GetValues<AreaInventoryMode>().Select(AreaInventoryHole.Art).ToArray());
        CollectionAssert.IsSubsetOf(new[] { 232, 233 }, AreaInventoryArtContent.Numbers.ToArray());
        CollectionAssert.DoesNotContain(AreaInventoryArtLayout.ArtNumbers.ToArray(), 233, "the round view's art is no reason for the diagnostic window");
        Assert.IsTrue(AreaInventoryHole.Contains(AreaInventoryMode.Loot, 16, 17) && AreaInventoryHole.Contains(AreaInventoryMode.Loot, 104.9f, 105.9f),
            "a square: its corners are in");
        Assert.IsFalse(AreaInventoryHole.Contains(AreaInventoryMode.Loot, 105, 50) || AreaInventoryHole.Contains(AreaInventoryMode.Loot, 50, 106)
            || AreaInventoryHole.Contains(AreaInventoryMode.Loot, 15.9f, 50));
        Assert.IsTrue(AreaInventoryHole.Contains(AreaInventoryMode.Own, 11, 9) && !AreaInventoryHole.Contains(AreaInventoryMode.Own, 100, 9));
        Assert.IsTrue(AreaInventoryHole.Contains(AreaInventoryMode.Barter, 16, 17) && !AreaInventoryHole.Contains(AreaInventoryMode.Barter, 11, 9));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaInventoryHole.Corner((AreaInventoryMode)4));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaInventoryHole.Art((AreaInventoryMode)(-1)));
        // The game window's (311, 96, 178, 178) with the view centred on the holder at (400, 200): the square's centre is 15 pixels higher.
        Assert.AreEqual((311f, 96f, 178f), AreaInventoryHole.Source(400, 200, 1));
        Assert.AreEqual((400f - 178, 200f - 30 - 178, 356f), AreaInventoryHole.Source(400, 200, 2), "at zoom 2 the square doubles");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaInventoryHole.Source(0, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaInventoryHole.Source(0, 0, float.NaN));
    }

    [TestMethod]
    public void ARoundViewReleaseClosesTheWindowAndOnlyTheOwnWindowDropsAHeldItemOnTheMap()
    {
        // ARC-UI-060 (IW34, IW18): nothing held closes the window in every mode; a held item goes back and the window closes outside
        // the own window, and in the own window it takes the map drop (NOT READY in the viewer).
        foreach (var mode in Enum.GetValues<AreaInventoryMode>())
        {
            Assert.AreEqual(AreaRoundViewRelease.Close, AreaInventoryHole.Release(mode, held: false), mode.ToString());
            Assert.AreEqual(mode == AreaInventoryMode.Own ? AreaRoundViewRelease.MapDrop : AreaRoundViewRelease.PutBackAndClose,
                AreaInventoryHole.Release(mode, held: true), mode.ToString());
        }
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaInventoryHole.Release((AreaInventoryMode)7, held: false));
    }
}
