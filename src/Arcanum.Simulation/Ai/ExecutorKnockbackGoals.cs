using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// ARC-DEV-141 (MIG13 S8c, docs/architecture/goal-table.md §3.5, the S8 read GT59–GT62): what the knockback goals need of the world. Goal 43 <c>ag_knockback</c> belongs to the pushed object (its own run: any
/// object the magic-tech component targets, GT59) and pushes goal 64 <c>ag_attempt_move_straight_knockback</c>, which slides the object and never pops back: every ending of 64 is the end step of the whole run, and neither
/// goal has an end node (GT60). Everything is by the pushed object's entity; the line, its steps, the block test and the collision damage are the host's.
/// </summary>
public interface IAiKnockbackWorld
{
    /// <summary>Node 1 of goal 43 (0x424D90): the object stands on the goal's destination tile already (the run pops at once).</summary>
    bool KnockbackAtDestination(string target, CellCoord destination);
    /// <summary>Node 3 of goal 43 (0x4268F0): builds the 10-pixel line from the object's tile to <paramref name="destination"/>; false when the line is refused (more than 100 steps).</summary>
    bool KnockbackBuildLine(string target, CellCoord destination);
    /// <summary>Node 3 of goal 64 (0x42F2D0): the slide's setup (the steps start at the first); false ends the run.</summary>
    bool KnockbackSetup(string target);
    /// <summary>Node 2 of goal 64 (0x42FA50): one tick of the slide (four line steps, the block test at each tile change, the collision damage on a block); true goes on 35 ms later, false ends the run.</summary>
    bool KnockbackTick(string target);
}

public static partial class AiExecutorRules
{
    /// <summary>The tick of a knockback's slide, 35 ms apart (the global 0x5DE6CC every tick sets, GT60).</summary>
    public const int KnockbackTickMs = KnockbackRules.TickMs;

    /// <summary>The two knockback goals the knockback stepper interprets.</summary>
    public static bool IsKnockbackGoal(GoalType goal) => goal is GoalType.Knockback or GoalType.AttemptMoveStraightKnockback;

    /// <summary>The goal a knockback's own run starts with (43).</summary>
    public static bool IsKnockbackRunGoal(GoalType goal) => goal == GoalType.Knockback;

    /// <summary>The frame of a knockback's run: goal 43 at its destination tile (slot 5), with the path flag set (GT58). The source object (slot 4) is only the starter's.</summary>
    public static GoalFrame KnockbackFrame(CellCoord destination) => new(GoalType.Knockback, null, destination) { PathPending = true };

    /// <summary>
    /// One step of a knockback's run (the stepper 0x423560 over the nodes of goals 43 and 64): 43: 1 the object on its destination (pop) else 2; 2 the path flag set (3) else push 64; 3 the line (refused: 4) else push 64;
    /// 4 the end step. 64: 1 the slide playing (2) else its setup (3); 2 the tick (false: the end step) else 35 ms later; 3 the setup (false: the end step) else the first tick follows at once. 64 never pops back into 43.
    /// </summary>
    public static AiGoalStepResult StepKnockback(GoalRun run, IAiKnockbackWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        return StepObjectRun(run, (owner, top) => KnockbackNode(owner, top, world), _ => { });
    }

    private static Transition KnockbackNode(string target, GoalFrame goal, IAiKnockbackWorld world)
    {
        var destination = goal.Tile ?? default;
        switch (goal.Goal)
        {
            case GoalType.Knockback:
                switch (goal.Node)
                {
                    // 1: already on the destination -> pop; else 2.
                    case 1: return world.KnockbackAtDestination(target, destination) ? new(Move.Pop) : Next(2);
                    // 2: the path flag set -> build the line (3); clear -> the slide (64 pushed).
                    case 2: return goal.PathPending ? Next(3) : new(Move.Push, Frame: SlideFrame(destination));
                    // 3: the line; a refused one goes to the end step (4). The builder clears the flag.
                    case 3:
                        return world.KnockbackBuildLine(target, destination)
                            ? new(Move.Push, Top: goal with { PathPending = false }, Frame: SlideFrame(destination))
                            : Next(4);
                    // 4: the end step, whichever way it is reached.
                    default: return new(Move.End);
                }
            case GoalType.AttemptMoveStraightKnockback:
                switch (goal.Node)
                {
                    // 1: the slide is playing -> the tick (2), else its setup (3).
                    case 1: return PlayingNow(goal) ? Next(2) : Next(3);
                    // 2: a tick of four steps: the slide goes on 35 ms later, or it ended (a block, the line's end, off the map): the end step of the whole run.
                    case 2: return world.KnockbackTick(target) ? new(Move.Restart, Delay: KnockbackTickMs) : new(Move.End);
                    // 3: the setup; the first tick follows at once.
                    default: return world.KnockbackSetup(target) ? new(Move.Restart, Top: WithPlaying(goal, true)) : new(Move.End);
                }
            default: return new(Move.Kill);
        }
    }

    private static GoalFrame SlideFrame(CellCoord destination) => new(GoalType.AttemptMoveStraightKnockback, null, destination);
}
