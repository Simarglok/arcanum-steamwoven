using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-052: the loot window's original layout (ledger IW26–IW32): the holder's grid and its scroll, the hit test, the press and
/// release of the holder's items, the boxes' refused art and the holder's image (no game data). ARC-UI-065: the steal window's frame, drops
/// and right releases (IW35–IW36).</summary>
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

    // ARC-UI-072 (B30, ledger WL4/IW29): the body's doll view.
    private static AreaInventoryView DollView(string? helmet, string? weapon) => new([], "npc:1", "Body", [])
    {
        Grid = [new AreaGridItem("own:1", 0, 1, 1, "Own")],
        BodyDoll = Enum.GetValues<Arcanum.Simulation.Equipment.EquipmentSlot>().Select(slot => slot switch
        {
            Arcanum.Simulation.Equipment.EquipmentSlot.Helmet when helmet is not null => new AreaDollSlot(slot, helmet, "Helmet"),
            Arcanum.Simulation.Equipment.EquipmentSlot.Weapon when weapon is not null => new AreaDollSlot(slot, weapon, "Dagger"),
            _ => new AreaDollSlot(slot, null, null),
        }).ToArray(),
    };

    [TestMethod]
    public void TheBodysDollHasNineSlotRectanglesFromTheTableAndHitsOnlyThemAndTheirLocationsAreTheWearSlots()
    {
        Assert.AreEqual(9, AreaLootArtLayout.BodySlotRects.Count);
        // The helmet's rectangle 157, 182, 42 × 42 and the boots' 156, 330, 42 × 42 (table 0x5CAE08).
        Assert.AreEqual(new AreaLootHit(null, null, 1000, false), AreaLootArtLayout.HitTest(157, 182, 0, (0, 0), doll: true));
        Assert.AreEqual(new AreaLootHit(null, null, 1000, false), AreaLootArtLayout.HitTest(157 + 41.9f, 182 + 41.9f, 0, (0, 0), doll: true));
        Assert.AreEqual(AreaLootHit.None, AreaLootArtLayout.HitTest(157 + 42, 182 + 10, 0, (0, 0), doll: true), "just right of the helmet");
        Assert.AreEqual(new AreaLootHit(null, null, 1008, false), AreaLootArtLayout.HitTest(160, 335, 0, (0, 0), doll: true));
        Assert.AreEqual(new AreaLootHit(null, null, 1004, false), AreaLootArtLayout.HitTest(100, 260, 0, (0, 0), doll: true), "the weapon's");
        // The grid is not there in the doll view: its first cell hits nothing, and Take all and the hero's side still hit.
        Assert.AreEqual(AreaLootHit.None, AreaLootArtLayout.HitTest(8, 136, 0, (0, 0), doll: true));
        Assert.IsTrue(AreaLootArtLayout.HitTest(AreaLootArtLayout.TakeAll.X + 1, AreaLootArtLayout.TakeAll.Y + 1, 0, (10, 10), doll: true).TakeAll);
        Assert.IsNotNull(AreaLootArtLayout.HitTest(AreaInventoryDrag.GridX + 4, AreaInventoryDrag.GridY + 4, 0, (0, 0), doll: true).Cell);
        // Without the doll view the same point is the grid's.
        Assert.AreEqual(new AreaLootHit(null, null, 2, false), AreaLootArtLayout.HitTest(8 + 64, 136, 0, (0, 0)));
    }

    [TestMethod]
    public void AWornItemOfTheDollPressesAsTheHoldersItemAndTakesOnTheHerosGrid()
    {
        var view = DollView("helm", "dagger");
        var held = AreaLootArtLayout.Press(view, AreaLootArtLayout.HitTest(100, 260, 0, (0, 0), doll: true));
        Assert.AreEqual(new AreaInventoryHeld("dagger", null, 0) { Partner = true }, held);
        Assert.IsNull(AreaLootArtLayout.Press(view, AreaLootArtLayout.HitTest(250, 192, 0, (0, 0), doll: true)), "an empty slot (ring 2)");
        Assert.AreEqual((int)Arcanum.Simulation.Equipment.EquipmentSlot.Ring2, AreaLootArtLayout.HitTest(250, 192, 0, (0, 0), doll: true).Location, "x 241 and up is ring 2");
        Assert.AreEqual((int)Arcanum.Simulation.Equipment.EquipmentSlot.Ring1, AreaLootArtLayout.HitTest(230, 192, 0, (0, 0), doll: true).Location, "x 220..240 is ring 1");
        var heroItem = new AreaInventoryHeld("hero-item", null, 0);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(heroItem, AreaLootArtLayout.HitTest(100, 260, 0, (0, 0), doll: true), AreaInventoryMode.Steal).Kind,
            "a hero's item on a doll slot of the steal window is no plant");
        var drop = AreaLootArtLayout.Release(held!, AreaLootArtLayout.HitTest(AreaInventoryDrag.GridX + 4, AreaInventoryDrag.GridY + 4, 0, (0, 0), doll: true), AreaInventoryMode.Loot);
        Assert.AreEqual(AreaInventoryDropKind.Take, drop.Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(held!, AreaLootArtLayout.HitTest(160, 200, 0, (0, 0), doll: true), AreaInventoryMode.Loot).Kind,
            "released on the doll again: it goes back");
        Assert.AreEqual(AreaInventoryDropKind.Take, AreaLootArtLayout.RightRelease(view, AreaLootArtLayout.HitTest(160, 200, 0, (0, 0), doll: true), AreaInventoryMode.Loot)?.Kind);
    }

    [TestMethod]
    public void TheDollAndBagButtonsAreAtTheirPlacesInTheLootAndStealWindowsOfANonContainerAndTheirArtsAreOptional()
    {
        Assert.AreEqual(true, AreaLootArtLayout.ToggleHit(23, 108, (30, 20), (30, 20)));
        Assert.AreEqual(false, AreaLootArtLayout.ToggleHit(74 + 29, 108 + 19, (30, 20), (30, 20)));
        Assert.IsNull(AreaLootArtLayout.ToggleHit(23 + 30, 108, (30, 20), (30, 20)));
        Assert.IsNull(AreaLootArtLayout.ToggleHit(60, 108, (30, 20), (30, 20)));
        var body = DollView(null, null);
        Assert.IsTrue(AreaLootArtLayout.TogglesShown(AreaInventoryMode.Loot, body));
        Assert.IsTrue(AreaLootArtLayout.TogglesShown(AreaInventoryMode.Steal, body));
        Assert.IsFalse(AreaLootArtLayout.TogglesShown(AreaInventoryMode.Own, body));
        Assert.IsFalse(AreaLootArtLayout.TogglesShown(AreaInventoryMode.Barter, body));
        Assert.IsFalse(AreaLootArtLayout.TogglesShown(AreaInventoryMode.Loot, body with { HolderContainer = true }), "a container has no doll");
        CollectionAssert.AreEqual(new[] { 349, 338, 339 }, AreaLootArtLayout.DollArtNumbers.ToArray());
        CollectionAssert.IsSubsetOf(AreaLootArtLayout.DollArtNumbers.ToArray(), AreaInventoryArtContent.Numbers.ToArray());
        CollectionAssert.DoesNotContain(AreaLootArtLayout.ArtNumbers.ToArray(), 349, "the doll arts are optional: the grid view stays without them");
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
        Assert.AreEqual(new AreaInventoryHeld("loot:1", null, 10) { Partner = true }, held, "its lower cell: the grab's offset");
        Assert.IsNull(AreaLootArtLayout.Press(view, new AreaLootHit(null, null, 12, false)));
        Assert.AreEqual(AreaInventoryDropKind.Take, AreaLootArtLayout.Release(held!, new AreaLootHit(5, null, null, false), AreaInventoryMode.Loot).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(held!, new AreaLootHit(null, AreaInventoryIcon.Drop, null, false), AreaInventoryMode.Loot).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(held!, new AreaLootHit(null, null, 30, false), AreaInventoryMode.Loot).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(held!, AreaLootHit.None, AreaInventoryMode.Loot).Kind);
    }

    [TestMethod]
    public void TheHerosItemMovesAsInItsOwnWindowAndNotIntoTheHolder()
    {
        var view = View();
        var own = AreaLootArtLayout.Press(view, new AreaLootHit(0, null, null, false));
        Assert.AreEqual(new AreaInventoryHeld("own:1", null, 0), own);
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.GridMove, own!, GridSlot: 7), AreaLootArtLayout.Release(own!, new AreaLootHit(7, null, null, false), AreaInventoryMode.Loot));
        Assert.AreEqual(AreaInventoryDropKind.DropToGround, AreaLootArtLayout.Release(own!, new AreaLootHit(null, AreaInventoryIcon.Drop, null, false), AreaInventoryMode.Loot).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(own!, new AreaLootHit(null, null, 3, false), AreaInventoryMode.Loot).Kind, "NOT READY: into the holder");
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

    [TestMethod]
    public void AnItemWithoutALocationGoesToTheFirstFreePlaceAndWornItemsAreLeftOut()
    {
        var grid = AreaLootArtLayout.Arrange([
            new AreaLootGridItem("made:1", -1, 1, 1, "Made", Kind), new AreaLootGridItem("seed:1", 0, 2, 2, "Big", Kind),
            new AreaLootGridItem("worn:1", 1004, 1, 1, "Sword", Kind), new AreaLootGridItem("off:1", -1, 1, 3, "Staff", Kind),
            new AreaLootGridItem("odd:1", 2003, 1, 1, "Odd", Kind)]);
        CollectionAssert.AreEqual(new[] { "made:1", "seed:1", "off:1" }, grid.Select(item => item.Entity).ToArray());
        Assert.AreEqual(2, grid[0].Location, "right of the 2 × 2 item placed at 0");
        Assert.AreEqual(3, grid[2].Location);
        Assert.AreEqual(0, grid[1].Location);
    }

    [TestMethod]
    public void TheStealWindowIsTheLootFrameWithoutTakeAll()
    {
        Assert.IsTrue(AreaLootArtLayout.TakeAllShown(AreaInventoryMode.Loot));
        Assert.IsFalse(AreaLootArtLayout.TakeAllShown(AreaInventoryMode.Steal), "the button is made in mode 2 alone (IW30)");
        Assert.IsFalse(AreaLootArtLayout.TakeAllShown(AreaInventoryMode.Own));
        Assert.AreEqual(AreaInventoryHole.Corner(AreaInventoryMode.Loot), AreaInventoryHole.Corner(AreaInventoryMode.Steal));
        Assert.AreEqual(AreaInventoryHole.Art(AreaInventoryMode.Loot), AreaInventoryHole.Art(AreaInventoryMode.Steal));
    }

    [TestMethod]
    public void AStealWindowDropAcrossTheGridsIsATheftOrAPlant()
    {
        var view = View(new AreaLootGridItem("loot:1", 11, 1, 2, "Sword", Kind));
        var partner = AreaLootArtLayout.Press(view, new AreaLootHit(null, null, 11, false))!;
        var own = AreaLootArtLayout.Press(view, new AreaLootHit(0, null, null, false))!;
        Assert.AreEqual(AreaInventoryDropKind.Take, AreaLootArtLayout.Release(partner, new AreaLootHit(5, null, null, false), AreaInventoryMode.Steal).Kind,
            "the victim's item on the hero's grid: a theft (0x575180)");
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Plant, own, GridSlot: 3),
            AreaLootArtLayout.Release(own, new AreaLootHit(null, null, 3, false), AreaInventoryMode.Steal), "the hero's item on the victim's grid: a plant (0x575100)");
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(partner, new AreaLootHit(null, null, 30, false), AreaInventoryMode.Steal).Kind,
            "NOT READY: a move within the victim's grid");
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(partner, new AreaLootHit(null, AreaInventoryIcon.Use, null, false), AreaInventoryMode.Steal).Kind);
        Assert.AreEqual(AreaInventoryDropKind.GridMove, AreaLootArtLayout.Release(own, new AreaLootHit(7, null, null, false), AreaInventoryMode.Steal).Kind);
        Assert.AreEqual(AreaInventoryDropKind.DropToGround, AreaLootArtLayout.Release(own, new AreaLootHit(null, AreaInventoryIcon.Drop, null, false), AreaInventoryMode.Steal).Kind,
            "the hero's own item on the drop box drops as in mode 0");
    }

    [TestMethod]
    public void ARightReleaseMovesTheItemToTheOtherSide()
    {
        var view = View(new AreaLootGridItem("loot:1", 11, 1, 2, "Sword", Kind));
        var lower = new AreaLootHit(null, null, 21, false);
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Take, new AreaInventoryHeld("loot:1", null, 10) { Partner = true }),
            AreaLootArtLayout.RightRelease(view, lower, AreaInventoryMode.Steal));
        Assert.AreEqual(AreaInventoryDropKind.Take, AreaLootArtLayout.RightRelease(view, lower, AreaInventoryMode.Loot)!.Kind);
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Plant, new AreaInventoryHeld("own:1", null, 0)),
            AreaLootArtLayout.RightRelease(view, new AreaLootHit(0, null, null, false), AreaInventoryMode.Steal));
        Assert.IsNull(AreaLootArtLayout.RightRelease(view, new AreaLootHit(0, null, null, false), AreaInventoryMode.Loot), "NOT READY: into the holder");
        Assert.IsNull(AreaLootArtLayout.RightRelease(view, new AreaLootHit(null, null, 12, false), AreaInventoryMode.Steal), "no item");
        Assert.IsNull(AreaLootArtLayout.RightRelease(view, new AreaLootHit(null, AreaInventoryIcon.Drop, null, false), AreaInventoryMode.Steal));
        Assert.IsNull(AreaLootArtLayout.RightRelease(view, lower, AreaInventoryMode.Own), "the own window's right release is the use (IW5)");
    }
}
