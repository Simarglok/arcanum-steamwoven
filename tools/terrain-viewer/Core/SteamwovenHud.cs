using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2452: where the play screen's parts go in the Steamwoven layout (the column, docs/ui/steamwoven-interface-trd.md "Column mode"): the layout of
/// <see cref="SteamwovenLayoutBuilder"/> for the window, the scale of the two strips and their places on it. Godot-free, so the viewer reads rectangles.
/// </summary>
/// <remarks>
/// The strips keep the original's 800-unit layout at one scale <see cref="StripScale"/>, the column's own (`3.6 · k · f`, or frame height / 600 in the fitted column). The top strip stands on the bottom edge of the top band and the
/// bottom strip hangs from the top edge of the bottom band, so each strip's rim meets the rim of the band that continues it; in the unfitted column at k = 1 the top strip is 147.6 · f high in a 150 · f band (the board shows above it) and
/// the bottom strip 572.4 · f in a 570 · f band (its last 2.4 · f px lie beyond the frame's bottom edge).
/// </remarks>
public sealed record SteamwovenHudPlan(
    SteamwovenLayout Layout, SteamwovenLayoutOptions Options, double StripScale, int StripLeft, int TopStripY, int BottomStripY, int TopInset, int BottomInset)
{
    /// <summary>The device-pixel gap of the top band above the top strip's art (the board shows there).</summary>
    public int TopGap => TopStripY - Layout.Top.Y;

    /// <summary>ARC-UI-2890: the column plan of the layout model: the strips are `Column.TopStrip` and `Column.BottomStrip`, the windows stand in `Column.Window`, and the world shows left and right of the column.</summary>
    public SteamwovenColumnPlan Column => Layout.Column;

    /// <summary>ARC-UI-2890: the window zone: the column's (column-wide, at the column's unit scale).</summary>
    public PixelRect WindowZone => Column.Window.Zone;
}

/// <summary>ARC-UI-2452: the plan builder of the play screen and the layout options it runs with.</summary>
public static class SteamwovenHud
{
    /// <summary>The layout options the viewer runs with: the fitted column (ARC-UI-2926: the only layout).</summary>
    public static SteamwovenLayoutOptions Options { get; set; } = SteamwovenLayoutOptions.Default;

    /// <summary>True while the older generated-interface smokes run: they check the whole-factor geometry of the strips, which the Steamwoven layout replaces.</summary>
    public static bool Suspended { get; set; }

    /// <summary>The plan for a window of <paramref name="width"/> × <paramref name="height"/> device pixels, or null where the layout is not supported (below f = 0.25) or <paramref name="options"/> are invalid.</summary>
    public static SteamwovenHudPlan? Plan(int width, int height, SteamwovenLayoutOptions? options = null)
    {
        options ??= Options;
        var result = SteamwovenLayoutBuilder.Compute(width, height, options);
        if (result.Layout is not { } layout) return null;
        // ARC-UI-2890: the column's own strip scale (3.6 · k · f, or frame height / 600 in the fitted column).
        var unit = layout.Column.StripScale;
        var stripWidth = unit * AreaGameBarLayout.ScreenWidth;
        var stripLeft = layout.Frame.X + (((Ratio)layout.Frame.Width - stripWidth) / 2).RoundHalfUp();
        var topStripY = layout.Top.Bottom - (unit * AreaGameBarLayout.TopHeight).RoundHalfUp();
        var bottomStripY = layout.Bottom.Y;
        return new SteamwovenHudPlan(layout, options, unit.ToDouble(), stripLeft, topStripY, bottomStripY, layout.Middle.Y, height - layout.Middle.Bottom);
    }
}
