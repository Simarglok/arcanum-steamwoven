using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-013 options window (ledger OP1–OP8): the original's table, the menu's window 6, the values and the settings file;
/// ARC-UI-014 its project control, the rule mode.</summary>
[TestClass]
public sealed class AreaGameOptionsTests
{
    private static readonly AreaGameOptions Values = new(1, 0, true, false, 5, 5, 5);

    private static AreaMainMenu Options(AreaMenuWindow root = AreaMenuWindow.Main)
    {
        var menu = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, root);
        menu.Type('o');
        Assert.AreEqual(AreaMenuWindow.Options, menu.Window);
        menu.SetOptions(Values);
        return menu;
    }

    private static string[] Labels(AreaMainMenu menu) => [.. menu.Shown.Select(button => menu.Texts.Text(button.Message))];

    [TestMethod]
    public void The_table_is_the_originals_three_pages_with_their_settings_and_defaults()
    {
        Assert.AreEqual(16, AreaOptionTable.All.Count(c => !c.Project), "the original's 16 controls");
        CollectionAssert.AreEqual(new int?[] { 100, 101, 102, 103, 104, 105, 106, 107, null }, AreaOptionTable.Page(AreaOptionsPage.Game).Select(c => c.LabelMessage).ToArray(), "OP2, then the rule mode (the HD lines are the launcher screen's since ARC-UI-146)");
        CollectionAssert.AreEqual(new int?[] { 200, 201, 202, 203, 204, null, null, null }, AreaOptionTable.Page(AreaOptionsPage.Video).Select(c => c.LabelMessage).ToArray());
        CollectionAssert.AreEqual(new int?[] { 300, 301, 302 }, AreaOptionTable.Page(AreaOptionsPage.Sound).Select(c => c.LabelMessage).ToArray());
        CollectionAssert.AreEqual(new[] { AreaOption.Rules, AreaOption.ControlScheme, AreaOption.PromptStyle, AreaOption.EvidenceLog }, AreaOptionTable.All.Where(c => c.Project).Select(c => c.Option).ToArray(), "the project controls");
        Assert.AreEqual(0, AreaOptionTable.Of(AreaOption.EvidenceLog).Default, "ARC-GPAD-1376: the evidence log is off by default");
        Assert.AreEqual(1 + AreaOptionTable.TestTaskCount, AreaOptionTable.Of(AreaOption.EvidenceLog).Limit, "ARC-UI-1797: Off, On, Task 1 to 8 in one line");
        Assert.AreEqual(AreaOptionTable.TextDurationDefault, AreaOptionTable.Of(AreaOption.TextDuration).Default, "ARC-UI-1797: the original's text duration default");
        Assert.AreEqual(10, AreaOptionTable.Of(AreaOption.TextDuration).Max);
        Assert.AreEqual(AreaOptionTable.TextDurationDefault, Values.Value(AreaOption.TextDuration));
        Assert.AreEqual(5, new AreaGameOptions(1, 0, true, false, 5, 5, 5, TextDuration: 5).Value(AreaOption.TextDuration));
        Assert.AreEqual(1, new AreaGameOptions(1, 0, true, false, 5, 5, 5, EvidenceLog: true).Value(AreaOption.EvidenceLog), "the log on, no task");
        Assert.AreEqual(4, new AreaGameOptions(1, 0, true, false, 5, 5, 5, EvidenceLog: true, TestTask: 3).Value(AreaOption.EvidenceLog), "the log on in task 3");
        Assert.AreEqual(0, new AreaGameOptions(1, 0, true, false, 5, 5, 5, EvidenceLog: false, TestTask: 3).Value(AreaOption.EvidenceLog), "a task runs only while the log is on");
        Assert.IsFalse(Values.Value(AreaOption.EvidenceLog) == 1);
        for (var i = 0; i < AreaOptionTable.All.Count; i++) Assert.AreEqual((AreaOption)i, AreaOptionTable.All[i].Option, "the table is indexed by the option");
        var defaults = AreaOptionTable.All.ToDictionary(c => c.Setting, c => c.Default);
        Assert.AreEqual(1, defaults["difficulty"]);
        Assert.AreEqual(0, defaults["turn-based"]);
        Assert.AreEqual(1, defaults["auto attack"]);
        Assert.AreEqual(0, defaults["always run"]);
        Assert.AreEqual(6, defaults["text duration"]);
        Assert.AreEqual(2, defaults["float speed"]);
        Assert.AreEqual(1, defaults["text floaters"], "CH10: messages only");
        Assert.AreEqual(1, defaults["combat taunts"], "OH5: the original registers combat taunts on");
        Assert.AreEqual(5, defaults["music volume"]);
        Assert.AreEqual(9, AreaOptionTable.Of(AreaOption.Brightness).Max, "brightness's record sets its slider's top");
        Assert.AreEqual(10, AreaOptionTable.Of(AreaOption.Effects).Max, "a slider's default top");
        Assert.AreEqual((int)RuleMode.Steamwoven, defaults["rules"], "Steamwoven is the default mode");
        CollectionAssert.AreEqual(new[]
            {
                AreaOption.Difficulty, AreaOption.ViolenceFilter, AreaOption.DefaultCombatMode, AreaOption.AutoAttack, AreaOption.AutoSwitchWeapons, AreaOption.AlwaysRun, AreaOption.FollowerSkills, AreaOption.Brightness, AreaOption.TextDuration, AreaOption.Floats, AreaOption.FloatSpeed, AreaOption.CombatTaunts,
                AreaOption.Effects, AreaOption.Voice, AreaOption.Music, AreaOption.Rules, AreaOption.ControlScheme, AreaOption.PromptStyle, AreaOption.EvidenceLog,
            },
            AreaOptionTable.All.Where(c => c.Shown).Select(c => c.Option).ToArray(), "only the controls with a function are shown");
        Assert.IsTrue(AreaOptionTable.Offered(AreaOptionsPage.Video), "ARC-UI-021: the floating text's two controls");
        Assert.AreEqual(AreaOptionKind.List, AreaOptionTable.Of(AreaOption.Floats).Kind);
        Assert.AreEqual(2, AreaOptionTable.Of(AreaOption.Floats).Limit);
        Assert.AreEqual(10, AreaOptionTable.Of(AreaOption.FloatSpeed).Limit);
        Assert.AreEqual(AreaGameSettings.Defaults, new AreaGameSettings(1, RealTimeCombatRules.TurnBasedDefault, RealTimeCombatRules.AutoAttackDefault));
    }

    [TestMethod]
    public void A_list_wraps_around_and_a_slider_stops_at_its_ends()
    {
        var difficulty = AreaOptionTable.Of(AreaOption.Difficulty);
        Assert.AreEqual(2, difficulty.Step(1, 1));
        Assert.AreEqual(0, difficulty.Step(2, 1), "OP3: a list wraps to its first value");
        Assert.AreEqual(2, difficulty.Step(0, -1), "and back to its last");
        var mode = AreaOptionTable.Of(AreaOption.DefaultCombatMode);
        Assert.AreEqual(2, mode.Max);
        Assert.AreEqual(0, mode.Step(1, 1), "fast turn-based (2) is not offered: turn-based wraps to real time");
        var music = AreaOptionTable.Of(AreaOption.Music);
        Assert.AreEqual(10, music.Step(9, 1));
        Assert.IsNull(music.Step(10, 1), "a slider stops at its top");
        Assert.IsNull(music.Step(0, -1), "and at 0");
        Assert.IsNull(music.Step(5, 0));
        Assert.IsNull(music.Step(11, -1), "a value out of range changes nothing");
    }

    [TestMethod]
    public void Options_opens_from_every_menu_at_the_game_page()
    {
        var main = Options();
        CollectionAssert.AreEqual(new[] { 4000, 4001, 4002, 4003 }, main.Buttons.Select(b => b.Message).ToArray(), "OP1 window 6 buttons");
        CollectionAssert.AreEqual(new[] { "Game", "Video", "Sound", "Done" }, Labels(main));
        Assert.AreEqual(AreaOptionsPage.Game, main.Page);
        CollectionAssert.AreEqual(new[] { AreaOption.Difficulty, AreaOption.ViolenceFilter, AreaOption.DefaultCombatMode, AreaOption.AutoAttack, AreaOption.AutoSwitchWeapons, AreaOption.AlwaysRun, AreaOption.FollowerSkills, AreaOption.Rules },
            main.Controls.Select(c => c.Option).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            "Module: " + AreaOptionTable.NoModules,
            "Default Combat Mode: " + AreaOptionTable.NoFastTurnBased,
        }, main.HiddenNotes.ToArray());
        Assert.AreEqual(0, main.OptionsFocus);
        var play = Options(AreaMenuWindow.InPlay);
        CollectionAssert.AreEqual(new[] { AreaMenuWindow.InPlay }, play.BackStack.ToArray());
        var limited = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, AreaMenuWindow.InPlayLimited);
        limited.Type('O');
        Assert.AreEqual(AreaMenuWindow.Options, limited.Window, "window 4's Options");
    }

    [TestMethod]
    public void Pages_change_by_letter_and_controls_by_arrow()
    {
        var menu = Options();
        menu.Type('v');
        Assert.AreEqual(AreaOptionsPage.Video, menu.Page, "ARC-UI-021: the Video page");
        CollectionAssert.AreEqual(new[] { AreaOption.Brightness, AreaOption.TextDuration, AreaOption.Floats, AreaOption.FloatSpeed, AreaOption.CombatTaunts, AreaOption.ControlScheme, AreaOption.PromptStyle, AreaOption.EvidenceLog }, menu.Controls.Select(c => c.Option).ToArray());
        Assert.AreEqual(0, menu.HiddenNotes.Count, "ARC-UI-1797: Text Duration is shown, so no control of the Video page is hidden");
        Assert.AreEqual(8, menu.Controls.Count, "the Video page art holds eight lines");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.TextDuration, Value: 7), menu.Change(1, 1), "Text Duration's first step from the default 6");
        Assert.AreEqual("6/10", menu.OptionsTexts.Value(AreaOptionTable.Of(AreaOption.TextDuration), 6));
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Brightness, Value: 1), menu.Change(0, 1), "ARC-UI-1737: the slider's first step");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Change(0, -1), "a slider stops at 0");
        menu.SetOptions(Values with { Brightness = 9 });
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Change(0, 1), "and at its top, 9: the factor 2.0 is the original's refusal");
        Assert.AreEqual("0/9", menu.OptionsTexts.Value(AreaOptionTable.Of(AreaOption.Brightness), 0));
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Floats, Value: 2), menu.Change(2, 1), "Minimal to Verbose");
        menu.SetOptions(Values with { Floats = 2 });
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Floats, Value: 0), menu.Change(2, 1), "a list wraps to Never");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.FloatSpeed, Value: 3), menu.Change(3, 1));
        Assert.AreEqual("Verbose", menu.OptionsTexts.Value(AreaOptionTable.Of(AreaOption.Floats), 2));
        menu.Type('s');
        Assert.AreEqual(AreaOptionsPage.Sound, menu.Page);
        CollectionAssert.AreEqual(new[] { AreaOption.Effects, AreaOption.Voice, AreaOption.Music }, menu.Controls.Select(c => c.Option).ToArray());
        Assert.AreEqual(0, menu.HiddenNotes.Count, "the Sound page hides nothing");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Voice, Value: 6), menu.Change(1, 1));
        Assert.AreEqual(1, menu.OptionsFocus, "a control's arrow takes the focus");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Music, Value: 4), menu.Change(2, -1));
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Change(3, 1), "no fourth control");
        menu.SetOptions(Values with { Effects = 10 });
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Change(0, 1), "the slider's top");
        menu.Type('g');
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.AutoAttack, Value: 0), menu.Change(3, 1), "On wraps to Off");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.DefaultCombatMode, Value: 1), menu.Change(2, -1), "real time back to turn-based");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.ViolenceFilter, Value: 1), menu.Change(1, 1), "ARC-UI-1737: the Violence Filter is the Game page's second line, off to on");
        var unknown = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null);
        unknown.Type('o');
        Assert.AreEqual(AreaMenuCommand.Nothing, unknown.Change(0, 1), "no values yet, no change");
    }

    [TestMethod]
    public void Done_goes_back_from_the_main_menu_and_closes_the_menu_from_play_and_Escape_does_nothing()
    {
        var main = Options();
        Assert.AreEqual(AreaMenuCommand.Nothing, main.Escape(), "OP7: no Escape in the options window");
        Assert.AreEqual(AreaMenuWindow.Options, main.Window);
        Assert.AreEqual(AreaMenuCommand.Nothing, main.OptionsKey(), "the O key acts only from play");
        Assert.AreEqual(AreaMenuCommand.Nothing, main.Type('d'));
        Assert.AreEqual(AreaMenuWindow.Main, main.Window, "OP6: Done goes back to the main menu");
        var play = Options(AreaMenuWindow.InPlay);
        Assert.AreEqual(AreaMenuCommandKind.Continue, play.Type('D').Kind, "OP6: from play Done closes the menu");
        Assert.AreEqual(AreaMenuCommandKind.Continue, play.OptionsKey().Kind, "OP7: and so does the O key");
        Assert.AreEqual(AreaMenuCommand.Nothing, play.Escape());
    }

    [TestMethod]
    public void The_controller_moves_over_the_controls_then_the_buttons()
    {
        var menu = Options();
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Difficulty, Value: 2), menu.Confirm());
        menu.Move(-1);
        Assert.AreEqual(11, menu.OptionsFocus, "8 controls and 4 buttons, wrapping");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Confirm(), "Done");
        Assert.AreEqual(AreaMenuWindow.Main, menu.Window);
    }

    [TestMethod]
    public void The_hd_lines_are_not_in_the_options_window_but_their_settings_still_round_trip()
    {
        // ARC-UI-146: HD graphics, HD interface and HD pack are the launcher screen's lines (ARC-UI-143); the saved settings are the same.
        var menu = Options(AreaMenuWindow.InPlay);
        Assert.AreEqual(8, menu.Controls.Count, "the Game page ends with Rules");
        Assert.AreEqual(HdPackSourceMode.Auto, AreaGameSettings.Defaults.HdPackSource);
        Assert.IsNull(AreaGameSettings.Defaults.HdPackPath);
        Assert.IsFalse(AreaGameSettings.Defaults.HdInterface);
        var custom = AreaGameSettings.Defaults with { HdPackSource = HdPackSourceMode.Custom, HdPackPath = "/packs/my \"hd\" pack", HdInterface = true };
        Assert.AreEqual(custom, AreaGameSettingsCodec.Deserialize(AreaGameSettingsCodec.Serialize(custom)), "a path with quotes round-trips");
    }

    [TestMethod]
    public void The_rule_mode_line_switches_between_the_two_built_in_modes()
    {
        var menu = Options(AreaMenuWindow.InPlay);
        var rules = AreaOptionTable.Of(AreaOption.Rules);
        Assert.AreEqual(7, menu.Controls.ToList().IndexOf(rules), "the Game page's first project line");
        Assert.AreEqual((int)RuleMode.Steamwoven, Values.Value(AreaOption.Rules));
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Rules, Value: (int)RuleMode.Original), menu.Change(7, 1));
        menu.SetOptions(Values with { Rules = RuleMode.Original });
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Rules, Value: (int)RuleMode.Steamwoven), menu.Change(7, 1), "the list wraps");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Rules, Value: (int)RuleMode.Steamwoven), menu.Change(7, -1));
        Assert.AreEqual(Enum.GetValues<RuleMode>().Length - 1, rules.Max, "one value per built-in mode, no custom mix (after alpha)");
        // The project's texts only: a game file's message 108 is never its label, nor a message of any file its values.
        var game = AreaOptionsTexts.From((_, _) => "game text", "test");
        Assert.AreEqual("Rules", game.Label(rules));
        Assert.AreEqual("Steamwoven", game.Value(rules, 0));
        Assert.AreEqual("Original", game.Value(rules, 1));
        Assert.AreEqual("[2]", game.Value(rules, 2));
        Assert.AreEqual("game text", game.Label(AreaOptionTable.Of(AreaOption.Difficulty)), "an original control still reads the game");
    }

    [TestMethod]
    public void No_page_shows_more_controls_than_the_art_has_slots()
    {
        // AreaMenuArtLayout.OptionsSlot throws for a ninth: the Game page holds six lines now and eight with the two hidden originals (ARC-UI-1737: the Violence Filter needed the room, the Controls line and the evidence log's two lines moved to the Video page, which holds eight).
        var capacity = AreaMenuArtLayout.OptionsSlotColumns.Count * AreaMenuArtLayout.OptionsSlotRows.Count;
        foreach (var page in Enum.GetValues<AreaOptionsPage>())
            Assert.IsTrue(AreaOptionTable.Page(page).Count(control => control.Shown) <= capacity, page + " has more shown controls than slots");
    }

    [TestMethod]
    public void The_controls_line_is_one_video_page_list_of_the_original_and_the_three_schemes_beside_the_prompt_style()
    {
        var menu = Options(AreaMenuWindow.InPlay);
        menu.Type('v'); // ARC-UI-1737: the Controls line moved to the Video page, beside the prompt style
        var scheme = AreaOptionTable.Of(AreaOption.ControlScheme);
        var prompts = AreaOptionTable.Of(AreaOption.PromptStyle);
        Assert.AreEqual(5, menu.Controls.ToList().IndexOf(scheme), "the Video page's line before the prompt style");
        Assert.AreEqual(6, menu.Controls.ToList().IndexOf(AreaOptionTable.Of(AreaOption.PromptStyle)));
        foreach (var control in new[] { scheme, prompts })
        {
            Assert.IsTrue(control.Project && control.Shown && control.Kind == AreaOptionKind.List && control.LabelMessage is null, control.Option + " is a shown project list");
            Assert.AreEqual(0, control.Default, control.Option + " defaults to the first value (Original, Auto)");
        }
        Assert.AreEqual((Enum.GetValues<ControlChoice>().Length - 1, Enum.GetValues<PromptStyle>().Length - 1), (scheme.Max, prompts.Max), "one value per enum value");
        Assert.AreNotEqual(scheme.Setting, prompts.Setting);
        var texts = AreaOptionsTexts.Project("test");
        CollectionAssert.AreEqual(new[] { "Controls", "Prompt style" }, new[] { scheme, prompts }.Select(texts.Label).ToArray());
        CollectionAssert.AreEqual(new[] { "Original", "A Direct", "B Pointer", "C Touch+pad" }, Enumerable.Range(0, 4).Select(v => texts.Value(scheme, v)).ToArray());
        CollectionAssert.AreEqual(new[] { "Auto", "Xbox", "PlayStation", "Nintendo", "Steam Deck" }, Enumerable.Range(0, 5).Select(v => texts.Value(prompts, v)).ToArray());
        // A game file never names them: the same texts with a game that answers everything.
        var game = AreaOptionsTexts.From((_, _) => "game text", "test");
        Assert.AreEqual("Controls", game.Label(scheme));
        Assert.AreEqual("A Direct", game.Value(scheme, 1));
        // The value shown comes from the host's choice; a touch scheme wins over a pad scheme (an old file with both reads as C).
        Assert.AreEqual((0, 0), (Values.Value(AreaOption.ControlScheme), Values.Value(AreaOption.PromptStyle)));
        var chosenValue = Values with { Controls = new ControlSchemeSettings(Pad: PadScheme.Pointer, Prompts: PromptStyle.SteamDeck) };
        Assert.AreEqual((2, 4), (chosenValue.Value(AreaOption.ControlScheme), chosenValue.Value(AreaOption.PromptStyle)));
        Assert.AreEqual(3, (Values with { Controls = new ControlSchemeSettings(Pad: PadScheme.Direct, Touch: TouchScheme.TouchPad) }).Value(AreaOption.ControlScheme));
        // A value change is a command and the list wraps (ARC-UI-013 OP3).
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.ControlScheme, Value: 1), menu.Change(5, 1));
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.ControlScheme, Value: 3), menu.Change(5, -1), "the list wraps back to its last value");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.CombatTaunts, Value: 0), menu.Change(4, 1), "ARC-AI-1527: the combat taunts line is on by default; On wraps to Off");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.PromptStyle, Value: 1), menu.Change(6, 1));
    }

    [TestMethod]
    public void Follower_skills_is_the_game_pages_seventh_original_slot_on_by_default_and_its_value_follows_the_setting()
    {
        // ARC-COMBAT-1777 (OH4): the skill module's init registers 1 (On); the row was hidden until the chooser (0x4C9050) was built.
        var control = AreaOptionTable.Of(AreaOption.FollowerSkills);
        Assert.IsTrue(control.Shown && !control.Project && control.Kind == AreaOptionKind.List && control.Page == AreaOptionsPage.Game && control.Slot == 7);
        Assert.AreEqual(107, control.LabelMessage, "OP2: 100 + slot");
        Assert.AreEqual((1, 1), (control.Default, control.Limit));
        Assert.AreEqual("follower skills", control.Setting);
        Assert.AreEqual(1, Values.Value(AreaOption.FollowerSkills));
        Assert.AreEqual(0, (Values with { FollowerSkills = false }).Value(AreaOption.FollowerSkills));
        var menu = Options(AreaMenuWindow.InPlay);
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.FollowerSkills, Value: 0), menu.Change(6, -1), "Off is one step from On");
        Assert.AreEqual("Follower Skills", AreaOptionsTexts.Project("test").Label(control));
    }

    [TestMethod]
    public void Auto_switch_weapons_is_the_game_pages_sixth_original_slot_off_by_default_and_its_value_follows_the_setting()
    {
        // ARC-COMBAT-1738 (OH3): the original registers no default, so it reads 0 (Off) until the window writes it.
        var control = AreaOptionTable.Of(AreaOption.AutoSwitchWeapons);
        Assert.IsTrue(control.Shown && !control.Project && control.Kind == AreaOptionKind.List && control.Page == AreaOptionsPage.Game && control.Slot == 5);
        Assert.AreEqual(105, control.LabelMessage, "OP2: 100 + slot");
        Assert.AreEqual((0, 1), (control.Default, control.Limit));
        Assert.AreEqual("auto switch", control.Setting);
        Assert.AreEqual(0, Values.Value(AreaOption.AutoSwitchWeapons));
        Assert.AreEqual(1, (Values with { AutoSwitchWeapons = true }).Value(AreaOption.AutoSwitchWeapons));
        var menu = Options(AreaMenuWindow.InPlay);
        Assert.AreEqual(4, menu.Controls.ToList().IndexOf(control), "after Auto Attack, before Always Run");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.AutoSwitchWeapons, Value: 1), menu.Change(4, 1), "On is one step from Off");
        Assert.AreEqual("Auto Switch Weapons", AreaOptionsTexts.Project("test").Label(control));
        Assert.AreEqual("Off", AreaOptionsTexts.Project("test").Value(control, 0));
    }

    [TestMethod]
    public void The_evidence_log_line_is_off_by_default_and_uses_the_projects_texts()
    {
        var menu = Options(AreaMenuWindow.InPlay);
        menu.Type('v'); // ARC-UI-1737: the two test lines are the Video page's (the Game page needed the room for the Violence Filter)
        var log = AreaOptionTable.Of(AreaOption.EvidenceLog);
        Assert.AreEqual(7, menu.Controls.ToList().IndexOf(log), "ARC-UI-1797: the log and its task marker are the page's last line");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.EvidenceLog, Value: 1), menu.Change(7, 1), "On is one step from the default Off");
        var game = AreaOptionsTexts.From((_, _) => "game text", "test");
        Assert.AreEqual("Evidence Log", game.Label(log));
        Assert.AreEqual("Off", game.Value(log, 0));
        Assert.AreEqual("On", game.Value(log, 1));
        Assert.AreEqual("Task 1", game.Value(log, 2));
        Assert.AreEqual("Task 8", game.Value(log, 1 + AreaOptionTable.TestTaskCount));
        Assert.AreEqual("[10]", game.Value(log, 2 + AreaOptionTable.TestTaskCount));
    }

    [TestMethod]
    public void Texts_come_from_the_game_files_and_fall_back_to_the_projects()
    {
        var project = AreaOptionsTexts.Project("test");
        Assert.AreEqual("Default Combat Mode", project.Label(AreaOptionTable.Of(AreaOption.DefaultCombatMode)));
        Assert.AreEqual("Moderate", project.Value(AreaOptionTable.Of(AreaOption.Difficulty), 1));
        Assert.AreEqual("5/10", project.Value(AreaOptionTable.Of(AreaOption.Music), 5));
        var game = AreaOptionsTexts.From((file, number) => (file, number) switch
        {
            (AreaOptionsTexts.LabelsPath, 101) => "Сложность",
            (AreaOptionTable.Difficulties, 2) => "Трудно",
            _ => null,
        }, "test");
        Assert.AreEqual("Сложность", game.Label(AreaOptionTable.Of(AreaOption.Difficulty)));
        Assert.AreEqual("Трудно", game.Value(AreaOptionTable.Of(AreaOption.Difficulty), 2));
        Assert.AreEqual("Easy", game.Value(AreaOptionTable.Of(AreaOption.Difficulty), 0), "a missing text is the project's");
        Assert.AreEqual("[7]", game.Value(AreaOptionTable.Of(AreaOption.Difficulty), 7));
        Assert.AreEqual("Game", AreaMainMenuTexts.Project("test").Text(4000));
        Assert.AreEqual('D', AreaMainMenuTexts.Project("test").Hotkey(new AreaMenuButton(4003, AreaMenuRole.OptionsDone)));
    }

    [TestMethod]
    public void The_settings_file_round_trips_and_refuses_anything_else()
    {
        var settings = new AreaGameSettings(2, true, false) { Floats = 2, FloatSpeed = 7, HdInterface = true };
        Assert.AreEqual(settings, AreaGameSettingsCodec.Deserialize(AreaGameSettingsCodec.Serialize(settings)));
        Assert.AreEqual("""{"version":6,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":false,"hdPack":"auto","hdPackPath":null}""", AreaGameSettingsCodec.Serialize(AreaGameSettings.Defaults));
        Assert.AreEqual(AreaGameSettings.Defaults with { HdInterface = true }, AreaGameSettingsCodec.Deserialize("""{"version":4,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"auto","hdPackPath":null}"""),
            "an ARC-DEV-350 file reads as it is (version 4: no generated interface)");
        Assert.AreEqual(AreaGameSettings.Defaults, AreaGameSettingsCodec.Deserialize("""{"version":5,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":false,"hdPack":"auto","hdPackPath":null,"generatedInterface":true}"""),
            "a version 5 file reads with its retired generated-interface value ignored (ARC-UI-3142)");
        Assert.AreEqual(AreaGameSettings.Defaults with { HdInterface = true }, AreaGameSettingsCodec.Deserialize("""{"version":5,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"auto","hdPackPath":null,"generatedInterface":true}"""),
            "an ARC-UI-140 file with the generated interface on reads as HD on (ARC-UI-3142: HD On is the Steamwoven interface)");
        Assert.AreEqual(AreaGameSettings.Defaults, AreaGameSettingsCodec.Deserialize("""{"version":3,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":false}"""),
            "an ARC-UI-095 file reads with the HD pack source Auto (ARC-DEV-350)");
        Assert.AreEqual(AreaGameSettings.Defaults, AreaGameSettingsCodec.Deserialize("""{"version":2,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2}"""),
            "an ARC-UI-021 file reads with the HD interface off");
        Assert.AreEqual(new AreaGameSettings(2, true, false), AreaGameSettingsCodec.Deserialize("""{"version":1,"difficulty":2,"turnBased":true,"autoAttack":false}"""),
            "an ARC-UI-013 file reads with the floaters' defaults");
        foreach (var bad in new[]
        {
            "", "[]", "{}", """{"version":2,"difficulty":1,"turnBased":false,"autoAttack":true}""", """{"version":3,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2}""",
            """{"version":3,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":1}""", """{"version":4,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true}""",
            """{"version":4,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"cloud","hdPackPath":null}""",
            """{"version":4,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"custom","hdPackPath":3}""",
            """{"version":4,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"custom","hdPackPath":""}""",
            """{"version":4,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":1,"hdPackPath":null}""",
            """{"version":2,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true}""",
            """{"version":5,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"auto","hdPackPath":null}""",
            """{"version":5,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"auto","hdPackPath":null,"generatedInterface":1}""",
            """{"version":6,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"auto","hdPackPath":null,"generatedInterface":true}""",
            """{"version":7,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"auto","hdPackPath":null}""",
            """{"version":2,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":3,"floatSpeed":2}""", """{"version":2,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":11}""",
            """{"version":1,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2}""",
            """{"version":1,"difficulty":3,"turnBased":false,"autoAttack":true}""", """{"version":1,"difficulty":1,"turnBased":0,"autoAttack":true}""",
            """{"version":1,"difficulty":1,"turnBased":false}""", """{"version":1,"difficulty":1,"turnBased":false,"autoAttack":true,"x":1}""",
        })
            Assert.IsNull(AreaGameSettingsCodec.Deserialize(bad), bad);
        Assert.AreEqual(GameDifficulty.Easy, new AreaGameSettings(0, false, true).SessionDifficulty);
        Assert.AreEqual(GameDifficulty.Normal, AreaGameSettings.Defaults.SessionDifficulty);
        Assert.AreEqual(GameDifficulty.Hard, settings.SessionDifficulty);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AreaGameSettings(3, false, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => settings with { Difficulty = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => settings with { Floats = 3 });
        Assert.Throws<ArgumentOutOfRangeException>(() => settings with { FloatSpeed = -1 });
        Assert.AreEqual(2, AreaGameSettings.Defaults.FloatSpeed);
    }
}
