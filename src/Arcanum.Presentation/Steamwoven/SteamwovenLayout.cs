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
    SteamwovenBackdrop MiddleBackdrop, SteamwovenBackdrop FullScreenBackdrop)
{
    /// <summary>A master position along the frame's width as a device x: `round(frame left + position · f)`, per edge (SWI-LAY-7).</summary>
    public int DeviceX(int masterXFromFrameLeft) => Frame.X + (Scale * masterXFromFrameLeft).RoundHalfUp();
}

/// <summary>
/// ARC-UI-2450: builds the <see cref="SteamwovenLayout"/>. `f` is `min(W / 3840, H / 2160)`, kept as an exact fraction. A window from 16:10 to 16:9 is
/// laid out whole; a narrower one is letterboxed to 2400 · f and a wider one pillarboxed to 3840 · f, centred, and a bar under 4 px per side is
/// dropped (the frame is the window; the middle band or the sides take the difference). The panel heights are rounded as sizes from the frame's top
/// and bottom edges (`round(150 · f)`, `round(570 · f)`: this is what the TRD reference table shows, e.g. 238 and 599 at 1600 × 900, where rounding the
/// bottom panel's top edge would give 237 and 600); every other edge (centre block, split, zone, backdrop) is rounded from its own exact position, halves
/// up, so neighbouring parts share edges. The window zone is centred in the middle band and clamped to it: where the exact zone is a pixel taller
/// than the rounded band (1600 × 900), it loses that pixel. Godot-free; the viewer reads the rectangles.
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
        if (scale < SteamwovenGrid.MinSupportedScale) return Fail(SteamwovenLayoutFailure.ScaleBelowSupport);

        var frame = FrameFor(width, height, scale, wider);
        var bars = new PixelInsets(frame.X, frame.Y, width - frame.Right, height - frame.Bottom);

        var step = SteamwovenGrid.StepFor(scale);
        var runtime = scale / SteamwovenGrid.StepScale(step);

        var topH = (scale * options.TopPanelMaster).RoundHalfUp();
        var bottomH = (scale * options.BottomPanelMaster).RoundHalfUp();
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

        var middleBackdrop = Backdrop(middleRect, SteamwovenGrid.ReferenceWidth, SteamwovenGrid.BackdropHeight, scale, zone);
        var fullZone = CentredZone(frame, centreLeft, centreRight, scale * SteamwovenGrid.FullScreenZoneHeight);
        var fullBackdrop = Backdrop(frame, SteamwovenGrid.ReferenceWidth, SteamwovenGrid.FullScreenHeight, scale, fullZone);

        var layout = new SteamwovenLayout(
            width, height, scale, step, runtime, runtime == Ratio.One,
            frame, bars, BandOf(topRect), BandOf(middleRect), BandOf(bottomRect),
            zone, split, middleBackdrop, fullBackdrop);
        return new SteamwovenLayoutResult(layout, null);
    }

    private static SteamwovenLayoutResult Fail(SteamwovenLayoutFailure failure) => new(null, failure);

    private static Ratio Halve(this Ratio value) => value / 2;

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
