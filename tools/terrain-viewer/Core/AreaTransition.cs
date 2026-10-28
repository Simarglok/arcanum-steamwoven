using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.GlobalFlags;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Reputation;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One loaded map: its strict Area snapshot, its step evidence and its chart.</summary>
public sealed record AreaMapRuntime(AreaSnapshot Snapshot, AreaStepSnapshot Steps, AreaChart Chart)
{
    public int RuntimeMap => Snapshot.RuntimeMap;
}

public enum AreaUseOutcome
{
    Accepted, NotATrigger, TooFar, TriggerTileBlocked, StepBlocked, StepUnknown, TransitionPending, DestinationUnavailable,
    /// <summary>ARC-SCRIPT-015: a scripted use ran and asked for no teleport (its guards held it back).</summary>
    ScriptEnded,
    /// <summary>ARC-SCRIPT-015: a scripted use did not run (outside the viewer's set, no content) or the session refused its effects.</summary>
    ScriptRefused,
}

/// <summary>The answer to one use of an object. <see cref="Distance"/> is the Chebyshev distance to it.</summary>
public sealed record AreaUseResult(AreaUseOutcome Outcome, AreaEndpoint? Endpoint, int Distance, string Reason);

/// <summary>A processed cross-map transfer: the endpoint, both runtime maps and the exact arrival.</summary>
public sealed record AreaTransition(AreaEndpoint Endpoint, int FromMap, int ToMap, AreaStepCell Arrival, CellCoord ArrivalChartCell);

/// <summary>
/// ARC-WORLD-009 use rule (docs/research/cave-transition-evidence.md T3), shared by the Crash Site pair and map mode
/// (ARC-WORLD-031): a use of a trigger object is accepted within Chebyshev distance 2 when the object's tile is not
/// terrain-blocking and one step from the actor towards the object is not blocked by anything but the object itself.
/// </summary>
public static class AreaUseRule
{
    public const int UseRange = 2;

    /// <summary><see cref="AreaUseOutcome.Accepted"/> with the distance when the use may run, else the refusal.</summary>
    public static (AreaUseOutcome Outcome, int Distance, string Reason) Check(AreaMapRuntime map, AreaObjectKey trigger, AreaStepCell objectCell, AreaStepCell actor)
    {
        ArgumentNullException.ThrowIfNull(map);
        var dx = objectCell.X - actor.X; var dy = objectCell.Y - actor.Y;
        var distance = checked((int)Math.Max(Math.Abs(dx), Math.Abs(dy)));
        if (distance > UseRange) return (AreaUseOutcome.TooFar, distance, "OUTSIDE_USE_RANGE");
        // The object's own tile must not be terrain-blocking.
        if (map.Steps.Terrain(objectCell) is not { Coverage: CardinalTraversalCoverage.Supported } terrain)
            return (AreaUseOutcome.StepUnknown, distance, "TRIGGER_TILE_TERRAIN_UNKNOWN");
        if (terrain.Disposition == CardinalTraversalDisposition.Blocked) return (AreaUseOutcome.TriggerTileBlocked, distance, terrain.ReasonCode);
        if (distance > 0)
        {
            // One step towards the object, with the object itself ignored.
            var direction = Direction(Math.Sign(dx), Math.Sign(dy));
            var next = new AreaStepCell(actor.X + Math.Sign(dx), actor.Y + Math.Sign(dy));
            var step = map.Steps.Assess(map.Steps.Request(actor, next, direction));
            var others = step.Objects.Where(o => o.Key != trigger).ToArray();
            var blocked = step.DestinationTerrain?.Disposition == CardinalTraversalDisposition.Blocked || step.WallBlocker is not null ||
                others.Any(o => o.Disposition == CardinalTraversalDisposition.Blocked);
            if (blocked) return (AreaUseOutcome.StepBlocked, distance, step.ReasonCode);
            if (step.Coverage == CardinalTraversalCoverage.Unsupported || step.Gaps.Any(g => g.Object != trigger))
                return (AreaUseOutcome.StepUnknown, distance, step.ReasonCode);
        }
        return (AreaUseOutcome.Accepted, distance, "OK");
    }

    private static CardinalTraversalDirection Direction(int dx, int dy) => (dx, dy) switch
    {
        (-1, -1) => CardinalTraversalDirection.XMinusYMinus, (-1, 0) => CardinalTraversalDirection.XMinus,
        (-1, 1) => CardinalTraversalDirection.XMinusYPlus, (0, 1) => CardinalTraversalDirection.YPlus,
        (1, 1) => CardinalTraversalDirection.XPlusYPlus, (1, 0) => CardinalTraversalDirection.XPlus,
        (1, -1) => CardinalTraversalDirection.XPlusYMinus, _ => CardinalTraversalDirection.YMinus,
    };
}

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
    public const int UseRange = AreaUseRule.UseRange;
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
        var check = AreaUseRule.Check(map, trigger, new(tx, ty), actor);
        if (check.Outcome != AreaUseOutcome.Accepted) return new(check.Outcome, endpoint, check.Distance, check.Reason);
        var distance = check.Distance;
        if (!_maps.TryGetValue(endpoint.RuntimeDestination, out var destination))
            return new(AreaUseOutcome.DestinationUnavailable, endpoint, distance, "DESTINATION_MAP_NOT_LOADED");
        var arrival = new AreaStepCell(endpoint.ArrivalX, endpoint.ArrivalY);
        if (destination.Snapshot.CatalogueMap != endpoint.CatalogueDestination || !destination.Steps.IsAdmitted(arrival))
            return new(AreaUseOutcome.DestinationUnavailable, endpoint, distance, "ARRIVAL_NOT_ADMITTED");
        var chartCell = destination.Chart.ToChart(arrival);
        if (!destination.Chart.Contains(chartCell))
            return new(AreaUseOutcome.DestinationUnavailable, endpoint, distance, "ARRIVAL_OUTSIDE_CHART");
        Pending = new(endpoint, CurrentMap, endpoint.RuntimeDestination, arrival, chartCell);
        return new(AreaUseOutcome.Accepted, endpoint, distance, "QUEUED");
    }

    /// <summary>Applies the pending transfer exactly once: the current map changes and the session (when
    /// present) records the actor on the destination map at the authored arrival, together with
    /// <paramref name="changes"/> (ARC-WORLD-017: the companions it carries or leaves behind) in one atomic step.
    /// Changes without a session are refused. A session refusal (for example the change budget) applies nothing:
    /// the pending transfer is dropped, <see cref="LastRefusal"/> names the failure and null is returned.</summary>
    public AreaTransition? Process(NeighborDirection facing = NeighborDirection.XPlus, IReadOnlyList<MapChangeEdit>? changes = null)
    {
        if (Pending is not { } transition) return null;
        if (_session is null && changes is { Count: > 0 }) throw new InvalidOperationException("AREA_TRANSITION_CHANGES_WITHOUT_SESSION");
        if (_session is not null)
        {
            var placed = _session.ApplyChanges(_session.Version, new ActorPlacement(new(transition.ToMap),
                new(checked((int)transition.Arrival.X), checked((int)transition.Arrival.Y)), facing), changes ?? []);
            if (placed.Disposition == GameSessionDisposition.Rejected)
            {
                Pending = null; LastRefusal = placed.Failure;
                return null;
            }
        }
        CurrentMap = transition.ToMap; Pending = null; LastRefusal = GameSessionFailure.None;
        return transition;
    }

    /// <summary>The session failure of the last refused <see cref="Process"/>, None after an applied one.</summary>
    public GameSessionFailure LastRefusal { get; private set; }

    /// <summary>Drops a pending transfer unapplied (a viewer reset or reload takes precedence over it).
    /// Returns the dropped transfer, or null when none was pending.</summary>
    public AreaTransition? Cancel()
    {
        var pending = Pending; Pending = null;
        return pending;
    }

    private static bool IsTrigger(AreaEndpoint endpoint, AreaObjectKey key) =>
        key.Kind == AreaSourceKind.Static && key.Sector == endpoint.Sector && key.SourceIndex == endpoint.Ordinal && key.ObjectIdSha256 == endpoint.SourceObjectIdSha256;

    /// <summary>A new-game session for the viewer: the clock profile of the area lighting source, the original
    /// new-game global flags (0..3199 known false, ARC-SCRIPT-006) that dialogs read and write, an empty story
    /// domain and the actor at the given exterior cell. The cave's story scripts are out of scope
    /// (docs/research/cave-transition-evidence.md T9). ARC-WORLD-027: with a ready pre-made <paramref name="hero"/> the
    /// session's player character is that hero and its main-menu flag starts true. ARC-UI-007: the hero's load also brings the
    /// experience data, so the session awards experience (ARC-WORLD-030) and knows the next level. ARC-WORLD-044: the module's
/// <paramref name="reputations"/> table, when the host read one (<see cref="AreaReputations.Load"/>). ARC-WORLD-101: a pre-made hero's
/// prototype player name becomes the session hero's (<see cref="GameSessionOwner.SetHeroPlayerName"/>; a refusal fails the start).</summary>
    public static GameSessionCreationResult CreateSession(ProfileBinding clock, Guid world, Guid module, AreaStepCell actor, int runtimeMap,
        AreaHeroLoad? hero = null, ReputationCatalog? reputations = null)
    {
        // ARC-UI-030: only the new character starts without a main-menu flag.
        if (hero is not null && (hero.Status != AreaHeroStatus.Ready || hero.Definition is null
            || hero.Flag is null && hero.Hero.BasicPrototype != AreaStartingHero.GenericPrototype))
            throw new ArgumentException("AREA_HERO_NOT_READY", nameof(hero));
        var identity = new GlobalFlagIdentity(new WorldId(world), new ModuleId(module));
        var flags = AreaStartingHero.NewGameFlags(identity, new OwnerEpoch(Guid.NewGuid()), hero?.Flag);
        var created = GameSessionOwner.Create(new(clock, flags, new(new(runtimeMap), new(checked((int)actor.X), checked((int)actor.Y)), NeighborDirection.XPlus),
            Hero: hero?.Definition, Experience: hero?.Experience, AutoLevel: hero?.AutoLevel, Reputations: reputations));
        // ARC-WORLD-101: a pre-made hero keeps its prototype's player name (field 0x10E; the new character's is typed on its screen).
        if (created.Owner is { } named && hero is { Hero.BasicPrototype: not AreaStartingHero.GenericPrototype, Read.Hero.PlayerName: { Count: > 0 } name }
            && named.SetHeroPlayerName(named.Version, name) is { Disposition: GameSessionDisposition.Rejected } refused)
        {
            named.Dispose();
            return new(null, refused.Failure, ClockFailure.None, GlobalFlagReason.None);
        }
        if (hero?.KitWear is not { } wear || created.Owner is not { } owner) return created;
        // ARC-WORLD-034: the pre-made start wields the hero's best items before the game begins (the original's best-items call,
        // 0x4654F0, from the pre-made menu step 0x545B60). The viewer has one diagnostic body art and no inventory grid, so it answers
        // the art lookup and the insert check with yes; a refused call fails the start, typed.
        var worn = owner.EquipBest(owner.Version, entity => KitIndex(entity) is { } index && index < wear.Count ? wear[index] : null,
            (_, _) => true, _ => SessionInsertVerdict.Accepted);
        if (worn.Disposition != GameSessionDisposition.Rejected) return created;
        owner.Dispose();
        return new(null, worn.Failure, ClockFailure.None, GlobalFlagReason.None);
    }

    // kit:1 is the first kit item.
    private static int? KitIndex(string entity) =>
        entity.StartsWith("kit:", StringComparison.Ordinal) && int.TryParse(entity.AsSpan(4), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= 1 ? number - 1 : null;
}
