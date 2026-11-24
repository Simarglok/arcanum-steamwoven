using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-012 over the strict synthetic AREA owner in the production app launched without the menu, on a temporary save
    /// folder and synthetic heroes; the start scene "loads" by publishing the synthetic scene again: Escape in play opens the in-play
    /// window (Escape and Continue close it), Save Game saves and ends it, Load Game's window brings it back when closed unused, Quit
    /// asks first (N and Escape keep playing) and then ends the game for a fresh start scene with the main menu reopened (saves kept);
    /// a new game picked on a scene that is not the start scene loads the start scene and starts there; the reopened root's Escape
    /// exits.</summary>
    private static async Task AssertAreaSceneFlowAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.World.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try
        {
            app.PrepareMainMenuForSelfTest();
            app.PrepareSceneFlowForSelfTest(arguments, result);
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = await app.AreaSceneFlowForSelfTestAsync(host);
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-012 self-test PASS synthetic scene flow ({summary})");
    }
}

public partial class TerrainViewerApp
{
    private int _sceneLoadsForSelfTest;

    /// <summary>The launch arguments and a start-scene load that publishes the synthetic scene again (no game folder here).</summary>
    internal void PrepareSceneFlowForSelfTest(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        if (!SelfTestBootstrap) throw new InvalidOperationException("AREA_SCENE_FLOW_SELF_TEST_ONLY");
        _launchArguments = arguments;
        _sceneLoadForSelfTest = next => { _sceneLoadsForSelfTest++; PublishSyntheticForSelfTest(next, result); return Task.CompletedTask; };
    }

    internal async Task<string> AreaSceneFlowForSelfTestAsync(Node host)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA scene flow: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null || _mainMenuDirectoryForSelfTest is null)
            throw new InvalidOperationException("AREA_SCENE_FLOW_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        var library = _saveLibraryForSelfTest!;
        void Letter(char letter)
        {
            var key = (Key)char.ToUpperInvariant(letter);
            InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = letter, Pressed = true });
            InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = letter, Pressed = false });
        }
        string[] Buttons() => [.. _mainMenuPanel!.ButtonsForSelfTest];
        try
        {
            // Launched without the menu: Escape in play opens the in-play window; Escape and Continue close it.
            Require(!MainMenuOpen && _mainMenu is null, "no menu at a launch without --main-menu");
            var running = _areaSession;
            // ARC-INPUT-1124 (main loop 0x401705, key up): the press alone opens nothing, the release opens the menu; a release with no press
            // before it (taken by another owner) opens nothing. ARC-INPUT-2647 (menu handler 0x546EE0, key up, ledger SF6): the menu's own
            // Escape press does nothing, its release closes the menu and is not passed on, so it does not reopen it.
            static InputEventKey EscapeEvent(bool pressed, bool echo = false) => new() { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = pressed, Echo = echo };
            InputWorld(EscapeEvent(true));
            Require(!MainMenuOpen && _mainMenu is null, "an Escape press alone opened the menu (the original acts on the release)");
            InputWorld(EscapeEvent(true, echo: true));
            Require(!MainMenuOpen, "an Escape echo opened the menu");
            InputWorld(EscapeEvent(false));
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlay, "the Escape release opened the in-play menu");
            InputWorld(EscapeEvent(true));
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlay, "the menu's own Escape press closed it (the original acts on the release)");
            InputWorld(EscapeEvent(true, echo: true));
            Require(MainMenuOpen, "an Escape echo closed the menu");
            InputWorld(EscapeEvent(false));
            Require(!MainMenuOpen && _mainMenu is null, "the menu's own Escape release did not close it, or the release reopened the menu");
            InputWorld(EscapeEvent(false));
            Require(!MainMenuOpen && _mainMenu is null, "a release with no press before it opened the menu");
            InputWorld(EscapeEvent(true)); InputWorld(EscapeEvent(false));
            Require(MainMenuOpen, "a second press and release opened the menu");
            InventoryKey(Key.Escape);
            Require(!MainMenuOpen && _mainMenu is null && !WorldModal, "Escape continued play after the release step");
            InventoryKey(Key.Escape);
            Require(MainMenuOpen && WorldModal && !_worldGate.IsOpen && _mainMenu!.Window == AreaMenuWindow.InPlay && ReferenceEquals(running, _areaSession),
                "Escape opened the in-play menu modal");
            Require(Buttons().SequenceEqual(["Save Game  (S)", "Load Game  (L)", "Options  (O)", "Quit  (Q)", "Continue  (C)"]), "in-play buttons: " + string.Join("|", Buttons()));
            InventoryKey(Key.Escape);
            Require(!MainMenuOpen && _mainMenu is null && !WorldModal, "Escape continued play");
            InventoryKey(Key.Escape); Letter('c');
            Require(!MainMenuOpen && _mainMenu is null && ReferenceEquals(running, _areaSession), "Continue closed it");

            // Save Game: the save window opens once the game is drawn without the menu; a save ends both (SF4).
            InventoryKey(Key.Escape); Letter('s');
            for (var frame = 0; frame < 10 && !SaveListOpen; frame++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            Require(SaveListOpen && !MainMenuOpen && _mainMenu is not null && _saveList!.Mode == AreaSaveListMode.Save, "Save Game opened the save window");
            InventoryKey(Key.Enter);
            Require(!SaveListOpen && !MainMenuOpen && _mainMenu is null && library.List().Entries.Count == 1 && !WorldModal, "a save ended the menu: " + _quickSaveStatus);
            // Load Game: closing the window unused brings the in-play window back.
            InventoryKey(Key.Escape); Letter('l');
            Require(SaveListOpen && _saveList!.Mode == AreaSaveListMode.Load && _mainMenu is not null, "Load Game opened the load window");
            InputWorld(EscapeEvent(true));
            Require(SaveListOpen && !MainMenuOpen, "the load window's Escape press closed it (the original acts on the release)");
            InputWorld(EscapeEvent(false));
            Require(!SaveListOpen && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlay, "closing it unused brought the in-play menu back");

            // Quit asks first: N and Escape keep playing; Y ends the game for the start scene and the reopened main menu.
            Letter('q');
            Require(_mainMenu!.QuitPending && Buttons().SequenceEqual(["Yes  (Y)", "No  (N)"]), "the quit box asks: " + string.Join("|", Buttons()));
            Letter('n');
            Require(MainMenuOpen && !_mainMenu!.QuitPending && ReferenceEquals(running, _areaSession), "N kept playing");
            Letter('q'); InputWorld(EscapeEvent(true));
            Require(MainMenuOpen && _mainMenu!.QuitPending, "the quit box's Escape press answered (the yes/no box 0x51EBF0 acts on the release)");
            InputWorld(EscapeEvent(false));
            Require(MainMenuOpen && !_mainMenu!.QuitPending && _mainMenu.Window == AreaMenuWindow.InPlay, "Escape answered no");
            InventoryKey(Key.Escape);
            Require(!MainMenuOpen && ReferenceEquals(running, _areaSession), "Escape continued play");
            InventoryKey(Key.Escape);
            var loads = _sceneLoadsForSelfTest;
            Letter('q');
            // ARC-INPUT-2648 (yes/no box 0x51EBF0 answers on the release): a lone release and the press do nothing, the release of the press says yes.
            InputWorld(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
            InputWorld(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = true });
            Require(_sceneLoadsForSelfTest == loads && MainMenuOpen && _mainMenu!.QuitPending, "the Return press and a lone release left the quit box open");
            InputWorld(new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = false });
            Require(_sceneLoadsForSelfTest == loads + 1 && !ReferenceEquals(running, _areaSession) && _areaSession!.QueryHero() is null
                && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && _mainMenu.Reopened && library.List().Entries.Count == 1,
                "Quit loaded the start scene with a fresh session and reopened the main menu, the save kept");
            Require(Buttons().SequenceEqual(["Single Player  (S)", "Options  (O)", "Exit Game  (E)"]), "the main menu's buttons: " + string.Join("|", Buttons()));

            // A new game on a scene that is not the start scene (here a --sector start) loads the start scene and starts there.
            _arguments = _arguments! with { AreaMapSector = new AreaSectorCoordinate(1, 1) };
            var placeholder = _areaSession;
            Letter('s'); Letter('n'); Letter('p');
            Require(_mainMenu!.Window == AreaMenuWindow.PickCharacter && _mainMenu.NewGameRefusal is null, "Pick Character offered away from the start scene");
            MainMenuCommand(() => _mainMenu!.Start());
            Require(_sceneLoadsForSelfTest == loads + 2 && !MainMenuOpen && _mainMenu is null && !ReferenceEquals(placeholder, _areaSession)
                && _areaSession!.QueryHero()?.Prototype == AreaStartingHero.Premade[0].BasicPrototype + Arcanum.Data.Characters.PremadeHeroReader.PrototypeOffset && _arguments!.AreaMapSector is null
                && _quickSaveStatus == "new game as halfling-male", "the start scene loaded and the new game began there: " + _quickSaveStatus);
            // A later publish of the scene (a window move, a transfer, a reload) opens no menu over play.
            await _sceneLoadForSelfTest!(_arguments!);
            Require(!MainMenuOpen && _mainMenu is null && !WorldModal, "a later publish opened no menu");

            // Quit again: the reopened main menu's root Escape exits the game (MM6; here only counted).
            InventoryKey(Key.Escape); Letter('q'); Letter('y');
            Require(MainMenuOpen && _mainMenu!.Reopened && _areaSession!.QueryHero() is null, "the second quit reopened the main menu");
            InventoryKey(Key.Escape);
            Require(_mainMenuExitsForSelfTest == 1, "Escape on the reopened root exited");
            return "Escape opens the in-play window (Escape/Continue close it), Save Game saves and ends it, Load Game back unused, " +
                "Quit asks (N, Escape keep playing; Return yes) and loads the start scene with the main menu reopened (save kept), " +
                "a new game off the start scene loads it and starts there (no menu at a later publish), reopened root Escape exits";
        }
        finally
        {
            if (SaveListOpen) EndSaveList();
            if (_mainMenu is not null) EndMainMenu("self-test end");
            _saveLibraryForSelfTest = null; _quickKeys.Reset(); _heroLoaderForSelfTest = null; _mainMenuExitForSelfTest = null;
            _sceneLoadForSelfTest = null; _launchArguments = null; _sceneFlow = null;
            _worldFocusSimulationForSelfTest = simulatedFocus;
            System.IO.Directory.Delete(_mainMenuDirectoryForSelfTest, recursive: true);
            _mainMenuDirectoryForSelfTest = null;
        }
    }
}
