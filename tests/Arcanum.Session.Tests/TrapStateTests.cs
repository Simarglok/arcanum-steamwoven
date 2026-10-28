using Arcanum.Simulation.Traps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

// ARC-WORLD-076: the trap state owner (ledger TT1–TT9). Entities, scripts and flags are invented.
[TestClass]
public sealed class TrapStateTests
{
    private static readonly SessionTrapped Chest = new(Exterior, "oid:chest"), Plate = new(Exterior, "oid:plate");
    // An auto-removing nonmagical native trap on a chest, and a magical one on a trap object with a 5×5 square.
    private static readonly SessionTrapBaseline TrappedChest = new(TrapCarrier.TrappedObject, 30001,
        TrapRules.NonmagicalFlag | TrapRules.AutoRemoveFlag, 0, 0);
    private static readonly SessionTrapBaseline TrapPlate = new(TrapCarrier.TrapObject, 30000,
        TrapRules.MagicalFlag | TrapRules.AutoRemoveFlag | TrapRules.SquareTwoFlag, 0x05000000, TrapRules.DontDrawFlag);

    [TestMethod]
    public void An_untouched_trap_is_its_baseline()
    {
        using var owner = Create();
        Assert.AreEqual(new SessionTrapState(TrapClass.Nonmagical, 30001, 0, false), owner.QueryTrap(Chest, TrappedChest));
        Assert.IsFalse(owner.TrapKnownTo(Chest, TrappedChest, TrapObserver.Player));
        Assert.IsTrue(owner.TrapKnownTo(Chest, TrappedChest, TrapObserver.FreeNpc));
        Assert.AreEqual(TrapSpotGate.Roll, owner.TrapSpotGate(Chest, TrappedChest, TrapObserver.Player, 0));
        Assert.AreEqual(TrapSpotGate.NoTrap, owner.TrapSpotGate(Chest, TrappedChest with { HeaderFlags = 0 }, TrapObserver.Player, 0));
        Assert.AreEqual(0, owner.QueryMapChanges(Exterior).Count);
    }

    [TestMethod]
    public void A_spot_mark_is_recorded_once_and_only_for_the_pc_side()
    {
        using var owner = Create();
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.FreeNpc).Disposition);
        Changed(owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.LedNpc), owner, 1);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.Player).Disposition);
        // The trap object turns drawn.
        Assert.AreEqual(new SessionTrapState(TrapClass.Magical, 30000, TrapRules.SpottedFlag, false), owner.QueryTrap(Plate, TrapPlate));
        Assert.IsTrue(owner.TrapKnownTo(Plate, TrapPlate, TrapObserver.Player));
        Assert.AreEqual(TrapSpotGate.Known, owner.TrapSpotGate(Plate, TrapPlate, TrapObserver.Player, 0));
        Assert.AreEqual(1L, owner.QueryMapChanges(Exterior).Single(c => c.Key.Property == GameSessionOwner.TrapStateProperty).Value);
        Assert.AreEqual(GameSessionDisposition.NoChange, owner.MarkTrapSpotted(owner.Version, Chest, TrappedChest with { Script = 0 }, TrapObserver.Player).Disposition);
    }

    [TestMethod]
    public void A_use_that_spots_the_trap_is_cancelled_and_a_known_trap_fires_and_removes_itself()
    {
        using var owner = Create();
        var spotted = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, spottedNow: true);
        Assert.AreEqual(SessionTrapOutcome.Spotted, spotted.Outcome);
        Changed(spotted.Mutation, owner, 1);
        Assert.IsTrue(spotted.State.Spotted);
        // Known now, so a spot attempt is not read: the trap fires and its auto-removal clears it.
        var fired = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, spottedNow: true);
        Assert.AreEqual(SessionTrapOutcome.Fired, fired.Outcome);
        Changed(fired.Mutation, owner, 2);
        Assert.AreEqual(new NativeTrapRun(30001, NativeTrapEffect.Attack, 0, true, 3, 4, 5), fired.Run);
        Assert.AreEqual(new SessionTrapState(TrapClass.None, 0, TrapRules.SpottedFlag, true), fired.State);
        var after = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, spottedNow: false);
        Assert.AreEqual((SessionTrapOutcome.NoTrap, GameSessionDisposition.NoChange), (after.Outcome, after.Mutation.Disposition));
        // A chest is not destroyed by its trap.
        Assert.AreEqual(SessionObjectCondition.Whole, owner.QueryObjectCondition(Exterior, Chest.Entity, Arcanum.Simulation.Combat.DamageableObjectKind.Container));
    }

    [TestMethod]
    public void A_free_npc_that_spots_a_pc_trap_cancels_its_use_but_marks_nothing()
    {
        using var owner = Create();
        var placed = TrappedChest with { ObjectFlags = TrapRules.PlacedByPcFlag };
        var result = owner.TriggerTrap(owner.Version, Chest, placed, TrapObserver.FreeNpc, SessionTrapCause.Use, spottedNow: true);
        Assert.AreEqual((SessionTrapOutcome.Spotted, GameSessionDisposition.NoChange), (result.Outcome, result.Mutation.Disposition));
        Assert.IsFalse(result.State.Spotted);
    }

    [TestMethod]
    public void A_step_fires_a_trap_object_unless_the_mover_floats_and_its_removal_destroys_the_object()
    {
        using var owner = Create();
        var over = owner.TriggerTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, SessionTrapCause.Step, false, moverSpellFlags: 0x2);
        Assert.AreEqual((SessionTrapOutcome.SteppedOver, GameSessionDisposition.NoChange), (over.Outcome, over.Mutation.Disposition));
        var fired = owner.TriggerTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, SessionTrapCause.Step, false, moverSpellFlags: 0x1);
        Assert.AreEqual(SessionTrapOutcome.Fired, fired.Outcome);
        Changed(fired.Mutation, owner, 1);
        Assert.AreEqual(new NativeTrapRun(30000, NativeTrapEffect.Spell, 2, true, 0, 1, 2), fired.Run);
        Assert.IsTrue(fired.State.Removed);
        Assert.AreEqual(SessionObjectCondition.Destroyed, owner.QueryObjectCondition(Exterior, Plate.Entity, Arcanum.Simulation.Combat.DamageableObjectKind.Trap));
    }

    [TestMethod]
    public void An_attack_fires_the_trap_without_a_spot_and_never_removes_it()
    {
        using var owner = Create();
        var fired = owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Attack, spottedNow: true);
        Assert.AreEqual((SessionTrapOutcome.Fired, GameSessionDisposition.NoChange), (fired.Outcome, fired.Mutation.Disposition));
        Assert.IsNotNull(fired.Run);
        Assert.IsTrue(owner.QueryTrap(Chest, TrappedChest).Trapped);
    }

    [TestMethod]
    public void An_ordinary_trap_script_fires_for_the_host_to_run_and_stays()
    {
        using var owner = Create();
        var ordinary = TrappedChest with { Script = 1234 };
        var fired = owner.TriggerTrap(owner.Version, Chest, ordinary, TrapObserver.Player, SessionTrapCause.Use, spottedNow: false);
        Assert.AreEqual((SessionTrapOutcome.Fired, GameSessionDisposition.NoChange, (NativeTrapRun?)null),
            (fired.Outcome, fired.Mutation.Disposition, fired.Run));
    }

    [TestMethod]
    public void Disarming_removes_a_known_trap_yields_the_device_or_springs_it()
    {
        using var owner = Create();
        var unknown = owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, true, true, 3);
        Assert.AreEqual((TrapDisarm.NotKnown, GameSessionDisposition.NoChange), (unknown.Outcome, unknown.Mutation.Disposition));
        Changed(owner.MarkTrapSpotted(owner.Version, Chest, TrappedChest, TrapObserver.Player), owner, 1);
        var failed = owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, false, false, 3);
        Assert.AreEqual((TrapDisarm.Failed, GameSessionDisposition.NoChange), (failed.Outcome, failed.Mutation.Disposition));
        var device = owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, true, true, 2);
        Assert.AreEqual((TrapDisarm.RemovedWithDevice, 6047), (device.Outcome, device.DevicePrototype));
        Changed(device.Mutation, owner, 2);
        Assert.IsTrue(device.State.Removed);
        Assert.AreEqual(TrapDisarm.NotTrapped, owner.DisarmTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, true, false, 3).Outcome);

        // A critical failure springs the trap as a use: an auto-removing one clears.
        using var second = Create();
        Changed(second.MarkTrapSpotted(second.Version, Plate, TrapPlate, TrapObserver.Player), second, 1);
        var sprung = second.DisarmTrap(second.Version, Plate, TrapPlate, TrapObserver.Player, false, true, 0);
        Assert.AreEqual(TrapDisarm.Sprung, sprung.Outcome);
        Changed(sprung.Mutation, second, 2);
        Assert.AreEqual(NativeTrapEffect.Spell, sprung.Run!.Value.Effect);
        Assert.IsTrue(sprung.State.Removed);
    }

    [TestMethod]
    public void The_state_survives_a_snapshot_and_bad_input_is_refused()
    {
        using var owner = Create();
        Changed(owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate, TrapObserver.Player), owner, 1);
        Changed(owner.TriggerTrap(owner.Version, Chest, TrappedChest, TrapObserver.Player, SessionTrapCause.Use, false).Mutation, owner, 2);
        using var restored = Restore(owner.Capture());
        Assert.AreEqual(owner.QueryTrap(Plate, TrapPlate), restored.QueryTrap(Plate, TrapPlate));
        Assert.AreEqual(owner.QueryTrap(Chest, TrappedChest), restored.QueryTrap(Chest, TrappedChest));
        Assert.IsTrue(restored.QueryTrap(Chest, TrappedChest).Removed);

        Rejected(owner.MarkTrapSpotted(owner.Version, new(Exterior, "bad id"), TrapPlate, TrapObserver.Player), owner, GameSessionFailure.InvalidTrap, 2);
        Rejected(owner.MarkTrapSpotted(owner.Version, Plate, TrapPlate with { Script = -1 }, TrapObserver.Player), owner, GameSessionFailure.InvalidTrap, 2);
        Rejected(owner.TriggerTrap(owner.Version, Plate, TrapPlate, TrapObserver.Player, (SessionTrapCause)9, false).Mutation, owner,
            GameSessionFailure.InvalidTrap, 2);
        Rejected(owner.DisarmTrap(owner.Version, Plate, TrapPlate with { Carrier = (TrapCarrier)0 }, TrapObserver.Player, true, false, 0).Mutation,
            owner, GameSessionFailure.InvalidTrap, 2);
        var stale = new GameSessionVersion(owner.Identity, 1);
        Rejected(owner.TriggerTrap(stale, Plate, TrapPlate, TrapObserver.Player, SessionTrapCause.Use, false).Mutation, owner, GameSessionFailure.StaleVersion, 2);
        Assert.ThrowsExactly<ArgumentException>(() => owner.QueryTrap(new(Exterior, ""), TrapPlate));
    }
}
