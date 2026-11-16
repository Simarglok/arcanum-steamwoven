using Arcanum.Scripting.Dialog;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-2408 (ledger IV32): the readers that match an item by its name number follow a script's rename (the item's own field, SessionItemName). Over a synthetic seed whose items are mobile objects (oid: entities)
// held by an NPC; entities and numbers are invented.
[TestClass]
public sealed class ItemNameReadersTests
{
    private static readonly DialogParticipantId Pc = new(1), Npc = new(2);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private const string Ring = "oid:ring", Sword = "oid:sword", Cup = "oid:cup";

    private static SessionMapInventorySeed Seed() => new(
        [new("virgil", SessionHolderKind.Npc, false, false, [], [Ring, Sword]), new("priest", SessionHolderKind.Npc, false, false, [], [Cup])],
        [new SessionSeedItem(Ring, Facts(SessionItemType.Armor, 2804), null), new SessionSeedItem(Sword, Facts(SessionItemType.Weapon, 2000, location: 1), null), new SessionSeedItem(Cup, Facts(SessionItemType.Generic, 5650), null)]);

    private static GameSessionOwner Owner()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static void Rename(GameSessionOwner owner, string entity, int number) =>
        Assert.AreNotEqual(GameSessionDisposition.Rejected, owner.ApplyChanges(owner.Version, null, [new(Exterior, SessionItemName.Key(entity), number)]).Disposition);

    private static SessionDialogItemHoldPort Port(GameSessionOwner owner) => new(owner, Pc, participant => participant == Npc ? "virgil" : null, _ => null);

    [TestMethod]
    public void The_item_name_is_the_data_number_until_a_rename_wrote_the_items_field()
    {
        using var owner = Owner();
        Assert.AreEqual(2804, owner.QueryItemName(Ring, 2804));
        Assert.AreEqual(5650, owner.QueryItemName(Cup, 5650), "another item: its own number");
        Rename(owner, Ring, 2805);
        Assert.AreEqual(2805, owner.QueryItemName(Ring, 2804), "the renamed number");
        Assert.AreEqual(2000, owner.QueryItemName(Sword, 2000), "a rename of one item leaves the others");
        Assert.AreEqual(7, owner.QueryItemName("made:1", 7), "a made item has no record");
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.QueryItemName(null!, 1));
    }

    [TestMethod]
    public void The_named_item_finder_follows_the_rename_for_a_holder_and_for_the_hero()
    {
        using var owner = Owner();
        Assert.AreEqual(Ring, owner.QueryNamedItem("virgil", 2804)!.Entity);
        Rename(owner, Ring, 2805);
        Assert.AreEqual(Ring, owner.QueryNamedItem("virgil", 2805)!.Entity, "found by the new number");
        Assert.IsNull(owner.QueryNamedItem("virgil", 2804), "and no longer by the old one");
        // The item keeps its field when it moves to the hero's pack.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToPc(owner.Version, "virgil", 2805, Accept).Disposition);
        Assert.AreEqual(Ring, owner.QueryNamedItem(null, 2805)!.Entity);
        Assert.IsNull(owner.QueryNamedItem(null, 2804));
    }

    [TestMethod]
    public void The_dialog_item_tests_and_the_take_follow_the_rename()
    {
        using var owner = Owner();
        Rename(owner, Ring, 2805);
        var port = Port(owner);
        Assert.IsTrue(port.NpcHolds(new(Npc, Pc, 2805)), "the npc holds the renamed ring by its new number");
        Assert.IsFalse(port.NpcHolds(new(Npc, Pc, 2804)), "not by the old one");
        Assert.IsTrue(port.NpcHolds(new(Npc, Pc, 2000)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToPc(owner.Version, "virgil", 2805, Accept).Disposition);
        Assert.IsTrue(port.PartyHolds(new(Npc, Pc, 2805)), "the hero's pack lists it by the new number");
        Assert.IsFalse(port.PartyHolds(new(Npc, Pc, 2804)));
    }
}
