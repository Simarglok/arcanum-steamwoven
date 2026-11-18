using Arcanum.Presentation.Steamwoven;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

// ARC-UI-2450 (SWI-LAY-1..7, -10, -11; SWI-BDR-2, -3, -5, -6): the Steamwoven layout model against the TRD reference table.
[TestClass]
public sealed class SteamwovenLayoutTests
{
    // Window, frame, f, step, run-time scale, top, middle, bottom, centre, side, horizontal bar, vertical bar (TRD "Screen frame and scale").
    private static readonly (int W, int H, int FW, int FH, double F, SteamwovenStep Step, double Run, int Top, int Middle, int Bottom, int Centre, int Side, int BarX, int BarY)[] Table =
    [
        (3840, 2160, 3840, 2160, 1.0000, SteamwovenStep.One, 1.000, 120, 1584, 456, 2880, 480, 0, 0),
        (2560, 1440, 2560, 1440, 0.6667, SteamwovenStep.TwoThirds, 1.000, 80, 1056, 304, 1920, 320, 0, 0),
        (1920, 1080, 1920, 1080, 0.5000, SteamwovenStep.Half, 1.000, 60, 792, 228, 1440, 240, 0, 0),
        (1280, 720, 1280, 720, 0.3333, SteamwovenStep.Third, 1.000, 40, 528, 152, 960, 160, 0, 0),
        (3840, 2400, 3840, 2400, 1.0000, SteamwovenStep.One, 1.000, 120, 1824, 456, 2880, 480, 0, 0),
        (2560, 1600, 2560, 1600, 0.6667, SteamwovenStep.TwoThirds, 1.000, 80, 1216, 304, 1920, 320, 0, 0),
        (1920, 1200, 1920, 1200, 0.5000, SteamwovenStep.Half, 1.000, 60, 912, 228, 1440, 240, 0, 0),
        (1280, 800, 1280, 800, 0.3333, SteamwovenStep.Third, 1.000, 40, 608, 152, 960, 160, 0, 0),
        (3024, 1964, 3024, 1890, 0.7875, SteamwovenStep.One, 0.787, 95, 1436, 359, 2268, 378, 0, 37),
        (3456, 2234, 3456, 2160, 0.9000, SteamwovenStep.One, 0.900, 108, 1642, 410, 2592, 432, 0, 37),
        (2880, 1800, 2880, 1800, 0.7500, SteamwovenStep.One, 0.750, 90, 1368, 342, 2160, 360, 0, 0),
        (1366, 768, 1366, 768, 0.3556, SteamwovenStep.Half, 0.711, 43, 563, 162, 1024, 171, 0, 0),
        (1600, 900, 1600, 900, 0.4167, SteamwovenStep.Half, 0.833, 50, 660, 190, 1200, 200, 0, 0),
        (1680, 945, 1680, 945, 0.4375, SteamwovenStep.Half, 0.875, 53, 692, 200, 1260, 210, 0, 0),
        (3440, 1440, 2560, 1440, 0.6667, SteamwovenStep.TwoThirds, 1.000, 80, 1056, 304, 1920, 320, 440, 0),
        (5120, 1440, 2560, 1440, 0.6667, SteamwovenStep.TwoThirds, 1.000, 80, 1056, 304, 1920, 320, 1280, 0),
        (1024, 768, 1024, 640, 0.2667, SteamwovenStep.Third, 0.800, 32, 486, 122, 768, 128, 0, 64),
        (5120, 2880, 5120, 2880, 1.3333, SteamwovenStep.One, 1.333, 160, 2112, 608, 3840, 640, 0, 0),
    ];

    private static SteamwovenLayout Layout(int w, int h, SteamwovenLayoutOptions? options = null)
    {
        var result = SteamwovenLayoutBuilder.Compute(w, h, options);
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
            Assert.AreEqual(r.Top, l.Top.Band.Height, tag + " top");
            Assert.AreEqual(r.Middle, l.Middle.Band.Height, tag + " middle");
            Assert.AreEqual(r.Bottom, l.Bottom.Band.Height, tag + " bottom");
            Assert.AreEqual(r.Centre, l.Middle.Centre.Width, tag + " centre");
            Assert.AreEqual(r.Side, l.Middle.LeftSide.Width, tag + " left side");
            Assert.AreEqual(r.Side, l.Middle.RightSide.Width, tag + " right side");
            Assert.AreEqual(r.BarX, l.Bars.Left, tag + " bar left");
            Assert.AreEqual(r.BarX, l.Bars.Right, tag + " bar right");
            Assert.AreEqual(r.BarY, l.Bars.Top, tag + " bar top");
            Assert.AreEqual(r.BarY, l.Bars.Bottom, tag + " bar bottom");
        }
    }

    [TestMethod]
    public void BandsTileTheFrameAndCentreAndSidesTileEachBand()
    {
        foreach (var r in Table)
        {
            var l = Layout(r.W, r.H);
            var tag = $"{r.W}x{r.H}";
            Assert.AreEqual(l.Frame.Y, l.Top.Band.Y, tag);
            Assert.AreEqual(l.Top.Band.Bottom, l.Middle.Band.Y, tag);
            Assert.AreEqual(l.Middle.Band.Bottom, l.Bottom.Band.Y, tag);
            Assert.AreEqual(l.Frame.Bottom, l.Bottom.Band.Bottom, tag);
            Assert.IsTrue(l.Frame.X >= 0 && l.Frame.Right <= r.W && l.Frame.Y >= 0 && l.Frame.Bottom <= r.H, tag + " frame inside the window");
            foreach (var band in new[] { l.Top, l.Middle, l.Bottom })
            {
                Assert.AreEqual(band.Band.X, band.LeftSide.X, tag);
                Assert.AreEqual(band.LeftSide.Right, band.Centre.X, tag);
                Assert.AreEqual(band.Centre.Right, band.RightSide.X, tag);
                Assert.AreEqual(band.Band.Right, band.RightSide.Right, tag);
                Assert.AreEqual(band.Band.Height, band.Centre.Height, tag);
            }
        }
    }

    [TestMethod]
    public void TheWindowZoneIsTheCentreBlockCentredInTheMiddleBandAndStaysInsideIt()
    {
        foreach (var r in Table)
        {
            var l = Layout(r.W, r.H);
            var tag = $"{r.W}x{r.H}";
            Assert.AreEqual(l.Middle.Centre.X, l.WindowZone.X, tag);
            Assert.AreEqual(l.Middle.Centre.Width, l.WindowZone.Width, tag);
            Assert.IsTrue(l.WindowZone.Y >= l.Middle.Band.Y && l.WindowZone.Bottom <= l.Middle.Band.Bottom, tag + " zone inside the middle band");
            var exact = (int)Math.Round(1440 * l.Scale.ToDouble(), MidpointRounding.AwayFromZero);
            Assert.IsTrue(Math.Abs(l.WindowZone.Height - exact) <= 1, $"{tag} zone height {l.WindowZone.Height} vs {exact}");
            Assert.AreEqual(l.WindowZone.Y - l.Middle.Band.Y, l.Middle.Band.Bottom - l.WindowZone.Bottom, 1, tag + " centred vertically");
        }
        var wide = Layout(1920, 1080);
        Assert.AreEqual(new PixelRect(240, 96, 1440, 720), wide.WindowZone, "on a 16:9 frame the zone is the centre block, 36 px above and below in the 792 px band");
        var tall = Layout(1920, 1200);
        Assert.AreEqual(new PixelRect(240, 156, 1440, 720), tall.WindowZone, "on 16:10 the zone has 96 px above and below");
    }

    [TestMethod]
    public void TheMiddleBackdropIsTheCentredCropOfTheMasterOrTheMasterScaledToCover()
    {
        var native = Layout(3840, 2400).MiddleBackdrop;
        Assert.AreEqual(new PixelRect(0, 120, 3840, 1824), native.Target);
        Assert.AreEqual(native.Target, native.Master, "16:10 at x1: the master exactly");
        Assert.AreEqual(Ratio.One, native.Scale);

        var sixteenNine = Layout(3840, 2160).MiddleBackdrop;
        Assert.AreEqual(new PixelRect(0, 120, 3840, 1584), sixteenNine.Target);
        Assert.AreEqual(new PixelRect(0, 0, 3840, 1824), sixteenNine.Master, "16:9: the centred crop, 120 px off the top and bottom");

        var small = Layout(1920, 1080).MiddleBackdrop;
        Assert.AreEqual(new PixelRect(0, 0, 1920, 912), small.Master);
        Assert.AreEqual(new Ratio(1, 2), small.Scale);

        // 1366 x 768: the frame is 1 px wider than 3840 f; the master is scaled up to cover, centred.
        var dropped = Layout(1366, 768).MiddleBackdrop;
        Assert.AreEqual(new Ratio(1366, 3840), dropped.Scale);
        Assert.IsTrue(dropped.Master.X <= dropped.Target.X && dropped.Master.Right >= dropped.Target.Right);
        Assert.IsTrue(dropped.Master.Y <= dropped.Target.Y && dropped.Master.Bottom >= dropped.Target.Bottom);
    }

    [TestMethod]
    public void TheFullScreenBackdropCoversTheWholeFrameAndItsUnitZoneIsCentred()
    {
        var full = Layout(3840, 2400).FullScreenBackdrop;
        Assert.AreEqual(new PixelRect(0, 0, 3840, 2400), full.Target);
        Assert.AreEqual(full.Target, full.Master);
        Assert.AreEqual(new PixelRect(480, 120, 2880, 2160), full.Zone);

        var sixteenNine = Layout(3840, 2160).FullScreenBackdrop;
        Assert.AreEqual(new PixelRect(0, -120, 3840, 2400), sixteenNine.Master, "a shorter frame shows the centred crop");
        Assert.AreEqual(new PixelRect(480, 0, 2880, 2160), sixteenNine.Zone, "800 x 600 units fill a 16:9 frame's height");

        var letterbox = Layout(1024, 768).FullScreenBackdrop;
        Assert.AreEqual(new PixelRect(0, 64, 1024, 640), letterbox.Target, "the black bars are outside the backdrop");
        Assert.AreEqual(letterbox.Target, letterbox.Master);
        Assert.AreEqual(new PixelRect(128, 96, 768, 576), letterbox.Zone);

        foreach (var r in Table)
        {
            var l = Layout(r.W, r.H);
            var b = l.FullScreenBackdrop;
            var tag = $"{r.W}x{r.H}";
            Assert.AreEqual(l.Frame, b.Target, tag);
            Assert.IsTrue(b.Master.X <= l.Frame.X && b.Master.Right >= l.Frame.Right && b.Master.Y <= l.Frame.Y && b.Master.Bottom >= l.Frame.Bottom, tag + " covers the frame");
            Assert.IsTrue(b.Zone.X >= l.Frame.X && b.Zone.Right <= l.Frame.Right && b.Zone.Y >= l.Frame.Y && b.Zone.Bottom <= l.Frame.Bottom, tag + " zone in the frame");
        }
    }

    [TestMethod]
    public void ATwoPartWindowSplitsAt1290MasterPixelsFromTheCentreBlocksLeftEdge()
    {
        var l = Layout(3840, 2160);
        Assert.AreEqual(480 + 1290, l.Split.SplitX);
        Assert.AreEqual(new PixelRect(0, 120, 1770, 1584), l.Split.Left, "the left material runs from the frame's left edge");
        Assert.AreEqual(new PixelRect(1770, 120, 2070, 1584), l.Split.Right, "the right material to the frame's right edge");

        var half = Layout(1920, 1080);
        Assert.AreEqual(240 + 645, half.Split.SplitX);

        var bars = Layout(3440, 1440);
        Assert.AreEqual(440 + 320 + 860, bars.Split.SplitX, "the split follows the frame, not the window");
        Assert.AreEqual(440, bars.Split.Left.X);
        Assert.AreEqual(3000, bars.Split.Right.Right);

        foreach (var r in Table)
        {
            var s = Layout(r.W, r.H);
            var fromCentre = s.Split.SplitX - s.Middle.Centre.X;
            Assert.AreEqual(1290 * s.Scale.ToDouble(), fromCentre, 1.0, $"{r.W}x{r.H}");
            Assert.AreEqual(s.Split.Left.Right, s.Split.Right.X);
            Assert.AreEqual(s.Frame.Right, s.Split.Right.Right);
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

        Assert.AreEqual(SteamwovenLayoutFailure.ScaleBelowSupport, SteamwovenLayoutBuilder.Compute(959, 540).Failure);
        Assert.AreEqual(SteamwovenLayoutFailure.ScaleBelowSupport, SteamwovenLayoutBuilder.Compute(960, 539).Failure);
        Assert.AreEqual(SteamwovenLayoutFailure.ScaleBelowSupport, SteamwovenLayoutBuilder.Compute(640, 480).Failure);
        Assert.IsNull(SteamwovenLayoutBuilder.Compute(959, 540).Layout);
        Assert.AreEqual(SteamwovenLayoutFailure.InvalidSize, SteamwovenLayoutBuilder.Compute(0, 1080).Failure);
        Assert.AreEqual(SteamwovenLayoutFailure.InvalidSize, SteamwovenLayoutBuilder.Compute(1920, -1).Failure);
        Assert.AreEqual(SteamwovenLayoutFailure.InvalidSize, SteamwovenLayoutBuilder.Compute(SteamwovenGrid.MaxWindowSide + 1, 1080).Failure);
        Assert.IsTrue(SteamwovenLayoutBuilder.Compute(SteamwovenGrid.MaxWindowSide, SteamwovenGrid.MaxWindowSide).IsOk);
    }

    [TestMethod]
    public void TheBandHeightsComeFromOneOptionsObjectThatMustFollowTheGrid()
    {
        var other = new SteamwovenLayoutOptions { TopPanelMaster = 180, BottomPanelMaster = 540 };
        var l = Layout(3840, 2160, other);
        Assert.AreEqual(180, l.Top.Band.Height);
        Assert.AreEqual(540, l.Bottom.Band.Height);
        Assert.AreEqual(1440, l.Middle.Band.Height);

        foreach (var bad in new[]
        {
            new SteamwovenLayoutOptions { TopPanelMaster = 0 },
            new SteamwovenLayoutOptions { BottomPanelMaster = -6 },
            new SteamwovenLayoutOptions { TopPanelMaster = 151 },
            new SteamwovenLayoutOptions { BottomPanelMaster = 571 },
            new SteamwovenLayoutOptions { TopPanelMaster = 270 },
            new SteamwovenLayoutOptions { TopPanelMaster = 2147483640, BottomPanelMaster = 12 },
            new SteamwovenLayoutOptions { TopPanelMaster = 720, BottomPanelMaster = 6 },
        })
            Assert.AreEqual(SteamwovenLayoutFailure.InvalidOptions, SteamwovenLayoutBuilder.Compute(1920, 1080, bad).Failure, $"{bad.TopPanelMaster}/{bad.BottomPanelMaster}");
        Assert.IsTrue(SteamwovenLayoutOptions.Default.IsValid);
        Assert.IsTrue(new SteamwovenLayoutOptions { TopPanelMaster = 360, BottomPanelMaster = 360 }.IsValid, "together exactly 720");
    }

    [TestMethod]
    public void EveryMasterLengthIsAMultipleOfSixAndEveryBakedStepLengthIsWhole()
    {
        int[] masters =
        [
            SteamwovenGrid.ReferenceWidth, SteamwovenGrid.ReferenceHeight, SteamwovenGrid.TallReferenceHeight, SteamwovenGrid.CentreWidth,
            SteamwovenGrid.WindowZoneHeight, SteamwovenLayoutOptions.Default.BackdropHeight, SteamwovenGrid.FullScreenHeight, SteamwovenGrid.FullScreenZoneHeight,
            SteamwovenGrid.SplitFromCentreLeft, SteamwovenGrid.DefaultTopPanel, SteamwovenGrid.DefaultBottomPanel,
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
    public void OriginalUnitOffsetsBecomeMasterPixelsSnappedToTheGridWithAShiftOfAtMostThreePixels()
    {
        Assert.AreEqual(0, SteamwovenGrid.UnitsToMaster(0));
        Assert.AreEqual(2880, SteamwovenGrid.UnitsToMaster(800));
        Assert.AreEqual(1290, SteamwovenGrid.UnitsToMaster(358), "358 x 3.6 = 1288.8");
        Assert.AreEqual(150, SteamwovenGrid.UnitsToMaster(41), "the original top panel, 147.6");
        Assert.AreEqual(576, SteamwovenGrid.UnitsToMaster(160), "160 x 3.6 = 576 is already on the grid");
        for (var units = 0; units <= 800; units++)
        {
            var master = SteamwovenGrid.UnitsToMaster(units);
            Assert.AreEqual(0, master % 6, $"{units}");
            Assert.IsTrue(Math.Abs(master - units * 3.6) <= 3.0 + 1e-9, $"{units}: shift {master - units * 3.6}");
        }
    }

    [TestMethod]
    public void PanelHeightsAreRoundedAsSizesHalvesUpAndRatiosBehave()
    {
        // 1968 x 1107: f = 0.5125; the top panel 61.5 rounds up to 62 and the bottom panel 233.7 to 234.
        var l = Layout(1968, 1107);
        Assert.AreEqual(62, l.Top.Band.Height);
        Assert.AreEqual(234, l.Bottom.Band.Height);
        Assert.AreEqual(811, l.Middle.Band.Height);
        // 1680 x 945: f = 0.4375; the bottom panel 199.5 rounds up as a size (200; rounding its top edge would give 199), the top panel 52.5 to 53.
        var tie = Layout(1680, 945);
        Assert.AreEqual(53, tie.Top.Band.Height);
        Assert.AreEqual(200, tie.Bottom.Band.Height);
        Assert.AreEqual(945 - 200, tie.Bottom.Band.Y);
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
        // DeviceX: round(frame left + position · f).
        Assert.AreEqual(440 + 667, Layout(3440, 1440).DeviceX(1000));
    }

    [TestMethod]
    public void TheSameSizeAlwaysGivesTheSameLayout()
    {
        foreach (var r in Table) Assert.AreEqual(Layout(r.W, r.H), Layout(r.W, r.H));
        Assert.AreNotEqual(Layout(1920, 1080), Layout(1920, 1200));
    }

    [TestMethod]
    public void ALetterboxedFrameWithADroppedBarGetsTheBackdropScaledToCoverAndTheFramePositionsAreExact()
    {
        // 1280 x 807: 7 px over 16:10, the bar is dropped; the middle band (807 - 40 - 152 = 615) is 7 px taller than 1824 f = 608.
        var l = Layout(1280, 807);
        Assert.AreEqual(new PixelRect(0, 0, 1280, 807), l.Frame);
        Assert.AreEqual(615, l.Middle.Band.Height);
        Assert.AreEqual(new Ratio(615, 1824), l.MiddleBackdrop.Scale);
        Assert.AreEqual(new PixelRect(0, 40, 1280, 615), l.MiddleBackdrop.Target);
        Assert.AreEqual(new PixelRect(-7, 40, 1294, 615), l.MiddleBackdrop.Master, "3840 x 1824 at 615/1824 is 1294.7 x 615, centred: 7 px off the left, 7 off the right");
        Assert.AreEqual(new PixelRect(0, 0, 1280, 807), l.FullScreenBackdrop.Target);
        Assert.IsTrue(l.FullScreenBackdrop.Scale > l.Scale, "a frame taller than 16:10 scales the full-screen master up");

        // Frame and centre positions of the rows with bars.
        var pillar = Layout(3440, 1440);
        Assert.AreEqual(new PixelRect(760, 0, 1920, 80), pillar.Top.Centre, "x of the centre block: bar + side");
        Assert.AreEqual(new PixelRect(440, 80, 2560, 1056), pillar.Middle.Band);
        Assert.AreEqual(new PixelRect(760, 80, 1920, 1056), pillar.Middle.Centre);
        Assert.AreEqual(new PixelRect(1280 + 320, 80, 1920, 1056), Layout(5120, 1440).Middle.Centre);
        Assert.AreEqual(new PixelRect(0, 37, 3024, 95), Layout(3024, 1964).Top.Band);
        Assert.AreEqual(new PixelRect(378, 37 + 95, 2268, 1436), Layout(3024, 1964).Middle.Centre);
        Assert.AreEqual(new PixelRect(0, 64 + 32, 1024, 486), Layout(1024, 768).Middle.Band);
    }

    // ARC-UI-2526 (SWI-LAY-11): the centre block's controls for the 120 / 456 bands, in master px from the centre block's left edge and the band's top edge.
    private static readonly (SteamwovenControlId Id, int X, int Y, int W, int H)[] DefaultControls =
    [
        (SteamwovenControlId.Character, 12, 12, 96, 96), (SteamwovenControlId.Logbook, 150, 12, 96, 96), (SteamwovenControlId.Map, 282, 12, 96, 96), (SteamwovenControlId.Inventory, 414, 12, 96, 96),
        (SteamwovenControlId.FatePoints, 564, 24, 72, 72), (SteamwovenControlId.Sleep, 2178, 24, 72, 72), (SteamwovenControlId.Clock, 2334, 18, 462, 84), (SteamwovenControlId.ExperienceStrip, 762, 108, 1380, 48),
        (SteamwovenControlId.Combat, 312, 168, 120, 120), (SteamwovenControlId.Skills, 2496, 18, 120, 120), (SteamwovenControlId.Spells, 2334, 168, 120, 120), (SteamwovenControlId.Schematics, 2496, 318, 120, 120),
        (SteamwovenControlId.HealthVial, 48, 66, 102, 318), (SteamwovenControlId.FatigueVial, 2712, 66, 102, 318), (SteamwovenControlId.MessageWindow, 762, 78, 1380, 294), (SteamwovenControlId.HoverImage, 786, 102, 228, 228),
    ];

    [TestMethod]
    public void TheControlsOfTheDefaultPanelsSitAtTheTrdMasterRectangles()
    {
        var l = Layout(3840, 2160);
        Assert.AreEqual(DefaultControls.Length, l.Controls.Count);
        foreach (var (id, x, y, w, h) in DefaultControls)
        {
            var control = l.Control(id);
            var band = control.Spec.Band == SteamwovenHudBand.Top ? l.Top : l.Bottom;
            Assert.AreEqual(new SteamwovenControlSpec(id, control.Spec.Band, x, y, w, h), control.Spec, id.ToString());
            // At f = 1 the device rectangle is the master one from the centre block and the band.
            Assert.AreEqual(new PixelRect(band.Centre.X + x, band.Band.Y + y, w, h), control.Device, id.ToString());
            Assert.IsTrue(x % 6 == 0 && y % 6 == 0 && w % 6 == 0 && h % 6 == 0, id + " on the 6 px grid");
        }
    }

    [TestMethod]
    public void TheControlsStayInsideTheirBandAndTheCentreBlockAndDoNotOverlapInABand()
    {
        foreach (var options in new[] { SteamwovenLayoutOptions.Default, new SteamwovenLayoutOptions { TopPanelMaster = 150, BottomPanelMaster = 570 }, new SteamwovenLayoutOptions { TopPanelMaster = 90, BottomPanelMaster = 420 } })
        {
            var specs = SteamwovenControls.For(options.TopPanelMaster, options.BottomPanelMaster);
            foreach (var spec in specs)
            {
                var band = spec.Band == SteamwovenHudBand.Top ? options.TopPanelMaster : options.BottomPanelMaster;
                var tag = $"{options.TopPanelMaster}/{options.BottomPanelMaster} {spec.Id}";
                Assert.IsTrue(spec.X >= 0 && spec.X + spec.Width <= SteamwovenGrid.CentreWidth, tag + " inside the centre block");
                if (spec.Id != SteamwovenControlId.ExperienceStrip) Assert.IsTrue(spec.Y >= 0 && spec.Y + spec.Height <= band, tag + " inside the band");
                Assert.IsTrue(spec.Width > 0 && spec.Height > 0, tag);
            }
            foreach (var a in specs.Where(s => s.Id != SteamwovenControlId.HoverImage && s.Id != SteamwovenControlId.ExperienceStrip))
                foreach (var b in specs.Where(s => s.Id != SteamwovenControlId.HoverImage && s.Id != SteamwovenControlId.ExperienceStrip && s.Band == a.Band && s.Id > a.Id))
                    Assert.IsFalse(a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height, $"{options.TopPanelMaster}/{options.BottomPanelMaster} {a.Id} overlaps {b.Id}");
        }
    }

    [TestMethod]
    public void TheOriginalStripHeightsGiveTheOriginalsSquareButtonSizesAndHorizontalOffsetsAreTheOriginalsTimesThreePointSix()
    {
        var specs = SteamwovenControls.For(150, 570).ToDictionary(s => s.Id);
        Assert.AreEqual(120, specs[SteamwovenControlId.Inventory].Width, "34 units = 122.4 px, snapped to 120");
        Assert.AreEqual(138, specs[SteamwovenControlId.Skills].Width, "39 units = 140.4 px, snapped to 138");
        foreach (var (id, x) in SteamwovenControls.OriginalX)
            Assert.AreEqual(SteamwovenGrid.UnitsToMaster(x), specs[id].X, id.ToString());
        Assert.AreEqual(SteamwovenGrid.UnitsToMaster(649), specs[SteamwovenControlId.Spells].X, "the original's stagger of the right-hand column is kept");
    }

    [TestMethod]
    public void TheControlsOfASmallerWindowAreTheMasterEdgesRoundedFromTheirOwnPosition()
    {
        var l = Layout(1920, 1080);
        var skills = l.Control(SteamwovenControlId.Skills);
        Assert.AreEqual(new PixelRect(240 + 1248, 852 + 9, 60, 60), skills.Device, "2496 / 2, 18 / 2 from the bottom band's top (852)");
        var clock = l.Control(SteamwovenControlId.Clock);
        Assert.AreEqual(new PixelRect(240 + 1167, 9, 231, 42), clock.Device);
        // Every control keeps its shared edges when the scale is not a half: 1366 x 768, f = 0.3556.
        var odd = Layout(1366, 768);
        var map = odd.Control(SteamwovenControlId.Map);
        var left = odd.Top.Centre.X + (odd.Scale * 282).RoundHalfUp();
        Assert.AreEqual(left, map.Device.X);
        Assert.AreEqual(odd.Top.Centre.X + (odd.Scale * (282 + 96)).RoundHalfUp(), map.Device.Right);
    }
}
