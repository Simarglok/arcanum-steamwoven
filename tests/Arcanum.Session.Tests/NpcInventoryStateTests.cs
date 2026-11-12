using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The session's store of NPC inventory changes (ARC-WORLD-055) over the synthetic seed of InventoryStateTests: virgil (alive, ring,
// sword, vest worn, ring-copy), corpse (dead), chest (a container). Entities and numbers are invented.
[TestClass]
public sealed class NpcInventoryStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind Gear = new(SessionItemType.Generic, 2320, 5, 6);

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    private static string Added(GameSessionOwner owner, string npc, SessionItemKind? kind = null)
    {
        var result = owner.AddItemToNpc(owner.Version, npc, kind ?? Gear, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        return result.Item!;
    }

    [TestMethod]
    public void A_made_item_joins_the_end_of_the_npc_list_and_numbers_are_shared()
    {
        using var owner = Attached();
        Assert.AreEqual(0, owner.QueryNpcInventory("virgil").Count);
        var checkedItems = new List<SessionInventoryItem>();
        var result = owner.AddItemToNpc(owner.Version, "virgil", Gear, item => { checkedItems.Add(item); return SessionInsertVerdict.Accepted; });
        Assert.AreEqual((GameSessionDisposition.Changed, "made:1", (SessionInsertVerdict?)SessionInsertVerdict.Accepted, 1L),
            (result.Disposition, result.Item, result.Verdict, owner.Version.Revision));
        Assert.AreEqual(new SessionInventoryItem("made:1", new SessionItemFacts(Gear, 0, 0, -1, null, [])), checkedItems.Single() with
            { Facts = checkedItems.Single().Facts with { ScriptPoints = [] } }, "the insert check sees the new item for the NPC");
        CollectionAssert.AreEqual(new[] { new SessionMadeItem("made:1", Gear) }, owner.QueryNpcInventory("virgil").ToArray());
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy", "made:1" }, List(owner, "virgil"), "after the seed items");
        Assert.AreEqual(Gear, owner.QueryMapInventory(Exterior)!.Holders[0].Items[^1].Facts.Kind);
        Assert.AreEqual("made:2", Added(owner, "corpse"));
        // A deleted made item keeps its number taken.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveItemFromNpc(owner.Version, "corpse", "made:2").Disposition);
        Assert.AreEqual("made:3", Added(owner, "corpse"));
        Assert.IsTrue(owner.IsItemDestroyed("made:2"));
        CollectionAssert.AreEqual(new[] { "note", "boots", "torn", "helmet", "made:3" }, List(owner, "corpse"));
        Assert.AreEqual(0, owner.QueryPcInventory().Count + owner.QueryMadeItems().Count, "nothing reached the PC");
    }

    [TestMethod]
    public void Adding_is_refused_before_any_effect()
    {
        using var owner = Attached(new GameSessionLimits(maxNpcItems: 1));
        void Refused(InventoryMutationResult result, GameSessionFailure failure, SessionInsertVerdict? verdict = null)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure, verdict), (result.Disposition, result.Failure, result.Verdict));
            Assert.AreEqual(0, owner.Version.Revision);
            Assert.AreEqual(0, owner.QueryNpcInventory("virgil").Count);
        }
        Refused(owner.AddItemToNpc(owner.Version, "bad id", Gear, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.AddItemToNpc(owner.Version, "made:9", Gear, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.AddItemToNpc(owner.Version, "virgil", Gear with { Type = (SessionItemType)99 }, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.AddItemToNpc(new GameSessionVersion(owner.Identity, 5), "virgil", Gear, Accept), GameSessionFailure.StaleVersion);
        Refused(owner.AddItemToNpc(owner.Version, "chest", Gear, Accept), GameSessionFailure.UnknownHolder);
        Refused(owner.AddItemToNpc(owner.Version, "stranger", Gear, Accept), GameSessionFailure.UnknownHolder);
        Refused(owner.AddItemToNpc(owner.Version, "virgil", Gear with { Type = SessionItemType.Gold }, Accept), GameSessionFailure.ItemTypeUnsupported);
        Refused(owner.AddItemToNpc(owner.Version, "virgil", Gear, _ => SessionInsertVerdict.NoRoom), GameSessionFailure.InsertRefused, SessionInsertVerdict.NoRoom);
        // A call back into the owner from the insert check makes the addition stale.
        var first = true;
        var stale = owner.AddItemToNpc(owner.Version, "virgil", Gear, _ =>
        {
            if (first) { first = false; Assert.AreEqual(GameSessionDisposition.Changed, owner.AddItemToNpc(owner.Version, "corpse", Gear, Accept).Disposition); }
            return SessionInsertVerdict.Accepted;
        });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (stale.Disposition, stale.Failure));
        Assert.AreEqual(0, owner.QueryNpcInventory("virgil").Count);
        // The store is full (one item).
        var full = owner.AddItemToNpc(owner.Version, "virgil", Gear, Accept);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, full.Failure);
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryNpcInventory("bad id"));
    }

    [TestMethod]
    public void A_seed_attached_from_the_insert_check_makes_the_addition_stale()
    {
        using var owner = Attached();
        var swapped = Seed() with { Holders = [.. Seed().Holders.Select(h => h.Entity == "virgil" ? h with { Kind = SessionHolderKind.Container } : h)] };
        var result = owner.AddItemToNpc(owner.Version, "virgil", Gear, _ =>
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, swapped).Disposition);
            return SessionInsertVerdict.Accepted;
        });
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.StaleVersion), (result.Disposition, result.Failure));
        Assert.AreEqual(0, owner.QueryNpcInventory("virgil").Count);
    }

    [TestMethod]
    public void One_npc_list_holds_at_most_512_items_with_its_seed_items()
    {
        using var owner = Create();
        var items = Enumerable.Range(0, SessionMapInventorySeed.MaxListLength - 1)
            .Select(i => new SessionSeedItem($"item-{i}", Facts(SessionItemType.Generic, 100 + i), null)).ToArray();
        var seed = new SessionMapInventorySeed([new("npc", SessionHolderKind.Npc, false, false, [], [.. items.Select(i => i.Entity)])], items);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AttachMapInventory(Exterior, seed).Disposition);
        Assert.AreEqual("made:1", Added(owner, "npc"));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.AddItemToNpc(owner.Version, "npc", Gear, Accept).Failure);
        // Deleting a seed item frees a place.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveItemFromNpc(owner.Version, "npc", "item-0").Disposition);
        Assert.AreEqual("made:2", Added(owner, "npc"));
        // A restored store whose NPC no longer fits with a seed's items is refused at attach.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text.Replace("\"destroyed\":[\"item-0\"]", "\"destroyed\":[]")));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, restored.AttachMapInventory(Exterior, seed).Failure);
    }

    [TestMethod]
    public void A_refused_negative_in_lays_an_added_item_on_the_pc_tile()
    {
        // ARC-WORLD-1131: the transfer that the insert check refuses ends with the item on the PC's tile (0x415BA0), not with the NPC and not in the made list.
        using var owner = Attached();
        var given = Added(owner, "virgil", Gear with { Name = 8888 });
        var refused = owner.GiveItemToPc(owner.Version, "virgil", 8888, _ => SessionInsertVerdict.TooHeavy);
        Assert.AreEqual((GameSessionDisposition.Changed, given, (SessionInsertVerdict?)SessionInsertVerdict.TooHeavy), (refused.Disposition, refused.Item, refused.Verdict));
        Assert.AreEqual(0, owner.QueryNpcInventory("virgil").Count);
        Assert.AreEqual(0, owner.QueryMadeItems().Count);
        var lies = owner.QueryPlacedItems().Single();
        Assert.AreEqual((given, SessionPlacedSource.Made), (lies.Entity, lies.Source));
    }

    [TestMethod]
    public void A_party_record_is_known_without_a_seed()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.AddItemToNpc(owner.Version, "oid:a", Gear, Accept).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "oid:a").Disposition);
        Assert.AreEqual("made:1", Added(owner, "oid:a"));
        Assert.AreEqual("made:2", Added(owner, "oid:a"), "an NPC that holds added items stays known");
        CollectionAssert.AreEqual(new[] { "made:1", "made:2" }, owner.QueryNpcInventory("oid:a").Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void Loot_and_negative_in_move_an_added_item_to_the_pc_made_list()
    {
        using var owner = Attached();
        var made = Added(owner, "corpse", Gear with { Name = 7777 });
        var looted = owner.LootItem(owner.Version, "corpse", made, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, made), (looted.Disposition, looted.Item));
        CollectionAssert.AreEqual(new[] { new SessionMadeItem(made, Gear with { Name = 7777 }) }, owner.QueryMadeItems().ToArray());
        Assert.AreEqual(0, owner.QueryNpcInventory("corpse").Count);
        CollectionAssert.DoesNotContain(List(owner, "corpse"), made);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.LootItem(owner.Version, "corpse", made, Accept).Failure, "taken once");
        // A living NPC's added item is not loot; the dialog's negative IN gives it by its name class.
        var given = Added(owner, "virgil", Gear with { Name = 8888 });
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, owner.LootItem(owner.Version, "virgil", given, Accept).Failure);
        var moved = owner.GiveItemToPc(owner.Version, "virgil", 8888, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, given), (moved.Disposition, moved.Item));
        CollectionAssert.AreEqual(new[] { made, given }, owner.QueryMadeItems().Select(item => item.Entity).ToArray());
        Assert.AreEqual(0, owner.QueryNpcInventory("virgil").Count);
        Assert.AreEqual(0, owner.QueryPcInventory().Count, "made items never become taken world items");
        // A heroless PC may hold made items, in entry order rather than number order; the list round-trips.
        var third = Added(owner, "corpse");
        var later = Added(owner, "corpse");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", later, Accept).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "corpse", third, Accept).Disposition);
        Assert.AreEqual(("made:3", "made:4"), (third, later));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        CollectionAssert.AreEqual(new[] { made, given, later, third }, restored.QueryMadeItems().Select(item => item.Entity).ToArray());
        Assert.IsNull(restored.QueryHero());
    }

    [TestMethod]
    public void Removal_deletes_a_seed_or_added_item_with_the_durability_rules()
    {
        using var owner = Attached();
        var made = Added(owner, "virgil");
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.RemoveItemFromNpc(owner.Version, "virgil", "bad id").Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.RemoveItemFromNpc(owner.Version, "made:1", "sword").Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.RemoveItemFromNpc(new GameSessionVersion(owner.Identity, 0), "virgil", "sword").Failure);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.RemoveItemFromNpc(owner.Version, "chest", "sword").Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.RemoveItemFromNpc(owner.Version, "corpse", "sword").Failure, "another NPC's item");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.RemoveItemFromNpc(owner.Version, "virgil", "apple").Failure, "a ground item");
        Assert.AreEqual(GameSessionFailure.WornItemUnsupported, owner.RemoveItemFromNpc(owner.Version, "virgil", "vest").Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
        var removed = owner.RemoveItemFromNpc(owner.Version, "virgil", "sword");
        Assert.AreEqual((GameSessionDisposition.Changed, "sword", revision + 1), (removed.Disposition, removed.Item, owner.Version.Revision));
        Assert.IsTrue(owner.IsItemDestroyed("sword"));
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.RemoveItemFromNpc(owner.Version, "virgil", "sword").Failure, "deleted once");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveItemFromNpc(owner.Version, "virgil", made).Disposition);
        CollectionAssert.AreEqual(new[] { "ring", "vest", "ring-copy" }, List(owner, "virgil"));
        Assert.AreEqual(0, owner.QueryNpcInventory("virgil").Count);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.LootItem(owner.Version, "corpse", "sword", Accept).Failure);
    }

    [TestMethod]
    public void Removal_needs_room_for_the_deletion_record()
    {
        using var owner = Attached(new GameSessionLimits(maxDurabilityRecords: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.RemoveItemFromNpc(owner.Version, "virgil", "sword").Disposition);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.RemoveItemFromNpc(owner.Version, "virgil", "ring").Failure);
        Assert.IsFalse(owner.IsItemDestroyed("ring"));
    }

    [TestMethod]
    public void The_store_round_trips_and_follows_the_npc_identity()
    {
        using var owner = Attached();
        var corpse = Added(owner, "corpse");
        Added(owner, "virgil");
        Added(owner, "virgil", Gear with { Prototype = 2321 });
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"npcs\":[{\"npc\":\"corpse\",\"container\":false,\"items\":[{\"entity\":\"" + corpse + "\",\"type\":\"Generic\",\"prototype\":2320,\"name\":5,\"description\":6,\"complexity\":0}]}," +
            "{\"npc\":\"virgil\",\"container\":false,\"items\":[{\"entity\":\"made:2\"");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        CollectionAssert.AreEqual(new[] { "made:2", "made:3" }, restored.QueryNpcInventory("virgil").Select(item => item.Entity).ToArray());
        // The changes are the NPC's, not a map's: they come back with whatever seed lists the NPC.
        Assert.IsNull(restored.QueryMapInventory(Exterior));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, Seed()).Disposition);
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy", "made:2", "made:3" }, List(restored, "virgil"));
        Assert.AreEqual("made:4", Added(restored, "virgil"), "the numbering continues after a restore");
        // A seed that lists an NPC holding added items as a container is another map's data.
        var swapped = Seed() with { Holders = [.. Seed().Holders.Select(h => h.Entity == "corpse" ? h with { Kind = SessionHolderKind.Container, Dead = false } : h)] };
        using var other = Restore(Decode(text));
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, other.AttachMapInventory(Exterior, swapped).Failure);
    }

    [TestMethod]
    public void Decode_and_restore_refuse_a_damaged_store()
    {
        using var owner = Attached();
        Added(owner, "corpse");
        Added(owner, "virgil");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Corpse = "{\"npc\":\"corpse\",\"container\":false,\"items\":[{\"entity\":\"made:1\",\"type\":\"Generic\",\"prototype\":2320,\"name\":5,\"description\":6,\"complexity\":0}]}";
        const string Virgil = "{\"npc\":\"virgil\",\"container\":false,\"items\":[{\"entity\":\"made:2\",\"type\":\"Generic\",\"prototype\":2320,\"name\":5,\"description\":6,\"complexity\":0}]}";
        StringAssert.Contains(text, Corpse + "," + Virgil);
        SnapshotDecodeFailure Decoded(string damaged) => GameSessionSnapshotCodec.TryDecode(damaged).Failure;
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, Decoded(text.Replace(Corpse + "," + Virgil, Virgil + "," + Corpse)), "NPC order");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, Decoded(text.Replace(Corpse, "{\"npc\":\"corpse\",\"container\":false,\"items\":[]}")), "an empty list");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, Decoded(text.Replace("\"made:2\"", "\"made:1\"")), "an item held twice");
        GameSessionFailure Restored(string damaged, GameSessionLimits? limits = null) =>
            GameSessionOwner.Restore(Decode(damaged), RestoreOptions(limits)).Failure;
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace("\"made:2\"", "\"crafted\"")), "not a made entity");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace("\"npc\":\"virgil\"", "\"npc\":\"made:7\"")), "a made entity as the NPC");
        Assert.AreEqual(GameSessionFailure.InvalidItem, Restored(text.Replace("\"made:2\",\"type\":\"Generic\"", "\"made:2\",\"type\":\"Gold\"")));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace("\"destroyed\":[]", "\"destroyed\":[\"made:2\"]")), "a deleted item");
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, Restored(text, new GameSessionLimits(maxNpcItems: 1)));
        Assert.AreEqual(GameSessionFailure.None, Restored(text, new GameSessionLimits(maxNpcItems: 2)));
    }

    // ARC-UI-1458: a made item an NPC holds has no map row; the session names its kind so a viewer can describe it (an Identify, a hover).
    [TestMethod]
    public void A_made_item_an_npc_holds_has_a_prototype_kind()
    {
        using var owner = Attached();
        var item = Added(owner, "virgil");
        Assert.AreEqual(Gear, owner.QueryPrototypeItemKind(item));
        Assert.IsNull(owner.QueryPrototypeItemKind("made:99"));
        Assert.IsNull(owner.QueryPrototypeItemKind("bad id"));
    }
}
