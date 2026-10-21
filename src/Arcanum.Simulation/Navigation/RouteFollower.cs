using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Navigation;

public enum RouteFollowStatus { Idle, Following, Arrived, Stopped }

/// <summary>One bounded advance the host must run next: advance to <see cref="Until"/> with exactly these batches.</summary>
public sealed record RouteSubAdvance(SimulationTime Until, IReadOnlyList<InputBatch> Batches);

/// <summary>Every sub-advance one drive ran, in order; a failed drive stops at the first non-completed result.</summary>
public sealed record RouteDrive(MovementState State, IReadOnlyList<MovementResult> Advances, string? FailureCode)
{
    public bool Completed => FailureCode is null;
}

/// <summary>
/// Click-to-move goal follower over <see cref="MovementRuntime"/> (Project policy, parity NOT_VERIFIED).
/// A click sets a goal cell; the follower turns <see cref="RoutePlanner"/> segments into ordinary actor intents
/// at exact step boundaries, plans again after each segment and never cancels a step it started: a new goal or a
/// stop takes effect at the next cell centre. It owns no clock, I/O or evidence; the host runs every advance.
/// ARC-WORLD-042: with corridor limits given (a host opts in; the player's click route does, a companion follow does not),
/// a goal of the source the local planner cannot reach (beyond its 32-cell reach, or behind its window's edge or expansion
/// limit) is followed along a <see cref="RouteCorridor"/> over the whole source, from waypoint to waypoint with the local
/// planner; a leg that fails on the way (an occupant on a waypoint) builds the corridor again once from where the actor
/// stands; a corridor that only nears the goal (no route reaches it) stops at its end with <c>ROUTE_PARTIAL</c>, and one
/// that reaches nothing stops with its own reason. A goal outside the source stays refused; a host heads for one through
/// the end of its own partial corridor (a streamed window, ARC-RENDER-024).
/// </summary>
public sealed class RouteFollower
{
    public const int SubAdvanceMaximum = 1024;
    private readonly IRouteStepSource _source;
    private readonly RouteLimits _limits;
    private NeighborDirection[] _segment = [];
    private int _segmentNext;
    private CellCoord? _segmentCell;
    private SimulationTime? _requestedAt;
    private InputKey? _issued;

    public RouteFollower(IRouteStepSource source, RouteLimits? limits = null, RouteCorridorLimits? corridorLimits = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _limits = limits ?? new RouteLimits();
        _corridorLimits = corridorLimits;
    }
    private readonly RouteCorridorLimits? _corridorLimits;
    private int _waypointNext;
    private bool _corridorRebuilt;
    /// <summary>The corridor followed toward the current goal (ARC-WORLD-042), null while the local planner reaches it.</summary>
    public RouteCorridor? Corridor { get; private set; }

    public RouteFollowStatus Status { get; private set; }
    public CellCoord? Target { get; private set; }
    /// <summary>Chebyshev distance from <see cref="Target"/> at which the goal counts as reached (0: on the goal cell).
    /// A companion follow goal (ARC-WORLD-016) stops within its range of the leader; a click goal uses 0.</summary>
    public int Range { get; private set; }
    public string? StopReason { get; private set; }
    /// <summary>The newest plan made for the current goal, including a failed one.</summary>
    public RoutePlan? LastPlan { get; private set; }
    public int PlannedSegments { get; private set; }
    public IReadOnlyList<NeighborDirection> RemainingSegment => Array.AsReadOnly(_segment[_segmentNext..]);

    /// <summary>Sets a goal observed at <paramref name="observedAt"/>, reached when the actor settles within
    /// <paramref name="range"/> cells of it. The goal already followed with the same range is kept (no replan, no duplicate).</summary>
    public bool SetTarget(CellCoord target, SimulationTime observedAt, int range = 0)
    {
        if (!observedAt.IsValid) throw new ArgumentException("ROUTE_TIME_INVALID", nameof(observedAt));
        if (range is < 0 or > RouteLimits.AxisDistanceMaximum) throw new ArgumentOutOfRangeException(nameof(range));
        if (Status == RouteFollowStatus.Following && Target == target && Range == range) return false;
        Target = target; Range = range; Status = RouteFollowStatus.Following; StopReason = null; LastPlan = null; PlannedSegments = 0;
        _requestedAt = observedAt; ResetSegment(); Corridor = null; _waypointNext = 0; _corridorRebuilt = false;
        return true;
    }

    /// <summary>Drops the goal; a started step still finishes at its cell centre.</summary>
    public void Stop(SimulationTime observedAt, string reason = "ROUTE_STOPPED_BY_HOST")
    {
        if (!observedAt.IsValid) throw new ArgumentException("ROUTE_TIME_INVALID", nameof(observedAt));
        ArgumentException.ThrowIfNullOrEmpty(reason);
        if (Status != RouteFollowStatus.Following) return;
        Finish(RouteFollowStatus.Stopped, reason); _requestedAt = observedAt;
    }

    /// <summary>
    /// The next advance toward <paramref name="target"/>. Decisions are made only at a settled cell or at the exact end
    /// of the current step, for the cell the actor will stand on, so no input lands inside a step. Planning happens
    /// here, so <see cref="LastPlan"/>, <see cref="PlannedSegments"/> and a plan failure are recorded even if the host
    /// does not run the returned advance. While following, the goal owns movement; otherwise the follower clears only an
    /// intent it issued itself. Observation times gate a settled actor only: a goal change made during a step applies at
    /// that step's end.
    /// </summary>
    public RouteSubAdvance Next(MovementState state, SimulationTime target)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!target.IsValid || target.Value < state.Time.Value) throw new ArgumentException("ROUTE_TARGET_TIME_INVALID", nameof(target));
        if (state.Segment is { } segment)
        {
            if (segment.EndTime.Value > target.Value) return new(target, []);
            // While following, the click owns movement; afterwards only an intent the follower issued is cleared.
            // With nothing of its own to decide, the follower never splits another source's advance at a step end.
            if (Status != RouteFollowStatus.Following && !Owned(state)) return new(target, []);
            var intent = Desired(state.Phase == MovementPhase.Returning ? segment.From : segment.To);
            if (intent == state.LatestIntent) return new(segment.EndTime, []);
            return new(segment.EndTime, [Issue(state, segment.EndTime, intent)]);
        }
        var owned = Status == RouteFollowStatus.Following || Owned(state);
        var desired = Desired(state.CommittedCell);
        if (!desired.Active && !(state.LatestIntent.Active && owned)) return new(target, []);
        // A settled actor takes the input when the goal was observed, or at the target if that time is already closed.
        var at = target;
        if (_requestedAt is { } requested && Admissible(state, requested))
        {
            if (requested.Value > target.Value) return new(target, []); // Not observed yet within this advance.
            at = requested;
        }
        if (!Admissible(state, at)) return new(target, []);
        // A settled actor with an active intent was denied its last step: clear it first so the new one is material.
        return new(at, [desired.Active && state.LatestIntent.Active ? Issue(state, at, ActorIntent.Neutral, desired) : Issue(state, at, desired)]);
    }

    /// <summary>Records one completed advance: consumed segment steps, denials and arrival.</summary>
    public void Observe(MovementResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Status != MovementStatus.Completed || result.State is null) throw new ArgumentException("ROUTE_RESULT_NOT_COMPLETED", nameof(result));
        foreach (var e in result.Events)
        {
            if (Status != RouteFollowStatus.Following) break;
            if (e.Kind == MovementEventKind.StepStarted)
            {
                if (_segmentCell == e.Key.From && _segmentNext < _segment.Length && _segment[_segmentNext] == e.Key.Direction)
                { _segmentNext++; _segmentCell = e.Key.To; }
                else ResetSegment(); // Someone else moved the actor: plan again from where it stands.
            }
            else if (e.Kind == MovementEventKind.StepRejected) Finish(RouteFollowStatus.Stopped, "ROUTE_STEP_DENIED");
        }
        var state = result.State;
        if (Status == RouteFollowStatus.Following && state.Phase == MovementPhase.Settled && Reached(state.CommittedCell))
            Finish(RouteFollowStatus.Arrived, "ROUTE_ARRIVED");
        if (Status != RouteFollowStatus.Following && !state.LatestIntent.Active) _requestedAt = null;
    }

    /// <summary>Runs <see cref="Next"/>/<see cref="Observe"/> until <paramref name="target"/> through the host's advance.</summary>
    public RouteDrive Drive(MovementState state, SimulationTime target, Func<SimulationTime, IReadOnlyList<InputBatch>, MovementResult> advance)
    {
        ArgumentNullException.ThrowIfNull(advance);
        var results = new List<MovementResult>();
        for (var i = 0; i < SubAdvanceMaximum; i++)
        {
            var step = Next(state, target);
            var result = advance(step.Until, step.Batches);
            results.Add(result);
            if (result.Status != MovementStatus.Completed || result.State is null) return new(state, results.AsReadOnly(), "ROUTE_ADVANCE_REJECTED");
            Observe(result);
            state = result.State;
            if (step.Until == target) return new(state, results.AsReadOnly(), null);
        }
        return new(state, results.AsReadOnly(), "ROUTE_SUB_ADVANCE_LIMIT");
    }

    private ActorIntent Desired(CellCoord cell)
    {
        if (Status != RouteFollowStatus.Following || Target is not { } goal || Reached(cell)) return ActorIntent.Neutral;
        if (_segmentCell != cell || _segmentNext == _segment.Length)
        {
            var local = LocalGoal(cell, goal);
            if (local is null) { Finish(RouteFollowStatus.Stopped, "ROUTE_PARTIAL"); return ActorIntent.Neutral; }
            var plan = RoutePlanner.Plan(_source, cell, local.Value, _limits);
            // Beyond the local reach: a corridor over the whole source, once per goal; a leg along it that fails (an occupant
            // on its waypoint) builds it once more from here.
            if (plan.Outcome != RouteOutcome.Found && _corridorLimits is { } corridorLimits
                && (Corridor is null ? CorridorReasons.Contains(plan.ReasonCode) : !_corridorRebuilt))
            {
                _corridorRebuilt = Corridor is not null;
                Corridor = RouteCorridor.Plan(_source, cell, goal, corridorLimits); _waypointNext = 0;
                if (Corridor.Outcome is RouteCorridorOutcome.NoRoute or RouteCorridorOutcome.Refused)
                { LastPlan = plan; Finish(RouteFollowStatus.Stopped, Corridor.ReasonCode); return ActorIntent.Neutral; }
                if (LocalGoal(cell, goal) is not { } waypoint) { Finish(RouteFollowStatus.Stopped, "ROUTE_PARTIAL"); return ActorIntent.Neutral; }
                plan = RoutePlanner.Plan(_source, cell, waypoint, _limits);
            }
            LastPlan = plan;
            if (plan.Outcome != RouteOutcome.Found) { Finish(RouteFollowStatus.Stopped, plan.ReasonCode); return ActorIntent.Neutral; }
            _segment = plan.Segment.ToArray(); _segmentNext = 0; _segmentCell = cell; PlannedSegments++;
        }
        return ActorIntent.Toward(_segment[_segmentNext]);
    }

    // The local planner's refusals a longer corridor may get past (a route outside its window).
    private static readonly HashSet<string> CorridorReasons = new(StringComparer.Ordinal)
        { "ROUTE_TARGET_OUT_OF_RANGE", "ROUTE_WINDOW_EXHAUSTED", "ROUTE_EXPANSION_LIMIT" };

    /// <summary>The cell the local planner heads for: the goal, or along a corridor its next waypoint not yet reached; null
    /// at the end of a corridor that only nears the goal.</summary>
    private CellCoord? LocalGoal(CellCoord cell, CellCoord goal)
    {
        if (Corridor is not { } corridor) return goal;
        while (_waypointNext < corridor.Waypoints.Count && corridor.Waypoints[_waypointNext] == cell) _waypointNext++;
        if (_waypointNext < corridor.Waypoints.Count) return corridor.Waypoints[_waypointNext];
        return corridor.Outcome == RouteCorridorOutcome.Found ? goal : null;
    }

    private bool Reached(CellCoord cell) => Target is { } goal &&
        Math.Max(Math.Abs((long)goal.X - cell.X), Math.Abs((long)goal.Y - cell.Y)) <= Range;

    private void Finish(RouteFollowStatus status, string reason) { Status = status; StopReason = reason; ResetSegment(); }
    private void ResetSegment() { _segment = []; _segmentNext = 0; _segmentCell = null; }

    private static bool Admissible(MovementState state, SimulationTime time) =>
        state.IsClosed ? time.Value > state.Time.Value : time.Value >= state.Time.Value;

    // Ownership is the receipt key, not the intent value: another source that sets the same direction owns it.
    private bool Owned(MovementState state) => _issued is { } issued && state.LatestInputKey == issued;

    private InputBatch Issue(MovementState state, SimulationTime time, params ActorIntent[] intents)
    {
        var batch = Batch(state, time, intents);
        _issued = new InputKey(time, batch.Entries[^1].Sequence);
        return batch;
    }

    private static InputBatch Batch(MovementState state, SimulationTime time, params ActorIntent[] intents)
    {
        // Keys stay strictly after the latest receipt even when it shares this timestamp.
        var sequence = state.LatestInputKey is { } latest ? checked(latest.Sequence + 1) : 0;
        var inputs = new MovementInput[intents.Length];
        for (var i = 0; i < intents.Length; i++) inputs[i] = MovementInput.Set(checked(sequence + i), intents[i]);
        return new(time, inputs);
    }
}
