using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-052: the loot window's original layout (ledger IW26–IW32): the holder's grid and its scroll, the hit test, the press and
/// release of the holder's items, the boxes' refused art and the holder's image (no game data).</summary>
[TestClass]
public sealed class AreaLootArtTests
{
    private static readonly SessionItemKind Kind = new(SessionItemType.Generic, 9000, 0, 0);

    private static AreaInventoryView View(params AreaLootGridItem[] loot) =>
        new([], "npc:1", "Body", []) { LootGrid = loot, Grid = [new AreaGridItem("own:1", 0, 1, 1, "Own")] };

    [TestMethod]
    public void TheLootArtsAreRead()
    {
        CollectionAssert.AreEqual(new[] { 234, 808, 348, 347 }, AreaLootArtLayout.ArtNumbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaLootArtLayout.ArtNumbers.ToArray(), AreaInventoryArtContent.Numbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaLootArtLayout.ImageNumbers.ToArray(), AreaInventoryArtContent.Numbers.ToArray());
        CollectionAssert.AllItemsAreUnique(AreaInventoryArtContent.Numbers.ToArray());
    }

    [TestMethod]
    public void TheHoldersGridIsTenByEightShownRowsFrom8And136WithItsScroll()
    {
        Assert.AreEqual(new AreaLootHit(null, null, 0, false), AreaLootArtLayout.HitTest(8, 136, 0, (0, 0)));
        Assert.AreEqual(new AreaLootHit(null, null, 79, false), AreaLootArtLayout.HitTest(8 + 319.9f, 136 + 255.9f, 0, (0, 0)));
        Assert.AreEqual(new AreaLootHit(null, null, 21, false), AreaLootArtLayout.HitTest(8 + 32, 136, 2, (0, 0)), "(row + scroll) × 10 + column");
        Assert.AreEqual(AreaLootHit.None, AreaLootArtLayout.HitTest(8 + 320, 136, 0, (0, 0)), "the scroll bar's column");
        Assert.AreEqual(AreaLootHit.None, AreaLootArtLayout.HitTest(8, 136 + 256, 0, (0, 0)), "below the eighth row");
        Assert.AreEqual(AreaLootHit.None, AreaLootArtLayout.HitTest(160, 200 - 70, 0, (0, 0)), "the hero's doll is not shown: no slot");
    }

    [TestMethod]
    public void TheHerosSideIsItsGridAndBoxesAndTakeAllIsTheButtonsArt()
    {
        Assert.AreEqual(new AreaLootHit(0, null, null, false), AreaLootArtLayout.HitTest(368, 8, 3, (0, 0)), "the hero's grid does not scroll");
        Assert.AreEqual(new AreaLootHit(null, AreaInventoryIcon.Drop, null, false), AreaLootArtLayout.HitTest(703, 344, 0, (0, 0)));
        Assert.AreEqual(new AreaLootHit(null, AreaInventoryIcon.Use, null, false), AreaLootArtLayout.HitTest(703, 290, 0, (0, 0)));
        Assert.AreEqual(new AreaLootHit(null, null, null, true), AreaLootArtLayout.HitTest(301, 47, 0, (60, 20)));
        Assert.AreEqual(AreaLootHit.None, AreaLootArtLayout.HitTest(301 + 60, 47, 0, (60, 20)));
    }

    [TestMethod]
    public void AnItemIsDrawnAtItsLocationAndCutToTheShownRows()
    {
        var item = new AreaLootGridItem("loot:1", 23, 1, 2, "Sword", Kind);
        Assert.AreEqual((8f + 96, 136f + 64, 32f, 64f), AreaLootArtLayout.ItemRect(item, 0));
        Assert.AreEqual((8f + 96, 136f, 32f, 32f), AreaLootArtLayout.ItemRect(item, 3), "its upper cell is scrolled away");
        Assert.IsNull(AreaLootArtLayout.ItemRect(item, 4));
        Assert.AreEqual((8f + 96, 136f + 32 * 7, 32f, 32f), AreaLootArtLayout.ItemRect(new AreaLootGridItem("loot:2", 73, 1, 2, "Staff", Kind), 0));
        Assert.IsTrue(AreaLootArtLayout.GridLocation(0) && AreaLootArtLayout.GridLocation(999));
        Assert.IsFalse(AreaLootArtLayout.GridLocation(1004) || AreaLootArtLayout.GridLocation(2001) || AreaLootArtLayout.GridLocation(-1));
        Assert.AreEqual((4, 88), (AreaLootArtLayout.ScrollMax(container: false), AreaLootArtLayout.ScrollMax(container: true)));
    }

    [TestMethod]
    public void AHoldersItemIsTakenOnTheHerosGridAndGoesBackElsewhere()
    {
        var view = View(new AreaLootGridItem("loot:1", 11, 1, 2, "Sword", Kind));
        var held = AreaLootArtLayout.Press(view, new AreaLootHit(null, null, 21, false));
        Assert.AreEqual(new AreaInventoryHeld("loot:1", null, 0) { Partner = true }, held, "its lower cell");
        Assert.IsNull(AreaLootArtLayout.Press(view, new AreaLootHit(null, null, 12, false)));
        Assert.AreEqual(AreaInventoryDropKind.Take, AreaLootArtLayout.Release(held!, new AreaLootHit(5, null, null, false)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(held!, new AreaLootHit(null, AreaInventoryIcon.Drop, null, false)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(held!, new AreaLootHit(null, null, 30, false)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(held!, AreaLootHit.None).Kind);
    }

    [TestMethod]
    public void TheHerosItemMovesAsInItsOwnWindowAndNotIntoTheHolder()
    {
        var view = View();
        var own = AreaLootArtLayout.Press(view, new AreaLootHit(0, null, null, false));
        Assert.AreEqual(new AreaInventoryHeld("own:1", null, 0), own);
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.GridMove, own!, GridSlot: 7), AreaLootArtLayout.Release(own!, new AreaLootHit(7, null, null, false)));
        Assert.AreEqual(AreaInventoryDropKind.DropToGround, AreaLootArtLayout.Release(own!, new AreaLootHit(null, AreaInventoryIcon.Drop, null, false)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(own!, new AreaLootHit(null, null, 3, false)).Kind, "NOT READY: into the holder");
    }

    [TestMethod]
    public void TheBoxesRefuseTheHoldersItem()
    {
        var partner = new AreaInventoryHeld("loot:1", null, 0) { Partner = true };
        var own = new AreaInventoryHeld("own:1", null, 0);
        Assert.AreEqual(348, AreaLootArtLayout.BoxArt(AreaInventoryIcon.Drop, partner, over: true));
        Assert.AreEqual(347, AreaLootArtLayout.BoxArt(AreaInventoryIcon.Use, partner, over: true));
        Assert.AreEqual(224, AreaLootArtLayout.BoxArt(AreaInventoryIcon.Drop, partner, over: false));
        Assert.AreEqual(225, AreaLootArtLayout.BoxArt(AreaInventoryIcon.Drop, own, over: true));
        Assert.AreEqual(342, AreaLootArtLayout.BoxArt(AreaInventoryIcon.Use, null, over: true));
    }

    [TestMethod]
    public void TheHoldersImageIsItsKindFlagsRaceOrContainer()
    {
        Assert.AreEqual(375, AreaLootArtLayout.HolderImage(16, 0, 0, 0), "a human");
        Assert.AreEqual(378, AreaLootArtLayout.HolderImage(16, 0, 1, 0), "a dwarf");
        Assert.AreEqual(634, AreaLootArtLayout.HolderImage(16, 0, 11, 0));
        Assert.IsNull(AreaLootArtLayout.HolderImage(16, 0, 12, 0));
        Assert.AreEqual(384, AreaLootArtLayout.HolderImage(16, 0x4 | 0x8000, 0, 0), "undead first");
        Assert.AreEqual(383, AreaLootArtLayout.HolderImage(16, 0x8000 | 0x8, 0, 0));
        Assert.AreEqual(385, AreaLootArtLayout.HolderImage(16, 0x8 | 0x20000000, 0, 0));
        Assert.AreEqual(434, AreaLootArtLayout.HolderImage(16, 0x20000000, 0, 0));
        Assert.AreEqual(832, AreaLootArtLayout.HolderImage(2, 0, -1, 3023), "a junk pile");
        Assert.AreEqual(833, AreaLootArtLayout.HolderImage(2, 0, -1, 3056));
        Assert.AreEqual(435, AreaLootArtLayout.HolderImage(2, 0, -1, 3024));
        Assert.IsNull(AreaLootArtLayout.HolderImage(3, 0, 0, 0));
    }
}
