using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Data.Reputations;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-074: the logbook window's original look over invented values (no original data): the layout table, the spread
/// layout rules of 0x53F8F0, the tab order and the game folder's own labels.</summary>
[TestClass]
public sealed class AreaLogbookArtTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    [TestMethod]
    public void The_layout_is_the_window_table_of_the_original()
    {
        Assert.AreEqual((0, 41, 800, 400), AreaLogbookArtLayout.Window);
        Assert.AreEqual((264, 260, 261), (AreaLogbookArtLayout.SideArt, AreaLogbookArtLayout.BookArt, AreaLogbookArtLayout.PageArt));
        Assert.AreEqual((165, 0), AreaLogbookArtLayout.BookCorner);
        Assert.AreEqual((172, 23), AreaLogbookArtLayout.PageCorner);
        Assert.AreEqual(7, AreaLogbookArtLayout.Tabs.Count);
        CollectionAssert.AreEqual(new[] { 265, 267, 271, 266, 270, 268, 269 }, AreaLogbookArtLayout.Tabs.Select(tab => tab.Art).ToArray());
        CollectionAssert.AreEqual(new[] { 83, 129, 175, 221, 267, 313, 359 }, AreaLogbookArtLayout.Tabs.Select(tab => tab.Y).ToArray());
        Assert.AreEqual((695, 221), (AreaLogbookArtLayout.Tabs[3].X, AreaLogbookArtLayout.Tabs[3].Y));
        Assert.AreEqual((272, 273), (AreaLogbookArtLayout.PreviousArt, AreaLogbookArtLayout.NextArt));
        Assert.AreEqual((213, 77), AreaLogbookArtLayout.PreviousCorner);
        Assert.AreEqual((675, 77), AreaLogbookArtLayout.NextCorner);
        Assert.AreEqual((222, 72, 215, 270), AreaLogbookArtLayout.Columns[0]);
        Assert.AreEqual((468, 72, 215, 270), AreaLogbookArtLayout.Columns[1]);
        Assert.AreEqual((236, 43, 187, 19), AreaLogbookArtLayout.Titles[0]);
        Assert.AreEqual((482, 43, 187, 19), AreaLogbookArtLayout.Titles[1]);
        Assert.AreEqual((222, 346, 215, 15), AreaLogbookArtLayout.PageNumbers[0]);
        Assert.AreEqual((468, 346, 215, 15), AreaLogbookArtLayout.PageNumbers[1]);
        Assert.AreEqual(12, AreaLogbookArtLayout.ArtNumbers.Distinct().Count());
        Assert.AreEqual("-Quests-", AreaLogbookArtLayout.TitleText("Quests"));
        Assert.AreEqual("-3-", AreaLogbookArtLayout.PageNumberText(3));
        Assert.AreEqual(1, AreaLogbookArtLayout.LeftPage(0));
        Assert.AreEqual(5, AreaLogbookArtLayout.LeftPage(2));
    }

    [TestMethod]
    public void The_styles_are_the_originals_colours_with_a_strike_for_the_three_struck_ones()
    {
        Assert.AreEqual((AreaLogbookArtLayout.Black, false), AreaLogbookArtLayout.Look(AreaLogbookStyle.Plain));
        Assert.AreEqual((AreaLogbookArtLayout.Black, true), AreaLogbookArtLayout.Look(AreaLogbookStyle.Struck));
        Assert.AreEqual(((byte)0, (byte)0, (byte)150), AreaLogbookArtLayout.Look(AreaLogbookStyle.Active).Colour);
        Assert.AreEqual((((byte)0, (byte)120, (byte)0), true), AreaLogbookArtLayout.Look(AreaLogbookStyle.Completed));
        Assert.AreEqual((((byte)150, (byte)0, (byte)0), true), AreaLogbookArtLayout.Look(AreaLogbookStyle.Failed));
    }

    [TestMethod]
    public void Entries_fill_the_left_column_with_one_line_between_and_never_split()
    {
        // Line height 10, a column 270 high. Entries of 3 lines stand at 0, 40, 80, ... with a gap line between.
        var spread = AreaLogbookArtLayout.Spread([3, 3, 3], 10, 0);
        Assert.AreEqual((0, 2), (spread.Start, spread.End));
        CollectionAssert.AreEqual(new[] { 0, 40, 80 }, spread.Placed.Select(p => p.Y).ToArray());
        Assert.IsTrue(spread.Placed.All(p => p.Column == 0 && !p.Truncated && p.Lines == 3));

        // 20 lines (200) then a gap (210 < 270): the next entry of 10 lines (100) does not fit the 60 left and goes whole to the right column.
        var moved = AreaLogbookArtLayout.Spread([20, 10, 1], 10, 0);
        CollectionAssert.AreEqual(new[] { (0, 0, 0), (1, 1, 0), (2, 1, 110) }, moved.Placed.Select(p => (p.Entry, p.Column, p.Y)).ToArray());
        Assert.AreEqual(2, moved.End);
    }

    [TestMethod]
    public void An_entry_that_fills_a_column_closes_it_and_the_right_column_ends_the_spread()
    {
        // 26 lines = 260; with the gap 270 reaches the remaining height: the column is closed. The right column does the same and ends.
        var spread = AreaLogbookArtLayout.Spread([26, 26, 26], 10, 0);
        CollectionAssert.AreEqual(new[] { (0, 0), (1, 1) }, spread.Placed.Select(p => (p.Entry, p.Column)).ToArray());
        Assert.AreEqual(1, spread.End);
        // The next spread starts at the first entry not placed.
        CollectionAssert.AreEqual(new[] { 0, 2 }, AreaLogbookArtLayout.Starts([26, 26, 26], 10).ToArray());
        var second = AreaLogbookArtLayout.Spread([26, 26, 26], 10, 2);
        Assert.AreEqual((2, 2), (second.Start, second.End));
    }

    [TestMethod]
    public void An_entry_that_does_not_fit_the_right_column_starts_the_next_spread()
    {
        // Left: 20 lines. Right: 20 lines, then 10 lines do not fit the 60 left: the right column ends and the entry starts the next spread.
        var spread = AreaLogbookArtLayout.Spread([20, 20, 10], 10, 0);
        // 200 + 10 gap = 210 < 270 keeps the left column open; the second 20-line entry (200) does not fit the 60 left and goes right.
        CollectionAssert.AreEqual(new[] { (0, 0), (1, 1) }, spread.Placed.Select(p => (p.Entry, p.Column)).ToArray());
        // The third (100) fits the right column's remaining 60? No (210 used, 60 left): the spread ends at entry 1.
        Assert.AreEqual(1, spread.End);
        Assert.AreEqual(2, AreaLogbookArtLayout.Starts([20, 20, 10], 10)[1]);
    }

    [TestMethod]
    public void Only_the_first_entry_of_a_fresh_column_is_cut_at_the_columns_bottom()
    {
        var spread = AreaLogbookArtLayout.Spread([30, 2], 10, 0);
        Assert.AreEqual(new AreaLogbookPlaced(0, 0, 0, 27, true), spread.Placed[0]);
        // The cut entry counts as consumed and closes the column: the next goes to the right.
        Assert.AreEqual(new AreaLogbookPlaced(1, 1, 0, 2, false), spread.Placed[1]);
        Assert.AreEqual(1, spread.End);
        // Not fresh: the same 30 lines after another entry are handed to the right column and cut there.
        var late = AreaLogbookArtLayout.Spread([2, 30], 10, 0);
        Assert.AreEqual(new AreaLogbookPlaced(1, 1, 0, 27, true), late.Placed[1]);
    }

    [TestMethod]
    public void The_last_entry_has_no_gap_and_an_empty_list_has_an_empty_spread()
    {
        // 27 lines = 270 exactly fills the column as the last entry (no gap): placed, and the list ends.
        var last = AreaLogbookArtLayout.Spread([27], 10, 0);
        Assert.AreEqual(new AreaLogbookPlaced(0, 0, 0, 27, false), last.Placed.Single());
        Assert.AreEqual(0, last.End);
        var empty = AreaLogbookArtLayout.Spread([], 10, 0);
        Assert.AreEqual((0, -1), (empty.Start, empty.End));
        Assert.AreEqual(0, empty.Placed.Count);
        CollectionAssert.AreEqual(new[] { 0 }, AreaLogbookArtLayout.Starts([], 10).ToArray());
    }

    [TestMethod]
    public void The_book_keeps_at_most_a_hundred_spreads()
    {
        var starts = AreaLogbookArtLayout.Starts(Enumerable.Repeat(27, 400).ToArray(), 10);
        Assert.AreEqual(AreaLogbookArtLayout.MaxSpreads, starts.Count);
        Assert.AreEqual(2, starts[1]);
        Assert.AreEqual(198, starts[^1]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AreaLogbookArtLayout.Spread([1], 0, 0));
    }

    [TestMethod]
    public void A_rebuilt_book_finds_its_kept_spread_by_the_start_entry()
    {
        int[] starts = [0, 10, 25];
        Assert.AreEqual(0, AreaLogbookArtLayout.SpreadOf(starts, 0));
        Assert.AreEqual(1, AreaLogbookArtLayout.SpreadOf(starts, 10));
        Assert.AreEqual(1, AreaLogbookArtLayout.SpreadOf(starts, 24));
        Assert.AreEqual(2, AreaLogbookArtLayout.SpreadOf(starts, 99));
        Assert.AreEqual(0, AreaLogbookArtLayout.SpreadOf([0], 5));
    }

    [TestMethod]
    public void An_empty_entry_only_closes_the_left_column_and_a_line_taller_than_a_column_makes_no_progress()
    {
        // The original's empty entry (-1) closes the left column; in the right column it ends the spread.
        var spread = AreaLogbookArtLayout.Spread([0, 2, 0, 3], 10, 0);
        CollectionAssert.AreEqual(new[] { (1, 1) }, spread.Placed.Select(p => (p.Entry, p.Column)).ToArray());
        Assert.AreEqual(1, spread.End);
        // A line taller than the whole column cannot be placed at all: the spread consumes nothing and the book keeps one spread.
        var tall = AreaLogbookArtLayout.Spread([1], 300, 0);
        Assert.AreEqual(-1, tall.End);
        Assert.AreEqual(0, tall.Placed.Count);
        CollectionAssert.AreEqual(new[] { 0 }, AreaLogbookArtLayout.Starts([1, 1], 300).ToArray());
    }

    [TestMethod]
    public void A_background_text_is_cut_into_column_sized_pieces_at_the_last_break_that_fits()
    {
        // Height = the characters' count / 10 rounded up lines of 10; a column holds 30 (3 lines).
        static int Height(string text) => text.Length == 0 ? 0 : (text.Length + 9) / 10 * 10;
        // Fits whole: one piece.
        CollectionAssert.AreEqual(new[] { "short text" }, AreaLogbookArtLayout.SplitPieces("short text", Height, 30).ToArray());
        // 70 characters: the text before the last space at or under 30 characters fits; the piece keeps the space.
        var text = "aaaa bbbb cccc dddd eeee ffff gggg hhhh iiii jjjj kkkk llll mmmm nnnn";
        var pieces = AreaLogbookArtLayout.SplitPieces(text, Height, 30);
        Assert.IsTrue(pieces.Count >= 3);
        Assert.AreEqual(text, string.Concat(pieces));
        Assert.IsTrue(pieces.Take(pieces.Count - 1).All(piece => piece.EndsWith(' ') && Height(piece.TrimEnd()) <= 30));
        Assert.IsTrue(Height(pieces[^1]) <= 30);
        // A line feed is a break too; a text with no break that fits stays whole.
        CollectionAssert.AreEqual(new[] { "aaaaaaaaaaaaaaaaaaaa\n", "bbbbbbbbbbbbbbbbbbbb" }, AreaLogbookArtLayout.SplitPieces("aaaaaaaaaaaaaaaaaaaa\nbbbbbbbbbbbbbbbbbbbb", Height, 30).ToArray());
        CollectionAssert.AreEqual(new[] { new string('x', 100) }, AreaLogbookArtLayout.SplitPieces(new string('x', 100), Height, 30).ToArray());
        // Empty text is one empty piece; consecutive breaks stay inside the pieces and nothing is lost.
        CollectionAssert.AreEqual(new[] { string.Empty }, AreaLogbookArtLayout.SplitPieces(string.Empty, Height, 30).ToArray());
        var spaced = "aaaaaaaaaa  \n\n bbbbbbbbbb cccccccccc dddddddddd eeeeeeeeee";
        Assert.AreEqual(spaced, string.Concat(AreaLogbookArtLayout.SplitPieces(spaced, Height, 30)));
    }

    [TestMethod]
    public void The_reputations_tab_lists_the_pcs_reputations_in_stored_order_with_their_names()
    {
        using var session = ConversationSessionFixture.Create();
        session.AddReputation(session.Version, 1005);
        Assert.AreEqual(ClockMutationDisposition.Changed, session.JumpClocks(session.QueryClockSnapshot().Token, 3_600_000).Disposition);
        session.AddReputation(session.Version, 1002);
        var names = Mes("{1002}{Hero of Tarant}\n{1005}{Thief}");
        var table = ReputationTable.Create(Mes("{1002}{1 2 3}\n{1005}{4 5 6}"), names).Table!;
        var texts = LogbookTexts.Create(true, reputations: table);
        var view = AreaLogbook.Build(session, texts, AreaLogbookTab.Reputation, MesTextProfile.Windows1252).View!;
        CollectionAssert.AreEqual(new[] { 1005, 1002 }, view.Entries.Select(e => e.Number).ToArray(), "stored order, not sorted");
        CollectionAssert.AreEqual(new[] { "Thief", "Hero of Tarant" }, view.Entries.Select(e => e.Text).ToArray());
        Assert.IsTrue(view.Entries.All(e => e.Style == AreaLogbookStyle.Plain && e.State is null));
        Assert.StartsWith("January 1, 1885  3:00pm", AreaLogbook.Line(view.Entries[0]));
        Assert.AreEqual("no hero: normal texts", view.Note);
        // Without the table the entries stay, with no text and a note.
        var bare = AreaLogbook.Build(session, LogbookTexts.Create(true), AreaLogbookTab.Reputation, MesTextProfile.Windows1252).View!;
        Assert.AreEqual(2, bare.Entries.Count);
        Assert.IsTrue(bare.Entries.All(e => e.Text.Length == 0));
        Assert.AreEqual("no reputation names loaded; no hero: normal texts", bare.Note);
        // Quests and rumors are unchanged by the new tabs.
        Assert.AreEqual(0, AreaLogbook.Build(session, texts, AreaLogbookTab.Quests, MesTextProfile.Windows1252).View!.Entries.Count);
    }

    [TestMethod]
    public void The_background_tab_shows_the_heros_description_body_without_a_date_line()
    {
        var backgrounds = BackgroundTable.Create(Mes("{10}{1000}"), Mes("{1000}{Name\nThe long story body.}"));
        var texts = LogbookTexts.Create(true, backgrounds: backgrounds);
        using var none = ConversationSessionFixture.Create();
        var empty = AreaLogbook.Build(none, texts, AreaLogbookTab.Background, MesTextProfile.Windows1252).View!;
        Assert.AreEqual(0, empty.Entries.Count, "no hero, no background");
        Assert.AreEqual("no hero: no background; no hero: normal texts", empty.Note);
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 385, []);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load);
        using var session = created.Owner!;
        var view = AreaLogbook.Build(session, texts, AreaLogbookTab.Background, MesTextProfile.Windows1252).View!;
        var entry = view.Entries.Single();
        Assert.AreEqual("The long story body.", entry.Text);
        Assert.AreEqual("The long story body.", AreaLogbook.Line(entry), "a background has no date line");
        Assert.AreEqual(AreaLogbookStyle.Plain, entry.Style);
        Assert.IsNull(view.Note);
        Assert.AreEqual("no background texts loaded", AreaLogbook.Build(session, LogbookTexts.Create(true), AreaLogbookTab.Background, MesTextProfile.Windows1252).View!.Note);
    }

    [TestMethod]
    public void The_tab_order_is_the_originals_and_the_switch_skips_the_tabs_not_built()
    {
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6 }, Enum.GetValues<AreaLogbookTab>().Select(tab => (int)tab).ToArray());
        Assert.IsTrue(new[] { AreaLogbookTab.Rumors, AreaLogbookTab.Quests, AreaLogbookTab.Reputation, AreaLogbookTab.Background }.All(AreaLogbook.Built));
        Assert.IsFalse(AreaLogbook.Built(AreaLogbookTab.Keys) || AreaLogbook.Built(AreaLogbookTab.BlessingsCurses) || AreaLogbook.Built(AreaLogbookTab.KillsInjuries));
        Assert.AreEqual(AreaLogbookTab.Quests, AreaLogbook.Next(AreaLogbookTab.Rumors));
        Assert.AreEqual(AreaLogbookTab.Reputation, AreaLogbook.Next(AreaLogbookTab.Quests));
        Assert.AreEqual(AreaLogbookTab.Background, AreaLogbook.Next(AreaLogbookTab.Reputation));
        Assert.AreEqual(AreaLogbookTab.Rumors, AreaLogbook.Next(AreaLogbookTab.Background));
        Assert.AreEqual(AreaLogbookTab.Background, AreaLogbook.Next(AreaLogbookTab.BlessingsCurses));
        Assert.AreEqual(AreaLogbookTab.Rumors, AreaLogbook.Next(AreaLogbookTab.Keys));
        Assert.AreEqual("Reputation", AreaLogbook.TabName(AreaLogbookTab.Reputation));
    }

    [TestMethod]
    public void The_date_line_takes_the_game_folders_month_names_when_given_twelve()
    {
        var months = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" };
        Assert.AreEqual("B 1, 1885  12:00am    ", AreaLogbook.Date(30L * 86_400_000, 1885, months));
        Assert.ThrowsExactly<ArgumentException>(() => AreaLogbook.Date(0, 1885, ["A"]));
        Assert.AreEqual("January 1, 1885  12:00am    ", AreaLogbook.Date(0, 1885, null));
    }

    [TestMethod]
    public void The_logbook_reads_its_labels_from_the_game_folders_ui_file_when_it_has_one()
    {
        using var session = ConversationSessionFixture.Create();
        session.SetQuestState(session.Version, 1001, SessionQuestState.Accepted);
        var ui = new StringBuilder("{0}{Gossip}\n{1}{Deeds}\n");
        for (var month = 1; month <= 12; month++) ui.Append($"{{{6 + month}}}{{Month{month}}}\n");
        for (var state = 1; state <= 6; state++) ui.Append($"{{{19 + state}}}{{State{state}}}\n");
        var texts = LogbookTexts.Create(true, Mes("{1001}{Quest B}"), null, Mes("{20040}{R}"), null, Mes(ui.ToString()));
        var view = AreaLogbook.Build(session, texts, AreaLogbookTab.Quests, MesTextProfile.Windows1252).View!;
        Assert.AreEqual("Deeds", view.Title);
        Assert.AreEqual("State2", view.Entries.Single().State);
        Assert.StartsWith("Month1 1,", view.Entries.Single().Date);
        Assert.AreEqual("Gossip", AreaLogbook.Build(session, texts, AreaLogbookTab.Rumors, MesTextProfile.Windows1252).View!.Title);
        // Without the file, or with a file missing a month, the project's labels stay.
        var plain = AreaLogbook.Build(session, LogbookTexts.Create(true, Mes("{1001}{Quest B}")), AreaLogbookTab.Quests, MesTextProfile.Windows1252).View!;
        Assert.IsNull(plain.Title);
        Assert.AreEqual("accepted", plain.Entries.Single().State);
        Assert.StartsWith("January 1,", plain.Entries.Single().Date);
        // A tab that is not built is an empty page with a note.
        var keys = AreaLogbook.Build(session, texts, AreaLogbookTab.Keys, MesTextProfile.Windows1252).View!;
        Assert.AreEqual(0, keys.Entries.Count);
        Assert.IsTrue(keys.Note!.Contains("not built", StringComparison.Ordinal));
    }
}
