using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.Simulation.Goals;
using Arcanum.World;
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

    private static (AreaAiHostRebindTests.Fixture Fixture, AreaAiHost Host) Open((int X, int Y)? standpointA = null)
    {
        var f = AreaAiHostRebindTests.Open(standpointA);
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
    public void A_retarget_the_table_refuses_keeps_the_old_run_and_its_animation()
    {
        var (f, host) = Open();
        using var fixture = f;
        // The attack starter that retargets in place proposes a run without asking the port: the table's own start interrupts, and a refusal keeps the old run.
        host.PerformAttack(f.A, AiSchedulerRules.Pc);
        var first = host.RunOf(f.A)!;
        Assert.AreEqual(GoalType.Attack, first.Bottom.Goal);
        host.Session.ReplaceGoalFrames(first.Handle, [first.Bottom, new GoalFrame(GoalType.AttemptSpell)]);
        Assert.AreEqual(GoalType.AttemptSpell, host.RunOf(f.A)!.Top.Goal);
        host.BeginAttack(f.A, AiSchedulerRules.Pc);

        host.PerformAttack(f.A, f.B);

        Assert.AreEqual(first.Handle, host.RunOf(f.A)!.Handle, "the current goal's priority 4 refused the interrupt: the NPC goes on with its run");
        Assert.AreEqual(1, host.Session.QueryGoals().RunsOf(f.A).Length);
        Assert.IsNotNull(host.AttackPose(f.A, host.NowMs), "the refused start's end nodes are the proposed run's: the kept run's attack animation is not cut");
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

    [TestMethod]
    public void A_no_interrupt_overlay_is_left_running_beside_the_new_run()
    {
        var (f, host) = Open();
        using var fixture = f;
        var overlay = Own(host, f.A, GoalType.SpreadOut);

        host.PerformAttack(f.A, AiSchedulerRules.Pc);

        Assert.IsTrue(Alive(host, overlay), "a no-interrupt goal reports success at level 3 and keeps running (GS16): a drop at the highest level would have ended it");
        Assert.AreEqual(2, host.Session.QueryGoals().RunsOf(f.A).Length, "the attack runs beside it");
        Assert.IsTrue(host.Session.QueryGoals().RunsOf(f.A).Any(r => r.Bottom.Goal == GoalType.Attack), host.TraceOf(f.A)?.Perform);
    }

    [TestMethod]
    public void A_stepped_run_that_is_interrupted_has_its_end_nodes_run_once_at_the_mark()
    {
        var (f, host) = Open();
        using var fixture = f;
        var walk = Own(host, f.A, GoalType.MoveToTile);
        Assert.IsNotNull(host.Session.BeginGoalStep(walk), "the run is being stepped");
        host.BeginAttack(f.A, AiSchedulerRules.Pc);
        Assert.IsNotNull(host.AttackPose(f.A, host.NowMs));

        host.InterruptAnimations(f.A);

        Assert.IsNull(host.AttackPose(f.A, host.NowMs), "the marked run's end nodes ran at the mark (GS16: the stepper ends it, the host's end nodes are not repeated)");
        Assert.IsTrue(host.Session.QueryGoals().TryGet(walk, out var marked) && marked.Done, "only marked: the step's close frees it");
        Assert.AreEqual(0, host.Session.DrainGoalEnds().Length, "no unclaimed end to run them again");
    }

    [TestMethod]
    public void A_rounds_start_reaches_a_not_counted_run_of_a_participant_but_not_of_another_critter()
    {
        var (f, host) = Open();
        using var fixture = f;
        var loopA = Own(host, f.A, GoalType.AnimateLoop);
        var loopB = Own(host, f.B, GoalType.AnimateLoop);
        var world = new AreaCombatWorld(1, () =>
            [new(SessionCombatant.Hero, new CellCoord(58, 12), 0, null), new(SessionCombatant.Critter(f.A), new CellCoord(62, 10), 0, null)],
            entity => entity == f.A ? new SessionCombatantFacts { Speed = 7, Life = new LifeFacts() } : null);
        var begun = f.Session.StartCombatRound(f.Session.Version, world);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (begun.Disposition, begun.Failure));

        host.ObserveRound();

        Assert.IsFalse(Alive(host, loopA), "the sweep skips a not-counted run (0x424310); each participant's own pass at level 3 ends it (0x424070)");
        Assert.IsTrue(Alive(host, loopB), "a critter that is no participant keeps its not-counted run");
    }

    [TestMethod]
    public void A_standpoint_move_interrupts_at_level_4_and_moves_whatever_the_interrupt_answers()
    {
        var (f, host) = Open(standpointA: (40, 40));
        using var fixture = f;
        var walk = Own(host, f.A, GoalType.MoveToTile);
        var attempt = Own(host, f.A, GoalType.AttemptSpell);
        // Level 4 stops at the first refusal of the owner's runs in slot order: the walk (slot 0) ended, the attempt (slot 1, priority 4) refused.
        host.RunStandpoints(f.A);

        Assert.IsFalse(Alive(host, walk), "the walk, priority 2, ended at level 4 (GS20)");
        Assert.IsTrue(Alive(host, attempt), "a spell attempt, priority 4, needs a level above 4");
        Assert.AreEqual(new CellCoord(40, 40), host.Tile(f.A), "0x4ACDB0 passes the interrupt's answer by and moves the NPC anyway (GT21): " + host.TraceOf(f.A)?.Route);
    }

    [TestMethod]
    public void The_starters_plans_follow_the_original_levels()
    {
        foreach (var goal in new[] { GoalType.MoveToTile, GoalType.RunToTile, GoalType.Follow, GoalType.Attack, GoalType.ThrowItem, GoalType.SpreadOut })
            CollectionAssert.AreEqual(new[] { 3 }, AreaAiHost.StarterPlan(goal).LevelsFor(GoalDescriptors.Of(goal)!).ToArray(), $"goal {goal}: level 3 (GS13)");
        Assert.IsTrue(AreaAiHost.StarterPlan(GoalType.Attack).RefusesDuplicate && AreaAiHost.StarterPlan(GoalType.Follow).RefusesDuplicate
            && AreaAiHost.StarterPlan(GoalType.ThrowItem).RefusesDuplicate && AreaAiHost.StarterPlan(GoalType.SpreadOut).RefusesDuplicate, "attack, follow, throw and spread out refuse a duplicate");
        Assert.IsFalse(AreaAiHost.StarterPlan(GoalType.MoveToTile).RefusesDuplicate);
        foreach (var goal in new[] { GoalType.CastSpell, GoalType.CastSpellWithAnimation, GoalType.Flee, GoalType.UseSkill, GoalType.UseItem, GoalType.PickupItem })
            CollectionAssert.AreEqual(new[] { GoalDescriptors.Of(goal)!.Priority }, AreaAiHost.StarterPlan(goal).LevelsFor(GoalDescriptors.Of(goal)!).ToArray(), $"goal {goal}: its own priority (GS12)");
    }
}
