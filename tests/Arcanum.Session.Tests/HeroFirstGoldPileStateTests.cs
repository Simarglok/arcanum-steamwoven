using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-1132 (ledger NT1, HG1): the first gold pile of a hero with no coin is a new object moved in through the insert path (0x4617F0, check
// 0x466510); with no free grid place for it the pile stays on the ground and the coins never reach the hero, the call still succeeding. The half-elf
// hero's kit is three 1 x 1 items; an art source that sizes each as 10 x 4 cells fills the 120-cell grid. Entities and numbers are invented.
[TestClass]
public sealed class HeroFirstGoldPileStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9076, 3200, 33200);

    private static GameSessionOwner Hero(int gold, bool full)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf() with { Gold = gold });
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        if (full) Fill(owner);
        return owner;
    }

    // Every kit item takes 10 x 4 cells (320 x 128 pixels), the gold pile 1 x 1: the three kit items fill the grid.
    private static void Fill(GameSessionOwner owner) =>
        owner.AttachItemArtSizes((entity, _) => entity == SessionHeroGrid.GoldEntity ? (32u, 32u) : (320u, 128u));

    private static bool HasPile(GameSessionOwner owner) => owner.QueryHeroGrid().Items.Any(item => item.Entity == SessionHeroGrid.GoldEntity);

    [TestMethod]
    public void The_first_pile_with_no_free_place_lies_on_the_heros_tile_and_the_call_succeeds()
    {
        using var owner = Hero(0, full: true);
        Assert.AreEqual(3, owner.QueryHeroGrid().Items.Count);
        var revision = owner.Version.Revision;
        var given = owner.GiveGoldToHero(owner.Version, 40);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:1", revision + 1),
            (given.Disposition, given.Failure, given.Item, owner.Version.Revision), "one revision, the ground pile named");
        Assert.AreEqual(0, owner.QueryGold(), "the coins never reached the hero");
        Assert.IsFalse(HasPile(owner));
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("made:1", SessionPlacedSource.Made, Exterior, Start.Cell, SessionHeroGrid.GoldKind), (placed.Entity, placed.Source, placed.Map, placed.Cell, placed.Kind));
        Assert.AreEqual(40, owner.QueryMapInventory(Exterior)!.Ground.Single(ground => ground.Item.Entity == "made:1").Item.Facts.Quantity);
        Assert.AreEqual(0, owner.QueryItemDecays().Count, "a created pile is no drop: no decay timer");
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
    }

    [TestMethod]
    public void The_first_pile_with_a_free_place_enters_the_grid_as_before()
    {
        using var owner = Hero(0, full: false);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToHero(owner.Version, 40).Disposition);
        Assert.AreEqual(40, owner.QueryGold());
        Assert.IsTrue(HasPile(owner));
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void A_hero_that_holds_a_pile_grows_it_with_no_check_in_a_full_grid()
    {
        using var owner = Hero(385, full: false);
        // kit:1 and kit:2 take 10 x 4 cells, kit:3 10 x 3: 110 cells, the pile 1 x 1 is the next, so the grid is nearly full.
        owner.AttachItemArtSizes((entity, _) => entity switch { SessionHeroGrid.GoldEntity => (32u, 32u), "kit:3" => (320u, 96u), _ => (320u, 128u) });
        Assert.IsTrue(HasPile(owner));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToHero(owner.Version, 15).Disposition);
        Assert.AreEqual(400, owner.QueryGold(), "a pile that exists grows by a plain add (NT1)");
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void Coins_taken_from_an_npc_by_a_hero_without_room_lie_on_the_ground_and_the_npc_loses_them()
    {
        using var owner = Hero(0, full: true);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", 30, GoldPile, Accept).Disposition);
        var taken = owner.TakeGoldFromNpc(owner.Version, "virgil", 30);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (taken.Disposition, taken.Failure));
        Assert.AreEqual(0, owner.QueryGold());
        Assert.IsFalse(HasPile(owner));
        var lying = owner.QueryMapInventory(Exterior)!.Ground.Single(ground => ground.Item.Facts.Kind.Type == SessionItemType.Gold);
        Assert.AreEqual((30, Start.Cell, SessionHeroGrid.GoldKind), (lying.Item.Facts.Quantity, lying.Cell, lying.Item.Facts.Kind), "a pile the session made has no seed row: the neutral gold kind");
    }

    [TestMethod]
    public void A_pickup_of_a_ground_pile_with_no_free_place_is_refused_and_the_pile_stays()
    {
        using var owner = Hero(385, full: false);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroGold(owner.Version, 385, GoldPile).Disposition);
        Fill(owner);
        var version = owner.Version;
        var picked = owner.PickUpItem(owner.Version, "made:1", Accept);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.InsertRefused, (SessionInsertVerdict?)SessionInsertVerdict.NoRoom),
            (picked.Disposition, picked.Failure, picked.Verdict), "the insert path itself refuses (0x466510 code 2)");
        Assert.AreEqual(version, owner.Version, "no revision");
        Assert.AreEqual(0, owner.QueryGold());
        Assert.IsFalse(owner.IsItemDestroyed("made:1"));
        Assert.AreEqual("made:1", owner.QueryPlacedItems().Single().Entity);
        Assert.AreEqual("made:1", owner.QueryItemDecays().Single().Entity, "its decay timer is kept");
    }

    [TestMethod]
    public void A_ground_pile_made_for_a_full_grid_survives_a_save_and_restore()
    {
        using var owner = Hero(0, full: true);
        Assert.AreEqual("made:1", owner.GiveGoldToHero(owner.Version, 40).Item);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual("made:1", restored.QueryPlacedItems().Single().Entity);
        Assert.AreEqual(0, restored.QueryGold());
    }

    [TestMethod]
    public void The_pile_left_on_the_ground_is_picked_up_when_a_place_frees()
    {
        using var owner = Hero(0, full: true);
        Assert.AreEqual("made:1", owner.GiveGoldToHero(owner.Version, 40).Item);
        owner.AttachItemArtSizes((_, _) => (32u, 32u));
        var picked = owner.PickUpItem(owner.Version, "made:1", Accept);
        Assert.AreEqual(GameSessionDisposition.Changed, picked.Disposition);
        Assert.AreEqual(40, owner.QueryGold());
        Assert.IsTrue(HasPile(owner));
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void A_full_gold_record_budget_refuses_the_ground_pile_before_any_change()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf() with { Gold = 0 }, new GameSessionLimits(maxGoldRecords: 1));
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Fill(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", 30, GoldPile, Accept).Disposition, "the one record");
        var version = owner.Version;
        var refused = owner.GiveGoldToHero(owner.Version, 40);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (refused.Disposition, refused.Failure));
        Assert.AreEqual(version, owner.Version);
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void A_take_that_needs_a_ground_pile_over_the_coin_record_budget_is_refused_before_any_change()
    {
        // One coin record allowed and held by the NPC's pile: the ground pile would be a second.
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf() with { Gold = 0 }, new GameSessionLimits(maxGoldRecords: 1));
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Fill(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToNpcFromNothing(owner.Version, "virgil", 30, GoldPile, Accept).Disposition);
        var version = owner.Version;
        var refused = owner.TakeGoldFromNpc(owner.Version, "virgil", 10);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (refused.Disposition, refused.Failure));
        Assert.AreEqual(version, owner.Version);
    }

    [TestMethod]
    public void The_ground_pile_counts_against_the_heros_item_budget()
    {
        using var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf() with { Gold = 0 }, new GameSessionLimits(maxPcItems: 3));
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Fill(owner);
        var version = owner.Version;
        var refused = owner.GiveGoldToHero(owner.Version, 40);
        Assert.AreEqual((GameSessionDisposition.Rejected, GameSessionFailure.BudgetExceeded), (refused.Disposition, refused.Failure), "the kit already holds three");
        Assert.AreEqual(version, owner.Version);
    }
}

