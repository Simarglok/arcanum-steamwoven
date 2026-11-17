using Arcanum.Presentation.Steamwoven;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-2452: where the play screen's parts go in the Steamwoven layout (docs/ui/steamwoven-interface-trd.md SWI-LAY): the layout of
/// <see cref="SteamwovenLayoutBuilder"/> for the window, the scale of the two strips and their places on it. Godot-free, so the viewer reads rectangles.
/// </summary>
/// <remarks>
/// The strips keep the original's 800-unit layout: one scale <see cref="StripScale"/> = `f · 3.6 · k` for both, where `k` is 1 for the TRD's panel heights
/// (150 and 570 master px) and the smaller of the two panel ratios for another pair, so a thinner panel gives a smaller centre block with wider sides, never
/// a squashed strip. The top strip stands on the bottom edge of the top band and the bottom strip hangs from the top edge of the bottom band, so each strip's
/// rim meets the rim of the board that continues it at the sides; the strip is 147.6 · f high in a 150 · f band (the board shows above it) and 572.4 · f in
/// a 570 · f band (its last 2.4 · f px lie beyond the frame's bottom edge). Those fractions are the cost of keeping the original's offsets (SWI-LAY-11).
/// </remarks>
public sealed record SteamwovenHudPlan(
    SteamwovenLayout Layout, SteamwovenLayoutOptions Options, double StripScale, int StripLeft, int TopStripY, int BottomStripY, int TopInset, int BottomInset)
{
    /// <summary>The device-pixel gap of the top band above the top strip's art (the board shows there).</summary>
    public int TopGap => TopStripY - Layout.Top.Band.Y;
}

/// <summary>ARC-UI-2452: the prototype's plan builder and the panel-height variant it runs with.</summary>
public static class SteamwovenHud
{
    /// <summary>The panel heights the prototype is laid out with: the TRD's provisional values by default; the owner decision #2453 fixes them.</summary>
    public static SteamwovenLayoutOptions Options { get; set; } = SteamwovenLayoutOptions.Default;

    /// <summary>The thinner variant the owner compares with the TRD's (every length a multiple of 6 px).</summary>
    public static SteamwovenLayoutOptions Thin { get; } = new() { TopPanelMaster = 120, BottomPanelMaster = 456 };

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
        var topK = new Ratio(options.TopPanelMaster, SteamwovenGrid.DefaultTopPanel);
        var bottomK = new Ratio(options.BottomPanelMaster, SteamwovenGrid.DefaultBottomPanel);
        var k = topK < bottomK ? topK : bottomK;
        var unit = layout.Scale * new Ratio(SteamwovenGrid.UnitNumerator, SteamwovenGrid.UnitDenominator) * k;
        var stripWidth = unit * AreaGameBarLayout.ScreenWidth;
        var stripLeft = layout.Frame.X + (((Ratio)layout.Frame.Width - stripWidth) / 2).RoundHalfUp();
        var topStripY = layout.Top.Band.Bottom - (unit * AreaGameBarLayout.TopHeight).RoundHalfUp();
        var bottomStripY = layout.Bottom.Band.Y;
        return new SteamwovenHudPlan(layout, options, unit.ToDouble(), stripLeft, topStripY, bottomStripY, layout.Middle.Band.Y, height - layout.Middle.Band.Bottom);
    }
}
