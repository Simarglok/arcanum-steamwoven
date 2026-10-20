using Arcanum.Simulation.Events;
using Arcanum.Simulation.Locks;

namespace Arcanum.Session;

/// <summary>A portal or container of a map: its runtime map and its session entity id.</summary>
public readonly record struct SessionOpenable(SessionMapId Map, string Entity);

/// <summary>
/// The T0 lock facts of one openable object, read by the host from its map data: its kind, its lock flags (portal field
/// 46, container field 56), the key id of its lock, its ART frame and, for a container, whether it is a junk pile
/// (description 3023, which has no lock: ledger LK1).
/// </summary>
public readonly record struct SessionOpenableBaseline(OpenableKind Kind, int Flags, int LockKey, int Frame, bool JunkPile = false);

/// <summary>An openable object's current lock flags and ART frame, and whether it counts as locked at the session's hour.</summary>
public readonly record struct SessionOpenableState(int Flags, int Frame, bool Locked)
{
    public bool Open => Frame != 0;
}

/// <summary>A Lock event that came due in a pass (ledger LK5): the object and its state after the event.</summary>
public sealed record SessionLockFiring(GameEvent Event, SessionOpenable Object, SessionOpenableState State);

/// <summary>
/// ARC-WORLD-050: the lock state owner of portals and containers on any map, for doors and windows at runtime
/// (ARC-WORLD-051) and the pick-locks skill (skills plan S9). An object's state is its T0 baseline until a lock, jam or
/// frame write; the first write records its kind, flags and frame as map changes of its entity (<see cref="FlagsProperty"/>,
/// <see cref="FrameProperty"/>, <see cref="KindProperty"/>), which the snapshot already carries, so the domain adds no
/// schema. Unlocking a locked object and jamming one that was not jammed schedule the original's Lock event
/// (<see cref="SessionEventKinds.LockReset"/>) on the object's map; when it comes due it locks the object, clears its
/// jam and closes it (ledger LK3–LK5).
/// </summary>
public sealed partial class GameSessionOwner
{
    public const string FlagsProperty = "lock.flags", FrameProperty = "lock.frame", KindProperty = "lock.kind";

    /// <summary>The object's current state: its recorded changes, or its baseline when it has none.</summary>
    public SessionOpenableState QueryOpenable(SessionOpenable openable, SessionOpenableBaseline baseline)
    {
        ThrowIfDisposed();
        if (!ValidOpenable(openable, baseline)) throw new ArgumentException("Invalid openable object or baseline.", nameof(openable));
        var (flags, frame) = Current(openable, baseline);
        // A junk pile is not lockable, so the original's locked query answers 0 for it (ledger LK1, LK2).
        return new(flags, frame, !baseline.JunkPile && LockRules.IsLocked(flags, _clock.QueryClockSnapshot().Hour));
    }

    /// <summary>Locks or unlocks the object (ledger LK3), as a script, a key or the pick-locks skill does; unlocking a locked
    /// object schedules the Lock event an hour later. A junk pile has no lock (NoChange). One revision.</summary>
    public GameSessionMutationResult SetOpenableLocked(GameSessionVersion expected, SessionOpenable openable, SessionOpenableBaseline baseline,
        bool locked) => LockWrite(expected, openable, baseline, (flags, hour) => LockRules.SetLocked(flags, locked, hour), LockRules.UnlockResetDelayMs);

    /// <summary>Jams or frees the object (ledger LK4), as a critical failure of pick locks does; jamming an object that was
    /// not jammed schedules the Lock event a day later. A junk pile has no lock (NoChange). One revision.</summary>
    public GameSessionMutationResult SetOpenableJammed(GameSessionVersion expected, SessionOpenable openable, SessionOpenableBaseline baseline,
        bool jammed) => LockWrite(expected, openable, baseline, (flags, hour) => LockRules.SetJammed(flags, jammed, hour), LockRules.JamResetDelayMs);

    /// <summary>Records the object's ART frame (its open state): a portal step of <see cref="PortalFrameRules"/>, or a
    /// container's open (next frame) and close (previous frame). 0..31, the ART id's frame bits. One revision.</summary>
    public GameSessionMutationResult SetOpenableFrame(GameSessionVersion expected, SessionOpenable openable, SessionOpenableBaseline baseline,
        int frame)
    {
        ThrowIfDisposed();
        if (!ValidOpenable(openable, baseline) || frame is < 0 or > MaxFrame) return Reject(GameSessionFailure.InvalidOpenable);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var (flags, current) = Current(openable, baseline);
        if (current == frame) return NoChange();
        return ApplyChanges(expected, null, Edits(openable, baseline.Kind, flags, frame));
    }

    /// <summary>ARC-WORLD-051: marks the object busted (bit 0x80) on frame 0, as the original busts a window (ledger LK12): a
    /// busted portal is unlocked and opens for every walker. The busted ART (bit 0x200), a door the original destroys instead
    /// and a destroyed state are the caller's. One revision; NoChange when already busted on frame 0.</summary>
    public GameSessionMutationResult SetOpenableBusted(GameSessionVersion expected, SessionOpenable openable, SessionOpenableBaseline baseline)
    {
        ThrowIfDisposed();
        if (!ValidOpenable(openable, baseline)) return Reject(GameSessionFailure.InvalidOpenable);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var (flags, frame) = Current(openable, baseline);
        if ((flags & LockRules.BustedBit) != 0 && frame == 0) return NoChange();
        return ApplyChanges(expected, null, Edits(openable, baseline.Kind, flags | LockRules.BustedBit, 0));
    }

    /// <summary>
    /// The open attempt of the player for the object (ledger LK7, LK8) with its current flags at the session's hour.
    /// <paramref name="scriptRefused"/> is the host's result of the object's unlock script (SAP 3); <paramref name="leaving"/>
    /// whether a portal step leaves a building as both of the original's attempts see it (ledger LV5). The session inventory
    /// holds no keys yet (ARC-WORLD-014 refuses keys and key rings), so a lock that needs a key stays locked.
    /// </summary>
    public OpenAttempt AttemptOpen(SessionOpenable openable, SessionOpenableBaseline baseline, bool scriptRefused, bool leaving = false)
    {
        ThrowIfDisposed();
        if (!ValidOpenable(openable, baseline)) throw new ArgumentException("Invalid openable object or baseline.", nameof(openable));
        var (flags, _) = Current(openable, baseline);
        return LockRules.Attempt(new(baseline.Kind, flags, _clock.QueryClockSnapshot().Hour, OpenAttemptWalker.Player, scriptRefused,
            baseline.LockKey, Array.Empty<int>(), leaving, baseline.JunkPile));
    }

    /// <summary>The ART id's frame bits (14..18) hold 0..31.</summary>
    public const int MaxFrame = 31;

    private GameSessionMutationResult LockWrite(GameSessionVersion expected, SessionOpenable openable, SessionOpenableBaseline baseline,
        Func<int, int, LockWrite> write, long delayMs)
    {
        ThrowIfDisposed();
        if (!ValidOpenable(openable, baseline)) return Reject(GameSessionFailure.InvalidOpenable);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (baseline.JunkPile) return NoChange();
        var (flags, frame) = Current(openable, baseline);
        var result = write(flags, _clock.QueryClockSnapshot().Hour);
        // A write that schedules always changes a bit (unlocking a locked object, jamming a free one).
        if (result.Flags == flags) return NoChange();
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (result.SchedulesReset && (_events.QueryPending().Count(e => e.Kind == SessionEventKinds.LockReset) >= SessionEventKinds.MaxLockResets ||
            _events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - delayMs))
            return Reject(GameSessionFailure.EventBudgetExceeded);
        var applied = ApplyChanges(expected, null, Edits(openable, baseline.Kind, result.Flags, frame));
        if (applied.Disposition == GameSessionDisposition.Rejected || !result.SchedulesReset) return applied;
        // The Lock event of the original's lock and jam setters; the changes above made the revision move already. Every
        // refusal was checked before the changes (the kind keeps any number of events), so a failure here is a defect.
        var scheduled = _events.ScheduleAfter(SessionEventKinds.LockReset,
            GameEventPayload.Of([new(openable.Map.RuntimeMap, openable.Entity)], now), delayMs, new(0, now, 0));
        if (scheduled.Disposition != GameEventScheduleDisposition.Added)
            throw new InvalidOperationException("An admitted Lock event could not be scheduled.");
        return applied;
    }

    // A Lock event: lock, unjam and close the object it names (ledger LK5); dropped when the object has no recorded state.
    internal GameEventHandlerOutcome FireLockReset(GameEventFiring firing)
    {
        var entity = firing.Event.Payload.Entities[0];
        var openable = new SessionOpenable(new(entity.Map), entity.Code);
        if (Recorded(openable) is not { } state) return GameEventHandlerOutcome.Refused;
        var flags = LockRules.AfterReset(state.Flags);
        var frame = state.Kind == OpenableKind.Portal ? PortalFrameRules.Closed : state.Frame == 1 ? 0 : state.Frame;
        var result = ApplyChanges(Version, null, Edits(openable, state.Kind, flags, frame));
        if (result.Disposition == GameSessionDisposition.Rejected) return GameEventHandlerOutcome.Refused;
        _passLocks?.Add(new(firing.Event, openable, new(flags, frame, LockRules.IsLocked(flags, _clock.QueryClockSnapshot().Hour))));
        return GameEventHandlerOutcome.Completed;
    }

    // The queue's existence check for a Lock event's object when the host cannot answer it: the session's own record.
    private bool LockObjectExists(GameEventEntity entity) => Recorded(new(new(entity.Map), entity.Code)) is not null;

    private (OpenableKind Kind, int Flags, int Frame)? Recorded(SessionOpenable openable) => RecordedIn(_maps, openable);

    // An object's recorded state, or null when any of its three values is missing or out of range (a crafted save).
    private static (OpenableKind Kind, int Flags, int Frame)? RecordedIn(IReadOnlyDictionary<SessionMapId, Dictionary<MapChangeKey, long>> maps,
        SessionOpenable openable)
    {
        if (!SessionValidation.Map(openable.Map) || !SessionValidation.Entity(openable.Entity) || !maps.TryGetValue(openable.Map, out var changes))
            return null;
        long? Read(string property) => changes.TryGetValue(MapChangeKey.ForEntity(openable.Entity, property), out var value) ? value : null;
        return Read(KindProperty) is { } kind and ((long)OpenableKind.Portal or (long)OpenableKind.Container) &&
            Read(FlagsProperty) is { } flags and >= int.MinValue and <= int.MaxValue && Read(FrameProperty) is { } frame and >= 0 and <= MaxFrame
            ? ((OpenableKind)kind, (int)flags, (int)frame) : null;
    }

    private (int Flags, int Frame) Current(SessionOpenable openable, SessionOpenableBaseline baseline) =>
        Recorded(openable) is { } state ? (state.Flags, state.Frame) : (baseline.Flags, baseline.Frame);

    private static MapChangeEdit[] Edits(SessionOpenable openable, OpenableKind kind, int flags, int frame) =>
    [
        new(openable.Map, MapChangeKey.ForEntity(openable.Entity, KindProperty), (long)kind),
        new(openable.Map, MapChangeKey.ForEntity(openable.Entity, FlagsProperty), flags),
        new(openable.Map, MapChangeKey.ForEntity(openable.Entity, FrameProperty), frame),
    ];

    private static bool ValidOpenable(SessionOpenable openable, SessionOpenableBaseline baseline) =>
        SessionValidation.Map(openable.Map) && SessionValidation.Entity(openable.Entity) && openable.Entity != SessionEventKinds.AbsentRole &&
        baseline.Kind is OpenableKind.Portal or OpenableKind.Container && baseline.Frame is >= 0 and <= MaxFrame &&
        (!baseline.JunkPile || baseline.Kind == OpenableKind.Container);
}
