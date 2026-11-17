using Arcanum.Data.Messages;
using Arcanum.Data.Traversal;
using Arcanum.Input;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Clock;
using Arcanum.Simulation.Reputation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
// ARC-UI-030 (N10b): "New Character" makes the new character from the generic prototype (read as the pre-made heroes are) and opens
// its window: the gender, race and background buttons change it under the rule setting (NW4), Accept starts a new game as it the
// same way a pre-made hero starts (its race and gender in the session's hero, its background set with its effect, no main-menu flag).
// ARC-UI-034: Accept now opens the character editor (window 14, TerrainViewerAreaCreationEditor.cs) on a draft session of the
// character; the editor's Accept starts the game with its presses replayed, its Cancel goes back to a new character.
public partial class TerrainViewerApp
{
    /// <summary>ARC-UI-012: what the start scene does once it is published: open the main menu again, or start a new game as a hero.</summary>
    /// <summary>ARC-DEV-387: <c>LoadSave</c> opens the scene a save from the menu was made on (the start map is not loaded for it) and loads the save into it.</summary>
    private enum SceneFlowKind { Menu, NewGame, LoadSave }
    private sealed record SceneFlowRequest(SceneFlowKind Kind, int Hero, string? Map, AreaNewCharacter? Creation = null, string? SaveName = null, string? SavePrefix = null);
    /// <summary>What a new game needs to make its session as this scene's launch made it.</summary>
    private sealed record NewGameRecipe(ProfileBinding Clock, Guid Module, AreaStepCell Spawn, int RuntimeMap, ReputationCatalog? Reputations);

    private AreaMainMenuPanel? _mainMenuPanel;
    private AreaMainMenu? _mainMenu;
    private NewGameRecipe? _newGameRecipe;
    private bool _mainMenuShown;
    private string? _mainMenuStatus;
    private readonly Dictionary<int, AreaHeroLoad> _mainMenuHeroes = [];
    private AreaHeroLoad? _mainMenuNewCharacter;
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
        // ARC-DEV-381: the menu opened before the scene was ready: it attaches to the scene now (and runs what it held).
        // (a save's map loading under the menu is not this attach's scene: only its own flow's publication counts)
        if (_menuLoadingSave) return;
        if (_menuAwaitsScene) { AttachEarlyMenuToScene(); return; }
        if (!arguments.MainMenu || _mainMenuShown) return;
        // Only the first published scene: a map without a session (unlit without a clock, or not in the map list) shows no menu,
        // and a later transfer does not open it in the middle of play.
        _mainMenuShown = true;
        if (!QuickKeysActive) { _mainMenuStatus = "not opened: the first scene has no game session"; GD.Print("ARC-UI-011 main menu not opened: no game session"); return; }
        OpenMainMenu(MainMenuTexts(arguments));
        Callable.From(SoakOpenWindow).CallDeferred();
    }

    /// <summary>The menu's texts, read once a run (the game's MainMenu.mes, or the project's with the reason).</summary>
    private AreaMainMenuTexts MainMenuTexts(TerrainViewerArguments arguments)
    {
        if (_mainMenuTexts is { } read) return read;
        var texts = SelfTestBootstrap ? AreaMainMenuTexts.Project("self-test: no game folder") : AreaMainMenuTexts.Load(arguments.ModulePath);
        GD.Print($"ARC-UI-011 main menu texts fromGame={texts.FromGame} detail={texts.Detail}");
        return _mainMenuTexts = texts;
    }

    private void OpenMainMenu(AreaMainMenuTexts texts, AreaMenuWindow root = AreaMenuWindow.Main, bool reopened = false, bool early = false)
    {
        // A menu waiting under the save list (or for its window to open) is not replaced.
        // ARC-UI-026: the death screen still covering the game is the one modal state the menu opens under (it fades in over it).
        // ARC-DEV-381: the early menu opens before the start scene has a session (see TerrainViewerAreaEarlyMenu.cs).
        if ((!QuickKeysActive && !early) || MainMenuOpen || (PanelOutsideStackOpen && !HeroDeathCovering) || _mainMenu is not null) return;
        // ARC-DEV-367: the call, then the first frame drawn with the menu (the start to the menu of the first opening).
        using var span = PerfProbe.Enabled ? PerfProbe.Span("menu.open." + root) : default;
        PerfProbe.Mark("menu.open");
        PerfMarkAfterDraw("menu.first-frame");
        if (_mainMenuPanel is null)
        {
            _mainMenuPanel = new AreaMainMenuPanel { DebugMode = DebugMode }; AddChild(_mainMenuPanel);
            _mainMenuPanel.ButtonPressed += index => MainMenuCommand(() => _mainMenu!.Activate(index));
            _mainMenuPanel.BrowseRequested += direction => MainMenuCommand(() => { _mainMenu!.Browse(direction); return AreaMenuCommand.Nothing; });
            _mainMenuPanel.StartRequested += () => MainMenuCommand(() => _mainMenu!.Start());
            _mainMenuPanel.BackRequested += () => MainMenuCommand(() => { _mainMenu!.Back(); return AreaMenuCommand.Nothing; }, cancels: true);
            _mainMenuPanel.AnswerRequested += yes => MainMenuCommand(() => _mainMenu!.Answer(yes));
            _mainMenuPanel.OptionChangeRequested += (index, direction) => MainMenuCommand(() => _mainMenu!.Change(index, direction));
            _mainMenuPanel.SoundRequested += id => PlayBarSound(id, "menu button");
            _mainMenuPanel.CreationPressed += button => MainMenuCommand(() => { _mainMenu!.PressCreation(button, CreationRules()); return AreaMenuCommand.Nothing; });
            _mainMenuPanel.AcceptRequested += () => MainMenuCommand(() => _mainMenu!.Accept());
            _mainMenuPanel.NameClicked += () => MainMenuCommand(() => { _mainMenu!.BeginName(); return AreaMenuCommand.Nothing; });
            // ARC-UI-068: a press beside the name field ends its typing (NW8); without typing nothing changes and nothing is redrawn.
            _mainMenuPanel.PressedOutsideName += () =>
            {
                if (_mainMenu?.Creation is { TypingName: true }) MainMenuCommand(() => { _mainMenu!.PressOutsideName(); return AreaMenuCommand.Nothing; });
            };
        }
        AttachMenuArt(_mainMenuPanel);
        AttachMenuDialogArt(_mainMenuPanel);
        AttachPremadeBrowserArt(_mainMenuPanel);
        // A key held across the menu must not act on its release afterwards (its press predates the menu).
        QuarantineWorld(); _quickKeys.Reset();
        StopWorldRoute("MAIN_MENU");
        // ARC-DEV-111: the menu opens over a window that stays open under it; its layer is the last child's, so it draws on top.
        if (_mainMenuPanel.GetParent() == this) MoveChild(_mainMenuPanel, GetChildCount() - 1);
        _mainMenu = new AreaMainMenu(texts, root == AreaMenuWindow.Main && !early ? NewGameRefusal() : null, root, reopened);
        _mainMenu.IntroRefusal = MovieRefusal();
        _mainMenu.CreditsRefusal = CreditsRefusal();
        _mainMenuStatus = null;
        GD.Print($"ARC-UI-011 main menu open window={root} reopened={reopened} newGame={_mainMenu.NewGameRefusal ?? "ready"}");
        // ARC-UI-139: the main menu's soundtrack (the shop map's sector pair, ARC-DEV-332) plays through the scheme player without a session or a
        // scene, from the moment the menu opens until it ends; the in-play menu keeps the game's music.
        if (root == AreaMenuWindow.Main) StartMenuMusic();
        RenderMainMenu();
        RefreshReadyStatus();
        // ARC-RENDER-1242 (0x4020F0, 0x401000): the start-up movies play before the first menu is used; the menu stands under them (the game build only).
        if (root == AreaMenuWindow.Main && !reopened) PlayStartupMovies();
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
    private string? BeginSceneFlow(SceneFlowKind kind, int hero, string reason, AreaNewCharacter? creation = null)
    {
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending) return "a reset or reload is in progress";
        if (_mapHandoff is { Kind: not MapHandoffKind.Stream }) return "another map is loading";
        // ARC-UI-026: a quit and a death (or two deaths) never load the start scene twice at once.
        if (_sceneFlow is not null) return "the start scene is loading";
        if (StartSceneArguments(out var why) is not { } next) return why;
        _sceneFlow = new(kind, hero, next.AreaMapName, creation);
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
        try
        {
            if (_sceneLoadForSelfTest is { } load) await load(next);
            else await StartLoadAsync(next, false);
        }
        finally
        {
            // The start scene did not publish (a failure stays on screen with its stage, or the load threw): nothing runs on a later
            // scene, and the next quit, new game or death may load it again.
            if (flow is not null && ReferenceEquals(_sceneFlow, flow))
            {
                _sceneFlow = null;
                // ARC-DEV-387: a save's map that did not publish (superseded or failed) gives the menu its input back.
                if (flow.Kind == SceneFlowKind.LoadSave && _menuLoadingSave)
                {
                    _menuLoadingSave = false;
                    ClearTransition(); // ARC-UI-1362
                    if (_mainMenu is { } waiting) { waiting.Notice = "the save's map did not load"; RenderMainMenu(); }
                }
                // ARC-UI-038: the opening shop's session the flow carried goes with it.
                flow.Creation?.DropShop();
                GD.Print("ARC-UI-012 scene flow: the start scene was not published " + (_lastPublishedFailure ?? "(superseded)"));
            }
            // ARC-UI-026: a death's black screen gives way to whatever is shown now (the reopened menu fades in already; a failure, or
            // a load another one superseded, is not left under black).
            HeroDeathSceneReady();
        }
    }

    /// <summary>The start scene is published: the main menu opens again over it, or the new game starts.</summary>
    private void ContinueSceneFlow(SceneFlowRequest flow)
    {
        // ARC-DEV-387: the scene a save from the menu was made on: the save loads into it.
        if (flow.Kind == SceneFlowKind.LoadSave) { FinishSaveSceneFlow(flow); return; }
        if (!QuickKeysActive || _arguments is null)
        {
            GD.Print("ARC-UI-012 scene flow: the start scene has no game session");
            // ARC-UI-038: the opening shop's session the flow carried goes with it.
            flow.Creation?.DropShop();
            HeroDeathSceneReady();
            return;
        }
        var texts = MainMenuTexts(_arguments);
        string? refused = null;
        if (flow.Kind == SceneFlowKind.NewGame)
        {
            // The start scene is the one a new game starts on; it never loads the start scene again.
            refused = StartNewGame(flow.Hero, sceneFlow: false, flow.Creation);
            if (refused is null)
            {
                _mainMenuStatus = "new game as " + (flow.Creation is { } creation ? CreationName(creation) : AreaStartingHero.Premade[flow.Hero].Name) + " on the start scene";
                RefreshReadyStatus();
                return;
            }
            // ARC-UI-038: a refused start drops the opening shop's session the flow carried.
            flow.Creation?.DropShop();
        }
        // A quit, or a new game the start scene refused: the main menu, opened again (its root Escape exits the game, MM6).
        OpenMainMenu(texts, AreaMenuWindow.Main, reopened: true);
        if (_mainMenu is not null && refused is not null) { _mainMenu.Notice = refused; RenderMainMenu(); }
        // ARC-UI-026: after a death the main menu fades in from black (PD8).
        HeroDeathSceneReady();
    }

    private void RenderMainMenu()
    {
        // ARC-UI-038: the opening shop's barter window goes with the shop's session (its Back, the start, the menu's end).
        if (_barterInShop && !ShopBarterActive) EndBarter();
        if (_mainMenu is null || _mainMenuPanel is null) return;
        BindOptionsWindow();
        _mainMenu.Rules = CreationRules();
        // ARC-UI-066: the browsed hero in the browser's art when it read.
        Texture2D? portrait = null;
        var browsed = _mainMenu.Window == AreaMenuWindow.PickCharacter ? BrowsedHeroArt(_mainMenu.Hero, out portrait) : null;
        // ARC-UI-068: the new character in its window's art when it can be drawn.
        var created = _mainMenu is { Window: AreaMenuWindow.NewCharacter, Creation: { } creation } ? NewCharacterArt(creation, out portrait) : null;
        _mainMenuPanel.Render(_mainMenu, _mainMenu.Window == AreaMenuWindow.PickCharacter ? HeroDescription(_mainMenu.Hero)
            : _mainMenu.Window == AreaMenuWindow.Shop && _mainMenu.Creation?.Kit is { } kit ? ShopDescription(kit, _mainMenu.Creation.Shop) : "", browsed, portrait,
            created, ShopArtShown, _editorPanel?.IsOpen == true ? _editorPanel.ArtMode : CharacterEditorArtReady);
        RenderCharacterEditor();
    }

    /// <summary>ARC-UI-036: the opening shop's kit as the window lists it: the gold, then each item's name (the game's text when the names
    /// loaded) with its prototype, the clothes the kit chose and the items it skipped.</summary>
    private string ShopDescription(AreaOpeningKit kit, AreaShopSession? shop)
    {
        // ARC-UI-038: with the merchant's barter open the hero's goods are the barter window's.
        if (shop?.Owner is { } owner)
            return $"trading with {shop.Barter.Merchant.Name} on {shop.Map.Name}: gold {owner.QueryGold()}; Accept starts the game, Back returns to the editor";
        var lines = new List<string> { $"gold {kit.Gold} (background {kit.Background})" };
        for (var i = 0; i < kit.Kit.Count; i++)
            lines.Add($"kit:{i + 1} {AreaInventoryHost.KindName(kit.Kit[i].Kind, _hoverNames?.Catalog)} (prototype {kit.Kit[i].Kind.Prototype})");
        lines.Add(kit.Clothes is null ? "no clothes for this race's size" : kit.Jacket ? $"the suit did not fit: jacket {kit.Clothes}" : $"clothes {kit.Clothes}");
        if (kit.Skipped > 0) lines.Add($"{kit.Skipped} item(s) skipped: no prototype");
        return string.Join("\n", lines);
    }

    /// <summary>ARC-UI-030: the character-creation rules of the rule setting the launcher and options window hold (NW4).</summary>
    private CharacterCreationRuleProfile CreationRules() => GameRules.For(_ruleMode).Creation;

    /// <summary>ARC-UI-030: the new character's name in the status lines (ARC-UI-033: its name field, with its race and gender).</summary>
    private static string CreationName(AreaNewCharacter creation) => $"{creation.NameText()}, a new {creation.Race} {creation.Gender} character";

    /// <summary>ARC-UI-030: the new character's start (the generic prototype), read once a run as the pre-made heroes are.</summary>
    private AreaHeroLoad MainMenuNewCharacter()
    {
        if (_mainMenuNewCharacter is { } cached) return cached;
        var generic = AreaStartingHero.NewCharacter;
        var load = _heroLoaderForSelfTest?.Invoke(generic) ?? AreaStartingHero.Load(_arguments!.ModulePath, generic);
        GD.Print($"ARC-UI-030 new character start status={load.Status} detail={load.Detail} backgrounds={load.Backgrounds?.Count.ToString() ?? load.BackgroundsDetail} portraits={load.Portraits?.GameCount.ToString() ?? load.PortraitsDetail} portrait={load.Read?.Hero?.Portrait}");
        return _mainMenuNewCharacter = load;
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
    private void MainMenuCommand(Func<AreaMenuCommand> step, bool cancels = false)
    {
        if (!MainMenuOpen || _mainMenu is null) return;
        // ARC-DEV-381: a choice that waits for the start scene takes no other input; Escape or Back takes it back.
        if (_menuDeferred is not null) { if (cancels) CancelDeferredMenuCommand(); return; }
        // ARC-DEV-387: a save's map is loading under the menu; it takes no input until the save is in.
        if (_menuLoadingSave || _pendingLoad is not null) return;
        var command = step();
        // ARC-DEV-387: the New Game window (or any window after it) is the moment the start map's load begins.
        if (_startLoadPending && _mainMenu is { } entered && AreaMainMenu.NeedsStartScene(entered.Window)) BeginStartLoad("the player entered window " + entered.Window);
        if (_menuAwaitsScene && DefersUntilScene(command.Kind)) { DeferMenuCommand(command); return; }
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
                // ARC-DEV-387: no scene yet (the start map is not loaded): the save's own map loads and the save loads into it.
                if (_menuAwaitsScene)
                {
                    if (BeginSaveSceneFlow(latest.Name, "Last save ") is { } lastRefused) { _mainMenu.Notice = lastRefused; break; }
                    ShowSaveLoadingMenu();
                    return;
                }
                // ARC-UI-1504: the load shows the load screen first; a refusal comes back to the menu with the reason.
                LoadGameShown(SaveLibrary.Slot(latest.Name), "Last save ", MainMenuTables(), AreaLoadMessageRoute.Result, ok =>
                {
                    if (ok) { EndMainMenu("last save " + latest.Name); return; }
                    if (_mainMenu is not { } menu) return;
                    menu.Notice = _quickSaveStatus; RenderMainMenu(); RefreshAreaStatus();
                });
                return;
            case AreaMenuCommandKind.ViewIntro:
                // ARC-RENDER-1170 (0x5445F0): movies 1 and 7, then the window shows again; the menu stands under the movies.
                if (PlayMovies([1, 7], 0, () => { RenderMainMenu(); RefreshAreaStatus(); }, "view intro")) { _mainMenuPanel!.CloseView(); return; }
                _mainMenu.Notice = AreaMainMenu.NoIntro;
                break;
            case AreaMenuCommandKind.Credits:
                // ARC-RENDER-1171 (0x544640): the slides of script 999 show over the menu, which stands under them.
                if (StartCredits(out var creditsRefused)) return;
                _mainMenu.Notice = creditsRefused;
                break;
            case AreaMenuCommandKind.StartNewGame:
                // ARC-RENDER-1170: the new game's movies (1 and 7) play before the start, which then runs as ever.
                if (PlayMovies([1, 7], 0, () => ContinueMainMenuStart(command), "new game")) { _mainMenuPanel!.CloseView(); return; }
                ContinueMainMenuStart(command);
                return;
            case AreaMenuCommandKind.OpenNewCharacter:
                // ARC-UI-030: the original makes the PC when the window opens (0x5446F0); a start that does not read keeps window 11.
                var generic = MainMenuNewCharacter();
                if (generic.Status != AreaHeroStatus.Ready) { _mainMenu.Notice = $"the new character cannot be made ({generic.Status} {generic.Detail})"; break; }
                // ARC-UI-033: the field's default name is main-menu message 500 in the game's encoding (the project's text without it).
                var defaultName = _mainMenu.Texts.FromGame ? MesText.Encode(_mainMenu.Texts.Text(CharacterNameRules.DefaultNameMessage), AreaHoverIndex.TextProfile) : null;
                _mainMenu.OpenNewCharacter(new AreaNewCharacter(generic, defaultName));
                GD.Print("ARC-UI-030 new character window open");
                // ARC-UI-038: the shop map loads behind the menu from here on, so the opening shop usually finds it loaded.
                BeginShopMapLoad();
                break;
            case AreaMenuCommandKind.OpenCharacterEditor:
                // ARC-UI-034: the original's Accept opens window 14, the character editor in creation mode (CM1).
                var editing = _mainMenu.Creation!;
                if (MakeCreationDraft(editing, out var draftRefused) is { } draft)
                {
                    // The editor disposes the draft when it refuses it (its editor did not open).
                    AreaCreationEditor opening;
                    try { opening = new AreaCreationEditor(editing, draft); }
                    catch (ArgumentException refusedDraft) { _mainMenu.Notice = "the character editor cannot open: " + refusedDraft.Message; break; }
                    _mainMenu.OpenCharacterEditor(opening);
                    GD.Print($"ARC-UI-034 character editor open race={editing.Race} gender={editing.Gender} background={editing.Background} unspent={draft.QueryHeroExperience()?.UnspentPoints}");
                }
                else _mainMenu.Notice = "the character editor cannot open: " + draftRefused;
                break;
            case AreaMenuCommandKind.OpenShop:
                // ARC-UI-036: the editor's Accept opens window 15, whose refresh makes the kit (0x545E80 → 0x460FF0, OS2, OS3).
                if (_mainMenu.Editor is not { } shopEditor) { _mainMenu.Notice = "the opening shop cannot open: the character editor is gone"; break; }
                if (AreaOpeningKit.Make(shopEditor, out var kitRefused) is { } kit)
                {
                    _mainMenu.OpenShop(kit);
                    GD.Print($"ARC-UI-036 shop open background={kit.Background} gold={kit.Gold} items={string.Join(",", kit.Items)} kit={kit.Kit.Count} "
                        + $"clothes={kit.Clothes} jacket={kit.Jacket} skipped={kit.Skipped} presses={shopEditor.Creation.Presses.Count}");
                    // ARC-UI-038: the merchant's barter opens at once when the shop map is loaded (else once it is, per frame).
                    SyncShopBarter();
                    if (_mainMenu is null) return;
                }
                else _mainMenu.Notice = "the opening shop cannot open: " + kitRefused;
                break;
            case AreaMenuCommandKind.StartNewCharacter:
                if (PlayMovies([1, 7], 0, () => ContinueMainMenuStart(command), "new game")) { _mainMenuPanel!.CloseView(); return; }
                ContinueMainMenuStart(command);
                return;
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

    /// <summary>The start of a new game the menu command asks for, after the movies of its request played (or at once when they cannot).
    /// The same steps as the menu's own: a refusal shows on the window, a start closes the menu, a start scene that loads first returns.</summary>
    private void ContinueMainMenuStart(AreaMenuCommand command)
    {
        if (_mainMenu is not { } menu) return;
        var made = command.Kind == AreaMenuCommandKind.StartNewCharacter ? menu.Creation : null;
        if (command.Kind == AreaMenuCommandKind.StartNewCharacter && made is null) { menu.Notice = "new game refused: no new character"; RenderMainMenu(); return; }
        var refused = made is not null ? StartNewGame(-1, creation: made) : StartNewGame(command.Hero);
        if (refused is not null) menu.Notice = refused;
        else if (_mainMenu is not null)
        {
            EndMainMenu("new game as " + (made is not null ? CreationName(made) : AreaStartingHero.Premade[command.Hero].Name));
            return;
        }
        else return; // the start scene loads first (ARC-UI-012)
        RenderMainMenu();
        RefreshAreaStatus();
    }

    /// <summary>A new game as pre-made hero <paramref name="index"/>: a session made as this scene's launch made it (its clock, module,
    /// start cell and reputations) with that hero, adopted as a load adopts its session. Null on success, else why nothing changed.</summary>
    /// <param name="sceneFlow">ARC-UI-012: on another scene than the start scene, load the start scene and start there (null is
    /// returned once that load began); false refuses instead.</param>
    /// <param name="creation">ARC-UI-030: start as this new character instead (<paramref name="index"/> is then ignored).</param>
    private string? StartNewGame(int index, bool sceneFlow = true, AreaNewCharacter? creation = null)
    {
        using var span = PerfProbe.Span("game.new");
        AreaHeroLoad hero;
        if (creation is null) hero = MainMenuHero(index);
        // ARC-UI-036: the new character starts with the opening shop's kit.
        else if (creation.StartHero(out var why) is { } made) hero = made;
        else return "new game refused: the new character cannot be made (" + why + ")";
        if (hero.Status != AreaHeroStatus.Ready) return $"new game refused: the hero cannot be read ({hero.Status} {hero.Detail})";
        var name = creation is null ? hero.Hero.Name : CreationName(creation);
        if (StartSceneRefusal() is { } elsewhere)
        {
            if (!sceneFlow) return "new game refused: " + elsewhere;
            return BeginSceneFlow(SceneFlowKind.NewGame, index, $"new game as {name}: {elsewhere}, the start scene loads first", creation) is { } flowRefused
                ? $"new game refused: {elsewhere} and the start scene cannot load ({flowRefused})" : null;
        }
        if (_newGameRecipe is not { } recipe) return "new game refused: no recipe";
        var busy = QuickBusyReason();
        if (busy is QuickBusy.NoActor or QuickBusy.Lifecycle or QuickBusy.MapLoading) return "new game refused: " + Describe(busy);
        GameSessionOwner owner;
        var fromShop = false;
        // ARC-UI-038: a refused start on the shop's session drops it (its trades with it); the shop opens anew on the next frame.
        string ShopRefused(string why)
        {
            creation!.DropShop();
            if (_mainMenu is { Window: AreaMenuWindow.Shop } shopMenu) shopMenu.ShopNote = AreaMainMenu.ShopLoading;
            return "new game refused: " + why;
        }
        // ARC-UI-038: the new character's game goes on with the opening shop's session (its trades kept): the start wields its best items
        // and teleports the PC from the shop map to the start map's tile (0x5412E0, OS5).
        if (creation?.Shop is { Owner: not null } shop)
        {
            if (shop.Start(hero.KitWear ?? [], new(recipe.RuntimeMap), new(checked((int)recipe.Spawn.X), checked((int)recipe.Spawn.Y))) is { } shopRefused)
                return ShopRefused(shopRefused);
            owner = shop.Take();
            fromShop = true;
        }
        else
        {
            var created = AreaTransitionCoordinator.CreateSession(recipe.Clock, Guid.NewGuid(), recipe.Module, recipe.Spawn, recipe.RuntimeMap, hero, recipe.Reputations,
                NewSessionSeed(), NewSessionThrowsGamblingRoll());
            if (created.Owner is not { } made) return $"new game refused: the session was not created ({created.Failure})";
            owner = made;
            // ARC-UI-030: the background the window set, with its effect, as the original's background buttons set it on the PC.
            if (creation?.Apply(owner, hero.Effects ?? (_ => null)) is { } backgroundRefused)
            {
                owner.Dispose();
                return "new game refused: " + backgroundRefused;
            }
            // ARC-UI-036: then the start's best weapon and body armour (0x5412E0, OS5).
            if (creation is not null && AreaNewCharacter.WieldStart(owner, hero.KitWear ?? []) is { } wieldRefused)
            {
                owner.Dispose();
                return "new game refused: " + wieldRefused;
            }
            // ARC-DEV-115 (MIG11 S4, RL29): then the start's teleport of the hero to the start map, the session's change of place.
            var startRefused = AreaNewGameStart.Run(owner, new(recipe.RuntimeMap), new(checked((int)recipe.Spawn.X), checked((int)recipe.Spawn.Y)), null, out var startRun);
            if (startRun is not null) LogChangeOfPlace(startRun);
            if (startRefused is not null)
            {
                owner.Dispose();
                return "new game refused: " + startRefused;
            }
        }
        // As a load: the maps' inventory seeds and the dialog host move to the new session before anything changes.
        if (_inventoryHost?.Attach(owner) is { } attached && attached != GameSessionFailure.None)
        {
            owner.Dispose();
            var why = $"the inventory seeds do not attach ({attached})";
            return fromShop ? ShopRefused(why) : "new game refused: " + why;
        }
        AreaConversationHost? talk = null;
        try { talk = _conversationHost?.Rebind(owner); }
        catch (ArgumentException)
        {
            owner.Dispose();
            return fromShop ? ShopRefused("the dialog state is not readable") : "new game refused: the dialog state is not readable";
        }
        AdoptHeroTables(hero);
        AdoptLoadedSession(owner, AreaRuntimes(), talk, "new game");
        // ARC-DEV-332: the new game's teleport to the start map is a map open: every sound stops, the map's default pair plays and then the
        // start sector's pair (the adopted session starts without sound state; a load keeps the saved one).
        if (_readyArea is { } started) EnterAreaMapSound(started.Snapshot.CatalogueMap);
        SetQuickStatus("new game as " + name);
        GD.Print("ARC-UI-011 new game " + System.Text.Json.JsonSerializer.Serialize(new {
            Name = name, hero.Definition!.Prototype, hero.Flag, Race = hero.Definition.Race.ToString(), Gender = hero.Definition.Gender.ToString(),
            Background = owner.QueryHeroBackground(), Description = owner.QueryHeroBackgroundDescription(), Map = recipe.RuntimeMap, Cell = recipe.Spawn,
            Revision = owner.Version.Revision }));
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
        _areaKitGridMove = hero?.KitGridMove;
        _areaKitArt = hero?.KitArt;
        _areaKitMissileArt = hero?.KitMissileArt;
        _areaKitThrowWeight = hero?.KitThrowWeight;
        _areaKitItemSpells = hero?.KitItemSpells;
        _areaKitUse = hero?.KitUse;
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
        // ARC-UI-030: a new character's session takes the generic start's tables (its race and gender are the session's own).
        if (hero.Prototype == AreaStartingHero.GenericPrototype + Arcanum.Data.Characters.PremadeHeroReader.PrototypeOffset)
            return MainMenuNewCharacter() is { Status: AreaHeroStatus.Ready } generic ? SessionKitTables(generic, loaded) : null;
        for (var i = 0; i < AreaStartingHero.Premade.Count; i++)
            if (AreaStartingHero.Premade[i].BasicPrototype + Arcanum.Data.Characters.PremadeHeroReader.PrototypeOffset == hero.Prototype)
                return MainMenuHero(i) is { Status: AreaHeroStatus.Ready } ready ? ready : null;
        return null;
    }

    /// <summary>ARC-UI-036: a new character's hero tables for the kit its session holds (the opening shop's kit): a map transfer or a
    /// load reads the generic start again, whose kit is empty, so the kit's tables are rebuilt from the session's kit items, held, placed
    /// or given
    /// (<see cref="AreaStartingHero.WithSessionKit"/>). A pre-made hero's load, or null, as it is; a kit whose facts do not read keeps the
    /// generic tables (its items then have no facts here) and says so.</summary>
    private static AreaHeroLoad? SessionKitTables(AreaHeroLoad? hero, GameSessionOwner session)
    {
        if (hero is null || hero.Hero.BasicPrototype != AreaStartingHero.GenericPrototype) return hero;
        if (AreaStartingHero.WithSessionKit(hero, session.QueryKitItemsAnywhere(), out var detail) is { } rebuilt) return rebuilt;
        GD.Print("ARC-UI-036 kit tables not rebuilt: " + detail);
        return hero;
    }

    /// <summary>The scene ends under the menu (a load of a save on another map tears it down): the menu ends with it, so the save
    /// list closing in the teardown does not bring it back.</summary>
    private void EndMainMenuForTeardown()
    {
        // ARC-DEV-381: the early menu stands over no scene, so the first scene's publication tears nothing of it down.
        if (_mainMenu is null || _menuAwaitsScene) return;
        GD.Print("ARC-UI-011 main menu closed: the scene ended (another map loads)");
        DropMainMenuShop();
        _mainMenu.DropEditor(); CloseCharacterEditor();
        _mainMenuPanel?.CloseView(); _mainMenu = null; _mainMenuStatus = "closed: another map loads";
        StopMenuMusic();
    }

    private void EndMainMenu(string reason)
    {
        if (_mainMenu is null) return;
        GD.Print("ARC-UI-011 main menu closed: " + reason);
        DropMainMenuShop();
        _mainMenu.DropEditor(); CloseCharacterEditor();
        _mainMenuPanel?.CloseView(); _mainMenu = null; _mainMenuStatus = reason;
        StopMenuMusic();
        QuarantineWorld(); RefreshReadyStatus();
    }

    /// <summary>The save list closed while the menu waited under it: a load or a save ends the menu (a save as the original's,
    /// ledger SF4), otherwise it comes back.</summary>
    private void SaveListClosedUnderMainMenu(bool finished)
    {
        if (_mainMenu is null || MainMenuOpen || (!QuickKeysActive && !_menuAwaitsScene)) return;
        if (finished) { EndMainMenu("finished in the save list"); return; }
        _mainMenu.Resume(null);
        QuarantineWorld(); _quickKeys.Reset();
        RenderMainMenu(); RefreshReadyStatus();
    }

    /// <summary>Per frame: a session that is gone (a reset, map mode left) ends the menu.</summary>
    private void SyncMainMenu()
    {
        if (_mainMenu is not null && !QuickKeysActive && !_menuAwaitsScene) { DropMainMenuShop(); _mainMenu.DropEditor(); CloseCharacterEditor(); _mainMenuPanel?.CloseView(); _mainMenu = null; }
        SyncShopBarter();
    }

    /// <summary>ARC-UI-038: the menu ends: the opening shop's session goes with it, unless a new game takes it after the start scene loads
    /// (the scene flow carries the character).</summary>
    private void DropMainMenuShop()
    {
        if (_barterInShop) EndBarter();
        if (_mainMenu?.Creation is { } creation && ReferenceEquals(_sceneFlow?.Creation, creation)) return;
        _mainMenu?.DropShop();
    }

    /// <summary>While open the menu owns the keys: Escape goes back (or continues play, exits from a reopened main menu, answers
    /// the quit box no), a typed letter chooses its button (MM6, SF2, SF3). ARC-UI-013: in the options window Up and Down move the
    /// focus, Left and Right change the focused control, and the O key closes the menu from play (OP7).</summary>
    private void HandleMainMenuKey(InputEventKey key)
    {
        if (!key.Pressed || key.Echo || _mainMenu is null) return;
        var routed = key.Keycode != Key.None ? key.Keycode : key.PhysicalKeycode;
        if (routed == Key.Escape) { MainMenuCommand(() => _mainMenu.Escape(), cancels: true); return; }
        // The quit box takes Return as yes, as the original's yes/no box (SF3).
        if (routed is Key.Enter or Key.KpEnter && _mainMenu.QuitPending) { MainMenuCommand(() => _mainMenu.Answer(true)); return; }
        // ARC-UI-034: Return in the character editor goes on as its Accept does (the menu's key handler, CM5).
        if (routed is Key.Enter or Key.KpEnter && _mainMenu.Window is AreaMenuWindow.CharacterEditor or AreaMenuWindow.Shop) { MainMenuCommand(() => _mainMenu.Accept()); return; }
        // ARC-UI-033: while the name field is typed into, Backspace removes its last character and Enter ends the typing.
        if (routed is Key.Backspace or Key.Enter or Key.KpEnter && _mainMenu.Creation is { TypingName: true })
        {
            MainMenuCommand(() => { _mainMenu.NameKey(backspace: routed == Key.Backspace); return AreaMenuCommand.Nothing; });
            return;
        }
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
        if (intent.Cancel) MainMenuCommand(() => _mainMenu.Escape(), cancels: true);
        else if (intent.Confirm) MainMenuCommand(() => _mainMenu.Confirm());
        else if (intent.MenuPressedDirection != 0)
            MainMenuCommand(() =>
            {
                if (_mainMenu.Window == AreaMenuWindow.PickCharacter) _mainMenu.Browse(intent.MenuPressedDirection); else _mainMenu.Move(intent.MenuPressedDirection);
                return AreaMenuCommand.Nothing;
            });
    }

    /// <summary>ARC-UI-012: Escape in play opens the in-play menu (the original's window 3, or window 4: ledger SF1). ARC-DEV-111 (MIG16
    /// S4, SA45, SA51; read in the executable, main loop 0x401705): Escape is a global key of the main loop that no mode and no window
    /// handles itself, so it opens the menu in every interface mode: window 4 while a dialog, the world map or a barter (its dialog is
    /// still active) is up or another has the turn in a round, else window 3. A press the palette or UI focus owns does nothing.
    /// ARC-INPUT-1124 (main loop, the Escape branch at 0x401705 is in the key-up arm, record flag 0): the menu opens on the key's
    /// RELEASE, not its press. The press only arms the route while no menu is open; the release of an armed press opens the menu. The
    /// release of a press that another owner took (the palette, a note entry, the bug report, the death box, the menu's own Escape) finds
    /// the route unarmed and does nothing, so one physical press never closes something and reopens the menu.</summary>
    private bool RouteGameMenuKey(WorldKeyDecision decision)
    {
        if (!QuickKeysActive || decision.Key != WorldKey.Escape) return false;
        // The palette's own Escape closes it (its pulse); a menu waiting for the save window to open takes no second one.
        if (PaletteOpen) { _gameMenuEscapeArmed = false; return false; }
        if (decision.Echo) return true;
        if (decision.Pressed) { _gameMenuEscapeArmed = _mainMenu is null; return true; }
        if (!_gameMenuEscapeArmed) return true;
        _gameMenuEscapeArmed = false;
        if (_mainMenu is not null) return true;
        if (!_worldFocused || PanelOutsideStackOpen || WorldUiFocus) { _mainMenuStatus = "Escape ignored: palette, menu or UI focus owns input"; RefreshAreaStatus(); return true; }
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending || _mapHandoff is { Kind: not MapHandoffKind.Stream })
        {
            _mainMenuStatus = "Escape ignored: a load is in progress"; RefreshAreaStatus(); return true;
        }
        OpenInPlayMenuOverWindows(optionsWindow: false);
        return true;
    }

    /// <summary>The in-play menu's start (0x541180), shared by Escape (the in-play window) and the O key (the options window, ARC-UI-2026).</summary>
    private void OpenInPlayMenuOverWindows(bool optionsWindow)
    {
        var limited = AnotherHasTheTurn || ConversationOpen || WorldMapOpen || BarterOpen || ItemServiceOpen;
        // 0x573440 (the menu's start, 0x541180): the inventory window closes, with any overlay over it; barter (the inventory family) too.
        // Target modes over the base and the other windows stay as they are, and Continue returns to them (0x5412D0).
        if (InventoryOpen) EndInventory();
        if (BarterOpen) CloseBarterByPlayer();
        // ARC-UI-1731: the item identify and repair windows are the inventory window in its modes 5 and 6 (the same family).
        if (ItemServiceOpen) EndItemService();
        GD.Print($"ARC-DEV-111 {(optionsWindow ? "the O key opens the options window" : "Escape opens the in-play menu")} kind={(optionsWindow ? 4 : limited ? 3 : 2)} (stack {StackText()})");
        OpenMainMenu(MainMenuTexts(_arguments ?? _launchArguments!), limited ? AreaMenuWindow.InPlayLimited : AreaMenuWindow.InPlay);
        if (optionsWindow && _mainMenu is { } menu && MenuOpenedHere(menu)) { menu.OpenOptionsWindow(); RenderMainMenu(); }
    }

    private bool MenuOpenedHere(AreaMainMenu menu) => ReferenceEquals(menu, _mainMenu) && menu.Window is AreaMenuWindow.InPlay or AreaMenuWindow.InPlayLimited;

    /// <summary>
    /// ARC-UI-2026 (main loop 0x401560, the O branch in the key-up arm like Escape's; menu start 0x541180 with kind 4 = window 6; read in the
    /// exports): the release of O opens the options window directly, in play, over any window and over the dialog (the dialog's handler
    /// lets the release through, SA43), at the Game page, without the pause Escape raises (kind 4 skips the pause counter). The only guard
    /// is a focused text field and a menu already up; the hero's state and another participant's turn do not refuse it. The press only arms the route.
    /// ARC-UI-2066 (OP9, OP7): the branch reads the scan code 0x18 of a type 5 message (the keyboard poll 0x52B380), not the typed character, so the
    /// physical O key acts on every layout (as the options window's own O does) and a typed O on another key does nothing; while Ctrl or Alt is held
    /// the poll queues no scan code for a non-modifier key, so Ctrl+O and Alt+O do nothing (Shift+O works).
    /// </summary>
    private bool RouteOptionsKey(InputEventKey key, WorldKeyDecision decision)
    {
        if (!QuickKeysActive) return false;
        if (key.PhysicalKeycode != Key.O) return decision.Key == WorldKey.O; // a typed O elsewhere on the layout is no key of the original (swallowed, nothing happens)
        // A letter typed on the physical O key is a separate character message in the original (its own route goes on); only a typed O is the key itself.
        var consumed = decision.Key == WorldKey.O;
        if (key.CtrlPressed || key.AltPressed || key.MetaPressed) return consumed;
        if (decision.Echo) return consumed;
        if (decision.Pressed) { _optionsKeyArmed = _mainMenu is null; return consumed; }
        if (!_optionsKeyArmed) return consumed;
        _optionsKeyArmed = false;
        if (_mainMenu is not null) return consumed;
        if (!_worldFocused || PanelOutsideStackOpen || WorldUiFocus) { _mainMenuStatus = "O ignored: palette, menu or UI focus owns input"; RefreshAreaStatus(); return consumed; }
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending || _mapHandoff is { Kind: not MapHandoffKind.Stream })
        {
            _mainMenuStatus = "O ignored: a load is in progress"; RefreshAreaStatus(); return consumed;
        }
        OpenInPlayMenuOverWindows(optionsWindow: true);
        return consumed;
    }

    private bool _optionsKeyArmed;

    // ARC-INPUT-1124: an Escape pressed while the in-play route owned it; its release opens the menu (the original acts on key up).
    private bool _gameMenuEscapeArmed;

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
        static void Require(bool value, string why)
        {
            if (value) return;
            // The scene's failure line names only the exception's type: the reason is printed first.
            GD.Print("ARC-UI-011 main menu smoke FAILED: " + why);
            throw new InvalidOperationException("ARC-UI-011 main menu smoke: " + why);
        }
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
            await MainMenuArtForSmoke(Require);
            Require(_mainMenu.NewGameRefusal is null, "a new game can start on this map: " + _mainMenu.NewGameRefusal);
            // ARC-DEV-381 (O1): the menu opened while the start map loaded; the choices that need the scene wait for it.
            EarlyMenuForSmoke(Require, Choose);
            var placeholder = _areaSession;
            // ARC-DEV-332, ARC-UI-139: the menu plays the opening map's sector pair (the shop map's 26/29, "Main Menu") through its own scheme player (no
            // session): it began when the menu opened, before the scene, and went on across the scene's attach; the scene's own scheme is not played.
            if (_audio is not null && AreaSoundCatalog() is not null && SoundWorld().Maps is { } menuMaps && Arcanum.Data.Audio.MainMenuSound.Choose(menuMaps) is { } menuPlace)
            {
                var menuSector = menuPlace.Sector is { } menuCoordinate ? SectorSound((menuPlace.Map.Catalogue, menuCoordinate.Id)) : null;
                var menuMusic = menuSector is { } menuRecord && (menuRecord.MusicScheme != 0 || menuRecord.AmbientScheme != 0) ? menuRecord.MusicScheme : MapSoundDefault(menuPlace.Map.Catalogue)?.Music ?? 0;
                Require(_menuMusic is { } menuState && menuState.Slot0?.Id == menuMusic && menuState.Voices.Count > 0,
                    $"the menu plays the opening map's scheme {menuMusic} through its own player: slot {_menuMusic?.Slot0?.Id} voices {_menuMusic?.Voices.Count}");
                GD.Print($"ARC-DEV-332 native-smoke menu-sound PASS map={menuPlace.Map.Name} music={menuMusic} voices={_menuMusic!.Voices.Count} (ARC-UI-139, no session)");
            }
            Choose(10);
            Require(_mainMenu.Window == AreaMenuWindow.SinglePlayer, "single player by its letter");
            Choose(52);
            Require(MainMenuOpen && ReferenceEquals(placeholder, _areaSession) && _mainMenu.Notice == "no save to continue from", "Last Save without a save stayed");
            Choose(50);
            // ARC-UI-2489 (M03): the new-game window, as NAME-newgame.png beside ARC_AREA_MENU_CAPTURE's main window.
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_MENU_CAPTURE") is { Length: > 0 } newGameCapture)
            {
                Require(_mainMenu.Window == AreaMenuWindow.NewGame, "the new game window opened for its capture: " + _mainMenu.Window);
                for (var i = 0; i < 2; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
                using var newGameShot = GetViewport().GetTexture().GetImage();
                Require(newGameShot.SavePng(System.IO.Path.ChangeExtension(newGameCapture, null) + "-newgame.png") == Error.Ok, "the new game capture saved");
            }
            Choose(420);
            Require(_mainMenu.Window == AreaMenuWindow.PickCharacter, "the pre-made browser opened");
            // ARC-UI-066: the browser in the original's look, browsed by its arrows.
            await PremadeBrowserArtForSmoke(Require);
            var first = MainMenuHero(0);
            var definition = first.Definition ?? throw new InvalidOperationException("ARC-UI-011 main menu smoke: the first hero did not read: " + first.Detail);
            Require(first.Status == AreaHeroStatus.Ready && _mainMenuPanel!.HeroForSelfTest.StartsWith("1/12 halfling-male\n", StringComparison.Ordinal),
                "the first real hero reads: " + first.Status + " " + first.Detail);
            MainMenuCommand(() => { _mainMenu!.Browse(1); return AreaMenuCommand.Nothing; });
            Require(MainMenuHero(1).Status == AreaHeroStatus.Ready && _mainMenuPanel!.HeroForSelfTest.StartsWith("2/12 gnome-male\n", StringComparison.Ordinal), "Next read the second hero");
            MainMenuCommand(() => { _mainMenu!.Browse(-1); return AreaMenuCommand.Nothing; });
            // ARC-UI-066: Start pressed and released over itself in the browser's art.
            await ClickPremadeForSmoke(AreaPremadeBrowserButton.Start);
            var session = _areaSession!;
            var flags = session.Flags;
            var flag = flags.Read(new Arcanum.Scripting.GlobalFlags.GlobalFlagReadRequest(new(Guid.NewGuid()), flags.Version, new(first.Flag!.Value)));
            Require(!MainMenuOpen && !ReferenceEquals(placeholder, session) && session.QueryHero()?.Prototype == first.Definition!.Prototype
                && session.QueryGold() == first.Definition.Gold && flag.Value == true, $"the new game is the first hero's: {_quickSaveStatus} {session.QueryHero()?.Prototype} flag {flag.Status} {flag.Value}");
            Require(_menuMusic is null, "the menu's song ended with the menu (ARC-UI-139)");
            // ARC-DEV-332: the start map opened: its arrival sector's pair (else the map's default) plays, no scheme of the menu is left.
            if (_audio is not null && AreaSoundCatalog() is not null && _readyArea is { } startedArea)
            {
                var arrival = CurrentSoundSector() is { } arrivalKey ? SectorSound(arrivalKey) : null;
                var startMusic = arrival is { } record && (record.MusicScheme != 0 || record.AmbientScheme != 0) ? record.MusicScheme : MapSoundDefault(startedArea.Snapshot.CatalogueMap)?.Music ?? 0;
                Require(session.QuerySound().State.Slot0 == startMusic, $"the new game plays the start map's scheme {startMusic}: slots {session.QuerySound().State.Slot0}/{session.QuerySound().State.Slot1}");
                GD.Print($"ARC-DEV-332 native-smoke new-game-sound PASS music={startMusic}");
            }
            // ARC-UI-069: and its player name (field 0x10E, MW21), the save info's name.
            Require(session.QueryHero()!.PlayerName.Length > 0 && session.QueryHero()!.PlayerName.SequenceEqual(first.Read!.Hero!.PlayerName),
                $"the started hero keeps its player name: {AreaStartingHero.Name(session.QueryHero()!)}");
            // ARC-UI-068: the hero keeps its story's number (field 0x102, MW19).
            Require(session.QueryHeroBackgroundDescription() == first.Read!.Hero!.BackgroundDescription && first.Read.Hero.BackgroundDescription > 1000,
                $"the started hero keeps its background description: {session.QueryHeroBackgroundDescription()} (read {first.Read.Hero.BackgroundDescription})");
            for (var i = 0; i < 3; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            QuickKey(Key.F7);
            Require(_saveLibraryForSelfTest.AutoSlot.Exists, "F7 saved the new game: " + _quickSaveStatus);
            // ARC-UI-115: cover Save as well as Load without the scene smoke's unrelated map handoff.
            if (System.Environment.GetEnvironmentVariable("ARC_SAVE_HD_VERIFY") == "1")
            {
                OpenSaveList(AreaSaveListMode.Save);
                Require(SaveListOpen && _saveList!.Mode == AreaSaveListMode.Save, "HD probe opened the save window");
                await SaveListArtForSmoke(Require);
                EndSaveList(); // inspect only; the temporary auto-save is the only save written
            }
            // ARC-UI-062: Load Game shows the load window in the original's look with the auto-save listed; a press on its row selects
            // it, and Previous Screen pressed and released over itself gives the menu back.
            OpenMainMenu(texts);
            Choose(10); Choose(51);
            Require(SaveListOpen && _saveList!.Mode == AreaSaveListMode.Load && _saveList.Entries.Count == 1, "Load Game opened the load window with the auto-save");
            await SaveListArtForSmoke(Require);
            var saveArt = _saveListPanel!.ArtForSelfTest;
            async Task Click(Vector2 at)
            {
                foreach (var pressed in new[] { true, false })
                {
                    GetViewport().PushInput(new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.Left, Pressed = pressed, Position = at, GlobalPosition = at, ButtonMask = pressed ? MouseButtonMask.Left : 0,
                    });
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }
            }
            var (rowX, rowY, _, _) = AreaSaveListArtLayout.Row(0);
            await Click(saveArt.ScreenPointForSelfTest(rowX + 10, rowY + 10));
            Require(SaveListOpen && _saveList!.Selected == 0, "a press on the auto-save's row selects it: " + _saveList?.Selected);
            await SaveInfoForSmoke(Require, Click);
            var (_, backX, backY) = AreaSaveListArtLayout.Button(AreaSaveListArtButton.Back);
            await Click(saveArt.ScreenPointForSelfTest(backX + 30, backY + 30));
            Require(!SaveListOpen && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.SinglePlayer, "Previous Screen closed the load window back to the menu: " + _mainMenu?.Window);
            Choose(52);
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

            // ARC-UI-030: New Character on the real generic start and background table: a dwarf's refused gender press keeps the
            // background (Steamwoven, the automated run's setting), an elf woman takes a background, and Accept starts her.
            OpenMainMenu(texts);
            Choose(10); Choose(50); Choose(421);
            Require(_mainMenu!.Window == AreaMenuWindow.NewCharacter, "New Character opened its window: " + _mainMenu.Notice);
            // ARC-UI-068: the window in the original's look, its arrows, bands and name field by the pointer.
            await NewCharacterArtForSmoke(Require);
            var creation = _mainMenu.Creation!;
            Require(creation.Race == Arcanum.Simulation.Character.CharacterRace.Human && creation.Gender == Arcanum.Simulation.Character.CharacterGender.Male
                && creation.Background == 0 && creation.Chosen is null && creation.Generic.Backgrounds is not null && creation.BackgroundText() is not null,
                "the generic human man with the real background table: " + creation.Generic.BackgroundsDetail);
            void Press(AreaNewCharacterButton button) => _mainMenuPanel!.CreationPressForSelfTest(button);
            Press(AreaNewCharacterButton.RaceNext); Press(AreaNewCharacterButton.BackgroundNext);
            var dwarfBackground = creation.Chosen;
            Press(AreaNewCharacterButton.GenderNext);
            Require(creation.Race == Arcanum.Simulation.Character.CharacterRace.Dwarf && creation.Gender == Arcanum.Simulation.Character.CharacterGender.Male
                && dwarfBackground is not null && creation.Chosen == (CreationRules().RefusedGenderClearsBackground ? null : dwarfBackground),
                $"the dwarf's gender press under {CreationRules().Name}: {creation.Chosen}");
            Press(AreaNewCharacterButton.RaceNext); Press(AreaNewCharacterButton.GenderNext);
            Require(creation.Race == Arcanum.Simulation.Character.CharacterRace.Elf && creation.Gender == Arcanum.Simulation.Character.CharacterGender.Female
                && creation.Chosen is null, "an elf woman, background cleared");
            Press(AreaNewCharacterButton.BackgroundNext);
            var chosen = creation.Chosen ?? throw new InvalidOperationException("ARC-UI-030 new character smoke: no background landed");
            // ARC-UI-068: the art shows her background's name, and its story when its number is above 1000 (MW19).
            var elfArt = _mainMenuPanel!.NewCharacterArtForSelfTest.ViewForSelfTest;
            Require(elfArt is not null && elfArt.Character.BackgroundName.Length > 0
                && (chosen.Description > AreaPremadeBrowserArtLayout.DefaultDescription ? elfArt.Character.DescriptionTitle is null : elfArt.Character.DescriptionTitle is not null),
                $"the art shows her background {chosen.Background} ({chosen.Description}): {elfArt?.Character.BackgroundName}");
            // ARC-UI-033: the elf woman's portraits from the real file (her first after the gender change, then next and previous), the
            // default name refused by Accept (506), and a name typed through the window's keys.
            var portraits = creation.Generic.Portraits ?? throw new InvalidOperationException("ARC-UI-033 new character smoke: portraits " + creation.Generic.PortraitsDetail);
            var woman = new Arcanum.Data.Characters.PortraitSubject((int)creation.Race, Arcanum.Data.Characters.PortraitCatalogue.FemaleGender);
            var firstPortrait = creation.Portrait;
            Require(firstPortrait == portraits.First(woman) && creation.PortraitText() is not null, $"the elf woman's first portrait: {firstPortrait}");
            Press(AreaNewCharacterButton.PortraitNext);
            var secondPortrait = creation.Portrait;
            Require(secondPortrait != firstPortrait && portraits.Allows(secondPortrait, woman), $"portrait next: {secondPortrait}");
            Press(AreaNewCharacterButton.PortraitPrevious); Press(AreaNewCharacterButton.PortraitNext);
            Require(creation.Portrait == secondPortrait, "portrait previous and next again");
            _mainMenuPanel!.AcceptForSelfTest();
            Require(MainMenuOpen && _mainMenu.Window == AreaMenuWindow.NewCharacter && _mainMenu.Notice == _mainMenu.Texts.Text(CharacterNameRules.RefusalMessage)
                && creation.Name.SequenceEqual(creation.DefaultName), "Accept refused the default name: " + _mainMenu.Notice);
            _mainMenuPanel.NameClickForSelfTest();
            Require(creation.TypingName && creation.Name.IsEmpty, "a click on the name field cleared it");
            foreach (var typed in "Velaa")
                HandleMainMenuKey(new InputEventKey { Pressed = true, Keycode = Key.A, Unicode = typed });
            HandleMainMenuKey(new InputEventKey { Pressed = true, Keycode = Key.Backspace });
            HandleMainMenuKey(new InputEventKey { Pressed = true, Keycode = Key.Enter });
            Require(!creation.TypingName && creation.NameText() == "Vela" && _mainMenuPanel.NewCharacterArtForSelfTest.ViewForSelfTest?.Character.Name == "Vela",
                "the typed name: " + _mainMenuPanel.NewCharacterArtForSelfTest.ViewForSelfTest?.Character.Name);
            var beforeAccept = _areaSession;
            // ARC-UI-034: Accept opens the character editor on her draft; the real stats, skills and spells pages spend her points: willpower
            // (the original's sheet order, row 7), a hit point, the first skill and a college 1 spell (willpower 9 lets rank 1 be learned); a
            // second hit-point − is refused at the opening. ARC-UI-036: Return opens the opening shop with her kit; its Back returns to the
            // editor, where the willpower bought before the shop is still free (taken back and bought again); Return twice starts the game
            // with the presses replayed, the kit's gold and items and its best weapon and body armour worn.
            _mainMenuPanel!.AcceptForSelfTest();
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.CharacterEditor && CharacterEditorShown && ReferenceEquals(beforeAccept, _areaSession)
                && _editorPanel!.StatsForSelfTest is { UnspentPoints: 5, EditorOpen: true } && _editorPanel.SkillsForSelfTest is not null,
                "Accept opened the character editor: " + _mainMenu?.Notice);
            var willpower = _editorPanel!.StatsForSelfTest!.Rows[7];
            Require(willpower.Stat == Arcanum.Simulation.Character.CharacterStat.Willpower && willpower.Value == 8, "row 7 is willpower 8: " + AreaStatsPage.Line(willpower));
            // ARC-UI-147: with the game's art the editor is the sheet's original look and the willpower's + is pressed by the pointer on it.
            if (_editorPanel.ArtMode) await CharacterEditorArtForSmoke(Require);
            else _editorPanel!.StatPlusForSelfTest(7).EmitSignal(BaseButton.SignalName.Pressed);
            _editorPanel!.StatPlusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            _editorPanel!.SkillPlusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            _editorPanel!.CollegeTabForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            _editorPanel!.SpellPlusForSelfTest.EmitSignal(BaseButton.SignalName.Pressed);
            var learned = _editorPanel!.SkillMessageForSelfTest;
            _editorPanel!.StatMinusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            _editorPanel!.StatMinusForSelfTest(0).EmitSignal(BaseButton.SignalName.Pressed);
            var editorPresses = _mainMenu!.Creation!.Presses.Count;
            Require(_editorPanel!.StatsForSelfTest is { UnspentPoints: 2 } && editorPresses == 5 && learned.StartsWith("learned ", StringComparison.Ordinal)
                && _editorPanel!.SkillMessageForSelfTest == "refused: nothing bought since the sheet opened to refund",
                $"the editor's presses: {editorPresses} learned='{learned}' last='{_editorPanel!.SkillMessageForSelfTest}'");
            HandleMainMenuKey(new InputEventKey { Pressed = true, Keycode = Key.Enter });
            var shopKit = _mainMenu!.Creation!.Kit ?? throw new InvalidOperationException("ARC-UI-036 opening shop smoke: no kit (" + _mainMenu.Notice + ")");
            Require(MainMenuOpen && _mainMenu.Window == AreaMenuWindow.Shop && !CharacterEditorShown && shopKit is { Background: var kitBackground, Clothes: not null }
                && kitBackground == chosen.Background && shopKit.Gold == creation.Generic.Backgrounds!.Block(chosen.Background).Block!.Money
                && shopKit.Items.Contains(shopKit.Clothes!.Value) && ReferenceEquals(beforeAccept, _areaSession)
                && (_mainMenuPanel!.ShopForSelfTest.StartsWith($"gold {shopKit.Gold}", StringComparison.Ordinal) || _mainMenuPanel.ShopForSelfTest.StartsWith("trading with ", StringComparison.Ordinal)
                    || _mainMenuPanel.ShopArtMode),
                $"Return opened the opening shop with her kit: {_mainMenu.Notice} '{_mainMenuPanel!.ShopForSelfTest}'");
            // ARC-UI-038: the shop map's merchant (loaded behind the menu since the new-character window) opens its barter above the shop:
            // the chest's regenerated coins are the merchant's; the cheapest ware she can pay is bought.
            async Task<AreaShopSession> ShopBarterAsync()
            {
                for (var i = 0; i < 3600 && _mainMenu is { Window: AreaMenuWindow.Shop, ShopNote: AreaMainMenu.ShopLoading }; i++)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var opened = _mainMenu?.Creation?.Shop ?? throw new InvalidOperationException("ARC-UI-038 shop barter smoke: no shop session (" + _mainMenu?.ShopNote + ")");
                Require(BarterOpen && _barterInShop && ReferenceEquals(_barterSession, opened.Owner) && opened.Owner!.QueryActor().Map.RuntimeMap == opened.Map.RuntimeMap
                    && _barter!.View() is { MerchantGold: > 0, Wares.Count: > 0 }
                    && (_mainMenuPanel!.ShopForSelfTest.StartsWith("trading with ", StringComparison.Ordinal) || _mainMenuPanel.ShopArtMode),
                    $"the shop's barter opened on the shop map: {_mainMenu?.ShopNote} '{_mainMenuPanel!.ShopForSelfTest}'");
                return opened;
            }
            (string Entity, int Price) BuyCheapest()
            {
                var view = _barter!.View();
                var ware = view.Wares.Where(line => line.Quote.Refusal == Arcanum.Simulation.Items.TradeRefusal.None && line.Count == 1 && line.Quote.Price <= view.HeroGold)
                    .OrderBy(line => line.Quote.Price).FirstOrDefault() ?? throw new InvalidOperationException("ARC-UI-038 shop barter smoke: no ware she can pay");
                // The window's Buy button runs this handler.
                BuyFromMerchant(ware.Entity);
                Require(_barter!.View().HeroGold == view.HeroGold - ware.Quote.Price && _barter.View().Hero.Any(line => line.Entity == ware.Entity),
                    $"she bought {ware.Text} for {ware.Quote.Price}: {_barterMessage}");
                return (ware.Entity, ware.Quote.Price);
            }
            var firstShop = await ShopBarterAsync();
            var firstShopView = _barter!.View();
            var firstBuy = BuyCheapest();
            var firstOwner = firstShop.Owner!;
            // ARC-UI-069: the shop in the original's look; its Back clicked in the art.
            await ShopArtForSmoke(Require, back: true);
            Require(_mainMenu!.Window == AreaMenuWindow.CharacterEditor && CharacterEditorShown && _mainMenu.Creation!.Kit is null, "the shop's Back returned to the editor");
            Require(!BarterOpen && firstShop.Owner is null && _mainMenu.Creation.Shop is null, "the shop's Back closed the barter and dropped the shop's session");
            _editorPanel!.StatMinusForSelfTest(7).EmitSignal(BaseButton.SignalName.Pressed);
            var refundedAfterShop = _editorPanel!.StatsForSelfTest is { UnspentPoints: 3 } && _editorPanel.StatsForSelfTest.Rows[7].Value == 8;
            _editorPanel!.StatPlusForSelfTest(7).EmitSignal(BaseButton.SignalName.Pressed);
            Require(refundedAfterShop && _editorPanel!.StatsForSelfTest is { UnspentPoints: 2 } && _mainMenu!.Creation!.Presses.Count == editorPresses + 2,
                "the willpower bought before the shop was taken back and bought again: " + _editorPanel!.SkillMessageForSelfTest);
            HandleMainMenuKey(new InputEventKey { Pressed = true, Keycode = Key.Enter });
            Require(_mainMenu!.Window == AreaMenuWindow.Shop && _mainMenu.Creation!.Kit is { } again && again.Items.SequenceEqual(shopKit.Items), "Return opened the shop again");
            var shop = await ShopBarterAsync();
            Require(!ReferenceEquals(shop.Owner, firstOwner) && _barter!.View().HeroGold == shopKit.Gold, "the shop opened anew: the first trade went with its Back");
            var bought = BuyCheapest();
            Require(_barterPanel!.ArtView?.Pc.Any(line => line.Entity == bought.Entity) == true, "the bought ware is on the hero's side of the original look");
            var shopMap = shop.Map;
            var shopOwner = shop.Owner;
            HandleMainMenuKey(new InputEventKey { Pressed = true, Keycode = Key.Enter });
            var made = _areaSession!.QueryHero();
            Require(!MainMenuOpen && !CharacterEditorShown && !ReferenceEquals(beforeAccept, _areaSession) && made is { Race: Arcanum.Simulation.Character.CharacterRace.Elf,
                Gender: Arcanum.Simulation.Character.CharacterGender.Female, Level: 1, UnspentPoints: 2 }
                && made.Stats[(int)Arcanum.Simulation.Character.CharacterStat.Willpower] == 9 && made.Advancement.HitPointPoints == 0
                && made.Advancement.BasicSkills[0] == 1 && made.Advancement.Colleges[0] == 1 && !_areaSession.HeroEditorOpen
                && made.Prototype == AreaStartingHero.GenericPrototype + Arcanum.Data.Characters.PremadeHeroReader.PrototypeOffset
                && _areaSession.QueryHeroBackground() == chosen.Background && _areaSession.QueryHeroBackgroundDescription() == chosen.Description
                && _areaSession.QueryHeroEffects()!.Effects.Count(e => e.Cause == Arcanum.Simulation.Character.EffectCause.Background) == 1
                && _areaSession.QueryGold() == shopKit.Gold - bought.Price && _areaSession.QueryHeroKit().Count == shopKit.Kit.Count, $"Accept started the elf woman: {_quickSaveStatus} {made?.Race} {made?.Gender} bg {_areaSession.QueryHeroBackground()} gold {_areaSession.QueryGold()}");
            // ARC-UI-038: the game goes on with the shop's session: the bought ware is hers and the PC stands on the start map.
            Require(ReferenceEquals(_areaSession, shopOwner) && shop.Owner is null && !BarterOpen && _areaSession.QueryActor().Map.RuntimeMap == CurrentAreaMap && CurrentAreaMap != shopMap.RuntimeMap
                && (_areaSession.QueryMadeItems().Any(item => item.Entity == bought.Entity) || _areaSession.QueryPcInventory().Any(item => item.Entity == bought.Entity)),
                $"the started game is the shop's session with the bought ware on the start map: map {_areaSession.QueryActor().Map.RuntimeMap} shown {CurrentAreaMap}");
            Require(made!.Portrait == secondPortrait && AreaStartingHero.Name(made) == "Vela", $"the session's portrait and name: {made.Portrait} {AreaStartingHero.Name(made)}");
            for (var i = 0; i < 3; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            GD.Print("ARC-UI-030 native-smoke new character PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Backgrounds = creation.Generic.Backgrounds!.Count, DwarfBackground = dwarfBackground!.Background, Rules = CreationRules().Name,
                chosen.Background, chosen.Description, chosen.Effect, made!.Prototype, Revision = _areaSession.Version.Revision }));
            GD.Print("ARC-UI-034 native-smoke character editor PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Presses = editorPresses, made.UnspentPoints, Willpower = made.Stats[(int)Arcanum.Simulation.Character.CharacterStat.Willpower],
                Skill = made.Advancement.BasicSkills[0], College = made.Advancement.Colleges[0], Learned = learned, Life = _areaSession.QueryHeroLife()?.Status.HitPoints }));
            var armour = _areaSession.QueryEquipment().Worn.FirstOrDefault(worn => worn.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Armor);
            GD.Print("ARC-UI-036 native-smoke opening shop PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                shopKit.Background, shopKit.Gold, Items = string.Join(",", shopKit.Items), Kit = shopKit.Kit.Count, shopKit.Clothes, shopKit.Jacket,
                shopKit.Skipped, Armour = armour?.Entity, Worn = _areaSession.QueryEquipment().Worn.Count, RefundedAfterShop = refundedAfterShop,
                Presses = editorPresses + 2 }));
            GD.Print("ARC-UI-038 native-smoke shop merchant PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                shopMap.Name, shopMap.RuntimeMap, shopMap.Merchant, Container = shop.Barter.Merchant.Container, MerchantGold = firstShopView.MerchantGold,
                Wares = firstShopView.Wares.Count, Created = shop.ContainerRegeneration?.Regeneration?.Created.Count, FirstBuy = firstBuy.Price, Bought = bought.Entity,
                bought.Price, Gold = _areaSession.QueryGold(), Map = _areaSession.QueryActor().Map.RuntimeMap, Worn = _areaSession.QueryEquipment().Worn.Count }));
            GD.Print("ARC-UI-033 native-smoke name and portrait PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Portraits = portraits.GameCount, FirstPortrait = firstPortrait, made.Portrait, NameBytes = made.PlayerName.Length,
                DefaultNameBytes = creation.DefaultName.Length, Refusal = CharacterNameRules.RefusalMessage }));
            // ARC-DEV-387: a game saved here, then Last Save before any scene: the save's own map loads and the save into it.
            QuickKey(Key.F7);
            Require(_saveLibraryForSelfTest.AutoSlot.Exists, "F7 saved the game for the last-save check: " + _quickSaveStatus);
            await LastSaveBeforeSceneForSmoke(Require, Choose, texts);
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
