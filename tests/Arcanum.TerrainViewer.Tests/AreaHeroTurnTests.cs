using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-DEV-145 (MIG13 S10a, GT102): the hero's rotate goal (53) timeline: 30 ms per 45 degrees, the facing done one tick before the run ends, the end node snapping an interrupted turn.</summary>
[TestClass]
public sealed class AreaHeroTurnTests
{
    [TestMethod]
    public void ANinetyDegreeTurnReachesItsFacingThirtyMillisecondsAfterItsFirstStep()
    {
        var turn = new AreaHeroTurn();
        turn.Start(5, 3, 1_000);
        Assert.IsFalse(turn.Advance(1_000 + AreaHeroTurn.FirstStepMs - 1), "nothing before the first step");
        Assert.IsTrue(turn.Advance(1_000 + AreaHeroTurn.FirstStepMs));
        Assert.AreEqual(4, turn.Facing);
        Assert.IsFalse(turn.Advance(1_000 + AreaHeroTurn.FirstStepMs + 29));
        Assert.IsTrue(turn.Advance(1_000 + AreaHeroTurn.FirstStepMs + 30));
        Assert.AreEqual(3, turn.Facing);
        Assert.IsTrue(turn.Active, "the run ends one tick after the last step");
        Assert.IsFalse(turn.Advance(1_000 + AreaHeroTurn.FirstStepMs + 60));
        Assert.IsFalse(turn.Active);
    }

    [TestMethod]
    public void ALateFrameRunsEveryStepThatCameDue()
    {
        var turn = new AreaHeroTurn();
        turn.Start(5, 1, 0);
        Assert.IsTrue(turn.Advance(10_000));
        Assert.AreEqual(1, turn.Facing);
        Assert.IsFalse(turn.Active);
    }

    [TestMethod]
    public void AnInterruptedTurnEndsFacingItsTarget()
    {
        var turn = new AreaHeroTurn();
        turn.Start(5, 1, 0);
        turn.Advance(AreaHeroTurn.FirstStepMs);
        Assert.AreEqual(4, turn.Facing);
        Assert.IsTrue(turn.End());
        Assert.AreEqual(1, turn.Facing);
        Assert.IsFalse(turn.Active);
        Assert.IsFalse(turn.End(), "no run, nothing to end");
    }

    [TestMethod]
    public void AHalfTurnTakesFourStepsAndTheRunEndsAHundredAndTwentyMillisecondsAfterTheFirst()
    {
        var turn = new AreaHeroTurn();
        turn.Start(5, 1, 0);
        const int first = AreaHeroTurn.FirstStepMs;
        Assert.IsTrue(turn.Advance(first + 90));
        Assert.AreEqual(1, turn.Facing, "facing done at +90 ms after the first step");
        Assert.IsTrue(turn.Active);
        Assert.IsFalse(turn.Advance(first + 119));
        Assert.IsTrue(turn.Active);
        Assert.IsFalse(turn.Advance(first + 120));
        Assert.IsFalse(turn.Active);
    }

    [TestMethod]
    public void AStartWhileRunningRestartsFromTheNewFacing()
    {
        var turn = new AreaHeroTurn();
        turn.Start(5, 1, 0);
        turn.Advance(AreaHeroTurn.FirstStepMs);
        turn.Start(turn.Facing, 7, 100);
        Assert.AreEqual(7, turn.Target);
        turn.Advance(10_000);
        Assert.AreEqual(7, turn.Facing);
    }
}
