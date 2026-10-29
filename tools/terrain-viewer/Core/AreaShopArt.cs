namespace Arcanum.TerrainViewer.Core;

/// <summary>The opening shop's art buttons (window 15, button table 0x5C5408; ledger MW30).</summary>
public enum AreaShopArtButton { Accept, Back }

/// <summary>
/// ARC-UI-069: the original look of the opening shop (window 15), read from its descriptor 0x5C5468 and button table 0x5C5408 (ledger
/// MW30): no background of its own (-1: the window at (0, 41) between the strips 336 and 335, MW1), flags 5 and two art buttons in the
/// bottom strip, Accept 321 `NextBut` at (675, 55) and Back 323 `BackBut` at (69, 55) (table flag 1, as window 12's, MW27). Its refresh
/// opens the inventory window in barter mode (OS2), which is 800 × 400 at the screen's (0, 41) as every game window; with the main menu
/// up the open does not turn on the round view (IW33) and a release in its square does not close the window (IW34). Coordinates here are
/// the 800 × 600 screen's. Godot-free.
/// </summary>
public static class AreaShopArtLayout
{
    public const int AcceptArt = AreaPremadeBrowserArtLayout.StartArt, BackArt = AreaPremadeBrowserArtLayout.BackArt;
    /// <summary>MainMenu.mes: the buttons' hints (7003 Accept, 7002 Previous Screen; table 0x5C5408).</summary>
    public const int AcceptHint = AreaPremadeBrowserArtLayout.StartHint, BackHint = AreaPremadeBrowserArtLayout.BackHint;

    public static IReadOnlyList<int> ArtNumbers { get; } =
        [AreaPremadeBrowserArtLayout.TopStripArt, AreaPremadeBrowserArtLayout.BottomStripArt, AcceptArt, BackArt];

    /// <summary>The barter window's place on the screen: the game windows' 800 × 400 at (0, 41).</summary>
    public static (int X, int Y, int Width, int Height) Window { get; } = (0, AreaPremadeBrowserArtLayout.WindowTop, 800, 400);

    /// <summary>A button's art and its top-left on the screen: (675, 55) and (69, 55) of the bottom strip (flag 1, MW12).</summary>
    public static (int Art, int X, int Y) Button(AreaShopArtButton button) => button switch
    {
        AreaShopArtButton.Accept => (AcceptArt, 675, AreaPremadeBrowserArtLayout.BottomStripTop + 55),
        AreaShopArtButton.Back => (BackArt, 69, AreaPremadeBrowserArtLayout.BottomStripTop + 55),
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>The button's hint message in MainMenu.mes.</summary>
    public static int Hint(AreaShopArtButton button) => button switch
    {
        AreaShopArtButton.Accept => AcceptHint,
        AreaShopArtButton.Back => BackHint,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>PROJECT: the button the controller's focus row lights (the menu's shop rows: 0 Accept, 1 Back); the original has no focus.</summary>
    public static AreaShopArtButton? Focused(int row) => row switch
    {
        0 => AreaShopArtButton.Accept,
        1 => AreaShopArtButton.Back,
        _ => null,
    };
}
