using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-GPAD-1377: the whole-number scale of the original's windows on the handheld (1280 × 800, a 7 inch screen) and the
/// smallest-text rule (9 px at least, 12 px recommended).</summary>
[TestClass]
public sealed class AreaUiScaleTests
{
    [TestMethod]
    public void The_handheld_screen_draws_the_original_whole_screen_once()
    {
        // 800 × 600 fits 1280 × 800 once; a second whole step needs 1600 × 1200, so no whole factor above 1 exists there.
        Assert.AreEqual(1, AreaUiScale.ScreenFactor(AreaUiScale.HandheldWidth, AreaUiScale.HandheldHeight));
        Assert.AreEqual(1, AreaUiScale.ScreenFactor(1599, 1200));
        Assert.AreEqual(1, AreaUiScale.ScreenFactor(1600, 1199));
        Assert.AreEqual(2, AreaUiScale.ScreenFactor(1600, 1200));
    }

    [TestMethod]
    public void Other_resolutions_keep_their_whole_factor()
    {
        // The factor the viewer used before the policy was written down (floor of the smaller ratio, at least 1): unchanged.
        foreach (var (width, height, factor) in new[] { (800, 600, 1), (1024, 768, 1), (1280, 720, 1), (1920, 1080, 1), (2560, 1440, 2), (2560, 1600, 2), (3840, 2160, 3), (640, 480, 1), (1, 1, 1) })
            Assert.AreEqual(factor, AreaUiScale.ScreenFactor(width, height), $"{width} x {height}");
    }

    [TestMethod]
    public void The_text_rule_is_nine_pixels_at_least_and_twelve_recommended()
    {
        // The owner's numbers (2026-11-12); the viewer's smallest Godot text and tools/research/ui_text_scale.py read the same two.
        var limits = new[] { AreaUiScale.MinimumTextPx, AreaUiScale.PreferredTextPx };
        CollectionAssert.AreEqual(new[] { 9, 12 }, limits);
    }

    [TestMethod]
    public void A_font_under_the_minimum_takes_the_smallest_whole_text_only_factor()
    {
        // ARC-UI-1428: Garmond8 (8) and Garmond6 (6) at the handhelds' screen factor 1 reach 9 px only with a text factor of 2.
        Assert.AreEqual(2, AreaUiScale.TextOnlyFactor(8, 1));
        Assert.AreEqual(2, AreaUiScale.TextOnlyFactor(6, 1));
        Assert.AreEqual(3, AreaUiScale.TextOnlyFactor(3, 1));
        // Garmond9 sits on the minimum, Garmond8 is enough at screen factor 2, and an unknown size is not multiplied.
        Assert.AreEqual(1, AreaUiScale.TextOnlyFactor(9, 1));
        Assert.AreEqual(1, AreaUiScale.TextOnlyFactor(8, 2));
        Assert.AreEqual(1, AreaUiScale.TextOnlyFactor(0, 1));
        Assert.AreEqual(1, AreaUiScale.TextOnlyFactor(-4, 1));
        for (var size = 1; size <= 12; size++)
            for (var screen = 1; screen <= 3; screen++)
                Assert.IsTrue(size * screen * AreaUiScale.TextOnlyFactor(size, screen) >= AreaUiScale.MinimumTextPx, $"{size} x {screen}");
    }
}
