using Arcanum.Presentation.Steamwoven;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

// ARC-UI-2450, ARC-UI-2926: the frame, the scale and the baked step of the Steamwoven layout (the column; the unfitted column mode, the 16:10 to 16:9 frame of the TRD's reference table) and the strips' bands.
[TestClass]
public sealed class SteamwovenLayoutTests
{
    // Window, frame, f, step, run-time scale, horizontal bar, vertical bar (TRD "Screen frame and scale").
    private static readonly (int W, int H, int FW, int FH, double F, SteamwovenStep Step, double Run, int BarX, int BarY)[] Table =
    [
        (3840, 2160, 3840, 2160, 1.0000, SteamwovenStep.One, 1.000, 0, 0),
        (2560, 1440, 2560, 1440, 0.6667, SteamwovenStep.TwoThirds, 1.000, 0, 0),
        (1920, 1080, 1920, 1080, 0.5000, SteamwovenStep.Half, 1.000, 0, 0),
        (1280, 720, 1280, 720, 0.3333, SteamwovenStep.Third, 1.000, 0, 0),
        (3840, 2400, 3840, 2400, 1.0000, SteamwovenStep.One, 1.000, 0, 0),
        (2560, 1600, 2560, 1600, 0.6667, SteamwovenStep.TwoThirds, 1.000, 0, 0),
        (1920, 1200, 1920, 1200, 0.5000, SteamwovenStep.Half, 1.000, 0, 0),
        (1280, 800, 1280, 800, 0.3333, SteamwovenStep.Third, 1.000, 0, 0),
        (3024, 1964, 3024, 1890, 0.7875, SteamwovenStep.One, 0.787, 0, 37),
        (3456, 2234, 3456, 2160, 0.9000, SteamwovenStep.One, 0.900, 0, 37),
        (2880, 1800, 2880, 1800, 0.7500, SteamwovenStep.One, 0.750, 0, 0),
        (1366, 768, 1366, 768, 0.3556, SteamwovenStep.Half, 0.711, 0, 0),
        (1600, 900, 1600, 900, 0.4167, SteamwovenStep.Half, 0.833, 0, 0),
        (1680, 945, 1680, 945, 0.4375, SteamwovenStep.Half, 0.875, 0, 0),
        (3440, 1440, 2560, 1440, 0.6667, SteamwovenStep.TwoThirds, 1.000, 440, 0),
        (5120, 1440, 2560, 1440, 0.6667, SteamwovenStep.TwoThirds, 1.000, 1280, 0),
        (1024, 768, 1024, 640, 0.2667, SteamwovenStep.Third, 0.800, 0, 64),
        (5120, 2880, 5120, 2880, 1.3333, SteamwovenStep.One, 1.333, 0, 0),
    ];

    /// <summary>The unfitted column (the original's strips at k = 1): the frame rules of the TRD's reference table.</summary>
    private static SteamwovenLayout Layout(int w, int h, SteamwovenLayoutOptions? options = null)
    {
        var result = SteamwovenLayoutBuilder.Compute(w, h, options ?? SteamwovenLayoutOptions.ColumnOriginal);
        Assert.IsTrue(result.IsOk, $"{w}x{h}: {result.Failure}");
        return result.Layout!;
    }

    [TestMethod]
    public void TheTrdReferenceTableIsReproducedExactly()
    {
        foreach (var r in Table)
        {
            var l = Layout(r.W, r.H);
            var tag = $"{r.W}x{r.H}";
            Assert.AreEqual(r.FW, l.Frame.Width, tag + " frame width");
            Assert.AreEqual(r.FH, l.Frame.Height, tag + " frame height");
            Assert.AreEqual(r.F, l.Scale.ToDouble(), 5e-5, tag + " f");
            Assert.AreEqual(r.Step, l.Step, tag + " step");
            Assert.AreEqual(r.Run, l.RuntimeScale.ToDouble(), 5e-4, tag + " run-time scale");
            Assert.AreEqual(r.BarX, l.Bars.Left, tag + " bar left");
            Assert.AreEqual(r.BarX, l.Bars.Right, tag + " bar right");
            Assert.AreEqual(r.BarY, l.Bars.Top, tag + " bar top");
            Assert.AreEqual(r.BarY, l.Bars.Bottom, tag + " bar bottom");
        }
    }

    [TestMethod]
    public void TheBandsTileTheFrameAndTheColumnLiesInThem()
    {
        foreach (var options in new[] { SteamwovenLayoutOptions.ColumnOriginal, SteamwovenLayoutOptions.ColumnFitted })
            foreach (var r in Table)
            {
                var l = Layout(r.W, r.H, options);
                var tag = $"{r.W}x{r.H} fit={options.Column.FitHeight}";
                Assert.AreEqual(l.Frame.Y, l.Top.Y, tag);
                Assert.AreEqual(l.Top.Bottom, l.Middle.Y, tag);
                Assert.AreEqual(l.Middle.Bottom, l.Bottom.Y, tag);
                Assert.AreEqual(l.Frame.Bottom, l.Bottom.Bottom, tag);
                Assert.IsTrue(l.Top.Width == l.Frame.Width && l.Middle.Width == l.Frame.Width && l.Bottom.Width == l.Frame.Width, tag);
                Assert.AreEqual(l.Top.Y, l.Column.TopStrip.Y, tag);
                Assert.AreEqual(l.Top.Height, l.Column.TopStrip.Height, tag);
                Assert.AreEqual(l.Bottom.Height, l.Column.BottomStrip.Height, tag);
            }
    }

    [TestMethod]
    public void AnAspectFrom16To10To16To9IsLaidOutWholeAndOthersGetBars()
    {
        Assert.AreEqual(new PixelRect(0, 0, 1280, 800), Layout(1280, 800).Frame);
        Assert.AreEqual(new PixelRect(0, 0, 1280, 720), Layout(1280, 720).Frame);
        Assert.AreEqual(new PixelRect(0, 64, 1024, 640), Layout(1024, 768).Frame, "4:3 letterbox");
        Assert.AreEqual(new PixelRect(0, 20, 1280, 800), Layout(1280, 840).Frame, "taller than 16:10 by 40 px: bars of 20");
        Assert.AreEqual(new PixelRect(0, 0, 1280, 805), Layout(1280, 805).Frame, "5 px extra: under 4 px a side, dropped");
        Assert.AreEqual(new PixelRect(0, 0, 1280, 807), Layout(1280, 807).Frame, "7 px extra: 3.5 per side, dropped");
        Assert.AreEqual(new PixelRect(0, 4, 1280, 800), Layout(1280, 808).Frame, "8 px extra: 4 per side, kept");
        var pillar = Layout(3440, 1440);
        Assert.AreEqual(new PixelRect(440, 0, 2560, 1440), pillar.Frame);
        Assert.AreEqual(new PixelInsets(440, 0, 440, 0), pillar.Bars);
        Assert.AreEqual(new PixelRect(1280, 0, 2560, 1440), Layout(5120, 1440).Frame);
        Assert.AreEqual(new PixelRect(0, 0, 1366, 768), Layout(1366, 768).Frame, "a bar of 0.5 px is dropped; the sides take the difference");
        Assert.AreEqual(new PixelRect(0, 0, 1287, 720), Layout(1287, 720).Frame, "7 px extra: dropped");
        Assert.AreEqual(new PixelRect(4, 0, 1280, 720), Layout(1288, 720).Frame, "8 px extra: kept");
        // An odd difference gives the left bar the larger half (a half goes up).
        var odd = Layout(1289, 720);
        Assert.AreEqual(new PixelInsets(5, 0, 4, 0), odd.Bars);
    }

    [TestMethod]
    public void TheStepIsTheSmallestOneAtLeastFAndTheTestForAnExactStepIsExact()
    {
        var third = Layout(1280, 720);
        Assert.AreEqual(SteamwovenStep.Third, third.Step);
        Assert.IsTrue(third.ExactStep);
        Assert.AreEqual(Ratio.One, third.RuntimeScale);

        // 1281 x 720 is 1 px too wide: f = 720 / 2160 = 1/3 exactly, so the exact test holds with a dropped bar.
        var wide = Layout(1281, 720);
        Assert.AreEqual(new Ratio(1, 3), wide.Scale);
        Assert.IsTrue(wide.ExactStep);

        // 1281 x 722 is f = 1281 / 3840 > 1/3: the next step up, filtered.
        var above = Layout(1281, 722);
        Assert.AreEqual(SteamwovenStep.Half, above.Step);
        Assert.IsFalse(above.ExactStep);

        Assert.IsTrue(Layout(3840, 2160).ExactStep);
        Assert.IsTrue(Layout(2560, 1440).ExactStep, "f = 2/3 exactly, not 0.6667");
        Assert.IsTrue(Layout(1920, 1200).ExactStep);
        Assert.IsFalse(Layout(1366, 768).ExactStep);
        var big = Layout(5120, 2880);
        Assert.AreEqual(SteamwovenStep.One, big.Step, "above x1 the x1 set is scaled up");
        Assert.AreEqual(new Ratio(4, 3), big.RuntimeScale);
        Assert.IsFalse(big.ExactStep);
    }

    [TestMethod]
    public void BelowAQuarterTheInterfaceIsNotSupportedAndInvalidInputIsATypedFailure()
    {
        var edge = Layout(960, 540);
        Assert.AreEqual(new Ratio(1, 4), edge.Scale);
        Assert.AreEqual(SteamwovenStep.Third, edge.Step);
        Assert.AreEqual(new Ratio(3, 4), edge.RuntimeScale);

        Assert.AreEqual(SteamwovenLayoutFailure.ScaleBelowSupport, SteamwovenLayoutBuilder.Compute(959, 540, SteamwovenLayoutOptions.ColumnOriginal).Failure);
        Assert.AreEqual(SteamwovenLayoutFailure.ScaleBelowSupport, SteamwovenLayoutBuilder.Compute(960, 539, SteamwovenLayoutOptions.ColumnOriginal).Failure);
        Assert.AreEqual(SteamwovenLayoutFailure.ScaleBelowSupport, SteamwovenLayoutBuilder.Compute(640, 480, SteamwovenLayoutOptions.ColumnOriginal).Failure);
        Assert.IsNull(SteamwovenLayoutBuilder.Compute(959, 540, SteamwovenLayoutOptions.ColumnOriginal).Layout);
        Assert.AreEqual(SteamwovenLayoutFailure.InvalidSize, SteamwovenLayoutBuilder.Compute(0, 1080).Failure);
        Assert.AreEqual(SteamwovenLayoutFailure.InvalidSize, SteamwovenLayoutBuilder.Compute(1920, -1).Failure);
        Assert.AreEqual(SteamwovenLayoutFailure.InvalidSize, SteamwovenLayoutBuilder.Compute(SteamwovenGrid.MaxWindowSide + 1, 1080).Failure);
        Assert.IsTrue(SteamwovenLayoutBuilder.Compute(SteamwovenGrid.MaxWindowSide, SteamwovenGrid.MaxWindowSide).IsOk);
    }

    [TestMethod]
    public void TheStripHeightsComeFromTheColumnModeThatMustFollowTheGrid()
    {
        var l = Layout(3840, 2160);
        Assert.AreEqual(150, l.Top.Height);
        Assert.AreEqual(570, l.Bottom.Height);
        Assert.AreEqual(1440, l.Middle.Height);
        Assert.AreEqual((150, 570), (SteamwovenLayoutOptions.ColumnOriginal.TopPanelMaster, SteamwovenLayoutOptions.ColumnOriginal.BottomPanelMaster));

        Assert.IsTrue(SteamwovenLayoutOptions.Default.IsValid);
        Assert.AreEqual(SteamwovenLayoutOptions.ColumnFitted, SteamwovenLayoutOptions.Default, "the layout the game uses is the fitted column");
        Assert.IsTrue(SteamwovenLayoutOptions.ColumnOriginal.IsValid && SteamwovenLayoutOptions.ColumnFitted.IsValid);
        foreach (var bad in new[]
        {
            new SteamwovenLayoutOptions(new SteamwovenColumnMode(new Ratio(1, 2))),
            new SteamwovenLayoutOptions(new SteamwovenColumnMode(Ratio.One) { WorldDim = new SteamwovenWorldDim(2, 0) }),
            new SteamwovenLayoutOptions(new SteamwovenColumnMode(new Ratio(4, 5)) { FitHeight = true }),
        })
            Assert.AreEqual(SteamwovenLayoutFailure.InvalidOptions, SteamwovenLayoutBuilder.Compute(1920, 1080, bad).Failure);
    }

    [TestMethod]
    public void EveryMasterLengthIsAMultipleOfSixAndEveryBakedStepLengthIsWhole()
    {
        int[] masters =
        [
            SteamwovenGrid.ReferenceWidth, SteamwovenGrid.ReferenceHeight, SteamwovenGrid.TallReferenceHeight, SteamwovenGrid.CentreWidth,
            SteamwovenGrid.WindowZoneHeight, SteamwovenGrid.FullScreenZoneHeight, SteamwovenGrid.StripTopPanel, SteamwovenGrid.StripBottomPanel,
            (SteamwovenGrid.ReferenceWidth - SteamwovenGrid.CentreWidth) / 2,
        ];
        foreach (var master in masters)
        {
            Assert.AreEqual(0, master % SteamwovenGrid.GridPx, $"{master} is a multiple of 6");
            foreach (var step in Enum.GetValues<SteamwovenStep>())
            {
                var scale = SteamwovenGrid.StepScale(step);
                Assert.AreEqual(0, master * scale.Numerator % scale.Denominator, $"{master} at {scale} is a whole number");
                Assert.AreEqual((int)(master * scale.Numerator / scale.Denominator), SteamwovenGrid.BakedLength(master, step));
            }
        }
        Assert.AreEqual(2, SteamwovenGrid.BakedLength(6, SteamwovenStep.Third));
        Assert.AreEqual(3, SteamwovenGrid.BakedLength(6, SteamwovenStep.Half));
        Assert.AreEqual(4, SteamwovenGrid.BakedLength(6, SteamwovenStep.TwoThirds));
        Assert.AreEqual(6, SteamwovenGrid.BakedLength(6, SteamwovenStep.One));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SteamwovenGrid.BakedLength(7, SteamwovenStep.One));
    }

    [TestMethod]
    public void StripHeightsAreRoundedAsSizesHalvesUpAndRatiosBehave()
    {
        // 1968 x 1107: f = 0.5125; the top strip 76.875 rounds up to 77 and the bottom strip 292.125 to 292.
        var l = Layout(1968, 1107);
        Assert.AreEqual(77, l.Top.Height);
        Assert.AreEqual(292, l.Bottom.Height);
        Assert.AreEqual(738, l.Middle.Height);
        // 1680 x 945: f = 0.4375; the bottom strip 249.375 rounds to 249 as a size (its top edge is 945 - 249), the top strip 65.625 to 66.
        var tie = Layout(1680, 945);
        Assert.AreEqual(66, tie.Top.Height);
        Assert.AreEqual(249, tie.Bottom.Height);
        Assert.AreEqual(945 - 249, tie.Bottom.Y);
        Assert.AreEqual(1, new Ratio(1, 2).RoundHalfUp());
        Assert.AreEqual(0, new Ratio(-1, 2).RoundHalfUp());
        Assert.AreEqual(-1, new Ratio(-3, 4).RoundHalfUp());
        Assert.AreEqual(2, new Ratio(3, 2).RoundHalfUp());
        Assert.AreEqual(0, default(Ratio).RoundHalfUp());
        Assert.AreEqual(Ratio.Zero, default(Ratio));
        Assert.AreEqual(new Ratio(2, 3), new Ratio(4, 6));
        Assert.IsTrue(new Ratio(1, 3) < new Ratio(1, 2));
        Assert.AreEqual(new Ratio(1, 2), new Ratio(1, 3) + new Ratio(1, 6));
        Assert.AreEqual(new Ratio(1, 6), new Ratio(1, 2) - new Ratio(1, 3));
        Assert.AreEqual(new Ratio(3, 2), new Ratio(1, 2) / new Ratio(1, 3));
        Assert.AreEqual(new Ratio(-1, 2), new Ratio(1, -2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new Ratio(1, 0));
        Assert.ThrowsExactly<DivideByZeroException>(() => new Ratio(1) / Ratio.Zero);
    }

    [TestMethod]
    public void TheSameSizeAlwaysGivesTheSameLayout()
    {
        foreach (var r in Table) Assert.AreEqual(Layout(r.W, r.H), Layout(r.W, r.H));
        Assert.AreNotEqual(Layout(1920, 1080), Layout(1920, 1200));
    }
}
