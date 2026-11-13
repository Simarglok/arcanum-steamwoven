using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1654 (the room test of the insert check 0x466510 for a holder's grid, 0x4664C0): QueryHolderRoom over the seed's item locations, the arranged slots and the made items. Entities, prototypes and sizes are invented.
[TestClass]
public sealed class HolderRoomStateTests
{
    private const string Npc = "oid:guard";
    private static readonly CellCoord SwordCell = new(90, 200), ArrowsCell = new(91, 200);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionItemFacts Sized(SessionItemType type, int prototype, int width, int height, int location, int? quantity = null) =>
        new(new(type, prototype, 10, 20), 1, 0, location, quantity, []) { InventoryWidth = width, InventoryHeight = height, AmmoType = type == SessionItemType.Ammo ? 0 : null };

    private static SessionSeedItem Held(string entity, int width, int height, int location, SessionItemType type = SessionItemType.Generic, int prototype = 7000, int? quantity = null) =>
        new(entity, Sized(type, prototype, width, height, location, quantity), null);

    // The guard holds the given items; the loose sword (1 x 1, prototype 2901), a tall loose pike (1 x 3) and loose arrows (prototype 1500) lie on the ground.
    private static GameSessionOwner Open(params SessionSeedItem[] held)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        SessionSeedItem[] items =
        [
            .. held,
            new("sword", Sized(SessionItemType.Weapon, 2901, 1, 1, -1), SwordCell),
            new("pike", Sized(SessionItemType.Weapon, 2902, 1, 3, -1), new CellCoord(92, 200)),
            new("arrows", Sized(SessionItemType.Ammo, 1500, 1, 1, -1, 5), ArrowsCell),
        ];
        var seed = new SessionMapInventorySeed([new(Npc, SessionHolderKind.Npc, false, false, [], held.Select(item => item.Entity).ToArray())], items);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    private static SessionInventoryItem Ground(GameSessionOwner owner, string entity) => owner.QueryMapInventory(Exterior)!.Ground.Single(g => g.Item.Entity == entity).Item;

    // Slabs of ten columns fill whole rows from the top: rows of the grid taken.
    private static SessionSeedItem[] Rows(int rows) =>
        [.. Enumerable.Range(0, rows).Select(row => Held("slab" + row, 10, 1, row * 10))];

    [TestMethod]
    public void AnNpcWithFreeCellsHasRoomForAnItem()
    {
        using var owner = Open(Rows(11));
        Assert.IsTrue(owner.QueryHolderRoom(Npc, Ground(owner, "sword")), "the last row is free");
        Assert.IsTrue(owner.QueryHolderRoom(Npc, Ground(owner, "arrows")));
    }

    [TestMethod]
    public void AFullGridRefusesAnItemOfAnyKindExceptAnAmmunitionStackItJoins()
    {
        using var owner = Open([.. Rows(12), Held("quiver", 1, 1, 1000, SessionItemType.Ammo, 1500, 3)]);
        Assert.IsFalse(owner.QueryHolderRoom(Npc, Ground(owner, "sword")), "twelve full rows: no place for a cell");
        Assert.IsFalse(owner.QueryHolderRoom(Npc, Ground(owner, "pike")));
        Assert.IsTrue(owner.QueryHolderRoom(Npc, Ground(owner, "arrows")), "the quiver holds ammunition of prototype 1500: the pickup joins it and needs no place (0x462410, 0x462930)");
        Assert.IsFalse(owner.QueryHolderRoom(Npc, owner.QueryHolderItems(Npc)!.Single(listed => listed.Entity == "quiver")), "the finder skips the item itself: the quiver does not join its own stack");
    }

    [TestMethod]
    public void AnAmmunitionItemOfAnotherPrototypeNeedsAPlaceAsAnyOther()
    {
        using var owner = Open([.. Rows(12), Held("bolts", 1, 1, 1000, SessionItemType.Ammo, 1501, 3)]);
        Assert.IsFalse(owner.QueryHolderRoom(Npc, Ground(owner, "arrows")));
    }

    [TestMethod]
    public void TheItemsRectangleMustFitAsAWholeAndTheWornAndQuickSlotItemsTakeNoCell()
    {
        // Eleven full rows and a free last row with one cell taken: the 1 x 3 pike has no place, the sword has.
        using var owner = Open([.. Rows(11), Held("coin", 1, 1, 110)]);
        Assert.IsTrue(owner.QueryHolderRoom(Npc, Ground(owner, "sword")));
        Assert.IsFalse(owner.QueryHolderRoom(Npc, Ground(owner, "pike")));
        // The same grid with the slabs worn or on quick slots holds nothing in the grid.
        using var worn = Open(Held("slab0", 10, 12, 1003), Held("slab1", 10, 12, 2004));
        Assert.IsTrue(worn.QueryHolderRoom(Npc, Ground(worn, "pike")));
    }

    [TestMethod]
    public void AnItemWithNoLocationTakesAPlaceAndTheListIsFullerForIt()
    {
        // Eleven full rows and a last row filled by ten items the map gives no location (-1): they take the first free places in list order.
        using var owner = Open([.. Rows(11), .. Enumerable.Range(0, 10).Select(i => Held("made" + i, 1, 1, -1))]);
        Assert.IsFalse(owner.QueryHolderRoom(Npc, Ground(owner, "sword")), "the ten unplaced items filled the last row");
        using var spare = Open([.. Rows(11), .. Enumerable.Range(0, 9).Select(i => Held("made" + i, 1, 1, -1))]);
        Assert.IsTrue(spare.QueryHolderRoom(Npc, Ground(spare, "sword")), "one cell is left");
        Assert.IsFalse(spare.QueryHolderRoom(Npc, Ground(spare, "pike")));
    }

    [TestMethod]
    public void AnItemTheNpcPickedUpTakesAPlaceOfItsOwnForTheNextQuestion()
    {
        // The last row is free (ten cells); nine pickups of 1 x 1 items leave one cell, so the tenth is the last that fits.
        var picks = Enumerable.Range(0, 10).Select(i => Held("loose" + i, 1, 1, -1, SessionItemType.Weapon, 3000 + i) with { Cell = new CellCoord(100 + i, 200) }).ToArray();
        var seed = new SessionMapInventorySeed([new(Npc, SessionHolderKind.Npc, false, false, [], Rows(11).Select(item => item.Entity).ToArray())], [.. Rows(11), .. picks, new("extra", Sized(SessionItemType.Weapon, 3100, 1, 1, -1), new CellCoord(120, 200))]);
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        var extra = Ground(owner, "extra");
        for (var i = 0; i < 10; i++)
        {
            Assert.IsTrue(owner.QueryHolderRoom(Npc, extra), $"{i} items picked up: there is still a cell");
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "loose" + i, Accept).Disposition);
        }
        Assert.IsFalse(owner.QueryHolderRoom(Npc, extra), "the last row is full");
    }

    [TestMethod]
    public void AnArrangedGridIsReadAtTheSlotsTheArrangementGave()
    {
        // Sixty 1 x 1 items stand on the cells of a checkerboard: no free place is two cells tall. A loot window's open lays the list out again (ARC-RES-1410), and the room test follows the new slots.
        using var owner = Open([.. Enumerable.Range(0, 120).Where(cell => (cell / 10 + cell % 10) % 2 == 1).Select(cell => Held("dot" + cell, 1, 1, cell))]);
        Assert.IsTrue(owner.QueryHolderRoom(Npc, Ground(owner, "sword")), "a single cell is free");
        Assert.IsFalse(owner.QueryHolderRoom(Npc, Ground(owner, "pike")), "no 1 x 3 column is free on the map's data");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArrangeHolderGrid(owner.Version, Npc).Disposition);
        Assert.IsTrue(owner.QueryHolderRoom(Npc, Ground(owner, "pike")), "the arranged list takes six rows and leaves the rest free");
    }

    [TestMethod]
    public void AContainerHasNinetySixRowsAndAnUnknownHolderAnEmptyGrid()
    {
        var chest = new SessionSeedHolder("chest", SessionHolderKind.Container, false, false, [], Rows(12).Select(item => item.Entity).ToArray());
        var seed = new SessionMapInventorySeed([chest], [.. Rows(12), new("sword", Sized(SessionItemType.Weapon, 2901, 1, 1, -1), SwordCell)]);
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.IsTrue(owner.QueryHolderRoom("chest", Ground(owner, "sword")), "twelve rows are no more than an eighth of a container's grid");
        Assert.IsTrue(owner.QueryHolderRoom("oid:nobody", Ground(owner, "sword")));
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryHolderRoom("not an entity", Ground(owner, "sword")));
    }
}
