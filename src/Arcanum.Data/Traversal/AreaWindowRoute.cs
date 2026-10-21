using Arcanum.Data.Area;
using Arcanum.Data.Content;
using Arcanum.Data.Maps;

namespace Arcanum.Data.Traversal;

public enum AreaWindowRouteStatus { Found, NoRoute, LimitExceeded, Refused }

/// <summary>Limits of a window route; each must be positive and at most its policy maximum.</summary>
public sealed class AreaWindowRouteLimits
{
    public const int MaxWindowLoadsPolicy = 1024, MaxExpansionsPolicy = 8_000_000, MaxResidentWindowsPolicy = 64;

    public AreaWindowRouteLimits(int maxWindowLoads = 256, int maxExpansions = 1_000_000, int maxResidentWindows = 24)
    {
        if (maxWindowLoads is < 1 or > MaxWindowLoadsPolicy) throw new ArgumentOutOfRangeException(nameof(maxWindowLoads));
        if (maxExpansions is < 1 or > MaxExpansionsPolicy) throw new ArgumentOutOfRangeException(nameof(maxExpansions));
        if (maxResidentWindows is < 1 or > MaxResidentWindowsPolicy) throw new ArgumentOutOfRangeException(nameof(maxResidentWindows));
        MaxWindowLoads = maxWindowLoads; MaxExpansions = maxExpansions; MaxResidentWindows = maxResidentWindows;
    }
    /// <summary>Windows the search may load (a reload after eviction counts again).</summary>
    public int MaxWindowLoads { get; }
    public int MaxExpansions { get; }
    /// <summary>Window step evidence kept at once (least recently used dropped first).</summary>
    public int MaxResidentWindows { get; }
}

/// <summary>A route's cells from its start to its end (both included; empty without a route), the windows it loaded and
/// its expansions.</summary>
public sealed class AreaWindowRouteResult
{
    internal AreaWindowRouteResult(AreaWindowRouteStatus status, string detail, IReadOnlyList<AreaStepCell> cells, int windowLoads, int expansions)
    { Status = status; Detail = detail; Cells = cells; WindowLoads = windowLoads; Expansions = expansions; }
    public AreaWindowRouteStatus Status { get; }
    public string Detail { get; }
    public IReadOnlyList<AreaStepCell> Cells { get; }
    public int WindowLoads { get; }
    public int Expansions { get; }
}

/// <summary>
/// ARC-WORLD-042: a walking route across a map too large to load whole (the main world map), by the window step evidence
/// (ARC-DATA-032, ARC-DATA-034): an eight-direction search of one cell per step (Chebyshev estimate) whose every step is
/// assessed by a window whose core holds both of its cells, loaded on demand around the step's sector and kept in a
/// bounded cache. The route is the step evidence's own: a step is taken only when the window assesses it Open. It is a
/// planning aid (a long walk's waypoints), not the original's path search (parity NOT_VERIFIED); routes within one window
/// are the route follower's (<c>RouteCorridor</c>). ARC-WORLD-060: a host whose steps differ from the T0 evidence (objects the
/// session destroyed or switched off, which the original's object lists skip, ledger FC2) passes the verdict it walks by
/// (<c>stepOpen</c>, over the window's assessment), so the route reads the same state as the walk.
/// </summary>
public static class AreaWindowRoute
{
    private static readonly CardinalTraversalDirection[] Directions = Enum.GetValues<CardinalTraversalDirection>();

    /// <param name="stepOpen">Whether a step the window assessed is walkable (the assessment, the step's cell and direction); by default
    /// its T0 disposition is Open.</param>
    public static AreaWindowRouteResult Plan(ContentStack stack, WorldCatalog catalog, string mapName, AreaStepCell from, AreaStepCell to,
        AreaWindowRouteLimits? limits = null, CancellationToken token = default,
        Func<AreaStepAssessment, AreaStepCell, CardinalTraversalDirection, bool>? stepOpen = null)
    {
        stepOpen ??= static (assessment, _, _) => assessment.Disposition == CardinalTraversalDisposition.Open;
        ArgumentNullException.ThrowIfNull(stack); ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(mapName);
        limits ??= new();
        var windows = new Windows(stack, catalog, mapName, limits);
        if (from == to) return new(AreaWindowRouteStatus.Found, "ROUTE_ALREADY_AT_TARGET", Array.AsReadOnly(new[] { from }), 0, 0);
        var expansions = 0;
        try
        {
            if (windows.For(from, from) is not { } first || !first.IsAdmitted(from))
                return new(AreaWindowRouteStatus.Refused, "ROUTE_START_NOT_ADMITTED", [], windows.Loads, 0);
            // A goal no window admits, or no step enters, is refused before the search floods the map toward it.
            if (windows.For(to, to) is not { } last || !last.IsAdmitted(to))
                return new(AreaWindowRouteStatus.Refused, "ROUTE_GOAL_NOT_ADMITTED", [], windows.Loads, 0);
            if (!Directions.Any(direction => Entered(windows, Step(to, direction), to, stepOpen)))
                return new(AreaWindowRouteStatus.Refused, "ROUTE_GOAL_UNENTERABLE", [], windows.Loads, 0);
            long Estimate(AreaStepCell c) => Math.Max(Math.Abs(c.X - to.X), Math.Abs(c.Y - to.Y));
            var cost = new Dictionary<AreaStepCell, long> { [from] = 0 };
            var parent = new Dictionary<AreaStepCell, AreaStepCell>();
            var closed = new HashSet<AreaStepCell>();
            var open = new PriorityQueue<AreaStepCell, (long Total, long Estimate)>();
            open.Enqueue(from, (Estimate(from), Estimate(from)));
            while (open.TryDequeue(out var cell, out var priority))
            {
                token.ThrowIfCancellationRequested();
                if (closed.Contains(cell) || priority.Total != cost[cell] + Estimate(cell)) continue;
                if (cell == to)
                {
                    var cells = new List<AreaStepCell>();
                    for (var at = to; ; at = parent[at]) { cells.Add(at); if (at == from) break; }
                    cells.Reverse();
                    return new(AreaWindowRouteStatus.Found, "ROUTE_WINDOWS", cells.AsReadOnly(), windows.Loads, expansions);
                }
                if (expansions == limits.MaxExpansions) return new(AreaWindowRouteStatus.LimitExceeded, "ROUTE_EXPANSION_LIMIT", [], windows.Loads, expansions);
                expansions++; closed.Add(cell);
                foreach (var direction in Directions)
                {
                    var next = Step(cell, direction);
                    if (closed.Contains(next)) continue;
                    var window = windows.For(cell, next);
                    if (window is null || !window.IsAdmitted(cell) || !window.IsAdmitted(next)) continue;
                    if (!stepOpen(window.Assess(window.Request(cell, next, direction)), cell, direction)) continue;
                    var total = cost[cell] + 1;
                    if (cost.TryGetValue(next, out var known) && known <= total) continue;
                    cost[next] = total; parent[next] = cell; open.Enqueue(next, (total + Estimate(next), Estimate(next)));
                }
            }
            return new(AreaWindowRouteStatus.NoRoute, "ROUTE_NOT_FOUND", [], windows.Loads, expansions);
        }
        catch (WindowLimitException) { return new(AreaWindowRouteStatus.LimitExceeded, "ROUTE_WINDOW_LIMIT", [], windows.Loads, expansions); }
        catch (AreaStepInvocationException error) { return new(AreaWindowRouteStatus.Refused, $"ROUTE_WINDOW_EVIDENCE_{error.Code}", [], windows.Loads, expansions); }
    }

    /// <summary>Whether the step from <paramref name="from"/> onto its neighbour <paramref name="to"/> is Open.</summary>
    private static bool Entered(Windows windows, AreaStepCell from, AreaStepCell to, Func<AreaStepAssessment, AreaStepCell, CardinalTraversalDirection, bool> stepOpen)
    {
        var direction = Directions.First(d => Step(from, d) == to);
        return windows.For(from, to) is { } window && window.IsAdmitted(from) && window.IsAdmitted(to)
            && stepOpen(window.Assess(window.Request(from, to, direction)), from, direction);
    }

    internal static AreaStepCell Step(AreaStepCell cell, CardinalTraversalDirection direction) => direction switch
    {
        CardinalTraversalDirection.XMinusYMinus => new(cell.X - 1, cell.Y - 1),
        CardinalTraversalDirection.XMinus => new(cell.X - 1, cell.Y),
        CardinalTraversalDirection.XMinusYPlus => new(cell.X - 1, cell.Y + 1),
        CardinalTraversalDirection.YPlus => new(cell.X, cell.Y + 1),
        CardinalTraversalDirection.XPlusYPlus => new(cell.X + 1, cell.Y + 1),
        CardinalTraversalDirection.XPlus => new(cell.X + 1, cell.Y),
        CardinalTraversalDirection.XPlusYMinus => new(cell.X + 1, cell.Y - 1),
        _ => new(cell.X, cell.Y - 1),
    };

    private sealed class WindowLimitException : Exception;

    /// <summary>Window step evidence by centre sector, loaded on demand; a sector with no window (outside the map, a plan that
    /// is not ready, or an incomplete load) has none and blocks the route in that sector only.</summary>
    private sealed class Windows(ContentStack stack, WorldCatalog catalog, string mapName, AreaWindowRouteLimits limits)
    {
        private readonly Dictionary<(long X, long Y), (AreaStepSnapshot? Steps, long Used)> _cache = new();
        private long _clock;
        public int Loads { get; private set; }

        /// <summary>A window whose core holds both cells' sectors: a resident one (one without evidence only for its own
        /// sector), else the one around the first cell's sector.</summary>
        public AreaStepSnapshot? For(AreaStepCell a, AreaStepCell b)
        {
            long ax = a.X >> 6, ay = a.Y >> 6, bx = b.X >> 6, by = b.Y >> 6;
            foreach (var (centre, entry) in _cache)
                if ((entry.Steps is not null || centre == (ax, ay)) && Math.Abs(ax - centre.X) <= 1 && Math.Abs(ay - centre.Y) <= 1 && Math.Abs(bx - centre.X) <= 1 && Math.Abs(by - centre.Y) <= 1)
                {
                    _cache[centre] = (entry.Steps, ++_clock);
                    return entry.Steps;
                }
            return Load(ax, ay);
        }

        private AreaStepSnapshot? Load(long x, long y)
        {
            if (Loads == limits.MaxWindowLoads) throw new WindowLimitException();
            Loads++;
            if (_cache.Count == limits.MaxResidentWindows) _cache.Remove(_cache.MinBy(p => p.Value.Used).Key);
            AreaStepSnapshot? steps = null;
            if (x is >= 0 and < int.MaxValue && y is >= 0 and < int.MaxValue)
            {
                var planned = AreaMapPlans.CreateWindow(stack, catalog, mapName, new((int)x, (int)y));
                if (planned.Status == AreaMapPlanStatus.Ready)
                {
                    using var owner = planned.Plan!.Open();
                    var inspection = owner.Inspect();
                    if (inspection.IsComplete) steps = AreaStepInspector.InspectWholeMap(inspection.Snapshot!, stack, planned.Plan.Map.AggregatePath, 1);
                }
            }
            _cache[(x, y)] = (steps, ++_clock);
            return steps;
        }
    }
}
