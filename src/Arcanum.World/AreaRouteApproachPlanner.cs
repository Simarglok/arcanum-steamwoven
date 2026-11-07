using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.World;

/// <summary>A reachable cell to approach an object, with the safe route cost used to choose among candidates.</summary>
public sealed record AreaRouteApproach(CellCoord Cell, IReadOnlyList<NeighborDirection> Steps, long Cost, bool UsesCorridor);

/// <summary>
/// ARC-WORLD-121: choose an object approach by the cost of a complete Open route from the actor. A candidate that is itself
/// enterable from some neighbour can still be unreachable from this actor; it is skipped unless the route planner proves a path.
/// </summary>
public static class AreaRouteApproachPlanner
{
    private const int StepCost = 10, TurnCost = 1, WindowJumpCost = 50;

    /// <summary>Nearby cells in the viewer's stable preference order: closest to the object, then closest to the actor.</summary>
    public static IReadOnlyList<CellCoord> Candidates(CellCoord center, CellCoord actor, int range)
    {
        if (range is < 0 or > RouteLimits.AxisDistanceMaximum) throw new ArgumentOutOfRangeException(nameof(range));
        var cells = new List<CellCoord>();
        for (var dy = -range; dy <= range; dy++)
        for (var dx = -range; dx <= range; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var x = (long)center.X + dx; var y = (long)center.Y + dy;
            if (x is < int.MinValue or > int.MaxValue || y is < int.MinValue or > int.MaxValue) continue;
            cells.Add(new((int)x, (int)y));
        }
        return cells.OrderBy(cell => Distance(cell, center)).ThenBy(cell => Distance(cell, actor)).ToArray();
    }

    /// <summary>
    /// Select the lowest-cost route among <paramref name="candidates"/>. Ties keep their input order. When the local walk
    /// request cannot reach a candidate, a corridor is considered only for the same bounded failures the live follower uses.
    /// </summary>
    public static AreaRouteApproach? Select(IRouteStepSource source, CellCoord actor, IEnumerable<CellCoord> candidates,
        bool allowCorridor, RouteLimits? limits = null, RouteCorridorLimits? corridorLimits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidates);
        limits ??= new RouteLimits();
        AreaRouteApproach? best = null;
        var corridorPolicy = allowCorridor ? corridorLimits ?? new RouteCorridorLimits() : null;
        foreach (var candidate in candidates)
        {
            if (!source.Contains(candidate) || !OpenEntry(source, candidate)) continue;
            if (candidate == actor)
            {
                if (best is null || best.Cost > 0) best = new(candidate, Array.Empty<NeighborDirection>(), 0, false);
                continue;
            }

            var budget = Math.Min(RouteLimits.WalkBudget(actor, candidate), limits.Budget);
            var local = RoutePlanner.Plan(source, actor, candidate, limits.WithBudget(budget));
            AreaRouteApproach? approach = local.Outcome == RouteOutcome.Found
                ? FromPlan(source, actor, local, usesCorridor: false)
                : corridorPolicy is not null && CorridorMayReach(local.ReasonCode)
                    ? ThroughCorridor(source, actor, candidate, limits, corridorPolicy)
                    : null;
            if (approach is not null && (best is null || approach.Cost < best.Cost)) best = approach;
        }
        return best;
    }

    private static AreaRouteApproach? ThroughCorridor(IRouteStepSource source, CellCoord actor, CellCoord candidate,
        RouteLimits limits, RouteCorridorLimits corridorLimits)
    {
        var corridor = RouteCorridor.Plan(source, actor, candidate, corridorLimits);
        if (corridor.Outcome != RouteCorridorOutcome.Found) return null;
        var fullBudget = limits.WithBudget(limits.Budget);
        var steps = new List<NeighborDirection>(); var cell = actor; NeighborDirection? previous = null; long cost = 0;
        foreach (var waypoint in corridor.Waypoints)
        {
            var leg = RoutePlanner.Plan(source, cell, waypoint, fullBudget);
            if (leg.Outcome == RouteOutcome.AlreadyAtTarget) continue;
            if (leg.Outcome != RouteOutcome.Found) return null;
            foreach (var direction in leg.Steps)
            {
                cost += StepCost + (source.ExtraCost(cell, direction) > 0 ? WindowJumpCost : 0)
                    + (previous is { } last && last != direction ? TurnCost : 0);
                cell = RoutePlanner.Neighbor(cell, direction); steps.Add(direction); previous = direction;
            }
        }
        return cell == candidate ? new(candidate, Array.AsReadOnly(steps.ToArray()), cost, true) : null;
    }

    private static AreaRouteApproach FromPlan(IRouteStepSource source, CellCoord actor, RoutePlan plan, bool usesCorridor)
    {
        var cell = actor; NeighborDirection? previous = null; long cost = 0;
        foreach (var direction in plan.Steps)
        {
            cost += StepCost + (source.ExtraCost(cell, direction) > 0 ? WindowJumpCost : 0)
                + (previous is { } last && last != direction ? TurnCost : 0);
            cell = RoutePlanner.Neighbor(cell, direction); previous = direction;
        }
        return new(plan.Target, plan.Steps, cost, usesCorridor);
    }

    private static bool OpenEntry(IRouteStepSource source, CellCoord target)
    {
        if (source.GoalHoldsCritter(target)) return false;
        foreach (var direction in Enum.GetValues<NeighborDirection>())
        {
            var opposite = (NeighborDirection)(((int)direction + 4) & 7);
            CellCoord from;
            try { from = RoutePlanner.Neighbor(target, opposite); }
            catch (OverflowException) { continue; }
            if (source.Contains(from) && source.Assess(from, direction) == RouteStepVerdict.Open) return true;
        }
        return false;
    }

    private static bool CorridorMayReach(string reason) => reason is "ROUTE_TARGET_OUT_OF_RANGE" or "ROUTE_SEARCH_OUT_OF_RANGE"
        or "ROUTE_WINDOW_EXHAUSTED" or "ROUTE_BUDGET_EXHAUSTED";

    private static long Distance(CellCoord a, CellCoord b) => Math.Max(Math.Abs((long)a.X - b.X), Math.Abs((long)a.Y - b.Y));
}
