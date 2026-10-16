using Arcanum.Simulation.Randomness;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Randomness;

[TestClass]
public sealed class SeededRandomTests
{
    private const ulong Seed = 0x0123456789ABCDEFUL;

    [TestMethod]
    public void OutputsArePublishedSplitMix64Values()
    {
        // The first outputs of the public-domain SplitMix64 reference for seeds 0 and 1234567.
        CollectionAssert.AreEqual(new[] { 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL },
            new[] { SeededRandom.Output(0, 1), SeededRandom.Output(0, 2), SeededRandom.Output(0, 3) });
        CollectionAssert.AreEqual(new[] { 6457827717110365317UL, 3203168211198807973UL, 9817491932198370423UL },
            new[] { SeededRandom.Output(1234567, 1), SeededRandom.Output(1234567, 2), SeededRandom.Output(1234567, 3) });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SeededRandom.Output(0, 0));
    }

    [TestMethod]
    public void RangesFollowTheSequenceAndEqualBoundsDrawNothing()
    {
        // Values worked out by hand from the outputs: min + draw mod span.
        var random = new SeededRandom(Seed);
        Assert.AreEqual((66, 1L), Next(random, 1, 100));
        Assert.AreEqual((44, 2L), Next(random, 1, 100));
        Assert.AreEqual((7, 3L), Next(random, 5, 10));
        Assert.AreEqual((1, 4L), Next(random, -3, 3));
        Assert.AreEqual((0, 4L), Next(random, 0, 0));
        Assert.AreEqual((53, 5L), Next(random, 1, 100));
        Assert.AreEqual(Seed, random.Seed);

        // The widest range takes the draw's low 32 bits unchanged in order.
        Assert.AreEqual((613395101, 1L), Next(new SeededRandom(Seed), int.MinValue, int.MaxValue));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SeededRandom(Seed).TryNext(2, 1, out _));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new SeededRandom(Seed, -1));
    }

    [TestMethod]
    public void AGeneratorRebuiltFromSeedAndDrawsContinuesTheSequence()
    {
        var first = new SeededRandom(Seed);
        for (var i = 0; i < 3; i++) first.TryNext(1, 100, out _);
        var rebuilt = new SeededRandom(first.Seed, first.Draws);
        var expected = new[] { 41, 53, 47, 2, 10 };
        CollectionAssert.AreEqual(expected, Enumerable.Range(0, 5).Select(_ => Next(first, 1, 100).Value).ToArray());
        CollectionAssert.AreEqual(expected, Enumerable.Range(0, 5).Select(_ => Next(rebuilt, 1, 100).Value).ToArray());
        Assert.AreEqual(8, rebuilt.Draws);
    }

    [TestMethod]
    public void ADrawInTheBiasedTailIsReplacedByTheNext()
    {
        // Seed -Gamma makes the first draw 0, below 2^64 mod 100 = 16, so a d100 skips it.
        var seed = unchecked(0UL - SeededRandom.Gamma);
        Assert.AreEqual(0UL, SeededRandom.Output(seed, 1));
        var random = new SeededRandom(seed);
        Assert.AreEqual((1 + (int)(SeededRandom.Output(seed, 2) % 100), 2L), Next(random, 1, 100));
        Assert.AreEqual(36, 1 + (int)(SeededRandom.Output(seed, 2) % 100));
        // A power-of-two span has no tail: draw 0 is the low bound.
        Assert.AreEqual((1, 1L), Next(new SeededRandom(seed), 1, 64));
    }

    [TestMethod]
    public void TheLastDrawEndsTheSequenceWithoutMovingIt()
    {
        var end = new SeededRandom(Seed, long.MaxValue);
        Assert.IsFalse(end.TryNext(1, 100, out var value));
        Assert.AreEqual(0, value);
        Assert.AreEqual(long.MaxValue, end.Draws);
        Assert.AreEqual((5, long.MaxValue), Next(end, 5, 5));

        // A rejected last draw leaves the position where it was.
        var seed = unchecked(0UL - (ulong)long.MaxValue * SeededRandom.Gamma);
        Assert.AreEqual(0UL, SeededRandom.Output(seed, long.MaxValue));
        var nearEnd = new SeededRandom(seed, long.MaxValue - 1);
        Assert.IsFalse(nearEnd.TryNext(1, 100, out _));
        Assert.AreEqual(long.MaxValue - 1, nearEnd.Draws);
        Assert.AreEqual((1, long.MaxValue), Next(nearEnd, 1, 64));
    }

    [TestMethod]
    public void FacesOfADieComeUpEvenly()
    {
        var random = new SeededRandom(Seed);
        var counts = new int[6];
        for (var i = 0; i < 60000; i++) counts[Next(random, 1, 6).Value - 1]++;
        // Expected 10,000 each; the bound is about four standard deviations.
        foreach (var count in counts) Assert.IsTrue(Math.Abs(count - 10000) < 400, $"{string.Join(",", counts)}");
        Assert.AreEqual(60000, random.Draws);
    }

    private static (int Value, long Draws) Next(SeededRandom random, int min, int max)
    {
        Assert.IsTrue(random.TryNext(min, max, out var value));
        return (value, random.Draws);
    }
}
