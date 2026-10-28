using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.InventoryStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-UI-029: the hero's drop of part or all of an ammunition stack on its own tile, after the quantity window (ledger QW5, IW16).
// The half-elf hero takes a stack of 30 bullets from virgil first (made:2). Entities and numbers are invented.
[TestClass]
public sealed class HeroAmmoDropStateTests
{
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemKind Bullets = new(SessionItemType.Ammo, 7059, 3301, 33301);

    private static GameSessionOwner Hero(GameSessionLimits? limits = null)
    {
        var owner = HeroStateTests.CreateWith(HeroStateTests.HalfElf(), limits);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        var npcStack = owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 30)).Item!;
        Assert.AreEqual("made:2", owner.TakeAmmoFromNpc(owner.Version, "virgil", npcStack, 30, Bullets, Accept).Item);
        return owner;
    }

    [TestMethod]
    public void Part_of_a_stack_lies_on_the_heros_tile_as_a_new_stack_and_the_rest_stays()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        var dropped = owner.DropHeroAmmo(owner.Version, "made:2", 12, Bullets);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, "made:3", revision + 1),
            (dropped.Disposition, dropped.Failure, dropped.Item, owner.Version.Revision), "one revision");
        Assert.AreEqual(owner.Version, dropped.Version);
        Assert.AreEqual((18, 12), (owner.QueryAmmo("made:2")!.Value.Units, owner.QueryAmmo("made:3")!.Value.Units));
        var placed = owner.QueryPlacedItems().Single();
        Assert.AreEqual(("made:3", SessionPlacedSource.Made, Exterior, Start.Cell, Bullets), (placed.Entity, placed.Source, placed.Map, placed.Cell, placed.Kind));
        CollectionAssert.AreEqual(new[] { "made:2" }, owner.QueryHeroAmmo().Select(stack => stack.Entity).ToArray(), "the rest stays in the hero's list");
        Assert.AreEqual("made:3", owner.QueryItemDecays().Single().Entity, "the drop's placement schedules the decay (ID4)");

        // The rest: the stack goes (a deletion) and a second new stack joins the first on the tile in a junk pile (JP1).
        Assert.AreEqual("made:4", owner.DropHeroAmmo(owner.Version, "made:2", 18, Bullets).Item);
        Assert.IsTrue(owner.IsItemDestroyed("made:2"));
        Assert.AreEqual(0, owner.QueryHeroAmmo().Count);
        CollectionAssert.AreEquivalent(new[] { "made:3", "made:4" }, owner.QueryJunkPiles().Single().Items.Select(item => item.Entity).ToArray());

        // Taking one back merges nothing (the hero has no stack now) and returns it to the made items with its units.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, owner.QueryJunkPiles().Single().Entity, "made:4", Accept).Disposition);
        Assert.AreEqual(18, owner.QueryHeroAmmo().Single(stack => stack.Entity == "made:4").Units);
    }

    [TestMethod]
    public void A_dropped_stack_survives_a_save()
    {
        using var owner = Hero();
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroAmmo(owner.Version, "made:2", 5, Bullets).Disposition);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text), RestoreOptions());
        Assert.AreEqual((25, 5), (restored.QueryAmmo("made:2")!.Value.Units, restored.QueryAmmo("made:3")!.Value.Units));
        Assert.AreEqual("made:3", restored.QueryPlacedItems().Single().Entity);
    }

    [TestMethod]
    public void Refusals_and_a_count_of_zero_change_nothing()
    {
        using var owner = Hero();
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.DropHeroAmmo(owner.Version, "made:2", 0, Bullets).Disposition, "a count of 0 moves nothing (QW5)");
        Assert.AreEqual(GameSessionFailure.AmmoShort, owner.DropHeroAmmo(owner.Version, "made:2", 31, Bullets).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.DropHeroAmmo(owner.Version, "made:2", -1, Bullets).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidAmmo, owner.DropHeroAmmo(owner.Version, "made:2", 1, Bullets with { Type = SessionItemType.Gold }).Failure);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.DropHeroAmmo(owner.Version, "kit:1", 1, Bullets).Failure, "not an ammunition stack");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.DropHeroAmmo(owner.Version, "made:9", 1, Bullets).Failure);
        Assert.AreEqual(GameSessionFailure.InvalidItem, owner.DropHeroAmmo(owner.Version, "", 1, Bullets).Failure);
        var stale = owner.Version with { Revision = owner.Version.Revision - 1 };
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.DropHeroAmmo(stale, "made:2", 1, Bullets).Failure);
        Assert.AreEqual(revision, owner.Version.Revision);
        Assert.AreEqual(30, owner.QueryAmmo("made:2")!.Value.Units);
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void The_budgets_count_the_new_stack_and_free_an_emptied_one()
    {
        // Two ammunition records allowed: the hero's stack and a second stack given to virgil fill them.
        using var owner = Hero(new GameSessionLimits(maxAmmoRecords: 2));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.AddItemToNpc(owner.Version, "virgil", Bullets, Accept, (1, 5)).Disposition);
        Assert.AreEqual(GameSessionFailure.BudgetExceeded, owner.DropHeroAmmo(owner.Version, "made:2", 10, Bullets).Failure, "part: two records");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DropHeroAmmo(owner.Version, "made:2", 30, Bullets).Disposition, "all: one replaces the other");
    }
}
