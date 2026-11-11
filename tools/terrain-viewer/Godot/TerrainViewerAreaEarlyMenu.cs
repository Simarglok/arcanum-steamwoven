using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-381 (O1 of the load-time diagnosis): the main menu opens as soon as the menu art is read, while the start map loads behind
// it, instead of after the whole map is published. The menu that opens early is the same one (the same window, texts and art); what
// differs is that no scene exists yet, so
//  - a button that opens a window the menu itself draws works at once (the single player and new game windows, the pre-made hero
//    browser, the options window, Exit);
//  - a choice that needs the scene (Start in the browser, New Character, Load Game, Last Save) is held: the menu shows that it
//    waits, takes no other input except Escape or Back, which take the choice back, and runs the choice when the scene is published;
//  - when the scene is published the menu attaches to it as the one that opened over a ready scene does (the new game answer from
//    the scene, the menu's soundtrack), and a held choice runs.
// A start scene that cannot load closes the menu, so the failure's own screen shows as before. Later openings (after a quit, a death
// or in play) are unchanged: they open over a published scene.
public partial class TerrainViewerApp
{
    /// <summary>The menu is open and the start scene is still loading behind it.</summary>
    private bool _menuAwaitsScene;
    /// <summary>The choice that waits for the scene (set only while <see cref="_menuAwaitsScene"/>).</summary>
    private AreaMenuCommand? _menuDeferred;
    /// <summary>For the smoke and the timing probe: the menu opened before the scene was published, and how the attach went.</summary>
    private EarlyMenuRecord? _earlyMenuRecord;

    private sealed record EarlyMenuRecord(ulong OpenedMsec)
    {
        public bool Attached { get; set; }
        public ulong AttachedMsec { get; set; }
        public AreaMenuCommandKind? Held { get; set; }
        public string? Refusal { get; set; }
    }

    /// <summary>The choices that need the scene: the start of a new game (as a hero or as the new character), the new character
    /// window (its draft session and the shop map need the scene's recipe) and the two loads of a save.</summary>
    private static bool DefersUntilScene(AreaMenuCommandKind kind)
        => kind is AreaMenuCommandKind.StartNewGame or AreaMenuCommandKind.OpenNewCharacter or AreaMenuCommandKind.LoadGame or AreaMenuCommandKind.LastSave;

    /// <summary>Opens the main menu over the start map's load, right after the load began (the launch's first load only; area mode with
    /// the menu asked for). Does nothing when the load already ended (it failed at once, or a self-test publishes it).</summary>
    private void OpenEarlyMainMenu()
    {
        if (_arguments is not { MainMenu: true, Mode: TerrainViewerMode.Area } arguments || SelfTestBootstrap || _mainMenuShown || _sceneFlow is not null
            || _lastLoad is null || _lastLoad.IsCompleted) return;
        var texts = MainMenuTexts(arguments);
        _menuAwaitsScene = true;
        OpenMainMenu(texts, early: true);
        if (_mainMenu is null) { _menuAwaitsScene = false; return; }
        // The first scene's own opening is this one: its publication attaches the menu, it does not open another (MaybeOpenMainMenu).
        _mainMenuShown = true;
        _earlyMenuRecord = new(Time.GetTicksMsec());
        GD.Print($"ARC-DEV-381 early main menu open, the start scene loads behind it (ms since start {Time.GetTicksMsec()})");
    }

    /// <summary>Holds a choice that needs the scene and tells the player.</summary>
    private void DeferMenuCommand(AreaMenuCommand command)
    {
        _menuDeferred = command;
        if (_earlyMenuRecord is { } record) record.Held = command.Kind;
        if (_mainMenu is { } menu) menu.Notice = "the world is still loading; the choice goes on when it is ready (Escape takes it back)";
        GD.Print($"ARC-DEV-381 menu choice held until the scene is published kind={command.Kind}");
        RenderMainMenu();
    }

    private void CancelDeferredMenuCommand()
    {
        GD.Print($"ARC-DEV-381 menu choice taken back kind={_menuDeferred?.Kind}");
        _menuDeferred = null;
        if (_earlyMenuRecord is { } record) record.Held = null;
        if (_mainMenu is { } menu) menu.Notice = null;
        RenderMainMenu();
    }

    /// <summary>The start scene is published under the open menu: the menu takes the scene's answer for a new game, plays its
    /// soundtrack and runs the choice it held.</summary>
    private void AttachEarlyMenuToScene()
    {
        _menuAwaitsScene = false;
        var held = _menuDeferred; _menuDeferred = null;
        if (_mainMenu is not { } menu) return;
        if (!QuickKeysActive)
        {
            // A first scene without a game session shows no menu (as a menu that opens over a published scene), and nothing runs.
            _mainMenuStatus = "not opened: the first scene has no game session"; GD.Print("ARC-UI-011 main menu not opened: no game session");
            menu.DropEditor(); CloseCharacterEditor(); _mainMenuPanel?.CloseView(); _mainMenu = null;
            return;
        }
        var refusal = NewGameRefusal();
        menu.NewGameRefusal = refusal;
        // A scene that is not a start scene a new game can use: the browser the player sits in cannot start, so the window goes back.
        if (refusal is not null) while (menu.Window == AreaMenuWindow.PickCharacter && menu.Back()) { }
        var needsNewGame = held?.Kind is AreaMenuCommandKind.StartNewGame or AreaMenuCommandKind.OpenNewCharacter;
        // Only the hold's own note (or a refusal) is replaced: a note an option change left (the HD pack source's) stays.
        if (refusal is not null && needsNewGame) menu.Notice = "new game refused: " + refusal;
        else if (held is not null) menu.Notice = null;
        EnterMainMenuSound();
        if (_earlyMenuRecord is { } record) { record.Attached = true; record.AttachedMsec = Time.GetTicksMsec(); record.Refusal = refusal; }
        GD.Print($"ARC-DEV-381 early main menu attached to the scene newGame={refusal ?? "ready"} held={held?.Kind.ToString() ?? "none"}");
        RenderMainMenu();
        RefreshReadyStatus();
        if (held is { } command && !(needsNewGame && refusal is not null)) MainMenuCommand(() => command);
    }

    /// <summary>The start scene failed to load under the early menu: the menu closes and the failure's screen is shown.</summary>
    private void EarlyMenuSceneFailed()
    {
        if (!_menuAwaitsScene) return;
        _menuAwaitsScene = false; _menuDeferred = null;
        GD.Print("ARC-DEV-381 early main menu closed: the start scene did not load");
        if (_mainMenu is not null) { _mainMenu.DropEditor(); _mainMenuPanel?.CloseView(); _mainMenu = null; }
        _mainMenuStatus = "closed: the start scene did not load";
        // The first opening is still owed: a later successful publication of the start scene opens the menu as before.
        _mainMenuShown = false;
    }

    /// <summary>The smoke's check of the early menu (the <c>menu</c> stage, on the menu that opened over the loading start map): it opened
    /// before the scene was published and attached to it; then the waiting is played again on the published scene, as it was while
    /// the scene loaded: a choice that needs the scene is held and the menu takes no other input, Escape takes it back, and the
    /// scene's attach runs a held choice (here Last Save with no save, which changes nothing) and a start of a new game.</summary>
    private void EarlyMenuForSmoke(Action<bool, string> require, Action<int> choose)
    {
        require(_earlyMenuRecord is { Attached: true } record && record.OpenedMsec < record.AttachedMsec && !_menuAwaitsScene && record.Held is null,
            $"the menu opened before the scene was published and attached to it: {_earlyMenuRecord}");
        var menu = _mainMenu!;
        var placeholder = _areaSession;
        // The waiting again: the scene stands, but the menu behaves as it did while the scene loaded.
        _menuAwaitsScene = true;
        choose(10); choose(52);
        require(_menuDeferred is { Kind: AreaMenuCommandKind.LastSave } && menu.Window == AreaMenuWindow.SinglePlayer && menu.Notice?.Contains("still loading", StringComparison.Ordinal) == true,
            $"Last Save pressed while the scene loads is held, with a note: {_menuDeferred} {menu.Notice}");
        MainMenuCommand(() => { menu.Back(); return AreaMenuCommand.Nothing; });
        choose(50);
        require(menu.Window == AreaMenuWindow.SinglePlayer && _menuDeferred is not null, "a held choice takes no other input (Back is ignored, Single Player's New Game is not opened)");
        HandleMainMenuKey(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
        require(_menuDeferred is null && menu.Window == AreaMenuWindow.SinglePlayer && menu.Notice is null, $"Escape took the held choice back: {_menuDeferred} {menu.Notice}");
        // A held start of a new game: the browser works while the scene loads, its Start waits.
        choose(50); choose(420);
        require(menu.Window == AreaMenuWindow.PickCharacter, "the pre-made browser opens while the scene loads");
        MainMenuCommand(() => menu.Start());
        require(_menuDeferred is { Kind: AreaMenuCommandKind.StartNewGame, Hero: 0 } && ReferenceEquals(placeholder, _areaSession) && MainMenuOpen,
            $"Start in the browser is held and starts nothing yet: {_menuDeferred}");
        HandleMainMenuKey(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
        require(_menuDeferred is null && menu.Window == AreaMenuWindow.PickCharacter, "Escape took the held start back and stayed in the browser");
        for (var back = 0; back < 3; back++) MainMenuCommand(() => { menu.Back(); return AreaMenuCommand.Nothing; });
        require(menu.Window == AreaMenuWindow.Main, "back at the main window");
        // The scene's attach runs a held choice: Last Save with no save is a harmless one.
        choose(10); choose(52);
        require(_menuDeferred is { Kind: AreaMenuCommandKind.LastSave }, "Last Save is held again");
        AttachEarlyMenuToScene();
        require(!_menuAwaitsScene && _menuDeferred is null && MainMenuOpen && ReferenceEquals(placeholder, _areaSession) && menu.Notice == "no save to continue from",
            $"the attach ran the held Last Save (no save, the menu stays): {menu.Notice}");
        MainMenuCommand(() => { menu.Back(); return AreaMenuCommand.Nothing; });
        require(menu.Window == AreaMenuWindow.Main, "back at the main window for the rest of the stage");
        GD.Print("ARC-DEV-381 native-smoke early-menu PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            OpenedMsec = _earlyMenuRecord!.OpenedMsec, AttachedMsec = _earlyMenuRecord.AttachedMsec }));
    }
}
