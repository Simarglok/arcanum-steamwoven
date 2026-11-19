using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2452: where the play screen's parts go in the Steamwoven layout (docs/ui/steamwoven-interface-trd.md SWI-LAY): the layout of
/// <see cref="SteamwovenLayoutBuilder"/> for the window, the scale of the two strips and their places on it. Godot-free, so the viewer reads rectangles.
/// </summary>
/// <remarks>
/// The strips keep the original's 800-unit layout: one scale <see cref="StripScale"/> = `f · 3.6 · k` for both, where `k` is 1 for the original strips' own heights (150 and 570 master px)
/// and the smaller of the two panel ratios for another pair (0.8 for the default 120 and 456, the owner's choice of #2453), so a thinner panel gives a smaller centre block with wider sides, never
/// a squashed strip: the prototype's stand-in for the art that will be drawn to fit the panel. The top strip stands on the bottom edge of the top band and the bottom strip hangs from the top edge of the bottom band, so each strip's
/// rim meets the rim of the board that continues it at the sides; at k = 1 the strip is 147.6 · f high in a 150 · f band (the board shows above it) and 572.4 · f in
/// a 570 · f band (its last 2.4 · f px lie beyond the frame's bottom edge); at k = 0.8 it is 118.1 · f in 120 · f and 457.9 · f in 456 · f.
/// This scale is the prototype's stand-in only: the owner's choice of #2526 (TRD SWI-LAY-11) is a centre block of 2880 px with the controls placed by <see cref="SteamwovenLayout.Controls"/>.
/// </remarks>
public sealed record SteamwovenHudPlan(
    SteamwovenLayout Layout, SteamwovenLayoutOptions Options, double StripScale, int StripLeft, int TopStripY, int BottomStripY, int TopInset, int BottomInset)
{
    /// <summary>The device-pixel gap of the top band above the top strip's art (the board shows there).</summary>
    public int TopGap => TopStripY - Layout.Top.Band.Y;

    /// <summary>ARC-UI-2890: the column plan of the layout model (null: the full-width layout). In column mode the strips are `Column.TopStrip` and `Column.BottomStrip`, the windows stand in `Column.Window`, and the world shows left and right of the column.</summary>
    public SteamwovenColumnPlan? Column => Layout.Column;

    /// <summary>ARC-UI-2890: the window zone: the column's (column-wide, at the column's unit scale) in column mode, else the layout's.</summary>
    public PixelRect WindowZone => Column?.Window.Zone ?? Layout.WindowZone;
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
        // k = min(top / 150, bottom / 570), exact: the strips fit both bands at one scale.
        var topK = new Ratio(options.TopPanelMaster, SteamwovenGrid.StripTopPanel);
        var bottomK = new Ratio(options.BottomPanelMaster, SteamwovenGrid.StripBottomPanel);
        var k = topK < bottomK ? topK : bottomK;
        // ARC-UI-2890: the column's own strip scale (3.6 · k · f, or frame height / 600 in the fitted column).
        var unit = layout.Column?.StripScale ?? layout.Scale * new Ratio(SteamwovenGrid.UnitNumerator, SteamwovenGrid.UnitDenominator) * k;
        var stripWidth = unit * AreaGameBarLayout.ScreenWidth;
        var stripLeft = layout.Frame.X + (((Ratio)layout.Frame.Width - stripWidth) / 2).RoundHalfUp();
        var topStripY = layout.Top.Band.Bottom - (unit * AreaGameBarLayout.TopHeight).RoundHalfUp();
        var bottomStripY = layout.Bottom.Band.Y;
        return new SteamwovenHudPlan(layout, options, unit.ToDouble(), stripLeft, topStripY, bottomStripY, layout.Middle.Band.Y, height - layout.Middle.Band.Bottom);
    }
}
