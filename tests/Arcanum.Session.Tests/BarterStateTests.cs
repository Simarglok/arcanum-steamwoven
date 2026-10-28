using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// The barter window's trades (ARC-UI-050, ledger BT5) over the synthetic seed of InventoryStateTests (virgil alive with a ring and a
// sword, the merchant with a pile of 5 coins) and the half-elf hero (385 gold, kit:1..3). Entities and numbers are invented.
[TestClass]
public sealed class BarterStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9056, 3200, 33200);
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static GameSessionOwner Hero()
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf());
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
    public void A_buy_moves_the_item_to_the_hero_then_the_hero_pays_the_npc()
    {
        using var owner = Hero();
        Changed(owner.BarterBuy(owner.Version, "virgil", "sword", 1, 120, Bullets, GoldPile, Accept, Accept), "sword");
        Assert.AreEqual((265, 120), (owner.QueryGold(), owner.QueryNpcGold("virgil")), "a new pile for an NPC without one");
        Assert.IsTrue(owner.QueryPcInventory().Any(item => item.Entity == "sword"));
        CollectionAssert.AreEqual(new[] { "ring", "vest", "ring-copy", "made:1" }, List(owner, "virgil"));
        // A price of 0 (a follower's window) moves the item for nothing.
        Changed(owner.BarterBuy(owner.Version, "virgil", "ring", 1, 0, Bullets, GoldPile, Accept, Accept), "ring");
        Assert.AreEqual((265, 120), (owner.QueryGold(), owner.QueryNpcGold("virgil")));
    }

    [TestMethod]
    public void A_sale_moves_the_heros_item_then_the_npc_pays_from_its_pile()
    {
        using var owner = Hero();
        Changed(owner.BarterSell(owner.Version, "merchant", "kit:1", 1, 5, Bullets, Accept), "kit:1");
        Assert.AreEqual((390, 0), (owner.QueryGold(), owner.QueryNpcGold("merchant")));
        Assert.IsTrue(owner.IsItemDestroyed("coins"), "the merchant paid its last coin");
        CollectionAssert.AreEqual(new[] { "kit:1" }, List(owner, "merchant"));
        // A merchant without coins takes the item for the capped price of 0.
        Changed(owner.BarterSell(owner.Version, "merchant", "kit:2", 1, 0, Bullets, Accept), "kit:2");
        Assert.AreEqual(390, owner.QueryGold());
    }

    [TestMethod]
    public void Ammunition_trades_count_units_at_the_unit_price()
    {
        using var owner = Hero();
        var stack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Changed(owner.BarterBuy(owner.Version, "virgil", stack, 20, 3, Bullets, GoldPile, Accept, Accept));
        Assert.AreEqual((325, 60, 10), (owner.QueryGold(), owner.QueryNpcGold("virgil"), owner.QueryAmmo(stack)!.Value.Units));
        var mine = owner.QueryHeroAmmo().Single().Entity;
        Changed(owner.BarterSell(owner.Version, "virgil", mine, 10, 2, Bullets, Accept));
        Assert.AreEqual((345, 40, 20, 10), (owner.QueryGold(), owner.QueryNpcGold("virgil"), owner.QueryAmmo(stack)!.Value.Units,
            owner.QueryAmmo(mine)!.Value.Units));
    }

    [TestMethod]
    public void Trades_are_refused_before_any_effect()
    {
        using var owner = Hero();
        void Refused(InventoryMutationResult result, GameSessionFailure failure)
        {
            Assert.AreEqual((GameSessionDisposition.Rejected, failure), (result.Disposition, result.Failure));
            Assert.AreEqual((0L, 385, 5), (owner.Version.Revision, owner.QueryGold(), owner.QueryNpcGold("merchant")));
            CollectionAssert.AreEqual(new[] { "ring", "sword", "vest", "ring-copy" }, List(owner, "virgil"));
        }
        Refused(owner.BarterBuy(owner.Version, "virgil", "sword", 1, -1, Bullets, GoldPile, Accept, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.BarterBuy(owner.Version, "virgil", "sword", 2, 1, Bullets, GoldPile, Accept, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.BarterBuy(owner.Version, "virgil", "sword", 1, 1, Bullets, Bullets, Accept, Accept), GameSessionFailure.InvalidItem);
        Refused(owner.BarterBuy(new(owner.Identity, 9), "virgil", "sword", 1, 1, Bullets, GoldPile, Accept, Accept), GameSessionFailure.StaleVersion);
        Refused(owner.BarterBuy(owner.Version, "chest", "sword", 1, 1, Bullets, GoldPile, Accept, Accept), GameSessionFailure.UnknownHolder);
        Refused(owner.BarterBuy(owner.Version, "virgil", "sword", 1, 386, Bullets, GoldPile, Accept, Accept), GameSessionFailure.GoldShort);
        Refused(owner.BarterBuy(owner.Version, "virgil", "sword", 1, 10, Bullets, GoldPile, _ => SessionInsertVerdict.TooHeavy, Accept), GameSessionFailure.InsertRefused);
        Refused(owner.BarterSell(owner.Version, "merchant", "kit:1", 1, 6, Bullets, Accept), GameSessionFailure.GoldShort);
        Refused(owner.BarterSell(owner.Version, "virgil", "kit:1", 1, 1, Bullets, Accept), GameSessionFailure.GoldShort);
        Refused(owner.BarterSell(owner.Version, "merchant", "ring", 1, 1, Bullets, Accept), GameSessionFailure.ItemNotAvailable);
        Refused(owner.BarterSell(owner.Version, "merchant", "kit:1", 1, 1, Bullets, _ => SessionInsertVerdict.NoRoom), GameSessionFailure.InsertRefused);
    }

    [TestMethod]
    public void A_payment_refused_after_the_move_leaves_the_item_moved_as_the_original_does()
    {
        using var owner = Hero();
        // virgil has no pile: its new pile's insert check refuses after the sword moved.
        var result = owner.BarterBuy(owner.Version, "virgil", "sword", 1, 10, Bullets, GoldPile, Accept, _ => SessionInsertVerdict.NoRoom);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, "sword"), (result.Disposition, result.Failure, result.Item));
        Assert.AreEqual((385, 0), (owner.QueryGold(), owner.QueryNpcGold("virgil")));
        Assert.IsTrue(owner.QueryPcInventory().Any(item => item.Entity == "sword"));
        Assert.AreEqual(owner.Version, result.Version);
    }
}
