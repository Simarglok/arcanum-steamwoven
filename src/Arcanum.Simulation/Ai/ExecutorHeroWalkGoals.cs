using System.Collections.Immutable;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Ai;

/// <summary>What a plan of the hero's walk answered (<see cref="IAiHeroWalkWorld.PlanWalk"/>).</summary>
public enum AiHeroPlanOutcome
{
    /// <summary>A path was planned.</summary>
    Found,
    /// <summary>The planner found no path (and, for the PC, its one straight-line retry found none either).</summary>
    NoPath,
    /// <summary>The destination is beyond the local planner's reach and the host follows a corridor to it (a Steamwoven walk the original does not have): the host's route follower takes the whole goal.</summary>
    Far,
}

/// <summary>A plan of the hero's walk: the cells the path enters in order (at most one segment is walked before the goal plans again); <see cref="Straight"/> marks the PC's straight-line retry (ledger LR4: the run's flag 0x400, every later plan is the straight line).</summary>
public sealed record AiHeroPlan(AiHeroPlanOutcome Outcome, ImmutableArray<CellCoord> Steps = default, bool Straight = false);

/// <summary>
/// ARC-DEV-149 (MIG13 S10b-2b, docs/architecture/goal-table.md S10b-2b): what the hero's walk goals need of the world. The hero is the owner <c>pc</c>, so every member answers for it: its tile (the cell it
/// stands on or is stepping to), the planner and the step ports, the turn the walk pushes. The action points of a step stay the grid's own step charge (a refused charge refuses the step, and a refused step ends the run for a PC).
/// </summary>
public interface IAiHeroWalkWorld
{
    /// <summary>The cell the hero stands on, or the cell its step under way enters (a step that began is not cancelled); null when it has none on the map.</summary>
    CellCoord? Tile();
    /// <summary>The hero's encumbrance level (the run is refused from <see cref="AiExecutorRules.RunRefusedEncumbrance"/>).</summary>
    int Encumbrance { get; }
    /// <summary>The hero is down (falling, unconscious, getting up, dead): it takes no step (the hero's own hosts get it up).</summary>
    bool Down { get; }
    /// <summary>A turn-based round runs (the no-path wait ends the run at once).</summary>
    bool InRound { get; }
    /// <summary>The planner 0x426040 for the walk to <paramref name="destination"/> (the walk's budget, LR2, and the PC's straight-line retry, LR4; <paramref name="straight"/> plans the straight line at once).</summary>
    AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight);
    /// <summary>Whether the step <paramref name="from"/> → <paramref name="next"/> is blocked (the Original setting's critter test before a segment's last step, LR5, <paramref name="lastOfSegment"/>); the terrain and the round's action points are the grid's own refusal.</summary>
    AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment);
    /// <summary>Starts the step <paramref name="from"/> → <paramref name="next"/> on the hero's body: the game milliseconds it takes (at least 1), or 0 when the body cannot take it (the step ends the run).</summary>
    int BeginStep(CellCoord from, CellCoord next, bool running);
    /// <summary>
    /// ARC-DEV-163: the step <paramref name="from"/> → <paramref name="to"/> that <see cref="BeginStep"/> gave is queued on the hero's body and has not begun: the body takes it at the exact end of the step under way (or at its next advance
    /// when it stands), and the table's call can come before that (in a frame the table ticks first and the body advances after, so the body is up to a frame behind the table's due time). False once the body took it, refused it or lost it.
    /// </summary>
    bool StepPending(CellCoord from, CellCoord to);
    /// <summary>Hands the walk to <paramref name="destination"/> to the host's route follower (a far goal): false when it refuses it.</summary>
    bool FollowRoute(CellCoord destination);
    /// <summary>The route follower still follows <paramref name="destination"/> (or the far goal handed on across a window move).</summary>
    bool RouteFollowing(CellCoord destination);
    /// <summary>The hero has to turn to <paramref name="rotation"/> before the walk's first step (it stands settled and faces another way).</summary>
    bool NeedsTurn(int rotation);
    /// <summary>One step of goal 53 pushed by the walk toward <paramref name="rotation"/> (node 0x42C440): the milliseconds until the next step, or 0 when the turn is over (done, or the hero no longer stands).</summary>
    int TurnStep(int rotation);

    // ---- ARC-DEV-169 (MIG13 S11a): the hero's talk (goals 21 and 8) ----

    /// <summary>The target of a talk goal: its cell and whether node 1 refuses it; null when it is gone (destroyed, off, or no longer on the map).</summary>
    AiHeroTarget? ObjectTarget(string target) => null;
    /// <summary>The cell the hero walks to so as to stand next to <paramref name="target"/> (the host's approach cell: reachable, within the range 1 of goal 8); null when there is none.</summary>
    CellCoord? ApproachTile(string target, int range) => null;
    /// <summary>ARC-DEV-174: the act of the use goals 38 and 81 (node 4, 0x428A10): the use of <paramref name="target"/> is asked for (the object's use script, a map exit, the loot window), whatever the script answers.</summary>
    void UseObject(string target) { }
    /// <summary>ARC-DEV-176 (MIG13 S11d): the door ports the opener goals 11, 12, 49 and 50 run over (the hero opens and closes doors as any opener); null without a door host (no door goals run).</summary>
    IAiDoorWorld? Doors => null;
    /// <summary>ARC-DEV-176: the hero stands settled (no step under way): a door's use waits for it, so a door never closes under the hero's step.</summary>
    bool HeroSettled => true;
    /// <summary>ARC-DEV-175: the act of goal 16 (node 1, 0x42A9B0): the picker takes <paramref name="target"/> (an item on the ground, or one in a junk pile) into its inventory, or is refused (weight, room, a script).</summary>
    void PickUpObject(string target) { }
    /// <summary>Node 0x42AF00's call to the interface (0x57CC70): the dialog opens with <paramref name="target"/> (the script's dialog point, the refusals of the can-speak test, the window).</summary>
    void OpenDialog(string target) { }
}

public static partial class AiExecutorRules
{
    /// <summary>The wait of a goal that follows the host's route (a far goal) or that waits for the body to begin a step it was given (<see cref="IAiHeroWalkWorld.StepPending"/>), in game milliseconds: a frame.</summary>
    public const int HeroRoutePollMs = 16;

    /// <summary>The goals the hero's walk stepper interprets: the walk to a tile (3), the run to a tile (4), the walk step goal (5), the pause (6) and the rotate (53) the walk pushes, and (ARC-DEV-169) the talk (21) and the approach to an object (8).</summary>
    public static bool IsHeroWalkGoal(GoalType goal) => goal is GoalType.MoveToTile or GoalType.RunToTile or GoalType.AttemptMove or GoalType.MoveToPause or GoalType.Rotate or GoalType.Talk or GoalType.MoveNear or GoalType.UseObject or GoalType.UseContainer or GoalType.PickupItem or GoalType.AttemptPickup or GoalType.OpenDoor or GoalType.AttemptOpenDoor or GoalType.CloseDoor or GoalType.AttemptCloseDoor;

    /// <summary>
    /// One step of the hero's walk run (the stepper 0x423560 over the walk nodes read in ARC-DEV-148): the nodes run until one waits or the run ends. Goal 3 (goal 4 has the extra first node that sets the run flag when
    /// the encumbrance is below 4, so its numbers are one higher): 1 the exact arrival test (pop), 2 the get-up (the hero's own host's; a hero that is down ends the run), 3 the unconceal (not modelled), 4 a held plan
    /// pushes goal 5 and a needed one (the replan flag, or no plan) goes on to 5 the plan (0x426040: the replan flag cleared), 6 the turn goal 53 toward the path's first rotation (pushed when the hero faces another
    /// way, and node 4 then pushes goal 5 when the turn pops), the no-path outcome the wait goal 6 (1000 ms outside a round, then the run ends; in a round the run ends at once). Goal 5 takes the planned segment one step
    /// at a time: the hero must stand where the last step ended (a refused step leaves it behind: the run ends, no re-route), a retarget (the replan flag) or the segment's end pops, a blocked step ends the run, the step starts on the
    /// hero's body and the next comes after its time; the end node of goal 5 sets the replan flag, so goal 3 plans again after each segment. The action points of a step are the grid's (nothing is charged here).
    /// A Steamwoven far goal (<see cref="AiHeroPlanOutcome.Far"/>) is handed to the host's route follower and the goal waits while it follows (<see cref="GoalFrameFlags.RouteFollow"/>).
    /// </summary>
    public static AiGoalStepResult StepHeroWalk(GoalRun run, IAiHeroWalkWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        var frames = run.Frames.ToBuilder();
        var state = new HeroWalkState(run.Flags, run.PathFlags);
        var reason = AiExecutorReason.None;
        for (var nodes = 1; ; nodes++)
        {
            if (nodes > MaxNodes) return new(null, 0, AiRunEnd.NodeLimit, nodes - 1, reason);
            var top = frames[^1];
            var t = HeroWalkNode(top, state, world);
            if (t.Reason != AiExecutorReason.None) reason = t.Reason;
            if (t.Top is { } changed) frames[^1] = top = changed;
            switch (t.Move)
            {
                case Move.Next:
                    frames[^1] = top with { Node = t.Node };
                    break;
                case Move.Push:
                    if (frames.Count >= MaxDepth) return new(null, 0, AiRunEnd.Overflow, nodes, reason);
                    frames[^1] = top with { Node = 1 };
                    frames.Add(t.Frame!);
                    break;
                case Move.Pop:
                    // The end node of goal 5: it sets "needs plan", so the walk plans again after each segment.
                    if (top.Goal == GoalType.AttemptMove) state.PathFlags |= GoalPathFlags.Replan;
                    frames.RemoveAt(frames.Count - 1);
                    if (frames.Count == 0) return new(null, 0, AiRunEnd.Finished, nodes, reason);
                    frames[^1] = frames[^1] with { Node = 1 };
                    break;
                case Move.Replace:
                    // ARC-DEV-176 (the original's step word 0x70000000 + N): the goal is replaced in its frame (goal 38 becomes 11 or 49, goal 11 becomes 12).
                    frames[^1] = t.Frame!;
                    break;
                case Move.Wait:
                    frames[^1] = top with { Node = t.Node };
                    return new(run.WithFrames(frames.ToImmutable()) with { Flags = state.Flags, PathFlags = state.PathFlags }, Math.Max(t.Delay, 0), AiRunEnd.Running, nodes, reason);
                case Move.Restart:
                    frames[^1] = top with { Node = 1 };
                    return new(run.WithFrames(frames.ToImmutable()) with { Flags = state.Flags, PathFlags = state.PathFlags }, Math.Max(t.Delay, 0), AiRunEnd.Running, nodes, reason);
                case Move.End:
                    return new(null, 0, AiRunEnd.EndStep, nodes, reason);
                default:
                    return new(null, 0, AiRunEnd.Killed, nodes, reason);
            }
            if (t.Delay > 0) return new(run.WithFrames(frames.ToImmutable()) with { Flags = state.Flags, PathFlags = state.PathFlags }, t.Delay, AiRunEnd.Running, nodes, reason);
        }
    }

    // The run's flags the nodes change within one step (the run flags and the path flags, +0xC and +0xC00).
    private sealed class HeroWalkState(GoalRunFlags flags, GoalPathFlags pathFlags)
    {
        public GoalRunFlags Flags = flags;
        public GoalPathFlags PathFlags = pathFlags;
    }

    private static Transition HeroWalkNode(GoalFrame goal, HeroWalkState state, IAiHeroWalkWorld world) => goal.Goal switch
    {
        GoalType.MoveToTile or GoalType.RunToTile => HeroTileNode(goal, state, world),
        GoalType.AttemptMove => HeroStepNode(goal, state, world),
        GoalType.MoveToPause => PauseNode(goal),
        GoalType.Rotate => world.TurnStep(goal.Rotation) is > 0 and var wait ? new(Move.Restart, Delay: wait) : new(Move.Pop),
        GoalType.Talk => HeroTalkNode(goal, world),
        GoalType.UseObject or GoalType.UseContainer => HeroUseNode(goal, world),
        GoalType.PickupItem => HeroPickupNode(goal, world),
        GoalType.AttemptPickup => HeroAttemptPickupNode(goal, world),
        GoalType.MoveNear => HeroMoveNearNode(goal, world),
        // The opener goals over the door ports (the same nodes as an NPC opener's, S7).
        GoalType.OpenDoor or GoalType.AttemptOpenDoor or GoalType.CloseDoor or GoalType.AttemptCloseDoor => world.Doors is { } doors ? DoorNode(AiSchedulerRules.Pc, goal, doors) : new(Move.Kill),
        _ => new(Move.Kill),
    };

    private static bool HeroRunning(HeroWalkState state, IAiHeroWalkWorld world) =>
        (state.Flags & GoalRunFlags.Running) != 0 && world.Encumbrance < RunRefusedEncumbrance;

    // Goals 3 and 4: the walk to a tile.
    private static Transition HeroTileNode(GoalFrame goal, HeroWalkState state, IAiHeroWalkWorld world)
    {
        var destination = goal.Tile;
        var node = goal.Node;
        // Goal 4's extra first node: the run flag is set when the encumbrance is below 4 (the gait of the whole run).
        if (goal.Goal == GoalType.RunToTile)
        {
            if (node == 1)
            {
                if (world.Encumbrance < RunRefusedEncumbrance) state.Flags |= GoalRunFlags.Running;
                return Next(2);
            }
            node--;
        }
        if (destination is not { } tile || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
        // Steamwoven: a far goal the host's route follower walks; the goal waits while it follows, and plans again after a retarget.
        if ((goal.Flags & GoalFrameFlags.RouteFollow) != 0)
        {
            if ((state.PathFlags & GoalPathFlags.Replan) != 0) return new(Move.Next, 1, Top: goal with { Flags = goal.Flags & ~GoalFrameFlags.RouteFollow });
            if (at == tile || !world.RouteFollowing(tile)) return new(Move.Pop, Reason: AiExecutorReason.RunToTile);
            return new(Move.Restart, Delay: HeroRoutePollMs);
        }
        switch (node)
        {
            // 1: the exact arrival test.
            case 1:
                return at == tile ? new(Move.Pop, Reason: AiExecutorReason.RunToTile) : Next(goal.Goal == GoalType.RunToTile ? 3 : 2);
            // 2: the get-up (the hero's own host gets it up); a hero that is down walks nowhere.
            case 2:
                return world.Down ? End(AiExecutorReason.ObjectGone) : Next(goal.Goal == GoalType.RunToTile ? 4 : 3);
            // 3: the unconceal: nothing is pushed (a concealed hero's sneak does not stop a walk here).
            case 3:
                return Next(goal.Goal == GoalType.RunToTile ? 5 : 4);
            // 4: a plan the run holds (no replan flag) goes on to goal 5; a needed one is planned (5).
            case 4:
                if ((state.PathFlags & GoalPathFlags.Replan) != 0 || goal.Path.IsDefaultOrEmpty) return Next(goal.Goal == GoalType.RunToTile ? 6 : 5);
                return new(Move.Push, Frame: new GoalFrame(GoalType.AttemptMove, null, tile) { Path = goal.Path, PathIndex = 0 }, Reason: AiExecutorReason.RunToTile);
            // 5: the plan.
            case 5:
                var running = HeroRunning(state, world);
                var plan = world.PlanWalk(tile, running, (state.Flags & GoalRunFlags.Flight) != 0);
                switch (plan.Outcome)
                {
                    case AiHeroPlanOutcome.Found when !plan.Steps.IsDefaultOrEmpty:
                        state.PathFlags &= ~GoalPathFlags.Replan;
                        if (plan.Straight) state.Flags |= GoalRunFlags.Flight;
                        var steps = plan.Steps.Length > SegmentSteps ? plan.Steps[..SegmentSteps] : plan.Steps;
                        return new(Move.Next, goal.Goal == GoalType.RunToTile ? 7 : 6, Top: goal with { Path = steps, PathIndex = 0 });
                    case AiHeroPlanOutcome.Far when world.FollowRoute(tile):
                        state.PathFlags &= ~GoalPathFlags.Replan;
                        return new(Move.Restart, Delay: HeroRoutePollMs, Top: goal with { Flags = goal.Flags | GoalFrameFlags.RouteFollow });
                    default:
                        // The common no-path node: the pause goal 6 in real time (a second, then the run ends), the end at once in a round.
                        state.PathFlags &= ~GoalPathFlags.Replan;
                        return world.InRound ? End(AiExecutorReason.NoPath) : new(Move.Push, Frame: new GoalFrame(GoalType.MoveToPause), Reason: AiExecutorReason.NoPath);
                }
            // 6: the turn toward the path's first rotation (a hero that faces it already, or is not standing, does not turn); goal 5 follows (node 4).
            default:
                if (!goal.Path.IsDefaultOrEmpty && LineOfFireRules.Rotation(at, goal.Path[0]) is var rotation && world.NeedsTurn(rotation))
                    return new(Move.Push, Frame: new GoalFrame(GoalType.Rotate) { Rotation = rotation });
                return Next(goal.Goal == GoalType.RunToTile ? 5 : 4);
        }
    }

    // Goal 5: the step walk over the planned segment.
    private static Transition HeroStepNode(GoalFrame goal, HeroWalkState state, IAiHeroWalkWorld world)
    {
        if (world.Down || world.Tile() is not { } at) return End(AiExecutorReason.ObjectGone);
        var index = goal.PathIndex;
        // A retarget (the replan flag): back to goal 3, which plans again from where the hero is (a step the retarget dropped before it began leaves the hero behind, which is no refusal).
        if ((state.PathFlags & GoalPathFlags.Replan) != 0) return new(Move.Pop);
        // The last step must have entered its cell (its refusal leaves the hero behind: a refused step ends the walk of a PC, nothing re-routes it).
        // A step the body only queued (it begins at the end of the step under way) is no refusal: the goal looks again in a frame (ARC-DEV-163).
        if (index > 0 && index <= goal.Path.Length && goal.Path[index - 1] != at)
            return world.StepPending(at, goal.Path[index - 1]) ? new(Move.Restart, Delay: HeroRoutePollMs) : End(AiExecutorReason.StepBlocked);
        // The segment's end: back to goal 3, which plans again.
        if (index >= goal.Path.Length) return new(Move.Pop);
        var from = index == 0 ? at : goal.Path[index - 1];
        var next = goal.Path[index];
        if (world.StepBlocked(from, next, lastOfSegment: index == goal.Path.Length - 1) != AiStepBlock.Clear) return End(AiExecutorReason.StepBlocked);
        var ms = world.BeginStep(from, next, HeroRunning(state, world));
        if (ms <= 0) return End(AiExecutorReason.StepBlocked);
        return new(Move.Next, 1, Delay: Math.Max(ms, 1), Top: goal with { PathIndex = index + 1 });
    }

    /// <summary>The frame of the hero's walk run: goal 3 to <paramref name="destination"/>, or goal 4 when <paramref name="run"/>.</summary>
    public static GoalFrame HeroWalkFrame(CellCoord destination, bool run) => new(run ? GoalType.RunToTile : GoalType.MoveToTile, null, destination);
}
