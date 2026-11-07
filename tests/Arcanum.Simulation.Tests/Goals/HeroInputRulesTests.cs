using Arcanum.Simulation.Goals;
using Arcanum.Simulation.Movement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Goals;

/// <summary>ARC-DEV-145 (MIG13 S10a): the hero's input rules against the S10 read (GT100–GT102): always run 0x4304C0, the rotate starter's gates 0x433440, goal 53's step 0x42C440 and the right release's tail (RR5).</summary>
[TestClass]
public sealed class HeroInputRulesTests
{
    [TestMethod]
    [DataRow(true, false, false, true)]
    [DataRow(true, true, false, false)]
    [DataRow(true, false, true, true)]
    [DataRow(true, true, true, false)]
    [DataRow(false, false, false, false)]
    [DataRow(false, true, false, true)]
    [DataRow(false, false, true, true)]
    [DataRow(false, true, true, true)]
    public void AlwaysRunFollowsTheOptionCtrlAndNumLock(bool option, bool ctrl, bool numLock, bool runs) =>
        Assert.AreEqual(runs, HeroInputRules.AlwaysRun(option, ctrl, numLock));

    [TestMethod]
    [DataRow(0, 1, 1)]
    [DataRow(0, 4, 1)]   // diff -4 (a 180-degree tie): +1
    [DataRow(4, 0, 3)]   // diff +4 (the tie): -1
    [DataRow(0, 7, 7)]   // diff -7: wraps the short way
    [DataRow(7, 0, 0)]   // diff +7: wraps the short way
    [DataRow(2, 5, 3)]
    [DataRow(5, 2, 4)]
    [DataRow(3, 3, 3)]
    public void RotateStepTurnsTheShortWay(int current, int target, int landed) =>
        Assert.AreEqual(landed, HeroInputRules.RotateStep(current, target));

    [TestMethod]
    public void RotateStepCountsTheStepsOfNinetyAndOneEightyDegrees()
    {
        Assert.AreEqual(0, HeroInputRules.RotateSteps(5, 5));
        Assert.AreEqual(1, HeroInputRules.RotateSteps(5, 4));
        Assert.AreEqual(2, HeroInputRules.RotateSteps(5, 3));
        Assert.AreEqual(4, HeroInputRules.RotateSteps(5, 1));
        Assert.AreEqual(2, HeroInputRules.RotateSteps(0, 6));
        for (var from = 0; from < 8; from++)
            for (var to = 0; to < 8; to++)
                Assert.IsTrue(HeroInputRules.RotateSteps(from, to) <= 4, $"{from}->{to}");
    }

    [TestMethod]
    public void RotateStepRejectsARotationOutsideZeroToSeven()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HeroInputRules.RotateStep(8, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HeroInputRules.RotateStep(0, -1));
    }

    private static RotateStartFacts Facts(bool hasObject = true, int facing = 5, int target = 3, bool active = true, bool inRound = false, bool current = false, bool mainRun = false) =>
        new(hasObject, facing, target, active, inRound, current, mainRun);

    [TestMethod]
    public void RotateStartTestsItsGatesInTheOriginalsOrder()
    {
        Assert.AreEqual(RotateStartVerdict.Start, HeroInputRules.RotateStart(Facts()));
        Assert.AreEqual(RotateStartVerdict.NoObject, HeroInputRules.RotateStart(Facts(hasObject: false)));
        Assert.AreEqual(RotateStartVerdict.AlreadyFacing, HeroInputRules.RotateStart(Facts(facing: 3, active: false, mainRun: true)));
        Assert.AreEqual(RotateStartVerdict.NotActive, HeroInputRules.RotateStart(Facts(active: false, inRound: true)));
        Assert.AreEqual(RotateStartVerdict.NotParticipant, HeroInputRules.RotateStart(Facts(inRound: true, current: false, mainRun: true)));
        Assert.AreEqual(RotateStartVerdict.MainRunBusy, HeroInputRules.RotateStart(Facts(inRound: true, current: true, mainRun: true)));
        Assert.AreEqual(RotateStartVerdict.Start, HeroInputRules.RotateStart(Facts(inRound: true, current: true)));
    }

    [TestMethod]
    public void TheTailTurnsTheHeroTowardThePointerAfterTheInterrupt()
    {
        // The hero at (10,10), the pointer on (10,14): the same x, larger y is rotation 3; the hero faces 5.
        var tail = HeroInputRules.RightReleaseTailOf(2, new CellCoord(10, 10), new CellCoord(10, 14), 5, true, false, false);
        Assert.IsTrue(tail.Interrupted);
        Assert.IsTrue(tail.StartsTurn);
        Assert.AreEqual(3, tail.Rotation);
    }

    [TestMethod]
    public void ASpellAttemptRefusesTheInterruptAndEndsTheTail()
    {
        var tail = HeroInputRules.RightReleaseTailOf(4, new CellCoord(10, 10), new CellCoord(10, 14), 5, true, false, false);
        Assert.IsFalse(tail.Interrupted);
        Assert.IsFalse(tail.StartsTurn);
        Assert.IsNull(tail.Rotation);
        // Priority 5 refuses as well; an ordinary action (3) and a walk (2) do not.
        Assert.IsFalse(HeroInputRules.RightReleaseTailOf(5, new CellCoord(0, 0), new CellCoord(1, 1), 5, true, false, false).Interrupted);
        Assert.IsTrue(HeroInputRules.RightReleaseTailOf(3, new CellCoord(0, 0), new CellCoord(1, 1), 5, true, false, false).Interrupted);
    }

    [TestMethod]
    public void NoPointerTileMeansNoTurnButTheInterruptStands()
    {
        var tail = HeroInputRules.RightReleaseTailOf(2, new CellCoord(10, 10), null, 5, true, false, false);
        Assert.IsTrue(tail.Interrupted);
        Assert.IsFalse(tail.StartsTurn);
        Assert.IsNull(tail.Rotation);
    }

    [TestMethod]
    public void ThePointerOnTheHeroTileTurnsToRotationFiveUnlessFacingIt()
    {
        var turn = HeroInputRules.RightReleaseTailOf(0, new CellCoord(4, 4), new CellCoord(4, 4), 0, true, false, false);
        Assert.AreEqual(5, turn.Rotation);
        Assert.IsTrue(turn.StartsTurn);
        var already = HeroInputRules.RightReleaseTailOf(0, new CellCoord(4, 4), new CellCoord(4, 4), 5, true, false, false);
        Assert.AreEqual(RotateStartVerdict.AlreadyFacing, already.Turn);
    }

    [TestMethod]
    public void AnotherParticipantsTurnStartsNoTurnInARound()
    {
        var tail = HeroInputRules.RightReleaseTailOf(0, new CellCoord(0, 0), new CellCoord(0, 3), 5, true, inRound: true, isCurrentParticipant: false);
        Assert.IsTrue(tail.Interrupted);
        Assert.AreEqual(RotateStartVerdict.NotParticipant, tail.Turn);
    }

    // ---- ARC-DEV-147: the stick's held walk (Steamwoven's mapping of a held input) ----

    [TestMethod]
    [DataRow(NeighborDirection.XPlus, 108, 100)]
    [DataRow(NeighborDirection.XMinusYMinus, 92, 92)]
    [DataRow(NeighborDirection.YPlus, 100, 108)]
    [DataRow(NeighborDirection.XPlusYMinus, 108, 92)]
    public void AHeldWalkAsksForTheTileTheReachAheadInTheSticksDirection(NeighborDirection direction, int x, int y) =>
        Assert.AreEqual(new CellCoord(x, y), HeroInputRules.HeldWalkTarget(new CellCoord(100, 100), direction, _ => true));

    [TestMethod]
    public void AHeldWalkStopsAtTheMapsEdgeAndHasNoTileWhenTheFirstCellIsOutside()
    {
        var inside = (CellCoord c) => c.X < 103;
        var near = HeroInputRules.HeldWalkTarget(new CellCoord(100, 100), NeighborDirection.XPlus, inside);
        Assert.AreEqual(2, near!.Value.X - 100, "clamped to the last cell the map contains");
        Assert.IsNull(HeroInputRules.HeldWalkTarget(new CellCoord(102, 100), NeighborDirection.XPlus, c => c.X < 103));
    }

    [TestMethod]
    [DataRow(0.0, false)]
    [DataRow(250.0, false)]
    [DataRow(250.5, true)]
    [DataRow(1000.0, true)]
    public void AHeldInputRepeatsOnceMoreThanTwoHundredFiftyMillisecondsPassed(double elapsedMs, bool due) =>
        Assert.AreEqual(due, HeroInputRules.HeldRepeatDue(elapsedMs));
}
