using System.Reflection;
using Arcanum.Data.Area;
using Arcanum.Input;
using GamepadLab.Core;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.TerrainViewer.Core;
using Arcanum.TerrainViewer.Tests;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public static partial class TerrainViewerSelfTest
{
    /// <summary>ARC-UI-013 over the strict synthetic AREA owner in the production app launched without the menu (no preference file:
    /// an automated run): Options in the in-play window opens the options window at its Game page with the project's texts; each arrow
    /// changes its setting at once (difficulty on the session, the turn-based setting, auto attack, Always Run, a volume); Escape does
    /// nothing there, Done and the O key close the menu; from the main menu Done goes back to it.</summary>
    private static async Task AssertAreaOptionsAsync(Node host)
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
            app.PublishSyntheticForSelfTest(arguments, result);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            summary = app.AreaOptionsForSelfTest(arguments);
        }
        finally
        {
            app.ClearSyntheticForSelfTest();
            viewport.Free();
        }
        GD.Print($"ARC-UI-013 self-test PASS synthetic options window ({summary})");
    }
}

public partial class TerrainViewerApp
{
    internal string AreaOptionsForSelfTest(TerrainViewerArguments arguments)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA options: " + why); }
        if (!SelfTestBootstrap || _readyArea is null || _areaSession is null || _mainMenuDirectoryForSelfTest is null)
            throw new InvalidOperationException("AREA_OPTIONS_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true;
        var before = (_gameSettings, _turnBasedSetting, _alwaysRunOption, _audioVolumes, _ruleMode);
        var clock = 100.0;
        _controllerClockForSelfTest = () => clock;
        _padIdentityForSelfTest = id => id == 7 ? new PadIdentity("DualSense Wireless Controller", "030000004c050000e60c000000000000") : new PadIdentity("Xbox Wireless Controller", "030000005e040000130b000001050000");
        void Letter(char letter)
        {
            var key = (Key)char.ToUpperInvariant(letter);
            InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = letter, Pressed = true });
            InputWorld(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = letter, Pressed = false });
        }
        string[] Controls() => [.. _mainMenuPanel!.ControlsForSelfTest];
        try
        {
            Require(_gameSettingsStore is null or { Persistent: false } && _gameSettings == AreaGameSettings.Defaults && !_turnBasedSetting,
                "an automated run starts from the original's defaults without a file");
            // In play: Escape, then the in-play window's Options (its letter O) opens window 6 at the Game page.
            InventoryKey(Key.Escape); Letter('o');
            Require(MainMenuOpen && WorldModal && _mainMenu!.Window == AreaMenuWindow.Options && _mainMenuPanel!.HeaderForSelfTest == "Options: Game",
                "Options opened the options window at the Game page: " + _mainMenuPanel?.HeaderForSelfTest);
            var panel = _mainMenuPanel!;
            Require(panel.ButtonsForSelfTest.SequenceEqual(["Game  (G)", "Video  (V)", "Sound  (S)", "Done  (D)"]), "page buttons: " + string.Join("|", panel.ButtonsForSelfTest));
            Require(Controls().SequenceEqual(["Difficulty: Moderate", "Violence Filter: Off", "Default Combat Mode: Real Time", "Auto Attack: On", "Auto Switch Weapons: Off", "Always Run: Off", "Follower Skills: On", "Rules: Steamwoven"]),
                "Game page: " + string.Join("|", Controls()));
            Require(!panel.NoteForSelfTest.Contains("Video: ", StringComparison.Ordinal) && !panel.NoteForSelfTest.Contains("Violence Filter: ", StringComparison.Ordinal) && panel.NoteForSelfTest.Contains("Default Combat Mode: ", StringComparison.Ordinal),
                "hidden controls are named: " + panel.NoteForSelfTest);

            // Each arrow applies at once: the difficulty on the session, the turn-based setting, auto attack, Always Run.
            InventoryKey(Key.Right);
            Require(_gameSettings.Difficulty == 2 && _areaSession!.Difficulty == GameDifficulty.Hard && Controls()[0] == "Difficulty: Hard", "Right made it hard on the session");
            panel.ChangeForSelfTest(0, 1);
            Require(_gameSettings.Difficulty == 0 && _areaSession!.Difficulty == GameDifficulty.Easy, "the list wrapped to easy");
            // ARC-UI-1737 (OH2): the Violence Filter is the Game page's second line, off at the start; the AI host reads it (an automated run has no file; the smoke checks the saved file).
            Require(!_gameSettings.ViolenceFilter && (_aiHost is null || !_aiHost.ViolenceFilter), "the violence filter starts off");
            panel.ChangeForSelfTest(1, 1);
            Require(_gameSettings.ViolenceFilter && Controls()[1] == "Violence Filter: On" && (_aiHost is null || _aiHost.ViolenceFilter),
                "the violence filter is on and reaches the AI host: " + string.Join("|", Controls()));
            panel.ChangeForSelfTest(1, 1);
            Require(!_gameSettings.ViolenceFilter && Controls()[1] == "Violence Filter: Off" && (_aiHost is null || !_aiHost.ViolenceFilter), "the list wrapped back to off");
            panel.ChangeForSelfTest(2, 1);
            Require(_turnBasedSetting && _gameSettings.TurnBased && Controls()[2] == "Default Combat Mode: Turn Based", "turn-based on: " + _mainMenu!.Notice);
            panel.ChangeForSelfTest(2, 1);
            Require(!_turnBasedSetting && !_gameSettings.TurnBased, "fast turn-based is skipped: back to real time");
            panel.ChangeForSelfTest(3, -1);
            panel.ChangeForSelfTest(5, 1);
            Require(!_gameSettings.AutoAttack && _alwaysRunOption && Controls()[3] == "Auto Attack: Off" && Controls()[5] == "Always Run: On", "auto attack off, Always Run on");
            // ARC-UI-014: the rule mode switches the session in play, both ways.
            panel.ChangeForSelfTest(7, 1);
            Require(_ruleMode == RuleMode.Original && _areaSession!.Rules.Mode == RuleMode.Original && Controls()[7] == "Rules: Original", "Original on the session");
            panel.ChangeForSelfTest(7, 1);
            Require(_ruleMode == RuleMode.Steamwoven && _areaSession!.Rules.Mode == RuleMode.Steamwoven && Controls()[7] == "Rules: Steamwoven", "the list wrapped to Steamwoven");

            // ARC-UI-146: the HD lines (HD graphics, HD interface, HD pack) are no longer in this window: the Game page ends with Rules (ARC-UI-1737: the Controls line and the evidence log's two lines are the Video page's).
            Require(Controls().Length == 8 && !Controls().Any(line => line.StartsWith("HD ", StringComparison.Ordinal)), "no HD line: " + string.Join("|", Controls()));

            // ARC-UI-021: the Video page's floating text level and speed apply at once.
            Letter('v');
            Require(_mainMenu!.Page == AreaOptionsPage.Video && Controls().SequenceEqual(["Brightness: 0/9", "Floats: Minimal", "Float Speed: 2/10", "Combat Taunts: On", "Controls: Original", "Prompt style: Auto", "Evidence Log: Off", "Test Task: None"]), "Video page: " + string.Join("|", Controls()));
            Require(panel.NoteForSelfTest.Contains("Text Duration: ", StringComparison.Ordinal), "its hidden controls are named: " + panel.NoteForSelfTest);
            // ARC-UI-1737 (OH1): Brightness is the Video page's first line, 0 to 9; the final colour layer shows at 1 and above with the factor 1.0 + 0.1 x value, hides at 0.
            Require(_brightnessLayer is null || !_brightnessLayer.Visible, "brightness 0 draws no layer: today's picture");
            panel.ChangeForSelfTest(0, 1);
            Require(_gameSettings.Brightness == 1 && Controls()[0] == "Brightness: 1/9" && _brightnessLayer is { Visible: true, Layer: AreaBrightnessLayer.TopLayer } && Math.Abs(_brightnessLayer.Gain - 1.1f) < 1e-4f,
                "brightness 1 shows the layer with the factor 1.1: " + string.Join("|", Controls()));
            for (var i = 0; i < 8; i++) panel.ChangeForSelfTest(0, 1);
            Require(_gameSettings.Brightness == 9 && Controls()[0] == "Brightness: 9/9" && Math.Abs(_brightnessLayer!.Gain - 1.9f) < 1e-4f, "brightness 9, factor 1.9: " + Controls()[0]);
            panel.ChangeForSelfTest(0, 1);
            Require(_gameSettings.Brightness == 9, "the slider stops at 9 (the factor 2.0 is out of range in the original)");
            for (var i = 0; i < 9; i++) panel.ChangeForSelfTest(0, -1);
            Require(_gameSettings.Brightness == 0 && !_brightnessLayer!.Visible && Controls()[0] == "Brightness: 0/9", "back at 0 the layer is hidden again");
            panel.ChangeForSelfTest(1, 1);
            panel.ChangeForSelfTest(2, -1);
            Require(_gameSettings.Floats == 2 && _gameSettings.FloatSpeed == 1 && Controls()[1] == "Floats: Verbose" && Controls()[2] == "Float Speed: 1/10",
                "floats verbose, speed 1: " + string.Join("|", Controls()));
            // ARC-AI-1527: Combat Taunts is the Video page's fifth slot (OP2), on at the start; the list wraps and is saved at once.
            panel.ChangeForSelfTest(3, 1);
            Require(!_gameSettings.CombatTaunts && Controls()[3] == "Combat Taunts: Off", "taunts off: " + string.Join("|", Controls()));
            panel.ChangeForSelfTest(3, 1);
            Require(_gameSettings.CombatTaunts && Controls()[3] == "Combat Taunts: On", "the list wrapped back to on: " + string.Join("|", Controls()));

            // ARC-GPAD-1374, ARC-GPAD-1463: the Controls line (the Game page's) and the Prompt style line (the Video page's) switch at run time with no restart
            // and no window is closed; the strip follows the device in hand.
            Require(Controllers is { Device: ActiveDevice.KeyboardMouse } && PromptStripForSelfTest.Length == 0, "the mouse and keyboard show no prompt line");
            void Hand(InputEvent input) { ObserveControllerInput(input); clock += .3; SyncControllerPrompts(); }
            Hand(new InputEventJoypadButton { Device = 7, ButtonIndex = JoyButton.A, Pressed = true });
            Require(Controllers.Device == ActiveDevice.Pad && Controllers.PadDeviceId == 7 && Controllers.Family == GlyphFamily.PlayStation
                && PromptStripForSelfTest.StartsWith("PlayStation pad", StringComparison.Ordinal) && PromptStripForSelfTest.Contains("original controls", StringComparison.Ordinal),
                "pad 7 is in hand, PlayStation glyphs: " + PromptStripForSelfTest);
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Options, "a change of device resets no window");
            panel.ChangeForSelfTest(5, 1);
            Require(_gameSettings.Prompts == PromptStyle.Xbox && Controls()[5] == "Prompt style: Xbox" && Controllers.Family == GlyphFamily.Xbox, "the override wins: " + Controls()[5]);
            panel.ChangeForSelfTest(5, -1); panel.ChangeForSelfTest(5, -1);
            Require(_gameSettings.Prompts == PromptStyle.SteamDeck && Controls()[5] == "Prompt style: Steam Deck", "a list wraps back to its last value");
            panel.ChangeForSelfTest(5, 1);
            Require(_gameSettings.Prompts == PromptStyle.Auto && Controllers.Family == GlyphFamily.PlayStation, "Auto follows the pad again");
            Require(_mainMenu!.Page == AreaOptionsPage.Video && Controls()[4] == "Controls: Original", "the Video page holds the Controls line before the prompt style: " + string.Join("|", Controls()));
            panel.ChangeForSelfTest(4, 1);
            Require(_gameSettings.Pad == PadScheme.Direct && _gameSettings.Touch == TouchScheme.Off && Controls()[4] == "Controls: A Direct" && Controllers.Settings.Pad == PadScheme.Direct,
                "Controls: A Direct at once: " + Controls()[4]);
            clock += .3; SyncControllerPrompts();
            Require(PromptStripForSelfTest == "PlayStation pad - A Direct", "the strip follows the switch: " + PromptStripForSelfTest);
            panel.ChangeForSelfTest(4, 1);
            Require(_gameSettings.Pad == PadScheme.Pointer && Controls()[4] == "Controls: B Pointer", "B Pointer");
            Hand(new InputEventScreenTouch { Index = 0, Position = new(100, 100), Pressed = true });
            Require(Controllers.Device == ActiveDevice.Touch && PromptStripForSelfTest.StartsWith("Touch", StringComparison.Ordinal) && PromptStripForSelfTest.Contains("original controls", StringComparison.Ordinal),
                "a touch is in hand: " + PromptStripForSelfTest);
            panel.ChangeForSelfTest(4, 1);
            Require(_gameSettings.Touch == TouchScheme.TouchPad && _gameSettings.Pad == PadScheme.Off && Controls()[4] == "Controls: C Touch+pad", "Controls: C Touch+pad (no pad scheme beside it)");
            clock += .3; SyncControllerPrompts();
            Require(PromptStripForSelfTest == "Touch - C Touch+pad", "touch line: " + PromptStripForSelfTest);
            foreach (var shown in new[] { "mouse", "keyboard", "click", "Esc" })
                Require(!PromptStripForSelfTest.Contains(shown, StringComparison.OrdinalIgnoreCase), "no mouse or keyboard glyph on touch: " + PromptStripForSelfTest);
            Hand(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(300, 300) });
            clock += 1; Hand(new InputEventKey { Keycode = Key.A, PhysicalKeycode = Key.A, Pressed = true });
            Require(Controllers.Device == ActiveDevice.KeyboardMouse && PromptStripForSelfTest.Length == 0, "the mouse takes the strip away: " + PromptStripForSelfTest);
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Options, "the window stayed open through every device change");
            panel.ChangeForSelfTest(4, 1);
            Require(_gameSettings.Pad == PadScheme.Off && _gameSettings.Touch == TouchScheme.Off && Controls()[4] == "Controls: Original", "the list wraps back to Original");

            // The Sound page: a volume moves its channel; a slider stops at its end.
            Letter('s');
            Require(_mainMenu!.Page == AreaOptionsPage.Sound && Controls().SequenceEqual(["Effects: 5/10", "Voice: 5/10", "Music: 5/10"]), "Sound page: " + string.Join("|", Controls()));
            panel.ChangeForSelfTest(2, -1);
            Require(_audioVolumes.Music == 4 && Controls()[2] == "Music: 4/10", "music down");
            for (var i = 0; i < 6; i++) panel.ChangeForSelfTest(0, 1);
            Require(_audioVolumes.Effects == 10, "the effects slider stops at 10");

            // Escape does nothing here; Done closes the menu from play; the O key does too.
            InventoryKey(Key.Escape);
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Options, "Escape did nothing in the options window");
            Letter('d');
            Require(!MainMenuOpen && _mainMenu is null && !WorldModal, "Done closed the menu from play");
            InventoryKey(Key.Escape); Letter('o');
            Require(_mainMenu?.Window == AreaMenuWindow.Options && panel.HeaderForSelfTest == "Options: Game", "the window opens at the Game page again");
            InventoryKey(Key.O);
            Require(!MainMenuOpen && _mainMenu is null, "the O key closed the menu from play");

            // From the main menu: Options, then Done goes back to it.
            OpenMainMenu(MainMenuTexts(arguments));
            Letter('o');
            Require(_mainMenu?.Window == AreaMenuWindow.Options, "Options from the main menu");
            InventoryKey(Key.O);
            Require(_mainMenu?.Window == AreaMenuWindow.Options, "the O key does nothing from the main menu");
            Letter('d');
            Require(MainMenuOpen && _mainMenu!.Window == AreaMenuWindow.Main, "Done went back to the main menu");
            return "in-play Options opens window 6 at Game, arrows apply difficulty/turn-based/auto attack/Always Run/rule mode/floaters/volume at once, "
                + "the controller scheme lines switch at run time and the prompt strip follows the device in hand (pad family, touch, mouse), "
                + "lists wrap and sliders stop, Escape does nothing, Done and O close the menu from play, Done goes back from the main menu";
        }
        finally
        {
            if (_mainMenu is not null) EndMainMenu("self-test end");
            (_gameSettings, _turnBasedSetting, _alwaysRunOption, _audioVolumes, _ruleMode) = before;
            ApplyDisplayOptions(); // ARC-UI-1737: the brightness layer and the AI host follow the restored settings
            ResetControllersForSelfTest();
            if (_areaSession is { } current) ApplySessionSettings(current);
            _saveLibraryForSelfTest = null; _quickKeys.Reset(); _heroLoaderForSelfTest = null; _mainMenuExitForSelfTest = null;
            _worldFocusSimulationForSelfTest = simulatedFocus;
            System.IO.Directory.Delete(_mainMenuDirectoryForSelfTest, recursive: true);
            _mainMenuDirectoryForSelfTest = null;
        }
    }
}
