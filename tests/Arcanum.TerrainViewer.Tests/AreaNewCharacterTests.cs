using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-030 (N10b): the new-character window (ledger NW1–NW5, BD4, BD10) over a synthetic generic hero and background table.</summary>
[TestClass]
public sealed class AreaNewCharacterTests
{
    // Block 0 for everyone (effect 86), block 1 for a dwarf man (effect 87, no rule), block 2 for a human or elf woman (effect 86).
    private const string Blocks = "{0}{1000}{1}{86}{2}{}{3}{400}{4}{}\n{10}{1001}{11}{87}{12}{DWM}{13}{0}{14}{}\n{20}{1002}{21}{86}{22}{HUF,ELF}{23}{0}{24}{}";
    private const string Texts = "{1000}{Default\nplain}{1001}{Stout\nstub}{1002}{Lady\nfine}";

    private static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.Latin1.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, $"line {parsed.Line}");
        return parsed.File!;
    }

    // Race effect 64 + race raises constitution by the race number, the female effect beauty by 1, background effect 86 strength by 1.
    private static EffectRule? Effect(int number) => number switch
    {
        >= 64 and <= 71 => new(number, true, [new(EffectAttributeKind.Stat, 2, EffectChangeOperator.Add, number - 64)]),
        330 => new(number, true, [new(EffectAttributeKind.Stat, 3, EffectChangeOperator.Add, 1)]),
        86 => new(number, true, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1)]),
        _ => null,
    };

    private static AreaHeroLoad Generic(bool backgrounds = true)
    {
        var read = new PremadeHero(AreaStartingHero.GenericPrototype, AreaStartingHero.GenericPrototype + PremadeHeroReader.PrototypeOffset, 0, 1, 0, 20, 1005, 0, 0,
            Effect(64)!, null, 0, 0, []);
        return new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.NewCharacter, AreaStartingHero.Definition(read, out _), null, 0,
            new PremadeHeroRead(PremadeHeroFailure.None, read), "synthetic", Effects: Effect)
        {
            Backgrounds = backgrounds ? BackgroundTable.Create(Mes(Blocks), Mes(Texts)) : null,
            BackgroundsDetail = backgrounds ? null : "FileMissing rules/backgrnd.mes",
        };
    }

    private static readonly CharacterCreationRuleProfile Steamwoven = CharacterCreationRuleProfile.Steamwoven;

    [TestMethod]
    public void The_window_starts_as_the_generic_human_man_without_a_background_set()
    {
        var creation = new AreaNewCharacter(Generic());
        Assert.AreEqual((CharacterRace.Human, CharacterGender.Male, 0), (creation.Race, creation.Gender, creation.Background));
        Assert.IsNull(creation.Chosen, "background 0 without its description number or effect (NW1)");
        Assert.AreEqual(("Default", "plain"), creation.BackgroundText(), "description 0 shows the default text 1000");
        Assert.ThrowsExactly<ArgumentException>(() => new AreaNewCharacter(Generic() with { Hero = AreaStartingHero.Premade[0] }));
        Assert.ThrowsExactly<ArgumentException>(() => new AreaNewCharacter(Generic() with { Status = AreaHeroStatus.HeroRefused }));
    }

    [TestMethod]
    public void A_background_press_sets_the_landed_block_and_a_race_change_clears_it()
    {
        var creation = new AreaNewCharacter(Generic());
        Assert.IsNull(creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven));
        Assert.AreEqual(new BackgroundChoice(0, 1000, 86), creation.Chosen, "no block after 0 is offered to a human man: the button wraps to the first (BD4)");
        Assert.IsNull(creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven));
        Assert.AreEqual((CharacterRace.Dwarf, 0), (creation.Race, creation.Background));
        Assert.IsNull(creation.Chosen, "the race change cleared the background (BD10)");
        creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven);
        Assert.AreEqual(new BackgroundChoice(1, 1001, 87), creation.Chosen);
        Assert.AreEqual(("Stout", "stub"), creation.BackgroundText());
        creation.Press(AreaNewCharacterButton.BackgroundPrevious, Steamwoven);
        Assert.AreEqual(new BackgroundChoice(0, 1000, 86), creation.Chosen);
    }

    [TestMethod]
    public void A_refused_gender_press_clears_the_background_only_under_original()
    {
        foreach (var rules in CharacterCreationRuleProfile.All)
        {
            var creation = new AreaNewCharacter(Generic());
            creation.Press(AreaNewCharacterButton.RaceNext, rules);
            creation.Press(AreaNewCharacterButton.BackgroundNext, rules);
            Assert.IsNull(creation.Press(AreaNewCharacterButton.GenderNext, rules));
            Assert.AreEqual(CharacterGender.Male, creation.Gender, "a dwarf has no women");
            Assert.AreEqual(rules.RefusedGenderClearsBackground ? null : new BackgroundChoice(1, 1001, 87), creation.Chosen, rules.Name + " (NW4)");
        }
    }

    [TestMethod]
    public void A_woman_steps_from_the_cleared_background_and_previous_wraps_to_the_last_offered_block()
    {
        var creation = new AreaNewCharacter(Generic());
        creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.GenderPrevious, Steamwoven);
        Assert.AreEqual((CharacterGender.Female, 0), (creation.Gender, creation.Background));
        Assert.IsNull(creation.Chosen, "the gender change cleared the background");
        creation.Press(AreaNewCharacterButton.BackgroundPrevious, Steamwoven);
        Assert.AreEqual(new BackgroundChoice(2, 1002, 86), creation.Chosen, "nothing before 0: the last block reached forward (0x545300)");
        creation.Press(AreaNewCharacterButton.RacePrevious, Steamwoven);
        Assert.AreEqual(CharacterRace.HalfOrc, creation.Race, "a woman wraps to the last race with women");
    }

    [TestMethod]
    public void Without_the_background_table_a_background_press_is_refused_and_nothing_changes()
    {
        var creation = new AreaNewCharacter(Generic(backgrounds: false));
        StringAssert.Contains(creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven), "FileMissing");
        Assert.IsNull(creation.Chosen);
        Assert.IsNull(creation.BackgroundText());
    }

    [TestMethod]
    public void The_hero_has_the_chosen_race_and_gender_effects_and_the_session_gets_the_background_with_its_effect()
    {
        var creation = new AreaNewCharacter(Generic());
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.GenderNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven);
        Assert.AreEqual((CharacterRace.Elf, CharacterGender.Female), (creation.Race, creation.Gender));
        Assert.AreEqual(new BackgroundChoice(2, 1002, 86), creation.Chosen);
        var hero = creation.Hero(out var detail) ?? throw new AssertFailedException(detail ?? "hero");
        var definition = hero.Definition!;
        Assert.AreEqual((AreaStartingHero.GenericPrototype + PremadeHeroReader.PrototypeOffset, CharacterRace.Elf, CharacterGender.Female, 0),
            (definition.Prototype, definition.Race, definition.Gender, definition.Background));
        Assert.AreEqual((66, 330), (definition.RaceEffect!.EffectId, definition.GenderEffect!.EffectId), "NW3: 64 + race and the female effect");
        Assert.IsNull(hero.Flag, "no main-menu flag for a new character");

        var created = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, hero);
        using var owner = created.Owner ?? throw new AssertFailedException("session " + created.Failure);
        Assert.AreEqual((0, 0), (owner.QueryHeroBackground(), owner.QueryHeroBackgroundDescription()));
        Assert.IsNull(creation.Apply(owner, Effect));
        Assert.AreEqual((2, 1002), (owner.QueryHeroBackground(), owner.QueryHeroBackgroundDescription()));
        var effects = owner.QueryHeroEffects()!;
        Assert.AreEqual(1, effects.Effects.Count(e => e.Number == 86 && e.Cause == EffectCause.Background));
        var stats = owner.QueryHeroStats(new(0))!.Value.Snapshot!;
        Assert.AreEqual((9, 10, 9), (stats.GetEffective(CharacterStat.Strength), stats.GetEffective(CharacterStat.Constitution),
            stats.GetEffective(CharacterStat.Beauty)), "background +1 ST, elf +2 CN, woman +1 BE");
    }

    [TestMethod]
    public void A_window_left_without_a_background_press_starts_with_background_0_and_no_effect()
    {
        var creation = new AreaNewCharacter(Generic());
        creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        var hero = creation.Hero(out _)!;
        using var owner = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, hero).Owner!;
        var revision = owner.Version.Revision;
        Assert.IsNull(creation.Apply(owner, Effect));
        Assert.AreEqual(revision, owner.Version.Revision, "nothing to set");
        Assert.AreEqual(0, owner.QueryHeroEffects()!.Effects.Count(e => e.Cause == EffectCause.Background));
    }

    [TestMethod]
    public void A_background_effect_without_a_rule_enters_the_list_with_no_changes()
    {
        var creation = new AreaNewCharacter(Generic());
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven);
        using var owner = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, creation.Hero(out _)).Owner!;
        Assert.IsNull(creation.Apply(owner, Effect));
        Assert.AreEqual((1, 1001), (owner.QueryHeroBackground(), owner.QueryHeroBackgroundDescription()));
        Assert.AreEqual(1, owner.QueryHeroEffects()!.Effects.Count(e => e.Number == 87 && e.Cause == EffectCause.Background));
    }

    // The installed original (opt-in): the generic prototype reads as the human man with the background table (79 blocks); the elf
    // woman gets effects 66 and 330, and every block the window offers her reads a description and effect number. No text is printed.
    [TestMethod, TestCategory("RealData")]
    public void The_installed_generic_start_reads_with_its_background_table()
    {
        var root = Environment.GetEnvironmentVariable("ARC_AREA_CONVERSATION_GAME_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Inconclusive("ARC_AREA_CONVERSATION_GAME_ROOT is required.");
        var load = AreaStartingHero.Load(Path.Combine(root, "modules", "Arcanum.dat"), AreaStartingHero.NewCharacter);
        Assert.AreEqual(AreaHeroStatus.Ready, load.Status, load.Detail);
        Assert.IsNull(load.Flag);
        Assert.AreEqual((CharacterRace.Human, CharacterGender.Male, 0, 0, 0), (load.Definition!.Race, load.Definition.Gender, load.Definition.Background,
            load.Definition.Gold, load.Definition.Kit.Count));
        Assert.AreEqual(79, load.Backgrounds?.Count, load.BackgroundsDetail);
        var creation = new AreaNewCharacter(load);
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven); creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.GenderNext, Steamwoven);
        var seen = new HashSet<int>();
        for (var i = 0; i < 100 && seen.Add(creation.Background); i++)
        {
            Assert.IsNull(creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven), "block after " + creation.Background);
            Assert.IsNotNull(creation.BackgroundText(), "text of " + creation.Background);
        }
        Assert.AreEqual(38, seen.Count, "CC-06: 38 backgrounds for an elf woman, block 0 included");
        var hero = creation.Hero(out var detail)?.Definition ?? throw new AssertFailedException(detail ?? "hero");
        Assert.AreEqual((CharacterRace.Elf, CharacterGender.Female, 66, 330), (hero.Race, hero.Gender, hero.RaceEffect!.EffectId, hero.GenderEffect!.EffectId));
    }

    [TestMethod]
    public void The_menu_opens_the_window_from_new_game_only_and_accept_or_cancel_leave_it()
    {
        var menu = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null);
        menu.Type('s'); menu.Type('n');
        Assert.AreEqual(AreaMenuCommandKind.OpenNewCharacter, menu.Type('n').Kind, "New Character's letter");
        Assert.AreEqual(AreaMenuWindow.NewGame, menu.Window, "the host opens the window with the character it made");
        menu.OpenNewCharacter(new AreaNewCharacter(Generic()));
        Assert.AreEqual(AreaMenuWindow.NewCharacter, menu.Window);
        Assert.AreEqual(0, menu.Shown.Count, "art buttons only: no letters");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('x'));
        CollectionAssert.Contains(menu.HiddenNotes.ToArray(), AreaMainMenu.NoEditor);
        menu.PressCreation(AreaNewCharacterButton.RaceNext, Steamwoven);
        Assert.AreEqual(CharacterRace.Dwarf, menu.Creation!.Race);
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.StartNewCharacter), menu.Accept());
        // The controller: rows gender, race, background, Accept, Cancel.
        menu.Move(1);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Confirm());
        Assert.AreEqual(CharacterRace.Elf, menu.Creation.Race, "Confirm on the race row presses next");
        menu.Move(1); menu.Move(1);
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.StartNewCharacter), menu.Confirm(), "the Accept row");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Escape());
        Assert.AreEqual(AreaMenuWindow.NewGame, menu.Window, "Escape goes back");
        Assert.IsNull(menu.Creation, "and drops the character");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Accept());

        var refused = new AreaMainMenu(AreaMainMenuTexts.Project("test"), "not the start map");
        refused.Type('s'); refused.Type('n');
        refused.OpenNewCharacter(new AreaNewCharacter(Generic()));
        Assert.AreEqual(AreaMenuWindow.NewGame, refused.Window, "no new game here: the window does not open");
    }
}
