using Arcanum.Presentation.Display;
using Arcanum.Input;
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
    // ARC-UI-1737 (AC3): the final colour multiply in the drawn frame. Samples of the open Video page are read at brightness 0 twice (the pixels that agree are the
    // stable ones: the world behind the window may animate), then at 5 and 9: each stable sample must be BrightnessRules.Apply of its value at 0, and after the slider
    // returns to 0 the frame is the first one again byte for byte (no layer is drawn at 0).
    private async Task BrightnessPixelCheck(Func<int, string?> setBrightness)
    {
        static void Require(bool value, string why)
        {
            if (value) return;
            GD.Print("ARC-UI-1737 brightness smoke FAILED: " + why);
            throw new InvalidOperationException("ARC-UI-1737 brightness smoke: " + why);
        }
        async Task Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        (int R, int G, int B)[] Sample()
        {
            using var image = GetViewport().GetTexture().GetImage();
            var points = new List<(int, int, int)>();
            const int Columns = 64, Rows = 48;
            for (var row = 0; row < Rows; row++)
                for (var column = 0; column < Columns; column++)
                {
                    var color = image.GetPixel((column * 2 + 1) * image.GetWidth() / (Columns * 2), (row * 2 + 1) * image.GetHeight() / (Rows * 2));
                    points.Add(((int)Math.Round(color.R * 255), (int)Math.Round(color.G * 255), (int)Math.Round(color.B * 255)));
                }
            return [.. points];
        }
        Require(_gameSettings.Brightness == 0 && (_brightnessLayer is null || !_brightnessLayer.Visible), "the setting starts at 0 with no layer");
        await Frames(3);
        var first = Sample();
        await Frames(2);
        var second = Sample();
        // A stable sample with a colour a multiply can change (not black, not already white in every channel).
        var stable = Enumerable.Range(0, first.Length).Where(i => first[i] == second[i] && (first[i].R + first[i].G + first[i].B) > 0 && (first[i].R < 255 || first[i].G < 255 || first[i].B < 255)).ToArray();
        Require(stable.Length >= 200, "enough stable coloured samples to compare: " + stable.Length);
        foreach (var value in new[] { 5, 9 })
        {
            Require(setBrightness(value) is null && _gameSettings.Brightness == value && _brightnessLayer is { Visible: true }, "the setting moved to " + value);
            await Frames(3);
            var lit = Sample();
            await Frames(2);
            var litAgain = Sample();
            // The frame under the layer may still animate a little (a highlight, a light, a pixel that flickers by one level): a sample counts only when two frames at this brightness agree too,
            // and up to 1 % of those may still miss (a wrong multiply would miss nearly all of them).
            var steady = stable.Where(i => lit[i] == litAgain[i]).ToArray();
            var wrong = steady.Where(i => lit[i] != (BrightnessRules.Apply(first[i].R, value), BrightnessRules.Apply(first[i].G, value), BrightnessRules.Apply(first[i].B, value))).ToArray();
            var changed = steady.Count(i => lit[i] != first[i]);
            Require(steady.Length >= stable.Length * 9 / 10 && wrong.Length <= steady.Length / 100 && changed > steady.Length / 2,
                $"brightness {value}: {wrong.Length} of {steady.Length} steady samples ({stable.Length} stable at 0) differ from the ramp (first {(wrong.Length > 0 ? $"{first[wrong[0]]} -> {lit[wrong[0]]}" : "-")}), {changed} changed");
            GD.Print($"ARC-UI-1737 brightness {value}: {steady.Length - wrong.Length} of {steady.Length} steady samples equal the ramp, {changed} changed");
        }
        Require(setBrightness(0) is null && _gameSettings.Brightness == 0 && !_brightnessLayer!.Visible, "the setting is back at 0, the layer hidden");
        await Frames(3);
        var again = Sample();
        var differ = stable.Count(i => again[i] != first[i]);
        Require(differ <= stable.Length / 100, "brightness 0 again is the first frame: " + differ + " of " + stable.Length + " samples differ");
        GD.Print($"ARC-UI-1737 brightness 0 again: {stable.Length - differ} of {stable.Length} samples equal the first frame");
    }

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
            Require(Controls().SequenceEqual(["Difficulty: Moderate", "Violence Filter: Off", "Default Combat Mode: Real Time", "Auto Attack: On", "Auto Switch Weapons: Off", "Always Run: Off", "Follower Skills: On", "Rules: Steamwoven"]), "Game page: " + string.Join("|", Controls()));
            // ARC-UI-061: the shown controls take the first slots: four in the first column, the fifth to the eighth in the second (as drawn).
            await Frames(1);
            Require(art.ControlPlacesForSelfTest.SequenceEqual(Enumerable.Range(0, 8).Select(AreaMenuArtLayout.OptionsSlot)),
                "the Game page's controls stand in their art at the first slots: " + string.Join(" ", art.ControlPlacesForSelfTest));
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_OPTIONS_CAPTURE") is { Length: > 0 } gameCapture)
            {
                using var image = GetViewport().GetTexture().GetImage();
                var path = System.IO.Path.ChangeExtension(gameCapture, null) + "-game" + System.IO.Path.GetExtension(gameCapture);
                Require(image.SavePng(path) == Error.Ok, "the Game page's capture saved to " + path);
            }
            // ARC-UI-014: Original chosen in the main menu is the rule mode the new game begins in.
            panel.ChangeForSelfTest(7, 1);
            Require(_ruleMode == RuleMode.Original && Controls()[7] == "Rules: Original", "Original chosen before the game");
            // ARC-COMBAT-1777 (OH4): Follower Skills is the Game page's seventh line, on until written; it changes at once and is saved in the settings file.
            Require(_gameSettings.FollowerSkills && Controls()[6] == "Follower Skills: On", "follower skills starts on: " + Controls()[6]);
            panel.ChangeForSelfTest(6, -1);
            Require(!_gameSettings.FollowerSkills && Controls()[6] == "Follower Skills: Off" && _gameSettingsStore is { Persistent: true } && !_gameSettingsStore.Load().FollowerSkills, "the Follower Skills line turned the setting off and saved it: " + Controls()[6]);
            panel.ChangeForSelfTest(6, 1);
            Require(_gameSettings.FollowerSkills && Controls()[6] == "Follower Skills: On" && _gameSettingsStore?.Load().FollowerSkills == true, "and on again: " + Controls()[6]);
            // ARC-UI-1737 (OH2): the Violence Filter is the Game page's second line; it is saved in the smoke's own settings file at once.
            panel.ChangeForSelfTest(1, 1);
            Require(_gameSettings.ViolenceFilter && Controls()[1] == "Violence Filter: On" && _gameSettingsStore is { Persistent: true } && _gameSettingsStore.Load().ViolenceFilter,
                "the violence filter is on and saved: " + Controls()[1]);
            panel.ChangeForSelfTest(1, 1);
            Require(!_gameSettings.ViolenceFilter && !_gameSettingsStore!.Load().ViolenceFilter, "and off again");
            // ARC-COMBAT-1738 (OH3): Auto Switch Weapons is the Game page's fifth line, off until written; it changes at once and is saved in the settings file.
            Require(!_gameSettings.AutoSwitchWeapons, "auto switch starts off");
            panel.ChangeForSelfTest(4, 1);
            Require(_gameSettings.AutoSwitchWeapons && Controls()[4] == "Auto Switch Weapons: On" && _gameSettingsStore is { Persistent: true } && _gameSettingsStore.Load().AutoSwitchWeapons,
                "the Auto Switch Weapons line turned the setting on and saved it: " + Controls()[4]);
            panel.ChangeForSelfTest(4, 1);
            Require(!_gameSettings.AutoSwitchWeapons && Controls()[4] == "Auto Switch Weapons: Off" && _gameSettingsStore?.Load().AutoSwitchWeapons == false, "and off again: " + Controls()[4]);
            // The evidence log's line is the Video page's last (ARC-UI-1737; ARC-UI-1797: the log and its task marker are one line, Off, On, Task 1 to 8).
            Key(Godot.Key.V, 'v');
            Require(_mainMenu!.Page == AreaOptionsPage.Video && Controls()[1] == "Text Duration: 6/10" && Controls()[7] == "Evidence Log: Off", "the Video page holds Text Duration and the evidence log's one line: " + string.Join("|", Controls()));
            // ARC-UI-1797: Text Duration is shown, changes at once and is saved in the settings file (no text bubble reads it yet).
            panel.ChangeForSelfTest(1, 1);
            Require(_gameSettings.TextDuration == 7 && Controls()[1] == "Text Duration: 7/10" && _gameSettingsStore is { Persistent: true } && _gameSettingsStore.Load().TextDuration == 7, "Text Duration 7 is saved: " + Controls()[1]);
            panel.ChangeForSelfTest(1, -1);
            Require(_gameSettings.TextDuration == 6 && _gameSettingsStore?.Load().TextDuration == 6, "and back at the original's default 6");
            // ARC-GPAD-1376: the evidence log is off at the start, a session writes its closed lines to the device's own folder and ends cleanly.
            Require(!EvidenceRecording && _evidenceTask == 0, "the evidence log starts off");
            panel.ChangeForSelfTest(7, 1);
            Require(EvidenceRecording && _evidenceTask == 0 && Controls()[7] == "Evidence Log: On", "the Evidence Log line turned the log on");
            panel.ChangeForSelfTest(7, 1);
            Require(_evidenceTask == 1 && Controls()[7] == "Evidence Log: Task 1", "the next step started task 1");
            RecordEvidence(EvidenceCommand.Examine, EvidenceOutcome.WrongTarget);
            panel.ChangeForSelfTest(7, -1);
            Require(EvidenceRecording && _evidenceTask == 0 && Controls()[7] == "Evidence Log: On", "back to On: the task ended, the log runs");
            panel.ChangeForSelfTest(7, -1);
            Require(!EvidenceRecording && _evidenceTask == 0 && Controls()[7] == "Evidence Log: Off", "the line turned it off again");
            var evidenceFiles = System.IO.Directory.GetFiles(System.IO.Path.Combine(ViewerAutomatedStorage.Folder(), "evidence"), "evidence-*.jsonl");
            Require(evidenceFiles.Length == 1, "one evidence file in the isolated folder: " + evidenceFiles.Length);
            var evidenceLines = System.IO.File.ReadAllLines(evidenceFiles[0]);
            var evidenceEvents = evidenceLines.Skip(1).Select(EvidenceLogCodec.Parse).ToArray();
            Require(evidenceLines[0] == EvidenceLogCodec.Header && evidenceEvents.All(e => e is not null)
                && evidenceEvents.Select(e => e!.Value.Kind).SequenceEqual([EvidenceKind.SessionStart, EvidenceKind.TaskStart, EvidenceKind.Command, EvidenceKind.TaskEnd, EvidenceKind.SessionEnd])
                && evidenceEvents[2]!.Value is { Command: EvidenceCommand.Examine, Outcome: EvidenceOutcome.WrongTarget, Task: 1, Scheme: EvidenceScheme.Original },
                "the file holds the session's closed lines: " + string.Join(" | ", evidenceLines));
            // ARC-GPAD-1463: the Controls line (the Video page's, ARC-UI-1737) changes at run time, no restart, and is saved at once in the smoke's own settings
            // file; ARC-GPAD-1374: the prompt style is the Video page's. The strip shows nothing while the mouse is in hand and names the pad when one is used.
            Require(_mainMenu!.Page == AreaOptionsPage.Video && Controls()[5] == "Controls: Original", "the Video page holds the Controls line before the prompt style: " + string.Join("|", Controls()));
            panel.ChangeForSelfTest(5, 1);
            Require(Controls()[5] == "Controls: A Direct" && _gameSettings is { Pad: Arcanum.Input.PadScheme.Direct, Touch: Arcanum.Input.TouchScheme.Off },
                "the Controls line changed at once: " + Controls()[5]);
            Require(_gameSettingsStore is { Persistent: true } && _gameSettingsStore.Load() is { Pad: Arcanum.Input.PadScheme.Direct, Touch: Arcanum.Input.TouchScheme.Off },
                "the Controls line is saved in the settings file");
            Key(Godot.Key.V, 'v');
            Require(_mainMenu!.Page == AreaOptionsPage.Video && Controls().Length == 8 && Controls()[0] == "Brightness: 0/9" && Controls()[4] == "Combat Taunts: On" && Controls()[5] == "Controls: A Direct" && Controls()[6] == "Prompt style: Auto", "the Video page holds the brightness, the text duration, the floats, the taunts, the prompt style and the evidence log: " + string.Join("|", Controls()));
            panel.ChangeForSelfTest(6, 1); panel.ChangeForSelfTest(6, 1);
            Require(Controls()[6] == "Prompt style: PlayStation" && _gameSettings.Prompts == Arcanum.Input.PromptStyle.PlayStation
                && _gameSettingsStore?.Load().Prompts == Arcanum.Input.PromptStyle.PlayStation, "the prompt style changed and is saved: " + Controls()[6]);
            // ARC-UI-1737 (OH1): the Brightness slider is the Video page's first line; the pixels are checked below, on the Sound page (the Video page redraws its own slider).
            Require(Controls()[0] == "Brightness: 0/9", "the Brightness slider starts at 0: " + Controls()[0]);
            Require(PromptStripForSelfTest.Length == 0 && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Options, "the mouse shows no strip and no window was reset");
            // A pad in hand (device 7, a DualSense: never device 0): after the hysteresis the strip names it with its family, over the open window.
            var clock = 1000.0;
            _controllerClockForSelfTest = () => clock;
            _padIdentityForSelfTest = _ => new Arcanum.Input.PadIdentity("DualSense Wireless Controller", "030000004c050000e60c000000000000");
            ObserveControllerInput(new InputEventJoypadButton { Device = 7, ButtonIndex = JoyButton.A, Pressed = true });
            clock += .3; await Frames(2);
            Require(PromptStripForSelfTest == "PlayStation pad - A Direct" && MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Options,
                "a pad in hand shows its family over the open window: " + PromptStripForSelfTest);
            if (System.Environment.GetEnvironmentVariable("ARC_AREA_OPTIONS_CAPTURE") is { Length: > 0 } padCapture)
            {
                using var image = GetViewport().GetTexture().GetImage();
                var path = System.IO.Path.ChangeExtension(padCapture, null) + "-pad" + System.IO.Path.GetExtension(padCapture);
                Require(image.SavePng(path) == Error.Ok, "the pad strip's capture saved to " + path);
            }
            ObserveControllerInput(new InputEventKey { Keycode = Godot.Key.A, PhysicalKeycode = Godot.Key.A, Pressed = true });
            clock += .3; await Frames(2);
            Require(PromptStripForSelfTest.Length == 0, "a key takes the strip away: " + PromptStripForSelfTest);
            _controllerClockForSelfTest = null; _padIdentityForSelfTest = null;
            panel.ChangeForSelfTest(6, 1); panel.ChangeForSelfTest(6, 1); panel.ChangeForSelfTest(6, 1);
            Require(_gameSettings.Prompts == Arcanum.Input.PromptStyle.Auto, "the prompt style wrapped back to Auto: " + Controls()[6]);
            Require(_mainMenu!.Page == AreaOptionsPage.Video, "still on the Video page");
            panel.ChangeForSelfTest(5, 1); panel.ChangeForSelfTest(5, 1);
            Require(_gameSettings is { Pad: Arcanum.Input.PadScheme.Off, Touch: Arcanum.Input.TouchScheme.TouchPad } && Controls()[5] == "Controls: C Touch+pad", "C Touch+pad: " + Controls()[5]);
            panel.ChangeForSelfTest(5, 1);
            Require(_gameSettings is { Pad: Arcanum.Input.PadScheme.Off, Touch: Arcanum.Input.TouchScheme.Off } && Controls()[5] == "Controls: Original", "back to Original: " + Controls()[5]);
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
            // ARC-UI-1737 (OH1, FR-LIGHT-003): the Brightness setting is the final colour multiply. The same frame is read at 0 (no layer: today's picture), 5 and 9; every
            // steady pixel must equal BrightnessRules.Apply of its value at 0 (the multiply is the original's ramp: channel x (1 + 0.1 x value), whole, capped at white).
            await BrightnessPixelCheck(value => ApplyOptionChange(AreaOption.Brightness, value));
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
            panel.ChangeForSelfTest(2, 1);
            Require(_turnBasedSetting && Controls()[2] == "Default Combat Mode: Turn Based", "the hero's turn-based switch took it: " + _mainMenu!.Notice + " " + _turnBasedNotice);
            panel.ChangeForSelfTest(2, 1);
            Require(!_turnBasedSetting && Controls()[2] == "Default Combat Mode: Real Time", "back to real time");
            panel.ChangeForSelfTest(7, -1);
            Require(_areaSession!.Rules.Mode == RuleMode.Steamwoven && Controls()[7] == "Rules: Steamwoven", "Steamwoven again in play");
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
            ApplyDisplayOptions(); // ARC-UI-1737
            ResetControllersForSelfTest();
            if (_areaSession is { } current) ApplySessionSettings(current);
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }
}
