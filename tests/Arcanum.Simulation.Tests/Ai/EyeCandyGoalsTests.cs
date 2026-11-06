using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Ai;

/// <summary>ARC-DEV-143 (MIG13 S9b, goal-table.md GT83–GT93): the nodes of the eye-candy goals 45–48, 58, 59, 75, 76 and the hover 44 over a fake world. Time is the sum of the steps' delays.</summary>
[TestClass]
public sealed class EyeCandyGoalsTests
{
    private const string Hero = "player";

    private sealed class World : IAiOverlayWorld
    {
        public EyeCandyFacts? Facts = new(4, 2, 100, 0, true, true);
        public bool Place = true;
        public int RandomAnswer = 1;
        public readonly List<string> Log = [];
        public int Burns, Shift;

        public EyeCandyFacts? EyeCandyFactsOf(string owner, GoalFrame goal) => Facts;
        public bool EyeCandyPlace(string owner, GoalFrame goal) { Log.Add("place"); return Place; }
        public int EyeCandyRandom(int low, int high) { Log.Add($"random{low}-{high}"); return RandomAnswer; }
        public bool EyeCandyFireDamage(string owner, GoalFrame goal) { Burns++; Log.Add($"burn@{goal.EyeCandy?.Frame}"); return true; }
        public void EyeCandyCallback(string owner, int tag, int phase) => Log.Add($"callback{phase}:{tag}");
        public void HoverShift(string owner, int deltaY) { Shift += deltaY; Log.Add($"hover{Shift}"); }
    }

    // Steps the run until it ends or `until` ms pass; returns the (time, frame) of each step that left the run running, and the end.
    private static List<(long At, int Frame)> Drive(ref GoalRun run, World world, long until, out AiGoalStepResult? ended)
    {
        var seen = new List<(long, int)>(); long at = 0; ended = null;
        while (at <= until)
        {
            var step = AiExecutorRules.StepOverlay(run, world);
            if (step.Ended) { ended = step; return seen; }
            run = step.Run!;
            seen.Add((at, run.Top.EyeCandy?.Frame ?? -1));
            at += step.DelayMs;
        }
        return seen;
    }

    private static GoalRun Run(uint play = 0, bool looping = false, int tag = 7) => new(Hero, default, AiExecutorRules.EyeCandyFrame(play, 0x1234, 12, tag, looping));

    [TestMethod]
    public void TheGoalsAreNoInterruptOverlaysOfPriorityFive()
    {
        foreach (var goal in new[] { GoalType.EyeCandy, GoalType.EyeCandyReverse, GoalType.EyeCandyCallback, GoalType.EyeCandyReverseCallback, GoalType.EyeCandyEndCallback,
            GoalType.EyeCandyReverseEndCallback, GoalType.EyeCandyFireDamage, GoalType.EyeCandyReverseFireDamage, GoalType.Floating })
        {
            Assert.IsTrue(AiExecutorRules.IsOverlayGoal(goal), goal.ToString());
            Assert.IsTrue(AiExecutorRules.IsObjectRunGoal(goal), "the NPC AI host leaves an overlay alone");
            var descriptor = GoalDescriptors.Of(goal)!;
            Assert.AreEqual(5, descriptor.Priority);
            Assert.IsTrue(descriptor.NoInterrupt);
            Assert.IsTrue(descriptor.Carried);
        }
        Assert.IsFalse(AiExecutorRules.IsOverlayGoal(GoalType.AnimateLoop));
        Assert.IsFalse(AiExecutorRules.IsEyeCandyGoal(GoalType.Floating));
    }

    [TestMethod]
    public void ThePlayFlagsChooseTheGoalAndTheCallbackFlagsBeatFireDamage()
    {
        Assert.AreEqual(GoalType.EyeCandy, AiExecutorRules.EyeCandyGoalOf(0));
        Assert.AreEqual(GoalType.EyeCandy, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayStack | AiExecutorRules.PlayDestroy | AiExecutorRules.PlayNoId));
        Assert.AreEqual(GoalType.EyeCandyReverse, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayReverse));
        Assert.AreEqual(GoalType.EyeCandyFireDamage, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayFireDamage));
        Assert.AreEqual(GoalType.EyeCandyReverseFireDamage, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayFireDamage | AiExecutorRules.PlayReverse));
        Assert.AreEqual(GoalType.EyeCandyCallback, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayCallback));
        Assert.AreEqual(GoalType.EyeCandyReverseCallback, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayCallback | AiExecutorRules.PlayReverse));
        Assert.AreEqual(GoalType.EyeCandyEndCallback, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayEndCallback));
        Assert.AreEqual(GoalType.EyeCandyReverseEndCallback, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayEndCallback | AiExecutorRules.PlayReverse));
        Assert.AreEqual(GoalType.EyeCandyCallback, AiExecutorRules.EyeCandyGoalOf(AiExecutorRules.PlayCallback | AiExecutorRules.PlayEndCallback | AiExecutorRules.PlayFireDamage), "0x8 beats 0x10 and 0x40");
        // Ice (0x240) is fire damage (0x40) with the normal-damage frame flag; ice alone (0x200) is a plain eye candy.
        var ice = AiExecutorRules.EyeCandyFrame(AiExecutorRules.PlayIceDamage, 1, 2, 3, false);
        Assert.AreEqual(GoalType.EyeCandyFireDamage, ice.Goal);
        Assert.IsTrue((ice.Flags & GoalFrameFlags.NormalDamage) != 0);
        Assert.AreEqual(GoalType.EyeCandy, AiExecutorRules.EyeCandyGoalOf(0x200));
        Assert.AreEqual(0, (int)(AiExecutorRules.EyeCandyFrame(0x200, 1, 2, 3, false).Flags & GoalFrameFlags.NormalDamage));
    }

    [TestMethod]
    public void AnEyeCandyShowsFrameKAtKPacesAndEndsAtFramesPaces()
    {
        var world = new World();
        var run = Run();
        var seen = Drive(ref run, world, 10_000, out var ended);
        // Begin at 0 (frame 0), then one frame a pace: 1 at 100, 2 at 200, 3 at 300; the step at 400 finds the last frame and the goal pops (frames x pace).
        CollectionAssert.AreEqual(new[] { 0L, 100, 200, 300 }, seen.Select(s => s.At).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, seen.Select(s => s.Frame).ToArray());
        Assert.AreEqual(AiRunEnd.Finished, ended!.End);
        Assert.AreEqual(4, seen.Count, "begin plus three frames");
        Assert.AreEqual(0, world.Burns);
        Assert.AreEqual("place", world.Log[0], "the first begin finds the overlay places");
    }

    [TestMethod]
    public void ALoopingEyeCandyBeginsAgainAndNeverEnds()
    {
        var world = new World();
        var run = Run(looping: true);
        var seen = Drive(ref run, world, 1_250, out var ended);
        Assert.IsNull(ended);
        // Begin, frames 1..3, the finish begins again at frame 0 (a 100 ms pace each), and so on.
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3, 0 }, seen.Select(s => s.Frame).ToArray());
        Assert.AreEqual(1, world.Log.Count(l => l == "place"), "the overlay places are found once (GT68: the 0x2000 flag stays)");
        Assert.AreNotEqual(0, (int)(run.Top.Flags & GoalFrameFlags.NotCounted), "a looping eye candy is also not counted");
    }

    [TestMethod]
    public void TheRandomStartIsAppliedOnlyWhenTheOverlayShowsAFrameOtherThanZero()
    {
        var world = new World { RandomAnswer = 2 };
        var run = Run(AiExecutorRules.PlayRandomStart, looping: true);
        var seen = Drive(ref run, world, 550, out _);
        // The first begin keeps frame 0 (0x4317AA); the looping second begin finds the last frame and takes the random one.
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 2, 3 }, seen.Select(s => s.Frame).ToArray());
        CollectionAssert.AreEqual(new[] { "random0-3" }, world.Log.Where(l => l.StartsWith("random", StringComparison.Ordinal)).ToArray());
    }

    [TestMethod]
    public void AReverseEyeCandyPlaysFromTheLastFrameToFrameZero()
    {
        var world = new World();
        var run = Run(AiExecutorRules.PlayReverse);
        var seen = Drive(ref run, world, 10_000, out var ended);
        CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, seen.Select(s => s.Frame).ToArray());
        Assert.AreEqual(AiRunEnd.Finished, ended!.End, "a step from frame 0 is false: the finish pops");
        // A random reverse start is 1..last.
        var random = new World { RandomAnswer = 2 };
        var again = Run(AiExecutorRules.PlayReverse | AiExecutorRules.PlayRandomStart);
        Assert.AreEqual(2, Drive(ref again, random, 0, out _)[0].Frame);
        CollectionAssert.AreEqual(new[] { "place", "random1-3" }, random.Log.ToArray());
    }

    [TestMethod]
    public void ABackgroundAtItsLastFrameEndsTheGoalAfterTheTimingPartMoved()
    {
        var world = new World { Facts = new(6, 0, 100, 3, true, false) };
        var run = Run();
        var seen = Drive(ref run, world, 10_000, out var ended);
        Assert.AreEqual(AiRunEnd.Finished, ended!.End);
        // Timing part frames 0,1,2: the background (3 frames) is at its last frame on the third step, which ends the goal with the timing part already on frame 3.
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, seen.Select(s => s.Frame).ToArray());
    }

    [TestMethod]
    public void ACallbackEyeCandyCallsBackOnceAtItsActionFrameEvenWhenLooping()
    {
        var world = new World();
        var run = Run(AiExecutorRules.PlayCallback, looping: true, tag: 9);
        Drive(ref run, world, 1_250, out var ended);
        Assert.IsNull(ended);
        CollectionAssert.AreEqual(new[] { "callback3:9" }, world.Log.Where(l => l.StartsWith("callback", StringComparison.Ordinal)).ToArray(), "the gate fires once (0x4 and 0x8 are never cleared)");
        Assert.IsTrue((run.Top.Flags & GoalFrameFlags.ActionHandled) != 0);
    }

    [TestMethod]
    public void AnActionFrameOfZeroOrPastTheLastFrameNeverCallsBack()
    {
        foreach (var action in new[] { 0, 4, 9 })
        {
            var world = new World { Facts = new(4, action, 100, 0, true, false) };
            var run = Run(AiExecutorRules.PlayCallback);
            Drive(ref run, world, 10_000, out var ended);
            Assert.AreEqual(AiRunEnd.Finished, ended!.End);
            Assert.AreEqual(0, world.Log.Count(l => l.StartsWith("callback", StringComparison.Ordinal)), $"action frame {action}");
        }
        // A part that does not time the release (a background only) never sets the flag either.
        var untimed = new World { Facts = new(4, 2, 100, 0, false, false) };
        var run2 = Run(AiExecutorRules.PlayCallback);
        Drive(ref run2, untimed, 10_000, out _);
        Assert.AreEqual(0, untimed.Log.Count(l => l.StartsWith("callback", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AnEndCallbackEyeCandyCallsBackOnceAtItsEndAndPopsWithoutLooping()
    {
        var world = new World();
        var run = Run(AiExecutorRules.PlayEndCallback, looping: true, tag: 4);
        var seen = Drive(ref run, world, 10_000, out var ended);
        Assert.AreEqual(AiRunEnd.Finished, ended!.End, "58 never loops: a looping flag does not keep it");
        CollectionAssert.AreEqual(new[] { "callback4:4" }, world.Log.Where(l => l.StartsWith("callback", StringComparison.Ordinal)).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, seen.Select(s => s.Frame).ToArray());
    }

    [TestMethod]
    public void ACallbackWithNoTagIsPassedOnForTheWorldToIgnore()
    {
        var world = new World();
        var run = Run(AiExecutorRules.PlayCallback, tag: -1);
        Drive(ref run, world, 10_000, out _);
        CollectionAssert.AreEqual(new[] { "callback3:-1" }, world.Log.Where(l => l.StartsWith("callback", StringComparison.Ordinal)).ToArray());
    }

    [TestMethod]
    public void AFireEyeCandyBurnsAtItsBeginAndBeforeEveryStepOnAFrameThatIsOneModThree()
    {
        var world = new World { Facts = new(6, 0, 100, 0, true, true) };
        var run = Run(AiExecutorRules.PlayFireDamage);
        Drive(ref run, world, 10_000, out var ended);
        Assert.AreEqual(AiRunEnd.Finished, ended!.End);
        // The begin burns; the step from frame 1 and the step from frame 4 burn (the frame before the advance); the last-frame step from 5 does not (5 mod 3 = 2).
        CollectionAssert.AreEqual(new[] { "burn@0", "burn@1", "burn@4" }, world.Log.Where(l => l.StartsWith("burn", StringComparison.Ordinal)).ToArray());
        // An underlay-timed part never burns on the steps.
        var underlay = new World { Facts = new(6, 0, 100, 0, true, false) };
        var run2 = Run(AiExecutorRules.PlayFireDamage);
        Drive(ref run2, underlay, 10_000, out _);
        CollectionAssert.AreEqual(new[] { "burn@0" }, underlay.Log.Where(l => l.StartsWith("burn", StringComparison.Ordinal)).ToArray());
    }

    [TestMethod]
    public void ARefusedPlaceOrAnUnreadArtEndsTheRunAtTheBegin()
    {
        var full = new World { Place = false };
        var run = Run();
        Assert.AreEqual(0, Drive(ref run, full, 1_000, out var ended).Count);
        Assert.AreEqual(AiRunEnd.EndStep, ended!.End);
        var unread = new World { Facts = null };
        var run2 = Run();
        Drive(ref run2, unread, 1_000, out ended);
        Assert.AreEqual(AiRunEnd.EndStep, ended!.End);
    }

    [TestMethod]
    public void AnEyeCandyQueuedUnderAnotherPlaysAfterItWithItsOwnBegin()
    {
        var world = new World();
        var queued = AiExecutorRules.EyeCandyFrame(AiExecutorRules.PlayReverse, 0x99, 13, 7, false);
        var first = AiExecutorRules.EyeCandyFrame(0, 0x1234, 12, 7, false);
        var run = new GoalRun(Hero, default, queued).WithFrames([queued, first]);
        var seen = Drive(ref run, world, 10_000, out var ended);
        Assert.AreEqual(AiRunEnd.Finished, ended!.End);
        // Frames 0..3 of the first, then the queued reverse one from its last frame down to 0.
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 3, 2, 1, 0 }, seen.Select(s => s.Frame).ToArray());
        Assert.AreEqual(2, world.Log.Count(l => l == "place"), "each goal finds its own overlay places");
    }

    [TestMethod]
    public void TheHoverRaisesFifteenPixelsThenBobsTenPixelsInTenSteps()
    {
        var world = new World();
        var run = new GoalRun(Hero, default, AiExecutorRules.HoverFrame());
        var begin = AiExecutorRules.StepOverlay(run, world);
        Assert.AreEqual(-15, world.Shift, "the begin raises the object by 15");
        Assert.AreEqual(0, begin.DelayMs, "the first bob step follows at once");
        run = begin.Run!;
        var offsets = new List<int>();
        var delays = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            var step = AiExecutorRules.StepOverlay(run, world);
            Assert.IsFalse(step.Ended);
            offsets.Add(world.Shift); delays.Add(step.DelayMs); run = step.Run!;
        }
        CollectionAssert.AreEqual(new[] { -13, -11, -9, -7, -5, -7, -9, -11, -13, -15, -13, -11 }, offsets);
        Assert.IsTrue(delays.All(d => d == 100));
        for (var i = 0; i <= 12; i++) Assert.AreEqual(i == 0 ? -15 : offsets[i - 1], AiExecutorRules.HoverOffsetAfter(i), $"step {i}");
        Assert.IsTrue((run.Top.Flags & GoalFrameFlags.Raised) != 0);
    }

    [TestMethod]
    public void AnOverlayStepOfANonOverlayGoalOrAnEyeCandyWithNoDataKillsTheRun()
    {
        var world = new World();
        var bad = new GoalRun(Hero, default, new GoalFrame(GoalType.EyeCandy));
        Assert.AreEqual(AiRunEnd.Killed, AiExecutorRules.StepOverlay(bad, world).End);
    }
}
