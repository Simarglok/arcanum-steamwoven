using System.Text;
using Arcanum.Data.Characters;
using Arcanum.Data.Messages;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-030 (N10b): the new-character window (ledger NW1–NW5, BD4, BD10) over a synthetic generic hero and background table;
/// ARC-UI-033: its portrait buttons (PO9) over a synthetic portrait file and its name field (NW6–NW8); ARC-UI-034: the character editor
/// in creation mode (window 14, CM1–CM8) on a draft session and the replay of its presses. ARC-UI-036: the opening shop's tests
/// (<see cref="AreaOpeningShopTests"/>) share this fixture.</summary>
[TestClass]
public sealed class AreaNewCharacterTests
{
    // Block 0 for everyone (effect 86), block 1 for a dwarf man (effect 87, no rule), block 2 for a human or elf woman (effect 86, money 250,
    // a weapon and a prototype the kit fixture lacks).
    private const string Blocks = "{0}{1000}{1}{86}{2}{}{3}{400}{4}{}\n{10}{1001}{11}{87}{12}{DWM}{13}{0}{14}{}\n{20}{1002}{21}{86}{22}{HUF,ELF}{23}{250}{24}{6049 7000}";
    private const string Texts = "{1000}{Default\nplain}{1001}{Stout\nstub}{1002}{Lady\nfine}";
    // Human men 1000, 1003, 1005 (the generic start), elf women 1001, 1004, an NPC-only 1002, and 1006 for anyone.
    private const string Portraits = "{1000}{HUM1}{1001}{ELF1}{1002}{NPC1}{1003}{HUM2}{1004}{ELF2}{1005}{HUM3}{1006}{ANY1}";

    internal static MesFile Mes(string text)
    {
        var parsed = MesFile.Parse(Encoding.Latin1.GetBytes(text));
        Assert.AreEqual(MesParseStatus.Parsed, parsed.Status, $"line {parsed.Line}");
        return parsed.File!;
    }

    // Race effect 64 + race raises constitution by the race number, the female effect beauty by 1, background effect 86 strength by 1.
    internal static EffectRule? Effect(int number) => number switch
    {
        >= 64 and <= 71 => new(number, true, [new(EffectAttributeKind.Stat, 2, EffectChangeOperator.Add, number - 64)]),
        330 => new(number, true, [new(EffectAttributeKind.Stat, 3, EffectChangeOperator.Add, 1)]),
        86 => new(number, true, [new(EffectAttributeKind.Stat, 0, EffectChangeOperator.Add, 1)]),
        _ => null,
    };

    internal static AreaHeroLoad Generic(bool backgrounds = true, bool portraits = true, Func<int, PrototypeFile>? kit = null)
    {
        var read = new PremadeHero(AreaStartingHero.GenericPrototype, AreaStartingHero.GenericPrototype + PremadeHeroReader.PrototypeOffset, 0, 1, 0, 20, 1005, 0, 0,
            Effect(64)!, null, 0, 0, []);
        return new AreaHeroLoad(AreaHeroStatus.Ready, AreaStartingHero.NewCharacter, AreaStartingHero.Definition(read, out _), null, 0,
            new PremadeHeroRead(PremadeHeroFailure.None, read), "synthetic", Effects: Effect)
        {
            Backgrounds = backgrounds ? BackgroundTable.Create(Mes(Blocks), Mes(Texts)) : null,
            BackgroundsDetail = backgrounds ? null : "FileMissing rules/backgrnd.mes",
            Portraits = portraits ? PortraitCatalogue.Create(Mes(Portraits)) : null,
            PortraitsDetail = portraits ? null : "FileMissing portrait/gameport.mes",
            KitPrototypes = kit ?? AreaOpeningShopTests.KitFiles(),
        };
    }

    internal static readonly CharacterCreationRuleProfile Steamwoven = CharacterCreationRuleProfile.Steamwoven;

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
        // Only the new character may start without a flag: a pre-made hero without one is still refused.
        Assert.ThrowsExactly<ArgumentException>(() => AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, hero with { Hero = AreaStartingHero.Premade[0] }));

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

    [TestMethod]
    public void The_portrait_buttons_step_through_the_pair_s_portraits_and_wrap_as_the_original()
    {
        var creation = new AreaNewCharacter(Generic());
        Assert.AreEqual(1005, creation.Portrait, "the generic prototype's portrait (NW1)");
        Assert.AreEqual("HUM3", creation.PortraitText());
        int Press(AreaNewCharacterButton button) { Assert.IsNull(creation.Press(button, Steamwoven)); return creation.Portrait; }
        Assert.AreEqual(1006, Press(AreaNewCharacterButton.PortraitNext), "ANY suits everyone");
        Assert.AreEqual(1000, Press(AreaNewCharacterButton.PortraitNext), "past the last: the first (PO9)");
        Assert.AreEqual(1006, Press(AreaNewCharacterButton.PortraitPrevious), "before 1000: the last from it");
        Assert.AreEqual(1005, Press(AreaNewCharacterButton.PortraitPrevious));
        Assert.AreEqual(1003, Press(AreaNewCharacterButton.PortraitPrevious), "an elf woman's and the NPC's are skipped");
    }

    [TestMethod]
    public void A_race_or_gender_change_takes_the_pair_s_first_portrait_and_a_refused_press_keeps_it()
    {
        var creation = new AreaNewCharacter(Generic());
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        Assert.AreEqual((CharacterRace.Dwarf, 1006), (creation.Race, creation.Portrait), "a dwarf man's first: ANY");
        creation.Press(AreaNewCharacterButton.PortraitPrevious, Steamwoven);
        Assert.AreEqual(1006, creation.Portrait, "no earlier one for him: previous keeps it");
        creation.Press(AreaNewCharacterButton.GenderNext, CharacterCreationRuleProfile.Original);
        Assert.AreEqual((CharacterGender.Male, 1006), (creation.Gender, creation.Portrait), "a refused gender keeps the portrait in both settings");
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.GenderNext, Steamwoven);
        Assert.AreEqual((CharacterRace.Elf, CharacterGender.Female, 1001), (creation.Race, creation.Gender, creation.Portrait));
        var hero = creation.Hero(out var detail) ?? throw new AssertFailedException(detail ?? "hero");
        Assert.AreEqual(1001, hero.Definition!.Portrait, "the hero starts with the chosen portrait");
        using var owner = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, hero).Owner!;
        Assert.AreEqual(1001, owner.QueryHero()!.Portrait);

        var bare = new AreaNewCharacter(Generic(portraits: false));
        StringAssert.StartsWith(bare.Press(AreaNewCharacterButton.PortraitNext, Steamwoven), "the portraits did not read");
        bare.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        Assert.AreEqual((1005, (string?)null), (bare.Portrait, bare.PortraitText()), "without the file a race change keeps the portrait");
    }

    [TestMethod]
    public void The_name_field_types_at_its_end_and_ends_as_the_original()
    {
        var creation = new AreaNewCharacter(Generic(), "Your Name"u8.ToArray());
        Assert.AreEqual(("Your Name", false), (creation.NameText(), creation.TypingName), "the field starts with message 500");
        Assert.IsNotNull(creation.TypeName('a'), "no typing before a click on the field");
        creation.BeginName();
        Assert.AreEqual(("", true), (creation.NameText(), creation.TypingName), "a click clears the field");
        foreach (var c in "Ren\u00e9") Assert.IsNull(creation.TypeName(c));
        Assert.AreEqual("Ren\u00e9", creation.NameText());
        Assert.IsNotNull(creation.TypeName('\u041a'), "a letter the game's encoding lacks");
        Assert.IsNotNull(creation.TypeName('\t'), "a control character");
        creation.BackspaceName();
        Assert.AreEqual("Ren", creation.NameText());
        creation.EndName(enter: true);
        Assert.AreEqual(("Ren", false), (creation.NameText(), creation.TypingName));

        creation.BeginName();
        for (var i = 0; i < 25; i++) creation.TypeName('z');
        Assert.AreEqual(CharacterNameRules.TypedLength, creation.Name.Length, "typing stops at 22");
        Assert.AreEqual("the name field is full", creation.TypeName('z'));
        foreach (var _ in Enumerable.Range(0, 30)) creation.BackspaceName();
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        Assert.AreEqual(("Ren", false), (creation.NameText(), creation.TypingName), "a click elsewhere gives an empty field its old text back");
        creation.BeginName();
        creation.EndName(enter: true);
        Assert.AreEqual("Your Name", creation.NameText(), "Enter gives an empty field the default name");
        Assert.IsFalse(creation.AcceptName().Accepted, "which Accept refuses (506)");
    }

    [TestMethod]
    public void Apply_stores_the_accepted_name_as_the_player_name()
    {
        var creation = new AreaNewCharacter(Generic(), "Your Name"u8.ToArray());
        creation.BeginName();
        foreach (var c in "Ren\u00e9") creation.TypeName(c);
        creation.EndName(enter: true);
        Assert.IsTrue(creation.AcceptName().Accepted);
        using var owner = AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(), Guid.NewGuid(),
            AreaGridSession.Cell(AreaGridSession.Spawn), 1, creation.Hero(out _)).Owner!;
        Assert.IsNull(creation.Apply(owner, Effect));
        CollectionAssert.AreEqual(new byte[] { 0x52, 0x65, 0x6E, 0xE9 }, owner.QueryHero()!.PlayerName.ToArray());
        Assert.AreEqual("Ren\u00e9", AreaStartingHero.Name(owner.QueryHero()!), "the status lines show the player name");
        Assert.AreEqual(1, owner.Version.Revision, "one revision: no background was chosen");
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
        CollectionAssert.DoesNotContain(menu.HiddenNotes.ToArray(), AreaMainMenu.ShopLoading, "ARC-UI-034: Accept opens the editor");
        menu.PressCreation(AreaNewCharacterButton.RaceNext, Steamwoven);
        Assert.AreEqual(CharacterRace.Dwarf, menu.Creation!.Race);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Accept(), "ARC-UI-033: the default name is refused");
        Assert.AreEqual("Your character needs a name.", menu.Notice, "message 506 (the project's text here)");
        menu.BeginName();
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('n'), "a typed letter goes into the name field, not to a button");
        Assert.IsTrue(menu.NameKey(backspace: false), "Enter ends the typing");
        Assert.IsFalse(menu.NameKey(backspace: true), "no longer typing: the key is the window's");
        Assert.AreEqual("n", menu.Creation.NameText());
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.OpenCharacterEditor), menu.Accept(), "ARC-UI-034: the host opens the editor");
        Assert.IsNull(menu.Notice);
        // The controller: rows gender, race, background, portrait, name, Accept, Cancel.
        menu.Move(1);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Confirm());
        Assert.AreEqual(CharacterRace.Elf, menu.Creation.Race, "Confirm on the race row presses next");
        menu.Move(1); menu.Move(1);
        menu.PressCreation(AreaNewCharacterButton.GenderNext, Steamwoven);
        Assert.AreEqual(1001, menu.Creation.Portrait, "an elf woman's first portrait");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Confirm());
        Assert.AreEqual(1004, menu.Creation.Portrait, "Confirm on the portrait row presses next");
        menu.Move(1);
        menu.Confirm();
        Assert.IsTrue(menu.Creation.TypingName, "Confirm on the name row begins the typing");
        menu.Type('x');
        menu.Confirm();
        Assert.AreEqual((false, "x"), (menu.Creation.TypingName, menu.Creation.NameText()), "and a second Confirm ends it as Enter");
        menu.Move(1);
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.OpenCharacterEditor), menu.Confirm(), "the Accept row");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Escape());
        Assert.AreEqual(AreaMenuWindow.NewGame, menu.Window, "Escape goes back");
        Assert.IsNull(menu.Creation, "and drops the character");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Accept());

        var refused = new AreaMainMenu(AreaMainMenuTexts.Project("test"), "not the start map");
        refused.Type('s'); refused.Type('n');
        refused.OpenNewCharacter(new AreaNewCharacter(Generic()));
        Assert.AreEqual(AreaMenuWindow.NewGame, refused.Window, "no new game here: the window does not open");
    }

    internal static (AreaNewCharacter Creation, AreaHeroLoad Hero) ElfWoman(Func<int, PrototypeFile>? kit = null)
    {
        var creation = new AreaNewCharacter(Generic(kit: kit));
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.RaceNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.GenderNext, Steamwoven);
        creation.Press(AreaNewCharacterButton.BackgroundNext, Steamwoven);
        return (creation, creation.Hero(out var detail) ?? throw new AssertFailedException(detail ?? "hero"));
    }

    internal static GameSessionOwner Session(AreaHeroLoad hero) => AreaTransitionCoordinator.CreateSession(ConversationSessionFixture.Clock, Guid.NewGuid(),
        Guid.NewGuid(), AreaGridSession.Cell(AreaGridSession.Spawn), 1, hero).Owner ?? throw new AssertFailedException("session");

    [TestMethod]
    public void The_editor_spends_on_a_draft_down_to_its_opening_and_the_start_replays_the_presses()
    {
        var (creation, hero) = ElfWoman();
        var draft = Session(hero);
        Assert.IsNull(creation.ApplyChoices(draft, Effect));
        using var editor = new AreaCreationEditor(creation, draft);
        Assert.IsTrue(draft.HeroEditorOpen, "creation mode opens the editor: its snapshot is the floor (CM3)");
        var start = editor.Stats()!;
        Assert.AreEqual((8, 9, 5), (start.Rows[2].Value, start.Rows[2].Effective, start.UnspentPoints), "strength 8, effective 9 with the background");
        Assert.IsTrue(editor.PressStat(2, spend: true).Changed);
        Assert.IsTrue(editor.PressStat(3, spend: true).Changed);
        Assert.IsTrue(editor.PressStat(3, spend: false).Changed);
        Assert.AreEqual("refused: nothing bought since the sheet opened to refund", editor.PressStat(3, spend: false).Message, "down to the opening only");
        Assert.IsTrue(editor.PressStat(0, spend: true).Changed);
        Assert.IsTrue(editor.PressSkill(0, spend: true).Changed);
        Assert.AreEqual(5, creation.Presses.Count, "a refused press is not recorded");
        CollectionAssert.AreEqual(new[]
        {
            new AreaCreationPress(AdvancementTrack.Stat, (int)CharacterStat.Strength, true), new(AdvancementTrack.Stat, (int)CharacterStat.Constitution, true),
            new(AdvancementTrack.Stat, (int)CharacterStat.Constitution, false), new(AdvancementTrack.HitPoints, 0, true), new(AdvancementTrack.BasicSkill, 0, true),
        }, creation.Presses.ToArray());
        Assert.AreEqual(2, editor.Stats()!.UnspentPoints);
        Assert.AreEqual((1, 2), (editor.Skills(null, MesTextProfile.Windows1252)!.Rows[0].Points, editor.Sheet()!.UnspentPoints));

        using var started = Session(hero);
        Assert.IsNull(creation.Apply(started, Effect));
        var made = started.QueryHero()!;
        var drafted = draft.QueryHero()!;
        CollectionAssert.AreEqual(drafted.Stats.ToArray(), made.Stats.ToArray());
        Assert.AreEqual(drafted.Advancement, made.Advancement);
        Assert.AreEqual((2, 9, 1), (made.UnspentPoints, made.Stats[(int)CharacterStat.Strength], made.Advancement.HitPointPoints));
        Assert.IsFalse(started.HeroEditorOpen, "the game starts with the editor closed: nothing refundable any more");
        Assert.AreEqual((2, 1002), (started.QueryHeroBackground(), started.QueryHeroBackgroundDescription()));
        editor.Dispose();
        Assert.IsTrue(editor.Disposed);
        Assert.ThrowsExactly<ObjectDisposedException>(() => draft.QueryHero());
        editor.Dispose();
    }

    private static bool Did(SessionPointResult result) => result.Disposition == GameSessionDisposition.Changed;

    [TestMethod]
    public void The_editor_buys_tech_degrees_down_to_its_opening_and_the_start_replays_them()
    {
        // ARC-MAGIC-043 (CM8, TC10): the degree page of the creation editor spends and gives back through the technology lane, refuses below
        // the snapshot the editor opened on, records only the presses that changed the character, and the start replays them.
        var (creation, hero) = ElfWoman();
        var draft = Session(hero);
        Assert.IsNull(creation.ApplyChoices(draft, Effect));
        using var editor = new AreaCreationEditor(creation, draft);
        var floor = AreaTechPage.Build(draft)!;
        Assert.IsTrue(floor.EditorOpen);
        CollectionAssert.AreEqual(new int[8], floor.Rows.Select(row => row.FloorDegree).ToArray(), "a new character opens with no degrees");
        var minus = editor.PressTech(0, spend: false);
        Assert.AreEqual((GameSessionDisposition.Rejected, PointChangeStatus.AtFloor), (minus.Disposition, minus.Change!.Status), "nothing bought since the editor opened (TC10)");
        Assert.AreEqual("refused: nothing bought since the sheet opened to refund", AreaTechPage.Press(minus, false).Message);
        Assert.AreEqual(0, creation.Presses.Count, "a refused press is not recorded");

        var first = editor.PressTech(4, spend: true);
        Assert.IsTrue(Did(first), AreaTechPage.Press(first, true).Message);
        Assert.AreEqual(1, AreaTechPage.Build(draft)!.Rows[4].Degree);
        Assert.AreEqual(4, editor.Stats()!.UnspentPoints);
        Assert.IsTrue(Did(editor.PressTech(4, spend: false)), "a degree bought in this editor can be given back");
        Assert.IsFalse(Did(editor.PressTech(4, spend: false)), "and no further than the opening");
        Assert.IsTrue(Did(editor.PressTech(0, spend: true)));
        Assert.IsTrue(Did(editor.PressTech(4, spend: true)));
        CollectionAssert.AreEqual(new[]
        {
            new AreaCreationPress(AdvancementTrack.TechDiscipline, 4, true), new(AdvancementTrack.TechDiscipline, 4, false),
            new(AdvancementTrack.TechDiscipline, 0, true), new(AdvancementTrack.TechDiscipline, 4, true),
        }, creation.Presses.ToArray());

        using var started = Session(hero);
        Assert.IsNull(creation.Apply(started, Effect));
        var made = started.QueryHeroTechnology()!.Value;
        Assert.AreEqual((1, 1, 0), (made[0].Degree, made[4].Degree, made[1].Degree));
        Assert.AreEqual(draft.QueryHero()!.UnspentPoints, started.QueryHero()!.UnspentPoints);
        Assert.IsFalse(started.HeroEditorOpen, "the game starts with the editor closed: no degree can be given back any more");
        Assert.IsFalse(Did(started.RefundHeroPoint(started.Version, AdvancementTrack.TechDiscipline, 0, AreaSkillsPage.RefundClick)));
    }

    [TestMethod]
    public void The_editor_refuses_a_degree_the_intelligence_does_not_allow_and_words_the_tech_page_in_the_window()
    {
        var (creation, hero) = ElfWoman();
        var draft = Session(hero);
        Assert.IsNull(creation.ApplyChoices(draft, Effect));
        using var editor = new AreaCreationEditor(creation, draft);
        Assert.IsTrue(Did(editor.PressTech(2, spend: true)));
        // Degree 2 needs 8 intelligence, degree 3 needs 11: refused by the lane while the intelligence stays below 11, nothing recorded.
        Assert.IsTrue(draft.QueryHero()!.Stats[(int)CharacterStat.Intelligence] < 11, "the new character's intelligence is below 11");
        Assert.IsTrue(Did(editor.PressTech(2, spend: true)));
        var third = editor.PressTech(2, spend: true);
        Assert.AreEqual("refused: intelligence too low for the next degree", AreaTechPage.Press(third, true).Message);
        Assert.AreEqual(2, creation.Presses.Count);
        StringAssert.StartsWith(AreaTechPage.Line(AreaTechPage.Build(draft)!.Rows[2]), "Electric: degree 2, expertise ");
    }

    [TestMethod]
    public void A_press_the_started_session_refuses_fails_the_start_and_closes_its_editor()
    {
        var (creation, hero) = ElfWoman();
        var draft = Session(hero);
        using (var editor = new AreaCreationEditor(creation, draft))
            for (var i = 0; i < 5; i++) Assert.IsTrue(editor.PressStat(1, spend: true).Changed);
        // A session whose hero already spent its points: the first replayed press finds none.
        using var other = Session(hero);
        other.OpenHeroEditor();
        Assert.IsTrue(other.SpendHeroPoint(other.Version, AdvancementTrack.Fatigue, 0).Disposition == GameSessionDisposition.Changed);
        for (var i = 0; i < 4; i++) other.SpendHeroPoint(other.Version, AdvancementTrack.Fatigue, 0);
        other.CloseHeroEditor();
        var refused = creation.Apply(other, Effect);
        StringAssert.StartsWith(refused, "the editor's press 1 (Fatigue 0 +) was refused (PointRefused NotEnoughPoints)");
        Assert.IsFalse(other.HeroEditorOpen);
    }

    [TestMethod]
    public void The_editor_takes_a_fresh_character_and_a_draft_with_its_editor_closed()
    {
        var (creation, hero) = ElfWoman();
        using var open = Session(hero);
        open.OpenHeroEditor();
        Assert.ThrowsExactly<ArgumentException>(() => new AreaCreationEditor(creation, open), "an open editor has another floor");
        Assert.ThrowsExactly<ObjectDisposedException>(() => open.QueryHero(), "the refused draft is disposed");
        var fresh = Session(hero);
        using (var editor = new AreaCreationEditor(creation, Session(hero))) editor.PressStat(2, spend: true);
        Assert.ThrowsExactly<ArgumentException>(() => new AreaCreationEditor(creation, fresh), "presses already recorded");
        Assert.ThrowsExactly<ObjectDisposedException>(() => fresh.QueryHero());
    }

    [TestMethod]
    public void The_menu_s_editor_window_accepts_cancels_to_a_new_character_and_owns_the_draft()
    {
        var menu = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null);
        menu.Type('s'); menu.Type('n');
        menu.OpenNewCharacter(new AreaNewCharacter(Generic()));
        var creation = menu.Creation!;
        menu.PressCreation(AreaNewCharacterButton.RaceNext, Steamwoven);
        menu.BeginName(); menu.Type('n'); menu.NameKey(backspace: false);
        Assert.AreEqual(AreaMenuCommandKind.OpenCharacterEditor, menu.Accept().Kind);
        var draft = Session(creation.Hero(out _)!);
        var editor = new AreaCreationEditor(creation, draft);
        menu.OpenCharacterEditor(editor);
        Assert.AreEqual((AreaMenuWindow.CharacterEditor, 0, 0), (menu.Window, menu.Shown.Count, menu.Focus));
        Assert.AreSame(editor, menu.Editor);
        CollectionAssert.DoesNotContain(menu.HiddenNotes.ToArray(), AreaMainMenu.ShopLoading);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('a'), "no letters in the editor (CM7)");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.OpenShop), menu.Accept(), "ARC-UI-036: Accept asks for the opening shop");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.OpenShop), menu.Confirm(), "the controller's Accept row");
        menu.Move(1);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Confirm(), "the Cancel row goes back");
        Assert.AreEqual(AreaMenuWindow.NewCharacter, menu.Window);
        Assert.IsTrue(editor.Disposed, "leaving the editor disposes its draft");
        Assert.IsNull(menu.Editor);
        Assert.AreNotSame(creation, menu.Creation, "window 12 opens again with a new character (CM6, NW1)");
        Assert.AreEqual((CharacterRace.Human, 0, 0), (menu.Creation!.Race, menu.Creation.Presses.Count, menu.Focus));
        CollectionAssert.AreEqual(creation.DefaultName.ToArray(), menu.Creation.Name.ToArray(), "the name starts as the default (NW7 is a later slice)");

        // Escape goes back the same way; an editor offered outside window 12, or for another character, is disposed at once.
        menu.BeginName(); menu.Type('m'); menu.NameKey(backspace: false);
        menu.Accept();
        var second = new AreaCreationEditor(menu.Creation, Session(menu.Creation.Hero(out _)!));
        menu.OpenCharacterEditor(second);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Escape());
        Assert.AreEqual((AreaMenuWindow.NewCharacter, true), (menu.Window, second.Disposed));
        var stranger = new AreaCreationEditor(new AreaNewCharacter(Generic()), Session(Generic()));
        menu.OpenCharacterEditor(stranger);
        Assert.AreEqual((AreaMenuWindow.NewCharacter, true), (menu.Window, stranger.Disposed));
        menu.Accept();
        var third = new AreaCreationEditor(menu.Creation, Session(menu.Creation.Hero(out _)!));
        menu.OpenCharacterEditor(third);
        menu.DropEditor();
        Assert.AreEqual((true, (AreaCreationEditor?)null), (third.Disposed, menu.Editor));
    }
}
