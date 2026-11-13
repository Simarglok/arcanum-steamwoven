using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Data.Written;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Written;

/// <summary>ARC-UI-049: the reading window's texts (ledger WR rows) over invented MES files (no game text).</summary>
[TestClass]
public sealed class WrittenTextsTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private static WrittenTexts Texts() => WrittenTexts.Create(
        Mes("{100}{12 Page one.}{101}{12c Centred.}{102}{7 Third.}{104}{13 After the gap.}"),
        Mes("{200}{Note line A}{201}{Note line B}"),
        Mes("{0}{Mast A}{1}{Mast B}{2}{Mast C}{3}{Mast D}{4}{Alt B}{5}{Alt D}{10}{20 Filler ten}{11}{20 Filler eleven}{12}{20 Filler twelve}{1300}{30 Headline}{1301}{31 Column}"),
        Mes(string.Concat(Enumerable.Range(1000, 12).Select(n => $"{{{n}}}{{H{n}}}")) + "{1012}{ STOP}{400}{Arrive}{401}{Soon}"),
        Mes("{500}{40c Plaque text}"));

    [TestMethod]
    public void A_book_shows_its_start_line_and_the_next_nine_to_the_first_gap_with_markup()
    {
        var book = Texts().Read((int)WrittenSubtype.Book, 100);
        Assert.AreEqual((WrittenReadStatus.Read, 477, 3), (book.Status, book.BackgroundArt, book.Body.Count), "104 is after the gap at 103");
        Assert.AreEqual(new WrittenMessage(100, "Page one.", 12, false), book.Body[0]);
        Assert.AreEqual(new WrittenMessage(101, "Centred.", 12, true), book.Body[1]);
        Assert.AreEqual(0, book.Header.Count);
        Assert.AreEqual(0, Texts().Read(0, 105).Body.Count, "no message at the start line: nothing");
    }

    [TestMethod]
    public void The_markup_takes_a_font_a_centre_mark_and_always_one_more_character()
    {
        Assert.AreEqual(new WrittenMessage(1, "ext", 0, false), WrittenTexts.Markup(1, "Text"), "no prefix: font 0 and the first character lost");
        Assert.AreEqual(new WrittenMessage(1, "x", 5, true), WrittenTexts.Markup(1, "5Cx"), "the tested character is the one dropped");
        Assert.AreEqual(new WrittenMessage(1, "Title", 0, true), WrittenTexts.Markup(1, "cTitle"), "no prefix: a leading c centres and is dropped");
        Assert.AreEqual(new WrittenMessage(1, "5 A", 0, false), WrittenTexts.Markup(1, "-5 A"), "atoi reads -5 (font 0), the digit loop stops at the sign, which is dropped");
        Assert.AreEqual(new WrittenMessage(1, " B", 3, false), WrittenTexts.Markup(1, "3\t  B"), "the tested tab, then one whitespace character only");
        Assert.AreEqual(new WrittenMessage(1, "Tx", 5, true), WrittenTexts.Markup(1, "5C Tx"));
        Assert.AreEqual(new WrittenMessage(1, "Y", 9, false), WrittenTexts.Markup(1, "9 Y"));
        Assert.AreEqual(new WrittenMessage(1, "Z", 0, false), WrittenTexts.Markup(1, "5000 Z"), "an art beyond 4095 reads as font 0 (both modes)");
        Assert.AreEqual(new WrittenMessage(1, "", 0, false), WrittenTexts.Markup(1, ""));
    }

    // ARC-TECHNOLOGY-009, WRD3 (owner decision 2026-10-21): Steamwoven keeps a message without a font prefix whole; Original (and no
    // rules, the tool's view) drops its first character as the executable does.
    [TestMethod]
    public void A_message_without_a_font_prefix_keeps_its_first_character_in_steamwoven()
    {
        var (steamwoven, original) = (WrittenRuleProfile.Steamwoven, WrittenRuleProfile.Original);
        Assert.AreEqual(new WrittenMessage(1, "Text", 0, false), WrittenTexts.Markup(1, "Text", steamwoven));
        Assert.AreEqual(new WrittenMessage(1, "ext", 0, false), WrittenTexts.Markup(1, "Text", original));
        Assert.AreEqual(WrittenTexts.Markup(1, "Text", original), WrittenTexts.Markup(1, "Text"), "no rules read as the original");
        Assert.AreEqual(new WrittenMessage(1, "cTitle", 0, false), WrittenTexts.Markup(1, "cTitle", steamwoven), "no markup: the c stays and does not centre");
        Assert.AreEqual(new WrittenMessage(1, "-5 A", 0, false), WrittenTexts.Markup(1, "-5 A", steamwoven), "a sign is no prefix");
        Assert.AreEqual(new WrittenMessage(1, " 7 B", 0, false), WrittenTexts.Markup(1, " 7 B", steamwoven), "nor is leading white space");
        Assert.AreEqual(new WrittenMessage(1, "", 0, false), WrittenTexts.Markup(1, "", steamwoven));
        foreach (var rules in WrittenRuleProfile.All)
        {
            Assert.AreEqual(new WrittenMessage(1, "x", 5, true), WrittenTexts.Markup(1, "5Cx", rules), "a prefixed message is the same in both");
            Assert.AreEqual(new WrittenMessage(1, "Z", 0, false), WrittenTexts.Markup(1, "5000 Z", rules));
        }
        var texts = WrittenTexts.Create(Mes("{100}{Plain start}{101}{12 Page}"), null, Mes("{10}{Filler}{1300}{30 Headline}"), null, Mes("{500}{Plaque}"));
        Assert.AreEqual("Plain start|Page", string.Join("|", texts.Read(0, 100, rules: steamwoven).Body.Select(m => m.Text)));
        Assert.AreEqual("lain start|Page", string.Join("|", texts.Read(0, 100, rules: original).Body.Select(m => m.Text)));
        Assert.AreEqual("Plaque", texts.Read(6, 500, rules: steamwoven).Body.Single().Text);
        Assert.AreEqual("laque", texts.Read(6, 500, rules: original).Body.Single().Text);
        Assert.AreEqual(("Filler", "iller"), (texts.NewspaperFiller(1300, 1, steamwoven).Single().Text, texts.NewspaperFiller(1300, 1, original).Single().Text));
        Assert.AreEqual(("steamwoven", "original"), (WrittenRuleProfile.FromName("steamwoven")!.Name, WrittenRuleProfile.FromName("original")!.Name));
        Assert.IsNull(WrittenRuleProfile.FromName("Original"));
    }

    [TestMethod]
    public void Notes_and_telegrams_are_joined_without_markup_and_telegrams_carry_their_headers()
    {
        var note = Texts().Read((int)WrittenSubtype.Note, 200);
        Assert.AreEqual(("Note line A\n", "Note line B\n", 489), (note.Body[0].Text, note.Body[1].Text, note.BackgroundArt));
        var telegram = Texts().Read((int)WrittenSubtype.Telegram, 400);
        Assert.AreEqual(("Arrive STOP", "Soon STOP"), (telegram.Body[0].Text, telegram.Body[1].Text), "each message followed by 1012");
        CollectionAssert.AreEqual(Enumerable.Range(1000, 12).ToArray(), telegram.Header.Select(h => h.Number).ToArray());
        Assert.AreEqual("H1011", telegram.Header[^1].Text);
    }

    [TestMethod]
    public void A_newspaper_has_its_masthead_headline_columns_and_wrapping_filler()
    {
        var texts = Texts();
        var paper = texts.Read((int)WrittenSubtype.Newspaper, 1300);
        Assert.AreEqual((487, "Headline", 30), (paper.BackgroundArt, paper.Body[0].Text, paper.Body[0].Font));
        CollectionAssert.AreEqual(new[] { 0, 1, 0, 2, 3, 2 }, paper.Header.Select(h => h.Number).ToArray());
        var alternate = texts.Read((int)WrittenSubtype.Newspaper, 1300, alternateNewspaper: true);
        Assert.AreEqual((817, WrittenTexts.NewspaperAlternateFont), (alternate.BackgroundArt, alternate.Header[1].Font));
        CollectionAssert.AreEqual(new[] { 0, 4, 0, 2, 5, 2 }, alternate.Header.Select(h => h.Number).ToArray());
        Assert.AreEqual(817, texts.Read((int)WrittenSubtype.Book, 100, alternateNewspaper: true).BackgroundArt, "the flag selects 817 for any subtype but a picture");
        Assert.AreEqual(3, texts.NewspaperFillers);
        // 10 + 1300 / 10 = 140 is at or past 10 + 3, so 10; then 11, 12, and back to 10.
        CollectionAssert.AreEqual(new[] { 10, 11, 12, 10 }, texts.NewspaperFiller(1300, 4).Select(m => m.Number).ToArray());
        CollectionAssert.AreEqual(new[] { 11, 12, 10 }, texts.NewspaperFiller(10, 3).Select(m => m.Number).ToArray(), "10 + 10 / 10 = 11");
        var gap = WrittenTexts.Create(null, null, Mes("{10}{1 Ten}{12}{1 Twelve}"), null, null);
        CollectionAssert.AreEqual(new[] { 10 }, gap.NewspaperFiller(0, 5).Select(m => m.Number).ToArray(), "a missing number stops the filling");
        Assert.AreEqual(0, WrittenTexts.Create(null, null, Mes("{11}{1 Eleven}"), null, null).NewspaperFillers, "no message 10: no filler");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Texts().NewspaperFiller(1300, WrittenTexts.MaxFillerCount + 1), "a bounded request");
        Assert.AreEqual(WrittenTexts.MaxFillerCount, Texts().NewspaperFiller(1300, WrittenTexts.MaxFillerCount).Count, "the fill wraps");
    }

    // ARC-UI-1739, ledger NP2: the interface's message hook (0x56CCA0) and the default issue's probe loop (0x4BF330).
    [TestMethod]
    public void A_headline_is_the_issue_message_without_its_markup_and_with_line_feeds_made_spaces()
    {
        var texts = WrittenTexts.Create(null, null, Mes("{5000}{30c First line\nsecond line}{5010}{31 Second issue}"), null, null);
        Assert.AreEqual("First line second line", texts.Headline(5000), "the font and the c mark are the markup; the line feed becomes a space");
        Assert.AreEqual("irst line second line", WrittenTexts.Create(null, null, Mes("{5000}{First line\nsecond line}"), null, null).Headline(5000), "no prefix: the original drops the first character");
        Assert.AreEqual("First line", WrittenTexts.Create(null, null, Mes("{5000}{First line}"), null, null).Headline(5000, WrittenRuleProfile.Steamwoven));
        Assert.AreEqual(string.Empty, texts.Headline(5001), "no message: an empty text");
        Assert.AreEqual(string.Empty, WrittenTexts.Create(null, null, null, null, null).Headline(5000), "no newspaper file: an empty text");
    }

    [TestMethod]
    public void The_default_issue_choices_count_the_headlines_from_5010_in_steps_of_ten_to_the_first_gap()
    {
        string File(params int[] numbers) => string.Concat(numbers.Select(n => $"{{{n}}}{{1 Text {n}}}"));
        Assert.AreEqual(0, WrittenTexts.Create(null, null, Mes(File(5000)), null, null).DefaultIssueChoices(), "only 5000: no choice, the default issue is 5000");
        Assert.AreEqual(3, WrittenTexts.Create(null, null, Mes(File(5000, 5010, 5020, 5030, 5050)), null, null).DefaultIssueChoices(), "5040 is missing: the loop stops there");
        Assert.AreEqual(0, WrittenTexts.Create(null, null, Mes(File(5000, 5020)), null, null).DefaultIssueChoices(), "5010 missing");
        // A message whose text is only its markup reads empty and ends the loop as a missing one does.
        Assert.AreEqual(1, WrittenTexts.Create(null, null, Mes("{5010}{1 A}{5020}{1 }"), null, null).DefaultIssueChoices());
        Assert.AreEqual(0, WrittenTexts.Create(null, null, null, null, null).DefaultIssueChoices());
    }

    [TestMethod]
    public void Plaques_pictures_schematics_and_odd_subtypes()
    {
        var texts = Texts();
        var plaque = texts.Read((int)WrittenSubtype.Plaque, 500);
        Assert.AreEqual((633, "Plaque text", true), (plaque.BackgroundArt, plaque.Body[0].Text, plaque.Body[0].Centered));
        var picture = texts.Read((int)WrittenSubtype.Picture, 900);
        Assert.AreEqual((WrittenReadStatus.Read, 900, 900, 0), (picture.Status, picture.BackgroundArt, picture.PictureArt, picture.Body.Count));
        Assert.AreEqual(WrittenReadStatus.ArtOutOfRange, texts.Read((int)WrittenSubtype.Picture, 4096).Status);
        Assert.AreEqual(WrittenReadStatus.ArtOutOfRange, texts.Read((int)WrittenSubtype.Picture, -1).Status);
        Assert.AreEqual(WrittenReadStatus.Schematic, texts.Read((int)WrittenSubtype.Schematic, 4010).Status);
        Assert.AreEqual(WrittenReadStatus.SubtypeOutOfRange, texts.Read(7, 100).Status);
        Assert.AreEqual(WrittenReadStatus.SubtypeOutOfRange, texts.Read(-2, 100).Status);
        Assert.AreEqual(0, texts.Read(0, int.MaxValue).Body.Count, "an overflowing number ends the text");
        var missing = WrittenTexts.Create(null, Mes(""), null, null, null);
        Assert.AreEqual(WrittenReadStatus.TextsUnavailable, missing.Read(0, 100).Status);
        CollectionAssert.AreEqual(new[] { WrittenDataStatus.FileMissing, WrittenDataStatus.FileEmpty, WrittenDataStatus.FileMissing, WrittenDataStatus.FileMissing,
            WrittenDataStatus.FileMissing }, missing.Refusals.Select(r => r.Status).ToArray());
    }

    [TestMethod]
    public void The_joined_note_stops_at_the_original_buffer()
    {
        var line = new string('x', 1999);
        var texts = WrittenTexts.Create(null, Mes(string.Concat(Enumerable.Range(1, 10).Select(n => $"{{{n}}}{{{line}}}"))), null, null, null);
        var note = texts.Read((int)WrittenSubtype.Note, 1);
        Assert.AreEqual(WrittenTexts.JoinedBufferBytes - 1, note.Body.Sum(m => m.Text.Length));
    }
}
