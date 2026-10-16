using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class FramePacingPolicyTests
{
    [TestMethod]
    public void ActiveFollowsTheDisplayAndIdleCapsAtSixty()
    {
        Assert.AreEqual(120, FramePacingPolicy.Target(0, 120));
        Assert.AreEqual(120, FramePacingPolicy.Target(.49, 119.88));
        Assert.AreEqual(60, FramePacingPolicy.Target(.5, 120));
        Assert.AreEqual(60, FramePacingPolicy.Target(30, 144));
        Assert.AreEqual(50, FramePacingPolicy.Target(30, 50));   // idle never above the display
        Assert.AreEqual(50, FramePacingPolicy.Target(0, 50));
        Assert.AreEqual(60, FramePacingPolicy.Target(0, -1));    // display rate unknown
        Assert.AreEqual(60, FramePacingPolicy.Target(0, double.NaN));
        Assert.AreEqual(480, FramePacingPolicy.Target(0, 10000));
        Assert.AreEqual(60, FramePacingPolicy.Target(double.NaN, 120)); // invalid clock is idle
        Assert.AreEqual(60, FramePacingPolicy.Target(-1, 120));
    }

    [TestMethod]
    public void CapIsCorrectedTowardsTheMeasuredTargetWithinBounds()
    {
        Assert.AreEqual(70, FramePacingPolicy.CorrectCap(60, 60, 50));     // limiter undershoot
        Assert.AreEqual(72, FramePacingPolicy.CorrectCap(60, 70, 58.4));
        Assert.AreEqual(72, FramePacingPolicy.CorrectCap(60, 72, 60.9));   // within tolerance
        Assert.AreEqual(66, FramePacingPolicy.CorrectCap(60, 72, 66));     // overshoot lowers
        Assert.AreEqual(60, FramePacingPolicy.CorrectCap(60, 61, 200));    // never below target
        Assert.AreEqual(720, FramePacingPolicy.CorrectCap(180, 700, 90));  // frame-bound: 4x target
        Assert.AreEqual(240, FramePacingPolicy.CorrectCap(60, 1000, 60));  // out-of-range cap clamped
        Assert.AreEqual(70, FramePacingPolicy.CorrectCap(60, 70, double.NaN));
        Assert.AreEqual(70, FramePacingPolicy.CorrectCap(60, 70, 0));
        Assert.IsTrue(FramePacingPolicy.OnTarget(60, 59));
        Assert.IsFalse(FramePacingPolicy.OnTarget(60, 58.9));
        Assert.IsFalse(FramePacingPolicy.OnTarget(60, double.NaN));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => FramePacingPolicy.CorrectCap(0, 60, 60));
    }
}
