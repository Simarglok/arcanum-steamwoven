using System.Text;
using Arcanum.Data.Messages;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-075 character sheet original look (ledger CHW1–CHW9): the art numbers, the buttons' and regions' places, the hover order,
/// the gauges and the texts, on an invented view and invented tables (no original data).</summary>
[TestClass]
public sealed class AreaCharacterSheetArtTests
{
    private static MesFile Mes(string text) => MesFile.Parse(Encoding.Latin1.GetBytes(text)).File!;

    private static AreaCharacterSheetView View(int alignment = 0, int magick = 0, int tech = 0, int? toNext = 1000, string name = "Hero") => new(name, CharacterRace.Elf,
        CharacterGender.Female, 3, 500, toNext, 7, alignment, magick, tech, 20, 25, 30, 35,
        [.. Enumerable.Range(0, 8).Select(i => new AreaSheetValue("S" + i, 10 + i, i == 1 ? AreaSheetMark.Raised : i == 2 ? AreaSheetMark.Lowered : AreaSheetMark.None))],
        [.. Enumerable.Range(0, 8).Select(i => new AreaSheetValue("D" + i, 100 + i))],
        [.. Enumerable.Range(0, 5).Select(i => new AreaSheetValue("R" + i, 50 + i))]);

    // Every glyph 5 wide.
    private static int Measure(AreaSheetFont font, string text) => text.Length * 5;

    [TestMethod]
    public void The_lens_is_the_close_square_with_the_cover_art_loaded_and_no_reason_for_the_diagnostic_window()
    {
        // ARC-UI-2694 (CHW9, 0x5597C0 turning the view-window mode 1 on with art 198): the live lens lies in the close rectangle.
        Assert.AreEqual((12, 10, 89), AreaCharacterSheetArtLayout.Lens, "the close rectangle (12, 10, 89, 89) of CHW9");
        CollectionAssert.Contains(AreaCharacterSheetArtLayout.AllArtNumbers.ToArray(), AreaCharacterSheetArtLayout.LensArt);
        CollectionAssert.DoesNotContain(AreaCharacterSheetArtLayout.ArtNumbers.ToArray(), AreaCharacterSheetArtLayout.LensArt, "the cover is optional: the sheet's look does not wait for it");
    }

    [TestMethod]
    public void The_window_reads_its_arts_and_the_tabs_and_buttons_stand_where_the_tables_put_them()
    {
        CollectionAssert.AreEqual(new[] { 22, 23, 24, 34, 35, 36, 254, 255, 27, 229, 171 }, AreaCharacterSheetArtLayout.ArtNumbers.ToArray());
        CollectionAssert.AreEqual(new[] { 22, 23, 24, 34, 35, 36, 254, 255, 27, 229, 171, 568 }, AreaCharacterSheetArtLayout.AllArtNumbers.Take(12).ToArray(), "then the skills and tech pages' arts (ARC-UI-076)");
        Assert.AreEqual((34, 527, 11, 132), AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Skills));
        Assert.AreEqual((35, 595, 11, 133), AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Tech));
        Assert.AreEqual((36, 663, 11, 134), AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Spells));
        Assert.AreEqual((568, 754, 21, 140), AreaCharacterSheetArtLayout.Tab(AreaSheetTab.Schemes));
        // 0x5C8028 (minus) and 0x5C7F88 (plus): hit points, fatigue, then the left column (ST, CN, DX, BE), then the right (IN, WP, PE, CH).
        (int, int)[] minus = [(409, 144), (409, 214), (170, 117), (170, 157), (170, 197), (170, 237), (313, 117), (313, 157), (313, 197), (313, 237)];
        (int, int)[] plus = [(465, 144), (465, 214), (224, 117), (224, 157), (224, 197), (224, 237), (367, 117), (367, 157), (367, 197), (367, 237)];
        for (var row = 0; row < AreaStatsPage.Rows; row++)
        {
            Assert.AreEqual(minus[row], AreaCharacterSheetArtLayout.Minus(row), "minus " + row);
            Assert.AreEqual(plus[row], AreaCharacterSheetArtLayout.Plus(row), "plus " + row);
        }
        CollectionAssert.AreEqual(new[] { 106, 107, 109, 110, 111, 112, 113, 114, 115, 116 }, Enumerable.Range(0, 10).Select(AreaCharacterSheetArtLayout.RowHint).ToArray());
        Assert.AreEqual((503, 63, 235, 325), AreaCharacterSheetArtLayout.Panel);
        Assert.AreEqual((255, 210, 0), AreaCharacterSheetArtLayout.Rgb(AreaSheetColour.Gold));
        Assert.AreEqual((0, 255, 0), AreaCharacterSheetArtLayout.Rgb(AreaSheetColour.Green));
        Assert.AreEqual((255, 0, 0), AreaCharacterSheetArtLayout.Rgb(AreaSheetColour.Red));
    }

    [TestMethod]
    public void The_pointer_regions_are_the_exes_table_and_the_first_one_that_contains_the_pointer_answers()
    {
        Assert.AreEqual(32, AreaCharacterSheetArtLayout.Regions.Count);
        CollectionAssert.AreEqual(Enumerable.Range(100, 32).ToArray(), AreaCharacterSheetArtLayout.Regions.Select(region => region.Id).ToArray(), "ids 100..131 in table order");
        Assert.AreEqual(100, AreaCharacterSheetArtLayout.RegionAt(150, 30), "the portrait");
        Assert.AreEqual(101, AreaCharacterSheetArtLayout.RegionAt(220, 40), "the character info");
        Assert.AreEqual(101, AreaCharacterSheetArtLayout.RegionAt(340, 40), "101 is created before 103 and 104 and takes their rectangle");
        Assert.AreEqual(103, AreaCharacterSheetArtLayout.RegionAt(457, 40), "103 answers only in the strip past 101's edge (x 455..459)");
        Assert.AreEqual(105, AreaCharacterSheetArtLayout.RegionAt(457, 80));
        Assert.IsFalse(Enumerable.Range(0, 800).SelectMany(x => Enumerable.Range(0, 400).Select(y => AreaCharacterSheetArtLayout.RegionAt(x, y))).Contains(104),
            "104 has 103's rectangle and is created after it: it never answers (the original's flaw)");
        Assert.AreEqual(102, AreaCharacterSheetArtLayout.RegionAt(50, 180), "the alignment gauge");
        Assert.AreEqual(106, AreaCharacterSheetArtLayout.RegionAt(440, 150));
        Assert.AreEqual(107, AreaCharacterSheetArtLayout.RegionAt(440, 220));
        Assert.AreEqual(108, AreaCharacterSheetArtLayout.RegionAt(770, 200), "the magick-tech meter");
        Assert.AreEqual(109, AreaCharacterSheetArtLayout.RegionAt(130, 115));
        Assert.AreEqual(116, AreaCharacterSheetArtLayout.RegionAt(270, 235));
        Assert.AreEqual(117, AreaCharacterSheetArtLayout.RegionAt(60, 290));
        Assert.AreEqual(118, AreaCharacterSheetArtLayout.RegionAt(60, 310));
        Assert.AreEqual(125, AreaCharacterSheetArtLayout.RegionAt(180, 364));
        Assert.AreEqual(126, AreaCharacterSheetArtLayout.RegionAt(320, 290));
        Assert.AreEqual(131, AreaCharacterSheetArtLayout.RegionAt(330, 366));
        Assert.IsNull(AreaCharacterSheetArtLayout.RegionAt(600, 200));
    }

    [TestMethod]
    public void The_gauges_follow_the_alignment_and_the_aptitude()
    {
        // 0x55EC90: the alignment (as the sheet holds it, the stored value / 10) over ten plus ten picks frame 0..20, truncating toward zero.
        Assert.AreEqual(0, AreaCharacterSheetArtLayout.AlignmentFrame(-100, 21));
        Assert.AreEqual(10, AreaCharacterSheetArtLayout.AlignmentFrame(0, 21));
        Assert.AreEqual(10, AreaCharacterSheetArtLayout.AlignmentFrame(-9, 21), "−9 / 10 truncates to 0");
        Assert.AreEqual(9, AreaCharacterSheetArtLayout.AlignmentFrame(-10, 21));
        Assert.AreEqual(20, AreaCharacterSheetArtLayout.AlignmentFrame(100, 21));
        Assert.AreEqual(5, AreaCharacterSheetArtLayout.AlignmentFrame(500, 6), "clamped to the art's frames");
        Assert.AreEqual((50, 160), AreaCharacterSheetArtLayout.AlignmentTopLeft(6, 21), "the hot point lands on (56, 181)");
        Assert.AreEqual((775, 221), AreaCharacterSheetArtLayout.AptitudeMarker(0));
        Assert.AreEqual((775, 93), AreaCharacterSheetArtLayout.AptitudeMarker(100), "221 − 100 × 128 / 100");
        Assert.AreEqual((775, 285), AreaCharacterSheetArtLayout.AptitudeMarker(-50));
        Assert.AreEqual((775, 220), AreaCharacterSheetArtLayout.AptitudeMarker(1), "1 × 128 / 100 truncates to 1");
    }

    [TestMethod]
    public void A_name_is_cut_from_its_end_until_it_fits()
    {
        Assert.AreEqual("Hero", AreaCharacterSheetArtLayout.FitName("Hero", text => text.Length * 5));
        var cut = AreaCharacterSheetArtLayout.FitName(new string('W', 80), text => text.Length * 5);
        Assert.AreEqual(48, cut.Length, "243 / 5 characters");
        Assert.AreEqual(string.Empty, AreaCharacterSheetArtLayout.FitName("W", _ => 1000));
        Assert.AreEqual(100, AreaCharacterSheetArtLayout.Left(new(AreaSheetFont.Label, 110, 0, "ab", AreaSheetColour.White, true), 20), "centred on x");
        Assert.AreEqual(110, AreaCharacterSheetArtLayout.Left(new(AreaSheetFont.Label, 110, 0, "ab", AreaSheetColour.White, false), 20));
    }

    [TestMethod]
    public void The_texts_stand_at_the_exes_places_in_its_fonts_and_colours()
    {
        var texts = AreaCharacterSheetArtLayout.Texts(View(alignment: -25, magick: 30, tech: 0), AreaCharacterSheetTexts.Project("test"), Measure);
        AreaSheetText At(int x, int y) => texts.Single(text => text.X == x && text.Y == y);
        Assert.AreEqual(new AreaSheetText(AreaSheetFont.Label, 212, 15, "Hero", AreaSheetColour.Gold, false), At(212, 15));
        Assert.AreEqual("Level: 3", At(212, 34).Text);
        Assert.AreEqual("Points: 7", At(212, 53).Text);
        Assert.AreEqual("XP Next: 1000", At(212, 72).Text);
        Assert.AreEqual("Race: elf", At(337, 34).Text);
        Assert.AreEqual("Gender: female", At(337, 53).Text);
        Assert.AreEqual((AreaSheetFont.Label, "Derived Stats", true), (At(178, 282).Font, At(178, 282).Text, At(178, 282).Centred));
        Assert.AreEqual("Resistance", At(383, 282).Text);
        Assert.AreEqual(" 25 ", At(447, 143).Text, "maximum hit points, centred");
        Assert.AreEqual(" 35 ", At(447, 213).Text, "maximum fatigue");
        // The primary stats: ST, CN, DX, BE at x 211, IN, WP, PE, CH at x 350, rows 40 apart from 115; raised green, lowered red.
        Assert.AreEqual((" 10 ", AreaSheetColour.White), (At(211, 115).Text, At(211, 115).Colour));
        Assert.AreEqual((" 11 ", AreaSheetColour.Green), (At(211, 155).Text, At(211, 155).Colour));
        Assert.AreEqual((" 12 ", AreaSheetColour.Red), (At(211, 195).Text, At(211, 195).Colour));
        Assert.AreEqual(" 17 ", At(350, 235).Text);
        // Derived stats and resistances in Flare12: two columns of four from y 308, then five from y 301.
        Assert.AreEqual((AreaSheetFont.Flare, "Carry Wt: 100"), (At(49, 308).Font, At(49, 308).Text));
        Assert.AreEqual("Speed: 103", At(49, 362).Text);
        Assert.AreEqual("Heal Rate: 104", At(178, 308).Text);
        Assert.AreEqual("Followers: 107", At(178, 362).Text);
        Assert.AreEqual("Damage: 50", At(322, 301).Text);
        Assert.AreEqual("Electrical: 54", At(322, 365).Text);
        // Cloister18: the alignment number and the aptitude numbers.
        Assert.AreEqual((AreaSheetFont.Cloister, "-25", true), (At(57, 198).Font, At(57, 198).Text, At(57, 198).Centred));
        Assert.AreEqual("30", At(767, 66).Text);
        Assert.AreEqual("0", At(767, 365).Text);
        Assert.AreEqual(3 + 1 + 2 + 2 + 2 + 8 + 8 + 5 + 3, texts.Count, "every text: name, level, points, xp, race, gender, two headers, hp, fatigue, 8, 8, 5, 3");
    }

    [TestMethod]
    public void The_last_level_prints_the_sentinel_and_unknown_experience_the_label_alone_and_a_long_name_is_cut()
    {
        var last = AreaCharacterSheetArtLayout.Texts(View(toNext: ExperienceRules.NoNextLevel, name: new string('W', 80)), AreaCharacterSheetTexts.Project("test"), Measure);
        Assert.AreEqual("XP Next: " + ExperienceRules.NoNextLevel, last.Single(text => text.Y == 72 && text.X == 212).Text, "XP3: 999999, printed as any value");
        var unknown = AreaCharacterSheetArtLayout.Texts(View(toNext: null), AreaCharacterSheetTexts.Project("test"), Measure);
        Assert.AreEqual("XP Next", unknown.Single(text => text.Y == 72 && text.X == 212).Text, "no experience data: PROJECT, the label alone");
        Assert.AreEqual(48, last.Single(text => text.Colour == AreaSheetColour.Gold).Text.Length);
    }

    [TestMethod]
    public void The_texts_come_from_the_games_tables_and_fall_back_to_project_labels()
    {
        var texts = AreaCharacterSheetTexts.Create(
            Mes("{0}{Pts}{1}{Lvl}{12}{Dmg}{100}{Portrait\nThis is the appearance.}{109}{Strength\nMuscle}{18}{Not enough points.}"),
            Mes("{8}{CarryW}{11}{Spd}"), Mes("{722}{Elfy}{740}{Fem}"));
        Assert.AreEqual("Pts", texts.Line(0));
        Assert.AreEqual("Lvl", texts.Line(1));
        Assert.AreEqual("XP Next", texts.Line(2), "a missing number keeps the project label");
        Assert.AreEqual("Dmg", texts.Line(12));
        Assert.AreEqual("Magick", texts.Line(13));
        Assert.AreEqual("CarryW", texts.DerivedName(0));
        Assert.AreEqual("Spd", texts.DerivedName(3));
        Assert.AreEqual("Damage Bonus", texts.DerivedName(1), "a missing number keeps the project label");
        Assert.AreEqual("Elfy", texts.RaceName(CharacterRace.Elf));
        Assert.AreEqual("Fem", texts.GenderName(CharacterGender.Female));
        Assert.AreEqual("male", texts.GenderName(CharacterGender.Male));
        Assert.AreEqual("Portrait\nThis is the appearance.", texts.Hint(100));
        Assert.IsNull(texts.Hint(101));
        Assert.AreEqual("Not enough points.", texts.Refusal(18));
        var drawn = AreaCharacterSheetArtLayout.Texts(View(), texts, Measure);
        Assert.AreEqual("Pts: 7", drawn.Single(text => text.Y == 53 && text.X == 212).Text);
        Assert.AreEqual("CarryW: 100", drawn.Single(text => text.Y == 308 && text.X == 49).Text);
        Assert.AreEqual("Race: Elfy", drawn.Single(text => text.Y == 34 && text.X == 337).Text);
    }

    [TestMethod]
    public void The_sheet_art_content_is_ready_only_when_every_art_read()
    {
        static AreaInterfaceArtLoad Load(int number) => new(number, AreaInterfaceArtStatus.Ready, null, [new AreaInterfaceFrame(5, 5, new byte[100], 0, 0)], "synthetic");
        var full = AreaCharacterSheetArtLayout.AllArtNumbers.ToDictionary(number => number, Load);
        Assert.IsTrue(new AreaCharacterSheetArtContent(full, AreaCharacterSheetTexts.Project("t"), "t").ArtReady);
        var noSchemes = full.Where(pair => pair.Key != AreaCharacterSheetArtLayout.SchemesTabArt).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.IsTrue(new AreaCharacterSheetArtContent(noSchemes, AreaCharacterSheetTexts.Project("t"), "t").ArtReady, "the schemes tab is no reason for the diagnostic window");
        var noGauge = full.Where(pair => pair.Key != AreaCharacterSheetArtLayout.AlignmentArt).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.IsFalse(new AreaCharacterSheetArtContent(noGauge, AreaCharacterSheetTexts.Project("t"), "t").ArtReady);
        Assert.IsFalse(AreaCharacterSheetArtContent.Empty("self-test").ArtReady);
    }

    /// <summary>ARC-UI-1791 (ledger CHW25): a PC's sheet adds the Age line (charedit.mes 6, `": %d "`) at (337, 72) in the label font, white; none without an age.</summary>
    [TestMethod]
    public void The_age_line_stands_under_race_and_gender_for_a_pc_only()
    {
        var texts = AreaCharacterSheetTexts.Create(Mes("{4}{Race}{5}{Gender}{6}{Age}"), null, null);
        var with = AreaCharacterSheetArtLayout.Texts(View() with { Age = 31 }, texts, Measure);
        var without = AreaCharacterSheetArtLayout.Texts(View(), texts, Measure);
        Assert.AreEqual(without.Count + 1, with.Count);
        Assert.AreEqual(new AreaSheetText(AreaSheetFont.Label, 337, 72, "Age: 31", AreaSheetColour.White, false), with.Single(text => text.Text.StartsWith("Age", StringComparison.Ordinal)));
        Assert.AreEqual(53, with.Single(text => text.Text.StartsWith("Gender", StringComparison.Ordinal)).Y);
        Assert.IsFalse(without.Any(text => text.Text.StartsWith("Age", StringComparison.Ordinal)));
        Assert.IsTrue(AreaCharacterSheetArtLayout.AllArtNumbers.Intersect(AreaCharacterSheetIcon.ArtNumbers).Count() == AreaCharacterSheetIcon.ArtNumbers.Distinct().Count(), "the sheet reads the icons' arts");
        CollectionAssert.DoesNotContain(AreaCharacterSheetArtLayout.ArtNumbers.ToArray(), 375, "the icons are optional: the sheet's look does not wait for them");
    }
}
