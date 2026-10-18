using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

[TestClass]
public sealed class SessionOwnerTests
{
    [TestMethod]
    public void NewSessionOwnsClockFlagsActorAndNoMapChanges()
    {
        using var owner = Create();
        Assert.AreEqual(0, owner.Version.Revision);
        Assert.AreEqual(Start, owner.QueryActor());
        var clock = owner.QueryClockSnapshot();
        Assert.AreEqual(Profile(), clock.ProfileBinding);
        Assert.AreEqual(54_000_000, clock.GameTimestampMs);
        Assert.AreEqual(0, clock.Revision);
        Assert.AreEqual(false, ReadFlag(owner, 3));
        Assert.AreEqual(true, ReadFlag(owner, 7));
        Assert.IsNull(ReadFlag(owner, 9));
        Assert.AreEqual(Identity, owner.Flags.Binding.Identity);
        Assert.AreEqual(0, owner.QueryMapChanges(Exterior).Count);
        Assert.AreEqual(0, owner.Capture().Maps.Count);
    }

    [TestMethod]
    public void CreationRejectsBeforeAnyComponentEscapes()
    {
        void Rejects(GameSessionDescriptor? descriptor, GameSessionFailure failure,
            ClockFailure clock = ClockFailure.None, GlobalFlagReason flags = GlobalFlagReason.None)
        {
            var result = GameSessionOwner.Create(descriptor);
            Assert.IsNull(result.Owner);
            Assert.AreEqual(failure, result.Failure);
            Assert.AreEqual(clock, result.ClockFailure);
            Assert.AreEqual(flags, result.FlagReason);
        }

        Rejects(null, GameSessionFailure.InvalidDescriptor);
        Rejects(Descriptor() with { Flags = null }, GameSessionFailure.InvalidDescriptor);
        Rejects(Descriptor() with { Actor = Start with { Map = new(0) } }, GameSessionFailure.InvalidActor);
        Rejects(Descriptor() with { Actor = Start with { Cell = new(-1, 0) } }, GameSessionFailure.InvalidActor);
        Rejects(Descriptor() with { Actor = Start with { Facing = (NeighborDirection)8 } }, GameSessionFailure.InvalidActor);
        Rejects(Descriptor() with { Clock = Profile() with { ClockSchemaVersion = 2 } }, GameSessionFailure.ClockRejected, ClockFailure.InvalidSchema);
        Rejects(Descriptor() with { Clock = Profile(hour: 24) }, GameSessionFailure.ClockRejected, ClockFailure.InvalidProfile);
        Rejects(Descriptor() with { Flags = Flags() with { Domain = [] } }, GameSessionFailure.FlagsRejected, flags: GlobalFlagReason.InvalidDomain);
        Rejects(Descriptor() with { Flags = Flags(new(World, default)) }, GameSessionFailure.FlagsRejected, flags: GlobalFlagReason.InvalidIdentity);
    }

    [TestMethod]
    public void MapChangesSurviveLeavingForAnotherMapAndReturning()
    {
        using var owner = Create();
        var door = MapChangeKey.ForSector(5, "door.open");
        var neighbor = MapChangeKey.ForSector(6, "door.open");
        var npc = MapChangeKey.ForEntity("npc-1", "hp");
        Changed(owner.SetMapChange(owner.Version, Exterior, door, 1), owner, 1);
        Changed(owner.SetMapChange(owner.Version, Exterior, npc, 12), owner, 2);

        var caveEntry = new ActorPlacement(Cave, new(3, 4), NeighborDirection.YPlus);
        Changed(owner.PlaceActor(owner.Version, caveEntry), owner, 3);
        Assert.AreEqual(caveEntry, owner.QueryActor());
        Assert.AreEqual(0, owner.QueryMapChanges(Cave).Count);
        var torch = MapChangeKey.ForEntity("torch-2", "lit");
        Changed(owner.SetMapChange(owner.Version, Cave, torch, 0), owner, 4);

        Changed(owner.PlaceActor(owner.Version, Start with { Facing = NeighborDirection.XMinus }), owner, 5);
        Assert.AreEqual(1, owner.QueryMapChange(Exterior, door));
        Assert.AreEqual(12, owner.QueryMapChange(Exterior, npc));
        Assert.IsNull(owner.QueryMapChange(Exterior, neighbor));
        Assert.AreEqual(0, owner.QueryMapChange(Cave, torch));
        CollectionAssert.AreEqual(new[] { new MapChange(door, 1), new MapChange(npc, 12) }, owner.QueryMapChanges(Exterior).ToArray());

        // The same state survives a save boundary into a new owner.
        using var restored = Restore(Decode(GameSessionSnapshotCodec.Encode(owner.Capture())));
        Assert.AreEqual(Start with { Facing = NeighborDirection.XMinus }, restored.QueryActor());
        CollectionAssert.AreEqual(owner.QueryMapChanges(Exterior).ToArray(), restored.QueryMapChanges(Exterior).ToArray());
        CollectionAssert.AreEqual(owner.QueryMapChanges(Cave).ToArray(), restored.QueryMapChanges(Cave).ToArray());
        Assert.IsNull(restored.QueryMapChange(Exterior, neighbor));
    }

    [TestMethod]
    public void MutationsAreVersionGuardedAndCountEachSemanticChangeOnce()
    {
        using var owner = Create();
        using var other = Create();
        var key = MapChangeKey.ForSector(5, "door.open");
        Rejected(owner.SetMapChange(other.Version, Exterior, key, 1), owner, GameSessionFailure.StaleVersion, 0);
        Rejected(owner.SetMapChange(default, Exterior, key, 1), owner, GameSessionFailure.StaleVersion, 0);
        Rejected(owner.SetMapChange(new(owner.Identity, 1), Exterior, key, 1), owner, GameSessionFailure.StaleVersion, 0);
        Rejected(owner.PlaceActor(other.Version, Start), owner, GameSessionFailure.StaleVersion, 0);

        // Input validation precedes the version check.
        Rejected(owner.SetMapChange(default, new(0), key, 1), owner, GameSessionFailure.InvalidMap, 0);
        Rejected(owner.SetMapChange(default, Exterior, key with { Property = "" }, 1), owner, GameSessionFailure.InvalidChangeKey, 0);
        Rejected(owner.PlaceActor(default, Start with { Map = new(-1) }), owner, GameSessionFailure.InvalidActor, 0);

        var first = owner.Version;
        Changed(owner.SetMapChange(first, Exterior, key, 1), owner, 1);
        Rejected(owner.SetMapChange(first, Exterior, key, 2), owner, GameSessionFailure.StaleVersion, 1);
        var noChange = owner.SetMapChange(owner.Version, Exterior, key, 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, noChange.Disposition);
        Assert.AreEqual(1, noChange.Version.Revision);
        Changed(owner.SetMapChange(owner.Version, Exterior, key, -7), owner, 2);
        Assert.AreEqual(-7, owner.QueryMapChange(Exterior, key));
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.PlaceActor(owner.Version, Start).Disposition);

        Changed(owner.ClearMapChange(owner.Version, Exterior, key), owner, 3);
        Assert.IsNull(owner.QueryMapChange(Exterior, key));
        Assert.AreEqual(0, owner.Capture().Maps.Count, "a map back at its baseline is dropped");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ClearMapChange(owner.Version, Exterior, key).Disposition);
        Assert.AreEqual(3, owner.Version.Revision);
    }

    [TestMethod]
    public void ChangeKeysAcceptOnlyBoundedProjectCodes()
    {
        using var owner = Create();
        var invalid = new[]
        {
            new MapChangeKey(MapChangeScope.Sector, -1, null, "p"),
            new MapChangeKey(MapChangeScope.Sector, 5, "npc", "p"),
            new MapChangeKey(MapChangeScope.Entity, 5, "npc", "p"),
            new MapChangeKey(MapChangeScope.Entity, 0, null, "p"),
            new MapChangeKey((MapChangeScope)0, 0, null, "p"),
            MapChangeKey.ForSector(5, "door open"),
            MapChangeKey.ForSector(5, "дверь"),
            MapChangeKey.ForSector(5, new string('p', 65)),
            MapChangeKey.ForEntity(new string('e', 129), "p"),
            MapChangeKey.ForEntity("npc/1", "p"),
        };
        foreach (var key in invalid)
            Rejected(owner.SetMapChange(owner.Version, Exterior, key, 1), owner, GameSessionFailure.InvalidChangeKey, 0);
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForSector(long.MaxValue, new string('p', 64)), 1), owner, 1);
        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForEntity(new string('e', 128), "A.b_c:d-9"), 1), owner, 2);
    }

    [TestMethod]
    public void BudgetsRefuseNewKeysButAllowUpdatesAtCapacity()
    {
        using var maps = Create(new GameSessionLimits(maxMaps: 1));
        Changed(maps.SetMapChange(maps.Version, Exterior, MapChangeKey.ForSector(1, "p"), 1), maps, 1);
        Rejected(maps.SetMapChange(maps.Version, Cave, MapChangeKey.ForSector(1, "p"), 1), maps, GameSessionFailure.BudgetExceeded, 1);

        using var perMap = Create(new GameSessionLimits(maxChangesPerMap: 2));
        Changed(perMap.SetMapChange(perMap.Version, Exterior, MapChangeKey.ForSector(1, "p"), 1), perMap, 1);
        Changed(perMap.SetMapChange(perMap.Version, Exterior, MapChangeKey.ForSector(2, "p"), 1), perMap, 2);
        Rejected(perMap.SetMapChange(perMap.Version, Exterior, MapChangeKey.ForSector(3, "p"), 1), perMap, GameSessionFailure.BudgetExceeded, 2);
        Changed(perMap.SetMapChange(perMap.Version, Exterior, MapChangeKey.ForSector(2, "p"), 9), perMap, 3);
        Changed(perMap.SetMapChange(perMap.Version, Cave, MapChangeKey.ForSector(3, "p"), 1), perMap, 4);

        using var total = Create(new GameSessionLimits(maxTotalChanges: 2));
        Changed(total.SetMapChange(total.Version, Exterior, MapChangeKey.ForSector(1, "p"), 1), total, 1);
        Changed(total.SetMapChange(total.Version, Cave, MapChangeKey.ForSector(1, "p"), 1), total, 2);
        Rejected(total.SetMapChange(total.Version, Cave, MapChangeKey.ForSector(2, "p"), 1), total, GameSessionFailure.BudgetExceeded, 2);
        Changed(total.ClearMapChange(total.Version, Exterior, MapChangeKey.ForSector(1, "p")), total, 3);
        Changed(total.SetMapChange(total.Version, Cave, MapChangeKey.ForSector(2, "p"), 1), total, 4);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameSessionLimits(maxMaps: GameSessionLimits.PolicyMaxMaps + 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameSessionLimits(maxChangesPerMap: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new GameSessionLimits(maxTotalChanges: GameSessionLimits.PolicyMaxTotalChanges + 1));
    }

    // ARC-WORLD-017: a map transfer that carries followers is one atomic step.
    [TestMethod]
    public void ApplyChangesMovesTheActorAndSeveralMapChangesInOneRevisionOrNothing()
    {
        using var owner = Create();
        var cell = MapChangeKey.ForEntity("oid:npc", "npc.cell");
        var away = MapChangeKey.ForEntity("oid:npc", "npc.map");
        Changed(owner.SetMapChange(owner.Version, Exterior, cell, 7), owner, 1);
        var arrival = new ActorPlacement(Cave, new(2087, 2018), NeighborDirection.YPlus);
        Changed(owner.ApplyChanges(owner.Version, arrival, [new(Exterior, cell, null), new(Cave, cell, 9), new(Exterior, away, 80)]), owner, 2);
        Assert.AreEqual(arrival, owner.QueryActor());
        Assert.AreEqual((null, 9L, 80L), (owner.QueryMapChange(Exterior, cell), owner.QueryMapChange(Cave, cell), owner.QueryMapChange(Exterior, away)));
        // Nothing differs: NoChange, also with an empty list and without an actor; clearing an absent key is no change.
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ApplyChanges(owner.Version, arrival, [new(Cave, cell, 9), new(Exterior, cell, null)]).Disposition);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.ApplyChanges(owner.Version, null, []).Disposition);
        Assert.AreEqual(2, owner.Version.Revision);
        // Changes only: the actor stays.
        Changed(owner.ApplyChanges(owner.Version, null, [new(Exterior, away, null), new(Cave, cell, null)]), owner, 3);
        Assert.AreEqual((arrival, 0, 0), (owner.QueryActor(), owner.QueryMapChanges(Exterior).Count, owner.QueryMapChanges(Cave).Count));
        Assert.AreEqual(0, owner.Capture().Maps.Count, "a map left without changes is dropped");

        // Every refusal applies nothing: invalid actor, map or key, a key listed twice, a stale version.
        Rejected(owner.ApplyChanges(owner.Version, Start with { Map = new(0) }, [new(Exterior, cell, 1)]), owner, GameSessionFailure.InvalidActor, 3);
        Rejected(owner.ApplyChanges(owner.Version, Start, [new(new(0), cell, 1)]), owner, GameSessionFailure.InvalidMap, 3);
        Rejected(owner.ApplyChanges(owner.Version, Start, [new(Exterior, MapChangeKey.ForEntity("bad id", "p"), 1)]), owner, GameSessionFailure.InvalidChangeKey, 3);
        Rejected(owner.ApplyChanges(owner.Version, Start, [new(Exterior, cell, 1), new(Exterior, cell, null)]), owner, GameSessionFailure.InvalidChangeKey, 3);
        Rejected(owner.ApplyChanges(default, Start, [new(Exterior, cell, 1)]), owner, GameSessionFailure.StaleVersion, 3);
        Assert.AreEqual((arrival, 0), (owner.QueryActor(), owner.QueryMapChanges(Exterior).Count));
        Assert.ThrowsExactly<ArgumentNullException>(() => owner.ApplyChanges(owner.Version, null, null!));
    }

    [TestMethod]
    public void ApplyChangesChecksTheBudgetAfterTheWholeStep()
    {
        using var total = Create(new GameSessionLimits(maxTotalChanges: 2));
        var a = MapChangeKey.ForSector(1, "p"); var b = MapChangeKey.ForSector(2, "p"); var c = MapChangeKey.ForSector(3, "p");
        Changed(total.ApplyChanges(total.Version, null, [new(Exterior, a, 1), new(Exterior, b, 1)]), total, 1);
        // At capacity a move (clear one, add another) fits; a net new key does not, and nothing of it applies.
        Changed(total.ApplyChanges(total.Version, Start with { Map = Cave }, [new(Exterior, a, null), new(Cave, a, 1), new(Exterior, b, 5)]), total, 2);
        Rejected(total.ApplyChanges(total.Version, Start, [new(Exterior, c, 1), new(Cave, a, 2)]), total, GameSessionFailure.BudgetExceeded, 2);
        Assert.AreEqual((Cave, 1L, 5L, (long?)null), (total.QueryActor().Map, total.QueryMapChange(Cave, a)!.Value, total.QueryMapChange(Exterior, b)!.Value, total.QueryMapChange(Exterior, c)));

        using var maps = Create(new GameSessionLimits(maxMaps: 1));
        Changed(maps.ApplyChanges(maps.Version, null, [new(Exterior, a, 1)]), maps, 1);
        Rejected(maps.ApplyChanges(maps.Version, null, [new(Cave, a, 1)]), maps, GameSessionFailure.BudgetExceeded, 1);
        Changed(maps.ApplyChanges(maps.Version, null, [new(Exterior, a, null), new(Cave, a, 1)]), maps, 2);

        using var perMap = Create(new GameSessionLimits(maxChangesPerMap: 1));
        Rejected(perMap.ApplyChanges(perMap.Version, null, [new(Exterior, a, 1), new(Exterior, b, 1)]), perMap, GameSessionFailure.BudgetExceeded, 0);
        // No-op edits beside one that needs room: the room is still checked, and nothing applies.
        Changed(perMap.ApplyChanges(perMap.Version, null, [new(Exterior, a, 1)]), perMap, 1);
        Rejected(perMap.ApplyChanges(perMap.Version, Start with { Map = Cave }, [new(Exterior, a, 1), new(Cave, c, null), new(Exterior, b, 2)]), perMap, GameSessionFailure.BudgetExceeded, 1);
        Assert.AreEqual((Start, (long?)null), (perMap.QueryActor(), perMap.QueryMapChange(Exterior, b)));
    }

    [TestMethod]
    public void CaptureIsADetachedCoherentValue()
    {
        using var owner = GoldenSession();
        var token = owner.QueryClockSnapshot().Token;
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(token, 1_000, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(GlobalFlagWriteDisposition.Applied, WriteFlag(owner, 9, true).Disposition);
        var snapshot = owner.Capture();
        Assert.AreEqual(2, snapshot.Revision);
        Assert.AreEqual(54_001_000, snapshot.Clock.GameTimestampMs);
        Assert.AreEqual(1, snapshot.Clock.Revision);
        Assert.AreEqual(1, snapshot.Flags.Revision);
        CollectionAssert.AreEqual(new GlobalFlagEntry[] { new(new(3), false), new(new(7), true), new(new(9), true) }, snapshot.Flags.Entries.ToArray());

        Changed(owner.SetMapChange(owner.Version, Exterior, MapChangeKey.ForSector(5, "door.open"), 0), owner, 3);
        Changed(owner.PlaceActor(owner.Version, Start with { Map = Cave }), owner, 4);
        Assert.AreEqual(1, snapshot.Maps[0].Changes[0].Value);
        Assert.AreEqual(Start, snapshot.Actor);
        Assert.IsInstanceOfType<System.Collections.ObjectModel.ReadOnlyCollection<SessionMapState>>(snapshot.Maps);
        Assert.IsInstanceOfType<System.Collections.ObjectModel.ReadOnlyCollection<MapChange>>(snapshot.Maps[0].Changes);
        Assert.IsInstanceOfType<System.Collections.ObjectModel.ReadOnlyCollection<GlobalFlagEntry>>(snapshot.Flags.Entries);
    }

    [TestMethod]
    public void ClockForwardingKeepsTheClockContract()
    {
        using var owner = Create();
        var before = owner.QueryClockSnapshot();
        var paused = owner.SetPaused(before.Token, new(true, PauseReason.Menu));
        Assert.AreEqual(ClockMutationDisposition.Changed, paused.Disposition);
        var stale = owner.AdvanceGameTime(before.Token, 1, ClockAdvanceReason.OrdinaryAdvance);
        Assert.AreEqual(ClockFailure.StaleToken, stale.Failure);
        Assert.AreEqual(ClockMutationDisposition.NoChange,
            owner.AdvanceGameTime(paused.Snapshot.Token, 1, ClockAdvanceReason.OrdinaryAdvance).Disposition);
        Assert.AreEqual(0, owner.Version.Revision, "clock changes keep their own revision");
    }

    [TestMethod]
    public void DisposeEndsEveryComponentAndIsIdempotent()
    {
        var owner = Create();
        var flags = owner.Flags;
        var version = owner.FlagVersion;
        owner.Dispose();
        owner.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.Version);
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.Flags);
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.QueryActor());
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.QueryClockSnapshot());
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.Capture());
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.PlaceActor(default, Start));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.SetMapChange(default, Exterior, MapChangeKey.ForSector(1, "p"), 1));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.ClearMapChange(default, Exterior, MapChangeKey.ForSector(1, "p")));
        Assert.ThrowsExactly<ObjectDisposedException>(() => owner.QueryMapChanges(Exterior));
        var read = flags.Read(new(new(Guid.Parse("55555555-5555-5555-5555-555555555555")), version, new FlagId(3)));
        Assert.AreEqual(GlobalFlagReason.OwnerDisposed, read.Reason);
    }
}
