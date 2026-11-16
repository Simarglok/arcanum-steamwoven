using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.HeroStateTests;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-MAGIC-2400 (ledger BC10, TL33): the recharge event of a worn mana store lives on the map the PC was on when it was scheduled. When the PC changes map it travels with its item (the PC's own, a follower's) and any
// other event stays parked on the old map and fires late on the PC's return, with the elapsed time (a larger refill). The half-elf fixture's kit:2 is the hero's staff; "stone" lies on the exterior's ground,
// "sword" is a seed item of the NPC "smith" (a follower), "dagger" of the NPC "clerk" (who stays). Values are invented.
[TestClass]
public sealed class ManaRechargeMapTests
{
    private const string Staff = "kit:2", Stone = "stone", Sword = "sword", Dagger = "dagger";
    private static readonly ActorPlacement InCave = new(Cave, new(1, 1), Start.Facing);

    private sealed class World : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static GameSessionOwner Owner()
    {
        var owner = GameSessionOwner.Create(Descriptor() with { Hero = HalfElf() }).Owner!;
        var facts = InventoryStateTests.Facts(SessionItemType.Generic, 3601);
        var seed = new SessionMapInventorySeed(
            [new SessionSeedHolder("smith", SessionHolderKind.Npc, false, false, [], [Sword]), new SessionSeedHolder("clerk", SessionHolderKind.Npc, false, false, [], [Dagger])],
            [new SessionSeedItem(Stone, facts, new(91, 200)), new SessionSeedItem(Sword, facts, null), new SessionSeedItem(Dagger, facts, null)]);
        Assert.AreEqual(GameSessionFailure.None, owner.AttachMapInventory(Exterior, seed).Failure);
        Assert.AreEqual(GameSessionDisposition.Changed, owner.ForceJoinParty(owner.Version, "smith").Disposition);
        foreach (var item in new[] { Staff, Stone, Sword, Dagger })
            Assert.AreEqual(GameSessionDisposition.Changed, owner.WriteItemManaStore(owner.Version, item, 30, 10).Disposition, item);
        return owner;
    }

    private static void Advance(GameSessionOwner owner, long ms) => Assert.AreEqual(ClockMutationDisposition.Changed,
        owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    private static void Move(GameSessionOwner owner, ActorPlacement to) => Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, to).Disposition);

    [TestMethod]
    public void A_new_event_is_on_the_map_the_pc_is_on()
    {
        using var owner = Owner();
        owner.ScheduleManaRecharge(Staff, 6, merge: true);
        CollectionAssert.AreEqual(new[] { Exterior.RuntimeMap }, owner.QueryManaRechargeMaps(Staff).ToArray());
    }

    [TestMethod]
    public void The_pcs_own_item_and_a_followers_item_take_their_events_along_and_a_stay_at_home_npcs_stays_parked()
    {
        using var owner = Owner();
        foreach (var item in new[] { Staff, Sword, Dagger, Stone }) owner.ScheduleManaRecharge(item, 100, merge: true);
        Move(owner, InCave);
        CollectionAssert.AreEqual(new[] { Cave.RuntimeMap }, owner.QueryManaRechargeMaps(Staff).ToArray(), "the PC's own item");
        CollectionAssert.AreEqual(new[] { Cave.RuntimeMap }, owner.QueryManaRechargeMaps(Sword).ToArray(), "the follower's item");
        CollectionAssert.AreEqual(new[] { Exterior.RuntimeMap }, owner.QueryManaRechargeMaps(Dagger).ToArray(), "an NPC who stays");
        CollectionAssert.AreEqual(new[] { Exterior.RuntimeMap }, owner.QueryManaRechargeMaps(Stone).ToArray(), "an item on the ground");
        Advance(owner, 480_000);
        owner.ProcessEvents(new World());
        Assert.AreEqual(18, owner.QueryItemManaStore(Staff), "on the cave it fires on time: 2 × 4 units");
        Assert.AreEqual(18, owner.QueryItemManaStore(Sword));
        Assert.AreEqual(10, owner.QueryItemManaStore(Dagger), "parked: nothing happens to it on another map");
        Assert.AreEqual(10, owner.QueryItemManaStore(Stone));
    }

    [TestMethod]
    public void A_parked_event_fires_late_on_the_return_with_the_larger_refill()
    {
        using var owner = Owner();
        owner.ScheduleManaRecharge(Stone, 100, merge: true);
        Move(owner, InCave);
        Advance(owner, 7_200_000);
        owner.ProcessEvents(new World());
        Assert.AreEqual(10, owner.QueryItemManaStore(Stone), "still parked after two game hours");
        Assert.AreEqual(1, owner.QueryManaRecharges().Count);
        Move(owner, Start);
        owner.ProcessEvents(new World());
        Assert.AreEqual(110, owner.QueryItemManaStore(Stone), "elapsed 7,200 s: 60 units, min(100, 120) = all 100 at once (on time it would have been 8)");
        Assert.AreEqual(0, owner.QueryManaRecharges().Count);
    }

    [TestMethod]
    public void A_payment_merges_only_into_an_event_of_the_map_it_is_on_and_a_destroyed_item_loses_every_event()
    {
        using var owner = Owner();
        owner.ScheduleManaRecharge(Stone, 5, merge: true);
        Move(owner, InCave);
        owner.ScheduleManaRecharge(Stone, 7, merge: true);
        CollectionAssert.AreEqual(new[] { Exterior.RuntimeMap, Cave.RuntimeMap }, owner.QueryManaRechargeMaps(Stone).ToArray(), "the parked one is not in memory to merge into");
        CollectionAssert.AreEqual(new[] { 5, 7 }, owner.QueryManaRecharges().Select(e => e.Pending).Order().ToArray());
        owner.ScheduleManaRecharge(Stone, 3, merge: true);
        CollectionAssert.AreEqual(new[] { 5, 10 }, owner.QueryManaRecharges().Select(e => e.Pending).Order().ToArray(), "the event of this map took the amount");
        Assert.AreEqual(GameSessionDisposition.Changed, owner.DestroyItem(owner.Version, Stone, new(20, 0, 0, false)).Disposition);
        Assert.AreEqual(0, owner.QueryManaRecharges().Count);
    }

    [TestMethod]
    public void The_map_is_saved_and_restored_and_a_save_without_one_is_refused()
    {
        using var owner = Owner();
        owner.ScheduleManaRecharge(Stone, 5, merge: true);
        Move(owner, InCave);
        owner.ScheduleManaRecharge(Staff, 6, merge: true);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        StringAssert.Contains(text, $"\"entities\":[{{\"map\":{Exterior.RuntimeMap},\"entity\":\"{Stone}\"}}]");
        StringAssert.Contains(text, $"\"entities\":[{{\"map\":{Cave.RuntimeMap},\"entity\":\"{Staff}\"}}]");
        var restored = GameSessionOwner.Restore(Decode(text), RestoreOptions()).Owner!;
        using var _ = restored;
        CollectionAssert.AreEqual(new[] { Exterior.RuntimeMap }, restored.QueryManaRechargeMaps(Stone).ToArray());
        CollectionAssert.AreEqual(new[] { Cave.RuntimeMap }, restored.QueryManaRechargeMaps(Staff).ToArray());
        Assert.AreNotEqual(SnapshotDecodeFailure.None, GameSessionSnapshotCodec.TryDecode(text.Replace($"\"map\":{Exterior.RuntimeMap},\"entity\":\"{Stone}\"", $"\"map\":0,\"entity\":\"{Stone}\"")).Failure);
    }

    [TestMethod]
    public void A_schema_103_save_with_a_placeholder_map_puts_its_events_on_the_map_the_pc_was_on()
    {
        using var owner = Owner();
        owner.ScheduleManaRecharge(Staff, 6, merge: true);
        var current = GameSessionSnapshotCodec.Encode(owner.Capture());
        var entity = $"\"map\":{Exterior.RuntimeMap},\"entity\":\"{Staff}\"";
        StringAssert.Contains(current, entity);
        var older = current.Replace(entity, $"\"map\":1,\"entity\":\"{Staff}\"").Replace($"\"schemaVersion\":{GameSessionSnapshot.CurrentSchemaVersion},\"world\"", "\"schemaVersion\":103,\"world\"");
        var upgraded = GameSessionSnapshotCodec.TryDecode(older);
        Assert.IsNotNull(upgraded.Snapshot, $"{upgraded.Failure} {upgraded.Path}");
        Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, upgraded.Snapshot.SchemaVersion);
        Assert.AreEqual(Exterior.RuntimeMap, upgraded.Snapshot.Events.Pending.Single(e => e.Kind == SessionEventKinds.ItemRecharge).Entities[0].Map);
    }
}
