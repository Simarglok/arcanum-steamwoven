using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Residency;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-DEV-114 (MIG11 slice S3, RL21, RL24, ledger CP10): the request of a mover that is not the hero: a non-player object sent to another
    /// place (an NPC cast on by a teleport spell, a waypoint, a script). The original's one request slot takes it as it takes the hero's, but
    /// the request starts no fade and blanks no screen for it, any pending request that is not the local player's is overwritten
    /// (<see cref="ChangeOfPlaceSubmitStatus.Overwrote"/> returns the lost one) and the request refuses only while the hero's own is pending. The mover is an
    /// object of the open map (the original holds no live object of another map), so a destination on another map sends it away from the hero.
    /// A teleport spell's or a landing's caller empties the sector cache first (<see cref="ChangeOfPlaceRules.EmptiesCacheBeforeRequest"/>).
    /// Writes the slot only: no revision.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="mover"/> is not the entity of a map NPC (the hero has <see cref="RequestHeroChangeOfPlace"/>).</exception>
    public ChangeOfPlaceSubmission RequestChangeOfPlace(string mover, ChangeOfPlaceCaller caller, int destinationMap, CellCoord destination,
        ChangeOfPlaceFlags flags, int advanceSeconds, Func<int, bool> destinationListed)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(destinationListed);
        if (!SessionEventKinds.AiTickNpc(mover) || ObjectOriginOf(mover) != SessionObjectOrigin.MapObject)
            throw new ArgumentException("The mover must be a map NPC.", nameof(mover));
        var request = new ChangeOfPlaceRequest(mover, caller, destinationMap, destination, flags, advanceSeconds, IsPlayerCharacter: false, IsLocalPlayer: false);
        var submission = ChangeOfPlaceRules.Submit(_changeOfPlace, request, new(_actor.Map.RuntimeMap, destinationListed));
        _changeOfPlace = submission.Slot;
        EmptyCacheAfterSubmit(request, submission);
        return submission;
    }

    /// <summary>
    /// ARC-DEV-114 (RL24, 0x4D0BC0 with argument 0): saves and unloads every sector of the open map's cache into the eviction history at the
    /// session's game time, the pass a teleport spell's helper and the world-map landing run before their request. The map stays open and nothing
    /// is held, so every later load of a sector folds as a fresh one (with the wake-up marker once it was away 3,000 ms, RL8). Derived data: no
    /// revision. The sectors evicted, ascending, or null when <paramref name="map"/> has no sector cache (not the open map, or a host that opened none).
    /// </summary>
    public IReadOnlyList<ResidentSector>? EmptySectorCache(SessionMapId map)
    {
        ThrowIfDisposed();
        if (Sectors(map) is not { } sectors) return null;
        var change = SectorCacheRules.EvictUnlocked(sectors.State, _clock.QueryClockSnapshot().GameTimestampMs);
        if (change.Failure != SectorCacheFailure.None) return null;
        // ARC-DEV-106 (RL19): the sectors unloaded leave the light state with them.
        _sectors = sectors with { State = change.State, LightHours = System.Collections.Immutable.ImmutableDictionary<long, int>.Empty };
        return change.Evicted.Select(Sector).ToArray();
    }

    // The callers that save and unload every unlocked sector before their request do it for a request the original's slot would see: one the project's
    // validation or its destination check refuses (CD1) never reaches it, so it empties nothing; one the hero's pending request refuses has emptied the
    // cache, as the helper empties first and requests after. Both happen in the one call of the request, so nothing runs between them.
    internal void EmptyCacheAfterSubmit(ChangeOfPlaceRequest request, ChangeOfPlaceSubmission submission)
    {
        if (!ChangeOfPlaceRules.EmptiesCacheBeforeRequest(request.Caller) ||
            submission.Status is ChangeOfPlaceSubmitStatus.Invalid or ChangeOfPlaceSubmitStatus.DestinationNotListed) return;
        EmptySectorCache(_actor.Map);
    }

    // The 3 × 3 pre-cache at the destination (RL24, CP6): one cell of each sector around the hero's, as the viewer's arrival touch lists them. Best
    // effort: the original's process has no failure path after the payload, so a refused touch (a full revision counter) leaves the sectors to the
    // view's own touch. Another map has no cache here (the hero's map change dropped it); its host opens one and touches it itself.
    private void PreCacheArrival()
    {
        if (Sectors(_actor.Map) is null) return;
        var centre = ResidentSector.Of(_actor.Cell);
        var cells = new List<CellCoord>(9);
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (centre.X + dx >= 0 && centre.Y + dy >= 0) cells.Add(new((centre.X + dx) << ResidentSector.Shift, (centre.Y + dy) << ResidentSector.Shift));
        TouchSectors(Version, _actor.Map, cells);
    }

    // What a non-player mover's departure to another map does besides its placement (ledger CP11, FM6, RL15, RL22).
    private sealed record NpcDeparture(string Entity, bool OtherMap, int DestinationMap);

    private sealed class DeparturePlan
    {
        internal required NpcDeparture Departure { get; init; }
        internal SessionDanger? Cleared { get; init; }
        internal bool LeavesCombat { get; init; }
        internal bool Drops { get; init; }
        internal required int[] From { get; init; }
        internal bool Events { get; init; }
        internal bool Changes => Cleared is not null || LeavesCombat || Events;
    }

    // The pending events that name the mover and nothing else (the ones the original's dispatcher drops with the destroyed copy, RL15): its life
    // timers, a wait timer, its AI tick, a restock timer, a deferred call with only it. An event that also names another object is left alone.
    private List<GameEvent> OwnEvents(string entity) =>
        _events.QueryPending().Where(e => e.Payload.Entities.Length > 0 && e.Payload.Entities.All(x => x.Code == entity)).ToList();

    private DeparturePlan PlanDeparture(NpcDeparture departure)
    {
        var entity = departure.Entity;
        // FM6, both cases: focus, who hit me last, the enemy list and fleeing-from are cleared; the state bits stay.
        var record = _danger.Get(entity);
        var cleared = record.Focus is null && record.FleeingFrom is null && record.HitBy is null && record.Enemies.Count == 0
            ? null : record with { Focus = null, FleeingFrom = null, HitBy = null, Enemies = [] };
        if (!departure.OtherMap) return new() { Departure = departure, Cleared = cleared, From = [] };
        var drops = _rules.Lifecycle.NonPlayerMoveDropsEvents;
        var own = OwnEvents(entity);
        // Steamwoven: every such event moves with the NPC (spell runs included, which follow their caster); the Original setting drops them all but the
        // spell runs, whose table the session would be left holding without their events (CD12).
        var from = drops ? Array.Empty<int>() : own.Where(e => e.Kind != SessionEventKinds.AiTick)
            .SelectMany(e => e.Payload.Entities.Select(x => x.Map)).Where(map => map != departure.DestinationMap).Distinct().Order().ToArray();
        return new()
        {
            Departure = departure,
            Cleared = cleared,
            // Cross-map only: combat mode is left (0x4B3F20) and the AI events are removed (0x4AD7D0).
            LeavesCombat = _combat.Critters.Contains(entity),
            Drops = drops,
            From = from,
            Events = own.Any(e => e.Kind == SessionEventKinds.AiTick) || (drops ? own.Any(e => e.Kind != SessionEventKinds.SpellRun) : from.Length > 0),
        };
    }

    // The queue's part of the departure: the AI tick goes, the NPC's own events die or follow it. Refused inside an event pass whatever the setting, as the
    // hero's change is; atomic as far as the queue can refuse: the move comes first, and one NPC's events lie on one map while the session's rule holds.
    private GameSessionFailure DepartEvents(DeparturePlan plan)
    {
        var entity = plan.Departure.Entity;
        if (!plan.Departure.OtherMap) return GameSessionFailure.None;
        if (_passWorld is not null) return GameSessionFailure.InvalidEventPass;
        foreach (var from in plan.From)
        {
            var moved = _events.Relocate(from, plan.Departure.DestinationMap, e => e.Code == entity).Failure;
            if (moved == GameEventFailure.PassInProgress) return GameSessionFailure.InvalidEventPass;
            if (moved != GameEventFailure.None) return GameSessionFailure.EventConflict;
        }
        _events.RemoveAll(SessionEventKinds.AiTick, e => SessionEventKinds.NamesNpc(e, entity));
        // ARC-DEV-132: its goal runs stay with the map it leaves, so they end (both settings).
        KillDepartingGoals(entity);
        if (plan.Drops)
            foreach (var kind in OwnEvents(entity).Select(e => e.Kind).Where(kind => kind != SessionEventKinds.SpellRun).Distinct().ToArray())
                _events.RemoveAll(kind, e => e.Payload.Entities.All(x => x.Code == entity));
        return GameSessionFailure.None;
    }

    private void CommitDeparture(DeparturePlan plan)
    {
        if (plan.Cleared is not null) _danger.Put(plan.Cleared);
        var entity = plan.Departure.Entity;
        if (plan.LeavesCombat && _combat.Critters.Remove(entity)) UnlockOnCombatModeExit(entity);
    }

    // A non-player mover's payload (CP4 to CP6 without a player character): the host's placement edits, the danger clearing and, on another map,
    // the departure's event handling, in one revision. The destination map is not opened and the hero stays where it is.
    private GameSessionFailure MoverPayload(ChangeInFlight flight, IChangeOfPlaceHost host, SessionMapId destination)
    {
        var request = flight.Request;
        if (host.Arrive(request, destination, flight.MoveList ?? SessionMoveList.Empty) is not { Changes: not null } arrival) return GameSessionFailure.ChangeOfPlaceStopped;
        var departure = new NpcDeparture(request.Entity, destination != _actor.Map, destination.RuntimeMap);
        var placed = ApplyChangesCore(Version, null, arrival.Changes, null, departure);
        return placed.Disposition == GameSessionDisposition.Rejected ? placed.Failure : GameSessionFailure.None;
    }
}
