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
    /// <summary>Extra search cost of an Open step on top of the plain step's 10, 0..1000: a positive value marks a step across a
    /// window piece (ARC-WORLD-021), which the original's search costs 50 more (ledger DC4; since ARC-WORLD-073 the search adds
    /// those 50 whatever the positive value). Asked only for Open steps, at most once per step and plan, and must be deterministic
    /// within one plan. Such a step also ends the straight attempt. Default none: a wrapping source must forward it.</summary>
    int ExtraCost(CellCoord from, NeighborDirection direction) => 0;

    /// <summary>ARC-WORLD-073 (ledger DC4): whether a refused cardinal step was refused by an object other than a wall or portal,
    /// which drops the tile stepped onto from the search (no other step may then enter it). Asked only for refused cardinal steps.
    /// Default no.</summary>
    bool StepRefusedByObject(CellCoord from, NeighborDirection direction) => false;
}

public enum RouteOutcome { Found, AlreadyAtTarget, Refused, NoRoute }
/// <summary>How a found plan was made: the request's direct attempt, its search, or (ARC-WORLD-073, ledger LR4) the PC's one
/// straight-line retry after no path, whose steps are not tested when planned.</summary>
public enum RouteMethod { Direct, LocalSearch, StraightRetry }

/// <summary>Planner limits can be lowered, never raised above the policy maxima.</summary>
public sealed class RouteLimits
{
    /// <summary>Policy maxima (ARC-WORLD-073, the original's request, ledger LR1–LR3): the returned segment (8 rotations), the
    /// per-axis reach (32: the direct attempt's; the search's window is 64 × 64 and needs both axes under 32) and the search
    /// budget (200: a tile whose estimate over 10 exceeds it is dropped; a walk asks <c>min(4 × distance + 5, 200)</c>).</summary>
    public const int SegmentMaximum = 8, AxisDistanceMaximum = 32, BudgetMaximum = OriginalPathRequest.BufferRotations;

    public RouteLimits(int segmentSteps = SegmentMaximum, int axisDistance = AxisDistanceMaximum, int budget = BudgetMaximum)
    {
        if (segmentSteps is < 1 or > SegmentMaximum || axisDistance is < 1 or > AxisDistanceMaximum ||
            budget is < 1 or > BudgetMaximum) throw new ArgumentOutOfRangeException(nameof(segmentSteps));
        SegmentSteps = segmentSteps; AxisDistance = axisDistance; Budget = budget;
    }
    public int SegmentSteps { get; }
    public int AxisDistance { get; }
    public int Budget { get; }

    /// <summary>The walk's first budget toward a goal (ledger LR2, 0x426320): <c>min(4 × distance + 5, 200)</c>, the distance
    /// the larger axis.</summary>
    [OriginalFunction(0x426320, OriginalFunctionScope.Partial)]
    public static int WalkBudget(CellCoord from, CellCoord to) =>
        (int)Math.Min(4L * Math.Max(Math.Abs((long)to.X - from.X), Math.Abs((long)to.Y - from.Y)) + 5, BudgetMaximum);

    /// <summary>The same limits with another budget.</summary>
    public RouteLimits WithBudget(int budget) => new(SegmentSteps, AxisDistance, budget);
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
