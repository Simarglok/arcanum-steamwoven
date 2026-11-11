using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC_AREA_MAP_SMOKE_ONLY=options, map mode on the start map with --main-menu true; an automated run, so no
    // preference file is read or written): the main menu's Options opens the options window with the game's labels and values at
    // the original's defaults; on the Sound page a music change moves the channel; Done goes back to the main menu. A new game as the
    // first pre-made hero, then in play Escape and Options: a difficulty change reaches the session, the default combat mode goes
    // through the hero's turn-based switch, and the O key closes the menu. The settings are restored at the end. ARC-UI-059: the window
    // shows its background 556 and the page buttons in the original's look at (130, 221 + 40 i) (MW7), and a click on Sound's label shows
    // the Sound page; ARC_AREA_OPTIONS_CAPTURE saves an in-app readback of the Game page to the caller's ignored path. ARC-UI-061: the
    // controls stand in their art at the first slots (MW8–MW10), and the pointer on Music's left arrow lights it, a press shows it down
    // with sound 3000, and the release over it plays 3001 and lowers the music (a release away from it changes nothing); with ARC_AREA_OPTIONS_CAPTURE the Sound page is saved
    // too, next to it with "-sound" before the extension.
    private async Task RunAreaOptionsSmokeAsync()
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            // The scene's failure line names only the exception's type: the reason is printed first.
            GD.Print("ARC-UI-013 options smoke FAILED: " + why);
            throw new InvalidOperationException("ARC-UI-013 options smoke: " + why);
        }
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
            // ARC-DEV-061: the smoke runner points GAME_SETTINGS_TEST_PATH into the smoke's own folder, so the store may have a file there,
            // but nothing is saved in it yet.
            Require((_gameSettingsStore is { Persistent: false } || !string.IsNullOrWhiteSpace(OS.GetEnvironment(GameSettingsStore.TestVariable)))
                && _gameSettingsStore is { HasSaved: false } && _gameSettings == AreaGameSettings.Defaults,
                "an automated run starts at the original's defaults, with no file or only the isolated test file, nothing saved");
            Choose(12);
            var texts = _mainMenu!.OptionsTexts;
            Require(_mainMenu.Window == AreaMenuWindow.Options && texts.FromGame && _mainMenuPanel!.HeaderForSelfTest == "Options: Game",
                $"Options opened the options window with the game's texts: {texts.Detail} {_mainMenuPanel?.HeaderForSelfTest}");
            var panel = _mainMenuPanel!;
            // ARC-UI-059: the page buttons are the art's labels (no letters, as the original's) at the window's own points; 33 is the
            // installed Morph30 font's height (MW4).
            var art = panel.ArtForSelfTest;
            var places = art.ButtonsForSelfTest;
            Require(panel.ArtMode && _menuArtContent is { ArtReady: true } && panel.ButtonsForSelfTest.SequenceEqual(["Game", "Video", "Sound", "Done"]),
                "the options window shows the game's art and page labels: " + _menuArtContent?.Detail + " " + string.Join("|", panel.ButtonsForSelfTest));
            Require(places.Count == 4 && places.Select((place, slot) => place.Y == AreaMenuArtLayout.OptionsFirstButtonY + AreaMenuArtLayout.OptionsButtonStep * slot
                && place.X + place.Width / 2 == AreaMenuArtLayout.OptionsButtonX && place.Width > 0 && place.Height == 33).All(ok => ok),
                "the page labels stand centred at (130, 221 + 40 i): " + string.Join(" ", places));
            Require(art.HoveredForSelfTest == -1, "no page label is lit while the focus is on the first control: " + art.HoveredForSelfTest);
            Require(Controls().SequenceEqual(["Difficulty: Moderate", "Default Combat Mode: Real Time", "Auto Attack: On", "Always Run: Off", "Rules: Steamwoven"]), "Game page: " + string.Join("|", Controls()));
            // ARC-UI-061: the shown controls take the first slots: four in the first column, the fifth at the second's top (as drawn).
            await Frames(1);
            Require(art.ControlPlacesForSelfTest.SequenceEqual(Enumerable.Range(0, 5).Select(AreaMenuArtLayout.OptionsSlot)),
                "the Game page's controls stand in their art at the first slots: " + string.Join(" ", art.ControlPlacesForSelfTest));
            // ARC-UI-014: Original chosen in the main menu is the rule mode the new game begins in.
            panel.ChangeForSelfTest(4, 1);
            Require(_ruleMode == RuleMode.Original && Controls()[4] == "Rules: Original", "Original chosen before the game");
            // ARC-DEV-350: the HD pack problem box: the reason on screen, Continue closes it, Close game quits (the smoke takes the quit over).
            var quits = 0;
            _hdPackProblemQuitForSelfTest = () => quits++;
            ShowHdPackProblem("smoke problem text");
            Require(_hdPackProblemPanel is { IsOpen: true } box && box.MessageForSelfTest == "smoke problem text", "the HD pack problem box opened with its reason");
            _hdPackProblemPanel!.PressContinueForSelfTest();
            Require(!_hdPackProblemPanel.IsOpen && quits == 0, "Continue closed the box and went on");
            ShowHdPackProblem("again");
            _hdPackProblemPanel.PressCloseGameForSelfTest();
            Require(!_hdPackProblemPanel.IsOpen && quits == 1, "Close game asked to quit once");
            _hdPackProblemQuitForSelfTest = null;
            await Frames(2);
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_OPTIONS_CAPTURE") is { Length: > 0 } capture)
            {
                using var image = GetViewport().GetTexture().GetImage();
                Require(image.SavePng(capture) == Error.Ok, "the capture saved to " + capture);
            }
            var sound = panel.ButtonCentreForSelfTest(2) ?? throw new InvalidOperationException("ARC-UI-059 options smoke: no Sound label");
            GetViewport().PushInput(new InputEventMouseMotion { Position = sound, GlobalPosition = sound });
            await Frames(1);
            Require(art.HoveredForSelfTest == 2, "the pointer over Sound lights it: " + art.HoveredForSelfTest);
            foreach (var pressed in new[] { true, false })
            {
                GetViewport().PushInput(new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left, Pressed = pressed, Position = sound, GlobalPosition = sound, ButtonMask = pressed ? MouseButtonMask.Left : 0,
                });
                await Frames(1);
            }
            Require(_mainMenu!.Window == AreaMenuWindow.Options && _mainMenu.Page == AreaOptionsPage.Sound && panel.ArtMode
                && Controls().SequenceEqual(["Effects: 5/10", "Voice: 5/10", "Music: 5/10"]), "a click on Sound showed the Sound page: " + string.Join("|", Controls()));
            await Frames(1);
            Require(art.ControlPlacesForSelfTest.SequenceEqual([(261, 122), (261, 197), (261, 272)]), "the Sound page's sliders at slots 0..2: "
                + string.Join(" ", art.ControlPlacesForSelfTest));
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_OPTIONS_CAPTURE") is { Length: > 0 } soundCapture)
            {
                await Frames(2);
                using var image = GetViewport().GetTexture().GetImage();
                var path = System.IO.Path.ChangeExtension(soundCapture, null) + "-sound" + System.IO.Path.GetExtension(soundCapture);
                Require(image.SavePng(path) == Error.Ok, "the Sound page's capture saved to " + path);
            }
            // ARC-UI-061: Music's left arrow, a push button (GB3, GB4, GB6).
            var sounds = new List<int>();
            void Heard(int id) => sounds.Add(id);
            art.SoundRequested += Heard;
            try
            {
                var less = art.ArrowCentreForSelfTest(2, AreaOptionsArrow.Less) ?? throw new InvalidOperationException("ARC-UI-061 options smoke: no Music arrow");
                GetViewport().PushInput(new InputEventMouseMotion { Position = less, GlobalPosition = less });
                await Frames(1);
                Require(art.ArrowFrameForSelfTest(2, AreaOptionsArrow.Less) == 2 && art.ArrowFrameForSelfTest(2, AreaOptionsArrow.More) == 0 && sounds.Count == 0,
                    "the pointer over Music's left arrow lights it, silently: " + art.ArrowFrameForSelfTest(2, AreaOptionsArrow.Less) + " " + string.Join(",", sounds));
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = less, GlobalPosition = less, ButtonMask = MouseButtonMask.Left });
                await Frames(1);
                Require(art.ArrowFrameForSelfTest(2, AreaOptionsArrow.Less) == 1 && sounds.SequenceEqual([3000]) && _audioVolumes.Music == 5,
                    "a press shows it down with 3000 and changes nothing yet: " + string.Join(",", sounds));
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = less, GlobalPosition = less });
                await Frames(1);
            }
            finally { art.SoundRequested -= Heard; }
            Require(sounds.SequenceEqual([3000, 3001]) && _audioVolumes.Music == 4 && Controls()[2] == "Music: 4/10",
                "the release over it played 3001 and lowered the music one step: " + string.Join(",", sounds) + " " + _audioVolumes.Music);
            // A press on the arrow released away from it lets it go silently and changes nothing (GB3).
            sounds.Clear();
            art.SoundRequested += Heard;
            try
            {
                var less = art.ArrowCentreForSelfTest(2, AreaOptionsArrow.Less)!.Value;
                var away = less + new Vector2(0, 40 * art.Scale.Y);
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = less, GlobalPosition = less, ButtonMask = MouseButtonMask.Left });
                await Frames(1);
                GetViewport().PushInput(new InputEventMouseMotion { Position = away, GlobalPosition = away, ButtonMask = MouseButtonMask.Left });
                GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = away, GlobalPosition = away });
                await Frames(1);
            }
            finally { art.SoundRequested -= Heard; }
            Require(sounds.SequenceEqual([3000]) && _audioVolumes.Music == 4 && art.ArrowFrameForSelfTest(2, AreaOptionsArrow.Less) == 0,
                "a release away from the arrow changed nothing, silently, and left it idle: " + string.Join(",", sounds) + " " + _audioVolumes.Music);
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
                Texts = texts.Detail, Art = _menuArtContent!.Detail, Pages = places.Select(place => $"{place.X},{place.Y} {place.Width}x{place.Height}").ToArray(), Map = _readyMapName, Hero = first.Hero.Name, Difficulty = _areaSession!.Difficulty.ToString(), Rules = _areaSession.Rules.Name, Revision = _areaSession.Version.Revision }));
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
