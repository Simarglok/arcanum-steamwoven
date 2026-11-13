using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-1742 scheme page of the character sheet (ledger ALP1..ALP16): the layout tables, the list the name files give, the hero's choice, on
/// invented tables (no original data).</summary>
[TestClass]
public sealed class AreaCharacterSheetSchemesTests
{
    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.ASCII.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

    private static AreaCharacterSheetTexts Texts(int games = 3) => AreaCharacterSheetTexts.Create(null, null, null,
        gameSchemeNames: Mes("{0}{No scheme}\n" + string.Concat(Enumerable.Range(0, games).Select(i => $"{{{50 + i}}}{{Game {50 + i}}}\n")) + "{3}{Not a game scheme}\n{200}{Too high}\n"),
        userSchemeNames: Mes("{3}{User 3}\n{49}{User 49}\n{50}{Not a user scheme}\n"));

    [TestMethod]
    public void The_page_reads_its_arts()
    {
        var arts = AreaCharacterSheetSchemesLayout.ArtNumbers;
        CollectionAssert.AreEqual(new[] { 567, 317, 318, 238, 787, 240 }, arts.ToArray());
        CollectionAssert.IsSubsetOf(arts.ToArray(), AreaCharacterSheetArtLayout.AllArtNumbers.ToArray());
        Assert.AreEqual(AreaCharacterSheetArtLayout.AllArtNumbers.Count, AreaCharacterSheetArtLayout.AllArtNumbers.Distinct().Count(), "the sheet loads each art once");
        Assert.AreEqual(568, AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Schemes).Art);
    }

    [TestMethod]
    public void The_rows_stand_where_the_table_puts_them()
    {
        Assert.AreEqual((23, 66, 160, 16), AreaCharacterSheetSchemesLayout.Row(0));
        Assert.AreEqual((23, 66 + 14 * 16, 160, 16), AreaCharacterSheetSchemesLayout.Row(14), "screen y 394 less the window's 104 is 290");
        Assert.AreEqual(290, AreaCharacterSheetSchemesLayout.Row(14).Y);
        Assert.AreEqual((209, 60, 17, 255), AreaCharacterSheetSchemesLayout.Bar);
        Assert.AreEqual((23, 66, 177, 240), AreaCharacterSheetSchemesLayout.Wheel);
        Assert.AreEqual((117, 21), (AreaCharacterSheetSchemesLayout.HeaderCentreX, AreaCharacterSheetSchemesLayout.HeaderY));
        Assert.AreEqual(-1, AreaCharacterSheetSchemesLayout.RowAt(22, 70));
        Assert.AreEqual(0, AreaCharacterSheetSchemesLayout.RowAt(23, 66));
        Assert.AreEqual(0, AreaCharacterSheetSchemesLayout.RowAt(100, 81));
        Assert.AreEqual(1, AreaCharacterSheetSchemesLayout.RowAt(100, 82));
        Assert.AreEqual(14, AreaCharacterSheetSchemesLayout.RowAt(182, 305));
        Assert.AreEqual(-1, AreaCharacterSheetSchemesLayout.RowAt(183, 305), "160 wide");
        Assert.AreEqual(-1, AreaCharacterSheetSchemesLayout.RowAt(100, 306), "fifteen rows");
    }

    [TestMethod]
    public void The_list_is_the_names_in_order_with_zero_first()
    {
        var page = AreaSchemesPage.Build(Texts(), 51)!;
        // 0 and 50..52 from the game file, 3 and 49 from the user file; 3 in the game file, 50 in the user file and 200 are not looked up.
        CollectionAssert.AreEqual(new[] { 0, 3, 49, 50, 51, 52 }, page.Entries.Select(e => e.Number).ToArray());
        CollectionAssert.AreEqual(new[] { "No scheme", "User 3", "User 49", "Game 50", "Game 51", "Game 52" }, page.Entries.Select(e => e.Name).ToArray());
        Assert.AreEqual((51, "Game 51"), (page.Scheme, page.SchemeName));
        Assert.AreEqual("No scheme", AreaSchemesPage.Build(Texts(), 0)!.SchemeName);
        Assert.IsNull(AreaSchemesPage.Build(Texts(), 7)!.SchemeName, "a scheme without a name leaves the line empty");
        Assert.IsNull(AreaSchemesPage.Build(Texts(), null)!.SchemeName);
        Assert.IsNull(AreaSchemesPage.Build(AreaCharacterSheetTexts.Project("none"), 1), "without the name files there is no page");
    }

    [TestMethod]
    public void The_bar_exists_with_a_range_of_the_entries_over_fifteen()
    {
        Assert.AreEqual(0, AreaSchemesPage.Build(Texts(), 0)!.ScrollMaximum);
        Assert.AreEqual(6, AreaSchemesPage.Build(Texts(), 0)!.ShownRows);
        // 0, 3, 49 and 12 game schemes: fifteen entries, no range; one more: a range of 1.
        Assert.AreEqual((15, 0, 15), Counts(Texts(12)));
        Assert.AreEqual((16, 1, 15), Counts(Texts(13)));
        Assert.AreEqual((59, 44, 15), Counts(Texts(56)));
    }

    private static (int Entries, int Maximum, int Shown) Counts(AreaCharacterSheetTexts texts)
    {
        var page = AreaSchemesPage.Build(texts, 0)!;
        return (page.Entries.Count, page.ScrollMaximum, page.ShownRows);
    }

    [TestMethod]
    public void A_row_names_the_entry_the_scroll_gives_it()
    {
        var page = AreaSchemesPage.Build(Texts(56), 0)!;
        Assert.AreEqual(0, AreaCharacterSheetSchemesLayout.EntryAt(page, 0, 0)!.Number);
        Assert.AreEqual(3, AreaCharacterSheetSchemesLayout.EntryAt(page, 0, 1)!.Number);
        Assert.AreEqual(page.Entries[44].Number, AreaCharacterSheetSchemesLayout.EntryAt(page, 44, 0)!.Number);
        Assert.AreEqual(page.Entries[58].Number, AreaCharacterSheetSchemesLayout.EntryAt(page, 44, 14)!.Number);
        Assert.IsNull(AreaCharacterSheetSchemesLayout.EntryAt(page, 0, 15));
        // A short list: the rows past the last entry exist and answer nothing.
        var shorter = AreaSchemesPage.Build(Texts(), 0)!;
        Assert.IsNull(AreaCharacterSheetSchemesLayout.EntryAt(shorter, 0, 6));
        Assert.AreEqual(52, AreaCharacterSheetSchemesLayout.EntryAt(shorter, 0, 5)!.Number);
    }

    [TestMethod]
    public void The_bar_of_a_page_takes_the_art_measures_and_the_scroll()
    {
        var page = AreaSchemesPage.Build(Texts(56), 0)!;
        var bar = AreaCharacterSheetSchemesLayout.MakeBar(page, new(11, 15, 11, 14), 7);
        Assert.AreEqual((0, 44, 1, 7, 5, 3), (bar.Minimum, bar.Maximum, bar.Step, bar.Value, bar.Page, bar.WheelStep));
        Assert.AreEqual((209, 60, 17, 255), (bar.X, bar.Y, bar.Width, bar.Height));
    }

    [TestMethod]
    public void A_choice_writes_the_scheme_into_the_hero_and_the_snapshot()
    {
        using var session = ConversationSessionFixture.CreateWithHero();
        var before = session.QueryHero()!.Advancement.AutoLevelScheme;
        var texts = Texts(56);
        Assert.AreEqual(before, AreaSchemesPage.BuildForHero(session, texts)!.Scheme);
        var chosen = before == 70 ? 71 : 70;
        var result = AreaSchemesPage.Choose(session, chosen);
        Assert.AreEqual((GameSessionDisposition.Changed, GameSessionFailure.None, (int?)chosen), (result.Disposition, result.Failure, result.Scheme));
        Assert.AreEqual(chosen, session.QueryHero()!.Advancement.AutoLevelScheme);
        Assert.AreEqual(chosen, AreaSchemesPage.BuildForHero(session, texts)!.Scheme);
        StringAssert.Contains(GameSessionSnapshotCodec.Encode(session.Capture()), $"\"scheme\":{chosen}}}");
        Assert.AreEqual(GameSessionDisposition.NoChange, AreaSchemesPage.Choose(session, chosen).Disposition, "the same row again");
    }
}
