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

    // ARC-WORLD-1469 (TT7, 0x428A10): the use node of goal 12 for an NPC or a companion opener meets the door's trap with the opener as the triggerer; a trap that stops the use ends the opener's run.
    [TestMethod]
    public void TheUseNodeOfAnNpcOpenerAsksTheDoorsTrapAndATrapThatStopsTheUseEndsTheRun()
    {
        using var f = Open();
        var asked = new List<(string Opener, string Door)>();
        var stops = false;
        f.Host.OpenerScript = (opener, portal) => { asked.Add((opener, portal.Entity)); return stops; };
        Assert.IsTrue(f.Host.UseDoor("oid:thug", DoorA), "no trap stops the use");
        stops = true;
        Assert.IsFalse(f.Host.UseDoor("oid:thug", DoorA), "the trap stops it: the opener's run ends");
        CollectionAssert.AreEqual(new[] { ("oid:thug", DoorA), ("oid:thug", DoorA) }, asked);
        Assert.IsTrue(f.Host.UseDoor("oid:thug", "oid:not-a-door"), "a door of another window is not this host's");
        f.Host.OpenerScript = null;
        Assert.IsTrue(f.Host.UseDoor("oid:thug", DoorA), "no trap host: the use goes on as before");
        Assert.AreEqual(2, asked.Count, "the hero's use is its own port, not the opener script");
        Assert.IsTrue(f.Host.UseDoor(AreaHeroRunsHost.Hero, DoorA));
        Assert.AreEqual(2, asked.Count);
    }

    // ARC-WORLD-1581 (TT18, TT7, 0x428A10): the hero's use node charges first, then the door's trap meets the hero; a trap that stops the use ends the run with the door shut, and a refused charge never meets the trap.
    [TestMethod]
    public void TheUseNodeOfTheHeroChargesThenAsksTheDoorsTrapAndATrapThatStopsTheUseEndsTheRun()
    {
        using var f = Open();
        var log = new List<string>();
        var charged = true;
        var stops = false;
        f.Host.HeroUse = portal => { log.Add("charge " + portal.Entity); return charged; };
        f.Host.HeroScript = portal => { log.Add("trap " + portal.Entity); return stops; };
        Assert.IsTrue(f.Host.UseDoor(AreaHeroRunsHost.Hero, DoorA), "no trap stops the use");
        stops = true;
        Assert.IsFalse(f.Host.UseDoor(AreaHeroRunsHost.Hero, DoorA), "the trap stops it: the hero's run ends");
        charged = false;
        Assert.IsFalse(f.Host.UseDoor(AreaHeroRunsHost.Hero, DoorA), "a refused charge ends the run before any trap");
        CollectionAssert.AreEqual(new[] { "charge " + DoorA, "trap " + DoorA, "charge " + DoorA, "trap " + DoorA, "charge " + DoorA }, log);
        Assert.IsTrue(f.Host.UseDoor(AreaHeroRunsHost.Hero, "oid:not-a-door"), "a door of another window is not this host's");
        f.Host.HeroUse = null; f.Host.HeroScript = null;
        Assert.IsTrue(f.Host.UseDoor(AreaHeroRunsHost.Hero, DoorA), "no hero ports: the use goes on as before");
        var asked = new List<string>();
        f.Host.OpenerScript = (opener, _) => { asked.Add(opener); return false; };
        f.Host.HeroScript = _ => true;
        Assert.IsTrue(f.Host.UseDoor("oid:thug", DoorA), "an NPC opener does not meet the hero's port");
        CollectionAssert.AreEqual(new[] { "oid:thug" }, asked);
    }

    // ARC-SCRIPT-1778 (ledger AN20): a portal a script gave another art is read with that art's facts by the door host (found by entity at each call), the open frame is the new art's, and the map's own entry returns.
    [TestMethod]
    public void ARetargetedDoorOpensToTheNewArtsLastFrameAndTheMapsOwnArtReturns()
    {
        using var f = Open(flagsA: LockRules.StaysOpenBit);
        Assert.AreEqual(3, f.A.OpenFrame);
        Assert.IsTrue(f.Portals.Retarget(f.A.Key, 0x3000_0400u, new(true, 0, 7), 10, []));
        Assert.IsFalse(f.Portals.Retarget(f.A.Key, 0x3000_0400u, new(true, 0, 7), 10, []), "the same art again changes nothing");
        Assert.AreEqual(0x3000_0400u, f.Host.PortalOf(DoorA)!.ArtId, "the door host reads the retargeted portal");
        Assert.AreEqual(6, f.Portals.ByEntity(DoorA)!.OpenFrame, "rotation 0 steps through frames 4..6");
        Assert.IsTrue(f.Host.Open(f.Portals.ByEntity(DoorA)!, "hero"));
        f.Run(6000);
        Assert.AreEqual(6, f.Portals.Frame(f.Session, f.Portals.ByEntity(DoorA)!), "the open run ends on the new art's last open frame");
        Assert.IsTrue(f.Portals.Retarget(f.A.Key, null, new(false, 0, 1), 0, []));
        Assert.IsNull(f.Portals.ByEntity(DoorA)!.ArtId);
        Assert.AreEqual(3, f.Portals.ByEntity(DoorA)!.OpenFrame, "the map's own art returns");
    }

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
    public void ALostDoorHostEndsTheDoorRunAndLeavesTheDoor()
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
    public void AMapCloseDuringTheClosingEndsTheRunAtLevelSixAndItsEndNodeSnapsTheDoorShut()
    {
        using var f = Open();
        f.Host.Open(f.A, "hero");
        for (var i = 0; i < 400 && !(f.Host.GoalOf(f.A) == GoalType.AnimateDoorClosed && f.FrameOf(f.A) == 2); i++) f.Step.AdvanceHost(20);
        Assert.AreEqual(GoalType.AnimateDoorClosed, f.Host.GoalOf(f.A));
        var frame = f.FrameOf(f.A);
        Assert.IsTrue(frame is > 0 and < 3, "mid-closing: " + frame);
        // The map's close interrupts every run at level 6; the ends it reports go to the door host, whose end node closes the door (GT47).
        var change = f.Session.InterruptGoals(DoorA, GoalInterruptRules.MaxLevel);
        Assert.IsTrue(change.Steps.Any(step => step.Ended is not null));
        foreach (var end in change.Ended) f.Host.EndNodes(end);
        Assert.AreEqual(0, f.FrameOf(f.A));
    }

    [TestMethod]
    public void TheCloseWaitsTheOriginalsTwoIntervalsAfterTheLastOpenFrame()
    {
        using var f = Open();
        f.Host.Open(f.A, "hero");
        long t = 0, open = -1, closing = -1;
        for (; t < 8000 && f.FrameOf(f.A) != 0 || t == 0; t += 20)
        {
            f.Step.AdvanceHost(20);
            if (open < 0 && f.FrameOf(f.A) == 3) open = t;
            if (open >= 0 && closing < 0 && f.FrameOf(f.A) == 2) closing = t;
        }
        Assert.IsTrue(open >= 0 && closing >= 0, $"open at {open}, closing at {closing}");
        Assert.IsTrue(closing - open is >= 2 * AiExecutorRules.DoorCloseWaitMs - 40 and <= 2 * AiExecutorRules.DoorCloseWaitMs + 200, $"the closing begins {closing - open} ms after the last open frame");
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
