using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Skills;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-076 skills and tech pages of the character sheet (ledger CHW10–CHW16): the layout tables, the gauge and the degree colours,
/// the hover texts, on invented tables (no original data).</summary>
[TestClass]
public sealed class AreaCharacterSheetPagesTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    [TestMethod]
    public void The_pages_read_their_arts()
    {
        var arts = AreaCharacterSheetPagesLayout.ArtNumbers;
        Assert.AreEqual(arts.Count, arts.Distinct().Count(), "no art twice");
        foreach (var number in new[] { 29, 30, 302, 305, 306, 313, 623, 624, 625, 647, 648, 649, 656, 300, 301, 483 }) CollectionAssert.Contains(arts.ToArray(), number);
        Assert.AreEqual(2 + 4 + 8 + 3 + 2 + 8 + 3, arts.Count);
        CollectionAssert.IsSubsetOf(arts.ToArray(), AreaCharacterSheetArtLayout.AllArtNumbers.ToArray());
        Assert.AreEqual(653, AreaCharacterSheetPagesLayout.DegreeIcon(0), "herbology");
        Assert.AreEqual(649, AreaCharacterSheetPagesLayout.DegreeIcon(1), "chemistry");
        Assert.AreEqual(656, AreaCharacterSheetPagesLayout.DegreeIcon(7), "therapeutics");
    }

    [TestMethod]
    public void The_skills_page_stands_where_the_tables_put_it()
    {
        Assert.AreEqual((21, 17), AreaCharacterSheetPagesLayout.GroupTab(0));
        Assert.AreEqual((75, 17), AreaCharacterSheetPagesLayout.GroupTab(1));
        Assert.AreEqual((129, 17), AreaCharacterSheetPagesLayout.GroupTab(2));
        Assert.AreEqual((185, 17), AreaCharacterSheetPagesLayout.GroupTab(3));
        CollectionAssert.AreEqual(new[] { 63, 129, 195, 261 }, Enumerable.Range(0, 4).Select(AreaCharacterSheetPagesLayout.RowY).ToArray());
        Assert.AreEqual((18, 150), AreaCharacterSheetPagesLayout.SkillMinus(1), "84 + 66");
        Assert.AreEqual((192, 282), AreaCharacterSheetPagesLayout.SkillPlus(3));
        Assert.AreEqual((59, 87), AreaCharacterSheetPagesLayout.Gauge(0));
        Assert.AreEqual((17, 129, 200, 60), AreaCharacterSheetPagesLayout.SkillRegion(1));
        Assert.AreEqual(135, AreaCharacterSheetPagesLayout.GroupHint(0));
        Assert.AreEqual(138, AreaCharacterSheetPagesLayout.GroupHint(3));
        Assert.AreEqual(1000, AreaCharacterSheetPagesLayout.SkillHint(0));
        Assert.AreEqual(1012, AreaCharacterSheetPagesLayout.SkillHint(12), "the first tech skill");
        Assert.AreEqual(0, AreaCharacterSheetPagesLayout.GaugeVisible(0));
        Assert.AreEqual(30, AreaCharacterSheetPagesLayout.GaugeVisible(5), "6 pixels a level");
        Assert.AreEqual(120, AreaCharacterSheetPagesLayout.GaugeVisible(20));
        Assert.AreEqual(120, AreaCharacterSheetPagesLayout.GaugeVisible(27), "the gauge art is 120 wide");
        Assert.AreEqual(0, AreaCharacterSheetPagesLayout.GaugeVisible(-3));
        Assert.AreEqual(" (Expert)", AreaCharacterSheetPagesLayout.TrainingText("Expert"));
        Assert.AreEqual(string.Empty, AreaCharacterSheetPagesLayout.TrainingText(null), "untrained: nothing");
    }

    [TestMethod]
    public void The_tech_page_stands_where_the_tables_put_it()
    {
        (int, int)[] tabs = [(8, 7), (34, 29), (61, 7), (88, 28), (116, 7), (142, 29), (171, 7), (198, 29)];
        for (var d = 0; d < 8; d++) Assert.AreEqual(tabs[d], AreaCharacterSheetPagesLayout.DisciplineTab(d), "discipline " + d);
        Assert.AreEqual('1', AreaCharacterSheetPagesLayout.HeaderGlyph(0));
        Assert.AreEqual('8', AreaCharacterSheetPagesLayout.HeaderGlyph(7));
        Assert.AreEqual((46, 94), AreaCharacterSheetPagesLayout.DegreeName(0));
        Assert.AreEqual((46, 292), AreaCharacterSheetPagesLayout.DegreeName(6), "94 + 33 × 6");
        Assert.AreEqual((8, 96), AreaCharacterSheetPagesLayout.DegreeSlot(0));
        Assert.AreEqual((8, 294), AreaCharacterSheetPagesLayout.DegreeSlot(6));
        Assert.AreEqual((9, 66, 219, 27), AreaCharacterSheetPagesLayout.HeaderRegion);
        Assert.AreEqual((32, 99, 177, 27), AreaCharacterSheetPagesLayout.DegreeRegion(1));
        Assert.AreEqual((32, 291, 177, 27), AreaCharacterSheetPagesLayout.DegreeRegion(7), "the pointer's step is 32, the rows' 33");
        Assert.AreEqual(4003, AreaCharacterSheetPagesLayout.DisciplineHint(3));
        Assert.AreEqual(5000 + 8 * 2 + 5, AreaCharacterSheetPagesLayout.DegreeHint(2, 5));
    }

    [TestMethod]
    public void The_degree_rows_colour_and_buttons_follow_the_stored_and_the_snapshot_degree()
    {
        // Rows below the degree white, the next one yellow, the later ones grey, all white at degree 7.
        AreaSheetColour[] expected = [AreaSheetColour.White, AreaSheetColour.White, AreaSheetColour.Yellow, AreaSheetColour.Grey, AreaSheetColour.Grey,
            AreaSheetColour.Grey, AreaSheetColour.Grey];
        for (var row = 0; row < 7; row++) Assert.AreEqual(expected[row], AreaCharacterSheetPagesLayout.DegreeColour(row, 2), "row " + row);
        Assert.AreEqual(AreaSheetColour.Yellow, AreaCharacterSheetPagesLayout.DegreeColour(0, 0));
        Assert.IsTrue(Enumerable.Range(0, 7).All(row => AreaCharacterSheetPagesLayout.DegreeColour(row, 7) == AreaSheetColour.White));
        Assert.IsTrue(AreaCharacterSheetPagesLayout.PlusShown(6));
        Assert.IsFalse(AreaCharacterSheetPagesLayout.PlusShown(7));
        Assert.IsFalse(AreaCharacterSheetPagesLayout.MinusShown(0, 0), "no degree to give back");
        Assert.IsFalse(AreaCharacterSheetPagesLayout.MinusShown(3, 3), "at the editor's snapshot (the tech page draw hides it)");
        Assert.IsTrue(AreaCharacterSheetPagesLayout.MinusShown(3, 2));
        Assert.AreEqual((255, 255, 0), AreaCharacterSheetArtLayout.Rgb(AreaSheetColour.Yellow));
        Assert.AreEqual((128, 128, 128), AreaCharacterSheetArtLayout.Rgb(AreaSheetColour.Grey));
        Assert.AreEqual((0, 0, 255), AreaCharacterSheetArtLayout.Rgb(AreaSheetColour.Blue));
    }

    [TestMethod]
    public void The_hover_texts_come_from_the_games_tables()
    {
        var texts = AreaCharacterSheetTexts.Create(Mes("{139}{Current Degree\nThe degree reached.}"), null, null,
            Mes("{17}{Minimum IN}{28}{Strength}{29}{Dexterity}"),
            Mes("{0}{Herbology}{8}{Layman}{9}{Novice}{16}{Plants.}{" + (24 + 1 * 8 + 0) + "}{Degree one text.}{" + 40 + "}{Degree two text.}"));
        Assert.AreEqual("Herbology", texts.TechDisciplineName(0));
        Assert.AreEqual("Novice", texts.TechDegreeName(1));
        Assert.AreEqual("Plants.", texts.TechDisciplineDescription(0));
        Assert.AreEqual("Discipline 2", texts.TechDisciplineName(1), "a missing number keeps the project label");
        Assert.AreEqual("Herbology\nPlants.", AreaTechPage.DisciplineHint(texts, 0));
        Assert.AreEqual("Current Degree\nThe degree reached.", texts.Hint(AreaCharacterSheetPagesLayout.HeaderHint));
        var hint = AreaTechPage.DegreeHint(texts, 0, 1);
        StringAssert.StartsWith(hint, "Novice\nMinimum IN: " + AutoLevelRules.DegreeMinimumIntelligence(1));
        Assert.AreEqual(3, hint.Split('\n').Length, "the name, the minimum intelligence and the description");
        var row = new AreaSkillRow(2, Skill.Bow, "Bow", "Expert", "Shoots arrows.", 3, 12, SkillTraining.Expert, CharacterStat.Dexterity, 20, 1);
        var skill = AreaSkillsHover.Hint(row, texts);
        Assert.AreEqual((AreaMessageKind.SkillHint, "Bow", "Shoots arrows."), (skill.Kind, skill.Title, skill.Detail));
        Assert.AreEqual("Dexterity: 20", skill.Value, "intgame.mes 28 + stat with the minimum for the next point, on the right");
        Assert.AreEqual(string.Empty, AreaSkillsHover.Hint(row with { NextPointKeyStat = 0 }, texts).Value, "no further point: no line");
        Assert.AreEqual("Dex: 5", AreaSkillsHover.Hint(row with { NextPointKeyStat = 5 }, AreaCharacterSheetTexts.Create(null, null, null, Mes("{29}{Dex}"))).Value);
    }

    [TestMethod]
    public void The_pages_art_content_is_ready_only_with_the_pages_arts()
    {
        static AreaInterfaceArtLoad Load(int number) => new(number, AreaInterfaceArtStatus.Ready, null, [new AreaInterfaceFrame(5, 5, new byte[100], 0, 0)], "synthetic");
        var full = AreaCharacterSheetArtLayout.AllArtNumbers.ToDictionary(number => number, Load);
        var content = new AreaCharacterSheetArtContent(full, AreaCharacterSheetTexts.Project("t"), "t");
        Assert.IsTrue(content.ArtReady && content.PagesReady);
        var noPages = full.Where(pair => pair.Key != AreaCharacterSheetPagesLayout.TechBackgroundArt).ToDictionary(pair => pair.Key, pair => pair.Value);
        var partial = new AreaCharacterSheetArtContent(noPages, AreaCharacterSheetTexts.Project("t"), "t");
        Assert.IsTrue(partial.ArtReady, "the main page does not wait for the pages");
        Assert.IsFalse(partial.PagesReady);
    }
}
