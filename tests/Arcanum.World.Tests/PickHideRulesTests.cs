using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.World.Tests;

/// <summary>ARC-INPUT-2517 (VCG16): the box of the dead critters the left-press pick hides (0x440FF0, 0x4B9130).</summary>
[TestClass]
public sealed class PickHideRulesTests
{
    // The world pixel at the centre of tile (x, y): the tile's screen point (40(y-x-1), 20(x+y)) plus (40, 20).
    private static (long X, long Y) CentreOf(long x, long y) => (40 * (y - x - 1) + 40, 20 * (x + y) + 20);

    [TestMethod]
    public void A_view_centred_on_the_hero_hides_the_dead_of_its_screen_diamond_bounds()
    {
        var (cx, cy) = CentreOf(100, 100);
        var box = PickHideRules.DeadBox(100, 100, cx, cy, 1920, 1080)!.Value;
        Assert.AreEqual(new PickHideRules.Box(75, 126, 75, 126), box);
        Assert.IsTrue(box.Contains(100, 100) && box.Contains(126, 75) && box.Contains(75, 126), "the bounds are inclusive");
        Assert.IsFalse(box.Contains(127, 100) || box.Contains(100, 74), "a corpse beyond the box stays pickable");
    }

    [TestMethod]
    public void A_hero_off_the_view_centre_moves_the_box_with_it()
    {
        // The view centre stays on tile (100, 100); the hero stands ten tiles on in x: the rectangle follows the hero, not the view.
        var (cx, cy) = CentreOf(100, 100);
        var box = PickHideRules.DeadBox(110, 100, cx, cy, 1920, 1080)!.Value;
        Assert.AreEqual(new PickHideRules.Box(85, 136, 75, 126), box);
    }

    [TestMethod]
    public void The_box_is_clamped_to_the_map_and_empty_boxes_hide_nothing()
    {
        var (cx, cy) = CentreOf(10, 10);
        Assert.AreEqual(new PickHideRules.Box(0, 36, 0, 36), PickHideRules.DeadBox(10, 10, cx, cy, 1920, 1080)!.Value, "negative bounds go to tile 0");
        var (bx, by) = CentreOf(100, 100);
        Assert.IsNull(PickHideRules.DeadBox(100, 100, bx, by, 1920, 1080, mapWidth: 50, mapHeight: 50), "both bounds clamp to the last tile: an empty axis lists nothing");
        Assert.IsNull(PickHideRules.DeadBox(100, 100, bx, by, 0, 1080), "no view");
    }
}
