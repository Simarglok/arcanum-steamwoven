using Arcanum.Session;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-381 (O1 of the load-time diagnosis): the main menu opens as soon as the menu art is read, while the start map loads behind
// it, instead of after the whole map is published. The menu that opens early is the same one (the same window, texts and art); what
// differs is that no scene exists yet, so
//  - a button that opens a window the menu itself draws works at once (the single player and new game windows, the pre-made hero
//    browser, the options window, Exit);
//  - a choice that needs the scene (Start in the browser, Accept in the New Character window) is held: the menu shows that it
//    waits, takes no other input except Escape or Back, which take the choice back, and runs the choice when the scene is published;
//  - when the scene is published the menu attaches to it as the one that opened over a ready scene does (the new game answer from
//    the scene, the menu's soundtrack), and a held choice runs.
// A start scene that cannot load closes the menu, so the failure's own screen shows as before. Later openings (after a quit, a death
// or in play) are unchanged: they open over a published scene.
//
// ARC-DEV-387 (follow-up of O1, owner objection): in map mode the start map is not loaded at the start. It begins to load when the
// player enters the New Game window (the pre-made browser and the new character are one press from there, the way a new game goes;
// <see cref="AreaMainMenu.NeedsStartScene"/>), so that only a new game waits for it. Last Save, and a save chosen in the load window,
// need no start map: the save is read for the map it was made on and that map loads at once as the scene the load runs on
// (<see cref="AreaSaveLaunch"/>, a <see cref="SceneFlowKind.LoadSave"/> flow), one load instead of the start map's and then the save's;
// the menu stays, saying the save loads, and goes when the save is in. A load that began while the start map was loading replaces it.
// The exterior mode (no --area-map) keeps the start at once and the hold of every choice that needs the scene (ARC-DEV-381).
public partial class TerrainViewerApp
{
    /// <summary>The menu is open and the start scene is still loading behind it.</summary>
    private bool _menuAwaitsScene;
    /// <summary>The choice that waits for the scene (set only while <see cref="_menuAwaitsScene"/>).</summary>
    private AreaMenuCommand? _menuDeferred;
    /// <summary>For the smoke and the timing probe: the menu opened before the scene was published, and how the attach went.</summary>
    private EarlyMenuRecord? _earlyMenuRecord;
    /// <summary>ARC-DEV-387: the start map's load has not begun (it begins when a new game's way does, or a save's map loads instead).</summary>
    private bool _startLoadPending;
    /// <summary>ARC-DEV-387: this run's start is the lazy one (map mode with the menu): Last Save and Load Game need no scene.</summary>
    private bool _startLoadLazy;
    /// <summary>ARC-DEV-387: a save chosen in the menu loads its own map; the menu waits, saying so, and takes no input.</summary>
    private bool _menuLoadingSave;
    /// <summary>For the smoke: it opened the New Game window itself (the route of a player's first press) to start the lazy load, and the menu goes back to the main window once the scene is there.</summary>
    private bool _smokeStartedLoadFromMenu;

    private sealed record EarlyMenuRecord(ulong OpenedMsec)
    {
        public bool Attached { get; set; }
        public ulong AttachedMsec { get; set; }
        public AreaMenuCommandKind? Held { get; set; }
        public string? Refusal { get; set; }
    }

    /// <summary>The choices that need the scene: the start of a new game (as a hero or as the new character), the character editor
    /// (its draft session needs the scene's new-game recipe) and the two loads of a save.</summary>
    /// <remarks>ARC-DEV-387: in the lazy start the two loads of a save are not among them (they need no start map).
    /// ARC-UI-2282: the New Character window itself is not among them: it needs only the generic hero read from the module (and starts the
    /// shop map's own load), so it opens at once, as in the original; its Accept (the editor) is the first step that waits for the scene.</remarks>
    private bool DefersUntilScene(AreaMenuCommandKind kind)
        => kind is AreaMenuCommandKind.StartNewGame or AreaMenuCommandKind.OpenCharacterEditor
            || (!_startLoadLazy && kind is AreaMenuCommandKind.LoadGame or AreaMenuCommandKind.LastSave);

    /// <summary>ARC-DEV-387: whether this start leaves the start map unloaded until the player needs it: the menu in map mode, in a
    /// player's run, a smoke (which opens the New Game window itself) or a self-test not included.</summary>
    private bool DefersStartLoad(TerrainViewerArguments arguments)
        => arguments is { MainMenu: true, Mode: TerrainViewerMode.Area, AreaMapName: not null } && !SelfTestBootstrap && _sceneFlow is null;

    /// <summary>ARC-DEV-387: starts the start map's load behind the open menu (once).</summary>
    private void BeginStartLoad(string reason)
    {
        if (!_startLoadPending || _arguments is null) return;
        _startLoadPending = false;
        GD.Print($"ARC-DEV-387 start map load begins: {reason} (ms since start {Time.GetTicksMsec()})");
        _lastLoad = StartLoadAsync(_arguments);
    }

    /// <summary>Opens the main menu over the start map's load, right after the load began (the launch's first load only; area mode with
    /// the menu asked for). Does nothing when the load already ended (it failed at once, or a self-test publishes it).</summary>
    private void OpenEarlyMainMenu()
    {
        if (_arguments is not { MainMenu: true, Mode: TerrainViewerMode.Area } arguments || SelfTestBootstrap || _mainMenuShown || _sceneFlow is not null
            || (!_startLoadPending && (_lastLoad is null || _lastLoad.IsCompleted))) return;
        var texts = MainMenuTexts(arguments);
        _menuAwaitsScene = true;
        OpenMainMenu(texts, early: true);
        if (_mainMenu is null) { _menuAwaitsScene = false; return; }
        // The first scene's own opening is this one: its publication attaches the menu, it does not open another (MaybeOpenMainMenu).
        _mainMenuShown = true;
        _earlyMenuRecord = new(Time.GetTicksMsec());
        GD.Print($"ARC-DEV-381 early main menu open, the start scene {(_startLoadPending ? "is not loaded yet (ARC-DEV-387)" : "loads behind it")} (ms since start {Time.GetTicksMsec()})");
        // A smoke runs its stages on the published scene, so it takes the player's route to the load: Single Player, New Game.
        if (_startLoadPending && _nativeSmoke) Callable.From(SmokeOpenNewGameWindow).CallDeferred();
        Callable.From(SoakOpenWindow).CallDeferred();
    }

    private void SmokeOpenNewGameWindow()
    {
        if (_mainMenu is null || !_startLoadPending) return;
        foreach (var message in new[] { 10, 50 })
        {
            var index = _mainMenu.Shown.ToList().FindIndex(button => button.Message == message);
            if (index < 0) { BeginStartLoad("the smoke found no button " + message); return; }
            var button = index;
            MainMenuCommand(() => _mainMenu!.Activate(button));
        }
        _smokeStartedLoadFromMenu = true;
        if (_startLoadPending) BeginStartLoad("the smoke's New Game window did not start it");
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
        // ARC-UI-2282: the New Character window can be open at the attach too (it needs no scene): it goes back as the browser does.
        var creating = menu.Window == AreaMenuWindow.NewCharacter;
        if (refusal is not null) while (menu.Window is AreaMenuWindow.PickCharacter or AreaMenuWindow.NewCharacter && menu.Back()) { }
        var needsNewGame = creating || held?.Kind is AreaMenuCommandKind.StartNewGame or AreaMenuCommandKind.OpenCharacterEditor;
        // Only the hold's own note (or a refusal) is replaced: a note an option change left (the HD pack source's) stays.
        if (refusal is not null && needsNewGame) menu.Notice = "new game refused: " + refusal;
        else if (held is not null) menu.Notice = null;
        // The smoke took the player's route to the New Game window to start the load; its stages begin at the main window.
        if (_smokeStartedLoadFromMenu) { _smokeStartedLoadFromMenu = false; while (menu.Window != AreaMenuWindow.Main && menu.Back()) { } }
        // ARC-UI-139: the menu's song keeps playing across the attach (it belongs to the menu, not to the scene).
        if (_earlyMenuRecord is { } record) { record.Attached = true; record.AttachedMsec = Time.GetTicksMsec(); record.Refusal = refusal; }
        GD.Print($"ARC-DEV-381 early main menu attached to the scene newGame={refusal ?? "ready"} held={held?.Kind.ToString() ?? "none"}");
        // ARC-DEV-387: the load window may be open over the hidden menu (the start map loaded while a save was being chosen): the menu comes back with it.
        if (!SaveListOpen) RenderMainMenu();
        RefreshReadyStatus();
        // A held Accept runs only while its window still stands (the editor opens from the New Character window alone).
        if (held is { } command && !(needsNewGame && refusal is not null)
            && !(command.Kind == AreaMenuCommandKind.OpenCharacterEditor && (menu.Window != AreaMenuWindow.NewCharacter || menu.Creation is null))) MainMenuCommand(() => command);
    }

    /// <summary>ARC-DEV-387: a save chosen in the menu (Last Save, a row of the load window) while no scene stands: the save is read for
    /// its map and that map loads as the scene the load runs on (<see cref="AreaSaveLaunch"/>); the menu stays, saying so, and takes no
    /// input (<see cref="_menuLoadingSave"/>) until <see cref="FinishSaveSceneFlow"/>. Null when it began, else why nothing changed.</summary>
    private string? BeginSaveSceneFlow(string saveName, string prefix)
    {
        if (_sceneFlow is not null) return "a scene is loading";
        if (_launchArguments is not { } launch) return "the launch arguments are not known";
        var flow = new SceneFlowRequest(SceneFlowKind.LoadSave, -1, null, SaveName: saveName, SavePrefix: prefix);
        _sceneFlow = flow; _menuLoadingSave = true;
        // A start map load already in flight is given up now, not when the save's map begins (the save is resolved first): its publication must not attach the menu.
        _startLoadPending = false;
        _loadCancellation?.Cancel();
        _generation = _coordinator.BeginGeneration();
        GD.Print($"ARC-DEV-387 save chosen before any scene: its own map loads, not the start map save={saveName} startLoadInFlight={!_startLoadPending} (ms since start {Time.GetTicksMsec()})");
        // ARC-UI-1362: the original closes the map and opens the saved one with the clock turning: black with the clock from the choice until the save is in.
        BeginTransitionLoad();
        _lastLoad = LoadSaveSceneFlowAsync(flow, launch, SaveLibrary.Slot(saveName));
        return null;
    }

    /// <summary>The menu's windows as they stand while the save's map loads under the load screen (ARC-UI-1362: no wait note, the screen is black with the clock).</summary>
    private void ShowSaveLoadingMenu()
    {
        if (_mainMenu is null) return;
        RenderMainMenu();
    }

    private async Task LoadSaveSceneFlowAsync(SceneFlowRequest flow, TerrainViewerArguments launch, GameSessionSaveSlot slot)
    {
        AreaSaveLaunchResult resolved;
        try { resolved = await Task.Run(() => AreaSaveLaunch.Resolve(launch, slot)); }
        catch (Exception error) when (error is not OutOfMemoryException) { resolved = new(null, "the save's map could not be found: " + error.GetType().Name); }
        if (_exiting || !ReferenceEquals(_sceneFlow, flow)) return;
        if (resolved.Arguments is not { } next)
        {
            _sceneFlow = null; _menuLoadingSave = false;
            // The start map's load that the save flow gave up is owed again: the next New Game window begins it.
            _startLoadPending = true;
            ClearTransition(); // ARC-UI-1362: the menu comes back with the reason
            GD.Print("ARC-DEV-387 save refused before any scene: " + resolved.Refusal);
            if (_mainMenu is { } menu) { menu.Notice = resolved.Refusal; RenderMainMenu(); }
            return;
        }
        GD.Print($"ARC-DEV-387 save's map found: {next.AreaMapName} (runtime map {resolved.RuntimeMap}) loads now");
        _sceneFlow = flow with { Map = next.AreaMapName };
        await LoadSceneFlowAsync(next);
    }

    /// <summary>The save's scene is published under the waiting menu: the save loads into it. A load that did not take gives the
    /// menu back with the reason.</summary>
    private void FinishSaveSceneFlow(SceneFlowRequest flow)
    {
        _menuAwaitsScene = false; _menuLoadingSave = false;
        if (_mainMenu is null) { ClearTransition(); return; } // ARC-UI-1362: the screen never outlives the menu it began for
        var name = flow.SaveName!;
        if (QuickKeysActive && LoadGame(SaveLibrary.Slot(name), flow.SavePrefix ?? "Load ", MainMenuTables(), AreaLoadMessageRoute.Result, supersedePending: false))
        {
            GD.Print($"ARC-DEV-387 save loaded on its own map save={name} (ms since start {Time.GetTicksMsec()})");
            EndMainMenu("save " + name);
            // ARC-UI-1362: the save is in: the clock goes and the black gives the game back (a save that sent the hero to another map keeps it until that map is there).
            if (_mapHandoff is not { Kind: not MapHandoffKind.Stream }) ReleaseTransition();
            return;
        }
        ClearTransition(); // ARC-UI-1362: the load failed, the menu comes back with the reason
        var why = QuickKeysActive ? _quickSaveStatus : "the save's scene has no game session";
        // The menu comes back over the scene the save opened on, as an early menu attaches to its scene.
        _menuAwaitsScene = true;
        AttachEarlyMenuToScene();
        if (_mainMenu is { } menu) { menu.Notice = why; RenderMainMenu(); }
    }

    /// <summary>The start scene failed to load under the early menu: the menu closes and the failure's screen is shown.</summary>
    private void EarlyMenuSceneFailed()
    {
        // ARC-DEV-387: the save's map did not load: the menu stays, saying so (the player can choose again); the failure's screen is shown under it.
        if (_menuLoadingSave)
        {
            _menuLoadingSave = false;
            ClearTransition(); // ARC-UI-1362
            GD.Print("ARC-DEV-387 the save's map did not load: the menu stays");
            if (_mainMenu is { } loading) { loading.Notice = "the save's map did not load (" + _lastPublishedFailure + ")"; RenderMainMenu(); }
            return;
        }
        if (!_menuAwaitsScene) return;
        _menuAwaitsScene = false; _menuDeferred = null; _menuLoadingSave = false;
        GD.Print("ARC-DEV-381 early main menu closed: the start scene did not load");
        if (_mainMenu is not null) { _mainMenu.DropEditor(); _mainMenuPanel?.CloseView(); _mainMenu = null; }
        _mainMenuStatus = "closed: the start scene did not load";
        // The first opening is still owed: a later successful publication of the start scene opens the menu as before.
        _mainMenuShown = false;
    }

    /// <summary>The smoke's check of the early menu (the <c>menu</c> stage, on the menu that opened over the start map's lazy load): it opened
    /// before the scene was published, the load began on the smoke's own press of the New Game window (ARC-DEV-387) and the menu attached to the
    /// scene; then the waiting is played again on the published scene, as it was while the scene loaded: Last Save with no save needs no scene
    /// (it is not held), a choice that needs the scene is held and the menu takes no other input, Escape takes it back, and the scene's attach
    /// runs a held choice (here the New Character window's Accept, which opens the character editor on the scene's draft). ARC-UI-2282: the New
    /// Character window itself opens at once, with no scene.</summary>
    private void EarlyMenuForSmoke(Action<bool, string> require, Action<int> choose)
    {
        require(_earlyMenuRecord is { Attached: true } record && record.OpenedMsec < record.AttachedMsec && !_menuAwaitsScene && record.Held is null,
            $"the menu opened before the scene was published and attached to it: {_earlyMenuRecord}");
        require(!_startLoadPending && _startLoadLazy, $"the lazy start's load began (pending {_startLoadPending}, lazy {_startLoadLazy})");
        var menu = _mainMenu!;
        var placeholder = _areaSession;
        // The waiting again: the scene stands, but the menu behaves as it did while the scene loaded.
        _menuAwaitsScene = true;
        choose(10); choose(52);
        require(_menuDeferred is null && !_menuLoadingSave && _sceneFlow is null && menu.Window == AreaMenuWindow.SinglePlayer && menu.Notice == "no save to continue from",
            $"Last Save with no save is not held (a save's load needs no start map): {_menuDeferred} {menu.Notice}");
        // ARC-UI-2282: New Character opens its window at once (it needs only the generic hero of the module); its Accept, which opens the editor on the scene's draft, is held.
        choose(50); choose(421);
        require(_menuDeferred is null && menu.Window == AreaMenuWindow.NewCharacter && menu.Creation is not null && string.IsNullOrEmpty(menu.Notice),
            $"New Character pressed while the scene loads opens its window at once: {_menuDeferred} {menu.Window} {menu.Notice}");
        MainMenuCommand(() => new(AreaMenuCommandKind.OpenCharacterEditor));
        require(_menuDeferred is { Kind: AreaMenuCommandKind.OpenCharacterEditor } && menu.Window == AreaMenuWindow.NewCharacter && menu.Notice?.Contains("still loading", StringComparison.Ordinal) == true,
            $"Accept pressed while the scene loads is held, with a note: {_menuDeferred} {menu.Notice}");
        MainMenuCommand(() => { menu.Back(); return AreaMenuCommand.Nothing; });
        require(menu.Window == AreaMenuWindow.NewCharacter && _menuDeferred is not null, "a held choice takes no other input (Back is ignored)");
        HandleMainMenuKey(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
        require(_menuDeferred is null && menu.Window == AreaMenuWindow.NewCharacter && menu.Notice is null, $"Escape took the held choice back: {_menuDeferred} {menu.Notice}");
        MainMenuCommand(() => { menu.Back(); return AreaMenuCommand.Nothing; });
        require(menu.Window == AreaMenuWindow.NewGame, "Back left the New Character window");
        // A held start of a new game: the browser works while the scene loads, its Start waits.
        choose(420);
        require(menu.Window == AreaMenuWindow.PickCharacter, "the pre-made browser opens while the scene loads");
        MainMenuCommand(() => menu.Start());
        require(_menuDeferred is { Kind: AreaMenuCommandKind.StartNewGame, Hero: 0 } && ReferenceEquals(placeholder, _areaSession) && MainMenuOpen,
            $"Start in the browser is held and starts nothing yet: {_menuDeferred}");
        HandleMainMenuKey(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
        require(_menuDeferred is null && menu.Window == AreaMenuWindow.PickCharacter, "Escape took the held start back and stayed in the browser");
        for (var back = 0; back < 3; back++) MainMenuCommand(() => { menu.Back(); return AreaMenuCommand.Nothing; });
        require(menu.Window == AreaMenuWindow.Main, "back at the main window");
        // The scene's attach runs a held choice: the New Character window's Accept opens the character editor on the scene's draft.
        choose(10); choose(50); choose(421);
        MainMenuCommand(() => new(AreaMenuCommandKind.OpenCharacterEditor));
        require(_menuDeferred is { Kind: AreaMenuCommandKind.OpenCharacterEditor } && menu.Window == AreaMenuWindow.NewCharacter, "Accept is held again");
        AttachEarlyMenuToScene();
        require(!_menuAwaitsScene && _menuDeferred is null && MainMenuOpen && ReferenceEquals(placeholder, _areaSession) && menu.Window == AreaMenuWindow.CharacterEditor,
            $"the attach ran the held Accept: {menu.Window} {menu.Notice}");
        for (var back = 0; back < 4; back++) MainMenuCommand(() => { menu.Back(); return AreaMenuCommand.Nothing; });
        require(menu.Window == AreaMenuWindow.Main, "back at the main window for the rest of the stage");
        GD.Print("ARC-DEV-381 native-smoke early-menu PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            OpenedMsec = _earlyMenuRecord!.OpenedMsec, AttachedMsec = _earlyMenuRecord.AttachedMsec }));
    }

    /// <summary>ARC-DEV-387 (the <c>menu</c> stage, after a game was saved on the start map): Last Save pressed while no scene stands loads the
    /// save's own map and the save into it: the menu stays saying so and takes no input, the map is found by the save's runtime number, the scene
    /// is published and the save's session is the running one, with the menu gone. The "no scene" is the real one: the load clears the shown scene.</summary>
    private async Task LastSaveBeforeSceneForSmoke(Action<bool, string> require, Action<int> choose, AreaMainMenuTexts texts)
    {
        OpenMainMenu(texts);
        require(MainMenuOpen && _mainMenu is not null, "the menu opened for the save's load");
        var before = _areaSession;
        var hero = before!.QueryHero()!.Prototype;
        var map = CurrentAreaMap;
        _menuAwaitsScene = true; // the menu stands as it did before any scene (the scene itself goes when the load begins)
        var loadScreens = _transitionLoads;
        choose(10); choose(52);
        // ARC-UI-1362: the screen is black with the clock from the choice (no wait note on the menu).
        require(_menuLoadingSave && _sceneFlow is { Kind: SceneFlowKind.LoadSave } && MainMenuOpen && _transitionScreen.Loading && _transitionScreen.Alpha >= 1 && string.IsNullOrEmpty(_mainMenu!.Notice),
            $"Last Save is loading the save's own map under the load screen: loading {_menuLoadingSave} flow {_sceneFlow?.Kind} screen {_transitionScreen.Loading} {_transitionScreen.Alpha} note {_mainMenu?.Notice}");
        choose(50);
        require(_menuLoadingSave && _mainMenu!.Window == AreaMenuWindow.SinglePlayer, "the loading menu takes no other input");
        for (var i = 0; i < 6000 && (_menuLoadingSave || _sceneFlow is not null || MainMenuOpen); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        require(!MainMenuOpen && _mainMenu is null && !_menuLoadingSave && !_menuAwaitsScene && _sceneFlow is null,
            $"the save's load ended and the menu went: open {MainMenuOpen} loading {_menuLoadingSave} flow {_sceneFlow?.Kind} status {_quickSaveStatus}");
        require(_transitionLoads == loadScreens + 1 && !_transitionScreen.Covering && _transitionPanel is { Visible: false },
            $"the load screen showed once and went with the save's load: loads {_transitionLoads - loadScreens} covering {_transitionScreen.Covering}");
        require(_areaSession is { } loaded && !ReferenceEquals(loaded, before) && loaded.QueryHero()?.Prototype == hero && CurrentAreaMap == map && QuickKeysActive,
            $"the save's session is the running one on its own map: {_quickSaveStatus} map {CurrentAreaMap} (was {map})");
        GD.Print("ARC-DEV-387 native-smoke last-save-before-scene PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = map, Status = _quickSaveStatus }));
    }
}
