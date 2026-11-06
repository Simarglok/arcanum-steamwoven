using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;

namespace Arcanum.World;

/// <summary>How the stepper ended a run, or <see cref="Running"/> while it goes on (<see cref="GoalStepperResult"/>).</summary>
public enum GoalStepperEnd
{
    /// <summary>The run goes on after the answered delay.</summary>
    Running,
    /// <summary>The bottom goal popped: the run is done (no end step).</summary>
    Finished,
    /// <summary>A node chose the end step: the step ran the end nodes of every goal.</summary>
    EndStep,
    /// <summary>A node killed the run (a step or an attack it cannot pay).</summary>
    Killed,
    /// <summary>A push beyond eight goals ended the run.</summary>
    Overflow,
    /// <summary>The 101st node of one step: the owner's subturn ended and the run was killed.</summary>
    NodeLimit,
    /// <summary>The run's current goal has no node interpreter yet (goal-table.md §3.4: a typed end, never a silent skip).</summary>
    UnknownGoal,
}

/// <summary>
/// The code the stepper calls when it ends a run (goal-table.md §3.3, §3.4): the end nodes of the goals and the idle restart. Until MIG15 they live with the node
/// ports in the viewer's AI host.
/// </summary>
public interface IGoalStepHooks
{
    /// <summary>A run the stepper ended without its end step (killed, the node limit, an overflow, an unknown goal): the end nodes of its goals.</summary>
    void EndDropped(GoalRun run);
    /// <summary>A run that ended through its end step, whose end nodes the step ran: what they leave behind (the casts queued beneath its goals).</summary>
    void EndStepped(GoalRun run);
    /// <summary>The stepper's own end of a run, after the end hooks: the idle restart (the fidget, 0x4B4320). Not called after the node limit, an overflow or an unknown goal.</summary>
    void Restart(GoalRun run);
}

/// <summary>One step of the stepper: the answer for the session (<see cref="GameSessionOwner.EndGoalStep"/>), how it ended and the node interpreter's own result
/// (null for an unknown goal).</summary>
public sealed record GoalStepperResult(SessionGoalStepAnswer Answer, GoalStepperEnd End, AiGoalStepResult? Step);

/// <summary>
/// ARC-DEV-134 (MIG13 S4, docs/architecture/goal-table.md §3.2, §3.4): the one stepper of the goal runs, in the world runtime. It dispatches a run by its current goal to
/// the node interpreter of that goal (the NPC executor's 26 goals, <see cref="AiExecutorRules.Interprets"/>; the slices S6–S14 add the others), keeps the 100-node limit
/// and the end handling of the interpreter, calls the end hooks (<see cref="IGoalStepHooks"/>) and answers the next frame delay, at least 1 ms (a delay of 0 is the
/// interpreter's "next frame"; the host that steps the runs keeps each to one step a frame). The node ports (<see cref="IAiGoalWorld"/>) and the hooks are the viewer's AI host until MIG15.
/// </summary>
public static class GoalStepper
{
    /// <summary>
    /// Steps <paramref name="run"/> once (the stepper 0x423560, whose node loop is <see cref="AiExecutorRules.Step"/>). A run whose current goal has no interpreter ends as
    /// <see cref="GoalStepperEnd.UnknownGoal"/> with its end nodes (<see cref="IGoalStepHooks.EndDropped"/>). At an end the hooks run in the original's order: a
    /// killed run, the node limit and an overflow drop the run with its end nodes; an end step leaves its queued casts; every end but the node limit, an overflow and an
    /// unknown goal then restarts the idle animation. <paramref name="carried"/> is the reason the run's previous step reported (the overlay's; the run keeps none).
    /// </summary>
    public static GoalStepperResult Step(GoalRun run, IAiGoalWorld world, IAiRollSource rolls, CombatAnomalyProfile profile, IGoalStepHooks hooks,
        AiExecutorReason carried = AiExecutorReason.None)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rolls);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(hooks);
        if (!AiExecutorRules.Interprets(run.Top.Goal))
        {
            hooks.EndDropped(run);
            return new(SessionGoalStepAnswer.End, GoalStepperEnd.UnknownGoal, null);
        }
        var step = AiExecutorRules.Step(run, world, rolls, profile, carried);
        if (!step.Ended && step.Run is { } next) return new(SessionGoalStepAnswer.After(Math.Max(step.DelayMs, 1), next), GoalStepperEnd.Running, step);
        var end = step.End switch
        {
            AiRunEnd.Finished => GoalStepperEnd.Finished,
            AiRunEnd.EndStep => GoalStepperEnd.EndStep,
            AiRunEnd.Overflow => GoalStepperEnd.Overflow,
            AiRunEnd.NodeLimit => GoalStepperEnd.NodeLimit,
            _ => GoalStepperEnd.Killed,
        };
        // The end step ran the end nodes itself; a killed run, the node limit and an overflow drop the run without them.
        if (end is GoalStepperEnd.Killed or GoalStepperEnd.NodeLimit or GoalStepperEnd.Overflow) hooks.EndDropped(run);
        else if (end == GoalStepperEnd.EndStep) hooks.EndStepped(run);
        // The node limit frees its run without the idle restart; an overflow, which the reading did not follow, is taken as the node limit.
        if (end is not (GoalStepperEnd.NodeLimit or GoalStepperEnd.Overflow)) hooks.Restart(run);
        return new(SessionGoalStepAnswer.End, end, step);
    }

    /// <summary>
    /// ARC-DEV-138 (MIG13 S7, goal-table.md §3.5): steps a door goal's run (11, 12, 49, 50, 70, 71, 72) over the door ports (<see cref="IAiDoorWorld"/>). The opener's goals and the door's own
    /// goals use the same machine; a run whose current goal is no door goal ends as <see cref="GoalStepperEnd.UnknownGoal"/>. A killed run or the node limit drops the run, and the caller
    /// runs its end nodes (<see cref="AiExecutorRules.EndDoorRun"/>); the end step ran them itself.
    /// </summary>
    public static GoalStepperResult StepDoor(GoalRun run, IAiDoorWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if (!AiExecutorRules.IsDoorGoal(run.Top.Goal)) return new(SessionGoalStepAnswer.End, GoalStepperEnd.UnknownGoal, null);
        var step = AiExecutorRules.StepDoor(run, world);
        if (!step.Ended && step.Run is { } next) return new(SessionGoalStepAnswer.After(Math.Max(step.DelayMs, 1), next), GoalStepperEnd.Running, step);
        var end = step.End switch
        {
            AiRunEnd.Finished => GoalStepperEnd.Finished,
            AiRunEnd.EndStep => GoalStepperEnd.EndStep,
            AiRunEnd.NodeLimit => GoalStepperEnd.NodeLimit,
            _ => GoalStepperEnd.Killed,
        };
        return new(SessionGoalStepAnswer.End, end, step);
    }

    /// <summary>
    /// ARC-DEV-139 (MIG13 S8, goal-table.md §3.5): steps a spell missile's run (goals 28 and 78) over the missile ports (<see cref="IAiMissileWorld"/>). A run whose current goal is no missile goal ends as
    /// <see cref="GoalStepperEnd.UnknownGoal"/>. A killed run, the node limit or an overflow drops the run, and the caller runs its end nodes (<see cref="AiExecutorRules.EndMissileRun"/>); the end step and a pop
    /// ran them themselves.
    /// </summary>
    public static GoalStepperResult StepMissile(GoalRun run, IAiMissileWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if (!AiExecutorRules.IsMissileGoal(run.Top.Goal)) return new(SessionGoalStepAnswer.End, GoalStepperEnd.UnknownGoal, null);
        var step = AiExecutorRules.StepMissile(run, world);
        if (!step.Ended && step.Run is { } next) return new(SessionGoalStepAnswer.After(Math.Max(step.DelayMs, 1), next), GoalStepperEnd.Running, step);
        var end = step.End switch
        {
            AiRunEnd.Finished => GoalStepperEnd.Finished,
            AiRunEnd.EndStep => GoalStepperEnd.EndStep,
            AiRunEnd.NodeLimit => GoalStepperEnd.NodeLimit,
            AiRunEnd.Overflow => GoalStepperEnd.Overflow,
            _ => GoalStepperEnd.Killed,
        };
        return new(SessionGoalStepAnswer.End, end, step);
    }

    /// <summary>
    /// ARC-DEV-140 (MIG13 S8b, goal-table.md §3.5): steps a projectile's run (goal 36) over the projectile ports (<see cref="IAiProjectileWorld"/>). A run whose current goal is no projectile goal ends as
    /// <see cref="GoalStepperEnd.UnknownGoal"/>. A killed run, the node limit or an overflow drops the run, and the caller runs its end nodes (<see cref="AiExecutorRules.EndProjectileRun"/>); the end step and a pop
    /// ran them themselves.
    /// </summary>
    public static GoalStepperResult StepProjectile(GoalRun run, IAiProjectileWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if (!AiExecutorRules.IsProjectileGoal(run.Top.Goal)) return new(SessionGoalStepAnswer.End, GoalStepperEnd.UnknownGoal, null);
        var step = AiExecutorRules.StepProjectile(run, world);
        if (!step.Ended && step.Run is { } next) return new(SessionGoalStepAnswer.After(Math.Max(step.DelayMs, 1), next), GoalStepperEnd.Running, step);
        var end = step.End switch
        {
            AiRunEnd.Finished => GoalStepperEnd.Finished,
            AiRunEnd.EndStep => GoalStepperEnd.EndStep,
            AiRunEnd.NodeLimit => GoalStepperEnd.NodeLimit,
            AiRunEnd.Overflow => GoalStepperEnd.Overflow,
            _ => GoalStepperEnd.Killed,
        };
        return new(SessionGoalStepAnswer.End, end, step);
    }

    /// <summary>
    /// ARC-DEV-141 (MIG13 S8c, goal-table.md §3.5): steps a knockback's run (goals 43 and 64) over the knockback ports (<see cref="IAiKnockbackWorld"/>). A run whose current goal is no knockback goal ends as
    /// <see cref="GoalStepperEnd.UnknownGoal"/>. Neither goal has an end node (GT60), so a killed run, the node limit or an overflow need no end nodes run by the caller.
    /// </summary>
    public static GoalStepperResult StepKnockback(GoalRun run, IAiKnockbackWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if (!AiExecutorRules.IsKnockbackGoal(run.Top.Goal)) return new(SessionGoalStepAnswer.End, GoalStepperEnd.UnknownGoal, null);
        var step = AiExecutorRules.StepKnockback(run, world);
        if (!step.Ended && step.Run is { } next) return new(SessionGoalStepAnswer.After(Math.Max(step.DelayMs, 1), next), GoalStepperEnd.Running, step);
        var end = step.End switch
        {
            AiRunEnd.Finished => GoalStepperEnd.Finished,
            AiRunEnd.EndStep => GoalStepperEnd.EndStep,
            AiRunEnd.NodeLimit => GoalStepperEnd.NodeLimit,
            AiRunEnd.Overflow => GoalStepperEnd.Overflow,
            _ => GoalStepperEnd.Killed,
        };
        return new(SessionGoalStepAnswer.End, end, step);
    }
    /// <summary>
    /// ARC-DEV-142 (MIG13 S9a, goal-table.md §3.5): steps a scenery's loop run (goals 1 and 77) over the scenery ports (<see cref="IAiSceneryWorld"/>). A run whose current goal is no scenery goal ends as
    /// <see cref="GoalStepperEnd.UnknownGoal"/>. Neither goal has a modelled end node (the loop's ambient sound is not played), so a killed run, the node limit or an overflow need no end nodes run by the caller.
    /// </summary>
    public static GoalStepperResult StepScenery(GoalRun run, IAiSceneryWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if (!AiExecutorRules.IsSceneryGoal(run.Top.Goal)) return new(SessionGoalStepAnswer.End, GoalStepperEnd.UnknownGoal, null);
        var step = AiExecutorRules.StepScenery(run, world);
        if (!step.Ended && step.Run is { } next) return new(SessionGoalStepAnswer.After(Math.Max(step.DelayMs, 1), next), GoalStepperEnd.Running, step);
        var end = step.End switch
        {
            AiRunEnd.Finished => GoalStepperEnd.Finished,
            AiRunEnd.EndStep => GoalStepperEnd.EndStep,
            AiRunEnd.NodeLimit => GoalStepperEnd.NodeLimit,
            AiRunEnd.Overflow => GoalStepperEnd.Overflow,
            _ => GoalStepperEnd.Killed,
        };
        return new(SessionGoalStepAnswer.End, end, step);
    }

    /// <summary>
    /// ARC-DEV-143 (MIG13 S9b, goal-table.md §3.5): steps an overlay run (the eye candy 45–48, 58, 59, 75, 76 or the hover 44) over the overlay ports (<see cref="IAiOverlayWorld"/>). A run whose current goal is no overlay goal ends as
    /// <see cref="GoalStepperEnd.UnknownGoal"/>. The goals' end node (0x431B20) only clears overlay fields and a sound, which the run's presence stands for, so a killed run, the node limit or an overflow need no end nodes run by the caller.
    /// </summary>
    public static GoalStepperResult StepOverlay(GoalRun run, IAiOverlayWorld world)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(world);
        if (!AiExecutorRules.IsOverlayGoal(run.Top.Goal)) return new(SessionGoalStepAnswer.End, GoalStepperEnd.UnknownGoal, null);
        var step = AiExecutorRules.StepOverlay(run, world);
        if (!step.Ended && step.Run is { } next) return new(SessionGoalStepAnswer.After(Math.Max(step.DelayMs, 1), next), GoalStepperEnd.Running, step);
        var end = step.End switch
        {
            AiRunEnd.Finished => GoalStepperEnd.Finished,
            AiRunEnd.EndStep => GoalStepperEnd.EndStep,
            AiRunEnd.NodeLimit => GoalStepperEnd.NodeLimit,
            AiRunEnd.Overflow => GoalStepperEnd.Overflow,
            _ => GoalStepperEnd.Killed,
        };
        return new(SessionGoalStepAnswer.End, end, step);
    }
}
