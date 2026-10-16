using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Navigation;

/// <summary>
/// Bounded local route to a clicked cell (Project policy after the pinned CE structure, parity NOT_VERIFIED):
/// a straight attempt first, then an eight-direction cost search inside a fixed window around source and target.
/// No global search, navmesh, smoothing or promotion of Blocked/Unknown/Unsupported evidence to Open.
/// </summary>
public static class RoutePlanner
{
    // Search costs: an ordinary step, an extra point for a change of direction; estimate is the Chebyshev distance.
    private const int StepCost = 10, TurnCost = 1;
    private static readonly NeighborDirection[] Directions = Enum.GetValues<NeighborDirection>();

    public static RoutePlan Plan(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        limits ??= new RouteLimits();
        if (!source.Contains(from)) return Refused(from, target, "ROUTE_SOURCE_OUTSIDE_AREA");
        if (!source.Contains(target)) return Refused(from, target, "ROUTE_TARGET_OUTSIDE_AREA");
        if (from == target) return new(from, target, RouteOutcome.AlreadyAtTarget, null, "ROUTE_ALREADY_AT_TARGET", [], 0, 0, 0, 0, 0, 0);
        if (Math.Abs((long)target.X - from.X) > limits.AxisDistance || Math.Abs((long)target.Y - from.Y) > limits.AxisDistance)
            return Refused(from, target, "ROUTE_TARGET_OUT_OF_RANGE");
        try { return new Search(source, from, target, limits).Run(); }
        catch (OverflowException) { return Refused(from, target, "ROUTE_COORDINATE_OVERFLOW"); }
    }

    /// <summary>The straight-line rule: diagonal unless the minor remaining delta is under half the major one.</summary>
    public static NeighborDirection Toward(CellCoord from, CellCoord target)
    {
        if (from == target) throw new ArgumentException("ROUTE_SAME_CELL");
        long dx = (long)target.X - from.X, dy = (long)target.Y - from.Y;
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        if (Math.Abs(dx) > Math.Abs(dy)) { if (2 * Math.Abs(dy) < Math.Abs(dx)) sy = 0; }
        else if (2 * Math.Abs(dx) < Math.Abs(dy)) sx = 0;
        return Direction(sx, sy);
    }

    /// <summary>The adjacent cell in <paramref name="direction"/> (checked arithmetic).</summary>
    public static CellCoord Neighbor(CellCoord cell, NeighborDirection direction) => MovementGeometry.Neighbor(cell, direction);

    private static NeighborDirection Direction(int dx, int dy)
    {
        foreach (var direction in Directions)
            if (MovementGeometry.Delta(direction) == (dx, dy)) return direction;
        throw new ArgumentException("ROUTE_DIRECTION_INVALID");
    }

    private static RoutePlan Refused(CellCoord from, CellCoord target, string reason) =>
        new(from, target, RouteOutcome.Refused, null, reason, [], 0, 0, 0, 0, 0, 0);

    private sealed class Search(IRouteStepSource source, CellCoord from, CellCoord target, RouteLimits limits)
    {
        private readonly Dictionary<(CellCoord, NeighborDirection), RouteStepVerdict> _verdicts = [];
        private int _blocked, _unknown, _unsupported, _expansions;

        internal RoutePlan Run()
        {
            if (Direct() is { } direct) return Found(RouteMethod.Direct, direct, "ROUTE_DIRECT");
            var (local, reason) = Local();
            return local is null ? Done(RouteOutcome.NoRoute, null, [], reason) : Found(RouteMethod.LocalSearch, local, "ROUTE_LOCAL_SEARCH");
        }

        private RoutePlan Found(RouteMethod method, NeighborDirection[] steps, string reason) => Done(RouteOutcome.Found, method, steps, reason);
        private RoutePlan Done(RouteOutcome outcome, RouteMethod? method, NeighborDirection[] steps, string reason) =>
            new(from, target, outcome, method, reason, steps, limits.SegmentSteps, _verdicts.Count, _expansions, _blocked, _unknown, _unsupported);

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

        // Every straight step must be Open; the first other verdict ends the attempt without a partial route.
        private NeighborDirection[]? Direct()
        {
            var steps = new List<NeighborDirection>();
            for (var cell = from; cell != target;)
            {
                var direction = Toward(cell, target);
                if (Verdict(cell, direction) != RouteStepVerdict.Open) return null;
                steps.Add(direction); cell = MovementGeometry.Neighbor(cell, direction);
            }
            return steps.ToArray();
        }

        // A window of 2*reach cells per axis around the source/target box; cells outside it are never searched.
        private (NeighborDirection[]? Steps, string Reason) Local()
        {
            var size = limits.WindowSize;
            long ox = Math.Min(from.X, target.X) - (size - Math.Abs((long)target.X - from.X)) / 2;
            long oy = Math.Min(from.Y, target.Y) - (size - Math.Abs((long)target.Y - from.Y)) / 2;
            int Index(CellCoord c) => (int)(c.X - ox) + (int)(c.Y - oy) * size;
            CellCoord At(int index) => new(checked((int)(ox + index % size)), checked((int)(oy + index / size)));
            bool Inside(long x, long y) => x >= ox && x < ox + size && y >= oy && y < oy + size &&
                x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue;
            int Estimate(int index)
            {
                var c = At(index);
                return StepCost * (int)Math.Max(Math.Abs((long)target.X - c.X), Math.Abs((long)target.Y - c.Y));
            }

            var cost = new int[size * size]; var parent = new int[size * size]; var entered = new sbyte[size * size];
            var closed = new bool[size * size];
            Array.Fill(cost, int.MaxValue);
            var open = new PriorityQueue<int, (int Total, int Index)>();
            int start = Index(from), goal = Index(target);
            cost[start] = 0; parent[start] = -1; entered[start] = -1;
            open.Enqueue(start, (Estimate(start), start));
            bool unknownFrontier = false, windowEdge = false;
            while (open.TryDequeue(out var current, out var priority))
            {
                if (closed[current] || priority.Total != cost[current] + Estimate(current)) continue;
                if (current == goal) return (Backtrack(parent, entered, goal), "ROUTE_LOCAL_SEARCH");
                if (_expansions == limits.Expansions) return (null, "ROUTE_EXPANSION_LIMIT");
                _expansions++; closed[current] = true;
                var cell = At(current);
                foreach (var direction in Directions)
                {
                    var (dx, dy) = MovementGeometry.Delta(direction);
                    if (!Inside((long)cell.X + dx, (long)cell.Y + dy)) { windowEdge = true; continue; }
                    var next = Index(MovementGeometry.Neighbor(cell, direction));
                    if (closed[next]) continue;
                    var verdict = Verdict(cell, direction);
                    if (verdict == RouteStepVerdict.Unknown) unknownFrontier = true;
                    if (verdict != RouteStepVerdict.Open) continue;
                    var total = cost[current] + StepCost + (entered[current] >= 0 && entered[current] != (sbyte)direction ? TurnCost : 0);
                    if (total >= cost[next]) continue;
                    cost[next] = total; parent[next] = current; entered[next] = (sbyte)direction;
                    open.Enqueue(next, (total + Estimate(next), next));
                }
            }
            // Unknown evidence or the window edge may hide a route; say so instead of reporting a proven blockage.
            return (null, unknownFrontier ? "ROUTE_EVIDENCE_INCOMPLETE" : windowEdge ? "ROUTE_WINDOW_EXHAUSTED" : "ROUTE_NOT_FOUND");
        }

        private static NeighborDirection[] Backtrack(int[] parent, sbyte[] entered, int goal)
        {
            var steps = new List<NeighborDirection>();
            for (var index = goal; parent[index] >= 0; index = parent[index]) steps.Add((NeighborDirection)entered[index]);
            steps.Reverse();
            return steps.ToArray();
        }
    }
}
