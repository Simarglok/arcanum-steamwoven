using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC_AREA_MAP_SMOKE_ONLY=scene, map mode on a map that is not the start map, with --main-menu true), on a
    // temporary save folder: the menu over that map offers a new game; Start loads the start map (the map list's runtime map 1)
    // through the ordinary load path and begins the game there as the first pre-made hero. In play, Escape opens the in-play
    // window with the game's texts; Save Game saves and ends it; Quit asks, and Y loads the start map again with a fresh session
    // without a hero and the main menu reopened over it (the save kept), whose root Escape exits (counted here, not quit).
    private async Task RunAreaSceneFlowSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-012 scene flow smoke: " + why); }
        var directory = System.IO.Directory.CreateTempSubdirectory("arc-ui-012-").FullName;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        _saveLibraryForSelfTest = new(directory);
        var exits = 0;
        _mainMenuExitForSelfTest = () => exits++;
        try
        {
            void Key(Key key, char unicode = '\0')
            {
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false });
            }
            void Choose(int message)
            {
                var button = _mainMenu!.Shown.FirstOrDefault(shown => shown.Message == message) ?? throw new InvalidOperationException("ARC-UI-012 scene flow smoke: no button " + message);
                var letter = _mainMenu.Texts.Hotkey(button) ?? throw new InvalidOperationException("ARC-UI-012 scene flow smoke: no letter for " + message);
                Key(letter is >= 'A' and <= 'Z' ? (Key)letter : Godot.Key.None, char.ToLowerInvariant(letter));
            }
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            async Task SceneLoaded() { await (_lastLoad ?? Task.CompletedTask); await Frames(3); }

            var launched = _readyMapName;
            Require(_arguments?.MainMenu == true && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && !_mainMenu.Reopened,
                "the menu is open over the map (run with --main-menu true)");
            Require(CurrentAreaMap != 1 && StartSceneRefusal() is not null && _mainMenu!.NewGameRefusal is null,
                $"the launch map {launched} is not the start map, and a new game is offered there");
            var texts = _mainMenu!.Texts;
            Require(texts.FromGame && texts.Text(30).Length > 0 && texts.Text(33).Length > 0 && texts.Text(AreaMainMenuTexts.QuitQuestion).Length > 0,
                "the in-play texts come from the game: " + texts.Detail);
            var placeholder = _areaSession;
            Choose(10); Choose(50); Choose(420);
            Require(_mainMenu!.Window == AreaMenuWindow.PickCharacter, "the pre-made browser opened");
            var first = MainMenuHero(0);
            Require(first.Status == AreaHeroStatus.Ready && first.Definition is not null, "the first real hero reads: " + first.Detail);
            MainMenuCommand(() => _mainMenu!.Start());
            await SceneLoaded();
            var start = _readyMapName;
            Require(CurrentAreaMap == 1 && start != launched && !MainMenuOpen && !ReferenceEquals(placeholder, _areaSession)
                && _areaSession!.QueryHero()?.Prototype == first.Definition!.Prototype, $"the start map {start} loaded and the game began there: {_quickSaveStatus}");
            var game = _areaSession;

            // In play: Escape opens the in-play window; Save Game saves and ends it.
            Key(Godot.Key.Escape);
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.InPlay && _mainMenu.Shown.Select(button => button.Message).SequenceEqual([30, 31, 32, 33, 34]),
                "Escape opened the in-play window");
            Choose(30);
            for (var frame = 0; frame < 30 && !SaveListOpen; frame++) await Frames(1);
            Require(SaveListOpen && _saveList!.Mode == AreaSaveListMode.Save, "Save Game opened the save window");
            await SaveListArtForSmoke(Require);
            Key(Godot.Key.Enter);
            Require(!SaveListOpen && !MainMenuOpen && _mainMenu is null && _saveLibraryForSelfTest.List().Entries.Count == 1, "the save ended the menu: " + _quickSaveStatus);

            // Quit: the box asks; Y loads the start map again with a fresh session and the main menu reopened.
            Key(Godot.Key.Escape); Choose(33);
            Require(_mainMenu!.QuitPending, "the quit box asks");
            Key(Godot.Key.Y, 'y');
            await SceneLoaded();
            Require(_readyMapName == start && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && _mainMenu.Reopened
                && !ReferenceEquals(game, _areaSession) && _areaSession!.QueryHero() is null && _saveLibraryForSelfTest.List().Entries.Count == 1,
                "Quit loaded the start map with a fresh session and reopened the main menu, the save kept");
            Key(Godot.Key.Escape);
            Require(exits == 1, "Escape on the reopened root exits the game");
            GD.Print("ARC-UI-012 native-smoke scene flow PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Texts = texts.Detail, Launched = launched, Start = start, Hero = first.Hero.Name, first.Definition!.Prototype, Saves = 1,
                Revision = _areaSession!.Version.Revision }));
        }
        finally
        {
            if (SaveListOpen) EndSaveList();
            if (_mainMenu is not null) EndMainMenu("smoke end");
            _saveLibraryForSelfTest = null; _quickKeys.Reset(); _mainMenuExitForSelfTest = null;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }
}
