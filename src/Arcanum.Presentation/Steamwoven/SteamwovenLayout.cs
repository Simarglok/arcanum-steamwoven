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

/// <summary>
/// The Steamwoven screen layout for one window size: the column (owner decision 2026-11-19, epic #2446; the full-width layout is gone, ARC-UI-2926). All rectangles are in device pixels of the window.
/// <see cref="Scale"/> is `f`, device pixels per master pixel (`frame height / 2160` in the fitted column, `min(W / 3840, H / 2160)` in the unfitted one); <see cref="Step"/> is the baked set drawn and
/// <see cref="RuntimeScale"/> is `f` over the step, drawn at whole pixels with no filtering when <see cref="ExactStep"/>. <see cref="Top"/>, <see cref="Middle"/> and <see cref="Bottom"/> are the bands of the
/// frame (the top strip's, the window zone's and the bottom strip's, over the frame's whole width); <see cref="Column"/> is the column plan inside them.
/// </summary>
public sealed record SteamwovenLayout(
    int WindowWidth, int WindowHeight,
    Ratio Scale, SteamwovenStep Step, Ratio RuntimeScale, bool ExactStep,
    PixelRect Frame, PixelInsets Bars,
    PixelRect Top, PixelRect Middle, PixelRect Bottom,
    SteamwovenColumnPlan Column);

/// <summary>
/// Builds the <see cref="SteamwovenLayout"/> of the column (TRD "Column mode"). `f` is `min(W / 3840, H / 2160)`, kept as an exact fraction, in the unfitted column; a window from 16:10 to 16:9 is
/// laid out whole; a narrower one is letterboxed to 2400 · f and a wider one pillarboxed to 3840 · f, centred, and a bar under 4 px per side is dropped (the frame is the window). In the fitted column
/// (ARC-UI-2890, the one the game uses) the frame is the window, or a 4:3 band of it, and `f` is the frame's height over 2160. The strips' heights are rounded as sizes from the frame's top and bottom edges, every other edge
/// from its own exact position, halves up, so neighbouring parts share edges. Godot-free; the viewer reads the rectangles.
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
        if (options.Column.FitHeight)
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
        var fit = options.Column.FitHeight;
        var fitUnit = new Ratio(frame.Height, 600);
        var topH = fit ? (fitUnit * 41).RoundHalfUp() : (scale * options.Column.TopStripMaster).RoundHalfUp();
        var bottomH = fit ? frame.Height - (fitUnit * 441).RoundHalfUp() : (scale * options.Column.BottomStripMaster).RoundHalfUp();
        var topRect = new PixelRect(frame.X, frame.Y, frame.Width, topH);
        var middleRect = new PixelRect(frame.X, frame.Y + topH, frame.Width, frame.Height - topH - bottomH);
        var bottomRect = new PixelRect(frame.X, frame.Bottom - bottomH, frame.Width, bottomH);

        var layout = new SteamwovenLayout(
            width, height, scale, step, runtime, runtime == Ratio.One,
            frame, bars, topRect, middleRect, bottomRect,
            ColumnPlan(options.Column, scale, width, frame, topRect, bottomRect, middleRect));
        return new SteamwovenLayoutResult(layout, null);
    }

    /// <summary>
    /// ARC-UI-2889: the column plan. The column is `2880 · k · f` wide, centred in the frame (edges rounded from their own exact position); the strips are the bands' heights
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
}
