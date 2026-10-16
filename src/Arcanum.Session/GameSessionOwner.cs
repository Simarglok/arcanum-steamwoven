using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;

namespace Arcanum.Session;

/// <summary>
/// Single owner of one game session: the clock, global flags, actor placement, map-local changes and the
/// story domain (PC quests, shared quest states, known rumors).
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
    private readonly Story _story;
    private readonly GameSessionOwnerIdentity _identity = new();
    private ActorPlacement _actor;
    private long _revision;
    private int _totalChanges;
    private bool _disposed;

    private GameSessionOwner(GameClockOwner clock, GlobalFlagOwner flags, GameSessionLimits limits,
        ActorPlacement actor, long revision, Dictionary<SessionMapId, Dictionary<MapChangeKey, long>> maps, Story story)
    {
        _clock = clock; _flags = flags; _flagView = new(flags); _limits = limits; _actor = actor; _revision = revision; _maps = maps;
        _story = story;
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
        return new(new(clock.Owner, flags.Owner, limits, descriptor.Actor, 0, [], new()),
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
        var limits = options.Limits ?? new GameSessionLimits();
        var failure = ImportMaps(snapshot.Maps, limits, out var maps);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportStory(snapshot.Story, snapshot.Clock.GameTimestampMs, out var story);
        if (failure != GameSessionFailure.None) return Rejected(failure);

        var savedClock = snapshot.Clock;
        var clock = GameClockOwner.FromDescriptor(new(savedClock.Profile, savedClock.GameTimestampMs,
            savedClock.Pause, 1, savedClock.Revision));
        if (clock.Owner is null) return Rejected(GameSessionFailure.ClockRejected, clock.Failure);
        var entries = snapshot.Flags.Entries;
        var flags = GlobalFlagOwner.Create(new(snapshot.Identity, options.FlagEpoch, snapshot.Flags.Revision,
            entries.Select(entry => entry.Flag).ToArray(),
            entries.Where(entry => entry.Value.HasValue).Select(entry => new GlobalFlagBootstrapValue(entry.Flag, entry.Value!.Value)).ToArray()));
        if (flags.Owner is null)
        {
            clock.Owner.Dispose();
            return Rejected(GameSessionFailure.FlagsRejected, flagReason: flags.Reason);
        }
        return new(new(clock.Owner, flags.Owner, limits, snapshot.Actor, snapshot.Revision, maps, story),
            GameSessionFailure.None, ClockFailure.None, GlobalFlagReason.None);
    }

    public GameSessionOwnerIdentity Identity { get { ThrowIfDisposed(); return _identity; } }
    public GameSessionVersion Version { get { ThrowIfDisposed(); return new(_identity, _revision); } }
    public GameSessionLimits Limits { get { ThrowIfDisposed(); return _limits; } }

    /// <summary>The session's flag owner for reads, writes and adapters. Its lifetime belongs to the session.</summary>
    public IVersionedGlobalFlagOwner Flags { get { ThrowIfDisposed(); return _flagView; } }

    /// <summary>Current GF version, the expected value for the next flag read or write.</summary>
    public GlobalFlagVersion FlagVersion { get { ThrowIfDisposed(); return _flags.Version; } }

    public ClockSnapshot QueryClockSnapshot() { ThrowIfDisposed(); return _clock.QueryClockSnapshot(); }

    public ClockMutationResult AdvanceGameTime(ClockToken token, long durationGameMs, ClockAdvanceReason reason)
    {
        ThrowIfDisposed();
        return _clock.AdvanceGameTime(token, durationGameMs, reason);
    }

    public ClockMutationResult SetPaused(ClockToken token, PauseDisposition disposition)
    {
        ThrowIfDisposed();
        return _clock.SetPaused(token, disposition);
    }

    public ActorPlacement QueryActor() { ThrowIfDisposed(); return _actor; }

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

    /// <summary>The PC's record of a quest, or null while it was never changed (state Unknown).</summary>
    public SessionQuest? QueryQuest(int quest)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsQuest(quest)) throw new ArgumentOutOfRangeException(nameof(quest));
        return _story.Quests.TryGetValue(quest, out var value) ? value : null;
    }

    /// <summary>A quest's shared state: Accepted (the source default) until a PC completes or botches it.</summary>
    public SessionQuestState QueryGlobalQuest(int quest)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsQuest(quest)) throw new ArgumentOutOfRangeException(nameof(quest));
        return _story.GlobalQuests.GetValueOrDefault(quest, SessionQuestState.Accepted);
    }

    /// <summary>The game time a rumor was learned, or null while the PC does not know it.</summary>
    public long? QueryRumor(int rumor)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsRumor(rumor)) throw new ArgumentOutOfRangeException(nameof(rumor));
        return _story.Rumors.TryGetValue(rumor, out var value) ? value : null;
    }

    /// <summary>Detached copy of the story domain in ascending order.</summary>
    public SessionStoryState QueryStory() { ThrowIfDisposed(); return _story.Capture(); }

    /// <summary>
    /// Requests a PC quest state with the source rules: a Completed, OtherCompleted or Botched quest and a request
    /// not above the current state change nothing. Otherwise a quest whose shared state is still Accepted takes
    /// the request (Completed/OtherCompleted complete it for everyone, Botched botches it); once the shared state
    /// left Accepted, the request becomes OtherCompleted (shared Completed) or Botched. Botched keeps the stored
    /// state and marks it. The change is stamped with the current session game time. Experience, alignment and
    /// reaction effects of the source belong to their own owners and are not applied here.
    /// </summary>
    public GameSessionMutationResult SetQuestState(GameSessionVersion expected, int quest, SessionQuestState requested)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsQuest(quest)) return Reject(GameSessionFailure.InvalidQuest);
        if (!Enum.IsDefined(requested)) return Reject(GameSessionFailure.InvalidQuestState);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var current = _story.Quests.TryGetValue(quest, out var record) ? record : new SessionQuest(quest, SessionQuestState.Unknown, false, 0);
        if (current.Effective is SessionQuestState.Completed or SessionQuestState.OtherCompleted or SessionQuestState.Botched ||
            current.Effective >= requested) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);

        var shared = _story.GlobalQuests.GetValueOrDefault(quest, SessionQuestState.Accepted);
        var state = requested;
        if (shared == SessionQuestState.Accepted)
        {
            if (state is SessionQuestState.Completed or SessionQuestState.OtherCompleted) shared = SessionQuestState.Completed;
            else if (state == SessionQuestState.Botched) shared = SessionQuestState.Botched;
        }
        else state = shared == SessionQuestState.Completed ? SessionQuestState.OtherCompleted : SessionQuestState.Botched;
        var timestamp = _clock.QueryClockSnapshot().GameTimestampMs;
        var next = state == SessionQuestState.Botched
            ? current with { Botched = true, TimestampMs = timestamp }
            : new SessionQuest(quest, state, false, timestamp);

        // All refusals precede the commit.
        _story.Quests[quest] = next;
        if (shared != SessionQuestState.Accepted) _story.GlobalQuests[quest] = shared;
        return Changed();
    }

    /// <summary>Marks a rumor known at the current session game time; an already known rumor keeps its time.</summary>
    public GameSessionMutationResult LearnRumor(GameSessionVersion expected, int rumor)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsRumor(rumor)) return Reject(GameSessionFailure.InvalidRumor);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_story.Rumors.ContainsKey(rumor)) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.Rumors.Add(rumor, _clock.QueryClockSnapshot().GameTimestampMs);
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
            new(flags.Version.Revision, flags.Entries.ToArray()), _actor, maps, _story.Capture());
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

    // Beyond each record's own range, the records must agree with the rules: no time after the saved clock, a PC
    // record Completed/OtherCompleted only under a shared Completed state, a botched one only under shared Botched,
    // and any other PC record only while the shared state is Accepted. A shared state without a PC record is kept
    // (the source's shared state is not owned by one PC).
    private static GameSessionFailure ImportStory(SessionStoryState saved, long clockMs, out Story story)
    {
        story = new();
        foreach (var quest in saved.Quests)
        {
            if (!SessionStoryLimits.IsQuest(quest.Quest)) return GameSessionFailure.InvalidQuest;
            // The stored state is never Botched itself, and an untouched quest is not listed.
            if (quest.State is < SessionQuestState.Unknown or > SessionQuestState.OtherCompleted || quest.TimestampMs < 0 ||
                (quest.State == SessionQuestState.Unknown && !quest.Botched)) return GameSessionFailure.InvalidQuestState;
            if (quest.TimestampMs > clockMs) return GameSessionFailure.InvalidSnapshot;
            if (!story.Quests.TryAdd(quest.Quest, quest)) return GameSessionFailure.InvalidSnapshot;
        }
        foreach (var quest in saved.GlobalQuests)
        {
            if (!SessionStoryLimits.IsQuest(quest.Quest)) return GameSessionFailure.InvalidQuest;
            if (quest.State is not (SessionQuestState.Completed or SessionQuestState.Botched)) return GameSessionFailure.InvalidQuestState;
            if (!story.GlobalQuests.TryAdd(quest.Quest, quest.State)) return GameSessionFailure.InvalidSnapshot;
        }
        foreach (var rumor in saved.Rumors)
        {
            if (!SessionStoryLimits.IsRumor(rumor.Rumor)) return GameSessionFailure.InvalidRumor;
            if (rumor.TimestampMs < 0 || rumor.TimestampMs > clockMs) return GameSessionFailure.InvalidSnapshot;
            if (!story.Rumors.TryAdd(rumor.Rumor, rumor.TimestampMs)) return GameSessionFailure.InvalidSnapshot;
        }
        foreach (var quest in story.Quests.Values)
        {
            var shared = quest.Effective switch
            {
                SessionQuestState.Botched => SessionQuestState.Botched,
                SessionQuestState.Completed or SessionQuestState.OtherCompleted => SessionQuestState.Completed,
                _ => SessionQuestState.Accepted,
            };
            if (story.GlobalQuests.GetValueOrDefault(quest.Quest, SessionQuestState.Accepted) != shared) return GameSessionFailure.InvalidQuestState;
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
internal sealed class SessionFlags(GlobalFlagOwner owner) : IVersionedGlobalFlagOwner
{
    public GlobalFlagBinding Binding => owner.Binding;
    public GlobalFlagVersion Version => owner.Version;
    public GlobalFlagReadResult Read(GlobalFlagReadRequest request) => owner.Read(request);
    public GlobalFlagWriteReceipt Write(GlobalFlagWriteRequest request) => owner.Write(request);
}

/// <summary>Mutable story domain of one owner; bounded by the quest and rumor number ranges.</summary>
internal sealed class Story
{
    internal Dictionary<int, SessionQuest> Quests { get; } = [];
    internal Dictionary<int, SessionQuestState> GlobalQuests { get; } = [];
    internal Dictionary<int, long> Rumors { get; } = [];

    internal SessionStoryState Capture() => new(
        Quests.Values.OrderBy(quest => quest.Quest).ToArray(),
        GlobalQuests.OrderBy(pair => pair.Key).Select(pair => new SessionGlobalQuest(pair.Key, pair.Value)).ToArray(),
        Rumors.OrderBy(pair => pair.Key).Select(pair => new SessionRumor(pair.Key, pair.Value)).ToArray());
}
