using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2452, ARC-UI-2608: where the play screen's parts go in the Steamwoven layout (docs/ui/steamwoven-interface-trd.md SWI-LAY): the layout of
/// <see cref="SteamwovenLayoutBuilder"/> for the window, the one scale of the HUD's units and the places of the two strips' unit frames. Godot-free, so the viewer reads rectangles.
/// </summary>
/// <remarks>
/// Variant B of the owner's choice of #2526 (SWI-LAY-11): the centre block keeps 2880 px, so a HUD unit (the original's 800-unit layout) is `f · 3.6` device pixels for every pair of
/// panel heights, and the HUD text has the windows' text scale. The controls (buttons, vials, clock, message window, experience strip and the hover image) are placed by
/// <see cref="SteamwovenLayout.Controls"/>; each strip's frame is the place of the original's own unit coordinates, which the panes drawn over the strips (spell, skill and weapon panes,
/// hot keys, counters) still use: the top frame stands where the clock window of the model lies (the original's clock y), the bottom frame where the message window lies (its original y),
/// so the panes follow their window. <see cref="ScreenOriginY"/> is the frame of the whole 800 × 600 screen the combat lamps use: its y 441 is the bottom band's upper edge.
/// </remarks>
public sealed record SteamwovenHudPlan(
    SteamwovenLayout Layout, SteamwovenLayoutOptions Options, double StripScale, int StripLeft, int TopStripY, int BottomStripY, int TopInset, int BottomInset, int ScreenOriginY)
{
    /// <summary>The device-pixel gap of the top band above the top frame (negative: the frame starts above the band).</summary>
    public int TopGap => TopStripY - Layout.Top.Band.Y;
}

/// <summary>ARC-UI-2452: the prototype's plan builder and the panel-height variant it runs with.</summary>
public static class SteamwovenHud
{
    /// <summary>The panel heights the prototype is laid out with: the owner's choice (#2453, 120 and 456 master px).</summary>
    public static SteamwovenLayoutOptions Options { get; set; } = SteamwovenLayoutOptions.Default;

    /// <summary>The variant the owner did not choose, for comparison: the original strips' own heights, 150 and 570 master px (every length a multiple of 6 px).</summary>
    public static SteamwovenLayoutOptions Original { get; } = new() { TopPanelMaster = SteamwovenGrid.StripTopPanel, BottomPanelMaster = SteamwovenGrid.StripBottomPanel };

    /// <summary>True while the older generated-interface smokes run: they check the whole-factor geometry of the strips, which the Steamwoven layout replaces.</summary>
    public static bool Suspended { get; set; }

    /// <summary>Reads <c>top,bottom</c> master pixels (for example <c>120,456</c>); null when it is not a valid pair of the layout rules.</summary>
    public static SteamwovenLayoutOptions? ParseOptions(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var top) || !int.TryParse(parts[1], out var bottom)) return null;
        var options = new SteamwovenLayoutOptions { TopPanelMaster = top, BottomPanelMaster = bottom };
        return options.IsValid ? options : null;
    }

    /// <summary>The plan for a window of <paramref name="width"/> × <paramref name="height"/> device pixels, or null where the layout is not supported (below f = 0.25) or <paramref name="options"/> break the grid.</summary>
    public static SteamwovenHudPlan? Plan(int width, int height, SteamwovenLayoutOptions? options = null)
    {
        options ??= Options;
        var result = SteamwovenLayoutBuilder.Compute(width, height, options);
        if (result.Layout is not { } layout) return null;
        var unit = layout.Scale * new Ratio(SteamwovenGrid.UnitNumerator, SteamwovenGrid.UnitDenominator);
        var stripLeft = layout.Top.Centre.X;
        var clock = layout.Control(SteamwovenControlId.Clock).Device;
        var message = layout.Control(SteamwovenControlId.MessageWindow).Device;
        var topStripY = clock.Y - (unit * AreaClockLayout.WindowY).RoundHalfUp();
        var bottomStripY = message.Y - (unit * (AreaMessageLayout.Window.Y - AreaGameBarLayout.BottomTop)).RoundHalfUp();
        var screenOriginY = layout.Bottom.Band.Y - (unit * AreaGameBarLayout.BottomTop).RoundHalfUp();
        return new SteamwovenHudPlan(layout, options, unit.ToDouble(), stripLeft, topStripY, bottomStripY, layout.Middle.Band.Y, height - layout.Middle.Band.Bottom, screenOriginY);
    }
}
