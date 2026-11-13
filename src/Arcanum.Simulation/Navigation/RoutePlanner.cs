using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Navigation;

/// <summary>
/// The click route's plan (ARC-WORLD-073): the original's path request (<see cref="OriginalPathRequest"/>, ledger DC2–DC8, LR1) over the
/// caller's step evidence: the goal's gate (some admitted neighbour enters it by an Open step), the direct attempt up to 32 cells on the
/// larger axis, else the search over a 64 × 64 window when both axes are under 32 apart, which drops a tile whose estimate over 10
/// exceeds the budget (<see cref="RouteLimits.Budget"/>). No partial route. Only Open steps are taken; Blocked, Unknown and Unsupported
/// evidence is never promoted. The whole route found is returned (<see cref="RoutePlan.Steps"/>); a walker takes the first segment and
/// plans again (LR3).
/// </summary>
public static class RoutePlanner
{
    private const int MaximumExtraCost = 1000;

    /// <param name="avoid">Cells no step may enter in this plan (the follower's cells where a step was refused, ARC-WORLD-073).</param>
    public static RoutePlan Plan(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits? limits = null, IReadOnlySet<CellCoord>? avoid = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= new RouteLimits();
        if (!source.Contains(from)) return Refused(from, target, "ROUTE_SOURCE_OUTSIDE_AREA");
        if (!source.Contains(target)) return Refused(from, target, "ROUTE_TARGET_OUTSIDE_AREA");
        if (from == target) return new(from, target, RouteOutcome.AlreadyAtTarget, null, "ROUTE_ALREADY_AT_TARGET", [], 0, 0, 0, 0, 0, 0);
        if (Math.Abs((long)target.X - from.X) > limits.AxisDistance || Math.Abs((long)target.Y - from.Y) > limits.AxisDistance)
            return Refused(from, target, "ROUTE_TARGET_OUT_OF_RANGE");
        var map = new SourceMap(source, target, avoid);
        OriginalPath path;
        try { path = OriginalPathRequest.Request(map, from, target, limits.Budget); }
        catch (OverflowException) { return Refused(from, target, "ROUTE_COORDINATE_OVERFLOW"); }
        if (path.End is OriginalPathEnd.Direct or OriginalPathEnd.Search)
        {
            var steps = path.Rotations.Take(path.Length).Select(r => (NeighborDirection)r).ToArray();
            return map.Done(from, limits, RouteOutcome.Found, path.End == OriginalPathEnd.Direct ? RouteMethod.Direct : RouteMethod.LocalSearch, steps,
                path.End == OriginalPathEnd.Direct ? "ROUTE_DIRECT" : "ROUTE_LOCAL_SEARCH", path.Expansions);
        }
        // Unknown evidence or the window's edge may hide a route; say so instead of reporting a proven blockage.
        var reason = path.End switch
        {
            OriginalPathEnd.Gate => map.Unknown ? "ROUTE_EVIDENCE_INCOMPLETE" : "ROUTE_NOT_FOUND",
            OriginalPathEnd.SearchOutOfRange => "ROUTE_SEARCH_OUT_OF_RANGE",
            // The budget dropped a cell: a corridor may pass it, whatever evidence was Unknown.
            OriginalPathEnd.SearchPruned => "ROUTE_BUDGET_EXHAUSTED",
            _ when map.Unknown => "ROUTE_EVIDENCE_INCOMPLETE",
            _ when path.WindowEdge => "ROUTE_WINDOW_EXHAUSTED",
            _ => "ROUTE_NOT_FOUND",
        };
        return map.Done(from, limits, RouteOutcome.NoRoute, null, [], reason, path.Expansions);
    }

    /// <summary>
    /// The attack's approach (ARC-WORLD-073, ledger AP1–AP4, 0x4280D0): a route to the target's own tile with the request's flag 0x1 (the
    /// goal's gate skipped, the step onto it untested) and the walk's first budget toward it, ending one step short of it; the cell it
    /// ends on is <see cref="RoutePlan.Target"/>. No route, or one of no step (the target next to the walker and no step onto it), is
    /// <see cref="RouteOutcome.NoRoute"/>: the attack ends (the original after about a second outside a round). No straight-line retry.
    /// </summary>
    [OriginalFunction(0x4280D0, OriginalFunctionScope.Partial)]
    public static RoutePlan PlanApproach(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= new RouteLimits();
        if (!source.Contains(from)) return Refused(from, target, "ROUTE_SOURCE_OUTSIDE_AREA");
        if (!source.Contains(target)) return Refused(from, target, "ROUTE_TARGET_OUTSIDE_AREA");
        if (from == target) return Refused(from, target, "ROUTE_APPROACH_ON_TARGET");
        if (Math.Abs((long)target.X - from.X) > limits.AxisDistance || Math.Abs((long)target.Y - from.Y) > limits.AxisDistance)
            return Refused(from, target, "ROUTE_TARGET_OUT_OF_RANGE");
        var map = new SourceMap(source, target);
        OriginalPath path;
        try { path = OriginalPathRequest.Request(map, from, target, Math.Min(RouteLimits.WalkBudget(from, target), limits.Budget), intoGoal: true); }
        catch (OverflowException) { return Refused(from, target, "ROUTE_COORDINATE_OVERFLOW"); }
        if (path.End is OriginalPathEnd.Direct or OriginalPathEnd.Search && path.Length > 0)
        {
            var steps = path.Rotations.Take(path.Length).Select(r => (NeighborDirection)r).ToArray();
            var end = from;
            foreach (var step in steps) end = Neighbor(end, step);
            return map.Done(from, limits, RouteOutcome.Found, path.End == OriginalPathEnd.Direct ? RouteMethod.Direct : RouteMethod.LocalSearch,
                steps, "ROUTE_APPROACH", path.Expansions, end);
        }
        var reason = path.End switch
        {
            OriginalPathEnd.Direct or OriginalPathEnd.Search => "ROUTE_APPROACH_ADJACENT",
            OriginalPathEnd.SearchOutOfRange => "ROUTE_SEARCH_OUT_OF_RANGE",
            // The budget dropped a cell: a corridor may pass it, whatever evidence was Unknown.
            OriginalPathEnd.SearchPruned => "ROUTE_BUDGET_EXHAUSTED",
            _ when map.Unknown => "ROUTE_EVIDENCE_INCOMPLETE",
            _ when path.WindowEdge => "ROUTE_WINDOW_EXHAUSTED",
            _ => "ROUTE_NOT_FOUND",
        };
        return map.Done(from, limits, RouteOutcome.NoRoute, null, [], reason, path.Expansions);
    }

    /// <summary>
    /// The PC's retry after no path (ARC-WORLD-073, ledger LR4, 0x426040 with flags 0x78): a straight line toward the goal, one
    /// <see cref="Toward"/> step at a time, every step accepted and none tested when planned (the walk tests each as it goes and stops
    /// at the first refused one); nothing beyond 32 cells on the larger axis (the direct attempt's limit).
    /// </summary>
    [OriginalFunction(0x426040, OriginalFunctionScope.Partial)]
    public static RoutePlan StraightRetry(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= new RouteLimits();
        if (!source.Contains(from)) return Refused(from, target, "ROUTE_SOURCE_OUTSIDE_AREA");
        if (!source.Contains(target)) return Refused(from, target, "ROUTE_TARGET_OUTSIDE_AREA");
        if (from == target) return new(from, target, RouteOutcome.AlreadyAtTarget, null, "ROUTE_ALREADY_AT_TARGET", [], 0, 0, 0, 0, 0, 0);
        if (Math.Max(Math.Abs((long)target.X - from.X), Math.Abs((long)target.Y - from.Y)) > Math.Min(limits.AxisDistance, OriginalPathRequest.DirectLimit))
            return Refused(from, target, "ROUTE_TARGET_OUT_OF_RANGE");
        var steps = new List<NeighborDirection>();
        for (var cell = from; cell != target; cell = Neighbor(cell, steps[^1])) steps.Add(Toward(cell, target));
        return new(from, target, RouteOutcome.Found, RouteMethod.StraightRetry, "ROUTE_STRAIGHT_RETRY", steps.ToArray(), limits.SegmentSteps, 0, 0, 0, 0, 0);
    }

    /// <summary>
    /// The back-off's "away" destination (ARC-AI-1512, 0x41F6C0 behind the request flag 0x800 of 0x4AC7B0): <see cref="OriginalPathRequest.Away"/>
    /// over the caller's step evidence, in the caller's cells; none when <paramref name="from"/> lies outside the area or no line is clear.
    /// </summary>
    public static CellCoord? AwayTile(IRouteStepSource source, CellCoord from, CellCoord target, int range)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.Contains(from)) return null;
        try { return OriginalPathRequest.Away(new SourceMap(source, target), from, target, range); }
        catch (OverflowException) { return null; }
    }

    /// <summary>The straight-line rule (0x4B8D50): diagonal unless the minor remaining delta is under half the major one.</summary>
    public static NeighborDirection Toward(CellCoord from, CellCoord target)
    {
        if (from == target) throw new ArgumentException("ROUTE_SAME_CELL");
        return (NeighborDirection)OriginalPathRequest.Rotation(from, target);
    }

    /// <summary>The adjacent cell in <paramref name="direction"/> (checked arithmetic).</summary>
    public static CellCoord Neighbor(CellCoord cell, NeighborDirection direction) => MovementGeometry.Neighbor(cell, direction);

    private static RoutePlan Refused(CellCoord from, CellCoord target, string reason) =>
        new(from, target, RouteOutcome.Refused, null, reason, [], 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// The caller's step evidence as the request's map: a step leaves the area outside it; no tile is blocked by itself (the step's
    /// verdict decides); an Open step passes, across a window piece when it has extra cost; a refused cardinal step the source says an
    /// object refused drops its tile; every step costs 10; the goal's gate is an admitted neighbour entering it by an Open step. Each
    /// directed step is asked at most once per plan.
    /// </summary>
    internal sealed class SourceMap(IRouteStepSource source, CellCoord target, IReadOnlySet<CellCoord>? avoid = null) : IOriginalPathMap
    {
        private readonly Dictionary<(CellCoord, NeighborDirection), RouteStepVerdict> _verdicts = [];
        private readonly Dictionary<(CellCoord, NeighborDirection), int> _extras = [];
        private int _blocked, _unknown, _unsupported;
        internal bool Unknown => _unknown > 0;

        public CellCoord? Step(CellCoord from, int rotation)
        {
            var next = OriginalPathRequest.Neighbor(from, rotation);
            return source.Contains(next) ? next : null;
        }

        public bool TileBlocked(CellCoord cell) => false;

        public OriginalPathTraversal Traverse(CellCoord from, int rotation)
        {
            var direction = (NeighborDirection)rotation;
            if (avoid is { Count: > 0 } && avoid.Contains(OriginalPathRequest.Neighbor(from, rotation))) return new(true, false, null);
            if (Verdict(from, direction) != RouteStepVerdict.Open)
                return new(true, false, (rotation & 1) == 1 && source.StepRefusedByObject(from, direction) ? 2 : null);
            return new(false, Extra(from, direction) > 0, null);
        }

        public int EnterCost(CellCoord to) => OriginalPathRequest.BaseCost;

        public bool DestinationOpen(CellCoord to)
        {
            if (avoid?.Contains(to) == true || source.GoalHoldsCritter(to)) return false;
            foreach (var direction in Enum.GetValues<NeighborDirection>())
            {
                var (dx, dy) = MovementGeometry.Delta(direction);
                if ((long)to.X - dx is < int.MinValue or > int.MaxValue || (long)to.Y - dy is < int.MinValue or > int.MaxValue) continue;
                var neighbor = new CellCoord(to.X - dx, to.Y - dy);
                if (source.Contains(neighbor) && Verdict(neighbor, direction) == RouteStepVerdict.Open) return true;
            }
            return false;
        }

        private RouteStepVerdict Verdict(CellCoord cell, NeighborDirection direction)
        {
            if (_verdicts.TryGetValue((cell, direction), out var known)) return known;
            var verdict = source.Assess(cell, direction);
            if (!Enum.IsDefined(verdict)) throw new InvalidOperationException("ROUTE_VERDICT_INVALID");
            _verdicts.Add((cell, direction), verdict);
            if (verdict == RouteStepVerdict.Blocked) _blocked++;
            else if (verdict == RouteStepVerdict.Unknown) _unknown++;
            else if (verdict == RouteStepVerdict.Unsupported) _unsupported++;
            return verdict;
        }

        // Extra cost of an Open step, asked once per step and bounded.
        private int Extra(CellCoord cell, NeighborDirection direction)
        {
            if (_extras.TryGetValue((cell, direction), out var known)) return known;
            var extra = source.ExtraCost(cell, direction);
            if (extra is < 0 or > MaximumExtraCost) throw new InvalidOperationException("ROUTE_EXTRA_COST_INVALID");
            _extras.Add((cell, direction), extra);
            return extra;
        }

        internal RoutePlan Done(CellCoord from, RouteLimits limits, RouteOutcome outcome, RouteMethod? method, NeighborDirection[] steps, string reason, int expansions,
            CellCoord? end = null) =>
            new(from, end ?? target, outcome, method, reason, steps, limits.SegmentSteps, _verdicts.Count, expansions, _blocked, _unknown, _unsupported);
    }
}
