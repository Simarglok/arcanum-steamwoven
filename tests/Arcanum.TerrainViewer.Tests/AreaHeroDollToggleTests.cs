using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1918 (ledger IW26, IW30, IW14): the hero's own doll toggle of the windows of modes 1 to 4: the tables read from the executable (0x5CACE8, 0x5CAEC8, 0x5CAF18), the
/// buttons' places, the hit test with the toggle set and the press, release and right release of the hero's worn items in the loot, steal and barter windows (no game data).</summary>
[TestClass]
public sealed class AreaHeroDollToggleTests
{
    private static readonly SessionItemKind Kind = new(SessionItemType.Generic, 9000, 0, 0);

    private static AreaInventoryView View(string? helmet = "helm:1", string? weapon = null) => new([], "npc:1", "Body", [])
    {
        Grid = [new AreaGridItem("own:1", 0, 1, 1, "Own")],
        LootGrid = [new AreaLootGridItem("loot:1", 0, 1, 1, "Loot", Kind)],
        Doll = Enum.GetValues<EquipmentSlot>().Select(slot => slot switch
        {
            EquipmentSlot.Helmet when helmet is not null => new AreaDollSlot(slot, helmet, "Helmet"),
            EquipmentSlot.Weapon when weapon is not null => new AreaDollSlot(slot, weapon, "Dagger"),
            _ => new AreaDollSlot(slot, null, null),
        }).ToArray(),
    };

    private static (float X, float Y) Centre(EquipmentSlot slot)
    {
        var rect = AreaHeroDollToggle.SlotRects[slot];
        return (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    [TestMethod]
    public void TheTablesAreTheOriginalsRead()
    {
        // The slot rectangles of 0x5CACE8: helmet 499, 107, 64, 64; ring 1 595, 107, 32, 32; weapon 371, 170, 96, 128; every one of the own window's table 348 pixels to the right.
        Assert.AreEqual((499f, 107f, 64f, 64f), AreaHeroDollToggle.SlotRects[EquipmentSlot.Helmet]);
        Assert.AreEqual((595f, 107f, 32f, 32f), AreaHeroDollToggle.SlotRects[EquipmentSlot.Ring1]);
        Assert.AreEqual((371f, 170f, 96f, 128f), AreaHeroDollToggle.SlotRects[EquipmentSlot.Weapon]);
        foreach (var (slot, own) in AreaInventoryDrag.SlotRects)
            Assert.AreEqual((own.X + 348, own.Y, own.Width, own.Height), AreaHeroDollToggle.SlotRects[slot], slot.ToString());
        // The weight lines of 0x5CAEC8 and the ratings of 0x5CAF18 and 0x5CAF28.
        CollectionAssert.AreEqual(new[] { (431, 32, 185, 15), (431, 47, 185, 15), (431, 62, 185, 15) }, AreaHeroDollToggle.Lines.ToArray());
        Assert.AreEqual((425, 358, 23, 35), AreaHeroDollToggle.DamageRatingBox);
        Assert.AreEqual((609, 358, 23, 35), AreaHeroDollToggle.ArmourRatingBox);
        // The cover 341 at (361, 0) and the buttons 338 and 339 at (713, 37) and (751, 37).
        Assert.AreEqual(341, AreaHeroDollToggle.ArtNumbers[0]);
        Assert.AreEqual((361, 0), AreaHeroDollToggle.Cover);
        Assert.AreEqual((713, 37), AreaHeroDollToggle.DollButton);
        Assert.AreEqual((751, 37), AreaHeroDollToggle.BagButton);
        CollectionAssert.IsSubsetOf(AreaHeroDollToggle.ArtNumbers.ToArray(), AreaInventoryArtContent.Numbers.ToArray());
        CollectionAssert.AllItemsAreUnique(AreaInventoryArtContent.Numbers.ToArray());
    }

    [TestMethod]
    public void TheTogglesAreMadeInModes1To4Only()
    {
        Assert.IsFalse(AreaHeroDollToggle.TogglesShown(AreaInventoryMode.Own), "the own window draws the doll all the time");
        foreach (var mode in new[] { AreaInventoryMode.Barter, AreaInventoryMode.Loot, AreaInventoryMode.Steal, AreaInventoryMode.Contents })
            Assert.IsTrue(AreaHeroDollToggle.TogglesShown(mode), mode.ToString());
    }

    [TestMethod]
    public void TheButtonsAreHitBySizeOfTheirArt()
    {
        Assert.AreEqual(true, AreaHeroDollToggle.ToggleHit(713, 37, (30, 20), (30, 20)));
        Assert.AreEqual(true, AreaHeroDollToggle.ToggleHit(713 + 29, 37 + 19, (30, 20), (30, 20)));
        Assert.AreEqual(false, AreaHeroDollToggle.ToggleHit(751, 37, (30, 20), (30, 20)));
        Assert.IsNull(AreaHeroDollToggle.ToggleHit(713 + 30, 37, (30, 20), (30, 20)), "right of the doll button, left of the bag button");
        Assert.IsNull(AreaHeroDollToggle.ToggleHit(713, 36, (30, 20), (30, 20)));
        Assert.IsNull(AreaHeroDollToggle.ToggleHit(751 + 30, 37, (30, 20), (30, 20)));
    }

    [TestMethod]
    public void WithTheToggleSetASlotIsHitAndNoGridCellExists()
    {
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var (x, y) = Centre(slot);
            Assert.AreEqual(new AreaInventoryHit(null, slot), AreaHeroDollToggle.HitTest(x, y), slot.ToString());
        }
        // The grid's first cell (368, 8) is no cell now; the boxes are the same.
        Assert.AreEqual(AreaInventoryHit.None, AreaHeroDollToggle.HitTest(368 + 16, 8 + 16));
        Assert.AreEqual(AreaInventoryIcon.Drop, AreaHeroDollToggle.HitTest(AreaInventoryDrag.IconX + 10, AreaInventoryDrag.DropIconY + 10).Icon);
        Assert.AreEqual(AreaInventoryIcon.Use, AreaHeroDollToggle.HitTest(AreaInventoryDrag.IconX + 10, AreaInventoryDrag.UseIconY + 10).Icon);
        // The own window's slot rectangles (the left of the window) are not the toggle's.
        Assert.AreEqual(AreaInventoryHit.None, AreaHeroDollToggle.HitTest(160, 130));
    }

    [TestMethod]
    public void TheLootWindowHitsTheHeroSlotsOnlyWithTheToggleAndTheGridOnlyWithout()
    {
        var (x, y) = Centre(EquipmentSlot.Helmet);
        Assert.IsNull(AreaLootArtLayout.HitTest(x, y, 0, (0, 0)).Slot, "toggle clear: the doll is not shown, no slot is hit (the point is a cell of the grid)");
        Assert.AreEqual(new AreaLootHit(null, null, null, false, EquipmentSlot.Helmet), AreaLootArtLayout.HitTest(x, y, 0, (0, 0), heroDoll: true));
        Assert.AreEqual(new AreaLootHit(0, null, null, false), AreaLootArtLayout.HitTest(368 + 16, 8 + 16, 0, (0, 0)), "toggle clear: the grid's first cell");
        Assert.AreEqual(AreaLootHit.None, AreaLootArtLayout.HitTest(368 + 16, 8 + 16, 0, (0, 0), heroDoll: true), "toggle set: no grid");
        // The holder's side is the same either way.
        Assert.AreEqual(new AreaLootHit(null, null, 0, false), AreaLootArtLayout.HitTest(8, 136, 0, (0, 0), heroDoll: true));
        Assert.AreEqual(AreaInventoryIcon.Use, AreaLootArtLayout.HitTest(AreaInventoryDrag.IconX + 10, AreaInventoryDrag.UseIconY + 10, 0, (0, 0), heroDoll: true).Icon);
    }

    [TestMethod]
    public void APressOnAWornSlotLiftsTheItemWithItsSlotAndAnEmptySlotNothing()
    {
        var view = View(weapon: "dagger:1");
        var press = AreaLootArtLayout.Press(view, new AreaLootHit(null, null, null, false, EquipmentSlot.Helmet));
        Assert.AreEqual(new AreaInventoryHeld("helm:1", EquipmentSlot.Helmet, 0), press);
        Assert.IsFalse(press!.Partner, "the hero's own item, not the holder's");
        Assert.IsNull(AreaLootArtLayout.Press(view, new AreaLootHit(null, null, null, false, EquipmentSlot.Boots)), "an empty slot");
    }

    [TestMethod]
    public void AWornItemReleasedOnAnotherHeroSlotIsWornThereOnItsOwnSlotItGoesBack()
    {
        var held = new AreaInventoryHeld("helm:1", EquipmentSlot.Helmet, 0);
        foreach (var mode in new[] { AreaInventoryMode.Loot, AreaInventoryMode.Steal, AreaInventoryMode.Contents })
        {
            Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.Wear, held, Slot: EquipmentSlot.Medallion),
                AreaLootArtLayout.Release(held, new AreaLootHit(null, null, null, false, EquipmentSlot.Medallion), mode), mode.ToString());
            Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(held, new AreaLootHit(null, null, null, false, EquipmentSlot.Helmet), mode).Kind, mode + " own slot");
        }
        // A worn item released on a cell of the hero's grid is taken off into it (as in the own window).
        var off = AreaLootArtLayout.Release(held, new AreaLootHit(5, null, null, false), AreaInventoryMode.Loot);
        Assert.AreEqual(new AreaInventoryDrop(AreaInventoryDropKind.TakeOffTo, held, GridSlot: 5), off);
        // A grid item released on a hero slot is worn there.
        var grid = new AreaInventoryHeld("own:1", null, 0);
        Assert.AreEqual(AreaInventoryDropKind.Wear, AreaLootArtLayout.Release(grid, new AreaLootHit(null, null, null, false, EquipmentSlot.Armor), AreaInventoryMode.Loot).Kind);
    }

    [TestMethod]
    public void AWornItemReleasedOnTheHoldersGridGoesBackAndIsNoPlantNotReady()
    {
        var worn = new AreaInventoryHeld("helm:1", EquipmentSlot.Helmet, 0);
        var plain = new AreaInventoryHeld("own:1", null, 0);
        var onGrid = new AreaLootHit(null, null, 3, false);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(worn, onGrid, AreaInventoryMode.Steal).Kind);
        Assert.AreEqual(AreaInventoryDropKind.Plant, AreaLootArtLayout.Release(plain, onGrid, AreaInventoryMode.Steal).Kind, "a grid item is still a plant attempt");
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(worn, onGrid, AreaInventoryMode.Loot).Kind);
    }

    [TestMethod]
    public void TheHoldersItemOnAHeroSlotGoesBackAndARightReleaseOnAWornItemMovesNothing()
    {
        var partner = new AreaInventoryHeld("loot:1", null, 0) { Partner = true };
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaLootArtLayout.Release(partner, new AreaLootHit(null, null, null, false, EquipmentSlot.Helmet), AreaInventoryMode.Loot).Kind,
            "NOT READY: a holder's item worn on the hero's slot");
        var view = View();
        var onWorn = new AreaLootHit(null, null, null, false, EquipmentSlot.Helmet);
        Assert.IsNull(AreaLootArtLayout.RightRelease(view, onWorn, AreaInventoryMode.Steal), "NOT READY: a plant of a worn item");
        Assert.IsNull(AreaLootArtLayout.RightRelease(view, onWorn, AreaInventoryMode.Loot));
        Assert.AreEqual(AreaInventoryDropKind.Plant, AreaLootArtLayout.RightRelease(view, new AreaLootHit(0, null, null, false), AreaInventoryMode.Steal)?.Kind, "a grid item is still planted");
    }

    [TestMethod]
    public void TheBarterWindowHitsAndLiftsTheHeroSlotsWithTheToggleToo()
    {
        var (x, y) = Centre(EquipmentSlot.Helmet);
        Assert.IsNull(AreaBarterArtLayout.HitTest(x, y, 0, (0, 0)).Slot, "toggle clear: no slot (the point is a cell of the grid)");
        var hit = AreaBarterArtLayout.HitTest(x, y, 0, (0, 0), heroDoll: true);
        Assert.AreEqual(new AreaBarterHit(null, null, null, false, EquipmentSlot.Helmet), hit);
        Assert.AreEqual(AreaBarterHit.None, AreaBarterArtLayout.HitTest(368 + 16, 8 + 16, 0, (0, 0), heroDoll: true), "toggle set: no grid");
        Assert.AreEqual(new AreaBarterHit(0, null, null, false), AreaBarterArtLayout.HitTest(368 + 16, 8 + 16, 0, (0, 0)));
        var view = View();
        var held = AreaBarterArtLayout.Press(view, hit);
        Assert.AreEqual(new AreaInventoryHeld("helm:1", EquipmentSlot.Helmet, 0), held);
        // A worn item sold on the shown grid is a sale (the barter lines list the worn items); on a hero slot or anywhere else it goes back (NOT READY: the hero's moves in the window).
        Assert.AreEqual(AreaInventoryDropKind.Sell, AreaBarterArtLayout.Release(held!, new AreaBarterHit(null, null, 4, false)).Kind);
        Assert.AreEqual(AreaInventoryDropKind.PutBack, AreaBarterArtLayout.Release(held!, new AreaBarterHit(null, null, null, false, EquipmentSlot.Armor)).Kind);
    }
}
