using Arcanum.Simulation.Combat;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Combat;

// ARC-RENDER-040: how the original plays the dying animation and the knockdown fall (ledger DA1–DA6).
[TestClass]
public sealed class DyingAnimationRulesTests
{
    [TestMethod]
    public void TheDelayIsOneThousandOverTheRateInIntegers()
    {
        Assert.AreEqual(90, DyingAnimationRules.Delay(11), "the installed fall: 11 frames at 11 fps");
        Assert.AreEqual(100, DyingAnimationRules.Delay(10));
        Assert.AreEqual(66, DyingAnimationRules.Delay(15));
        Assert.AreEqual(0, DyingAnimationRules.Delay(1001));
        Assert.AreEqual(DyingAnimationRules.UnreadArtDelayMs, DyingAnimationRules.Delay(0));
        Assert.AreEqual(DyingAnimationRules.UnreadArtDelayMs, DyingAnimationRules.Delay(-3));
    }

    [TestMethod]
    public void FrameOneShowsAtOnceThenOneFramePerDelayAndTheLastStays()
    {
        Assert.AreEqual(1, DyingAnimationRules.Frame(0, 11, 90));
        Assert.AreEqual(1, DyingAnimationRules.Frame(89, 11, 90));
        Assert.AreEqual(2, DyingAnimationRules.Frame(90, 11, 90));
        Assert.AreEqual(8, DyingAnimationRules.Frame(8 * 90 - 1, 11, 90));
        Assert.AreEqual(10, DyingAnimationRules.Frame(9 * 90, 11, 90), "the last frame from (n − 2) delays");
        Assert.AreEqual(10, DyingAnimationRules.Frame(long.MaxValue, 11, 90));
        Assert.AreEqual(0, DyingAnimationRules.Frame(-1, 11, 90), "before the start");
        Assert.AreEqual(0, DyingAnimationRules.Frame(0, 1, 90), "a one-frame animation");
        Assert.AreEqual(10, DyingAnimationRules.Frame(0, 11, 0), "a zero delay runs every step at once");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DyingAnimationRules.Frame(0, 0, 90));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DyingAnimationRules.Frame(0, 3, -1));
    }

    [TestMethod]
    public void TheGoalEndsOneDelayAfterTheLastFrameShows()
    {
        Assert.AreEqual(900, DyingAnimationRules.EndMs(11, 90));
        Assert.AreEqual(10, DyingAnimationRules.Frame(DyingAnimationRules.EndMs(11, 90) - 90, 11, 90), "the last frame shows a delay before");
        Assert.AreEqual(9, DyingAnimationRules.Frame(DyingAnimationRules.EndMs(11, 90) - 91, 11, 90));
        Assert.AreEqual(0, DyingAnimationRules.EndMs(1, 90));
        Assert.AreEqual(0, DyingAnimationRules.EndMs(11, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DyingAnimationRules.EndMs(0, 90));
    }

    [TestMethod]
    public void ACritterInItsFallKeepsIt()
    {
        Assert.IsTrue(DyingAnimationRules.LiesFallen(7));
        foreach (var other in new[] { 0, 1, 6, 8, 17, 18, 19, 20 }) Assert.IsFalse(DyingAnimationRules.LiesFallen(other), $"animation {other}");
    }

    [TestMethod]
    public void TheStartRefusesACritterAlreadyLyingPastFrameZero()
    {
        Assert.AreEqual(90, DyingAnimationRules.Start(0, 3, 11), "a standing critter plays its dying");
        Assert.AreEqual(90, DyingAnimationRules.Start(20, 4, 11), "an attacking one too");
        foreach (var lying in new[] { 7, 17, 18, 19 })
        {
            Assert.IsNull(DyingAnimationRules.Start(lying, 1, 11), $"animation {lying} past frame 0 keeps its pose");
            Assert.AreEqual(90, DyingAnimationRules.Start(lying, 0, 11), $"animation {lying} at frame 0 plays");
        }
        Assert.AreEqual(90, DyingAnimationRules.Start(8, 5, 11), "getting up is no lying pose");
        Assert.AreEqual(90, DyingAnimationRules.Start(16, 5, 11));
        Assert.AreEqual(90, DyingAnimationRules.Start(20, 5, 11));
    }
}
