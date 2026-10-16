using Arcanum.Scripting.GlobalFlags;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Party;

namespace Arcanum.Session;

/// <summary>
/// Single owner of one game session: the clock, global flags, actor placement, map-local changes and the
/// story domain (PC quests, shared quest states, known rumors), the PC's party (followers, waiting NPCs) and the items
/// the PC took from the world (pickup, loot, negative dialog IN) over host-attached T0 map inventories.
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
    private readonly PartyDomain _party;
    private readonly InventoryDomain _inventory;
    private readonly GameSessionOwnerIdentity _identity = new();
    private ActorPlacement _actor;
    private SessionLightingState? _lighting;
    private long _revision;
    private int _totalChanges;
    private bool _disposed;

    private GameSessionOwner(GameClockOwner clock, GlobalFlagOwner flags, GameSessionLimits limits,
        ActorPlacement actor, long revision, Dictionary<SessionMapId, Dictionary<MapChangeKey, long>> maps, Story story,
        PartyDomain party, InventoryDomain inventory, SessionLightingState? lighting)
    {
        _clock = clock; _flags = flags; _flagView = new(flags); _limits = limits; _actor = actor; _revision = revision; _maps = maps;
        _story = story; _party = party; _inventory = inventory; _lighting = lighting;
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
        return new(new(clock.Owner, flags.Owner, limits, descriptor.Actor, 0, [], new(), new(), new(), null),
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
        failure = ImportStory(snapshot.Story, snapshot.Clock.GameTimestampMs, out var story);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportParty(snapshot.Party, limits, out var party);
        if (failure != GameSessionFailure.None) return Rejected(failure);
        failure = ImportInventory(snapshot.Inventory, limits, out var inventory);
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
        return new(new(clock.Owner, flags.Owner, limits, snapshot.Actor, snapshot.Revision, maps, story, party, inventory, snapshot.Lighting),
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
    /// with its saved kind, and no item the PC took from another map may be in it, else
    /// <see cref="GameSessionFailure.InventoryMismatch"/> (a save of other data). A seed is not session state: like recorded
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
        if (!_inventory.Seeds.ContainsKey(map) && _inventory.Seeds.Count >= _limits.MaxMaps) return Reject(GameSessionFailure.BudgetExceeded);
        _inventory.Seeds[map] = admitted;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, new(_identity, _revision));
    }

    /// <summary>The current inventory of an attached map (its seed without the items the PC took), or null when no seed
    /// is attached.</summary>
    public SessionMapInventory? QueryMapInventory(SessionMapId map)
    {
        ThrowIfDisposed();
        return _inventory.Seeds.TryGetValue(map, out var seed) ? _inventory.View(map, seed) : null;
    }

    /// <summary>Detached copy of the items the PC took from the world, in the order they entered its list. The PC's own
    /// starting items are not session state yet.</summary>
    public IReadOnlyList<SessionPcItem> QueryPcInventory() { ThrowIfDisposed(); return Array.AsReadOnly(_inventory.Pc.ToArray()); }

    /// <summary>
    /// Ground pickup (CE <c>AG_PICKUP_ITEM</c> → <c>item_transfer</c>): an item lying on the actor's map goes to the end of
    /// the PC's list. Reach, the walk to the item and the combat action-point cost belong to the host. Refused before any
    /// effect: an item with an item <c>SAP_GET</c> or <c>SAP_INSERT_ITEM</c> script (not run here), a stack or key type
    /// (stack merge and key rings are not modelled), and a refusal of <paramref name="insertCheck"/>, which runs last.
    /// </summary>
    public InventoryMutationResult PickUpItem(GameSessionVersion expected, string item, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
        if (!seed.Items.TryGetValue(item, out var row) || row.Cell is null || _inventory.Taken.Contains(item))
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        return Take(new(item, row.Facts), false, insertCheck);
    }

    /// <summary>
    /// Loot (a click on a dead critter opens it as a container; the moved item runs <c>item_transfer</c> to the PC): an item
    /// still in the list of a dead NPC on the actor's map goes to the end of the PC's list. Only an NPC the seed marks dead
    /// is lootable here: chests (generated on first open) are not. Refused before any effect, beyond the item rules of
    /// <see cref="PickUpItem"/>: a holder with a <c>SAP_USE</c> script, an item in a worn slot (unequip effects), and an item
    /// <c>SAP_REMOVE_ITEM</c> script. The PC's own <c>SAP_TRANSFER</c> attachment is not modelled (the session has no PC
    /// object).
    /// </summary>
    public InventoryMutationResult LootItem(GameSessionVersion expected, string holder, string item, SessionInsertCheck insertCheck)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(insertCheck);
        if (!SessionValidation.Entity(holder) || !SessionValidation.Entity(item)) return RejectItem(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return RejectItem(GameSessionFailure.StaleVersion);
        if (!_inventory.Seeds.TryGetValue(_actor.Map, out var seed)) return RejectItem(GameSessionFailure.InventoryUnavailable);
        if (!seed.Holders.TryGetValue(holder, out var row)) return RejectItem(GameSessionFailure.UnknownHolder);
        if (row.Kind != SessionHolderKind.Npc || !row.Dead) return RejectItem(GameSessionFailure.HolderNotLootable);
        if (row.ScriptPoints.Contains(SessionScriptPoint.Use)) return RejectItem(GameSessionFailure.HolderScriptUnsupported);
        if (!seed.HolderOf.TryGetValue(item, out var of) || of != holder || _inventory.Taken.Contains(item))
            return RejectItem(GameSessionFailure.ItemNotAvailable);
        return Take(new(item, seed.Items[item].Facts), true, insertCheck);
    }

    /// <summary>
    /// Negative dialog <c>IN -name</c> (CE <c>DIALOG_ACTION_IN</c>, ARC-RES-021): the first item in the NPC's current list
    /// whose name class is <paramref name="name"/> goes to the end of the PC's list, under the item rules of
    /// <see cref="LootItem"/>. No such item and no substitute inventory changes nothing, as in the source; an NPC with a
    /// substitute inventory then is <see cref="GameSessionFailure.SubstituteInventoryUnsupported"/>. The source drops an item
    /// the PC cannot take at the PC's location (into a junk pile when items lie there); that fallback is not modelled, so a
    /// refusal of <paramref name="insertCheck"/> is <see cref="GameSessionFailure.InsertRefused"/> with no effect.
    /// </summary>
    public InventoryMutationResult GiveItemToPc(GameSessionVersion expected, string npc, int name, SessionInsertCheck insertCheck)
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
        return Take(found, true, insertCheck);
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
            new(flags.Version.Revision, flags.Entries.ToArray()), _actor, maps, _story.Capture(), _party.Capture(), _inventory.Capture(), _lighting);
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

    // Pickup, loot and IN share the item rules and the commit: every refusal precedes the insert check, which runs last.
    private InventoryMutationResult Take(SessionInventoryItem item, bool parented, SessionInsertCheck insertCheck)
    {
        var facts = item.Facts;
        if (!InventoryDomain.Admitted(facts.Kind.Type)) return RejectItem(GameSessionFailure.ItemTypeUnsupported);
        if (parented && facts.Worn) return RejectItem(GameSessionFailure.WornItemUnsupported);
        if (facts.HasScript(SessionScriptPoint.InsertItem) ||
            facts.HasScript(parented ? SessionScriptPoint.RemoveItem : SessionScriptPoint.Get)) return RejectItem(GameSessionFailure.ItemScriptUnsupported);
        if (_inventory.Pc.Count >= _limits.MaxPcItems) return RejectItem(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return RejectItem(GameSessionFailure.RevisionExhausted);
        var verdict = insertCheck(item);
        // Any answer but Accepted refuses, an undefined one included.
        if (verdict != SessionInsertVerdict.Accepted) return RejectItem(GameSessionFailure.InsertRefused, verdict);
        _inventory.Add(new(item.Entity, _actor.Map, facts.Kind));
        _revision++;
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, item.Entity, verdict, new(_identity, _revision));
    }

    private InventoryMutationResult RejectItem(GameSessionFailure failure, SessionInsertVerdict? verdict = null) =>
        new(GameSessionDisposition.Rejected, failure, null, verdict, new(_identity, _revision));

    // The PC's items keep their saved order; each must be one a transfer can produce (an admitted type from a valid map).
    private static GameSessionFailure ImportInventory(SessionInventoryState saved, GameSessionLimits limits, out InventoryDomain inventory)
    {
        inventory = new();
        if (saved.Pc.Count > limits.MaxPcItems) return GameSessionFailure.BudgetExceeded;
        foreach (var item in saved.Pc)
        {
            if (!SessionValidation.Entity(item.Entity) || !SessionValidation.Map(item.Origin) || !Enum.IsDefined(item.Kind.Type) ||
                !InventoryDomain.Admitted(item.Kind.Type)) return GameSessionFailure.InvalidItem;
            if (inventory.Taken.Contains(item.Entity)) return GameSessionFailure.InvalidSnapshot;
            inventory.Add(item);
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
