using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Ai;

/// <summary>What one tick of a spell missile's flight did (<see cref="IAiMissileWorld.MissileTick"/>, the tick 0x42F390): the leg goes on, its steps ran out in this tick, or a step left the map.</summary>
public enum MissileTickResult { Continue, LegEnded, OffMap }

/// <summary>
/// ARC-DEV-139 (MIG13 S8, docs/architecture/goal-table.md §3.5, the S8 read GT52, GT56, GT57, GT58): what the spell-missile goals need of the world. A spell missile is a created object
/// (session entity string) that owns its run: goal 28 <c>ag_shoot_spell</c> on the bottom, goal 78 <c>ag_attempt_move_straight_spell</c> pushed for each leg. The caster and the spell run are the host's
/// (the original keeps the caster in slot 4); the line, its steps and the draw are the host's too. Everything is by the missile's entity.
/// </summary>
public interface IAiMissileWorld
{
    /// <summary>Node 1 of goal 28 (0x424D90): the missile is on the goal's destination tile.</summary>
    bool MissileAtDestination(string missile, CellCoord destination);
    /// <summary>Node 4 of goal 28 (0x42A180): the target object is where the missile is, or the goal has no target object (a tile cast), or the target is gone (the release's own re-check then decides).</summary>
    bool MissileTargetHere(string missile, string? target);
    /// <summary>Node 9 of goal 28 (0x42BEC0): the target's tile when it differs from <paramref name="destination"/> (it is written to the destination and the path flag is set), null when it is already there.</summary>
    CellCoord? MissileRetarget(string missile, string? target, CellCoord destination);
    /// <summary>Node 3 of goal 28 (0x426840): builds the line from the missile's tile to <paramref name="destination"/> (10-pixel steps, at most 100); false when the line is refused.</summary>
    bool MissileBuildLine(string missile, CellCoord destination);
    /// <summary>Node 8 of goal 28 (0x429ED0): the steps per tick into the run's slot; false ends the run.</summary>
    bool MissileSpeed(string missile);
    /// <summary>Node 5 of goal 28 (0x429C80): the release, the target re-check and the BEGIN of the spell run. False (a failed re-check; the run is freed unpaid) ends the run.</summary>
    bool MissileRelease(string missile);
    /// <summary>Node 6 of goal 28 (0x424D00): the missile is destroyed after its release.</summary>
    void MissileDestroy(string missile);
    /// <summary>The end node of goal 28 (0x429E70): unless the missile is destroyed already, it is destroyed and its spell run is freed unpaid (MG44).</summary>
    void MissileLost(string missile);
    /// <summary>Node 3 of goal 78 (0x42F2D0): the leg's setup (the steps start at the first); the pace the missile waits after the leg, in game milliseconds (1000 / the art's frame rate, 100 without one).</summary>
    int MissileLegSetup(string missile);
    /// <summary>Node 2 of goal 78 (0x42F390): one tick of the leg's steps at the missile's speed, the missile moved in the session.</summary>
    MissileTickResult MissileTick(string missile);
}

public static partial class AiExecutorRules
{
    /// <summary>The ticks of a missile's flight, 35 ms apart (the global 0x5DE6CC every tick sets, GT53).</summary>
    public const int MissileTickMs = ProjectileRules.TickMs;

    /// <summary>The two spell-missile goals the missile stepper interprets.</summary>
    public static bool IsMissileGoal(GoalType goal) => goal is GoalType.ShootSpell or GoalType.AttemptMoveStraightSpell;

    /// <summary>The goal a spell missile's own run starts with (28); the other goals a run can start with are the NPC's or a door's.</summary>
    public static bool IsMissileRunGoal(GoalType goal) => goal == GoalType.ShootSpell;

    /// <summary>The goals whose runs a host of its own steps (a door's 70, 71, 72; a spell missile's 28; a projectile's 36; a pushed object's knockback 43): not the NPC AI host's.</summary>
    public static bool IsObjectRunGoal(GoalType goal) => IsDoorRunGoal(goal) || IsMissileRunGoal(goal) || IsProjectileRunGoal(goal) || IsKnockbackRunGoal(goal);

    /// <summary>The frame of a spell missile's run: goal 28 at its target object (null for a tile cast) and its destination tile, with the path flag set (GT58) and the spell run it releases.</summary>
    public static GoalFrame MissileFrame(string? target, CellCoord destination, int spellRun) =>
        new(GoalType.ShootSpell, target, destination) { SpellRun = spellRun, PathPending = true };

    /// <summary>
    /// One step of a spell missile's run (the stepper 0x423560 over the missile nodes): the nodes run until one waits or the run ends. Goal 28 pushes goal 78 for each leg; 78 ticks every 35 ms and pops
    /// after the missile's pace when the leg's steps run out, which resumes 28 at its first node. The pop of the bottom goal ends the run, as every end runs the end node of goal 28.
    /// </summary>
    public static AiGoalStepResult StepMissile(GoalRun run, IAiMissileWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        return StepObjectRun(run, (owner, top) => MissileNode(owner, top, world), frame => MissileEndNode(run.Owner, frame, world));
    }

    // The loop of the runs a created object owns (the spell missile's, the projectile's): the nodes run until one waits or the run ends; a pop of the bottom goal ends the run, and every end runs the end node of the
    // goal that has one (the ARC-DEV-139 loop, shared since ARC-DEV-140).
    private static AiGoalStepResult StepObjectRun(GoalRun run, Func<string, GoalFrame, Transition> node, Action<GoalFrame> endNode)
    {
        var frames = run.Frames.ToBuilder();
        for (var nodes = 1; ; nodes++)
        {
            if (nodes > MaxNodes) return new(null, 0, AiRunEnd.NodeLimit, nodes - 1, AiExecutorReason.None);
            var top = frames[^1];
            var t = node(run.Owner, top);
            if (t.Top is { } changed) frames[^1] = top = changed;
            switch (t.Move)
            {
                case Move.Next:
                    frames[^1] = top with { Node = t.Node };
                    break;
                case Move.Push:
                    if (frames.Count >= MaxDepth) return new(null, 0, AiRunEnd.Overflow, nodes, AiExecutorReason.None);
                    frames[^1] = top with { Node = 1 };
                    frames.Add(t.Frame!);
                    break;
                case Move.Pop:
                    endNode(frames[^1]);
                    frames.RemoveAt(frames.Count - 1);
                    if (frames.Count == 0) return new(null, 0, AiRunEnd.Finished, nodes, AiExecutorReason.None);
                    frames[^1] = frames[^1] with { Node = 1 };
                    break;
                case Move.Restart:
                    frames[^1] = top with { Node = 1 };
                    return new(run.WithFrames(frames.ToImmutable()), Math.Max(t.Delay, 0), AiRunEnd.Running, nodes, AiExecutorReason.None);
                case Move.End:
                    for (var i = frames.Count - 1; i >= 0; i--) endNode(frames[i]);
                    return new(null, 0, AiRunEnd.EndStep, nodes, AiExecutorReason.None);
                default:
                    return new(null, 0, AiRunEnd.Killed, nodes, AiExecutorReason.None);
            }
            if (t.Delay > 0) return new(run.WithFrames(frames.ToImmutable()), t.Delay, AiRunEnd.Running, nodes, AiExecutorReason.None);
        }
    }

    /// <summary>The end nodes of a missile run ended from outside the stepper (an interrupt such as a round's start, a map's close), top down; a killed run skips them (GS21).</summary>
    public static void EndMissileRun(GoalRun run, IAiMissileWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if ((run.Flags & GoalRunFlags.Killed) != 0) return;
        for (var i = run.Frames.Length - 1; i >= 0; i--) MissileEndNode(run.Owner, run.Frames[i], world);
    }

    // The end node of goal 28 (0x429E70): the missile that is not destroyed yet is destroyed and its spell run freed unpaid. Goal 78 has no end node (GT52).
    private static void MissileEndNode(string missile, GoalFrame frame, IAiMissileWorld world)
    {
        if (frame.Goal == GoalType.ShootSpell) world.MissileLost(missile);
    }

    private static Transition MissileNode(string missile, GoalFrame goal, IAiMissileWorld world)
    {
        switch (goal.Goal)
        {
            case GoalType.ShootSpell:
                var destination = goal.Tile ?? default;
                switch (goal.Node)
                {
                    // 1: on the destination -> the target test (4), else the line (2).
                    case 1: return world.MissileAtDestination(missile, destination) ? Next(4) : Next(2);
                    // 2: the path flag set (a new or stale destination, GT58) -> build the line (3); clear -> fly the same line again (8).
                    case 2: return goal.PathPending ? Next(3) : Next(8);
                    // 3: the line; a refused one (more than 100 steps) ends the goal, its end node destroys the missile and frees the spell run (SM5, MG44). The builder clears the flag.
                    case 3:
                        return world.MissileBuildLine(missile, destination) ? new(Move.Next, 8, Top: goal with { PathPending = false }) : new(Move.End);
                    // 4: the target is here (or there is none) -> the release (5), else follow it (9).
                    case 4: return world.MissileTargetHere(missile, goal.Target) ? Next(5) : Next(9);
                    // 5: the release; a failed re-check ends the goal.
                    case 5: return world.MissileRelease(missile) ? Next(6) : new(Move.End);
                    // 6: the missile is destroyed after its release; the goal pops.
                    case 6:
                        world.MissileDestroy(missile);
                        return new(Move.Pop);
                    // 8: the speed, then the leg: goal 78 is pushed.
                    case 8:
                        return world.MissileSpeed(missile)
                            ? new(Move.Push, Frame: new GoalFrame(GoalType.AttemptMoveStraightSpell, goal.Target, destination) { SpellRun = goal.SpellRun })
                            : new(Move.End);
                    // 9: the retarget; the target already on the destination pops (no release; the end node destroys the missile), else the destination moves to it and the line is built again.
                    case 9:
                        return world.MissileRetarget(missile, goal.Target, destination) is { } moved
                            ? new(Move.Next, 1, Top: goal with { Tile = moved, PathPending = true })
                            : new(Move.Pop);
                    default: return new(Move.Kill);
                }
            case GoalType.AttemptMoveStraightSpell:
                switch (goal.Node)
                {
                    // 1: the leg is playing -> the tick (2), else its setup (3).
                    case 1: return PlayingNow(goal) ? Next(2) : Next(3);
                    // 2: a tick of steps; the leg goes on 35 ms later, or its steps ran out and the goal pops after the missile's pace (the stored delay, -2); a step off the map ends the run.
                    case 2:
                        return world.MissileTick(missile) switch
                        {
                            MissileTickResult.Continue => new(Move.Restart, Delay: MissileTickMs),
                            MissileTickResult.LegEnded => new(Move.Pop, Delay: Math.Max(goal.RestMs, 0)),
                            _ => new(Move.End),
                        };
                    // 3: the setup: the pace is stored, the leg plays; the first tick follows at once.
                    default:
                        return new(Move.Restart, Top: WithPlaying(goal with { RestMs = world.MissileLegSetup(missile) }, true));
                }
            default:
                return new(Move.Kill);
        }
    }
}
