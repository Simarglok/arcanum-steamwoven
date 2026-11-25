using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-RES-1410 (ledger XR16 (g), IW39): the arrangement the original's inventory window writes at its open on a container, an NPC or a junk pile
// (0x5727B0 -> 0x4670A0, layout 0). Entities, prototypes and sizes are invented.
[TestClass]
public sealed class HolderGridArrangeStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionSeedItem Item(string entity, int prototype, int width, int height, int location) =>
        new(entity, new SessionItemFacts(new(SessionItemType.Generic, prototype, 10, 20), 0, 0, location, null, []) { InventoryWidth = width, InventoryHeight = height }, null);

    private static GameSessionOwner Owner(SessionMapInventorySeed seed)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    private static string Slots(GameSessionOwner owner, string holder) =>
        string.Join(" ", owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => $"{item.Entity}@{item.Facts.InventoryLocation}"));

    private static SessionMapInventorySeed Chest(params SessionSeedItem[] items) =>
        new([new("chest", SessionHolderKind.Container, false, false, [], items.Select(item => item.Entity).ToArray())], items);

    [TestMethod]
    public void AContainersListIsSortedTallestFirstAndTheSlotsAreWritten()
    {
        // The map's data places the items in an order of its own; the open lays them out row by row, tallest then widest first.
        using var owner = Owner(Chest(Item("crate", 5001, 2, 3, 50), Item("apple", 5002, 1, 1, 0), Item("plank", 5003, 10, 1, 33)));
        var revision = owner.Version.Revision;
        var done = owner.ArrangeHolderGrid(owner.Version, "chest");
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, false, 3), (done.Disposition, done.Failure, done.NoRoom, done.Moved));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision");
        Assert.AreEqual("crate@0 apple@2 plank@30", Slots(owner, "chest"), "the crate's 2 x 3 block first, the plank needs a whole row, the apple fills the first free cell");
        var again = owner.ArrangeHolderGrid(owner.Version, "chest");
        Assert.AreEqual((GameSessionDisposition.NoChange, false, 0), (again.Disposition, again.NoRoom, again.Moved), "an arranged list is already where the function puts it");
        Assert.AreEqual(revision + 1, owner.Version.Revision, "no revision for no change");
    }

    [TestMethod]
    public void AColumnByColumnLayoutAndAnUnknownLayoutOfTheBarterButton()
    {
        // ARC-UI-3198 (IW38): the barter button arranges the follower's list in the layout of its press, which alternates with the hero's.
        var guard = new SessionSeedHolder("guard", SessionHolderKind.Npc, false, false, [], ["a", "b", "c"]);
        using var owner = Owner(new([guard], [Item("a", 5001, 1, 3, 40), Item("b", 5002, 1, 1, 77), Item("c", 5003, 1, 1, 66)]));
        var bad = owner.ArrangeHolderGrid(owner.Version, "guard", (InventoryArrangeLayout)99);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InvalidItem), (bad.Disposition, bad.Failure));
        var done = owner.ArrangeHolderGrid(owner.Version, "guard", InventoryArrangeLayout.Vertical);
        Assert.AreEqual(GameSessionDisposition.Changed, done.Disposition);
        Assert.AreEqual("a@0 b@30 c@40", Slots(owner, "guard"), "column by column: the free cells are scanned down the columns, so the others stack under the tall item");
        var back = owner.ArrangeHolderGrid(owner.Version, "guard", InventoryArrangeLayout.Horizontal);
        Assert.AreEqual(GameSessionDisposition.Changed, back.Disposition);
        Assert.AreEqual("a@0 b@1 c@2", Slots(owner, "guard"), "row by row again");
    }

    [TestMethod]
    public void ATakenItemsCellClosesUpAtTheNextOpenAndNotBefore()
    {
        using var owner = Owner(Chest(Item("tall", 5001, 1, 3, 40), Item("mid", 5002, 1, 2, 7), Item("small", 5003, 1, 1, 3)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArrangeHolderGrid(owner.Version, "chest").Disposition);
        Assert.AreEqual("tall@0 mid@1 small@2", Slots(owner, "chest"));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "chest", "tall", Accept).Disposition);
        Assert.AreEqual("mid@1 small@2", Slots(owner, "chest"), "while the window stays open the others keep their cells (the taken one leaves a hole)");
        var next = owner.ArrangeHolderGrid(owner.Version, "chest");
        Assert.AreEqual((GameSessionDisposition.Changed, 2), (next.Disposition, next.Moved));
        Assert.AreEqual("mid@0 small@1", Slots(owner, "chest"), "the next open lays the rest out again");
    }

    [TestMethod]
    public void WornAndQuickSlotItemsStayOutAndAnNpcsGridHasTwelveRows()
    {
        // The leave-out step swaps each worn or quick-slot item with the list's last one, so the rest is laid out in a scrambled order (IW39).
        var guard = new SessionSeedHolder("guard", SessionHolderKind.Npc, false, false, [], ["sword", "potion", "rock", "ring"]);
        using var owner = Owner(new([guard], [Item("sword", 5001, 1, 3, 1000), Item("potion", 5002, 1, 1, 2000), Item("rock", 5003, 1, 1, 77), Item("ring", 5004, 1, 1, 5)]));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArrangeHolderGrid(owner.Version, "guard").Disposition);
        Assert.AreEqual("sword@1000 potion@2000 rock@1 ring@0", Slots(owner, "guard"), "the worn sword and the quick-slot potion keep their locations; ring and rock swapped places");
    }

    [TestMethod]
    public void AListThatDoesNotFitWritesNothing()
    {
        // 12 rows for a critter: two 10 x 7 slabs do not fit one grid, so the original stops and writes nothing.
        var guard = new SessionSeedHolder("guard", SessionHolderKind.Npc, false, false, [], ["slab1", "slab2", "coin"]);
        using var owner = Owner(new([guard], [Item("slab1", 5001, 10, 7, 0), Item("slab2", 5002, 10, 7, 90), Item("coin", 5003, 1, 1, 77)]));
        var revision = owner.Version.Revision;
        var done = owner.ArrangeHolderGrid(owner.Version, "guard");
        Assert.AreEqual((GameSessionDisposition.NoChange, true, 0), (done.Disposition, done.NoRoom, done.Moved));
        Assert.AreEqual("slab1@0 slab2@90 coin@77", Slots(owner, "guard"), "the map's locations stand");
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void AContainerHasNinetySixRowsSoTheSameListFits()
    {
        using var owner = Owner(Chest(Item("slab1", 5001, 10, 7, 0), Item("slab2", 5002, 10, 7, 90), Item("coin", 5003, 1, 1, 77)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArrangeHolderGrid(owner.Version, "chest").Disposition);
        Assert.AreEqual("slab1@0 slab2@70 coin@140", Slots(owner, "chest"));
    }

    [TestMethod]
    public void ASessionJunkPileIsArrangedInItsRecord()
    {
        var apple = Item("apple", 3600, 2, 1, 0) with { Cell = new CellCoord(90, 200) };
        using var owner = Owner(new([], [apple]));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        var there = new CellCoord(120, 210);
        owner.PlaceHeroItem("kit:2", there);
        owner.PlaceHeroItem("apple", there);
        owner.PlaceHeroItem("kit:3", there);
        CollectionAssert.AreEqual(new[] { ("kit:2", 0), ("apple", 1), ("kit:3", 3) }, owner.QueryJunkPiles().Single().Items.Select(item => (item.Entity, item.Slot)).ToArray());
        var done = owner.ArrangeHolderGrid(owner.Version, "junk:1");
        Assert.AreEqual((GameSessionDisposition.Changed, 2), (done.Disposition, done.Moved), "the third item's slot did not change");
        CollectionAssert.AreEqual(new[] { ("kit:2", 2), ("apple", 0), ("kit:3", 3) }, owner.QueryJunkPiles().Single().Items.Select(item => (item.Entity, item.Slot)).ToArray(),
            "the pile's entries keep their order, the apple (two cells wide) takes the first slot");
        CollectionAssert.AreEqual(new[] { "kit:2", "apple", "kit:3" }, owner.QueryMapInventory(Exterior)!.JunkPiles.Single().Items.Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void RefusedForAnUnknownHolderAndAStaleVersion()
    {
        using var owner = Owner(Chest(Item("apple", 5002, 1, 1, 3)));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.UnknownHolder), Pair(owner.ArrangeHolderGrid(owner.Version, "nobody")));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.UnknownHolder), Pair(owner.ArrangeHolderGrid(owner.Version, "")));
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), Pair(owner.ArrangeHolderGrid(new(owner.Identity, 99), "chest")));
        Assert.AreEqual("apple@3", Slots(owner, "chest"), "nothing changed");
    }

    private static (GameSessionDisposition, GameSessionFailure) Pair(SessionGridArrangement result) => (result.Disposition, result.Failure);
}
