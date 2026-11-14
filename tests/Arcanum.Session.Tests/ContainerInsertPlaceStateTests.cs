using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-AI-1753 (the insert 0x466640 writes the free place into the item's inventory location, field 0x5E, for a container's list too; ledger NL8): an item that enters a container through a barter sale or
// a script's transfer keeps the place it took, so a later removal of an earlier item moves no other item. Entities, prototypes and sizes are invented; the half-elf fixture's kit is three 1 x 1 items.
[TestClass]
public sealed class ContainerInsertPlaceStateTests
{
    private const string Merchant = "smith";
    private const string Chest = "chest";
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static SessionItemFacts Sized(SessionItemType type, int prototype, int width, int height, int location, int? quantity = null) =>
        new(new(type, prototype, 10, 20), 1, 0, location, quantity, []) { InventoryWidth = width, InventoryHeight = height, AmmoType = type == SessionItemType.Ammo ? 0 : null };

    // The chest holds two ten-column slabs (cells 0..19); its grid has 96 rows, so the first free place is 20.
    private static GameSessionOwner Open(params SessionSeedItem[] extra)
    {
        var slabs = Enumerable.Range(0, 2).Select(row => new SessionSeedItem("slab" + row, Sized(SessionItemType.Generic, 7000, 10, 1, row * 10), null)).ToArray();
        var seed = new SessionMapInventorySeed(
            [new(Merchant, SessionHolderKind.Npc, false, false, [], []), new(Chest, SessionHolderKind.Container, false, false, [], slabs.Concat(extra).Select(item => item.Entity).ToArray())],
            [.. slabs, .. extra]);
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        return owner;
    }

    // The chest's list after the slabs, as entity@location.
    private static string Tail(GameSessionOwner owner) =>
        string.Join(" ", owner.QueryHolderItems(Chest)!.Where(item => !item.Entity.StartsWith("slab", StringComparison.Ordinal)).Select(item => $"{item.Entity}@{item.Facts.InventoryLocation}"));

    private static void Sell(GameSessionOwner owner, string item) =>
        Assert.AreEqual(GameSessionDisposition.Changed, owner.BarterSell(owner.Version, Merchant, item, 1, 0, Bullets, Accept, container: Chest).Disposition);

    [TestMethod]
    public void AnItemSoldIntoTheContainerKeepsItsPlaceWhenAnEarlierOneLeaves()
    {
        using var owner = Open();
        Sell(owner, "kit:1");
        Sell(owner, "kit:2");
        Assert.AreEqual("kit:1@20 kit:2@21", Tail(owner), "each sale took the first free place and keeps it");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.BarterBuy(owner.Version, Merchant, "kit:1", 1, 0, Bullets, new(SessionItemType.Gold, 9056, 3200, 33200), Accept, Accept, container: Chest).Disposition);
        Assert.AreEqual("kit:2@21", Tail(owner), "the removal of the first item moves no other item");
        Sell(owner, "kit:3");
        Assert.AreEqual("kit:2@21 kit:3@20", Tail(owner), "the freed place is the first free one again");
    }

    [TestMethod]
    public void AnItemAScriptMovedIntoTheContainerTakesAPlace()
    {
        using var owner = Open();
        var first = owner.QueryHeroKit()[0];
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TransferNamedItem(owner.Version, null, Chest, first.Kind.Name, Accept).Disposition);
        Assert.AreEqual($"{first.Entity}@20", Tail(owner));
        Assert.AreEqual(1, owner.Capture().Inventory.HolderSlots.Single().Slots.Count);
    }

    [TestMethod]
    public void AGivenItemDoesNotShiftWhenAnEarlierUnplacedSeedItemIsTaken()
    {
        // A seed item without a location (-1) takes the first free cell in list order when the room is tested, so the sold item's place is the next one; taking the seed item must not move it.
        using var owner = Open(new SessionSeedItem("loose", Sized(SessionItemType.Weapon, 3000, 1, 1, -1), null));
        Assert.AreEqual("loose@-1", Tail(owner), "the seed item has no stored place");
        Sell(owner, "kit:1");
        Assert.AreEqual("loose@-1 kit:1@21", Tail(owner), "the room test gave the seed item cell 20");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.BarterBuy(owner.Version, Merchant, "loose", 1, 0, Bullets, new(SessionItemType.Gold, 9056, 3200, 33200), Accept, Accept, container: Chest).Disposition);
        Assert.AreEqual("kit:1@21", Tail(owner), "the sold item keeps its place");
    }

    [TestMethod]
    public void TheContainersPlacesAreSavedWithTheInventoryAndSurviveALoad()
    {
        using var owner = Open();
        Sell(owner, "kit:1");
        Sell(owner, "kit:2");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"holderSlots\":[{\"holder\":\"chest\",\"slots\":[{\"item\":\"kit:1\",\"slot\":20},{\"item\":\"kit:2\",\"slot\":21}]}]");
        using var restored = Restore(Decode(text));
        var seed = new SessionMapInventorySeed(
            [new(Merchant, SessionHolderKind.Npc, false, false, [], []), new(Chest, SessionHolderKind.Container, false, false, [], ["slab0", "slab1"])],
            [new("slab0", Sized(SessionItemType.Generic, 7000, 10, 1, 0), null), new("slab1", Sized(SessionItemType.Generic, 7000, 10, 1, 10), null)]);
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual("kit:1@20 kit:2@21", Tail(restored), "the restored session draws the same places");
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "the text is canonical");
    }

    [TestMethod]
    public void AnItemThatLeavesTheContainerLeavesNoSlotBehind()
    {
        using var owner = Open();
        Sell(owner, "kit:1");
        Assert.AreEqual(1, owner.Capture().Inventory.HolderSlots.Single().Slots.Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.BarterBuy(owner.Version, Merchant, "kit:1", 1, 0, Bullets, new(SessionItemType.Gold, 9056, 3200, 33200), Accept, Accept, container: Chest).Disposition);
        Assert.AreEqual(0, owner.Capture().Inventory.HolderSlots.Count);
    }

    [TestMethod]
    public void AnItemThatFindsNoFreePlaceStaysWithoutOne()
    {
        // A grid of 96 rows full of ten-column slabs: the sale is not refused by the host's check here, and no place is written.
        var slabs = Enumerable.Range(0, 96).Select(row => new SessionSeedItem("fill" + row, Sized(SessionItemType.Generic, 7000, 10, 1, row * 10), null)).ToArray();
        var seed = new SessionMapInventorySeed(
            [new(Merchant, SessionHolderKind.Npc, false, false, [], []), new(Chest, SessionHolderKind.Container, false, false, [], slabs.Select(item => item.Entity).ToArray())], [.. slabs]);
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Sell(owner, "kit:1");
        Assert.AreEqual(0, owner.Capture().Inventory.HolderSlots.Count);
    }
}
