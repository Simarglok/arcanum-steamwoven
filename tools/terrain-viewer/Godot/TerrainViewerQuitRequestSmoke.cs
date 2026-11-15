using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-DEV-2270, ARC_AREA_MAP_SMOKE_ONLY=report): a close request asks the quit question (the original's dialog box, message
    // 5100) over the game and freezes it; Escape or N answers no and the game goes on; Y answers yes (the process end is the smoke's stand-in);
    // other keys are swallowed; a held key (a stream of close requests) quits without the question while the setting is on and keeps asking
    // while it is off; the question can be asked and answered again and again.
    private async Task RunAreaQuitSmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-DEV-2270 quit smoke FAIL: " + why); throw new InvalidOperationException("ARC-DEV-2270 quit smoke: " + why); }
        if (_areaSession is null) { GD.Print("ARC-DEV-2270 native-smoke quit NONE (no game session)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var settings = _launcherSettings;
        _worldFocusSimulationForSelfTest = true;
        var ends = new List<string>();
        long clock = 10_000;
        async Task Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
        void Push(Key key) { GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true }); GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false }); }
        try
        {
            _worldFocused = true;
            if (WorldModal && PaletteOpen) ToggleWorldSettings();
            Require(!WorldModal && !QuitAsking, "no window is open before the question: " + BugReportWindows());
            _quitForSelfTest = how => ends.Add(how);
            _quitClockForSelfTest = () => clock;
            _launcherSettings = settings with { HoldToQuit = true };
            await Frames(2);
            var question = QuitTextsNow().Text(AreaMainMenuTexts.QuitQuestion);
            var revision = _areaSession.Version.Revision;

            HandleCloseRequest();
            await Frames(2);
            Require(QuitAsking && WorldModal && WorldMenuPause && ends.Count == 0, "a close request asks the question, makes the game modal and freezes it, and ends nothing");
            Require(_quitPanel!.QuestionForSelfTest == question, "the question is message 5100: " + _quitPanel.QuestionForSelfTest);
            Require(_quitPanel.InArt == _quitPanel.DialogForSelfTest.HasArt, "the original's dialog box asks when its art is read");
            // A second request while asking is not an answer; another key is swallowed.
            clock += 2_000; HandleCloseRequest();
            Push(Key.Z); Push(Key.Space); Push(Key.L);
            await Frames(1);
            Require(QuitAsking && ends.Count == 0 && !LogbookOpen, "a second close request and other keys change nothing under the question");
            Push(Key.Escape);
            await Frames(1);
            Require(!QuitAsking && !WorldModal && !WorldMenuPause && ends.Count == 0, "Escape answers no: the game goes on");
            Require(_areaSession.Version.Revision == revision, "asking and answering no left the session alone");

            // Again, and again: N answers no too.
            for (var round = 0; round < 3; round++)
            {
                clock += 5_000; HandleCloseRequest();
                await Frames(1);
                Require(QuitAsking, "the question is asked again, round " + round);
                Push(Key.N);
                await Frames(1);
                Require(!QuitAsking && !WorldModal && ends.Count == 0, "N answers no, round " + round);
            }
            // A click on the box's No (the real wiring) answers no as well.
            clock += 5_000; HandleCloseRequest();
            await Frames(1);
            _quitPanel.PressForSelfTest(false);
            await Frames(1);
            Require(!QuitAsking && ends.Count == 0, "the No button answers no");

            // Yes ends the game.
            clock += 5_000; HandleCloseRequest();
            await Frames(1);
            Push(Key.Y);
            await Frames(1);
            Require(ends.SequenceEqual(["yes"]) && !QuitAsking, "Y answers yes and the game ends: " + string.Join(",", ends));
            _quitGate.Answered(); ends.Clear();

            // A held key: the first request asks, the key's repeat delay passes, then a stream of repeats quits at once.
            clock += 5_000; HandleCloseRequest();
            clock += 450; HandleCloseRequest();
            var repeats = 0;
            while (ends.Count == 0 && repeats < 200) { clock += 30; repeats++; HandleCloseRequest(); }
            Require(ends.SequenceEqual(["held key"]) && repeats is >= 40 and <= 60, $"a held key quits without the answer: {string.Join(",", ends)} after {repeats} repeats");
            _quitGate.Answered(); ends.Clear(); _quitPanel.Dismiss();
            Require(!QuitAsking && !WorldModal, "the held quit left no question behind");

            // With the setting off the held key only asks.
            _launcherSettings = settings with { HoldToQuit = false };
            clock += 5_000; HandleCloseRequest();
            clock += 450; HandleCloseRequest();
            for (var i = 0; i < 200; i++) { clock += 30; HandleCloseRequest(); }
            await Frames(1);
            Require(QuitAsking && ends.Count == 0, "a held key keeps asking when the hold setting is off");
            Push(Key.Escape);
            await Frames(1);
            Require(!QuitAsking && !WorldModal, "and Escape answers no");
            GD.Print("ARC-DEV-2270 native-smoke quit PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = CurrentAreaMap, InArt = _quitPanel.DialogForSelfTest.HasArt }));
        }
        finally
        {
            if (QuitAsking) { _quitPanel!.Dismiss(); _quitGate.Answered(); }
            _quitForSelfTest = null; _quitClockForSelfTest = null; _launcherSettings = settings;
            if (IsWorld && _worldInitialized) { QuarantineWorld(); _quickKeys.Reset(); RefreshReadyStatus(); }
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
