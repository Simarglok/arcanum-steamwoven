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

    // ARC-WORLD-1299 (VP24, VPG4): the walk destroys exactly an item with spell flag 0x4000 or item flag 0x10; a no-drop item (0x20) lies down like any other.
    [TestMethod]
    public void AMapNpcsSeedItemsLieOnTheGroundExceptTheSummonedAndTheHiddenOnesAndANoDropItemIsNotLost()
    {
        using var owner = Create();
        var facts = InventoryStateTests.Facts(SessionItemType.Food, 3600);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("npc-1", SessionHolderKind.Npc, false, false, [], ["bread", "cursed", "ghost", "hidden", "both"])],
            [new SessionSeedItem("bread", facts, null), new SessionSeedItem("cursed", facts with { NoDrop = true }, null),
                new SessionSeedItem("ghost", facts with { Summoned = true }, null), new SessionSeedItem("hidden", facts with { NoDisplay = true }, null),
                new SessionSeedItem("both", facts with { Summoned = true, NoDisplay = true, NoDrop = true }, null)])).Failure);
        owner.Kill(owner.Version, Target("npc-1"));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
        var fired = owner.ProcessEvents(new World(Body)).BodyDecays.Single();
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 2, 3), (fired.Outcome, fired.Dropped, fired.Destroyed));
        Assert.AreEqual(1L, owner.QueryMapChange(Exterior, MapChangeKey.ForEntity("npc-1", GameSessionOwner.ObjectDestroyedProperty)), "a map NPC's destroy is the script destroy's map change");
        foreach (var lying in new[] { "bread", "cursed" })
            Assert.IsNotNull(owner.QueryMapChange(Exterior, MapChangeKey.ForEntity(lying, GameSessionOwner.ItemDroppedProperty)), lying + " lies on the ground (a no-drop item is dropped like any other)");
        foreach (var gone in new[] { "ghost", "hidden", "both" })
            Assert.IsNull(owner.QueryMapChange(Exterior, MapChangeKey.ForEntity(gone, GameSessionOwner.ItemDroppedProperty)), gone);
        foreach (var gone in new[] { "ghost", "hidden", "both" }) Assert.IsTrue(owner.IsItemDestroyed(gone), gone);
        Assert.IsFalse(owner.IsItemDestroyed("bread") || owner.IsItemDestroyed("cursed"));
        CollectionAssert.AreEquivalent(new[] { "bread", "cursed" }, owner.QueryItemDecays().Select(decay => decay.Entity).ToArray(), "the dropped items decay");
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

    private static GameSessionOwner HeroWithNpc(params SessionSeedItem[] ground)
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new(
            [new SessionSeedHolder("npc-1", SessionHolderKind.Npc, false, false, [], [])], ground)).Failure);
        return owner;
    }

    private static SessionBodyDecayFiring Decay(GameSessionOwner owner)
    {
        owner.Kill(owner.Version, Target("npc-1"));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
        return owner.ProcessEvents(new World(Body)).BodyDecays.Single();
    }

    private static void RestoresTheSame(GameSessionOwner owner)
    {
        using var back = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        CollectionAssert.AreEquivalent(owner.QueryPlacedItems().ToArray(), back.QueryPlacedItems().ToArray());
    }

    // ARC-WORLD-1299 (BD3's limit lifted): an item the hero gave the body is laid down as a placed kit or taken item, a made item as a created one, and the save restores.
    [TestMethod]
    public void AnItemTheHeroGaveTheBodyLiesOnTheGroundAndTheSaveRestores()
    {
        using var owner = HeroWithNpc(new SessionSeedItem("gem", InventoryStateTests.Facts(SessionItemType.Generic, 5600), new CellCoord(101, 200)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "gem", _ => SessionInsertVerdict.Accepted).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "npc-1", "kit:2", _ => SessionInsertVerdict.Accepted).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "npc-1", "gem", _ => SessionInsertVerdict.Accepted).Disposition);
        var made = MadeFor(owner, "npc-1");
        var fired = Decay(owner);
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 3, 0), (fired.Outcome, fired.Dropped, fired.Destroyed));
        var placed = owner.QueryPlacedItems().OrderBy(item => item.Entity, StringComparer.Ordinal).ToArray();
        CollectionAssert.AreEqual(new[] { ("gem", SessionPlacedSource.Pc), ("kit:2", SessionPlacedSource.Kit), (made, SessionPlacedSource.Created) },
            placed.Select(item => (item.Entity, item.Source)).ToArray());
        Assert.AreEqual(Exterior, placed.Single(item => item.Entity == "gem").Origin, "a taken item remembers its seed");
        Assert.IsFalse(owner.IsItemDestroyed("kit:2") || owner.IsItemDestroyed("gem"));
        Assert.AreEqual(0, owner.QueryNpcInventory("npc-1").Count, "nothing is left in the body's list");
        RestoresTheSame(owner);
    }

    // A taken item with spell flag 0x4000 or item flag 0x10 in its origin seed is destroyed when its holder's body decays.
    [TestMethod]
    public void AGivenTakenItemWithTheSummonedOrNoDisplayFlagInItsSeedIsDestroyed()
    {
        var plain = InventoryStateTests.Facts(SessionItemType.Generic, 5600);
        using var owner = HeroWithNpc(new SessionSeedItem("ghost", plain with { Summoned = true }, new CellCoord(101, 200)),
            new SessionSeedItem("hidden", plain with { NoDisplay = true }, new CellCoord(101, 200)), new SessionSeedItem("kept", plain, new CellCoord(101, 200)));
        foreach (var entity in new[] { "ghost", "hidden", "kept" })
        {
            Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, entity, _ => SessionInsertVerdict.Accepted).Disposition, entity);
            Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "npc-1", entity, _ => SessionInsertVerdict.Accepted).Disposition, entity);
        }
        var fired = Decay(owner);
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 1, 2), (fired.Outcome, fired.Dropped, fired.Destroyed));
        CollectionAssert.AreEqual(new[] { "kept" }, owner.QueryPlacedItems().Select(item => item.Entity).ToArray());
        Assert.IsTrue(owner.IsItemDestroyed("ghost") && owner.IsItemDestroyed("hidden"));
        RestoresTheSame(owner);
    }

    // The coins an NPC holds (a made gold pile with its record) lie on the ground as a coin pile the hero can pick up, as a hero's own gold drop; the save restores.
    [TestMethod]
    public void TheCoinsOfTheBodyLieOnTheGroundAsAPileThatMergesIntoTheHeroGold()
    {
        using var owner = HeroWithNpc();
        var pileKind = new SessionItemKind(SessionItemType.Gold, 9056, 3200, 33200);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToNpcFromNothing(owner.Version, "npc-1", 40, pileKind, _ => SessionInsertVerdict.Accepted).Disposition);
        var gold = owner.QueryGold();
        var fired = Decay(owner);
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 1, 0), (fired.Outcome, fired.Dropped, fired.Destroyed));
        var pile = owner.QueryPlacedItems().Single();
        Assert.AreEqual((SessionItemType.Gold, SessionPlacedSource.Made, Exterior, Body), (pile.Kind.Type, pile.Source, pile.Map, pile.Cell));
        RestoresTheSame(owner);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, pile.Entity, _ => SessionInsertVerdict.Accepted).Disposition);
        Assert.AreEqual(gold + 40, owner.QueryGold());
    }

    // An ammunition stack the body holds (taken from the world and given) lies on the ground with its units, and the save restores it.
    [TestMethod]
    public void AnAmmunitionStackOfTheBodyLiesOnTheGroundWithItsUnits()
    {
        var arrows = new SessionItemFacts(new(SessionItemType.Ammo, 1500, 1500, 31500), 1, 0, 0, 20, []) { AmmoType = 0 };
        using var owner = HeroWithNpc(new SessionSeedItem("arrows", arrows, new CellCoord(101, 200)));
        Assert.AreEqual(GameSessionDisposition.Changed, owner.PickUpItem(owner.Version, "arrows", _ => SessionInsertVerdict.Accepted).Disposition);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveItemToNpc(owner.Version, "npc-1", "arrows", _ => SessionInsertVerdict.Accepted).Disposition);
        var fired = Decay(owner);
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 1, 0), (fired.Outcome, fired.Dropped, fired.Destroyed));
        Assert.AreEqual("arrows", owner.QueryPlacedItems().Single().Entity);
        Assert.AreEqual(20, owner.QueryAmmo("arrows")?.Units);
        RestoresTheSame(owner);
        var back = owner.PickUpItem(owner.Version, "arrows", _ => SessionInsertVerdict.Accepted);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (back.Disposition, back.Failure));
    }

    // The budgets come first: a body whose made items do not fit the created items' bound waits a minute and nothing is placed, destroyed or written.
    [TestMethod]
    public void ABodyWhoseItemsExceedTheCreatedItemsBoundWaitsAMinuteWithNothingChanged()
    {
        using var owner = Create(new GameSessionLimits(maxCreatedItems: 1));
        var critter = Critter(owner);
        MadeFor(owner, critter);
        MadeFor(owner, critter);
        owner.Kill(owner.Version, Target(critter));
        Advance(owner, BloodPoolRules.CritterDecayDelayMs);
        var fired = owner.ProcessEvents(new World(Body)).BodyDecays.Single();
        Assert.AreEqual(SessionBodyDecayOutcome.Postponed, fired.Outcome);
        Assert.AreEqual(0, owner.QueryPlacedItems().Count);
        Assert.IsNotNull(owner.QueryCreatedObject(critter), "the body stays");
        Assert.AreEqual(1, owner.QueryBodyDecays().Count, "the event is scheduled again");
    }

    // A session with no hero has no ground record for a given item or a coin pile: they go with the body (the documented limit); a made item still lies down.
    [TestMethod]
    public void WithoutAHeroTheCoinsOfTheBodyAreDestroyedWithItAndAMadeItemLiesDown()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, new([new SessionSeedHolder("npc-1", SessionHolderKind.Npc, false, false, [], [])], [])).Failure);
        var pileKind = new SessionItemKind(SessionItemType.Gold, 9056, 3200, 33200);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.GiveGoldToNpcFromNothing(owner.Version, "npc-1", 40, pileKind, _ => SessionInsertVerdict.Accepted).Disposition);
        var made = MadeFor(owner, "npc-1");
        var fired = Decay(owner);
        Assert.AreEqual((SessionBodyDecayOutcome.Removed, 1, 1), (fired.Outcome, fired.Dropped, fired.Destroyed));
        CollectionAssert.AreEqual(new[] { made }, owner.QueryPlacedItems().Select(item => item.Entity).ToArray());
        RestoresTheSame(owner);
    }
}
