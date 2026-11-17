using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-094: the hero throws part or all of its gold pile at a location after the lift's quantity question (ledger QW1, QW5, TT1, IW18, HG1, HG4).
// The half-elf hero starts with 385 coins. Entities and numbers are invented.
[TestClass]
public sealed class HeroGoldThrowStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind GoldPile = new(SessionItemType.Gold, 9076, 3200, 33200);

    private static GameSessionOwner Hero(GameSessionLimits? limits = null)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), limits);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        return owner;
    }

    private static CellCoord At(GameSessionOwner owner, int dx) => new(owner.QueryActor().Cell.X + dx, owner.QueryActor().Cell.Y);

    private static SessionHeroThrow Throw(GameSessionOwner owner, int? count, SessionItemKind? kind = null) =>
        new(SessionHeroGrid.GoldEntity, new(SessionHeroGrid.GoldKind, false, []), At(owner, 3)) { Count = count, SplitKind = kind ?? GoldPile };

    [TestMethod]
    public void Part_of_the_pile_is_split_off_and_thrown_as_a_new_pile_that_lands_where_it_ends_and_is_picked_back_into_the_gold()
    {
        using var owner = Hero();
        var thrown = owner.ThrowHeroItem(owner.Version, Throw(owner, 100));
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (thrown.Disposition, thrown.Failure));
        var shot = thrown.Shots.Single();
        Assert.AreEqual(("made:1", 285), (owner.QueryThrownShot(shot)!.Value.Item, owner.QueryGold()), "the new pile flies, the rest stays in the counter");
        // ARC-COMBAT-2435: a throw at a tile rolls as a throw at a location, so it lands on the tile or, after a miss, on its scattered tile.
        var landing = thrown.Destination!.Value;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot, landing).Disposition);
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("made:1", landing), (placed.Entity, placed.Cell));
        Assert.AreEqual(0, owner.QueryItemDecays().Count, "a thrown pile schedules no decay");
        // The pickup turns it back into coins (HG4).
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "made:1", Accept).Disposition);
        Assert.AreEqual(385, owner.QueryGold());
    }

    [TestMethod]
    public void Throwing_every_coin_empties_the_counter_and_takes_the_grid_pile_with_it()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionFailure.None, owner.ThrowHeroItem(owner.Version, Throw(owner, 385)).Failure);
        Assert.AreEqual(0, owner.QueryGold());
        Assert.IsFalse(owner.QueryHeroGrid().Items.Any(item => item.Entity == SessionHeroGrid.GoldEntity));
    }

    [TestMethod]
    public void The_throw_is_refused_before_any_effect()
    {
        using var owner = Hero();
        var before = owner.Version;
        Assert.AreEqual(GameSessionFailure.AttackUnsupported, owner.ThrowHeroItem(owner.Version, Throw(owner, null)).Failure, "no count");
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.ThrowHeroItem(owner.Version, Throw(owner, 0)).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.ThrowHeroItem(owner.Version, Throw(owner, 5, new(SessionItemType.Ammo, 1, 0, 0))).Failure, "the split kind is gold");
        Assert.AreEqual(GameSessionFailure.GoldShort, owner.ThrowHeroItem(owner.Version, Throw(owner, 386)).Failure);
        Assert.AreEqual(before, owner.Version);
        Assert.AreEqual(385, owner.QueryGold());
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        using var tight = Hero(new GameSessionLimits(maxGoldRecords: 1));
        Assert.AreEqual(GameSessionDisposition.Changed, tight.DropHeroGold(tight.Version, 1, GoldPile).Disposition);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, tight.ThrowHeroItem(tight.Version, Throw(tight, 1)).Failure, "the record budget");
        using var broke = Hero();
        Assert.AreEqual(GameSessionFailure.None, broke.ThrowHeroItem(broke.Version, Throw(broke, 385)).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, broke.ThrowHeroItem(broke.Version, Throw(broke, 1)).Failure, "no coins: no pile");
    }

    [TestMethod]
    public void The_thrown_pile_survives_a_save_and_the_range_of_a_coin_is_that_of_a_weightless_item()
    {
        using var owner = Hero();
        Assert.IsNotNull(owner.QueryHeroThrowRange(SessionHeroGrid.GoldEntity, 100));
        var shot = owner.ThrowHeroItem(owner.Version, Throw(owner, 100)).Shots.Single();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LandThrownItem(owner.Version, shot, At(owner, 3)).Disposition);
        Assert.AreEqual(285, owner.QueryGold());
        Assert.AreEqual(100, owner.Capture().Inventory.Piles.Single(pile => pile.Entity == "made:1").Quantity);
    }

    [TestMethod]
    public void A_throw_at_a_critter_splits_the_coins_when_the_item_leaves()
    {
        using var owner = Hero();
        var rat = SessionCritterCombat.From("oid:rat", AttackStateTests.Npc(hpAdjustment: 5000)).Combat!;
        var thrown = owner.ThrowHeroItem(owner.Version, Throw(owner, 10) with { Struck = rat, StruckCell = At(owner, 3) });
        Assert.AreEqual(GameSessionFailure.None, thrown.Failure);
        Assert.AreEqual(375, owner.QueryGold());
        Assert.AreEqual("made:1", owner.QueryThrownShot(thrown.Shots[0])!.Value.Item);
    }
}
