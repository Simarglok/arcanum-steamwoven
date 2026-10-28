using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-011 over the strict synthetic AREA owner in the production app with <c>--main-menu true</c>, on a temporary save
    /// folder and synthetic heroes: the menu opens modal over the published scene with only the offered buttons; letters choose,
    /// Escape goes back (not at the root); "Last Save" with no save stays; the pre-made browser cycles and Start begins a new game
    /// under a new session with that hero; "Last Save" loads the newest save; "Load Game" opens the load window over the waiting
    /// menu, which comes back when it closes unused and ends when it loads; "Exit Game" exits.</summary>
    private static async Task AssertAreaMainMenuAsync(Node host)
    {
        using var source = new AreaOwnerFixture(motionBodies: true);
        var read = typeof(TerrainLoadCoordinator).Assembly.GetType("Arcanum.TerrainViewer.Core.AreaLoadOperation")!
            .GetMethod("ReadArea", BindingFlags.NonPublic | BindingFlags.Static)!;
        var area = (AreaLoadResult)read.Invoke(null, [7L, source.Owner, CancellationToken.None, null])!;
        var steps = AreaOwnerFixture.Steps(area.Snapshot, source.Streams[new(AreaSourceRole.Module)].Bytes, 7);
        source.Owner.Dispose();
        var result = SyntheticActorFixture.Create(human: true, area: true).Result with { Generation = 7, Area = area, AreaStepSnapshot = steps };
        var arguments = TerrainViewerArguments.Parse(["--mode", "crash-site-area", "--module", "synthetic-module",
            "--resource", "synthetic-terrain", "--actor-resource", "synthetic-actor", "--prototypes", "synthetic-prototypes",
            "--area-mobile", "synthetic-mobile", "--area-effects", "synthetic-effects", "--area-patch", "synthetic-patch", "--source-id", "synthetic",
            "--main-menu", "true"]);
        var viewport = new SubViewport { Size = new(800, 600), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var app = new TerrainViewerApp { SelfTestBootstrap = true };
        viewport.AddChild(app); host.AddChild(viewport);
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        app.SetProcess(false); app.SetProcessInput(false);
        string summary;
        try
        {
            app.PrepareMainMenuForSelfTest();
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = app.AreaMainMenuForSelfTest();
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-011 self-test PASS synthetic main menu ({summary})");
    }
}

public partial class TerrainViewerApp
{
    private string? _mainMenuDirectoryForSelfTest;
    private int _mainMenuExitsForSelfTest;

    /// <summary>Before the publication that opens the menu: a temporary save folder, synthetic heroes and an exit that only counts.</summary>
    internal void PrepareMainMenuForSelfTest()
    {
        if (!SelfTestBootstrap) throw new InvalidOperationException("AREA_MAIN_MENU_SELF_TEST_ONLY");
        _mainMenuDirectoryForSelfTest = System.IO.Directory.CreateTempSubdirectory("arc-ui-011-").FullName;
        _saveLibraryForSelfTest = new(_mainMenuDirectoryForSelfTest);
        _heroLoaderForSelfTest = SyntheticHero;
        _mainMenuExitForSelfTest = () => _mainMenuExitsForSelfTest++;
    }

    /// <summary>A synthetic pre-made hero: the premade's prototype, a half-elf level 1 with 385 gold and one kit item.</summary>
    private static AreaHeroLoad SyntheticHero(AreaPremadeHero premade)
    {
        var hero = new SessionHeroDefinition(premade.BasicPrototype + Arcanum.Data.Characters.PremadeHeroReader.PrototypeOffset, CharacterRace.HalfElf,
            CharacterGender.Male, [8, 8, 8, 8, 8, 8, 8, 8, 0, 0], 1, 5, 0, null, null, 385, [new SessionHeroKitItem(new(SessionItemType.Weapon, 4027, 6000, 6001))]);
        return new(AreaHeroStatus.Ready, premade, hero, 2350 + premade.Index, 0, null, "synthetic");
    }

    internal string AreaMainMenuForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA main menu: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null || _mainMenuDirectoryForSelfTest is null)
            throw new InvalidOperationException("AREA_MAIN_MENU_READY_REQUIRED");
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
            // Opened by the publication: modal, the root window with the offered buttons and the hidden ones named.
            var placeholder = _areaSession;
            Require(MainMenuOpen && WorldModal && !_worldGate.IsOpen && _mainMenu!.Window == AreaMenuWindow.Main, "the menu opened modal over the scene");
            Require(Buttons().SequenceEqual(["Single Player  (S)", "Exit Game  (E)"]), "root buttons: " + string.Join("|", Buttons()));
            Require(_mainMenuPanel!.NoteForSelfTest.Contains("MultiPlayer: ", StringComparison.Ordinal) && _mainMenuPanel.NoteForSelfTest.Contains("Options: ", StringComparison.Ordinal)
                && _mainMenuPanel.NoteForSelfTest.Contains("Credits: ", StringComparison.Ordinal), "the hidden buttons are named: " + _mainMenuPanel.NoteForSelfTest);
            Require(GetSelfTestSnapshot().Hud.Contains(" | main menu open", StringComparison.Ordinal), "HUD open status");
            // The root ignores Escape; a letter of a hidden button (M) or of no button (Q) does nothing; the menu owns F7 and L.
            InventoryKey(Key.Escape); Letter('m'); Letter('q'); QuickKey(Key.F7); InventoryKey(Key.L);
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && !library.AutoSlot.Exists && !LogbookOpen, "Escape, M, Q, F7 and L did nothing");

            // Single player: "Last Save" with no save stays on the window.
            Letter('s');
            Require(_mainMenu!.Window == AreaMenuWindow.SinglePlayer && Buttons().SequenceEqual(["New Game  (N)", "Load Game  (L)", "Last Save  (S)", "Cancel  (C)"]),
                "single player buttons: " + string.Join("|", Buttons()));
            Letter('S');
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.SinglePlayer && ReferenceEquals(placeholder, _areaSession)
                && _mainMenuPanel.NoticeForSelfTest == "no save to continue from", "Last Save with no save stayed: " + _mainMenuPanel.NoticeForSelfTest);
            InventoryKey(Key.Escape);
            Require(_mainMenu!.Window == AreaMenuWindow.Main, "Escape went back to the root");

            // New game: the browser cycles through the twelve heroes; Start begins a new game with the browsed one.
            Letter('s'); Letter('n');
            Require(_mainMenu!.Window == AreaMenuWindow.NewGame && Buttons().SequenceEqual(["Pick Character  (P)", "Cancel  (X)"])
                && _mainMenuPanel.NoteForSelfTest.Contains("New Character: ", StringComparison.Ordinal), "new game buttons: " + string.Join("|", Buttons()));
            Letter('p');
            Require(_mainMenu!.Window == AreaMenuWindow.PickCharacter && _mainMenuPanel.HeroForSelfTest.StartsWith("1/12 halfling-male\n", StringComparison.Ordinal),
                "the browser starts at the first hero: " + _mainMenuPanel.HeroForSelfTest);
            Letter('n');
            Require(_mainMenu!.Window == AreaMenuWindow.PickCharacter && _mainMenu!.Hero == 0, "the browser takes no letters");
            MainMenuCommand(() => { _mainMenu!.Browse(-1); return AreaMenuCommand.Nothing; });
            Require(_mainMenuPanel.HeroForSelfTest.StartsWith("12/12 half-elf-female\n", StringComparison.Ordinal), "Previous wrapped to the last hero");
            MainMenuCommand(() => { _mainMenu!.Browse(1); _mainMenu!.Browse(1); _mainMenu!.Browse(1); return AreaMenuCommand.Nothing; });
            Require(_mainMenuPanel.HeroForSelfTest.StartsWith("3/12 half-elf-male\n", StringComparison.Ordinal), "Next browsed on: " + _mainMenuPanel.HeroForSelfTest);
            MainMenuCommand(() => _mainMenu!.Start());
            Require(!MainMenuOpen && _mainMenu is null && !WorldModal && !ReferenceEquals(placeholder, _areaSession)
                && _areaSession!.QueryHero()?.Prototype == 16069 + Arcanum.Data.Characters.PremadeHeroReader.PrototypeOffset
                && _areaSession.QueryGold() == 385 && _quickSaveStatus == "new game as half-elf-male", "Start began a new game as the third hero: " + _quickSaveStatus);
            Require(GetSelfTestSnapshot().Hud.Contains(" | main menu: new game as half-elf-male", StringComparison.Ordinal), "HUD closed status");
            Require(_menuTablesHero?.Name == "half-elf-male", "the picked hero's tables were adopted");

            // "Last Save" loads the newest save and ends the menu.
            QuickKey(Key.F7);
            Require(library.AutoSlot.Exists, "F7 saved the new game: " + _quickSaveStatus);
            OpenMainMenu(AreaMainMenuTexts.Project("self-test"));
            var beforeLast = _areaSession; var heroPrototype = _areaSession!.QueryHero()?.Prototype;
            Letter('s'); Letter('s');
            Require(!MainMenuOpen && !ReferenceEquals(beforeLast, _areaSession) && _areaSession!.QueryHero()?.Prototype == heroPrototype
                && _quickSaveStatus?.StartsWith("Last save quick loaded", StringComparison.Ordinal) == true, "Last Save loaded the auto-save: " + _quickSaveStatus);
            Require(_menuTablesHero?.Name == "half-elf-male", "the loaded hero's tables are adopted with it");

            // "Load Game": the load window opens over the waiting menu; Escape brings the menu back; Return loads and ends it.
            OpenMainMenu(AreaMainMenuTexts.Project("self-test"));
            Letter('s'); Letter('l');
            Require(SaveListOpen && !MainMenuOpen && _mainMenu is not null && _saveList!.Mode == AreaSaveListMode.Load && WorldModal, "Load Game opened the load window");
            InventoryKey(Key.Escape);
            Require(!SaveListOpen && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.SinglePlayer && WorldModal, "closing it unused brought the menu back");
            var beforeLoad = _areaSession;
            Letter('l'); InventoryKey(Key.Enter);
            Require(!SaveListOpen && !MainMenuOpen && _mainMenu is null && !ReferenceEquals(beforeLoad, _areaSession) && !WorldModal, "a load from the window ended the menu");

            // A controller: down to Exit Game and confirm. "Exit Game" exits (here only counted).
            OpenMainMenu(AreaMainMenuTexts.Project("self-test"));
            static IntentFrame Pad(bool confirm, int direction) => new(default, default, confirm, false, false, false, false, false, false,
                direction, 0, InputMethod.Gamepad, true, "self-test") { MenuPressedDirection = direction };
            HandleMainMenuPulse(Pad(false, 1));
            HandleMainMenuPulse(Pad(true, 0));
            Require(_mainMenuExitsForSelfTest == 1, "Exit Game exited");
            return "opened modal by --main-menu with the offered buttons, letters and Escape (not at the root), Last Save without a save stays, " +
                "browser cycles, Start begins a new game as the hero, Last Save loads the newest, Load Game window over the waiting menu (back unused, ends on a load), " +
                "controller Exit";
        }
        finally
        {
            if (SaveListOpen) EndSaveList();
            if (_mainMenu is not null) EndMainMenu("self-test end");
            _saveLibraryForSelfTest = null; _quickKeys.Reset(); _heroLoaderForSelfTest = null; _mainMenuExitForSelfTest = null;
            _worldFocusSimulationForSelfTest = simulatedFocus;
            System.IO.Directory.Delete(_mainMenuDirectoryForSelfTest, recursive: true);
            _mainMenuDirectoryForSelfTest = null;
        }
    }
}
