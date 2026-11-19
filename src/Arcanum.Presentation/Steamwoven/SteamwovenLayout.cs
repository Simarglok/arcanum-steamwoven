namespace Arcanum.Presentation.Steamwoven;

/// <summary>Why no layout was made.</summary>
public enum SteamwovenLayoutFailure
{
    /// <summary>A window side is not in 1..<see cref="SteamwovenGrid.MaxWindowSide"/>.</summary>
    InvalidSize,
    /// <summary>The options break the grid or window-zone rules (<see cref="SteamwovenLayoutOptions.IsValid"/>).</summary>
    InvalidOptions,
    /// <summary>`f` is below 0.25 (SWI-LAY-4): the Steamwoven interface is not supported and the Original look is used.</summary>
    ScaleBelowSupport,
}

/// <summary>A typed result: a layout or the reason for none.</summary>
public sealed record SteamwovenLayoutResult(SteamwovenLayout? Layout, SteamwovenLayoutFailure? Failure)
{
    public bool IsOk => Layout is not null;
}

/// <summary>One band: its rectangle, the centre block that keeps the original layout and the two sides that carry the same material.</summary>
public sealed record SteamwovenBand(PixelRect Band, PixelRect Centre, PixelRect LeftSide, PixelRect RightSide);

/// <summary>
/// A two-part window (inventory, loot, barter) in the middle band (SWI-BDR-5): the left material runs from the frame's left edge to <see cref="SplitX"/>,
/// the right one from there to the frame's right edge.
/// </summary>
public sealed record SteamwovenSplit(int SplitX, PixelRect Left, PixelRect Right);

/// <summary>
/// A backdrop master on the screen. <see cref="Target"/> is what it fills; <see cref="Master"/> is the whole master as drawn (it overhangs the target
/// where the target is shorter: a centred crop, or where the master is scaled up to cover); <see cref="Scale"/> is device pixels per master pixel
/// of that drawing; <see cref="Zone"/> is the original window zone in the screen, at the content scale `f`.
/// </summary>
public sealed record SteamwovenBackdrop(PixelRect Target, PixelRect Master, Ratio Scale, PixelRect Zone);

/// <summary>
/// ARC-UI-2450 (SWI-LAY-1..7, -10, -11; SWI-BDR-2, -3, -5, -6): the Steamwoven screen layout for one window size. All rectangles are in device
/// pixels of the window. <see cref="Scale"/> is `f`, device pixels per master pixel; <see cref="Step"/> is the baked set drawn and
/// <see cref="RuntimeScale"/> is `f` over the step, drawn at whole pixels with no filtering when <see cref="ExactStep"/>.
/// </summary>
public sealed record SteamwovenLayout(
    int WindowWidth, int WindowHeight,
    Ratio Scale, SteamwovenStep Step, Ratio RuntimeScale, bool ExactStep,
    PixelRect Frame, PixelInsets Bars,
    SteamwovenBand Top, SteamwovenBand Middle, SteamwovenBand Bottom,
    PixelRect WindowZone, SteamwovenSplit Split,
    SteamwovenBackdrop MiddleBackdrop, SteamwovenBackdrop FullScreenBackdrop,
    SteamwovenControlList Controls,
    SteamwovenColumnPlan? Column = null)
{
    /// <summary>The control with this id (ARC-UI-2526, SWI-LAY-11).</summary>
    public SteamwovenControl Control(SteamwovenControlId id) => Controls.First(control => control.Id == id);

    /// <summary>A master position along the frame's width as a device x: `round(frame left + position · f)`, per edge (SWI-LAY-7).</summary>
    public int DeviceX(int masterXFromFrameLeft) => Frame.X + (Scale * masterXFromFrameLeft).RoundHalfUp();
}

/// <summary>
/// ARC-UI-2450: builds the <see cref="SteamwovenLayout"/>. `f` is `min(W / 3840, H / 2160)`, kept as an exact fraction. A window from 16:10 to 16:9 is
/// laid out whole; a narrower one is letterboxed to 2400 · f and a wider one pillarboxed to 3840 · f, centred, and a bar under 4 px per side is
/// dropped (the frame is the window; the middle band or the sides take the difference). The panel heights are rounded as sizes from the frame's top
/// and bottom edges (`round(120 · f)`, `round(456 · f)` with the default heights, as the TRD reference table shows); every other edge (centre block, split, zone, backdrop) is rounded from its own exact position, halves
/// up, so neighbouring parts share edges. With the column mode of the options (ARC-UI-2889) <see cref="SteamwovenLayout.Column"/> holds the column plan; every other member is the full-width layout (except in the fitted column of ARC-UI-2890, whose frame, bands, `f` and step are the fitted composition's)
/// computed as before (the bands span the frame, <see cref="SteamwovenLayout.Controls"/> sit on the full-width centre block), which a column viewer does not draw. The window zone is centred in the middle band and kept inside it. Godot-free; the viewer reads the rectangles.
/// </summary>
public static class SteamwovenLayoutBuilder
{
    public static SteamwovenLayoutResult Compute(int width, int height, SteamwovenLayoutOptions? options = null)
    {
        options ??= SteamwovenLayoutOptions.Default;
        if (width < 1 || height < 1 || width > SteamwovenGrid.MaxWindowSide || height > SteamwovenGrid.MaxWindowSide)
            return Fail(SteamwovenLayoutFailure.InvalidSize);
        if (!options.IsValid) return Fail(SteamwovenLayoutFailure.InvalidOptions);

        long w = width, h = height;
        var wider = 9 * w > 16 * h;
        var scale = wider ? new Ratio(h, SteamwovenGrid.ReferenceHeight) : new Ratio(w, SteamwovenGrid.ReferenceWidth);
        // ARC-UI-2890: the fitted column supports any aspect ratio: the 4:3 composition fills the window's height (or its width where the window is narrower than 4:3: black bars above and below), the world shows at the sides.
        // Its kit scale is `f = frame height / 2160` (the composition's 600 units are 2160 master px at f = 1), so an 800 x 600 window has f = 0.278 and is supported, whatever its width.
        PixelRect frame;
        if (options.Column is { FitHeight: true })
        {
            frame = FitFrame(width, height);
            scale = new Ratio(frame.Height, SteamwovenGrid.ReferenceHeight);
        }
        else
        {
            if (scale < SteamwovenGrid.MinSupportedScale) return Fail(SteamwovenLayoutFailure.ScaleBelowSupport);
            frame = FrameFor(width, height, scale, wider);
        }
        if (scale < SteamwovenGrid.MinSupportedScale) return Fail(SteamwovenLayoutFailure.ScaleBelowSupport);
        var bars = new PixelInsets(frame.X, frame.Y, width - frame.Right, height - frame.Bottom);

        var step = SteamwovenGrid.StepFor(scale);
        var runtime = scale / SteamwovenGrid.StepScale(step);

        // ARC-UI-2890: in the fitted column the bands are the composition's: the top strip's 41 units and the bottom strip's 159 at `u = frame height / 600`, the middle band the window's 400 between them (each edge rounded from its own exact position).
        var fit = options.Column is { FitHeight: true };
        var fitUnit = new Ratio(frame.Height, 600);
        var topH = fit ? (fitUnit * 41).RoundHalfUp() : (scale * options.TopPanelMaster).RoundHalfUp();
        var bottomH = fit ? frame.Height - (fitUnit * 441).RoundHalfUp() : (scale * options.BottomPanelMaster).RoundHalfUp();
        var topRect = new PixelRect(frame.X, frame.Y, frame.Width, topH);
        var middleRect = new PixelRect(frame.X, frame.Y + topH, frame.Width, frame.Height - topH - bottomH);
        var bottomRect = new PixelRect(frame.X, frame.Bottom - bottomH, frame.Width, bottomH);

        // The centre block is centred in the frame: left edge = round((frame width − 2880 f) / 2), right edge = round((frame width + 2880 f) / 2).
        var centreWidth = scale * SteamwovenGrid.CentreWidth;
        var centreLeft = frame.X + ((Ratio)frame.Width - centreWidth).Halve().RoundHalfUp();
        var centreRight = frame.X + ((Ratio)frame.Width + centreWidth).Halve().RoundHalfUp();
        SteamwovenBand BandOf(PixelRect band) => new(
            band,
            PixelRect.FromEdges(centreLeft, band.Y, centreRight, band.Bottom),
            PixelRect.FromEdges(band.X, band.Y, centreLeft, band.Bottom),
            PixelRect.FromEdges(centreRight, band.Y, band.Right, band.Bottom));

        var zone = CentredZone(middleRect, centreLeft, centreRight, scale * SteamwovenGrid.WindowZoneHeight);
        var splitX = frame.X + ((Ratio)frame.Width - scale * 300).Halve().RoundHalfUp();
        var split = new SteamwovenSplit(
            splitX,
            PixelRect.FromEdges(frame.X, middleRect.Y, splitX, middleRect.Bottom),
            PixelRect.FromEdges(splitX, middleRect.Y, frame.Right, middleRect.Bottom));

        var middleBackdrop = Backdrop(middleRect, SteamwovenGrid.ReferenceWidth, options.BackdropHeight, scale, zone);
        var fullZone = CentredZone(frame, centreLeft, centreRight, scale * SteamwovenGrid.FullScreenZoneHeight);
        var fullBackdrop = Backdrop(frame, SteamwovenGrid.ReferenceWidth, SteamwovenGrid.FullScreenHeight, scale, fullZone);

        var layout = new SteamwovenLayout(
            width, height, scale, step, runtime, runtime == Ratio.One,
            frame, bars, BandOf(topRect), BandOf(middleRect), BandOf(bottomRect),
            zone, split, middleBackdrop, fullBackdrop,
            PlaceControls(options, scale, centreLeft, topRect, bottomRect),
            options.Column is { } column ? ColumnPlan(column, scale, width, frame, topRect, bottomRect, middleRect) : null);
        return new SteamwovenLayoutResult(layout, null);
    }

    /// <summary>The centre block's controls (<see cref="SteamwovenControls"/>) in device pixels: each edge is rounded from its own exact position, from the centre block's left edge and the band's top edge, halves up.</summary>
    private static SteamwovenControlList PlaceControls(SteamwovenLayoutOptions options, Ratio scale, int centreLeft, PixelRect top, PixelRect bottom)
    {
        var placed = new List<SteamwovenControl>();
        foreach (var spec in SteamwovenControls.For(options.TopPanelMaster, options.BottomPanelMaster))
        {
            var band = spec.Band == SteamwovenHudBand.Top ? top : bottom;
            int Edge(int origin, int master) => origin + (scale * master).RoundHalfUp();
            placed.Add(new SteamwovenControl(spec, PixelRect.FromEdges(
                Edge(centreLeft, spec.X), Edge(band.Y, spec.Y), Edge(centreLeft, spec.X + spec.Width), Edge(band.Y, spec.Y + spec.Height))));
        }
        return new SteamwovenControlList(placed);
    }

    /// <summary>
    /// ARC-UI-2889: the column plan. The column is `2880 · k · f` wide, centred in the frame (edges rounded from their own exact position); the strips are the full-width layout's panel heights
    /// (150 / 570 · k master px, rounded as sizes from the frame's edges); the window zone (800 × 400 units at `3.6 · k · f`) and the full-screen zone (800 × 600 units) are centred vertically in
    /// the middle band and in the frame and kept inside them.
    /// </summary>
    private static SteamwovenColumnPlan ColumnPlan(SteamwovenColumnMode mode, Ratio scale, int windowWidth, PixelRect frame, PixelRect top, PixelRect bottom, PixelRect middle)
    {
        // The fitted column's strip scale in the model's terms (`f · k`, unit = 3.6 times it) is frame height / 2160, so `u = frame height / 600`.
        var stripScale = mode.FitHeight ? new Ratio(frame.Height, SteamwovenGrid.ReferenceHeight) : scale * mode.K;
        var columnWidth = stripScale * SteamwovenGrid.CentreWidth;
        var left = frame.X + ((Ratio)frame.Width - columnWidth).Halve().RoundHalfUp();
        var right = frame.X + ((Ratio)frame.Width + columnWidth).Halve().RoundHalfUp();

        SteamwovenColumnZone ZoneIn(PixelRect area, int zoneUnitsHeightMaster)
        {
            // The fitted column's window zone is the middle band itself (no gap); the full-screen zone is the whole frame in both modes at 16:9 and in the fitted one.
            var zone = mode.FitHeight && zoneUnitsHeightMaster == SteamwovenGrid.WindowZoneHeight ? PixelRect.FromEdges(left, area.Y, right, area.Bottom) : CentredZone(area, left, right, stripScale * zoneUnitsHeightMaster);
            return new SteamwovenColumnZone(
                zone,
                PixelRect.FromEdges(left, area.Y, right, zone.Y),
                PixelRect.FromEdges(left, zone.Bottom, right, area.Bottom));
        }

        var worldLeftEdge = mode.WorldToEdges ? 0 : frame.X;
        var worldRightEdge = mode.WorldToEdges ? windowWidth : frame.Right;
        return new SteamwovenColumnPlan(
            mode, stripScale * new Ratio(SteamwovenGrid.UnitNumerator, SteamwovenGrid.UnitDenominator),
            PixelRect.FromEdges(left, frame.Y, right, frame.Bottom),
            PixelRect.FromEdges(left, top.Y, right, top.Bottom),
            PixelRect.FromEdges(left, bottom.Y, right, bottom.Bottom),
            ZoneIn(middle, SteamwovenGrid.WindowZoneHeight),
            ZoneIn(frame, SteamwovenGrid.FullScreenZoneHeight),
            PixelRect.FromEdges(worldLeftEdge, frame.Y, left, frame.Bottom),
            PixelRect.FromEdges(right, frame.Y, worldRightEdge, frame.Bottom));
    }

    private static SteamwovenLayoutResult Fail(SteamwovenLayoutFailure failure) => new(null, failure);

    private static Ratio Halve(this Ratio value) => value / 2;

    /// <summary>The frame of the fitted column: the whole window, or, where the window is taller than 4:3, a 4:3 band of the window's width centred vertically (the bars above and below are black, however thin).</summary>
    private static PixelRect FitFrame(int width, int height)
    {
        var composition = new Ratio(3L * width, 4).RoundHalfUp();
        if (height <= composition) return new PixelRect(0, 0, width, height);
        return new PixelRect(0, new Ratio(height - composition, 2).RoundHalfUp(), width, composition);
    }

    private static PixelRect FrameFor(int width, int height, Ratio scale, bool wider)
    {
        long w = width, h = height;
        if (5 * w < 8 * h)
        {
            // Narrower than 16:10: letterbox to 2400 · f high.
            var frameH = (scale * SteamwovenGrid.TallReferenceHeight).RoundHalfUp();
            var extra = height - frameH;
            if (extra < 2 * SteamwovenGrid.MinBarPx) return new PixelRect(0, 0, width, height);
            var barTop = new Ratio(extra, 2).RoundHalfUp();
            return new PixelRect(0, barTop, width, frameH);
        }
        if (wider)
        {
            // Wider than 16:9: pillarbox to 3840 · f wide.
            var frameW = (scale * SteamwovenGrid.ReferenceWidth).RoundHalfUp();
            var extra = width - frameW;
            if (extra < 2 * SteamwovenGrid.MinBarPx) return new PixelRect(0, 0, width, height);
            var barLeft = new Ratio(extra, 2).RoundHalfUp();
            return new PixelRect(barLeft, 0, frameW, height);
        }
        return new PixelRect(0, 0, width, height);
    }

    /// <summary>A zone as wide as the centre block, `exactHeight` high, centred vertically in <paramref name="area"/> and kept inside it.</summary>
    private static PixelRect CentredZone(PixelRect area, int left, int right, Ratio exactHeight)
    {
        var top = area.Y + ((Ratio)area.Height - exactHeight).Halve().RoundHalfUp();
        var bottom = area.Y + ((Ratio)area.Height + exactHeight).Halve().RoundHalfUp();
        return PixelRect.FromEdges(left, Math.Max(top, area.Y), right, Math.Min(bottom, area.Bottom));
    }

    /// <summary>
    /// A master of <paramref name="masterWidth"/> × <paramref name="masterHeight"/> over <paramref name="target"/>: drawn at `f`, or scaled up uniformly
    /// whenever the target is larger than the master at `f` (a dropped bar under 4 px, or a rounded band a pixel taller than the master at `f`), centred; a shorter target shows the centred crop.
    /// </summary>
    private static SteamwovenBackdrop Backdrop(PixelRect target, int masterWidth, int masterHeight, Ratio scale, PixelRect zone)
    {
        var cover = Ratio.Max(scale, Ratio.Max(new Ratio(target.Width, masterWidth), new Ratio(target.Height, masterHeight)));
        var drawnW = cover * masterWidth;
        var drawnH = cover * masterHeight;
        var master = PixelRect.FromEdges(
            target.X + ((Ratio)target.Width - drawnW).Halve().RoundHalfUp(),
            target.Y + ((Ratio)target.Height - drawnH).Halve().RoundHalfUp(),
            target.X + ((Ratio)target.Width + drawnW).Halve().RoundHalfUp(),
            target.Y + ((Ratio)target.Height + drawnH).Halve().RoundHalfUp());
        return new SteamwovenBackdrop(target, master, cover, zone);
    }
}
