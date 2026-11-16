namespace Arcanum.Presentation.Steamwoven;

/// <summary>
/// ARC-UI-2450 (SWI-LAY-10): the band heights of the play screen, in master pixels, in one place. The values are provisional until the owner decision
/// of the panel proportions (#2453). Both must be positive multiples of 6 and together at most 720 so that the 1440 px window zone fits the middle
/// band of a 16:9 frame.
/// </summary>
public sealed record SteamwovenLayoutOptions
{
    public int TopPanelMaster { get; init; } = SteamwovenGrid.DefaultTopPanel;
    public int BottomPanelMaster { get; init; } = SteamwovenGrid.DefaultBottomPanel;

    /// <summary>The most the two panels may take together: the 720 px that the original's 200 units leave beside the 1440 px window zone.</summary>
    public const int MaxPanelsTotal = SteamwovenGrid.ReferenceHeight - SteamwovenGrid.WindowZoneHeight;

    public static SteamwovenLayoutOptions Default { get; } = new();

    /// <summary>Whether the values satisfy the grid and window-zone rules.</summary>
    public bool IsValid =>
        TopPanelMaster > 0 && BottomPanelMaster > 0
        && TopPanelMaster % SteamwovenGrid.GridPx == 0 && BottomPanelMaster % SteamwovenGrid.GridPx == 0
        && TopPanelMaster <= MaxPanelsTotal && BottomPanelMaster <= MaxPanelsTotal - TopPanelMaster;
}
