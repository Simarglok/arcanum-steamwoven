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
    public void The_handheld_class_is_a_window_no_larger_than_1280_by_800()
    {
        Assert.IsTrue(AreaUiScale.IsHandheld(1280, 800));
        Assert.IsTrue(AreaUiScale.IsHandheld(1280, 720));
        Assert.IsFalse(AreaUiScale.IsHandheld(1920, 1080));
        Assert.IsFalse(AreaUiScale.IsHandheld(1280, 801));
    }

    [TestMethod]
    public void Text_is_legible_from_nine_pixels_at_the_windows_factor()
    {
        // The owner's numbers (2026-11-12): 9 px at least, 12 px recommended; the boundary is exact.
        var limits = new[] { AreaUiScale.MinimumTextPx, AreaUiScale.PreferredTextPx };
        CollectionAssert.AreEqual(new[] { 9, 12 }, limits);
        Assert.IsTrue(AreaUiScale.TextIsLegible(9, 1));
        Assert.IsFalse(AreaUiScale.TextIsLegible(8, 1));
        Assert.IsTrue(AreaUiScale.TextIsLegible(6, 2));
        Assert.AreEqual(24, AreaUiScale.TextPx(12, 2));
    }
}
