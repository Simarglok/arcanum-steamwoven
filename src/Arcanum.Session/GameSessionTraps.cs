using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>An object that may carry a trap: its runtime map and its session entity id (a trap object or a trapped object).</summary>
public readonly record struct SessionTrapped(SessionMapId Map, string Entity);

/// <summary>
/// The T0 trap facts of one object, read by the host from its map data: whether it is a trap object (type 17) or another
/// object, its use script (slot 1: number and counters), the script file's header flags and the object flags (field 0x13,
/// of which the trap state reads the PC-placed and spotted bits).
/// </summary>
public readonly record struct SessionTrapBaseline(TrapCarrier Carrier, int Script, uint HeaderFlags, int Counters, int ObjectFlags);

/// <summary>An object's current trap state: its trap class (None once removed), its use script's number (0 once removed),
/// its object flags with the spotted mark, and whether the trap was removed (a removed trap object is destroyed).</summary>
public readonly record struct SessionTrapState(TrapClass Class, int Script, int ObjectFlags, bool Removed)
{
    public bool Trapped => Class != TrapClass.None;
    public bool Spotted => (ObjectFlags & TrapRules.SpottedFlag) != 0;
    public bool PlacedByPc => (ObjectFlags & TrapRules.PlacedByPcFlag) != 0;
}

/// <summary>How a trap meets its triggerer (ledger TT7–TT9).</summary>
public enum SessionTrapCause
{
    /// <summary>A use of the object (the object entry point at the use point): the spot attempt first, then the run, whose
    /// auto-removal is written back.</summary>
    Use,
    /// <summary>A mover arrives on the trap object's tile: as a use, unless the mover's spell flags keep it off the trap.</summary>
    Step,
    /// <summary>An attack brings the object to 0 hit points (ledger OD4): the run with no spot attempt and no write-back,
    /// so an auto-removing trap stays.</summary>
    Attack,
}

/// <summary>What one trap meeting did.</summary>
public enum SessionTrapOutcome
{
    /// <summary>The object carries no trap (or it was removed): the use goes on as an ordinary one.</summary>
    NoTrap,
    /// <summary>A step by a mover with spell flag 0x2 or 0x4: nothing runs.</summary>
    SteppedOver,
    /// <summary>The triggerer spotted the trap now: it is marked when the triggerer is on the PC's side, and the use is
    /// cancelled (skip default).</summary>
    Spotted,
    /// <summary>The trap went off: a native run (<see cref="SessionTrapResult.Run"/>), or an ordinary script the host runs
    /// through the dispatcher when the run is null.</summary>
    Fired,
}

/// <summary>One trap meeting: the session's mutation, the outcome, the native run (null for an ordinary trap script), and
/// the trap's state after it.</summary>
public sealed record SessionTrapResult(GameSessionMutationResult Mutation, SessionTrapOutcome Outcome, NativeTrapRun? Run, SessionTrapState State);

/// <summary>One disarm attempt: the session's mutation, the outcome, the device prototype it yields (0: none; the item is
/// the caller's to create in the disarmer's inventory), the run of a sprung trap and the trap's state after it.</summary>
public sealed record SessionTrapDisarmResult(GameSessionMutationResult Mutation, TrapDisarm Outcome, int DevicePrototype, NativeTrapRun? Run,
    SessionTrapState State);

/// <summary>
/// ARC-WORLD-076: the trap state owner of trap objects and trapped objects on any map (ledger TT1–TT10), for the spot and
/// disarm skills (S10), the trap devices (ARC-TECHNOLOGY-011/012) and the hosts' use and step paths. An object's trap is
/// its T0 baseline until a spot mark or a removal; each write records the object's trap state as one map change of its
/// entity (<see cref="TrapStateProperty"/>: bit 1 spotted, bit 2 removed), which the snapshot already carries, so the domain
/// adds no schema. A removed trap object is also recorded destroyed (<see cref="ObjectConditionProperty"/>). The rolls are
/// the caller's (the spot trap and disarm traps skills); the native runs' effects (spell and attacks, 0x4BCDC0) are a later
/// slice: the session returns the run.
/// </summary>
public sealed partial class GameSessionOwner
{
    public const string TrapStateProperty = "trap.state";
    private const long TrapSpottedBit = 1, TrapRemovedBit = 2;

    /// <summary>The object's current trap state: its recorded changes over its baseline.</summary>
    public SessionTrapState QueryTrap(SessionTrapped trapped, SessionTrapBaseline baseline)
    {
        ThrowIfDisposed();
        if (!ValidTrapped(trapped, baseline)) throw new ArgumentException("Invalid trapped object or baseline.", nameof(trapped));
        return TrapState(trapped, baseline);
    }

    /// <summary>Whether the observer knows the object's trap (ledger TT2); false when it carries none.</summary>
    public bool TrapKnownTo(SessionTrapped trapped, SessionTrapBaseline baseline, TrapObserver observer)
    {
        var state = QueryTrap(trapped, baseline);
        return state.Trapped && TrapRules.Known(observer, state.ObjectFlags);
    }

    /// <summary>The gate of a spot attempt on the object (ledger TT3): whether the observer's roll is needed at all.</summary>
    public TrapSpotGate TrapSpotGate(SessionTrapped trapped, SessionTrapBaseline baseline, TrapObserver observer, int playerSpellFlags)
    {
        var state = QueryTrap(trapped, baseline);
        return TrapRules.SpotGate(observer, false, state.Class, state.ObjectFlags, playerSpellFlags);
    }

    /// <summary>Marks the trap spotted by the observer (ledger TT4): only the player or an NPC led by a PC marks it. One
    /// revision; NoChange when the object carries no trap, is marked already or the observer marks nothing.</summary>
    public GameSessionMutationResult MarkTrapSpotted(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline,
        TrapObserver observer)
    {
        ThrowIfDisposed();
        if (!ValidTrapped(trapped, baseline)) return Reject(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var state = TrapState(trapped, baseline);
        if (!state.Trapped) return NoChange();
        var mark = TrapRules.MarkSpotted(observer, baseline.Carrier, state.ObjectFlags);
        if (!mark.Marked || state.Spotted) return NoChange();
        return ApplyChanges(expected, null, TrapEdits(trapped, baseline, RecordedTrap(trapped) | TrapSpottedBit));
    }

    /// <summary>
    /// The object's trap meets a triggerer (ledger TT7–TT9). <paramref name="spottedNow"/> is the caller's spot attempt on a
    /// trap the triggerer does not know (the gate of <see cref="TrapSpotGate"/> and the spot trap roll); it is read only for a
    /// use or step. A use or step whose triggerer spots the trap now marks it (on the PC's side) and is cancelled; otherwise the
    /// trap fires: a native script's run, whose auto-removal clears the trap (a trap object is destroyed), or an ordinary trap
    /// script the host runs. An attack fires the trap and never removes it. One revision when the state changes.
    /// </summary>
    public SessionTrapResult TriggerTrap(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline,
        TrapObserver triggerer, SessionTrapCause cause, bool spottedNow, int moverSpellFlags = 0)
    {
        ThrowIfDisposed();
        if (!ValidTrapped(trapped, baseline) || !Enum.IsDefined(cause)) return TrapRejected(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return TrapRejected(GameSessionFailure.StaleVersion);
        var state = TrapState(trapped, baseline);
        if (cause == SessionTrapCause.Step && !TrapRules.StepTriggers(moverSpellFlags)) return new(NoChange(), SessionTrapOutcome.SteppedOver, null, state);
        if (!state.Trapped) return new(NoChange(), SessionTrapOutcome.NoTrap, null, state);
        if (cause != SessionTrapCause.Attack && spottedNow && !TrapRules.Known(triggerer, state.ObjectFlags))
        {
            var marked = MarkTrapSpotted(expected, trapped, baseline, triggerer);
            return new(marked, SessionTrapOutcome.Spotted, null, TrapState(trapped, baseline));
        }
        return Fire(expected, trapped, baseline, state, cause);
    }

    /// <summary>
    /// A disarm attempt on the object (ledger TT5, TT6) from the caller's disarm traps result (<paramref name="success"/>,
    /// <paramref name="critical"/>) and the disarmer's training in it. Success removes the trap (a trap object is destroyed);
    /// an expert's critical success also returns its device prototype; a critical failure sets it off as a use would (the
    /// trap is known, so no spot). One revision when the state changes.
    /// </summary>
    public SessionTrapDisarmResult DisarmTrap(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline,
        TrapObserver disarmer, bool success, bool critical, int disarmTraining)
    {
        ThrowIfDisposed();
        if (!ValidTrapped(trapped, baseline)) return new(Reject(GameSessionFailure.InvalidTrap), TrapDisarm.NotTrapped, 0, null, default);
        if (!IsCurrent(expected)) return new(Reject(GameSessionFailure.StaleVersion), TrapDisarm.NotTrapped, 0, null, default);
        var state = TrapState(trapped, baseline);
        var device = TrapRules.DevicePrototype(state.Script, state.ObjectFlags, baseline.Counters);
        var outcome = TrapRules.Disarm(state.Class, state.Trapped && TrapRules.Known(disarmer, state.ObjectFlags), success, critical,
            disarmTraining, device);
        switch (outcome)
        {
            case TrapDisarm.Removed or TrapDisarm.RemovedWithDevice:
                var removed = ApplyChanges(expected, null, TrapEdits(trapped, baseline, RecordedTrap(trapped) | TrapRemovedBit));
                return new(removed, outcome, outcome == TrapDisarm.RemovedWithDevice ? device : 0, null, TrapState(trapped, baseline));
            case TrapDisarm.Sprung:
                var fired = Fire(expected, trapped, baseline, state, SessionTrapCause.Use);
                return new(fired.Mutation, outcome, 0, fired.Run, fired.State);
            default:
                return new(NoChange(), outcome, 0, null, state);
        }
    }

    private SessionTrapResult Fire(GameSessionVersion expected, SessionTrapped trapped, SessionTrapBaseline baseline, SessionTrapState state,
        SessionTrapCause cause)
    {
        var run = TrapRules.NativeRun(state.Script, baseline.HeaderFlags);
        // The object entry point writes the cleared number back (E3); the attack's direct trigger (0x4BCB00) loses it (OD6).
        if (run is not { AutoRemoves: true } || cause == SessionTrapCause.Attack) return new(NoChange(), SessionTrapOutcome.Fired, run, state);
        var removed = ApplyChanges(expected, null, TrapEdits(trapped, baseline, RecordedTrap(trapped) | TrapRemovedBit));
        return new(removed, SessionTrapOutcome.Fired, run, TrapState(trapped, baseline));
    }

    private SessionTrapResult TrapRejected(GameSessionFailure failure) => new(Reject(failure), SessionTrapOutcome.NoTrap, null, default);

    private SessionTrapState TrapState(SessionTrapped trapped, SessionTrapBaseline baseline)
    {
        var recorded = RecordedTrap(trapped);
        if ((recorded & TrapRemovedBit) != 0) return new(TrapClass.None, 0, baseline.ObjectFlags | ((recorded & TrapSpottedBit) != 0 ? TrapRules.SpottedFlag : 0), true);
        var flags = baseline.ObjectFlags;
        if ((recorded & TrapSpottedBit) != 0) flags = TrapRules.MarkSpotted(TrapObserver.Player, baseline.Carrier, flags).ObjectFlags;
        return new(TrapRules.Classify(baseline.Script, baseline.HeaderFlags), baseline.Script, flags, false);
    }

    // The recorded trap bits (0 when none, or when a crafted save holds a value outside them).
    private long RecordedTrap(SessionTrapped trapped) =>
        _maps.TryGetValue(trapped.Map, out var changes) && changes.TryGetValue(MapChangeKey.ForEntity(trapped.Entity, TrapStateProperty), out var value) &&
        value is >= 0 and <= (TrapSpottedBit | TrapRemovedBit) ? value : 0;

    private static MapChangeEdit[] TrapEdits(SessionTrapped trapped, SessionTrapBaseline baseline, long bits) =>
        baseline.Carrier == TrapCarrier.TrapObject && (bits & TrapRemovedBit) != 0
            ?
            [
                new(trapped.Map, MapChangeKey.ForEntity(trapped.Entity, TrapStateProperty), bits),
                new(trapped.Map, MapChangeKey.ForEntity(trapped.Entity, ObjectConditionProperty), (long)SessionObjectCondition.Destroyed),
            ]
            : [new(trapped.Map, MapChangeKey.ForEntity(trapped.Entity, TrapStateProperty), bits)];

    private static bool ValidTrapped(SessionTrapped trapped, SessionTrapBaseline baseline) =>
        SessionValidation.Map(trapped.Map) && SessionValidation.Entity(trapped.Entity) && trapped.Entity != SessionEventKinds.AbsentRole &&
        Enum.IsDefined(baseline.Carrier) && baseline.Script is >= 0 and <= Arcanum.Data.Scripts.ScrCatalogue.MaxScriptNumber;
}
