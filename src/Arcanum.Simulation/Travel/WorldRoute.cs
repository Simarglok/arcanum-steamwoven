namespace Arcanum.Simulation.Travel;

/// <summary>Whether a sector of the start map lets a world-map route through (ledger WT7): not script-blocked, and either its
/// own sector file or terrain without <c>/b</c>; a sector outside the map does not.</summary>
public delegate bool TravelSectorPasses(TravelSector sector);

/// <summary>A planned segment: the number of direction steps it wrote at <see cref="Start"/> of the route buffer, or a refusal.</summary>
public readonly record struct TravelSegmentPlan(bool Planned, int Start, int Steps);

/// <summary>
/// ARC-WORLD-056: the world-map route of the retail executable (ledger WT5, WT7, WT8). Segments write their directions into one
/// shared buffer of <see cref="BufferSteps"/>, as the original's waypoints do; a segment starts where the previous one ended.
/// The buffer outlives a route, so the original's stale step after a detour (A2) reads what an earlier route left.
/// </summary>
public sealed class WorldRoute
{
    public const int BufferSteps = 5000, MaxWaypoints = 30, Window = 16;

    private byte[] _buffer = new byte[BufferSteps];

    /// <summary>A copy with the same buffer, for planning that may be dropped (a re-plan after a deletion).</summary>
    public WorldRoute Clone() => new() { _buffer = (byte[])_buffer.Clone() };

    /// <summary>The direction byte at a route position (the travel step reads them in order).</summary>
    public int Step(int index) => index is >= 0 and < BufferSteps ? (sbyte)_buffer[index] : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>
    /// Plans one segment from <paramref name="from"/> to <paramref name="to"/> with at most <paramref name="max"/> steps written at
    /// <paramref name="start"/> (0x4207D0): straight steps toward the target while the next sector passes, else the detour
    /// (0x420900, 0x4209C0). The same sector, a blocked destination or no detour refuses the segment.
    /// </summary>
    [OriginalFunction(0x4207D0, OriginalFunctionScope.Full)]
    public TravelSegmentPlan Plan(TravelSector from, TravelSector to, int start, TravelSectorPasses passes, TravelRuleProfile rules)
    {
        ArgumentNullException.ThrowIfNull(passes); ArgumentNullException.ThrowIfNull(rules);
        if (start is < 0 or > BufferSteps) throw new ArgumentOutOfRangeException(nameof(start));
        var max = BufferSteps - start;
        var current = from; var count = 0;
        while (count < max && current != to)
        {
            var direction = WorldTravelGeometry.Direction(current, to);
            _buffer[start + count] = (byte)direction;
            var next = WorldTravelGeometry.Neighbor(current, direction);
            if (passes(next)) { current = next; count++; continue; }
            if (Detour(current, to, start + count, max - count, passes) is not { } detour) return new(false, start, count);
            // The original's loop also counts its own step after a detour, leaving one byte unwritten (A2). A count past the
            // buffer's end refuses the segment (project policy: the original would read past its buffer).
            count += detour.Steps + (rules.DetourKeepsStaleStep ? 1 : 0);
            if (count > max) return new(false, start, max);
            current = detour.End;
        }
        return current == to && count > 0 ? new(true, start, count) : new(false, start, count);
    }

    // The straight line from the blocked step to the first sector that passes, joined by the window search (0x420900).
    [OriginalFunction(0x420900, OriginalFunctionScope.Full)]
    private (int Steps, TravelSector End)? Detour(TravelSector from, TravelSector to, int at, int max, TravelSectorPasses passes)
    {
        var walker = from;
        while (walker != to)
        {
            var next = WorldTravelGeometry.Neighbor(walker, WorldTravelGeometry.Direction(walker, to));
            if (passes(next)) return Search(from, next, at, max, passes) is { } steps ? (steps, next) : null;
            walker = next;
        }
        return null;
    }

    /// <summary>
    /// The window search (0x4209C0): only for sectors fewer than 8 apart on both axes; a 16 × 16 window around both; a step costs
    /// 10, plus 1 when its direction differs from the step before; the estimate is 10 × the Chebyshev distance; the open node of
    /// least cost + estimate expands next (the lowest window index of equals); a node whose (cost + estimate) / 10 exceeds
    /// <paramref name="max"/> is dropped; a cheaper cost reopens a closed node. The path's directions are written at
    /// <paramref name="at"/>; null when there is none or it is longer than <paramref name="max"/>.
    /// </summary>
    [OriginalFunction(0x4209C0, OriginalFunctionScope.Full)]
    private int? Search(TravelSector from, TravelSector to, int at, int max, TravelSectorPasses passes)
    {
        long dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
        if (dx >= 8 || dy >= 8) return null;
        long ox = Math.Min(from.X, to.X) - (Window - dx) / 2, oy = Math.Min(from.Y, to.Y) - (Window - dy) / 2;
        const int Blocked = unchecked((int)0xFFFF8000);
        var cost = new int[Window * Window];
        var parent = new int[Window * Window];
        Array.Fill(parent, -1);
        int Index(TravelSector s) => checked((int)((s.Y - oy) * Window + (s.X - ox)));
        TravelSector At(int index) => new(ox + index % Window, oy + index / Window);
        var start = Index(from); var goal = Index(to);
        cost[start] = 1;
        while (true)
        {
            var best = -1; var bestF = 0;
            for (var cell = 0; cell < cost.Length; cell++)
            {
                if (cost[cell] <= 0) continue;
                var f = cost[cell] + Estimate(cell, goal);
                if ((uint)(f / 10) > (uint)max) { cost[cell] = Blocked; continue; }
                if (best == -1 || f < bestF) (best, bestF) = (cell, f);
            }
            if (best == -1) return null;
            if (best == goal) break;
            var here = At(best);
            for (var direction = 0; direction < 8; direction++)
            {
                var next = WorldTravelGeometry.Neighbor(here, direction);
                if (next.X < ox || next.X >= ox + Window || next.Y < oy || next.Y >= oy + Window) continue;
                var cell = Index(next);
                if (cost[cell] == Blocked) continue;
                if (!passes(next)) { cost[cell] = Blocked; continue; }
                var g = cost[best] + 10;
                if (parent[best] != -1 && WindowDirection(parent[best], best) != direction) g++;
                var old = cost[cell];
                if (old == 0 || (old > 0 && old > g) || (old < 0 && -old > g)) { cost[cell] = g; parent[cell] = best; }
            }
            cost[best] = -cost[best];
        }
        var steps = 0;
        for (var cell = parent[goal]; cell != -1; cell = parent[cell]) steps++;
        if (steps > max) return null;
        var walk = goal;
        for (var step = steps - 1; step >= 0; step--) { _buffer[at + step] = (byte)WindowDirection(parent[walk], walk); walk = parent[walk]; }
        return steps;
    }

    // 10 × the Chebyshev distance of two window cells (0x4200C0).
    [OriginalFunction(0x4200C0, OriginalFunctionScope.Full)]
    internal static int Estimate(int a, int b) => 10 * Math.Max(Math.Abs(a % Window - b % Window), Math.Abs(a / Window - b / Window));

    // The direction between two window cells (0x420110): the signs of the two deltas alone. Unlike the sector direction
    // (0x4CFCD0, WorldTravelGeometry.Direction) it has no minor-axis rule: the oracle (ARC-DEV-1105) found the function reads
    // 8 octants of the plane, so a step of (10, 1) is a diagonal. The same cell gives 5.
    [OriginalFunction(0x420110, OriginalFunctionScope.Full)]
    internal static int WindowDirection(int from, int to)
    {
        int sx = Math.Sign(to % Window - from % Window), sy = Math.Sign(to / Window - from / Window);
        for (var code = 0; code < 8; code++)
            if (WorldTravelGeometry.DeltaX[code] == sx && WorldTravelGeometry.DeltaY[code] == sy) return code;
        return 5;
    }
}
