using Arcanum.Data.Content;
using Arcanum.Data.Messages;

namespace Arcanum.TerrainViewer.Core;

/// <summary>The save and load window's art buttons (window descriptors 0x5C4868 and 0x5C4608, button tables 0x5C47A8 and 0x5C4548).</summary>
public enum AreaSaveListArtButton { Confirm, Back, Delete }

/// <summary>
/// ARC-UI-062: the original look of the save (8) and load (7) windows, read from their descriptors, opens 0x543380 and 0x541F20,
/// redraws 0x543990 and 0x5425C0 and the menu window build 0x546340 (ledger MW11–MW15). The background 745 `SaveLoadBackground`
/// (800 × 400) is the window at (0, 41) between the strips 336 and 335 (MW1). Three art buttons: Save / Load 321 `NextBut` and
/// Previous Screen 323 `BackBut` in the bottom strip, Delete Save Game 32 `cncl_big` in the window. The list stands in the window's
/// rectangle (42, 120, 145, 213): a row every 20 pixels from its top, eleven rows shown from the scroll top, each the save's
/// description in the 19-row art font 27 `morph15font`, near-white, the selected row gold; a description too wide for the row's 141
/// pixels ends in "...". Coordinates here are the 800 × 600 screen's. Godot-free.
/// </summary>
public static class AreaSaveListArtLayout
{
    public const int ScreenWidth = 800, ScreenHeight = 600;
    public const int BackgroundArt = 745, TopStripArt = 336, BottomStripArt = 335, ConfirmArt = 321, BackArt = 323, DeleteArt = 32, FontArt = 27;
    /// <summary>The window's top (a background not 600 rows high, MW1) and the bottom strip's (the art buttons' y origin, MW12).</summary>
    public const int WindowTop = 41, BottomStripTop = 441, TopStripHeight = 41, BottomStripHeight = 159;
    public const int RowHeight = 20, RowTextShrink = 4, ShownRows = 11, ListFillExtra = 5;
    /// <summary>MainMenu.mes: the new-save row's label "New" (5055) and the buttons' hints (7021 Save Game, 7020 Load Game, 7002
    /// Previous Screen, 7022 Delete Save Game).</summary>
    public const int NewSaveMessage = 5055, SaveHint = 7021, LoadHint = 7020, BackHint = 7002, DeleteHint = 7022;
    public const string Ellipsis = "...";

    /// <summary>The list's rectangle (descriptor +0x5C..+0x68, the window's (42, 120, 145, 213)) on the screen.</summary>
    public static (int X, int Y, int Width, int Height) List { get; } = (42, WindowTop + 120, 145, 213);
    /// <summary>The preview's rectangle (0x5C46C0, the window's (281, 55, 468 × 300)) on the screen.</summary>
    public static (int X, int Y, int Width, int Height) Preview { get; } = (281, WindowTop + 55, 468, 300);
    /// <summary>The scroll bar's rectangle (0x5C4798, the window's (213, 111, 12 × 232)) and the rectangle its wheel acts on
    /// (0x64C274: the window's (34, 110, 195 × 232)) on the screen.</summary>
    public static (int X, int Y, int Width, int Height) ScrollBar { get; } = (213, WindowTop + 111, 12, 232);
    public static (int X, int Y, int Width, int Height) WheelArea { get; } = (34, WindowTop + 110, 195, 232);

    /// <summary>The bottom strip's three windows (0x5C3680; MW1), each drawn from the same part of 335.</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> BottomStripParts { get; } =
        [(200, BottomStripTop, 400, 47), (0, BottomStripTop, 200, BottomStripHeight), (600, BottomStripTop, 200, BottomStripHeight)];

    /// <summary>The row colours: font 0x64C210 (250, 250, 250) and the selected row's 0x64C240 (255, 210, 0) (r, g, b; font build
    /// 0x540E3C..0x540FB0, MW3's channel order).</summary>
    public static (byte R, byte G, byte B) RowColour { get; } = (250, 250, 250);
    public static (byte R, byte G, byte B) SelectedColour { get; } = (255, 210, 0);

    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, TopStripArt, BottomStripArt, ConfirmArt, BackArt, DeleteArt, FontArt];

    /// <summary>A button's art and its top-left on the screen: the bottom-strip buttons (table flag 1) at (675, 55) and (69, 55) of the
    /// strip, Delete (flag 0) at the screen's (115, 396) (button create 0x5461C0, MW12).</summary>
    public static (int Art, int X, int Y) Button(AreaSaveListArtButton button) => button switch
    {
        AreaSaveListArtButton.Confirm => (ConfirmArt, 675, BottomStripTop + 55),
        AreaSaveListArtButton.Back => (BackArt, 69, BottomStripTop + 55),
        AreaSaveListArtButton.Delete => (DeleteArt, 115, 396),
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>The button's hint message in MainMenu.mes (the table's last field, MW12).</summary>
    public static int Hint(AreaSaveListArtButton button, AreaSaveListMode mode) => button switch
    {
        AreaSaveListArtButton.Confirm => mode == AreaSaveListMode.Save ? SaveHint : LoadHint,
        AreaSaveListArtButton.Back => BackHint,
        AreaSaveListArtButton.Delete => DeleteHint,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    /// <summary>The scroll top that keeps <paramref name="selected"/> shown: Up above the top scrolls to it, Down past the eleventh row
    /// scrolls it to the bottom (0x543060, 0x5430D0); the top stays within 0 and the row count less eleven (open 0x543380).</summary>
    public static int ScrollTop(int top, int selected, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        if (selected >= 0 && selected < top) top = selected;
        else if (selected >= top + ShownRows) top = selected - ShownRows + 1;
        return Math.Clamp(top, 0, MaxScrollTop(rows));
    }

    public static int MaxScrollTop(int rows) => Math.Max(0, rows - ShownRows);

    /// <summary>The text rectangle of the shown row <paramref name="slot"/> (0..10 from the scroll top): the list's x, its top plus 20 a
    /// row, the list's width less 4 (0x54276E..0x5427DB).</summary>
    public static (int X, int Y, int Width, int Height) Row(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, ShownRows);
        return (List.X, List.Y + RowHeight * slot, List.Width - RowTextShrink, RowHeight);
    }

    /// <summary>The row a press at a screen point picks, or null outside the list: the scroll top plus the point's distance below the
    /// list's top in rows (event handler 0x5471ED, callbacks 0x5424F0, 0x543920). A row past the end picks none in the load window
    /// and the last row in the save window.</summary>
    public static int? RowAt(double x, double y, int top, int rows, AreaSaveListMode mode)
    {
        var (left, upper, width, height) = List;
        if (x < left || x >= left + width || y < upper || y >= upper + height) return null;
        var row = top + (int)(y - upper) / RowHeight;
        return row < rows ? row : mode == AreaSaveListMode.Save ? rows - 1 : -1;
    }

    /// <summary>The text a row shows within <paramref name="width"/>: the whole text when narrower, else the longest start that leaves
    /// room for "..." followed by it (0x542D00).</summary>
    public static string Fit(AreaArtFont font, string text, int width)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        if (font.Measure(text).Width < width) return text;
        var room = width - font.Measure(Ellipsis).Width;
        var kept = text.Length;
        while (kept > 0 && font.Measure(text[..kept]).Width > room) kept--;
        return text[..kept] + Ellipsis;
    }

    public static bool Contains((int X, int Y, int Width, int Height) rect, double x, double y) =>
        x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
}

/// <summary>ARC-UI-062: the save window's art and the new-save row's label read from the game folder.</summary>
public sealed record AreaSaveListArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string NewSaveLabel, string Detail)
{
    /// <summary>True when every art read: the windows then show the original look (all or nothing, as every one is in the installed
    /// game).</summary>
    public bool ArtReady => AreaSaveListArtLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready
        && load.Frames.Count > 0);

    public AreaArtFont? Font => ArtReady ? new(Art[AreaSaveListArtLayout.FontArt].Frames) : null;

    /// <summary>PROJECT: the new-save row's label when MainMenu.mes does not give 5055.</summary>
    public const string ProjectNewSaveLabel = "New";

    public static AreaSaveListArtContent Empty(string reason) => new(new Dictionary<int, AreaInterfaceArtLoad>(), ProjectNewSaveLabel, reason);

    /// <summary>Reads the art and MainMenu.mes 5055 from the game folder of <paramref name="modulePath"/>; failures are reported,
    /// never thrown. The frame limit is raised to 256 for the font's 225 frames.</summary>
    public static AreaSaveListArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.Open(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaSaveListArtLayout.ArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256));
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        var label = ReadNewSaveLabel(stack, out var labelDetail);
        return new(art, label ?? ProjectNewSaveLabel,
            $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}; {labelDetail}");
    }

    private static string? ReadNewSaveLabel(ContentStack stack, out string detail)
    {
        if (AreaStartingHero.Table(stack, AreaMainMenuTexts.MainMenuPath, ContentLoadPhase.GameStart, required: true, out var read) is not { } table)
        {
            detail = $"{AreaMainMenuTexts.MainMenuPath} not read ({read})";
            return null;
        }
        var text = table.Find(AreaSaveListArtLayout.NewSaveMessage) is { } message
            && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } decoded } ? decoded : null;
        detail = $"label {AreaSaveListArtLayout.NewSaveMessage} {(text is null ? "missing" : "read")}";
        return text;
    }
}
