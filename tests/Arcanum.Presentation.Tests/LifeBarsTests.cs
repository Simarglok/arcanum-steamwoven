using Arcanum.Presentation.Hud;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

/// <summary>ARC-UI-020: the health and fatigue bar rules against the behaviour recorded in docs/research/ledger/ui-audio.md
/// CH7–CH9 (the PC's gauges and numbers, the hovered critter's bars and text, the follower portraits); values invented,
/// expected results worked by hand.</summary>
[TestClass]
public sealed class LifeBarsTests
{
    [TestMethod]
    public void Percent_truncates_toward_zero_and_reads_a_zero_maximum_as_none()
    {
        Assert.AreEqual(66, LifeBarRules.Percent(2, 3), "200 / 3 = 66.7 → 66");
        Assert.AreEqual(100, LifeBarRules.Percent(30, 30));
        Assert.AreEqual(-3, LifeBarRules.Percent(-1, 30), "−100 / 30 = −3.3 → −3, not clamped");
        Assert.AreEqual(0, LifeBarRules.Percent(5, 0), "the original divides by zero here: the technical fix reads 0");
        Assert.AreEqual(int.MaxValue, LifeBarRules.Percent(int.MaxValue, 1), "no overflow");
        Assert.AreEqual(-50, LifeBarRules.Percent(1, -2), "a negative maximum divides as the original's integer division");
    }

    [TestMethod]
    public void The_pc_gauges_fill_the_percent_of_their_height_and_half_at_a_zero_maximum()
    {
        Assert.AreEqual(88, LifeBarRules.GaugeFill(40, 40, LifeBarRules.GaugeHeight));
        Assert.AreEqual(58, LifeBarRules.GaugeFill(27, 40, 88), "67 % of 88 = 58.96 → 58 (the percent truncated first)");
        Assert.AreEqual(0, LifeBarRules.GaugeFill(1, 101, 88), "0 % → nothing");
        Assert.AreEqual(44, LifeBarRules.GaugeFill(7, 0, 88), "a zero maximum counts as 50 %");
        Assert.AreEqual(0, LifeBarRules.GaugeFill(-5, 40, 88), "a dead PC's negative fill is clamped to the gauge");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => LifeBarRules.GaugeFill(1, 1, -1));
    }

    [TestMethod]
    public void Gauge_numbers_are_c_style_three_digit_zero_padded()
    {
        Assert.AreEqual("007", LifeBarRules.GaugeNumber(7));
        Assert.AreEqual("000", LifeBarRules.GaugeNumber(0));
        Assert.AreEqual("120", LifeBarRules.GaugeNumber(120));
        Assert.AreEqual("1234", LifeBarRules.GaugeNumber(1234), "a wider value is not cut");
        Assert.AreEqual("-05", LifeBarRules.GaugeNumber(-5), "the sign counts in the width");
        Assert.AreEqual("-123", LifeBarRules.GaugeNumber(-123));
        Assert.AreEqual("-2147483648", LifeBarRules.GaugeNumber(int.MinValue));
    }

    [TestMethod]
    public void A_hovered_bar_fills_one_pixel_more_than_its_share_unless_full()
    {
        Assert.AreEqual(0, LifeBarRules.HoverFill(0, 291), "0 % draws nothing");
        Assert.AreEqual(0, LifeBarRules.HoverFill(-7, 291), "a negative percent is clamped to 0");
        Assert.AreEqual(3, LifeBarRules.HoverFill(1, 291), "291 / 100 = 2.91 → 2, + 1");
        Assert.AreEqual(146, LifeBarRules.HoverFill(50, 291), "145.5 → 145, + 1");
        Assert.AreEqual(291, LifeBarRules.HoverFill(100, 291), "the whole width gets no extra pixel");
        Assert.AreEqual(291, LifeBarRules.HoverFill(250, 291), "clamped to 100 %");
        Assert.AreEqual(100, LifeBarRules.HoverFill(99, 100), "99 + 1");
    }

    [TestMethod]
    public void A_hovered_bar_reads_current_over_maximum_for_the_pc_side_and_a_percent_otherwise()
    {
        Assert.AreEqual("12/30", LifeBarRules.HoverText(12, 30, pcSide: true));
        Assert.AreEqual("40%", LifeBarRules.HoverText(12, 30, pcSide: false));
        Assert.AreEqual("-3%", LifeBarRules.HoverText(-1, 30, pcSide: false), "the text keeps the unclamped percent");
        Assert.AreEqual("0%", LifeBarRules.HoverText(4, 0, pcSide: false), "a zero maximum: the technical fix");
    }

    [TestMethod]
    public void Portrait_bars_crop_their_art_to_the_percent_and_tint_below_twenty()
    {
        Assert.AreEqual(0, LifeBarRules.PortraitFill(0, 34));
        Assert.AreEqual(0, LifeBarRules.PortraitFill(-10, 34));
        Assert.AreEqual(16, LifeBarRules.PortraitFill(50, 33), "16.5 → 16, no extra pixel");
        Assert.AreEqual(34, LifeBarRules.PortraitFill(140, 34), "capped at 100 %");
        Assert.IsTrue(LifeBarRules.PortraitTinted(5, 26), "19 % is below 20");
        Assert.IsFalse(LifeBarRules.PortraitTinted(26, 130), "exactly 20 % is not");
        Assert.IsTrue(LifeBarRules.PortraitTinted(-2, 30), "a dead follower too");
        Assert.IsTrue(LifeBarRules.PortraitTinted(3, 0), "a zero maximum reads 0 %");
    }

    [TestMethod]
    public void GaugeArtPlacesTheLipAboveTheFillLine()
    {
        Assert.AreEqual((88, 0, 0, 0), LifeBarRules.GaugeArt(0, 88), "empty: the empty art only");
        Assert.AreEqual((48, 40, 0, 48), LifeBarRules.GaugeArt(40, 88), "the art's top 8 rows over the empty part, down to the bottom");
        Assert.AreEqual((8, 0, 0, 88), LifeBarRules.GaugeArt(80, 88), "the lip just reaches the top");
        Assert.AreEqual((3, 0, 5, 88), LifeBarRules.GaugeArt(85, 88), "the lip is clipped at the top: art rows from 5");
        Assert.AreEqual((0, 0, 8, 88), LifeBarRules.GaugeArt(88, 88), "full: art rows 8 to 95");
        Assert.AreEqual((0, 0, 8, 88), LifeBarRules.GaugeArt(120, 88), "clamped to the gauge");
        Assert.AreEqual((88, 0, 0, 0), LifeBarRules.GaugeArt(-4, 88));
    }

    [TestMethod]
    public void CompactFillCountsTheDamagePercentFromTheTop()
    {
        Assert.AreEqual((1, 100), LifeBarRules.CompactFill(30, 30, false), "no damage: rows 1 to 100");
        Assert.AreEqual((34, 67), LifeBarRules.CompactFill(2, 3, false), "damage 1 of 3 = 33 %: from row 34, not the life percent's 66");
        Assert.AreEqual((100, 1), LifeBarRules.CompactFill(1, 100, false), "99 % damage: one row");
        Assert.AreEqual((100, 0), LifeBarRules.CompactFill(0, 30, false), "dead: nothing, the number at row 100");
        Assert.AreEqual((101, 0), LifeBarRules.CompactFill(0, 30, true), "the fatigue column caps at 101");
        Assert.AreEqual((100, 0), LifeBarRules.CompactFill(-5, 30, false), "below 0 too");
        Assert.AreEqual((100, 0), LifeBarRules.CompactFill(7, 0, false), "a zero maximum (a crash in the original) reads fully damaged");
        Assert.AreEqual((-9, 110), LifeBarRules.CompactFill(33, 30, false), "above the maximum the fill starts above the panel");
    }

    [TestMethod]
    public void CompactNumberIsTwoDigitPadded()
    {
        Assert.AreEqual("07", LifeBarRules.CompactNumber(7));
        Assert.AreEqual("00", LifeBarRules.CompactNumber(0));
        Assert.AreEqual("123", LifeBarRules.CompactNumber(123));
        Assert.AreEqual("-5", LifeBarRules.CompactNumber(-5), "the sign counts in the width");
        Assert.AreEqual("-2147483648", LifeBarRules.CompactNumber(int.MinValue));
    }
}
