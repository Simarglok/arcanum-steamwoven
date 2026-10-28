using Arcanum.TerrainViewer.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

/// <summary>ARC-UI-011 main menu state (ledger MM1–MM7).</summary>
[TestClass]
public sealed class AreaMainMenuTests
{
    private static AreaMainMenu Menu(string? refusal = null) => new(AreaMainMenuTexts.Project("test"), refusal);

    private static string[] Labels(AreaMainMenu menu) => [.. menu.Shown.Select(button => menu.Texts.Text(button.Message))];

    [TestMethod]
    public void The_windows_list_the_originals_buttons_in_order_and_hide_what_is_not_built()
    {
        var menu = Menu();
        Assert.AreEqual(AreaMenuWindow.Main, menu.Window);
        CollectionAssert.AreEqual(new[] { 10, 11, 12, 13, 14 }, menu.Buttons.Select(button => button.Message).ToArray(), "MM2 window 2");
        CollectionAssert.AreEqual(new[] { "Single Player", "Exit Game" }, Labels(menu));
        CollectionAssert.AreEqual(new[] { "MultiPlayer: " + AreaMainMenu.NoNetworkPlay, "Options: " + AreaMainMenu.NoOptions, "Credits: " + AreaMainMenu.NoCredits },
            menu.HiddenNotes.ToArray());
        menu.Activate(0);
        Assert.AreEqual(AreaMenuWindow.SinglePlayer, menu.Window);
        CollectionAssert.AreEqual(new[] { 50, 51, 52, 53, 54 }, menu.Buttons.Select(button => button.Message).ToArray(), "MM2 window 5");
        CollectionAssert.AreEqual(new[] { "New Game", "Load Game", "Last Save", "Cancel" }, Labels(menu));
        CollectionAssert.AreEqual(new[] { "View Intro: " + AreaMainMenu.NoIntro }, menu.HiddenNotes.ToArray());
        menu.Activate(0);
        Assert.AreEqual(AreaMenuWindow.NewGame, menu.Window);
        CollectionAssert.AreEqual(new[] { 420, 421, 422 }, menu.Buttons.Select(button => button.Message).ToArray(), "MM2 window 11");
        CollectionAssert.AreEqual(new[] { "Pick Character", "Cancel" }, Labels(menu));
        CollectionAssert.AreEqual(new[] { AreaMenuWindow.Main, AreaMenuWindow.SinglePlayer }, menu.BackStack.ToArray());
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
        Assert.IsNull(AreaMainMenuTexts.From(number => number == 60 ? "No" : null, "test").Hotkey(new(50, AreaMenuRole.Open)), "not one letter: no hotkey");
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
        CollectionAssert.AreEqual(new[] { "Pick Character: not the start map", "New Character: " + AreaMainMenu.NoEditor }, menu.HiddenNotes.ToArray());
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
        menu.Move(1);
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
}
