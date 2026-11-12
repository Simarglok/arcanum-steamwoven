using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-011 main menu state (ledger MM1–MM7); ARC-UI-012 in-play windows, quit box and reopened root (SF1–SF3, MM6).</summary>
[TestClass]
public sealed class AreaMainMenuTests
{
    private static AreaMainMenu Menu(string? refusal = null) => new(AreaMainMenuTexts.Project("test"), refusal);

    private static string[] Labels(AreaMainMenu menu) => [.. menu.Shown.Select(button => menu.Texts.Text(button.Message))];

    /// <summary>ARC-RENDER-1170: View Intro (message 53, window 5) is shown and asks the host to play the intro only when the host found a decoder and the
    /// movie table (<see cref="AreaMainMenu.IntroRefusal"/> null); the typed hotkey letter does the same; it hides again with a reason when it cannot.</summary>
    [TestMethod]
    public void View_Intro_is_shown_and_asks_for_the_intro_only_when_the_host_can_play_movies()
    {
        var menu = Menu();
        menu.IntroRefusal = null;
        menu.Activate(0);
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window);
        CollectionAssert.AreEqual(new[] { "New Game", "Load Game", "Last Save", "View Intro", "Cancel" }, Labels(menu));
        Assert.AreEqual(0, menu.HiddenNotes.Count);
        var command = menu.Activate(3);
        Assert.AreEqual(AreaMenuCommandKind.ViewIntro, command.Kind);
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window, "the menu stays on its window; the host shows it again after the movies");
        menu.IntroRefusal = "movies need ffmpeg (install it or set ARC_FFMPEG)";
        CollectionAssert.AreEqual(new[] { "New Game", "Load Game", "Last Save", "Cancel" }, Labels(menu));
        CollectionAssert.AreEqual(new[] { "View Intro: movies need ffmpeg (install it or set ARC_FFMPEG)" }, menu.HiddenNotes.ToArray());
        Assert.AreEqual(AreaMenuCommandKind.None, menu.Activate(3).Kind, "with the intro hidden the fourth shown button is Cancel, not the intro");
    }

    [TestMethod]
    public void The_windows_list_the_originals_buttons_in_order_and_hide_what_is_not_built()
    {
        var menu = Menu();
        Assert.AreEqual(AreaMenuWindow.Main, menu.Window);
        CollectionAssert.AreEqual(new[] { 10, 11, 12, 13, 14 }, menu.Buttons.Select(button => button.Message).ToArray(), "MM2 window 2");
        CollectionAssert.AreEqual(new[] { "Single Player", "Options", "Exit Game" }, Labels(menu));
        CollectionAssert.AreEqual(new[] { "MultiPlayer: " + AreaMainMenu.NoNetworkPlay, "Credits: " + AreaMainMenu.NoCredits },
            menu.HiddenNotes.ToArray());
        menu.Activate(0);
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window);
        CollectionAssert.AreEqual(new[] { 50, 51, 52, 53, 54 }, menu.Buttons.Select(button => button.Message).ToArray(), "MM2 window 5");
        CollectionAssert.AreEqual(new[] { "New Game", "Load Game", "Last Save", "Cancel" }, Labels(menu));
        CollectionAssert.AreEqual(new[] { "View Intro: " + AreaMainMenu.NoIntro }, menu.HiddenNotes.ToArray());
        menu.Activate(0);
        Assert.AreEqual(AreaMenuWindow.NewGame, menu.Window);
        CollectionAssert.AreEqual(new[] { 420, 421, 422 }, menu.Buttons.Select(button => button.Message).ToArray(), "MM2 window 11");
        CollectionAssert.AreEqual(new[] { "Pick Character", "New Character", "Cancel" }, Labels(menu));
        CollectionAssert.AreEqual(new[] { AreaMenuWindow.Main, AreaMenuWindow.SinglePlayer }, menu.BackStack.ToArray());
    }

    [TestMethod]
    public void The_new_game_answer_can_come_after_the_menu_opened_and_changes_the_buttons_and_the_start()
    {
        // ARC-DEV-381: the menu opens while the start scene loads (no refusal yet, the browser works) and takes the scene's answer when it is published.
        var menu = Menu();
        menu.Activate(0); menu.Activate(0); menu.Activate(0);
        Assert.AreEqual(AreaMenuWindow.PickCharacter, menu.Window);
        Assert.AreEqual(AreaMenuCommandKind.StartNewGame, menu.Start().Kind);
        menu.NewGameRefusal = "the shown map is another";
        Assert.AreEqual(AreaMenuCommandKind.None, menu.Start().Kind, "a refusal that comes later stops the start");
        Assert.IsTrue(menu.Back());
        CollectionAssert.AreEqual(new[] { 422 }, menu.Shown.Select(button => button.Message).ToArray(), "the new game buttons are hidden with the reason");
        Assert.IsTrue(menu.HiddenNotes.Any(note => note.Contains("the shown map is another", StringComparison.Ordinal)));
        menu.NewGameRefusal = null;
        CollectionAssert.AreEqual(new[] { 420, 421, 422 }, menu.Shown.Select(button => button.Message).ToArray(), "the answer ready again shows them");
    }

    [TestMethod]
    public void The_start_scene_is_needed_from_the_new_game_window_on_and_not_by_the_other_windows()
    {
        // ARC-DEV-387: the start map's load begins when a new game's way does; Single Player, the options and the saves' windows need none.
        var menu = Menu();
        Assert.IsFalse(AreaMainMenu.NeedsStartScene(menu.Window), "the main window");
        menu.Activate(0);
        Assert.IsFalse(AreaMainMenu.NeedsStartScene(menu.Window), "Single Player (Load Game and Last Save are its buttons)");
        menu.Activate(0);
        Assert.AreEqual(AreaMenuWindow.NewGame, menu.Window);
        Assert.IsTrue(AreaMainMenu.NeedsStartScene(menu.Window), "New Game");
        menu.Activate(0);
        Assert.AreEqual(AreaMenuWindow.PickCharacter, menu.Window);
        Assert.IsTrue(AreaMainMenu.NeedsStartScene(menu.Window), "the pre-made browser");
        foreach (var window in new[] { AreaMenuWindow.Main, AreaMenuWindow.InPlay, AreaMenuWindow.InPlayLimited, AreaMenuWindow.SinglePlayer, AreaMenuWindow.Options })
            Assert.IsFalse(AreaMainMenu.NeedsStartScene(window), window.ToString());
        foreach (var window in new[] { AreaMenuWindow.NewGame, AreaMenuWindow.NewCharacter, AreaMenuWindow.PickCharacter, AreaMenuWindow.CharacterEditor, AreaMenuWindow.Shop })
            Assert.IsTrue(AreaMainMenu.NeedsStartScene(window), window.ToString());
    }

    [TestMethod]
    public void Hotkeys_are_the_label_message_plus_ten_upper_cased_and_only_shown_buttons_take_them()
    {
        var menu = Menu();
        Assert.AreEqual('S', menu.Texts.Hotkey(menu.Shown[0]));
        Assert.AreEqual(20, menu.Shown[0].HotkeyMessage);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('m'), "MultiPlayer is hidden");
        Assert.AreEqual(AreaMenuWindow.Main, menu.Window);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('s'));
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window, "a lower-case letter is upper-cased first (MM6)");
        Assert.AreEqual(AreaMenuCommandKind.LastSave, menu.Type('S').Kind);
        Assert.AreEqual(AreaMenuCommandKind.LoadGame, menu.Type('l').Kind);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('v'), "View Intro is hidden");
        menu.Type('c');
        Assert.AreEqual(AreaMenuWindow.Main, menu.Window, "Cancel goes back");
        Assert.AreEqual(AreaMenuCommandKind.Exit, menu.Type('E').Kind);
    }

    [TestMethod]
    public void Game_texts_replace_the_projects_and_a_missing_one_falls_back()
    {
        var texts = AreaMainMenuTexts.From(number => number switch { 10 => "Одиночная игра", 20 => "о", 14 => "", _ => null }, "test");
        var menu = new AreaMainMenu(texts, null);
        Assert.AreEqual("Одиночная игра", texts.Text(10));
        Assert.AreEqual("Exit Game", texts.Text(14), "an empty text takes the project's");
        Assert.AreEqual('О', texts.Hotkey(menu.Shown[0]));
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('о'));
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window);
        Assert.AreEqual('N', AreaMainMenuTexts.From(number => number == 60 ? "no" : null, "test").Hotkey(new(50, AreaMenuRole.Open)), "the first character (MM3)");
        Assert.IsFalse(AreaMainMenuTexts.Project("why").FromGame);
        Assert.AreEqual("why", AreaMainMenuTexts.Project("why").Detail);
    }

    [TestMethod]
    public void Escape_goes_back_and_does_nothing_at_the_root()
    {
        var menu = Menu();
        Assert.IsFalse(menu.Back(), "the root window ignores Escape the first time it opens");
        menu.Type('s'); menu.Type('n'); menu.Type('p');
        Assert.AreEqual(AreaMenuWindow.PickCharacter, menu.Window);
        Assert.IsTrue(menu.Back());
        Assert.AreEqual(AreaMenuWindow.NewGame, menu.Window);
        Assert.IsTrue(menu.Back()); Assert.IsTrue(menu.Back());
        Assert.AreEqual(AreaMenuWindow.Main, menu.Window);
        Assert.AreEqual(0, menu.BackStack.Count);
        Assert.IsFalse(menu.Back());
    }

    [TestMethod]
    public void The_browser_starts_at_the_first_hero_cycles_through_all_and_starts_with_the_browsed_one()
    {
        var menu = Menu();
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Start(), "outside the browser");
        menu.Type('s'); menu.Type('n'); menu.Type('p');
        Assert.AreEqual(0, menu.Hero);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('n'), "the browser has no letters");
        menu.Browse(-1);
        Assert.AreEqual(AreaStartingHero.Premade.Count - 1, menu.Hero, "Previous wraps to the last");
        menu.Browse(1); menu.Browse(5);
        Assert.AreEqual(1, menu.Hero, "one step per call");
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.StartNewGame, 1), menu.Start());
        Assert.AreEqual(new AreaMenuCommand(AreaMenuCommandKind.StartNewGame, 1), menu.Confirm());
        menu.Back(); menu.Type('p');
        Assert.AreEqual(0, menu.Hero, "the browser starts at the first hero each time it opens");
    }

    [TestMethod]
    public void A_scene_that_is_not_the_start_map_hides_pick_character_with_the_reason()
    {
        var menu = Menu("not the start map");
        menu.Type('s'); menu.Type('n');
        CollectionAssert.AreEqual(new[] { "Cancel" }, Labels(menu));
        CollectionAssert.AreEqual(new[] { "Pick Character: not the start map", "New Character: not the start map" }, menu.HiddenNotes.ToArray());
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('p'));
        Assert.AreEqual(AreaMenuWindow.NewGame, menu.Window);
    }

    [TestMethod]
    public void The_controller_moves_the_focus_over_shown_buttons_and_confirms_it()
    {
        var menu = Menu();
        Assert.AreEqual(0, menu.Focus);
        menu.Move(1);
        Assert.AreEqual(1, menu.Focus);
        menu.Move(1); menu.Move(1);
        Assert.AreEqual(0, menu.Focus, "wraps");
        menu.Move(-1);
        Assert.AreEqual(AreaMenuCommandKind.Exit, menu.Confirm().Kind);
        menu.Move(1);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Confirm());
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window);
        Assert.AreEqual(0, menu.Focus, "a new window focuses its first button");
    }

    [TestMethod]
    public void Last_save_without_a_save_and_a_resumed_window_keep_the_window()
    {
        var menu = Menu();
        menu.Type('s');
        Assert.AreEqual(AreaMenuCommandKind.LastSave, menu.Type('s').Kind);
        menu.NoSave();
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window);
        Assert.AreEqual("no save to continue from", menu.Notice);
        menu.Resume(null);
        Assert.IsNull(menu.Notice);
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window);
    }

    [TestMethod]
    public void Main_menu_argument_is_an_area_option_without_a_hero()
    {
        string[] Area(params string[] extra) => ["--mode", "crash-site-area", "--module", "m", "--resource", "r", "--source-id", "s", "--prototypes", "p",
            "--actor-resource", "a", "--area-mobile", "mo", "--area-effects", "e", "--area-patch", "pa", .. extra];
        Assert.IsFalse(TerrainViewerArguments.Parse(Area()).MainMenu);
        Assert.IsTrue(TerrainViewerArguments.Parse(Area("--main-menu", "true")).MainMenu);
        Assert.IsFalse(TerrainViewerArguments.Parse(Area("--main-menu", "false")).MainMenu);
        Assert.IsTrue(TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "m", "--resource", "r", "--source-id", "s",
            "--actor-resource", "a", "--game-root", "g", "--area-map", "Arcanum1-024-fixed", "--main-menu", "true"]).MainMenu, "map mode");
        foreach (var bad in new[] { "yes", "True", "" })
            Assert.Throws<ArgumentException>(() => TerrainViewerArguments.Parse(Area("--main-menu", bad)), bad);
        Assert.Throws<ArgumentException>(() => TerrainViewerArguments.Parse(Area("--main-menu", "true", "--main-menu", "true")), "once");
        Assert.Throws<ArgumentException>(() => TerrainViewerArguments.Parse(Area("--main-menu", "true", "--hero", "elf-male")), "the menu picks the hero");
        Assert.Throws<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode", "world-diagnostic", "--module", "m", "--resource", "r",
            "--source-id", "s", "--prototypes", "p", "--actor-resource", "a", "--main-menu", "true"]), "AREA only");
    }

    [TestMethod]
    public void The_in_play_windows_list_the_originals_buttons()
    {
        var menu = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, AreaMenuWindow.InPlay);
        Assert.AreEqual(AreaMenuWindow.InPlay, menu.Window);
        CollectionAssert.AreEqual(new[] { 30, 31, 32, 33, 34 }, menu.Buttons.Select(button => button.Message).ToArray(), "SF1 window 3");
        CollectionAssert.AreEqual(new[] { "Save Game", "Load Game", "Options", "Quit", "Continue" }, Labels(menu));
        Assert.AreEqual(0, menu.HiddenNotes.Count);
        Assert.AreEqual(AreaMenuCommandKind.SaveGame, menu.Type('s').Kind);
        Assert.AreEqual(AreaMenuCommandKind.LoadGame, menu.Type('L').Kind);
        Assert.AreEqual(AreaMenuCommandKind.Continue, menu.Type('c').Kind);
        var limited = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, AreaMenuWindow.InPlayLimited);
        CollectionAssert.AreEqual(new[] { 70, 71, 72 }, limited.Buttons.Select(button => button.Message).ToArray(), "SF1 window 4");
        CollectionAssert.AreEqual(new[] { "Options", "Quit", "Continue" }, Labels(limited));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, AreaMenuWindow.SinglePlayer));
    }

    [TestMethod]
    public void Quit_asks_first_and_only_yes_quits_to_the_main_menu()
    {
        var menu = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, AreaMenuWindow.InPlay);
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('q'));
        Assert.IsTrue(menu.QuitPending, "SF3: the quit box asks (message 5100)");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('s'), "the box owns the letters");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Activate(0), "and the buttons");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Type('n'));
        Assert.IsFalse(menu.QuitPending, "N answers no and the window stays");
        menu.Type('Q');
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Escape(), "Escape answers no");
        Assert.IsFalse(menu.QuitPending);
        Assert.AreEqual(AreaMenuWindow.InPlay, menu.Window);
        menu.Type('q');
        Assert.AreEqual(AreaMenuCommandKind.QuitToMenu, menu.Type('y').Kind);
        menu.Type('q');
        Assert.AreEqual(AreaMenuCommandKind.QuitToMenu, menu.Confirm().Kind, "a controller's confirm answers yes");
        Assert.AreEqual(AreaMenuCommand.Nothing, menu.Answer(true), "no box, no answer");
        Assert.AreEqual("Quit the current game?", menu.Texts.Text(AreaMainMenuTexts.QuitQuestion));
    }

    [TestMethod]
    public void Escape_continues_play_in_play_and_exits_from_a_reopened_main_menu()
    {
        Assert.AreEqual(AreaMenuCommandKind.Continue, new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, AreaMenuWindow.InPlay).Escape().Kind, "SF2");
        Assert.AreEqual(AreaMenuCommandKind.Continue, new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, AreaMenuWindow.InPlayLimited).Escape().Kind);
        var first = Menu();
        Assert.AreEqual(AreaMenuCommand.Nothing, first.Escape(), "MM6: the first opening ignores Escape at the root");
        var again = new AreaMainMenu(AreaMainMenuTexts.Project("test"), null, reopened: true);
        again.Type('s');
        Assert.AreEqual(AreaMenuCommand.Nothing, again.Escape(), "a window goes back first");
        Assert.AreEqual(AreaMenuWindow.Main, again.Window);
        Assert.AreEqual(AreaMenuCommandKind.Exit, again.Escape().Kind, "MM6: the reopened root exits the game");
    }
}
