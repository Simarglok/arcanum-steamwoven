using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-SCRIPT-1279 (ledger WO2, WO6, WO7, WO8): the session's side of transfer item (41), set written start (94), destroy item named (126) and toggle item inventory display (127) over the synthetic seed of
// InventoryStateTests (virgil: ring 2804, sword 2000, vest 2800 worn, ring-copy 2804; corpse: note, boots, torn, helmet; an empty chest) and the half-elf hero (kit:1..3). Entities and numbers are invented.
[TestClass]
public sealed class ScriptItemStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static string[] List(GameSessionOwner owner, string holder) => owner.QueryHolderItems(holder)!.Select(item => item.Entity).ToArray();

    [TestMethod]
    public void The_finder_takes_the_first_item_of_the_holders_own_list_with_the_name()
    {
        using var owner = Hero();
        Assert.AreEqual("ring", owner.QueryNamedItem("virgil", 2804)!.Entity, "the first of two with the name");
        Assert.AreEqual("vest", owner.QueryNamedItem("virgil", 2800)!.Entity, "a worn item is in the list too");
        Assert.IsTrue(owner.QueryNamedItem("virgil", 2800)!.Worn);
        Assert.IsNull(owner.QueryNamedItem("virgil", 9999), "no such name");
        Assert.IsNull(owner.QueryNamedItem("chest", 2804), "an empty container holds nothing");
        Assert.IsNull(owner.QueryNamedItem("nobody", 2804), "no attached map lists the holder");
        Assert.IsNull(owner.QueryNamedItem("corpse", 2804), "another holder's item is not in this list");
        var kit = owner.QueryHeroKit();
        Assert.AreEqual(kit[1].Entity, owner.QueryNamedItem(null, kit[1].Kind.Name)!.Entity, "the hero's own list, its kit first");
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryNamedItem("bad id", 1));
        using var heroless = Attached();
        Assert.IsNull(heroless.QueryNamedItem(null, 2804), "no hero, no list");
    }

    [TestMethod]
    public void Destroy_item_named_deletes_the_first_match_as_every_deletion_does()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var destroyed = owner.DestroyNamedItem(owner.Version, "virgil", 2804);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "ring", revision + 1), (destroyed.Disposition, destroyed.Failure, destroyed.Item, destroyed.Version.Revision));
        Assert.IsTrue(owner.IsItemDestroyed("ring"));
        CollectionAssert.AreEqual(new[] { "sword", "vest", "ring-copy" }, List(owner, "virgil"));
        Assert.AreEqual("ring-copy", owner.DestroyNamedItem(owner.Version, "virgil", 2804).Item, "the second of the name is the first now");
        var none = owner.DestroyNamedItem(owner.Version, "virgil", 2804);
        Assert.AreEqual((GameSessionDisposition.NoChange, (string?)null), (none.Disposition, none.Item));
        Assert.AreEqual(revision + 2, owner.Version.Revision);
        // The hero's own list: a kit item goes.
        var kit = owner.QueryHeroKit()[0];
        Assert.AreEqual(kit.Entity, owner.DestroyNamedItem(owner.Version, null, kit.Kind.Name).Item);
        Assert.IsTrue(owner.IsItemDestroyed(kit.Entity));
        Assert.IsFalse(owner.QueryHeroKit().Any(item => item.Entity == kit.Entity));
        // A worn item of an NPC goes with its slot.
        Assert.AreEqual("vest", owner.DestroyNamedItem(owner.Version, "virgil", 2800).Item);
        CollectionAssert.AreEqual(new[] { "sword" }, List(owner, "virgil"));
    }

    [TestMethod]
    public void Destroy_item_named_is_refused_before_any_effect_for_an_item_whose_scripts_it_would_skip()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        // torn (5607) has a remove-item script, which the removal would fire.
        var plan = owner.QueryNamedItemDestroy("corpse", 5607);
        Assert.AreEqual("torn", plan.Item!.Entity);
        StringAssert.Contains(plan.Refusal, "script");
        var refused = owner.DestroyNamedItem(owner.Version, "corpse", 5607);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.ItemScriptUnsupported), (refused.Disposition, refused.Failure));
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DestroyNamedItem(new(owner.Identity, 7), "virgil", 2804).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DestroyNamedItem(owner.Version, "bad id", 2804).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsFalse(owner.IsItemDestroyed("torn"));
        Assert.IsNull(owner.QueryNamedItemDestroy("virgil", 9999).Item, "nothing to destroy is no refusal");
    }

    [TestMethod]
    public void Transfer_item_moves_the_first_named_item_between_the_hero_and_an_npc_or_container()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        // An NPC's item to the hero.
        var taken = owner.TransferNamedItem(owner.Version, "virgil", null, 2804, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "ring", revision + 1), (taken.Disposition, taken.Failure, taken.Item, taken.Version.Revision));
        Assert.IsTrue(owner.QueryPcInventory().Any(item => item.Entity == "ring"));
        CollectionAssert.AreEqual(new[] { "sword", "vest", "ring-copy" }, List(owner, "virgil"));
        // A hero's kit item to an NPC, then into the container, and out of it again to the hero.
        var kit = owner.QueryHeroKit()[0];
        var given = owner.TransferNamedItem(owner.Version, null, "virgil", kit.Kind.Name, Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, kit.Entity), (given.Disposition, given.Item));
        Assert.AreEqual(kit.Entity, List(owner, "virgil")[^1], "at the end of the list");
        var boxed = owner.TransferNamedItem(owner.Version, null, "chest", owner.QueryHeroKit()[0].Kind.Name, Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, boxed.Disposition, boxed.Failure.ToString());
        Assert.AreEqual(1, List(owner, "chest").Length);
        var back = owner.TransferNamedItem(owner.Version, "chest", null, owner.QueryHolderItems("chest")![0].Facts.Kind.Name, Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, back.Disposition, back.Failure.ToString());
        Assert.AreEqual(0, List(owner, "chest").Length);
    }

    [TestMethod]
    public void Transfer_item_does_nothing_without_an_item_and_is_refused_for_what_the_session_cannot_move()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var none = owner.TransferNamedItem(owner.Version, "virgil", null, 9999, Accept);
        Assert.AreEqual(GameSessionDisposition.NoChange, none.Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.TransferNamedItem(owner.Version, "virgil", "virgil", 2804, Accept).Disposition, "the same holder");
        void Refused(InventoryMutationResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual(revision, owner.Version.Revision);
        }
        Refused(owner.TransferNamedItem(owner.Version, "virgil", "corpse", 2804, Accept), GameSessionFailure.HolderPairUnsupported);
        Refused(owner.TransferNamedItem(owner.Version, "virgil", null, 2800, Accept), GameSessionFailure.WornItemUnsupported);
        Refused(owner.TransferNamedItem(owner.Version, "corpse", null, 2807, Accept), GameSessionFailure.ItemScriptUnsupported);
        // ARC-MAGIC-1823, ARC-MAGIC-2252: a script host runs the item scripts of the take from a holder as of the hero's give (WieldEventScriptTests); without one they stay refused.
        Refused(owner.TransferNamedItem(new(owner.Identity, 7), "virgil", null, 2804, Accept), GameSessionFailure.StaleVersion);
        Refused(owner.TransferNamedItem(owner.Version, "virgil", null, 2804, _ => SessionInsertVerdict.NoRoom), GameSessionFailure.InsertRefused);
        Refused(owner.TransferNamedItem(owner.Version, null, "nobody", owner.QueryHeroKit()[0].Kind.Name, Accept), GameSessionFailure.UnknownHolder);
        Assert.IsNull(owner.QueryNamedItemTransfer("virgil", null, 2807).Item, "2807 is the corpse's boots, not in virgil's list");
        Assert.AreEqual("boots", owner.QueryNamedItemTransfer("corpse", null, 2807).Item!.Entity);
        Assert.AreEqual(3, owner.QueryHeroKit().Count);
    }

    // The seed with two more ground items the hero can pick up (relic: a destroy script; idol: a remove-item script), a keeper whose list has a transfer script (point 32) and a smith holding a key without a number.
    private static GameSessionOwner HeroWithScriptedItems()
    {
        var seed = Seed();
        seed = seed with
        {
            Holders = [.. seed.Holders, new("warden", SessionHolderKind.Npc, false, false, [SessionScriptPoint.Transfer], []), new("smith", SessionHolderKind.Npc, false, false, [], ["signet"])],
            Items = [.. seed.Items, new("relic", Facts(SessionItemType.Generic, 5700, scripts: 2), new CellCoord(110, 200)), new("idol", Facts(SessionItemType.Generic, 5701, scripts: SessionScriptPoint.RemoveItem), new CellCoord(111, 200)),
                new("signet", Facts(SessionItemType.Key, 5710), null)],
        };
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual("relic", owner.PickUpItem(owner.Version, "relic", Accept).Item);
        Assert.AreEqual("idol", owner.PickUpItem(owner.Version, "idol", Accept).Item);
        return owner;
    }

    [TestMethod]
    public void A_hero_item_taken_from_a_map_keeps_the_script_points_of_its_seed_row_for_the_checks()
    {
        using var owner = HeroWithScriptedItems();
        var revision = owner.Version.Revision;
        CollectionAssert.Contains(owner.QueryNamedItem(null, 5700)!.ScriptPoints.ToArray(), 2);
        CollectionAssert.Contains(owner.QueryNamedItem(null, 5701)!.ScriptPoints.ToArray(), SessionScriptPoint.RemoveItem);
        // Destroyed it would run its destroy or remove-item script: refused before any effect, with the failure the plan names.
        var destroy = owner.QueryNamedItemDestroy(null, 5700);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, destroy.Failure);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.DestroyNamedItem(owner.Version, null, 5700).Failure);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.DestroyNamedItem(owner.Version, null, 5701).Failure);
        // Given away it would run its remove-item script: the plan refuses it as the move itself would.
        var plan = owner.QueryNamedItemTransfer(null, "virgil", 5701);
        Assert.AreEqual((GameSessionFailure.ItemScriptUnsupported, true), (plan.Failure, plan.Refusal is not null));
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.TransferNamedItem(owner.Version, null, "virgil", 5701, Accept).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.IsFalse(owner.IsItemDestroyed("relic"));
        // An item with no such point goes: the destroy-script item of the hero can be handed over (nothing runs on that move).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.TransferNamedItem(owner.Version, null, "virgil", 5700, Accept).Disposition);
    }

    [TestMethod]
    public void Transfer_item_refuses_before_any_effect_what_the_move_would_refuse_for_its_holder_or_type()
    {
        using var owner = HeroWithScriptedItems();
        var revision = owner.Version.Revision;
        void Planned(string? from, string? to, int name, GameSessionFailure failure)
        {
            var plan = owner.QueryNamedItemTransfer(from, to, name);
            Assert.AreEqual(failure, plan.Failure, $"{from} -> {to} {name}");
            Assert.IsNotNull(plan.Refusal);
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (owner.TransferNamedItem(owner.Version, from, to, name, Accept).Disposition, owner.TransferNamedItem(owner.Version, from, to, name, Accept).Failure));
        }
        // A new holder with a transfer script: the move starts with it (0x4617F0) and the session runs none.
        Planned(null, "warden", owner.QueryHeroKit()[0].Kind.Name, GameSessionFailure.HolderScriptUnsupported);
        // Gold is the coin total's, a key goes only onto the ring with a number.
        Planned("merchant", null, 3200, GameSessionFailure.ItemTypeUnsupported);
        Planned("smith", null, 5710, GameSessionFailure.InvalidItem);
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void Transfer_item_refuses_a_container_that_is_not_on_the_actors_map()
    {
        using var owner = Hero();
        var elsewhere = new SessionMapId(80);
        var vault = new SessionMapInventorySeed([new("vault", SessionHolderKind.Container, false, false, [], ["gem"])], [new("gem", Facts(SessionItemType.Generic, 5720), null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(elsewhere, vault).Failure);
        var revision = owner.Version.Revision;
        var plan = owner.QueryNamedItemTransfer("vault", null, 5720);
        Assert.AreEqual((GameSessionFailure.HolderNotLootable, "gem"), (plan.Failure, plan.Item!.Entity));
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, owner.TransferNamedItem(owner.Version, "vault", null, 5720, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.HolderNotLootable, owner.TransferNamedItem(owner.Version, null, "vault", owner.QueryHeroKit()[0].Kind.Name, Accept).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
    }

    [TestMethod]
    public void Item_fields_are_written_toggled_saved_and_dropped_with_a_deleted_item()
    {
        using var owner = Hero();
        Assert.IsTrue(owner.QueryItemFieldWrites("note").IsEmpty);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetItemWrittenStart(owner.Version, "note", 42).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetItemWrittenStart(owner.Version, "note", 42).Disposition, "the same start again");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ToggleItemDisplay(owner.Version, "note").Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ToggleItemDisplay(owner.Version, "cup").Disposition);
        Assert.AreEqual(new SessionItemFieldWrites(42, true), owner.QueryItemFieldWrites("note"));
        Assert.AreEqual(revision + 3, owner.Version.Revision);
        // A negative or large start is written as read.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.SetItemWrittenStart(owner.Version, "boots", -7).Disposition);
        CollectionAssert.AreEqual(new[] { "boots", "cup", "note" }, owner.QueryAllItemFieldWrites().Select(row => row.Entity).ToArray());

        // The save keeps them, in entity order, and a restore gives them back.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"itemFields\":[{\"entity\":\"boots\",\"writtenStart\":-7,\"displayFlipped\":false},{\"entity\":\"cup\",\"writtenStart\":null,\"displayFlipped\":true},{\"entity\":\"note\",\"writtenStart\":42,\"displayFlipped\":true}]");
        using (var restored = Restore(Decode(text)))
        {
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
            Assert.AreEqual(new SessionItemFieldWrites(42, true), restored.QueryItemFieldWrites("note"));
        }
        foreach (var damaged in new[]
        {
            text.Replace("{\"entity\":\"boots\",\"writtenStart\":-7,\"displayFlipped\":false}", "{\"entity\":\"boots\",\"writtenStart\":null,\"displayFlipped\":false}"),
            text.Replace("{\"entity\":\"cup\"", "{\"entity\":\"zebra\""),
            text.Replace("\"writtenStart\":42,\"displayFlipped\":true", "\"writtenStart\":42,\"displayFlipped\":1"),
        })
        {
            Assert.AreNotEqual(text, damaged);
            Assert.AreNotEqual(SnapshotDecodeFailure.None, GameSessionSnapshotCodec.TryDecode(damaged).Failure, "an empty row, rows out of order or a flag that is no boolean is not a save");
        }

        // The second toggle gives the data's bit back, leaving nothing; a deleted item drops its fields.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ToggleItemDisplay(owner.Version, "cup").Disposition);
        Assert.IsTrue(owner.QueryItemFieldWrites("cup").IsEmpty);
        using var other = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, other.SetItemWrittenStart(other.Version, "cup", 3).Disposition);
        Assert.AreEqual("cup", other.DestroyNamedItem(other.Version, "priest", 5650).Item);
        Assert.IsTrue(other.QueryItemFieldWrites("cup").IsEmpty, "a deleted item keeps no written field");
    }

    [TestMethod]
    public void Item_fields_are_refused_before_any_effect()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        void Refused(GameSessionMutationResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual(revision, owner.Version.Revision);
        }
        Refused(owner.SetItemWrittenStart(owner.Version, "bad id", 1), GameSessionFailure.InvalidItem);
        Refused(owner.ToggleItemDisplay(owner.Version, "bad id"), GameSessionFailure.InvalidItem);
        Refused(owner.SetItemWrittenStart(new(owner.Identity, 9), "note", 1), GameSessionFailure.StaleVersion);
        Refused(owner.ToggleItemDisplay(new(owner.Identity, 9), "note"), GameSessionFailure.StaleVersion);
        Assert.AreEqual("cup", owner.DestroyNamedItem(owner.Version, "priest", 5650).Item);
        revision = owner.Version.Revision;
        Refused(owner.SetItemWrittenStart(owner.Version, "cup", 1), GameSessionFailure.InvalidItem);
        Refused(owner.ToggleItemDisplay(owner.Version, "cup"), GameSessionFailure.InvalidItem);
    }
}
