using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Reputation;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-UI-011: the main menu over the AREA scene (exterior or map mode) once it is ready, with --main-menu true. The scene's session
// is the placeholder the original also has before its menu; the menu replaces it: "Pick Character" starts a new game as the browsed
// pre-made hero (a session made as a fresh launch makes it, on the start map's spawn), "Last Save" loads the newest save, "Load Game"
// opens the save list's load window with the menu waiting under it, "Exit Game" quits. The menu is modal like the logbook (gate
// closed, clock paused) and owns the keys: a button's hotkey letter chooses it, Escape goes back (ledger MM1–MM7). It opens once a
// run; a new game or a load closes it.
// ARC-UI-012 scene flow (ledger SF1–SF5): in play, Escape opens the same menu at the original's in-play window (Save Game, Load
// Game, Options, Quit, Continue; the options window is ARC-UI-013, TerrainViewerAreaOptions.cs). Quit asks first and then ends the game as the original resets and reloads its module:
// the start scene loads again from the launch arguments (the start map, a fresh session without a hero, no --sector) and the main
// menu opens over it, its root Escape now exiting the game (MM6). A new game picked on another scene (another map, the cave, a
// --sector start, a session a transfer or load brought) loads the start scene the same way and starts there. Saves are not touched.
public partial class TerrainViewerApp
{
    /// <summary>ARC-UI-012: what the start scene does once it is published: open the main menu again, or start a new game as a hero.</summary>
    private enum SceneFlowKind { Menu, NewGame }
    private sealed record SceneFlowRequest(SceneFlowKind Kind, int Hero, string? Map);
    /// <summary>What a new game needs to make its session as this scene's launch made it.</summary>
    private sealed record NewGameRecipe(ProfileBinding Clock, Guid Module, AreaStepCell Spawn, int RuntimeMap, ReputationCatalog? Reputations);

    private AreaMainMenuPanel? _mainMenuPanel;
    private AreaMainMenu? _mainMenu;
    private NewGameRecipe? _newGameRecipe;
    private bool _mainMenuShown;
    private string? _mainMenuStatus;
    private readonly Dictionary<int, AreaHeroLoad> _mainMenuHeroes = [];
    private Func<AreaPremadeHero, AreaHeroLoad>? _heroLoaderForSelfTest;
    private Action? _mainMenuExitForSelfTest;
    /// <summary>The pre-made hero whose tables the menu adopted last (null: none).</summary>
    private AreaPremadeHero? _menuTablesHero;
    /// <summary>ARC-UI-012: the arguments the run was launched with (the start scene is loaded from them).</summary>
    private TerrainViewerArguments? _launchArguments;
    private SceneFlowRequest? _sceneFlow;
    private AreaMainMenuTexts? _mainMenuTexts;
    private Func<TerrainViewerArguments, Task>? _sceneLoadForSelfTest;
    private bool MainMenuOpen => _mainMenuPanel?.IsOpen == true;

    /// <summary>After a scene is published: opens the menu the first time, when asked for and the scene has a session.</summary>
    private void MaybeOpenMainMenu(TerrainViewerArguments arguments)
    {
        // ARC-UI-012: the start scene a quit or a new game elsewhere asked for opens the menu again or starts the game.
        if (_sceneFlow is { } flow && string.Equals(flow.Map, arguments.AreaMapName, StringComparison.Ordinal))
        {
            // The start scene's own first opening is this one: a later publish (a window move, a transfer, a reload) opens none.
            _sceneFlow = null; _mainMenuShown = true;
            ContinueSceneFlow(flow);
            return;
        }
        if (!arguments.MainMenu || _mainMenuShown) return;
        // Only the first published scene: a map without a session (unlit without a clock, or not in the map list) shows no menu,
        // and a later transfer does not open it in the middle of play.
        _mainMenuShown = true;
        if (!QuickKeysActive) { _mainMenuStatus = "not opened: the first scene has no game session"; GD.Print("ARC-UI-011 main menu not opened: no game session"); return; }
        OpenMainMenu(MainMenuTexts(arguments));
    }

    /// <summary>The menu's texts, read once a run (the game's MainMenu.mes, or the project's with the reason).</summary>
    private AreaMainMenuTexts MainMenuTexts(TerrainViewerArguments arguments)
    {
        if (_mainMenuTexts is { } read) return read;
        var texts = SelfTestBootstrap ? AreaMainMenuTexts.Project("self-test: no game folder") : AreaMainMenuTexts.Load(arguments.ModulePath);
        GD.Print($"ARC-UI-011 main menu texts fromGame={texts.FromGame} detail={texts.Detail}");
        return _mainMenuTexts = texts;
    }

    private void OpenMainMenu(AreaMainMenuTexts texts, AreaMenuWindow root = AreaMenuWindow.Main, bool reopened = false)
    {
        // A menu waiting under the save list (or for its window to open) is not replaced.
        if (!QuickKeysActive || MainMenuOpen || WorldModal || _mainMenu is not null) return;
        if (_mainMenuPanel is null)
        {
            _mainMenuPanel = new AreaMainMenuPanel(); AddChild(_mainMenuPanel);
            _mainMenuPanel.ButtonPressed += index => MainMenuCommand(() => _mainMenu!.Activate(index));
            _mainMenuPanel.BrowseRequested += direction => MainMenuCommand(() => { _mainMenu!.Browse(direction); return AreaMenuCommand.Nothing; });
            _mainMenuPanel.StartRequested += () => MainMenuCommand(() => _mainMenu!.Start());
            _mainMenuPanel.BackRequested += () => MainMenuCommand(() => { _mainMenu!.Back(); return AreaMenuCommand.Nothing; });
            _mainMenuPanel.AnswerRequested += yes => MainMenuCommand(() => _mainMenu!.Answer(yes));
            _mainMenuPanel.OptionChangeRequested += (index, direction) => MainMenuCommand(() => _mainMenu!.Change(index, direction));
        }
        // A key held across the menu must not act on its release afterwards (its press predates the menu).
        QuarantineWorld(); _quickKeys.Reset();
        StopWorldRoute("MAIN_MENU");
        _mainMenu = new AreaMainMenu(texts, root == AreaMenuWindow.Main ? NewGameRefusal() : null, root, reopened);
        _mainMenuStatus = null;
        GD.Print($"ARC-UI-011 main menu open window={root} reopened={reopened} newGame={_mainMenu.NewGameRefusal ?? "ready"}");
        RenderMainMenu();
        RefreshReadyStatus();
    }

    /// <summary>Why a new game cannot start: null when it starts on the shown scene or on the start scene loaded again (ARC-UI-012).</summary>
    private string? NewGameRefusal() => StartSceneRefusal() is null ? null : StartSceneArguments(out var why) is null ? why : null;

    /// <summary>Why the shown scene is not the start scene a new game starts on, or null. The original's new game puts the PC on the
    /// start map (runtime map 1) at its map-list location; the shown map must be it, with the session this scene's launch made.</summary>
    private string? StartSceneRefusal()
    {
        if (_readyArea is null) return "no scene";
        // The exterior mode shows the start map's Crash Site; map mode shows it when the map is runtime map 1.
        if (InCave) return "the shown map is the Crash Site cave";
        if (_readyMapName is { } map && CurrentAreaMap != 1) return $"the shown map is {map}";
        if (_newGameRecipe is null) return "this scene's session came from a transfer or a load, not from its start";
        // --sector opens the map around another sector, so the scene's start cell is not the map list's start.
        if (_arguments?.AreaMapSector is not null) return "--sector moved the scene's start away from the map list's start";
        return null;
    }

    /// <summary>ARC-UI-012: the start scene as the launch makes it: the launch arguments on the start map (the exterior, or the map
    /// list's runtime map 1 in map mode), at its map-list start (no --sector, no arrival), without a hero (the menu picks it) and under
    /// the default data rules; null with the reason when the start map is not known here.</summary>
    private TerrainViewerArguments? StartSceneArguments(out string? why)
    {
        why = null;
        if (_launchArguments is not { } launch) { why = "the launch arguments are not known"; return null; }
        launch = launch with { Hero = null, AreaMapArrival = null, AreaMapSector = null, DataRules = null };
        if (launch.AreaMapName is null) return launch;
        if (CurrentAreaMap == 1 && _readyMapName is { } shown) return launch with { AreaMapName = shown };
        if (_mapTransfers?.Destinations.TryGetValue(1, out var start) == true) return launch with { AreaMapName = start.Name };
        why = "the map list's start map (runtime map 1) is not known on the shown map";
        return null;
    }

    /// <summary>ARC-UI-012: loads the start scene (the shown game ends with its scene; saves are not touched) and then runs
    /// <paramref name="kind"/> on it. Null when the load started, else why nothing changed.</summary>
    private string? BeginSceneFlow(SceneFlowKind kind, int hero, string reason)
    {
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) return "a reset or reload is in progress";
        if (_mapHandoff is { Kind: not MapHandoffKind.Stream }) return "another map is loading";
        if (StartSceneArguments(out var why) is not { } next) return why;
        _sceneFlow = new(kind, hero, next.AreaMapName);
        GD.Print("ARC-UI-012 scene flow " + System.Text.Json.JsonSerializer.Serialize(new {
            Kind = kind.ToString(), Hero = hero, From = _readyMapName ?? (InCave ? "cave" : "exterior"), To = next.AreaMapName ?? "exterior", Reason = reason }));
        EndMainMenu(reason);
        StopWorldRoute("SCENE_FLOW");
        _menuTablesHero = null;
        _lastLoad = LoadSceneFlowAsync(next);
        return null;
    }

    private async Task LoadSceneFlowAsync(TerrainViewerArguments next)
    {
        var flow = _sceneFlow;
        _arguments = next; _lastPublishedFailure = null;
        if (_sceneLoadForSelfTest is { } load) await load(next);
        else await StartLoadAsync(next, false);
        // The start scene did not publish (a failure stays on screen with its stage): nothing runs on a later scene.
        if (flow is not null && ReferenceEquals(_sceneFlow, flow))
        {
            _sceneFlow = null;
            GD.Print("ARC-UI-012 scene flow: the start scene was not published " + (_lastPublishedFailure ?? "(superseded)"));
        }
    }

    /// <summary>The start scene is published: the main menu opens again over it, or the new game starts.</summary>
    private void ContinueSceneFlow(SceneFlowRequest flow)
    {
        if (!QuickKeysActive || _arguments is null) { GD.Print("ARC-UI-012 scene flow: the start scene has no game session"); return; }
        var texts = MainMenuTexts(_arguments);
        string? refused = null;
        if (flow.Kind == SceneFlowKind.NewGame)
        {
            // The start scene is the one a new game starts on; it never loads the start scene again.
            refused = StartNewGame(flow.Hero, sceneFlow: false);
            if (refused is null)
            {
                _mainMenuStatus = "new game as " + AreaStartingHero.Premade[flow.Hero].Name + " on the start scene";
                RefreshReadyStatus();
                return;
            }
        }
        // A quit, or a new game the start scene refused: the main menu, opened again (its root Escape exits the game, MM6).
        OpenMainMenu(texts, AreaMenuWindow.Main, reopened: true);
        if (_mainMenu is not null && refused is not null) { _mainMenu.Notice = refused; RenderMainMenu(); }
    }

    private void RenderMainMenu()
    {
        if (_mainMenu is null || _mainMenuPanel is null) return;
        BindOptionsWindow();
        _mainMenuPanel.Render(_mainMenu, _mainMenu.Window == AreaMenuWindow.PickCharacter ? HeroDescription(_mainMenu.Hero) : "");
    }

    /// <summary>The browsed hero as the browser shows it (no original text: the project's name and the loaded start).</summary>
    private string HeroDescription(int index)
    {
        var load = MainMenuHero(index);
        var name = AreaStartingHero.Premade[index].Name;
        if (load.Status != AreaHeroStatus.Ready || load.Definition is not { } hero) return $"{index + 1}/{AreaStartingHero.Premade.Count} {name}: cannot start ({load.Status} {load.Detail})";
        return $"{index + 1}/{AreaStartingHero.Premade.Count} {name}\nprototype {hero.Prototype}, {hero.Race} {hero.Gender}, level {hero.Level} "
            + $"({hero.UnspentPoints} points to spend by its scheme)\ngold {hero.Gold}, starting kit {hero.Kit.Count} items";
    }

    /// <summary>A pre-made hero read once a run (as the launch's --hero reads it; cheap), kept for the browser and the start.</summary>
    private AreaHeroLoad MainMenuHero(int index)
    {
        if (_mainMenuHeroes.TryGetValue(index, out var cached)) return cached;
        var premade = AreaStartingHero.Premade[index];
        var load = _heroLoaderForSelfTest?.Invoke(premade) ?? AreaStartingHero.Load(_arguments!.ModulePath, premade);
        GD.Print($"ARC-UI-011 main menu hero name={premade.Name} status={load.Status} detail={load.Detail}");
        _mainMenuHeroes[index] = load;
        return load;
    }

    /// <summary>Runs one step of the menu and then the command it returns.</summary>
    private void MainMenuCommand(Func<AreaMenuCommand> step)
    {
        if (!MainMenuOpen || _mainMenu is null) return;
        var command = step();
        switch (command.Kind)
        {
            case AreaMenuCommandKind.LoadGame:
                // The load window opens over the menu; the menu waits (hidden) and comes back when the window closes unused.
                _mainMenuPanel!.CloseView();
                OpenSaveList(AreaSaveListMode.Load);
                if (!SaveListOpen) { _mainMenu.Resume("the save list could not open: " + _saveListStatus); RenderMainMenu(); }
                return;
            case AreaMenuCommandKind.LastSave:
                if (SaveLibrary.Latest() is not { } latest)
                {
                    // The original goes back to this window without a message (MM4).
                    _mainMenu.NoSave();
                    GD.Print("ARC-UI-011 main menu last save: none");
                    break;
                }
                if (LoadGame(SaveLibrary.Slot(latest.Name), "Last save ", MainMenuTables())) { EndMainMenu("last save " + latest.Name); return; }
                _mainMenu.Notice = _quickSaveStatus;
                break;
            case AreaMenuCommandKind.StartNewGame:
                if (StartNewGame(command.Hero) is { } refused) _mainMenu.Notice = refused;
                else if (_mainMenu is not null) { EndMainMenu("new game as " + AreaStartingHero.Premade[command.Hero].Name); return; }
                else return; // the start scene loads first (ARC-UI-012)
                break;
            case AreaMenuCommandKind.SaveGame:
                // ARC-UI-012: the save window opens over the waiting in-play menu once the game is drawn without it (the preview).
                _mainMenuPanel!.CloseView(); QuarantineWorld();
                OpenSaveListAfterDraw(AreaSaveListMode.Save);
                return;
            case AreaMenuCommandKind.Continue:
                EndMainMenu("continue");
                return;
            case AreaMenuCommandKind.ChangeOption:
                // ARC-UI-013: the setting changes at once (OP3); a refusal keeps it and names why.
                if (ApplyOptionChange(command.Option, command.Value) is { } optionRefused && _mainMenu is not null) _mainMenu.Notice = optionRefused;
                if (_mainMenu is null) return;
                break;
            case AreaMenuCommandKind.QuitToMenu:
                if (BeginSceneFlow(SceneFlowKind.Menu, -1, "quit to the main menu") is { } quitRefused) _mainMenu.Notice = "quit refused: " + quitRefused;
                else return;
                break;
            case AreaMenuCommandKind.Exit:
                GD.Print("ARC-UI-011 main menu exit");
                if (_mainMenuExitForSelfTest is { } exit) { exit(); return; }
                GetTree().Quit(0);
                return;
        }
        RenderMainMenu();
        RefreshAreaStatus();
    }

    /// <summary>A new game as pre-made hero <paramref name="index"/>: a session made as this scene's launch made it (its clock, module,
    /// start cell and reputations) with that hero, adopted as a load adopts its session. Null on success, else why nothing changed.</summary>
    /// <param name="sceneFlow">ARC-UI-012: on another scene than the start scene, load the start scene and start there (null is
    /// returned once that load began); false refuses instead.</param>
    private string? StartNewGame(int index, bool sceneFlow = true)
    {
        var hero = MainMenuHero(index);
        if (hero.Status != AreaHeroStatus.Ready) return $"new game refused: the hero cannot be read ({hero.Status} {hero.Detail})";
        if (StartSceneRefusal() is { } elsewhere)
        {
            if (!sceneFlow) return "new game refused: " + elsewhere;
            return BeginSceneFlow(SceneFlowKind.NewGame, index, $"new game as {hero.Hero.Name}: {elsewhere}, the start scene loads first") is { } flowRefused
                ? $"new game refused: {elsewhere} and the start scene cannot load ({flowRefused})" : null;
        }
        if (_newGameRecipe is not { } recipe) return "new game refused: no recipe";
        var busy = QuickBusyReason();
        if (busy is QuickBusy.NoActor or QuickBusy.Lifecycle or QuickBusy.MapLoading) return "new game refused: " + Describe(busy);
        var created = AreaTransitionCoordinator.CreateSession(recipe.Clock, Guid.NewGuid(), recipe.Module, recipe.Spawn, recipe.RuntimeMap, hero, recipe.Reputations);
        if (created.Owner is not { } owner) return $"new game refused: the session was not created ({created.Failure})";
        // As a load: the maps' inventory seeds and the dialog host move to the new session before anything changes.
        if (_inventoryHost?.Attach(owner) is { } attached && attached != GameSessionFailure.None)
        {
            owner.Dispose();
            return $"new game refused: the inventory seeds do not attach ({attached})";
        }
        AreaConversationHost? talk = null;
        try { talk = _conversationHost?.Rebind(owner); }
        catch (ArgumentException)
        {
            owner.Dispose();
            return "new game refused: the dialog state is not readable";
        }
        AdoptHeroTables(hero);
        AdoptLoadedSession(owner, AreaRuntimes(), talk, "new game");
        SetQuickStatus("new game as " + hero.Hero.Name);
        GD.Print("ARC-UI-011 new game " + System.Text.Json.JsonSerializer.Serialize(new {
            hero.Hero.Name, hero.Definition!.Prototype, hero.Flag, Map = recipe.RuntimeMap, Cell = recipe.Spawn, Revision = owner.Version.Revision }));
        return null;
    }

    /// <summary>The hero tables of a session the menu brings: its effect rules and kit facts. The scene's arguments name the hero
    /// too, so a map transfer reads it again with the map (as with --hero) instead of losing them.</summary>
    private void AdoptHeroTables(AreaHeroLoad? hero)
    {
        _areaEffectRules = hero?.Effects;
        _areaKitHealing = hero?.KitHealing;
        _areaKitLockpicks = hero?.KitLockpicks;
        _areaKitDurability = hero?.KitDurability;
        _areaKitWear = hero?.KitWear;
        _areaKitArt = hero?.KitArt;
        _areaKitMissileArt = hero?.KitMissileArt;
        _areaKitThrowWeight = hero?.KitThrowWeight;
        _areaKitItemSpells = hero?.KitItemSpells;
        _menuTablesHero = hero?.Hero;
        if (_arguments is not null) _arguments = _arguments with { Hero = hero?.Hero };
    }

    /// <summary>The game-wide tables a hero save loaded from the menu takes (the experience and auto-level data are the same for
    /// every pre-made hero): the first hero's read, or null when it does not read.</summary>
    private AreaHeroLoad? MainMenuTables() => MainMenuHero(0) is { Status: AreaHeroStatus.Ready } ready ? ready : null;

    /// <summary>The pre-made hero a loaded session holds (by its prototype), read as the browser reads it; null for none or another PC.</summary>
    private AreaHeroLoad? LoadedMenuHero(GameSessionOwner loaded)
    {
        if (loaded.QueryHero() is not { } hero) return null;
        for (var i = 0; i < AreaStartingHero.Premade.Count; i++)
            if (AreaStartingHero.Premade[i].BasicPrototype + Arcanum.Data.Characters.PremadeHeroReader.PrototypeOffset == hero.Prototype)
                return MainMenuHero(i) is { Status: AreaHeroStatus.Ready } ready ? ready : null;
        return null;
    }

    /// <summary>The scene ends under the menu (a load of a save on another map tears it down): the menu ends with it, so the save
    /// list closing in the teardown does not bring it back.</summary>
    private void EndMainMenuForTeardown()
    {
        if (_mainMenu is null) return;
        GD.Print("ARC-UI-011 main menu closed: the scene ended (another map loads)");
        _mainMenuPanel?.CloseView(); _mainMenu = null; _mainMenuStatus = "closed: another map loads";
    }

    private void EndMainMenu(string reason)
    {
        if (_mainMenu is null) return;
        GD.Print("ARC-UI-011 main menu closed: " + reason);
        _mainMenuPanel?.CloseView(); _mainMenu = null; _mainMenuStatus = reason;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>The save list closed while the menu waited under it: a load or a save ends the menu (a save as the original's,
    /// ledger SF4), otherwise it comes back.</summary>
    private void SaveListClosedUnderMainMenu(bool finished)
    {
        if (_mainMenu is null || MainMenuOpen || !QuickKeysActive) return;
        if (finished) { EndMainMenu("finished in the save list"); return; }
        _mainMenu.Resume(null);
        QuarantineWorld(); _quickKeys.Reset();
        RenderMainMenu(); RefreshReadyStatus();
    }

    /// <summary>Per frame: a session that is gone (a reset, map mode left) ends the menu.</summary>
    private void SyncMainMenu()
    {
        if (_mainMenu is not null && !QuickKeysActive) { _mainMenuPanel?.CloseView(); _mainMenu = null; }
    }

    /// <summary>While open the menu owns the keys: Escape goes back (or continues play, exits from a reopened main menu, answers
    /// the quit box no), a typed letter chooses its button (MM6, SF2, SF3). ARC-UI-013: in the options window Up and Down move the
    /// focus, Left and Right change the focused control, and the O key closes the menu from play (OP7).</summary>
    private void HandleMainMenuKey(InputEventKey key)
    {
        if (!key.Pressed || key.Echo || _mainMenu is null) return;
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (routed == Key.Escape) { MainMenuCommand(() => _mainMenu.Escape()); return; }
        // The quit box takes Return as yes, as the original's yes/no box (SF3).
        if (routed is Key.Enter or Key.KpEnter && _mainMenu.QuitPending) { MainMenuCommand(() => _mainMenu.Answer(true)); return; }
        if (_mainMenu.Window == AreaMenuWindow.Options && !_mainMenu.QuitPending)
        {
            // The original's key table reads the O key's scan code here, not the typed letter.
            if (key.PhysicalKeycode == Key.O) { MainMenuCommand(() => _mainMenu.OptionsKey()); return; }
            // The keyboard moves the focus over the controls and buttons as a controller does (the original has no such keys).
            if (routed is Key.Up or Key.Down) { MainMenuCommand(() => { _mainMenu.Move(routed == Key.Up ? -1 : 1); return AreaMenuCommand.Nothing; }); return; }
            if (routed is Key.Left or Key.Right)
            {
                var direction = routed == Key.Left ? -1 : 1;
                MainMenuCommand(() => _mainMenu.Change(_mainMenu.OptionsFocus, direction));
                return;
            }
        }
        if (key.Unicode is > 0 and < 0x10000 && !char.IsControl((char)key.Unicode) && _mainMenu.Window != AreaMenuWindow.PickCharacter)
            MainMenuCommand(() => _mainMenu.Type((char)key.Unicode));
    }

    private void HandleMainMenuPulse(IntentFrame intent)
    {
        if (_mainMenu is null) return;
        if (intent.Cancel) MainMenuCommand(() => _mainMenu.Escape());
        else if (intent.Confirm) MainMenuCommand(() => _mainMenu.Confirm());
        else if (intent.MenuPressedDirection != 0)
            MainMenuCommand(() =>
            {
                if (_mainMenu.Window == AreaMenuWindow.PickCharacter) _mainMenu.Browse(intent.MenuPressedDirection); else _mainMenu.Move(intent.MenuPressedDirection);
                return AreaMenuCommand.Nothing;
            });
    }

    /// <summary>ARC-UI-012: Escape in play opens the in-play menu (the original's window 3, or window 4 in a turn-based round on
    /// another's turn; ledger SF1). A press the palette, a dialog or UI focus owns does nothing.</summary>
    private bool RouteGameMenuKey(WorldKeyDecision decision)
    {
        if (!QuickKeysActive || decision.Key != WorldKey.Escape) return false;
        // The palette's own Escape closes it (its pulse); a menu waiting for the save window to open takes no second one.
        if (PaletteOpen) return false;
        if (!decision.Pressed || decision.Echo || _mainMenu is not null) return true;
        if (!_worldFocused || WorldModal || WorldUiFocus) { _mainMenuStatus = "Escape ignored: palette, dialog or UI focus owns input"; RefreshAreaStatus(); return true; }
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending || _mapHandoff is { Kind: not MapHandoffKind.Stream })
        {
            _mainMenuStatus = "Escape ignored: a load is in progress"; RefreshAreaStatus(); return true;
        }
        var limited = _areaSession!.QueryCombatRound() is { } round && InTurnBasedRound && !round.Actor.IsHero;
        OpenMainMenu(MainMenuTexts(_arguments ?? _launchArguments!), limited ? AreaMenuWindow.InPlayLimited : AreaMenuWindow.InPlay);
        return true;
    }

    private string AreaMainMenuStatus() => !QuickKeysActive ? "" : MainMenuOpen ? " | main menu open"
        : _mainMenuStatus is null ? "" : " | main menu: " + _mainMenuStatus;
}

public partial class TerrainViewerApp
{
    // Native observation (ARC_AREA_MAP_SMOKE_ONLY=menu, map mode on the start map with --main-menu true), on a temporary save folder:
    // the menu opened over the published map with the game's own texts; each step goes by the button's own hotkey letter through the
    // production key route. "Last Save" with no save stays; the browser reads the real pre-made heroes; Start begins a new game as
    // the first hero (its prototype, gold and main-menu flag in the new session); after F7, a reopened menu's "Last Save" loads it.
    private async Task RunAreaMainMenuSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-011 main menu smoke: " + why); }
        var directory = System.IO.Directory.CreateTempSubdirectory("arc-ui-011-").FullName;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        _saveLibraryForSelfTest = new(directory);
        try
        {
            void Choose(int message)
            {
                var button = _mainMenu!.Shown.FirstOrDefault(shown => shown.Message == message) ?? throw new InvalidOperationException("ARC-UI-011 main menu smoke: no button " + message);
                var letter = _mainMenu.Texts.Hotkey(button) ?? throw new InvalidOperationException("ARC-UI-011 main menu smoke: no letter for " + message);
                var key = letter is >= 'A' and <= 'Z' ? (Key)letter : Key.None;
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = char.ToLowerInvariant(letter), Pressed = true });
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = char.ToLowerInvariant(letter), Pressed = false });
            }
            Require(_arguments?.MainMenu == true && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main, "the menu is open over the map (run with --main-menu true)");
            var texts = _mainMenu!.Texts;
            Require(texts.FromGame && texts.Text(10).Length > 0 && texts.Text(52).Length > 0, "the texts come from the game: " + texts.Detail);
            Require(_mainMenu.NewGameRefusal is null, "a new game can start on this map: " + _mainMenu.NewGameRefusal);
            var placeholder = _areaSession;
            Choose(10);
            Require(_mainMenu.Window == AreaMenuWindow.SinglePlayer, "single player by its letter");
            Choose(52);
            Require(MainMenuOpen && ReferenceEquals(placeholder, _areaSession) && _mainMenu.Notice == "no save to continue from", "Last Save without a save stayed");
            Choose(50); Choose(420);
            Require(_mainMenu.Window == AreaMenuWindow.PickCharacter, "the pre-made browser opened");
            var first = MainMenuHero(0);
            var definition = first.Definition ?? throw new InvalidOperationException("ARC-UI-011 main menu smoke: the first hero did not read: " + first.Detail);
            Require(first.Status == AreaHeroStatus.Ready && _mainMenuPanel!.HeroForSelfTest.StartsWith("1/12 halfling-male\n", StringComparison.Ordinal),
                "the first real hero reads: " + first.Status + " " + first.Detail);
            MainMenuCommand(() => { _mainMenu!.Browse(1); return AreaMenuCommand.Nothing; });
            Require(MainMenuHero(1).Status == AreaHeroStatus.Ready && _mainMenuPanel!.HeroForSelfTest.StartsWith("2/12 gnome-male\n", StringComparison.Ordinal), "Next read the second hero");
            MainMenuCommand(() => { _mainMenu!.Browse(-1); return AreaMenuCommand.Nothing; });
            MainMenuCommand(() => _mainMenu!.Start());
            var session = _areaSession!;
            var flags = session.Flags;
            var flag = flags.Read(new Arcanum.Scripting.GlobalFlags.GlobalFlagReadRequest(new(Guid.NewGuid()), flags.Version, new(first.Flag!.Value)));
            Require(!MainMenuOpen && !ReferenceEquals(placeholder, session) && session.QueryHero()?.Prototype == first.Definition!.Prototype
                && session.QueryGold() == first.Definition.Gold && flag.Value == true, $"the new game is the first hero's: {_quickSaveStatus} {session.QueryHero()?.Prototype} flag {flag.Status} {flag.Value}");
            for (var i = 0; i < 3; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            QuickKey(Key.F7);
            Require(_saveLibraryForSelfTest.AutoSlot.Exists, "F7 saved the new game: " + _quickSaveStatus);
            OpenMainMenu(texts);
            Choose(10); Choose(52);
            Require(!MainMenuOpen && !ReferenceEquals(session, _areaSession) && _areaSession!.QueryHero()?.Prototype == definition.Prototype
                && _quickSaveStatus?.StartsWith("Last save quick loaded", StringComparison.Ordinal) == true, "Last Save loaded the auto-save: " + _quickSaveStatus);
            Require(_areaSession!.Experience is not null && _areaSession.AutoLevel is not null && _areaEffectRules is not null && _areaKitHealing is not null
                && _arguments!.Hero?.Name == "halfling-male", "the hero's tables came with the new game and the load");
            for (var i = 0; i < 3; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            GD.Print("ARC-UI-011 native-smoke main menu PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Texts = texts.Detail, Map = _readyMapName, Hero = first.Hero.Name, definition.Prototype, Flag = first.Flag, definition.Gold,
                LastSave = "auto-save", Revision = _areaSession!.Version.Revision }));
        }
        finally
        {
            if (_mainMenu is not null) EndMainMenu("smoke end");
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }
}
