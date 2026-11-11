using System.Globalization;
using Arcanum.Data.Content;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.World;

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
/// ARC-UI-067 (ledger MW22–MW25): the preview rectangle shows the selected save's picture at its own size from the corner, its info
/// texts over it while the info display is on (the toggle button 747 `TextToggle`), and the cover 748 `SaveShotCover` over all; the
/// lens 746 `SaveLoadPCLens` stands at the window's (84, 10).
/// </summary>
public static class AreaSaveListArtLayout
{
    public const int ScreenWidth = 800, ScreenHeight = 600;
    public const int BackgroundArt = 745, TopStripArt = 336, BottomStripArt = 335, ConfirmArt = 321, BackArt = 323, DeleteArt = 32, FontArt = 27;
    /// <summary>ARC-UI-067: the lens, the info toggle (two frames) and the preview's cover (MW23, MW25).</summary>
    public const int LensArt = 746, ToggleArt = 747, CoverArt = 748;
    /// <summary>The window's top (a background not 600 rows high, MW1) and the bottom strip's (the art buttons' y origin, MW12).</summary>
    public const int WindowTop = 41, BottomStripTop = 441, TopStripHeight = 41, BottomStripHeight = 159;
    public const int RowHeight = 20, RowTextShrink = 4, ShownRows = 11, ListFillExtra = 5;
    /// <summary>MainMenu.mes: the new-save row's label "New" (5055) and the buttons' hints (7021 Save Game, 7020 Load Game, 7002
    /// Previous Screen, 7022 Delete Save Game).</summary>
    public const int NewSaveMessage = 5055, SaveHint = 7021, LoadHint = 7020, BackHint = 7002, DeleteHint = 7022;
    /// <summary>ARC-UI-067, MainMenu.mes: the info that did not read (5005), a location without a name (5050), the level's format (5051)
    /// and the toggle's hint (7025 "Toggle Info Display"); MW24, MW25.</summary>
    public const int UnreadMessage = 5005, UnknownLocationMessage = 5050, LevelMessage = 5051, ToggleHint = 7025;
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

    /// <summary>The info texts' colour: the error text's font 0x64C218 is art 27 in (255, 50, 50) (MW14, MW23); the others take the
    /// rows' (250, 250, 250).</summary>
    public static (byte R, byte G, byte B) WarningColour { get; } = (255, 50, 50);

    public static IReadOnlyList<int> ArtNumbers { get; } = [BackgroundArt, TopStripArt, BottomStripArt, ConfirmArt, BackArt, DeleteArt, FontArt, LensArt, ToggleArt, CoverArt];

    /// <summary>ARC-UI-067: the toggle's top-left (table 0x5C45D8, flag 0: the screen's point; MW25) and the lens's rectangle
    /// (0x5C4780, the window's (84, 10, 89 × 89)) on the screen.</summary>
    public static (int X, int Y) Toggle { get; } = (499, 82);
    public static (int X, int Y, int Width, int Height) Lens { get; } = (84, WindowTop + 10, 89, 89);

    /// <summary>ARC-UI-067: the info texts' rectangles (0x5C46D0..0x5C4770, the window's, 18 rows high) on the screen (MW24).</summary>
    public static (int X, int Y, int Width, int Height) HeroNameText { get; } = (289, WindowTop + 192, 451, 18);
    public static (int X, int Y, int Width, int Height) DescriptionText { get; } = (294, WindowTop + 173, 441, 18);
    public static (int X, int Y, int Width, int Height) LocationText { get; } = (281, WindowTop + 212, 465, 18);
    public static (int X, int Y, int Width, int Height) DateText { get; } = (448, WindowTop + 126, 139, 18);
    public static (int X, int Y, int Width, int Height) TimeText { get; } = (448, WindowTop + 138, 139, 18);
    public static (int X, int Y, int Width, int Height) LevelText { get; } = (547, WindowTop + 126, 109, 18);
    /// <summary>The portrait's top-left: the corner of 0x5C46E0 (its size is the image's own, PO3's size 0).</summary>
    public static (int X, int Y) Portrait { get; } = (484, WindowTop + 98);
    /// <summary>The story text's four lines, narrower down the preview.</summary>
    public static IReadOnlyList<(int X, int Y, int Width, int Height)> StoryLines { get; } =
        [(288, WindowTop + 232, 456, 18), (300, WindowTop + 252, 432, 18), (329, WindowTop + 272, 386, 18), (348, WindowTop + 292, 338, 18)];

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

    /// <summary>A text centred on its rectangle (0x542DF0): narrower than it, x moves by the free width halved (toward zero); otherwise
    /// it starts at the rectangle's x.</summary>
    public static int Centred(AreaArtFont font, string text, (int X, int Y, int Width, int Height) rect)
    {
        ArgumentNullException.ThrowIfNull(font);
        var width = font.Measure(text ?? throw new ArgumentNullException(nameof(text))).Width;
        return width < rect.Width ? rect.X + (rect.Width - width) / 2 : rect.X;
    }

    /// <summary>A text ending at its rectangle's x (0x542EA0): narrower than the rectangle, it starts its own width left of the x;
    /// otherwise at the x.</summary>
    public static int RightOf(AreaArtFont font, string text, (int X, int Y, int Width, int Height) rect)
    {
        ArgumentNullException.ThrowIfNull(font);
        var width = font.Measure(text ?? throw new ArgumentNullException(nameof(text))).Width;
        return width < rect.Width ? rect.X - width : rect.X;
    }

    /// <summary>A text broken over <paramref name="rects"/> (0x542F50), each line centred as <see cref="Centred"/>: a line is the rest of
    /// the text, cut at its last space while it is as wide as its rectangle or wider; the next line starts past that space. A line that
    /// does not fit and has no space left ends the text (the original logs it); so does the last rectangle.</summary>
    public static IReadOnlyList<AreaSaveInfoText> Wrapped(AreaArtFont font, string text, IReadOnlyList<(int X, int Y, int Width, int Height)> rects)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(rects);
        var lines = new List<AreaSaveInfoText>();
        var rest = text;
        foreach (var rect in rects)
        {
            var line = rest;
            int? cut = null;
            while (font.Measure(line).Width >= rect.Width)
            {
                var space = line.LastIndexOf(' ');
                if (space < 0) return lines;
                (line, cut) = (line[..space], space);
            }
            lines.Add(new(line, Centred(font, line, rect), rect.Y, false));
            if (cut is not { } at || at + 1 >= rest.Length) break;
            rest = rest[(at + 1)..];
        }
        return lines;
    }

    /// <summary>The order of the date's day, month and year: the user locale's (0x45AC70 asks LOCALE_IDATE; month first when it
    /// cannot tell), here from a .NET short date pattern by its first day, month or year letter.</summary>
    public static AreaSaveDateOrder DateOrder(string? shortDatePattern)
    {
        foreach (var c in shortDatePattern ?? "")
            switch (char.ToLowerInvariant(c))
            {
                case 'd': return AreaSaveDateOrder.DayMonthYear;
                case 'y': return AreaSaveDateOrder.YearMonthDay;
                case 'm': return AreaSaveDateOrder.MonthDayYear;
            }
        return AreaSaveDateOrder.MonthDayYear;
    }

    /// <summary>The game date as "%d/%d/%d" in the locale's order and the clock time as "%02d:%02d:%02d" (0x45AC70, 0x45AC20).</summary>
    public static string Date(GameSaveTime time, AreaSaveDateOrder order) => order switch
    {
        AreaSaveDateOrder.DayMonthYear => string.Create(CultureInfo.InvariantCulture, $"{time.Day}/{time.Month}/{time.Year}"),
        AreaSaveDateOrder.YearMonthDay => string.Create(CultureInfo.InvariantCulture, $"{time.Year}/{time.Month}/{time.Day}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{time.Month}/{time.Day}/{time.Year}"),
    };

    public static string Time(GameSaveTime time) => string.Create(CultureInfo.InvariantCulture, $"{time.Hour:D2}:{time.Minute:D2}:{time.Second:D2}");

    /// <summary>The level's text: the format 5051 with its first "%d" replaced by the level (the original's sprintf; a format without one
    /// is shown as it is).</summary>
    public static string Level(string format, int level)
    {
        ArgumentNullException.ThrowIfNull(format);
        var at = format.IndexOf("%d", StringComparison.Ordinal);
        return at < 0 ? format : string.Concat(format.AsSpan(0, at), level.ToString(CultureInfo.InvariantCulture), format.AsSpan(at + 2));
    }

    /// <summary>The info texts over the preview in the original's order (load redraw 0x542978..0x542AFA, save 0x543D77..0x543F04): the
    /// hero's name, the level, the description, the location (5050 when it has no name), the date and time right of nothing (ending at
    /// their rectangles' x) and the story state's text over four lines when it has one. The portrait is drawn by the caller between
    /// the name and the level.</summary>
    public static IReadOnlyList<AreaSaveInfoText> InfoTexts(AreaArtFont font, AreaSaveInfo info, AreaSaveInfoMessages messages, AreaSaveDateOrder order)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(messages);
        AreaSaveInfoText At(string text, (int X, int Y, int Width, int Height) rect) => new(text, Centred(font, text, rect), rect.Y, false);
        var level = Level(messages.LevelFormat, info.Level);
        var location = info.Location.Length > 0 ? info.Location : messages.UnknownLocation;
        var date = Date(info.Time, order);
        var time = Time(info.Time);
        List<AreaSaveInfoText> texts =
        [
            At(info.HeroName, HeroNameText), At(level, LevelText), At(info.Description, DescriptionText), At(location, LocationText),
            new(date, RightOf(font, date, DateText), DateText.Y, false), new(time, RightOf(font, time, TimeText), TimeText.Y, false),
        ];
        if (info.StoryText is { Length: > 0 } story) texts.AddRange(Wrapped(font, story, StoryLines));
        return texts;
    }

    /// <summary>What a listed save's info shows, or null when it has no readable details (the original's info read failing, MW23):
    /// the summary's texts, the auto slot's fixed description when it has none (the original's F7 passes "Auto-Save"), and the story
    /// state's text when <paramref name="stories"/> has one (a details file of schema 1 recorded no story state).</summary>
    public static AreaSaveInfo? Info(GameSaveEntry entry, IReadOnlyDictionary<int, string> stories)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(stories);
        if (entry.Summary is not { } summary) return null;
        var description = summary.Description.Length > 0 ? summary.Description : entry.IsAuto ? GameSaveLibrary.AutoDescription : "";
        var story = summary.StoryState is { } state && stories.TryGetValue(state, out var text) ? text : null;
        return new(summary.HeroName, description, summary.Level, summary.Location, summary.GameTime, summary.Portrait, story);
    }

    /// <summary>The info that did not read (0x404270 failed): 5005 in the warning colour, centred on the hero's name's rectangle.</summary>
    public static AreaSaveInfoText Unread(AreaArtFont font, AreaSaveInfoMessages messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return new(messages.Unread, Centred(font, messages.Unread, HeroNameText), HeroNameText.Y, true);
    }
}

/// <summary>ARC-UI-067: the order of a date's parts (MW24).</summary>
public enum AreaSaveDateOrder { MonthDayYear, DayMonthYear, YearMonthDay }

/// <summary>ARC-UI-067: one info text at its top-left on the screen; <see cref="Warning"/> draws it in the warning colour.</summary>
public sealed record AreaSaveInfoText(string Text, int X, int Y, bool Warning);

/// <summary>ARC-UI-067: what the selected save's info shows (the original's save info file, MW22; here the details file's summary).
/// <see cref="StoryText"/> is the story state's message in storystate.mes, null when it has none.</summary>
public sealed record AreaSaveInfo(string HeroName, string Description, int Level, string Location, GameSaveTime Time, int Portrait, string? StoryText);

/// <summary>ARC-UI-067: the MainMenu.mes texts of the info display (PROJECT English ones when the file lacks them).</summary>
public sealed record AreaSaveInfoMessages(string Unread, string UnknownLocation, string LevelFormat)
{
    public static AreaSaveInfoMessages Project { get; } = new("Unable to read the save's info", "Unknown", "Level %d");
}

/// <summary>ARC-UI-062: the save window's art and the new-save row's label read from the game folder; ARC-UI-067: the info display's
/// MainMenu.mes texts and storystate.mes's texts by story state.</summary>
public sealed record AreaSaveListArtContent(IReadOnlyDictionary<int, AreaInterfaceArtLoad> Art, string NewSaveLabel, string Detail,
    AreaSaveInfoMessages Messages, IReadOnlyDictionary<int, string> StoryTexts)
{
    /// <summary>The story state's file (the original's mes\storystate.mes, read by the script state's module load 0x444890; MW24).</summary>
    public const string StoryStatePath = "mes/storystate.mes";

    /// <summary>True when every art read: the windows then show the original look (all or nothing, as every one is in the installed
    /// game).</summary>
    public bool ArtReady => AreaSaveListArtLayout.ArtNumbers.All(number => Art.TryGetValue(number, out var load) && load.Status == AreaInterfaceArtStatus.Ready
        && load.Frames.Count > 0);

    public AreaArtFont? Font => ArtReady ? new(Art[AreaSaveListArtLayout.FontArt].Frames) : null;

    /// <summary>PROJECT: the new-save row's label when MainMenu.mes does not give 5055.</summary>
    public const string ProjectNewSaveLabel = "New";

    public static AreaSaveListArtContent Empty(string reason) =>
        new(new Dictionary<int, AreaInterfaceArtLoad>(), ProjectNewSaveLabel, reason, AreaSaveInfoMessages.Project, new Dictionary<int, string>());

    /// <summary>Reads the art, MainMenu.mes 5055, 5005, 5050 and 5051 and storystate.mes from the game folder of
    /// <paramref name="modulePath"/>; failures are reported, never thrown. The frame limit is raised to 256 for the font's 225 frames.</summary>
    public static AreaSaveListArtContent Load(string modulePath)
    {
        ArgumentNullException.ThrowIfNull(modulePath);
        var found = AreaContentRoot.Discover(modulePath);
        if (found.Status != AreaContentRootStatus.Ready) return Empty($"not read ({found.Status} {found.Detail})");
        var opened = ContentStack.OpenShared(found.Plan!);
        if (opened.Status != ContentStackStatus.Ready) return Empty($"not read (content stack {opened.Status})");
        using var stack = opened.Stack!;
        var art = AreaInterfaceArt.Load(stack, AreaSaveListArtLayout.ArtNumbers, new AreaInterfaceArtLimits(MaxFrames: 256));
        var failed = art.Values.Where(load => load.Status != AreaInterfaceArtStatus.Ready).Select(load => $"{load.Number} {load.Status} {load.Detail}").ToArray();
        var menu = AreaStartingHero.Table(stack, AreaMainMenuTexts.MainMenuPath, ContentLoadPhase.GameStart, required: true, out var menuRead);
        var label = Text(menu, AreaSaveListArtLayout.NewSaveMessage);
        var project = AreaSaveInfoMessages.Project;
        var messages = new AreaSaveInfoMessages(Text(menu, AreaSaveListArtLayout.UnreadMessage) ?? project.Unread,
            Text(menu, AreaSaveListArtLayout.UnknownLocationMessage) ?? project.UnknownLocation, Text(menu, AreaSaveListArtLayout.LevelMessage) ?? project.LevelFormat);
        var menuDetail = menu is null ? $"{AreaMainMenuTexts.MainMenuPath} not read ({menuRead})"
            : "texts " + string.Join(",", new[] { AreaSaveListArtLayout.NewSaveMessage, AreaSaveListArtLayout.UnreadMessage, AreaSaveListArtLayout.UnknownLocationMessage,
                AreaSaveListArtLayout.LevelMessage }.Select(number => $"{number} {(Text(menu, number) is null ? "missing" : "read")}"));
        // A module-load table (MW24): every source. required: true only names why it did not read; no story text is shown then.
        var story = AreaStartingHero.Table(stack, StoryStatePath, ContentLoadPhase.ModuleLoaded, required: true, out var storyRead);
        var stories = new Dictionary<int, string>();
        foreach (var message in story?.Messages ?? [])
            if (Text(story, message.Number) is { } text) stories[message.Number] = text;
        return new(art, label ?? ProjectNewSaveLabel,
            $"art {art.Count - failed.Length}/{art.Count} read{(failed.Length == 0 ? "" : " (" + string.Join("; ", failed) + ")")}; {menuDetail}; "
            + (story is null ? $"{StoryStatePath} not read ({storyRead})" : $"story texts {stories.Count}"), messages, stories);
    }

    private static string? Text(MesFile? table, int number) => table?.Find(number) is { } message
        && MesText.Decode(message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } decoded } ? decoded : null;
}
