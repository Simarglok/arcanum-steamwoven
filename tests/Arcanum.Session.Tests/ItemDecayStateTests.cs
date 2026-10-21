using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-062: the Item Decay event over the ground items the session places, with the half-elf fixture's kit and an apple the PC
// takes from the exterior's seed (as PlacedItemsStateTests). Values are invented.
[TestClass]
public sealed class ItemDecayStateTests
{
    private static readonly CellCoord AppleCell = new(90, 200), There = new(120, 210);
    private static readonly SessionInsertCheck Accept = _ => SessionInsertVerdict.Accepted;

    private sealed class World(bool shows = false) : ISessionLootWindowWorld
    {
        public bool LootWindowShowsContainer { get; set; } = shows;
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static GameSessionOwner Owner(bool appleNoDecay = false)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var facts = InventoryStateTests.Facts(SessionItemType.Food, 3600) with { NoDecay = appleNoDecay };
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([], [new SessionSeedItem("apple", facts, AppleCell)])).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        return owner;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    [TestMethod]
    public void ADroppedItemDecaysTwoGameDaysLaterUnlessPickedUp()
    {
        using var owner = Owner();
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        owner.PlaceHeroItem("kit:2", There);
        owner.PlaceHeroItem("apple", There);
        CollectionAssert.AreEqual(new[] { ("kit:2", Exterior, now + ItemDecayRules.DelayMs), ("apple", Exterior, now + ItemDecayRules.DelayMs) },
            owner.QueryItemDecays().ToArray());
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        Assert.AreEqual("kit:2", owner.QueryItemDecays().Single().Entity, "the pickup cancelled the apple's event");

        Advance(owner, ItemDecayRules.DelayMs - 1);
        Assert.AreEqual(0, owner.ProcessEvents(new World()).Decays.Count, "not due a millisecond early");
        Advance(owner, 1);
        var revision = owner.Version.Revision;
        var fired = owner.ProcessEvents(new World()).Decays.Single();
        Assert.AreEqual((Exterior, "kit:2", ItemDecayStep.Destroy), (fired.Map, fired.Entity, fired.Step));
        Assert.AreEqual(revision + 1, owner.Version.Revision, "one revision");
        Assert.IsTrue(owner.IsItemDestroyed("kit:2"));
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        Assert.AreEqual(0, owner.QueryMapInventory(Exterior)!.Ground.Count);
        CollectionAssert.AreEqual(new[] { "kit:1", "kit:3" }, owner.QueryHeroKit().Select(item => item.Entity).ToArray());
        Assert.AreEqual("apple", owner.QueryPcInventory().Single().Entity, "the apple the PC holds stays");
    }

    [TestMethod]
    public void ADropAgainRestartsTheTwoDays()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("apple", There);
        Advance(owner, 1000);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "apple", Accept).Disposition);
        owner.PlaceHeroItem("apple", There);
        var only = owner.QueryItemDecays().Single();
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + ItemDecayRules.DelayMs, only.DeadlineMs);
    }

    [TestMethod]
    public void TheLootWindowShowingAContainerDelaysTheDecayAMinuteEachTime()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:1", There);
        Advance(owner, ItemDecayRules.DelayMs);
        var world = new World(shows: true);
        Assert.AreEqual(ItemDecayStep.Retry, owner.ProcessEvents(world).Decays.Single().Step);
        Assert.IsFalse(owner.IsItemDestroyed("kit:1"));
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + ItemDecayRules.RetryDelayMs, owner.QueryItemDecays().Single().DeadlineMs);
        Advance(owner, ItemDecayRules.RetryDelayMs);
        Assert.AreEqual(ItemDecayStep.Retry, owner.ProcessEvents(world).Decays.Single().Step, "still shown: another minute");
        world.LootWindowShowsContainer = false;
        Advance(owner, ItemDecayRules.RetryDelayMs);
        Assert.AreEqual(ItemDecayStep.Destroy, owner.ProcessEvents(world).Decays.Single().Step);
        Assert.IsTrue(owner.IsItemDestroyed("kit:1"));
    }

    [TestMethod]
    public void AnItemOnAnotherMapDecaysWhenItsMapIsActiveAgain()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:3", There);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Start with { Map = new(80) }, []).Disposition);
        Advance(owner, ItemDecayRules.DelayMs + 5000);
        Assert.AreEqual(0, owner.ProcessEvents(new World()).Decays.Count, "its map is not active");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Start, []).Disposition);
        Assert.AreEqual(ItemDecayStep.Destroy, owner.ProcessEvents(new World()).Decays.Single().Step, "late, once its map is back");
    }

    [TestMethod]
    public void AnItemWithTheNoDecayFlagGetsNoEvent()
    {
        using var owner = Owner(appleNoDecay: true);
        owner.PlaceHeroItem("apple", There);
        Assert.AreEqual(0, owner.QueryItemDecays().Count);
    }

    [TestMethod]
    public void PendingDecaysAreSavedAndCheckedOnRestore()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        var deadline = $"\"kind\":\"item.decay\",\"deadlineMs\":{now + ItemDecayRules.DelayMs},";
        var values = $"\"values\":[{now},{ItemDecayRules.DelayMs}]";
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, deadline);
        StringAssert.Contains(text, values);
        using var restored = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryItemDecays().ToArray(), restored.QueryItemDecays().ToArray());
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()), "canonical");
        // A delay that is neither the decay's nor the retry's is refused, even with a deadline that matches it.
        var wrong = text.Replace(deadline, $"\"kind\":\"item.decay\",\"deadlineMs\":{now + 1000},").Replace(values, $"\"values\":[{now},1000]");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(Decode(wrong), RestoreOptions()).Failure);
        // So is a deadline that is not its time plus its delay.
        var late = text.Replace(deadline, $"\"kind\":\"item.decay\",\"deadlineMs\":{now + ItemDecayRules.DelayMs + 1},");
        Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(Decode(late), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void ADeletedItemLosesItsEvent()
    {
        using var owner = Owner();
        owner.PlaceHeroItem("kit:2", There);
        var deleted = owner.DestroyItem(owner.Version, "kit:2", new ItemCondition(10, 0, 0, false));
        Assert.AreEqual(ItemBreakStep.Deleted, deleted.Break);
        Assert.AreEqual(0, owner.QueryItemDecays().Count);
    }
}
