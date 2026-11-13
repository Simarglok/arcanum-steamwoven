using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-1742: the original look of the character sheet's auto-level scheme page (window 0x64CA60, art 567, created by 0x55D060, drawn by 0x55D210,
/// buttons by 0x55DF90, scroll bar by 0x55EFF0; ledger ALP1..ALP16), read from the retail 1.0.7.4 executable. The window is 235 × 325 at the main window's
/// (503, 63); every coordinate here is <b>panel-relative</b> (the exe's tables hold screen coordinates, the drawer 0x55B880 subtracts the window's origin,
/// (503, 104)). The page is the shown critter's scheme name centred near the top in Flare12 blue, and the list of scheme names below it: fifteen rows of
/// 160 × 16 from (23, 66), each name in Flare12 white (blue under the pointer), scrolled by the bar at (209, 60), 17 × 255 (<see cref="AreaSheetScrollBar"/>).
/// A press on a row writes that scheme into the critter. Godot-free.
/// </summary>
public static class AreaCharacterSheetSchemesLayout
{
    public const int BackgroundArt = 567, UpArrowArt = 317, DownArrowArt = 318, ThumbTopArt = 238, ThumbMiddleArt = 787, ThumbBottomArt = 240,
        Rows = AreaSchemesPage.Rows, RowX = 23, RowWidth = 160, RowHeight = 16, FirstRowY = 66, HeaderCentreX = 117, HeaderY = 21,
        ArrowPressSound = 3014, ArrowReleaseSound = 3015, ThumbTopHeight = 5, ThumbBottomHeight = 7;

    /// <summary>The scroll bar's place and size (descriptor 0x5C8F40: x, y, width, height).</summary>
    public static (int X, int Y, int Width, int Height) Bar { get; } = (209, 60, 17, 255);

    /// <summary>The rectangle the wheel acts on (x, y, width, height).</summary>
    public static (int X, int Y, int Width, int Height) Wheel { get; } = (23, 66, 177, 240);

    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, UpArrowArt, DownArrowArt, ThumbTopArt, ThumbMiddleArt, ThumbBottomArt];

    /// <summary>A row's rectangle (table 0x5C8E50: the buttons 0x55D060 makes, art −1, panel-relative).</summary>
    public static (int X, int Y, int Width, int Height) Row(int row) => (RowX, FirstRowY + RowHeight * row, RowWidth, RowHeight);

    /// <summary>The row under a point, or −1.</summary>
    public static int RowAt(double x, double y)
    {
        for (var row = 0; row < Rows; row++)
            if (AreaCharacterSheetPagesLayout.Inside(Row(row), x, y)) return row;
        return -1;
    }

    /// <summary>The scheme a row shows with the list scrolled to <paramref name="scroll"/>, or null for a row past the last entry (its button exists and
    /// answers nothing).</summary>
    public static AreaSchemeEntry? EntryAt(AreaSchemesPageView view, int scroll, int row)
    {
        ArgumentNullException.ThrowIfNull(view);
        var index = scroll + row;
        return row >= 0 && row < view.ShownRows && index >= 0 && index < view.Entries.Count ? view.Entries[index] : null;
    }

    /// <summary>The scroll bar of a page: the arrows' measures come from the art (the exe reads them at the bar's creation), the value starts at <paramref name="scroll"/>.</summary>
    public static AreaSheetScrollBar MakeBar(AreaSchemesPageView view, AreaScrollArrows arrows, int scroll = 0)
    {
        ArgumentNullException.ThrowIfNull(view);
        var (x, y, width, height) = Bar;
        return new(x, y, width, height, 0, view.ScrollMaximum, 1, scroll, arrows, Wheel);
    }
}
