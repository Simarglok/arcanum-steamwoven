using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// Live actors over the T0 step evidence (ARC-WORLD-016): <see cref="Moved"/> are T0 records the T0 evidence no longer
/// speaks for (every companion's, wherever it stands: its live cell is in <see cref="Occupied"/> instead),
/// <see cref="Occupied"/> the chart cells other live actors hold for the mover (committed cell and the other cell of a
/// started step). Immutable.
/// </summary>
public sealed class AreaOccupancy
{
    public static AreaOccupancy None { get; } = new([], []);

    public AreaOccupancy(IEnumerable<AreaObjectKey> moved, IEnumerable<CellCoord> occupied)
    {
        ArgumentNullException.ThrowIfNull(moved);
        ArgumentNullException.ThrowIfNull(occupied);
        Moved = moved.ToHashSet(); Occupied = occupied.ToHashSet();
    }

    public IReadOnlySet<AreaObjectKey> Moved { get; }
    public IReadOnlySet<CellCoord> Occupied { get; }
    public bool IsEmpty => Moved.Count == 0 && Occupied.Count == 0;
}

/// <summary>One directed step's verdict after the live overlay; <see cref="Overlaid"/> when it differs from the T0 assessment.</summary>
public readonly record struct AreaStepVerdict(CardinalTraversalCoverage Coverage, CardinalTraversalDisposition? Disposition, string ReasonCode, bool Overlaid);

/// <summary>
/// The overlay rule (project policy, parity NOT_VERIFIED). A live actor blocks like the living critter it is in the T0
/// object rules: on the destination and, for a diagonal step, on either shoulder. A moved T0 record no longer blocks
/// and its evidence gaps no longer count. The Data precedence is kept: terrain, then wall, then object blockers (T0
/// records, then live actors), then gaps; a step the T0 evidence refuses for terrain, a wall, the core edge or an
/// unavailable path branch is never changed.
/// </summary>
public static class AreaOccupancyRules
{
    public const string DestinationActor = "DESTINATION_ACTOR_OCCUPIED", ShoulderActor = "SHOULDER_ACTOR_OCCUPIED";

    public static AreaStepVerdict Apply(AreaStepAssessment assessment, CellCoord from, NeighborDirection direction, AreaOccupancy? occupancy)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        var raw = new AreaStepVerdict(assessment.Coverage, assessment.Disposition, assessment.ReasonCode, false);
        if (occupancy is null || occupancy.IsEmpty || assessment.Coverage == CardinalTraversalCoverage.Unsupported ||
            assessment.ReasonCode is "DESTINATION_TERRAIN_BLOCKED" or "WALL_EDGE_BLOCKED" or "PATH_BRANCH_UNAVAILABLE") return raw;
        AreaStepVerdict Verdict(CardinalTraversalCoverage coverage, CardinalTraversalDisposition? disposition, string reason) =>
            new(coverage, disposition, reason, coverage != raw.Coverage || disposition != raw.Disposition || reason != raw.ReasonCode);

        var blocker = assessment.Objects.FirstOrDefault(o => o.Disposition == CardinalTraversalDisposition.Blocked && !occupancy.Moved.Contains(o.Key));
        if (blocker is not null)
            return Verdict(CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked,
                blocker.Role == AreaStepObjectRole.Destination ? "DESTINATION_OBJECT_BLOCKER" : "SHOULDER_OBJECT_BLOCKER");
        if (occupancy.Occupied.Contains(RoutePlanner.Neighbor(from, direction)))
            return Verdict(CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, DestinationActor);
        var d = (int)direction;
        if ((d & 1) == 0 && (occupancy.Occupied.Contains(RoutePlanner.Neighbor(from, (NeighborDirection)((d + 7) % 8))) ||
            occupancy.Occupied.Contains(RoutePlanner.Neighbor(from, (NeighborDirection)(d + 1)))))
            return Verdict(CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Blocked, ShoulderActor);
        if (assessment.Gaps.Any(gap => gap.Object is not { } key || !occupancy.Moved.Contains(key)))
            return Verdict(CardinalTraversalCoverage.Unknown, null, "REQUIRED_EVIDENCE_UNAVAILABLE");
        return Verdict(CardinalTraversalCoverage.Supported, CardinalTraversalDisposition.Open,
            assessment.WindowJump is not null ? "WINDOW_JUMP" : "COMPLETE_NONBLOCKING_EVIDENCE");
    }

    /// <summary>The route verdict of an overlaid step: only Supported Open is Open.</summary>
    public static RouteStepVerdict Route(AreaStepVerdict verdict) => verdict.Coverage switch
    {
        CardinalTraversalCoverage.Supported => verdict.Disposition == CardinalTraversalDisposition.Open ? RouteStepVerdict.Open : RouteStepVerdict.Blocked,
        CardinalTraversalCoverage.Unknown => RouteStepVerdict.Unknown,
        CardinalTraversalCoverage.Unsupported => RouteStepVerdict.Unsupported,
        _ => throw new InvalidOperationException("ROUTE_COVERAGE_INVALID")
    };
}
