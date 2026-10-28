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
public partial class TerrainViewerApp
{
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
    private bool MainMenuOpen => _mainMenuPanel?.IsOpen == true;

    /// <summary>After a scene is published: opens the menu the first time, when asked for and the scene has a session.</summary>
    private void MaybeOpenMainMenu(TerrainViewerArguments arguments)
    {
        if (!arguments.MainMenu || _mainMenuShown || !QuickKeysActive) return;
        _mainMenuShown = true;
        var texts = SelfTestBootstrap ? AreaMainMenuTexts.Project("self-test: no game folder") : AreaMainMenuTexts.Load(arguments.ModulePath);
        GD.Print($"ARC-UI-011 main menu texts fromGame={texts.FromGame} detail={texts.Detail}");
        OpenMainMenu(texts);
    }

    private void OpenMainMenu(AreaMainMenuTexts texts)
    {
        if (!QuickKeysActive || MainMenuOpen || WorldModal) return;
        if (_mainMenuPanel is null)
        {
            _mainMenuPanel = new AreaMainMenuPanel(); AddChild(_mainMenuPanel);
            _mainMenuPanel.ButtonPressed += index => MainMenuCommand(() => _mainMenu!.Activate(index));
            _mainMenuPanel.BrowseRequested += direction => MainMenuCommand(() => { _mainMenu!.Browse(direction); return AreaMenuCommand.Nothing; });
            _mainMenuPanel.StartRequested += () => MainMenuCommand(() => _mainMenu!.Start());
            _mainMenuPanel.BackRequested += () => MainMenuCommand(() => { _mainMenu!.Back(); return AreaMenuCommand.Nothing; });
        }
        // A key held across the menu must not act on its release afterwards (its press predates the menu).
        QuarantineWorld(); _quickKeys.Reset();
        StopWorldRoute("MAIN_MENU");
        _mainMenu = new AreaMainMenu(texts, NewGameRefusal());
        _mainMenuStatus = null;
        GD.Print($"ARC-UI-011 main menu open newGame={_mainMenu.NewGameRefusal ?? "ready"}");
        RenderMainMenu();
        RefreshReadyStatus();
    }

    /// <summary>Why a new game cannot start on the shown scene, or null. The original's new game puts the PC on the start map
    /// (runtime map 1) at its map-list location; the shown map must be it, with the session this scene's launch made.</summary>
    private string? NewGameRefusal()
    {
        if (_readyArea is null) return "no scene";
        // The exterior mode shows the start map's Crash Site; map mode shows it when the map is runtime map 1.
        if (InCave) return "a new game starts on the start map; the shown map is the Crash Site cave (scene flow N8)";
        if (_readyMapName is { } map && CurrentAreaMap != 1) return $"a new game starts on the start map; the shown map is {map} (scene flow N8)";
        if (_newGameRecipe is null) return "this scene's session came from a transfer or a load, not from its start";
        return null;
    }

    private void RenderMainMenu()
    {
        if (_mainMenu is null || _mainMenuPanel is null) return;
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
                if (LoadGame(SaveLibrary.Slot(latest.Name), "Last save ")) { EndMainMenu("last save " + latest.Name); return; }
                _mainMenu.Notice = _quickSaveStatus;
                break;
            case AreaMenuCommandKind.StartNewGame:
                if (StartNewGame(command.Hero) is { } refused) _mainMenu.Notice = refused;
                else { EndMainMenu("new game as " + AreaStartingHero.Premade[command.Hero].Name); return; }
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
    private string? StartNewGame(int index)
    {
        if (NewGameRefusal() is { } refusal || _newGameRecipe is not { } recipe) return "new game refused: " + (NewGameRefusal() ?? "no recipe");
        var busy = QuickBusyReason();
        if (busy is QuickBusy.NoActor or QuickBusy.Lifecycle or QuickBusy.MapLoading) return "new game refused: " + Describe(busy);
        var hero = MainMenuHero(index);
        if (hero.Status != AreaHeroStatus.Ready) return $"new game refused: the hero cannot be read ({hero.Status} {hero.Detail})";
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
        _areaEffectRules = hero.Effects;
        _areaKitHealing = hero.KitHealing;
        _areaKitLockpicks = hero.KitLockpicks;
        _areaKitDurability = hero.KitDurability;
        _areaKitArt = hero.KitArt;
        AdoptLoadedSession(owner, AreaRuntimes(), talk, "new game");
        SetQuickStatus("new game as " + hero.Hero.Name);
        GD.Print("ARC-UI-011 new game " + System.Text.Json.JsonSerializer.Serialize(new {
            hero.Hero.Name, hero.Definition!.Prototype, hero.Flag, Map = recipe.RuntimeMap, Cell = recipe.Spawn, Revision = owner.Version.Revision }));
        return null;
    }

    private void EndMainMenu(string reason)
    {
        if (_mainMenu is null) return;
        GD.Print("ARC-UI-011 main menu closed: " + reason);
        _mainMenuPanel?.CloseView(); _mainMenu = null; _mainMenuStatus = reason;
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>The save list closed while the menu waited under it: a load ends the menu, otherwise it comes back.</summary>
    private void SaveListClosedUnderMainMenu(bool loaded)
    {
        if (_mainMenu is null || MainMenuOpen || !QuickKeysActive) return;
        if (loaded) { EndMainMenu("loaded from the save list"); return; }
        _mainMenu.Resume(null);
        QuarantineWorld(); _quickKeys.Reset();
        RenderMainMenu(); RefreshReadyStatus();
    }

    /// <summary>Per frame: a session that is gone (a reset, map mode left) ends the menu.</summary>
    private void SyncMainMenu()
    {
        if (_mainMenu is not null && !QuickKeysActive) { _mainMenuPanel?.CloseView(); _mainMenu = null; }
    }

    /// <summary>While open the menu owns the keys: Escape goes back, a typed letter chooses its button (MM6).</summary>
    private void HandleMainMenuKey(InputEventKey key)
    {
        if (!key.Pressed || key.Echo || _mainMenu is null) return;
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (routed == Key.Escape) { MainMenuCommand(() => { _mainMenu.Back(); return AreaMenuCommand.Nothing; }); return; }
        if (key.Unicode is > 0 and < 0x10000 && !char.IsControl((char)key.Unicode) && _mainMenu.Window != AreaMenuWindow.PickCharacter)
            MainMenuCommand(() => _mainMenu.Type((char)key.Unicode));
    }

    private void HandleMainMenuPulse(IntentFrame intent)
    {
        if (_mainMenu is null) return;
        if (intent.Cancel) MainMenuCommand(() => { _mainMenu.Back(); return AreaMenuCommand.Nothing; });
        else if (intent.Confirm) MainMenuCommand(() => _mainMenu.Confirm());
        else if (intent.MenuPressedDirection != 0)
            MainMenuCommand(() =>
            {
                if (_mainMenu.Window == AreaMenuWindow.PickCharacter) _mainMenu.Browse(intent.MenuPressedDirection); else _mainMenu.Move(intent.MenuPressedDirection);
                return AreaMenuCommand.Nothing;
            });
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
