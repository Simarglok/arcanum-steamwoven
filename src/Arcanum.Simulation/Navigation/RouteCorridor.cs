using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Navigation;

public enum RouteCorridorOutcome { Found, Partial, NoRoute, Refused }

/// <summary>Limits of a corridor search; they can be lowered, never raised above the policy maxima.</summary>
public sealed class RouteCorridorLimits
{
    /// <summary>Policy maxima: expanded cells (a 3×3-sector window has 36,864 cells) and steps between waypoints.</summary>
    public const int ExpansionMaximum = 262144, WaypointStepMaximum = 24;

    public RouteCorridorLimits(int expansions = 65536, int waypointSteps = WaypointStepMaximum)
    {
        if (expansions is < 1 or > ExpansionMaximum) throw new ArgumentOutOfRangeException(nameof(expansions));
        if (waypointSteps is < 1 or > WaypointStepMaximum) throw new ArgumentOutOfRangeException(nameof(waypointSteps));
        Expansions = expansions; WaypointSteps = waypointSteps;
    }
    public int Expansions { get; }
    public int WaypointSteps { get; }
}

/// <summary>
/// A corridor to a goal beyond the local planner's reach (ARC-WORLD-042): the cells of an eight-direction cost search over
/// every cell the source contains, with the local planner's costs (10 a step, the source's extra cost, 1 a turn), cut into
/// waypoints at most <see cref="RouteCorridorLimits.WaypointSteps"/> steps apart, each one within the local planner's reach
/// of the one before. A goal the source does not contain or no route reaches gives a <see cref="RouteCorridorOutcome.Partial"/>
/// corridor to the reached cell nearest it (Chebyshev, then cost). Project policy, parity NOT_VERIFIED.
/// </summary>
public sealed class RouteCorridor
{
    private const int StepCost = 10, TurnCost = 1, MaximumExtraCost = 1000;
    private static readonly NeighborDirection[] Directions = Enum.GetValues<NeighborDirection>();

    private RouteCorridor(CellCoord from, CellCoord target, RouteCorridorOutcome outcome, string reason, IReadOnlyList<CellCoord> waypoints, int cells, int expansions)
    { From = from; Target = target; Outcome = outcome; ReasonCode = reason; Waypoints = waypoints; Cells = cells; Expansions = expansions; }

    public CellCoord From { get; }
    public CellCoord Target { get; }
    public RouteCorridorOutcome Outcome { get; }
    public string ReasonCode { get; }
    /// <summary>The waypoints in order, the last one the corridor's end (the target when <see cref="Outcome"/> is Found).</summary>
    public IReadOnlyList<CellCoord> Waypoints { get; }
    /// <summary>Cells entered from <see cref="From"/> to the corridor's end.</summary>
    public int Cells { get; }
    public int Expansions { get; }
    public CellCoord? End => Waypoints.Count == 0 ? null : Waypoints[^1];

    public static RouteCorridor Plan(IRouteStepSource source, CellCoord from, CellCoord target, RouteCorridorLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= new RouteCorridorLimits();
        if (!source.Contains(from)) return new(from, target, RouteCorridorOutcome.Refused, "ROUTE_SOURCE_OUTSIDE_AREA", [], 0, 0);
        if (from == target) return new(from, target, RouteCorridorOutcome.Found, "ROUTE_ALREADY_AT_TARGET", [], 0, 0);
        long Estimate(CellCoord c) => StepCost * Math.Max(Math.Abs((long)target.X - c.X), Math.Abs((long)target.Y - c.Y));
        var cost = new Dictionary<CellCoord, long> { [from] = 0 };
        var parent = new Dictionary<CellCoord, (CellCoord Cell, NeighborDirection Direction)>();
        var closed = new HashSet<CellCoord>();
        var open = new PriorityQueue<CellCoord, (long Total, long Estimate)>();
        open.Enqueue(from, (Estimate(from), Estimate(from)));
        var nearest = from; var expansions = 0; var limited = false;
        while (open.TryDequeue(out var cell, out var priority))
        {
            if (closed.Contains(cell) || priority.Total != cost[cell] + Estimate(cell)) continue;
            if (cell == target) return Done(RouteCorridorOutcome.Found, "ROUTE_CORRIDOR", cell);
            if (expansions == limits.Expansions) { limited = true; break; }
            expansions++; closed.Add(cell);
            var estimate = Estimate(cell);
            if (estimate < Estimate(nearest) || estimate == Estimate(nearest) && cost[cell] < cost[nearest]) nearest = cell;
            foreach (var direction in Directions)
            {
                CellCoord next;
                try { next = MovementGeometry.Neighbor(cell, direction); }
                catch (OverflowException) { continue; }
                if (closed.Contains(next) || !source.Contains(next) || source.Assess(cell, direction) != RouteStepVerdict.Open) continue;
                var extra = source.ExtraCost(cell, direction);
                if (extra is < 0 or > MaximumExtraCost) throw new InvalidOperationException("ROUTE_EXTRA_COST_INVALID");
                var turn = parent.TryGetValue(cell, out var entry) && entry.Direction != direction ? TurnCost : 0;
                var total = cost[cell] + StepCost + extra + turn;
                if (cost.TryGetValue(next, out var known) && known <= total) continue;
                cost[next] = total; parent[next] = (cell, direction);
                open.Enqueue(next, (total + Estimate(next), Estimate(next)));
            }
        }
        // No route to the target inside the search: the corridor ends at the reached cell nearest it.
        return nearest == from
            ? new(from, target, RouteCorridorOutcome.NoRoute, limited ? "ROUTE_CORRIDOR_EXPANSION_LIMIT" : "ROUTE_NOT_FOUND", [], 0, expansions)
            : Done(RouteCorridorOutcome.Partial, limited ? "ROUTE_CORRIDOR_EXPANSION_LIMIT" : source.Contains(target) ? "ROUTE_CORRIDOR_PARTIAL" : "ROUTE_TARGET_OUTSIDE_AREA", nearest);

        RouteCorridor Done(RouteCorridorOutcome outcome, string reason, CellCoord end)
        {
            var cells = new List<CellCoord>();
            for (var cell = end; cell != from; cell = parent[cell].Cell) cells.Add(cell);
            cells.Reverse();
            var waypoints = new List<CellCoord>();
            for (var index = limits.WaypointSteps - 1; index < cells.Count - 1; index += limits.WaypointSteps) waypoints.Add(cells[index]);
            waypoints.Add(cells[^1]);
            return new(from, target, outcome, reason, waypoints.AsReadOnly(), cells.Count, expansions);
        }
    }
}
