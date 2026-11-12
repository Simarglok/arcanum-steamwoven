using Arcanum.Simulation.Movement;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-090: a merchant's substitute container in the barter window (ledger BT4, BT5, BT11): the open moves the container's coins
// onto the merchant and identifies both lists, the trades move items out of and into the container while the coins stay the
// merchant's, and the store and the identified items are saved (schema 50). A synthetic seed: the smith (alive, a pile of 10 coins and
// a hammer), its chest (piles of 30, 0 and 12 coins, a magickal amulet, an axe and 20 bullets) and an unrelated box; the half-elf hero
// (385 gold, kit:1..3). Entities and numbers are invented.
[TestClass]
public sealed class MerchantStockStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static SessionItemFacts Ammo(int units) => Facts(SessionItemType.Ammo, 3301, quantity: units) with { AmmoType = 1 };

    internal static SessionMapInventorySeed Seed(bool smithPile = true) => WithoutPurse(new(
        [
            new("smith", SessionHolderKind.Npc, false, true, [], smithPile ? ["purse", "hammer"] : ["hammer"]),
            new("chest", SessionHolderKind.Container, false, false, [], ["gold-a", "gold-zero", "amulet", "axe", "gold-b", "bullets"]),
            new("box", SessionHolderKind.Container, false, false, [], []),
        ],
        [
            new("purse", Facts(SessionItemType.Gold, 3200, quantity: 10), null),
            new("hammer", Facts(SessionItemType.Weapon, 2001, location: 1004), null),
            new("gold-a", Facts(SessionItemType.Gold, 3200, quantity: 30), null),
            new("gold-zero", Facts(SessionItemType.Gold, 3200, quantity: 0), null),
            new("amulet", Facts(SessionItemType.Armor, 2805, complexity: 20), null),
            new("axe", Facts(SessionItemType.Weapon, 2002), null),
            new("gold-b", Facts(SessionItemType.Gold, 3200, quantity: 12), null),
            new("bullets", Ammo(20), null),
        ]), smithPile);

    private static SessionMapInventorySeed WithoutPurse(SessionMapInventorySeed seed, bool keep) =>
        keep ? seed : seed with { Items = [.. seed.Items.Where(item => item.Entity != "purse")] };

    private static GameSessionOwner Hero(SessionMapInventorySeed? seed = null, RuleMode mode = RuleMode.Steamwoven)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HeroStateTests.HalfElf(), Rules = mode }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed ?? Seed()).Failure);
        return owner;
    }

    private static string[] List(GameSessionOwner owner, string holder) =>
        owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == holder).Items.Select(item => item.Entity).ToArray();

    private static SessionBarterOpenResult Open(GameSessionOwner owner, string? container = "chest", bool identify = true, SessionInsertCheck? insert = null) =>
        owner.OpenBarter(owner.Version, "smith", container, identify, GoldPile, insert ?? Accept);

    private static void Changed(InventoryMutationResult result, string? item = null)
    {
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (result.Disposition, result.Failure));
        if (item is not null) Assert.AreEqual(item, result.Item);
    }

    [TestMethod]
    public void A_container_s_loot_window_takes_a_named_gold_pile_ammunition_units_and_items()
    {
        // ARC-WORLD-101: a standing container is a holder of TakeGoldFromNpc (the pile given) and TakeAmmoFromNpc, as its loot window
        // takes from it; a container's use script ran at the window's admission, so it refuses no loot.
        var seed = Seed() with { Holders = [.. Seed().Holders.Select(h => h.Entity == "chest" ? h with { ScriptPoints = [SessionScriptPoint.Use] } : h)] };
        using var owner = Hero(seed);
        var gold = owner.QueryGold();
        Changed(owner.TakeGoldFromNpc(owner.Version, "chest", 5, "gold-b"), "gold-b");
        Assert.AreEqual(gold + 5, owner.QueryGold());
        Changed(owner.TakeGoldFromNpc(owner.Version, "chest", 30), "gold-a");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.TakeGoldFromNpc(owner.Version, "chest", 1, "purse").Failure, "the smith's pile is not the chest's");
        Assert.AreEqual(GameSessionFailure.GoldShort, owner.TakeGoldFromNpc(owner.Version, "chest", 8, "gold-b").Failure, "7 coins are left");
        Changed(owner.TakeAmmoFromNpc(owner.Version, "chest", "bullets", 15, Bullets, Accept));
        Assert.AreEqual(5, owner.QueryAmmo("bullets")!.Value.Units);
        Changed(owner.LootItem(owner.Version, "chest", "amulet", Accept), "amulet");
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.TakeGoldFromNpc(owner.Version, "ghost", 1).Failure);
        CollectionAssert.AreEqual(new[] { "gold-zero", "axe", "gold-b", "bullets" }, List(owner, "chest"));
    }

    [TestMethod]
    public void The_open_moves_the_containers_coins_onto_the_merchant_and_identifies_both_lists()
    {
        using var owner = Hero();
        var opened = Open(owner);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, 42, 2, "purse", 6), (opened.Disposition, opened.Failure, opened.Coins,
            opened.Piles, opened.Pile, opened.Identified));
        Assert.AreEqual((1L, 52), (owner.Version.Revision, owner.QueryNpcGold("smith")), "one revision; the piles joined the smith's own");
        Assert.IsTrue(owner.IsItemDestroyed("gold-a") && owner.IsItemDestroyed("gold-b"), "the container's piles are destroyed (0x464830)");
        CollectionAssert.AreEqual(new[] { "gold-zero", "amulet", "axe", "bullets" }, List(owner, "chest"), "a pile of 0 coins stays (BT11)");
        CollectionAssert.AreEqual(new[] { "amulet", "axe", "bullets", "gold-zero", "hammer", "purse" }, owner.Capture().Inventory.Identified.ToArray(),
            "every entry of both lists, worn and gold included (0x4643D0)");
        Assert.IsTrue(owner.QueryItemIdentified("amulet") && !owner.QueryItemIdentified("gold-a"));
        // A second open has nothing left to move or mark.
        Assert.AreEqual((GameSessionDisposition.NoChange, 1L), (Open(owner).Disposition, owner.Version.Revision));
    }

    [TestMethod]
    public void A_merchant_without_a_pile_gets_one_after_its_insert_check_and_a_follower_identifies_nothing()
    {
        using var owner = Hero(Seed(smithPile: false));
        var seen = new List<SessionInventoryItem>();
        var opened = Open(owner, identify: false, insert: item => { seen.Add(item); return SessionInsertVerdict.Accepted; });
        Assert.AreEqual((42, 2, "made:1", 0), (opened.Coins, opened.Piles, opened.Pile, opened.Identified));
        Assert.AreEqual(30, seen.Single().Facts.Quantity, "the new pile is the first pile's coins when it moves in (0x464970)");
        Assert.AreEqual(42, owner.QueryNpcGold("smith"));
        CollectionAssert.AreEqual(new[] { "hammer", "made:1" }, List(owner, "smith"));
        Assert.AreEqual(0, owner.Capture().Inventory.Identified.Count);
        // A refused new pile moves nothing.
        using var refused = Hero(Seed(smithPile: false));
        Assert.AreEqual(GameSessionFailure.InsertRefused, Open(refused, insert: _ => SessionInsertVerdict.TooHeavy).Failure);
        Assert.AreEqual((0L, 0), (refused.Version.Revision, refused.QueryNpcGold("smith")));
        // Without a container only the merchant's own list is identified.
        using var alone = Hero();
        Assert.AreEqual((0, 2), (Open(alone, container: null).Coins, alone.Capture().Inventory.Identified.Count));
    }

    [TestMethod]
    public void The_open_is_refused_before_any_effect()
    {
        using var owner = Hero();
        void Refused(SessionBarterOpenResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, 10), (owner.Version.Revision, owner.QueryNpcGold("smith")));
        }
        Refused(owner.OpenBarter(owner.Version, "smith", "smith", true, GoldPile, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.OpenBarter(owner.Version, "smith", "chest", true, Bullets, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.OpenBarter(owner.Version, "made:1", "chest", true, GoldPile, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.OpenBarter(new(owner.Identity, 9), "smith", "chest", true, GoldPile, Accept), GameSessionFailure.StaleVersion);
        Refused(owner.OpenBarter(owner.Version, "chest", null, true, GoldPile, Accept), GameSessionFailure.UnknownHolder);
        Refused(owner.OpenBarter(owner.Version, "smith", "purse", true, GoldPile, Accept), GameSessionFailure.UnknownHolder);
        Refused(owner.OpenBarter(owner.Version, "smith", "nowhere", true, GoldPile, Accept), GameSessionFailure.UnknownHolder);
        // A pile with an insert-item script (the event 0x464830 fires, not run here).
        var scripted = Seed() with { Items = [.. Seed().Items.Select(item => item.Entity == "gold-b"
            ? item with { Facts = item.Facts with { ScriptPoints = [SessionScriptPoint.InsertItem] } } : item)] };
        using var withScript = Hero(scripted);
        Assert.AreEqual(GameSessionFailure.ItemScriptUnsupported, Open(withScript).Failure);
        // A merchant pile that would pass 32 bits.
        var rich = Seed() with { Items = [.. Seed().Items.Select(item => item.Entity == "purse" ? item with { Facts = item.Facts with { Quantity = int.MaxValue - 41 } } : item)] };
        using var full = Hero(rich);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, Open(full).Failure);
        Assert.AreEqual(int.MaxValue - 41, full.QueryNpcGold("smith"));
    }

    [TestMethod]
    public void Trades_move_items_out_of_and_into_the_container_and_coins_between_hero_and_merchant()
    {
        using var owner = Hero();
        Open(owner);
        Changed(owner.BarterBuy(owner.Version, "smith", "axe", 1, 100, Bullets, GoldPile, Accept, Accept, container: "chest"), "axe");
        Assert.AreEqual((285, 152), (owner.QueryGold(), owner.QueryNpcGold("smith")), "the coins go to the merchant, not the container");
        Assert.IsTrue(owner.QueryPcInventory().Any(item => item.Entity == "axe") && owner.QueryItemIdentified("axe"));
        Changed(owner.BarterSell(owner.Version, "smith", "kit:1", 1, 20, Bullets, Accept, container: "chest", identify: true), "kit:1");
        Assert.AreEqual((305, 132), (owner.QueryGold(), owner.QueryNpcGold("smith")));
        CollectionAssert.AreEqual(new[] { "gold-zero", "amulet", "bullets", "kit:1" }, List(owner, "chest"), "a sold item goes into the shown object (BT5)");
        CollectionAssert.AreEqual(new[] { "purse", "hammer" }, List(owner, "smith"));
        Assert.IsTrue(owner.QueryItemIdentified("kit:1"), "a sale outside a follower's window identifies the item");
        // The sold item is bought back out of the container and returns to the kit.
        Changed(owner.BarterBuy(owner.Version, "smith", "kit:1", 1, 30, Bullets, GoldPile, Accept, Accept, container: "chest"), "kit:1");
        Assert.IsTrue(owner.QueryHeroKit().Any(item => item.Entity == "kit:1"));
        CollectionAssert.AreEqual(new[] { "gold-zero", "amulet", "bullets" }, List(owner, "chest"));
        // Units of the container's stack, then the hero's units back into it.
        Changed(owner.BarterBuy(owner.Version, "smith", "bullets", 15, 2, Bullets, GoldPile, Accept, Accept, container: "chest"));
        var mine = owner.QueryHeroAmmo().Single().Entity;
        Assert.AreEqual((5, 15), (owner.QueryAmmo("bullets")!.Value.Units, owner.QueryAmmo(mine)!.Value.Units));
        Changed(owner.BarterSell(owner.Version, "smith", mine, 10, 1, Bullets, Accept, container: "chest", identify: true));
        Assert.AreEqual((15, 5), (owner.QueryAmmo("bullets")!.Value.Units, owner.QueryAmmo(mine)!.Value.Units), "they join the container's stack");
        // Selling the whole stack moves the stack object into the container's list.
        Changed(owner.BarterSell(owner.Version, "smith", "kit:2", 1, 0, Bullets, Accept, container: "chest"));
        Assert.IsFalse(owner.QueryItemIdentified("kit:2"), "a follower's sale (identify false) marks nothing");
    }

    [TestMethod]
    public void In_original_mode_units_sold_into_a_container_make_a_new_stack()
    {
        // AMA5: the container's stack lookup by type never finds one, so the units enter as a new stack.
        using var owner = Hero(mode: RuleMode.Original);
        Changed(owner.BarterBuy(owner.Version, "smith", "bullets", 15, 0, Bullets, GoldPile, Accept, Accept, container: "chest"));
        var mine = owner.QueryHeroAmmo().Single().Entity;
        Changed(owner.BarterSell(owner.Version, "smith", mine, 10, 0, Bullets, Accept, container: "chest"));
        Assert.AreEqual(5, owner.QueryAmmo("bullets")!.Value.Units);
        var added = List(owner, "chest").Last();
        Assert.AreEqual((10, Bullets.Prototype), (owner.QueryAmmo(added)!.Value.Units, owner.QueryMapInventory(Exterior)!.Holders.Single(h => h.Entity == "chest")
            .Items.Single(item => item.Entity == added).Facts.Kind.Prototype));
    }

    [TestMethod]
    public void Container_trades_are_refused_before_any_effect()
    {
        using var owner = Hero();
        void Refused(InventoryMutationResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, 385, 10), (owner.Version.Revision, owner.QueryGold(), owner.QueryNpcGold("smith")));
            CollectionAssert.AreEqual(new[] { "gold-a", "gold-zero", "amulet", "axe", "gold-b", "bullets" }, List(owner, "chest"));
        }
        Refused(owner.BarterBuy(owner.Version, "smith", "axe", 1, 1, Bullets, GoldPile, Accept, Accept, container: "smith"), GameSessionFailure.InvalidItem);
        Refused(owner.BarterBuy(owner.Version, "smith", "axe", 1, 1, Bullets, GoldPile, Accept, Accept, container: "purse"), GameSessionFailure.UnknownHolder);
        Refused(owner.BarterBuy(owner.Version, "smith", "axe", 1, 1, Bullets, GoldPile, Accept, Accept, container: "box"), GameSessionFailure.ItemNotAvailable);
        Refused(owner.BarterBuy(owner.Version, "smith", "hammer", 1, 1, Bullets, GoldPile, Accept, Accept, container: "chest"), GameSessionFailure.ItemNotAvailable);
        Refused(owner.BarterBuy(owner.Version, "smith", "bullets", 21, 1, Bullets, GoldPile, Accept, Accept, container: "chest"), GameSessionFailure.AmmoShort);
        Refused(owner.BarterSell(owner.Version, "smith", "kit:1", 1, 1, Bullets, Accept, container: "nowhere"), GameSessionFailure.UnknownHolder);
        Refused(owner.BarterSell(owner.Version, "smith", "kit:1", 1, 11, Bullets, Accept, container: "chest"), GameSessionFailure.GoldShort);
        Refused(owner.BarterSell(owner.Version, "smith", "kit:1", 1, 1, Bullets, _ => SessionInsertVerdict.NoRoom, container: "chest"), GameSessionFailure.InsertRefused);
    }

    [TestMethod]
    public void The_container_store_and_the_identified_items_round_trip_and_bind_to_a_container()
    {
        using var owner = Hero();
        Open(owner);
        Changed(owner.BarterSell(owner.Version, "smith", "kit:1", 1, 5, Bullets, Accept, container: "chest", identify: true));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, ",\"npcs\":[{\"npc\":\"chest\",\"container\":true,\"items\":[{\"entity\":\"kit:1\"");
        StringAssert.Contains(text, ",\"identified\":[\"amulet\",\"axe\",\"bullets\",\"gold-zero\",\"hammer\",\"kit:1\",\"purse\"],\"lastDiscardedMade\":0,\"grid\":[");
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, Seed()).Disposition);
        CollectionAssert.AreEqual(new[] { "gold-zero", "amulet", "axe", "bullets", "kit:1" }, List(restored, "chest"));
        Assert.IsTrue(restored.QueryItemIdentified("kit:1"));
        Assert.IsNull(restored.QueryNpcGold("chest"), "a container is not an NPC");
        Assert.AreEqual(0, restored.QueryNpcInventory("chest").Count);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, restored.GiveItemToNpc(restored.Version, "chest", "kit:2", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, restored.AddItemToNpc(restored.Version, "chest", Bullets, Accept).Failure);
        // A deleted item loses its flag: buying every unit of the container's stack deletes it (0x4658E0).
        Assert.IsTrue(restored.QueryItemIdentified("bullets"));
        Changed(restored.BarterBuy(restored.Version, "smith", "bullets", 20, 0, Bullets, GoldPile, Accept, Accept, container: "chest"));
        Assert.IsTrue(restored.IsItemDestroyed("bullets") && !restored.QueryItemIdentified("bullets"), "the emptied stack is deleted with its flag");
        // A seed that lists the filled container as an NPC is another map's data.
        var swapped = Seed() with { Holders = [.. Seed().Holders.Select(h => h.Entity == "chest" ? h with { Kind = SessionHolderKind.Npc } : h)] };
        using var other = Restore(Decode(text));
        Assert.AreEqual(GameSessionFailure.InventoryMismatch, other.AttachMapInventory(Exterior, swapped).Failure);
        // Damaged identified rows.
        SnapshotDecodeFailure Decoded(string damaged) => GameSessionSnapshotCodec.TryDecode(damaged).Failure;
        Assert.AreEqual(SnapshotDecodeFailure.NotCanonical, Decoded(text.Replace("\"amulet\",\"axe\"", "\"axe\",\"amulet\"")), "order");
        Assert.AreEqual(SnapshotDecodeFailure.MissingField, Decoded(text.Replace(",\"container\":true", "")), "the container flag");
        // A deleted item cannot stay identified.
        var deleted = Decode(text.Replace("\"bullets\",\"gold-zero\"", "\"bullets\",\"gold-a\",\"gold-zero\""));
        Assert.AreEqual(GameSessionFailure.InvalidSnapshot, GameSessionOwner.Restore(deleted, RestoreOptions()).Failure);
    }

    // ARC-WORLD-1306 (VPG7): the break's spill walks the container's whole list, the items a barter window sold into it included.
    private static readonly CellCoord ChestCell = new(120, 210);

    private static SessionObjectTarget Chest() => new(Exterior, "chest", new SessionObjectBaseline
    {
        Kind = Arcanum.Simulation.Combat.DamageableObjectKind.Container, Points = 1, Cell = ChestCell,
        Openable = new(Arcanum.Simulation.Locks.OpenableKind.Container, 0, 0, 0),
    });

    private static void Break(GameSessionOwner owner)
    {
        var broke = owner.DamageObject(owner.Version, Chest(), 50);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, Arcanum.Simulation.Combat.ObjectBreak.ContainerSpilled),
            (broke.Disposition, broke.Failure, broke.Hit!.Break));
    }

    [TestMethod]
    public void A_broken_container_spills_an_item_the_hero_sold_into_it_and_a_save_keeps_it()
    {
        // The chest's own items (gold, amulet, axe, bullets) and the sold kit:1 spill last first, each through the drop placement.
        var plain = Seed() with
        {
            Holders = [.. Seed().Holders.Select(h => h.Entity == "chest" ? h with { Items = ["amulet", "axe", "bullets"] } : h)],
            Items = [.. Seed().Items.Where(item => !item.Entity.StartsWith("gold", StringComparison.Ordinal))],
        };
        using var owner = Hero(plain);
        Changed(owner.BarterSell(owner.Version, "smith", "kit:1", 1, 0, Bullets, Accept, container: "chest"), "kit:1");
        Assert.IsTrue(owner.QueryHeroKit().All(item => item.Entity != "kit:1"));
        Break(owner);
        Assert.AreEqual(0, List(owner, "chest").Length, "a spilled container lists nothing");
        Assert.AreEqual(0, owner.Capture().Inventory.Npcs.Count, "the sold item left the container's list");
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, plain).Disposition);
        CollectionAssert.AreEqual(new[] { "kit:1", "bullets", "axe", "amulet" }, restored.QueryMapInventory(Exterior)!.JunkPiles.Single().Items.Select(item => item.Entity).ToArray(), "a save keeps the pile");
        var inventory = owner.QueryMapInventory(Exterior)!;
        var pile = inventory.JunkPiles.Single();
        Assert.AreEqual(ChestCell, pile.Cell);
        CollectionAssert.AreEqual(new[] { "kit:1", "bullets", "axe", "amulet" }, pile.Items.Select(item => item.Entity).ToArray(), "the list's end first");
        CollectionAssert.AreEqual(new[] { "kit:1", "bullets", "axe", "amulet" }, owner.QueryItemDecays().Select(d => d.Entity).ToArray(), "each decays");
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, pile.Entity, "kit:1", Accept).Failure, "the hero's sold item is loot again");
        Assert.IsTrue(owner.QueryHeroKit().Any(item => item.Entity == "kit:1"), "it goes back to the kit");
    }

    [TestMethod]
    public void A_lone_sold_item_lies_on_the_containers_cell_and_a_save_restores_it()
    {
        var empty = Seed() with { Holders = [.. Seed().Holders.Select(h => h.Entity == "chest" ? h with { Items = [] } : h)], Items = [.. Seed().Items.Where(item => item.Entity is "purse" or "hammer")] };
        using var owner = Hero(empty);
        Changed(owner.BarterSell(owner.Version, "smith", "kit:1", 1, 0, Bullets, Accept, container: "chest"), "kit:1");
        Break(owner);
        var ground = owner.QueryMapInventory(Exterior)!.Ground;
        Assert.AreEqual(("kit:1", ChestCell), (ground.Single().Item.Entity, ground.Single().Cell));
        Assert.AreEqual("kit:1", owner.QueryItemDecays().Single().Entity);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, empty).Disposition);
        var back = restored.QueryMapInventory(Exterior)!.Ground;
        Assert.AreEqual(("kit:1", ChestCell), (back.Single().Item.Entity, back.Single().Cell));
        Assert.AreEqual(GameSessionFailure.None, restored.PickUpItem(restored.Version, "kit:1", Accept).Failure);
        Assert.IsTrue(restored.QueryHeroKit().Any(item => item.Entity == "kit:1"));
    }

    [TestMethod]
    public void An_ammunition_stack_sold_into_a_container_lies_on_the_ground_with_its_units()
    {
        // The Original setting makes the sold units a new stack of the container's list (AMA5).
        var empty = Seed() with { Holders = [.. Seed().Holders.Select(h => h.Entity == "chest" ? h with { Items = ["bullets"] } : h)], Items = [.. Seed().Items.Where(item => item.Entity is "purse" or "hammer" or "bullets")] };
        using var owner = Hero(empty, RuleMode.Original);
        Changed(owner.BarterBuy(owner.Version, "smith", "bullets", 15, 0, Bullets, GoldPile, Accept, Accept, container: "chest"));
        var mine = owner.QueryHeroAmmo().Single().Entity;
        Changed(owner.BarterSell(owner.Version, "smith", mine, 10, 0, Bullets, Accept, container: "chest"));
        var sold = List(owner, "chest").Last();
        Break(owner);
        Assert.AreEqual(10, owner.QueryAmmo(sold)!.Value.Units, "the units stay with the stack");
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(GameSessionDisposition.Changed, restored.AttachMapInventory(Exterior, empty).Disposition);
        CollectionAssert.AreEqual(new[] { sold, "bullets" }, restored.QueryMapInventory(Exterior)!.JunkPiles.Single().Items.Select(item => item.Entity).ToArray());
        Assert.AreEqual(10, restored.QueryAmmo(sold)!.Value.Units);
    }

    [TestMethod]
    public void A_break_that_cannot_hold_every_sold_item_is_refused_whole()
    {
        var seed = Seed();
        using var owner = GameSessionOwner.Create(Descriptor(new GameSessionLimits(maxPcItems: 3)) with { Hero = HeroStateTests.HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Changed(owner.BarterSell(owner.Version, "smith", "kit:1", 1, 0, Bullets, Accept, container: "chest"), "kit:1");
        Changed(owner.LootItem(owner.Version, "chest", "axe", Accept), "axe");
        var before = GameSessionSnapshotCodec.Encode(owner.Capture());
        var refused = owner.DamageObject(owner.Version, Chest(), 50);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (refused.Disposition, refused.Failure));
        Assert.AreEqual(before, GameSessionSnapshotCodec.Encode(owner.Capture()), "nothing changed: the chest is whole and keeps the sold item");
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObject(Chest()).Condition);
    }
}
