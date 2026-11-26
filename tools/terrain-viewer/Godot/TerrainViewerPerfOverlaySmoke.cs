using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // Native observation (ARC-DEV-3389, ARC_AREA_MAP_SMOKE_ONLY=report): through the production key route Ctrl+F shows the overlay and
    // hides it again, a held Ctrl+F does not flicker it, over the open logbook it toggles and a bare F does not, the shown panel reports
    // numbers, takes no pointer, stays inside the window and records nothing in the session. In a window run on a macOS with the
    // metalperftrace tool the cycle goes through the Metal HUD too.
    private async Task RunAreaPerfOverlaySmokeAsync()
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-DEV-3389 perf overlay smoke FAIL: " + why); throw new InvalidOperationException("ARC-DEV-3389 perf overlay smoke: " + why); }
        if (_areaSession is null) { GD.Print("ARC-DEV-3389 native-smoke perf-overlay NONE (no game session)"); return; }
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused;
        _worldFocusSimulationForSelfTest = true;
        var panelPutAway = false; var simulatedBars = _gameBarForSelfTest;
        var startMode = _perfMode; var metalHud = _metalHud;
        try
        {
            _worldFocused = true;
            if (WorldModal && PaletteOpen) ToggleWorldSettings();
            Require(!WorldModal && _perfMeter is null, "the overlay is hidden at the start and no window is open: " + BugReportWindows());
            Require(metalHud == (DisplayServer.GetName() != "headless" && MetalHudSwitch.Available), "a window run with the tool offers the HUD");
            if (_perfMode != PerfOverlayMode.Off) ShowPerf(PerfOverlayMode.Off);
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
            // The HUD is drawn by the system over the window, so only a capture of the window itself shows it. The window is raised
            // for it: a covered window presents no frames.
            async Task WindowShot(string name)
            {
                if (string.IsNullOrEmpty(capture) || !metalHud) return;
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
                try
                {
                    // By the clock, not by frames: the HUD of a running process comes up a moment after the request.
                    var settled = Time.GetTicksMsec() + 3000;
                    while (Time.GetTicksMsec() < settled) await Frames(1);
                    var number = MetalHudSwitch.WindowNumberForSelfTest(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
                    System.IO.Directory.CreateDirectory(capture);
                    using var shot = System.Diagnostics.Process.Start("/usr/sbin/screencapture",
                        ["-x", "-o", $"-l{number}", System.IO.Path.Combine(capture, name + ".png")]);
                    shot.WaitForExit(10000);
                }
                finally { DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, false); }
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
            await WindowShot("window-off");
            var revision = _areaSession.Version.Revision;
            Chord();
            Require(_perfMeter is not null && _perfPanel is { IsShown: true }, "Ctrl+F showed the overlay");
            Require(_areaSession.Version.Revision == revision, "the chord records nothing in the session");
            Require(!WorldModal && !FateOpen, "Ctrl+F opened no window of the game");
            Require(_perfMode == PerfOverlayMode.Overlay, "the first step of the cycle is the panel");
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
            // A held chord steps once: its echoes and release are the overlay's.
            var afterPanel = metalHud ? PerfOverlayMode.MetalHud : PerfOverlayMode.Off;
            Send(true, ctrl: true); Send(true, ctrl: true, echo: true); Send(true, ctrl: true, echo: true); Send(false, ctrl: false);
            Require(_perfMode == afterPanel && _perfMeter is null && !panel.IsShown, "a held Ctrl+F left the panel by one step");
            Require(_metalHudProblem is null, "the system tool accepts the request: " + _metalHudProblem);
            if (metalHud)
            {
                await WindowShot("window-hud");
                Chord();
                Require(_perfMode == PerfOverlayMode.Off && _perfMeter is null && !panel.IsShown, "Ctrl+F on the HUD turns everything off");
            }
            Chord(); Require(panel.IsShown, "Ctrl+F shows the panel again");
            ShowPerf(PerfOverlayMode.Off); Require(!panel.IsShown && _perfMeter is null, "the panel is gone");
            // Over a window that owns every key (the logbook): the chord still reaches the overlay, a bare F does not.
            void Logbook(bool pressed) => InputWorld(new InputEventKey { Keycode = Key.L, PhysicalKeycode = Key.L, Pressed = pressed });
            Logbook(true); Logbook(false); await Frames(1);
            Require(LogbookOpen, "L opened the logbook");
            Send(true, ctrl: false); Send(false, ctrl: false);
            Require(_perfMeter is null, "a bare F does not show the overlay");
            Chord(); Require(panel.IsShown && LogbookOpen, "Ctrl+F shows the overlay over the logbook");
            Chord(); Require(!panel.IsShown && _perfMode == afterPanel && LogbookOpen, "Ctrl+F leaves the panel and keeps the logbook open");
            await WindowShot("window-hud-again");
            if (metalHud) { Chord(); Require(_perfMode == PerfOverlayMode.Off && _metalHudProblem is null, "the HUD goes off again: " + _metalHudProblem); }
            await WindowShot("window-off-again");
            Logbook(true); Logbook(false); await Frames(1);
            Require(!LogbookOpen, "L closed the logbook");
            GD.Print("ARC-DEV-3389 native-smoke perf-overlay PASS " + System.Text.Json.JsonSerializer.Serialize(new { Map = CurrentAreaMap, MetalHud = metalHud, Text = text.Split('\n') }));
        }
        finally
        {
            if (_perfMode != startMode) ShowPerf(startMode);
            if (panelPutAway) _hud?.TogglePanel();
            _gameBarForSelfTest = simulatedBars;
            _worldFocusSimulationForSelfTest = simulatedFocus; _worldFocused = focused;
        }
    }
}
