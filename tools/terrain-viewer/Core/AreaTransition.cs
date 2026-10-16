using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One loaded map: its strict Area snapshot, its step evidence and its chart.</summary>
public sealed record AreaMapRuntime(AreaSnapshot Snapshot, AreaStepSnapshot Steps, AreaChart Chart)
{
    public int RuntimeMap => Snapshot.RuntimeMap;
}

public enum AreaUseOutcome
{
    Accepted, NotATrigger, TooFar, TriggerTileBlocked, StepBlocked, StepUnknown, TransitionPending, DestinationUnavailable,
}

/// <summary>The answer to one use of an object. <see cref="Distance"/> is the Chebyshev distance to it.</summary>
public sealed record AreaUseResult(AreaUseOutcome Outcome, AreaEndpoint? Endpoint, int Distance, string Reason);

/// <summary>A processed cross-map transfer: the endpoint, both runtime maps and the exact arrival.</summary>
public sealed record AreaTransition(AreaEndpoint Endpoint, int FromMap, int ToMap, AreaStepCell Arrival, CellCoord ArrivalChartCell);

/// <summary>
/// ARC-WORLD-009 transition rule (docs/research/cave-transition-evidence.md T3, T4): a use of an endpoint's
/// trigger object is accepted within Chebyshev distance 2 when the object's tile is not terrain-blocking and
/// one step from the actor towards the object is not blocked by anything but the object itself. An accepted
/// use is queued; <see cref="Process"/> applies it once, at the caller's next tick, and every request while one
/// is pending is refused. The arrival is the authored cell, never inferred; a map without the destination or
/// an arrival outside its admitted area makes the endpoint unavailable. Caller-serialized; no I/O.
/// </summary>
public sealed class AreaTransitionCoordinator
{
    public const int UseRange = 2;
    private readonly IReadOnlyDictionary<int, AreaMapRuntime> _maps;
    private readonly GameSessionOwner? _session;

    public AreaTransitionCoordinator(IEnumerable<AreaMapRuntime> maps, int currentMap, GameSessionOwner? session = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        var map = new Dictionary<int, AreaMapRuntime>();
        foreach (var runtime in maps)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (runtime.Steps.InputIdentity.AreaProfile != runtime.Snapshot.Profile || !map.TryAdd(runtime.RuntimeMap, runtime))
                throw new ArgumentException("AREA_TRANSITION_MAPS_INVALID");
        }
        if (!map.ContainsKey(currentMap)) throw new ArgumentException("AREA_TRANSITION_CURRENT_MAP_REQUIRED");
        _maps = map; CurrentMap = currentMap; _session = session;
    }

    public int CurrentMap { get; private set; }
    public AreaMapRuntime Current => _maps[CurrentMap];
    public AreaTransition? Pending { get; private set; }
    public IReadOnlyCollection<int> Maps => _maps.Keys.ToArray();
    public AreaMapRuntime Map(int runtimeMap) => _maps[runtimeMap];

    /// <summary>The endpoints of the current map whose trigger objects are loaded, with their object cells.</summary>
    public IReadOnlyList<(AreaEndpoint Endpoint, AreaObjectRecord Trigger)> Triggers() => Current.Snapshot.Endpoints
        .Select(e => (e, Current.Snapshot.Objects.SingleOrDefault(o => IsTrigger(e, o.Key))))
        .Where(p => p.Item2 is not null).Select(p => (p.e, p.Item2!)).ToArray();

    public AreaUseResult RequestUse(AreaObjectKey trigger, AreaStepCell actor)
    {
        if (Pending is not null) return new(AreaUseOutcome.TransitionPending, Pending.Endpoint, -1, "TRANSITION_PENDING");
        var map = Current;
        var endpoint = map.Snapshot.Endpoints.SingleOrDefault(e => IsTrigger(e, trigger));
        var target = endpoint is null ? null : map.Snapshot.Objects.SingleOrDefault(o => o.Key == trigger);
        if (endpoint is null || target?.WorldX is not { } tx || target.WorldY is not { } ty)
            return new(AreaUseOutcome.NotATrigger, null, -1, "NO_ENDPOINT_FOR_OBJECT");
        var objectCell = new AreaStepCell(tx, ty);
        var dx = objectCell.X - actor.X; var dy = objectCell.Y - actor.Y;
        var distance = checked((int)Math.Max(Math.Abs(dx), Math.Abs(dy)));
        if (distance > UseRange) return new(AreaUseOutcome.TooFar, endpoint, distance, "OUTSIDE_USE_RANGE");
        // The object's own tile must not be terrain-blocking.
        if (map.Steps.Terrain(objectCell) is not { Coverage: CardinalTraversalCoverage.Supported } terrain)
            return new(AreaUseOutcome.StepUnknown, endpoint, distance, "TRIGGER_TILE_TERRAIN_UNKNOWN");
        if (terrain.Disposition == CardinalTraversalDisposition.Blocked) return new(AreaUseOutcome.TriggerTileBlocked, endpoint, distance, terrain.ReasonCode);
        if (distance > 0)
        {
            // One step towards the object, with the object itself ignored.
            var direction = Direction(Math.Sign(dx), Math.Sign(dy));
            var next = new AreaStepCell(actor.X + Math.Sign(dx), actor.Y + Math.Sign(dy));
            var step = map.Steps.Assess(map.Steps.Request(actor, next, direction));
            var others = step.Objects.Where(o => o.Key != trigger).ToArray();
            var blocked = step.DestinationTerrain?.Disposition == CardinalTraversalDisposition.Blocked || step.WallBlocker is not null ||
                others.Any(o => o.Disposition == CardinalTraversalDisposition.Blocked);
            if (blocked) return new(AreaUseOutcome.StepBlocked, endpoint, distance, step.ReasonCode);
            if (step.Coverage == CardinalTraversalCoverage.Unsupported || step.Gaps.Any(g => g.Object != trigger))
                return new(AreaUseOutcome.StepUnknown, endpoint, distance, step.ReasonCode);
        }
        if (!_maps.TryGetValue(endpoint.RuntimeDestination, out var destination))
            return new(AreaUseOutcome.DestinationUnavailable, endpoint, distance, "DESTINATION_MAP_NOT_LOADED");
        var arrival = new AreaStepCell(endpoint.ArrivalX, endpoint.ArrivalY);
        if (destination.Snapshot.CatalogueMap != endpoint.CatalogueDestination || !destination.Steps.IsAdmitted(arrival))
            return new(AreaUseOutcome.DestinationUnavailable, endpoint, distance, "ARRIVAL_NOT_ADMITTED");
        var chartCell = destination.Chart.ToChart(arrival);
        if (chartCell.X is < AreaGridSession.MinimumChartCell or > AreaGridSession.MaximumChartCell ||
            chartCell.Y is < AreaGridSession.MinimumChartCell or > AreaGridSession.MaximumChartCell)
            return new(AreaUseOutcome.DestinationUnavailable, endpoint, distance, "ARRIVAL_OUTSIDE_CHART");
        Pending = new(endpoint, CurrentMap, endpoint.RuntimeDestination, arrival, chartCell);
        return new(AreaUseOutcome.Accepted, endpoint, distance, "QUEUED");
    }

    /// <summary>Applies the pending transfer exactly once: the current map changes and the session (when
    /// present) records the actor on the destination map at the authored arrival.</summary>
    public AreaTransition? Process(NeighborDirection facing = NeighborDirection.XPlus)
    {
        if (Pending is not { } transition) return null;
        if (_session is not null)
        {
            var placed = _session.PlaceActor(_session.Version, new(new(transition.ToMap),
                new(checked((int)transition.Arrival.X), checked((int)transition.Arrival.Y)), facing));
            if (placed.Disposition == GameSessionDisposition.Rejected) throw new InvalidOperationException("AREA_TRANSITION_SESSION_" + placed.Failure);
        }
        CurrentMap = transition.ToMap; Pending = null;
        return transition;
    }

    private static bool IsTrigger(AreaEndpoint endpoint, AreaObjectKey key) =>
        key.Kind == AreaSourceKind.Static && key.Sector == endpoint.Sector && key.SourceIndex == endpoint.Ordinal && key.ObjectIdSha256 == endpoint.SourceObjectIdSha256;

    private static CardinalTraversalDirection Direction(int dx, int dy) => (dx, dy) switch
    {
        (-1, -1) => CardinalTraversalDirection.XMinusYMinus, (-1, 0) => CardinalTraversalDirection.XMinus,
        (-1, 1) => CardinalTraversalDirection.XMinusYPlus, (0, 1) => CardinalTraversalDirection.YPlus,
        (1, 1) => CardinalTraversalDirection.XPlusYPlus, (1, 0) => CardinalTraversalDirection.XPlus,
        (1, -1) => CardinalTraversalDirection.XPlusYMinus, _ => CardinalTraversalDirection.YMinus,
    };

    /// <summary>A new-game session for the viewer: the clock profile of the area lighting source, a GF domain of
    /// the cave story flags (all Unknown; no script runs here) and the actor at the given exterior cell.</summary>
    public static GameSessionCreationResult CreateSession(ProfileBinding clock, Guid world, Guid module, AreaStepCell actor, int runtimeMap)
    {
        var identity = new GlobalFlagIdentity(new WorldId(world), new ModuleId(module));
        var flags = new GlobalFlagOwnerDescriptor(identity, new OwnerEpoch(Guid.NewGuid()), 0, [new FlagId(2645), new FlagId(2672)], []);
        return GameSessionOwner.Create(new(clock, flags, new(new(runtimeMap), new(checked((int)actor.X), checked((int)actor.Y)), NeighborDirection.XPlus)));
    }
}
