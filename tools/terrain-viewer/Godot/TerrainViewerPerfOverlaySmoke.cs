using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-DEV-3389, ARC_AREA_MAP_SMOKE_ONLY=report): through the production key route Ctrl+F shows the overlay and
    // hides it again, a held Ctrl+F does not flicker it, over the open logbook it toggles and a bare F does not, the shown panel reports
    // numbers, takes no pointer, stays inside the window and records nothing in the session.
    private async Task RunAreaPerfOverlaySmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-DEV-3389 perf overlay smoke FAIL: " + why); throw new InvalidOperationException("ARC-DEV-3389 perf overlay smoke: " + why); }
        if (_areaSession is null) { GD.Print("ARC-DEV-3389 native-smoke perf-overlay NONE (no game session)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true;
        var panelPutAway = false; var simulatedBars = _gameBarForSelfTest;
        try
        {
            _worldFocused = true;
            if (WorldModal && PaletteOpen) ToggleWorldSettings();
            Require(!WorldModal && _perfMeter is null, "the overlay is hidden at the start and no window is open: " + BugReportWindows());
            void Send(bool pressed, bool ctrl, bool echo = false)
                => InputWorld(new InputEventKey { Keycode = Key.F, PhysicalKeycode = Key.F, CtrlPressed = ctrl, Pressed = pressed, Echo = echo });
            void Chord() { Send(true, ctrl: true); Send(false, ctrl: true); }
            async Task Frames(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                }
            }
            // ARC_PERF_OVERLAY_CAPTURE: a folder for pictures of the game before and with the overlay.
            var capture = System.Environment.GetEnvironmentVariable("ARC_PERF_OVERLAY_CAPTURE");
            async Task Shot(string name)
            {
                if (string.IsNullOrEmpty(capture) || DisplayServer.GetName() == "headless") return;
                await Frames(2);
                System.IO.Directory.CreateDirectory(capture);
                GetViewport().GetTexture().GetImage()?.SavePng(System.IO.Path.Combine(capture, name + ".png"));
            }
            // For the pictures: the interface bars on and the automated run's diagnostic panel put away, as in a player's game.
            if (!string.IsNullOrEmpty(capture))
            {
                _gameBarForSelfTest = true;
                if (_hud is { PanelShown: true }) { _hud.TogglePanel(); panelPutAway = true; }
                await Frames(30);
                GD.Print($"ARC-DEV-3389 perf overlay capture bars={GameBarActive} diagnostic={_hud?.PanelShown}");
            }
            await Frames(3);
            await Shot("before");
            var revision = _areaSession.Version.Revision;
            Chord();
            Require(_perfMeter is not null && _perfPanel is { IsShown: true }, "Ctrl+F showed the overlay");
            Require(_areaSession.Version.Revision == revision, "the chord records nothing in the session");
            Require(!WorldModal && !FateOpen, "Ctrl+F opened no window of the game");
            // Past two refreshes (each 500 ms) so the rates have two samples.
            var until = Time.GetTicksMsec() + 1600;
            while (Time.GetTicksMsec() < until) await Frames(1);
            var panel = _perfPanel!;
            var text = panel.TextForSelfTest;
            Require(text.StartsWith("FPS ", StringComparison.Ordinal) && !text.StartsWith("FPS ...", StringComparison.Ordinal)
                && !text.Contains("frame ms   avg n/a", StringComparison.Ordinal) && !text.Contains("alloc n/a", StringComparison.Ordinal)
                && !text.Contains("resident n/a", StringComparison.Ordinal), "the overlay reports numbers: " + text.Replace('\n', '|'));
            Require(panel.TakesNoPointerForSelfTest, "the overlay takes no click");
            var rect = panel.RectForSelfTest; var visible = GetViewport().GetVisibleRect();
            Require(visible.Encloses(rect), $"the overlay lies inside the window: {rect} in {visible}");
            Require(rect.Position.Y >= AreaGameBarPanel.TopInset, $"the overlay stays below the top bar: {rect} inset {AreaGameBarPanel.TopInset}");
            await Shot("shown");
            // A held chord toggles once: its echoes and release are the overlay's.
            Send(true, ctrl: true); Send(true, ctrl: true, echo: true); Send(true, ctrl: true, echo: true); Send(false, ctrl: false);
            Require(_perfMeter is null && !panel.IsShown, "a held Ctrl+F hid the overlay once");
            Chord(); Require(panel.IsShown, "Ctrl+F shows it again"); Chord(); Require(!panel.IsShown && _perfMeter is null, "Ctrl+F hides it");
            // Over a window that owns every key (the logbook): the chord still reaches the overlay, a bare F does not.
            void Logbook(bool pressed) => InputWorld(new InputEventKey { Keycode = Key.L, PhysicalKeycode = Key.L, Pressed = pressed });
            Logbook(true); Logbook(false); await Frames(1);
            Require(LogbookOpen, "L opened the logbook");
            Send(true, ctrl: false); Send(false, ctrl: false);
            Require(_perfMeter is null, "a bare F does not show the overlay");
            Chord(); Require(panel.IsShown && LogbookOpen, "Ctrl+F shows the overlay over the logbook");
            Chord(); Require(!panel.IsShown && LogbookOpen, "Ctrl+F hides it and leaves the logbook open");
            Logbook(true); Logbook(false); await Frames(1);
            Require(!LogbookOpen, "L closed the logbook");
            GD.Print("ARC-DEV-3389 native-smoke perf-overlay PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = CurrentAreaMap, Text = text.Split('\n') }));
        }
        finally
        {
            if (_perfMeter is not null) TogglePerfOverlay();
            if (panelPutAway) _hud?.TogglePanel();
            _gameBarForSelfTest = simulatedBars;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
