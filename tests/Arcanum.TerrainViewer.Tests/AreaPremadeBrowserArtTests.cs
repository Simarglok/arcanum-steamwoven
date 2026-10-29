using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-066 pre-made browser art (ledger MW17–MW22): the art, the buttons' and fields' places, the stats' order and labels,
/// the centring and the story's lines, and the browsed hero's view, on synthetic glyph frames and tables.</summary>
[TestClass]
public sealed class AreaPremadeBrowserArtTests
{
    /// <summary>A font whose every glyph is 5 wide with an advance of 4, 19 high (frame 0 is character 31).</summary>
    private static AreaArtFont Font() =>
        new([.. Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, 19, new byte[5 * 19 * 4], 4))]);

    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.Latin1.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status);
        return parsed.File!;
    }

    [TestMethod]
    public void The_window_reads_the_originals_art_and_fields()
    {
        CollectionAssert.AreEqual(new[] { 766, 336, 335, 321, 323, 283, 284, 27, 171 }, AreaPremadeBrowserArtLayout.ArtNumbers.ToArray());
        Assert.AreEqual((95, 69), AreaPremadeBrowserArtLayout.Portrait, "MW18: the window's (95, 28) below the top strip");
        Assert.AreEqual((46, 224, 228, 19), AreaPremadeBrowserArtLayout.Name);
        Assert.AreEqual((66, 284, 186, 19), AreaPremadeBrowserArtLayout.Gender);
        Assert.AreEqual((66, 334, 186, 19), AreaPremadeBrowserArtLayout.Race);
        Assert.AreEqual((66, 384, 186, 19), AreaPremadeBrowserArtLayout.Background);
        Assert.AreEqual((339, 64, 409, 19), AreaPremadeBrowserArtLayout.StatsTitle);
        Assert.AreEqual((350, 196, 395, 212), AreaPremadeBrowserArtLayout.Description);
        Assert.AreEqual((350, 200, 395, 19), AreaPremadeBrowserArtLayout.RaceTitle);
        Assert.AreEqual((350, 226, 395, 182), AreaPremadeBrowserArtLayout.RaceText);
        Assert.IsFalse(AreaPremadeBrowserArtContent.Empty("none").ArtReady);
        Assert.IsNull(AreaPremadeBrowserArtContent.Empty("none").TextFont);
    }

    [TestMethod]
    public void The_buttons_stand_at_the_exes_places()
    {
        Assert.AreEqual((321, 675, 496), AreaPremadeBrowserArtLayout.Button(AreaPremadeBrowserButton.Start), "MW17: bottom strip (675, 55)");
        Assert.AreEqual((323, 69, 496), AreaPremadeBrowserArtLayout.Button(AreaPremadeBrowserButton.Back));
        Assert.AreEqual((283, 43, 118), AreaPremadeBrowserArtLayout.Button(AreaPremadeBrowserButton.Previous), "a window button: the screen's point");
        Assert.AreEqual((284, 244, 118), AreaPremadeBrowserArtLayout.Button(AreaPremadeBrowserButton.Next));
        Assert.AreEqual(7003, AreaPremadeBrowserArtLayout.Hint(AreaPremadeBrowserButton.Start));
        Assert.AreEqual(7002, AreaPremadeBrowserArtLayout.Hint(AreaPremadeBrowserButton.Back));
        Assert.IsNull(AreaPremadeBrowserArtLayout.Hint(AreaPremadeBrowserButton.Next), "the arrows carry no hint (-1)");
    }

    [TestMethod]
    public void Fifteen_stats_stand_in_four_columns()
    {
        CollectionAssert.AreEqual(new[] { 0, 2, 1, 3, 4, 6, 5, 7, 8, 9, 10, 11, 12, 13, 14 }, AreaPremadeBrowserArtLayout.StatOrder.ToArray());
        Assert.AreEqual(15, AreaPremadeBrowserArtLayout.StatFields.Count);
        Assert.AreEqual((346, 87, 50, 15), AreaPremadeBrowserArtLayout.StatFields[0]);
        Assert.AreEqual((406, 147, 50, 15), AreaPremadeBrowserArtLayout.StatFields[7]);
        Assert.AreEqual((472, 87, 139, 15), AreaPremadeBrowserArtLayout.StatFields[8]);
        Assert.AreEqual((618, 106, 147, 15), AreaPremadeBrowserArtLayout.StatFields[13], "the fourth column's second row is one higher");
        Assert.AreEqual(CharacterStat.Constitution, AreaPremadeBrowserArtLayout.Stat(2));
        Assert.AreEqual(CharacterStat.CarryWeight, AreaPremadeBrowserArtLayout.Stat(8), "the original's 8 is carry weight");
        Assert.AreEqual(CharacterStat.ReactionModifier, AreaPremadeBrowserArtLayout.Stat(14));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaPremadeBrowserArtLayout.Stat(15));
    }

    [TestMethod]
    public void A_narrower_line_is_centred_and_a_wider_one_starts_at_the_left()
    {
        var field = (10, 0, 100, 19);
        Assert.AreEqual(10 + (100 - 21) / 2, AreaPremadeBrowserArtLayout.Centred(field, 21));
        Assert.AreEqual(10, AreaPremadeBrowserArtLayout.Centred(field, 100), "as wide as the field: not centred (0x542DF0)");
        Assert.AreEqual(10, AreaPremadeBrowserArtLayout.Centred(field, 150));
    }

    [TestMethod]
    public void A_story_wraps_at_words_line_feeds_and_tabs()
    {
        var font = Font();
        // "aaaa bbbb": the first word advances 16, the space 4, the second measures 17: 37 in all.
        var lines = AreaPremadeBrowserArtLayout.Wrap(font, "aaaa bbbb", 36);
        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual(5, lines[0].Count, "the first line holds its breaking space (0x535C40)");
        Assert.AreEqual(0, lines[1][0].X, "the next line starts after the space");
        Assert.AreEqual(1, AreaPremadeBrowserArtLayout.Wrap(font, "aaaa bbbb", 37).Count, "a line as wide as the field fits");
        Assert.AreEqual(20, AreaPremadeBrowserArtLayout.Wrap(font, "aaaa bbbb", 37)[0][5].X, "the second word after the space");
        var hyphen = AreaPremadeBrowserArtLayout.Wrap(font, "aa-bb", 15);
        Assert.AreEqual((2, 3, 2), (hyphen.Count, hyphen[0].Count, hyphen[1].Count), "a line breaks after a hyphen, which it keeps");
        Assert.AreEqual(0, AreaPremadeBrowserArtLayout.Wrap(font, "aaaaaaaaaa", 10).Count, "a line with no break ends the text (0x535708)");
        Assert.AreEqual(1, AreaPremadeBrowserArtLayout.Wrap(font, "aa aaaaaaaaaa b", 10).Count, "the lines before it are drawn");
    }

    [TestMethod]
    public void Line_feeds_and_tabs_shape_the_lines()
    {
        var font = Font();
        var lines = AreaPremadeBrowserArtLayout.Wrap(font, "Name\r\n\r\n\tStory", 395);
        Assert.AreEqual(3, lines.Count, "two line feeds: a blank line between");
        Assert.AreEqual(0, lines[1].Count, "a carriage return has no glyph");
        Assert.AreEqual(20, lines[2][0].X, "a tab advances 20 (MW4)");
        Assert.AreEqual(0, AreaPremadeBrowserArtLayout.Wrap(font, "", 10).Count, "an empty text has no line");
        Assert.AreEqual(1, AreaPremadeBrowserArtLayout.Wrap(font, "ab\n", 10).Count, "a closing line feed starts no line");
        var escaped = AreaPremadeBrowserArtLayout.Wrap(font, @"a\nb\tc", 395);
        Assert.AreEqual(2, escaped.Count, "the two characters \\n start a line (0x5418A0)");
        Assert.AreEqual(4 + 5 * 4, escaped[1][^1].X, "the two characters \\t are five spaces (0x541830)");
    }

    [TestMethod]
    public void The_stats_are_the_starting_values_under_the_innate_effects()
    {
        var race = new StatEffectDefinition(71, [new StatModifier(CharacterStat.Strength, StatModifierOperator.Add, 4)]);
        var woman = new StatEffectDefinition(330, [new StatModifier(CharacterStat.Strength, StatModifierOperator.Add, -1),
            new StatModifier(CharacterStat.Constitution, StatModifierOperator.Add, 1)]);
        var hero = new SessionHeroDefinition(16090, CharacterRace.HalfOgre, CharacterGender.Female, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, race, woman, 0, []);
        var values = AreaPremadeBrowserArtLayout.StatValues(hero)!;
        Assert.AreEqual(15, values.Count);
        Assert.AreEqual((11, 9, 8), (values[0], values[1], values[2]), "strength 8 + 4 − 1, constitution 8 + 1, dexterity 8");
        var owner = CharacterStatsOwner.Create(new(CharacterRace.HalfOgre, CharacterGender.Female,
            new(new Dictionary<CharacterRace, StatEffectDefinition> { [CharacterRace.HalfOgre] = race }, new Dictionary<CharacterGender, StatEffectDefinition> { [CharacterGender.Female] = woman }),
            CharacterStatLimits.Default)).Owner!;
        var snapshot = owner.Query(new(0)).Snapshot!;
        Assert.AreEqual(snapshot.GetEffective(CharacterStat.CarryWeight), values[8], "the derived stats are the owner's");
        Assert.AreEqual(snapshot.GetEffective(CharacterStat.ReactionModifier), values[14]);
        Assert.IsNull(AreaPremadeBrowserArtLayout.StatValues(hero with { Stats = [8, 8] }), "a stored list of the wrong length");
    }

    [TestMethod]
    public void The_view_takes_the_heros_texts_from_the_game_tables()
    {
        var art = AreaPremadeBrowserArtLayout.ArtNumbers.ToDictionary(number => number,
            number => new AreaInterfaceArtLoad(number, AreaInterfaceArtStatus.Ready, null, [new AreaInterfaceFrame(5, 19, new byte[5 * 19 * 4], 4)], "synthetic"));
        var menu = Mes("{720}{Human}{725}{Halfling}{739}{Initial Stats}{740}{Female}{741}{Male}{745}{Race}{781}{Halfling man text}");
        var stats = Mes(string.Concat(Enumerable.Range(0, 15).Select(number => $"{{{500 + number}}}{{S{number}}}")));
        var backgrounds = BackgroundTable.Create(Mes("{390}{1039}{391}{}{392}{NPC}"), Mes("{1000}{Default\nText}{1039}{Merwin\r\n\r\n\tMerwin was raised.}"));
        var content = new AreaPremadeBrowserArtContent(art, menu, stats, backgrounds, null, "synthetic");
        Assert.IsTrue(content.ArtReady);
        var definition = new SessionHeroDefinition(16087, CharacterRace.Halfling, CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 0, []);
        var read = new PremadeHero(16067, 16087, 5, 1, 0, 20, 1017, 39, 0, new(69, true, []), null, 0, 0, [])
            { PlayerName = Encoding.Latin1.GetBytes("Merwin Tumblebrook"), BackgroundDescription = 1039 };
        var load = new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.Premade[0], definition, 2350, 0, new(PremadeHeroFailure.None, read), "synthetic");
        var view = content.View(0, load)!;
        Assert.AreEqual(("Merwin Tumblebrook", "Male", "Halfling", "Initial Stats", 1017), (view.Name, view.Gender, view.Race, view.StatsTitle, view.Portrait));
        Assert.AreEqual("Merwin", view.BackgroundName.TrimEnd('\r'), "the story's first line");
        Assert.IsNull(view.DescriptionTitle, "a description above 1000 shows the background's story");
        Assert.IsTrue(view.Description.EndsWith("\n\tMerwin was raised.", StringComparison.Ordinal) && !view.Description.Contains("Merwin\r"), view.Description);
        Assert.AreEqual("S0: 8", view.Stats[0]);
        Assert.AreEqual("S2: 8", view.Stats[1], "constitution second");
        var plain = content.View(0, load with { Read = new(PremadeHeroFailure.None, read with { BackgroundDescription = 0 }) })!;
        Assert.AreEqual(("Race", "Halfling man text"), (plain.DescriptionTitle, plain.Description), "else the race text 770 + 2 × race + gender");
        Assert.IsNull(content.View(0, load with { Status = AreaHeroStatus.StackFailed }), "a hero that did not read keeps the diagnostic window");
        Assert.IsNull(AreaPremadeBrowserArtContent.Empty("none").View(0, load));
    }
}
