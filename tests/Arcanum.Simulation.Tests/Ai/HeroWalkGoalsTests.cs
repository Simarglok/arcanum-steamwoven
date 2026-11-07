using System.Collections.Immutable;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-149 (MIG13 S10b-2b, goal-table.md S10b-2b): the nodes of the hero's walk goals 3, 4, 5, 6 and 53 over a fake world. Time is the sum of the steps' delays.</summary>
[TestClass]
public sealed class HeroWalkGoalsTests
{
    private static readonly CellCoord Start = new(10, 10);

    private sealed class World : IAiHeroWalkWorld
    {
        public CellCoord At = Start;
        public int Encumbrance, Facing = 5, TurnMs = 30;
        public bool Down, InRound, Settled = true, RefuseStep, Blocked, Far, NoPath, StraightPlan, Following, Queue;
        public readonly List<string> Log = [];
        public (CellCoord From, CellCoord To)? Pending;
        public readonly List<(CellCoord Destination, bool Running, bool Straight)> Plans = [];

        public CellCoord? Tile() => At;
        int IAiHeroWalkWorld.Encumbrance => Encumbrance;
        bool IAiHeroWalkWorld.Down => Down;
        bool IAiHeroWalkWorld.InRound => InRound;

        public AiHeroPlan PlanWalk(CellCoord destination, bool running, bool straight)
        {
            Plans.Add((destination, running, straight));
            Log.Add("plan");
            if (Far) return new(AiHeroPlanOutcome.Far);
            if (NoPath) return new(AiHeroPlanOutcome.NoPath);
            var cells = new List<CellCoord>();
            for (var at = At; at != destination;) cells.Add(at = new(at.X + Math.Sign(destination.X - at.X), at.Y + Math.Sign(destination.Y - at.Y)));
            return new(AiHeroPlanOutcome.Found, [.. cells], StraightPlan);
        }

        public AiStepBlock StepBlocked(CellCoord from, CellCoord next, bool lastOfSegment) => Blocked && lastOfSegment ? AiStepBlock.Occupied : AiStepBlock.Clear;

        public int BeginStep(CellCoord from, CellCoord next, bool running)
        {
            if (RefuseStep) return 0;
            Log.Add(running ? $"run {next.X},{next.Y}" : $"walk {next.X},{next.Y}");
            // Queue: the body holds the step until the step under way ends (ARC-DEV-163); the test lets the body begin it by hand (At and Pending).
            if (Queue) Pending = (At, next); else At = next;
            return running ? 250 : 500;
        }

        public bool StepPending(CellCoord from, CellCoord to) => Pending is { } p && p.From == from && p.To == to;
        public bool FollowRoute(CellCoord destination) { Log.Add("route"); Following = true; return true; }
        public bool RouteFollowing(CellCoord destination) => Following;
        public bool NeedsTurn(int rotation) => Settled && Facing != rotation;

        public int TurnStep(int rotation)
        {
            if (!Settled || Facing == rotation) return 0;
            Facing = (Facing + Math.Sign(rotation - Facing) + 8) % 8;
            Log.Add("turn");
            return TurnMs;
        }
    }

    private static GoalRun Run(CellCoord destination, bool run = false) =>
        new("pc", default, AiExecutorRules.HeroWalkFrame(destination, run)) { Flags = run ? GoalRunFlags.Running : GoalRunFlags.None };

    private static List<(AiGoalStepResult Step, long At)> Drive(GoalRun run, World world, int limit = 200)
    {
        var steps = new List<(AiGoalStepResult, long)>(); long at = 0;
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepHeroWalk(run, world);
            steps.Add((step, at));
            if (step.Ended) return steps;
            at += step.DelayMs; run = step.Run!;
        }
        throw new InvalidOperationException("the walk did not end");
    }

    [TestMethod]
    public void TheGoalSetIsTheWalkTheRunTheStepThePauseAndTheTurn()
    {
        foreach (var goal in new[] { GoalType.MoveToTile, GoalType.RunToTile, GoalType.AttemptMove, GoalType.MoveToPause, GoalType.Rotate })
            Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(goal), goal.ToString());
        // ARC-DEV-181: the attack goals 18 and 19 are stepped over the same ports; the NPC-only attack goals stay out.
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.Attack));
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.AttemptAttack));
        // ARC-DEV-182: the throw goal 37 is stepped over the same ports.
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.ThrowItem));
        // ARC-DEV-183: the casting goals 26 and 27 too.
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.CastSpell));
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.AttemptSpell));
        // ARC-DEV-184: the skill goal 33 and the pick pocket attempt 86 too.
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.UseSkill));
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.AttemptPickpocket));
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.AttemptPickLocks)); // ARC-DEV-185
        Assert.IsFalse(AiExecutorRules.IsHeroWalkGoal(GoalType.Kill));
        // ARC-DEV-169: the talk goal 21 and its approach 8 are stepped over the same ports.
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.Talk));
        Assert.IsTrue(AiExecutorRules.IsHeroWalkGoal(GoalType.MoveNear));
        Assert.IsFalse(AiExecutorRules.IsHeroWalkGoal(GoalType.RunNear));
    }

    [TestMethod]
    public void AWalkPlansTurnsPushesGoalFiveAndTakesOneStepATimeThenPlansAgainAndArrives()
    {
        var w = new World { Facing = LineOfFireRules.Rotation(Start, new(11, 10)) };
        var steps = Drive(Run(new(13, 10)), w);
        CollectionAssert.AreEqual(new[] { "plan", "walk 11,10", "walk 12,10", "walk 13,10" }, w.Log);
        Assert.AreEqual(1, w.Plans.Count, "one plan covers the segment; the arrival test ends the run before another is made");
        Assert.AreEqual(new CellCoord(13, 10), w.At);
        // Each step waits its time (one frame after the end frame: the table's delay is the step's time).
        CollectionAssert.AreEqual(new long[] { 500, 500, 500 }, steps.Where(s => !s.Step.Ended).Select(s => (long)s.Step.DelayMs).Skip(0).Take(3).ToArray());
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End);
    }

    [TestMethod]
    public void AHeroFacingAnotherWayGetsTheTurnGoalPushedBeforeTheFirstStepAndTheTurnPopsIntoTheWalk()
    {
        var w = new World { Facing = (LineOfFireRules.Rotation(Start, new(11, 10)) + 2) % 8 };
        var steps = Drive(Run(new(12, 10)), w);
        Assert.AreEqual("plan", w.Log[0]);
        Assert.AreEqual("turn", w.Log[1]);
        Assert.AreEqual("turn", w.Log[2], "two 45-degree steps");
        Assert.AreEqual("walk 11,10", w.Log[3], "the walk goes on when the turn pops (no new plan: node 4 pushes goal 5)");
        Assert.AreEqual(1, w.Plans.Count);
        Assert.AreEqual(w.At, new CellCoord(12, 10));
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End);
    }

    [TestMethod]
    public void AHeroThatIsNotStandingDoesNotTurn()
    {
        var w = new World { Facing = 0, Settled = false };
        Drive(Run(new(12, 10)), w);
        CollectionAssert.DoesNotContain(w.Log, "turn");
        Assert.AreEqual(new CellCoord(12, 10), w.At);
    }

    [TestMethod]
    public void GoalFourSetsTheRunFlagBelowEncumbranceFourAndItsStepsRun()
    {
        var w = new World { Facing = 5, Encumbrance = 3 };
        var first = AiExecutorRules.StepHeroWalk(new GoalRun("pc", default, AiExecutorRules.HeroWalkFrame(new(12, 10), run: true)), w);
        Assert.IsTrue(first.Run!.Flags.HasFlag(GoalRunFlags.Running), "the extra first node of goal 4");
        Assert.IsTrue(w.Plans[0].Running);
        Assert.IsTrue(w.Log.Any(l => l.StartsWith("run ", StringComparison.Ordinal)));

        var heavy = new World { Facing = 5, Encumbrance = 4 };
        var step = AiExecutorRules.StepHeroWalk(new GoalRun("pc", default, AiExecutorRules.HeroWalkFrame(new(12, 10), run: true)), heavy);
        Assert.IsFalse(step.Run!.Flags.HasFlag(GoalRunFlags.Running), "a hero at encumbrance 4 does not get the run flag");
        Assert.IsTrue(heavy.Log.All(l => !l.StartsWith("run ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ARunningGoalThreeKeepsRunningFromTheNextFrame()
    {
        var w = new World { Facing = 5 };
        var run = Run(new(14, 10), run: true).WithFrames([AiExecutorRules.HeroWalkFrame(new(14, 10), run: false)]);
        Drive(run, w);
        Assert.IsTrue(w.Log.Where(l => l.StartsWith("run ", StringComparison.Ordinal) || l.StartsWith("walk ", StringComparison.Ordinal)).All(l => l.StartsWith("run ", StringComparison.Ordinal)), "the flag is the run's, the goal stays 3");
    }

    [TestMethod]
    public void ALongPathPlansAgainAfterEachSegmentOfEightSteps()
    {
        var w = new World { Facing = 5 };
        Drive(Run(new(30, 10)), w);
        Assert.AreEqual(new CellCoord(30, 10), w.At);
        Assert.AreEqual(3, w.Plans.Count, "20 steps: 8 + 8 + 4");
        Assert.AreEqual(20, w.Log.Count(l => l.StartsWith("walk ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TheReplanFlagOfARetargetPopsGoalFiveAtTheNextStepAndPlansTheNewDestination()
    {
        var w = new World { Facing = 5 };
        var run = Run(new(30, 10));
        var step = AiExecutorRules.StepHeroWalk(run, w);
        Assert.AreEqual(1, w.Log.Count(l => l.StartsWith("walk ", StringComparison.Ordinal)));
        var retargeted = step.Run!.WithFrames([step.Run.Frames[0] with { Tile = new(10, 16) }, step.Run.Frames[1]]) with { PathFlags = step.Run.PathFlags | GoalPathFlags.Replan };
        var after = AiExecutorRules.StepHeroWalk(retargeted, w);
        Assert.AreEqual(2, w.Plans.Count, "goal 3 planned again");
        Assert.AreEqual(new CellCoord(10, 16), w.Plans[1].Destination);
        Assert.IsFalse(after.Run!.PathFlags.HasFlag(GoalPathFlags.Replan), "the plan cleared the flag");
    }

    [TestMethod]
    public void ARefusedStepEndsTheRunAndAHeroLeftBehindEndsItAtTheNextStep()
    {
        var w = new World { Facing = 5, RefuseStep = true };
        var steps = Drive(Run(new(14, 10)), w);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        Assert.AreEqual(1, w.Plans.Count);
        Assert.AreEqual(AiExecutorReason.StepBlocked, steps[^1].Step.Reason);

        var left = new World { Facing = 5 };
        var step = AiExecutorRules.StepHeroWalk(Run(new(14, 10)), left);
        left.At = Start; // the grid refused the step: the body stayed
        var next = AiExecutorRules.StepHeroWalk(step.Run!, left);
        Assert.AreEqual(AiRunEnd.EndStep, next.End, "no re-route of a refused step");
        Assert.AreEqual(1, left.Plans.Count);
    }

    [TestMethod]
    public void AStepTheBodyOnlyQueuedIsNoRefusalTheGoalLooksAgainInAFrameAndGoesOnWhenItBegins()
    {
        // ARC-DEV-163: the step is given in the last stretch of the step under way, so it begins after the table's next call; waiting the whole step is not enough (the table's wait is the step's time cut to a millisecond).
        var w = new World { Facing = 5, Queue = true };
        var first = AiExecutorRules.StepHeroWalk(Run(new(14, 10)), w);
        Assert.AreEqual(AiRunEnd.Running, first.End);
        Assert.IsTrue(w.Pending is { } queued && queued.From == Start && queued.To == new CellCoord(11, 10), "the step is queued on the body");
        var waiting = AiExecutorRules.StepHeroWalk(first.Run!, w);
        Assert.AreEqual(AiRunEnd.Running, waiting.End, "the step is queued, not refused");
        Assert.AreEqual(AiExecutorRules.HeroRoutePollMs, waiting.DelayMs, "looks again in a frame");
        Assert.AreEqual(1, w.Log.Count(l => l.StartsWith("walk ", StringComparison.Ordinal)), "nothing new is given while the step waits");
        Assert.AreEqual(1, w.Plans.Count);
        var again = AiExecutorRules.StepHeroWalk(waiting.Run!, w);
        Assert.AreEqual(AiRunEnd.Running, again.End);
        Assert.AreEqual(AiExecutorRules.HeroRoutePollMs, again.DelayMs);
        // The body begins the step: the goal gives the next one.
        w.At = new(11, 10); w.Pending = null;
        var gone = AiExecutorRules.StepHeroWalk(again.Run!, w);
        Assert.AreEqual(AiRunEnd.Running, gone.End);
        Assert.AreEqual(2, w.Log.Count(l => l.StartsWith("walk ", StringComparison.Ordinal)), "the next step is given once the body began the last");
    }

    [TestMethod]
    public void AQueuedStepDoesNotHoldBackARetargetOrAHeroThatWentDown()
    {
        // The replan flag of a retarget is read before the wait: goal 5 pops and goal 3 plans the new destination from where the hero is.
        var w = new World { Facing = 5, Queue = true };
        var first = AiExecutorRules.StepHeroWalk(Run(new(14, 10)), w);
        var waiting = AiExecutorRules.StepHeroWalk(first.Run!, w);
        Assert.AreEqual(AiExecutorRules.HeroRoutePollMs, waiting.DelayMs);
        var retargeted = waiting.Run!.WithFrames([waiting.Run.Frames[0] with { Tile = new(10, 16) }, waiting.Run.Frames[1]]) with { PathFlags = waiting.Run.PathFlags | GoalPathFlags.Replan };
        var after = AiExecutorRules.StepHeroWalk(retargeted, w);
        Assert.AreEqual(AiRunEnd.Running, after.End);
        Assert.AreEqual(2, w.Plans.Count, "goal 3 planned the new destination");
        Assert.AreEqual(new CellCoord(10, 16), w.Plans[1].Destination);

        // A hero that went down while its step waits takes no step: the run ends.
        var d = new World { Facing = 5, Queue = true };
        var begun = AiExecutorRules.StepHeroWalk(Run(new(14, 10)), d);
        d.Down = true;
        var ended = AiExecutorRules.StepHeroWalk(begun.Run!, d);
        Assert.AreEqual(AiRunEnd.EndStep, ended.End);
        Assert.AreEqual(AiExecutorReason.ObjectGone, ended.Reason);
    }

    [TestMethod]
    public void AQueuedStepThatTheBodyRefusedOrLostEndsTheRun()
    {
        var w = new World { Facing = 5, Queue = true };
        var first = AiExecutorRules.StepHeroWalk(Run(new(14, 10)), w);
        var waiting = AiExecutorRules.StepHeroWalk(first.Run!, w);
        Assert.AreEqual(AiRunEnd.Running, waiting.End);
        w.Pending = null; // the grid refused it (or the hero was moved off its cell): the body stayed
        var ended = AiExecutorRules.StepHeroWalk(waiting.Run!, w);
        Assert.AreEqual(AiRunEnd.EndStep, ended.End);
        Assert.AreEqual(AiExecutorReason.StepBlocked, ended.Reason);
        Assert.AreEqual(1, w.Plans.Count, "no re-route");
    }

    [TestMethod]
    public void TheOriginalSettingsCritterTestEndsTheWalkBeforeASegmentsLastStep()
    {
        var w = new World { Facing = 5, Blocked = true };
        var steps = Drive(Run(new(13, 10)), w);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        Assert.AreEqual(2, w.Log.Count(l => l.StartsWith("walk ", StringComparison.Ordinal)));
        Assert.AreEqual(new CellCoord(12, 10), w.At);
    }

    [TestMethod]
    public void NoPathPushesThePauseGoalForASecondInRealTimeAndEndsAtOnceInARound()
    {
        var w = new World { NoPath = true };
        var steps = Drive(Run(new(13, 10)), w);
        Assert.AreEqual(AiRunEnd.EndStep, steps[^1].Step.End);
        Assert.AreEqual(AiExecutorRules.NoPathPauseMs, steps[^1].At, "a second after the plan");
        Assert.AreEqual(0, w.Log.Count(l => l.StartsWith("walk ", StringComparison.Ordinal)));

        var round = new World { NoPath = true, InRound = true };
        var ended = Drive(Run(new(13, 10)), round);
        Assert.AreEqual(1, ended.Count, "in a round it ends in the planning step");
        Assert.AreEqual(AiRunEnd.EndStep, ended[0].Step.End);
    }

    [TestMethod]
    public void AHeroThatIsDownOrAlreadyOnTheDestinationWalksNowhere()
    {
        var down = new World { Down = true };
        Assert.AreEqual(AiRunEnd.EndStep, AiExecutorRules.StepHeroWalk(Run(new(13, 10)), down).End);
        Assert.AreEqual(0, down.Plans.Count);

        var there = new World();
        var step = AiExecutorRules.StepHeroWalk(Run(Start), there);
        Assert.AreEqual(AiRunEnd.Finished, step.End, "the exact arrival test pops the bottom goal");
        Assert.AreEqual(0, there.Plans.Count);
    }

    [TestMethod]
    public void AFarGoalIsHandedToTheRouteAndTheRunWaitsWhileItFollowsThenEnds()
    {
        var w = new World { Far = true };
        var first = AiExecutorRules.StepHeroWalk(Run(new(70, 10)), w);
        Assert.AreEqual(AiRunEnd.Running, first.End);
        Assert.IsTrue(first.Run!.Bottom.Flags.HasFlag(GoalFrameFlags.RouteFollow));
        Assert.AreEqual(AiExecutorRules.HeroRoutePollMs, first.DelayMs);
        CollectionAssert.AreEqual(new[] { "plan", "route" }, w.Log);
        var waiting = AiExecutorRules.StepHeroWalk(first.Run, w);
        Assert.AreEqual(AiRunEnd.Running, waiting.End, "the route follows: the goal waits");
        Assert.AreEqual(1, w.Plans.Count);
        w.Following = false;
        Assert.AreEqual(AiRunEnd.Finished, AiExecutorRules.StepHeroWalk(waiting.Run!, w).End, "the route arrived or stopped");

        // A retarget while the route follows plans the new goal again.
        var again = new World { Far = true };
        var farRun = AiExecutorRules.StepHeroWalk(Run(new(70, 10)), again).Run!;
        var retargeted = farRun.WithFrames([farRun.Frames[0] with { Tile = new(13, 10) }]) with { PathFlags = farRun.PathFlags | GoalPathFlags.Replan };
        again.Far = false;
        var replanned = Drive(retargeted, again);
        Assert.AreEqual(2, again.Plans.Count);
        Assert.AreEqual(new CellCoord(13, 10), again.At);
        Assert.AreEqual(AiRunEnd.Finished, replanned[^1].Step.End);
    }

    [TestMethod]
    public void AStraightRetryPlanSetsTheRunsFlagAndEveryLaterPlanAsksTheStraightLine()
    {
        var w = new World { Facing = 5, StraightPlan = true };
        Drive(Run(new(30, 10)), w);
        Assert.IsFalse(w.Plans[0].Straight);
        Assert.IsTrue(w.Plans.Skip(1).All(p => p.Straight), "flag 0x400: every later plan is the straight line");
    }

    [TestMethod]
    public void ARetargetThatDroppedAStepBeforeItBeganIsNoRefusalAndTheNewDestinationIsWalked()
    {
        var w = new World { Facing = 5 };
        var step = AiExecutorRules.StepHeroWalk(Run(new(30, 10)), w);
        var second = AiExecutorRules.StepHeroWalk(step.Run!, w); // the second step is given
        Assert.AreEqual(2, w.Log.Count(l => l.StartsWith("walk ", StringComparison.Ordinal)));
        // The click retargets and the route stop dropped the step that was queued: the body stays on the cell the first step entered.
        w.At = new(11, 10);
        var retargeted = second.Run!.WithFrames([second.Run.Frames[0] with { Tile = new(11, 16) }, second.Run.Frames[1]]) with { PathFlags = second.Run.PathFlags | GoalPathFlags.Replan };
        var walked = Drive(retargeted, w);
        Assert.AreEqual(AiRunEnd.Finished, walked[^1].Step.End, "the replan flag wins over the left-behind test");
        Assert.AreEqual(new CellCoord(11, 16), w.At);
        Assert.AreEqual(new CellCoord(11, 16), w.Plans[^1].Destination);
    }

    [TestMethod]
    public void ARunKeepsItsFramesAtMostEightGoalsDeepAndStaysInsideTheNodeLimit()
    {
        var w = new World { Facing = 0 };
        var run = Run(new(40, 10));
        for (var i = 0; i < 60; i++)
        {
            var step = AiExecutorRules.StepHeroWalk(run, w);
            Assert.IsTrue(step.Nodes < AiExecutorRules.MaxNodes);
            if (step.Ended) break;
            Assert.IsTrue(step.Run!.Frames.Length <= GoalRun.MaxFrames);
            run = step.Run;
        }
        Assert.AreEqual(new CellCoord(40, 10), w.At);
    }
}
