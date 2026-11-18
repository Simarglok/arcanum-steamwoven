using Arcanum.Simulation.Items;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-RES-1441 (schema 95): the slots the arrangement at a loot, steal or See Contents open gave the items of a seed holder (ARC-RES-1410, ledger XR16 (g)) are saved with the inventory section, so a restored
// session draws the same slots. Entities, prototypes and sizes are invented.
[TestClass]
public sealed class HolderSlotsSavedTests
{
    private static SessionSeedItem Item(string entity, int prototype, int width, int height, int location) =>
        new(entity, new SessionItemFacts(new(SessionItemType.Generic, prototype, 10, 20), 0, 0, location, null, []) { InventoryWidth = width, InventoryHeight = height }, null);

    // A chest and a shop keeper, each with a list the map's data places in an order of its own.
    private static SessionMapInventorySeed Seed() => new(
        [new("chest", SessionHolderKind.Container, false, false, [], ["crate", "apple", "plank"]),
         new("shop", SessionHolderKind.Npc, false, false, [], ["tall", "small"])],
        [Item("crate", 5001, 2, 3, 50), Item("apple", 5002, 1, 1, 0), Item("plank", 5003, 10, 1, 33), Item("tall", 5004, 1, 3, 40), Item("small", 5005, 1, 1, 3)]);

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static string Slots(GameSessionOwner owner, string holder) =>
        string.Join(" ", owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => $"{item.Entity}@{item.Facts.InventoryLocation}"));

    private static GameSessionOwner Reload(string text)
    {
        var restored = Restore(Decode(text));
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        return restored;
    }

    private static GameSessionFailure RestoreFailure(string text)
    {
        var decoded = GameSessionSnapshotCodec.TryDecode(text);
        Assert.IsNotNull(decoded.Snapshot, text);
        return GameSessionOwner.Restore(decoded.Snapshot!, RestoreOptions()).Failure;
    }

    [TestMethod]
    public void The_arranged_slots_of_a_container_and_an_NPC_survive_a_save_and_a_load()
    {
        using var owner = Owner();
        Assert.AreEqual("crate@50 apple@0 plank@33", Slots(owner, "chest"));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArrangeHolderGrid(owner.Version, "chest").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArrangeHolderGrid(owner.Version, "shop").Disposition);
        var arranged = (Slots(owner, "chest"), Slots(owner, "shop"));
        Assert.AreEqual(("crate@0 apple@2 plank@30", "tall@0 small@1"), arranged);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"holderSlots\":[{\"holder\":\"chest\",\"slots\":[{\"item\":\"apple\",\"slot\":2},{\"item\":\"crate\",\"slot\":0},{\"item\":\"plank\",\"slot\":30}]}," +
            "{\"holder\":\"shop\",\"slots\":[{\"item\":\"small\",\"slot\":1},{\"item\":\"tall\",\"slot\":0}]}]");
        using var restored = Reload(text);
        Assert.AreEqual(arranged, (Slots(restored, "chest"), Slots(restored, "shop")), "the restored session draws the arranged slots, not the map data's");
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "the text is canonical");
        // The next open of the restored session finds the list arranged already.
        Assert.AreEqual(GameSessionDisposition.NoChange, restored.ArrangeHolderGrid(restored.Version, "chest").Disposition);
    }

    [TestMethod]
    public void A_session_that_arranged_nothing_saves_an_empty_list()
    {
        using var owner = Owner();
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(owner.Capture()), ",\"itemFields\":[],\"holderSlots\":[],\"lastDrops\":[]}");
    }

    [TestMethod]
    public void A_damaged_record_is_refused()
    {
        using var owner = Owner();
        owner.ArrangeHolderGrid(owner.Version, "chest");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Saved = "\"holderSlots\":[{\"holder\":\"chest\",\"slots\":[{\"item\":\"apple\",\"slot\":2},{\"item\":\"crate\",\"slot\":0},{\"item\":\"plank\",\"slot\":30}]}]";
        StringAssert.Contains(text, Saved);
        Assert.AreEqual(GameSessionFailure.None, RestoreFailure(text));
        // Items out of order, a repeated item, a slot outside the grid, a holder row without items, an unknown field, holders out of order, a holder that is no entity: refused by the codec.
        foreach (var damaged in new[]
        {
            Saved.Replace("{\"item\":\"apple\",\"slot\":2},{\"item\":\"crate\",\"slot\":0}", "{\"item\":\"crate\",\"slot\":0},{\"item\":\"apple\",\"slot\":2}"),
            Saved.Replace("{\"item\":\"crate\",\"slot\":0}", "{\"item\":\"apple\",\"slot\":0}"),
            Saved.Replace("\"slot\":30", "\"slot\":960"),
            Saved.Replace("\"slot\":30", "\"slot\":-1"),
            Saved.Replace("\"slots\":[{\"item\":\"apple\",\"slot\":2},{\"item\":\"crate\",\"slot\":0},{\"item\":\"plank\",\"slot\":30}]", "\"slots\":[]"),
            Saved.Replace("\"holder\":\"chest\"", "\"holder\":\"chest\",\"extra\":1"),
            Saved.Replace("]}]", "]},{\"holder\":\"chest\",\"slots\":[{\"item\":\"zed\",\"slot\":1}]}]"),
            Saved.Replace("\"holder\":\"chest\"", "\"holder\":\"no way\""),
        })
        {
            Assert.AreNotEqual(SnapshotDecodeFailure.None, GameSessionSnapshotCodec.TryDecode(text.Replace(Saved, damaged)).Failure, damaged);
        }
    }

    [TestMethod]
    public void The_ninety_four_to_ninety_five_step_gives_the_inventory_an_empty_list_and_refuses_a_save_that_has_one()
    {
        using var owner = Owner();
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var older = SnapshotUpgradeTests.Schema94(current);
        StringAssert.Contains(older, "\"schemaVersion\":94,\"world\"");
        Assert.IsFalse(older.Contains("holderSlots", StringComparison.Ordinal));
        var upgraded = Decode(older);
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.SchemaVersion);
        Assert.AreEqual(0, upgraded.Inventory.HolderSlots.Count);
        Assert.AreEqual(current, GameSessionSnapshotCodec.Encode(upgraded));
        var damaged = GameSessionSnapshotCodec.TryDecode(older.Replace(",\"itemFields\":[]}", ",\"itemFields\":[],\"holderSlots\":[]}"));
        Assert.AreEqual((SnapshotDecodeFailure.UnknownField, "$.inventory.holderSlots"), (damaged.Failure, damaged.Path));
    }

    [TestMethod]
    public void A_slot_of_a_deleted_item_is_forgotten_so_the_save_still_loads_and_an_emptied_row_goes()
    {
        using var owner = Owner();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArrangeHolderGrid(owner.Version, "shop").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ArrangeHolderGrid(owner.Version, "chest").Disposition);
        var gone = owner.DestroyItem(owner.Version, "tall", new ItemCondition(10, 0, 0, false));
        Assert.AreEqual(GameSessionDisposition.Changed, gone.Disposition, gone.Failure.ToString());
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        Assert.IsFalse(text.Contains("\"item\":\"tall\"", StringComparison.Ordinal), "the deleted item keeps no slot");
        StringAssert.Contains(text, "{\"holder\":\"shop\",\"slots\":[{\"item\":\"small\",\"slot\":1}]}");
        using var restored = Reload(text);
        Assert.AreEqual(GameSessionDisposition.Changed, restored.DestroyItem(restored.Version, "small", new ItemCondition(10, 0, 0, false)).Disposition);
        Assert.IsFalse(GameSessionSnapshotCodec.Encode(restored.Capture()).Contains("\"holder\":\"shop\"", StringComparison.Ordinal), "a holder row without items is not kept");
    }

    // ARC-RES-1466: a restored save may hold a slot that lies in a container's grid but not in an NPC's; the seed names the holder's kind, so the attach refuses it.
    [TestMethod]
    public void A_saved_slot_outside_the_grid_of_the_holders_kind_is_refused_when_the_seed_is_attached()
    {
        using var owner = Owner();
        owner.ArrangeHolderGrid(owner.Version, "chest");
        owner.ArrangeHolderGrid(owner.Version, "shop");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Shop = "{\"item\":\"small\",\"slot\":1},{\"item\":\"tall\",\"slot\":0}";
        const string Chest = "{\"item\":\"plank\",\"slot\":30}";
        StringAssert.Contains(text, Shop);
        StringAssert.Contains(text, Chest);
        GameSessionFailure Attach(string changed)
        {
            using var restored = Restore(Decode(changed));
            return restored.AttachMapInventory(Exterior, Seed()).Failure;
        }
        Assert.AreEqual(GameSessionFailure.None, Attach(text));
        // The last cell of each grid is fine, the next one is outside it.
        Assert.AreEqual(GameSessionFailure.None, Attach(text.Replace(Shop, Shop.Replace("\"slot\":1", "\"slot\":119"))));
        Assert.AreEqual(GameSessionFailure.None, Attach(text.Replace(Chest, Chest.Replace("\"slot\":30", "\"slot\":959"))));
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, Attach(text.Replace(Shop, Shop.Replace("\"slot\":1", "\"slot\":120"))));
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, Attach(text.Replace(Shop, Shop.Replace("\"slot\":1", "\"slot\":959"))));
        // A slot saved for a holder the attached seed does not list is not checked (the seed names no kind for it).
        using var damaged = Restore(Decode(text.Replace(Shop, Shop.Replace("\"slot\":1", "\"slot\":120"))));
        var chestOnly = new SessionMapInventorySeed([Seed().Holders[0]], Seed().Items.Take(3).ToArray());
        Assert.AreEqual(GameSessionFailure.None, damaged.AttachMapInventory(Exterior, chestOnly).Failure);
        var before = Slots(damaged, "chest");
        var holderCount = damaged.QueryMapInventory(Exterior)!.Holders.Count;
        // A refused attach changes nothing: the earlier seed stays.
        var refused = damaged.AttachMapInventory(Exterior, Seed());
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InventoryMismatch), (refused.Disposition, refused.Failure));
        Assert.AreEqual((before, holderCount), (Slots(damaged, "chest"), damaged.QueryMapInventory(Exterior)!.Holders.Count));
    }

    // ARC-RES-1466: a created critter is an NPC holder of the seed the session adds itself, so its saved slots are checked as an NPC's.
    [TestMethod]
    public void A_saved_slot_outside_the_grid_of_a_created_critter_is_refused_when_the_seed_is_attached()
    {
        using var owner = Owner();
        var made = owner.CreateObject(owner.Version, SessionCreatedKind.Critter, 17087, Exterior, new(100, 200)).Object!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        const string Empty = "\"holderSlots\":[]";
        StringAssert.Contains(text, Empty);
        string Row(int slot) => $"\"holderSlots\":[{{\"holder\":\"{made.Entity}\",\"slots\":[{{\"item\":\"made:1\",\"slot\":{slot}}}]}}]";
        GameSessionFailure Attach(int slot)
        {
            using var restored = Restore(Decode(text.Replace(Empty, Row(slot))));
            return restored.AttachMapInventory(Exterior, Seed()).Failure;
        }
        Assert.AreEqual(GameSessionFailure.None, Attach(119));
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, Attach(120));
    }
}
