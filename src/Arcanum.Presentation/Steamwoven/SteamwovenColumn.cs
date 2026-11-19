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
    public static Ratio KOriginal => Ratio.One;
    public static Ratio KThin => new(4, 5);

    /// <summary>Only the two presets are defined: the strips' master heights are the original's 150 / 570 scaled by k, whole multiples of 6.</summary>
    public bool IsValid => K == KOriginal || K == KThin;

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
