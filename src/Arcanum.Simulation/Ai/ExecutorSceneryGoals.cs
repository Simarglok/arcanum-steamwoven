using Arcanum.Simulation.Animation;
using Arcanum.Simulation.Goals;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// ARC-DEV-142 (MIG13 S9a, docs/architecture/goal-table.md §3.5, the S9 read GT75–GT78): what the scenery animation loop needs of the world. An animated scenery owns a run of goal 1 <c>ag_animate_loop</c>, or of
/// goal 77 <c>ag_animate_loop_fire_dmg</c> for scenery flag 0x10 (the burning scenery); both are the scenery's main run (priority 1, not counted, no no-interrupt flag, GT67), started when the object enters the object
/// list (GT77). The frame, the object's offsets, the art and the distance to the local player character are the host's; the nodes are the loop's order.
/// </summary>
public interface IAiSceneryWorld : IAiFireWorld
{
    /// <summary>Node 4 of goal 1 and 77, the begin (0x42DA50): reads the art and stores its frame delay (1000 / the rate, 100 ms when the header fails) and answers it; null ends the run (a stoned object, a critter of the refusing kind).</summary>
    int? SceneryBegin(string scenery);
    /// <summary>The distance test (0x441AE0 against the local player character, or always in multiplayer): the scenery lies within 30 tiles (<see cref="SceneryAnimationRules.NearTiles"/>).</summary>
    bool SceneryNear(string scenery);
    /// <summary>Node 3 (0x42DDE0): steps the frame (wraps at the last, moves the object's offsets by the frame's delta); false when the object is stoned (nothing steps) or the new frame number mod 3 is 1.</summary>
    bool SceneryAdvance(string scenery);
}

public static partial class AiExecutorRules
{
    /// <summary>A loop away from the player character looks again after this long without stepping (<see cref="SceneryAnimationRules.FarRetryMs"/>, GT75).</summary>
    public const int SceneryFarRetryMs = (int)SceneryAnimationRules.FarRetryMs;

    /// <summary>The two scenery-loop goals the scenery stepper interprets.</summary>
    public static bool IsSceneryGoal(GoalType goal) => goal is GoalType.AnimateLoop or GoalType.AnimateLoopFireDamage;

    /// <summary>The goals a scenery's own run starts with (1 and 77).</summary>
    public static bool IsSceneryRunGoal(GoalType goal) => IsSceneryGoal(goal);

    /// <summary>The frame of a scenery's run (0x433580, GT77): goal 77 for a scenery with flag 0x10, else goal 1.</summary>
    public static GoalFrame SceneryFrame(bool burning) => new(burning ? GoalType.AnimateLoopFireDamage : GoalType.AnimateLoop);

    /// <summary>
    /// One step of a scenery's run (the stepper 0x423560 over the nodes of goals 1 and 77): 1 the loop is running (2) else its begin (4); 4 the begin (a refusal ends the run; goal 1 waits one frame delay, goal 77 goes
    /// to the fire damage at once); 2 the distance, measured again on every third call (a near scenery goes on to 3, a far one looks again after 800 ms); 3 the next frame, then one frame delay (goal 77 turns to the fire damage
    /// when the new frame number mod 3 is 1); 5 the fire damage of goal 77, then one frame delay. The run never ends by itself.
    /// </summary>
    public static AiGoalStepResult StepScenery(GoalRun run, IAiSceneryWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        return StepObjectRun(run, (owner, top) => SceneryNode(owner, top, world), _ => { });
    }

    private static Transition SceneryNode(string scenery, GoalFrame goal, IAiSceneryWorld world)
    {
        var burning = goal.Goal == GoalType.AnimateLoopFireDamage;
        if (!IsSceneryGoal(goal.Goal)) return new(Move.Kill);
        var pause = Math.Max(goal.RestMs, 0);
        switch (goal.Node)
        {
            // 1: the loop's flag (the run's 0x10) set -> the distance test (2), clear -> the begin (4).
            case 1: return PlayingNow(goal) ? Next(2) : Next(4);
            // 2: every third call measures the distance again; a near scenery steps (3), a far one sleeps 800 ms and looks again.
            case 2:
            {
                var checks = goal.Count + 1;
                var near = goal.Near;
                if (checks >= SceneryAnimationRules.ChecksPerMeasure) { checks = 0; near = world.SceneryNear(scenery); }
                var top = goal with { Count = checks, Near = near };
                return near ? new(Move.Next, 3, Top: top) : new(Move.Restart, Delay: SceneryFarRetryMs, Top: top);
            }
            // 3: the next frame; goal 77 burns on the frames whose number mod 3 is 1 (and for a stoned object), both goals wait one frame delay.
            case 3:
                return world.SceneryAdvance(scenery) || !burning ? new(Move.Restart, Delay: pause) : Next(5);
            // 5: the fire damage (3 fire on the scenery's own tile, once per round and doubled in a round, GT94); then one frame delay.
            case 5:
                return new(Move.Restart, Delay: pause, Top: FireDamage(scenery, goal, world));
            // 4: the begin: the pace, the distance and the loop's flag; goal 77 burns at once.
            default:
            {
                if (world.SceneryBegin(scenery) is not { } rest) return new(Move.End);
                var top = WithPlaying(goal with { RestMs = rest, Near = world.SceneryNear(scenery), Count = 0 }, true);
                return burning ? new(Move.Next, 5, Top: top) : new(Move.Restart, Delay: rest, Top: top);
            }
        }
    }
}
