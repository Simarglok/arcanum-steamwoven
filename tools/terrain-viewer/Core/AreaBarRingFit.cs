namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2767: where a round bar button stands so that it is centred in the ring the generated strip draws round its hole. The generated chrome lays a brass ring round every round hole of the strip's
/// Legacy art (<see cref="InterfaceChromeRenderer.Holes"/>, centred on the hole's own box), and the button's own picture (a 23 × 23 ball for Fate Points and Sleep) is drawn over that hole. The original's
/// places put the ball where its own baked rim met the strip's, one Legacy pixel off the hole's centre on one axis for Fate Points (the ball is a unit lower: the hole is 21 × 21 at (158, 9), the ball
/// 23 × 23 at (157, 9)) and on both for Sleep (hole at (605, 9), ball at (605, 9) a unit to the right of the hole's centre), which is invisible at the original's integer scale but shows as an
/// off-centre ball in the generated ring at the Steamwoven strips' scale. The Original look keeps the original's places (and hit areas); the Steamwoven layout shifts the ball by <see cref="Shift"/>.
/// Godot-free; the holes are read from the Legacy frame of the strip art, so no number of the game's art is kept here.
/// </summary>
public static class AreaBarRingFit
{
    /// <summary>The largest shift (Legacy pixels, along either axis) the rule takes: the ball and its hole are the same button, so a hole further away than the original's own slack is another one's.</summary>
    public const double MaxShift = 2;

    /// <summary>
    /// The shift, in Legacy pixels, that puts the centre of the button's box (<paramref name="x"/>, <paramref name="y"/>, <paramref name="width"/> × <paramref name="height"/>, strip pixels) on the centre of the
    /// round hole whose centre is nearest to it; (0, 0) when no round hole lies within <see cref="MaxShift"/> on both axes (a square hole gets a square rim the button already fills, no hole: no ring).
    /// </summary>
    public static (double X, double Y) Shift(double x, double y, double width, double height, IReadOnlyList<InterfaceChromeRenderer.Hole> holes)
    {
        double bx = x + width * 0.5, by = y + height * 0.5;
        (double X, double Y)? best = null;
        var bestDistance = double.MaxValue;
        foreach (var hole in holes)
        {
            if (!hole.Round) continue;
            double dx = (hole.X0 + hole.X1) * 0.5 - bx, dy = (hole.Y0 + hole.Y1) * 0.5 - by;
            if (Math.Abs(dx) > MaxShift || Math.Abs(dy) > MaxShift) continue;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance) { best = (dx, dy); bestDistance = distance; }
        }
        return best ?? (0, 0);
    }
}
