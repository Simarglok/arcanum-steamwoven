using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1428: the text rule of the reading windows on the handheld (the masthead's Garmond8 and Garmond6 under 9 px at factor 1).</summary>
[TestClass]
public sealed class AreaReadingTextTests
{
    [TestMethod]
    public void The_masthead_small_font_is_drawn_at_the_text_only_factor_on_both_handhelds()
    {
        foreach (var (width, height) in new[] { (1280, 800), (1920, 1080) })
        {
            var screen = AreaUiScale.ScreenFactor(width, height);
            Assert.AreEqual(1, screen);
            Assert.AreEqual(16, AreaReadingText.HeaderLinePx(482, screen), "Garmond8 at the masthead");
            Assert.AreEqual(12, AreaReadingText.HeaderLinePx(481, screen), "Garmond6");
            Assert.AreEqual(9, AreaReadingText.HeaderLinePx(483, screen), "Garmond9 stays on the minimum");
            Assert.AreEqual(20, AreaReadingText.HeaderLinePx(485, screen), "Zurich20");
            Assert.AreEqual(30, AreaReadingText.HeaderLinePx(484, screen), "Georgia30");
        }
    }

    [TestMethod]
    public void No_header_line_is_under_the_minimum_at_any_factor()
    {
        foreach (var font in AreaReadingText.FontPx.Keys.Concat([0, 1, 999]))
            for (var screen = 1; screen <= 4; screen++)
            {
                var px = AreaReadingText.HeaderLinePx(font, screen);
                Assert.IsTrue(px >= AreaUiScale.MinimumTextPx, $"font {font} x{screen} = {px}");
                Assert.IsTrue(px <= Math.Max(AreaUiScale.MinimumTextPx, AreaReadingText.MaximumLinePx * screen), $"font {font} x{screen} = {px}");
            }
    }

    [TestMethod]
    public void A_larger_screen_factor_does_not_multiply_the_text_twice()
    {
        // 8 px at screen factor 2 is already 16 px: no text-only factor on top.
        Assert.AreEqual(16, AreaReadingText.HeaderLinePx(482, 2));
        Assert.AreEqual(UnknownAt(3), AreaReadingText.HeaderLinePx(490, 3));
    }

    private static int UnknownAt(int screen) => Math.Min(AreaReadingText.UnknownFontPx * screen, AreaReadingText.MaximumLinePx * screen);
}
