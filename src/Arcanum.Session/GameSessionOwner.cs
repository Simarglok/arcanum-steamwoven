using System.Collections.Immutable;
using Arcanum.Scripting.Dialog;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Scripting.Scr;
using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Randomness;
using Arcanum.Simulation.Reputation;

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
    private readonly SessionMapChanges _maps;
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
    private DangerDomain _danger = new();
    private readonly GameEventQueue _events;
    // Set only while a pass runs: the host's answers for the timers it fires and the life steps they made.
    private ISessionEventWorld? _passWorld;
    // ARC-WORLD-096: the pass world as T, or the host world a spell host's pass world wraps as T.
    private T? PassWorldAs<T>() where T : class => _passWorld as T ?? (_passWorld as ISessionWrappingEventWorld)?.Wrapped as T;
    private List<SessionLifeFiring>? _passFirings;
    private List<SessionScriptCallFiring>? _passCalls;
    private List<SessionEffectFiring>? _passEffects;
    private List<SessionLockFiring>? _passLocks;
    private List<SessionWaitFiring>? _passWaits;
    private List<SessionRespawnFiring>? _passRespawns;
    private List<SessionDecayFiring>? _passDecays;
    private List<SessionAiTickFiring>? _passAiTicks;
    private List<SessionRestockFiring>? _passRestocks;
    // Replaced, not mutated, when a life step that drew on a trial copy commits.
    private SeededRandom _random;
    private readonly SessionCombatRolls _combatRolls;
    private readonly GameSessionOwnerIdentity _identity = new();
    private ActorPlacement _actor;
    private SessionLightingState? _lighting;
    private GameRules _rules = GameRules.Steamwoven;
    private GameDifficulty _difficulty = GameDifficulty.Normal;
    private long _revision;
    private bool _disposed;

    private GameSessionOwner(GameClockOwner clock, GlobalFlagOwner flags, GameSessionLimits limits,
        ActorPlacement actor, long revision, SessionMapChanges maps, Story story,
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
                definition.Advancement, definition.InnateSkills, background: definition.Background,
                backgroundDescription: definition.BackgroundDescription, playerName: definition.PlayerName, portrait: definition.Portrait);
            var kit = definition.Kit?.ToArray();
            if (hero is null || definition.Gold < 0 || kit is null || kit.Length > limits.MaxPcItems)
                return Rejected(GameSessionFailure.InvalidHero);
            // A new PC's level set runs its scheme over the starting points (ARC-WORLD-036).
            if (CreationScheme(hero, descriptor.AutoLevel) is { } outcome)
                hero.Apply(AdvancementDomain.WithSheet(hero.State, outcome.Sheet, hero.State.Experience));
            for (var index = 0; index < kit.Length; index++)
            {
                if (kit[index] is not { } item || !Enum.IsDefined(item.Kind.Type) || !InventoryDomain.KitAdmitted(item.Kind.Type))
                    return Rejected(GameSessionFailure.InvalidHero);
                if (!item.Weight.IsValid) return Rejected(GameSessionFailure.InvalidHero);
                inventory.Add(new SessionKitItem(HeroDomain.KitEntity(index), item.Kind));
                inventory.SetWeight(HeroDomain.KitEntity(index), item.Weight);
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
        var owner = new GameSessionOwner(clock.Owner, flags.Owner, limits, descriptor.Actor, 0, new(), new(), new(), hero, inventory,
            new(), new(), new(descriptor.Experience), new(descriptor.AutoLevel), new(descriptor.RandomSeed), life, new(), new(), null, sink,
            new GameEventQueue(SessionEventKinds.Build(sink), SessionEventKinds.Limits))
        { _rules = GameRules.For(descriptor.Rules), _difficulty = descriptor.Difficulty, _reputationCatalog = descriptor.Reputations ?? ReputationCatalog.Empty };
        // ARC-UI-019: the kit takes its grid slots in kit order.
        owner.SettleHeroGrid();
        owner.MarkEncumbranceOfNewHero();
        return new(owner, GameSessionFailure.None, ClockFailure.None, GlobalFlagReason.None);
    }

    /// <summary>
    /// Builds a new owner from a snapshot after checking world, module, clock profile and budgets.
    /// Never touches an existing owner, so a refused restore keeps the previous session usable.
    /// The new owner has a fresh identity: versions and clock tokens of any earlier owner are stale.
    /// </summary>
    public static GameSessionCreationResult Restore(GameSessionSnapshot? snapshot, GameSessionRestoreOptions? options)
    {
        if (options is null || !Enum.IsDefined(options.Rules) || !Enum.IsDefined(options.Difficulty) || options.LoadRandom is { Draws: < 0 })
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
        var failure = ImportMaps(snapshot.Maps, snapshot.Objects.State, limits, out var maps);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportStory(snapshot.Story, snapshot.Clock.GameTimestampMs, out var story);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportParty(snapshot.Party, limits, out var party);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportHero(snapshot.Hero, out var hero);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportDurability(snapshot.Durability, limits, out var durability);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportInventory(snapshot.Inventory, hero is not null, limits, durability.Destroyed, out var inventory);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = RestoreJunkPiles(inventory, snapshot.Objects.JunkPiles, durability.Destroyed);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportEquipment(snapshot.Equipment, inventory, hero is not null, out var equipment);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportWornConditions(equipment, durability);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportHotkeys(snapshot.Hotkeys, hero is not null, inventory, equipment, out var hotkeys, out var recentActions);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportHeroGrid(snapshot.Inventory.Grid, inventory, equipment, hotkeys);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportSchematics(snapshot.Schematics, hero is not null, limits, out var found);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportLife(snapshot.Life, hero is not null, snapshot.Clock.GameTimestampMs, limits, out var life);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportBloodPools(snapshot.Objects.BloodPools, hero is not null, snapshot.Clock.GameTimestampMs, inventory);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportObjectMarks(snapshot.Objects, inventory);
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
        failure = ImportConcealment(snapshot.Concealment, hero is not null, party, combat);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportDanger(snapshot.Danger, limits, out var danger);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportSound(snapshot.Sound, GameRules.For(options.Rules).Sound, options.SoundMapDefault, out var sound);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        var sink = new SessionEventSink();
        failure = ImportEvents(snapshot.Events, hero?.State, life, party, maps, snapshot.Clock.GameTimestampMs, snapshot.Actor.Map.RuntimeMap, sink,
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
        // GI6 (ARC-DEV-077): the Original setting goes on with the host's running generator, Steamwoven with the saved one.
        var random = GameRules.For(options.Rules).Lifecycle.LoadKeepsRunningRandom ? options.LoadRandom ?? new(0, 0) : snapshot.Random;
        var owner = new GameSessionOwner(clock.Owner, flags.Owner, limits, snapshot.Actor, snapshot.Revision, maps, story, party,
            hero, inventory, equipment, effects, experience, advancement, new(random.Seed, random.Draws), life, reactions, combat,
            snapshot.Lighting, sink, events!)
        { _rules = GameRules.For(options.Rules), _difficulty = options.Difficulty, _sound = sound, _danger = danger, _durability = durability, _foundSchematics = found,
          _reputationCatalog = options.Reputations ?? ReputationCatalog.Empty, _hotkeys = hotkeys, _recentActions = recentActions };
        owner.MarkEncumbranceOfRestore();
        owner.MarkHotkeyPlaces();
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
        return _maps.Get(map, key);
    }

    /// <summary>Detached copy of one map's changes in canonical key order; empty for an untouched map.</summary>
    public IReadOnlyList<MapChange> QueryMapChanges(SessionMapId map)
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_maps.ForMap(map));
    }

    /// <summary>
    /// ARC-DEV-072 (MIG1): every map that holds a change of the entity's property (the entity-scoped key), with its value, in map
    /// order; empty when no map does. A read only: it answers "on which map was this entity's record changed" by identity, for
    /// example the home map that names where a travelling NPC stands now.
    /// </summary>
    public IReadOnlyList<(SessionMapId Map, long Value)> QueryEntityChanges(string entity, string property)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(property);
        var key = MapChangeKey.ForEntity(entity, property);
        if (ObjectStateVocabulary.Routed(key)) return _maps.RecordsOf(entity, property);
        return Array.AsReadOnly(_maps.Maps().Select(map => (Map: map, Value: _maps.Get(map, key))).Where(pair => pair.Value is not null)
            .Select(pair => (pair.Map, pair.Value!.Value)).ToArray());
    }

    /// <summary>
    /// ARC-DEV-092 (MIG7a, TM2, GR5): the recorded fields of one mobile map-data object, by property, wherever the object stands: the state of
    /// its directory entry (the property names of <see cref="ObjectStateVocabulary"/>; the map of a field is the map its record names). A read only;
    /// empty for an object with no recorded change and for an entity that is not a mobile map object.
    /// </summary>
    public IReadOnlyList<SessionObjectField> QueryObjectState(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        return Array.AsReadOnly(_maps.FieldsOf(entity));
    }

    public GameSessionMutationResult SetMapChange(GameSessionVersion expected, SessionMapId map, MapChangeKey key, long value)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Key(key)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var current = _maps.Get(map, key);
        if (current == value) return NoChange();
        if (current is null)
        {
            // A new key needs room in the map and in the session; a routed record that moves here from another map needs room in this map only.
            // (A record that is the only change of the map it leaves frees that map, so it does not take a new map's place.)
            var freesItsMap = _maps.RecordMap(key) is { } from && _maps.CountOf(from) == 1;
            if ((_maps.CountOf(map) == 0 && _maps.MapCount >= _limits.MaxMaps && !freesItsMap) || _maps.CountOf(map) >= _limits.MaxChangesPerMap ||
                (_maps.RecordMap(key) is null && _maps.Total >= _limits.MaxTotalChanges))
                return Reject(GameSessionFailure.BudgetExceeded);
        }
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);

        // All refusals precede the commit; only the stores update.
        _maps.Set(map, key, value);
        return Changed();
    }

    /// <summary>Returns one key to its loaded baseline. A map left without changes is dropped.</summary>
    public GameSessionMutationResult ClearMapChange(GameSessionVersion expected, SessionMapId map, MapChangeKey key)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (!SessionValidation.Key(key)) return Reject(GameSessionFailure.InvalidChangeKey);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_maps.Get(map, key) is null) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _maps.Remove(map, key);
        return Changed();
    }

    // The changes that differ from the recorded values. A routed key (MIG7a) has one record, so its edits are judged in order against the record
    // the earlier edits of the step leave (a write to another map moves it, a removal on a map it is not on is nothing), the way the apply loop and
    // ChangesFit walk them; any other key is judged against the state before the step (each is listed once).
    private MapChangeEdit[] EffectiveChanges(IReadOnlyList<MapChangeEdit> changes)
    {
        var records = new Dictionary<MapChangeKey, (SessionMapId? Map, long? Value)>();
        var effective = new List<MapChangeEdit>();
        foreach (var change in changes)
        {
            if (!ObjectStateVocabulary.Routed(change.Key))
            {
                if (_maps.Get(change.Map, change.Key) != change.Value) effective.Add(change);
                continue;
            }
            if (!records.TryGetValue(change.Key, out var record))
            {
                var held = _maps.RecordMap(change.Key);
                record = (held, held is { } map ? _maps.Get(map, change.Key) : null);
            }
            if (change.Value is null)
            {
                if (record.Map != change.Map) continue;
                record = (null, null);
            }
            else
            {
                if (record.Map == change.Map && record.Value == change.Value) continue;
                record = (change.Map, change.Value);
            }
            records[change.Key] = record;
            effective.Add(change);
        }
        return effective.ToArray();
    }

    // Whether the map-change budgets hold on the state after the whole step.
    private bool ChangesFit(IReadOnlyList<MapChangeEdit> effective)
    {
        var counts = _maps.Counts();
        var total = _maps.Total;
        // A routed key has one record on one map (MIG7a): a write to another map moves it, a removal on a map it is not on is nothing. The
        // edits are walked in order against that record.
        var records = new Dictionary<MapChangeKey, SessionMapId?>();
        foreach (var change in effective)
        {
            var routed = ObjectStateVocabulary.Routed(change.Key);
            SessionMapId? held = routed ? (records.TryGetValue(change.Key, out var seen) ? seen : _maps.RecordMap(change.Key)) : _maps.Get(change.Map, change.Key) is null ? null : change.Map;
            if (change.Value is null)
            {
                if (held != change.Map) continue;
                counts[change.Map] = counts.GetValueOrDefault(change.Map) - 1; total--;
                if (routed) records[change.Key] = null;
            }
            else
            {
                if (held is null) { counts[change.Map] = counts.GetValueOrDefault(change.Map) + 1; total++; }
                else if (held != change.Map)
                {
                    counts[held.Value] = counts.GetValueOrDefault(held.Value) - 1;
                    counts[change.Map] = counts.GetValueOrDefault(change.Map) + 1;
                }
                if (routed) records[change.Key] = change.Map;
            }
        }
        return counts.Count(pair => pair.Value > 0) <= _limits.MaxMaps && counts.Values.All(count => count <= _limits.MaxChangesPerMap) && total <= _limits.MaxTotalChanges;
    }

    /// <summary>
    /// ARC-WORLD-017: one guarded atomic step over the actor and several map changes, such as a map transfer that
    /// carries the PC's followers. Every input is validated (the actor when given, each map and key, no key listed
    /// twice), then the version, then the whole result: NoChange when nothing would differ, the budget against the state
    /// after every edit (an update of an existing key never needs room), revision capacity. A refusal applies nothing;
    /// otherwise every edit applies and the revision moves by exactly one.
    /// <para>
    /// ARC-DEV-073 (MIG10): with <paramref name="moveList"/> (from <see cref="QueryTransferMoveList"/>) the step is the party side of
    /// the PC's change of place, in the same revision: the followers it lists to wait are ordered to wait on the map they leave
    /// (their wait timers park there), FM6's transient links (focus, who hit me last, enemy list, fleeing-from) are cleared for every
    /// follower that travels, and on another map the travellers' pending events, AI ticks included, move with the PC instead of
    /// the ticks being dropped (research FM1, FM2, FM6, RL22, RL23, RL28). A list that does not match the party is
    /// <see cref="GameSessionFailure.InvalidCompanion"/>, a wait timer that does not fit
    /// <see cref="GameSessionFailure.EventBudgetExceeded"/>; either applies nothing.
    /// </para>
    /// </summary>
    public GameSessionMutationResult ApplyChanges(GameSessionVersion expected, ActorPlacement? actor, IReadOnlyList<MapChangeEdit> changes,
        SessionMoveList? moveList = null)
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
        var effective = EffectiveChanges(changes);
        TransferPlan? transfer = null;
        if (moveList is not null && PlanTransfer(moveList, out transfer) is var planned && planned != GameSessionFailure.None) return Reject(planned);
        if (!actorChanged && effective.Length == 0 && transfer?.Changes != true) return NoChange();
        if (!ChangesFit(effective)) return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        // The last refusal: an atomic move that changes nothing when it fails.
        if (actorChanged && MoveFollowerEvents(actor!.Value.Map, transfer?.Travelling) is var moved && moved != GameSessionFailure.None) return Reject(moved);

        // All refusals precede the commit.
        if (transfer is not null)
        {
            CommitTransfer(transfer);
            // FM6 may leave a traveller's record holding only check bits: the map change's reset covers it too.
            if (actorChanged && actor!.Value.Map != _actor.Map) DropCheckOnlyDanger();
        }
        if (actorChanged) _actor = actor!.Value;
        foreach (var change in effective)
        {
            if (change.Value is { } value) _maps.Set(change.Map, change.Key, value);
            else _maps.Remove(change.Map, change.Key);
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

    /// <summary>ARC-SCRIPT-019: the game's story-state number (get story state, 0x445090; ledger SS1), 0 at a new game.</summary>
    [OriginalFunction(0x445090, OriginalFunctionScope.Full)]
    public int QueryStoryState() { ThrowIfDisposed(); return _story.StoryState; }

    /// <summary>
    /// ARC-SCRIPT-019: set story state (0x4450A0; ledger SS1): the number only ever rises, so a value at or below the current one
    /// is NoChange; single player (the network message is not sent).
    /// </summary>
    [OriginalFunction(0x4450A0, OriginalFunctionScope.Partial)]
    public GameSessionMutationResult RaiseStoryState(GameSessionVersion expected, int value)
    {
        ThrowIfDisposed();
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (value <= _story.StoryState) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _story.StoryState = value;
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
    /// index. <see cref="DialogPcFacts.IsDumb"/> is false: the source sets it only for backgrounds 55, 74 and 75 (the dumb test
    /// 0x45FC00), which the session does not apply yet; the hero's background id is held (ARC-WORLD-069), and a pre-made hero's is
    /// 39..50.
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
    /// With <paramref name="timer"/> (ARC-WORLD-045, OL23) the original's wait timer is scheduled on the actor's map when the
    /// leader's charisma is not extraordinary, the NPC is not mind controlled and its wait index (one step higher with any
    /// leader Persuasion training) is below 6; when it comes due the NPC stops waiting, jilted (<see cref="SessionEventKinds.PartyWait"/>).
    /// No room for the timer is <see cref="GameSessionFailure.EventBudgetExceeded"/> and changes nothing; an undefined training
    /// is <see cref="GameSessionFailure.InvalidConsentFacts"/>. Without <paramref name="timer"/> no timer is scheduled.
    /// </summary>
    public GameSessionMutationResult WaitParty(GameSessionVersion expected, string entity, PartyWaitFacts? timer = null)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) return Reject(GameSessionFailure.InvalidCompanion);
        if (timer is not null && !Enum.IsDefined(timer.LeaderPersuasion)) return Reject(GameSessionFailure.InvalidConsentFacts);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        if (current.Standing == CompanionStanding.Free) return Reject(GameSessionFailure.NotInParty);
        if (current.Standing == CompanionStanding.Waiting) return NoChange();
        var delay = WaitDelay(timer, out var charisma);
        if (delay is { } d && !WaitTimerFits(d, entity)) return Reject(GameSessionFailure.EventBudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        _party.Put(current with { Standing = CompanionStanding.Waiting, Forced = false }, false);
        if (delay is { } wait) ScheduleWaitTimer(entity, wait, charisma);
        return Changed();
    }

    /// <summary>
    /// Leave (C6): a follower or waiting NPC becomes free, forced and waiting cleared, spread out kept; jilted when
    /// <see cref="PartyLeave.Jilt"/>. An unforced leave is <see cref="GameSessionFailure.LeaveRefused"/> under an
    /// extraordinary-charisma leader or mind control. A free NPC is NoChange. A waiting NPC's wait timer is removed (OL25).
    /// </summary>
    public GameSessionMutationResult LeaveParty(GameSessionVersion expected, string entity, PartyLeave leave)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(leave);
        if (!SessionValidation.Entity(entity)) return Reject(GameSessionFailure.InvalidCompanion);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        if (current.Standing == CompanionStanding.Free) return NoChange();
        // ARC-WORLD-067: with a hero the leader's charisma is the hero's effective charisma.
        if (!leave.Force && !FollowConsentRules.MayLeaveUnforced(HeroPartySide()?.Charisma ?? leave.LeaderCharisma, leave.MindControlled))
            return Reject(GameSessionFailure.LeaveRefused);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        // The NPC already has a record, so a flagged free record needs no new budget.
        _party.Put(current with { Standing = CompanionStanding.Free, Forced = false, Jilted = leave.Jilt }, false);
        // ARC-SKILLS-008 (ST2): a follower that leaves is no longer concealed.
        _combat.Concealed.Remove(entity);
        ClearNoAttackOnLeave(entity);
        if (current.Standing == CompanionStanding.Waiting) CancelWaitTimers(entity);
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
    /// ARC-COMBAT-024: a window seed (<see cref="SessionMapInventorySeed.Window"/>) lists only its window's objects, so an item the PC took
    /// from the map (or a junk pile item of the map) it does not list is no mismatch: the row of the map's previous seed is kept for it (the
    /// facts a taken item's rules read, as complexity, decay and scripts), or, with none (a restored game whose window holds none), the item
    /// has no row here. An item it lists is checked as in any seed.
    /// </summary>
    public GameSessionMutationResult AttachMapInventory(SessionMapId map, SessionMapInventorySeed seed)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map)) return Reject(GameSessionFailure.InvalidMap);
        if (InventoryDomain.Validate(seed) is not { } admitted) return Reject(GameSessionFailure.InvalidInventorySeed);
        // ARC-WORLD-071: a taken item the hero gave an NPC keeps its origin map, with the kind its NPC entry holds.
        foreach (var item in _inventory.Pc.Select(pc => (pc.Entity, pc.Origin, pc.Kind))
                     .Concat(_inventory.Placed.Where(p => p.Origin is not null).Select(p => (p.Entity, Origin: p.Origin!.Value, p.Kind)))
                     .Concat(_inventory.Hero.GivenAll.Where(g => g.Origin is not null)
                         .Select(g => (g.Entity, Origin: g.Origin!.Value, _inventory.AddedTo(_inventory.NpcOf(g.Entity)!).First(e => e.Entity == g.Entity).Kind))))
        {
            var listed = admitted.Items.TryGetValue(item.Entity, out var row);
            if (item.Origin == map ? (listed ? row!.Facts.Kind != item.Kind : !admitted.Source.Window) : listed) return Reject(GameSessionFailure.InventoryMismatch);
        }
        // Kit and made items came from no map, so no seed may list them; the made: prefix is the session's own.
        if (_inventory.Kit.Any(item => admitted.Items.ContainsKey(item.Entity)) ||
            _inventory.Placed.Any(item => item.Origin is null && admitted.Items.ContainsKey(item.Entity)) ||
            _inventory.Hero.GivenAll.Any(item => item.Origin is null && admitted.Items.ContainsKey(item.Entity)) ||
            // ARC-WORLD-071: a tracked pile the seed lists is a gold pile.
            _inventory.Piles.Keys.Any(entity => admitted.Items.TryGetValue(entity, out var listed) && listed.Facts.Kind.Type != SessionItemType.Gold) ||
            admitted.Items.Keys.Any(entity => entity.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal)))
            return Reject(GameSessionFailure.InventoryMismatch);
        // ARC-WORLD-055: a holder that holds added items is an NPC, or (ARC-WORLD-090) a container a barter window filled, of the same
        // kind as the seed's holder, and its seed items and added items fit one list.
        if (admitted.Holders.Values.Any(holder => _inventory.Lists.HasList(holder.Entity) &&
                (holder.Kind == SessionHolderKind.Container) != _inventory.Lists.IsContainer(holder.Entity)))
            return Reject(GameSessionFailure.InventoryMismatch);
        if (admitted.Holders.Values.Any(holder => _inventory.AddedTo(holder.Entity).Count +
                holder.Items.Count(entity => !_inventory.Taken.Contains(entity)) > SessionMapInventorySeed.MaxListLength))
            return Reject(GameSessionFailure.BudgetExceeded);
        // An object lies on one map: an item another attached map lists would be hidden there too once taken.
        foreach (var (other, attached) in _inventory.Seeds)
            if (other != map && admitted.Items.Keys.Any(attached.Items.ContainsKey)) return Reject(GameSessionFailure.InventoryMismatch);
        // ARC-WORLD-063: an item in a junk pile on this map that is no placed item is an item of this seed; no seed of another map lists
        // it. (Where it lies is the seed's and the map changes' answer: an item no longer on the pile's cell is not shown in the pile.)
        foreach (var pile in _inventory.Objects.Piles)
            foreach (var entry in pile.Items.Where(entry => _inventory.PlacedItem(entry.Entity) is null))
                if (pile.Map == map ? !admitted.Items.ContainsKey(entry.Entity) && !admitted.Source.Window : admitted.Items.ContainsKey(entry.Entity))
                    return Reject(GameSessionFailure.InventoryMismatch);
        if (!_inventory.Seeds.ContainsKey(map) && _inventory.Seeds.Count >= _limits.MaxMaps) return Reject(GameSessionFailure.BudgetExceeded);
        // ARC-COMBAT-024: a window keeps the previous seed's rows of the map's items the session tracks that it does not list (lookups only:
        // they join neither its ground nor a holder's list).
        if (admitted.Source.Window && _inventory.Seeds.TryGetValue(map, out var previous))
        {
            var piled = _inventory.Objects.Piles.Where(pile => pile.Map == map).SelectMany(pile => pile.Items).Select(entry => entry.Entity).ToHashSet(StringComparer.Ordinal);
            foreach (var (entity, kept) in previous.Items)
                if ((_inventory.Taken.Contains(entity) || piled.Contains(entity)) && !admitted.Items.ContainsKey(entity)) admitted.Items.Add(entity, kept);
        }
        _inventory.Seeds[map] = admitted;
        _inventory.SeedGeneration++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, new(_identity, _revision));
    }

    /// <summary>The current inventory of an attached map (its seed without the items the PC took), or null when no seed
    /// is attached.</summary>
    public SessionMapInventory? QueryMapInventory(SessionMapId map)
    {
        ThrowIfDisposed();
        return _inventory.Seeds.TryGetValue(map, out var seed)
            ? _inventory.View(map, seed, KilledNpc, holder => SpillCell(map, seed, holder), item => DroppedCell(map, seed, item)) : null;
    }

    /// <summary>Detached copy of the items the PC took from the world, in the order they entered its list. The hero's
    /// starting kit, which precedes them, is <see cref="QueryHeroKit"/>.</summary>
    public IReadOnlyList<SessionPcItem> QueryPcInventory() { ThrowIfDisposed(); return Array.AsReadOnly(_inventory.Pc.ToArray()); }

    /// <summary>Detached copy of the PC's made items (ARC-TECHNOLOGY-004): made from schematics or taken from an NPC that held them
    /// (ARC-WORLD-055), in the order they entered the list.</summary>
    public IReadOnlyList<SessionMadeItem> QueryMadeItems() { ThrowIfDisposed(); return Array.AsReadOnly(_inventory.Made.ToArray()); }

    /// <summary>
    /// Ground pickup (the pickup goal, then the item transfer 0x4617F0): an item lying on the actor's map goes to the end of
    /// the PC's list. Since ARC-WORLD-059 one of the hero's placed items on the actor's map goes back to the list it came from (a kit
    /// item at its kit number's place, a made item to the made items) after the insert check; it runs no script, and a world item
    /// whose attached seed shows a get or insert-item script is refused (<see cref="GameSessionFailure.ItemScriptUnsupported"/>), and a
    /// thrown item still in flight (ARC-COMBAT-017, hidden until it lands) is <see cref="GameSessionFailure.ItemNotAvailable"/>. Reach, the walk to the item and the combat action-point cost belong to the host. Refused before any
    /// effect: an item with an item <c>SAP_GET</c> script and no host (ARC-SCRIPT-041: with one it runs before the insert check and
    /// can decline, <see cref="InventoryMutationResult.DeclinedBy"/>), gold or a key type (gold and key rings are not modelled),
    /// and a refusal of <paramref name="insertCheck"/>, which runs after every other check. Since ARC-WORLD-066 an ammunition item
    /// merges into the hero's stack of its prototype (or starts one; a sum past 32 bits is <see cref="GameSessionFailure.BudgetExceeded"/>). An item
    /// <c>SAP_INSERT_ITEM</c> script runs through <paramref name="scripts"/> (ARC-SCRIPT-013, see <see cref="ISessionItemScriptHost"/>);
    /// without a host such an item is refused.
    /// ARC-MAGIC-022: with <paramref name="triggers"/> the pickup executor (0x4CBAA0) runs after the move (<see cref="InventoryMutationResult.ItemSpells"/>).
    /// </summary>
    [OriginalFunction(0x4617F0, OriginalFunctionScope.Partial)]
    public InventoryMutationResult PickUpItem(GameSessionVersion expected, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts = null,
        ISessionItemTriggers? triggers = null) => PickedUp(PickUp(expected, item, insertCheck, scripts), triggers);

    private InventoryMutationResult PickUp(GameSessionVersion expected, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        // ARC-COMBAT-017: a thrown item in flight is hidden (object flag 0x2) until it lands.
        if (ItemInFlight(item)) return RejectItem(GameSessionFailure.ItemNotAvailable);
        // ARC-WORLD-063: an item in a junk pile is taken from the pile (LootItem), not from the ground.
        if (_inventory.Objects.PileOf(item) is not null) return RejectItem(GameSessionFailure.ItemNotAvailable);
        // ARC-WORLD-059: one of the hero's items the session placed on this map goes back to its list.
        if (_inventory.PlacedItem(item) is { } placed && placed.Map == _actor.Map) return Picked(item, TakePlaced(placed, insertCheck));
        if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
        // ARC-COMBAT-008: an item of a container an attack broke lies on the ground at the container's cell; ARC-COMBAT-009: an NPC's
        // item a critical dropped lies at the cell it was dropped on.
        if (!seed.Items.TryGetValue(item, out var row) || _inventory.Taken.Contains(item) || LooseCell(_actor.Map, seed, item) is null)
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        return Picked(item, Take(seed, new(item, _inventory.WithAmmo(item, row.Facts)), null, insertCheck, scripts));
    }

    /// <summary>
    /// Loot (a click on a dead critter opens it as a container; the moved item runs 0x4617F0 to the PC): an item
    /// still in the list of a dead NPC on the actor's map goes to the end of the PC's list. Only an NPC the seed marks dead, or one
    /// that died in this session (ARC-COMBAT-005, <see cref="QueryNpcKilled"/>), is lootable here; since ARC-WORLD-101 a container of the
    /// seed that did not spill is too (its seed items, the items a barter or a regeneration put into it), whose open the host admits
    /// (lock, use script: the window's admission 0x572370, so a container's use script is no refusal here) and generates first
    /// (<see cref="GenerateContainerOnce"/>). Refused before any effect, beyond the item rules of
    /// <see cref="PickUpItem"/>: a body with a <c>SAP_USE</c> script, an item in a worn slot (unequip effects), and an item
    /// <c>SAP_REMOVE_ITEM</c> script without a host. The PC's own <c>SAP_TRANSFER</c> attachment is not modelled without a host (the
    /// session has no PC object); since ARC-SCRIPT-041 that slot runs through <paramref name="scripts"/>, with the item's remove-item script.
    /// ARC-MAGIC-022: with <paramref name="triggers"/> the pickup executor (0x4CBAA0) runs after the move (<see cref="InventoryMutationResult.ItemSpells"/>).
    /// </summary>
    [OriginalFunction(0x461CA0, OriginalFunctionScope.Partial)]
    public InventoryMutationResult LootItem(GameSessionVersion expected, string holder, string item, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null, ISessionItemTriggers? triggers = null) => PickedUp(Loot(expected, holder, item, insertCheck, scripts), triggers);

    private InventoryMutationResult Loot(GameSessionVersion expected, string holder, string item, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(holder) || !SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        // ARC-WORLD-063: a junk pile the session made on the actor's map is looted as the container it is.
        if (_inventory.Objects.Pile(holder) is { } pile && pile.Map == _actor.Map) return LootJunkPile(pile, item, insertCheck, scripts);
        if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
        if (!seed.Holders.TryGetValue(holder, out var row)) return RejectItem(GameSessionFailure.UnknownHolder);
        // ARC-WORLD-101: a container that did not spill is looted as its window shows it (0x572370 admits it in loot mode 2).
        var container = row.Kind == SessionHolderKind.Container;
        if (container ? SpillCell(_actor.Map, seed, holder) is not null : row.Kind != SessionHolderKind.Npc || !row.Dead && !KilledNpc(holder))
            return RejectItem(GameSessionFailure.HolderNotLootable);
        // A container's use script ran at its window's admission (the host's, 0x572370); a body's is not run (not modelled).
        if (container) return TakeFromContainer(holder, item, false, 0, default, insertCheck, scripts);
        if (row.ScriptPoints.Contains(SessionScriptPoint.Use)) return RejectItem(GameSessionFailure.HolderScriptUnsupported);
        // ARC-WORLD-055: a made item the session added to this NPC is in its list too.
        if (_inventory.AddedTo(holder).FirstOrDefault(added => added.Entity == item) is { Entity: not null } made)
            return Take(seed, new(item, _inventory.AddedFacts(made)), holder, insertCheck, scripts);
        if (!seed.HolderOf.TryGetValue(item, out var of) || of != holder || _inventory.Taken.Contains(item) || DroppedCell(_actor.Map, seed, item) is not null)
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        return Take(seed, new(item, _inventory.WithAmmo(item, seed.Items[item].Facts)), holder, insertCheck, scripts);
    }

    /// <summary>
    /// Negative dialog <c>IN -name</c> (the dialog <c>IN</c> action, ARC-RES-021): the first item in the NPC's current list
    /// whose name class is <paramref name="name"/> goes to the end of the PC's list, under the item rules of
    /// <see cref="LootItem"/>. No such item and no substitute inventory changes nothing, as in the source; an NPC with a
    /// substitute inventory then is <see cref="GameSessionFailure.SubstituteInventoryUnsupported"/>. The source drops an item
    /// the PC cannot take at the PC's location (into a junk pile when items lie there); that fallback is not modelled, so a
    /// refusal of <paramref name="insertCheck"/> is <see cref="GameSessionFailure.InsertRefused"/> with no effect.
    /// ARC-DEV-087 (MIG6e): the NPC may stand on another attached map's seed (a companion carried here); the item then keeps that map as its origin
    /// (<see cref="NpcHolderSeed"/>), and an item or holder of that map with a script to run is refused (the script host answers for the shown map).
    /// ARC-MAGIC-022: with <paramref name="triggers"/> the pickup executor (0x4CBAA0) runs after the move.
    /// </summary>
    public InventoryMutationResult GiveItemToPc(GameSessionVersion expected, string npc, int name, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts = null, ISessionItemTriggers? triggers = null) => PickedUp(GiveToPc(expected, npc, name, insertCheck, scripts), triggers);

    private InventoryMutationResult GiveToPc(GameSessionVersion expected, string npc, int name, SessionInsertCheck insertCheck, ISessionItemScriptHost? scripts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(npc) || name < 1) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        // ARC-DEV-087 (MIG6e): the holder is the NPC's wherever it stands: a companion carried here lists on its home map's seed.
        if (!NpcHolderSeed(npc, out var map, out var seed, out var row)) return RejectItem(UnseededHolder());
        if (_inventory.Current(seed, row, entry => DroppedCell(map, seed, entry)).FirstOrDefault(entry => entry.Facts.Kind.Name == name) is not { } found)
            return row.HasSubstituteInventory ? RejectItem(GameSessionFailure.SubstituteInventoryUnsupported)
                : new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, null, new(_identity, _revision));
        return Take(seed, found, npc, insertCheck, scripts, map);
    }

    /// <summary>
    /// ARC-DEV-087 (MIG6e): the attached seed that lists <paramref name="npc"/> as an NPC holder: the actor's map first, else the other
    /// attached maps in runtime-map order (the home map of a companion carried here, whose seed the host attached). An object lies on one
    /// map, so at most one other seed lists it. The item's origin on a move to the hero is this map, not the actor's.
    /// </summary>
    private bool NpcHolderSeed(string npc, out SessionMapId map, out InventoryDomain.Seed seed, out SessionSeedHolder row)
    {
        map = _actor.Map;
        if (_inventory.Seeds.TryGetValue(map, out seed!) && seed.Holders.TryGetValue(npc, out row!) && row.Kind == SessionHolderKind.Npc) return true;
        foreach (var (other, attached) in _inventory.Seeds.OrderBy(pair => pair.Key.RuntimeMap))
        {
            if (other == _actor.Map || !attached.Holders.TryGetValue(npc, out row!) || row.Kind != SessionHolderKind.Npc) continue;
            (map, seed) = (other, attached);
            return true;
        }
        (seed, row) = (null!, null!);
        return false;
    }

    // The refusal of NpcHolderSeed: no seed on the actor's map and no other that lists the holder is an unavailable inventory.
    private GameSessionFailure UnseededHolder() => _inventory.Seeds.ContainsKey(_actor.Map) ? GameSessionFailure.UnknownHolder : GameSessionFailure.InventoryUnavailable;

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
    /// ARC-MAGIC-024: with <paramref name="parties"/> (a spell's damage step, DH1) the result's item events are the step's wrapper
    /// calls (<see cref="ItemSpellTriggerRules.SpellDamageStep"/>); without them only the fatigue setter's going unconscious, whose
    /// call needs no context (IC4).
    /// </summary>
    public SessionLifeResult ApplyDamage(GameSessionVersion expected, SessionLifeTarget target, LifeDamage damage, SessionDamageParties? parties = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(damage);
        var valid = LifeRules.IsValid(damage) && (parties is null || parties.Valid);
        return Life(expected, target, valid, (state, facts, rolls) => LifeRules.ApplyDamage(state, facts, damage, rolls),
            damageStep: parties is null ? null : (damage, parties));
    }

    /// <summary>A kill (<see cref="LifeRules.Kill"/>), such as a script's; a death stamps the current game time.</summary>
    public SessionLifeResult Kill(GameSessionVersion expected, SessionLifeTarget target)
    {
        ThrowIfDisposed();
        return Life(expected, target, true, (state, facts, _) => LifeRules.Kill(state, facts));
    }

    /// <summary>One healing (<see cref="LifeRules.Heal"/>), resurrection included; amounts are 0..1,000,000. A raise asks
    /// <paramref name="raiseScript"/> (the target's raise script, ARC-MAGIC-012's veto port; none allows it). The port is asked while
    /// the step is computed, before the call's later refusals, so it must answer without side effects and without calling the
    /// session.</summary>
    public SessionLifeResult Heal(GameSessionVersion expected, SessionLifeTarget target, LifeHeal heal, Func<bool>? raiseScript = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(heal);
        var valid = heal.HitPoints is >= 0 and <= LifeLimits.MaxAmount && heal.Fatigue is >= 0 and <= LifeLimits.MaxAmount &&
            heal.Poison is >= 0 and <= LifeLimits.MaxAmount;
        return Life(expected, target, valid, (state, facts, _) => LifeRules.Heal(state, facts, heal, raiseScript));
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
        // A pass inside the spell-run rules (a host port) is refused: it would consume their events without running them.
        if (_passWorld is not null || _spellRunsBusy || budget < 1 || budget > _events.Limits.MaxPerPass)
            return new(GameSessionFailure.InvalidEventPass, null, Array.Empty<SessionLifeFiring>(), new(_identity, _revision));
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var firings = new List<SessionLifeFiring>();
        var calls = new List<SessionScriptCallFiring>();
        var effects = new List<SessionEffectFiring>();
        var locks = new List<SessionLockFiring>();
        var waits = new List<SessionWaitFiring>();
        var respawns = new List<SessionRespawnFiring>();
        var spellRuns = new List<SessionSpellRunFiring>();
        var decays = new List<SessionDecayFiring>();
        var aiTicks = new List<SessionAiTickFiring>();
        var restocks = new List<SessionRestockFiring>();
        _passDecays = decays;
        _passAiTicks = aiTicks;
        _passRestocks = restocks;
        // ARC-WORLD-062: the items the pending decay events name are the session's to answer for (the hero's kit and made items are
        // no map object of the host's, and a seed item's map may not be attached yet: its handler postpones).
        var decaying = _events.Any(SessionEventKinds.ItemDecay)
            ? _events.QueryPending(SessionEventKinds.ItemDecay).Select(e => e.Payload.Entities[0].Code).ToHashSet(StringComparer.Ordinal)
            : null;
        // ARC-WORLD-094: so are the objects the pending restock events name (by map and entity), other than an NPC with a life record
        // (the host's facts answer for it as for its timers), for a host that has no object check: the handler asks the restock host,
        // and a missing object does nothing there (RK3). A host with an object check answers for them as for any object.
        var restocking = _events.Any(SessionEventKinds.Restock) && world is not ISessionScriptWorld
            ? _events.QueryPending(SessionEventKinds.Restock).Select(e => e.Payload.Entities[0]).ToHashSet()
            : null;
        _passRespawns = respawns;
        _passSpellRuns = spellRuns;
        _passWorld = world;
        _passFirings = firings;
        _passCalls = calls;
        _passEffects = effects;
        _passLocks = locks;
        _passWaits = waits;
        GameEventPassResult pass;
        try
        {
            // A life timer's NPC must be present with its life record; an object a deferred call names must exist (an
            // absent role always does).
            pass = _events.ProcessDue(new(new(0, now, 0), _actor.Map.RuntimeMap, budget, entity =>
                entity.Code == SessionEventKinds.AbsentRole
                || decaying?.Contains(entity.Code) == true && !_durability.Destroyed.Contains(entity.Code)
                || restocking?.Contains(entity) == true && !_life.Critters.ContainsKey(entity.Code)
                || (_life.Critters.ContainsKey(entity.Code)
                    ? world.CritterFacts(new(entity.Map), entity.Code) is not null
                    // A host that knows objects answers for every kind; otherwise a Lock event's object exists while the
                    // session records its lock state (ARC-WORLD-050), and a waiting NPC while the session records it waiting
                    // (ARC-WORLD-045).
                    : world is ISessionScriptWorld scripts ? scripts.ObjectExists(new(entity.Map), entity.Code)
                    : LockObjectExists(entity) || RespawnObjectExists(entity) || _party.Get(entity.Code).Standing == CompanionStanding.Waiting)));
        }
        finally
        {
            _passWorld = null;
            _passFirings = null;
            _passCalls = null;
            _passEffects = null;
            _passLocks = null;
            _passWaits = null;
            _passRespawns = null;
            _passSpellRuns = null;
            _passDecays = null;
            _passAiTicks = null;
            _passRestocks = null;
        }
        return new(pass.Failure == GameEventFailure.None ? GameSessionFailure.None : GameSessionFailure.InvalidEventPass, pass,
            firings.AsReadOnly(), new(_identity, _revision)) { ScriptCalls = calls.AsReadOnly(), Effects = effects.AsReadOnly(),
            Locks = locks.AsReadOnly(), Waits = waits.AsReadOnly(), Respawns = respawns.AsReadOnly(), SpellRuns = spellRuns.AsReadOnly(),
            Decays = decays.AsReadOnly(), AiTicks = aiTicks.AsReadOnly(), Restocks = restocks.AsReadOnly() };
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
    // ARC-DEV-073: with a move list only its travellers go (a follower ordered to wait stays, a follower of a follower goes) and their
    // AI ticks go with them (RL28: the carry comes before the payload; one tick, not two, as the destination's first AI start
    // finds it pending); without one every follower's events move and every tick is dropped, as before.
    private GameSessionFailure MoveFollowerEvents(SessionMapId to, IReadOnlyCollection<string>? travelling = null)
    {
        if (to == _actor.Map) return GameSessionFailure.None;
        var followers = (travelling ?? _party.Order).ToHashSet(StringComparer.Ordinal);
        var failure = _events.Relocate(_actor.Map.RuntimeMap, to.RuntimeMap, entity => followers.Contains(entity.Code)).Failure;
        // ARC-SCRIPT-010: a deferred call's player and follower roles travel one by one (the original keeps their handles
        // across maps), its other roles stay where they were. Both steps refuse only during a pass, which the first one
        // already reports, so the rewrite cannot fail after the relocation moved anything.
        if (failure == GameEventFailure.None)
            failure = _events.RelocateEntities(SessionEventKinds.ScriptCall, _actor.Map.RuntimeMap, to.RuntimeMap,
                entity => entity.Code == SessionEventKinds.PlayerEntity || followers.Contains(entity.Code)).Failure;
        // ARC-WORLD-084: the AI ticks of the map left go (the followers' too, which the relocation carried); the host's sector
        // fold wakes the new map's NPCs. So do the danger records that hold only the gate's check bits: the fold's wake-up sets
        // "check wield" again, whose full re-wield covers "check weapon" too, so dropping them changes nothing an NPC does and
        // keeps the danger budget from filling with NPCs woken on earlier maps.
        if (failure == GameEventFailure.None)
        {
            _events.RemoveAll(SessionEventKinds.AiTick, e => travelling is null || e.ScopeMap != to.RuntimeMap);
            DropCheckOnlyDanger();
        }
        return failure switch
        {
            GameEventFailure.None => GameSessionFailure.None,
            GameEventFailure.PassInProgress => GameSessionFailure.InvalidEventPass,
            _ => GameSessionFailure.EventConflict,
        };
    }

    // The danger records that hold only the gate's check bits (see MoveFollowerEvents).
    private void DropCheckOnlyDanger()
    {
        foreach (var record in _danger.Records.Values.Where(r => (r with { Flags = r.Flags & ~CheckBits }).IsBaseline && !r.IsBaseline).ToArray())
            _danger.Put(record with { Flags = Arcanum.Simulation.Ai.DangerFlags.None });
    }

    /// <summary>Copies every component at this call boundary into one detached snapshot.</summary>
    public GameSessionSnapshot Capture()
    {
        ThrowIfDisposed();
        var clock = _clock.QueryClockSnapshot();
        var flags = _flags.CaptureSnapshot();
        var maps = _maps.CaptureMaps();
        return new(GameSessionSnapshot.CurrentSchemaVersion, _flags.Binding.Identity, _revision,
            new(clock.ProfileBinding, clock.GameTimestampMs, clock.PauseDisposition, clock.Revision),
            new(flags.Version.Revision, flags.Entries.ToArray()), _actor, maps, _story.Capture(), _party.Capture(), _hero?.State,
            _inventory.Capture(), _equipment.Capture(), _durability.Capture(), new(_foundSchematics), _effects.Capture(), _experience.Capture(), _advancement.Capture(),new(_random.Seed, _random.Draws), _life.Capture(), _reactions.Capture(), _combat.Round?.Capture(_combat.SubturnStarts), CaptureCombatMode(), CaptureConcealment(), _danger.Capture(), CaptureEvents(), _lighting, _sound.Capture(),
            QueryHotkeys(), _inventory.Objects.Capture().WithState(_maps.CaptureFields()));
    }

    /// <summary>Idempotent; disposes the clock and flag owners with the session.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clock.Dispose();
        _flags.Dispose();
    }

    // ARC-DEV-092 (MIG7a, schema 62): the maps section holds the per-map changes only; the fields of mobile map-data objects are the
    // objects section's (an older save's step moved them), so a routed key in the maps section is a damaged save. Both count against one budget.
    private static GameSessionFailure ImportMaps(IReadOnlyList<SessionMapState> saved, IReadOnlyList<SessionObjectField> fields,
        GameSessionLimits limits, out SessionMapChanges maps)
    {
        maps = new();
        if (saved.Count > limits.MaxMaps) return GameSessionFailure.BudgetExceeded;
        var total = 0;
        foreach (var map in saved)
        {
            if (!SessionValidation.Map(map.Map) || map.Changes.Count == 0) return GameSessionFailure.InvalidMap;
            if (map.Changes.Count > limits.MaxChangesPerMap) return GameSessionFailure.BudgetExceeded;
            total += map.Changes.Count;
            if (total > limits.MaxTotalChanges) return GameSessionFailure.BudgetExceeded;
            foreach (var change in map.Changes)
            {
                if (!SessionValidation.Key(change.Key)) return GameSessionFailure.InvalidChangeKey;
                if (ObjectStateVocabulary.Routed(change.Key) || maps.Get(map.Map, change.Key) is not null) return GameSessionFailure.InvalidSnapshot;
                maps.Set(map.Map, change.Key, change.Value);
            }
        }
        foreach (var field in fields)
        {
            if (!SessionValidation.Map(field.Map)) return GameSessionFailure.InvalidMap;
            if (!SessionValidation.Entity(field.Entity) || !ObjectStateVocabulary.Routed(field.Entity, field.Property)) return GameSessionFailure.InvalidChangeKey;
            var key = MapChangeKey.ForEntity(field.Entity, field.Property);
            if (maps.RecordMap(key) is not null) return GameSessionFailure.InvalidSnapshot;
            if (maps.CountOf(field.Map) >= limits.MaxChangesPerMap || maps.Total >= limits.MaxTotalChanges ||
                (maps.CountOf(field.Map) == 0 && maps.MapCount >= limits.MaxMaps)) return GameSessionFailure.BudgetExceeded;
            maps.Set(field.Map, key, field.Value);
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
        // ARC-WORLD-056: known areas, and a last discovered area that the PC knows (the original sets it with the known bit).
        foreach (var area in saved.KnownAreas)
            if (!SessionStoryLimits.IsArea(area) || !story.KnownAreas.Add(area)) return GameSessionFailure.InvalidSnapshot;
        if (saved.LastArea != 0 && !story.KnownAreas.Contains(saved.LastArea)) return GameSessionFailure.InvalidSnapshot;
        story.LastArea = saved.LastArea;
        if (saved.StoryState < 0) return GameSessionFailure.InvalidSnapshot;
        story.StoryState = saved.StoryState;
        var reputations = ImportReputations(saved.Reputations, clockMs, story.Reputations);
        if (reputations != GameSessionFailure.None) return reputations;
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

    // With a hero the session owns the PC's side of the check (ARC-SKILLS-002, ARC-WORLD-067, HeroPartySide): its persuasion training,
    // effective charisma, level and alignment (dialog alignment changes write it, ChangeHeroAlignment), and the max followers they
    // give; the caller's values stand in only without a hero.
    private FollowConsent Consent(PartyConsentFacts facts, bool forcedFollower, bool ignoreLimit)
    {
        var (persuasion, charisma, alignment, level, maxFollowers) = HeroPartySide() is { } pc
            ? (pc.Persuasion, pc.Charisma, pc.Alignment, pc.Level, pc.MaxFollowers)
            : (facts.PcPersuasion, facts.PcCharisma, facts.PcAlignment, facts.PcLevel, facts.MaxFollowers);
        return FollowConsentRules.Evaluate(new(facts.MindControlledByPc, persuasion, charisma, alignment, level,
            facts.OtherLeaderCharisma, forcedFollower, facts.Reaction, facts.Thresholds, facts.NpcAlignment, facts.NpcLevel, maxFollowers,
            _party.NonForcedFollowers, ignoreLimit));
    }

    private static bool ValidFacts(PartyConsentFacts facts) => Enum.IsDefined(facts.PcPersuasion);

    // Join and unwait share the commit: leave any standing, append at the end of the array. A waiting NPC's wait timer is
    // removed (OL25: join leaves first, and unwait cancels it).
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
        if (current.Standing == CompanionStanding.Waiting) CancelWaitTimers(current.Entity);
        // ARC-SKILLS-008 (ST2): a new follower takes its leader's concealment.
        if (_combat.HeroConcealed) _combat.Concealed.Add(current.Entity); else _combat.Concealed.Remove(current.Entity);
        Revise();
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
            if (!SessionValidation.Entity(record.Entity) || record.Standing != CompanionStanding.Following || record.Jilted ||
                !CompanionLoyaltyRules.IsValid(record.Loyalty)) return GameSessionFailure.InvalidCompanion;
            if (party.Records.ContainsKey(record.Entity)) return GameSessionFailure.InvalidSnapshot;
            party.Put(record, true);
        }
        string? previous = null;
        foreach (var record in saved.Others)
        {
            if (!SessionValidation.Entity(record.Entity) || record.Forced || record.IsBaseline || !CompanionLoyaltyRules.IsValid(record.Loyalty) ||
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
    // ARC-WORLD-059: a placed item back to the list it came from (a kit item at its number's place). It runs no script: a world item
    // whose attached origin seed shows a get or insert-item script is refused (ItemScriptUnsupported); a kit or made item's scripts the
    // session does not know.
    private InventoryMutationResult TakePlaced(SessionPlacedItem placed, SessionInsertCheck insertCheck)
    {
        var listed = placed.Origin is { } origin && _inventory.Seeds.TryGetValue(origin, out var seed) && seed.Items.TryGetValue(placed.Entity, out var item)
            ? item : null;
        if (listed is not null && (listed.Facts.HasScript(SessionScriptPoint.Get) || listed.Facts.HasScript(SessionScriptPoint.InsertItem)))
            return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        // ARC-WORLD-066: a placed ammunition stack merges into the hero's stack of the same prototype when it holds one.
        var into = placed.Kind.Type == SessionItemType.Ammo ? HeroStackOfPrototype(placed.Kind.Prototype) : null;
        if (into is { } target && ((long)target.Units + _inventory.Ammo[placed.Entity].Units > int.MaxValue ||
            !_durability.Records.ContainsKey(placed.Entity) && _durability.Count >= _limits.MaxDurabilityRecords)) return RejectItem(GameSessionFailure.BudgetExceeded);
        // ARC-WORLD-081: a created item enters the hero's list, so it needs the hero's room.
        if (placed.Source == SessionPlacedSource.Created && _inventory.Held >= _limits.MaxPcItems) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var revision = _revision;
        // ARC-WORLD-069: a seed item keeps its seed's complexity for the Technophobia refusal; ARC-WORLD-083: a made item the one it was
        // placed with (the original's drop and pickup leave field 0x60 alone, ledger MC1); a kit item has none in the session (0).
        var complexity = _inventory.PlacedComplexity(placed);
        var placedFacts = _inventory.WithAmmo(placed.Entity, InventoryDomain.MadeFacts(placed.Kind, complexity));
        var verdict = HeroInsertVerdict(insertCheck(new SessionInventoryItem(placed.Entity, placedFacts)), complexity, Incoming(placed.Entity, placedFacts));
        ThrowIfDisposed();
        if (_revision != revision || _inventory.PlacedItem(placed.Entity) != placed || _actor.Map != placed.Map)
            return RejectItem(GameSessionFailure.StaleVersion, verdict);
        if (verdict != SessionInsertVerdict.Accepted) return RejectItem(GameSessionFailure.InsertRefused, verdict);
        if (into is { } stack)
        {
            var units = _inventory.Ammo[placed.Entity].Units;
            DestroyObject(placed.Entity);
            _inventory.Ammo[stack.Entity] = stack with { Units = stack.Units + units };
        }
        else _inventory.Return(placed);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, placed.Entity, verdict, new(_identity, _revision));
    }

    /// <summary>
    /// ARC-WORLD-059: places one of the hero's items (its kit, a taken or a made item) on the ground of the actor's map at
    /// <paramref name="cell"/>: a worn item leaves its slot, the item leaves its list and joins the placed items, and stays taken.
    /// One revision. The caller checked the item and the cell. ARC-COMBAT-017: a thrown item is laid down without the drop's decay
    /// (<paramref name="decays"/> false: the throw's plain placement 0x4415C0 schedules none, ledger ID4).
    /// </summary>
    internal void PlaceHeroItem(string entity, Arcanum.Simulation.Movement.CellCoord cell, bool decays = true)
    {
        SessionPlacedItem placed;
        if (_inventory.Hero.KitItem(entity) is { } kit)
        {
            _inventory.Hero.Remove(entity);
            placed = new(entity, kit.Kind, SessionPlacedSource.Kit, null, _actor.Map, cell);
        }
        else if (_inventory.Hero.PcItem(entity) is { } pc)
        {
            _inventory.Hero.Remove(entity);
            placed = new(entity, pc.Kind, SessionPlacedSource.Pc, pc.Origin, _actor.Map, cell);
        }
        else if (_inventory.Hero.MadeItem(entity) is { } made)
        {
            _inventory.Hero.Remove(entity);
            placed = new(entity, made.Kind, SessionPlacedSource.Made, null, _actor.Map, cell, made.Complexity);
        }
        else throw new InvalidOperationException("PLACED_ITEM_NOT_HELD");
        if (_equipment.SlotOf(entity) is { } slot) _equipment.Worn.Remove(slot);
        _inventory.Hero.AddPlaced(placed);
        // ARC-WORLD-063: the drop's placement may put it into a junk pile; ARC-WORLD-062: every drop on the ground then schedules the
        // item's decay (ledger ID4). A thrown item's plain placement does neither.
        if (decays)
        {
            SettleDrop(entity, placed.Map, cell);
            ScheduleDecay(entity, placed.Map);
        }
        Revise();
    }

    /// <summary>ARC-WORLD-059: the hero's items the session placed on the ground, on every map, in placing order.</summary>
    public IReadOnlyList<SessionPlacedItem> QueryPlacedItems() { ThrowIfDisposed(); return Array.AsReadOnly(_inventory.Placed.ToArray()); }

    // The insertion (0x466640): since ARC-WORLD-066 its merge of ammunition into a held stack of the same prototype. ARC-SCRIPT-041:
    // the item events of the two transfers in the original's order (ledger IE1–IE4): from the ground (0x461810) the item's get
    // script, which can cancel, then the insert check, then the insertion's insert-item script; from a holder (0x461CA0) the
    // recipient's transfer script with the holder as triggerer and the item as extra object, which can cancel, then the insert
    // check, the removal's remove-item script (0x467860, the holder as triggerer) and the insert-item script.
    [Arcanum.Simulation.OriginalFunction(0x466640, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    private InventoryMutationResult Take(InventoryDomain.Seed seed, SessionInventoryItem item, string? holder, SessionInsertCheck insertCheck,
        ISessionItemScriptHost? scripts, SessionMapId? seedMap = null)
    {
        // ARC-DEV-087 (MIG6e): the map whose seed lists the item; it is the actor's unless the holder is another map's NPC (a carried companion).
        var origin = seedMap ?? _actor.Map;
        var parented = holder is not null;
        var facts = item.Facts;
        if (!InventoryDomain.Admitted(facts.Kind.Type)) return RejectItem(GameSessionFailure.ItemTypeUnsupported);
        if (parented && facts.Worn) return RejectItem(GameSessionFailure.WornItemUnsupported);
        // The item's own points on this path; the session runs them only through a host.
        var own = parented ? SessionScriptPoint.RemoveItem : SessionScriptPoint.Get;
        if (scripts is null && (facts.HasScript(own) || facts.HasScript(SessionScriptPoint.InsertItem))) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        // ARC-DEV-087 (MIG6e): the script host answers for the shown map's rows (every script object of a run stands on it), so the remove-item,
        // get and insert-item scripts of an item whose row is another map's seed row are not run: such a move is refused, never moved with a
        // script skipped. (The gate of a move from a holder is the PC's transfer script, not the holder's, so a holder's own scripts do not matter.)
        if (origin != _actor.Map && seed.Items.ContainsKey(item.Entity) && (facts.HasScript(own) || facts.HasScript(SessionScriptPoint.InsertItem)))
            return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        // ARC-WORLD-066: ammunition merges into the hero's stack of the same prototype (its units added, the item deleted, 0x466640),
        // else enters the list as a new stack. The sum must fit 32 bits (the original wraps: crash class, refused in both settings).
        SessionAmmoRecord? ammo = null, into = null;
        if (facts.Kind.Type == SessionItemType.Ammo)
        {
            if (AmmoOf(item.Entity, facts) is not { } brought) return RejectItem(GameSessionFailure.InvalidItem);
            ammo = brought;
            into = HeroStackOfPrototype(facts.Kind.Prototype);
            if (into is { } target)
            {
                if ((long)target.Units + brought.Units > int.MaxValue) return RejectItem(GameSessionFailure.BudgetExceeded);
                if (!_durability.Records.ContainsKey(item.Entity) && _durability.Count >= _limits.MaxDurabilityRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
            }
            else if (!_inventory.Ammo.ContainsKey(item.Entity) && _inventory.Ammo.Count >= _limits.MaxAmmoRecords) return RejectItem(GameSessionFailure.BudgetExceeded);
        }
        if (into is null && _inventory.Held >= _limits.MaxPcItems) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var (revision, map, flagVersion) = (_revision, _actor.Map, _flags.Version);
        // ARC-WORLD-055: a made item comes from the NPC that holds it (and is in Taken while it does), any other from a seed.
        var npc = _inventory.NpcOf(item.Entity);
        bool Stale() => _revision != revision || _actor.Map != map || _flags.Version != flagVersion || !_inventory.Seeds.TryGetValue(origin, out var current) ||
            !ReferenceEquals(current, seed) || (npc is null ? _inventory.Taken.Contains(item.Entity) : _inventory.NpcOf(item.Entity) != npc);

        var runs = scripts is null ? null : new ItemScriptRuns(this, scripts, map.RuntimeMap, new Dictionary<string, int>(StringComparer.Ordinal) { [item.Entity] = facts.Complexity });
        if (runs is not null)
        {
            // The gate: the item's get script, or the recipient's transfer script (the PC's slot is the host's to answer). The
            // original consults neither the insert check nor the item's other scripts after skip default.
            var gatePoint = parented ? SessionScriptPoint.Transfer : SessionScriptPoint.Get;
            var gate = parented
                ? runs.Run(SessionScriptPoint.Transfer, SessionEventKinds.PlayerEntity, holder!, item.Entity)
                : facts.HasScript(SessionScriptPoint.Get) ? runs.Run(SessionScriptPoint.Get, item.Entity, SessionEventKinds.PlayerEntity, null) : null;
            ThrowIfDisposed();
            if (Stale()) return RejectItem(GameSessionFailure.StaleVersion) with { Scripts = runs.Runs };
            if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure) with { Scripts = runs.Runs };
            if (gate?.Outcome == ScrOutcome.SkipDefault) return Declined(gatePoint, runs);
        }
        // ARC-WORLD-069: the Technophobia refusal between the host's weight and room answers (0x466510).
        // ARC-WORLD-085: the weight is the session's for an item it can weigh (its seed facts or the row it keeps).
        var verdict = HeroInsertVerdict(insertCheck(item), facts.Complexity, Incoming(item.Entity, facts));
        ThrowIfDisposed();
        if (Stale()) return RejectItem(GameSessionFailure.StaleVersion, verdict) with { Scripts = runs?.Runs ?? [] };
        // Any answer but Accepted refuses, an undefined one included. Session policy: a refused transfer changes nothing, so a
        // gate script's writes are dropped with it (the original keeps them; census: no installed item or PC carries such a script).
        if (verdict != SessionInsertVerdict.Accepted) return RejectItem(GameSessionFailure.InsertRefused, verdict) with { Scripts = runs?.Runs ?? [] };

        ScrRunResult? insertRun = null;
        if (runs is not null)
        {
            // The original moves the item first and then runs these scripts, whose results it ignores (IE3, IF10). The admitted
            // commands read nothing the move changes, so the runs are made before the commit and applied with it, or the whole
            // transfer is refused.
            if (parented && facts.HasScript(SessionScriptPoint.RemoveItem)) runs.Run(SessionScriptPoint.RemoveItem, item.Entity, holder!, null);
            if (runs.Failure == GameSessionFailure.None && facts.HasScript(SessionScriptPoint.InsertItem))
                insertRun = runs.Run(SessionScriptPoint.InsertItem, item.Entity, SessionEventKinds.PlayerEntity, null);
            ThrowIfDisposed();
            if (Stale()) return RejectItem(GameSessionFailure.StaleVersion, verdict) with { InsertScript = insertRun, Scripts = runs.Runs };
            runs.Prepare();
            if (runs.Failure != GameSessionFailure.None) return RejectItem(runs.Failure, verdict) with { InsertScript = insertRun, Scripts = runs.Runs };
        }
        if (into is { } stack)
        {
            // The inserted stack's units join the held one and the inserted item is deleted (from the NPC's added items too).
            DestroyObject(item.Entity);
            _inventory.Ammo[stack.Entity] = stack with { Units = stack.Units + ammo!.Value.Units };
        }
        else if (npc is not null)
        {
            // A made item keeps its entity and kind: it leaves the NPC's added items and joins the hero's made items; ARC-WORLD-071: a kit
            // or taken item the hero gave the NPC returns to the list it came from.
            _inventory.RemoveFromNpc(item.Entity);
            _inventory.ReturnGiven(item.Entity, facts.Kind, facts.Complexity);
        }
        else
        {
            _inventory.Add(new SessionPcItem(item.Entity, origin, facts.Kind));
            // ARC-WORLD-085: the item keeps its seed's weight facts wherever it goes.
            _inventory.SetWeight(item.Entity, facts.WeightFacts);
        }
        if (into is null && ammo is { } added) _inventory.Ammo[item.Entity] = added;
        runs?.Commit();
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item.Entity, verdict, new(_identity, _revision))
            { InsertScript = insertRun, Scripts = runs?.Runs ?? [], Messages = runs?.Messages ?? [] };
    }

    /// <summary>ARC-MAGIC-022: the pickup executor (0x4CBAA0, IU10) through <paramref name="triggers"/> for an item that entered the hero's
    /// list (not for an ammunition item merged into a stack: it is deleted, and the original returns before the pickup).</summary>
    private InventoryMutationResult PickedUp(InventoryMutationResult result, ISessionItemTriggers? triggers)
    {
        if (triggers is null || result.Disposition != GameSessionDisposition.Changed || result.Item is not { } item || _durability.Destroyed.Contains(item))
            return result;
        return result with { ItemSpells = [triggers.Fire(Arcanum.Simulation.Magic.ItemSpellTriggerRules.Pickup, item, SessionSpellCaster.Hero)], Version = Version };
    }

    private InventoryMutationResult RejectItem(GameSessionFailure failure, SessionInsertVerdict? verdict = null) =>
        new(GameSessionDisposition.Rejected, failure, null, verdict, new(_identity, _revision));

    // The hero is rebuilt from its saved fields under the same checks as a new one.
    private static GameSessionFailure ImportHero(SessionHeroState? saved, out HeroDomain? hero)
    {
        hero = null;
        if (saved is null) return GameSessionFailure.None;
        hero = HeroDomain.Build(saved.Prototype, saved.Race, saved.Gender, saved.Stats, saved.Level, saved.UnspentPoints, saved.Alignment,
            saved.RaceEffect, saved.GenderEffect, saved.Experience, saved.Advancement, saved.InnateSkills, saved.Fate, saved.Background,
            saved.BackgroundDescription, saved.PlayerName, saved.Portrait);
        return hero is null ? GameSessionFailure.InvalidHero : GameSessionFailure.None;
    }

    // The PC's items keep their saved order; each must be one the session can produce: a kit item, which only a hero has,
    // named kit:1, kit:2, … in order (no transfer removes items yet), and an admitted type taken from a valid map. Gold,
    // too, only comes with a hero.
    private static GameSessionFailure ImportInventory(SessionInventoryState saved, bool hero, GameSessionLimits limits, IReadOnlySet<string> destroyed,
        out InventoryDomain inventory)
    {
        inventory = new();
        if (saved.Gold < 0 || (saved.Gold > 0 && !hero) || (saved.Kit.Count > 0 && !hero)) return GameSessionFailure.InvalidSnapshot;
        inventory.Gold = saved.Gold;
        // ARC-WORLD-081: the items scripts created on the ground have a budget of their own.
        var created = saved.Placed.Count(item => item?.Source == SessionPlacedSource.Created);
        if (saved.Kit.Count + saved.Pc.Count + saved.Made.Count + saved.Placed.Count - created > limits.MaxPcItems || created > limits.MaxCreatedItems)
            return GameSessionFailure.BudgetExceeded;
        // Kit items are kit:1, kit:2, … in order; a deleted one (ARC-WORLD-053) or one placed on the ground (ARC-WORLD-059) leaves
        // its number out.
        // ARC-WORLD-071: a kit item the hero gave an NPC leaves its number out too.
        var placedKit = saved.Placed.Where(item => item?.Source == SessionPlacedSource.Kit).Select(item => item.Entity)
            .Concat(saved.Given.Where(item => item.Origin is null && HeroDomain.KitNumber(item.Entity) is not null).Select(item => item.Entity))
            .ToHashSet(StringComparer.Ordinal);
        var next = 0;
        foreach (var item in saved.Kit)
        {
            if (!Enum.IsDefined(item.Kind.Type) || !InventoryDomain.KitAdmitted(item.Kind.Type)) return GameSessionFailure.InvalidItem;
            while (next < limits.MaxPcItems + destroyed.Count + placedKit.Count &&
                (destroyed.Contains(HeroDomain.KitEntity(next)) || placedKit.Contains(HeroDomain.KitEntity(next)))) next++;
            if (item.Entity != HeroDomain.KitEntity(next++)) return GameSessionFailure.InvalidSnapshot;
            inventory.Add(item);
        }
        foreach (var item in saved.Pc)
        {
            if (!SessionValidation.Entity(item.Entity) || !SessionValidation.Map(item.Origin) || !Enum.IsDefined(item.Kind.Type) ||
                !InventoryDomain.Admitted(item.Kind.Type)) return GameSessionFailure.InvalidItem;
            if (inventory.Taken.Contains(item.Entity) || destroyed.Contains(item.Entity) ||
                item.Entity.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal)) return GameSessionFailure.InvalidSnapshot;
            inventory.Add(item);
        }
        // Made items (ARC-TECHNOLOGY-004): made:n, an admitted type, not deleted, each once. Since ARC-WORLD-055 the PC can take a made
        // item from an NPC (with or without a hero), so the list keeps entry order, not number order.
        foreach (var item in saved.Made)
        {
            if (!Enum.IsDefined(item.Kind.Type) || !InventoryDomain.Admitted(item.Kind.Type)) return GameSessionFailure.InvalidItem;
            if (SessionMadeItem.Number(item.Entity) is null || destroyed.Contains(item.Entity) || inventory.Taken.Contains(item.Entity))
                return GameSessionFailure.InvalidSnapshot;
            inventory.Add(item);
        }
        // ARC-WORLD-071: the given items by entity: a kit item only with a hero and within the kit's numbers (checked below), a taken item
        // with a valid origin map; the gold piles by entity, 1 or more coins, not deleted.
        if (saved.Given.Count > limits.MaxNpcItems || saved.Piles.Count > limits.MaxGoldRecords) return GameSessionFailure.BudgetExceeded;
        string? lastGiven = null;
        foreach (var item in saved.Given)
        {
            if (!SessionValidation.Entity(item.Entity) || lastGiven is not null && string.CompareOrdinal(lastGiven, item.Entity) >= 0 || destroyed.Contains(item.Entity))
                return GameSessionFailure.InvalidSnapshot;
            lastGiven = item.Entity;
            var fits = item.Origin is { } origin
                ? SessionValidation.Map(origin) && HeroDomain.KitNumber(item.Entity) is null && !item.Entity.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal)
                : hero && HeroDomain.KitNumber(item.Entity) is not null;
            if (!fits) return GameSessionFailure.InvalidSnapshot;
            inventory.Hero.SetGiven(item);
        }
        string? lastPile = null;
        foreach (var pile in saved.Piles)
        {
            // A move that empties a pile destroys it, so a tracked pile holds a coin or more.
            if (!SessionValidation.Entity(pile.Entity) || lastPile is not null && string.CompareOrdinal(lastPile, pile.Entity) >= 0 || pile.Quantity < 1 ||
                destroyed.Contains(pile.Entity)) return GameSessionFailure.InvalidSnapshot;
            lastPile = pile.Entity;
            inventory.Piles[pile.Entity] = pile.Quantity;
        }
        // ARC-WORLD-055: the made items added to NPCs, by NPC entity in ordinal order, each list non-empty and within one list's bound;
        // the same item rules as the hero's made items, and no item held twice. ARC-WORLD-071: an entry may also be a given item (its
        // record above) or a made gold pile (with its pile record).
        if (saved.Npcs.Sum(npc => (long)(npc.Items?.Count ?? 0)) > limits.MaxNpcItems) return GameSessionFailure.BudgetExceeded;
        string? previous = null;
        foreach (var npc in saved.Npcs)
        {
            if (npc?.Items is null || !SessionValidation.Entity(npc.Npc) || npc.Npc.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal) ||
                npc.Items.Count is 0 or > SessionMapInventorySeed.MaxListLength || previous is not null && string.CompareOrdinal(previous, npc.Npc) >= 0)
                return GameSessionFailure.InvalidSnapshot;
            previous = npc.Npc;
            // ARC-WORLD-090: a container's list (a barter window's sales).
            foreach (var item in npc.Items)
            {
                // A gold item is only a made pile with its record.
                var pile = item.Kind.Type == SessionItemType.Gold && inventory.Piles.ContainsKey(item.Entity);
                if (!Enum.IsDefined(item.Kind.Type) || !InventoryDomain.Admitted(item.Kind.Type) && !pile) return GameSessionFailure.InvalidItem;
                var given = inventory.Hero.IsGiven(item.Entity);
                // ARC-WORLD-083: only a made item keeps a complexity of its own (a given item's seed answers; a pile has none).
                if ((SessionMadeItem.Number(item.Entity) is null) != given || pile && given || (given || pile) && item.Complexity != 0 ||
                    given && !InventoryDomain.KitAdmitted(item.Kind.Type) && HeroDomain.KitNumber(item.Entity) is not null ||
                    destroyed.Contains(item.Entity) || inventory.Taken.Contains(item.Entity))
                    return GameSessionFailure.InvalidSnapshot;
                if (npc.Container) inventory.AddToContainer(npc.Npc, item); else inventory.AddToNpc(npc.Npc, item);
                inventory.Taken.Add(item.Entity);
            }
        }
        // ARC-WORLD-059: the hero's items placed on the ground: a kit item only with a hero, a taken item with its origin map, a made
        // item by its number; a valid map and cell; not deleted and listed once in the whole inventory. Each stays taken.
        // A placed kit item's number lies within the kit's numbers (the kit, its placed and its deleted items together).
        var kitNumbers = saved.Kit.Count + placedKit.Count + destroyed.Count(entity => HeroDomain.KitNumber(entity) is not null);
        foreach (var item in saved.Placed)
        {
            // Only the hero's drops place an item; ARC-WORLD-081: a script creates an item with or without a hero.
            if (!hero && item?.Source != SessionPlacedSource.Created) return GameSessionFailure.InvalidSnapshot;
            if (item is null || !SessionValidation.Entity(item.Entity) || !Enum.IsDefined(item.Source) || !Enum.IsDefined(item.Kind.Type) ||
                !InventoryDomain.Admitted(item.Kind.Type)) return GameSessionFailure.InvalidItem;
            var sourceFits = item.Source switch
            {
                SessionPlacedSource.Kit => HeroDomain.KitNumber(item.Entity) is { } number && number <= kitNumbers && item.Origin is null,
                SessionPlacedSource.Made or SessionPlacedSource.Created => SessionMadeItem.Number(item.Entity) is not null && item.Origin is null &&
                    (item.Source == SessionPlacedSource.Made || InventoryDomain.KitAdmitted(item.Kind.Type)),
                _ => item.Origin is { } origin && SessionValidation.Map(origin) && HeroDomain.KitNumber(item.Entity) is null &&
                    !item.Entity.StartsWith(SessionMadeItem.Prefix, StringComparison.Ordinal),
            };
            if (!sourceFits || !SessionValidation.Map(item.Map) || !DropCellFits(item.Cell) || destroyed.Contains(item.Entity) ||
                inventory.Taken.Contains(item.Entity)) return GameSessionFailure.InvalidSnapshot;
            inventory.Hero.AddPlaced(item);
            inventory.Taken.Add(item.Entity);
        }
        // ARC-WORLD-071: every given item is in an NPC's list, and a given kit item lies within the kit's numbers; a pile record names no
        // item of the hero's (a seed pile, or a made pile in an NPC's list).
        foreach (var item in inventory.Hero.GivenAll)
            if (inventory.NpcOf(item.Entity) is null || item.Origin is null && HeroDomain.KitNumber(item.Entity) > kitNumbers) return GameSessionFailure.InvalidSnapshot;
        foreach (var entity in inventory.Piles.Keys)
            if (SessionMadeItem.Number(entity) is not null && inventory.NpcOf(entity) is null || inventory.Hero.InPack(entity) ||
                inventory.PlacedItem(entity) is not null || inventory.NpcOf(entity) is { } holder && inventory.AddedTo(holder).First(item => item.Entity == entity).Kind.Type != SessionItemType.Gold)
                return GameSessionFailure.InvalidSnapshot;
        // A deleted item is hidden from every seed, from pickup and from loot, like a taken one.
        foreach (var entity in destroyed) inventory.Taken.Add(entity);
        // ARC-WORLD-090: the identified items by entity in ordinal order, within the bound, none deleted.
        if (saved.Identified.Count > SessionInventoryState.MaxIdentified) return GameSessionFailure.BudgetExceeded;
        string? lastIdentified = null;
        foreach (var entity in saved.Identified)
        {
            if (!SessionValidation.Entity(entity) || lastIdentified is not null && string.CompareOrdinal(lastIdentified, entity) >= 0 || destroyed.Contains(entity))
                return GameSessionFailure.InvalidSnapshot;
            lastIdentified = entity;
            inventory.Identified.Add(entity);
        }
        // ARC-WORLD-094: the highest discarded made number.
        if (saved.LastDiscardedMade < 0) return GameSessionFailure.InvalidSnapshot;
        inventory.LastDiscardedMade = saved.LastDiscardedMade;
        // ARC-WORLD-100: the generated-once containers by map and then entity in ordinal order, within the bound.
        if (saved.Generated.Count > SessionInventoryState.MaxGenerated) return GameSessionFailure.BudgetExceeded;
        SessionGeneratedContainer? lastGenerated = null;
        foreach (var row in saved.Generated)
        {
            if (row.Map.RuntimeMap < 1 || !SessionEventKinds.RestockHolder(row.Entity) || lastGenerated is { } before &&
                (before.Map.RuntimeMap > row.Map.RuntimeMap || before.Map == row.Map && string.CompareOrdinal(before.Entity, row.Entity) >= 0))
                return GameSessionFailure.InvalidSnapshot;
            lastGenerated = row;
            inventory.Generated.Add(row);
        }
        // ARC-WORLD-103: the dressed NPCs by entity in ordinal order, within the bound: an NPC (an object of a map), its worn items in
        // ascending slots, each item worn once and not deleted.
        if (saved.NpcWorn.Count > SessionInventoryState.MaxNpcWorn) return GameSessionFailure.BudgetExceeded;
        string? lastDressed = null;
        var wornItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var npc in saved.NpcWorn)
        {
            if (npc?.Worn is null || !SessionEventKinds.RestockHolder(npc.Npc) || lastDressed is not null && string.CompareOrdinal(lastDressed, npc.Npc) >= 0)
                return GameSessionFailure.InvalidSnapshot;
            lastDressed = npc.Npc;
            var record = new SortedDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, string>();
            foreach (var worn in npc.Worn)
                if (!Enum.IsDefined(worn.Slot) || !SessionValidation.Entity(worn.Item) || destroyed.Contains(worn.Item) || !wornItems.Add(worn.Item) ||
                    !record.TryAdd(worn.Slot, worn.Item) || record.Keys.Last() != worn.Slot) return GameSessionFailure.InvalidSnapshot;
            inventory.NpcWorn.Add(npc.Npc, record);
        }
        // ARC-SCRIPT-042: the kept item script slots by entity in ordinal order and then by point, within the bound; a slot of an object
        // point (not the dialog-override one) of an item that was not deleted, with a script number 0..99999.
        if (saved.ScriptSlots.Count > SessionItemScripts.MaxStoredSlots) return GameSessionFailure.BudgetExceeded;
        SessionItemScriptSlot? lastSlot = null;
        foreach (var slot in saved.ScriptSlots)
        {
            if (slot is null || !SessionValidation.Entity(slot.Entity) || slot.Point is < 0 or >= ScrPoint.Count || slot.Point == ScrPoint.DialogOverride ||
                slot.Script is < 0 or > Arcanum.Data.Scripts.ScrCatalogue.MaxScriptNumber || destroyed.Contains(slot.Entity) || lastSlot is not null &&
                (string.CompareOrdinal(lastSlot.Entity, slot.Entity) is var order && (order > 0 || order == 0 && lastSlot.Point >= slot.Point)))
                return GameSessionFailure.InvalidSnapshot;
            lastSlot = slot;
            inventory.ScriptSlots[(slot.Entity, slot.Point)] = new(slot.Script, slot.LocalFlags, slot.Counters);
        }
        // ARC-WORLD-066: the ammunition stacks by entity in ordinal order, within the budget, each of a type 0..3 with 0 or more units and
        // not deleted; every ammunition item the hero holds or placed, and every one added to an NPC, has its stack; a stack of the
        // hero's, the NPC store's or a placed item is of an ammunition item.
        if (saved.Ammo.Count > limits.MaxAmmoRecords) return GameSessionFailure.BudgetExceeded;
        string? last = null;
        foreach (var stack in saved.Ammo)
        {
            if (!SessionValidation.Entity(stack.Entity) || last is not null && string.CompareOrdinal(last, stack.Entity) >= 0 ||
                stack.AmmoType is < 0 or >= SessionAmmoRecord.AmmoTypes || stack.Units < 0 || destroyed.Contains(stack.Entity))
                return GameSessionFailure.InvalidSnapshot;
            last = stack.Entity;
            inventory.Ammo[stack.Entity] = stack;
        }
        var tracked = inventory.Pc.Select(item => (item.Entity, item.Kind)).Concat(inventory.Made.Select(item => (item.Entity, item.Kind)))
            .Concat(inventory.Placed.Select(item => (item.Entity, item.Kind)))
            .Concat(inventory.Lists.All.Select(item => (item.Entity, item.Kind)));
        foreach (var (entity, kind) in tracked)
            if ((kind.Type == SessionItemType.Ammo) != inventory.Ammo.ContainsKey(entity)) return GameSessionFailure.InvalidSnapshot;
        // ARC-WORLD-085: the weight rows by entity in ordinal order, each of an object the inventory holds (the hero's, a placed one or
        // one in an NPC's added list) with valid facts and a non-zero weight or adjustment.
        string? lastWeight = null;
        foreach (var row in saved.Weights)
        {
            if (!SessionValidation.Entity(row.Entity) || lastWeight is not null && string.CompareOrdinal(lastWeight, row.Entity) >= 0 || !row.Facts.IsValid ||
                row.Facts.Weight == 0 && row.Facts.MagicWeight == 0 || destroyed.Contains(row.Entity) || !inventory.Hero.InPack(row.Entity) &&
                inventory.PlacedItem(row.Entity) is null && inventory.NpcOf(row.Entity) is null)
                return GameSessionFailure.InvalidSnapshot;
            lastWeight = row.Entity;
            inventory.Weights[row.Entity] = row.Facts;
        }
        return GameSessionFailure.None;
    }

    // ARC-DEV-084 (TM1): the high-water marks of the junk and pool numbers, after the piles and pools are in: a mark never sits below
    // a number a saved pile or pool holds (the older schema's load had no mark, so its piles and pools set it).
    private static GameSessionFailure ImportObjectMarks(SessionObjectRecords saved, InventoryDomain inventory)
    {
        if (saved.LastJunkNumber < 0 || saved.LastPoolNumber < 0) return GameSessionFailure.InvalidSnapshot;
        inventory.Objects.LastJunkNumber = Math.Max(saved.LastJunkNumber, inventory.Objects.Piles.Select(pile => SessionJunkPile.Number(pile.Entity) ?? 0).DefaultIfEmpty(0).Max());
        inventory.Objects.LastPoolNumber = Math.Max(saved.LastPoolNumber, inventory.Objects.Pools.Select(pool => SessionBloodPool.Number(pool.Entity) ?? 0).DefaultIfEmpty(0).Max());
        return GameSessionFailure.None;
    }

    // ARC-WORLD-063: the junk piles within the bounds, each a pile entity (junk:n) once, on a valid map and cell, its items once in all
    // piles with rectangles inside the grid that do not overlap; a placed item in a pile lies on the pile's map and cell, any other
    // item in a pile is no item of the hero's, of an NPC's added list or deleted (a seed item, checked when its seed attaches).
    private static GameSessionFailure RestoreJunkPiles(InventoryDomain inventory, IReadOnlyList<SessionJunkPile> saved, IReadOnlySet<string> destroyed)
    {
        if (saved.Count > SessionJunkPile.MaxPiles || saved.Sum(pile => (long)(pile?.Items?.Count ?? 0)) > SessionJunkPile.MaxItems) return GameSessionFailure.BudgetExceeded;
        var piles = new HashSet<string>(StringComparer.Ordinal);
        var items = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pile in saved)
        {
            if (pile?.Items is null || SessionJunkPile.Number(pile.Entity) is null || !piles.Add(pile.Entity) || !SessionValidation.Map(pile.Map) ||
                !DropCellFits(pile.Cell)) return GameSessionFailure.InvalidSnapshot;
            foreach (var item in pile.Items)
            {
                if (item is null || !SessionValidation.Entity(item.Entity) || !items.Add(item.Entity) || piles.Contains(item.Entity) ||
                    !InventoryGridRules.Fits(item.Grid, InventoryGridRules.ContainerRows) || destroyed.Contains(item.Entity))
                    return GameSessionFailure.InvalidSnapshot;
                var held = inventory.PlacedItem(item.Entity) is { } placed ? placed.Map != pile.Map || placed.Cell != pile.Cell
                    : inventory.Taken.Contains(item.Entity) || inventory.NpcOf(item.Entity) is not null;
                if (held) return GameSessionFailure.InvalidSnapshot;
            }
            // Rectangles inside the grid (checked above) overlap when they cover fewer cells than their areas add up to.
            if (pile.Items.Sum(item => (long)item.Width * item.Height) !=
                InventoryGridRules.Occupied(pile.Items.Select(item => item.Grid), InventoryGridRules.ContainerRows).Count(cell => cell))
                return GameSessionFailure.InvalidSnapshot;
            inventory.Objects.AddPile(pile with { Items = Array.AsReadOnly(pile.Items.ToArray()) });
        }
        return GameSessionFailure.None;
    }

    // Every life call: target, input and version checks, then the step on a trial copy of the generator, then NoChange (the
    // trial's draws are adopted, as the source spends them), then the record budget and revision capacity. A refusal never
    // adopts the trial copy, so it draws nothing.
    private SessionLifeResult Life(GameSessionVersion expected, SessionLifeTarget target, bool validInput,
        Func<LifeState, LifeFacts, ICombatRollSource, LifeOutcome> step,
        Func<LifeState, LifeState, IEnumerable<LifeTimer>>? handlerTimers = null, int eventMap = 0, int recoveryCopies = 1,
        (LifeDamage Damage, SessionDamageParties Parties)? damageStep = null)
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
        var itemEvents = LifeItemEvents(target, facts!, outcome, damageStep);
        if (!changed) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, outcome, new(_identity, _revision)) { ItemEvents = itemEvents };
        if (target.IsHero) _life.Hero = next;
        else _life.Critters[target.Entity!] = next;
        EndSubturnOnIncapacitation(target, outcome);
        if (outcome.Events.HasFlag(LifeEvents.Died)) LeaveCombatModeOnDeath(target);
        // OL25: the kill notification unwaits the NPC, which removes its wait timer.
        if (outcome.Events.HasFlag(LifeEvents.Died) && !target.IsHero) CancelWaitTimers(target.Entity!);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, outcome, new(_identity, _revision)) { ItemEvents = itemEvents };
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

    // The maps other than this one where the NPC has pending timers (none while the one-map rule holds); its wait timer
    // (ARC-WORLD-045) counts too.
    private int[] StrayMaps(string entity, int map)
    {
        var maps = new SortedSet<int>();
        foreach (var kind in NpcKinds.Append(SessionEventKinds.PartyWait))
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
    private static GameSessionFailure ImportEvents(SessionEventState saved, SessionHeroState? heroState, LifeDomain life, PartyDomain party,
        SessionMapChanges mapChanges, long clockMs, int actorMap,
        SessionEventSink sink, GameEventQueueProfile profile, out GameEventQueue? events)
    {
        events = null;
        var hero = heroState is not null;
        var registry = SessionEventKinds.Build(sink);
        var converted = ImmutableArray.CreateBuilder<GameEvent>(saved.Pending.Count);
        var maps = new Dictionary<string, int>(StringComparer.Ordinal);
        var decaying = new HashSet<string>(StringComparer.Ordinal);
        var restocking = new HashSet<GameEventEntity>();
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
            if (pending.Kind == SessionEventKinds.HeroEncumbrance)
            {
                // The hero's encumbrance timer (ARC-WORLD-087): set no later than the saved clock, one of the drain intervals before its
                // deadline (the registry keeps one). The level it found is read when it fires.
                if (heroState is null || !pending.Entities.IsEmpty || pending.Values[0] < 0 || pending.Values[0] > clockMs ||
                    !Enumerable.Range(0, CarryRules.Levels).Any(level => CarryRules.DrainIntervalMs(level) == pending.DeadlineMs - pending.Values[0]))
                    return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, 0, new(pending.Entities, pending.Values)));
                continue;
            }
            if (pending.Kind == SessionEventKinds.LockReset)
            {
                // A Lock event (ARC-WORLD-050): one object whose lock state the saved map changes record, set no later than
                // the saved clock, an hour (unlock) or a day (jam) before its deadline.
                if (pending.Entities.Length != 1 || pending.Entities[0].Code == SessionEventKinds.AbsentRole ||
                    RecordedIn(mapChanges, new(new(pending.Entities[0].Map), pending.Entities[0].Code)) is null ||
                    pending.Values[0] < 0 || pending.Values[0] > clockMs ||
                    (pending.DeadlineMs - pending.Values[0]) is not (Arcanum.Simulation.Locks.LockRules.UnlockResetDelayMs or Arcanum.Simulation.Locks.LockRules.JamResetDelayMs))
                    return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, pending.Entities[0].Map, new(pending.Entities, pending.Values)));
                continue;
            }
            if (pending.Kind == SessionEventKinds.AiTick)
            {
                // An AI tick (ARC-WORLD-084): one NPC of the actor's map, set no later than the saved clock, its delay before its
                // deadline, one per NPC (the queue's key check). Whether the NPC is still there is asked when it fires.
                if (pending.Entities.Length != 1 || pending.Entities[0].Map != actorMap || !SessionValidation.Entity(pending.Entities[0].Code) ||
                    !SessionEventKinds.AiTickPayload(new(pending.Entities, pending.Values)) || pending.Values[0] > clockMs ||
                    pending.DeadlineMs != pending.Values[0] + pending.Values[1])
                    return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, pending.Entities[0].Map, new(pending.Entities, pending.Values)));
                continue;
            }
            if (pending.Kind == SessionEventKinds.ItemDecay)
            {
                // An Item Decay event (ARC-WORLD-062): one item on a map, set no later than the saved clock, the decay delay or the
                // retry's before its deadline, one per item. Whether the item still lies there is asked when it fires (seeds are host
                // data the restore does not have).
                if (pending.Entities.Length != 1 || pending.Entities[0].Map < 1 || !SessionValidation.Entity(pending.Entities[0].Code) ||
                    !SessionEventKinds.DecayPayload(new(pending.Entities, pending.Values)) || pending.Values[0] > clockMs ||
                    pending.DeadlineMs != pending.Values[0] + pending.Values[1] || !decaying.Add(pending.Entities[0].Code))
                    return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, pending.Entities[0].Map, new(pending.Entities, pending.Values)));
                continue;
            }
            if (pending.Kind == SessionEventKinds.Restock)
            {
                // A restock event (ARC-WORLD-094): one NPC or container on a map, set no later than the saved clock, a restock delay or
                // the retry's before its deadline, one per object. Whether the object is still there is asked when it fires.
                if (pending.Entities.Length != 1 || pending.Entities[0].Map < 1 || !SessionEventKinds.RestockPayload(new(pending.Entities, pending.Values)) ||
                    pending.Values[0] > clockMs || pending.DeadlineMs != pending.Values[0] + pending.Values[1] || !restocking.Add(pending.Entities[0]))
                    return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, pending.Entities[0].Map, new(pending.Entities, pending.Values)));
                continue;
            }
            if (pending.Kind == SessionEventKinds.ObjectRespawn)
            {
                // A respawn event (ARC-COMBAT-008): one scenery the saved map changes record switched off, set no later than the
                // saved clock, its delay before its deadline.
                if (pending.Entities.Length != 1 || pending.Entities[0].Code == SessionEventKinds.AbsentRole ||
                    RecordedObject(mapChanges, new(pending.Entities[0].Map), pending.Entities[0].Code) is not { Condition: SessionObjectCondition.Off } ||
                    pending.Values[0] < 0 || pending.Values[0] > clockMs || pending.Values[1] is < 1 or > SessionEventKinds.MaxRespawnDelayMs ||
                    pending.DeadlineMs != pending.Values[0] + pending.Values[1])
                    return GameSessionFailure.InvalidEvents;
                converted.Add(new(pending.Sequence, pending.Kind, kind.Domain, pending.DeadlineMs, pending.Entities[0].Map, new(pending.Entities, pending.Values)));
                continue;
            }
            if (pending.Kind == SessionEventKinds.PartyWait)
            {
                // A wait timer (ARC-WORLD-045): one NPC the saved party records waiting, set no later than the saved clock, its
                // duration of the wait table (0 stored as 1 ms) before its deadline, any charisma; one per NPC.
                if (pending.Entities.Length != 1 || pending.Entities[0].Map < 1 || party.Get(pending.Entities[0].Code).Standing != CompanionStanding.Waiting ||
                    pending.Values[0] < 0 || pending.Values[0] > clockMs || !CompanionLoyaltyRules.WaitDurationsMs.Contains(pending.Values[1]) ||
                    pending.DeadlineMs != pending.Values[0] + Math.Max(pending.Values[1], 1) || pending.Values[2] is < int.MinValue or > int.MaxValue ||
                    converted.Any(e => e.Kind == SessionEventKinds.PartyWait && SessionEventKinds.NamesNpc(e, pending.Entities[0].Code)) ||
                    // One map per NPC's timers, as for the life timers.
                    (!maps.TryAdd(pending.Entities[0].Code, pending.Entities[0].Map) && maps[pending.Entities[0].Code] != pending.Entities[0].Map))
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
        if (converted.Count(e => e.Kind == SessionEventKinds.ScriptCall) > SessionEventKinds.MaxScriptCalls ||
            converted.Count(e => e.Kind == SessionEventKinds.AiTick) > SessionEventKinds.MaxAiTicks ||
            converted.Count(e => e.Kind == SessionEventKinds.HeroEffectEnd) > SessionEffectLimits.MaxTimedEffects ||
            converted.Count(e => e.Kind == SessionEventKinds.LockReset) > SessionEventKinds.MaxLockResets ||
            converted.Count(e => e.Kind == SessionEventKinds.ObjectRespawn) > SessionEventKinds.MaxObjectRespawns ||
            converted.Count(e => e.Kind == SessionEventKinds.ItemDecay) > SessionEventKinds.MaxItemDecays ||
            converted.Count(e => e.Kind == SessionEventKinds.Restock) > SessionEventKinds.MaxRestocks) return GameSessionFailure.EventBudgetExceeded;
        var result = GameEventQueue.Restore(registry, new(saved.Registry, saved.NextSequence, converted.ToImmutable()), profile,
            SessionEventKinds.Limits);
        if (result.Queue is null)
            return result.Failure == GameEventFailure.QueueFull ? GameSessionFailure.EventBudgetExceeded : GameSessionFailure.InvalidEvents;
        // OQ2's Original setting rebuilds the AI ticks as the original's load does when it folds the sectors in: each NPC's tick
        // becomes a wake-up tick 1 ms after the restored clock (ARC-WORLD-084), in snapshot order. They only replace what was
        // dropped, so they fit.
        foreach (var dropped in result.Rebuild)
            if (result.Queue.ScheduleAfter(SessionEventKinds.AiTick, GameEventPayload.Of(dropped.Payload.Entities, clockMs, 1, 1), 1,
                    new(0, clockMs, 0)).Disposition != GameEventScheduleDisposition.Added)
                return GameSessionFailure.InvalidEvents;
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

    private bool IsCurrent(GameSessionVersion expected) =>
        ReferenceEquals(expected.Owner, _identity) && expected.Revision == _revision;

    private GameSessionMutationResult Changed()
    {
        Revise();
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
    internal HashSet<int> QuelledRumors { get; } = [];
    internal HashSet<int> KnownAreas { get; } = [];
    internal int LastArea { get; set; }
    /// <summary>ARC-WORLD-044: the PC's reputations in acquisition order.</summary>
    internal List<SessionReputation> Reputations { get; } = [];
    /// <summary>ARC-SCRIPT-019: the story-state number (SS1).</summary>
    internal int StoryState { get; set; }

    internal SessionStoryState Capture() => new(
        Quests.Values.OrderBy(quest => quest.Quest).ToArray(),
        GlobalQuests.OrderBy(pair => pair.Key).Select(pair => new SessionGlobalQuest(pair.Key, pair.Value)).ToArray(),
        Rumors.OrderBy(pair => pair.Key).Select(pair => new SessionRumor(pair.Key, pair.Value)).ToArray(),
        QuelledRumors.Order().ToArray(), KnownAreas.Order().ToArray(), LastArea,
        Reputations.ToArray(), StoryState);
}
