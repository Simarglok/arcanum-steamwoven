using Arcanum.Input;
using Arcanum.Input.Godot;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal void WorldLifecycleMatrixForSelfTest()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD lifecycle: " + why); }
        var none = new RawInputFrame(-1, default);
        // Keyboard arrows pan; the actor moves only by the selected pad's stick.
        var held = none with { KeyboardCamera = new(1,0), KeyboardCameraHeld = true };
        void Frame(RawInputFrame keys, params RawInputFrame[] pads) => WorldFrameForSelfTest(keys, pads, .1);
        void Neutral(params RawInputFrame[] pads) { Frame(none,pads); Frame(none,pads); Frame(none,pads); }
        var padNeutral = new RawInputFrame(3,default);
        var padHeld = new RawInputFrame(3,new(-1,1),new(1,0));
        SelectSimulatedPadForSelfTest(); ResetWorldScene(); Neutral(padNeutral); _camera!.ZoomWorld(8, WorldAnchor, WorldViewport);
        Frame(held, padHeld with { RightStick = default }); var anchor = _worldActor.State.Position; var center = _camera.State;
        Frame(none with { KeyboardCameraHeld = true }, padNeutral);
        Require(_camera.FollowState.Mode == CameraMode.Manual && !_camera.FollowState.RecenterArmed, "opposing held pan armed return");
        WorldSettleForSelfTest(padNeutral); anchor = _worldActor.State.Position; center = _camera.State;
        _worldReducer.SetStartupDevices(new[] { 3 }); QuarantineWorld();
        Frame(held,padHeld); Frame(none,padHeld);
        Require(_worldActor.State.Position == anchor && _camera.State == center, "acquisition held moved");
        Neutral(padNeutral); Frame(none,padHeld); Frame(none,padHeld); Frame(none,padHeld);
        Require(_worldActor.State.Position != anchor && _camera.FollowState.Mode == CameraMode.Manual, "active simultaneous independent intents");
        var beforeInactive = (_worldActor.State, _camera.State, _camera.FollowState);
        WorldConnectionChanged(9,false);
        Require((_worldActor.State, _camera.State, _camera.FollowState) == beforeInactive, "inactive disconnect changed scene");
        WorldConnectionChanged(3,false); center = _camera.State;
        Require(!_worldGate.IsOpen, "active disconnect did not quarantine");
        WorldSettleForSelfTest();
        Require(!_worldActor.IsPlaying && _camera.State == center, "active disconnect accepted motion did not settle independently");
        ResetWorldScene(); WorldSettleForSelfTest(); anchor = _worldActor.State.Position; center = _camera.State;
        WorldConnectionChanged(3,true); Frame(none,padHeld); Frame(none,padHeld);
        Require(_worldActor.State.Position == anchor && _camera.State == center, "reconnect held moved");
        Frame(none,padNeutral); Frame(none with { KeyboardCameraHeld = true },padNeutral); Frame(none,padNeutral); Frame(none,padHeld);
        Require(_worldActor.State.Position == anchor, "opposing arrows or one neutral sample opened reconnect");
        Neutral(padNeutral); Frame(none,padHeld with { RightStick = default }); Frame(none,padHeld with { RightStick = default }); Frame(none,padHeld with { RightStick = default });
        Require(_worldActor.State.Position != anchor && _camera.FollowState.Mode == CameraMode.Follow, "fresh actor after reconnect did not follow");
        _worldReducer.SetStartupDevices(Array.Empty<int>()); ResetWorldScene(); WorldSettleForSelfTest(); Neutral();
        _camera.ZoomWorld(8,WorldAnchor,WorldViewport);
        var actorBefore = _worldActor.State;
        var beforeHud = _camera.State;
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = _hud!.Rect.Position + Vector2.One * 2 });
        Require(_camera.State == beforeHud, "HUD wheel zoomed world");
        // ARC-INPUT-001: LMB is not a world drag/Confirm; world-diagnostic has no click route (ARC-WORLD-007 is crash-site-area only).
        var followBefore = _camera.FollowState;
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(WorldViewport.X-2,WorldViewport.Y-2) });
        Frame(none);
        _Input(new InputEventMouseMotion { Position = new(WorldViewport.X-2,WorldViewport.Y-2), Relative = new(10,5) }); Frame(none);
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new(WorldViewport.X-2,WorldViewport.Y-2) });
        Frame(none);
        Require(_camera.State == beforeHud && _camera.FollowState == followBefore && _worldActor.State == actorBefore && !_dragging, "LMB dragged or acted on the world");
        // Ordinary original keys carry no diagnostic action: no Fit/Reset/Reload/
        // settings/zoom/actor intent from F1-F5, R/F/L, WASD, 1-0, +/- or Escape.
        foreach (var key in new[] { Key.F1,Key.F2,Key.F3,Key.F4,Key.F5,Key.R,Key.F,Key.L,Key.W,Key.A,Key.S,Key.D,Key.I,Key.C,Key.Space,Key.Enter,Key.Escape,
            Key.Key1,Key.Key2,Key.Key3,Key.Key4,Key.Key5,Key.Key6,Key.Key7,Key.Key8,Key.Key9,Key.Key0,Key.Plus,Key.Minus,Key.F9 })
        {
            _Input(new InputEventKey { Keycode = key, Pressed = true }); _Input(new InputEventKey { Keycode = key, Pressed = true, Echo = true });
            Frame(none); _Input(new InputEventKey { Keycode = key, Pressed = false }); Frame(none);
            Require(_camera.State == beforeHud && _camera.FollowState == followBefore && _worldActor.State == actorBefore && !WorldModal
                && _worldLifecycleSession is null && !_worldFitView && _worldGate.IsOpen, $"plain {key} had a diagnostic/world effect");
        }
        _camera.ZoomWorld(.5,WorldAnchor,WorldViewport);
        Require(_camera.FollowState == followBefore && _worldActor.State.Position == actorBefore.Position, "zoom changed state");
        // Palette actions (the same handlers its buttons call) are the Fit/Reset route.
        FitWorldView();
        Require(!_worldActor.IsPlaying && !_worldGate.IsOpen && _camera.FollowState.Mode == CameraMode.Manual && !_camera.FollowState.RecenterArmed && _worldFitView, "Fit lifecycle");
        var preferences = _worldSettings;
        PaletteResetScene();
        WorldSettleForSelfTest();
        Require(_worldActor.State.Position == new DiagnosticSectorPosition(6,1) && _worldActor.State.Facing == 5 && _worldActor.CurrentFrame == 0
            && _worldSettings == preferences && _camera.FollowState.Mode == CameraMode.Follow, "reset lifecycle/preferences");
        Neutral();
        // Canonical linked adapter reconciles every mapped level, including
        // opposite keys, semantic buttons and all four independent axes.
        var adapter = new GodotInputAdapter();
        adapter.RefreshKeyboardLevels(key => key is Key.W or Key.S or Key.Left or Key.Right);
        var keys = adapter.SampleKeyboard(true);
        Require(keys.KeyboardActor == default && keys.KeyboardCamera == default && keys.KeyboardActorHeld && keys.KeyboardCameraHeld
            && InputStateReducer.HasAnyLevel(keys,new()), "complete opposite-key observation");
        foreach (var key in new[] { Key.Enter,Key.Space,Key.Escape,Key.Backspace,Key.I,Key.Q,Key.E,Key.C,Key.F1,Key.Up,Key.Down,Key.Left,Key.Right,Key.W,Key.A,Key.S,Key.D })
        {
            adapter.RefreshKeyboardLevels(k => k == key);
            Require(InputStateReducer.HasAnyLevel(adapter.SampleKeyboard(true),new()), "mapped keyboard level omitted");
        }
        // The WORLD route adapter: no keyboard actor level or lab pulses; arrows
        // pan; only the open palette gets Up/Down/Enter/Space/Escape/Backspace.
        var world = new GodotInputAdapter { WorldRoute = true };
        foreach (var key in new[] { Key.W,Key.A,Key.S,Key.D,Key.F1,Key.I,Key.C,Key.Q,Key.E,Key.Enter,Key.Space,Key.Escape,Key.Backspace,Key.Key1,Key.R,Key.F,Key.L })
        {
            world.PaletteOpen = false; world.RefreshKeyboardLevels(k => k == key);
            Require(!InputStateReducer.HasAnyLevel(world.SampleKeyboard(true),new()), $"WORLD keyboard level from {key}");
            var mapped = world.Map(new InputEventKey { Keycode = key, Pressed = true }, true);
            Require(!mapped.Keyboard && mapped.KeyboardActor == default && !mapped.Settings && !mapped.Confirm, $"WORLD key event {key} became intent");
            world.Map(new InputEventKey { Keycode = key, Pressed = false }, true);
        }
        world.RefreshKeyboardLevels(k => k is Key.Right or Key.Down);
        Require(world.SampleKeyboard(true).KeyboardCamera == new Vec2(1,1) && world.SampleKeyboard(true).KeyboardCameraHeld, "WORLD arrows pan");
        world.PaletteOpen = true; world.RefreshKeyboardLevels(k => k is Key.Down or Key.Enter);
        var palette = world.SampleKeyboard(true);
        Require(palette.MenuDown && palette.Confirm && palette.KeyboardCamera == default && !palette.MenuUp, "palette keyboard controls");
        world.RefreshKeyboardLevels(k => k is Key.W or Key.S or Key.Q or Key.E or Key.F1);
        Require(!InputStateReducer.HasAnyLevel(world.SampleKeyboard(true),new()), "palette took original keys");
        foreach (var button in new[] { JoyButton.A,JoyButton.B,JoyButton.X,JoyButton.Y,JoyButton.LeftShoulder,JoyButton.RightShoulder,JoyButton.Start,JoyButton.DpadUp,JoyButton.DpadDown })
        {
            Require(adapter.RefreshControllerLevels(3,_ => 0,b => b == button) && InputStateReducer.HasAnyLevel(adapter.SampleController(3,true)!,new()), "mapped button omitted");
        }
        foreach (var axis in new[] { JoyAxis.LeftX,JoyAxis.LeftY,JoyAxis.RightX,JoyAxis.RightY })
        {
            Require(adapter.RefreshControllerLevels(3,a => a == axis ? 1 : 0,_ => false) && InputStateReducer.HasAnyLevel(adapter.SampleController(3,true)!,new()), "mapped axis omitted");
        }
        var beforeInvalid = adapter.SampleController(3,true);
        Require(!adapter.RefreshControllerLevels(3,_ => float.NaN,_ => false) && adapter.SampleController(3,true) == beforeInvalid, "invalid level observation committed");
        Require(adapter.RefreshControllerLevels(3,_ => 0,_ => false) && !InputStateReducer.HasAnyLevel(adapter.SampleController(3,true)!,new()), "full neutral observation");
        WorldIncompleteObservationMatrixForSelfTest();
        ResetWorldScene(); Neutral();
        GD.Print("ARC-WORLD-001 lifecycle matrix PASS SIMULATION active/inactive disconnect/reconnect/opposing-levels/drag/HUD/zoom/Fit/reset/complete adapter levels/incomplete quarantine; physical NOT_READY");
    }

    private void WorldIncompleteObservationMatrixForSelfTest()
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD incomplete observation: " + why); }
        var none = new RawInputFrame(-1, default);
        var pad = new RawInputFrame(3, default);
        var moving = pad with { LeftStick = new(-1,1) };
        foreach (var manual in new[] { false, true })
        foreach (var failure in new[] { "incomplete", "missing-active", "invalid-pad", "invalid-keyboard", "unfocused", "invalid-delta" })
        {
            _worldReducer.SetStartupDevices(new[] { 3 }); ResetWorldScene();
            WorldSettleForSelfTest(pad);
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, new[] { pad }, .1);
            _camera!.ZoomWorld(8, WorldAnchor, WorldViewport);
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, new[] { moving }, .37);
            Require(_worldActor.IsPlaying && _worldActor.State.Action == ActorDiagnosticAction.Walk, "precondition moving");
            if (manual)
            {
                WorldFrameForSelfTest(none with { KeyboardCamera = new(1,0), KeyboardCameraHeld = true }, new[] { moving }, .1);
                WorldFrameForSelfTest(none, new[] { pad }, .1);
                Require(_camera.FollowState.Mode == CameraMode.Manual && _camera.FollowState.RecenterArmed, "precondition armed Manual");
            }
            var position = _worldActor.State.Position; var center = _camera.State;
            _dragging = true; _worldMenuAnalogDirection = 1;
            WorldFrameForSelfTest(failure == "invalid-keyboard" ? none with { KeyboardCamera = new(double.NaN,0) }
                : failure == "unfocused" ? none with { Focused = false } : none,
                failure == "missing-active" ? Array.Empty<RawInputFrame>()
                : new[] { failure == "invalid-pad" ? pad with { LeftStick = new(double.NaN,0), Settings = true } : moving with { Settings = true } },
                failure == "invalid-delta" ? double.NaN : 2, failure != "incomplete");
            Require(_camera.State == center && !WorldModal
                && !_camera.FollowState.RecenterArmed && !_worldGate.IsOpen && !_dragging && _worldMenuAnalogDirection == 0,
                failure + " did not quarantine before actions");
            WorldSettleForSelfTest(pad); position = _worldActor.State.Position;
            Require(!_worldActor.IsPlaying && _worldActor.State.Action == ActorDiagnosticAction.Idle && _worldActor.CurrentFrame == 0
                && _camera.State == center, failure + " accepted return/finish did not settle independently");
            QuarantineWorld();
            WorldFrameForSelfTest(none, new[] { moving }, .1); WorldFrameForSelfTest(none, new[] { moving }, .1);
            WorldFrameForSelfTest(none, new[] { pad }, .1); WorldFrameForSelfTest(none, new[] { moving }, .1);
            Require(_worldActor.State.Position == position && _camera.State == center && !_worldGate.IsOpen, "held or single neutral reopened");
            WorldFrameForSelfTest(none, new[] { pad }, .1); WorldFrameForSelfTest(none, new[] { pad }, .1);
            Require(_worldGate.IsOpen && _worldActor.State.Position == position, "two full neutral frames required");
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, new[] { moving }, .1);
            Require(_worldActor.State.Position != position, "fresh activation did not recover");
        }
        _worldReducer.SetStartupDevices(Array.Empty<int>()); ResetWorldScene();
    }

    internal async Task WorldMouseSettingsMatrixForSelfTestAsync()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD mouse settings: " + why); }
        var oldPath = OS.GetEnvironment("WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH");
        var owned = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "world-click-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(owned);
        OS.SetEnvironment("WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH", System.IO.Path.Combine(owned, "owned.json"));
        var preferences = _worldSettings;
        var none = new RawInputFrame(-1, default);
        var changes = 0;
        var resets = 0;
        void Changed(InputSettings _) { changes++; TraceWorldInput($"Observer:Changed count={changes}"); }
        void ResetRequested() => resets++;
        _worldPanel!.Changed += Changed;
        _worldPanel.ResetRequested += ResetRequested;
        async Task ClickAsync(Control control)
        {
            var scroll = _worldPanel.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>().Single();
            scroll.EnsureControlVisible(control);
            await NextFrameAsync();
            // The actual window can reflow the longer zoom status one frame
            // after focus changes. Scroll the settled layout, not its old rect.
            scroll.EnsureControlVisible(control);
            await NextFrameAsync();
            var point = control.GetGlobalRect().GetCenter();
            Require(scroll.GetGlobalRect().HasPoint(point), "clicked control outside visible scroll viewport");
            Require(GetViewport().GetVisibleRect().HasPoint(point), $"clicked control outside actual viewport point={point.X}/{point.Y} size={WorldViewport.X}/{WorldViewport.Y}");
            GetViewport().PushInput(new InputEventMouseMotion { Position = point });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point });
            await NextFrameAsync();
        }
        try
        {
            _worldReducer.SetStartupDevices(Array.Empty<int>()); ResetWorldScene();
            if (!WorldModal) ToggleWorldSettings();
            var toggles = _worldPanel.FindChildren("*", "CheckButton", true, false).Cast<CheckButton>().Where(c => c.Name != "PassabilityOverlay").ToArray();
            Require(toggles.Length == 4, "four independent camera/actor controls required");
            foreach (var x in new[] { false, true }) foreach (var y in new[] { false, true })
            foreach (var axis in new[] { 0, 1, 2, 3 })
            {
                var initial = new InputSettings(InvertCameraX: x, InvertCameraY: y, InvertActorX: x, InvertActorY: y);
                SaveWorldSettings(initial); _worldPanel.Bind(initial);
                // Deliberately focus the OTHER axis: native clicks must not also
                // invoke semantic Confirm on this previously focused control.
                toggles[axis ^ 1].GrabFocus();
                for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), .1);
                await NextFrameAsync();
                var position = _worldActor.State.Position; var center = _camera!.State;
                changes = 0;
                await ClickAsync(toggles[axis]);
                var expected = initial with { InvertCameraX = axis == 0 ? !x : x, InvertCameraY = axis == 1 ? !y : y,
                    InvertActorX = axis == 2 ? !x : x, InvertActorY = axis == 3 ? !y : y };
                Require(_worldSettings == expected && toggles[0].ButtonPressed == expected.InvertCameraX
                    && toggles[1].ButtonPressed == expected.InvertCameraY && toggles[2].ButtonPressed == expected.InvertActorX
                    && toggles[3].ButtonPressed == expected.InvertActorY && changes == 1 && _worldStore.Load() == expected,
                    $"native click axis={axis} from={x}/{y} actual={_worldSettings.InvertCameraX}/{_worldSettings.InvertCameraY} changes={changes}");
                Require(_worldActor.State.Position == position && _camera.State == center && !_worldActor.IsPlaying
                    && !_worldGate.IsOpen && !_camera.FollowState.RecenterArmed, "mouse UI moved world/armed Follow");
                ToggleWorldSettings(); ToggleWorldSettings(); await NextFrameAsync();
                Require(_worldSettings == expected && _worldPanel.Settings == expected, "close/reopen changed preferences");
            }
            GD.Print("ARC-WORLD-001 native checkbox clicks PASS all four starting combinations/four axes/one change/persist/reopen/world stopped; SIMULATION not physical evidence");
            if (System.Environment.GetEnvironmentVariable("ARC_WORLD_INPUT_TRACE") == "1")
            {
                var sequence = 0;
                _worldInputTraceForSelfTest = line => GD.Print($"WORLD_INPUT_TRACE seq={++sequence} frame={Engine.GetProcessFrames()} {line}");
                _worldPanel.InputTraceForSelfTest = TraceWorldInput;
            }
            // Native semantic activation is consumed once, even with an echo,
            // held frame, unrelated pointer event and subsequent release.
            SaveWorldSettings(new InputSettings(InvertCameraX: false, InvertCameraY: false)); _worldPanel.Bind(_worldSettings);
            toggles[0].GrabFocus();
            TraceWorldInput("Keyboard:focus-requested");
            for (var i = 0; i < 3; i++) { WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), .1); TraceWorldInput($"Keyboard:neutral{i}"); }
            changes = 0;
            TraceWorldInput("Keyboard:before-press");
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, Pressed = true });
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, Pressed = true, Echo = true });
            WorldFrameForSelfTest(none with { Confirm = true }, Array.Empty<RawInputFrame>(), 2);
            GetViewport().PushInput(new InputEventMouseMotion { Position = new(799, 599) });
            GetViewport().PushInput(new InputEventKey { Keycode = Key.Space, Pressed = false });
            await NextFrameAsync();
            TraceWorldInput($"Keyboard:settled changes={changes}");
            Require(_worldSettings.InvertCameraX && !_worldSettings.InvertCameraY && changes == 1,
                $"keyboard/native activation duplicated actual={_worldSettings.InvertCameraX}/{_worldSettings.InvertCameraY}/{_worldSettings.InvertActorX}/{_worldSettings.InvertActorY} changes={changes}");
            _worldReducer.SetStartupDevices(new[] { 3 }); QuarantineWorld(); toggles[1].GrabFocus();
            var pad = new RawInputFrame(3, default);
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, new[] { pad }, .1);
            changes = 0;
            GetViewport().PushInput(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = true });
            WorldFrameForSelfTest(none, new[] { pad with { Confirm = true } }, 2);
            GetViewport().PushInput(new InputEventJoypadButton { Device = 3, ButtonIndex = JoyButton.A, Pressed = false });
            await NextFrameAsync();
            Require(_worldSettings.InvertCameraX && _worldSettings.InvertCameraY && changes == 1, "controller/native activation duplicated");
            _worldReducer.SetStartupDevices(Array.Empty<int>()); QuarantineWorld();
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), .1);
            // Reset preferences is a real native button click, not a default-property
            // oracle; it no longer resets the scene (Reset scene is its own action).
            var buttons = _worldPanel.FindChildren("*", "Button", true, false).Cast<Button>().ToArray();
            var reset = buttons.Single(b => b.Text == "Reset preferences");
            reset.GrabFocus(); await NextFrameAsync();
            changes = 0; resets = 0; await ClickAsync(reset);
            Require(_worldSettings == new InputSettings(InvertCameraX: false, InvertCameraY: false) && changes == 0 && resets == 1
                && _worldStore.Load() == _worldSettings && _worldLifecycleSession is null && !_worldResetPending && WorldModal,
                $"native Reset preferences changes={changes} resets={resets} defaults={_worldSettings == new InputSettings(InvertCameraX: false, InvertCameraY: false)} saved={_worldStore.Load() == _worldSettings} lifecycle={_worldLifecycleSession is not null}");
            // Palette Fit / Reset scene / Reload / exact zoom by native clicks: each
            // once, observable, and never a world intent behind the palette.
            var fitClicks = 0; var sceneClicks = 0; var reloadClicks = 0;
            void Fit() => fitClicks++; void Scene() => sceneClicks++; void Reload() => reloadClicks++;
            _worldPanel.FitRequested += Fit; _worldPanel.ResetSceneRequested += Scene; _worldPanel.ReloadRequested += Reload;
            try
            {
                var status = _worldPanel.FindChild("PaletteActionStatus", true, false) as Label;
                await ClickAsync(buttons.Single(b => b.Text.StartsWith("Fit", StringComparison.Ordinal)));
                Require(fitClicks == 1 && _worldFitView && WorldModal && !_worldGate.IsOpen, "palette Fit once");
                await ClickAsync(buttons.Single(b => b.Text == "100% (1.00)"));
                Require(_camera!.State.Zoom == 1 && !_worldFitView && WorldModal, "palette exact zoom 1.00");
                await ClickAsync(buttons.Single(b => b.Text == "Reset scene"));
                Require(sceneClicks == 1 && _worldResetPending && WorldModal && status?.Text.StartsWith("Reset scene requested", StringComparison.Ordinal) == true, "palette Reset scene once");
                WorldSettleForSelfTest();
                Require(!_worldResetPending && _worldActor.State.Position == new DiagnosticSectorPosition(6, 1) && _worldSettings == new InputSettings(InvertCameraX: false, InvertCameraY: false)
                    && status?.Text.StartsWith("Reset scene done", StringComparison.Ordinal) == true, "palette Reset scene settled and shown, preferences kept");
                await ClickAsync(buttons.Single(b => b.Text == "Reload current source"));
                Require(reloadClicks == 1 && WorldModal && status is not null
                    && (_arguments is null ? status.Text.StartsWith("Reload unavailable", StringComparison.Ordinal) && !_worldReloadPending
                        : status.Text.StartsWith("Reload requested", StringComparison.Ordinal) && _worldReloadPending), "palette Reload outcome shown");
                if (_worldReloadPending) RetireWorldLifecycle();
            }
            finally { _worldPanel.FitRequested -= Fit; _worldPanel.ResetSceneRequested -= Scene; _worldPanel.ReloadRequested -= Reload; }
            GD.Print("ARC-WORLD-001 settings activation PASS native keyboard/controller once/native Reset preferences false-false; ARC-INPUT-001 palette Fit/1.00/Reset scene/Reload clicks once with shown outcomes; lab default unchanged");
            await AssertWorldSettingsOpacityAsync();
        }
        finally
        {
            _worldInputTraceForSelfTest = null;
            _worldPanel!.InputTraceForSelfTest = null;
            _worldPanel.Changed -= Changed;
            _worldPanel.ResetRequested -= ResetRequested;
            if (WorldModal) ToggleWorldSettings();
            _worldSettings = preferences; _worldPanel.Bind(preferences);
            OS.SetEnvironment("WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH", oldPath);
            System.IO.Directory.Delete(owned, true);
            _worldAdapter.Clear(); _worldReducer.SetStartupDevices(Array.Empty<int>()); ResetWorldScene();
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), .1);
        }
    }

    private async Task AssertWorldSettingsOpacityAsync()
    {
        var viewport = GetViewport();
        var originalSize = new Vector2I((int)WorldViewport.X, (int)WorldViewport.Y);
        var probe = new CanvasLayer { Layer = 2 };
        var backdrop = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
        probe.AddChild(backdrop); AddChild(probe);
        var panel = _worldPanel!.GetChildren().OfType<PanelContainer>().Single();
        var header = _worldPanel.FindChild("WorldSettingsLimitation", true, false) as Label;
        var actor = _worldActor.State;
        try
        {
            foreach (var size in new[] { new Vector2I(480, 320), new(800, 600), new(1280, 800), new(1440, 900) })
            {
                if (viewport is SubViewport sub) sub.Size = size; else DisplayServer.WindowSetSize(size);
                backdrop.Size = size; await NextFrameAsync();
                var rect = panel.GetGlobalRect();
                if (WorldViewport != new Vector2(size.X, size.Y) || rect.Position.X < 0 || rect.Position.Y < 0
                    || rect.End.X > size.X || rect.End.Y > size.Y || header is null || !header.IsVisibleInTree()
                    || !header.Text.Contains("Bounded adjacent grid", StringComparison.Ordinal) || !header.Text.Contains("NOT VERIFIED", StringComparison.Ordinal)
                    || _worldActor.State != actor)
                    throw new InvalidOperationException("WORLD settings containment/limitations changed.");
                if (DisplayServer.GetName() == "headless") continue;
                foreach (var color in new[] { new Color(1, 0, 1, 1), new Color(0, 1, 1, 1) })
                {
                    backdrop.Color = color; await NextFrameAsync();
                    using var image = viewport.GetTexture().GetImage();
                    if (image.GetWidth() != size.X || image.GetHeight() != size.Y) throw new InvalidOperationException("WORLD settings readback size mismatch.");
                    foreach (var point in new[] { rect.Position + new Vector2(3, 3), rect.Position + new Vector2(3, rect.Size.Y / 2), rect.End - new Vector2(3, 3) })
                    {
                        var pixel = image.GetPixel((int)point.X, (int)point.Y);
                        if (Math.Abs(pixel.R * 255 - 16) > 1 || Math.Abs(pixel.G * 255 - 20) > 1 || Math.Abs(pixel.B * 255 - 28) > 1 || pixel.A < .99)
                            throw new InvalidOperationException("WORLD settings opaque literal background failed.");
                    }
                    // Text must actually render over the opaque surface, not
                    // just exist in a Label while being clipped or invisible.
                    var textRect = header.GetGlobalRect(); var bright = 0;
                    for (var y = (int)textRect.Position.Y; y < (int)textRect.End.Y; y++)
                    for (var x = (int)textRect.Position.X; x < (int)textRect.End.X; x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (pixel.R > .6 && pixel.G > .6 && pixel.B > .6) bright++;
                    }
                    if (bright < 20) throw new InvalidOperationException("WORLD settings limitation text not rendered.");
                }
                GD.Print($"ARC-WORLD-001 settings opacity pixels PASS requested=actual {size.X}x{size.Y} literal16/20/28/255 over magenta/cyan; limitation text rendered");
            }
            if (DisplayServer.GetName() == "headless") GD.Print("ARC-WORLD-001 settings opacity pixels NOT_VERIFIED headless; containment PASS");
        }
        finally
        {
            probe.QueueFree();
            if (viewport is SubViewport sub) sub.Size = originalSize; else DisplayServer.WindowSetSize(originalSize);
            await NextFrameAsync();
        }
    }

    internal async Task WorldModalRoutingMatrixForSelfTestAsync()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD modal routing: " + why); }
        var oldPath = OS.GetEnvironment("WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH");
        var owned = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "world-modal-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(owned);
        OS.SetEnvironment("WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH", System.IO.Path.Combine(owned,"owned.json"));
        var preferences = _worldSettings;
        var none = new RawInputFrame(-1, default);
        void Frame(RawInputFrame keys, double dt = .1, params RawInputFrame[] pads) => WorldFrameForSelfTest(keys, pads, dt);
        void KeyEvent(Key key, bool down, bool echo = false, uint unicode = 0) => GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = down, Echo = echo, Unicode = unicode });
        try
        {
            _worldReducer.SetStartupDevices(Array.Empty<int>()); ResetWorldScene();
            Frame(none); Frame(none); Frame(none);
            KeyEvent(Key.F1,true); KeyEvent(Key.F1,false);
            Require(!WorldModal, "plain F1 opened the palette");
            KeyEvent(Key.Ctrl,true); GetViewport().PushInput(new InputEventKey { Keycode = Key.Shift, Pressed = true, CtrlPressed = true });
            GetViewport().PushInput(new InputEventKey { Keycode = Key.F9, Pressed = true, CtrlPressed = true, ShiftPressed = true });
            GetViewport().PushInput(new InputEventKey { Keycode = Key.F9, Pressed = false, CtrlPressed = true, ShiftPressed = true });
            KeyEvent(Key.Shift,false); KeyEvent(Key.Ctrl,false);
            Require(WorldModal, "actual Ctrl+Shift+F9 did not open"); Frame(none); Frame(none); Frame(none);
            // Four input numbers, the zoom editor and the three volumes (ARC-UI-044).
            var spins = _worldPanel!.FindChildren("*","SpinBox",true,false).Cast<SpinBox>().ToArray();
            Require(spins.Length == 8 && GetViewport().GuiGetFocusOwner() == spins[0].GetLineEdit(), "initial native numeric focus");
            var position = _worldActor.State.Position; var center = _camera!.State;
            KeyEvent(Key.Down,true);
            Require(GetViewport().GuiGetFocusOwner() == spins[1].GetLineEdit(), "digital press must navigate exactly one");
            foreach (var dt in new[] { 0d, .001, .1, 2d })
            {
                for (var i = 0; i < 4; i++) Frame(none with { MenuDown = true, KeyboardCameraHeld = true },dt);
                KeyEvent(Key.Down,true,true);
                GetViewport().PushInput(new InputEventMouseMotion { Position = new(799,599), Relative = new(1,0) });
                Require(GetViewport().GuiGetFocusOwner() == spins[1].GetLineEdit(), "held/echo/unrelated event repeated navigation");
            }
            KeyEvent(Key.Down,false); Frame(none); KeyEvent(Key.Down,true); KeyEvent(Key.Down,false);
            Require(GetViewport().GuiGetFocusOwner() == spins[2].GetLineEdit(), "release/repress must navigate once");
            // Original letters are not palette navigation (W/S/Q/E/F1 stay reserved).
            foreach (var key in new[] { Key.W, Key.S, Key.Q, Key.E, Key.F1 }) { KeyEvent(key,true); KeyEvent(key,false); Frame(none); }
            Require(GetViewport().GuiGetFocusOwner() == spins[2].GetLineEdit() && WorldModal, "original letters navigated or closed the palette");
            // Real native editor receives digits, Backspace, Enter and Tab via
            // the viewport dispatch, not a direct Text/property replacement.
            var editor = spins[2].GetLineEdit(); editor.SelectAll();
            KeyEvent(Key.Key1,true,unicode:49); KeyEvent(Key.Key1,false);
            KeyEvent(Key.Period,true,unicode:46); KeyEvent(Key.Period,false);
            KeyEvent(Key.Key6,true,unicode:54); KeyEvent(Key.Key6,false);
            KeyEvent(Key.Backspace,true); KeyEvent(Key.Backspace,false);
            KeyEvent(Key.Key7,true,unicode:55); KeyEvent(Key.Key7,false);
            Require(editor.Text == "1.7" && WorldModal, "digits/edit keys swallowed or Backspace cancelled");
            KeyEvent(Key.Enter,true); KeyEvent(Key.Enter,false);
            await NextFrameAsync();
            Require(Math.Abs(spins[2].Value - 1.7) < 1e-9 && Math.Abs(_worldSettings.CameraSensitivity - 1.7) < 1e-9, "native numeric commit");
            KeyEvent(Key.Tab,true); KeyEvent(Key.Tab,false);
            Require(GetViewport().GuiGetFocusOwner() != editor, "Tab swallowed");
            Require(_worldActor.State.Position == position && _camera.State == center && !_worldActor.IsPlaying && !_worldGate.IsOpen
                && !_camera.FollowState.RecenterArmed, "UI ownership moved world or armed Follow");
            // Controller digital navigation uses the SAME aggregation/pulse seam.
            _worldPanel.Open(_worldSettings); _worldReducer.SetStartupDevices(new[] { 3 }); QuarantineWorld();
            var pad = new RawInputFrame(3,default);
            Frame(none,.1,pad); Frame(none,.1,pad); Frame(none,.1,pad);
            Frame(none,.1,pad with { MenuDown = true });
            Require(GetViewport().GuiGetFocusOwner() == spins[1].GetLineEdit(), "D-pad press");
            foreach(var dt in new[] { 0d,.001,.1,2d }) Frame(none,dt,pad with { MenuDown = true });
            Require(GetViewport().GuiGetFocusOwner() == spins[1].GetLineEdit(), "D-pad held repeat");
            Frame(none,.1,pad); Frame(none,.1,pad with { MenuDown = true });
            Require(GetViewport().GuiGetFocusOwner() == spins[2].GetLineEdit(), "D-pad release/repress");
            GD.Print("ARC-WORLD-001 modal production viewport routing PASS single-edge/held-dt/echo/mouse/repress/D-pad/native numeric Backspace Enter Tab; SIMULATION not physical evidence");
        }
        finally
        {
            if(WorldModal) ToggleWorldSettings();
            _worldSettings = preferences; _worldPanel!.Bind(preferences);
            OS.SetEnvironment("WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH",oldPath);
            System.IO.Directory.Delete(owned,true);
            _worldAdapter.Clear(); _worldReducer.SetStartupDevices(Array.Empty<int>()); ResetWorldScene();
            Frame(none); Frame(none); Frame(none);
        }
    }
}
