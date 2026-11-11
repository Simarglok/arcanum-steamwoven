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
        CollectionAssert.AreEqual(new int?[] { 100, 101, 102, 103, 104, 105, 106, 107, null, null, null, null }, AreaOptionTable.Page(AreaOptionsPage.Game).Select(c => c.LabelMessage).ToArray(), "OP2, then the rule mode, HD graphics, HD interface and HD pack");
        CollectionAssert.AreEqual(new int?[] { 200, 201, 202, 203, 204 }, AreaOptionTable.Page(AreaOptionsPage.Video).Select(c => c.LabelMessage).ToArray());
        CollectionAssert.AreEqual(new int?[] { 300, 301, 302 }, AreaOptionTable.Page(AreaOptionsPage.Sound).Select(c => c.LabelMessage).ToArray());
        CollectionAssert.AreEqual(new[] { AreaOption.Rules, AreaOption.HdGraphics, AreaOption.HdInterface, AreaOption.HdPack }, AreaOptionTable.All.Where(c => c.Project).Select(c => c.Option).ToArray(), "four project controls");
        for (var i = 0; i < AreaOptionTable.All.Count; i++) Assert.AreEqual((AreaOption)i, AreaOptionTable.All[i].Option, "the table is indexed by the option");
        var defaults = AreaOptionTable.All.ToDictionary(c => c.Setting, c => c.Default);
        Assert.AreEqual(1, defaults["difficulty"]);
        Assert.AreEqual(0, defaults["turn-based"]);
        Assert.AreEqual(1, defaults["auto attack"]);
        Assert.AreEqual(0, defaults["always run"]);
        Assert.AreEqual(6, defaults["text duration"]);
        Assert.AreEqual(2, defaults["float speed"]);
        Assert.AreEqual(1, defaults["text floaters"], "CH10: messages only");
        Assert.AreEqual(5, defaults["music volume"]);
        Assert.AreEqual(9, AreaOptionTable.Of(AreaOption.Brightness).Max, "brightness's record sets its slider's top");
        Assert.AreEqual(10, AreaOptionTable.Of(AreaOption.Effects).Max, "a slider's default top");
        Assert.AreEqual((int)RuleMode.Steamwoven, defaults["rules"], "Steamwoven is the default mode");
        CollectionAssert.AreEqual(new[]
            {
                AreaOption.Difficulty, AreaOption.DefaultCombatMode, AreaOption.AutoAttack, AreaOption.AlwaysRun, AreaOption.Floats, AreaOption.FloatSpeed,
                AreaOption.Effects, AreaOption.Voice, AreaOption.Music, AreaOption.Rules, AreaOption.HdGraphics, AreaOption.HdInterface, AreaOption.HdPack,
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
        CollectionAssert.AreEqual(new[] { AreaOption.Difficulty, AreaOption.DefaultCombatMode, AreaOption.AutoAttack, AreaOption.AlwaysRun, AreaOption.Rules, AreaOption.HdGraphics, AreaOption.HdInterface, AreaOption.HdPack },
            main.Controls.Select(c => c.Option).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            "Module: " + AreaOptionTable.NoModules, "Violence Filter: " + AreaOptionTable.NoViolenceFilter,
            "Default Combat Mode: " + AreaOptionTable.NoFastTurnBased, "Auto Switch Weapons: " + AreaOptionTable.NoAutoSwitch, "Follower Skills: " + AreaOptionTable.NoFollowerSkills,
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
        CollectionAssert.AreEqual(new[] { AreaOption.Floats, AreaOption.FloatSpeed }, menu.Controls.Select(c => c.Option).ToArray());
        CollectionAssert.AreEqual(new[]
        {
            "Brightness: " + AreaOptionTable.NoBrightness, "Text Duration: " + AreaOptionTable.NoTextDuration, "Combat Taunts: " + AreaOptionTable.NoTaunts,
        }, menu.HiddenNotes.ToArray());
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Floats, Value: 2), menu.Change(0, 1), "Minimal to Verbose");
        menu.SetOptions(Values with { Floats = 2 });
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Floats, Value: 0), menu.Change(0, 1), "a list wraps to Never");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.FloatSpeed, Value: 3), menu.Change(1, 1));
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
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.AutoAttack, Value: 0), menu.Change(2, 1), "On wraps to Off");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.DefaultCombatMode, Value: 1), menu.Change(1, -1), "real time back to turn-based");
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
    public void The_hd_graphics_line_is_a_project_on_off_list_after_the_rule_mode()
    {
        var menu = Options(AreaMenuWindow.InPlay);
        var hd = AreaOptionTable.Of(AreaOption.HdGraphics);
        Assert.AreEqual(5, menu.Controls.ToList().IndexOf(hd), "the Game page's last line, after Rules");
        Assert.IsTrue(hd.Project && hd.Shown);
        Assert.AreEqual(0, Values.Value(AreaOption.HdGraphics), "off by default");
        Assert.AreEqual(1, (Values with { HdGraphics = true }).Value(AreaOption.HdGraphics));
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.HdGraphics, Value: 1), menu.Change(5, 1));
        menu.SetOptions(Values with { HdGraphics = true });
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.HdGraphics, Value: 0), menu.Change(5, 1), "the list wraps");
        var game = AreaOptionsTexts.From((_, _) => "game text", "test");
        Assert.AreEqual("HD graphics", game.Label(hd), "the project's text, never a game message");
        Assert.AreEqual("Off", game.Value(hd, 0)); Assert.AreEqual("On", game.Value(hd, 1));
    }

    [TestMethod]
    public void The_hd_interface_line_is_its_own_project_on_off_list_after_hd_graphics()
    {
        var menu = Options(AreaMenuWindow.InPlay);
        var line = AreaOptionTable.Of(AreaOption.HdInterface);
        Assert.AreEqual(6, menu.Controls.ToList().IndexOf(line), "after HD graphics");
        Assert.IsTrue(line.Project && line.Shown);
        Assert.AreEqual(0, Values.Value(AreaOption.HdInterface), "off by default");
        Assert.AreEqual(0, (Values with { HdGraphics = true }).Value(AreaOption.HdInterface), "independent of HD graphics");
        Assert.AreEqual(0, (Values with { HdInterface = true }).Value(AreaOption.HdGraphics), "and HD graphics of it");
        Assert.AreEqual(1, (Values with { HdInterface = true }).Value(AreaOption.HdInterface));
        Assert.AreEqual(2, (Values with { HdInterface = true, GeneratedInterface = true }).Value(AreaOption.HdInterface), "ARC-UI-140: the third value");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.HdInterface, Value: 1), menu.Change(6, 1));
        menu.SetOptions(Values with { HdInterface = true });
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.HdInterface, Value: 2), menu.Change(6, 1), "On then Generated");
        menu.SetOptions(Values with { HdInterface = true, GeneratedInterface = true });
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.HdInterface, Value: 0), menu.Change(6, 1), "the list wraps");
        var game = AreaOptionsTexts.From((_, _) => "game text", "test");
        Assert.AreEqual("HD interface", game.Label(line), "the project's text, never a game message");
        Assert.AreEqual("Off", game.Value(line, 0)); Assert.AreEqual("On", game.Value(line, 1)); Assert.AreEqual("Generated (experimental)", game.Value(line, 2));
    }

    [TestMethod]
    public void The_hd_pack_line_is_a_project_list_of_the_three_sources_after_hd_interface()
    {
        var menu = Options(AreaMenuWindow.InPlay);
        var line = AreaOptionTable.Of(AreaOption.HdPack);
        Assert.AreEqual(7, menu.Controls.ToList().IndexOf(line), "the Game page's last line, after HD interface (the art's eighth slot)");
        Assert.IsTrue(line.Project && line.Shown);
        Assert.AreEqual(0, Values.Value(AreaOption.HdPack), "Auto by default");
        Assert.AreEqual(2, (Values with { HdPackSource = HdPackSourceMode.Custom }).Value(AreaOption.HdPack));
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.HdPack, Value: 1), menu.Change(7, 1));
        menu.SetOptions(Values with { HdPackSource = HdPackSourceMode.Custom });
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.HdPack, Value: 0), menu.Change(7, 1), "the list wraps from Custom to Auto");
        var game = AreaOptionsTexts.From((_, _) => "game text", "test");
        Assert.AreEqual("HD pack", game.Label(line), "the project's text, never a game message");
        CollectionAssert.AreEqual(new[] { "Auto", "Game", "Custom" }, new[] { 0, 1, 2 }.Select(v => game.Value(line, v)).ToArray());
        Assert.AreEqual(HdPackSourceMode.Auto, AreaGameSettings.Defaults.HdPackSource);
        Assert.IsNull(AreaGameSettings.Defaults.HdPackPath);
        var custom = AreaGameSettings.Defaults with { HdPackSource = HdPackSourceMode.Custom, HdPackPath = "/packs/my \"hd\" pack" };
        Assert.AreEqual(custom, AreaGameSettingsCodec.Deserialize(AreaGameSettingsCodec.Serialize(custom)), "a path with quotes round-trips");
    }

    [TestMethod]
    public void The_rule_mode_line_switches_between_the_two_built_in_modes()
    {
        var menu = Options(AreaMenuWindow.InPlay);
        var rules = AreaOptionTable.Of(AreaOption.Rules);
        Assert.AreEqual(4, menu.Controls.ToList().IndexOf(rules), "the Game page's last line");
        Assert.AreEqual((int)RuleMode.Steamwoven, Values.Value(AreaOption.Rules));
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Rules, Value: (int)RuleMode.Original), menu.Change(4, 1));
        menu.SetOptions(Values with { Rules = RuleMode.Original });
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Rules, Value: (int)RuleMode.Steamwoven), menu.Change(4, 1), "the list wraps");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.ChangeOption, Option: AreaOption.Rules, Value: (int)RuleMode.Steamwoven), menu.Change(4, -1));
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
        var generated = settings with { GeneratedInterface = true };
        Assert.AreEqual(generated, AreaGameSettingsCodec.Deserialize(AreaGameSettingsCodec.Serialize(generated)), "ARC-UI-140 round-trips");
        Assert.AreEqual("""{"version":5,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":false,"hdPack":"auto","hdPackPath":null,"generatedInterface":false}""", AreaGameSettingsCodec.Serialize(AreaGameSettings.Defaults));
        Assert.AreEqual(AreaGameSettings.Defaults with { HdInterface = true }, AreaGameSettingsCodec.Deserialize("""{"version":4,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":true,"hdPack":"auto","hdPackPath":null}"""),
            "an ARC-DEV-350 file reads with the generated interface off (ARC-UI-140)");
        Assert.AreEqual(AreaGameSettings.Defaults, AreaGameSettingsCodec.Deserialize("""{"version":5,"difficulty":1,"turnBased":false,"autoAttack":true,"floats":1,"floatSpeed":2,"hdInterface":false,"hdPack":"auto","hdPackPath":null,"generatedInterface":true}"""),
            "the generated interface is on only with the HD interface (ARC-UI-140)");
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
