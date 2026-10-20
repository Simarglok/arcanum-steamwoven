using System.Collections.Immutable;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Randomness;

namespace Arcanum.Session;

/// <summary>
/// Single owner of one game session: the clock, global flags, actor placement, map-local changes and the
/// story domain (PC quests, shared quest states, known rumors), the PC's party (followers, waiting NPCs), the player
/// character (stats, level, alignment) with its gold and starting kit, and the items the PC took from the world (pickup,
/// loot, negative dialog IN) over host-attached T0 map inventories, the hero's worn items (ARC-WORLD-028), the seeded gameplay randomness (labelled rolls) and the
/// life of the hero and the NPCs (hit points, fatigue, poison, injuries, knockout, death) with the game event queue of
/// their timers (ARC-WORLD-024), the NPCs' reaction memory toward the PC (ARC-WORLD-026), the hero's experience and the NPCs'
/// experience worth and pool (ARC-WORLD-030), and the active rule mode (Steamwoven or Original, ARC-WORLD-023) and game
/// difficulty, which are settings and not saved.
/// Caller-serialized like its components; no locks, events, files or Godot. Every call after
/// <see cref="Dispose"/> throws <see cref="ObjectDisposedException"/>.
/// </summary>
public sealed partial class GameSessionOwner : IDisposable
{
    private readonly GameClockOwner _clock;
    private readonly GlobalFlagOwner _flags;
    private readonly SessionFlags _flagView;
    private readonly GameSessionLimits _limits;
    private readonly Dictionary<SessionMapId, Dictionary<MapChangeKey, long>> _maps;
    private readonly Story _story;
    private readonly PartyDomain _party;
    private readonly InventoryDomain _inventory;
    private readonly EquipmentDomain _equipment;
    private readonly EffectDomain _effects;
    private readonly ExperienceDomain _experience;
    private readonly AdvancementDomain _advancement;
    private readonly HeroDomain? _hero;
    private readonly LifeDomain _life;
    private readonly ReactionDomain _reactions;
    private readonly CombatDomain _combat;
    private readonly GameEventQueue _events;
    // Set only while a pass runs: the host's answers for the timers it fires and the life steps they made.
    private ISessionEventWorld? _passWorld;
    private List<SessionLifeFiring>? _passFirings;
    private List<SessionScriptCallFiring>? _passCalls;
    private List<SessionEffectFiring>? _passEffects;
    private List<SessionLockFiring>? _passLocks;
    // Replaced, not mutated, when a life step that drew on a trial copy commits.
    private SeededRandom _random;
    private readonly SessionCombatRolls _combatRolls;
    private readonly GameSessionOwnerIdentity _identity = new();
    private ActorPlacement _actor;
    private SessionLightingState? _lighting;
    private GameRules _rules = GameRules.Steamwoven;
    private GameDifficulty _difficulty = GameDifficulty.Normal;
    private long _revision;
    private int _totalChanges;
    private bool _disposed;

    private GameSessionOwner(GameClockOwner clock, GlobalFlagOwner flags, GameSessionLimits limits,
        ActorPlacement actor, long revision, Dictionary<SessionMapId, Dictionary<MapChangeKey, long>> maps, Story story,
        PartyDomain party, HeroDomain? hero, InventoryDomain inventory, EquipmentDomain equipment, EffectDomain effects,
        ExperienceDomain experience, AdvancementDomain advancement, SeededRandom random, LifeDomain life,
        ReactionDomain reactions, CombatDomain combat, SessionLightingState? lighting, SessionEventSink sink, GameEventQueue events)
    {
        _clock = clock; _flags = flags; _flagView = new(flags); _limits = limits; _actor = actor; _revision = revision; _maps = maps;
        _story = story; _party = party; _hero = hero; _inventory = inventory; _equipment = equipment; _effects = effects;
        _experience = experience; _advancement = advancement; _random = random; _life = life; _reactions = reactions;
        _combat = combat;
        _lighting = lighting;
        _events = events;
        sink.Owner = this;
        _combatRolls = new(this);
        _totalChanges = maps.Values.Sum(changes => changes.Count);
    }

    /// <summary>
    /// Starts a new session from explicit inputs. Rejection leaves no component alive. A hero (ARC-WORLD-015) is checked
    /// before any component is built (<see cref="GameSessionFailure.InvalidHero"/>); its gold and kit items enter the PC's
    /// inventory at revision 0, the kit as <c>kit:1</c>, <c>kit:2</c>, … in order, and it starts unharmed (ARC-COMBAT-002).
    /// The generator starts at draw 0 of the descriptor's seed (ARC-WORLD-019).
    /// </summary>
    public static GameSessionCreationResult Create(GameSessionDescriptor? descriptor)
    {
        if (descriptor?.Flags is null || !Enum.IsDefined(descriptor.Rules) || !Enum.IsDefined(descriptor.Difficulty))
            return Rejected(GameSessionFailure.InvalidDescriptor);
        if (!SessionValidation.Actor(descriptor.Actor)) return Rejected(GameSessionFailure.InvalidActor);
        var limits = descriptor.Limits ?? new GameSessionLimits();
        HeroDomain? hero = null;
        var inventory = new InventoryDomain();
        if (descriptor.Hero is { } definition)
        {
            hero = HeroDomain.Build(definition.Prototype, definition.Race, definition.Gender, definition.Stats, definition.Level,
                definition.UnspentPoints, definition.Alignment, definition.RaceEffect, definition.GenderEffect, definition.Experience,
                definition.Advancement);
            var kit = definition.Kit?.ToArray();
            if (hero is null || definition.Gold < 0 || kit is null || kit.Length > limits.MaxPcItems)
                return Rejected(GameSessionFailure.InvalidHero);
            // A new PC's level set runs its scheme over the starting points (ARC-WORLD-036).
            if (CreationScheme(hero, descriptor.AutoLevel) is { } outcome)
                hero.Apply(AdvancementDomain.WithSheet(hero.State, outcome.Sheet, hero.State.Experience));
            for (var index = 0; index < kit.Length; index++)
            {
                if (kit[index] is not { } item || !Enum.IsDefined(item.Kind.Type) || !InventoryDomain.Admitted(item.Kind.Type))
                    return Rejected(GameSessionFailure.InvalidHero);
                inventory.Add(new SessionKitItem(HeroDomain.KitEntity(index), item.Kind));
            }
            inventory.Gold = definition.Gold;
        }
        var clock = GameClockOwner.Bootstrap(descriptor.Clock);
        if (clock.Owner is null) return Rejected(GameSessionFailure.ClockRejected, clock.Failure);
        var flags = GlobalFlagOwner.Create(descriptor.Flags);
        if (flags.Owner is null)
        {
            clock.Owner.Dispose();
            return Rejected(GameSessionFailure.FlagsRejected, flagReason: flags.Reason);
        }
        var life = new LifeDomain { Hero = hero is null ? null : new SessionLifeRecord(LifeState.Unharmed, null) };
        var sink = new SessionEventSink();
        var owner = new GameSessionOwner(clock.Owner, flags.Owner, limits, descriptor.Actor, 0, [], new(), new(), hero, inventory,
            new(), new(), new(descriptor.Experience), new(descriptor.AutoLevel), new(descriptor.RandomSeed), life, new(), new(), null, sink,
            new GameEventQueue(SessionEventKinds.Build(sink), SessionEventKinds.Limits))
        { _rules = GameRules.For(descriptor.Rules), _difficulty = descriptor.Difficulty };
        return new(owner, GameSessionFailure.None, ClockFailure.None, GlobalFlagReason.None);
    }

    /// <summary>
    /// Builds a new owner from a snapshot after checking world, module, clock profile and budgets.
    /// Never touches an existing owner, so a refused restore keeps the previous session usable.
    /// The new owner has a fresh identity: versions and clock tokens of any earlier owner are stale.
    /// </summary>
    public static GameSessionCreationResult Restore(GameSessionSnapshot? snapshot, GameSessionRestoreOptions? options)
    {
        if (options is null || !Enum.IsDefined(options.Rules) || !Enum.IsDefined(options.Difficulty))
            return Rejected(GameSessionFailure.InvalidDescriptor);
        if (snapshot is null) return Rejected(GameSessionFailure.InvalidSnapshot);
        if (snapshot.SchemaVersion != GameSessionSnapshot.CurrentSchemaVersion) return Rejected(GameSessionFailure.UnsupportedSchema);
        if (snapshot.Identity.World != options.ExpectedIdentity.World) return Rejected(GameSessionFailure.WrongWorld);
        if (snapshot.Identity.Module != options.ExpectedIdentity.Module) return Rejected(GameSessionFailure.WrongModule);
        if (snapshot.Clock.Profile != options.ExpectedClockProfile) return Rejected(GameSessionFailure.IncompatibleClockProfile);
        if (snapshot.Revision < 0 || snapshot.Random.Draws < 0) return Rejected(GameSessionFailure.InvalidSnapshot);
        if (!SessionValidation.Actor(snapshot.Actor)) return Rejected(GameSessionFailure.InvalidActor);
        if (snapshot.Lighting is { } lighting && !SessionValidation.Lighting(lighting)) return Rejected(GameSessionFailure.InvalidLighting);
        var limits = options.Limits ?? new GameSessionLimits();
        var failure = ImportMaps(snapshot.Maps, limits, out var maps);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportStory(snapshot.Story, snapshot.Clock.GameTimestampMs, out var story);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportParty(snapshot.Party, limits, out var party);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportHero(snapshot.Hero, out var hero);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportInventory(snapshot.Inventory, hero is not null, limits, out var inventory);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportEquipment(snapshot.Equipment, inventory, hero is not null, out var equipment);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportLife(snapshot.Life, hero is not null, snapshot.Clock.GameTimestampMs, limits, out var life);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportEffects(snapshot.Effects, hero, life, snapshot.Clock.GameTimestampMs, out var effects);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportExperience(snapshot.Experience, options.Experience, limits, out var experience);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportAdvancement(snapshot.Advancement, options.AutoLevel, limits, out var advancement);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportReactions(snapshot.Reactions, limits, out var reactions);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportCombat(snapshot.Combat, hero is not null, out var combat);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportCombatMode(snapshot.CombatMode, hero is not null, combat);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportSound(snapshot.Sound, GameRules.For(options.Rules).Sound, options.SoundMapDefault, out var sound);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        var sink = new SessionEventSink();
        failure = ImportEvents(snapshot.Events, hero?.State, life, snapshot.Clock.GameTimestampMs, sink,
            GameRules.For(options.Rules).Events, out var events);
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
        var owner = new GameSessionOwner(clock.Owner, flags.Owner, limits, snapshot.Actor, snapshot.Revision, maps, story, party,
            hero, inventory, equipment, effects, experience, advancement, new(snapshot.Random.Seed, snapshot.Random.Draws), life, reactions, combat,
            snapshot.Lighting, sink, events!)
        { _rules = GameRules.For(options.Rules), _difficulty = options.Difficulty, _sound = sound };
        return new(owner, GameSessionFailure.None, ClockFailure.None, GlobalFlagReason.None);
    }

    public GameSessionOwnerIdentity Identity { get { ThrowIfDisposed(); return _identity; } }
    public GameSessionVersion Version { get { ThrowIfDisposed(); return new(_identity, _revision); } }
    public GameSessionLimits Limits { get { ThrowIfDisposed(); return _limits; } }

    /// <summary>The session's flag owner for reads, writes and adapters. Its lifetime belongs to the session.</summary>
    public IVersionedGlobalFlagOwner Flags { get { ThrowIfDisposed(); return _flagView; } }

    /// <summary>Current GF version, the expected value for the next flag read or write.</summary>
    public GlobalFlagVersion FlagVersion { get { ThrowIfDisposed(); return _flags.Version; } }

    public ClockSnapshot QueryClockSnapshot() { ThrowIfDisposed(); return _clock.QueryClockSnapshot(); }

    /// <summary>Forwarded to the clock. An advance that leaves the hour of the recorded lighting clears it, so a
    /// capture never pairs a time with another hour's scheme; the host records the new hour's lighting. While a turn-based
    /// round is active (ARC-COMBAT-003) an ordinary advance is NoChange after the clock's own checks: game time then moves
    /// only by the round's one second, as in the source; an explicit wait still advances.</summary>
    public ClockMutationResult AdvanceGameTime(ClockToken token, long durationGameMs, ClockAdvanceReason reason)
    {
        ThrowIfDisposed();
        if (_combat.Round is not null && reason == ClockAdvanceReason.OrdinaryAdvance && durationGameMs > 0)
            return _clock.AdvanceGameTime(token, 0, reason);
        return AdvanceClock(token, durationGameMs, reason);
    }

    private ClockMutationResult AdvanceClock(ClockToken token, long durationGameMs, ClockAdvanceReason reason)
    {
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

    /// <summary>
    /// Places the actor, on the same map or across a transition. Map changes stay with their map; the pending timers of the
    /// PC's followers move with the PC to the new map (ARC-WORLD-024), and a timer that would collide with one already
    /// pending there refuses the whole step (<see cref="GameSessionFailure.EventConflict"/>).
    /// </summary>
    public GameSessionMutationResult PlaceActor(GameSessionVersion expected, ActorPlacement placement)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Actor(placement)) return Reject(GameSessionFailure.InvalidActor);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (placement == _actor) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        if (MoveFollowerEvents(placement.Map) is var moved && moved != GameSessionFailure.None) return Reject(moved);
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

    /// <summary>
    /// ARC-WORLD-017: one guarded atomic step over the actor and several map changes, such as a map transfer that
    /// carries the PC's followers. Every input is validated (the actor when given, each map and key, no key listed
    /// twice), then the version, then the whole result: NoChange when nothing would differ, the budget against the state
    /// after every edit (an update of an existing key never needs room), revision capacity. A refusal applies nothing;
    /// otherwise every edit applies and the revision moves by exactly one.
    /// </summary>
    public GameSessionMutationResult ApplyChanges(GameSessionVersion expected, ActorPlacement? actor, IReadOnlyList<MapChangeEdit> changes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(changes);
        if (actor is { } placement && !SessionValidation.Actor(placement)) return Reject(GameSessionFailure.InvalidActor);
        var seen = new HashSet<(SessionMapId, MapChangeKey)>();
        foreach (var change in changes)
        {
            if (!SessionValidation.Map(change.Map)) return Reject(GameSessionFailure.InvalidMap);
            if (!SessionValidation.Key(change.Key) || !seen.Add((change.Map, change.Key))) return Reject(GameSessionFailure.InvalidChangeKey);
        }
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var actorChanged = actor is { } next && next != _actor;
        var effective = changes.Where(change =>
        {
            var current = _maps.TryGetValue(change.Map, out var map) && map.TryGetValue(change.Key, out var value) ? value : (long?)null;
            return current != change.Value;
        }).ToArray();
        if (!actorChanged && effective.Length == 0) return NoChange();
        // The budget is checked on the state after the whole step.
        var counts = _maps.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
        var total = _totalChanges;
        foreach (var change in effective)
        {
            var exists = _maps.TryGetValue(change.Map, out var map) && map.ContainsKey(change.Key);
            var delta = change.Value is null ? (exists ? -1 : 0) : (exists ? 0 : 1);
            counts[change.Map] = counts.GetValueOrDefault(change.Map) + delta; total += delta;
        }
        if (counts.Count(pair => pair.Value > 0) > _limits.MaxMaps || counts.Values.Any(count => count > _limits.MaxChangesPerMap) || total > _limits.MaxTotalChanges)
            return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        // The last refusal: an atomic move that changes nothing when it fails.
        if (actorChanged && MoveFollowerEvents(actor!.Value.Map) is var moved && moved != GameSessionFailure.None) return Reject(moved);

        // All refusals precede the commit.
        if (actorChanged) _actor = actor!.Value;
        foreach (var change in effective)
        {
            if (change.Value is { } value)
            {
                if (!_maps.TryGetValue(change.Map, out var map)) _maps.Add(change.Map, map = []);
                if (map.TryAdd(change.Key, value)) _totalChanges++;
                else map[change.Key] = value;
            }
            else
            {
                var map = _maps[change.Map];
                map.Remove(change.Key); _totalChanges--;
                if (map.Count == 0) _maps.Remove(change.Map);
            }
        }
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

    /// <summary>Whether a rumor is quelled (proven false; ARC-UI-008). The set is game-wide, not the PC's.</summary>
    public bool QueryRumorQuelled(int rumor)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsRumor(rumor)) throw new ArgumentOutOfRangeException(nameof(rumor));
        return _story.QuelledRumors.Contains(rumor);
    }

    /// <summary>Detached copy of the story domain in ascending order.</summary>
    public SessionStoryState QueryStory() { ThrowIfDisposed(); return _story.Capture(); }

    /// <summary>
    /// ARC-UI-008: the quests of the PC's logbook, as the original lists them (checked in the executable, ledger LB6): every
    /// record whose stored state is not Unknown (a quest botched while still Unknown is left out), reported Botched when its
    /// botched mark is set and with its stored state otherwise, ordered by the game time of its last change. Records with
    /// the same time are ordered by quest number; the original sorts with its run-time library's unstable sort, whose order
    /// for equal times is not modelled.
    /// </summary>
    [OriginalFunction(0x4C52E0, OriginalFunctionScope.Partial)]
    public IReadOnlyList<SessionQuestLogEntry> QueryQuestLog()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_story.Quests.Values.Where(quest => quest.State != SessionQuestState.Unknown)
            .OrderBy(quest => quest.TimestampMs).ThenBy(quest => quest.Quest)
            .Select(quest => new SessionQuestLogEntry(quest.Quest, quest.Effective, quest.TimestampMs)).ToArray());
    }

    /// <summary>
    /// ARC-UI-008: the rumors of the PC's logbook, as the original lists them (ledger LB9): every rumor the PC knows, with the
    /// game time it was learned and whether it is quelled, ordered by that time. Rumors with the same time are ordered by
    /// number; the original's order for equal times is its run-time library sort's and is not modelled.
    /// </summary>
    [OriginalFunction(0x4C5A40, OriginalFunctionScope.Partial)]
    public IReadOnlyList<SessionRumorLogEntry> QueryRumorLog()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_story.Rumors.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key)
            .Select(pair => new SessionRumorLogEntry(pair.Key, pair.Value, _story.QuelledRumors.Contains(pair.Key))).ToArray());
    }

    /// <summary>
    /// Requests a PC quest state with the source rules: a Completed, OtherCompleted or Botched quest and a request
    /// not above the current state change nothing. Otherwise a quest whose shared state is still Accepted takes
    /// the request (Completed/OtherCompleted complete it for everyone, Botched botches it); once the shared state
    /// left Accepted, the request becomes OtherCompleted (shared Completed) or Botched. Botched keeps the stored
    /// state and marks it. The change is stamped with the current session game time. A Completed request that passes these
    /// checks first gives the hero the quest's experience (ARC-WORLD-030, as 0x4C4D20 does; see
    /// <see cref="AwardQuestExperience"/>) when the session has experience data and a hero, in the same revision. Alignment
    /// and reaction effects of the source belong to their own owners and are not applied here.
    /// </summary>
    public GameSessionMutationResult SetQuestState(GameSessionVersion expected, int quest, SessionQuestState requested) =>
        SetQuestState(expected, quest, requested, out _);

    /// <summary><see cref="SetQuestState(GameSessionVersion, int, SessionQuestState)"/>, also returning the experience the
    /// completion gave (null when it gave none or the request changed nothing).</summary>
    [OriginalFunction(0x4C4D20, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult SetQuestState(GameSessionVersion expected, int quest, SessionQuestState requested,
        out SessionExperienceAward? experience)
    {
        ThrowIfDisposed();
        experience = null;
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
        // The source gives the experience for the request, before the shared state can turn it into OtherCompleted.
        var award = requested == SessionQuestState.Completed && _hero is not null && _experience.Rules is { } rules
            ? Give(rules.QuestExperience(rules.QuestLevel(quest))) : null;

        // All refusals precede the commit.
        _story.Quests[quest] = next;
        if (shared != SessionQuestState.Accepted) _story.GlobalQuests[quest] = shared;
        if (award is not null)
        {
            _hero!.Apply(award.Hero);
            experience = award.Award;
        }
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

    /// <summary>
    /// ARC-UI-008: marks a rumor quelled (proven false), as the dialog action <c>RQ</c> does (ledger LB11–LB12): a game-wide
    /// mark that never clears and does not need the PC to know the rumor. Quelling a quelled rumor is NoChange. A number
    /// outside the rumor range is refused (the original writes it unchecked).
    /// </summary>
    [OriginalFunction(0x4C5690, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult QuellRumor(GameSessionVersion expected, int rumor)
    {
        ThrowIfDisposed();
        if (!SessionStoryLimits.IsRumor(rumor)) return Reject(GameSessionFailure.InvalidRumor);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_story.QuelledRumors.Contains(rumor)) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.QuelledRumors.Add(rumor);
        return Changed();
    }

    /// <summary>The player character's saved fields, or null for a session started without one.</summary>
    public SessionHeroState? QueryHero() { ThrowIfDisposed(); return _hero?.State; }

    /// <summary>
    /// The hero's stats under <paramref name="context"/> (base and effective values, derived stats, applied innate effects),
    /// or null without a hero. An out-of-range context is the character owner's typed failure.
    /// </summary>
    public CharacterStatsQueryResult? QueryHeroStats(CharacterStatContext context)
    {
        ThrowIfDisposed();
        return _hero?.Stats.Query(context);
    }

    /// <summary>
    /// The hero's DLG reply-eligibility facts, or null without a hero: gender, effective intelligence and the source race
    /// index. <see cref="DialogPcFacts.IsDumb"/> is false: the source sets it only for backgrounds 55, 74 and 75, and the
    /// session models no background (a pre-made hero's stored background 39..50 is display data, ARC-RES-027).
    /// </summary>
    public DialogPcFacts? QueryHeroDialogFacts() { ThrowIfDisposed(); return _hero?.DialogFacts(); }

    /// <summary>The PC's gold: the hero's starting coins, 0 without a hero. Gold pickup and spending are not modelled.</summary>
    public int QueryGold() { ThrowIfDisposed(); return _inventory.Gold; }

    /// <summary>Detached copy of the hero's starting kit (<c>kit:1</c>, <c>kit:2</c>, … in kit order); empty without a hero.
    /// In the PC's inventory these items precede <see cref="QueryPcInventory"/>.</summary>
    public IReadOnlyList<SessionKitItem> QueryHeroKit() { ThrowIfDisposed(); return Array.AsReadOnly(_inventory.Kit.ToArray()); }

    /// <summary>The NPC's party record; an NPC never recorded is free with no flags. An invalid entity id throws.</summary>
    public SessionCompanion QueryCompanion(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _party.Get(entity);
    }

    /// <summary>Detached copy of the party: the follower array in order, then waiting and flagged free NPCs.</summary>
    public SessionPartyState QueryParty() { ThrowIfDisposed(); return _party.Capture(); }

    /// <summary>
    /// Dialog <c>JO force node</c> (ARC-RES-024 C1, C5): runs the follow check with the charisma limit ignored when
    /// <paramref name="force"/>, then joins: the NPC leaves any current standing and is appended at the end of the
    /// follower array (never duplicated), forced when <paramref name="force"/>, jilted cleared, spread out kept. As read
    /// in the source, an NPC already in the array counts toward the limit itself. A refusal is
    /// <see cref="GameSessionFailure.ConsentRefused"/> with the reason in <see cref="PartyMutationResult.Consent"/>.
    /// Facts naming another leader for an NPC the PC already leads (following or waiting) contradict the session and
    /// are <see cref="GameSessionFailure.InvalidConsentFacts"/>.
    /// </summary>
    public PartyMutationResult JoinParty(GameSessionVersion expected, string entity, PartyConsentFacts facts, bool force)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(facts);
        if (!SessionValidation.Entity(entity)) return RejectParty(GameSessionFailure.InvalidCompanion);
        if (!ValidFacts(facts)) return RejectParty(GameSessionFailure.InvalidConsentFacts);
        if (!IsCurrent(expected)) return RejectParty(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        if (current.LedByPc && facts.OtherLeaderCharisma is not null) return RejectParty(GameSessionFailure.InvalidConsentFacts);
        var consent = Consent(facts, current.Forced, force);
        if (consent != FollowConsent.Accepted) return RejectParty(GameSessionFailure.ConsentRefused, consent);
        return Append(current, force, consent);
    }

    /// <summary>
    /// A join without a check, always forced: script follow, mind control and the follow spell component (C17). It also
    /// brings a waiting NPC back.
    /// </summary>
    public PartyMutationResult ForceJoinParty(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return RejectParty(GameSessionFailure.InvalidCompanion);
        if (!IsCurrent(expected)) return RejectParty(GameSessionFailure.StaleVersion);
        return Append(_party.Get(entity), true, null);
    }

    /// <summary>
    /// Dialog <c>UW force node</c> (C11): only a waiting NPC; runs the follow check (charisma limit ignored when
    /// <paramref name="force"/>; waiting NPCs are never forced and are outside the array). A refusal keeps it waiting.
    /// Success re-appends it at the end of the array, forced when <paramref name="force"/>.
    /// </summary>
    public PartyMutationResult UnwaitParty(GameSessionVersion expected, string entity, PartyConsentFacts facts, bool force)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(facts);
        if (!SessionValidation.Entity(entity)) return RejectParty(GameSessionFailure.InvalidCompanion);
        if (!ValidFacts(facts)) return RejectParty(GameSessionFailure.InvalidConsentFacts);
        if (!IsCurrent(expected)) return RejectParty(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        if (current.Standing != CompanionStanding.Waiting) return RejectParty(GameSessionFailure.NotWaiting);
        // The PC leads a waiting NPC, so facts naming another leader contradict the session.
        if (facts.OtherLeaderCharisma is not null) return RejectParty(GameSessionFailure.InvalidConsentFacts);
        var consent = Consent(facts, false, force);
        if (consent != FollowConsent.Accepted) return RejectParty(GameSessionFailure.ConsentRefused, consent);
        return Append(current, force, consent);
    }

    /// <summary>
    /// Wait (C11; the source's dialog WA action, not admitted by the conversation runtime yet, the wait order and the
    /// script wait): a follower leaves the array (forced cleared) and keeps the PC as leader. Already waiting is NoChange; a free NPC is <see cref="GameSessionFailure.NotInParty"/>.
    /// </summary>
    public GameSessionMutationResult WaitParty(GameSessionVersion expected, string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return Reject(GameSessionFailure.InvalidCompanion);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        if (current.Standing == CompanionStanding.Free) return Reject(GameSessionFailure.NotInParty);
        if (current.Standing == CompanionStanding.Waiting) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _party.Put(current with { Standing = CompanionStanding.Waiting, Forced = false }, false);
        return Changed();
    }

    /// <summary>
    /// Leave (C6): a follower or waiting NPC becomes free, forced and waiting cleared, spread out kept; jilted when
    /// <see cref="PartyLeave.Jilt"/>. An unforced leave is <see cref="GameSessionFailure.LeaveRefused"/> under an
    /// extraordinary-charisma leader or mind control. A free NPC is NoChange.
    /// </summary>
    public GameSessionMutationResult LeaveParty(GameSessionVersion expected, string entity, PartyLeave leave)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(leave);
        if (!SessionValidation.Entity(entity)) return Reject(GameSessionFailure.InvalidCompanion);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        if (current.Standing == CompanionStanding.Free) return NoChange();
        if (!leave.Force && !FollowConsentRules.MayLeaveUnforced(leave.LeaderCharisma, leave.MindControlled))
            return Reject(GameSessionFailure.LeaveRefused);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        // The NPC already has a record, so a flagged free record needs no new budget.
        _party.Put(current with { Standing = CompanionStanding.Free, Forced = false, Jilted = leave.Jilt }, false);
        return Changed();
    }

    /// <summary>Dialog SO / SC (C1): sets or clears the NPC's spread-out flag in any standing.</summary>
    public GameSessionMutationResult SetSpreadOut(GameSessionVersion expected, string entity, bool spreadOut)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return Reject(GameSessionFailure.InvalidCompanion);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        if (current.SpreadOut == spreadOut) return NoChange();
        var next = current with { SpreadOut = spreadOut };
        if (!next.IsBaseline && !_party.Records.ContainsKey(entity) && _party.Records.Count >= _limits.MaxCompanions)
            return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _party.Put(next, false);
        return Changed();
    }

    /// <summary>
    /// Attaches (or replaces) the T0 inventory of one map, the baseline the inventory rules read. The seed is checked and
    /// copied (<see cref="GameSessionFailure.InvalidInventorySeed"/>). Every item the PC took from this map must be in it
    /// with its saved kind, and no item the PC took from another map or another attached seed lists may be in it, else
    /// <see cref="GameSessionFailure.InventoryMismatch"/> (a save of other data, or one object on two maps). A seed is not session state: like recorded
    /// lighting it moves no revision, it is never saved and the host attaches it again after a restore.
    /// </summary>
    public GameSessionMutationResult AttachMapInventory(SessionMapId map, SessionMapInventorySeed seed)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (InventoryDomain.Validate(seed) is not { } admitted) return Reject(GameSessionFailure.InvalidInventorySeed);
        foreach (var item in _inventory.Pc)
        {
            var listed = admitted.Items.TryGetValue(item.Entity, out var row);
            if (item.Origin == map ? !listed || row!.Facts.Kind != item.Kind : listed) return Reject(GameSessionFailure.InventoryMismatch);
        }
        // Kit items came from no map, so no seed may list them.
        if (_inventory.Kit.Any(item => admitted.Items.ContainsKey(item.Entity))) return Reject(GameSessionFailure.InventoryMismatch);
        // An object lies on one map: an item another attached map lists would be hidden there too once taken.
        foreach (var (other, attached) in _inventory.Seeds)
            if (other != map && admitted.Items.Keys.Any(attached.Items.ContainsKey)) return Reject(GameSessionFailure.InventoryMismatch);
        if (!_inventory.Seeds.ContainsKey(map) && _inventory.Seeds.Count >= _limits.MaxMaps) return Reject(GameSessionFailure.BudgetExceeded);
        _inventory.Seeds[map] = admitted;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, new(_identity, _revision));
    }

    /// <summary>The current inventory of an attached map (its seed without the items the PC took), or null when no seed
    /// is attached.</summary>
    public SessionMapInventory? QueryMapInventory(SessionMapId map)
    {
        ThrowIfDisposed();
        return _inventory.Seeds.TryGetValue(map, out var seed) ? _inventory.View(map, seed, KilledNpc) : null;
    }

    /// <summary>Detached copy of the items the PC took from the world, in the order they entered its list. The hero's
    /// starting kit, which precedes them, is <see cref="QueryHeroKit"/>.</summary>
    public IReadOnlyList<SessionPcItem> QueryPcInventory() { ThrowIfDisposed(); return Array.AsReadOnly(_inventory.Pc.ToArray()); }

    /// <summary>
    /// Ground pickup (the pickup goal, then the item transfer 0x4617F0): an item lying on the actor's map goes to the end of
    /// the PC's list. Reach, the walk to the item and the combat action-point cost belong to the host. Refused before any
    /// effect: an item with an item <c>SAP_GET</c> script (not run here), a stack or key type (stack merge and key rings
    /// are not modelled), and a refusal of <paramref name="insertCheck"/>, which runs after every other check. An item
    /// <c>SAP_INSERT_ITEM</c> script runs through <paramref name="scripts"/> (ARC-SCRIPT-013, see <see cref="ISessionItemScriptHost"/>);
    /// without a host such an item is refused.
    /// </summary>
    [OriginalFunction(0x4617F0, OriginalFunctionScope.Partial)]
    public InventoryMutationResult PickUpItem(GameSessionVersion expected, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
        if (!seed.Items.TryGetValue(item, out var row) || row.Cell is null || _inventory.Taken.Contains(item))
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        return Take(seed, new(item, row.Facts), false, insertCheck, scripts);
    }

    /// <summary>
    /// Loot (a click on a dead critter opens it as a container; the moved item runs 0x4617F0 to the PC): an item
    /// still in the list of a dead NPC on the actor's map goes to the end of the PC's list. Only an NPC the seed marks dead, or one
    /// that died in this session (ARC-COMBAT-005, <see cref="QueryNpcKilled"/>), is lootable here: chests (generated on first open) are not. Refused before any effect, beyond the item rules of
    /// <see cref="PickUpItem"/>: a holder with a <c>SAP_USE</c> script, an item in a worn slot (unequip effects), and an item
    /// <c>SAP_REMOVE_ITEM</c> script. The PC's own <c>SAP_TRANSFER</c> attachment is not modelled (the session has no PC
    /// object).
    /// </summary>
    public InventoryMutationResult LootItem(GameSessionVersion expected, string holder, string item, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(holder) || !SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
        if (!seed.Holders.TryGetValue(holder, out var row)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (row.Kind != SessionHolderKind.Npc || !row.Dead && !KilledNpc(holder)) return RejectItem(GameSessionFailure.HolderNotLootable);
        if (row.ScriptPoints.Contains(SessionScriptPoint.Use)) return RejectItem(GameSessionFailure.HolderScriptUnsupported);
        if (!seed.HolderOf.TryGetValue(item, out var of) || of != holder || _inventory.Taken.Contains(item))
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        return Take(seed, new(item, seed.Items[item].Facts), true, insertCheck, scripts);
    }

    /// <summary>
    /// Negative dialog <c>IN -name</c> (the dialog <c>IN</c> action, ARC-RES-021): the first item in the NPC's current list
    /// whose name class is <paramref name="name"/> goes to the end of the PC's list, under the item rules of
    /// <see cref="LootItem"/>. No such item and no substitute inventory changes nothing, as in the source; an NPC with a
    /// substitute inventory then is <see cref="GameSessionFailure.SubstituteInventoryUnsupported"/>. The source drops an item
    /// the PC cannot take at the PC's location (into a junk pile when items lie there); that fallback is not modelled, so a
    /// refusal of <paramref name="insertCheck"/> is <see cref="GameSessionFailure.InsertRefused"/> with no effect.
    /// </summary>
    public InventoryMutationResult GiveItemToPc(GameSessionVersion expected, string npc, int name, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || name < 1) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
        if (!seed.Holders.TryGetValue(npc, out var row) || row.Kind != SessionHolderKind.Npc) return RejectItem(GameSessionFailure.UnknownHolder);
        if (_inventory.Current(seed, row).FirstOrDefault(entry => entry.Facts.Kind.Name == name) is not { } found)
            return row.HasSubstituteInventory ? RejectItem(GameSessionFailure.SubstituteInventoryUnsupported)
                : new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, new(_identity, _revision));
        return Take(seed, found, true, insertCheck, scripts);
    }

    /// <summary>The generator's seed and position (draws made so far).</summary>
    public SessionRandomState QueryRandom() { ThrowIfDisposed(); return new(_random.Seed, _random.Draws); }

    /// <summary>
    /// One labelled gameplay roll (ARC-WORLD-019): a uniform integer in <paramref name="min"/>..<paramref name="max"/>
    /// inclusive from the session's seeded sequence. The label (1..64 ASCII letters, digits and <c>. _ : -</c>, such as
    /// <c>combat.AttackCheck</c>) names what the roll decides; it does not change the value, which depends only on the seed
    /// and the position, so the same calls after the same restore return the same rolls. Equal bounds draw nothing.
    /// A bad label or range is <see cref="GameSessionFailure.InvalidRoll"/>, a generator at its last draw
    /// <see cref="GameSessionFailure.RandomExhausted"/>; a refusal draws nothing. Like clock and flag changes, a roll
    /// takes no expected version and does not move the session revision; the position is saved with the snapshot.
    /// </summary>
    public SessionRollResult Roll(string label, int min, int max)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Code(label, SessionValidation.MaxRollLabelCharacters) || min > max)
            return new(null, GameSessionFailure.InvalidRoll);
        if (!_random.TryNext(min, max, out var value)) return new(null, GameSessionFailure.RandomExhausted);
        return new(new(label, min, max, value, _random.Draws), GameSessionFailure.None);
    }

    /// <summary>The session generator as the attack resolver's roll source; rolls are labelled <c>combat.</c> and the
    /// purpose name (for example <c>combat.DodgeCheck</c>). Its lifetime belongs to the session.</summary>
    public ICombatRollSource CombatRolls { get { ThrowIfDisposed(); return _combatRolls; } }

    /// <summary>The hero's life (record and current values), or null for a session without a hero.</summary>
    public SessionLifeView? QueryHeroLife()
    {
        ThrowIfDisposed();
        if (_hero is null) return null;
        var record = _life.Hero!.Value;
        return new(record, LifeRules.Evaluate(record.State, HeroLifeFacts(record.State)), true);
    }

    /// <summary>
    /// The life of a target: the hero, or an NPC under the host's facts (its record, or its loaded state while it has none).
    /// A hero target without a hero is <see cref="GameSessionFailure.InvalidLifeTarget"/>; facts or a loaded state out of
    /// range are <see cref="GameSessionFailure.InvalidLifeFacts"/> and <see cref="GameSessionFailure.InvalidLifeState"/>.
    /// </summary>
    public SessionLifeQueryResult QueryLife(SessionLifeTarget target)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(target);
        var failure = Resolve(target, out var record, out var facts, out var recorded);
        if (failure != GameSessionFailure.None) return new(failure, null);
        return new(GameSessionFailure.None, new(record, LifeRules.Evaluate(record.State, facts!), recorded));
    }

    /// <summary>The saved life record of an NPC, or null while the session never changed it. An invalid entity id throws.</summary>
    public SessionLifeRecord? QueryLifeRecord(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _life.Critters.TryGetValue(entity, out var record) ? record : null;
    }

    /// <summary>
    /// Applies one damage packet (<see cref="LifeRules.ApplyDamage"/>; build it from an attack with
    /// <see cref="LifeDamage.FromAttack"/>). The knockout and second-arm rolls come from the session generator
    /// (<c>combat.KnockoutFatigue</c>, <c>combat.CrippleBothArms</c>). A death stamps the record with the current game time.
    /// </summary>
    public SessionLifeResult ApplyDamage(GameSessionVersion expected, SessionLifeTarget target, LifeDamage damage)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(damage);
        var valid = LifeRules.IsValid(damage);
        return Life(expected, target, valid, (state, facts, rolls) => LifeRules.ApplyDamage(state, facts, damage, rolls));
    }

    /// <summary>A kill (<see cref="LifeRules.Kill"/>), such as a script's; a death stamps the current game time.</summary>
    public SessionLifeResult Kill(GameSessionVersion expected, SessionLifeTarget target)
    {
        ThrowIfDisposed();
        return Life(expected, target, true, (state, facts, _) => LifeRules.Kill(state, facts));
    }

    /// <summary>One healing (<see cref="LifeRules.Heal"/>), resurrection included; amounts are 0..1,000,000.</summary>
    public SessionLifeResult Heal(GameSessionVersion expected, SessionLifeTarget target, LifeHeal heal)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(heal);
        var valid = heal.HitPoints is >= 0 and <= LifeLimits.MaxAmount && heal.Fatigue is >= 0 and <= LifeLimits.MaxAmount &&
            heal.Poison is >= 0 and <= LifeLimits.MaxAmount;
        return Life(expected, target, valid, (state, facts, _) => LifeRules.Heal(state, facts, heal));
    }

    /// <summary>
    /// A fatigue recovery event scheduled at <paramref name="scheduledAtGameMs"/> fires now (<see cref="LifeRules.RecoverFatigue"/>
    /// with <see cref="LifeRules.FatigueRecoverySteps"/> up to the session clock). A time after the clock is
    /// <see cref="GameSessionFailure.InvalidLifeAmount"/>. Scheduling the event belongs to the game event queue.
    /// </summary>
    public SessionLifeResult RecoverFatigue(GameSessionVersion expected, SessionLifeTarget target, long scheduledAtGameMs)
    {
        ThrowIfDisposed();
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var valid = scheduledAtGameMs >= 0 && scheduledAtGameMs <= now;
        return Life(expected, target, valid,
            (state, facts, _) => LifeRules.RecoverFatigue(state, facts, LifeRules.FatigueRecoverySteps(scheduledAtGameMs, now)));
    }

    /// <summary>A resting heal of <paramref name="hours"/> (0..87,600; <see cref="LifeRules.RestHeal"/>), for sleep or an NPC's hourly event.</summary>
    public SessionLifeResult RestHeal(GameSessionVersion expected, SessionLifeTarget target, int hours)
    {
        ThrowIfDisposed();
        return Life(expected, target, hours is >= 0 and <= LifeLimits.MaxRestHours,
            (state, facts, _) => LifeRules.RestHeal(state, facts, hours));
    }

    /// <summary>The 15-second poison damage tick (<see cref="LifeRules.PoisonDamage"/>); a death stamps the current game time.</summary>
    public SessionLifeResult PoisonDamageTick(GameSessionVersion expected, SessionLifeTarget target)
    {
        ThrowIfDisposed();
        return Life(expected, target, true, (state, facts, _) => LifeRules.PoisonDamage(state, facts));
    }

    /// <summary>The 120-second poison recovery tick (<see cref="LifeRules.PoisonRecoveryTick"/>).</summary>
    public SessionLifeResult PoisonRecoveryTick(GameSessionVersion expected, SessionLifeTarget target)
    {
        ThrowIfDisposed();
        return Life(expected, target, true, (state, facts, _) => LifeRules.PoisonRecoveryTick(state, facts));
    }

    /// <summary>The stun animation ended or could not start (<see cref="LifeRules.ClearStun"/>).</summary>
    public SessionLifeResult ClearStun(GameSessionVersion expected, SessionLifeTarget target)
    {
        ThrowIfDisposed();
        return Life(expected, target, true, (state, facts, _) => LifeRules.ClearStun(state, facts));
    }

    /// <summary>
    /// The active rules (owner decision D8, contract docs/world/rule-modes.md): the profile each domain reads at the moment
    /// it decides. Starts from the descriptor's or restore options' mode.
    /// </summary>
    public GameRules Rules { get { ThrowIfDisposed(); return _rules; } }

    /// <summary>
    /// Switches the rule mode at once, between any two calls, without a restart: the next decision of every domain
    /// follows the new mode. Domain state never depends on the mode, so nothing is converted, the revision does not move
    /// and the snapshot does not record it (D4). The same mode is <see cref="GameSessionDisposition.NoChange"/>; an
    /// undefined value is <see cref="GameSessionDisposition.Rejected"/> and keeps the active mode.
    /// </summary>
    public GameSessionDisposition SelectRules(RuleMode mode)
    {
        ThrowIfDisposed();
        if (!Enum.IsDefined(mode)) return GameSessionDisposition.Rejected;
        if (mode == _rules.Mode) return GameSessionDisposition.NoChange;
        _rules = GameRules.For(mode);
        return GameSessionDisposition.Changed;
    }

    /// <summary>The pending events (ARC-WORLD-024) in queue order: domain, deadline, sequence. Detached.</summary>
    public IReadOnlyList<GameEvent> QueryEvents() { ThrowIfDisposed(); return _events.QueryPending(); }

    /// <summary>
    /// One event pass at the session's game time (ARC-WORLD-024, slice E2): the due timers of the active map (the actor's),
    /// the hero's and the deferred SCR calls (ARC-SCRIPT-010, through an <see cref="ISessionScriptWorld"/> host) run in
    /// (deadline, sequence) order, at most <paramref name="budget"/> (1..500) of them, each through
    /// its life step. An NPC timer whose NPC the host no longer reports (<see cref="ISessionEventWorld.CritterFacts"/> null)
    /// or the session holds no life record for is dropped without running. Timers set during the pass wait for the next
    /// one. The pass takes no expected version, like a clock advance; each life step that changes a record adds one
    /// revision. Paused or turn-based time does not stop a pass: it fires what is already due. Hosts call it after each
    /// admitted clock advance and after each step of a long wait.
    /// </summary>
    public SessionEventPassResult ProcessEvents(ISessionEventWorld world, int budget = 500)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(world);
        if (_passWorld is not null || budget < 1 || budget > _events.Limits.MaxPerPass)
            return new(GameSessionFailure.InvalidEventPass, null, Array.Empty<SessionLifeFiring>(), new(_identity, _revision));
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var firings = new List<SessionLifeFiring>();
        var calls = new List<SessionScriptCallFiring>();
        var effects = new List<SessionEffectFiring>();
        var locks = new List<SessionLockFiring>();
        _passWorld = world;
        _passFirings = firings;
        _passCalls = calls;
        _passEffects = effects;
        _passLocks = locks;
        GameEventPassResult pass;
        try
        {
            // A life timer's NPC must be present with its life record; an object a deferred call names must exist (an
            // absent role always does).
            pass = _events.ProcessDue(new(new(0, now, 0), _actor.Map.RuntimeMap, budget, entity =>
                entity.Code == SessionEventKinds.AbsentRole || LockObjectExists(entity)
                || (_life.Critters.ContainsKey(entity.Code)
                    ? world.CritterFacts(new(entity.Map), entity.Code) is not null
                    : world is ISessionScriptWorld scripts && scripts.ObjectExists(new(entity.Map), entity.Code))));
        }
        finally
        {
            _passWorld = null;
            _passFirings = null;
            _passCalls = null;
            _passEffects = null;
            _passLocks = null;
        }
        return new(pass.Failure == GameEventFailure.None ? GameSessionFailure.None : GameSessionFailure.InvalidEventPass, pass,
            firings.AsReadOnly(), new(_identity, _revision)) { ScriptCalls = calls.AsReadOnly(), Effects = effects.AsReadOnly(),
            Locks = locks.AsReadOnly() };
    }

    /// <summary>
    /// ARC-SCRIPT-010: schedules a deferred SCR call after <paramref name="delayMs"/> (1 ms up to
    /// <see cref="SessionEventKinds.MaxScriptCallDelayMs"/>) of game time, as a call script in action does. One revision.
    /// </summary>
    public GameSessionMutationResult ScheduleScriptCall(GameSessionVersion expected, SessionScriptCall call, long delayMs)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(call);
        if (!ValidRole(call.Triggerer) || !ValidRole(call.Attachee)) return Reject(GameSessionFailure.InvalidEvents);
        if (delayMs is < 1 or > SessionEventKinds.MaxScriptCallDelayMs) return Reject(GameSessionFailure.InvalidEvents);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (_events.QueryPending().Count(e => e.Kind == SessionEventKinds.ScriptCall) >= SessionEventKinds.MaxScriptCalls ||
            _events.Count >= _events.Limits.MaxPending || now > long.MaxValue - delayMs) return Reject(GameSessionFailure.EventBudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var scheduled = _events.ScheduleAfter(SessionEventKinds.ScriptCall, SessionEventKinds.Payload(call, now, delayMs, _actor.Map.RuntimeMap),
            delayMs, new(0, now, 0));
        return scheduled.Disposition == GameEventScheduleDisposition.Added ? Changed() : Reject(GameSessionFailure.EventConflict);
    }

    /// <summary>
    /// ARC-SCRIPT-010: removes the first pending deferred call (in queue order) of <paramref name="script"/> whose
    /// attachee is the object <paramref name="attachee"/> names, by entity id whatever map either stores (the original
    /// compares the object handle; an absent attachee matches a call stored without one), as remove script call does;
    /// <see cref="GameSessionDisposition.NoChange"/> when there is none.
    /// </summary>
    public GameSessionMutationResult RemoveScriptCall(GameSessionVersion expected, int script, SessionScriptRole? attachee)
    {
        ThrowIfDisposed();
        if (!ValidRole(attachee)) return Reject(GameSessionFailure.InvalidEvents);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        bool Match(GameEvent e) => SessionEventKinds.Call(e.Payload) is var call && call.Script == script && call.Attachee?.Entity == attachee?.Entity;
        if (!_events.QueryPending().Any(e => e.Kind == SessionEventKinds.ScriptCall && Match(e))) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _events.RemoveFirst(SessionEventKinds.ScriptCall, Match);
        return Changed();
    }

    /// <summary>The pending deferred SCR calls in queue order.</summary>
    public IReadOnlyList<SessionPendingScriptCall> QueryScriptCalls()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_events.QueryPending().Where(e => e.Kind == SessionEventKinds.ScriptCall)
            .Select(e => new SessionPendingScriptCall(e.Sequence, e.DeadlineMs, SessionEventKinds.Call(e.Payload))).ToArray());
    }

    private static bool ValidRole(SessionScriptRole? role) => role is not { } r ||
        SessionValidation.Map(r.Map) && SessionValidation.Entity(r.Entity) && r.Entity != SessionEventKinds.AbsentRole;

    // Runs one deferred call through the pass's host; a host that cannot run scripts consumes it unrun.
    internal GameEventHandlerOutcome FireScriptCall(GameEventFiring firing)
    {
        var call = SessionEventKinds.Call(firing.Event.Payload);
        var ran = _passWorld is ISessionScriptWorld scripts && scripts.RunScriptCall(call);
        _passCalls?.Add(new(firing.Event, call, ran));
        return ran ? GameEventHandlerOutcome.Completed : GameEventHandlerOutcome.Refused;
    }

    // Runs one life timer (the queue already checked that an NPC's entity exists). Each branch follows the source handler:
    // fatigue recovery and the poison ticks act only while there is fatigue damage or poison, resting only on a living NPC
    // without a PC leader; what they write schedules the next timer as any life step does.
    internal GameEventHandlerOutcome FireLifeTimer(LifeTimer timer, GameEventFiring firing)
    {
        var e = firing.Event;
        var hero = e.Payload.Entities.IsEmpty;
        SessionLifeTarget target;
        var map = 0;
        if (hero)
        {
            if (_hero is null) return GameEventHandlerOutcome.Refused;
            target = SessionLifeTarget.Hero;
        }
        else
        {
            var entity = e.Payload.Entities[0];
            map = entity.Map;
            if (_passWorld?.CritterFacts(new(entity.Map), entity.Code) is not { } facts ||
                !_life.Critters.TryGetValue(entity.Code, out var saved)) return GameEventHandlerOutcome.Refused;
            target = SessionLifeTarget.Critter(entity.Code, facts, saved.State);
        }
        if (Resolve(target, out var record, out var lifeFacts, out _) != GameSessionFailure.None) return GameEventHandlerOutcome.Refused;
        var state = record.State;
        var dead = LifeRules.Evaluate(state, lifeFacts!).IsDead;
        var scheduledAt = e.Payload.Values[0];
        var now = firing.NowMs;
        SessionLifeResult result;
        switch (timer)
        {
            case LifeTimer.Fatigue:
                if (dead || state.FatigueDamage == 0) return GameEventHandlerOutcome.Completed;
                result = Life(Version, target, scheduledAt <= now,
                    (s, f, _) => LifeRules.RecoverFatigue(s, f, LifeRules.FatigueRecoverySteps(scheduledAt, now)),
                    (_, after) => after.FatigueDamage > 0 ? [LifeTimer.Fatigue] : [], map);
                break;
            case LifeTimer.PoisonDamage:
                if (state.PoisonLevel == 0) return GameEventHandlerOutcome.Completed;
                result = Life(Version, target, true, (s, f, _) => LifeRules.PoisonDamage(s, f), (_, _) => [LifeTimer.PoisonDamage], map);
                break;
            case LifeTimer.PoisonRecovery:
                if (state.PoisonLevel == 0) return GameEventHandlerOutcome.Completed;
                // TE10: in the Original mode a timer that stands for two source events applies the tick twice, the second
                // only while poison remains (as the second event finds it). The next timer stands for two either way.
                var twice = _rules.Events.PoisonRecoveryTwice && e.Payload.Values[1] == 2;
                result = Life(Version, target, true, (s, f, _) =>
                    {
                        var first = LifeRules.PoisonRecoveryTick(s, f);
                        return twice && first.After.PoisonLevel > 0 ? LifeRules.PoisonRecoveryTick(first.After, f) with { Before = s } : first;
                    },
                    (_, after) => after.PoisonLevel > 0 ? [LifeTimer.PoisonRecovery] : [], map, recoveryCopies: 2);
                break;
            case LifeTimer.Resting:
                if (hero || dead || _party.Get(target.Entity!).LedByPc) return GameEventHandlerOutcome.Completed;
                result = Life(Version, target, scheduledAt <= now,
                    (s, f, _) => LifeRules.RestHeal(s, f, LifeRules.RestingHours(scheduledAt, now)),
                    (before, after) => RestingWrites(before, after), map);
                break;
            default:
                return GameEventHandlerOutcome.Refused;
        }
        _passFirings?.Add(new(e, result));
        return result.Disposition == GameSessionDisposition.Rejected ? GameEventHandlerOutcome.Refused : GameEventHandlerOutcome.Completed;

        // The source's resting heal writes hit-point and fatigue damage that were positive, and each positive write
        // schedules its timer again.
        static IEnumerable<LifeTimer> RestingWrites(LifeState before, LifeState after)
        {
            if (before.HitPointDamage > 0 && after.HitPointDamage > 0) yield return LifeTimer.Resting;
            if (before.FatigueDamage > 0 && after.FatigueDamage > 0) yield return LifeTimer.Fatigue;
        }
    }

    // The timers a life step sets (docs/world/life-state-runtime.md: a positive fatigue-damage write schedules recovery in 80 s, a positive poison write
    // the 15-second damage and 120-second recovery ticks, a positive hit-point-damage write an NPC's hourly resting), as
    // changes from the stored state, plus the writes of a timer's own handler. One per critter and kind: a pending one stays.
    private static IEnumerable<LifeTimer> Timers(bool hero, LifeState before, LifeState after, Func<LifeState, LifeState, IEnumerable<LifeTimer>>? handler)
    {
        var timers = new SortedSet<LifeTimer>();
        if (after.FatigueDamage > 0 && after.FatigueDamage != before.FatigueDamage) timers.Add(LifeTimer.Fatigue);
        if (after.PoisonLevel > 0 && after.PoisonLevel != before.PoisonLevel)
        {
            timers.Add(LifeTimer.PoisonDamage);
            timers.Add(LifeTimer.PoisonRecovery);
        }
        if (!hero && after.HitPointDamage > 0 && after.HitPointDamage != before.HitPointDamage) timers.Add(LifeTimer.Resting);
        if (handler is not null) timers.UnionWith(handler(before, after));
        if (hero) timers.Remove(LifeTimer.Resting);
        return timers;
    }

    // The PC moves to another map: the pending timers of its followers go with it. Atomic: a key collision (which the
    // one-map-per-NPC rule leaves no way to reach) is EventConflict, a call from the host's world port during a pass
    // InvalidEventPass, and either moves nothing.
    private GameSessionFailure MoveFollowerEvents(SessionMapId to)
    {
        if (to == _actor.Map) return GameSessionFailure.None;
        var followers = _party.Order.ToHashSet(StringComparer.Ordinal);
        var failure = _events.Relocate(_actor.Map.RuntimeMap, to.RuntimeMap, entity => followers.Contains(entity.Code)).Failure;
        // ARC-SCRIPT-010: a deferred call's player and follower roles travel one by one (the original keeps their handles
        // across maps), its other roles stay where they were. Both steps refuse only during a pass, which the first one
        // already reports, so the rewrite cannot fail after the relocation moved anything.
        if (failure == GameEventFailure.None)
            failure = _events.RelocateEntities(SessionEventKinds.ScriptCall, _actor.Map.RuntimeMap, to.RuntimeMap,
                entity => entity.Code == SessionEventKinds.PlayerEntity || followers.Contains(entity.Code)).Failure;
        return failure switch
        {
            GameEventFailure.None => GameSessionFailure.None,
            GameEventFailure.PassInProgress => GameSessionFailure.InvalidEventPass,
            _ => GameSessionFailure.EventConflict,
        };
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
            new(flags.Version.Revision, flags.Entries.ToArray()), _actor, maps, _story.Capture(), _party.Capture(), _hero?.State,
            _inventory.Capture(), _equipment.Capture(), _effects.Capture(), _experience.Capture(), _advancement.Capture(),new(_random.Seed, _random.Draws), _life.Capture(), _reactions.Capture(), _combat.Round?.Capture(), CaptureCombatMode(), CaptureEvents(), _lighting, _sound.Capture());
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
        foreach (var rumor in saved.QuelledRumors)
        {
            if (!SessionStoryLimits.IsRumor(rumor)) return GameSessionFailure.InvalidRumor;
            if (!story.QuelledRumors.Add(rumor)) return GameSessionFailure.InvalidSnapshot;
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

    private FollowConsent Consent(PartyConsentFacts facts, bool forcedFollower, bool ignoreLimit) =>
        FollowConsentRules.Evaluate(new(facts.MindControlledByPc, facts.PcPersuasion, facts.PcCharisma, facts.PcAlignment,
            facts.PcLevel, facts.OtherLeaderCharisma, forcedFollower, facts.Reaction, facts.Thresholds, facts.NpcAlignment,
            facts.NpcLevel, facts.MaxFollowers, _party.NonForcedFollowers, ignoreLimit));

    private static bool ValidFacts(PartyConsentFacts facts) => Enum.IsDefined(facts.PcPersuasion);

    // Join and unwait share the commit: leave any standing, append at the end of the array.
    private PartyMutationResult Append(SessionCompanion current, bool forced, FollowConsent? consent)
    {
        var next = current with { Standing = CompanionStanding.Following, Forced = forced, Jilted = false };
        var order = _party.Order;
        if (next == current && order.Count > 0 && order[^1] == current.Entity)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, consent, new(_identity, _revision));
        if (!_party.Records.ContainsKey(current.Entity) && _party.Records.Count >= _limits.MaxCompanions)
            return RejectParty(GameSessionFailure.BudgetExceeded, consent);
        if (_revision == long.MaxValue) return RejectParty(GameSessionFailure.RevisionExhausted, consent);
        _party.Put(next, true);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, consent, new(_identity, _revision));
    }

    private PartyMutationResult RejectParty(GameSessionFailure failure, FollowConsent? consent = null) =>
        new(GameSessionDisposition.Rejected, failure, consent, new(_identity, _revision));

    // Followers keep their saved array order; the others are waiting NPCs or free NPCs with a flag, in ordinal order.
    // Every record must be one the transitions can produce, and the total stays within the caller's limit.
    private static GameSessionFailure ImportParty(SessionPartyState saved, GameSessionLimits limits, out PartyDomain party)
    {
        party = new();
        if (saved.Followers.Count + saved.Others.Count > limits.MaxCompanions) return GameSessionFailure.BudgetExceeded;
        foreach (var record in saved.Followers)
        {
            if (!SessionValidation.Entity(record.Entity) || record.Standing != CompanionStanding.Following || record.Jilted)
                return GameSessionFailure.InvalidCompanion;
            if (party.Records.ContainsKey(record.Entity)) return GameSessionFailure.InvalidSnapshot;
            party.Put(record, true);
        }
        string? previous = null;
        foreach (var record in saved.Others)
        {
            if (!SessionValidation.Entity(record.Entity) || record.Forced || record.IsBaseline ||
                record.Standing is not (CompanionStanding.Waiting or CompanionStanding.Free) ||
                (record.Standing == CompanionStanding.Waiting && record.Jilted)) return GameSessionFailure.InvalidCompanion;
            if (party.Records.ContainsKey(record.Entity) ||
                (previous is not null && string.CompareOrdinal(previous, record.Entity) >= 0)) return GameSessionFailure.InvalidSnapshot;
            previous = record.Entity;
            party.Put(record, false);
        }
        return GameSessionFailure.None;
    }

    // Pickup, loot and IN share the item rules and the commit: every refusal precedes the insert check, which runs after
    // the other checks, and the item's insert-item script, which runs last. Both are host callbacks, so a call back into the
    // owner from either (a transfer, a placement, a new seed) makes this transfer stale instead of committing over the
    // changed state.
    private InventoryMutationResult Take(InventoryDomain.Seed seed, SessionInventoryItem item, bool parented, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts)
    {
        var facts = item.Facts;
        if (!InventoryDomain.Admitted(facts.Kind.Type)) return RejectItem(GameSessionFailure.ItemTypeUnsupported);
        if (parented && facts.Worn) return RejectItem(GameSessionFailure.WornItemUnsupported);
        var insertScript = facts.HasScript(SessionScriptPoint.InsertItem);
        if ((insertScript && scripts is null) ||
            facts.HasScript(parented ? SessionScriptPoint.RemoveItem : SessionScriptPoint.Get)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        if (_inventory.Held >= _limits.MaxPcItems) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, map, flagVersion) = (_revision, _actor.Map, _flags.Version);
        bool Stale() => _revision != revision || _actor.Map != map || _flags.Version != flagVersion || !_inventory.Seeds.TryGetValue(map, out var current) ||
            !ReferenceEquals(current, seed) || _inventory.Taken.Contains(item.Entity);
        var verdict = insertCheck(item);
        ThrowIfDisposed();
        if (Stale()) return RejectItem(GameSessionFailure.StaleVersion, verdict);
        // Any answer but Accepted refuses, an undefined one included.
        if (verdict != SessionInsertVerdict.Accepted) return RejectItem(GameSessionFailure.InsertRefused, verdict);

        ScrRunResult? run = null;
        var messages = new List<SessionScriptMessage>();
        if (insertScript)
        {
            // The original inserts first and then runs the script, whose result it ignores (ledger IF10). The admitted
            // commands read nothing the insertion changes, so the run is made before the commit and its effects are
            // applied with it, or the whole transfer is refused.
            // The run reads global flags from the session itself, whatever the host would answer (a flag the session does
            // not know refuses the transfer).
            var reads = new SessionFlagReads(this, scripts!);
            run = ScrDispatcher.RunObject(new(SessionScriptPoint.InsertItem, new ScrObjectRef(map.RuntimeMap, item.Entity),
                new ScrObjectRef(map.RuntimeMap, SessionEventKinds.PlayerEntity), null), reads, new() { Rules = _rules.Scripts, Admission = SessionItemScripts.Admission });
            ThrowIfDisposed();
            var failure = reads.UnknownFlag ? GameSessionFailure.ItemScriptUnsupported : InsertScriptEffects(run, scripts!, messages);
            ThrowIfDisposed();
            if (Stale()) return RejectItem(GameSessionFailure.StaleVersion, verdict) with { InsertScript = run };
            if (failure != GameSessionFailure.None) return RejectItem(failure, verdict) with { InsertScript = run };
        }
        _inventory.Add(new SessionPcItem(item.Entity, _actor.Map, facts.Kind));
        foreach (var write in run?.Effects.GlobalFlags ?? [])
        {
            var receipt = _flags.Write(new GlobalFlagWriteRequest(new GlobalFlagRequestId(Guid.NewGuid()), _flags.Version, new FlagId(write.Index), write.Value));
            // Checked before the commit; a refusal here would be a broken invariant of the flag owner.
            if (receipt.Disposition is not (GlobalFlagWriteDisposition.Applied or GlobalFlagWriteDisposition.NoChange))
                throw new InvalidOperationException("INSERT_SCRIPT_FLAG_WRITE_" + receipt.Disposition);
        }
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item.Entity, verdict, new(_identity, _revision))
            { InsertScript = run, Messages = messages.AsReadOnly() };
    }

    // What the session applies from an insert-item run: global flags and printed lines. A script outside the admitted set,
    // a changed attachment (its flags, counters or number would need a home on the moved item) and anything else refuse the
    // transfer; a flag outside the session's domain and a line the host cannot show do too. Host reads only.
    private GameSessionFailure InsertScriptEffects(ScrRunResult run, ISessionItemScriptHost scripts, List<SessionScriptMessage> messages)
    {
        // A script file that exists but cannot be read is not the original's missing file: refused, not skipped.
        if (run.Outcome == ScrOutcome.Refused || run.Stop == ScrStop.UnreadableScript) return GameSessionFailure.ItemScriptUnsupported;
        var effects = run.Effects;
        if (effects.GlobalFlags.Count > 0 && _flags.Version.Revision > GlobalFlagLimits.MaxRevision - effects.GlobalFlags.Count)
            return GameSessionFailure.RevisionExhausted;
        if (effects.Writeback is not null || effects.DestroyTrapObject is not null || effects.Dialog is not null || effects.StoryState is not null || effects.Calls.Count > 0)
            return GameSessionFailure.ItemScriptUnsupported;
        foreach (var write in effects.GlobalFlags)
        {
            var read = _flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), _flags.Version, new FlagId(write.Index)));
            if (read.Status != GlobalFlagReadStatus.Known) return GameSessionFailure.ItemScriptUnsupported;
        }
        foreach (var line in effects.Lines)
        {
            var printed = scripts.PrintedLine(line.Script, line.Line) ?? throw new InvalidOperationException("INSERT_SCRIPT_LINE_NULL");
            if (printed.Status == SessionPrintedLineStatus.Unsupported) return GameSessionFailure.ItemScriptUnsupported;
            if (printed.Status == SessionPrintedLineStatus.Shown) messages.Add(new(line.Script, line.Line, line.Kind, printed.Text ?? string.Empty));
        }
        return GameSessionFailure.None;
    }

    private InventoryMutationResult RejectItem(GameSessionFailure failure, SessionInsertVerdict? verdict = null) =>
        new(GameSessionDisposition.Rejected, failure, null, verdict, new(_identity, _revision));

    // The hero is rebuilt from its saved fields under the same checks as a new one.
    private static GameSessionFailure ImportHero(SessionHeroState? saved, out HeroDomain? hero)
    {
        hero = null;
        if (saved is null) return GameSessionFailure.None;
        hero = HeroDomain.Build(saved.Prototype, saved.Race, saved.Gender, saved.Stats, saved.Level, saved.UnspentPoints, saved.Alignment,
            saved.RaceEffect, saved.GenderEffect, saved.Experience, saved.Advancement);
        return hero is null ? GameSessionFailure.InvalidHero : GameSessionFailure.None;
    }

    // The PC's items keep their saved order; each must be one the session can produce: a kit item, which only a hero has,
    // named kit:1, kit:2, … in order (no transfer removes items yet), and an admitted type taken from a valid map. Gold,
    // too, only comes with a hero.
    private static GameSessionFailure ImportInventory(SessionInventoryState saved, bool hero, GameSessionLimits limits, out InventoryDomain inventory)
    {
        inventory = new();
        if (saved.Gold < 0 || (saved.Gold > 0 && !hero) || (saved.Kit.Count > 0 && !hero)) return GameSessionFailure.InvalidSnapshot;
        inventory.Gold = saved.Gold;
        if (saved.Kit.Count + saved.Pc.Count > limits.MaxPcItems) return GameSessionFailure.BudgetExceeded;
        foreach (var item in saved.Kit)
        {
            if (!Enum.IsDefined(item.Kind.Type) || !InventoryDomain.Admitted(item.Kind.Type)) return GameSessionFailure.InvalidItem;
            if (item.Entity != HeroDomain.KitEntity(inventory.Kit.Count)) return GameSessionFailure.InvalidSnapshot;
            inventory.Add(item);
        }
        foreach (var item in saved.Pc)
        {
            if (!SessionValidation.Entity(item.Entity) || !SessionValidation.Map(item.Origin) || !Enum.IsDefined(item.Kind.Type) ||
                !InventoryDomain.Admitted(item.Kind.Type)) return GameSessionFailure.InvalidItem;
            if (inventory.Taken.Contains(item.Entity)) return GameSessionFailure.InvalidSnapshot;
            inventory.Add(item);
        }
        return GameSessionFailure.None;
    }

    // Every life call: target, input and version checks, then the step on a trial copy of the generator, then NoChange (the
    // trial's draws are adopted, as the source spends them), then the record budget and revision capacity. A refusal never
    // adopts the trial copy, so it draws nothing.
    private SessionLifeResult Life(GameSessionVersion expected, SessionLifeTarget target, bool validInput,
        Func<LifeState, LifeFacts, ICombatRollSource, LifeOutcome> step,
        Func<LifeState, LifeState, IEnumerable<LifeTimer>>? handlerTimers = null, int eventMap = 0, int recoveryCopies = 1)
    {
        ArgumentNullException.ThrowIfNull(target);
        var failure = Resolve(target, out var record, out var facts, out var recorded);
        if (failure != GameSessionFailure.None) return RejectLife(failure);
        if (!validInput) return RejectLife(GameSessionFailure.InvalidLifeAmount);
        if (!IsCurrent(expected)) return RejectLife(GameSessionFailure.StaleVersion);

        var rolls = new TrialCombatRolls(new SeededRandom(_random.Seed, _random.Draws));
        LifeOutcome outcome;
        try { outcome = step(record.State, facts!, rolls); }
        catch (TrialCombatRolls.ExhaustedException) { return RejectLife(GameSessionFailure.RandomExhausted); }
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var next = new SessionLifeRecord(outcome.After, outcome.Events.HasFlag(LifeEvents.Died) ? now : record.DeathTimeMs);
        // An NPC's timers belong to the map it is on: the timer's own map, else the actor's (the map where the step happened).
        var map = target.IsHero ? 0 : eventMap > 0 ? eventMap : _actor.Map.RuntimeMap;
        var timers = Timers(target.IsHero, record.State, next.State, handlerTimers)
            .Select(timer => TimerEvent(timer, target, map, now, recoveryCopies)).Where(pending => !IsPending(pending.Kind, target.Entity)).ToArray();
        // One map per NPC's timers: a step on another map than its pending timers' (a teleport the session did not see)
        // takes them along, as the NPC is evidently here now.
        var stray = target.IsHero ? [] : StrayMaps(target.Entity!, map);
        // A move needs the queue outside a pass: a life call from the host's world port during one is refused.
        if (stray.Length > 0 && _passWorld is not null) return RejectLife(GameSessionFailure.InvalidEventPass);
        if (timers.Length > 0 && (_events.Count > _events.Limits.MaxPending - timers.Length ||
            _events.NextSequence > long.MaxValue - timers.Length || now > long.MaxValue - SessionEventKinds.RestingDelayMs))
            return RejectLife(GameSessionFailure.EventBudgetExceeded);
        var changed = next != record;
        if (changed && !target.IsHero && !recorded && _life.Critters.Count >= _limits.MaxLifeRecords) return RejectLife(GameSessionFailure.BudgetExceeded);
        if (changed && _revision == long.MaxValue) return RejectLife(GameSessionFailure.RevisionExhausted);

        // All refusals precede the commit; the timers fit, so scheduling cannot fail.
        _random = rolls.Trial;
        foreach (var from in stray)
            if (_events.Relocate(from, map, entity => entity.Code == target.Entity).Failure != GameEventFailure.None)
                throw new InvalidOperationException("Timer relocation failed after admission.");
        foreach (var (kind, payload) in timers)
        {
            var scheduled = _events.ScheduleAfter(kind, payload, SessionEventKinds.Delay(TimerOf(kind)), new(0, now, 0));
            if (scheduled.Disposition != GameEventScheduleDisposition.Added) throw new InvalidOperationException("Timer scheduling failed after admission.");
        }
        if (!changed) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, outcome, new(_identity, _revision));
        if (target.IsHero) _life.Hero = next;
        else _life.Critters[target.Entity!] = next;
        EndSubturnOnIncapacitation(target, outcome);
        if (outcome.Events.HasFlag(LifeEvents.Died)) LeaveCombatModeOnDeath(target);
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, outcome, new(_identity, _revision));
    }

    private static (string Kind, GameEventPayload Payload) TimerEvent(LifeTimer timer, SessionLifeTarget target, int map, long now,
        int recoveryCopies)
    {
        long[] values = timer == LifeTimer.PoisonRecovery ? [now, recoveryCopies] : [now];
        return target.IsHero
            ? (SessionEventKinds.Kind(timer, true), new([], [.. values]))
            : (SessionEventKinds.Kind(timer, false), new([new GameEventEntity(map, target.Entity!)], [.. values]));
    }

    private static readonly string[] NpcKinds = [SessionEventKinds.NpcFatigue, SessionEventKinds.NpcPoisonDamage,
        SessionEventKinds.NpcPoisonRecovery, SessionEventKinds.NpcResting];

    // The maps other than this one where the NPC has pending timers (none while the one-map rule holds).
    private int[] StrayMaps(string entity, int map)
    {
        var maps = new SortedSet<int>();
        foreach (var kind in NpcKinds)
            _events.Any(kind, e =>
            {
                var at = e.Payload.Entities[0];
                if (at.Code == entity && at.Map != map) maps.Add(at.Map);
                return false;
            });
        return maps.ToArray();
    }

    // One per critter and kind, whatever map the pending one is on (an NPC keeps all its timers on one map).
    private bool IsPending(string kind, string? entity) =>
        _events.Any(kind, e => entity is null || e.Payload.Entities[0].Code == entity);

    private static LifeTimer TimerOf(string kind) => kind switch
    {
        SessionEventKinds.HeroFatigue or SessionEventKinds.NpcFatigue => LifeTimer.Fatigue,
        SessionEventKinds.HeroPoisonDamage or SessionEventKinds.NpcPoisonDamage => LifeTimer.PoisonDamage,
        SessionEventKinds.HeroPoisonRecovery or SessionEventKinds.NpcPoisonRecovery => LifeTimer.PoisonRecovery,
        SessionEventKinds.NpcResting => LifeTimer.Resting,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // The record and facts of a target: the hero's own, or an NPC's record (else its loaded state) under the host's facts.
    private GameSessionFailure Resolve(SessionLifeTarget target, out SessionLifeRecord record, out LifeFacts? facts, out bool recorded)
    {
        record = default;
        facts = null;
        recorded = false;
        if (target.IsHero)
        {
            if (_hero is null) return GameSessionFailure.InvalidLifeTarget;
            record = _life.Hero!.Value;
            facts = HeroLifeFacts(record.State);
            recorded = true;
            return GameSessionFailure.None;
        }
        if (!SessionValidation.Entity(target.Entity)) return GameSessionFailure.InvalidLifeTarget;
        if (!LifeRules.IsValid(target.Facts)) return GameSessionFailure.InvalidLifeFacts;
        if (!LifeRules.IsValid(target.Loaded)) return GameSessionFailure.InvalidLifeState;
        recorded = _life.Critters.TryGetValue(target.Entity!, out record);
        if (!recorded) record = new(target.Loaded, null);
        facts = target.Facts;
        return GameSessionFailure.None;
    }

    private SessionLifeResult RejectLife(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, null, new(_identity, _revision));

    private SessionEventState CaptureEvents()
    {
        var saved = _events.CaptureSnapshot();
        return new(saved.RegistryVersion, saved.NextSequence, saved.Events
            .Select(e => new SessionPendingEvent(e.Sequence, e.Kind, e.DeadlineMs, e.Payload.Entities, e.Payload.Values)).ToArray());
    }

    // Saved timers: the queue's strict restore (registry version, kinds, sequences, order, payloads, keys), plus what only
    // the session knows: a hero timer needs the hero, an NPC timer a life record of its NPC, and every timer was set at a
    // game time no later than the saved clock, exactly its kind's delay before its deadline.
    private static GameSessionFailure ImportEvents(SessionEventState saved, SessionHeroState? heroState, LifeDomain life, long clockMs,
        SessionEventSink sink, GameEventQueueProfile profile, out GameEventQueue? events)
    {
        events = null;
        var hero = heroState is not null;
        var registry = SessionEventKinds.Build(sink);
        var converted = ImmutableArray.CreateBuilder<GameEvent>(saved.Pending.Count);
        var maps = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pending in saved.Pending)
        {
            if (pending is null || !registry.TryGet(pending.Kind, out var kind) || pending.Entities.IsDefault || pending.Values.IsDefault ||
                pending.Values.Length != kind.ValueCount) return GameSessionFailure.InvalidEvents;
            if (pending.Kind == SessionEventKinds.ScriptCall)
            {
                // A deferred call: set at a game time no later than the saved clock, exactly its delay before its
                // deadline, two roles (each a valid entity or the absent marker). Its objects are checked when it fires.
                if (pending.Entities.Length != 2 || pending.Values[0] < 0 || pending.Values[0] > clockMs ||
                    pending.Values[1] is < 1 or > SessionEventKinds.MaxScriptCallDelayMs || pending.DeadlineMs != pending.Values[0] + pending.Values[1] ||
                    !pending.Entities.All(e => e.Map >= 1 && SessionValidation.Entity(e.Code))) return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, 0, new(pending.Entities, pending.Values)));
                continue;
            }
            if (pending.Kind == SessionEventKinds.HeroEffectEnd)
            {
                // A timed effect's END (ARC-WORLD-029): the hero's, set no later than the saved clock, exactly its duration
                // before its deadline, of a number no innate entry has.
                if (heroState is null || !pending.Entities.IsEmpty || pending.Values[0] < 0 || pending.Values[0] > clockMs ||
                    pending.Values[1] is < 1 or > SessionEffectLimits.MaxDurationMs || pending.DeadlineMs != pending.Values[0] + pending.Values[1] ||
                    EffectDomain.IsInnate(heroState, (int)pending.Values[2])) return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, 0, new(pending.Entities, pending.Values)));
                continue;
            }
            if (pending.Kind == SessionEventKinds.LockReset)
            {
                // A Lock event (ARC-WORLD-050): one object, set no later than the saved clock, an hour (unlock) or a day (jam)
                // before its deadline. Its object's state is checked when it fires.
                if (pending.Entities.Length != 1 || pending.Entities[0].Map < 1 || !SessionValidation.Entity(pending.Entities[0].Code) ||
                    pending.Values[0] < 0 || pending.Values[0] > clockMs ||
                    (pending.DeadlineMs - pending.Values[0]) is not (Arcanum.Simulation.Locks.LockRules.UnlockResetDelayMs or Arcanum.Simulation.Locks.LockRules.JamResetDelayMs))
                    return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, pending.Entities[0].Map, new(pending.Entities, pending.Values)));
                continue;
            }
            var scheduledAt = pending.Values[0];
            if (scheduledAt < 0 || scheduledAt > clockMs || scheduledAt > long.MaxValue - SessionEventKinds.Delay(TimerOf(kind.Name)) ||
                pending.DeadlineMs != scheduledAt + SessionEventKinds.Delay(TimerOf(kind.Name))) return GameSessionFailure.InvalidEvents;
            if (pending.Entities.IsEmpty ? !hero : !pending.Entities.All(entity => entity.Code is not null && life.Critters.ContainsKey(entity.Code)))
                return GameSessionFailure.InvalidEvents;
            // One map per NPC's timers.
            foreach (var entity in pending.Entities)
                if (!maps.TryAdd(entity.Code, entity.Map) && maps[entity.Code] != entity.Map) return GameSessionFailure.InvalidEvents;
            var scope = kind.Scope == GameEventScopeRule.Global || pending.Entities.IsEmpty ? 0 : pending.Entities[0].Map;
            converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, scope, new(pending.Entities, pending.Values)));
        }
        // No session kind is SavedUnlessRebuilt yet, so Rebuild is always empty; the AI tick (slice I6) reschedules it here.
        if (converted.Count(e => e.Kind == SessionEventKinds.ScriptCall) > SessionEventKinds.MaxScriptCalls ||
            converted.Count(e => e.Kind == SessionEventKinds.HeroEffectEnd) > SessionEffectLimits.MaxTimedEffects ||
            converted.Count(e => e.Kind == SessionEventKinds.LockReset) > SessionEventKinds.MaxLockResets) return GameSessionFailure.EventBudgetExceeded;
        var result = GameEventQueue.Restore(registry, new(saved.Registry, saved.NextSequence, converted.ToImmutable()), profile,
            SessionEventKinds.Limits);
        if (result.Queue is null)
            return result.Failure == GameEventFailure.QueueFull ? GameSessionFailure.EventBudgetExceeded : GameSessionFailure.InvalidEvents;
        events = result.Queue;
        return GameSessionFailure.None;
    }

    // The hero's record exists exactly when the hero does; NPC records are valid states with a death time no later than the
    // saved clock, unique, in ordinal order and within the caller's budget.
    private static GameSessionFailure ImportLife(SessionLifeState saved, bool hero, long clockMs, GameSessionLimits limits, out LifeDomain life)
    {
        life = new();
        if (saved.Hero.HasValue != hero) return GameSessionFailure.InvalidSnapshot;
        if (saved.Hero is { } heroRecord)
        {
            if (!LifeDomain.ValidRecord(heroRecord, clockMs)) return GameSessionFailure.InvalidLifeState;
            life.Hero = heroRecord;
        }
        if (saved.Critters.Count > limits.MaxLifeRecords) return GameSessionFailure.BudgetExceeded;
        string? previous = null;
        foreach (var critter in saved.Critters)
        {
            if (!SessionValidation.Entity(critter.Entity)) return GameSessionFailure.InvalidLifeTarget;
            if (!LifeDomain.ValidRecord(critter.Record, clockMs)) return GameSessionFailure.InvalidLifeState;
            if (previous is not null && string.CompareOrdinal(previous, critter.Entity) >= 0) return GameSessionFailure.InvalidSnapshot;
            previous = critter.Entity;
            life.Critters.Add(critter.Entity, critter.Record);
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

/// <summary>ARC-SCRIPT-013: the item script host with global flags read from the session; an unknown flag answers false and
/// is recorded, and the session then refuses the transfer.</summary>
internal sealed class SessionFlagReads(GameSessionOwner session, ISessionItemScriptHost host) : IScrHost
{
    internal bool UnknownFlag { get; private set; }

    public bool GlobalFlag(int index)
    {
        var flags = session.Flags;
        var read = flags.Read(new GlobalFlagReadRequest(new GlobalFlagRequestId(Guid.NewGuid()), flags.Version, new FlagId(index)));
        if (read.Status == GlobalFlagReadStatus.Known && read.Value is { } value) return value;
        UnknownFlag = true;
        return false;
    }

    public ScrSlot? Slot(ScrObjectRef attachee, int point) => host.Slot(attachee, point);
    public ScrScriptLookup Script(int number) => host.Script(number);
    public int StoryState() => host.StoryState();
    public bool IsMonsterGenerator(ScrObjectRef attachee) => host.IsMonsterGenerator(attachee);
    public ScrDialogState DialogState(ScrObjectRef attachee) => host.DialogState(attachee);
    public ScrTrapCheck CheckTrapBeforeUse(ScrObjectRef triggerer, ScrObjectRef attachee) =>
        host.CheckTrapBeforeUse(triggerer, attachee);
    public bool IsTrapObject(ScrObjectRef attachee) => host.IsTrapObject(attachee);
    public ScrObjectRef? Player() => host.Player();
    public ScrObjectRef? Leader(ScrObjectRef critter) => host.Leader(critter);
    public bool IsJilted(ScrObjectRef critter) => host.IsJilted(critter);
    public bool IsPc(ScrObjectRef critter) => host.IsPc(critter);
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
    internal HashSet<int> QuelledRumors { get; } = [];

    internal SessionStoryState Capture() => new(
        Quests.Values.OrderBy(quest => quest.Quest).ToArray(),
        GlobalQuests.OrderBy(pair => pair.Key).Select(pair => new SessionGlobalQuest(pair.Key, pair.Value)).ToArray(),
        Rumors.OrderBy(pair => pair.Key).Select(pair => new SessionRumor(pair.Key, pair.Value)).ToArray(),
        QuelledRumors.Order().ToArray());
}
