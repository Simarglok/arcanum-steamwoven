using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-063: the junk piles the session's drops make, with the half-elf fixture's kit (kit:1..kit:3) and an apple (two cells
// wide) the PC takes from the exterior's seed, as PlacedItemsStateTests. Values are invented.
[TestClass]
public sealed class JunkPileStateTests
{
    private static readonly CellCoord AppleCell = new(90, 200), There = new(120, 210), Beside = new(121, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;
    private static readonly SessionItemFacts AppleFacts = InventoryStateTests.Facts(SessionItemType.Food, 3600) with { InventoryWidth = 2 };

    private sealed class World : ISessionLootWindowWorld
    {
        public bool LootWindowShowsContainer => false;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static SessionMapInventorySeed Seed(SessionItemFacts? apple = null) => new([], [new SessionSeedItem("apple", apple ?? AppleFacts, AppleCell)]);

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, Seed()).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        return owner;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static long Now(GameSessionOwner owner) => owner.QueryClockSnapshot().GameTimestampMs;

    private static string Text(GameSessionOwner owner) => GameSessionSnapshotCodec.Encode(owner.Capture());

    [TestMethod]
    public void TheKitItemsAnywhereListHeldPlacedAndPiledKitItemsButNoTakenOne()
    {
        // ARC-UI-036: the host rebuilds the kit's facts by kit number from this list, so a dropped kit item keeps its facts.
        using var owner = Owner();
        var all = owner.QueryKitItemsAnywhere().Select(item => item.Entity).ToArray();
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2", "kit:3" }, all);
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("kit:3", Beside);
        CollectionAssert.AreEqual(new[] { "kit:1" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        CollectionAssert.AreEqual(all, owner.QueryKitItemsAnywhere().Select(item => item.Entity).ToArray(), "placed kit items are still listed");
        Assert.AreEqual(owner.QueryKitItemsAnywhere()[1].Kind, Kit[1].Kind);
        Assert.IsFalse(owner.QueryKitItemsAnywhere().Any(item => item.Entity == "apple"), "an item taken from the world is not a kit item");
    }

    [TestMethod]
    public void ASecondDropOnACellGathersBothIntoAPileAndALaterOneJoinsIt()
    {
        using var owner = Owner();
        var first = Now(owner);
        owner.PlaceHeroItem("kit:2", There);
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "one item lies loose");
        Advance(owner, 1000);
        var revision = owner.Version.Revision;
        owner.PlaceHeroItem("apple", There);
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision for the drop and its pile");
        var pile = owner.QueryJunkPiles().Single();
        Assert.AreEqual(("junk:1", Exterior, There), (pile.Entity, pile.Map, pile.Cell));
        CollectionAssert.AreEqual(new[] { new SessionJunkPileItem("kit:2", 0, 1, 1), new SessionJunkPileItem("apple", 1, 2, 1) }, pile.Items.ToArray(),
            "the loose item first, then the dropped one, each at its first free slot with its size");
        var view = owner.QueryMapInventory(Exterior)!;
        Assert.AreEqual(0, view.Ground.Count, "items in a pile are not on the ground");
        CollectionAssert.AreEqual(new[] { "kit:2", "apple" }, view.JunkPiles.Single().Items.Select(item => item.Entity).ToArray());
        // The swept item keeps its time (in a pile it still decays), the dropped one starts its two days.
        CollectionAssert.AreEqual(new[] { ("kit:2", first + ItemDecayRules.DelayMs), ("apple", Now(owner) + ItemDecayRules.DelayMs) },
            owner.QueryItemDecays().Select(d => (d.Entity, d.DeadlineMs)).ToArray());

        owner.PlaceHeroItem("kit:3", There);
        CollectionAssert.AreEqual(new[] { ("kit:2", 0), ("apple", 1), ("kit:3", 3) }, owner.QueryJunkPiles().Single().Items.Select(item => (item.Entity, item.Slot)).ToArray());
        owner.PlaceHeroItem("kit:1", Beside);
        Assert.AreEqual(("kit:1", Beside), owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single(), "another cell: loose");
        Assert.AreEqual(1, owner.QueryJunkPiles().Count);
    }

    [TestMethod]
    public void ThePileIsLootedAndDissolvesWhenTheLootWindowCloses()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("apple", There);
        owner.PlaceHeroItem("kit:3", There);
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.PickUpItem(owner.Version, "apple", Accept).Failure, "not on the ground");
        Assert.AreEqual(GameSessionFailure.ItemNotAvailable, owner.LootItem(owner.Version, "junk:1", "kit:1", Accept).Failure);
        Assert.AreEqual(GameSessionFailure.InsertRefused, owner.LootItem(owner.Version, "junk:1", "apple", _ => SessionInsertVerdict.TooHeavy).Failure);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.CloseJunkPile(owner.Version, "junk:1").Disposition, "three items: it stays");

        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "junk:1", "apple", Accept).Disposition);
        Assert.AreEqual("apple", owner.QueryPcInventory().Single().Entity);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "junk:1", "kit:2", Accept).Disposition);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:2" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        CollectionAssert.AreEqual(new[] { "kit:3" }, owner.QueryItemDecays().Select(d => d.Entity).ToArray(), "the loot ended their decay");
        Assert.AreEqual("kit:3", owner.QueryJunkPiles().Single().Items.Single().Entity, "the pile stands while the window shows it");

        // On another map the pile cannot be closed or looted.
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Start with { Map = new(80) }, []).Disposition);
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.CloseJunkPile(owner.Version, "junk:1").Failure);
        Assert.AreEqual(GameSessionFailure.InventoryUnavailable, owner.LootItem(owner.Version, "junk:1", "kit:3", Accept).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Start, []).Disposition);

        Advance(owner, 5000);
        var revision = owner.Version.Revision;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:1").Disposition);
        Assert.AreEqual(revision + 1, owner.Version.Revision);
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.AreEqual(("kit:3", There), owner.QueryMapInventory(Exterior)!.Ground.Select(g => (g.Item.Entity, g.Cell)).Single(), "dropped again on the cell");
        Assert.AreEqual(Now(owner) + ItemDecayRules.DelayMs, owner.QueryItemDecays().Single().DeadlineMs, "with a new decay event");
        Assert.AreEqual(GameSessionFailure.UnknownHolder, owner.CloseJunkPile(owner.Version, "junk:1").Failure);
        Assert.AreEqual(GameSessionFailure.StaleVersion, owner.CloseJunkPile(owner.Version with { Revision = 0 }, "junk:1").Failure);

        // A pile emptied while shown is removed on closing.
        owner.PlaceHeroItem("kit:2", There);
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, "junk:2", "kit:3", Accept).Failure,
            "a new pile takes the number after the highest ever given, a dissolved pile's is not given again (MIG6, TM1)");
        Assert.AreEqual(GameSessionFailure.None, owner.LootItem(owner.Version, "junk:2", "kit:2", Accept).Failure);
        Assert.AreEqual(0, owner.QueryJunkPiles().Single().Items.Count);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.CloseJunkPile(owner.Version, "junk:2").Disposition);
        Assert.AreEqual(0, owner.QueryJunkPiles().Count);
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count);
    }

    [TestMethod]
    public void AnItemThatDecaysInAPileOfTwoDissolvesIt()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        Advance(owner, 1000);
        owner.PlaceHeroItem("apple", There);
        Advance(owner, ItemDecayRules.DelayMs - 1000);
        var fired = owner.ProcessEvents(new World()).Decays.Single();
        Assert.AreEqual(("kit:2", ItemDecayStep.Destroy), (fired.Entity, fired.Step));
        Assert.IsTrue(owner.IsItemDestroyed("kit:2"));
        Assert.AreEqual(0, owner.QueryJunkPiles().Count, "left with the apple, the pile dissolved");
        Assert.AreEqual("apple", owner.QueryMapInventory(Exterior)!.Ground.Single().Item.Entity);
        Assert.AreEqual(Now(owner) + ItemDecayRules.DelayMs, owner.QueryItemDecays().Single().DeadlineMs, "the apple's two days start over");
    }

    [TestMethod]
    public void AnItemWithAScriptTheSessionDoesNotRunStaysLoose()
    {
        // The apple's insert-item script would run on its insertion into a pile, its get script when a new pile sweeps it.
        using var inserting = Owner();
        // The seed attached again shows the taken apple's script (a seed is host data).
        Assert.AreEqual(GameSessionFailure.None, inserting.AttachMapInventory(Exterior, Seed(AppleFacts with { ScriptPoints = [SessionScriptPoint.InsertItem] })).Failure);
        inserting.PlaceHeroItem("kit:2", There);
        inserting.PlaceHeroItem("apple", There);
        Assert.AreEqual(0, inserting.QueryJunkPiles().Count);
        Assert.AreEqual(2, inserting.QueryMapInventory(Exterior)!.Ground.Count);

        var getting = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        using (getting)
        {
            Assert.AreEqual(GameSessionFailure.None, getting.AttachMapInventory(Exterior, new([], [new SessionSeedItem("apple",
                AppleFacts with { ScriptPoints = [SessionScriptPoint.Get] }, There)])).Failure);
            getting.PlaceHeroItem("kit:2", There);
            var pile = getting.QueryJunkPiles().Single();
            CollectionAssert.AreEqual(new[] { "kit:2" }, pile.Items.Select(item => item.Entity).ToArray(), "the apple stays beside the new pile");
            Assert.AreEqual("apple", getting.QueryMapInventory(Exterior)!.Ground.Single().Item.Entity);
        }
    }

    [TestMethod]
    public void PilesAreSavedCanonicallyAndASaveNoDropMakesIsRefused()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("apple", There);
        var text = Text(owner);
        const string Pile = "\"junkPiles\":[{\"entity\":\"junk:1\",\"map\":79,\"x\":120,\"y\":210,\"items\":[{\"entity\":\"kit:2\",\"slot\":0,\"width\":1,\"height\":1},"
            + "{\"entity\":\"apple\",\"slot\":1,\"width\":2,\"height\":1}]}]";
        StringAssert.Contains(text, Pile);
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryJunkPiles().Select(p => (p.Entity, p.Map, p.Cell)).ToArray(), restored.QueryJunkPiles().Select(p => (p.Entity, p.Map, p.Cell)).ToArray());
        Assert.AreEqual(text, Text(restored), "canonical");
        Assert.AreEqual(GameSessionDisposition.Changed, restored.LootItem(restored.Version, "junk:1", "kit:2", Accept).Disposition, "a kit item needs no seed");

        void Fails(string changed, SnapshotDecodeFailure failure, string path)
        {
            var decoded = GameSessionSnapshotCodec.TryDecode(changed);
            Assert.AreEqual((failure, path), (decoded.Failure, decoded.Path));
        }
        Fails(text.Replace(Pile, Pile.Replace("\"width\":2", "\"width\":11")), SnapshotDecodeFailure.OutOfRange, "$.objects.junkPiles[0].items[1].width");
        Fails(text.Replace(Pile, Pile.Replace("\"slot\":1", "\"slot\":960")), SnapshotDecodeFailure.OutOfRange, "$.objects.junkPiles[0].items[1].slot");
        Fails(text.Replace(Pile, Pile.Replace("\"map\":79", "\"map\":0")), SnapshotDecodeFailure.OutOfRange, "$.objects.junkPiles[0].map");
        Fails(text.Replace(Pile, Pile.Replace(",\"height\":1}]", "}]")), SnapshotDecodeFailure.MissingField, "$.objects.junkPiles[0].items[1].height");

        void Refused(string changed, GameSessionFailure failure) =>
            Assert.AreEqual(failure, GameSessionOwner.Restore(Decode(changed), RestoreOptions()).Failure);
        // Each refused for its own reason: overlapping slots, a rectangle past the last column, a bad pile entity, two piles of one
        // entity, an item in two piles, a placed item away from its pile's cell, a deleted item, a kit item the hero holds.
        Refused(text.Replace(Pile, Pile.Replace("\"slot\":1", "\"slot\":0")), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace(Pile, Pile.Replace("\"slot\":1", "\"slot\":9")), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace(Pile, Pile.Replace("junk:1", "junk:x")), GameSessionFailure.InvalidSnapshot);
        var second = Pile["\"junkPiles\":[".Length..^1];
        Refused(text.Replace(Pile, Pile[..^1] + "," + second.Replace("kit:2", "kit:9").Replace("apple", "pear") + "]"), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace(Pile, Pile[..^1] + "," + second.Replace("junk:1", "junk:2").Replace("\"apple\",\"slot\":1", "\"pear\",\"slot\":1") + "]"), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace(Pile, Pile.Replace("\"x\":120", "\"x\":121")), GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace("\"destroyed\":[]", "\"destroyed\":[\"pear\"]").Replace(Pile, Pile.Replace("\"kit:2\",\"slot\":0", "\"pear\",\"slot\":0")),
            GameSessionFailure.InvalidSnapshot);
        Refused(text.Replace(Pile, Pile.Replace("\"kit:2\",\"slot\":0", "\"kit:1\",\"slot\":0")), GameSessionFailure.InvalidSnapshot);
    }

    [TestMethod]
    public void ASeedMustListThePilesWorldItemsAndNoOtherMapsSeedMay()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        using (owner)
        {
            Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [new SessionSeedItem("apple", AppleFacts, There)])).Failure);
            owner.PlaceHeroItem("kit:2", There);
            Assert.AreEqual(2, owner.QueryJunkPiles().Single().Items.Count, "the apple lying there joined the new pile");
            using var restored = Restore(Decode(Text(owner)));
            Assert.AreEqual(GameSessionFailure.InventoryMismatch, restored.AttachMapInventory(new(80), new([], [new SessionSeedItem("apple", AppleFacts, There)])).Failure);
            Assert.AreEqual(GameSessionFailure.InventoryMismatch, restored.AttachMapInventory(Exterior, new([], [])).Failure);
            Assert.AreEqual(GameSessionFailure.None, restored.AttachMapInventory(Exterior, new([], [new SessionSeedItem("apple", AppleFacts, There)])).Failure);
            Assert.AreEqual(GameSessionDisposition.Changed, restored.LootItem(restored.Version, "junk:1", "apple", Accept).Disposition);
            Assert.AreEqual(new SessionPcItem("apple", Exterior, AppleFacts.Kind), restored.QueryPcInventory().Single());
        }
    }

    [TestMethod]
    public void AFullPileIsPassedOverAndAnItemTooBigLiesBesideOrLeavesAnEmptyPile()
    {
        // The apple fills a whole pile (10 × 96 cells): a new pile takes it, the kit item dropped after it lies beside the pile.
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        using (owner)
        {
            Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [new SessionSeedItem("apple",
                AppleFacts with { InventoryWidth = 10, InventoryHeight = 96 }, There)])).Failure);
            owner.PlaceHeroItem("kit:2", There);
            CollectionAssert.AreEqual(new[] { ("junk:1", "apple") }, owner.QueryJunkPiles().SelectMany(p => p.Items.Select(i => (p.Entity, i.Entity))).ToArray());
            Assert.AreEqual("kit:2", owner.QueryMapInventory(Exterior)!.Ground.Single().Item.Entity, "too big for the pile: beside it");
            // The full pile is passed over: the next drop makes a second pile with the loose kit item.
            owner.PlaceHeroItem("kit:3", There);
            CollectionAssert.AreEqual(new[] { ("junk:1", "apple"), ("junk:2", "kit:2"), ("junk:2", "kit:3") },
                owner.QueryJunkPiles().SelectMany(p => p.Items.Select(i => (p.Entity, i.Entity))).ToArray());
            Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count);
        }

        // Nothing fits a new pile (both eleven cells wide): it stays empty, as in the original, and both lie loose; a later drop joins it.
        var empty = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        using (empty)
        {
            var wide = AppleFacts with { InventoryWidth = 11 };
            Assert.AreEqual(GameSessionFailure.None, empty.AttachMapInventory(Exterior, new([],
                [new SessionSeedItem("apple", wide, AppleCell), new SessionSeedItem("rock", wide with { Kind = wide.Kind with { Prototype = 4601 } }, There)])).Failure);
            Assert.AreEqual(GameSessionDisposition.Changed, empty.PickUpItem(empty.Version, "apple", Accept).Disposition);
            empty.PlaceHeroItem("apple", There);
            Assert.AreEqual(("junk:1", 0), empty.QueryJunkPiles().Select(p => (p.Entity, p.Items.Count)).Single());
            CollectionAssert.AreEquivalent(new[] { "rock", "apple" }, empty.QueryMapInventory(Exterior)!.Ground.Select(g => g.Item.Entity).ToArray());
            empty.PlaceHeroItem("kit:1", There);
            Assert.AreEqual(("junk:1", "kit:1"), empty.QueryJunkPiles().Select(p => (p.Entity, p.Items.Single().Entity)).Single());
        }
    }

    [TestMethod]
    public void ASeedItemDroppedFromAWornSlotIsLootedFromItsPileSlotAndABadSizeIsRefused()
    {
        // A sword the map data lays on the cell with a worn location (as an NPC's dropped weapon keeps it) is swept into a pile.
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        using (owner)
        {
            Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([],
                [new SessionSeedItem("sword", InventoryStateTests.Facts(SessionItemType.Weapon, 2001, 1004), There)])).Failure);
            owner.PlaceHeroItem("kit:2", There);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.LootItem(owner.Version, "junk:1", "sword", Accept).Disposition, "in the pile it lies in its slot");
            Assert.AreEqual("sword", owner.QueryPcInventory().Single().Entity);
            Assert.AreEqual(GameSessionFailure.InvalidInventorySeed, owner.AttachMapInventory(new(80), new([],
                [new SessionSeedItem("pebble", AppleFacts with { InventoryWidth = 0 }, There)])).Failure, "a size below one cell");
            Assert.AreEqual(GameSessionFailure.InvalidInventorySeed, owner.AttachMapInventory(new(80), new([],
                [new SessionSeedItem("pebble", AppleFacts with { InventoryHeight = SessionItemFacts.MaxInventoryCells + 1 }, There)])).Failure);
        }
    }
}
