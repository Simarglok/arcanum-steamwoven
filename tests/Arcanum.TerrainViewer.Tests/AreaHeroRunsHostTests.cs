using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-146 (MIG13 S10b, goal-table.md §3.5, GT14): the hero's walk as a run of the goal table (goal 3, or 4 for a runner; owner pc) over a session and a fake route. The walk starter's gate, the
/// retarget in place, the level-3 interrupt and its end node (which stops the route), and the step that ends the run when the route no longer follows its destination. No original data.
/// </summary>
[TestClass]
public sealed class AreaHeroRunsHostTests
{
    private static readonly CellCoord Goal = new(110, 100), Other = new(120, 90);

    private sealed class Feet : IAreaHeroRunsViewer
    {
        public bool May = true;
        public CellCoord? Following;
        public readonly List<(CellCoord Destination, string Why)> Stops = [];
        public bool MayStart() => May;
        public bool Walking(CellCoord destination) => Following == destination;
        public void StopWalking(CellCoord destination, string why) { if (Following == destination) { Following = null; Stops.Add((destination, why)); } }
        public int Facing = 5;
        public bool Holds = true;
        public readonly List<int> Shown = [];
        public readonly List<int> Snaps = [];
        public int HeroFacing => Facing;
        public bool TurnHolds => Holds;
        public void ShowFacing(int rotation) { Facing = rotation; Shown.Add(rotation); }
        public void SnapFacing(int rotation) { Facing = rotation; Snaps.Add(rotation); }
    }

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; } = ConversationSessionFixture.Create();
        public Feet Feet { get; } = new();
        public AreaHeroRunsHost Host { get; }
        public FrameStep Step { get; }

        public Fixture()
        {
            Host = new AreaHeroRunsHost(Session, Feet);
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null), null, null, null, null, null, () => Host) };
        }

        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
        public GoalRun Walk => Session.QueryGoals().RunsOf(AreaHeroRunsHost.Hero).Single();
        public void Dispose() => Session.Dispose();
    }

    [TestMethod]
    public void AWalkStartsAGoalThreeRunOfThePcAtPriorityTwoAndARunnerStartsGoalFour()
    {
        using var f = new Fixture();
        Assert.AreEqual(HeroWalkRunResult.Started, f.Host.Walk(Goal, run: false));
        Assert.AreEqual(GoalType.MoveToTile, f.Walk.Bottom.Goal);
        Assert.AreEqual(Goal, f.Walk.Bottom.Tile);
        Assert.AreEqual("pc", f.Walk.Owner);
        Assert.AreEqual(0, f.Host.HighestPriority, "the route does not walk yet: the run is not doing anything");
        f.Feet.Following = Goal;
        Assert.AreEqual(2, f.Host.HighestPriority);
        Assert.IsFalse(f.Walk.Flags.HasFlag(GoalRunFlags.Running));

        using var g = new Fixture();
        Assert.AreEqual(HeroWalkRunResult.Started, g.Host.Walk(Goal, run: true));
        Assert.AreEqual(GoalType.RunToTile, g.Walk.Bottom.Goal);
        Assert.IsTrue(g.Walk.Flags.HasFlag(GoalRunFlags.Running));
    }

    [TestMethod]
    public void ASecondClickRetargetsTheSameRunInPlaceAndTheSameTileChangesNothing()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        var handle = f.Walk.Handle;
        Assert.AreEqual(HeroWalkRunResult.Unchanged, f.Host.Walk(Goal, false));
        Assert.AreEqual(HeroWalkRunResult.Retargeted, f.Host.Walk(Other, false));
        Assert.AreEqual(handle, f.Walk.Handle, "the same run");
        Assert.AreEqual(Other, f.Walk.Bottom.Tile);
        Assert.IsTrue(f.Walk.PathFlags.HasFlag(GoalPathFlags.Replan));
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
        // A click with the other gait finds the same activity (goal 3 and 4 are aliases, GT3): retargeted too.
        Assert.AreEqual(HeroWalkRunResult.Retargeted, f.Host.Walk(Goal, true));
        Assert.AreEqual(handle, f.Walk.Handle);
    }

    [TestMethod]
    public void TheMovementGateRefusesTheWalkAndLeavesNoRun()
    {
        using var f = new Fixture();
        f.Feet.May = false;
        Assert.AreEqual(HeroWalkRunResult.Refused, f.Host.Walk(Goal, false));
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        f.Feet.May = true;
        f.Host.Walk(Goal, false);
        f.Feet.May = false;
        Assert.AreEqual(HeroWalkRunResult.Refused, f.Host.Walk(Other, false), "a running walk is not retargeted through the gate either");
        Assert.AreEqual(Goal, f.Walk.Bottom.Tile);
    }

    [TestMethod]
    public void TheRunStepsWhileTheRouteFollowsItsDestinationAndEndsWhenItStopsOrGoesElsewhere()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        f.Run(500);
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount, "the route walks: the run lives");
        Assert.IsTrue(f.Host.Steps > 5, "one step a frame");
        f.Feet.Following = null;
        f.Run(100);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the route arrived or stopped: the run ends");
        Assert.AreEqual(1, f.Host.RouteEnds);
        Assert.AreEqual(0, f.Feet.Stops.Count, "a run that ended itself asks nothing of the route");

        f.Host.Walk(Goal, false);
        f.Feet.Following = Other;
        f.Run(100);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the route follows another goal now (an approach): the run is not its walk");
        Assert.AreEqual(Other, f.Feet.Following, "and the route is left alone");
    }

    [TestMethod]
    public void TheLevelThreeInterruptEndsTheWalkAndItsEndNodeStopsTheRoute()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        Assert.IsTrue(f.Host.Interrupt(GoalInterruptRules.OrdinaryActions, "ROUTE_STOPPED_BY_RIGHT_RELEASE"));
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual((Goal, "ROUTE_STOPPED_BY_RIGHT_RELEASE"), f.Feet.Stops.Single());
        Assert.IsNull(f.Feet.Following);
        Assert.IsTrue(f.Host.Interrupt(GoalInterruptRules.OrdinaryActions, "again"), "nothing to interrupt: it succeeds");
    }

    [TestMethod]
    public void ASpellAttemptRefusesTheInterruptAndKeepsTheWalk()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        // A casting hero: a run of priority 4 on the table beside the walk, first in slot order (GS17: the first refusal stops the walk of the owner's runs).
        f.Session.KillGoals("pc");
        var cast = f.Session.StartGoal(new GoalStartRequest("pc", GoalStarters.GenericStartsAnyway, new GoalFrame(GoalType.AttemptSpell)), new OpenPorts());
        Assert.IsTrue(cast.Started);
        Assert.AreEqual(4, f.Host.HighestPriority);
        Assert.IsFalse(f.Host.Interrupt(GoalInterruptRules.OrdinaryActions, "right release"), "a run of priority 4 refuses level 3");
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(0, f.Feet.Stops.Count);
    }

    [TestMethod]
    public void ANewStartsOwnInterruptOfTheOldRunDoesNotStopTheRoute()
    {
        using var f = new Fixture();
        // A goal 53 turn of the hero is interrupted by the walk starter's level-3 interrupt (priority 2): no route stop, no leftover.
        f.Session.StartGoal(new GoalStartRequest("pc", GoalStarters.Generic, new GoalFrame(GoalType.Rotate)), new OpenPorts());
        f.Feet.Following = Goal;
        Assert.AreEqual(HeroWalkRunResult.Started, f.Host.Walk(Goal, false));
        Assert.AreEqual(1, f.Session.QueryGoals().InUseCount, "the turn ended, the walk runs");
        Assert.AreEqual(1, f.Feet.Snaps.Count, "the turn's end node snapped the facing");
        Assert.AreEqual(0, f.Feet.Stops.Count);
        Assert.AreEqual(Goal, f.Feet.Following);
    }

    [TestMethod]
    public void ATurnIsAGoalFiftyThreeRunOfThePcTheTableStepsItThirtyMillisecondsAnOctantAndItsEndNodeSnapsTheFacing()
    {
        using var f = new Fixture();
        f.Feet.Facing = 5;
        Assert.IsTrue(f.Host.Turn(3));
        Assert.AreEqual(GoalType.Rotate, f.Host.TurnRun!.Bottom.Goal);
        Assert.AreEqual(3, f.Host.TurnRun.Bottom.Rotation);
        Assert.AreEqual(2, f.Host.HighestPriority);
        f.Run(100);
        CollectionAssert.AreEqual(new[] { 4, 3 }, f.Feet.Shown, "two 45-degree steps the short way");
        Assert.AreEqual(2, f.Host.TurnSteps);
        f.Run(200);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "the step that finds the target reached ends the run");
        Assert.AreEqual(0, f.Feet.Snaps.Count, "a run that ended itself has nothing left to snap");

        f.Feet.Facing = 5;
        Assert.IsTrue(f.Host.Turn(2));
        f.Run(20);
        Assert.IsTrue(f.Host.Interrupt(GoalInterruptRules.OrdinaryActions, "right release"));
        CollectionAssert.AreEqual(new[] { 2 }, f.Feet.Snaps, "an interrupt runs the end node: the facing snaps to the target");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
    }

    [TestMethod]
    public void AHalfTurnTakesFourStepsAndTheRunEndsOneTickAfterTheLastAndALateFrameRunsEveryDueStep()
    {
        using var f = new Fixture();
        f.Feet.Facing = 1;
        Assert.IsTrue(f.Host.Turn(5));
        f.Run(120);
        Assert.AreEqual(4, f.Feet.Shown.Count, "180 degrees are four steps");
        Assert.AreEqual(5, f.Feet.Facing);
        f.Run(100);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);

        // One long frame: the table's steps that came due inside it all run.
        f.Feet.Facing = 0; f.Feet.Shown.Clear();
        Assert.IsTrue(f.Host.Turn(3));
        f.Step.AdvanceHost(400);
        Assert.AreEqual(3, f.Feet.Shown.Count, "a late frame runs every due step");
        Assert.AreEqual(3, f.Feet.Facing);
    }

    [TestMethod]
    public void ATurnTheHeroNoLongerHoldsEndsWithoutStepsAndAFacingAlreadyAtTheTargetEndsAtOnce()
    {
        using var f = new Fixture();
        f.Feet.Facing = 5;
        Assert.IsTrue(f.Host.Turn(2));
        f.Feet.Holds = false;
        f.Run(100);
        Assert.AreEqual(0, f.Feet.Shown.Count, "the hero walked or fell: no step");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual(1, f.Host.RouteEnds);

        f.Feet.Holds = true; f.Feet.Facing = 4;
        Assert.IsTrue(f.Host.Turn(4));
        f.Run(60);
        Assert.AreEqual(0, f.Feet.Shown.Count);
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount, "nothing to turn: the step ends the run");
    }

    [TestMethod]
    public void ATurnStartedOverAWalkEndsTheWalkAndStopsTheRoute()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        Assert.IsTrue(f.Host.Turn(2));
        Assert.IsNull(f.Host.WalkRun, "the level-2 start ended the walk");
        Assert.AreEqual("ROUTE_STOPPED_BY_TURN", f.Feet.Stops.Single().Why);
    }

    [TestMethod]
    public void AKilledOrResetWalkRunStillStopsTheRoute()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        f.Host.EndNodes(new GoalRunEnd(f.Walk, GoalEndKind.Killed, [GoalType.MoveToTile], 0), "KILLED");
        Assert.AreEqual("KILLED", f.Feet.Stops.Single().Why);
    }

    [TestMethod]
    public void TheEndNodeOfAWalkEndedFromOutsideStopsTheRouteAndAFinishedRunAsksNothing()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        var run = f.Walk;
        f.Host.EndNodes(new GoalRunEnd(run, GoalEndKind.Finished, [], 0));
        Assert.AreEqual(Goal, f.Feet.Following, "a finished run: nothing left to stop");
        f.Host.EndNodes(new GoalRunEnd(run, GoalEndKind.Interrupted, [GoalType.MoveToTile], 0), "ROUND_START");
        Assert.AreEqual((Goal, "ROUND_START"), f.Feet.Stops.Single());
    }

    [TestMethod]
    public void LosingTheHostEndsTheRunAtLevelSixAndStopsTheRoute()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        f.Feet.Following = Goal;
        f.Host.LoseAll("the session changed");
        Assert.AreEqual(0, f.Session.QueryGoals().InUseCount);
        Assert.AreEqual("the session changed", f.Feet.Stops.Single().Why);
    }

    [TestMethod]
    public void ARunWithNoHeroHostEndsInTheRouterAndOnlyTheHeroesWalkIsTheHeroHosts()
    {
        using var f = new Fixture();
        f.Host.Walk(Goal, false);
        var router = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null));
        Assert.AreEqual(SessionGoalStepAnswer.End, router.StepGoal(f.Walk, default));
        Assert.AreEqual(1, router.HeroRunsLost);
        Assert.IsTrue(AreaHeroRunsHost.IsHeroWalk(f.Walk));
        Assert.IsFalse(AreaHeroRunsHost.IsHeroWalk(new GoalRun("npc:a", default, new GoalFrame(GoalType.MoveToTile, tile: Goal))), "an NPC's walk is the AI host's");
        Assert.IsFalse(AreaHeroRunsHost.IsHeroWalk(new GoalRun("pc", default, new GoalFrame(GoalType.Attack, "npc:a"))));
    }

    private sealed class OpenPorts : IGoalStartPorts
    {
        public bool GatePasses(GoalStartRequest request) => true;
        public bool Loading => false;
        public bool IsDestroyed(string owner) => false;
    }
}
