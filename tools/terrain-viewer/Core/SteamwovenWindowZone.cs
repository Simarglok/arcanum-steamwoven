using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>Where a menu window lies in the Steamwoven layout: its top left in device pixels and the scale of one original unit (`f · 3.6`).</summary>
public readonly record struct SteamwovenWindowPlace(double Scale, double X, double Y);

/// <summary>
/// ARC-UI-2575 (SWI-BDR-2): the one hook every menu window takes its place from while the Steamwoven layout is on. A window of the original's 800-unit width is drawn at the
/// layout's unit scale `f · 3.6` (the same for every window, no per-window constant) and sits in the window zone of the layout model (<see cref="SteamwovenLayout.WindowZone"/>, the original's
/// 800 × 400 units, centred in the middle band), which lies inside the frame, so a window is never under a pillarbox bar. The layout is read for the window size alone: a window that
/// hides the bars (loot, barter, reading, the save and load lists) follows the same zone as one that keeps them. Godot-free; the viewer reads the numbers.
/// </summary>
public static class SteamwovenWindowZone
{
    /// <summary>The original screen's width in units, the width of every menu window.</summary>
    public const int ScreenWidthUnits = AreaGameBarLayout.ScreenWidth;

    /// <summary>The scale of one original unit in device pixels: `f · 3.6` (the exact fraction of the layout, as a double).</summary>
    public static double UnitScale(SteamwovenHudPlan plan) =>
        (plan.Layout.Scale * new Ratio(SteamwovenGrid.UnitNumerator, SteamwovenGrid.UnitDenominator)).ToDouble();

    /// <summary>
    /// A window <paramref name="widthUnits"/> × <paramref name="heightUnits"/> units in the window zone of a window of <paramref name="width"/> × <paramref name="height"/> device pixels, centred in the zone; null
    /// where the layout is not supported (the plan is null).
    /// </summary>
    public static SteamwovenWindowPlace? Body(int width, int height, int widthUnits, int heightUnits, SteamwovenLayoutOptions? options = null) =>
        SteamwovenHud.Plan(width, height, options) is { } plan ? Body(plan, widthUnits, heightUnits) : null;

    /// <summary>The same for a plan already built.</summary>
    public static SteamwovenWindowPlace Body(SteamwovenHudPlan plan, int widthUnits, int heightUnits)
    {
        var zone = plan.Layout.WindowZone;
        var unit = UnitScale(plan);
        var x = Math.Round(zone.X + (zone.Width - widthUnits * unit) / 2);
        var y = Math.Round(zone.Y + (zone.Height - heightUnits * unit) / 2);
        return new SteamwovenWindowPlace(unit, x, y);
    }

    /// <summary>
    /// An 800 × 600 original screen whose 800 × 400 body (the window between the screen's own two strips, <paramref name="bodyTopUnits"/> from the screen's top) lies in the window zone: the screen's top left
    /// and the unit scale. The strips above and below the body belong to the screen's art and hang over the zone; the screen is kept inside the frame (it is 2160 · f high and the frame is at least that high, so
    /// at 16:9 the body sits a little above the zone's place), and a frame shorter than the screen centres it. Null where the layout is not supported.
    /// </summary>
    public static SteamwovenWindowPlace? Screen(int width, int height, int bodyTopUnits, SteamwovenLayoutOptions? options = null) =>
        SteamwovenHud.Plan(width, height, options) is { } plan ? Screen(plan, bodyTopUnits) : null;

    /// <summary>The same for a plan already built.</summary>
    public static SteamwovenWindowPlace Screen(SteamwovenHudPlan plan, int bodyTopUnits)
    {
        var frame = plan.Layout.Frame;
        var zone = plan.Layout.WindowZone;
        var unit = UnitScale(plan);
        var screenHeight = AreaGameBarLayout.ScreenHeight * unit;
        var x = Math.Round(zone.X + (zone.Width - ScreenWidthUnits * unit) / 2);
        var wanted = zone.Y - bodyTopUnits * unit;
        var y = screenHeight >= frame.Height
            ? frame.Y + (frame.Height - screenHeight) / 2
            : Math.Clamp(wanted, frame.Y, frame.Bottom - screenHeight);
        return new SteamwovenWindowPlace(unit, x, Math.Round(y));
    }
}
