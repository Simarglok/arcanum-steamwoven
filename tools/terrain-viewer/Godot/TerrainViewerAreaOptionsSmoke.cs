using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC_AREA_MAP_SMOKE_ONLY=options, map mode on the start map with --main-menu true; an automated run, so no
    // preference file is read or written): the main menu's Options opens the options window with the game's labels and values at
    // the original's defaults; on the Sound page a music change moves the channel; Done goes back to the main menu. A new game as the
    // first pre-made hero, then in play Escape and Options: a difficulty change reaches the session, the default combat mode goes
    // through the hero's turn-based switch, and the O key closes the menu. The settings are restored at the end.
    private async Task RunAreaOptionsSmokeAsync()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("ARC-UI-013 options smoke: " + why); }
        var directory = System.IO.Directory.CreateTempSubdirectory("arc-ui-013-").FullName;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        _saveLibraryForSelfTest = new(directory);
        var before = (_gameSettings, _turnBasedSetting, _alwaysRunOption, _audioVolumes, _ruleMode);
        try
        {
            void Key(Key key, char unicode = '\0')
            {
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = true });
                InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = unicode, Pressed = false });
            }
            void Choose(int message)
            {
                var button = _mainMenu!.Shown.FirstOrDefault(shown => shown.Message == message) ?? throw new InvalidOperationException("ARC-UI-013 options smoke: no button " + message);
                var letter = _mainMenu.Texts.Hotkey(button) ?? throw new InvalidOperationException("ARC-UI-013 options smoke: no letter for " + message);
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
            string[] Controls() => [.. _mainMenuPanel!.ControlsForSelfTest];

            Require(_arguments?.MainMenu == true && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main && CurrentAreaMap == 1,
                "the menu is open over the start map (run with --main-menu true)");
            Require(_gameSettingsStore is { Persistent: false } && _gameSettings == AreaGameSettings.Defaults, "an automated run uses the original's defaults, no file");
            Choose(12);
            var texts = _mainMenu!.OptionsTexts;
            Require(_mainMenu.Window == AreaMenuWindow.Options && texts.FromGame && _mainMenuPanel!.HeaderForSelfTest == "Options: Game",
                $"Options opened the options window with the game's texts: {texts.Detail} {_mainMenuPanel?.HeaderForSelfTest}");
            var panel = _mainMenuPanel!;
            Require(panel.ButtonsForSelfTest.SequenceEqual(["Game  (G)", "Video  (V)", "Sound  (S)", "Done  (D)"]), "page buttons: " + string.Join("|", panel.ButtonsForSelfTest));
            Require(Controls().SequenceEqual(["Difficulty: Moderate", "Default Combat Mode: Real Time", "Auto Attack: On", "Always Run: Off", "Rules: Steamwoven"]), "Game page: " + string.Join("|", Controls()));
            // ARC-UI-014: Original chosen in the main menu is the rule mode the new game begins in.
            panel.ChangeForSelfTest(4, 1);
            Require(_ruleMode == RuleMode.Original && Controls()[4] == "Rules: Original", "Original chosen before the game");
            Choose(4002);
            Require(Controls().SequenceEqual(["Effects: 5/10", "Voice: 5/10", "Music: 5/10"]), "Sound page: " + string.Join("|", Controls()));
            panel.ChangeForSelfTest(2, -1);
            Require(_audioVolumes.Music == 4 && Controls()[2] == "Music: 4/10", "music down one step");
            panel.ChangeForSelfTest(2, 1);
            Choose(4003);
            Require(_mainMenu!.Window == AreaMenuWindow.Main && _audioVolumes.Music == 5, "Done went back to the main menu, music restored");

            // A new game, then in play: Escape, Options.
            Choose(10); Choose(50); Choose(420);
            var first = MainMenuHero(0);
            Require(_mainMenu!.Window == AreaMenuWindow.PickCharacter && first.Status == AreaHeroStatus.Ready, "the first real hero reads: " + first.Detail);
            MainMenuCommand(() => _mainMenu!.Start());
            await Frames(3);
            Require(!MainMenuOpen && _areaSession!.QueryHero() is not null && _areaSession.Difficulty == GameDifficulty.Normal && _areaSession.Rules.Mode == RuleMode.Original,
                "the new game began at moderate difficulty under the Original rules: " + _areaSession?.Rules.Name);
            Key(Godot.Key.Escape); Choose(32);
            Require(_mainMenu?.Window == AreaMenuWindow.Options && Controls()[0] == "Difficulty: Moderate", "Options from play opened at the Game page");
            Key(Godot.Key.Right);
            Require(_areaSession!.Difficulty == GameDifficulty.Hard && Controls()[0] == "Difficulty: Hard", "Right made the session's difficulty hard");
            panel.ChangeForSelfTest(1, 1);
            Require(_turnBasedSetting && Controls()[1] == "Default Combat Mode: Turn Based", "the hero's turn-based switch took it: " + _mainMenu!.Notice + " " + _turnBasedNotice);
            panel.ChangeForSelfTest(1, 1);
            Require(!_turnBasedSetting && Controls()[1] == "Default Combat Mode: Real Time", "back to real time");
            panel.ChangeForSelfTest(4, -1);
            Require(_areaSession!.Rules.Mode == RuleMode.Steamwoven && Controls()[4] == "Rules: Steamwoven", "Steamwoven again in play");
            Key(Godot.Key.O);
            Require(!MainMenuOpen && _mainMenu is null, "the O key closed the menu from play");
            await Frames(3);
            GD.Print("ARC-UI-013 native-smoke options PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Texts = texts.Detail, Map = _readyMapName, Hero = first.Hero.Name, Difficulty = _areaSession!.Difficulty.ToString(), Rules = _areaSession.Rules.Name, Revision = _areaSession.Version.Revision }));
        }
        finally
        {
            if (_mainMenu is not null) EndMainMenu("smoke end");
            if (_audioVolumes != before._audioVolumes) PaletteVolumeChanged(Arcanum.Presentation.Audio.AudioChannel.Music, before._audioVolumes.Music);
            (_gameSettings, _turnBasedSetting, _alwaysRunOption, _ruleMode) = (before._gameSettings, before._turnBasedSetting, before._alwaysRunOption, before._ruleMode);
            if (_areaSession is { } current) ApplySessionSettings(current);
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }
}
