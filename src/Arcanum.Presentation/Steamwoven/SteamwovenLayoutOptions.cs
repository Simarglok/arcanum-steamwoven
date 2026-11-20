namespace Arcanum.Presentation.Steamwoven;

/// <summary>
/// The options of the Steamwoven layout (the column, owner decision 2026-11-19, epic #2446; the full-width layout and its panel-height options are gone, ARC-UI-2926): the column mode
/// (<see cref="SteamwovenColumnMode"/>), whose strips are the original's scaled by k, so the strips' master heights are <see cref="SteamwovenColumnMode.TopStripMaster"/> and
/// <see cref="SteamwovenColumnMode.BottomStripMaster"/> (whole multiples of 6, together at most 720 so that the 1440 px window zone fits the middle band of a 16:9 frame).
/// </summary>
public sealed record SteamwovenLayoutOptions(SteamwovenColumnMode Column)
{
    /// <summary>The most the two strips may take together: the 720 px that the original's 200 units leave beside the 1440 px window zone.</summary>
    public const int MaxPanelsTotal = SteamwovenGrid.ReferenceHeight - SteamwovenGrid.WindowZoneHeight;

    /// <summary>The strips' master heights of the column mode (150 and 570 master px at k = 1).</summary>
    public int TopPanelMaster => Column.TopStripMaster;
    public int BottomPanelMaster => Column.BottomStripMaster;

    /// <summary>The layout the game uses: the fitted column (<see cref="ColumnFitted"/>).</summary>
    public static SteamwovenLayoutOptions Default => ColumnFitted;

    /// <summary>The column mode with the original's strips (k = 1: 150 and 570 master px), not fitted to the frame's height (the frame is the 16:10 to 16:9 band, letterboxed or pillarboxed).</summary>
    public static SteamwovenLayoutOptions ColumnOriginal { get; } = ForColumn(SteamwovenColumnMode.KOriginal);

    /// <summary>ARC-UI-2890: the column mode that fills the frame's height with the 4:3 composition of the strips and the window (<see cref="SteamwovenColumnMode.FitHeight"/>): no gaps, no wood fill.</summary>
    public static SteamwovenLayoutOptions ColumnFitted { get; } = ColumnOriginal with { Column = ColumnOriginal.Column with { FitHeight = true } };

    /// <summary>Options in column mode for <paramref name="k"/> (only 1 is defined).</summary>
    public static SteamwovenLayoutOptions ForColumn(Ratio k, bool worldToEdges = false)
    {
        var mode = new SteamwovenColumnMode(k, worldToEdges);
        return mode.IsValid
            ? new SteamwovenLayoutOptions(mode)
            : throw new ArgumentOutOfRangeException(nameof(k), "the column mode is defined for k = 1");
    }

    /// <summary>These options with the world side rectangles running to the window's edges.</summary>
    public SteamwovenLayoutOptions WithWorldToEdges(bool worldToEdges = true) => this with { Column = Column with { WorldToEdges = worldToEdges } };

    /// <summary>Whether the column mode is valid and its strips satisfy the grid and window-zone rules.</summary>
    public bool IsValid =>
        Column.IsValid
        && TopPanelMaster > 0 && BottomPanelMaster > 0
        && TopPanelMaster % SteamwovenGrid.GridPx == 0 && BottomPanelMaster % SteamwovenGrid.GridPx == 0
        && TopPanelMaster <= MaxPanelsTotal && BottomPanelMaster <= MaxPanelsTotal - TopPanelMaster;
}
