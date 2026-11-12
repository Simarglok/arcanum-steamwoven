using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>ARC-WORLD-1158 (E2-b, ledger PO10): a dead NPC's Decay Dead Bodies event one game day on: its inventory is laid on the ground (or destroyed when not droppable) and the critter is destroyed. Invented numbers only.</summary>
[TestClass]
public sealed class BodyDecayStateTests
{
    private const int Prototype = 28359;
    private static readonly CellCoord Body = new(101, 202);
    private static readonly LifeFacts Plain = new() { HitPointPoints = 10, FatiguePoints = 10 };

    private sealed class World(CellCoord? cell, LifeFacts? facts = null) : ISessionBodyWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => facts ?? Plain;
        public CellCoord? BodyCell(SessionMapId map, string entity) => cell;
    }

    private static SessionLifeTarget Target(string entity, LifeFacts? facts = null) => SessionLifeTarget.Critter(entity, facts ?? Plain, LifeState.Unharmed);

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static string MadeFor(GameSessionOwner owner, string holder)
    {
        var entity = owner.NextObjectEntity(SessionObjectOrigin.Made)!;
        owner.CreateMadeItem(new(entity, new(SessionItemType.Food, 1000, 1, 30001), SessionCreatePlace.NpcList, new SessionItemWeight(5, 0, 0), 0, holder));
        return entity;
    }

    private static string Critter(GameSessionOwner owner)
    {
        var made = owner.CreateObject(owner.Version, SessionCreatedKind.Critter, Prototype, Exterior, Body).Object!;
        return made.Entity;
    }

    [TestMethod]
    public void TheDeathOfAnNpcQueuesTheEventOneGameDayOnAndANewOneReplacesIt()
    {
        using var owner = Create();
        var critter = Critter(owner);
        var now = owner.QueryClockSnapshot().GameTimestampMs;
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Target(critter)).Disposition);
        CollectionAssert.AreEqual(new[] { (critter, Exterior, now + BloodPoolRules.CritterDecayDelayMs) }, owner.QueryBodyDecays().ToArray());
        owner.ScheduleBodyDecay(critter, Exterior.RuntimeMap, now + 5, Plain);
        Assert.AreEqual(1, owner.QueryBodyDecays().Count, "one per critter");
    }

    [TestMethod]
    public void ACritterWithTheNoDecayFlagQueuesNone()
    {
        using var owner = Create();
        var critter = Critter(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.Kill(owner.Version, Target(critter, Plain with { Traits = LifeTraits.NoBodyDecay })).Disposition);
        Assert.AreEqual(0, owner.QueryBodyDecays().Count);
    }

    [TestMethod]
    public void ADeadCreatedCritterDropsItsItemsAndIsDestroyed()
    {
        using var owner = Create();
        var critter = Critter(owner);
        var first = MadeFor(owner, critter);
        var second = MadeFor(owner, critter);
        owner.Kill(owner.Version, Target(critter));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs - 1);
        Assert.AreEqual(0, owner.ProcessEvents(new World(Body)).BodyDecays.Count, "not due a millisecond early");
        Advance(owner, 1);
        var fired = owner.ProcessEvents(new World(Body)).BodyDecays.Single();
        Assert.AreEqual((Exterior, critter, SessionBodyDecayOutcome.Removed, 2, 0), (fired.Map, fired.Entity, fired.Outcome, fired.Dropped, fired.Destroyed));
        Assert.IsNull(owner.QueryCreatedObject(critter), "the critter is destroyed");
        Assert.AreEqual(0, owner.QueryBodyDecays().Count);
        CollectionAssert.AreEquivalent(new[] { first, second }, owner.QueryPlacedItems().Select(item => item.Entity).ToArray());
        Assert.IsTrue(owner.QueryPlacedItems().All(item => item.Map == Exterior && item.Cell == Body));
        Assert.IsTrue(owner.QueryItemDecays().Count >= 2 || owner.QueryJunkPiles().Count > 0, "the dropped items decay as any drop");
    }

    [TestMethod]
    public void AMapNpcsSeedItemsLieOnTheGroundAndANotDroppableOneIsDestroyed()
    {
        using var owner = Create();
        var facts = InventoryStateTests.Facts(SessionItemType.Food, 3600);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("npc-1", SessionHolderKind.Npc, false, false, [], ["bread", "cursed"])],
            [new SessionSeedItem("bread", facts, null), new SessionSeedItem("cursed", facts with { NoDrop = true }, null)])).Failure);
        owner.Kill(owner.Version, Target("npc-1"));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
        var fired = owner.ProcessEvents(new World(Body)).BodyDecays.Single();
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 1, 1), (fired.Outcome, fired.Dropped, fired.Destroyed));
        Assert.AreEqual(1L, owner.QueryMapChange(Exterior, MapChangeKey.ForEntity("npc-1", GameSessionOwner.ObjectDestroyedProperty)), "a map NPC's destroy is the script destroy's map change");
        var ground = owner.QueryMapInventory(Exterior)!.Ground.Single();
        Assert.AreEqual(("bread", Body), (ground.Item.Entity, ground.Cell));
        Assert.IsTrue(owner.IsItemDestroyed("cursed"));
        Assert.AreEqual("bread", owner.QueryItemDecays().Single().Entity, "the dropped item decays");
    }

    [TestMethod]
    public void ACritterThatIsAliveAgainIsLeftAsItIs()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.ScheduleBodyDecay(critter, Exterior.RuntimeMap, owner.QueryClockSnapshot().GameTimestampMs, Plain);
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
        // No life record of the critter's own: the event finds no death and is consumed with no action.
        var pass = owner.ProcessEvents(new World(Body));
        Assert.AreEqual(0, owner.QueryBodyDecays().Count);
        Assert.IsNotNull(owner.QueryCreatedObject(critter));
        Assert.AreEqual(0, pass.BodyDecays.Count + owner.QueryPlacedItems().Count);
    }

    [TestMethod]
    public void WithoutAPlaceForTheBodyTheEventWaitsAMinute()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("npc-1", SessionHolderKind.Npc, false, false, [], [])], [])).Failure);
        owner.Kill(owner.Version, Target("npc-1"));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
        var fired = owner.ProcessEvents(new World(null)).BodyDecays.Single();
        Assert.AreEqual(SessionBodyDecayOutcome.Postponed, fired.Outcome);
        var pending = owner.QueryBodyDecays().Single();
        Assert.AreEqual(owner.QueryClockSnapshot().GameTimestampMs + ItemDecayRules.RetryDelayMs, pending.DeadlineMs);
        Advance(owner, ItemDecayRules.RetryDelayMs);
        Assert.AreEqual(SessionBodyDecayOutcome.Removed, owner.ProcessEvents(new World(Body)).BodyDecays.Single().Outcome);
    }

    [TestMethod]
    public void AnEventOnAnotherMapWaitsUntilItsMapIsActive()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.Kill(owner.Version, Target(critter));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ApplyChanges(owner.Version, Start with { Map = new(80) }, []).Disposition);
        Advance(owner, BloodPoolRules.CritterDecayDelayMs + 5000);
        Assert.AreEqual(0, owner.ProcessEvents(new World(Body)).BodyDecays.Count, "its map is not active");
    }

    [TestMethod]
    public void ThePendingEventIsSavedAndRestored()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.Kill(owner.Version, Target(critter));
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, "\"kind\":\"body.decay\"");
        using var back = Restore(Decode(text));
        CollectionAssert.AreEqual(owner.QueryBodyDecays().ToArray(), back.QueryBodyDecays().ToArray());
        // The restored session fires it as the original does: a day after the death.
        Advance(back, BloodPoolRules.CritterDecayDelayMs);
        Assert.AreEqual(SessionBodyDecayOutcome.Removed, back.ProcessEvents(new World(Body)).BodyDecays.Single().Outcome);
        // A saved event naming a critter with no life record is refused.
        var wrong = text.Replace("\"entity\":\"oid:critter:1\"}],\"values\":[" + owner.QueryClockSnapshot().GameTimestampMs + ",86400000]", "\"entity\":\"oid:ghost:1\"}],\"values\":[" + owner.QueryClockSnapshot().GameTimestampMs + ",86400000]");
        Assert.AreNotEqual(text, wrong);
        Assert.AreEqual(GameSessionFailure.InvalidEvents, GameSessionOwner.Restore(Decode(wrong), RestoreOptions()).Failure);
    }

    [TestMethod]
    public void ADestroyOfTheCreatedCritterRemovesItsPendingEvent()
    {
        using var owner = Create();
        var critter = Critter(owner);
        owner.Kill(owner.Version, Target(critter));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyCreatedObject(owner.Version, critter).Disposition);
        Assert.AreEqual(0, owner.QueryBodyDecays().Count);
    }

    [TestMethod]
    public void AnItemTheHeroGaveTheBodyIsDestroyedWithItAndTheSaveStillRestores()
    {
        using var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("npc-1", SessionHolderKind.Npc, false, false, [], [])], [])).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "npc-1", "kit:2", _ => SessionInsertVerdict.Accepted).Disposition);
        var made = MadeFor(owner, "npc-1");
        owner.Kill(owner.Version, Target("npc-1"));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
        var fired = owner.ProcessEvents(new World(Body)).BodyDecays.Single();
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 1, 1), (fired.Outcome, fired.Dropped, fired.Destroyed), "the made item lies down, the given kit item goes with the body");
        Assert.IsTrue(owner.IsItemDestroyed("kit:2"));
        CollectionAssert.AreEqual(new[] { made }, owner.QueryPlacedItems().Select(item => item.Entity).ToArray());
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        CollectionAssert.AreEqual(new[] { made }, back.QueryPlacedItems().Select(item => item.Entity).ToArray());
    }
}
