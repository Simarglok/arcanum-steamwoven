using System.Text;
using Arcanum.Data.Messages;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-077 spells page of the character sheet (ledger CHW17–CHW22): the layout tables, the colours of the five rows, the + and −
/// slots, the hit order, on invented tables (no original data).</summary>
[TestClass]
public sealed class AreaCharacterSheetSpellsTests
{
    private static AreaSpellsPageView View(int college, int rank, int floor)
    {
        var tabs = Enumerable.Range(0, 16).Select(c => new AreaCollegeTab(c, "college " + c, c == college ? rank : 0, false, "about " + c)).ToArray();
        var rows = Enumerable.Range(0, 5).Select(r => new AreaSpellRow(college * 5 + r, r + 1, "spell " + r, null, new[] { 1, 1, 5, 10, 15 }[r], 5, 4, null, null,
            r < rank ? AreaSpellState.Known : r == rank ? AreaSpellState.Next : AreaSpellState.Locked)).ToArray();
        return new(tabs, college, rows, rank, floor, 0, true);
    }

    private static (int, int)? Size(int art) => art is AreaCharacterSheetSpellsLayout.PlusArt or AreaCharacterSheetSpellsLayout.MinusArt ? (32, 32) : (24, 24);

    [TestMethod]
    public void The_page_reads_its_arts()
    {
        var arts = AreaCharacterSheetSpellsLayout.ArtNumbers;
        Assert.AreEqual(arts.Count, arts.Distinct().Count(), "no art twice");
        Assert.AreEqual(4 + 16 + 80, arts.Count);
        foreach (var number in new[] { 31, 28, 647, 648, 11, 122, 12, 16, 123, 127, 45, 49 }) CollectionAssert.Contains(arts.ToArray(), number);
        CollectionAssert.IsSubsetOf(arts.ToArray(), AreaCharacterSheetArtLayout.AllArtNumbers.ToArray());
        Assert.AreEqual(AreaCharacterSheetArtLayout.AllArtNumbers.Count, AreaCharacterSheetArtLayout.AllArtNumbers.Distinct().Count(), "the sheet loads each art once");
        Assert.AreEqual(11, AreaCharacterSheetSpellsLayout.CollegeArt(0), "conveyance");
        Assert.AreEqual(122, AreaCharacterSheetSpellsLayout.CollegeArt(15), "temporal");
        Assert.AreEqual(12, AreaCharacterSheetSpellsLayout.SpellArt(0, 0), "s_conveyance1");
        Assert.AreEqual(127, AreaCharacterSheetSpellsLayout.SpellArt(15, 4), "s_temporal5");
    }

    [TestMethod]
    public void The_tabs_icons_and_lines_stand_where_the_tables_put_them()
    {
        CollectionAssert.AreEqual(new[] { 13, 38, 63, 88, 113, 138, 162, 187 }, Enumerable.Range(0, 8).Select(c => AreaCharacterSheetSpellsLayout.CollegeTab(c).X).ToArray());
        CollectionAssert.AreEqual(new[] { 25, 50, 75, 100, 124, 149, 174, 199 }, Enumerable.Range(8, 8).Select(c => AreaCharacterSheetSpellsLayout.CollegeTab(c).X).ToArray());
        Assert.IsTrue(Enumerable.Range(0, 8).All(c => AreaCharacterSheetSpellsLayout.CollegeTab(c).Y == 15));
        Assert.IsTrue(Enumerable.Range(8, 8).All(c => AreaCharacterSheetSpellsLayout.CollegeTab(c).Y == 40));
        CollectionAssert.AreEqual(new[] { 82, 130, 177, 225, 272 }, Enumerable.Range(0, 5).Select(r => AreaCharacterSheetSpellsLayout.Icon(r).Y).ToArray());
        CollectionAssert.AreEqual(new[] { 76, 124, 171, 219, 266 }, Enumerable.Range(0, 5).Select(r => AreaCharacterSheetSpellsLayout.Name(r).Y).ToArray());
        CollectionAssert.AreEqual(new[] { 97, 145, 192, 240, 287 }, Enumerable.Range(0, 5).Select(r => AreaCharacterSheetSpellsLayout.Level(r).Y).ToArray());
        Assert.AreEqual((18, 79, 196, 35), AreaCharacterSheetSpellsLayout.Region(0));
        Assert.AreEqual((18, 269, 196, 35), AreaCharacterSheetSpellsLayout.Region(4));
        Assert.AreEqual(18, AreaCharacterSheetSpellsLayout.Icon(2).X);
        Assert.AreEqual(59, AreaCharacterSheetSpellsLayout.Name(2).X);
    }

    [TestMethod]
    public void The_rows_are_coloured_by_the_known_rank()
    {
        // Rank 2: two known (white), the next gold/yellow, two above it grey.
        CollectionAssert.AreEqual(new[] { AreaSheetColour.White, AreaSheetColour.White, AreaSheetColour.Gold, AreaSheetColour.Grey, AreaSheetColour.Grey },
            Enumerable.Range(0, 5).Select(r => AreaCharacterSheetSpellsLayout.NameColour(r, 2)).ToArray());
        CollectionAssert.AreEqual(new[] { AreaSheetColour.White, AreaSheetColour.White, AreaSheetColour.Yellow, AreaSheetColour.Grey, AreaSheetColour.Grey },
            Enumerable.Range(0, 5).Select(r => AreaCharacterSheetSpellsLayout.LevelColour(r, 2)).ToArray());
        Assert.IsTrue(Enumerable.Range(0, 5).All(r => AreaCharacterSheetSpellsLayout.NameColour(r, 5) == AreaSheetColour.White && AreaCharacterSheetSpellsLayout.LevelColour(r, 5) == AreaSheetColour.White),
            "all five known: all white");
        Assert.AreEqual(AreaSheetColour.Gold, AreaCharacterSheetSpellsLayout.NameColour(0, 0), "nothing known: the first is the next");
        CollectionAssert.AreEqual(new[] { false, false, true, true, true }, Enumerable.Range(0, 5).Select(r => AreaCharacterSheetSpellsLayout.Greyed(r, 2)).ToArray(),
            "the next spell's icon is greyed with the ones above it");
    }

    [TestMethod]
    public void The_plus_and_the_minus_show_by_the_rank_and_the_editors_floor()
    {
        Assert.IsTrue(AreaCharacterSheetSpellsLayout.PlusShown(0) && AreaCharacterSheetSpellsLayout.PlusShown(4));
        Assert.IsFalse(AreaCharacterSheetSpellsLayout.PlusShown(5));
        Assert.IsFalse(AreaCharacterSheetSpellsLayout.MinusShown(0, 0), "nothing to unlearn");
        Assert.IsTrue(AreaCharacterSheetSpellsLayout.MinusShown(3, 2));
        Assert.IsFalse(AreaCharacterSheetSpellsLayout.MinusShown(3, 3), "a spell learned before the sheet opened keeps no −");
    }

    [TestMethod]
    public void The_minimum_level_line_uses_the_charedit_text()
    {
        var charEdit = MesFile.Parse(Encoding.Latin1.GetBytes("{44}{Minimum Level}\n")).File!;
        var texts = AreaCharacterSheetTexts.Create(charEdit, null, null);
        Assert.AreEqual("Minimum Level: 5", AreaCharacterSheetSpellsLayout.LevelText(texts, 5));
        Assert.AreEqual("Minimum Level: 1", AreaCharacterSheetSpellsLayout.LevelText(AreaCharacterSheetTexts.Project("none"), 1), "the project's words without the text");
    }

    [TestMethod]
    public void The_tabs_win_the_hit_test_then_the_buttons_then_the_rows()
    {
        var view = View(3, 2, 0);
        // A tab: college 9 is the second of row two (x 50, y 40).
        Assert.AreEqual(new AreaCharacterSheetSpellsLayout.Hit(AreaCharacterSheetSpellsLayout.Part.CollegeTab, 9), AreaCharacterSheetSpellsLayout.HitAt(50 + 5, 40 + 5, view, Size));
        // The + sits on the next spell's icon (rank 2 → row 2), the − on the highest known (row 1); they win over the row regions.
        Assert.AreEqual(new AreaCharacterSheetSpellsLayout.Hit(AreaCharacterSheetSpellsLayout.Part.Plus, 2), AreaCharacterSheetSpellsLayout.HitAt(18 + 5, 177 + 5, view, Size));
        Assert.AreEqual(new AreaCharacterSheetSpellsLayout.Hit(AreaCharacterSheetSpellsLayout.Part.Minus, 1), AreaCharacterSheetSpellsLayout.HitAt(18 + 5, 130 + 5, view, Size));
        // Beside the icon the row's region answers.
        Assert.AreEqual(new AreaCharacterSheetSpellsLayout.Hit(AreaCharacterSheetSpellsLayout.Part.Row, 2), AreaCharacterSheetSpellsLayout.HitAt(100, 177 + 5, view, Size));
        Assert.AreEqual(new AreaCharacterSheetSpellsLayout.Hit(AreaCharacterSheetSpellsLayout.Part.Row, 0), AreaCharacterSheetSpellsLayout.HitAt(18 + 5, 82 + 5, view, Size), "row 0's icon is no button");
        Assert.IsNull(AreaCharacterSheetSpellsLayout.HitAt(5, 5, view, Size));
        // At the editor's floor the − is gone: the row answers.
        var floor = View(3, 2, 2);
        Assert.AreEqual(AreaCharacterSheetSpellsLayout.Part.Row, AreaCharacterSheetSpellsLayout.HitAt(18 + 5, 130 + 5, floor, Size)!.Value.Part);
        // At five spells there is no +.
        var full = View(3, 5, 0);
        Assert.AreEqual(AreaCharacterSheetSpellsLayout.Part.Minus, AreaCharacterSheetSpellsLayout.HitAt(18 + 5, 272 + 5, full, Size)!.Value.Part, "the − over the fifth");
        Assert.AreEqual(AreaCharacterSheetSpellsLayout.Part.Row, AreaCharacterSheetSpellsLayout.HitAt(18 + 5, 225 + 5, full, Size)!.Value.Part, "no + below the fifth");
    }

    [TestMethod]
    public void A_college_tab_hint_shows_the_colleges_icon_name_and_description()
    {
        var entry = AreaCharacterSheetSpellsLayout.CollegeEntry(View(0, 0, 0).Colleges[5]);
        Assert.AreEqual(AreaMessageKind.SpellHint, entry.Kind);
        Assert.AreEqual("college 5", entry.Title);
        Assert.AreEqual("about 5", entry.Detail);
        Assert.AreEqual(446 + 5, entry.Icon);
    }

    [TestMethod]
    public void The_spells_art_content_is_ready_only_with_the_spells_arts()
    {
        static AreaInterfaceArtLoad Load(int number) => new(number, AreaInterfaceArtStatus.Ready, null, [new AreaInterfaceFrame(5, 5, new byte[100], 0, 0)], "synthetic");
        var full = AreaCharacterSheetArtLayout.AllArtNumbers.ToDictionary(number => number, Load);
        var content = new AreaCharacterSheetArtContent(full, AreaCharacterSheetTexts.Project("t"), "t");
        Assert.IsTrue(content.ArtReady && content.PagesReady && content.SpellsReady);
        var noIcon = full.Where(pair => pair.Key != AreaCharacterSheetSpellsLayout.SpellArt(7, 3)).ToDictionary(pair => pair.Key, pair => pair.Value);
        var partial = new AreaCharacterSheetArtContent(noIcon, AreaCharacterSheetTexts.Project("t"), "t");
        Assert.IsTrue(partial.PagesReady && !partial.SpellsReady, "a missing spell icon keeps the diagnostic spells page, the other pages stay");
    }
}
