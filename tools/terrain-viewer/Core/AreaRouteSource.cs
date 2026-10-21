using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// Route evidence over the whole-core T0 step snapshot, in <see cref="AreaGridSession"/> chart cells. It asks the same
/// <see cref="AreaStepSnapshot.Assess"/> the session admits steps with, so a planned Open step is the step the runtime
/// receives. Only Supported Open is Open; Unknown and Unsupported (core edge) stay distinct from Blocked. With an
/// <paramref name="occupancy"/> provider (ARC-WORLD-016) every step gets the same live overlay the session admits it with.
/// ARC-WORLD-073: while <paramref name="crittersPass"/> answers yes (the Original rule mode's walk plan outside combat mode, the request's
/// flag 0x10, ledger LR2 and LRA1), living critters do not block a planned step: the T0 PC and NPC records on it count as moved and
/// the live actors are left out, as the original's traversal mode 4 (DC5); removed objects still do not block.
/// </summary>
public sealed class AreaRouteSource(AreaStepSnapshot snapshot, AreaChart? chart = null, Func<AreaOccupancy?>? occupancy = null,
    Func<IReadOnlyDictionary<Arcanum.Data.Area.AreaObjectKey, AreaPortalPassage>?>? portals = null, Func<bool>? crittersPass = null) : IRouteStepSource
{
    private const int PcType = 15, NpcType = 16;
    public AreaStepSnapshot Snapshot { get; } = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    public AreaChart Chart { get; } = chart ?? AreaChart.Exterior;

    public bool Contains(CellCoord cell) => Snapshot.IsAdmitted(Chart.ToWorld(cell));

    public RouteStepVerdict Assess(CellCoord from, NeighborDirection direction) => AreaOccupancyRules.Route(Overlaid(from, direction));

    /// <summary>ARC-WORLD-073 (ledger DC4): a refused step whose deciding blocker is an object other than a wall or portal (a container,
    /// scenery, a trap, a critter or a live actor), which drops the tile from the request's search on a cardinal step.</summary>
    public bool StepRefusedByObject(CellCoord from, NeighborDirection direction) =>
        Overlaid(from, direction).ReasonCode is "DESTINATION_OBJECT_BLOCKER" or AreaOccupancyRules.DestinationActor;

    private AreaStepVerdict Overlaid(CellCoord from, NeighborDirection direction)
    {
        var assessment = Assessment(from, direction);
        var live = occupancy?.Invoke();
        if (crittersPass?.Invoke() != true) return AreaOccupancyRules.Apply(assessment, from, direction, live);
        var critters = assessment.Objects.Where(o => o.ObjectType is PcType or NpcType).Select(o => o.Key).ToArray();
        return AreaOccupancyRules.Apply(assessment, from, direction, new AreaOccupancy((live?.Moved ?? new HashSet<Arcanum.Data.Area.AreaObjectKey>()).Concat(critters), []));
    }

    /// <summary>ARC-WORLD-021: a window jump costs 50 on top of the plain step, as the source's path search does
    /// (research ARC-RES-047 W7). The planner asks right after <see cref="Assess"/> for the same Open step, which reuses
    /// that assessment.</summary>
    public int ExtraCost(CellCoord from, NeighborDirection direction) => Assessment(from, direction).WindowJump is null ? 0 : WindowJumpCost;

    // The last assessed step: T0 evidence is immutable, so reusing it for the same step is exact.
    private (CellCoord From, NeighborDirection Direction, AreaStepAssessment Value)? _last;
    private AreaStepAssessment Assessment(CellCoord from, NeighborDirection direction)
    {
        if (_last is { } last && last.From == from && last.Direction == direction) return last.Value;
        var to = RoutePlanner.Neighbor(from, direction);
        // ARC-WORLD-051: doors and windows by their runtime state, as the actor's steps admit them.
        var assessment = Snapshot.Assess(Snapshot.Request(Chart.ToWorld(from), Chart.ToWorld(to), GridReplay.MapDirection(direction)), portals?.Invoke());
        _last = (from, direction, assessment);
        return assessment;
    }

    public const int WindowJumpCost = 50;
}
