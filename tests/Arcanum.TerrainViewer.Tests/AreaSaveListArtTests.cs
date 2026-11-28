using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-062 save and load window art (ledger MW11–MW15): the art, the buttons' places, the rows' places, the scroll top, the
/// row a press picks and the "..." fit, on synthetic glyph frames; ARC-UI-067 (MW22–MW25): the info display's places, placements,
/// formats and line breaks.</summary>
[TestClass]
public sealed class AreaSaveListArtTests
{
    /// <summary>A font whose every glyph is 5 wide with an advance of 4, 19 high (frame 0 is character 31).</summary>
    private static AreaArtFont Font() =>
        new([.. Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, 19, new byte[5 * 19 * 4], 4))]);

    [TestMethod]
    public void The_window_reads_the_originals_art()
    {
        CollectionAssert.AreEqual(new[] { 745, 336, 335, 321, 323, 32, 229, 746, 747, 748, 27, 354, 444 }, AreaSaveListArtLayout.ArtNumbers.ToArray());
        Assert.AreEqual((42, 161, 145, 213), AreaSaveListArtLayout.List, "MW13: the window's (42, 120) below the top strip");
        Assert.AreEqual((281, 96, 468, 300), AreaSaveListArtLayout.Preview);
        Assert.AreEqual((213, 152, 12, 232), AreaSaveListArtLayout.ScrollBar);
        CollectionAssert.AreEqual(new[] { (200, 441, 400, 47), (0, 441, 200, 159), (600, 441, 200, 159) }, AreaSaveListArtLayout.BottomStripParts.ToArray());
        Assert.IsFalse(AreaSaveListArtContent.Empty("none").ArtReady);
        Assert.IsNull(AreaSaveListArtContent.Empty("none").Font);
        Assert.AreEqual("New", AreaSaveListArtContent.Empty("none").NewSaveLabel);
    }

    [TestMethod]
    public void The_buttons_stand_at_the_exes_places()
    {
        Assert.AreEqual((321, 675, 496), AreaSaveListArtLayout.Button(AreaSaveListArtButton.Confirm), "MW12: bottom strip (675, 55)");
        Assert.AreEqual((323, 69, 496), AreaSaveListArtLayout.Button(AreaSaveListArtButton.Back));
        Assert.AreEqual((32, 115, 396), AreaSaveListArtLayout.Button(AreaSaveListArtButton.Delete), "a window button: the screen's point");
        Assert.AreEqual(7021, AreaSaveListArtLayout.Hint(AreaSaveListArtButton.Confirm, AreaSaveListMode.Save));
        Assert.AreEqual(7020, AreaSaveListArtLayout.Hint(AreaSaveListArtButton.Confirm, AreaSaveListMode.Load));
        Assert.AreEqual(7002, AreaSaveListArtLayout.Hint(AreaSaveListArtButton.Back, AreaSaveListMode.Load));
        Assert.AreEqual(7022, AreaSaveListArtLayout.Hint(AreaSaveListArtButton.Delete, AreaSaveListMode.Save));
    }

    [TestMethod]
    public void Rows_stand_twenty_apart_and_eleven_show()
    {
        Assert.AreEqual((42, 161, 141, 20), AreaSaveListArtLayout.Row(0));
        Assert.AreEqual((42, 361, 141, 20), AreaSaveListArtLayout.Row(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaSaveListArtLayout.Row(11));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaSaveListArtLayout.Row(-1));
    }

    [TestMethod]
    public void The_scroll_top_follows_the_selection()
    {
        Assert.AreEqual(0, AreaSaveListArtLayout.ScrollTop(0, 10, 30), "the eleventh row is still shown");
        Assert.AreEqual(1, AreaSaveListArtLayout.ScrollTop(0, 11, 30), "Down past it scrolls it to the bottom (0x5430D0)");
        Assert.AreEqual(5, AreaSaveListArtLayout.ScrollTop(8, 5, 30), "Up above the top scrolls to it (0x543060)");
        Assert.AreEqual(8, AreaSaveListArtLayout.ScrollTop(8, -1, 30), "no selection keeps the top");
        Assert.AreEqual(4, AreaSaveListArtLayout.ScrollTop(20, 14, 15), "the top stays within the rows less eleven");
        Assert.AreEqual(0, AreaSaveListArtLayout.ScrollTop(3, 2, 5));
        Assert.AreEqual(0, AreaSaveListArtLayout.MaxScrollTop(11));
        Assert.AreEqual(1, AreaSaveListArtLayout.MaxScrollTop(12));
    }

    [TestMethod]
    public void A_press_picks_the_row_under_it()
    {
        Assert.AreEqual(0, AreaSaveListArtLayout.RowAt(42, 161, 0, 5, AreaSaveListMode.Load));
        Assert.AreEqual(4, AreaSaveListArtLayout.RowAt(100, 161 + 59, 2, 5, AreaSaveListMode.Load), "the top plus the rows below the list's top");
        Assert.AreEqual(-1, AreaSaveListArtLayout.RowAt(100, 161 + 100, 0, 3, AreaSaveListMode.Load), "past the end: none in the load window");
        Assert.AreEqual(3, AreaSaveListArtLayout.RowAt(100, 161 + 100, 0, 4, AreaSaveListMode.Save), "and the last row in the save window");
        Assert.IsNull(AreaSaveListArtLayout.RowAt(41, 170, 0, 5, AreaSaveListMode.Load), "left of the list");
        Assert.IsNull(AreaSaveListArtLayout.RowAt(187, 170, 0, 5, AreaSaveListMode.Load), "its right edge is outside");
        Assert.IsNull(AreaSaveListArtLayout.RowAt(100, 161 + 213, 0, 30, AreaSaveListMode.Load), "below the list");
    }

    [TestMethod]
    public void A_wide_description_ends_in_dots()
    {
        var font = Font();
        Assert.AreEqual(21, font.Measure("abcde").Width, "advances 4 each, the last its width 5");
        Assert.AreEqual("abcde", AreaSaveListArtLayout.Fit(font, "abcde", 22));
        Assert.AreEqual("a...", AreaSaveListArtLayout.Fit(font, "abcde", 21), "a text as wide as the row is cut (0x542D00 draws whole only when narrower)");
        // "..." measures 13; the room is 22 − 13 = 9: two letters (9).
        Assert.AreEqual("ab...", AreaSaveListArtLayout.Fit(font, "abcdef", 22));
        Assert.AreEqual("...", AreaSaveListArtLayout.Fit(font, "abcde", 10));
        Assert.AreEqual("", AreaSaveListArtLayout.Fit(font, "", 141));
    }

    private static readonly AreaSaveInfo Info = new("Bob", "Before the cave", 3, "Shrouded Hills", new(1885, 3, 14, 9, 5, 7), 1004, null);

    [TestMethod]
    public void The_info_texts_stand_at_the_exes_places()
    {
        Assert.AreEqual((499, 82), AreaSaveListArtLayout.Toggle, "MW25: a screen point (table flag 0)");
        Assert.AreEqual((84, 51, 89, 89), AreaSaveListArtLayout.Lens);
        Assert.AreEqual((484, 139), AreaSaveListArtLayout.Portrait);
        Assert.AreEqual((289, 233, 451, 18), AreaSaveListArtLayout.HeroNameText, "the window's (289, 192)");
        Assert.AreEqual(4, AreaSaveListArtLayout.StoryLines.Count);
        Assert.AreEqual((348, 333, 338, 18), AreaSaveListArtLayout.StoryLines[3]);

        var font = Font();
        var texts = AreaSaveListArtLayout.InfoTexts(font, Info, AreaSaveInfoMessages.Project, AreaSaveDateOrder.MonthDayYear);
        // Widths: n letters measure 4n + 1.
        CollectionAssert.AreEqual(new[]
        {
            new AreaSaveInfoText("Bob", 289 + (451 - 13) / 2, 233, false),
            new AreaSaveInfoText("Level 3", 547 + (109 - 29) / 2, 167, false),
            new AreaSaveInfoText("Before the cave", 294 + (441 - 61) / 2, 214, false),
            new AreaSaveInfoText("Shrouded Hills", 281 + (465 - 57) / 2, 253, false),
            new AreaSaveInfoText("3/14/1885", 448 - 37, 167, false),
            new AreaSaveInfoText("09:05:07", 448 - 33, 179, false),
        }, texts.ToArray(), "MW24: centred, the date and time ending at their rectangles' x");
        Assert.AreEqual("Unknown", AreaSaveListArtLayout.InfoTexts(font, Info with { Location = "" }, AreaSaveInfoMessages.Project, AreaSaveDateOrder.MonthDayYear)[3].Text,
            "a location without a name shows 5050");
        Assert.AreEqual(7, AreaSaveListArtLayout.InfoTexts(font, Info with { StoryText = "Find the ring" }, AreaSaveInfoMessages.Project, AreaSaveDateOrder.MonthDayYear).Count,
            "the story state's text adds its lines");
        Assert.AreEqual(new AreaSaveInfoText("Unable to read the save's info", 289 + (451 - 121) / 2, 233, true), AreaSaveListArtLayout.Unread(font, AreaSaveInfoMessages.Project));
    }

    [TestMethod]
    public void Placements_follow_the_exes_three_writers()
    {
        var font = Font();
        Assert.AreEqual(10 + (30 - 21) / 2, AreaSaveListArtLayout.Centred(font, "abcde", (10, 0, 30, 18)));
        Assert.AreEqual(10, AreaSaveListArtLayout.Centred(font, "abcde", (10, 0, 21, 18)), "as wide as the rectangle: from its x (0x542DF0)");
        Assert.AreEqual(100 - 21, AreaSaveListArtLayout.RightOf(font, "abcde", (100, 0, 22, 18)), "0x542EA0 ends the text at the x");
        Assert.AreEqual(100, AreaSaveListArtLayout.RightOf(font, "abcde", (100, 0, 21, 18)));
        Assert.AreEqual("Level 12", AreaSaveListArtLayout.Level("Level %d", 12));
        Assert.AreEqual("12 lvl", AreaSaveListArtLayout.Level("%d lvl", 12));
        Assert.AreEqual("Level", AreaSaveListArtLayout.Level("Level", 12), "no %d: as it is");
    }

    [TestMethod]
    public void A_story_text_breaks_at_spaces_over_four_lines()
    {
        var font = Font();
        (int, int, int, int)[] rects = [(0, 0, 30, 18), (0, 20, 30, 18), (0, 40, 30, 18), (0, 60, 30, 18)];
        // Each line must be narrower than 30: at most 7 letters (29).
        var lines = AreaSaveListArtLayout.Wrapped(font, "aa bb cc dd ee ff gg hh ii jj", rects);
        CollectionAssert.AreEqual(new[] { "aa bb", "cc dd", "ee ff", "gg hh" }, lines.Select(line => line.Text).ToArray(), "the rest after four lines is dropped");
        CollectionAssert.AreEqual(new[] { 0, 20, 40, 60 }, lines.Select(line => line.Y).ToArray());
        Assert.AreEqual((30 - 21) / 2, lines[0].X, "each line centred");
        CollectionAssert.AreEqual(new[] { "ab" }, AreaSaveListArtLayout.Wrapped(font, "ab", rects).Select(line => line.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "ab" }, AreaSaveListArtLayout.Wrapped(font, "ab abcdefghij cd", rects).Select(line => line.Text).ToArray(),
            "a word that fits no line ends the text (0x542F50 logs it)");
        Assert.AreEqual(0, AreaSaveListArtLayout.Wrapped(font, "abcdefghij", rects).Count);
    }

    [TestMethod]
    public void Dates_follow_the_locales_order()
    {
        var time = new GameSaveTime(1885, 3, 14, 23, 0, 59);
        Assert.AreEqual("3/14/1885", AreaSaveListArtLayout.Date(time, AreaSaveListArtLayout.DateOrder("M/d/yyyy")));
        Assert.AreEqual("14/3/1885", AreaSaveListArtLayout.Date(time, AreaSaveListArtLayout.DateOrder("dd.MM.yyyy")));
        Assert.AreEqual("1885/3/14", AreaSaveListArtLayout.Date(time, AreaSaveListArtLayout.DateOrder("yyyy-MM-dd")));
        Assert.AreEqual(AreaSaveDateOrder.MonthDayYear, AreaSaveListArtLayout.DateOrder(null), "month first when it cannot tell");
        Assert.AreEqual("23:00:59", AreaSaveListArtLayout.Time(time));
    }

    [TestMethod]
    public void A_saves_info_comes_from_its_details()
    {
        var summary = new GameSaveSummary("", "Bob", 3, "Shrouded Hills", 1, new(1885, 3, 14, 9, 5, 7), 1004, 2);
        var auto = new GameSaveEntry(GameSaveLibrary.AutoName, true, 10, DateTime.UnixEpoch, GameSaveDetails.Present, summary, false);
        var stories = new Dictionary<int, string> { [2] = "Find the ring" };
        Assert.AreEqual(Info with { Description = "Auto-Save", StoryText = "Find the ring" }, AreaSaveListArtLayout.Info(auto, stories),
            "the auto slot's fixed description; the story state's text");
        Assert.AreEqual("", AreaSaveListArtLayout.Info(auto with { Name = "slot0001", IsAuto = false }, stories)!.Description);
        Assert.IsNull(AreaSaveListArtLayout.Info(auto with { Summary = summary with { StoryState = null } }, stories)!.StoryText, "schema 1: no story state");
        Assert.IsNull(AreaSaveListArtLayout.Info(auto with { Summary = null, Details = GameSaveDetails.Missing }, stories), "no details: the info did not read");
    }
}
