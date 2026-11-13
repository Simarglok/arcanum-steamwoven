using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1698 (the insert 0x466640 writes the free place the room test found into the item's inventory location, field 0x5E; ledger NL8): an item that enters an NPC's list through a pickup, a hero's give or a
// script keeps the place it took, so a later removal of an earlier item moves no other item. Entities, prototypes and sizes are invented; the half-elf fixture's kit is three 1 x 1 items.
[TestClass]
public sealed class HolderInsertPlaceStateTests
{
    private const string Npc = "oid:guard";
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static SessionItemFacts Sized(SessionItemType type, int prototype, int width, int height, int location, int? quantity = null) =>
        new(new(type, prototype, 10, 20), 1, 0, location, quantity, []) { InventoryWidth = width, InventoryHeight = height, AmmoType = type == SessionItemType.Ammo ? 0 : null };

    // Slabs of ten columns fill whole rows from the top: eleven rows of the twelve are taken, the last row's ten cells are free.
    private static SessionSeedItem[] Rows(int rows) =>
        [.. Enumerable.Range(0, rows).Select(row => new SessionSeedItem("slab" + row, Sized(SessionItemType.Generic, 7000, 10, 1, row * 10), null))];

    // The guard holds eleven slabs; ten loose 1 x 1 weapons and the loose arrows (prototype 1500) lie on the ground.
    private static (GameSessionOwner Owner, SessionMapInventorySeed Seed) Open(params SessionSeedItem[] extraHeld)
    {
        var slabs = Rows(11);
        var loose = Enumerable.Range(0, 10).Select(i => new SessionSeedItem("loose" + i, Sized(SessionItemType.Weapon, 3000 + i, 1, 1, -1), new CellCoord(100 + i, 200))).ToArray();
        var arrows = new SessionSeedItem("arrows", Sized(SessionItemType.Ammo, 1500, 1, 1, -1, 5), new CellCoord(120, 200));
        var seed = new SessionMapInventorySeed([new(Npc, SessionHolderKind.Npc, false, false, [], slabs.Concat(extraHeld).Select(item => item.Entity).ToArray())], [.. slabs, .. extraHeld, .. loose, arrows]);
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return (owner, seed);
    }

    // The tail of the guard's list: every item after the slabs, as entity@location.
    private static string Tail(GameSessionOwner owner) =>
        string.Join(" ", owner.QueryHolderItems(Npc)!.Where(item => !item.Entity.StartsWith("slab", StringComparison.Ordinal)).Select(item => $"{item.Entity}@{item.Facts.InventoryLocation}"));

    [TestMethod]
    public void AnItemTheNpcPickedUpKeepsItsPlaceWhenAnEarlierOneLeaves()
    {
        var (owner, _) = Open();
        using var _o = owner;
        foreach (var number in new[] { 0, 1, 2 }) Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "loose" + number, Accept).Disposition);
        Assert.AreEqual("loose0@110 loose1@111 loose2@112", Tail(owner), "each pickup took the first free place of the last row and keeps it");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, Npc, "loose0", Accept).Disposition);
        Assert.AreEqual("loose1@111 loose2@112", Tail(owner), "the removal of the first item moves no other item");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "loose3", Accept).Disposition);
        Assert.AreEqual("loose1@111 loose2@112 loose3@110", Tail(owner), "the freed place is the first free one again");
    }

    [TestMethod]
    public void AnItemTheHeroGaveTakesAPlaceToo()
    {
        var (owner, _) = Open();
        using var _o = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, Npc, "kit:1", Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, Npc, "kit:2", Accept).Disposition);
        Assert.AreEqual("kit:1@110 kit:2@111", Tail(owner));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TakeItemFromNpc(owner.Version, Npc, "kit:1", Accept).Disposition);
        Assert.AreEqual("kit:2@111", Tail(owner), "the item the hero took back leaves no gap-filling shift behind");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, Npc, "kit:3", Accept).Disposition);
        Assert.AreEqual("kit:2@111 kit:3@110", Tail(owner));
    }

    [TestMethod]
    public void AnItemAScriptCreatedInTheListTakesAPlace()
    {
        var (owner, _) = Open();
        using var _o = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "loose0", Accept).Disposition);
        var made = owner.AddItemToNpc(owner.Version, Npc, new(SessionItemType.Food, 1000, 1, 30001), Accept).Item!;
        Assert.AreEqual("loose0@110 " + made + "@111", Tail(owner));
    }

    [TestMethod]
    public void AnAmmunitionPickupThatJoinsAStackWritesNoPlace()
    {
        var (owner, _) = Open(new SessionSeedItem("quiver", Sized(SessionItemType.Ammo, 1500, 1, 1, 1000, 3), null));
        using var _o = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "arrows", Accept).Disposition);
        Assert.AreEqual(0, owner.Capture().Inventory.HolderSlots.Count, "the units joined the quiver: no item entered the list");
    }

    [TestMethod]
    public void ThePlacesAreSavedWithTheInventoryAndSurviveALoad()
    {
        var (owner, seed) = Open();
        using var _o = owner;
        foreach (var number in new[] { 0, 1 }) owner.PickUpItemByNpc(owner.Version, Npc, "loose" + number, Accept);
        owner.TakeItemFromNpc(owner.Version, Npc, "loose0", Accept);
        owner.GiveItemToNpc(owner.Version, Npc, "kit:1", Accept);
        Assert.AreEqual("loose1@111 kit:1@110", Tail(owner));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"holderSlots\":[{\"holder\":\"oid:guard\",\"slots\":[{\"item\":\"kit:1\",\"slot\":110},{\"item\":\"loose1\",\"slot\":111}]}]");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual("loose1@111 kit:1@110", Tail(restored), "the restored session draws the same places");
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "the text is canonical");
    }

    [TestMethod]
    public void AnItemThatLeavesTheListLeavesNoSlotBehind()
    {
        var (owner, _) = Open();
        using var _o = owner;
        owner.GiveItemToNpc(owner.Version, Npc, "kit:1", Accept);
        Assert.AreEqual(1, owner.Capture().Inventory.HolderSlots.Single().Slots.Count);
        owner.TakeItemFromNpc(owner.Version, Npc, "kit:1", Accept);
        Assert.AreEqual(0, owner.Capture().Inventory.HolderSlots.Count, "the save keeps no slot of an item that is no longer in the list");
    }

    [TestMethod]
    public void AnItemThatFindsNoFreePlaceStaysWithoutOne()
    {
        // The host's check says yes although the grid is full (a script's insert): no place is written, the item keeps none (-1).
        var (owner, _) = Open(Enumerable.Range(0, 10).Select(i => new SessionSeedItem("fill" + i, Sized(SessionItemType.Generic, 7001, 1, 1, 110 + i), null)).ToArray());
        using var _o = owner;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItemByNpc(owner.Version, Npc, "loose0", Accept).Disposition);
        Assert.AreEqual(0, owner.Capture().Inventory.HolderSlots.Count);
    }
}
