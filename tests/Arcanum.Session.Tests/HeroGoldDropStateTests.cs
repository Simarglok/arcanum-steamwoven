using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-091: the hero's drop of part or all of its gold pile on its own tile, after the quantity window (ledger QW1, QW5, IW16, HG1), and the
// pickup that merges the dropped coins back (JP8, NT1). The half-elf hero holds 385 coins. Entities and numbers are invented.
[TestClass]
public sealed class HeroGoldDropStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9076, 3200, 33200);

    private static GameSessionOwner Hero(GameSessionLimits? limits = null)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), limits);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static bool HasPile(GameSessionOwner owner) => owner.QueryHeroGrid().Items.Any(item => item.Entity == SessionHeroGrid.GoldEntity);

    [TestMethod]
    public void Part_of_the_pile_lies_on_the_heros_tile_as_a_new_pile_and_the_rest_stays()
    {
        using var owner = Hero();
        var cell = owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot;
        var revision = owner.Version.Revision;
        var dropped = owner.DropHeroGold(owner.Version, 100, GoldPile);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1", revision + 1),
            (dropped.Disposition, dropped.Failure, dropped.Item, owner.Version.Revision), "one revision");
        Assert.AreEqual(owner.Version, dropped.Version);
        Assert.AreEqual(285, owner.QueryGold());
        Assert.AreEqual(cell, owner.QueryHeroGrid().Items.Single(item => item.Entity == SessionHeroGrid.GoldEntity).Slot, "the pile keeps its cell");
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("made:1", SessionPlacedSource.Made, Exterior, Start.Cell, GoldPile), (placed.Entity, placed.Source, placed.Map, placed.Cell, placed.Kind));
        var lying = owner.QueryMapInventory(Exterior)!.Ground.Single(ground => ground.Item.Entity == "made:1");
        Assert.AreEqual((100, Start.Cell), (lying.Item.Facts.Quantity, lying.Cell));
        Assert.AreEqual("made:1", owner.QueryItemDecays().Single().Entity, "the drop's placement schedules the decay (ID4)");
        Assert.AreEqual(0, owner.QueryHeroKit().Count(item => item.Kind.Type == SessionItemType.Gold));
    }

    [TestMethod]
    public void Dropping_every_coin_destroys_the_pile_and_its_cell_frees()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroGold(owner.Version, 385, GoldPile).Disposition);
        Assert.AreEqual(0, owner.QueryGold());
        Assert.IsFalse(HasPile(owner), "the original destroys the pile with its last coin (NT1)");
        Assert.AreEqual(385, owner.QueryMapInventory(Exterior)!.Ground.Single(ground => ground.Item.Entity == "made:1").Item.Facts.Quantity);
    }

    [TestMethod]
    public void A_pickup_merges_the_dropped_coins_back_into_the_pile()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroGold(owner.Version, 385, GoldPile).Disposition);
        var picked = owner.PickUpItem(owner.Version, "made:1", Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1"), (picked.Disposition, picked.Failure, picked.Item));
        Assert.AreEqual(385, owner.QueryGold());
        Assert.IsTrue(HasPile(owner), "the pile comes back in the first free cell");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        Assert.IsTrue(owner.IsItemDestroyed("made:1"));
        Assert.AreEqual(0, owner.QueryItemDecays().Count, "the decay ends with the pile");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItem(owner.Version, "made:1", Accept).Failure, "a deleted pile is gone");
    }

    [TestMethod]
    public void Two_drops_on_one_tile_make_a_junk_pile_and_a_loot_merges_the_coins()
    {
        using var owner = Hero();
        Assert.AreEqual("made:1", owner.DropHeroGold(owner.Version, 50, GoldPile).Item);
        Assert.AreEqual("made:2", owner.DropHeroGold(owner.Version, 70, GoldPile).Item);
        var junk = owner.QueryJunkPiles().Single();
        CollectionAssert.AreEquivalent(new[] { "made:1", "made:2" }, junk.Items.Select(item => item.Entity).ToArray(), "gold piles do not merge on the ground (JP8)");
        Assert.AreEqual(265, owner.QueryGold());
        var looted = owner.LootItem(owner.Version, junk.Entity, "made:2", Accept);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (looted.Disposition, looted.Failure));
        Assert.AreEqual(335, owner.QueryGold());
        Assert.IsFalse(owner.QueryJunkPiles().Single().Items.Any(item => item.Entity == "made:2"));
    }

    [TestMethod]
    public void A_dropped_pile_survives_a_save_and_a_restore_refuses_one_without_its_record()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroGold(owner.Version, 120, GoldPile).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"piles\":[{\"entity\":\"made:1\",\"coins\":120}]");
        using (var restored = Restore(Decode(text), RestoreOptions()))
        {
            Assert.AreEqual(265, restored.QueryGold());
            Assert.AreEqual(120, restored.Capture().Inventory.Piles.Single(pile => pile.Entity == "made:1").Quantity);
            Assert.AreEqual("made:1", restored.QueryPlacedItems().Single().Entity);
            Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
            Assert.AreEqual(GameSessionDisposition.Changed, restored.PickUpItem(restored.Version, "made:1", Accept).Disposition);
            Assert.AreEqual(385, restored.QueryGold());
        }
        var noRecord = text.Replace("\"piles\":[{\"entity\":\"made:1\",\"coins\":120}]", "\"piles\":[]");
        Assert.AreNotEqual(GameSessionFailure.None, GameSessionOwner.Restore(Decode(noRecord), RestoreOptions()).Failure, "a gold item on the ground needs its coin record");
    }

    [TestMethod]
    public void Refusals_and_a_count_of_zero_change_nothing()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DropHeroGold(owner.Version, 0, GoldPile).Disposition, "a count of 0 moves nothing (QW5)");
        Assert.AreEqual(GameSessionFailure.GoldShort, owner.DropHeroGold(owner.Version, 386, GoldPile).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DropHeroGold(owner.Version, -1, GoldPile).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DropHeroGold(owner.Version, 1, GoldPile with { Type = SessionItemType.Ammo }).Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DropHeroGold(owner.Version with { Revision = revision - 1 }, 1, GoldPile).Failure);
        Assert.AreEqual((revision, 385, 0), (owner.Version.Revision, owner.QueryGold(), owner.QueryPlacedItems().Count));
        using var heroless = Attached();
        Assert.AreEqual(GameSessionFailure.GoldUnavailable, heroless.DropHeroGold(heroless.Version, 1, GoldPile).Failure);
        using var tight = Hero(new GameSessionLimits(maxGoldRecords: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, tight.DropHeroGold(tight.Version, 1, GoldPile).Disposition);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, tight.DropHeroGold(tight.Version, 1, GoldPile).Failure, "the record budget");
    }

    [TestMethod]
    public void A_pickup_whose_tombstone_the_durability_budget_cannot_hold_is_refused_and_changes_nothing()
    {
        using var owner = Hero(new GameSessionLimits(maxDurabilityRecords: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroGold(owner.Version, 1, GoldPile).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "made:1", Accept).Disposition, "the first tombstone fits");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroGold(owner.Version, 1, GoldPile).Disposition);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.PickUpItem(owner.Version, "made:2", Accept).Failure);
        Assert.AreEqual((revision, 384, 1), (owner.Version.Revision, owner.QueryGold(), owner.QueryPlacedItems().Count), "the coin stays on the ground");
    }
}
