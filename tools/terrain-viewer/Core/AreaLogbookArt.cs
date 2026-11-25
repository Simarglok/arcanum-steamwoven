using Arcanum.Data.Content;
using Arcanum.World;

namespace Arcanum.TerrainViewer.Core;

/// <summary>One entry placed on a spread: its index in the tab's list, the column (0 left, 1 right), its top in the column, the lines
/// it shows and whether the column's bottom cut it short (the original removes trailing characters, ledger LB15).</summary>
public sealed record AreaLogbookPlaced(int Entry, int Column, int Y, int Lines, bool Truncated);

/// <summary>A spread of the book: the first and the last entry it consumes (<see cref="End"/> is <see cref="Start"/> − 1 for an empty
/// list) and where each one stands.</summary>
public sealed record AreaLogbookSpread(int Start, int End, IReadOnlyList<AreaLogbookPlaced> Placed);

/// <summary>
/// ARC-UI-074: the logbook window in the original's look, read from the window table 0x5C33F8 (16-byte rectangles), the create 0x53F0E0,
/// the panel draw 0x53F490, the spread redraw 0x53F6E0 and the spread layout 0x53F8F0 (ledger LB14–LB17, two independent readers). The
/// window is the game windows' 800 × 400 at the screen's (0, 41); every coordinate below is the window's. Godot-free.
/// </summary>
public static class AreaLogbookArtLayout
{
    /// <summary>The game windows' place on the 800 × 600 screen (between the strips).</summary>
    public static (int X, int Y, int Width, int Height) Window { get; } = (0, 41, 800, 400);

    /// <summary>The side panel 264 `LogBooks_Side` at (0, 0), the book 260 `Book_Main` at (165, 0) and the page art 261 at (172, 23) that
    /// every tab shows (the only art number the panel draw is called with, 0x53F2B0; 259 and 262 belong to other windows).</summary>
    public const int SideArt = 264, BookArt = 260, PageArt = 261;
    public static (int X, int Y) BookCorner { get; } = (165, 0);
    public static (int X, int Y) PageCorner { get; } = (172, 23);

    /// <summary>The seven tab buttons' arts and corners in the table's order, which is the tab index (the radio group 0x538670 selects
    /// the button at the current tab's index); the corner x is 696 but the fourth, 695.</summary>
    public static IReadOnlyList<(int Art, int X, int Y)> Tabs { get; } =
    [
        (265, 696, 83), (267, 696, 129), (271, 696, 175), (266, 695, 221), (270, 696, 267), (268, 696, 313), (269, 696, 359),
    ];

    /// <summary>The previous-page and next-page buttons' arts and corners (table entries 0x5C3428 and 0x5C3438).</summary>
    public const int PreviousArt = 272, NextArt = 273;
    public static (int X, int Y) PreviousCorner { get; } = (213, 77);
    public static (int X, int Y) NextCorner { get; } = (675, 77);

    /// <summary>The two text columns (0x5C34B8, 0x5C34C8), the two title rectangles (0x5C34D8, 0x5C34E8) and the two page-number
    /// rectangles (0x5C34F8, 0x5C3508).</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Columns { get; } = [(222, 72, 215, 270), (468, 72, 215, 270)];
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Titles { get; } = [(236, 43, 187, 19), (482, 43, 187, 19)];
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> PageNumbers { get; } = [(222, 346, 215, 15), (468, 346, 215, 15)];

    /// <summary>The original keeps 100 spread starts (0x64879C); a next page past the hundredth is refused.</summary>
    public const int MaxSpreads = 100;

    /// <summary>The text colours (the display-table levels of the styles built at 0x53EB10): black, blue (0, 0, 150), green (0, 120, 0), red (150, 0, 0).</summary>
    public static readonly (byte R, byte G, byte B) Black = (0, 0, 0), Blue = (0, 0, 150), Green = (0, 120, 0), Red = (150, 0, 0);

    /// <summary>Every art number the window needs for the original look.</summary>
    public static IReadOnlyList<int> ArtNumbers { get; } =
        new[] { SideArt, BookArt, PageArt, PreviousArt, NextArt }.Concat(Tabs.Select(tab => tab.Art)).ToArray();

    /// <summary>ARC-UI-098 (ledger LB23): the PC lens, the round view of the hero in the side panel. The create 0x53F0E0 makes art 198 over
    /// the table's rectangle 0x5C3418 (25, 24, 89, 89) for the interface's view-window mode (0x550DA0, 1, IW33): the game view's square around the
    /// hero is copied into the rectangle and the art is drawn over it. The art is no reason for the diagnostic list: the lens is drawn when it reads.</summary>
    public const int LensArt = 198;
    public static (int X, int Y, int Side) Lens => (AreaLensPlace.Corner((25, 24)).X, AreaLensPlace.Corner((25, 24)).Y, AreaLensPlace.Side); // ARC-UI-2714: the common place of the kit background

    /// <summary>Every art number the window reads: <see cref="ArtNumbers"/> and the lens's art.</summary>
    public static IReadOnlyList<int> LoadedArtNumbers { get; } = [.. ArtNumbers, LensArt];

    /// <summary>The lens's rectangle test (0x551000 over the window's point): left and top edges in, right and bottom out; a square test, the
    /// round art is not consulted.</summary>
    public static bool LensContains(double x, double y) => x >= Lens.X && x < Lens.X + Lens.Side && y >= Lens.Y && y < Lens.Y + Lens.Side;

    /// <summary>A style's text colour and whether the style strikes the text through (ledger LB16: struck rumors black, completed green,
    /// failed red; the strike line has the text's colour).</summary>
    public static ((byte R, byte G, byte B) Colour, bool Struck) Look(AreaLogbookStyle style) => style switch
    {
        AreaLogbookStyle.Struck => (Black, true),
        AreaLogbookStyle.Active => (Blue, false),
        AreaLogbookStyle.Completed => (Green, true),
        AreaLogbookStyle.Failed => (Red, true),
        AreaLogbookStyle.Blessing => (Blue, false),
        AreaLogbookStyle.Curse => (Red, false),
        _ => (Black, false),
    };

    /// <summary>The title text the original frames: "-title-" in both title rectangles (format 0x5C3588).</summary>
    public static string TitleText(string title) => "-" + title + "-";

    /// <summary>The page-number text the original frames: "-N-" (format 0x5C3580).</summary>
    public static string PageNumberText(int page) => "-" + page + "-";

    /// <summary>The left page's number of the spread with index <paramref name="spread"/> (the right one is one more): 1, 3, 5, …</summary>
    public static int LeftPage(int spread) => 2 * spread + 1;

    /// <summary>
    /// The spread that starts at entry <paramref name="start"/> of a list whose entries wrap to <paramref name="lineCounts"/> lines of
    /// <paramref name="lineHeight"/> pixels (the layout 0x53F8F0 with the entry draw 0x53FAD0, LB15). Entries go into the left column from
    /// its top, a gap of one text line between two entries (none after the list's last). An entry that does not fit the column's remaining
    /// height is not split: the left column hands it whole to the right column; the right column ends the spread and the entry starts the
    /// next one. Only the first entry of a fresh column is cut to the column's height. An entry that fills the column (its height plus the
    /// gap reaches the remaining height) closes it: the left column hands over to the right, the right ends the spread.
    /// </summary>
    public static AreaLogbookSpread Spread(IReadOnlyList<int> lineCounts, int lineHeight, int start)
    {
        ArgumentNullException.ThrowIfNull(lineCounts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        var placed = new List<AreaLogbookPlaced>();
        var column = 0;
        var y = 0;
        var fresh = true;
        var index = start;
        while (index < lineCounts.Count)
        {
            var height = Columns[column].Height;
            var remaining = height - y;
            var lines = lineCounts[index];
            if (lines <= 0)
            {
                // The original's empty entry (-1) only closes the left column; in the right column it ends the spread without advancing.
                if (column == 0) { column = 1; y = 0; fresh = true; index++; continue; }
                break;
            }
            var text = lines * lineHeight;
            var shown = lines;
            var cut = false;
            if (text > remaining && fresh && remaining / lineHeight is var room and > 0) { shown = room; text = room * lineHeight; cut = true; }
            if (text > remaining)
            {
                if (column == 0) { column = 1; y = 0; fresh = true; continue; }
                break;
            }
            placed.Add(new(index, column, y, shown, cut));
            var advance = text + (index < lineCounts.Count - 1 ? lineHeight : 0);
            index++;
            if (!cut && advance < remaining) { y += advance; fresh = false; continue; }
            if (column == 0) { column = 1; y = 0; fresh = true; continue; }
            break;
        }
        return new(start, index - 1, placed);
    }

    /// <summary>
    /// The background tab's entries (0x540130, ledger LB18): the text wrapped as the column wraps it is measured in whole; when it is taller than
    /// <paramref name="maxHeight"/> it is cut into pieces, each ending after the last space or line feed whose text before it still fits
    /// (the scan stops at the first break that does not), until the rest fits; the rest is the last piece. A text with no break that fits stays one
    /// piece (the layout then cuts it). <paramref name="height"/> is the wrapped height of a text in the column's width.
    /// </summary>
    public static IReadOnlyList<string> SplitPieces(string text, Func<string, int> height, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(height);
        var pieces = new List<string>();
        var rest = text;
        while (height(rest) > maxHeight)
        {
            var cut = 0;
            for (var i = 0; i < rest.Length; i++)
            {
                if (rest[i] is not (' ' or '\n')) continue;
                if (height(rest[..i]) > maxHeight) break;
                cut = i + 1;
            }
            if (cut == 0) break;
            pieces.Add(rest[..cut]);
            rest = rest[cut..];
        }
        pieces.Add(rest);
        return pieces;
    }

    /// <summary>The index of the spread that holds entry <paramref name="entry"/> as its start or after it: the last start not above it
    /// (a rebuilt open book redraws the spread it showed, LB15: the kept start entry, not the kept spread number).</summary>
    public static int SpreadOf(IReadOnlyList<int> starts, int entry)
    {
        ArgumentNullException.ThrowIfNull(starts);
        var found = 0;
        for (var i = 0; i < starts.Count; i++) if (starts[i] <= entry) found = i;
        return found;
    }

    /// <summary>The starts of the book's spreads (at most <see cref="MaxSpreads"/>): the first is 0, each next one the entry after the
    /// previous spread's last (0x53F640).</summary>
    public static IReadOnlyList<int> Starts(IReadOnlyList<int> lineCounts, int lineHeight)
    {
        var starts = new List<int> { 0 };
        while (starts.Count < MaxSpreads)
        {
            var spread = Spread(lineCounts, lineHeight, starts[^1]);
            if (spread.End >= lineCounts.Count - 1 || spread.End < starts[^1]) break;
            starts.Add(spread.End + 1);
        }
        return starts;
    }
}

/// <summary>ARC-UI-074: what the logbook window draws from the game folder: its interface art.</summary>
public sealed record AreaLogbookArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string Detail)
{
    /// <summary>True when every art number read: the window then shows the original look.</summary>
    public bool ArtReady => AreaLogbookArtLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready
        && load.Frames.Count > 0);

    public static AreaLogbookArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), reason);

    /// <summary>Reads the window's art from the game folder of <paramref name="modulePath"/>; failures are reported, never thrown.</summary>
    public static AreaLogbookArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaLogbookArtLayout.LoadedArtNumbers);
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        return new(art, $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}");
    }
}
