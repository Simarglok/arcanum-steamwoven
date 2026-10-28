using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
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
}
