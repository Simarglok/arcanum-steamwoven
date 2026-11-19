using Arcanum.Simulation.Equipment;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// NPC gold and the moves between the hero and a living NPC (ARC-WORLD-071, ledger NT1–NT4, AM4) over the synthetic seed of
// InventoryStateTests (virgil alive, the merchant with a pile of 5 coins) and the half-elf hero (385 gold, kit:1..3). Entities and
// numbers are invented.
[TestClass]
public sealed class NpcTransferStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static GameSessionOwner Hero(GameSessionLimits? limits = null)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), limits);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    private static void Changed(InventoryMutationResult result, string? item = null)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        if (item is not null) Assert.AreEqual(item, result.Item);
    }

    [TestMethod]
    public void Coins_leave_a_pile_in_part_and_an_emptied_pile_is_destroyed()
    {
        using var owner = Hero();
        Assert.AreEqual((5, 385, 0), (owner.QueryNpcGold("merchant"), owner.QueryGold(), owner.QueryNpcGold("virgil")));
        var revision = owner.Version.Revision;
        Changed(owner.TakeGoldFromNpc(owner.Version, "merchant", 3), "coins");
        Assert.AreEqual((2, 388, revision + 1), (owner.QueryNpcGold("merchant"), owner.QueryGold(), owner.Version.Revision));
        Assert.AreEqual(2, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "merchant").Items.Single().Facts.Quantity, "the view shows the coins left");
        Assert.AreEqual(GameSessionFailure.GoldShort, owner.TakeGoldFromNpc(owner.Version, "merchant", 3).Failure, "fewer coins: nothing changes (NT1)");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.TakeGoldFromNpc(owner.Version, "merchant", 0).Disposition);
        Changed(owner.TakeGoldFromNpc(owner.Version, "merchant", 2), "coins");
        Assert.AreEqual((0, 390), (owner.QueryNpcGold("merchant"), owner.QueryGold()));
        Assert.IsTrue(owner.IsItemDestroyed("coins"), "exactly the count destroys the pile");
        Assert.AreEqual(0, List(owner, "merchant").Length);
        Assert.AreEqual(GameSessionFailure.GoldShort, owner.TakeGoldFromNpc(owner.Version, "virgil", 1).Failure, "no pile at all");
    }

    [TestMethod]
    public void The_heros_coins_grow_an_npcs_pile_or_make_a_new_one_after_its_insert_check()
    {
        using var owner = Hero();
        Changed(owner.GiveGoldToNpc(owner.Version, "merchant", 10, GoldPile, _ => SessionInsertVerdict.NoRoom), "coins");
        Assert.AreEqual((15, 375), (owner.QueryNpcGold("merchant"), owner.QueryGold()), "a pile grows with no insert check");
        var checkedItems = new List<SessionInventoryItem>();
        Changed(owner.GiveGoldToNpc(owner.Version, "virgil", 40, GoldPile, item => { checkedItems.Add(item); return SessionInsertVerdict.Accepted; }), "made:1");
        Assert.AreEqual((40, 335, 40), (owner.QueryNpcGold("virgil"), owner.QueryGold(), checkedItems.Single().Facts.Quantity));
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy", "made:1" }, List(owner, "virgil"), "a new pile at the end of the list");
        Changed(owner.GiveGoldToNpc(owner.Version, "virgil", 5, GoldPile, Accept), "made:1");
        Assert.AreEqual(45, owner.QueryNpcGold("virgil"));
        // The hero takes them back: the made pile goes at its last coin.
        Changed(owner.TakeGoldFromNpc(owner.Version, "virgil", 45), "made:1");
        Assert.AreEqual((0, 375, 0), (owner.QueryNpcGold("virgil"), owner.QueryGold(), owner.QueryNpcInventory("virgil").Count));
    }

    [TestMethod]
    public void Gold_moves_are_refused_before_any_effect()
    {
        using var owner = Hero(new GameSessionLimits(maxGoldRecords: 1));
        void Refused(InventoryMutationResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, 385, 5), (owner.Version.Revision, owner.QueryGold(), owner.QueryNpcGold("merchant")));
        }
        Refused(owner.TakeGoldFromNpc(owner.Version, "merchant", -1), GameSessionFailure.InvalidItem);
        Refused(owner.TakeGoldFromNpc(owner.Version, "made:1", 1), GameSessionFailure.InvalidItem);
        Refused(owner.TakeGoldFromNpc(new(owner.Identity, 7), "merchant", 1), GameSessionFailure.StaleVersion);
        // ARC-WORLD-101: a standing container is a holder too (its loot window); this one holds no coins.
        Refused(owner.TakeGoldFromNpc(owner.Version, "chest", 1), GameSessionFailure.GoldShort);
        Refused(owner.TakeGoldFromNpc(owner.Version, "ghost", 1), GameSessionFailure.UnknownHolder);
        Refused(owner.GiveGoldToNpc(owner.Version, "merchant", 1, GoldPile with { Type = SessionItemType.Generic }, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.GiveGoldToNpc(owner.Version, "merchant", 386, GoldPile, Accept), GameSessionFailure.GoldShort);
        Refused(owner.GiveGoldToNpc(owner.Version, "virgil", 1, GoldPile, _ => SessionInsertVerdict.NoRoom), GameSessionFailure.InsertRefused);
        // One gold record: the merchant's changed pile takes it, so a new pile for virgil is over the budget.
        Changed(owner.TakeGoldFromNpc(owner.Version, "merchant", 1));
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.GiveGoldToNpc(owner.Version, "virgil", 1, GoldPile, Accept).Failure);
        using var heroless = Attached();
        Assert.AreEqual(GameSessionFailure.GoldUnavailable, heroless.TakeGoldFromNpc(heroless.Version, "merchant", 1).Failure, "gold needs a hero");
        Assert.IsNull(heroless.QueryNpcGold("stranger"));
    }

    [TestMethod]
    public void A_given_kit_item_leaves_its_slot_joins_the_npcs_list_and_comes_back_to_its_place()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Equip(owner.Version, "kit:1", EquipmentSlot.Weapon,
            EquipmentStateTests.Wearable(1, EquipmentStateTests.Blade), EquipmentStateTests.Drawn, Accept).Disposition);
        var checkedItems = new List<SessionInventoryItem>();
        Changed(owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", item => { checkedItems.Add(item); return SessionInsertVerdict.Accepted; }), "kit:1");
        Assert.AreEqual("kit:1", checkedItems.Single().Entity, "the NPC's insert check sees the item");
        Assert.IsFalse(owner.QueryEquipment().Worn.Any(worn => worn.Entity == "kit:1"), "a worn item leaves its slot");
        CollectionAssert.AreEqual(new[] { "kit:2", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy", "kit:1" }, List(owner, "virgil"));
        CollectionAssert.AreEqual(new[] { new SessionGivenItem("kit:1", null) }, owner.QueryGivenItems().ToArray());
        Assert.AreEqual(0, owner.QueryNpcInventory("virgil").Count, "QueryNpcInventory still lists only made items");

        // The save keeps it in virgil's list; a given record that names no NPC's item is refused.
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"given\":[{\"entity\":\"kit:1\",\"origin\":0}]");
        using (var restored = Restore(Decode(text), RestoreOptions()))
            CollectionAssert.AreEqual(new[] { "kit:2", "kit:3" }, restored.QueryHeroKit().Select(item => item.Entity).ToArray());
        // A given record whose item is in no NPC's list (kit:2 is still in the hero's kit).
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(text.Replace("\"given\":[{\"entity\":\"kit:1\",\"origin\":0}]",
            "\"given\":[{\"entity\":\"kit:1\",\"origin\":0},{\"entity\":\"kit:2\",\"origin\":0}]")), RestoreOptions()).Failure);
        // A given kit item past the kit's numbers, though in an NPC's list: kit:3 given leaves the kit [kit:1, kit:2] in order, so only
        // the number check refuses kit:5.
        using (var other = Hero())
        {
            Changed(other.GiveItemToNpc(other.Version, "virgil", "kit:3", Accept), "kit:3");
            var past = GameSessionSnapshotCodec.Encode(other.Capture()).Replace("\"entity\":\"kit:3\"", "\"entity\":\"kit:5\"");
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(past), RestoreOptions()).Failure);
            // A given made item and an unordered given list.
            Changed(other.GiveItemToNpc(other.Version, "virgil", "kit:2", Accept), "kit:2");
            var two = GameSessionSnapshotCodec.Encode(other.Capture());
            StringAssert.Contains(two, "\"given\":[{\"entity\":\"kit:2\",\"origin\":0},{\"entity\":\"kit:3\",\"origin\":0}]");
            Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(two.Replace(
                "{\"entity\":\"kit:2\",\"origin\":0},{\"entity\":\"kit:3\",\"origin\":0}", "{\"entity\":\"kit:3\",\"origin\":0},{\"entity\":\"kit:2\",\"origin\":0}")).Failure);
            Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(Decode(two.Replace("\"entity\":\"kit:3\"", "\"entity\":\"made:7\"")),
                RestoreOptions()).Failure, "a made item is never a given record");
        }

        // Taking it back from the living NPC returns it to the kit at its number's place.
        Changed(owner.TakeItemFromNpc(owner.Version, "virgil", "kit:1", Accept), "kit:1");
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        Assert.AreEqual(0, owner.QueryGivenItems().Count);
    }

    [TestMethod]
    public void A_given_taken_item_keeps_its_origin_and_a_living_npcs_item_comes_to_the_hero()
    {
        using var owner = Hero();
        Changed(owner.TakeItemFromNpc(owner.Version, "virgil", "sword", Accept), "sword");
        Assert.AreEqual(new SessionPcItem("sword", Exterior, Facts(SessionItemType.Weapon, 2000).Kind), owner.QueryPcInventory().Single());
        Changed(owner.GiveItemToNpc(owner.Version, "merchant", "sword", Accept));
        CollectionAssert.AreEqual(new[] { new SessionGivenItem("sword", Exterior) }, owner.QueryGivenItems().ToArray());
        CollectionAssert.AreEqual(new[] { "coins", "sword" }, List(owner, "merchant"));
        Changed(owner.TakeItemFromNpc(owner.Version, "merchant", "sword", Accept));
        Assert.AreEqual(new SessionPcItem("sword", Exterior, Facts(SessionItemType.Weapon, 2000).Kind), owner.QueryPcInventory().Single(), "back with its origin");

        Assert.AreEqual(GameSessionFailure.WornItemUnsupported, owner.TakeItemFromNpc(owner.Version, "virgil", "vest", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemTypeUnsupported, owner.TakeItemFromNpc(owner.Version, "merchant", "coins", Accept).Failure, "gold is TakeGoldFromNpc's");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.TakeItemFromNpc(owner.Version, "virgil", "cup", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.TakeItemFromNpc(owner.Version, "chest", "cup", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.GiveItemToNpc(owner.Version, "virgil", "ring", Accept).Failure, "not the hero's");
        Assert.AreEqual(GameSessionFailure.InsertRefused, owner.GiveItemToNpc(owner.Version, "virgil", "sword", _ => SessionInsertVerdict.TooHeavy).Failure);
        Assert.AreEqual(1, owner.QueryPcInventory().Count, "a refusal leaves the item with the hero");
    }

    [TestMethod]
    public void Reduced_and_made_piles_and_a_given_taken_item_survive_a_save_and_restore_checks_them()
    {
        using var owner = Hero();
        Changed(owner.TakeGoldFromNpc(owner.Version, "merchant", 2));
        Changed(owner.GiveGoldToNpc(owner.Version, "virgil", 7, GoldPile, Accept), "made:1");
        Changed(owner.TakeItemFromNpc(owner.Version, "virgil", "sword", Accept));
        Changed(owner.GiveItemToNpc(owner.Version, "merchant", "sword", Accept));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"given\":[{\"entity\":\"sword\",\"origin\":79}],\"piles\":[{\"entity\":\"coins\",\"coins\":3},{\"entity\":\"made:1\",\"coins\":7}]");
        using (var restored = Restore(Decode(text), RestoreOptions()))
        {
            Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
            Assert.AreEqual((3, 7, 380), (restored.QueryNpcGold("merchant"), restored.QueryNpcGold("virgil"), restored.QueryGold()));
            CollectionAssert.AreEqual(new[] { "coins", "sword" }, List(restored, "merchant"));
            // A seed of the sword's origin map that no longer lists it, or lists it with another kind, does not fit.
            var seed = Seed();
            var without = seed with { Holders = [.. seed.Holders.Select(h => h.Entity == "virgil" ? h with { Items = ["ring", "vest", "ring-copy"] } : h)],
                Items = [.. seed.Items.Where(item => item.Entity != "sword")] };
            Assert.AreEqual(GameSessionFailure.InventoryMismatch, restored.AttachMapInventory(Exterior, without).Failure);
            // A seed that lists the merchant's tracked pile as no gold does not fit either.
            var notGold = seed with { Items = [.. seed.Items.Select(item => item.Entity == "coins" ? item with { Facts = Facts(SessionItemType.Generic, 3200) } : item)] };
            Assert.AreEqual(GameSessionFailure.InventoryMismatch, restored.AttachMapInventory(Exterior, notGold).Failure);
        }
        GameSessionFailure Restored(string changed) => GameSessionOwner.Restore(Decode(changed), RestoreOptions()).Failure;
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace("{\"entity\":\"coins\",\"coins\":3}", "{\"entity\":\"coins\",\"coins\":0}")), "no coins");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace("{\"entity\":\"coins\",\"coins\":3}", "{\"entity\":\"kit:2\",\"coins\":3}")), "a hero's item");
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, Restored(text.Replace("{\"entity\":\"made:1\",\"coins\":7}", "{\"entity\":\"made:1\",\"coins\":7},{\"entity\":\"made:9\",\"coins\":2}")),
            "a made pile in no NPC's list");
        Assert.AreEqual(GameSessionFailure.InvalidItem, Restored(text.Replace(",{\"entity\":\"made:1\",\"coins\":7}", "")), "a made gold pile without its record");
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, GameSessionSnapshotCodec.TryDecode(text.Replace("{\"entity\":\"coins\",\"coins\":3},{\"entity\":\"made:1\",\"coins\":7}",
            "{\"entity\":\"made:1\",\"coins\":7},{\"entity\":\"coins\",\"coins\":3}")).Failure);
        Assert.AreEqual(SnapshotDecodeFailure.OutOfRange, GameSessionSnapshotCodec.TryDecode(text.Replace("\"coins\":3}", "\"coins\":-3}")).Failure);
    }

    [TestMethod]
    public void The_ammunition_budget_counts_both_records()
    {
        // An archer holds an untracked seed stack of 30 arrows.
        var seed = Seed();
        var arrows = new SessionSeedItem("arrows", Facts(SessionItemType.Ammo, 3300, quantity: 30) with { AmmoType = 0 }, null);
        var custom = seed with
        {
            Holders = [.. seed.Holders, new("archer", SessionHolderKind.Npc, false, false, [], ["arrows"])],
            Items = [.. seed.Items, arrows],
        };
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), new GameSessionLimits(maxAmmoRecords: 1));
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, custom).Failure);
        // Part of the stack: its rest and the hero's new stack would be two records, one over the budget.
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 10, Bullets with { Prototype = 7058 }, Accept).Failure);
        Assert.AreEqual(0L, owner.Version.Revision);
        // The whole stack: the source goes, one new record.
        Changed(owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets with { Prototype = 7058 }, Accept), "made:1");
        Assert.AreEqual(30, owner.QueryAmmo("made:1")!.Value.Units);
        Assert.IsTrue(owner.IsItemDestroyed("arrows"));
    }

    [TestMethod]
    public void Ammunition_units_move_in_part_into_the_first_stack_of_their_type_or_a_new_one()
    {
        using var owner = Hero();
        var npcStack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Changed(owner.TakeAmmoFromNpc(owner.Version, "virgil", npcStack, 20, Bullets, Accept), "made:2");
        Assert.AreEqual((10, 20), (owner.QueryAmmo(npcStack)!.Value.Units, owner.QueryAmmo("made:2")!.Value.Units), "a new stack for the hero");
        Assert.AreEqual(GameSessionFailure.AmmoShort, owner.TakeAmmoFromNpc(owner.Version, "virgil", npcStack, 11, Bullets, Accept).Failure);
        Changed(owner.TakeAmmoFromNpc(owner.Version, "virgil", npcStack, 10, Bullets, _ => SessionInsertVerdict.NoRoom), "made:2");
        Assert.AreEqual(30, owner.QueryAmmo("made:2")!.Value.Units, "the hero's stack of the type grows with no insert check");
        Assert.IsTrue(owner.IsItemDestroyed(npcStack), "the NPC's stack went at its last unit");

        Changed(owner.GiveAmmoToNpc(owner.Version, "merchant", "made:2", 5, Bullets, Accept), "made:3");
        Assert.AreEqual((25, 5), (owner.QueryAmmo("made:2")!.Value.Units, owner.QueryAmmo("made:3")!.Value.Units));
        CollectionAssert.AreEqual(new[] { "coins", "made:3" }, List(owner, "merchant"));
        Changed(owner.GiveAmmoToNpc(owner.Version, "merchant", "made:2", 25, Bullets, Accept), "made:3");
        Assert.AreEqual((30, false), (owner.QueryAmmo("made:3")!.Value.Units, owner.QueryHeroAmmo().Any()));
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.GiveAmmoToNpc(owner.Version, "merchant", "made:3", 1, GoldPile, Accept).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.GiveAmmoToNpc(owner.Version, "merchant", "made:3", 1, Bullets, Accept).Failure, "not the hero's stack");

        // The ammunition records come back after a save.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())), RestoreOptions());
        Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual((5, 30), (restored.QueryNpcGold("merchant"), restored.QueryAmmo("made:3")!.Value.Units));
    }

    [TestMethod]
    public void A_given_ammunition_item_merges_into_the_npcs_stack()
    {
        using var owner = Hero();
        var npcStack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Changed(owner.TakeAmmoFromNpc(owner.Version, "virgil", npcStack, 10, Bullets, Accept), "made:2");
        Changed(owner.GiveItemToNpc(owner.Version, "virgil", "made:2", Accept));
        Assert.AreEqual((30, false), (owner.QueryAmmo(npcStack)!.Value.Units, owner.QueryHeroAmmo().Any()), "the NPC's stack grows by the moved stack");
        Assert.IsTrue(owner.IsItemDestroyed("made:2"), "the moved stack is deleted (NT3)");
        Assert.AreEqual(0, owner.QueryGivenItems().Count, "a made item is not recorded as given");
    }

    [TestMethod]
    public void Given_kit_items_do_not_count_against_the_heros_item_budget_on_restore()
    {
        using var owner = Hero();
        Changed(owner.GiveItemToNpc(owner.Version, "virgil", "kit:1", Accept));
        Changed(owner.GiveItemToNpc(owner.Version, "virgil", "kit:2", Accept));
        // The hero holds one item (kit:3); the kit order skips both given numbers within a budget of one.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())), RestoreOptions(new GameSessionLimits(maxPcItems: 1)));
        CollectionAssert.AreEqual(new[] { "kit:3" }, restored.QueryHeroKit().Select(item => item.Entity).ToArray());
    }

    [TestMethod]
    public void Pile_scripts_coin_overflow_and_an_emptied_scripted_stack()
    {
        // The merchant's pile has an insert-item script (the event the original fires on the source pile first); an archer holds a seed
        // stack with a remove-item script, which an emptying move deletes without that event (AM4, NT1); a hoarder's pile is nearly full.
        var seed = Seed();
        var custom = seed with
        {
            Holders = [.. seed.Holders, new("archer", SessionHolderKind.Npc, false, false, [], ["arrows"]),
                new("hoarder", SessionHolderKind.Npc, false, false, [], ["hoard"])],
            Items = [.. seed.Items.Select(item => item.Entity == "coins" ? item with { Facts = Facts(SessionItemType.Gold, 3200, quantity: 5, scripts: SessionScriptPoint.InsertItem) } : item),
                new SessionSeedItem("arrows", Facts(SessionItemType.Ammo, 3300, quantity: 30, scripts: SessionScriptPoint.RemoveItem) with { AmmoType = 0 }, null),
                new SessionSeedItem("hoard", Facts(SessionItemType.Gold, 3200, quantity: int.MaxValue - 5), null)],
        };
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, custom).Failure);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, owner.TakeGoldFromNpc(owner.Version, "merchant", 1).Failure);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.TakeGoldFromNpc(owner.Version, "hoarder", int.MaxValue - 5).Failure, "the hero's gold past 32 bits");
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.GiveGoldToNpc(owner.Version, "hoarder", 6, GoldPile, Accept).Failure, "the pile past 32 bits");
        Assert.AreEqual((0L, 385), (owner.Version.Revision, owner.QueryGold()));
        Changed(owner.TakeAmmoFromNpc(owner.Version, "archer", "arrows", 30, Bullets with { Prototype = 7058 }, Accept), "made:1");
        Assert.IsTrue(owner.IsItemDestroyed("arrows"));
    }
}
