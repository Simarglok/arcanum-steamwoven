using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Navigation;

/// <summary>Verdict of one directed adjacent step. Only <see cref="Open"/> is traversable; nothing else is promoted.</summary>
public enum RouteStepVerdict { Open, Blocked, Unknown, Unsupported }

/// <summary>
/// The caller's step evidence, the same evidence the movement runtime admits steps with.
/// Implementations must be deterministic and side-effect free for the duration of one plan.
/// </summary>
public interface IRouteStepSource
{
    /// <summary>Whether the cell belongs to the admitted chart. Outside cells are refused, not searched.</summary>
    bool Contains(CellCoord cell);
    RouteStepVerdict Assess(CellCoord from, NeighborDirection direction);
    /// <summary>Extra search cost of an Open step on top of the plain step's 10 (for example a window jump, +50 as the
    /// source's path search, ARC-WORLD-021), 0..1000. Asked only for Open steps, at most once per step and plan, and
    /// must be deterministic within one plan. A step with extra cost also ends the straight attempt. Default none: a
    /// wrapping source must forward it.</summary>
    int ExtraCost(CellCoord from, NeighborDirection direction) => 0;
}

public enum RouteOutcome { Found, AlreadyAtTarget, Refused, NoRoute }
public enum RouteMethod { Direct, LocalSearch }

/// <summary>Planner limits can be lowered, never raised above the policy maxima.</summary>
public sealed class RouteLimits
{
    /// <summary>Policy maxima: returned segment, per-axis reach (window is twice the reach), expanded cells.</summary>
    public const int SegmentMaximum = 8, AxisDistanceMaximum = 32, ExpansionMaximum = 4096;

    public RouteLimits(int segmentSteps = SegmentMaximum, int axisDistance = AxisDistanceMaximum, int expansions = ExpansionMaximum)
    {
        if (segmentSteps is < 1 or > SegmentMaximum || axisDistance is < 1 or > AxisDistanceMaximum ||
            expansions is < 1 or > ExpansionMaximum) throw new ArgumentOutOfRangeException(nameof(segmentSteps));
        SegmentSteps = segmentSteps; AxisDistance = axisDistance; Expansions = expansions;
    }
    public int SegmentSteps { get; }
    public int AxisDistance { get; }
    public int Expansions { get; }
    public int WindowSize => 2 * AxisDistance;
}

/// <summary>
/// Immutable plan from <see cref="From"/> to <see cref="Target"/>. <see cref="Steps"/> is the whole bounded route;
/// <see cref="Segment"/> is the prefix a follower may execute before it must plan again. A failed plan has no steps.
/// </summary>
public sealed class RoutePlan
{
    internal RoutePlan(CellCoord from, CellCoord target, RouteOutcome outcome, RouteMethod? method, string reason,
        NeighborDirection[] steps, int segmentSteps, int assessments, int expansions, int blocked, int unknown, int unsupported)
    {
        if ((outcome == RouteOutcome.Found) != (method.HasValue && steps.Length > 0)) throw new ArgumentException("Only a found route has a method and steps.");
        From = from; Target = target; Outcome = outcome; Method = method; ReasonCode = reason;
        Steps = Array.AsReadOnly(steps);
        Segment = Array.AsReadOnly(steps.Take(segmentSteps).ToArray());
        var cells = new CellCoord[steps.Length]; var cell = from;
        for (var i = 0; i < steps.Length; i++) cells[i] = cell = MovementGeometry.Neighbor(cell, steps[i]);
        if (steps.Length > 0 && cell != target) throw new ArgumentException("Route does not end at its target.");
        Cells = Array.AsReadOnly(cells);
        Assessments = assessments; Expansions = expansions;
        BlockedSteps = blocked; UnknownSteps = unknown; UnsupportedSteps = unsupported;
    }
    public CellCoord From { get; }
    public CellCoord Target { get; }
    public RouteOutcome Outcome { get; }
    public RouteMethod? Method { get; }
    public string ReasonCode { get; }
    public IReadOnlyList<NeighborDirection> Steps { get; }
    /// <summary>Cells entered by <see cref="Steps"/>, in order; the last one is the target.</summary>
    public IReadOnlyList<CellCoord> Cells { get; }
    public IReadOnlyList<NeighborDirection> Segment { get; }
    /// <summary>Step assessments asked of the source while planning (each directed step at most once).</summary>
    public int Assessments { get; }
    public int Expansions { get; }
    public int BlockedSteps { get; }
    public int UnknownSteps { get; }
    public int UnsupportedSteps { get; }
}
