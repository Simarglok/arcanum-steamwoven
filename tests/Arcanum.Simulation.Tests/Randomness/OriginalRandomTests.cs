using Arcanum.Simulation.Randomness;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Randomness;

/// <summary>ARC-WORLD-098: the original's generator (0x4CEF40, 0x4CEF50, 0x4CEF00; ledger RK11).</summary>
[TestClass]
public sealed class OriginalRandomTests
{
    [TestMethod]
    public void EachDrawStepsTheStateAndTakesItsBitsEightToTwentyTwo()
    {
        var random = new OriginalRandom(0);
        Assert.AreEqual(28403, random.Next(), "0x3C6EF35F >> 8 & 0x7FFF");
        Assert.AreEqual(0x3C6EF35Fu, random.State);
        Assert.AreEqual(20521, random.Next());
        Assert.AreEqual(0x47502932u, random.State, "the step wraps at 32 bits");
        Assert.AreEqual(15657, new OriginalRandom(-7).Next(), "a negative seed is its 32-bit pattern");
    }

    [TestMethod]
    public void SeedingSetsTheStateAsItIs()
    {
        var random = new OriginalRandom(5);
        random.Next();
        random.Seed(0);
        Assert.AreEqual(0u, random.State);
        Assert.AreEqual(28403, random.Next());
    }

    [TestMethod]
    public void EqualBoundsDrawNothingAndReversedBoundsAreSwapped()
    {
        var random = new OriginalRandom(12345);
        Assert.AreEqual(7, random.Between(7, 7));
        Assert.AreEqual(12345u, random.State, "no draw");
        Assert.AreEqual(11, random.Between(10, 20));
        var reversed = new OriginalRandom(12345);
        Assert.AreEqual(11, reversed.Between(20, 10));
        Assert.AreEqual((83, 39, 23), (random.Between(1, 100), random.Between(1, 100), random.Between(1, 100)));
    }

    [TestMethod]
    public void AWrappedWidthStillLandsInTheRangeAndTheWholeRangeDoesNotDivideByZero()
    {
        Assert.AreEqual(2137, new OriginalRandom(1).Next());
        Assert.AreEqual(-2_000_000_000 + 2137, new OriginalRandom(1).Between(-2_000_000_000, 2_000_000_000), "width 4,000,000,001 wraps negative");
        Assert.AreEqual(int.MinValue + 2137, new OriginalRandom(1).Between(int.MinValue, int.MaxValue), "width 2^32 wraps to 0: fixed");
    }
}
