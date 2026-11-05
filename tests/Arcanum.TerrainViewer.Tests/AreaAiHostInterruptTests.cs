using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-135 (MIG13 S5, docs/architecture/goal-table.md §3.3): the AI host's interrupts are the table's rule, not a drop. A starter interrupts the NPC's runs at its
/// level (3 for the walks, attack, throw, follow and spread out, the goal's own priority for the rest) and a run whose current goal refuses keeps the NPC's run and refuses the
/// start; every run the table ends from outside the stepper has its end nodes run once by the host (no fidget, no hook); the AI tick's far path interrupts at level 4; the
/// object's destroy kills with no rule; a round's start sweeps the table at level 3; the AI pre-check reads the main run's priority. Invented sectors and NPCs, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostInterruptTests
{
    private const string Other = "npc:other";

    private sealed class Ports : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Open()
    {
        var f = AreaAiHostRebindTests.Open();
        var host = f.Build(f.Scene(0, AreaAiHostRebindTests.Sector(0, 0), [AreaAiHostRebindTests.NpcA(f), new AreaAiNpc(f.B, f.RowB, new CellCoord(130, 10))]));
        return (f, host);
    }

    private static GoalHandle Own(AreaAiHost host, string owner, GoalType goal, GoalStartPlan? plan = null)
    {
        var started = host.Session.StartGoal(new(owner, plan ?? GoalStarters.NoInterrupt, new GoalFrame(goal)), new Ports());
        Assert.IsTrue(started.Started, $"{goal}: {started.Failure} {started.Outcome}");
        return started.Handle!.Value;
    }

    private static bool Alive(AreaAiHost host, GoalHandle handle) => host.Session.QueryGoals().TryGet(handle, out var run) && !run.Done;

    [TestMethod]
    public void A_starter_ends_the_replaced_run_at_level_3_with_its_end_nodes_and_starts_its_own()
    {
        var (f, host) = Open();
        using var fixture = f;
        var walk = Own(host, f.A, GoalType.MoveToTile);
        host.BeginAttack(f.A, AiSchedulerRules.Pc);
        Assert.IsNotNull(host.AttackPose(f.A, host.NowMs), "the end node's witness: an attack animation plays");

        host.PerformAttack(f.A, AiSchedulerRules.Pc);

        Assert.IsFalse(Alive(host, walk), "the attack starter's interrupt at level 3 ended the walk (priority 2)");
        Assert.AreEqual(GoalType.Attack, host.RunOf(f.A)?.Bottom.Goal, host.TraceOf(f.A)?.Perform);
        Assert.IsNull(host.AttackPose(f.A, host.NowMs), "the ended run's end nodes ran: its attack animation is not drawn on");
        Assert.IsFalse(host.DrainNotices().Any(n => n.Kind == AreaAiNoticeKind.Fidget), "an end from outside the stepper restarts no idle animation (GS24)");
        Assert.AreEqual(1, host.Session.QueryGoals().RunsOf(f.A).Length, "one run: the replaced one is gone");
    }

    [TestMethod]
    public void A_run_whose_current_goal_refuses_level_3_keeps_the_npcs_run_and_refuses_the_start()
    {
        var (f, host) = Open();
        using var fixture = f;
        var attempt = Own(host, f.A, GoalType.AttemptSpell);

        host.PerformAttack(f.A, AiSchedulerRules.Pc);

        Assert.IsTrue(Alive(host, attempt), "a spell attempt (priority 4) is ended only above level 4: the interrupt is refused");
        Assert.AreEqual(attempt, host.RunOf(f.A)!.Handle);
        Assert.AreEqual(1, host.Session.QueryGoals().RunsOf(f.A).Length, "no second run started");
        StringAssert.Contains(host.TraceOf(f.A)!.Perform, "Interrupt", "the starter's refusal");
    }

    [TestMethod]
    public void A_start_the_table_refuses_after_the_starter_ran_frees_what_its_run_took_and_keeps_the_old_run()
    {
        var (f, host) = Open();
        using var fixture = f;
        // The attack starter that retargets in place proposes a run without asking the port: the table's own start interrupts, and a refusal keeps the old run.
        host.PerformAttack(f.A, AiSchedulerRules.Pc);
        var first = host.RunOf(f.A)!;
        Assert.AreEqual(GoalType.Attack, first.Bottom.Goal);
        host.Session.ReplaceGoalFrames(first.Handle, [first.Bottom, new GoalFrame(GoalType.AttemptSpell)]);
        Assert.AreEqual(GoalType.AttemptSpell, host.RunOf(f.A)!.Top.Goal);

        host.PerformAttack(f.A, f.B);

        Assert.AreEqual(first.Handle, host.RunOf(f.A)!.Handle, "the current goal's priority 4 refused the interrupt: the NPC goes on with its run");
        Assert.AreEqual(1, host.Session.QueryGoals().RunsOf(f.A).Length);
    }

    [TestMethod]
    public void The_far_path_interrupts_at_level_4_which_ends_a_walk_and_leaves_a_spell_attempt()
    {
        var (f, host) = Open();
        using var fixture = f;
        var walk = Own(host, f.A, GoalType.MoveToTile);
        var attempt = Own(host, f.B, GoalType.AttemptSpell);
        host.BeginAttack(f.A, AiSchedulerRules.Pc);
        Assert.IsNotNull(host.AttackPose(f.A, host.NowMs));

        host.InterruptAnimations(f.A);
        host.InterruptAnimations(f.B);

        Assert.IsFalse(Alive(host, walk), "priority 2 ends at level 4");
        Assert.IsNull(host.AttackPose(f.A, host.NowMs), "with its end nodes");
        Assert.IsTrue(Alive(host, attempt), "priority 4 needs a level above 4 (GS18)");
        StringAssert.Contains(host.TraceOf(f.B)!.Step, "refused");
    }

    [TestMethod]
    public void The_objects_destroy_kills_every_run_with_no_rule_and_runs_their_end_nodes()
    {
        var (f, host) = Open();
        using var fixture = f;
        var overlay = Own(host, f.A, GoalType.EyeCandy);
        var dying = Own(host, f.A, GoalType.Dying);
        host.BeginAttack(f.A, AiSchedulerRules.Pc);
        Assert.IsNotNull(host.AttackPose(f.A, host.NowMs));

        host.Destroy(f.A);

        Assert.IsFalse(Alive(host, overlay) || Alive(host, dying), "a kill ends the no-interrupt overlay and the priority-5 goal alike");
        Assert.AreEqual(0, host.Session.QueryGoals().RunsOf(f.A).Length);
        Assert.IsNull(host.AttackPose(f.A, host.NowMs), "the end nodes ran");
    }

    [TestMethod]
    public void The_ai_pre_check_reads_the_main_runs_priority_and_an_overlay_is_not_the_main_run()
    {
        var (f, host) = Open();
        using var fixture = f;
        Assert.AreEqual(0, host.AnimationPriority(f.A), "no run: the NPC is decided for");
        Own(host, f.A, GoalType.SpreadOut);
        Assert.AreEqual(0, host.AnimationPriority(f.A), "an overlay is no main run (0x423300 skips a no-interrupt bottom goal)");
        Own(host, f.A, GoalType.Attack);
        Assert.AreEqual(3, host.AnimationPriority(f.A), "the main run's bottom goal: busy for the pass (above 2)");
        Assert.AreEqual(GoalType.SpreadOut, host.RunOf(f.A)!.Bottom.Goal, "the first run is the overlay in the lower slot, which the main-run query steps over");
    }

    [TestMethod]
    public void A_rounds_start_sweeps_the_table_at_level_3_once()
    {
        var (f, host) = Open();
        using var fixture = f;
        var walk = Own(host, f.A, GoalType.MoveToTile);
        var attempt = Own(host, f.B, GoalType.AttemptSpell);
        var overlay = Own(host, Other, GoalType.EyeCandy);
        host.BeginAttack(f.A, AiSchedulerRules.Pc);
        host.ObserveRound();
        Assert.IsTrue(Alive(host, walk), "no round: nothing is swept");

        var world = new AreaCombatWorld(1, () => [new(SessionCombatant.Hero, new CellCoord(58, 12), 0, null)], _ => null);
        var begun = f.Session.StartCombatRound(f.Session.Version, world);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (begun.Disposition, begun.Failure));
        Assert.IsNotNull(f.Session.QueryCombatRound());

        host.ObserveRound();

        Assert.IsFalse(Alive(host, walk), "the round's start ended the walk (0x424310 at level 3)");
        Assert.IsNull(host.AttackPose(f.A, host.NowMs), "with its end nodes");
        Assert.IsTrue(Alive(host, attempt), "a spell attempt refuses level 3, as in the original, whose sweep only logs it");
        Assert.IsTrue(Alive(host, overlay), "a no-interrupt overlay is left running");

        var again = Own(host, f.A, GoalType.MoveToTile);
        host.ObserveRound();
        Assert.IsTrue(Alive(host, again), "the sweep is the round's start only");
    }
}
