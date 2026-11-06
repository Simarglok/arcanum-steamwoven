using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Locks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>
/// ARC-DEV-138 (MIG13 S7, goal-table.md GT38–GT51): the nodes of the door goals 11, 12, 49, 50, 70, 71 and 72 over a fake door world with a portal ART (a door with three open frames 1..3,
/// 100 ms a step). Time is the sum of the steps' delays.
/// </summary>
[TestClass]
public sealed class DoorGoalsTests
{
    private const string Door = "oid:door", Hero = "oid:hero";
    private static readonly PortalArt Art = new(true, 2, 7);

    private sealed class World : IAiDoorWorld
    {
        public int Frame;
        public bool Valid = true, Refuses, Capable = true, Held, Stays, Round, Occupied, StartRefused, UseRefused;
        public readonly List<string> Log = [];
        public readonly List<GoalType> Started = [];
        public bool Busy { get; set; }
        public bool DoorValid(string door) => Valid;
        public bool DoorOpen(string door) => Frame != 0;
        public bool DoorBusy(string door) => Busy;
        public bool DoorRefusesOpen(string opener, string door) => Refuses;
        public bool DoorOpenerCapable(string opener) => Capable;
        public bool UseDoor(string opener, string door) { Log.Add("use"); return !UseRefused; }
        public bool DoorHeld(string door) => Held;
        public bool DoorStaysOpen(string door) => Stays;
        public bool DoorRound => Round;
        public bool DoorwayOccupied(string door) => Occupied;
        public bool StartDoorRun(string door, GoalType goal, string opener) { if (!StartRefused) Started.Add(goal); return !StartRefused; }
        public bool DoorStep(string door, bool opening)
        {
            var step = opening ? PortalFrameRules.OpenStep(Art, Frame) : PortalFrameRules.CloseStep(Art, Frame);
            if (step is not { } s) return false;
            Frame = s.Frame; Log.Add((opening ? "open " : "close ") + Frame); return true;
        }
        public int DoorStepMs(string door) => 100;
        public void PlayDoorSound(string door, DoorSound sound) => Log.Add("sound " + sound);
        public void SnapDoorClosed(string door) { Frame = 0; Log.Add("snap"); }
    }

    private static GoalRun Run(string owner, GoalFrame frame) => new(owner, default, frame);

    // Steps a run to its end; returns the steps with the time each ended at (the sum of the delays before it).
    private static List<(AiGoalStepResult Step, long At)> Drive(GoalRun run, IAiDoorWorld world, int limit = 200)
    {
        var steps = new List<(AiGoalStepResult, long)>(); long at = 0;
        for (var i = 0; i < limit; i++)
        {
            var step = AiExecutorRules.StepDoor(run, world);
            steps.Add((step, at));
            if (step.Ended) return steps;
            at += step.DelayMs; run = step.Run!;
        }
        throw new InvalidOperationException("the door run did not end");
    }

    [TestMethod]
    public void OpeningStepsToTheLastFrameThenPendsTheCloseAfterOneAndAHalfSeconds()
    {
        var world = new World();
        var run = Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorOpen, Hero));
        var first = AiExecutorRules.StepDoor(run, world);
        Assert.AreEqual(1, world.Frame, "the begin makes the first open step at once");
        CollectionAssert.AreEqual(new[] { "open 1", "sound Open" }, world.Log);
        Assert.AreEqual(100, first.DelayMs);
        var second = AiExecutorRules.StepDoor(first.Run!, world);
        Assert.AreEqual(2, world.Frame);
        var third = AiExecutorRules.StepDoor(second.Run!, world);
        Assert.AreEqual(3, world.Frame);
        // The step the portal refuses (the last frame is reached) turns the goal into 72 after 1.5 s.
        var last = AiExecutorRules.StepDoor(third.Run!, world);
        Assert.AreEqual(AiRunEnd.Running, last.End);
        Assert.AreEqual(AiExecutorRules.DoorCloseWaitMs, last.DelayMs);
        Assert.AreEqual(GoalType.PendClosingDoor, last.Run!.Top.Goal);
        Assert.AreEqual(3, world.Frame, "the door stays open while the close is pending");
    }

    [TestMethod]
    public void AStickyDoorStaysOpenAndItsRunEnds()
    {
        var world = new World { Stays = true };
        var steps = Drive(Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorOpen, Hero)), world);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End);
        Assert.AreEqual(3, world.Frame);
    }

    [TestMethod]
    public void ThePendingCloseRetriesEveryOneAndAHalfSecondsInARoundOrWithACritterInTheDoorway()
    {
        var world = new World { Frame = 3, Round = true };
        var run = Run(Door, AiExecutorRules.DoorRunFrame(GoalType.PendClosingDoor, Hero));
        var step = AiExecutorRules.StepDoor(run, world);
        Assert.AreEqual((AiRunEnd.Running, 1500, GoalType.PendClosingDoor), (step.End, step.DelayMs, step.Run!.Top.Goal));
        world.Round = false; world.Occupied = true;
        step = AiExecutorRules.StepDoor(step.Run, world);
        Assert.AreEqual((AiRunEnd.Running, 1500, GoalType.PendClosingDoor), (step.End, step.DelayMs, step.Run!.Top.Goal));
        world.Occupied = false;
        step = AiExecutorRules.StepDoor(step.Run, world);
        Assert.AreEqual((AiRunEnd.Running, 1500, GoalType.AnimateDoorClosed), (step.End, step.DelayMs, step.Run!.Top.Goal), "a clear doorway: the closing starts after one more wait");
        Assert.AreEqual(3, world.Frame, "and the door has not moved yet");
    }

    [TestMethod]
    public void ClosingStepsDownToFrameZeroPlaysTheSoundOnceAndEndsOneDelayAfterTheLastStep()
    {
        var world = new World { Frame = 3 };
        var steps = Drive(Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorClosed, Hero)), world);
        CollectionAssert.AreEqual(new[] { "close 2", "sound Close", "close 1", "close 0" }, world.Log);
        Assert.AreEqual(0, world.Frame);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End);
        Assert.AreEqual(400, steps[^1].At, "the begin and two steps close it by 200; the step that finds it closed waits one more delay before the pop");
    }

    [TestMethod]
    public void TheWholeOpenWaitCloseSequenceTakesThePortalsTimes()
    {
        var world = new World();
        var run = Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorOpen, Hero));
        var steps = Drive(run, world);
        var total = steps[^1].At;
        // open: 3 steps (0, 100, 200), the refused 4th at 300; 72 at 300 + 1500; 71's begin at 1800 + 1500; 3 close steps (3300, 3400, 3500), the refused one at 3600 and the pop a delay after.
        Assert.AreEqual(300 + 1500 + 1500 + 400, total);
        Assert.AreEqual(0, world.Frame);
    }

    [TestMethod]
    public void AnOpenGoalOnAClosedIdleDoorTurnsIntoTheAttemptAndStartsTheDoorsRun()
    {
        var world = new World();
        var step = AiExecutorRules.StepDoor(Run(Hero, AiExecutorRules.OpenDoorFrame(Door)), world);
        Assert.AreEqual(AiRunEnd.Finished, step.End, "the opener's goal pops at once, it does not wait for the animation");
        CollectionAssert.AreEqual(new[] { GoalType.AnimateDoorOpen }, world.Started);
        CollectionAssert.AreEqual(new[] { "use" }, world.Log);
    }

    [TestMethod]
    public void ARefusedOpenAttemptEndsTheOpenersWholeRunWithNoSound()
    {
        var world = new World { Refuses = true };
        var step = AiExecutorRules.StepDoor(Run(Hero, AiExecutorRules.OpenDoorFrame(Door)), world);
        Assert.AreEqual(AiRunEnd.EndStep, step.End);
        Assert.AreEqual(0, world.Started.Count);
        Assert.AreEqual(0, world.Log.Count, "no sound, no message, no use");
    }

    [TestMethod]
    public void AnOpenGoalOnAnOpenOrBusyDoorOrANonPortalPopsWithoutOpening()
    {
        foreach (var world in new[] { new World { Frame = 2 }, new World { Busy = true }, new World { Valid = false } })
        {
            var step = AiExecutorRules.StepDoor(Run(Hero, AiExecutorRules.OpenDoorFrame(Door)), world);
            Assert.AreEqual(AiRunEnd.Finished, step.End);
            Assert.AreEqual(0, world.Started.Count);
        }
    }

    [TestMethod]
    public void TheAttemptEndsWhenTheOpenerCannotHandleDoorsOrTheUseRefusesOrTheStartIsRefused()
    {
        foreach (var world in new[] { new World { Capable = false }, new World { UseRefused = true }, new World { StartRefused = true } })
        {
            var step = AiExecutorRules.StepDoor(Run(Hero, new GoalFrame(GoalType.AttemptOpenDoor, Door)), world);
            Assert.AreEqual(AiRunEnd.EndStep, step.End);
            Assert.AreEqual(0, world.Started.Count);
        }
    }

    [TestMethod]
    public void ACloseGoalOnAnOpenDoorStartsTheClosingAndAClosedIdleOneEnds()
    {
        var open = new World { Frame = 3 };
        var step = AiExecutorRules.StepDoor(Run(Hero, AiExecutorRules.CloseDoorFrame(Door)), open);
        Assert.AreEqual(AiRunEnd.Finished, step.End);
        CollectionAssert.AreEqual(new[] { GoalType.AnimateDoorClosed }, open.Started);
        Assert.AreEqual(0, open.Log.Count, "closing costs nothing extra: no use node, no script");
        var closed = new World();
        Assert.AreEqual(AiRunEnd.EndStep, AiExecutorRules.StepDoor(Run(Hero, AiExecutorRules.CloseDoorFrame(Door)), closed).End);
        Assert.AreEqual(0, closed.Started.Count);
    }

    [TestMethod]
    public void ACloseGoalOnADoorThatIsOpeningOrPendingInterruptsItAndAHeldDoorRefuses()
    {
        var busy = new World { Frame = 1, Busy = true };
        Assert.AreEqual(AiRunEnd.Finished, AiExecutorRules.StepDoor(Run(Hero, AiExecutorRules.CloseDoorFrame(Door)), busy).End);
        CollectionAssert.AreEqual(new[] { GoalType.AnimateDoorClosed }, busy.Started);
        var held = new World { Frame = 3, Held = true };
        Assert.AreEqual(AiRunEnd.EndStep, AiExecutorRules.StepDoor(Run(Hero, AiExecutorRules.CloseDoorFrame(Door)), held).End);
        Assert.AreEqual(0, held.Started.Count);
    }

    [TestMethod]
    public void AnInterruptedClosingSnapsAnOpenDoorShutAndAnInterruptedOpeningLeavesItWhereItStands()
    {
        var closing = new World { Frame = 2 };
        AiExecutorRules.EndDoorRun(Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorClosed, Hero)), closing);
        Assert.AreEqual(0, closing.Frame);
        CollectionAssert.AreEqual(new[] { "snap" }, closing.Log);
        var held = new World { Frame = 2, Held = true };
        AiExecutorRules.EndDoorRun(Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorClosed, Hero)), held);
        Assert.AreEqual(2, held.Frame);
        var opening = new World { Frame = 2 };
        AiExecutorRules.EndDoorRun(Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorOpen, Hero)), opening);
        Assert.AreEqual(2, opening.Frame);
        var pending = new World { Frame = 3 };
        AiExecutorRules.EndDoorRun(Run(Door, AiExecutorRules.DoorRunFrame(GoalType.PendClosingDoor, Hero)), pending);
        Assert.AreEqual(3, pending.Frame, "an interrupted pending close leaves the door open for good (GT47)");
    }

    [TestMethod]
    public void AnOpenGoalThatPopsOntoTheGoalThatPushedItHandsTheRunBackToItsOwner()
    {
        var world = new World();
        var walk = new GoalFrame(GoalType.AttemptMove, tile: new(3, 3));
        var run = Run(Hero, walk);
        run = run.WithFrames([walk, AiExecutorRules.OpenDoorFrame(Door)]);
        var step = AiExecutorRules.StepDoor(run, world);
        Assert.AreEqual(AiRunEnd.Running, step.End, "the walk is not killed by the door machine");
        Assert.AreEqual(GoalType.AttemptMove, step.Run!.Top.Goal);
        Assert.AreEqual(1, step.Run.Top.Node, "the walk resumes at node 1");
        CollectionAssert.AreEqual(new[] { GoalType.AnimateDoorOpen }, world.Started);
    }

    [TestMethod]
    public void AKilledClosingRunRunsNoEndNode()
    {
        var world = new World { Frame = 2 };
        var run = Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorClosed, Hero)) with { Flags = GoalRunFlags.Killed };
        AiExecutorRules.EndDoorRun(run, world);
        Assert.AreEqual(2, world.Frame);
    }

    [TestMethod]
    public void AnOpenStepThatTheArtCannotMakeEndsTheRunAtOnceWithNoSound()
    {
        // A one-frame ART cannot open: the begin is refused, the run pops with no sound and no pending close.
        var oneFrame = new OneFrameWorld();
        var steps = Drive(Run(Door, AiExecutorRules.DoorRunFrame(GoalType.AnimateDoorOpen, Hero)), oneFrame);
        Assert.AreEqual(AiRunEnd.Finished, steps[^1].Step.End);
        Assert.AreEqual(0, oneFrame.Log.Count);
    }

    private sealed class OneFrameWorld : IAiDoorWorld
    {
        public readonly List<string> Log = [];
        public bool DoorValid(string door) => true;
        public bool DoorOpen(string door) => false;
        public bool DoorBusy(string door) => false;
        public bool DoorRefusesOpen(string opener, string door) => false;
        public bool DoorHeld(string door) => false;
        public bool DoorStaysOpen(string door) => false;
        public bool DoorRound => false;
        public bool DoorwayOccupied(string door) => false;
        public bool StartDoorRun(string door, GoalType goal, string opener) => true;
        public bool DoorStep(string door, bool opening) => false;
        public int DoorStepMs(string door) => 100;
        public void PlayDoorSound(string door, DoorSound sound) => Log.Add("sound");
        public void SnapDoorClosed(string door) => Log.Add("snap");
    }
}
