using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Locks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-050: the lock state owner of portals and containers (ledger LK3–LK5). Entities and flags are invented; the
// session clock starts at 15:00.
[TestClass]
public sealed class LockStateTests
{
    private const long T0 = 54_000_000, Hour = 3_600_000;
    private static readonly SessionOpenable Door = new(Exterior, "sector:1:7"), Chest = new(Exterior, "oid:chest");
    private static readonly SessionOpenableBaseline LockedDoor = new(OpenableKind.Portal, LockRules.LockedBit, 12, 0);
    private static readonly SessionOpenableBaseline Box = new(OpenableKind.Container, LockRules.LockedBit, 0, 0);

    private sealed class World : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private static void Advance(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.AdvanceGameTime(owner.QueryClockSnapshot().Token, ms, ClockAdvanceReason.OrdinaryAdvance).Disposition);

    [TestMethod]
    public void An_untouched_object_is_its_baseline()
    {
        using var owner = Create();
        Assert.AreEqual(new SessionOpenableState(LockRules.LockedBit, 0, true), owner.QueryOpenable(Door, LockedDoor));
        Assert.AreEqual(OpenAttempt.Locked, owner.AttemptOpen(Door, LockedDoor, false));
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(Door, LockedDoor, false, leaving: true));
        // Locked by night is open at the start hour.
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(Door, LockedDoor with { Flags = 0x41 }, false));
        Assert.AreEqual(0, owner.QueryMapChanges(Exterior).Count);
    }

    [TestMethod]
    public void Unlocking_records_the_state_and_the_lock_event_relocks_and_closes_an_hour_later()
    {
        using var owner = Create();
        Changed(owner.SetOpenableLocked(owner.Version, Door, LockedDoor, false), owner, 1);
        Changed(owner.SetOpenableFrame(owner.Version, Door, LockedDoor, 3), owner, 2);
        Assert.AreEqual(new SessionOpenableState(0, 3, false), owner.QueryOpenable(Door, LockedDoor));
        var pending = owner.QueryEvents().Single();
        Assert.AreEqual((SessionEventKinds.LockReset, T0 + Hour), (pending.Kind, pending.DeadlineMs));
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(Door, LockedDoor, false));
        Advance(owner, Hour - 1);
        Assert.AreEqual(0, owner.ProcessEvents(new World()).Locks.Count);
        Advance(owner, 1);
        var pass = owner.ProcessEvents(new World());
        var firing = pass.Locks.Single();
        Assert.AreEqual((Door, new SessionOpenableState(LockRules.LockedBit, 0, true)), (firing.Object, firing.State));
        Assert.AreEqual(firing.State, owner.QueryOpenable(Door, LockedDoor));
        Assert.AreEqual(0, owner.QueryEvents().Count);
        Assert.AreEqual(3, owner.Version.Revision);
    }

    [TestMethod]
    public void A_jam_lasts_a_day_and_its_event_also_locks_an_unlocked_object()
    {
        using var owner = Create();
        var free = Box with { Flags = 0 };
        Changed(owner.SetOpenableJammed(owner.Version, Chest, free, true), owner, 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetOpenableJammed(owner.Version, Chest, free, true).Disposition);
        Changed(owner.SetOpenableFrame(owner.Version, Chest, free, 1), owner, 2);
        Assert.AreEqual(OpenAttempt.Jammed, owner.AttemptOpen(Chest, free, false));
        Assert.AreEqual(T0 + 24 * Hour, owner.QueryEvents().Single().DeadlineMs);
        Advance(owner, 24 * Hour);
        Assert.AreEqual(1, owner.ProcessEvents(new World()).Locks.Count);
        // Locked, free of the jam, and the open container (frame 1) closed.
        Assert.AreEqual(new SessionOpenableState(LockRules.LockedBit, 0, true), owner.QueryOpenable(Chest, free));
    }

    [TestMethod]
    public void A_container_on_another_frame_stays_open_and_a_junk_pile_has_no_lock()
    {
        using var owner = Create();
        Changed(owner.SetOpenableLocked(owner.Version, Chest, Box, false), owner, 1);
        Changed(owner.SetOpenableFrame(owner.Version, Chest, Box, 2), owner, 2);
        Advance(owner, Hour);
        owner.ProcessEvents(new World());
        Assert.AreEqual(new SessionOpenableState(LockRules.LockedBit, 2, true), owner.QueryOpenable(Chest, Box));
        var junk = Box with { JunkPile = true };
        var pile = new SessionOpenable(Exterior, "oid:pile");
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.SetOpenableLocked(owner.Version, pile, junk, false).Disposition);
        Assert.AreEqual(OpenAttempt.Opens, owner.AttemptOpen(pile, junk, false));
    }

    [TestMethod]
    public void A_lock_event_waits_while_its_map_is_not_active()
    {
        using var owner = Create();
        Changed(owner.SetOpenableLocked(owner.Version, Door, LockedDoor, false), owner, 1);
        Changed(owner.PlaceActor(owner.Version, new(Cave, new(1, 1), Start.Facing)), owner, 2);
        Advance(owner, 2 * Hour);
        Assert.AreEqual(0, owner.ProcessEvents(new World()).Locks.Count);
        Assert.AreEqual(1, owner.QueryEvents().Count);
        Changed(owner.PlaceActor(owner.Version, Start), owner, 3);
        Assert.AreEqual(1, owner.ProcessEvents(new World()).Locks.Count);
        Assert.IsTrue(owner.QueryOpenable(Door, LockedDoor).Locked);
    }

    [TestMethod]
    public void The_state_and_the_pending_event_survive_a_save()
    {
        using var owner = Create();
        Changed(owner.SetOpenableLocked(owner.Version, Door, LockedDoor, false), owner, 1);
        Changed(owner.SetOpenableFrame(owner.Version, Door, LockedDoor, 1), owner, 2);
        var text = GameSessionSnapshotCodec.Encode(owner.Capture());
        using var restored = Restore(Decode(text));
        Assert.AreEqual(text, GameSessionSnapshotCodec.Encode(restored.Capture()));
        Assert.AreEqual(new SessionOpenableState(0, 1, false), restored.QueryOpenable(Door, LockedDoor));
        Advance(restored, Hour);
        Assert.AreEqual(1, restored.ProcessEvents(new World()).Locks.Count);
        Assert.AreEqual(new SessionOpenableState(LockRules.LockedBit, 0, true), restored.QueryOpenable(Door, LockedDoor));
    }

    [TestMethod]
    public void Invalid_objects_and_frames_are_refused()
    {
        using var owner = Create();
        Rejected(owner.SetOpenableFrame(owner.Version, Door, LockedDoor, 32), owner, GameSessionFailure.InvalidOpenable, 0);
        Rejected(owner.SetOpenableLocked(owner.Version, new(Exterior, "bad id"), LockedDoor, false), owner, GameSessionFailure.InvalidOpenable, 0);
        Rejected(owner.SetOpenableLocked(owner.Version, Door, LockedDoor with { JunkPile = true }, false), owner, GameSessionFailure.InvalidOpenable, 0);
        Rejected(owner.SetOpenableLocked(new(owner.Identity, 5), Door, LockedDoor, false), owner, GameSessionFailure.StaleVersion, 0);
    }
}
