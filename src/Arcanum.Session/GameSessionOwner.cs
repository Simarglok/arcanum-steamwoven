using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;

namespace Arcanum.Session;

/// <summary>
/// Single owner of one game session: the clock, global flags, actor placement and map-local changes.
/// Caller-serialized like its components; no locks, events, files or Godot. Every call after
/// <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>.
/// </summary>
public sealed class GameSessionOwner : IDisposable
{
    private readonly GameClockOwner _clock;
    private readonly GlobalFlagOwner _flags;
    private readonly SessionFlags _flagView;
    private readonly GameSessionLimits _limits;
    private readonly Dictionary<SessionMapId, Dictionary<MapChangeKey, long>> _maps;
    private readonly GameSessionOwnerIdentity _identity = new();
    private ActorPlacement _actor;
    private SessionLightingState? _lighting;
    private long _revision;
    private int _totalChanges;
    private bool _disposed;

    private GameSessionOwner(GameClockOwner clock, GlobalFlagOwner flags, GameSessionLimits limits,
        ActorPlacement actor, long revision, Dictionary<SessionMapId, Dictionary<MapChangeKey, long>> maps,
        SessionLightingState? lighting)
    {
        _clock = clock; _flags = flags; _flagView = new(flags); _limits = limits; _actor = actor; _revision = revision; _maps = maps;
        _lighting = lighting;
        _totalChanges = maps.Values.Sum(changes => changes.Count);
    }

    /// <summary>Starts a new session from explicit inputs. Rejection leaves no component alive.</summary>
    public static GameSessionCreationResult Create(GameSessionDescriptor? descriptor)
    {
        if (descriptor?.Flags is null) return Rejected(GameSessionFailure.InvalidDescriptor);
        if (!SessionValidation.Actor(descriptor.Actor)) return Rejected(GameSessionFailure.InvalidActor);
        var limits = descriptor.Limits ?? new GameSessionLimits();
        var clock = GameClockOwner.Bootstrap(descriptor.Clock);
        if (clock.Owner is null) return Rejected(GameSessionFailure.ClockRejected, clock.Failure);
        var flags = GlobalFlagOwner.Create(descriptor.Flags);
        if (flags.Owner is null)
        {
            clock.Owner.Dispose();
            return Rejected(GameSessionFailure.FlagsRejected, flagReason: flags.Reason);
        }
        return new(new(clock.Owner, flags.Owner, limits, descriptor.Actor, 0, [], null),
            GameSessionFailure.None, ClockFailure.None, GlobalFlagReason.None);
    }

    /// <summary>
    /// Builds a new owner from a snapshot after checking world, module, clock profile and budgets.
    /// Never touches an existing owner, so a refused restore keeps the previous session usable.
    /// The new owner has a fresh identity: versions and clock tokens of any earlier owner are stale.
    /// </summary>
    public static GameSessionCreationResult Restore(GameSessionSnapshot? snapshot, GameSessionRestoreOptions? options)
    {
        if (options is null) return Rejected(GameSessionFailure.InvalidDescriptor);
        if (snapshot is null) return Rejected(GameSessionFailure.InvalidSnapshot);
        if (snapshot.SchemaVersion != GameSessionSnapshot.CurrentSchemaVersion) return Rejected(GameSessionFailure.UnsupportedSchema);
        if (snapshot.Identity.World != options.ExpectedIdentity.World) return Rejected(GameSessionFailure.WrongWorld);
        if (snapshot.Identity.Module != options.ExpectedIdentity.Module) return Rejected(GameSessionFailure.WrongModule);
        if (snapshot.Clock.Profile != options.ExpectedClockProfile) return Rejected(GameSessionFailure.IncompatibleClockProfile);
        if (snapshot.Revision < 0) return Rejected(GameSessionFailure.InvalidSnapshot);
        if (!SessionValidation.Actor(snapshot.Actor)) return Rejected(GameSessionFailure.InvalidActor);
        if (snapshot.Lighting is { } lighting && !SessionValidation.Lighting(lighting)) return Rejected(GameSessionFailure.InvalidLighting);
        var limits = options.Limits ?? new GameSessionLimits();
        var failure = ImportMaps(snapshot.Maps, limits, out var maps);
        if (failure != GameSessionFailure.None) return Rejected(failure);

        var savedClock = snapshot.Clock;
        var clock = GameClockOwner.FromDescriptor(new(savedClock.Profile, savedClock.GameTimestampMs,
            savedClock.Pause, 1, savedClock.Revision));
        if (clock.Owner is null) return Rejected(GameSessionFailure.ClockRejected, clock.Failure);
        // Applied lighting belongs to the saved hour; a mismatch would first show a stale scheme (FR-LIGHT-012).
        if (snapshot.Lighting is { } applied && applied.Hour != clock.Snapshot!.Value.Hour)
        {
            clock.Owner.Dispose();
            return Rejected(GameSessionFailure.InvalidLighting);
        }
        var entries = snapshot.Flags.Entries;
        var flags = GlobalFlagOwner.Create(new(snapshot.Identity, options.FlagEpoch, snapshot.Flags.Revision,
            entries.Select(entry => entry.Flag).ToArray(),
            entries.Where(entry => entry.Value.HasValue).Select(entry => new GlobalFlagBootstrapValue(entry.Flag, entry.Value!.Value)).ToArray()));
        if (flags.Owner is null)
        {
            clock.Owner.Dispose();
            return Rejected(GameSessionFailure.FlagsRejected, flagReason: flags.Reason);
        }
        return new(new(clock.Owner, flags.Owner, limits, snapshot.Actor, snapshot.Revision, maps, snapshot.Lighting),
            GameSessionFailure.None, ClockFailure.None, GlobalFlagReason.None);
    }

    public GameSessionOwnerIdentity Identity { get { ThrowIfDisposed(); return _identity; } }
    public GameSessionVersion Version { get { ThrowIfDisposed(); return new(_identity, _revision); } }
    public GameSessionLimits Limits { get { ThrowIfDisposed(); return _limits; } }

    /// <summary>The session's flag owner for reads, writes and adapters. Its lifetime belongs to the session.</summary>
    public IGlobalFlagOwner Flags { get { ThrowIfDisposed(); return _flagView; } }

    /// <summary>Current GF version, the expected value for the next flag read or write.</summary>
    public GlobalFlagVersion FlagVersion { get { ThrowIfDisposed(); return _flags.Version; } }

    public ClockSnapshot QueryClockSnapshot() { ThrowIfDisposed(); return _clock.QueryClockSnapshot(); }

    /// <summary>Forwarded to the clock. An advance that leaves the hour of the recorded lighting clears it, so a
    /// capture never pairs a time with another hour's scheme; the host records the new hour's lighting.</summary>
    public ClockMutationResult AdvanceGameTime(ClockToken token, long durationGameMs, ClockAdvanceReason reason)
    {
        ThrowIfDisposed();
        var result = _clock.AdvanceGameTime(token, durationGameMs, reason);
        if (result.Disposition == ClockMutationDisposition.Changed && _lighting is { } lighting && lighting.Hour != result.Snapshot.Hour)
            _lighting = null;
        return result;
    }

    public ClockMutationResult SetPaused(ClockToken token, PauseDisposition disposition)
    {
        ThrowIfDisposed();
        return _clock.SetPaused(token, disposition);
    }

    public ActorPlacement QueryActor() { ThrowIfDisposed(); return _actor; }

    /// <summary>The last recorded applied lighting, or null when none was recorded.</summary>
    public SessionLightingState? QueryLighting() { ThrowIfDisposed(); return _lighting; }

    /// <summary>
    /// Records the lighting the host has just applied. The hour must be the clock's current hour; an advance
    /// out of that hour clears the record until the host records again. Like clock and flag changes, this does
    /// not move the session revision.
    /// </summary>
    public GameSessionMutationResult RecordLighting(SessionLightingState lighting)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Lighting(lighting) || lighting.Hour != _clock.QueryClockSnapshot().Hour)
            return Reject(GameSessionFailure.InvalidLighting);
        if (_lighting == lighting) return NoChange();
        _lighting = lighting;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, new(_identity, _revision));
    }

    /// <summary>Places the actor, on the same map or across a transition. Map changes stay with their map.</summary>
    public GameSessionMutationResult PlaceActor(GameSessionVersion expected, ActorPlacement placement)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Actor(placement)) return Reject(GameSessionFailure.InvalidActor);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (placement == _actor) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _actor = placement;
        return Changed();
    }

    /// <summary>Current value of one map-local change, or null when the map is at its loaded baseline.</summary>
    public long? QueryMapChange(SessionMapId map, MapChangeKey key)
    {
        ThrowIfDisposed();
        return _maps.TryGetValue(map, out var changes) && changes.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Detached copy of one map's changes in canonical key order; empty for an untouched map.</summary>
    public IReadOnlyList<MapChange> QueryMapChanges(SessionMapId map)
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_maps.TryGetValue(map, out var changes) ? Sorted(changes) : []);
    }

    public GameSessionMutationResult SetMapChange(GameSessionVersion expected, SessionMapId map, MapChangeKey key, long value)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Key(key)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var hasMap = _maps.TryGetValue(map, out var changes);
        if (hasMap && changes!.TryGetValue(key, out var current))
        {
            if (current == value) return NoChange();
        }
        else if ((!hasMap && _maps.Count >= _limits.MaxMaps) ||
            (hasMap && changes!.Count >= _limits.MaxChangesPerMap) || _totalChanges >= _limits.MaxTotalChanges)
            return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);

        // All refusals precede the commit; only the dictionary updates and counters follow.
        if (!hasMap) _maps.Add(map, changes = []);
        if (changes!.TryAdd(key, value)) _totalChanges++;
        else changes[key] = value;
        return Changed();
    }

    /// <summary>Returns one key to its loaded baseline. A map left without changes is dropped.</summary>
    public GameSessionMutationResult ClearMapChange(GameSessionVersion expected, SessionMapId map, MapChangeKey key)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Key(key)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (!_maps.TryGetValue(map, out var changes) || !changes.ContainsKey(key)) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        changes.Remove(key);
        _totalChanges--;
        if (changes.Count == 0) _maps.Remove(map);
        return Changed();
    }

    /// <summary>Copies every component at this call boundary into one detached snapshot.</summary>
    public GameSessionSnapshot Capture()
    {
        ThrowIfDisposed();
        var clock = _clock.QueryClockSnapshot();
        var flags = _flags.CaptureSnapshot();
        var maps = _maps.OrderBy(pair => pair.Key.RuntimeMap)
            .Select(pair => new SessionMapState(pair.Key, Sorted(pair.Value))).ToArray();
        return new(GameSessionSnapshot.CurrentSchemaVersion, _flags.Binding.Identity, _revision,
            new(clock.ProfileBinding, clock.GameTimestampMs, clock.PauseDisposition, clock.Revision),
            new(flags.Version.Revision, flags.Entries.ToArray()), _actor, maps, _lighting);
    }

    /// <summary>Idempotent; disposes the clock and flag owners with the session.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Dispose();
        _flags.Dispose();
    }

    private static GameSessionFailure ImportMaps(IReadOnlyList<SessionMapState> saved, GameSessionLimits limits,
        out Dictionary<SessionMapId, Dictionary<MapChangeKey, long>> maps)
    {
        maps = [];
        if (saved.Count > limits.MaxMaps) return GameSessionFailure.BudgetExceeded;
        var total = 0;
        foreach (var map in saved)
        {
            if (!SessionValidation.Map(map.Map) || map.Changes.Count == 0) return GameSessionFailure.InvalidMap;
            if (map.Changes.Count > limits.MaxChangesPerMap) return GameSessionFailure.BudgetExceeded;
            total += map.Changes.Count;
            if (total > limits.MaxTotalChanges) return GameSessionFailure.BudgetExceeded;
            var changes = new Dictionary<MapChangeKey, long>();
            foreach (var change in map.Changes)
            {
                if (!SessionValidation.Key(change.Key)) return GameSessionFailure.InvalidChangeKey;
                if (!changes.TryAdd(change.Key, change.Value)) return GameSessionFailure.InvalidSnapshot;
            }
            if (!maps.TryAdd(map.Map, changes)) return GameSessionFailure.InvalidSnapshot;
        }
        return GameSessionFailure.None;
    }

    private static MapChange[] Sorted(Dictionary<MapChangeKey, long> changes) => changes
        .OrderBy(pair => pair.Key, MapChangeKeyComparer.Instance)
        .Select(pair => new MapChange(pair.Key, pair.Value)).ToArray();

    private bool IsCurrent(GameSessionVersion expected) =>
        ReferenceEquals(expected.Owner, _identity) && expected.Revision == _revision;

    private GameSessionMutationResult Changed()
    {
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, new(_identity, _revision));
    }

    private GameSessionMutationResult NoChange() =>
        new(GameSessionDisposition.NoChange, GameSessionFailure.None, new(_identity, _revision));

    private GameSessionMutationResult Reject(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, new(_identity, _revision));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static GameSessionCreationResult Rejected(GameSessionFailure failure,
        ClockFailure clockFailure = ClockFailure.None, GlobalFlagReason flagReason = GlobalFlagReason.None) =>
        new(null, failure, clockFailure, flagReason);
}

/// <summary>Forwarding view, so callers cannot reach the owner's Dispose or MarkFailed.</summary>
internal sealed class SessionFlags(GlobalFlagOwner owner) : IGlobalFlagOwner
{
    public GlobalFlagBinding Binding => owner.Binding;
    public GlobalFlagReadResult Read(GlobalFlagReadRequest request) => owner.Read(request);
    public GlobalFlagWriteReceipt Write(GlobalFlagWriteRequest request) => owner.Write(request);
}
