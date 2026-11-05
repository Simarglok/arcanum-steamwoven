using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;

namespace Arcanum.Simulation.Party;

/// <summary>What a follower does at one decision. <see cref="Walk"/> and <see cref="Run"/> both approach the leader;
/// <see cref="Run"/> is the source's run-near goal; the host chooses the step duration for it (the AREA viewer's run gait,
/// ARC-WORLD-020).</summary>
public enum FollowAction { Hold, Walk, Run, Jump, Separate }

/// <summary>
/// Follow distances of one companion (ARC-RES-024 C7–C10, docs/world/companion-follow-runtime.md,
/// F0; parity NOT_VERIFIED). Distances are Chebyshev cell distances.
/// A follower starts approaching when the leader is more than <see cref="StartDistance"/> cells away
/// (<see cref="SpreadOutStartDistance"/> when spread out), stops once within <see cref="Range"/>
/// (<see cref="SpreadOutRange"/>), runs beyond range + <see cref="WalkSlack"/>, is moved onto the leader's cell beyond
/// <see cref="JumpDistance"/> and steps off a cell it shares with the leader.
/// </summary>
public static class FollowDistanceRules
{
    public const int StartDistance = 4, SpreadOutStartDistance = 7, Range = 2, SpreadOutRange = 5, WalkSlack = 2, JumpDistance = 30;

    public static long Distance(CellCoord a, CellCoord b) => Math.Max(Math.Abs((long)a.X - b.X), Math.Abs((long)a.Y - b.Y));
    public static int StartOf(bool spreadOut) => spreadOut ? SpreadOutStartDistance : StartDistance;
    public static int RangeOf(bool spreadOut) => spreadOut ? SpreadOutRange : Range;

    /// <summary>The decision for a follower at <paramref name="follower"/>. <paramref name="approaching"/> is true while
    /// a follow goal is alive: it then holds only within range, otherwise it starts only beyond the start distance.</summary>
    public static FollowAction Decide(CellCoord follower, CellCoord leader, bool spreadOut, bool approaching)
    {
        var distance = Distance(follower, leader);
        if (distance > JumpDistance) return FollowAction.Jump;
        if (distance == 0) return FollowAction.Separate;
        if (distance <= (approaching ? RangeOf(spreadOut) : StartOf(spreadOut))) return FollowAction.Hold;
        return distance > RangeOf(spreadOut) + WalkSlack ? FollowAction.Run : FollowAction.Walk;
    }
}

/// <summary>One decision: the action, and for <see cref="FollowAction.Jump"/> the cell the host moves the follower to.</summary>
public readonly record struct CompanionFollowUpdate(FollowAction Action, CellCoord? JumpTo);

/// <summary>
/// Follow movement of one companion (ARC-WORLD-016, slice 3 of ARC-RES-024; project policy over the source distances).
/// It turns <see cref="FollowDistanceRules"/> into a <see cref="RouteFollower"/> goal toward the leader's cell with the
/// follow range, so the follower walks through the same bounded planner, exact movement and step evidence as a click
/// route, and a started step is never cancelled. The host calls <see cref="Update"/> once per frame with the leader's
/// committed cell, then <see cref="Drive"/> to the same frame boundary. Decisions use the leader cell of that call (a
/// frame, not the source's 100 ms re-check). A goal the planner cannot reach is not asked again until the follower or
/// the leader stands on another cell. The jump itself is the host's: it stands the follower settled on
/// <see cref="CompanionFollowUpdate.JumpTo"/>. Running speed is the host's step duration. No combat, turn-based cost or loyalty check.
/// ARC-WORLD-075: with <c>segmentEndCritterCheck</c> answering yes the route checks the cell before a segment's last step for a
/// living critter (the walk goals' step check, ledger EX20 and LR5); the original's approach walk (goal 61) then pops back to its
/// parent, which plans again, so a goal stopped there (<see cref="SegmentEndCritterReason"/>) is not remembered as unreachable and the
/// next update goes on approaching from where the follower stands (beyond the range it plans again, whatever the start distance): the
/// critter is then on the new plan's first step and is passed, so the follower pauses one frame before it (the original's pause is 2
/// action points and a random 0..300 ms wait, not modelled). An ordered goal (<see cref="WalkTo"/>) stopped there ends, as the walk-here
/// order's goal 4 does (LR5); the spread-out step's goal 69 would plan again in the original (an approximation).
/// </summary>
public sealed class CompanionFollower
{
    public const string NotFollowingReason = "FOLLOW_NOT_FOLLOWING";
    /// <summary>The route's stop before a segment's last step onto a living critter (<see cref="RouteFollower"/>, ledger LR5).</summary>
    public const string SegmentEndCritterReason = "ROUTE_SEGMENT_END_CRITTER";
    private static readonly NeighborDirection[] Directions = Enum.GetValues<NeighborDirection>();
    private readonly IRouteStepSource _source;
    private (CellCoord Follower, CellCoord Leader)? _unreachable;
    private bool _stopSeen = true;
    private bool _ordered;

    public CompanionFollower(IRouteStepSource source, RouteLimits? limits = null, Func<bool>? segmentEndCritterCheck = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        Route = new RouteFollower(source, limits, segmentEndCritterEndsWalk: segmentEndCritterCheck);
    }

    public RouteFollower Route { get; }
    public FollowAction LastAction { get; private set; } = FollowAction.Hold;
    /// <summary>The leader cell of the last update while following, else null.</summary>
    public CellCoord? Leader { get; private set; }
    /// <summary>The follower and leader cells at which the last goal ended without arriving (no route, step denied).</summary>
    public (CellCoord Follower, CellCoord Leader)? Unreachable => _unreachable;

    /// <summary>Whether an ordered goal (<see cref="WalkTo"/>) is running.</summary>
    public bool Ordered => _ordered;

    /// <summary>
    /// ARC-WORLD-046: an ordered goal to <paramref name="tile"/> (the walk-here order's run-to-tile goal, or the host's spread-out step):
    /// while it runs, the follow decisions do not replace it; once it arrives or stops, the follower follows again from where it
    /// stands (beyond the start distance it approaches, OL9). False when the target is not in the step source.
    /// </summary>
    public bool WalkTo(CellCoord tile, SimulationTime at)
    {
        if (!at.IsValid) throw new ArgumentException("FOLLOW_TIME_INVALID", nameof(at));
        if (!_source.Contains(tile)) return false;
        _ordered = true; _unreachable = null;
        Retarget(tile, at, 0);
        return true;
    }

    /// <summary>
    /// Decides at <paramref name="observedAt"/> (the frame boundary the host drives to next). <paramref name="following"/>
    /// is the session standing: a waiting or free NPC drops its goal (a started step still ends at its cell centre).
    /// </summary>
    public CompanionFollowUpdate Update(MovementState state, CellCoord leader, bool spreadOut, bool following, SimulationTime observedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!observedAt.IsValid) throw new ArgumentException("FOLLOW_TIME_INVALID", nameof(observedAt));
        if (!following)
        {
            Route.Stop(observedAt, NotFollowingReason);
            Leader = null; _unreachable = null; _stopSeen = true; _ordered = false;
            return Done(FollowAction.Hold);
        }
        Leader = leader;
        if (_ordered)
        {
            // An ordered goal is not replaced by the follow decisions; when it ends the follower decides again from where it stands.
            if (Route.Status == RouteFollowStatus.Following) return Done(FollowAction.Run);
            _ordered = false; _stopSeen = true;
            if (state.Phase != MovementPhase.Settled) return Done(FollowAction.Hold);
        }
        var settled = state.Phase == MovementPhase.Settled;
        var cell = state.CommittedCell;
        // A goal that ended without arriving is remembered for the cells it ended at; an approach stopped before a critter goes on
        // approaching: its parent plans again (EX20, LR11).
        var resume = false;
        if (!_stopSeen && Route.Status == RouteFollowStatus.Stopped && settled)
        {
            if (Route.StopReason != SegmentEndCritterReason) _unreachable = (cell, Route.Target ?? leader);
            else resume = Route.Range > 0;
            _stopSeen = true;
        }
        var range = FollowDistanceRules.RangeOf(spreadOut);
        if (Route.Status == RouteFollowStatus.Following)
        {
            var action = FollowDistanceRules.Decide(cell, leader, spreadOut, approaching: true);
            if (action == FollowAction.Jump && settled) return Jump(observedAt, leader);
            // A separation goal (range 0) ends by itself; an approach follows the leader's current cell and range.
            if (Route.Range == 0) return Done(FollowAction.Separate);
            Retarget(leader, observedAt, range);
            return Done(action == FollowAction.Run ? FollowAction.Run : FollowAction.Walk);
        }
        if (!settled) return Done(FollowAction.Hold);
        var decision = FollowDistanceRules.Decide(cell, leader, spreadOut, approaching: resume);
        switch (decision)
        {
            case FollowAction.Jump:
                return Jump(observedAt, leader);
            case FollowAction.Separate:
                foreach (var direction in Directions)
                {
                    var next = RoutePlanner.Neighbor(cell, direction);
                    // A neighbour holding a living critter is open to a mover that passes critters but refused as a goal (DC2).
                    if (!_source.Contains(next) || _source.Assess(cell, direction) != RouteStepVerdict.Open || _source.GoalHoldsCritter(next)) continue;
                    Retarget(next, observedAt, 0);
                    return Done(FollowAction.Separate);
                }
                return Done(FollowAction.Hold);
            case FollowAction.Walk or FollowAction.Run:
                if (_unreachable == (cell, leader)) return Done(FollowAction.Hold);
                _unreachable = null;
                Retarget(leader, observedAt, range);
                return Done(decision);
            default:
                return Done(FollowAction.Hold);
        }
    }

    /// <summary>Runs the follow goal to <paramref name="target"/> through the host's advance (see <see cref="RouteFollower.Drive"/>).</summary>
    public RouteDrive Drive(MovementState state, SimulationTime target, Func<SimulationTime, IReadOnlyList<InputBatch>, MovementResult> advance)
        => Route.Drive(state, target, advance);

    private void Retarget(CellCoord target, SimulationTime observedAt, int range)
    {
        if (Route.SetTarget(target, observedAt, range)) _stopSeen = false;
    }

    /// <summary>
    /// ARC-DEV-105: the AI host moves the follower itself (the catch-up 0x4AD4D0 interrupts its goals at priority 4 before the move 0x43E770):
    /// the route stops and an ordered goal ends, as after the follow decision's own jump.
    /// </summary>
    public void Interrupt(SimulationTime at)
    {
        if (!at.IsValid) throw new ArgumentException("FOLLOW_TIME_INVALID", nameof(at));
        Route.Stop(at, "FOLLOW_JUMPED");
        _unreachable = null; _stopSeen = true; _ordered = false;
    }

    private CompanionFollowUpdate Jump(SimulationTime observedAt, CellCoord leader)
    {
        Route.Stop(observedAt, "FOLLOW_JUMPED");
        _unreachable = null; _stopSeen = true;
        LastAction = FollowAction.Jump;
        return new(FollowAction.Jump, leader);
    }

    private CompanionFollowUpdate Done(FollowAction action)
    {
        LastAction = action;
        return new(action, null);
    }
}
