using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-2767: the Fate Points and Sleep balls are centred in the generated strip's ring round their hole, on a synthetic strip (no game art).</summary>
[TestClass]
public sealed class AreaBarRingFitTests
{
    private const int Width = 800, Height = 41;

    // An opaque strip with a round hole of diameter 21 whose box starts at each given point, and a square 34 × 34 hole at (4, 2) like the Character button's.
    private static IReadOnlyList<InterfaceChromeRenderer.Hole> Holes(params (int X, int Y)[] round)
    {
        var rgba = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var i = (y * Width + x) * 4;
            var hole = x >= 4 && x < 38 && y >= 2 && y < 36;
            foreach (var (hx, hy) in round)
            {
                double dx = x + 0.5 - (hx + 10.5), dy = y + 0.5 - (hy + 10.5);
                hole |= dx * dx + dy * dy < 10.5 * 10.5;
            }
            (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = hole ? ((byte)0, (byte)0, (byte)0, (byte)0) : ((byte)90, (byte)60, (byte)30, (byte)255);
        }
        return InterfaceChromeRenderer.Holes(Width, Height, rgba);
    }

    [TestMethod]
    public void The_fate_and_sleep_balls_move_onto_the_centres_of_their_holes()
    {
        // The original's places: the ball 23 × 23 at (157, 9) and (605, 9); the holes 21 × 21 at (158, 9) and (605, 9) (centres 168.5, 19.5 and 615.5, 19.5).
        var holes = Holes((158, 9), (605, 9));
        var fate = AreaGameBarLayout.Spec(AreaBarButton.FatePoints);
        var sleep = AreaGameBarLayout.Spec(AreaBarButton.Sleep);
        var (fx, fy) = AreaBarRingFit.Shift(fate.X, fate.Y, 23, 23, holes);
        var (sx, sy) = AreaBarRingFit.Shift(sleep.X, sleep.Y, 23, 23, holes);
        Assert.AreEqual((0.0, -1.0), (fx, fy));
        Assert.AreEqual((-1.0, -1.0), (sx, sy));
        // After the shift each ball's centre is its hole's centre to the pixel.
        Assert.AreEqual(168.5, fate.X + fx + 11.5, 1e-9); Assert.AreEqual(19.5, fate.Y + fy + 11.5, 1e-9);
        Assert.AreEqual(615.5, sleep.X + sx + 11.5, 1e-9); Assert.AreEqual(19.5, sleep.Y + sy + 11.5, 1e-9);
    }

    [TestMethod]
    public void A_ball_that_is_already_centred_stays_put()
    {
        Assert.AreEqual((0.0, 0.0), AreaBarRingFit.Shift(157, 8, 23, 23, Holes((158, 9))));
    }

    [TestMethod]
    public void A_square_hole_a_far_hole_and_no_hole_give_no_shift()
    {
        // The Character button (34 × 34 at (4, 2)) fills its square hole; a round hole further than the slack is another button's; with no holes the strip draws no ring.
        Assert.AreEqual((0.0, 0.0), AreaBarRingFit.Shift(4, 2, 34, 34, Holes((158, 9))));
        Assert.AreEqual((0.0, 0.0), AreaBarRingFit.Shift(157, 20, 23, 23, Holes((158, 9))));
        Assert.AreEqual((0.0, 0.0), AreaBarRingFit.Shift(157, 9, 23, 23, []));
    }
}
