using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Navigation;

/// <summary>
/// Synthetic chart: '.' open, '#' blocked, '?' unknown destination evidence; cells off the rows are outside the area.
/// A diagonal step whose shoulder is '#' is Blocked, mirroring object legs on diagonal shoulders.
/// </summary>
internal sealed class GridSource(params string[] rows) : IRouteStepSource
{
    public Dictionary<(CellCoord, NeighborDirection), int> Asked { get; } = [];
    public Func<CellCoord, NeighborDirection, RouteStepVerdict?>? Override { get; set; }
    public int OriginX { get; init; }
    public int OriginY { get; init; }
    private char At(CellCoord c)
    {
        int x = c.X - OriginX, y = c.Y - OriginY;
        return y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length ? rows[y][x] : ' ';
    }
    public bool Contains(CellCoord cell) => At(cell) != ' ';
    public RouteStepVerdict Assess(CellCoord from, NeighborDirection direction)
    {
        Asked[(from, direction)] = Asked.GetValueOrDefault((from, direction)) + 1;
        if (Override?.Invoke(from, direction) is { } forced) return forced;
        var to = RoutePlanner.Neighbor(from, direction);
        if (!Contains(from) || !Contains(to)) return RouteStepVerdict.Unsupported;
        if (((int)direction & 1) == 0)
        {
            var ccw = RoutePlanner.Neighbor(from, (NeighborDirection)(((int)direction + 7) % 8));
            var cw = RoutePlanner.Neighbor(from, (NeighborDirection)((int)direction + 1));
            if (!Contains(ccw) || !Contains(cw)) return RouteStepVerdict.Unsupported;
            if (At(ccw) == '#' || At(cw) == '#') return RouteStepVerdict.Blocked;
        }
        return At(to) switch { '#' => RouteStepVerdict.Blocked, '?' => RouteStepVerdict.Unknown, _ => RouteStepVerdict.Open };
    }
    public static GridSource Open(int width, int height) => new(Enumerable.Repeat(new string('.', width), height).ToArray());
}

/// <summary>Authoritative host: each advance replays the runtime and supplies the source's verdict for every opportunity.</summary>
internal sealed class RouteHost(IRouteStepSource admission, CellCoord start, StepDuration duration)
{
    public MovementState State { get; private set; } = Fixtures.Initial(start);
    public List<MovementEvent> Events { get; } = [];
    public MovementResult Advance(SimulationTime until, IReadOnlyList<InputBatch> batches)
    {
        var permits = new List<StepAssessmentTiming>();
        while (true)
        {
            var result = MovementRuntime.AdvanceTo(State, new(until, batches.ToArray(), permits.ToArray()));
            Assert.AreEqual(MovementStatus.Completed, result.Status, result.Failure?.ToString());
            var missing = result.Events.FirstOrDefault(e => e.Kind == MovementEventKind.StepRejected && e.Reason == "MissingAssessment");
            if (missing is null) { State = result.State!; Events.AddRange(result.Events); return result; }
            var key = missing.Key;
            permits.Add(admission.Assess(key.From, key.Direction) switch
            {
                RouteStepVerdict.Open => new(key, AssessmentCoverage.Supported, AssessmentDisposition.Open, duration, ["OPEN"], ["ROUTE_TEST"]),
                RouteStepVerdict.Blocked => new(key, AssessmentCoverage.Supported, AssessmentDisposition.Blocked, null, ["BLOCKED"], ["ROUTE_TEST"]),
                RouteStepVerdict.Unknown => new(key, AssessmentCoverage.Unknown, null, null, ["UNKNOWN"], ["ROUTE_TEST"]),
                _ => new(key, AssessmentCoverage.Unsupported, null, null, ["UNSUPPORTED"], ["ROUTE_TEST"]),
            });
        }
    }
    /// <summary>Frames of 1/<paramref name="perUnit"/> time units from the current watermark.</summary>
    public RouteDrive Frames(RouteFollower follower, int frames, int perUnit = 4)
    {
        RouteDrive? last = null;
        for (var i = 0; i < frames; i++)
        {
            var target = new SimulationTime(State.Time.Value + ExactScalar.Create(1, perUnit));
            last = follower.Drive(State, target, Advance);
            Assert.IsTrue(last.Completed, last.FailureCode);
        }
        return last!;
    }
    public CellCoord[] Committed() => Events.Where(e => e.Kind == MovementEventKind.CellCommitted).Select(e => e.Key.To).ToArray();
}
