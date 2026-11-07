using System.Globalization;
using Arcanum.Presentation.Hud;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-UI-077: the original look of the character sheet's spells page (window 0x64C7B0, art 31 <c>char_spells</c>, callback 0x55DC60, drawn by
/// 0x55CBC0), read from the retail 1.0.7.4 executable (ledger CHW17–CHW22). The window is 235 × 325 at the main window's (503, 63); every
/// coordinate here is <b>panel-relative</b> (the exe's tables hold screen coordinates, the drawers subtract the panel's origin). The page
/// has no college header, no points line and no mastery mark: sixteen college tabs of 24 × 24 (two rows), then the shown college's five
/// spells, each a 32 × 32 icon (the next spell and the ones above it greyed), the spell's name in Morph15 and "Minimum Level: n" in Pork12,
/// with a + over the next spell's icon and a − over the highest known one. Godot-free.
/// </summary>
public static class AreaCharacterSheetSpellsLayout
{
    public const int BackgroundArt = 31, PlusArt = AreaCharacterSheetPagesLayout.TechPlusArt, MinusArt = AreaCharacterSheetPagesLayout.TechMinusArt,
        NameFontArt = AreaCharacterSheetArtLayout.LabelFontArt, LevelFontArt = 28, Colleges = 16, Rows = 5, MinimumLevelText = 44;

    /// <summary>The art of each college's tab (table 0x5B55B0, from 0x4B1A80): 3 frames of 24 × 24. The spell icons of a college are the
    /// arts that follow it (table 0x5B5630): college art + 1 + the spell's row.</summary>
    private static readonly int[] CollegeArts = [11, 44, 38, 92, 50, 86, 56, 98, 62, 68, 74, 104, 110, 80, 116, 122];

    private static readonly int[] TabRowOne = [13, 38, 63, 88, 113, 138, 162, 187], TabRowTwo = [25, 50, 75, 100, 124, 149, 174, 199];
    private static readonly int[] IconY = [82, 130, 177, 225, 272], NameY = [76, 124, 171, 219, 266], LevelY = [97, 145, 192, 240, 287],
        RegionY = [79, 127, 174, 222, 269];

    public static IReadOnlyList<int> ArtNumbers { get; } =
    [
        BackgroundArt, PlusArt, MinusArt, LevelFontArt, .. CollegeArts.Order(),
        .. Enumerable.Range(0, Colleges).SelectMany(college => Enumerable.Range(0, Rows).Select(row => SpellArt(college, row))).Order(),
    ];

    public static int CollegeArt(int college) => CollegeArts[college];

    public static int SpellArt(int college, int row) => CollegeArts[college] + 1 + row;

    /// <summary>A college tab's top-left: the first eight at y 15, the other eight at y 40 (table 0x5C8630).</summary>
    public static (int X, int Y) CollegeTab(int college) => college < 8 ? (TabRowOne[college], 15) : (TabRowTwo[college - 8], 40);

    public static (int X, int Y) Icon(int row) => (18, IconY[row]);

    public static (int X, int Y) Name(int row) => (59, NameY[row]);

    public static (int X, int Y) Level(int row) => (59, LevelY[row]);

    /// <summary>A row's pointer region (table 0x5C8DC8): the icon and the two lines, 196 × 35.</summary>
    public static (int X, int Y, int Width, int Height) Region(int row) => (18, RegionY[row], 196, 35);

    public static int IconSize => 32;

    public static int TabSize => 24;

    /// <summary>The colour of a spell's name (0x55CD08..): all white at rank 5; otherwise the known ranks white, the next one gold, the ones
    /// above it grey.</summary>
    public static AreaSheetColour NameColour(int row, int rank) => rank >= Rows || row < rank ? AreaSheetColour.White
        : row == rank ? AreaSheetColour.Gold : AreaSheetColour.Grey;

    /// <summary>The colour of a spell's "Minimum Level" line: as the name, but the next one yellow.</summary>
    public static AreaSheetColour LevelColour(int row, int rank) => NameColour(row, rank) == AreaSheetColour.Gold ? AreaSheetColour.Yellow : NameColour(row, rank);

    /// <summary>Whether a row's icon is greyed: the next spell and the ones above it (the rows from the known rank on).</summary>
    public static bool Greyed(int row, int rank) => row >= rank;

    /// <summary>The + (learn) is shown over the next spell's icon below five known ranks; the − (unlearn) over the highest known one when some
    /// rank is known and, in the game (mode 1), the stored rank is above the editor's snapshot (0x55CF75).</summary>
    public static bool PlusShown(int rank) => rank < Rows;

    public static bool MinusShown(int rank, int floorRank) => rank > 0 && floorRank < rank;

    /// <summary>The line "Minimum Level: n" (<c>"%s: %d"</c> with charedit.mes 44).</summary>
    public static string LevelText(AreaCharacterSheetTexts texts, int minimumLevel)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return string.Format(CultureInfo.InvariantCulture, "{0}: {1}", texts.Hint(MinimumLevelText) ?? "Minimum Level", minimumLevel);
    }

    /// <summary>What the pointer is over. A + or − reads the spell it learns or unlearns (the next, the highest known); a region its row.</summary>
    public enum Part { CollegeTab, Plus, Minus, Row }

    public readonly record struct Hit(Part Part, int Index);

    /// <summary>The college tabs first (made with the window), then the + and − of the current draw, then the row regions
    /// (<see cref="AreaPageHit"/>'s rule: the first created wins, 0x5380F0). <paramref name="size"/> measures an art (width, height).</summary>
    public static Hit? HitAt(double x, double y, AreaSpellsPageView view, Func<int, (int Width, int Height)?> size)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(size);
        bool In((int X, int Y) place, int art)
        {
            var measured = size(art);
            return measured is { } s && AreaCharacterSheetPagesLayout.Inside((place.X, place.Y, s.Width, s.Height), x, y);
        }
        for (var college = 0; college < Colleges; college++)
            if (In(CollegeTab(college), CollegeArts[college])) return new(Part.CollegeTab, college);
        var rank = view.Rank;
        if (PlusShown(rank) && In(Icon(rank), PlusArt)) return new(Part.Plus, rank);
        if (MinusShown(rank, view.FloorRank) && In(Icon(rank - 1), MinusArt)) return new(Part.Minus, rank - 1);
        for (var row = 0; row < Rows; row++)
            if (AreaCharacterSheetPagesLayout.Inside(Region(row), x, y)) return new(Part.Row, row);
        return null;
    }

    /// <summary>The text bar's entry of a college tab (type 9, 0x55B004): the college's large icon (art 446 + college), its name and its
    /// description, or the mastery description for the mastered college. The entry's layout is not read (PROJECT: the spell hint's).</summary>
    public static AreaMessageEntry CollegeEntry(AreaCollegeTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return new(AreaMessageKind.SpellHint, tab.Name, Detail: tab.Description ?? string.Empty) { Icon = HotkeyBarRules.CollegeIcon(tab.College) };
    }
}
