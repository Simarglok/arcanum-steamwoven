using Arcanum.Data.Characters;
using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-068 new-character window art (ledger MW27–MW29): the art, the ten buttons' places, the name field's button and line,
/// the hover bands and the description they choose, and the new character's view, on synthetic glyph frames and tables over the
/// <see cref="AreaNewCharacterTests"/> fixture.</summary>
[TestClass]
public sealed class AreaNewCharacterArtTests
{
    /// <summary>A font whose every glyph is 5 wide with an advance of 4, 19 high.</summary>
    private static AreaArtFont Font() =>
        new([.. Enumerable.Range(0, 225).Select(_ => new AreaInterfaceFrame(5, 19, new byte[5 * 19 * 4], 4))]);

    private static AreaPremadeBrowserArtContent Content(bool arrows = true)
    {
        var art = AreaPremadeBrowserArtContent.AllArtNumbers.Where(number => arrows || number != AreaNewCharacterArtLayout.CycleRightArt).ToDictionary(number => number,
            number => new AreaInterfaceArtLoad(number, AreaInterfaceArtStatus.Ready, null, [new AreaInterfaceFrame(5, 19, new byte[5 * 19 * 4], 4)], "synthetic"));
        var menu = AreaNewCharacterTests.Mes("{720}{Human}{721}{Dwarf}{722}{Elf}{739}{Initial Stats}{740}{Female}{741}{Male}{742}{Woman text}{743}{Man text}"
            + "{745}{Race}{771}{Human man text}{772}{Dwarf woman text}{774}{Elf woman text}");
        var stats = AreaNewCharacterTests.Mes(string.Concat(Enumerable.Range(0, 15).Select(number => $"{{{500 + number}}}{{S{number}}}")));
        var backgrounds = BackgroundTable.Create(AreaNewCharacterTests.Mes("{0}{1000}{1}{86}{2}{}{3}{400}{4}{}\n{20}{1002}{21}{86}{22}{HUF,ELF}{23}{250}{24}{}"),
            AreaNewCharacterTests.Mes("{1000}{Default\nplain}{1002}{Lady\nfine story}"));
        return new(art, menu, stats, backgrounds, null, "synthetic");
    }

    [TestMethod]
    public void The_window_reads_its_art_and_its_buttons_places()
    {
        CollectionAssert.AreEqual(new[] { 765, 336, 335, 321, 323, 283, 284, 757, 758, 27, 171 }, AreaNewCharacterArtLayout.ArtNumbers.ToArray());
        CollectionAssert.AreEquivalent(new[] { 766, 765, 336, 335, 321, 323, 283, 284, 757, 758, 27, 171 }, AreaPremadeBrowserArtContent.AllArtNumbers.ToArray(),
            "one read serves both character windows");
        Assert.AreEqual((321, 675, 496), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.Accept), "MW27: the bottom strip's (675, 55)");
        Assert.AreEqual((323, 69, 496), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.Cancel));
        Assert.AreEqual((283, 43, 118), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.PortraitPrevious));
        Assert.AreEqual((284, 244, 118), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.PortraitNext));
        Assert.AreEqual((757, 42, 281), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.GenderPrevious), "beside the gender's field (y 284)");
        Assert.AreEqual((758, 255, 282), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.GenderNext), "the right arrow one row lower");
        Assert.AreEqual((757, 42, 331), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.RacePrevious));
        Assert.AreEqual((758, 255, 332), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.RaceNext));
        Assert.AreEqual((757, 42, 381), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.BackgroundPrevious));
        Assert.AreEqual((758, 255, 382), AreaNewCharacterArtLayout.Button(AreaNewCharacterArtButton.BackgroundNext));
        Assert.AreEqual(7003, AreaNewCharacterArtLayout.Hint(AreaNewCharacterArtButton.Accept));
        Assert.AreEqual(7002, AreaNewCharacterArtLayout.Hint(AreaNewCharacterArtButton.Cancel));
        Assert.IsNull(AreaNewCharacterArtLayout.Hint(AreaNewCharacterArtButton.RaceNext));
        Assert.AreEqual(AreaNewCharacterButton.GenderNext, AreaNewCharacterArtLayout.Arrow(AreaNewCharacterArtButton.GenderNext));
        Assert.AreEqual(AreaNewCharacterButton.PortraitPrevious, AreaNewCharacterArtLayout.Arrow(AreaNewCharacterArtButton.PortraitPrevious));
        Assert.IsNull(AreaNewCharacterArtLayout.Arrow(AreaNewCharacterArtButton.Accept));
        Assert.AreEqual((45, 224, 230, 20), AreaNewCharacterArtLayout.NameButton, "the name's rectangle two larger");
        CollectionAssert.AreEqual(new[] { AreaNewCharacterArtButton.RacePrevious, AreaNewCharacterArtButton.RaceNext }, AreaNewCharacterArtLayout.Focused(1).ToArray());
        Assert.AreEqual(0, AreaNewCharacterArtLayout.Focused(4).Count, "the name field's row lights no button");
    }

    [TestMethod]
    public void The_bands_are_43_rows_below_their_window_rectangles()
    {
        Assert.AreEqual(AreaNewCharacterHover.Gender, AreaNewCharacterArtLayout.Hover(22, 274), "(22, 231) + 43");
        Assert.AreEqual(AreaNewCharacterHover.Gender, AreaNewCharacterArtLayout.Hover(297, 312));
        Assert.AreEqual(AreaNewCharacterHover.None, AreaNewCharacterArtLayout.Hover(298, 300), "past the band's right edge");
        Assert.AreEqual(AreaNewCharacterHover.None, AreaNewCharacterArtLayout.Hover(100, 313), "between the bands");
        Assert.AreEqual(AreaNewCharacterHover.Race, AreaNewCharacterArtLayout.Hover(100, 324));
        Assert.AreEqual(AreaNewCharacterHover.Race, AreaNewCharacterArtLayout.Hover(100, 363));
        Assert.AreEqual(AreaNewCharacterHover.None, AreaNewCharacterArtLayout.Hover(100, 384), "the background's band is not tested");
    }

    [TestMethod]
    public void The_name_line_is_centred_or_shows_its_end_while_typed()
    {
        var font = Font();
        var field = AreaPremadeBrowserArtLayout.Name;
        Assert.AreEqual((AreaPremadeBrowserArtLayout.Centred(field, font.Measure("Vela").Width), "Vela"), AreaNewCharacterArtLayout.NameLine(font, "Vela", typing: false));
        Assert.AreEqual((AreaPremadeBrowserArtLayout.Centred(field, font.Measure(" ").Width), " "), AreaNewCharacterArtLayout.NameLine(font, "", typing: false),
            "an empty field draws a space (0x5A0A98)");
        Assert.AreEqual("Vela|", AreaNewCharacterArtLayout.NameLine(font, "Vela", typing: true).Text, "the caret closes the typed text (0x544100)");
        Assert.AreEqual("|", AreaNewCharacterArtLayout.NameLine(font, "", typing: true).Text);
        var wide = new string('a', 60);
        var (x, text) = AreaNewCharacterArtLayout.NameLine(font, wide, typing: true);
        Assert.AreEqual(field.X, x, "a typed text as wide as the field starts at its left");
        Assert.IsTrue(text.EndsWith("a|", StringComparison.Ordinal) && font.Measure(text).Width <= field.Width && font.Measure("a" + text).Width > field.Width,
            "its first characters are dropped until it fits (0x5418A0 flag 2): " + text.Length);
    }

    [TestMethod]
    public void The_new_character_view_is_drawn_from_the_window_s_object()
    {
        var content = Content();
        Assert.IsTrue(content.NewCharacterArtReady && content.ArtReady);
        Assert.IsFalse(Content(arrows: false).NewCharacterArtReady, "without an arrow's art the window keeps its diagnostic look");
        Assert.IsTrue(Content(arrows: false).ArtReady, "the browser needs no arrow");
        Assert.IsNull(Content(arrows: false).NewCharacterView(new AreaNewCharacter(AreaNewCharacterTests.Generic())));
        var creation = new AreaNewCharacter(AreaNewCharacterTests.Generic(), "Enter Name"u8.ToArray());
        var view = content.NewCharacterView(creation)!;
        Assert.AreEqual(("Enter Name", false, "Male", "Human", "Default", 1005), (view.Character.Name, view.Typing, view.Character.Gender, view.Character.Race,
            view.Character.BackgroundName, view.Character.Portrait), "the generic human man, background 0 reading 1000 (MW19)");
        Assert.AreEqual(("Race", "Human man text"), (view.Character.DescriptionTitle, view.Character.Description), "description 0: the race's text");
        Assert.AreEqual(("Man text", "Race", "Human man text"), (view.GenderText, view.RaceTitle, view.RaceText));
        Assert.AreEqual("S0: 8", view.Character.Stats[0]);
        Assert.AreEqual("S2: 8", view.Character.Stats[1], "constitution 8 + race 0 (the fixture's effect 64 adds the race number)");
        // An elf woman with background 2 (description 1002, effect 86: strength + 1).
        creation.Press(AreaNewCharacterButton.RaceNext, AreaNewCharacterTests.Steamwoven);
        creation.Press(AreaNewCharacterButton.RaceNext, AreaNewCharacterTests.Steamwoven);
        creation.Press(AreaNewCharacterButton.GenderNext, AreaNewCharacterTests.Steamwoven);
        creation.Press(AreaNewCharacterButton.BackgroundNext, AreaNewCharacterTests.Steamwoven);
        Assert.AreEqual(1002, creation.Chosen?.Description);
        var elf = content.NewCharacterView(creation)!;
        Assert.AreEqual(("Female", "Elf", "Lady"), (elf.Character.Gender, elf.Character.Race, elf.Character.BackgroundName));
        Assert.IsNull(elf.Character.DescriptionTitle, "a description above 1000: the story");
        Assert.AreEqual("fine story", elf.Character.Description);
        Assert.AreEqual("S0: 9", elf.Character.Stats[0], "the background's effect is in the shown strength (the object carries it)");
        Assert.AreEqual("S2: 10", elf.Character.Stats[1], "constitution 8 + 2 for the elf");
        Assert.AreEqual(("Woman text", "Elf woman text"), (elf.GenderText, elf.RaceText));
        creation.BeginName();
        Assert.AreEqual(("", true), (content.NewCharacterView(creation)!.Character.Name, content.NewCharacterView(creation)!.Typing), "a click cleared the field");
    }

    [TestMethod]
    public void The_bands_choose_the_description()
    {
        var content = Content();
        var creation = new AreaNewCharacter(AreaNewCharacterTests.Generic());
        var plain = content.NewCharacterView(creation)!;
        Assert.AreEqual(new AreaCharacterDescription("Race", "Human man text", AreaPremadeBrowserArtLayout.RaceText), AreaNewCharacterArtLayout.Description(plain, AreaNewCharacterHover.None));
        Assert.AreEqual(new AreaCharacterDescription(null, "Man text", AreaPremadeBrowserArtLayout.RaceText), AreaNewCharacterArtLayout.Description(plain, AreaNewCharacterHover.Gender),
            "the gender's text without a title where the race's stands (0x5C5090)");
        foreach (var press in new[] { AreaNewCharacterButton.RaceNext, AreaNewCharacterButton.RaceNext, AreaNewCharacterButton.GenderNext, AreaNewCharacterButton.BackgroundNext })
            creation.Press(press, AreaNewCharacterTests.Steamwoven);
        var story = content.NewCharacterView(creation)!;
        Assert.AreEqual(new AreaCharacterDescription(null, "fine story", AreaPremadeBrowserArtLayout.Description), AreaNewCharacterArtLayout.Description(story, AreaNewCharacterHover.None),
            "the story in the whole field");
        Assert.AreEqual(new AreaCharacterDescription("Race", "Elf woman text", AreaPremadeBrowserArtLayout.RaceText), AreaNewCharacterArtLayout.Description(story, AreaNewCharacterHover.Race),
            "the race's band shows the race's text over the story");
        Assert.AreEqual("Woman text", AreaNewCharacterArtLayout.Description(story, AreaNewCharacterHover.Gender).Text);
    }

    [TestMethod]
    public void A_press_beside_the_name_field_ends_its_typing()
    {
        var menu = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null);
        menu.Type('s'); menu.Type('n'); menu.Type('n');
        menu.OpenNewCharacter(new AreaNewCharacter(AreaNewCharacterTests.Generic(), "Enter Name"u8.ToArray()));
        var creation = menu.Creation!;
        menu.PressOutsideName();
        Assert.IsFalse(creation.TypingName, "nothing to end");
        menu.BeginName();
        Assert.IsTrue(creation.TypingName && creation.Name.IsEmpty);
        menu.PressOutsideName();
        Assert.IsFalse(creation.TypingName, "the end call (NW8)");
        Assert.AreEqual("Enter Name", creation.NameText(), "an empty field takes its old text back");
    }

    [TestMethod]
    public void A_pre_made_heros_definition_carries_its_story_number_and_an_effect_its_stat_part()
    {
        var read = new PremadeHero(16067, 16087, 5, 1, 0, 20, 1017, 39, 0, new(69, true, []), null, 0, 0, []) { BackgroundDescription = 1039 };
        var definition = AreaStartingHero.Definition(read, out _);
        Assert.AreEqual((39, 1039), (definition.Background, definition.BackgroundDescription), "field 0x102 (MW19)");
        // ARC-UI-069: and its player name (field 0x10E, MW21); one the session would refuse starts empty.
        Assert.AreEqual(0, definition.PlayerName.Count);
        var named = AreaStartingHero.Definition(read with { PlayerName = "Merwin"u8.ToArray() }, out _);
        CollectionAssert.AreEqual("Merwin"u8.ToArray(), named.PlayerName.ToArray());
        var tooLong = AreaStartingHero.Definition(read with { PlayerName = [.. Enumerable.Repeat((byte)'a', SessionHeroLimits.MaxPlayerNameBytes + 1)] }, out _);
        Assert.AreEqual(0, tooLong.PlayerName.Count);
        var rule = new EffectRule(86, true, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1), new(EffectAttributeKind.BasicSkill, 1, EffectChangeOperator.Add, 2)]);
        var stat = AreaStartingHero.StatEffect(rule)!;
        Assert.AreEqual(86, stat.EffectId);
        Assert.AreEqual(1, stat.Modifiers.Length, "the skill change is not a stat's");
        Assert.AreEqual(new Arcanum.Simulation.Character.StatModifier(Arcanum.Simulation.Character.CharacterStat.Strength,
            Arcanum.Simulation.Character.StatModifierOperator.Add, 1), stat.Modifiers[0]);
        Assert.IsNull(AreaStartingHero.StatEffect(new EffectRule(87, true, [new(EffectAttributeKind.BasicSkill, 1, EffectChangeOperator.Add, 2)])));
        Assert.IsNull(AreaStartingHero.StatEffect(null));
    }
}
