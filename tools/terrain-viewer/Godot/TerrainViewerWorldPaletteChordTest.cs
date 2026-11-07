using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>ARC-INPUT-001 AC2/AC3: Ctrl+Shift+F9 and the WORLD keyboard route through
    /// the production callback, with independent literal expectations. SIMULATION:
    /// synthetic Godot InputEvents, not native OS delivery or a physical keyboard.</summary>
    internal void WorldPaletteChordMatrixForSelfTest()
    {
        RequireExplicitTest();
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD palette chord: " + why); }
        var none = new RawInputFrame(-1, default);
        void Frame() => WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() }, .1);
        void K(Key key, bool down, bool ctrl = false, bool shift = false, bool alt = false, bool meta = false, bool echo = false, KeyLocation location = KeyLocation.Unspecified)
            => _Input(new InputEventKey { Keycode = key, Pressed = down, Echo = echo, CtrlPressed = ctrl, ShiftPressed = shift, AltPressed = alt, MetaPressed = meta, Location = location });
        // A leak is any echo/release of a consumed F9 press that was not itself
        // consumed (it would reach native UI or a later original sink), or any F9
        // decision naming an original action (F9 has none in the manual, the quick reference or the source).
        int Leaks()
        {
            var leaks = 0; var owned = false;
            foreach (var d in _worldChord.Trace.Where(d => d.Key == WorldKey.F9))
            {
                if (d.OriginalAction is not null) leaks++;
                if (d.Pressed && !d.Echo) owned = d.Consumed;
                else { if (owned && !d.Consumed) leaks++; if (!d.Pressed) owned = false; }
            }
            return leaks;
        }
        int Toggles() => _worldChord.Trace.Count(d => d.TogglePalette);
        void Fresh()
        {
            if (WorldModal) ToggleWorldSettings();
            GetViewport().GuiReleaseFocus(); WorldFocusForSelfTest(true);
            SelectSimulatedPadForSelfTest(); _worldChord.Reset(); Frame(); Frame(); Frame();
            Require(_worldGate.IsOpen && !WorldModal, "fresh neutral world");
            _worldChord.ClearTrace();
        }
        var original = _worldActor.Session!;
        void Configure(StepDuration? duration = null)
        {
            var time = _worldTestTime ?? SimulationTime.At(0);
            if (original is AreaGridSession area) _worldActor.Configure(area.Snapshot, duration is null ? time : SimulationTime.At(0), duration);
            else _worldActor.Configure(((DiagnosticGridSession)original).Snapshot, duration is null ? time : SimulationTime.At(0), duration);
            _worldGridInput = new(); RetireWorldLifecycle();
        }
        Fresh();
        var actor = _worldActor.State; var camera = _camera!.State; var follow = _camera.FollowState;

        // 1. Chord press then release in the natural order: exactly one open.
        K(Key.Ctrl, true, ctrl: true); K(Key.Shift, true, ctrl: true, shift: true);
        K(Key.F9, true, ctrl: true, shift: true);
        Require(WorldModal && Toggles() == 1, "chord opened once");
        K(Key.F9, false, ctrl: true, shift: true); K(Key.Shift, false, ctrl: true); K(Key.Ctrl, false);
        Require(WorldModal && Toggles() == 1 && Leaks() == 0 && !_worldChord.Owned, "release consumed without a second action");

        // 2. Modifier-first release closes once; the late bare F9 release is still owned.
        K(Key.Shift, true, shift: true); K(Key.Ctrl, true, ctrl: true, shift: true);
        K(Key.F9, true, ctrl: true, shift: true);
        Require(!WorldModal && Toggles() == 2, "chord closed once");
        K(Key.Ctrl, false, shift: true); K(Key.Shift, false);
        Require(_worldChord.Owned, "consumed F9 lost ownership on modifier release");
        K(Key.F9, false);
        Require(!WorldModal && Toggles() == 2 && Leaks() == 0 && !_worldChord.Owned, "modifier-first release leaked F9");

        // 3. Echo/auto-repeat never toggles again; a fresh re-press is a new chord.
        K(Key.Ctrl, true, ctrl: true); K(Key.Shift, true, ctrl: true, shift: true);
        K(Key.F9, true, ctrl: true, shift: true);
        for (var i = 0; i < 4; i++) K(Key.F9, true, ctrl: true, shift: true, echo: true);
        Require(WorldModal && Toggles() == 3 && Leaks() == 0, "echo toggled the palette");
        K(Key.F9, false, ctrl: true, shift: true);
        K(Key.F9, true, ctrl: true, shift: true); K(Key.F9, false, ctrl: true, shift: true);
        Require(!WorldModal && Toggles() == 4, "held-modifier re-press is exactly one new chord");
        K(Key.Shift, false, ctrl: true); K(Key.Ctrl, false);

        // 4. Both modifier sides (Godot reports Ctrl/Shift state on the F9 event).
        foreach (var (ctrlSide, shiftSide) in new[] { (KeyLocation.Right, KeyLocation.Right), (KeyLocation.Left, KeyLocation.Right), (KeyLocation.Right, KeyLocation.Left) })
        {
            SendPaletteChordForSelfTest(ctrlSide, shiftSide); Require(WorldModal, $"{ctrlSide} Ctrl/{shiftSide} Shift opened");
            SendPaletteChordForSelfTest(ctrlSide, shiftSide); Require(!WorldModal, $"{ctrlSide} Ctrl/{shiftSide} Shift closed");
        }
        Require(Leaks() == 0, "side matrix leaked F9");

        // 5. Partial or extra modifiers, and plain F9, are not the chord (and F9 has no other binding).
        var before = Toggles();
        foreach (var (ctrl, shift, alt, meta) in new[] { (false, false, false, false), (true, false, false, false), (false, true, false, false),
            (true, true, true, false), (true, true, false, true) })
        {
            K(Key.F9, true, ctrl, shift, alt, meta); K(Key.F9, true, ctrl, shift, alt, meta, echo: true); K(Key.F9, false, ctrl, shift, alt, meta); Frame();
        }
        Require(Toggles() == before && !WorldModal && !_worldChord.Owned, "non-chord F9 toggled");

        // 6. Held Ctrl+Shift do not turn original keys into diagnostics, and bare
        // modifiers do nothing: F1-F5, R/F/L, 1-0 and WASD stay reserved/inert.
        K(Key.Ctrl, true, ctrl: true); K(Key.Shift, true, ctrl: true, shift: true); Frame(); Frame();
        foreach (var key in new[] { Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.R, Key.F, Key.L, Key.Key1, Key.Key0, Key.W, Key.A, Key.S, Key.D })
        { K(key, true, ctrl: true, shift: true); K(key, false, ctrl: true, shift: true); Frame(); }
        K(Key.Shift, false, ctrl: true); K(Key.Ctrl, false); Frame(); Frame();
        Require(!WorldModal && _worldLifecycleSession is null && !_worldFitView && _worldActor.State == actor && _camera.State == camera
            && _camera.FollowState == follow && Toggles() == before, "held modifiers or original keys had a diagnostic/world effect");
        Require(_worldChord.Trace.Where(d => d.Key == WorldKey.F1 && d.Pressed).All(d => d.Role == WorldKeyRole.OriginalReserved && d.OriginalAction == "Followers: go to point")
            && _worldChord.Trace.Any(d => d.Key == WorldKey.Digit0 && d.OriginalAction == "Quick slot 10"), "original keys not routed as reserved");

        // 7. Text entry outside the palette owns the keyboard: the chord does not open it.
        var layer = new CanvasLayer(); var field = new LineEdit(); layer.AddChild(field); AddChild(layer);
        try
        {
            field.GrabFocus(); Require(WorldUiFocus, "text field focus");
            SendPaletteChordForSelfTest();
            Require(!WorldModal && Toggles() == before && Leaks() == 0, $"chord opened over text entry modal={WorldModal} toggles={Toggles()}/{before} leaks={Leaks()} focus={GetViewport().GuiGetFocusOwner()?.GetType().Name}");
        }
        finally { GetViewport().GuiReleaseFocus(); layer.QueueFree(); }

        // 8. Inside the palette the numeric editor keeps text; the chord still closes once.
        SendPaletteChordForSelfTest(); Require(WorldModal && GetViewport().GuiGetFocusOwner() is LineEdit, "palette opened on its numeric editor");
        var settings = _worldSettings;
        foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D, Key.F1, Key.R }) { K(key, true); K(key, false); }
        Require(WorldModal && _worldSettings == settings && _worldActor.State == actor, "palette text focus leaked keys");
        SendPaletteChordForSelfTest(); Require(!WorldModal && Toggles() == before + 2, "chord closed from the text field");

        // 9. Focus loss: an unfocused window cannot open; a press whose release is
        // lost to focus change keeps no stuck state, and the next chord works.
        WorldFocusForSelfTest(false); SendPaletteChordForSelfTest(); Require(!WorldModal, "unfocused chord opened");
        WorldFocusForSelfTest(true);
        K(Key.Ctrl, true, ctrl: true); K(Key.Shift, true, ctrl: true, shift: true); K(Key.F9, true, ctrl: true, shift: true);
        Require(WorldModal, "focused chord opened");
        WorldFocusForSelfTest(false); WorldFocusForSelfTest(true); // F9/modifier releases lost to another window
        K(Key.F9, true, ctrl: true, shift: true); K(Key.F9, false, ctrl: true, shift: true); K(Key.Shift, false, ctrl: true); K(Key.Ctrl, false);
        Require(!WorldModal && !_worldChord.Owned && Leaks() == 0, "lost release blocked the next chord or leaked");

        // 10. Device/method change: gamepad Start and the keyboard chord share one
        // palette state; each press toggles once.
        Fresh(); var toggles = Toggles();
        WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() with { Settings = true } }, .1);
        Require(WorldModal, "Start opened the palette");
        WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() with { Settings = true } }, .1);
        Require(WorldModal, "held Start repeated");
        WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() }, .1);
        SendPaletteChordForSelfTest(); Require(!WorldModal && Toggles() == toggles + 1, "keyboard chord closed the Start palette once");
        // Closing quarantines (existing modal policy): two complete neutral frames rearm.
        WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() }, .1); WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() }, .1);
        WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() with { Settings = true } }, .1);
        Require(WorldModal, "Start reopened after keyboard close");
        WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() }, .1);
        ToggleWorldSettings();

        // 11. Modal policy against an equal accepted-step/time trace: opening the
        // palette at 12 is the same timed neutral as releasing the stick at 12.
        string Run(bool palette)
        {
            Configure(StepDuration.Of(8)); _worldTestTime = SimulationTime.At(0); Fresh(); PublishWorldSelection();
            var events = new List<MovementEvent>();
            void At(int time) { WorldFrameForSelfTest(none, new[] { SimulatedPadLevel() }, .1, true, SimulationTime.At(time));
                if (_worldActor.Session!.LastAdvance is { } advance) events.AddRange(advance.Movement.Events.Where(e => !events.Any(o => o.Ordinal == e.Ordinal))); }
            At(9);
            _worldTestTime = SimulationTime.At(10); SimulatedSectorKey(Key.S, true); SimulatedSectorKey(Key.A, true);
            At(11);
            _worldTestTime = SimulationTime.At(12);
            if (palette) SendPaletteChordForSelfTest(); else { SimulatedSectorKey(Key.S, false); SimulatedSectorKey(Key.A, false); }
            for (var t = 13; t <= 15; t++) At(t);
            if (palette) { _worldTestTime = SimulationTime.At(16); SendPaletteChordForSelfTest(); SimulatedSectorKey(Key.S, false); SimulatedSectorKey(Key.A, false); }
            for (var t = 17; t <= 21; t++) At(t);
            Require(_worldActor.Session!.State.Phase == MovementPhase.Settled && !WorldModal, "paired trace settled");
            return string.Join("|", events.Select(e => $"{e.Kind}@{e.Time.Value.Numerator}/{e.Time.Value.Denominator}:{e.Key.From.X},{e.Key.From.Y}>{e.Key.To.X},{e.Key.To.Y}"));
        }
        var released = Run(false); var modal = Run(true);
        const string expected = "StepStarted@10/1:6,1>7,1|ActionChanged@10/1:6,1>7,1|StepCancelled@12/1:6,1>7,1|FacingChanged@12/1:6,1>7,1|CenterArrived@14/1:6,1>7,1|ActionChanged@14/1:6,1>7,1";
        Require(released == expected && modal == expected, $"palette open/close differs from the equal release trace released={released} modal={modal}");

        // Restore a fresh live-duration session for later matrices.
        Configure(); ReleaseSimulatedPadForSelfTest(); PublishWorldSelection(); _worldChord.ClearTrace();
        GD.Print("ARC-INPUT-001 palette chord matrix PASS SIMULATION order/modifier-first/echo/re-press/sides/partial-extra-modifiers/held-modifier original keys/text entry/palette text/focus loss/Start-keyboard/equal modal trace; native OS delivery and physical keyboard NOT_VERIFIED (ARC-INPUT-002)");
    }

    /// <summary>ARC-INPUT-001 AC3: the actual palette UI at the required 1280x800 and
    /// 2560x1440 viewports: open by chord, visible controls/state, field focus,
    /// native clicks for exact zoom/Fit/Close, pointer ownership (a palette click
    /// is never a world intent; a world LMB is inert). Opaque pixels when graphical.</summary>
    internal async Task WorldPaletteGraphicalMatrixForSelfTestAsync()
    {
        RequireExplicitTest();
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD palette UI: " + why); }
        var viewport = GetViewport(); var original = new Vector2I((int)WorldViewport.X, (int)WorldViewport.Y);
        var none = new RawInputFrame(-1, default);
        void Frame() => WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), .1);
        async Task Click(Vector2 point)
        {
            viewport.PushInput(new InputEventMouseMotion { Position = point });
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point });
            await NextFrameAsync();
        }
        var preferences = _worldPreferredZoom;
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 800), new(2560, 1440) })
            {
                if (viewport is SubViewport sub) sub.Size = size; else DisplayServer.WindowSetSize(size);
                await NextFrameAsync(); await NextFrameAsync();
                Require(WorldViewport == new Vector2(size.X, size.Y), $"actual viewport {WorldViewport} for {size}");
                if (WorldModal) ToggleWorldSettings();
                GetViewport().GuiReleaseFocus(); WorldFocusForSelfTest(true); ResetWorldScene(); WorldSettleForSelfTest(); Frame(); Frame(); Frame();
                var actor = _worldActor.State;
                // A world LMB with the palette closed is inert: world-diagnostic has no click route.
                var camera = _camera!.State; var follow = _camera.FollowState;
                await Click(WorldViewport - new Vector2(40, 40)); Frame();
                Require(_worldActor.State == actor && _camera.State == camera && _camera.FollowState == follow && !WorldModal, "world LMB acted");
                SendPaletteChordForSelfTest(); await NextFrameAsync(); await NextFrameAsync();
                var panel = _worldPanel!.GetChildren().OfType<PanelContainer>().Single(); var rect = panel.GetGlobalRect();
                Require(WorldModal && rect.Position.X >= 0 && rect.Position.Y >= 0 && rect.End.X <= size.X && rect.End.Y <= size.Y, $"palette contained rect={rect}");
                Require(viewport.GuiGetFocusOwner() is LineEdit, "palette field focus");
                var overlay = (CheckButton)_worldPanel.FindChild("PassabilityOverlay", true, false)!;
                Require(!overlay.Disabled && overlay.ButtonPressed == _passabilityOverlayOn && overlay.IsVisibleInTree(), "overlay checkbox bound to the session state");
                var summary = (Label)_worldPanel.FindChild("PaletteSummary", true, false)!;
                Require(summary.Text.StartsWith("Diagnostic summary:", StringComparison.Ordinal), "palette summary");
                var buttons = _worldPanel.FindChildren("*", "Button", true, false).OfType<Button>().ToArray();
                var scroll = _worldPanel.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>().Single();
                async Task Press(Button button)
                {
                    scroll.EnsureControlVisible(button); await NextFrameAsync(); scroll.EnsureControlVisible(button); await NextFrameAsync();
                    var point = button.GetGlobalRect().GetCenter();
                    Require(scroll.GetGlobalRect().HasPoint(point) && viewport.GetVisibleRect().HasPoint(point), button.Text + " visible");
                    await Click(point);
                }
                await Press(buttons.Single(b => b.Text == "100% (1.00)"));
                Require(_camera.State.Zoom == 1 && _camera.Camera.Zoom == Vector2.One && WorldModal, "native exact zoom 1.00");
                await Press(buttons.Single(b => b.Text == "200% (2.00)"));
                Require(_camera.State.Zoom == 2 && WorldModal, "native exact zoom 2.00");
                await Press(buttons.Single(b => b.Text.StartsWith("Fit", StringComparison.Ordinal)));
                Require(_worldFitView && WorldModal, "native Fit");
                // A click on the palette's own surface is consumed by the GUI, not the world.
                var afterFit = _camera.State;
                await Click(rect.Position + new Vector2(6, 6)); Frame();
                Require(_camera.State == afterFit && _worldActor.State == actor && WorldModal, "palette surface click reached the world");
                if (DisplayServer.GetName() != "headless")
                {
                    using var image = viewport.GetTexture().GetImage();
                    var pixel = image.GetPixel((int)rect.Position.X + 3, (int)rect.Position.Y + 3);
                    Require(Math.Abs(pixel.R * 255 - 16) <= 1 && Math.Abs(pixel.G * 255 - 20) <= 1 && Math.Abs(pixel.B * 255 - 28) <= 1, "opaque palette pixels");
                }
                // The ARC-RENDER-008 seam: bound available, a native click reports once;
                // binding never reports; unbound again it is inert.
                var overlayEvents = new List<bool>(); void Overlay(bool on) => overlayEvents.Add(on);
                _worldPanel.OverlayToggled += Overlay;
                try
                {
                    _worldPanel.BindOverlay(true, false, "Show passability (seam test)");
                    scroll.EnsureControlVisible(overlay); await NextFrameAsync(); scroll.EnsureControlVisible(overlay); await NextFrameAsync();
                    await Click(overlay.GetGlobalRect().GetCenter());
                    Require(overlayEvents.SequenceEqual(new[] { true }) && overlay.ButtonPressed, "overlay seam click reported once");
                    _worldPanel.BindOverlay(false, false, "Show passability (unavailable)");
                    await Click(overlay.GetGlobalRect().GetCenter());
                    Require(overlayEvents.Count == 1 && overlay.Disabled && !overlay.ButtonPressed && overlay.FocusMode == Control.FocusModeEnum.None, "unavailable overlay seam inert");
                }
                finally { _worldPanel.OverlayToggled -= Overlay; _passabilityOverlayOn = false; _overlayPanelState = null; SyncPassabilityOverlay(); }
                await Press(buttons.Single(b => b.Text.StartsWith("Close", StringComparison.Ordinal)));
                Require(!WorldModal && _worldActor.State == actor, "native Close");
                GD.Print($"ARC-INPUT-001 palette UI PASS viewport={size.X}x{size.Y} chord-open/contained/field focus/overlay checkbox/1.00/2.00/Fit/surface click/Close/world LMB inert; pixels=" + (DisplayServer.GetName() == "headless" ? "NOT_VERIFIED" : "PASS"));
            }
        }
        finally
        {
            if (WorldModal) ToggleWorldSettings();
            if (viewport is SubViewport sub) sub.Size = original; else DisplayServer.WindowSetSize(original);
            await NextFrameAsync();
            SetPreferredWorldZoom(preferences); ResetWorldScene(); WorldSettleForSelfTest();
        }
    }
}
