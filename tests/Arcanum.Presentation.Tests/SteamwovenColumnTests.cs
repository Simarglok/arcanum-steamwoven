using Arcanum.Presentation.Steamwoven;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Presentation.Tests;

// ARC-UI-2889: the column mode of the Steamwoven layout model (strips of the original's width, centred; window and full-screen zones; world at the sides).
[TestClass]
public sealed class SteamwovenColumnTests
{
    // Window, preset, frame (x, y, width, height), column edges, strip heights, window zone top and bottom and full-screen zone top and bottom (both absolute window y).
    // The numbers come from an independent exact-fraction calculation of the rules in the issue (`f = min(W / 3840, H / 2160)`, strips `round(150 or 570 · k · f)`, column `2880 · k · f` centred, zones `1440` and `2160 · k · f` centred), not from the builder.
    private static readonly (int W, int H, bool Thin, int FrameX, int FrameY, int FrameW, int FrameH, int ColL, int ColR, int TopH, int BotH, int WinTop, int WinBottom, int FullTop, int FullBottom)[] Table =
    [
        (3840, 2160, false, 0, 0, 3840, 2160, 480, 3360, 150, 570, 150, 1590, 0, 2160),
        (2560, 1440, false, 0, 0, 2560, 1440, 320, 2240, 100, 380, 100, 1060, 0, 1440),
        (1920, 1080, false, 0, 0, 1920, 1080, 240, 1680, 75, 285, 75, 795, 0, 1080),
        (2560, 1600, false, 0, 0, 2560, 1600, 320, 2240, 100, 380, 180, 1140, 80, 1520),
        (1920, 1200, false, 0, 0, 1920, 1200, 240, 1680, 75, 285, 135, 855, 60, 1140),
        (1280, 800, false, 0, 0, 1280, 800, 160, 1120, 50, 190, 90, 570, 40, 760),
        (1366, 768, false, 0, 0, 1366, 768, 171, 1195, 53, 203, 53, 565, 0, 768),
        (3440, 1440, false, 440, 0, 2560, 1440, 760, 2680, 100, 380, 100, 1060, 0, 1440),
        (1024, 768, false, 0, 64, 1024, 640, 128, 896, 40, 152, 136, 520, 96, 672),
        (3840, 2160, true, 0, 0, 3840, 2160, 768, 3072, 120, 456, 336, 1488, 216, 1944),
        (2560, 1440, true, 0, 0, 2560, 1440, 512, 2048, 80, 304, 224, 992, 144, 1296),
        (1920, 1080, true, 0, 0, 1920, 1080, 384, 1536, 60, 228, 168, 744, 108, 972),
        (2560, 1600, true, 0, 0, 2560, 1600, 512, 2048, 80, 304, 304, 1072, 224, 1376),
        (1920, 1200, true, 0, 0, 1920, 1200, 384, 1536, 60, 228, 228, 804, 168, 1032),
        (1280, 800, true, 0, 0, 1280, 800, 256, 1024, 40, 152, 152, 536, 112, 688),
        (1366, 768, true, 0, 0, 1366, 768, 273, 1093, 43, 162, 120, 529, 77, 691),
        (3440, 1440, true, 440, 0, 2560, 1440, 952, 2488, 80, 304, 224, 992, 144, 1296),
        (1024, 768, true, 0, 64, 1024, 640, 205, 819, 32, 122, 185, 493, 154, 614),
    ];

    private static SteamwovenLayoutOptions Preset(bool thin) => thin ? SteamwovenLayoutOptions.ColumnThin : SteamwovenLayoutOptions.ColumnOriginal;

    private static SteamwovenLayout Layout(int w, int h, SteamwovenLayoutOptions options)
    {
        var result = SteamwovenLayoutBuilder.Compute(w, h, options);
        Assert.IsTrue(result.IsOk, $"{w}x{h}: {result.Failure}");
        return result.Layout!;
    }

    private static SteamwovenColumnPlan Plan(int w, int h, bool thin) => Layout(w, h, Preset(thin)).Column!;

    [TestMethod]
    public void TheColumnPlanForEverySizeAndPresetHasTheExactEdges()
    {
        foreach (var row in Table)
        {
            var label = $"{row.W}x{row.H} {(row.Thin ? "k=0.8" : "k=1")}";
            var layout = Layout(row.W, row.H, Preset(row.Thin));
            var plan = layout.Column!;
            Assert.AreEqual(new PixelRect(row.FrameX, row.FrameY, row.FrameW, row.FrameH), layout.Frame, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, row.FrameY, row.ColR, row.FrameY + row.FrameH), plan.Column, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, row.FrameY, row.ColR, row.FrameY + row.TopH), plan.TopStrip, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, row.FrameY + row.FrameH - row.BotH, row.ColR, row.FrameY + row.FrameH), plan.BottomStrip, label);
            var middleTop = row.FrameY + row.TopH;
            var middleBottom = row.FrameY + row.FrameH - row.BotH;
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, row.WinTop, row.ColR, row.WinBottom), plan.Window.Zone, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, middleTop, row.ColR, row.WinTop), plan.Window.GapAbove, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, row.WinBottom, row.ColR, middleBottom), plan.Window.GapBelow, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, row.FullTop, row.ColR, row.FullBottom), plan.FullScreen.Zone, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, row.FrameY, row.ColR, row.FullTop), plan.FullScreen.GapAbove, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, row.FullBottom, row.ColR, row.FrameY + row.FrameH), plan.FullScreen.GapBelow, label);
            Assert.AreEqual(PixelRect.FromEdges(row.FrameX, row.FrameY, row.ColL, row.FrameY + row.FrameH), plan.WorldLeft, label);
            Assert.AreEqual(PixelRect.FromEdges(row.ColR, row.FrameY, row.FrameX + row.FrameW, row.FrameY + row.FrameH), plan.WorldRight, label);
        }
    }

    [TestMethod]
    public void At16To9WithTheOriginalStripsTheWindowZoneFillsTheMiddleBandAndTheGapsAreEmpty()
    {
        // 150 + 1440 + 570 = 2160 master px: the window zone is exactly the middle band.
        var master = Plan(3840, 2160, thin: false);
        Assert.AreEqual(150 + 1440 + 570, master.Column.Height);
        Assert.AreEqual(new PixelRect(480, 150, 2880, 1440), master.Window.Zone);
        Assert.AreEqual(new PixelRect(480, 150, 2880, 0), master.Window.GapAbove);
        Assert.AreEqual(new PixelRect(480, 1590, 2880, 0), master.Window.GapBelow);
        Assert.AreEqual(new PixelRect(480, 0, 2880, 150), master.TopStrip);
        Assert.AreEqual(new PixelRect(480, 1590, 2880, 570), master.BottomStrip);
        foreach (var (w, h) in new[] { (2560, 1440), (1920, 1080), (1366, 768), (3440, 1440), (1280, 720), (1600, 900), (1680, 945) })
        {
            var plan = Plan(w, h, thin: false);
            Assert.AreEqual(0, plan.Window.GapAbove.Height, $"{w}x{h} above");
            Assert.AreEqual(0, plan.Window.GapBelow.Height, $"{w}x{h} below");
            Assert.AreEqual(plan.TopStrip.Bottom, plan.Window.Zone.Y, $"{w}x{h}");
            Assert.AreEqual(plan.BottomStrip.Y, plan.Window.Zone.Bottom, $"{w}x{h}");
            Assert.AreEqual(0, plan.FullScreen.GapAbove.Height + plan.FullScreen.GapBelow.Height, $"{w}x{h} full screen");
        }
    }

    [TestMethod]
    public void TheThinPresetAndSixteenToTenLeaveGapsInsideTheColumn()
    {
        var thin = Plan(3840, 2160, thin: true);
        Assert.AreEqual(216, thin.Window.GapAbove.Height);
        Assert.AreEqual(216, thin.Window.GapBelow.Height);
        Assert.AreEqual(216, thin.FullScreen.GapAbove.Height);
        var tall = Plan(2560, 1600, thin: false);
        Assert.AreEqual(80, tall.Window.GapAbove.Height);
        Assert.AreEqual(80, tall.Window.GapBelow.Height);
        Assert.AreEqual(80, tall.FullScreen.GapAbove.Height);
    }

    [TestMethod]
    public void StripsGapsAndZoneTileTheColumnAndTheColumnAndWorldTileTheFrameAtEverySize()
    {
        var sizes = new[]
        {
            (3840, 2160), (2560, 1440), (1920, 1080), (2560, 1600), (1920, 1200), (1280, 800), (1366, 768), (3440, 1440), (1024, 768), (5120, 1440), (3024, 1964), (3456, 2234),
            (1600, 900), (1680, 945), (1280, 720), (1000, 700), (2000, 1250), (5120, 2880),
        };
        foreach (var edges in new[] { false, true })
        foreach (var k in new[] { SteamwovenColumnMode.KOriginal, SteamwovenColumnMode.KThin })
        foreach (var (w, h) in sizes)
        {
            var label = $"{w}x{h} k={k} edges={edges}";
            var layout = Layout(w, h, SteamwovenLayoutOptions.ForColumn(k, edges));
            var plan = layout.Column!;
            // The column, top to bottom: top strip, gap, window zone, gap, bottom strip.
            Assert.AreEqual(layout.Frame.Y, plan.TopStrip.Y, label);
            Assert.AreEqual(plan.TopStrip.Bottom, plan.Window.GapAbove.Y, label);
            Assert.AreEqual(plan.Window.GapAbove.Bottom, plan.Window.Zone.Y, label);
            Assert.AreEqual(plan.Window.Zone.Bottom, plan.Window.GapBelow.Y, label);
            Assert.AreEqual(plan.Window.GapBelow.Bottom, plan.BottomStrip.Y, label);
            Assert.AreEqual(layout.Frame.Bottom, plan.BottomStrip.Bottom, label);
            // The full-screen zone and its gaps tile the column's height.
            Assert.AreEqual(layout.Frame.Y, plan.FullScreen.GapAbove.Y, label);
            Assert.AreEqual(plan.FullScreen.GapAbove.Bottom, plan.FullScreen.Zone.Y, label);
            Assert.AreEqual(plan.FullScreen.Zone.Bottom, plan.FullScreen.GapBelow.Y, label);
            Assert.AreEqual(layout.Frame.Bottom, plan.FullScreen.GapBelow.Bottom, label);
            // Every column rectangle has the column's edges; the zones lie inside the frame and their gaps are never negative.
            foreach (var rect in new[] { plan.TopStrip, plan.BottomStrip, plan.Window.Zone, plan.Window.GapAbove, plan.Window.GapBelow, plan.FullScreen.Zone, plan.FullScreen.GapAbove, plan.FullScreen.GapBelow })
            {
                Assert.AreEqual(plan.Column.X, rect.X, label);
                Assert.AreEqual(plan.Column.Right, rect.Right, label);
                Assert.IsTrue(rect.Height >= 0, label);
            }
            Assert.IsTrue(plan.Window.Zone.Y >= plan.TopStrip.Bottom && plan.Window.Zone.Bottom <= plan.BottomStrip.Y, label);
            // The world and the column tile the frame (or the window with the edges option) across.
            var worldLeft = edges ? 0 : layout.Frame.X;
            var worldRight = edges ? w : layout.Frame.Right;
            Assert.AreEqual(new PixelRect(worldLeft, layout.Frame.Y, plan.Column.X - worldLeft, layout.Frame.Height), plan.WorldLeft, label);
            Assert.AreEqual(new PixelRect(plan.Column.Right, layout.Frame.Y, worldRight - plan.Column.Right, layout.Frame.Height), plan.WorldRight, label);
            // Centred within a pixel, and as wide as `round(2880 · k · f)` within a pixel (edges are rounded from their own exact position).
            Assert.IsTrue(Math.Abs((plan.Column.X - layout.Frame.X) - (layout.Frame.Right - plan.Column.Right)) <= 1, label);
            var exactWidth = (layout.Scale * k * SteamwovenGrid.CentreWidth).RoundHalfUp();
            Assert.IsTrue(Math.Abs(plan.Column.Width - exactWidth) <= 1, label);
        }
    }

    [TestMethod]
    public void TheWorldRunsToTheWindowsEdgesOnlyWithTheOptionAndOnlyAbove16To9()
    {
        var bars = Layout(3440, 1440, SteamwovenLayoutOptions.ColumnOriginal);
        Assert.AreEqual(new PixelRect(440, 0, 2560, 1440), bars.Frame);
        Assert.AreEqual(new PixelRect(440, 0, 320, 1440), bars.Column!.WorldLeft);
        Assert.AreEqual(new PixelRect(2680, 0, 320, 1440), bars.Column.WorldRight);
        var edges = Layout(3440, 1440, SteamwovenLayoutOptions.ColumnOriginal.WithWorldToEdges());
        Assert.AreEqual(new PixelRect(0, 0, 760, 1440), edges.Column!.WorldLeft);
        Assert.AreEqual(new PixelRect(2680, 0, 760, 1440), edges.Column.WorldRight);
        Assert.AreEqual(bars.Column.Column, edges.Column.Column, "the column does not move");
        var thinEdges = Layout(3440, 1440, SteamwovenLayoutOptions.ColumnThin.WithWorldToEdges());
        Assert.AreEqual(new PixelRect(0, 0, 952, 1440), thinEdges.Column!.WorldLeft);
        Assert.AreEqual(new PixelRect(2488, 0, 952, 1440), thinEdges.Column.WorldRight);
        // At 16:9 the frame is the window: the option changes nothing.
        Assert.AreEqual(Plan(1920, 1080, thin: false), Layout(1920, 1080, SteamwovenLayoutOptions.ColumnOriginal.WithWorldToEdges()).Column! with { Mode = Plan(1920, 1080, thin: false).Mode });
        // A letterboxed 4:3 frame keeps its vertical extent: the world fills the frame's height beside the column.
        var tall = Layout(1024, 768, SteamwovenLayoutOptions.ColumnOriginal.WithWorldToEdges());
        Assert.AreEqual(new PixelRect(0, 64, 128, 640), tall.Column!.WorldLeft);
        Assert.AreEqual(new PixelRect(896, 64, 128, 640), tall.Column.WorldRight);
    }

    [TestMethod]
    public void TheStripScaleIsThreePointSixTimesKTimesFAndTheZonesAreTheOriginalsUnitsAtThatScale()
    {
        var master = Plan(3840, 2160, thin: false);
        Assert.AreEqual(new Ratio(18, 5), master.StripScale);
        Assert.AreEqual(3.6, master.UnitScale, 1e-12);
        Assert.AreEqual(new Ratio(72, 25), Plan(3840, 2160, thin: true).StripScale);
        var half = Plan(1920, 1080, thin: true);
        Assert.AreEqual(new Ratio(18, 5) * new Ratio(4, 5) * new Ratio(1, 2), half.StripScale);
        // 800 x 400 units at the strip scale: 1152 x 576 at 1920x1080 k = 0.8 (the zone is not clamped there).
        Assert.AreEqual(1152, half.Window.Zone.Width);
        Assert.AreEqual(576, half.Window.Zone.Height);
        Assert.AreEqual(1152, half.FullScreen.Zone.Width);
        Assert.AreEqual(864, half.FullScreen.Zone.Height, "800 x 600 units");
        Assert.AreEqual(1152, half.Column.Width);
    }

    [TestMethod]
    public void ColumnOptionsAreConsistentAndTheFullWidthLayoutIsUnchangedByThem()
    {
        Assert.IsTrue(SteamwovenLayoutOptions.ColumnOriginal.IsValid);
        Assert.IsTrue(SteamwovenLayoutOptions.ColumnThin.IsValid);
        Assert.AreEqual((150, 570), (SteamwovenLayoutOptions.ColumnOriginal.TopPanelMaster, SteamwovenLayoutOptions.ColumnOriginal.BottomPanelMaster));
        Assert.AreEqual((120, 456), (SteamwovenLayoutOptions.ColumnThin.TopPanelMaster, SteamwovenLayoutOptions.ColumnThin.BottomPanelMaster));
        Assert.IsNull(SteamwovenLayoutOptions.Default.Column);
        Assert.AreEqual(SteamwovenLayoutOptions.Default, SteamwovenLayoutOptions.ColumnThin with { Column = null }, "the thin strips are today's default panel heights");
        // A column mode whose panels are not the strips', or with another k, is invalid and a typed failure.
        Assert.IsFalse((SteamwovenLayoutOptions.ColumnOriginal with { TopPanelMaster = 120, BottomPanelMaster = 456 }).IsValid);
        Assert.IsFalse((SteamwovenLayoutOptions.ColumnThin with { Column = new SteamwovenColumnMode(new Ratio(1, 2)) }).IsValid);
        Assert.AreEqual(SteamwovenLayoutFailure.InvalidOptions,
            SteamwovenLayoutBuilder.Compute(1920, 1080, SteamwovenLayoutOptions.ColumnOriginal with { TopPanelMaster = 120, BottomPanelMaster = 456 }).Failure);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => SteamwovenLayoutOptions.ForColumn(new Ratio(1, 2)));
        Assert.AreSame(SteamwovenLayoutOptions.Default.WithWorldToEdges(), SteamwovenLayoutOptions.Default, "no column mode, nothing to change");
        // Without the column mode there is no plan; with it every member but the plan is the full-width layout of the same panel heights.
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1600), (3440, 1440), (1024, 768) })
        {
            Assert.IsNull(Layout(w, h, SteamwovenLayoutOptions.Default).Column, $"{w}x{h}");
            var column = Layout(w, h, SteamwovenLayoutOptions.ColumnThin);
            Assert.IsNotNull(column.Column);
            Assert.AreEqual(Layout(w, h, SteamwovenLayoutOptions.Default), column with { Column = null }, $"{w}x{h}");
        }
    }

    // ARC-UI-2890 (owner decision 2026-11-19): the fitted column: the strips and the window are one 4:3 composition scaled to the frame's height (u = height / 600), no gaps.
    // Window, frame, column edges, top strip height, middle band bottom (= the bottom strip's top), all from an independent exact-fraction calculation.
    private static readonly (int W, int H, int FrameX, int FrameW, int FrameH, int ColL, int ColR, int TopH, int MiddleBottom)[] FitTable =
    [
        (3840, 2160, 0, 3840, 2160, 480, 3360, 148, 1588),
        (2560, 1440, 0, 2560, 1440, 320, 2240, 98, 1058),
        (1920, 1080, 0, 1920, 1080, 240, 1680, 74, 794),
        (2560, 1600, 0, 2560, 1600, 213, 2347, 109, 1176),
        (1920, 1200, 0, 1920, 1200, 160, 1760, 82, 882),
        (1280, 800, 0, 1280, 800, 107, 1173, 55, 588),
        (3440, 1440, 0, 3440, 1440, 760, 2680, 98, 1058),
        (1024, 768, 0, 1024, 768, 0, 1024, 52, 564),
        (1280, 1024, 0, 1280, 960, 0, 1280, 66, 706),
        (5120, 1440, 0, 5120, 1440, 1600, 3520, 98, 1058),
        (800, 600, 0, 800, 600, 0, 800, 41, 441),
        (1000, 2000, 0, 1000, 750, 0, 1000, 51, 551),
        (1366, 768, 0, 1366, 768, 171, 1195, 52, 564),
    ];

    [TestMethod]
    public void The_fitted_column_fills_the_frame_s_height_with_the_4_3_composition_and_has_no_gaps()
    {
        foreach (var row in FitTable)
        {
            var label = $"{row.W}x{row.H}";
            var layout = Layout(row.W, row.H, SteamwovenLayoutOptions.ColumnFitted);
            var plan = layout.Column!;
            Assert.IsTrue(plan.Mode.FitHeight, label);
            Assert.AreEqual(row.FrameW, layout.Frame.Width, label);
            Assert.AreEqual(row.FrameH, layout.Frame.Height, label);
            Assert.AreEqual(row.FrameX, layout.Frame.X, label);
            Assert.AreEqual(row.W - layout.Frame.Right + layout.Frame.X, row.W - row.FrameW, label + ": the bars together");
            Assert.AreEqual(layout.Bars.Top + layout.Bars.Bottom, row.H - row.FrameH, label + ": the bars above and below");
            Assert.AreEqual(layout.Scale, new Ratio(row.FrameH, 2160), label + ": the kit scale is frame height / 2160");
            Assert.AreEqual(row.ColL, plan.Column.X, label);
            Assert.AreEqual(row.ColR, plan.Column.Right, label);
            var topBottom = layout.Frame.Y + row.TopH;
            Assert.AreEqual(topBottom, plan.TopStrip.Bottom, label);
            Assert.AreEqual(layout.Frame.Y, plan.TopStrip.Y, label);
            Assert.AreEqual(layout.Frame.Y + row.MiddleBottom, plan.BottomStrip.Y, label);
            Assert.AreEqual(layout.Frame.Bottom, plan.BottomStrip.Bottom, label);
            // The window zone is the middle band itself: no gap above or below; the full-screen zone is the whole frame.
            Assert.AreEqual(PixelRect.FromEdges(row.ColL, topBottom, row.ColR, layout.Frame.Y + row.MiddleBottom), plan.Window.Zone, label);
            Assert.AreEqual(0, plan.Window.GapAbove.Height, label);
            Assert.AreEqual(0, plan.Window.GapBelow.Height, label);
            Assert.AreEqual(layout.Middle.Band.Y, topBottom, label);
            Assert.AreEqual(layout.Middle.Band.Bottom, layout.Frame.Y + row.MiddleBottom, label);
            Assert.AreEqual(layout.Frame.Height, plan.FullScreen.Zone.Height, label);
            // The unit scale is frame height / 600, and the column is 800 units wide (within a pixel).
            Assert.AreEqual(row.FrameH / 600.0, plan.UnitScale, 1e-9, label);
            Assert.AreEqual(800 * plan.UnitScale, plan.Column.Width, 1.0, label);
            // The world fills the rest of the frame at the sides.
            Assert.AreEqual(layout.Frame.Width, plan.WorldLeft.Width + plan.Column.Width + plan.WorldRight.Width, label);
        }
    }

    [TestMethod]
    public void At_16_9_the_fitted_column_is_the_original_strips_column_and_never_overflows_the_frame()
    {
        var fitted = Layout(3840, 2160, SteamwovenLayoutOptions.ColumnFitted).Column!;
        var original = Layout(3840, 2160, SteamwovenLayoutOptions.ColumnOriginal).Column!;
        Assert.AreEqual(original.Column, fitted.Column);
        Assert.AreEqual(original.UnitScale, fitted.UnitScale, 1e-9);
        // At 16:10 the composition is scaled up (4 f against 3.6 f); the column is 4:3, narrower than the frame.
        var tall = Layout(2560, 1600, SteamwovenLayoutOptions.ColumnFitted);
        Assert.IsTrue(tall.Column!.UnitScale > Layout(2560, 1600, SteamwovenLayoutOptions.ColumnOriginal).Column!.UnitScale);
        foreach (var (w, h) in new[] { (3840, 2160), (2560, 1600), (1920, 1200), (1280, 800), (1024, 768), (3440, 1440) })
        {
            var layout = Layout(w, h, SteamwovenLayoutOptions.ColumnFitted);
            Assert.IsTrue(layout.Column!.Column.X >= layout.Frame.X && layout.Column.Column.Right <= layout.Frame.Right, $"{w}x{h}");
            Assert.IsTrue(layout.Column.TopStrip.Bottom <= layout.Column.BottomStrip.Y, $"{w}x{h}");
        }
    }

    [TestMethod]
    public void The_fit_height_flag_needs_the_original_strips_k()
    {
        Assert.IsTrue(SteamwovenLayoutOptions.ColumnFitted.IsValid);
        var thinFitted = SteamwovenLayoutOptions.ColumnThin with { Column = SteamwovenLayoutOptions.ColumnThin.Column! with { FitHeight = true } };
        Assert.IsFalse(thinFitted.IsValid);
    }
}
