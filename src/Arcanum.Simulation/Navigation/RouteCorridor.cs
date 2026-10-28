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
/// A corridor to a goal beyond the local planner's reach (ARC-WORLD-042): the cells of the shared search's core (ARC-WORLD-073, the
/// original request's best-first search with its costs: 10 a step, 50 more across a window piece, 1 a turn; ledger DC4) over every cell
/// the source contains, with no budget and an expansion limit instead of the 64 × 64 window, cut into waypoints at most
/// <see cref="RouteCorridorLimits.WaypointSteps"/> steps apart, each one within the local planner's reach of the one before. A goal the
/// source does not contain or no route reaches gives a <see cref="RouteCorridorOutcome.Partial"/> corridor to the reached cell nearest
/// it (Chebyshev, then cost). Project policy (the original has no long route, ledger LR7), parity NOT_APPLICABLE.
/// </summary>
public sealed class RouteCorridor
{
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
        var search = BestFirst.Run(new RoutePlanner.SourceMap(source, target), from, target, source.Contains, null, limits.Expansions);
        if (search.Path is not null) return Done(RouteCorridorOutcome.Found, "ROUTE_CORRIDOR", target);
        // No route to the target inside the search: the corridor ends at the reached cell nearest it.
        return search.Nearest == from
            ? new(from, target, RouteCorridorOutcome.NoRoute, search.Limited ? "ROUTE_CORRIDOR_EXPANSION_LIMIT" : "ROUTE_NOT_FOUND", [], 0, search.Expansions)
            : Done(RouteCorridorOutcome.Partial, search.Limited ? "ROUTE_CORRIDOR_EXPANSION_LIMIT" : source.Contains(target) ? "ROUTE_CORRIDOR_PARTIAL" : "ROUTE_TARGET_OUTSIDE_AREA",
                search.Nearest);

        RouteCorridor Done(RouteCorridorOutcome outcome, string reason, CellCoord end)
        {
            var cells = new List<CellCoord>();
            for (var cell = end; cell != from; cell = search.Parents[cell].Parent) cells.Add(cell);
            cells.Reverse();
            var waypoints = new List<CellCoord>();
            for (var index = limits.WaypointSteps - 1; index < cells.Count - 1; index += limits.WaypointSteps) waypoints.Add(cells[index]);
            waypoints.Add(cells[^1]);
            return new(from, target, outcome, reason, waypoints.AsReadOnly(), cells.Count, search.Expansions);
        }
    }
}
