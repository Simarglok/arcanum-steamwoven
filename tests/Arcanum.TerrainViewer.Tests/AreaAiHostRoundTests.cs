using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-136 (MIG13 S5b, docs/architecture/goal-table.md §3.7, TL39, TL41): the round on the table. The round start's interrupts call nothing (TH10), a turn set's free of the last run
/// calls the animation callback for the new participant, the table's start gate refuses the gated starters for a critter that is not current, and the executor's duplicate tests read
/// every run of the NPC. Invented sectors and NPCs, no original data.
/// </summary>
[TestClass]
public sealed class AreaAiHostRoundTests
{
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

    // The hero and the two NPCs of the fixture in a round: the non-party critters act first in scan order, the hero last.
    private static AreaCombatWorld RoundWorld(AreaAiHostRebindTests.Fixture f) => new(1, () =>
        [new(SessionCombatant.Hero, new CellCoord(58, 12), 0, null), new(SessionCombatant.Critter(f.A), new CellCoord(62, 10), 0, null), new(SessionCombatant.Critter(f.B), new CellCoord(63, 10), 0, null)],
        entity => new SessionCombatantFacts { Speed = 7, Life = new LifeFacts() });

    private static AreaCombatWorld BeginRound(AreaAiHostRebindTests.Fixture f)
    {
        var world = RoundWorld(f);
        var begun = f.Session.StartCombatRound(f.Session.Version, world);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None), (begun.Disposition, begun.Failure));
        Assert.AreEqual(SessionCombatant.Critter(f.A), f.Session.QueryCombatRound()!.Actor);
        return world;
    }

    [TestMethod]
    public void A_rounds_start_calls_no_callback_and_a_turn_sets_free_of_the_last_run_calls_it_for_the_new_participant()
    {
        var (f, host) = Open();
        using var fixture = f;
        var startWalk = Own(host, f.A, GoalType.MoveToTile);
        var world = BeginRound(f);
        var callbacks = f.Session.AnimationCallbacks;

        host.ObserveRound();

        Assert.IsFalse(Alive(host, startWalk), "the round's start ended the walk");
        Assert.AreEqual(callbacks, f.Session.AnimationCallbacks, "the interrupts of a round's start come before the callback is registered and the round flag set (TH10)");
        Assert.IsNull(f.Session.QueryRealStep(SessionRealStep.TurnBasedStep));

        // A starts a walk in its subturn and the subturn ends from outside: B's turn set interrupts it, the free leaves no significant goal and calls the callback.
        var walk = Own(host, f.A, GoalType.MoveToTile);
        Assert.AreEqual(GameSessionDisposition.Changed, f.Session.EndCombatTurn(f.Session.Version, SessionCombatant.Critter(f.A), world).Disposition);
        Assert.AreEqual(SessionCombatant.Critter(f.B), f.Session.QueryCombatRound()!.Actor);

        host.ObserveRound();

        Assert.IsFalse(Alive(host, walk), "the turn set interrupted the previous critter's run");
        Assert.AreEqual(callbacks + 1, f.Session.AnimationCallbacks, "its free called the callback");
        Assert.IsNotNull(f.Session.QueryRealStep(SessionRealStep.TurnBasedStep), "the callback asked for the turn-based event");
        host.ObserveRound();
        Assert.AreEqual(callbacks + 1, f.Session.AnimationCallbacks, "once for the turn set");
    }

    [TestMethod]
    public void A_free_that_leaves_another_critters_significant_goal_calls_nothing()
    {
        var (f, host) = Open();
        using var fixture = f;
        var world = BeginRound(f);
        host.ObserveRound();
        var callbacks = f.Session.AnimationCallbacks;
        var walk = Own(host, f.A, GoalType.MoveToTile);
        var attack = Own(host, f.B, GoalType.Attack);

        Assert.IsTrue(host.Session.InterruptGoal(walk, GoalInterruptRules.MaxLevel).Succeeded);
        Assert.AreEqual(callbacks, f.Session.AnimationCallbacks, "B's attack still counts: the whole map's significant animations must end (TL39)");
        Assert.IsTrue(Alive(host, attack));
        Assert.IsTrue(host.Session.InterruptGoal(attack, GoalInterruptRules.MaxLevel).Succeeded);
        Assert.AreEqual(callbacks + 1, f.Session.AnimationCallbacks);
        Assert.IsNotNull(f.Session.QueryRealStep(SessionRealStep.TurnBasedStep));
        _ = world;
    }

    [TestMethod]
    public void The_tables_start_gate_refuses_the_gated_starters_for_a_critter_that_is_not_current_and_nothing_else()
    {
        var (f, host) = Open();
        using var fixture = f;
        var frame = new GoalFrame(GoalType.MoveToTile);
        GoalStartRequest Request(string owner, GoalStartPlan plan) => new(owner, plan, frame);
        Assert.IsTrue(host.StartGatePasses(Request(f.B, GoalStarters.Attack)), "outside a round every start passes");
        BeginRound(f);
        foreach (var plan in new[] { GoalStarters.Walk, GoalStarters.Run, GoalStarters.Follow, GoalStarters.Attack, GoalStarters.Throw, GoalStarters.SpreadOut })
        {
            Assert.IsTrue(host.StartGatePasses(Request(f.A, plan)), $"{plan.Kind}: the current participant passes");
            Assert.IsFalse(host.StartGatePasses(Request(f.B, plan)), $"{plan.Kind}: another participant is refused (0x4339A0, 0x4348E0)");
            Assert.IsFalse(host.StartGatePasses(Request("npc:stranger", plan)), $"{plan.Kind}: so is a critter outside the round");
        }
        foreach (var plan in new[] { GoalStarters.Generic, GoalStarters.NoInterrupt, GoalStarters.Stun, GoalStarters.Projectile, GoalStarters.KnockDown })
            Assert.IsTrue(host.StartGatePasses(Request(f.B, plan)), $"{plan.Kind}: the hit reactions, dying and the goals pushed for a critter off its turn start (TL41)");
    }

    [TestMethod]
    public void A_gated_start_for_a_critter_off_its_turn_starts_no_run()
    {
        var (f, host) = Open();
        using var fixture = f;
        BeginRound(f);
        host.ObserveRound();

        host.PerformAttack(f.B, AiSchedulerRules.Pc);

        Assert.AreEqual(0, host.Session.QueryGoals().RunsOf(f.B).Length, host.TraceOf(f.B)?.Perform);
        StringAssert.Contains(host.TraceOf(f.B)!.Perform, "Gate");
    }

    [TestMethod]
    public void The_duplicate_tests_read_every_run_of_the_npc_not_only_its_first()
    {
        var (f, host) = Open();
        using var fixture = f;
        Own(host, f.A, GoalType.SpreadOut);
        host.PerformAttack(f.A, AiSchedulerRules.Pc);
        Assert.AreEqual(2, host.Session.QueryGoals().RunsOf(f.A).Length, "an overlay in the lower slot and the attack beside it");
        Assert.AreEqual(GoalType.SpreadOut, host.RunOf(f.A)!.Bottom.Goal, "the first run is the overlay, which the old tests (the first run only) saw");
        var attack = host.Session.QueryGoals().RunsOf(f.A).Single(r => r.Bottom.Goal == GoalType.Attack).Handle;

        host.PerformAttack(f.A, AiSchedulerRules.Pc);

        StringAssert.Contains(host.TraceOf(f.A)!.Perform, "AlreadyQueued", "the attack on the same target is found among all the runs (0x44E710)");
        Assert.AreEqual(2, host.Session.QueryGoals().RunsOf(f.A).Length);
        Assert.IsTrue(Alive(host, attack), "the queued attack was kept, not interrupted and restarted");
    }
}
