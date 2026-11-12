using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Data.Written;
using Arcanum.Scripting.Scr;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Technology;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-049 reading window over invented texts and items (no original data): the views of each kind, the book's spreads
/// and their bounds, the script openers' facts, the inventory's written lines and facts, and the schematic message.</summary>
[TestClass]
public sealed class AreaReadingTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;
    private static readonly MesTextProfile Profile = AreaHoverIndex.TextProfile;

    private static WrittenTexts Texts() => WrittenTexts.Create(
        Mes(string.Concat(Enumerable.Range(100, 10).Select(n => $"{{{n}}}{{12 {string.Concat(Enumerable.Repeat($"Line {n} words. ", 30))}}}")) + "{300}{1c Short}"),
        Mes("{200}{Note A}{201}{Note B}"),
        Mes("{0}{Mast A}{1}{Mast B}{2}{Mast C}{3}{Mast D}{10}{2 Filler ten}{11}{2 Filler eleven}{1300}{3 Headline}{1301}{4 Column}"),
        Mes(string.Concat(Enumerable.Range(1000, 12).Select(n => $"{{{n}}}{{H{n}}}")) + "{1012}{ STOP}{400}{Arrive}"),
        Mes("{500}{40c Plaque text}"));

    [TestMethod]
    public void A_book_shows_two_pages_a_spread_and_turns_within_its_bounds()
    {
        var texts = Texts();
        var document = texts.Read((int)WrittenSubtype.Book, 100);
        var first = AreaReading.View(document, texts, 0, Profile)!;
        Assert.AreEqual((WrittenSubtype.Book, 477, 0), (first.Subtype, first.BackgroundArt, first.Spread));
        Assert.IsTrue(first.Spreads > 1 && !first.HasPrevious && first.HasNext, $"spreads {first.Spreads}");
        StringAssert.StartsWith(first.Left, "Line 100 words.");
        Assert.IsTrue(first.Left.Length <= AreaReading.BookPageCharacters && first.Right.Length > 0);
        var last = AreaReading.View(document, texts, 999, Profile)!;
        Assert.AreEqual(first.Spreads - 1, last.Spread, "a spread past the end shows the last");
        Assert.IsTrue(last.HasPrevious && !last.HasNext);
        Assert.AreEqual(0, AreaReading.View(document, texts, -5, Profile)!.Spread);
        var shortBook = AreaReading.View(texts.Read(0, 300), texts, 0, Profile)!;
        Assert.AreEqual(("[centred] Short", "", 1), (shortBook.Left, shortBook.Right, shortBook.Spreads));
        Assert.AreEqual("no text at the start line", AreaReading.View(texts.Read(0, 150), texts, 0, Profile)!.Note);
    }

    [TestMethod]
    public void Pages_break_at_a_space_and_stop_at_the_page_bound()
    {
        var pages = AreaReading.Pages(string.Concat(Enumerable.Repeat("word ", 400)));
        Assert.IsTrue(pages.All(p => p.Length <= AreaReading.BookPageCharacters && p.EndsWith("word", StringComparison.Ordinal)));
        Assert.AreEqual(WrittenTexts.MaxBookPages, AreaReading.Pages(new string('x', AreaReading.BookPageCharacters * 150)).Count);
        Assert.AreEqual(1, AreaReading.Pages("").Count);
    }

    [TestMethod]
    public void The_other_kinds_show_one_page_with_their_headers()
    {
        var texts = Texts();
        var note = AreaReading.View(texts.Read(1, 200), texts, 0, Profile)!;
        Assert.AreEqual(("Note A\nNote B\n", 489, 1, false), (note.Left, note.BackgroundArt, note.Spreads, note.HasNext));
        var telegram = AreaReading.View(texts.Read(3, 400), texts, 0, Profile)!;
        Assert.AreEqual(12, telegram.Header.Count);
        // ARC-UI-1428: the telegram's form lines carry Courier10 (WR7), the masthead its six fonts (WR8); an index outside gives 0.
        CollectionAssert.AreEqual(Enumerable.Repeat(AreaReading.TelegramFont, 12).ToArray(), telegram.HeaderFonts!.ToArray());
        Assert.AreEqual(480, telegram.HeaderFont(11));
        Assert.AreEqual(0, telegram.HeaderFont(12));
        Assert.AreEqual(0, telegram.HeaderFont(-1));
        Assert.AreEqual("Arrive STOP", telegram.Left, "each message followed by 1012");
        var paper = AreaReading.View(texts.Read(2, 1300), texts, 0, Profile)!;
        CollectionAssert.AreEqual(new[] { "Mast A", "Mast B", "Mast A", "Mast C", "Mast D", "Mast C" }, paper.Header.ToArray());
        CollectionAssert.AreEqual(new[] { 485, 484, 485, 482, 482, 482 }, paper.HeaderFonts!.ToArray());
        StringAssert.StartsWith(paper.Left, "Headline\nColumn\n[filler] ");
        var plaque = AreaReading.View(texts.Read(6, 500), texts, 0, Profile)!;
        Assert.AreEqual(("[centred] Plaque text", 633), (plaque.Left, plaque.BackgroundArt));
        var picture = AreaReading.View(texts.Read(4, 321), texts, 0, Profile)!;
        Assert.AreEqual((321, 321), (picture.PictureArt!.Value, picture.BackgroundArt), "the art is the start line");
        Assert.IsNull(AreaReading.View(texts.Read(5, 2000), texts, 0, Profile), "a schematic document has no view");
        Assert.IsNull(AreaReading.View(texts.Read(9, 1), texts, 0, Profile), "an unknown kind has no view");
    }

    [TestMethod]
    public void Script_openers_name_a_book_a_picture_or_a_plaque()
    {
        var viewer = new ScrObjectRef(1, AreaScriptHost.PcCode);
        Assert.AreEqual(new SessionWrittenFacts(0, 100), AreaReading.ScriptFacts(new(ScrReadingKind.Book, 100, viewer)));
        Assert.AreEqual(new SessionWrittenFacts(4, 7), AreaReading.ScriptFacts(new(ScrReadingKind.Picture, 7, viewer)));
        Assert.AreEqual(new SessionWrittenFacts(6, 500), AreaReading.ScriptFacts(new(ScrReadingKind.Plaque, 500, viewer)));
    }

    [TestMethod]
    public void The_inventory_marks_written_lines_and_gives_a_kit_item_its_prototype_facts()
    {
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Written, 9001, 6000, 6001)), new SessionHeroKitItem(new(SessionItemType.Generic, 15199, 6002, 6003))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var host = new AreaInventoryHost([]);
        var lines = host.View(session, null, 1).Pc;
        Assert.AreEqual((true, false), (lines[0].Written, lines[1].Written));
        var data = new AreaReadingData(Texts(), new Dictionary<int, SessionWrittenFacts> { [9001] = new(0, 100) { Prototype = 9001 } });
        var book = session.QueryHeroKit()[0].Entity;
        Assert.AreEqual(new SessionWrittenFacts(0, 100) { Prototype = 9001 }, host.WrittenFacts(session, book, data));
        Assert.IsNull(host.WrittenFacts(session, book, null), "no facts before the data load");
        Assert.IsNull(host.WrittenFacts(session, session.QueryHeroKit()[1].Entity, data), "not a written prototype");
        Assert.IsNull(host.WrittenFacts(session, "item:none", data), "not held");
        var use = session.UseWrittenItem(session.Version, book, host.WrittenFacts(session, book, data)!.Value, data.Texts);
        Assert.AreEqual((GameSessionDisposition.NoChange, WrittenReadStatus.Read), (use.Disposition, use.Document!.Status));
    }

    [TestMethod]
    public void A_written_start_a_script_wrote_replaces_the_prototypes_when_the_item_is_read()
    {
        // ARC-SCRIPT-1279 (WO6): set written start (94) writes the item's own start line; the reading facts take it over the data's.
        var hero = new SessionHeroDefinition(16089, CharacterRace.HalfElf, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0,
            null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Written, 9001, 6000, 6001))]);
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Find("half-elf-male")!, hero, 2352, 0, null, "synthetic");
        using var session = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, load).Owner!;
        var host = new AreaInventoryHost([]);
        var data = new AreaReadingData(Texts(), new Dictionary<int, SessionWrittenFacts> { [9001] = new(0, 100) { Prototype = 9001 } });
        var book = session.QueryHeroKit()[0].Entity;
        Assert.AreEqual(GameSessionDisposition.Changed, session.SetItemWrittenStart(session.Version, book, 250).Disposition);
        Assert.AreEqual(new SessionWrittenFacts(0, 250) { Prototype = 9001 }, host.WrittenFacts(session, book, data), "the script's start, not the prototype's 100");
        Assert.IsNull(host.WrittenFacts(session, "item:none", data), "an item the hero does not hold has no facts, whatever was written");
    }

    [TestMethod]
    public void A_schematic_document_names_messages_89_and_88_and_the_refusals_have_texts()
    {
        Assert.AreEqual("[tech 89] schematic 2000", AreaReading.SchematicMessage(SchematicReading.Registered, 2000, null, Profile));
        Assert.AreEqual("[tech 88]", AreaReading.SchematicMessage(SchematicReading.AlreadyKnown, 2000, null, Profile));
        StringAssert.Contains(AreaReading.SchematicMessage(SchematicReading.RefusedZero, 0, null, Profile), "TC8");
        Assert.AreEqual("no hero or the hero is dead", AreaReading.UseRefused(GameSessionFailure.InvalidLifeTarget));
        Assert.AreEqual("a picture art outside 0..4095", AreaReading.Refused(WrittenReadStatus.ArtOutOfRange));
    }
}
