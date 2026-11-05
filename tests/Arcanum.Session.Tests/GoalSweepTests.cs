using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Arcanum.Session.Tests.SessionFixtures;

namespace Arcanum.Session.Tests;

/// <summary>
/// ARC-DEV-135 (MIG13 S5, docs/architecture/goal-table.md §3.3): the session's sweeps. The round start's sweep (0x424310) interrupts every run whose current goal is
/// counted at level 3 by the rule and leaves the rest; the stepper's head (0x423566) sweeps a full table at level 3 once and goes on with its own run (the sweep's ends
/// are the owner host's to claim); a map's close clears the table-full flag together with the runs. Entity ids are invented; no original data.
/// </summary>
[TestClass]
public sealed class GoalSweepTests
{
    private const string A = "npc:a";
    private const string B = "npc:b";
    private const string C = "npc:c";
    private const string D = "npc:d";

    private sealed class NoWorld : ISessionEventWorld
    {
        public LifeFacts? CritterFacts(SessionMapId map, string entity) => null;
    }

    private sealed class Ports : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }

    private static GoalHandle Started(GameSessionOwner owner, GoalType goal, string who, GoalStartPlan? plan = null)
    {
        var result = owner.StartGoal(new(who, plan ?? GoalStarters.NoInterrupt, new GoalFrame(goal)), new Ports());
        Assert.IsTrue(result.Started, $"{goal}: {result.Failure} {result.Outcome}");
        return result.Handle!.Value;
    }

    private static void Jump(GameSessionOwner owner, long ms) =>
        Assert.AreEqual(ClockMutationDisposition.Changed, owner.JumpClocks(owner.QueryClockSnapshot().Token, ms).Disposition);

    private static GameEvent[] Steps(GameSessionOwner owner) => owner.QueryEvents().Where(e => e.Kind == SessionEventKinds.GoalStep).ToArray();

    [TestMethod]
    public void The_round_starts_sweep_ends_the_counted_runs_at_level_3_and_leaves_the_rest()
    {
        using var owner = Create();
        var walk = Started(owner, GoalType.MoveToTile, A);
        var spell = Started(owner, GoalType.AttemptSpell, B);
        var dying = Started(owner, GoalType.Dying, C);
        var loop = Started(owner, GoalType.AnimateLoop, D);
        var overlay = Started(owner, GoalType.EyeCandy, A);

        var swept = owner.SweepGoalsForRoundStart();

        Assert.AreEqual((SessionGoalFailure.None, true), (swept.Failure, swept.Succeeded), "the original's sweep always answers 1");
        CollectionAssert.AreEqual(new[] { walk }, swept.Ended.Select(e => e.Run.Handle).ToArray(), "priority 2 ends at level 3; priority 4 and 5 refuse, the overlay is left running");
        Assert.AreEqual(GoalEndKind.Interrupted, swept.Ended.Single().Kind);
        CollectionAssert.AreEqual(new[] { GoalInterruptOutcome.Ended, GoalInterruptOutcome.Refused, GoalInterruptOutcome.Refused, GoalInterruptOutcome.LeftRunning },
            swept.Steps.Select(s => s.Outcome).ToArray(), "the loop (priority 1, not counted) is not in the sweep at all");
        CollectionAssert.AreEquivalent(new[] { spell, dying, loop, overlay }, owner.QueryGoals().Runs.Select(r => r.Handle).ToArray());
        Assert.IsNull(owner.QueryGoalStep(walk), "the ended run lost its step");
        Assert.AreEqual(4, Steps(owner).Length, "the others keep theirs");

        // The round's second pass, each participant at level 3, does reach the not-counted run (priority 1 ends at level 3).
        var participant = owner.InterruptGoals(D, GoalInterruptRules.OrdinaryActions);
        Assert.AreEqual(GoalEndKind.Interrupted, participant.Ended.Single().Kind);
        Assert.IsNull(owner.QueryGoals().RunsOf(D).FirstOrDefault());
    }

    [TestMethod]
    public void A_full_table_is_swept_at_level_3_by_the_next_step_which_then_finds_its_run_gone_or_goes_on()
    {
        using var owner = Create();
        var moving = new List<GoalHandle>();
        for (var i = 0; i < GoalRunTable.SlotCount - 16; i++) moving.Add(Started(owner, GoalType.MoveToTile, $"obj:{i}"));
        var overlays = new List<GoalHandle>();
        for (var i = 0; i < 16; i++) overlays.Add(Started(owner, GoalType.EyeCandy, $"fx:{i}"));
        var full = owner.StartGoal(new("obj:x", GoalStarters.NoInterrupt, new GoalFrame(GoalType.MoveToTile)), new Ports());
        Assert.AreEqual(GoalStartOutcome.TableFull, full.Outcome);
        Assert.IsTrue(owner.QueryGoals().ExhaustionPending);

        Jump(owner, 5);
        var pass = owner.ProcessEvents(new NoWorld());
        Assert.AreEqual(GoalRunTable.SlotCount, pass.GoalSteps.Count, "every run's first step is due");

        // The stepper's head (GS2): the flag is set, so every run is interrupted at level 3 before the step goes on with its own run.
        Assert.IsNull(owner.BeginGoalStep(moving[0]), "the sweep ended the run about to step");
        Assert.IsFalse(owner.QueryGoals().ExhaustionPending, "and cleared the flag");
        Assert.AreEqual(16, owner.QueryGoals().InUseCount, "the overlays are left running (level 3 stops no overlay)");
        var drained = owner.DrainGoalEnds();
        CollectionAssert.AreEquivalent(moving.ToArray(), drained.Select(e => e.Run.Handle).ToArray(), "the ends are unclaimed: the owners' host runs their end nodes");
        Assert.IsTrue(drained.All(e => e.Kind == GoalEndKind.Interrupted));
        Assert.IsNull(owner.BeginGoalStep(moving[1]), "every other reported step finds its run gone");

        // With the flag clear the next step opens as usual and sweeps nothing more.
        var step = owner.BeginGoalStep(overlays[0]);
        Assert.AreEqual(overlays[0], step!.Handle);
        Assert.AreEqual(16, owner.QueryGoals().InUseCount);
        Assert.AreEqual(0, owner.DrainGoalEnds().Length);
    }

    [TestMethod]
    public void An_overlay_survives_the_sweep_and_its_step_opens_once()
    {
        using var owner = Create();
        var first = Started(owner, GoalType.EyeCandy, "fx:first");
        for (var i = 1; i < GoalRunTable.SlotCount; i++) Started(owner, GoalType.EyeCandy, $"fx:{i}");
        Assert.AreEqual(GoalStartOutcome.TableFull, owner.StartGoal(new("fx:x", GoalStarters.NoInterrupt, new GoalFrame(GoalType.EyeCandy)), new Ports()).Outcome);
        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        var run = owner.BeginGoalStep(first);
        Assert.IsNotNull(run, "an overlay survives the sweep (no-interrupt goals are left running below level 6), so its step opens");
        Assert.IsFalse(owner.QueryGoals().ExhaustionPending);
        Assert.IsNull(owner.BeginGoalStep(first), "one step is open at a time");
        Assert.AreEqual(GoalRunTable.SlotCount, owner.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void A_map_close_clears_the_table_full_flag_with_the_runs()
    {
        using var owner = Create();
        for (var i = 0; i < GoalRunTable.SlotCount; i++) Started(owner, GoalType.EyeCandy, $"fx:{i}");
        Assert.AreEqual(GoalStartOutcome.TableFull, owner.StartGoal(new("fx:x", GoalStarters.NoInterrupt, new GoalFrame(GoalType.EyeCandy)), new Ports()).Outcome);
        Assert.IsTrue(owner.QueryGoals().ExhaustionPending);

        Assert.AreEqual(GameSessionDisposition.Changed, owner.PlaceActor(owner.Version, new(Cave, new(5, 5), Start.Facing)).Disposition);

        Assert.AreEqual(0, owner.QueryGoals().InUseCount, "the close ended every run at level 6");
        Assert.IsFalse(owner.QueryGoals().ExhaustionPending, "a flag of the map left has nothing to sweep on the next one");
        var handle = Started(owner, GoalType.MoveToTile, A, GoalStarters.Generic);
        Jump(owner, 5);
        owner.ProcessEvents(new NoWorld());
        Assert.IsNotNull(owner.BeginGoalStep(handle), "the first step of the new map is not swept");
    }
}
