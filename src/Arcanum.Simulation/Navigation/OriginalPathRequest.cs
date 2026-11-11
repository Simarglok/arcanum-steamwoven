using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Navigation;

/// <summary>One step's object traversal as the original's path request reads it (0x43FD70, ledger DC5): refused, crossing a
/// window piece on a cardinal step, and the type of the object that refused it (0 a wall, 1 a portal; null none or unknown).</summary>
public readonly record struct OriginalPathTraversal(bool Blocked, bool Window, int? BlockerType)
{
    public static OriginalPathTraversal Open { get; } = new(false, false, null);
}

/// <summary>
/// The map the original's path request reads (ARC-WORLD-073, ledger DC2–DC8, LR1): the step to a neighbouring tile in rotation
/// 0..7 (the movement's <see cref="NeighborDirection"/> order, 0x4B8FF0; null off the map), the tile test (0x4D7110), the step's
/// object traversal in the request's mode (0x43FD70), the cost of entering a tile (10, or 90 onto a trap the mover avoids, DC8)
/// and the destination's gate (0x41F3C0). Implementations are deterministic and side-effect free for the duration of one request.
/// </summary>
public interface IOriginalPathMap
{
    CellCoord? Step(CellCoord from, int rotation);
    bool TileBlocked(CellCoord cell);
    OriginalPathTraversal Traverse(CellCoord from, int rotation);
    int EnterCost(CellCoord to);
    bool DestinationOpen(CellCoord to);
}

/// <summary>How a request ended: found by the direct attempt or the search, or why it found nothing.</summary>
public enum OriginalPathEnd { Same, Gate, Direct, Search, SearchOutOfRange, SearchExhausted, SearchPruned, BufferFull }

/// <summary>
/// The result of one path request (ARC-WORLD-073): the count it returns (<see cref="Result"/>, capped at
/// <see cref="OriginalPathRequest.ResultCap"/>; 0 for none), the steps its direct attempt completed before it failed
/// (<see cref="Partial"/>, the request's field 0x24), the whole rotation buffer as the request left it (<see cref="Rotations"/>,
/// each 0..7), the length of the route written (<see cref="Length"/>, 0 for none) and how it ended (<see cref="End"/>, with
/// whether the search met the window's edge and how many tiles it expanded).
/// </summary>
public sealed record OriginalPath(int Result, int Partial, IReadOnlyList<int> Rotations, int Length, OriginalPathEnd End, bool WindowEdge, int Expansions);

/// <summary>
/// ARC-WORLD-073: the original's path request (0x41F3C0, read in the retail 1.0.7.4 executable, ledger DC2–DC8 and LR1), the one
/// search the click route's planner, its corridor and the drop's placement (ARC-COMBAT-010) share: equal tiles give nothing; the
/// destination's gate; the direct attempt (0x41F840) up to 32 tiles on the larger axis, recording its completed steps on a failure;
/// else the best-first search (0x41F9F0) over a 64 × 64 window centred on both tiles when both axes are under 32 apart, which drops a
/// tile whose estimate over 10 exceeds the budget; the result capped at 8. Pure: the map answers every test.
/// </summary>
public static class OriginalPathRequest
{
    public const int DirectLimit = 32, Window = 64, ResultCap = 8, BufferRotations = 200, BaseCost = 10, WindowCost = 50, TurnCost = 1;
    private static readonly (int X, int Y)[] Offsets = [(-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1), (0, -1)];

    /// <summary>The neighbour of a tile in rotation 0..7, any other value the tile itself (0x4B8FF0's table), before the map's bounds.</summary>
    public static CellCoord Neighbor(CellCoord from, int rotation) =>
        rotation is >= 0 and <= 7 ? new(checked(from.X + Offsets[rotation].X), checked(from.Y + Offsets[rotation].Y)) : from;

    /// <summary>0x4B8D50: the rotation from one tile toward another (<see cref="Combat.LineOfFireRules.Rotation"/>).</summary>
    public static int Rotation(CellCoord a, CellCoord b) => Combat.LineOfFireRules.Rotation(a, b);

    /// <summary>One request from <paramref name="from"/> to <paramref name="to"/> with the search's <paramref name="budget"/>
    /// (1..<see cref="BufferRotations"/>; the drop asks 200, a walk <c>min(4 × distance + 5, 200)</c>, LR2). With
    /// <paramref name="intoGoal"/> (the request's flag 0x1, the attack's approach, ledger AP2): the goal's gate is skipped, the step onto
    /// the goal is taken untested, and the route written stops one step short of it (its last rotation is written but not counted).</summary>
    [OriginalFunction(0x41F3C0, OriginalFunctionScope.Partial)]
    public static OriginalPath Request(IOriginalPathMap map, CellCoord from, CellCoord to, int budget, bool intoGoal = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (budget is < 1 or > BufferRotations) throw new ArgumentOutOfRangeException(nameof(budget));
        var rotations = new int[BufferRotations];
        if (from == to) return new(0, 0, rotations, 0, OriginalPathEnd.Same, false, 0);
        if (!intoGoal && !map.DestinationOpen(to)) return new(0, 0, rotations, 0, OriginalPathEnd.Gate, false, 0);
        var direct = Direct(map, from, to, rotations, intoGoal, out var partial);
        if (direct > 0) return new(Math.Min(direct, ResultCap), partial, rotations, direct, OriginalPathEnd.Direct, false, 0);
        var search = Search(map, from, to, budget, rotations, intoGoal);
        return new(Math.Min(search.Length, ResultCap), partial, rotations, search.Length, search.End, search.WindowEdge, search.Expansions);
    }

    // 0x41F840: toward the target one rotation at a time, each step tested; a failure keeps the completed steps.
    [OriginalFunction(0x41F840, OriginalFunctionScope.Partial)]
    private static int Direct(IOriginalPathMap map, CellCoord from, CellCoord to, int[] rotations, bool intoGoal, out int partial)
    {
        partial = 0;
        if (Math.Max(Math.Abs((long)to.X - from.X), Math.Abs((long)to.Y - from.Y)) > DirectLimit) return 0;
        var cur = from;
        var i = 0;
        for (; i < BufferRotations && cur != to; i++)
        {
            var rotation = Rotation(cur, to);
            rotations[i] = rotation;
            if (map.Step(cur, rotation) is not { } next) break;
            if (!(intoGoal && next == to) && (map.TileBlocked(next) || map.Traverse(cur, rotation) is not { Blocked: false, Window: false }
                || map.EnterCost(next) != BaseCost)) break;
            cur = next;
        }
        // With flag 0x1 the step count less the step onto the goal (0x41F9D7).
        if (cur == to) return intoGoal ? i - 1 : i;
        partial = i;
        return 0;
    }

    private readonly record struct SearchResult(int Length, OriginalPathEnd End, bool WindowEdge, int Expansions);

    // 0x41F9F0 over the 64 × 64 window: every round the open tile of the smallest estimate (the first in index order on a tie).
    [OriginalFunction(0x41F9F0, OriginalFunctionScope.Partial)]
    private static SearchResult Search(IOriginalPathMap map, CellCoord from, CellCoord to, int budget, int[] rotations, bool intoGoal)
    {
        long dx = Math.Abs((long)from.X - to.X), dy = Math.Abs((long)from.Y - to.Y);
        if (dx >= DirectLimit || dy >= DirectLimit) return new(0, OriginalPathEnd.SearchOutOfRange, false, 0);
        var originX = Math.Min((long)from.X, to.X) - (Window - dx) / 2;
        var originY = Math.Min((long)from.Y, to.Y) - (Window - dy) / 2;
        var found = BestFirst.Run(map, from, to, (cell) => cell.X >= originX && cell.X < originX + Window && cell.Y >= originY && cell.Y < originY + Window,
            budget, null, intoGoal);
        if (found.Path is not { } path)
            return new(0, found.Pruned ? OriginalPathEnd.SearchPruned : OriginalPathEnd.SearchExhausted, found.WindowEdge, found.Expansions);
        if (path.Count > BufferRotations) return new(0, OriginalPathEnd.BufferFull, found.WindowEdge, found.Expansions);
        for (var i = 0; i < path.Count; i++) rotations[i] = path[i];
        // With flag 0x1 the route's length less the step onto the goal (0x420089).
        return new(intoGoal ? path.Count - 1 : path.Count, OriginalPathEnd.Search, found.WindowEdge, found.Expansions);
    }
}

/// <summary>
/// The search's core (ARC-WORLD-073), shared by the request's window (0x41F9F0, ledger DC4) and the click route's corridor (ARC-WORLD-042):
/// costs start at 1 on the start tile; each round takes the open tile of the smallest cost + 10 × (larger axis distance to the target),
/// the first in row-major order on a tie; the target ends it. Its neighbours in rotation order: off the map, outside the area or dropped,
/// skipped; a blocked tile is dropped; a refused step is skipped, and a cardinal one refused by an object other than a wall or portal (or
/// an unknown blocker the map names, type ≥ 2) drops the tile; a step costs the map's entry cost (10, 90 onto an avoided trap), + 50 across
/// a window piece, + 1 when it turns from the step before; a cheaper cost opens or reopens a tile. With a budget, a tile whose estimate
/// over 10 exceeds it is dropped when it is opened (the linear scan drops it before any later expansion); with an expansion limit the
/// search ends when it is reached. A priority queue keyed by (estimate, row, column) takes the tile the original's scan takes.
/// </summary>
internal static class BestFirst
{
    internal sealed record Outcome(IReadOnlyList<int>? Path, bool Pruned, bool WindowEdge, bool Limited, int Expansions,
        IReadOnlyDictionary<CellCoord, (CellCoord Parent, int Rotation)> Parents, CellCoord Nearest, long NearestCost);

    internal static Outcome Run(IOriginalPathMap map, CellCoord from, CellCoord to, Func<CellCoord, bool> inside, int? budget, int? expansions,
        bool intoGoal = false)
    {
        long Heuristic(CellCoord c) => 10L * Math.Max(Math.Abs((long)c.X - to.X), Math.Abs((long)c.Y - to.Y));
        // Positive: open; negative: closed (the original negates a closed tile's cost); absent: unseen.
        var cost = new Dictionary<CellCoord, long> { [from] = 1 };
        var dropped = new HashSet<CellCoord>();
        var parents = new Dictionary<CellCoord, (CellCoord Parent, int Rotation)>();
        var open = new PriorityQueue<CellCoord, (long F, long Y, long X)>();
        open.Enqueue(from, (1 + Heuristic(from), from.Y, from.X));
        bool pruned = false, edge = false, limited = false; var expanded = 0;
        var nearest = from; long nearestCost = 1;
        if (budget is { } first && (1 + Heuristic(from)) / 10 > first) { pruned = true; dropped.Add(from); open.Clear(); }
        while (open.TryDequeue(out var cur, out var key))
        {
            if (dropped.Contains(cur) || !cost.TryGetValue(cur, out var c) || c <= 0 || key.F != c + Heuristic(cur)) continue;
            if (cur == to) return new(Backtrack(parents, from, to), pruned, edge, false, expanded, parents, to, c);
            if (expansions is { } limit && expanded == limit) { limited = true; break; }
            expanded++;
            if (Heuristic(cur) < Heuristic(nearest) || Heuristic(cur) == Heuristic(nearest) && c < nearestCost) { nearest = cur; nearestCost = c; }
            for (var rotation = 0; rotation < 8; rotation++)
            {
                CellCoord? stepped;
                try { stepped = map.Step(cur, rotation); }
                catch (OverflowException) { continue; }
                if (stepped is not { } n) continue;
                if (!inside(n)) { edge = true; continue; }
                if (dropped.Contains(n)) continue;
                // With flag 0x1 the step onto the goal skips the tile and traversal tests (0x41FDD2).
                var untested = intoGoal && n == to;
                if (!untested && map.TileBlocked(n)) { dropped.Add(n); continue; }
                var traversal = untested ? OriginalPathTraversal.Open : map.Traverse(cur, rotation);
                if (traversal.Blocked)
                {
                    if ((rotation & 1) == 1 && traversal.BlockerType is { } type && type is not (0 or 1)) dropped.Add(n);
                    continue;
                }
                var step = (long)map.EnterCost(n);
                if (traversal.Window) step += OriginalPathRequest.WindowCost;
                var next = c + step;
                if (parents.TryGetValue(cur, out var entered) && entered.Rotation != rotation) next += OriginalPathRequest.TurnCost;
                var known = cost.TryGetValue(n, out var v) ? v : 0;
                if (known == 0 || known > 0 && known > next || known < 0 && -known > next)
                {
                    if (budget is { } b && (next + Heuristic(n)) / 10 > b) { dropped.Add(n); pruned = true; continue; }
                    cost[n] = next; parents[n] = (cur, rotation);
                    open.Enqueue(n, (next + Heuristic(n), n.Y, n.X));
                }
            }
            cost[cur] = -c;
        }
        return new(null, pruned, edge, limited, expanded, parents, nearest, nearestCost);
    }

    internal static IReadOnlyList<int> Backtrack(IReadOnlyDictionary<CellCoord, (CellCoord Parent, int Rotation)> parents, CellCoord from, CellCoord end)
    {
        var steps = new List<int>();
        for (var cell = end; cell != from; cell = parents[cell].Parent) steps.Add(parents[cell].Rotation);
        steps.Reverse();
        return steps;
    }
}
