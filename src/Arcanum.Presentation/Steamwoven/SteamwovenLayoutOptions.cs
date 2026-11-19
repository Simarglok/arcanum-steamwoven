namespace Arcanum.Presentation.Steamwoven;

/// <summary>
/// ARC-UI-2450 (SWI-LAY-10): the band heights of the play screen, in master pixels, in one place. The defaults are the owner's choice of 2026-11-17 (#2453): 120 and 456.
/// Both must be positive multiples of 6 and together at most 720 so that the 1440 px window zone fits the middle band of a 16:9 frame.
/// </summary>
public sealed record SteamwovenLayoutOptions
{
    public int TopPanelMaster { get; init; } = SteamwovenGrid.DefaultTopPanel;
    public int BottomPanelMaster { get; init; } = SteamwovenGrid.DefaultBottomPanel;

    /// <summary>The middle-band backdrop master's height (SWI-BDR-3): the middle band of a 16:10 frame (2400 px) at ×1, so 1824 for the default panels.</summary>
    public int BackdropHeight => SteamwovenGrid.TallReferenceHeight - TopPanelMaster - BottomPanelMaster;

    /// <summary>The most the two panels may take together: the 720 px that the original's 200 units leave beside the 1440 px window zone.</summary>
    public const int MaxPanelsTotal = SteamwovenGrid.ReferenceHeight - SteamwovenGrid.WindowZoneHeight;

    public static SteamwovenLayoutOptions Default { get; } = new();

    /// <summary>
    /// ARC-UI-2889: the column mode (null = the full-width layout of the other members). In column mode the strips are the original's scaled by k, so the panel heights must be
    /// <see cref="SteamwovenColumnMode.TopStripMaster"/> and <see cref="SteamwovenColumnMode.BottomStripMaster"/>; <see cref="ForColumn"/> builds a consistent pair.
    /// </summary>
    public SteamwovenColumnMode? Column { get; init; }

    /// <summary>The column mode with the original's strips (k = 1: 150 and 570 master px).</summary>
    public static SteamwovenLayoutOptions ColumnOriginal { get; } = ForColumn(SteamwovenColumnMode.KOriginal);

    /// <summary>ARC-UI-2890: the column mode that fills the frame's height with the 4:3 composition of the strips and the window (<see cref="SteamwovenColumnMode.FitHeight"/>): no gaps, no wood fill.</summary>
    public static SteamwovenLayoutOptions ColumnFitted { get; } = ColumnOriginal with { Column = ColumnOriginal.Column! with { FitHeight = true } };

    /// <summary>The column mode with the thinner strips (k = 0.8: 120 and 456 master px).</summary>
    public static SteamwovenLayoutOptions ColumnThin { get; } = ForColumn(SteamwovenColumnMode.KThin);

    /// <summary>Options in column mode for <paramref name="k"/> (1 or 4/5), the panel heights set to the strips'.</summary>
    public static SteamwovenLayoutOptions ForColumn(Ratio k, bool worldToEdges = false)
    {
        var mode = new SteamwovenColumnMode(k, worldToEdges);
        return mode.IsValid
            ? new SteamwovenLayoutOptions { Column = mode, TopPanelMaster = mode.TopStripMaster, BottomPanelMaster = mode.BottomStripMaster }
            : throw new ArgumentOutOfRangeException(nameof(k), "the column mode is defined for k = 1 and k = 4/5");
    }

    /// <summary>These options with the world side rectangles running to the window's edges (column mode only).</summary>
    public SteamwovenLayoutOptions WithWorldToEdges(bool worldToEdges = true) =>
        Column is null ? this : this with { Column = Column with { WorldToEdges = worldToEdges } };

    /// <summary>Whether the values satisfy the grid and window-zone rules.</summary>
    public bool IsValid =>
        TopPanelMaster > 0 && BottomPanelMaster > 0
        && TopPanelMaster % SteamwovenGrid.GridPx == 0 && BottomPanelMaster % SteamwovenGrid.GridPx == 0
        && TopPanelMaster <= MaxPanelsTotal && BottomPanelMaster <= MaxPanelsTotal - TopPanelMaster
        && (Column is null || (Column.IsValid && TopPanelMaster == Column.TopStripMaster && BottomPanelMaster == Column.BottomStripMaster));
}
