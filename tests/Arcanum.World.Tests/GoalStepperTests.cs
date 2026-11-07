using System.Reflection;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>
/// ARC-DEV-134 (MIG13 S4, docs/architecture/goal-table.md §3.4): the world runtime's one stepper dispatches a run by its current goal to the NPC executor's node
/// interpreter, ends a goal with no interpreter as a typed unknown goal, calls the end hooks in the original's order and answers the next frame delay (0 is the next
/// frame). The node ports are a stand-in that answers every question with its default. Entity ids are invented; no original data.
/// </summary>
[TestClass]
public sealed class GoalStepperTests
{
    private const string Npc = "npc:a";

    private sealed class Hooks : IGoalStepHooks
    {
        public List<string> Calls { get; } = [];
        public void EndDropped(GoalRun run) => Calls.Add("dropped " + (int)run.Top.Goal);
        public void EndStepped(GoalRun run) => Calls.Add("end step " + (int)run.Top.Goal);
        public void Restart(GoalRun run) => Calls.Add("restart " + (int)run.Top.Goal);
    }

    /// <summary>Node ports that answer every question with the default of its type (no tile, no object, not in a round) and record nothing.</summary>
    public class DefaultWorld : DispatchProxy
    {
        public int Asked { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Asked++;
            var returns = targetMethod?.ReturnType ?? typeof(void);
            return returns == typeof(void) || !returns.IsValueType ? null : Activator.CreateInstance(returns);
        }
    }

    private static (IAiGoalWorld World, DefaultWorld Proxy) Ports()
    {
        var world = DispatchProxy.Create<IAiGoalWorld, DefaultWorld>();
        return (world, (DefaultWorld)(object)world);
    }

    private sealed class NoRolls : IAiRollSource
    {
        public int Roll(AiRollPurpose purpose, int min, int max) => min;
    }

    private static GoalRun Run(GoalType goal, string? target = null) => new(Npc, new GoalHandle(3, 7), new GoalFrame(goal, target));

    [TestMethod]
    public void A_goal_with_no_interpreter_ends_typed_with_its_end_nodes_and_no_restart()
    {
        var (world, proxy) = Ports();
        var hooks = new Hooks();
        var result = GoalStepper.Step(Run(GoalType.Talk), world, new NoRolls(), CombatAnomalyProfile.Steamwoven, hooks);
        Assert.AreEqual(GoalStepperEnd.UnknownGoal, result.End);
        Assert.IsNull(result.Answer.NextDelayMs, "the run ends");
        Assert.IsNull(result.Step, "no interpreter ran");
        Assert.AreEqual(0, proxy.Asked, "the node ports were not asked");
        CollectionAssert.AreEqual(new[] { "dropped 21" }, hooks.Calls);
        Assert.IsFalse(AiExecutorRules.Interprets(GoalType.Talk));
    }

    [TestMethod]
    public void A_run_whose_bottom_goal_pops_finishes_and_restarts_the_idle_animation()
    {
        // The attack's first node finds no target (the ports answer no tile): the goal pops, the bottom goal is gone and the run is done (AG2: no end step).
        var (world, _) = Ports();
        var hooks = new Hooks();
        var result = GoalStepper.Step(Run(GoalType.Attack, "pc"), world, new NoRolls(), CombatAnomalyProfile.Steamwoven, hooks);
        Assert.AreEqual(GoalStepperEnd.Finished, result.End);
        Assert.IsNull(result.Answer.NextDelayMs);
        Assert.AreEqual((AiRunEnd.Finished, AiExecutorReason.AttackTargetGone), (result.Step!.End, result.Step.Reason));
        CollectionAssert.AreEqual(new[] { "restart 18" }, hooks.Calls, "no end hook after a finished run, then the idle restart");
    }

    [TestMethod]
    public void A_waiting_step_answers_its_delay_with_the_run_it_left_and_a_reason_holds_until_another_is_reported()
    {
        // The pause goal 6 waits one second at its first node (its only node with a delay), then ends at its second.
        var (world, _) = Ports();
        var hooks = new Hooks();
        var first = GoalStepper.Step(Run(GoalType.MoveToPause), world, new NoRolls(), CombatAnomalyProfile.Steamwoven, hooks, AiExecutorReason.NoPath);
        Assert.AreEqual(GoalStepperEnd.Running, first.End);
        Assert.AreEqual(AiExecutorRules.NoPathPauseMs, first.Answer.NextDelayMs);
        Assert.AreEqual(2, first.Answer.Run!.Top.Node);
        Assert.AreEqual(new GoalHandle(3, 7), first.Answer.Run.Handle, "the answered run keeps its handle and owner");
        Assert.AreEqual(AiExecutorReason.NoPath, first.Step!.Reason, "the carried reason holds");
        Assert.AreEqual(0, hooks.Calls.Count);

        var second = GoalStepper.Step(first.Answer.Run, world, new NoRolls(), CombatAnomalyProfile.Steamwoven, hooks);
        Assert.AreEqual(GoalStepperEnd.EndStep, second.End);
        CollectionAssert.AreEqual(new[] { "end step 6", "restart 6" }, hooks.Calls, "the end step leaves its queued casts, then the idle restart");
    }
}
