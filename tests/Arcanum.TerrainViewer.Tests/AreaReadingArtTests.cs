using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Data.Written;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1877 the reading window in the original's look (ledger WR17–WR23) on synthetic glyph frames and invented texts: the layout read from the window's tables, the text draw's
/// breaking, the fit test, the book's pages, the newspaper's columns, the telegram's form and the document's structure.</summary>
[TestClass]
public sealed class AreaReadingArtTests
{
    /// <summary>A font whose every glyph is 5 wide with an advance of 6 and a height of <paramref name="height"/> (frame 0 is character 31).</summary>
    private static AreaArtFont Font(int height = 10) =>
        new([.. Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, height, new byte[5 * height * 4], 6))]);

    private static readonly Func<int, AreaArtFont> Fonts = _ => Font();
    private static AreaReadingMessage Message(string text, int font = 481, bool centred = false) => new(text, font, centred);
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    [TestMethod]
    public void The_window_follows_the_descriptor_and_the_tables()
    {
        Assert.AreEqual((0, 41, 800, 399), AreaReadingArtLayout.Window, "0x5CA478: 800 × 399 at (0, 41)");
        CollectionAssert.AreEqual(new[] { 494, 231 }, new[] { AreaReadingArtLayout.FrameArt, AreaReadingArtLayout.LensArt });
        Assert.AreEqual((495, 496), (AreaReadingArtLayout.PreviousArt, AreaReadingArtLayout.NextArt));
        Assert.AreEqual((132, 0), AreaReadingArtLayout.BackgroundCorner, "0x56C590 with the x offset 0x84");
        Assert.AreEqual((25, 24, 89), AreaReadingArtLayout.Lens, "0x5CA4A8, 0x5CA4B0");
        Assert.AreEqual(((213, 77), (675, 77)), (AreaReadingArtLayout.PreviousCorner, AreaReadingArtLayout.NextCorner));
        CollectionAssert.AreEqual(new[] { 477, 489, 487, 491, 633, 817 }, AreaReadingArtLayout.BackgroundArts.ToArray());
        Assert.AreEqual(new AreaReadingRect(227, 57, 200, 290), AreaReadingArtLayout.BookPages[0]);
        Assert.AreEqual(new AreaReadingRect(476, 57, 200, 290), AreaReadingArtLayout.BookPages[1]);
        Assert.AreEqual(new AreaReadingRect(349, 49, 215, 300), AreaReadingArtLayout.NoteText);
        Assert.AreEqual(new AreaReadingRect(269, 40, 428, 327), AreaReadingArtLayout.PlaqueText);
        Assert.AreEqual(new AreaReadingRect(327, 126, 250, 240), AreaReadingArtLayout.TelegramBody);
        Assert.AreEqual(new AreaReadingRect(335, 90, 234, 20), AreaReadingArtLayout.TelegramLastForm);
        Assert.AreEqual(11, AreaReadingArtLayout.TelegramForm.Count);
        Assert.AreEqual(new AreaReadingHeaderSlot(479, 450, 36, 1), AreaReadingArtLayout.TelegramForm[5]);
        Assert.AreEqual(6, AreaReadingArtLayout.Masthead.Count);
        Assert.AreEqual(484, AreaReadingArtLayout.Masthead[1].Font);
        Assert.AreEqual(815, AreaReadingArtLayout.AlternateMasthead[1].Font);
        Assert.AreEqual(new AreaReadingRect(186, 97, 255, 90), AreaReadingArtLayout.Headline);
        CollectionAssert.AreEqual(new[] { 188, 328, 460, 592 }, AreaReadingArtLayout.Columns.Select(c => c.X).ToArray());
        CollectionAssert.AreEqual(new[] { 175, 175, 270, 270 }, AreaReadingArtLayout.Columns.Select(c => c.Height).ToArray());
        Assert.IsTrue(AreaReadingArtLayout.LensContains(25, 24) && AreaReadingArtLayout.LensContains(113, 112) && !AreaReadingArtLayout.LensContains(114, 50) && !AreaReadingArtLayout.LensContains(24, 50));
        Assert.AreEqual(AreaReadingArtLayout.White, AreaReadingArtLayout.Colour(WrittenSubtype.Plaque));
        Assert.AreEqual(AreaReadingArtLayout.Black, AreaReadingArtLayout.Colour(WrittenSubtype.Book));
        Assert.IsFalse(AreaReadingArtLayout.RequiredArtNumbers.Contains(817) || AreaReadingArtLayout.RequiredArtNumbers.Contains(815), "the alternate newspaper's arts are optional");
    }

    [TestMethod]
    public void A_font_steps_its_lines_by_the_space_glyph_and_falls_back_to_the_tallest()
    {
        Assert.AreEqual(10, Font(10).LineHeight);
        var frames = Enumerable.Range(0, 225).Select(i => new AreaInterfaceFrame(5, i == 1 ? 12 : 8, new byte[5 * 12 * 4], 6)).ToArray();
        Assert.AreEqual(12, new AreaArtFont(frames).LineHeight, "frame 1 is the space");
        var noSpace = Enumerable.Range(0, 225).Select(i => new AreaInterfaceFrame(5, i == 1 ? 0 : i == 7 ? 9 : 4, new byte[5 * 9 * 4], 6)).ToArray();
        Assert.AreEqual(9, new AreaArtFont(noSpace).LineHeight);
        Assert.AreEqual(0, new AreaArtFont([]).LineHeight);
    }

    [TestMethod]
    public void A_text_is_broken_at_the_width_and_drawn_while_a_line_fits()
    {
        var font = Font();
        // Advance 6: "aaaa bbbb" is 9 characters; a width of 30 takes five characters (the last counts its width 5: 4 × 6 + 5 = 29).
        var lines = AreaReadingFlow.Draw(font, 481, "aaaa bbbb", new(10, 20, 30, 100), false);
        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual((10, 20), (lines[0].X, lines[0].Y));
        Assert.AreEqual((10, 30), (lines[1].X, lines[1].Y), "the next line is one line height down");
        Assert.AreEqual(5, lines[0].Glyphs.Count, "the first line holds its space");
        Assert.AreEqual(4, lines[1].Glyphs.Count);
        // A height of 25 holds two lines (rows = 25 / 10), 19 holds one, 9 none.
        Assert.AreEqual(2, AreaReadingFlow.Draw(font, 481, "aaaa bbbb cccc dddd", new(0, 0, 30, 25), false).Count);
        Assert.AreEqual(1, AreaReadingFlow.Draw(font, 481, "aaaa bbbb", new(0, 0, 30, 19), false).Count);
        Assert.AreEqual(0, AreaReadingFlow.Draw(font, 481, "aaaa bbbb", new(0, 0, 30, 9), false).Count);
        // A line feed ends a line; a word wider than the width with no break stops the draw.
        Assert.AreEqual(2, AreaReadingFlow.Draw(font, 481, "ab\ncd", new(0, 0, 100, 100), false).Count);
        Assert.AreEqual(0, AreaReadingFlow.Draw(font, 481, "abcdefghijkl", new(0, 0, 30, 100), false).Count);
        Assert.AreEqual(1, AreaReadingFlow.Draw(font, 481, "ab cdefghijkl", new(0, 0, 30, 100), false).Count, "the first line ends at the space; the long word stops the draw");
    }

    [TestMethod]
    public void A_centred_text_centres_each_line_by_its_width_without_the_space_it_breaks_at()
    {
        var font = Font();
        // "ab cd": at width 100 one line of five characters: advances 6 × 4 before the last = 4 × 6 = 24 for 'ab c' … the line width is the sum of the advances before the end (24 + 6 = 30).
        var one = AreaReadingFlow.Draw(font, 481, "ab", new(50, 0, 100, 30), true);
        Assert.AreEqual(50 + (100 - 12) / 2, one[0].X);
        // A line that breaks at a space is centred by its width without the space: "aaaa bbbb" at 30 → "aaaa" (width 24) then "bbbb".
        var two = AreaReadingFlow.Draw(font, 481, "aaaa bbbb", new(0, 0, 30, 30), true);
        Assert.AreEqual((30 - 24) / 2, two[0].X);
    }

    [TestMethod]
    public void The_height_of_a_text_is_its_lines_and_a_trailing_line_feed_counts_one_more()
    {
        var font = Font();
        Assert.AreEqual(0, AreaReadingFlow.Height(font, "", 30));
        Assert.AreEqual(10, AreaReadingFlow.Height(font, "ab", 30));
        Assert.AreEqual(20, AreaReadingFlow.Height(font, "aaaa bbbb", 30));
        Assert.AreEqual(20, AreaReadingFlow.Height(font, "ab\n", 100), "0x535390 counts the empty line after the last line feed");
        Assert.AreEqual(0, AreaReadingFlow.Height(font, "abcdefghijkl", 30), "a word wider than the width gives no height");
    }

    [TestMethod]
    public void The_fit_test_takes_the_whole_text_or_the_longest_prefix_that_ends_at_a_break()
    {
        var font = Font();
        var whole = AreaReadingFlow.FitText(font, 481, "aaaa bbbb", false, new(0, 0, 30, 20));
        Assert.AreEqual((true, 20, 2), (whole.Whole, whole.Amount, whole.Lines.Count));
        // Three lines do not fit in 25: the prefix of two lines, cut after the second space ("aaaa " is 5 characters, "aaaa bbbb " is 10).
        var cut = AreaReadingFlow.FitText(font, 481, "aaaa bbbb cccc", false, new(0, 0, 30, 25));
        Assert.AreEqual((false, 10, 2), (cut.Whole, cut.Amount, cut.Lines.Count), "the prefix includes the space it ends at");
        // A rectangle one line high keeps one line: "aaaa " .
        var one = AreaReadingFlow.FitText(font, 481, "aaaa bbbb cccc", false, new(0, 0, 30, 10));
        Assert.AreEqual((false, 5, 1), (one.Whole, one.Amount, one.Lines.Count));
        // No break fits: nothing is drawn and nothing consumed.
        var none = AreaReadingFlow.FitText(font, 481, "aaaa bbbb", false, new(0, 0, 30, 5));
        Assert.AreEqual((false, 0, 0), (none.Whole, none.Amount, none.Lines.Count));
        // A line feed breaks as a space does; the text's last character is never a break.
        var feed = AreaReadingFlow.FitText(font, 481, "ab\ncd\nef", false, new(0, 0, 100, 25));
        Assert.AreEqual((false, 6, 2), (feed.Whole, feed.Amount, feed.Lines.Count));
        var end = AreaReadingFlow.FitText(font, 481, "ab cd ", false, new(0, 0, 100, 5));
        Assert.IsFalse(end.Whole);
        // The scan tries the last character too (the loop's bound is the length, 0x56C800): the text "ab cd\n" measures two lines (the line feed adds one) and a rectangle of one line keeps
        // all of it, cut at the line feed after "ab cd"; stopping one short would keep "ab " only.
        var last = AreaReadingFlow.FitText(font, 481, "ab cd\n", false, new(0, 0, 100, 10));
        Assert.AreEqual((false, 6, 1), (last.Whole, last.Amount, last.Lines.Count));
    }

    [TestMethod]
    public void A_book_flows_its_messages_over_pages_and_goes_on_where_a_page_cut_a_message()
    {
        var word = "aaaa ";
        // Each message is 40 words: at width 200 (33 characters = 6 words a line: 6 × 5 + … ) a page of 290 holds 29 lines.
        var messages = new[] { Message(string.Concat(Enumerable.Repeat(word, 40))), Message(string.Concat(Enumerable.Repeat("bbbb ", 400))), Message("last message") };
        var pages = AreaReadingFlow.BookPages(messages, Fonts);
        Assert.IsTrue(pages.Count >= 3, $"pages {pages.Count}");
        Assert.IsNotNull(pages[0].Next, "the first page ends inside the second message");
        Assert.AreEqual(1, pages[0].Next!.Value.Index);
        Assert.IsTrue(pages[0].Lines.All(l => l.X == 227 && l.Y >= 57 && l.Y + 10 <= 57 + 290), "every line is inside the left page");
        Assert.IsTrue(pages[1].Lines.All(l => l.X == 476), "the second page is the right one");
        Assert.AreEqual(57, pages[1].Lines[0].Y);
        Assert.AreEqual(227, pages[2].Lines[0].X, "the third page is a left page again");
        Assert.IsNull(pages[^1].Next, "the last page took the rest");
        // The message after a cut starts where the page left it: a page of one message and a half.
        var rest = pages[0].Next!.Value;
        Assert.IsTrue(rest.Offset > 0 && rest.Offset % 5 == 0, "it is cut after a space: offset " + rest.Offset);
        // The messages' lines follow one another: the second message starts under the first.
        Assert.AreEqual(57 + 7 * 10, pages[0].Lines[7].Y, "the first message takes seven lines and the second starts right under it");
    }

    [TestMethod]
    public void A_book_stops_at_a_hundred_pages_and_an_empty_book_has_one_empty_page()
    {
        var long_ = new[] { Message(string.Concat(Enumerable.Repeat("word ", 20000))) };
        Assert.AreEqual(AreaReadingArtLayout.MaxPages, AreaReadingFlow.BookPages(long_, Fonts).Count);
        var empty = AreaReadingFlow.BookPages([], Fonts);
        Assert.AreEqual(1, empty.Count);
        Assert.AreEqual(0, empty[0].Lines.Count);
        Assert.IsNull(empty[0].Next);
        // A message with a word wider than the page measures as no height (0x535390): it takes no room and draws nothing, and the page ends with the messages.
        var unbreakable = AreaReadingFlow.BookPages([Message(new string('x', 200) + " end"), Message("after")], Fonts);
        Assert.AreEqual(1, unbreakable.Count);
        Assert.AreEqual(1, unbreakable[0].Lines.Count, "only the message after it is drawn");
        Assert.AreEqual(57, unbreakable[0].Lines[0].Y, "and it starts at the top of the page");
    }

    [TestMethod]
    public void A_centred_message_is_centred_in_the_page()
    {
        var page = AreaReadingFlow.Page([Message("ab", 481, true)], (0, 0), AreaReadingArtLayout.BookPages[0], Fonts);
        Assert.AreEqual(227 + (200 - 12) / 2, page.Lines[0].X, "the line's width is the sum of its advances (0x535C40 counts the last character's advance too)");
    }

    [TestMethod]
    public void A_newspaper_draws_its_masthead_the_headline_and_fills_the_columns_with_the_rest()
    {
        var header = Enumerable.Range(0, 6).Select(i => Message("M" + i, AreaReadingArtLayout.Masthead[i].Font)).ToArray();
        var body = new[] { Message("Head line", 482), Message(string.Concat(Enumerable.Repeat("aaaa ", 60)), 482), Message("tail", 482) };
        var fillers = Enumerable.Range(0, 80).Select(i => Message("filler " + i, 482)).ToArray();
        var document = new AreaReadingArtDocument(WrittenSubtype.Newspaper, 487, false, header, body, fillers, "");
        var lines = AreaReadingFlow.Newspaper(document, Fonts);
        // The masthead: six lines at their points (alignment 1 centres on x, 2 ends at it).
        Assert.AreEqual(194, lines[3].X, "alignment 0 starts at x");
        Assert.AreEqual(76, lines[3].Y);
        Assert.AreEqual(712 - 11, lines[5].X, "alignment 2 ends at x: width of 'M5' is 6 + 5 = 11");
        Assert.IsTrue(lines.Count > 12);
        // The headline is in its rectangle, the others in the four columns, none outside.
        Assert.IsTrue(lines.Skip(6).Any(l => l.X == 186 && l.Y == 97), "the headline");
        var columns = lines.Skip(7).GroupBy(l => AreaReadingArtLayout.Columns.ToList().FindIndex(c => c.X == l.X)).ToDictionary(g => g.Key, g => g.ToList());
        Assert.IsTrue(columns.ContainsKey(0) && columns.ContainsKey(1), "the long message spills into the second column");
        foreach (var (index, list) in columns)
        {
            Assert.IsTrue(index >= 0, "every body line is at a column's x");
            var column = AreaReadingArtLayout.Columns[index];
            Assert.IsTrue(list.All(l => l.Y >= column.Y && l.Y + 10 <= column.Y + column.Height), $"column {index} holds its lines");
        }
        Assert.IsTrue(columns.ContainsKey(3), "the fillers run to the last column");
        Assert.IsTrue(columns[3].Max(l => l.Y) + 10 > 103 + 270 - 20, "and fill it");
    }

    [TestMethod]
    public void A_newspaper_with_one_message_has_no_fillers_and_an_alternate_one_has_its_own_masthead()
    {
        var header = Enumerable.Range(0, 6).Select(i => Message("M" + i, AreaReadingArtLayout.AlternateMasthead[i].Font)).ToArray();
        var fillers = new[] { Message("filler") };
        var one = AreaReadingFlow.Newspaper(new(WrittenSubtype.Newspaper, 817, true, header, [Message("Head")], fillers, ""), Fonts);
        Assert.AreEqual(7, one.Count, "six masthead lines and the headline: no column text, no filler");
        Assert.AreEqual(815, one[1].Font, "the alternate masthead's second font");
        var none = AreaReadingFlow.Newspaper(new(WrittenSubtype.Newspaper, 487, false, header, [], fillers, ""), Fonts);
        Assert.AreEqual(6, none.Count);
        var two = AreaReadingFlow.Newspaper(new(WrittenSubtype.Newspaper, 487, false, [], [Message("Head"), Message("body")], fillers, ""), Fonts);
        Assert.AreEqual(3, two.Count, "the headline, the body and the filler under it");
        Assert.AreEqual((188, 196), (two[1].X, two[1].Y));
        Assert.AreEqual((188, 206), (two[2].X, two[2].Y), "the filler starts under the body");
    }

    [TestMethod]
    public void A_telegram_draws_its_form_at_the_points_and_the_body_in_its_rectangle()
    {
        var header = Enumerable.Range(0, 12).Select(i => Message("H" + i, 0)).ToArray();
        var document = new AreaReadingArtDocument(WrittenSubtype.Telegram, 491, false, header, [Message("body one"), Message("two")], [], "body one STOP two STOP");
        var lines = AreaReadingFlow.Telegram(document, Fonts);
        Assert.AreEqual(11 + 1 + 1, lines.Count);
        Assert.AreEqual((321, 50, 481), (lines[2].X, lines[2].Y, lines[2].Font), "form line 2: left at (321, 50)");
        Assert.AreEqual(450 - 11 / 2, lines[5].X, "the font 479 line is centred on 450 (the width of 'H5' is 6 + 5 = 11)");
        Assert.AreEqual(479, lines[5].Font);
        Assert.AreEqual((335, 90), (lines[11].X, lines[11].Y), "message 1011 in its own rectangle");
        Assert.AreEqual((327, 126, 480), (lines[12].X, lines[12].Y, lines[12].Font), "the body");
    }

    /// <summary>ARC-UI-2765: the address and signature rule on invented letters.</summary>
    [TestMethod]
    public void The_note_rule_splits_an_address_a_body_and_a_signature()
    {
        var one = AreaReadingNoteRule.Split("Dear X,\n   body one\n   body two\nYours, Y\n");
        Assert.AreEqual(("Dear X,", "   body one\n   body two", "Yours, Y"), (one!.Value.Heading, one.Value.Body, one.Value.Signature), "a short body line is not a closing line");
        var two = AreaReadingNoteRule.Split("Dear X,\nbody\nLove always,\nY");
        Assert.AreEqual(("body", "Love always,\nY"), (two!.Value.Body, two.Value.Signature), "two short last lines are the signature");
        var blank = AreaReadingNoteRule.Split("Dear X,\n\nbody\n\nForever yours,\n\nY\n");
        Assert.AreEqual(("\nbody\n", "Forever yours,\n\nY"), (blank!.Value.Body, blank.Value.Signature), "a closing line and the name after a blank line are one signature");
        var keepsBody = AreaReadingNoteRule.Split("Dear X,\nbody\nY");
        Assert.AreEqual("body", keepsBody!.Value.Body, "a body line stays");
        Assert.AreEqual("Y", keepsBody.Value.Signature);
        Assert.AreEqual("short", AreaReadingNoteRule.Check("Dear X,\nY").Reason);
        Assert.AreEqual("no-comma", AreaReadingNoteRule.Check("Dear X\nbody\nY").Reason);
        Assert.AreEqual("long-last", AreaReadingNoteRule.Check("Dear X,\nbody\n" + new string('w', 41)).Reason);
        Assert.AreEqual("no-body", AreaReadingNoteRule.Check("Dear X,\n\n\n   \nY").Reason, "blank lines are not a body");
    }

    /// <summary>ARC-UI-2765: a note's body on synthetic frames: Pepper20 (497) 10 high, the readable face (840) 8 high, every glyph advancing 6.</summary>
    [TestMethod]
    public void A_note_body_is_drawn_in_the_readable_face_between_a_handwritten_address_and_signature()
    {
        AreaArtFont FontOf(int art) => art == 840 ? Font(8) : Font(10);
        AreaReadingArtDocument Note(string text) => new(WrittenSubtype.Note, 489, false, [], [Message(text)], [], text);
        var letter = Note("Dear X,\nbody body body\nYours, Y");
        Assert.IsTrue(AreaReadingFlow.Single(letter, FontOf).All(line => line.Font == 497), "the original's look draws Pepper20");
        var mixed = AreaReadingFlow.Single(letter, FontOf, readableNote: true);
        CollectionAssert.AreEqual(new[] { 497, 840, 497 }, mixed.Select(line => line.Font).ToArray());
        Assert.AreEqual((349, 49), (mixed[0].X, mixed[0].Y));
        Assert.AreEqual(49 + 10 + 6, mixed[1].Y, "the body follows the address by a gap");
        Assert.IsTrue(mixed[2].X > mixed[0].X && mixed[2].Y > mixed[1].Y, "the signature stands to the right under the body");
        // A letter that does not fit the rule is drawn whole in the readable face.
        var plain = AreaReadingFlow.Single(Note("one line only"), FontOf, readableNote: true);
        Assert.IsTrue(plain.Count == 1 && plain[0].Font == 840);
        // Too long for the readable face: the original's draw. A word wider than the rectangle is no fit.
        Assert.IsTrue(AreaReadingFlow.Single(Note(string.Join(' ', Enumerable.Repeat("word", 400))), FontOf, readableNote: true).All(line => line.Font == 497));
        Assert.IsTrue(AreaReadingFlow.Single(Note("a " + new string('w', 40) + " b"), FontOf, readableNote: true).All(line => line.Font == 497), "an unbreakable word keeps the original's draw");
        // Other kinds ignore the flag.
        Assert.AreEqual(0, AreaReadingFlow.Single(new(WrittenSubtype.Picture, 700, false, [], [], [], ""), FontOf, readableNote: true).Count);
    }

    [TestMethod]
    public void A_note_and_a_plaque_draw_in_their_rectangles()
    {
        var note = AreaReadingFlow.Single(new(WrittenSubtype.Note, 489, false, [], [Message("a\n"), Message("b\n")], [], "a\nb\n"), Fonts);
        Assert.AreEqual(2, note.Count);
        Assert.AreEqual((349, 49, 497), (note[0].X, note[0].Y, note[0].Font));
        Assert.AreEqual((349, 59), (note[1].X, note[1].Y));
        var plaque = AreaReadingFlow.Single(new(WrittenSubtype.Plaque, 633, false, [], [Message("plaque text", 500, true)], [], ""), Fonts);
        Assert.AreEqual(1, plaque.Count);
        Assert.AreEqual(269 + (428 - 66) / 2, plaque[0].X, "eleven advances of 6");
        Assert.AreEqual(40, plaque[0].Y);
        Assert.AreEqual(0, AreaReadingFlow.Single(new(WrittenSubtype.Picture, 700, false, [], [], [], ""), Fonts).Count, "a picture has no text");
    }

    [TestMethod]
    public void A_header_line_aligns_to_its_point_and_an_empty_one_is_nothing()
    {
        var font = Font();
        // "ab": width 6 + 5 = 11.
        Assert.AreEqual(100, AreaReadingFlow.HeaderLine(font, 1, "ab", new(1, 100, 5, 0))!.X);
        Assert.AreEqual(100 - 5, AreaReadingFlow.HeaderLine(font, 1, "ab", new(1, 100, 5, 1))!.X);
        Assert.AreEqual(100 - 11, AreaReadingFlow.HeaderLine(font, 1, "ab", new(1, 100, 5, 2))!.X);
        Assert.IsNull(AreaReadingFlow.HeaderLine(font, 1, "", new(1, 100, 5, 0)));
    }

    [TestMethod]
    public void A_view_carries_the_structure_the_original_look_draws()
    {
        var texts = WrittenTexts.Create(
            Mes("{100}{12 First}{101}{1c Second}"),
            Mes("{200}{Note A}{201}{Note B}"),
            Mes("{0}{Mast A}{1}{Mast B}{2}{Mast C}{3}{Mast D}{4}{Mast E}{5}{Mast F}{10}{2 Filler ten}{11}{2 Filler eleven}{1300}{3 Headline}{1301}{4 Column}"),
            Mes(string.Concat(Enumerable.Range(1000, 12).Select(n => $"{{{n}}}{{H{n}}}")) + "{1012}{ STOP}{400}{Arrive}"),
            Mes("{500}{40c Plaque text}"));
        var profile = AreaHoverIndex.TextProfile;
        var book = AreaReading.View(texts.Read((int)WrittenSubtype.Book, 100), texts, 0, profile)!.Art!;
        Assert.AreEqual((WrittenSubtype.Book, 477, false), (book.Subtype, book.BackgroundArt, book.Alternate));
        CollectionAssert.AreEqual(new[] { 12, 1 }, book.Body.Select(m => m.Font).ToArray());
        CollectionAssert.AreEqual(new[] { false, true }, book.Body.Select(m => m.Centred).ToArray());
        var paper = AreaReading.View(texts.Read((int)WrittenSubtype.Newspaper, 1300), texts, 0, profile)!.Art!;
        Assert.AreEqual(6, paper.Header.Count);
        Assert.AreEqual(2, paper.Body.Count);
        Assert.IsTrue(paper.Fillers.Count > 2 && paper.Fillers.Count <= AreaReading.NewspaperFillerForArt, "the fillers wrap through the file's own: " + paper.Fillers.Count);
        var alternate = AreaReading.View(texts.Read((int)WrittenSubtype.Newspaper, 1300, alternateNewspaper: true), texts, 0, profile)!.Art!;
        Assert.IsTrue(alternate.Alternate && alternate.BackgroundArt == 817);
        Assert.AreEqual(815, alternate.Header[1].Font);
        var note = AreaReading.View(texts.Read((int)WrittenSubtype.Note, 200), texts, 0, profile)!.Art!;
        Assert.AreEqual("Note A\nNote B\n", note.Joined);
        var telegram = AreaReading.View(texts.Read((int)WrittenSubtype.Telegram, 400), texts, 0, profile)!.Art!;
        Assert.AreEqual(12, telegram.Header.Count);
        StringAssert.StartsWith(telegram.Joined, "Arrive");
        StringAssert.Contains(telegram.Joined, "STOP");
        var picture = AreaReading.View(texts.Read((int)WrittenSubtype.Picture, 817), texts, 0, profile)!.Art!;
        Assert.IsFalse(picture.Alternate, "a picture of art 817 is not the alternate newspaper");
        Assert.AreEqual(817, picture.BackgroundArt);
    }

    [TestMethod]
    public void The_written_texts_name_the_fonts_their_markup_needs()
    {
        var texts = WrittenTexts.Create(Mes("{100}{12 A}{101}{1c B}{102}{12 C}{103}{Plain}"), Mes("{200}{9 note}"), Mes("{0}{482 x}{1}{5 y}"), null, Mes("{500}{40c p}"));
        CollectionAssert.AreEqual(new[] { 1, 5, 12, 40, 482 }, texts.MarkupFonts().ToArray(), "the note's file has no markup; a message with no prefix is font 0 and left out");
        CollectionAssert.AreEqual(new[] { 1, 5, 12, 40, 479, 480, 481, 482, 484, 485, 497, 815, 840 }, AreaReadingArtLayout.FontArts(texts.MarkupFonts()).ToArray());
    }
}
