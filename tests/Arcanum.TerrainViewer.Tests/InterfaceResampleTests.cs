using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-RENDER-3002, ARC-UI-148: the pixel size and the area filter of a pack frame at a device scale, on synthetic frames (no game art).</summary>
[TestClass]
public sealed class InterfaceResampleTests
{
    private static (int R, int G, int B, int A) Pixel(byte[] rgba, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    [TestMethod]
    public void A_whole_scale_gives_exactly_the_size_times_the_scale_and_a_fractional_one_rounds_each_edge()
    {
        Assert.AreEqual((96, 40), InterfaceResample.PixelSize(48, 20, 2));
        Assert.AreEqual((72, 30), InterfaceResample.PixelSize(48, 20, 1.5));
        Assert.AreEqual((173, 72), InterfaceResample.PixelSize(48, 20, 3.6));
    }

    [TestMethod]
    public void A_straight_resample_averages_alpha_and_weights_colour_by_it()
    {
        var source = new byte[2 * 1 * 4];
        (source[0], source[1], source[2], source[3]) = (255, 0, 0, 0);
        (source[4], source[5], source[6], source[7]) = (0, 0, 255, 255);
        var one = InterfaceResample.ResampleStraight(source, 2, 1, 1, 1);
        Assert.AreEqual((0, 0, 255, 128), Pixel(one, 1, 0, 0), "half the area is transparent: alpha is the mean, the colour is the opaque texel's");
        CollectionAssert.AreEqual(source, InterfaceResample.ResampleStraight(source, 2, 1, 2, 1), "the same size is returned as it is, alpha included");
        var hole = InterfaceResample.ResampleStraight(new byte[4 * 4 * 4], 4, 4, 3, 3);
        Assert.AreEqual((0, 0, 0, 0), Pixel(hole, 3, 1, 1), "a fully transparent area stays transparent");
        var opaque = new byte[4 * 4 * 4];
        for (var i = 0; i < opaque.Length; i += 4) (opaque[i], opaque[i + 1], opaque[i + 2], opaque[i + 3]) = (10, 20, 30, 255);
        foreach (var (w, h) in new[] { (3, 3), (2, 2), (1, 1), (3, 2) })
        {
            var flat = InterfaceResample.ResampleStraight(opaque, 4, 4, w, h);
            for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) Assert.AreEqual((10, 20, 30, 255), Pixel(flat, w, x, y), $"a flat opaque frame stays flat at {w}x{h}");
        }
    }

    [TestMethod]
    public void A_wrong_sized_source_or_target_is_refused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => InterfaceResample.ResampleStraight(new byte[10], 2, 2, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => InterfaceResample.ResampleStraight(new byte[16], 2, 2, 0, 1));
    }
}
