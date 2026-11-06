using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Locks;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>
/// ARC-DEV-138 (MIG13 S7, goal-table.md §3.5, GT38–GT51): the door host over a session and invented portals. A door's change is its own goal run stepped by the frame step on the animation clock:
/// the opening writes its frames one by one, the close comes 1.5 s after the last open frame unless the portal flag keeps it open, a critter on the door's tile or a round holds it,
/// and the pause stops the clock the run waits on. No original data.
/// </summary>
[TestClass]
public sealed class AreaDoorHostTests
{
    private const string DoorA = "oid:door-a", DoorB = "oid:door-b";

    private sealed class Fixture : IDisposable
    {
        public GameSessionOwner Session { get; }
        public AreaPortals Portals { get; }
        public AreaDoorHost Host { get; }
        public FrameStep Step { get; }
        public List<AreaStepCell> Critters { get; } = [];
        public List<(string Door, DoorSound Sound)> Sounds { get; } = [];

        public Fixture(int flagsA, int flagsB)
        {
            Session = ConversationSessionFixture.Create();
            Portals = AreaPortals.Of(new(1), [Portal(DoorA, 10, flagsA), Portal(DoorB, 20, flagsB)]);
            Host = new AreaDoorHost(Session, Portals, () => Critters, (portal, sound) => Sounds.Add((portal.Entity, sound)));
            Step = new FrameStep(Session, AreaAiTestSteps.NoAi) { GoalSteps = new AreaGoalStepRouter(() => Host, new AreaAiGoalSteps(() => null)) };
        }

        public AreaPortal A => Portals.All.Single(p => p.Entity == DoorA);
        public AreaPortal B => Portals.All.Single(p => p.Entity == DoorB);
        public int FrameOf(AreaPortal portal) => Portals.Frame(Session, portal);
        public void Dispose() => Session.Dispose();

        /// <summary>Advances the host clock in 20 ms frames.</summary>
        public void Run(long ms) { for (long t = 0; t < ms; t += 20) Step.AdvanceHost(20); }
    }

    private static AreaPortal Portal(string entity, long x, int flags)
    {
        var key = new AreaObjectKey(new string('a', 64), 1, AreaSourceKind.Static, null, 0, x, new string('b', 64), 1);
        return new(key, entity, new(x, 5), new(OpenableKind.Portal, flags, 0, 0), new(true, 2, 7), 10, false, []);
    }

    private static Fixture Open(int flagsA = 0, int flagsB = 0) => new(flagsA, flagsB);

    [TestMethod]
    public void AnOpenedDoorWritesItsFramesOneByOneThenClosesByItselfAfterOneAndAHalfSeconds()
    {
        using var f = Open();
        Assert.IsTrue(f.Host.Open(f.A, "hero"));
        Assert.AreEqual(0, f.FrameOf(f.A), "the first step comes 5 animation milliseconds later, not at the start");
        var seen = new List<int>();
        for (var i = 0; i < 200; i++)
        {
            f.Step.AdvanceHost(20);
            if (seen.Count == 0 || seen[^1] != f.FrameOf(f.A)) seen.Add(f.FrameOf(f.A));
        }
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 2, 1, 0 }, seen, "open to the last frame, wait, close frame by frame: " + string.Join(",", seen));
        CollectionAssert.AreEqual(new[] { (DoorA, DoorSound.Open), (DoorA, DoorSound.Close) }, f.Sounds);
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(DoorA).Length, "the run ended");
    }

    [TestMethod]
    public void ADoorWhosePortalFlagKeepsItOpenStaysOpen()
    {
        using var f = Open(flagsA: LockRules.StaysOpenBit);
        Assert.IsTrue(f.Host.Open(f.A, "hero"));
        f.Run(6000);
        Assert.AreEqual(3, f.FrameOf(f.A));
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(DoorA).Length);
    }

    [TestMethod]
    public void ACritterOnTheDoorsTileKeepsItOpenUntilItLeaves()
    {
        using var f = Open();
        f.Host.Open(f.A, "hero");
        f.Critters.Add(f.A.Cell);
        f.Run(6000);
        Assert.AreEqual(3, f.FrameOf(f.A), "the pending close retries while a living critter stands on the door's tile");
        Assert.AreEqual(GoalType.PendClosingDoor, f.Host.GoalOf(f.A));
        f.Critters.Clear();
        f.Run(4000);
        Assert.AreEqual(0, f.FrameOf(f.A));
        Assert.IsNull(f.Host.GoalOf(f.A));
    }

    [TestMethod]
    public void ClosingAnOpenDoorByUseInterruptsThePendingCloseAndClosesAtOnce()
    {
        using var f = Open();
        f.Host.Open(f.A, "hero");
        f.Run(800);
        Assert.AreEqual(GoalType.PendClosingDoor, f.Host.GoalOf(f.A));
        Assert.IsTrue(f.Host.Close(f.A, "hero"));
        Assert.AreEqual(GoalType.AnimateDoorClosed, f.Host.GoalOf(f.A));
        f.Run(1000);
        Assert.AreEqual(0, f.FrameOf(f.A));
        Assert.AreEqual(0, f.Session.QueryGoals().RunsOf(DoorA).Length, "no run is left");
    }

    [TestMethod]
    public void ACutShortClosingSnapsTheDoorShutAndACutShortOpeningLeavesItWhereItStands()
    {
        using var f = Open();
        f.Host.Open(f.A, "hero");
        f.Run(100);
        var mid = f.FrameOf(f.A);
        Assert.IsTrue(mid is > 0 and < 3, "mid-opening: " + mid);
        // A new open start interrupts the opening: no end node moves the door.
        f.Host.Open(f.A, "hero");
        Assert.AreEqual(mid, f.FrameOf(f.A));
        f.Run(500);
        f.Host.Close(f.A, "hero");
        f.Run(200);
        var closing = f.FrameOf(f.A);
        Assert.IsTrue(closing is > 0 and < 3, "mid-closing: " + closing);
        // An interrupt of the closing (a new close start) snaps it closed by the end node (0x42B6F0).
        f.Host.Close(f.A, "hero");
        Assert.AreEqual(0, f.FrameOf(f.A));
    }

    [TestMethod]
    public void TwoDoorsRunTheirOwnRunsAtOnce()
    {
        using var f = Open();
        f.Host.Open(f.A, "hero"); f.Host.Open(f.B, "hero");
        f.Run(500);
        Assert.AreEqual(3, f.FrameOf(f.A)); Assert.AreEqual(3, f.FrameOf(f.B));
        f.Run(5000);
        Assert.AreEqual(0, f.FrameOf(f.A)); Assert.AreEqual(0, f.FrameOf(f.B));
    }

    [TestMethod]
    public void TheMapClosesItsDoorRunsAndALostDoorHostEndsThem()
    {
        using var f = Open();
        f.Host.Open(f.A, "hero");
        f.Run(100);
        var router = new AreaGoalStepRouter(() => null, new AreaAiGoalSteps(() => null));
        var run = f.Session.QueryGoals().RunsOf(DoorA).Single();
        Assert.AreEqual(Arcanum.Session.SessionGoalStepAnswer.End, router.StepGoal(run, default));
        Assert.AreEqual(1, router.DoorsLost);
    }

    [TestMethod]
    public void ADoorHeldMagicallyCannotBeClosedAndTheDecisionSaysSo()
    {
        using var f = Open(flagsA: LockRules.MagicallyHeldBit);
        f.Host.Open(f.A, "hero");
        f.Run(500);
        Assert.AreEqual(AreaPortalUse.MagicallyHeld, f.Portals.Decide(f.Session, f.A));
        Assert.IsTrue(f.Host.DoorHeld(DoorA));
    }

    [TestMethod]
    public void TheDecisionOpensAClosedUnlockedDoorAndClosesAnOpenOne()
    {
        using var f = Open();
        Assert.AreEqual(AreaPortalUse.Opened, f.Portals.Decide(f.Session, f.B));
        Assert.AreEqual(0, f.FrameOf(f.B), "a decision writes nothing");
        f.Host.Open(f.B, "hero");
        f.Run(500);
        Assert.AreEqual(AreaPortalUse.Closed, f.Portals.Decide(f.Session, f.B));
    }
}
