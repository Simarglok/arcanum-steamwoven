using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Experiments.RelightLab.Tests;

[TestClass]
public sealed class WalkCycleShimmerTests
{
    private static CycleFrame Frame(int w, int h, Func<int, int, byte> value)
    {
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++) { var i = (y * w + x) * 4; rgba[i] = rgba[i + 1] = rgba[i + 2] = value(x, y); rgba[i + 3] = 255; }
        return new CycleFrame(w, h, rgba);
    }

    private static readonly NormalMapOptions Options = NormalMapOptions.For(SpriteKind.Critter);

    [TestMethod]
    public void A_still_animation_has_no_shimmer()
    {
        var still = Frame(12, 12, (x, y) => (byte)(60 + x * 5 + y * 3));
        var result = WalkCycleShimmer.Measure([still, still, still], Options);
        Assert.AreEqual(2, result.Pairs);
        Assert.AreEqual(0, result.RatioMean);
        Assert.AreEqual(0, result.MeanNormalChangeDegrees, 1e-4);
    }

    [TestMethod]
    public void A_loop_adds_the_pair_from_the_last_frame_back_to_the_first()
    {
        var a = Frame(10, 10, (x, _) => (byte)(40 + x * 10)); var b = Frame(10, 10, (_, y) => (byte)(40 + y * 10));
        Assert.AreEqual(1, WalkCycleShimmer.Measure([a, b], Options).Pairs);
        Assert.AreEqual(2, WalkCycleShimmer.Measure([a, b], Options, loop: true).Pairs);
        Assert.AreEqual(0, WalkCycleShimmer.Measure([a], Options, loop: true).Pairs, "one frame has no pair, with or without a loop");
    }

    [TestMethod]
    public void Frames_of_different_sizes_are_skipped_not_compared()
    {
        var a = Frame(10, 10, (x, _) => (byte)(40 + x * 10)); var b = Frame(8, 8, (x, _) => (byte)(40 + x * 10));
        var result = WalkCycleShimmer.Measure([a, b, b], Options);
        Assert.AreEqual(1, result.Skipped);
        Assert.AreEqual(1, result.Pairs);
    }

    [TestMethod]
    public void A_change_in_one_corner_moves_the_normals_there_and_not_elsewhere()
    {
        var a = Frame(24, 24, (x, y) => (byte)(80 + (x + y) % 3));
        var b = Frame(24, 24, (x, y) => x < 4 && y < 4 ? (byte)200 : (byte)(80 + (x + y) % 3));
        var result = WalkCycleShimmer.Measure([a, b], Options);
        Assert.IsTrue(result.RatioMean > 0 && result.RatioMean < 5, $"a finite, non-zero ratio ({result.RatioMean})");
        Assert.IsTrue(result.MeanNormalChangeDegrees > 0 && result.MeanNormalChangeDegrees < 10, $"the mean over the whole frame is small, the corner's change is diluted ({result.MeanNormalChangeDegrees})");
    }
}
