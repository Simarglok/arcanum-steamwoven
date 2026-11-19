namespace Arcanum.Presentation.Steamwoven;

/// <summary>
/// ARC-UI-2889: the column mode of the Steamwoven layout (owner decision 2026-11-19, epic #2446). The top and bottom strips keep the original's 800-unit width, centred, at the strip scale
/// `s = 3.6 · k · f` (device pixels per original UI unit); the menu windows take the same width; vertical gaps inside the column take the window's wood; the game world shows left and right of
/// the column. <see cref="K"/> is 1 (the original strips, 150 and 570 master px) or 0.8 (120 and 456, today's thinner pair). With the mode off the layout is the full-width one, unchanged.
/// </summary>
/// <param name="K">The strip scale factor against the original's: 1 or 4/5.</param>
/// <param name="WorldToEdges">Above 16:9 the world side rectangles run to the window's edges instead of ending at the frame's (the pillarbox bars of SWI-LAY-3 stay black otherwise).</param>
public sealed record SteamwovenColumnMode(Ratio K, bool WorldToEdges = false)
{
    /// <summary>ARC-UI-2890: how the viewer dims and desaturates the world at the sides while a menu window is open (presentation only; the values are here, in the options, not in the viewer's code).</summary>
    public SteamwovenWorldDim WorldDim { get; init; } = SteamwovenWorldDim.Default;

    /// <summary>
    /// ARC-UI-2890 (owner decision 2026-11-19: no wood fill in the gaps): the strips and the open window are one 4:3 composition (41 + 400 + 159 = 600 units of the original's 800 × 600 screen) scaled to the frame's whole height, `u = frame height / 600`
    /// device pixels per unit, the column `800 · u` wide; the window zone fills the middle band exactly, so there are no gaps, and the world (or the black bars) shows at the sides. At 16:9 it is the k = 1 column within a pixel or two (`u = 3.6 · f`; the strips are exactly 41 and 159 units, without the sliver and overhang of the 150 and 570 px bands); at 16:10 the
    /// composition is scaled up (4 · f). Only with k = 1; the scale does not depend on the panel heights of the options.
    /// </summary>
    public bool FitHeight { get; init; }

    public static Ratio KOriginal => Ratio.One;
    public static Ratio KThin => new(4, 5);

    /// <summary>Only the two presets are defined: the strips' master heights are the original's 150 / 570 scaled by k, whole multiples of 6.</summary>
    public bool IsValid => (K == KOriginal || K == KThin) && WorldDim.IsValid && (!FitHeight || K == KOriginal);

    /// <summary>The strips' master heights for <see cref="K"/>: 150 / 570 at k = 1, 120 / 456 at k = 0.8.</summary>
    public int TopStripMaster => (K * SteamwovenGrid.StripTopPanel).RoundHalfUp();
    public int BottomStripMaster => (K * SteamwovenGrid.StripBottomPanel).RoundHalfUp();
}

/// <summary>
/// A menu zone in the column and what lies around it: <see cref="Zone"/> is the original window (800 × 400 units) or full-screen plate (800 × 600 units) at the strip scale, as wide as the
/// column and centred; <see cref="GapAbove"/> and <see cref="GapBelow"/> are the column's rectangles above and below it (empty where the zone fills the height: 16:9 with k = 1 for the window zone).
/// </summary>
public sealed record SteamwovenColumnZone(PixelRect Zone, PixelRect GapAbove, PixelRect GapBelow);

/// <summary>
/// ARC-UI-2889: the column plan for one window size. All rectangles are in device pixels of the window, edges rounded from their own exact position (SWI-LAY-7), so neighbours share edges.
/// <see cref="Column"/> spans the frame's height; the strips sit at the frame's top and bottom; <see cref="Window"/> lies in the middle band between the strips and its gaps fill the rest of it;
/// <see cref="FullScreen"/> is centred in the whole frame (strips included) and its gaps take the rest of the column; <see cref="WorldLeft"/> and <see cref="WorldRight"/> are the frame
/// (or, with <see cref="SteamwovenColumnMode.WorldToEdges"/>, the window) left and right of the column over the frame's height.
/// </summary>
public sealed record SteamwovenColumnPlan(
    SteamwovenColumnMode Mode, Ratio StripScale,
    PixelRect Column, PixelRect TopStrip, PixelRect BottomStrip,
    SteamwovenColumnZone Window, SteamwovenColumnZone FullScreen,
    PixelRect WorldLeft, PixelRect WorldRight)
{
    /// <summary>The scale of one original unit in the strips and column windows, `3.6 · k · f`, as a double.</summary>
    public double UnitScale => StripScale.ToDouble();
}

/// <summary>
/// ARC-UI-2890 (AC3): the look of the world left and right of the column while a menu window is open: <see cref="Darken"/> is the share of the brightness taken away (0 to 1) and <see cref="Desaturate"/> the
/// share of the colour taken away (0 to 1). Presentation only; the world's state and the clicks on it are unchanged.
/// </summary>
public sealed record SteamwovenWorldDim(double Darken, double Desaturate)
{
    public static SteamwovenWorldDim Default { get; } = new(0.45, 0.55);

    public bool IsValid => Darken is >= 0 and <= 1 && Desaturate is >= 0 and <= 1;
}

/// <summary>ARC-UI-2890 (AC1): the layout the game option chooses: the column (the strips and the window as one 4:3 composition scaled to the frame's height, the world at the sides; the default while it is tested) or the full-width panels. ARC-UI-2925: the thin-strip preset (`column-thin`) is gone; a settings line that still says it reads as the default.</summary>
public enum SteamwovenLayoutChoice
{
    Column,
    FullWidth,
}

/// <summary>ARC-UI-2890: the names and options of <see cref="SteamwovenLayoutChoice"/>: the settings line and the launcher use the names, the layout model takes the options.</summary>
public static class SteamwovenLayoutChoices
{
    /// <summary>What a fresh install and an unreadable setting give while the column is tested (the owner's hypothesis of 2026-11-19).</summary>
    public const SteamwovenLayoutChoice Default = SteamwovenLayoutChoice.Column;

    public static string Name(SteamwovenLayoutChoice choice) => choice switch
    {
        SteamwovenLayoutChoice.Column => "column",
        SteamwovenLayoutChoice.FullWidth => "full-width",
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };

    /// <summary>The choice a name stands for (case does not matter), or null.</summary>
    public static SteamwovenLayoutChoice? TryParse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "column" => SteamwovenLayoutChoice.Column,
        "full-width" => SteamwovenLayoutChoice.FullWidth,
        _ => null,
    };

    /// <summary>The layout options of a choice: <see cref="SteamwovenLayoutOptions.ColumnFitted"/> or the full-width default.</summary>
    public static SteamwovenLayoutOptions Options(SteamwovenLayoutChoice choice) => choice switch
    {
        SteamwovenLayoutChoice.Column => SteamwovenLayoutOptions.ColumnFitted,
        SteamwovenLayoutChoice.FullWidth => SteamwovenLayoutOptions.Default,
        _ => throw new ArgumentOutOfRangeException(nameof(choice)),
    };
}
