using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private bool _worldFocusSimulationForSelfTest;

    private void WorldZoomExtremeFrameMatrixForSelfTest()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD extreme frame: " + why); }
        var none = new RawInputFrame(-1, default); var pad = new RawInputFrame(3, default);
        void Prepare(bool controller = true)
        {
            if (WorldModal) ToggleWorldSettings();
            GetViewport().GuiReleaseFocus(); _worldAdapter.Clear();
            _worldReducer.SetStartupDevices(controller ? new[] { 3 } : Array.Empty<int>());
            WorldFocusForSelfTest(true); SetPreferredWorldZoom(2); ResetWorldScene();
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, controller ? new[] { pad } : Array.Empty<RawInputFrame>(), .1);
            Require(_worldGate.IsOpen, "rearm");
        }
        foreach (var dt in new[] { 1e6, 1e307, double.MaxValue })
        foreach (var (lt, rt, expected) in new[] { (0d, 1d, 8d), (1d, 0d, .125), (1d, 1d, 2d), (0d, 0d, 2d) })
        {
            Prepare();
            var position = _worldActor.State.Position; var before = GetSelfTestSnapshot();
            var raw = pad with { LeftTrigger = lt, RightTrigger = rt };
            var context = WorldContext(dt, true);
            var reducer = new InputStateReducer(); reducer.SetStartupDevices(new[] { 3 });
            var intent = reducer.Reduce(raw, context);
            Require(intent.CameraPan == default(Vec2) && !intent.ActorActive && !intent.CameraActive, "neutral domains");
            WorldFrameForSelfTest(none, new[] { raw }, dt);
            Require(_worldGate.IsOpen && _camera!.State.Zoom == expected, $"dt={dt:R} LT={lt} RT={rt} literal={expected}");
            var after = GetSelfTestSnapshot();
            Require(_worldActor.State.Position == position && after.CacheCount == before.CacheCount
                && after.CreatedTextures == before.CreatedTextures && double.IsFinite(_camera!.State.X)
                && double.IsFinite(_camera.State.Y) && _worldActor.CurrentFrame >= 0 && _worldActor.CurrentFrame < 15,
                "finite presentation/actor geometry/cache");
            // Idle animation may advance at equal elapsed time; zoom must not alter it.
            var actor = _worldActor.State; var frame = _worldActor.CurrentFrame;
            Prepare(); WorldFrameForSelfTest(none, new[] { pad }, dt);
            Require(_worldActor.State == actor && _worldActor.CurrentFrame == frame, "equal-time actor/animation");
            GD.Print($"ARC-GPAD-004 extreme production PASS dt={dt:R} LT={lt} RT={rt} zoom={expected:R} pan=0 admitted; controller SIMULATION");
        }
        foreach (var dt in new[] { 1e307, double.MaxValue })
        {
            Prepare(false); WorldFrameForSelfTest(none, Array.Empty<RawInputFrame>(), dt);
            Require(_worldGate.IsOpen && _camera!.State.Zoom == 2, "controller-free neutral keyboard");
        }
        foreach (var dt in new[] { -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Prepare(); var state = _camera!.State; var position = _worldActor.State.Position;
            WorldFrameForSelfTest(none, new[] { pad with { RightTrigger = 1 } }, dt);
            Require(!_worldGate.IsOpen && _camera.State == state && _worldActor.State.Position == position, "invalid delta quarantine");
        }
        foreach (var raw in new[] {
            pad with { RightStick = new(1, 0), RightTrigger = 1 },
            pad with { RightStick = new(1, 1), RightTrigger = 1 },

            pad with { RightTrigger = double.NaN }, pad with { LeftTrigger = double.PositiveInfinity },
            pad with { RightTrigger = 1.1 }, pad with { LeftTrigger = -.1 },
            pad with { RightStick = new(double.NaN, 0) }, pad with { LeftStick = new(0, double.PositiveInfinity) } })
        {
            Prepare(); var state = _camera!.State; var position = _worldActor.State.Position;
            WorldFrameForSelfTest(none, new[] { raw }, double.MaxValue);
            Require(!_worldGate.IsOpen && _camera.State == state && _worldActor.State.Position == position, "genuine pan overflow/invalid levels quarantine " + raw);
        }
        Prepare();
        var actorOnly = pad with { LeftStick = new(-1,1), RightTrigger = 1 };
        for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, new[] { actorOnly }, double.MaxValue);
        Require(_worldGate.IsOpen && _camera!.State.Zoom == 8 && _worldActor.IsPlaying
            && _worldActor.Session!.State.CommittedCell == new Arcanum.Simulation.Movement.CellCoord(6,1),
            "finite independent actor receipt clock under huge camera delta");
        WorldSettleForSelfTest(pad);
        Prepare(); var camera = _camera!.State;
        WorldFrameForSelfTest(none with { KeyboardCamera = new(1, 0), KeyboardCameraHeld = true }, new[] { pad with { RightTrigger = 1 } }, double.MaxValue);
        Require(!_worldGate.IsOpen && _camera.State == camera, "genuine keyboard pan overflow quarantine");
        Prepare(); QuarantineWorld();
        for (var i = 0; i < 4; i++) WorldFrameForSelfTest(none, new[] { pad with { LeftTrigger = 1, RightTrigger = 1 } }, double.MaxValue);
        Require(!_worldGate.IsOpen, "opposites remain individually nonneutral");
        Prepare();
        GD.Print("ARC-GPAD-004 extreme complete-frame matrix PASS both neutral reductions, RT/LT/opposites/idle, equal-time actor, real overflow and invalid quarantine; not pure-policy substitution");
    }

    internal async Task WorldSettingsFocusMatrixForSelfTestAsync()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD visible focus: " + why); }
        var panel = _worldPanel!;
        var scroll = panel.FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>().Single();
        // Navigable palette controls: 8 input settings, zoom editor, 3 presets, Fit,
        // Reset scene, Reload, Game time +1 hour, Ambient lighting on/off (ARC-RENDER-010),
        // the passability checkbox (ARC-RENDER-008, available with a movement authority),
        // Reset preferences, Close.
        var overlay = scroll.GetChild(0).GetChildren().OfType<CheckButton>().Single(c => c.Name == "PassabilityOverlay");
        Require(!overlay.Disabled && overlay.FocusMode == Control.FocusModeEnum.All, "overlay checkbox available and navigable");
        var controls = scroll.GetChild(0).GetChildren().OfType<Control>().Where(c => c is SpinBox or BaseButton).ToArray();
        Require(controls.Length == 20, "all camera/actor/zoom/palette controls retained");
        var last = controls.Length - 1;
        var none = new RawInputFrame(-1, default); var pad = new RawInputFrame(3, default);
        var viewport = GetViewport(); var original = viewport.GetVisibleRect().Size;
        // Complete simulated levels own timing; actual GUI/layout still runs.
        // Godot enables overridden _Process on tree entry, even if disabled
        // before Ready. Do not mix live controller levels with device 3 here.
        var processing = IsProcessing(); SetProcess(false);
        // Application-wide OS notifications also reach nested test viewports.
        // Keep SIMULATION focus explicit while testing native control dispatch;
        // unrelated window activation must not reset a held analog latch midway.
        _worldFocusSimulationForSelfTest = true;
        void Resize(Vector2I size)
        {
            if (viewport is SubViewport sub) sub.Size = size; else GetWindow().Size = size;
        }
        var before = _camera!.State; var preferred = _worldPreferredZoom; var stored = _worldZoomStore.Load();
        var input = _worldSettings; var actor = _worldActor.State; var operations = 0;
        void Zoom(double _) => operations++;
        void Action() => operations++;
        panel.ZoomRequested += Zoom; panel.FitRequested += Action; panel.ResetRequested += Action; panel.CloseRequested += Action;
        panel.ResetSceneRequested += Action; panel.ReloadRequested += Action;
        panel.GameHourRequested += Action; panel.LightingToggleRequested += Action;
        async Task Visible(int index, string mode)
        {
            await NextFrameAsync(); await NextFrameAsync();
            var focused = viewport.GuiGetFocusOwner(); var control = controls[index];
            Require(focused == control || (focused is not null && control.IsAncestorOf(focused)), $"{mode} single-step index={index} actual={focused?.GetPath()} wanted={control.GetPath()} hostFocused={_worldFocused} active={_worldReducer.ActiveDevice}");
            var rect = focused!.GetGlobalRect(); var clip = scroll.GetGlobalRect();
            Require(clip.Encloses(rect) && viewport.GetVisibleRect().Encloses(rect), $"{mode} index={index} rect={rect} clip={clip}");
            Require(_camera.State == before && _worldPreferredZoom == preferred && _worldZoomStore.Load() == stored
                && _worldSettings == input && _worldActor.State == actor && operations == 0, "navigation/resize changed state/action/store");
        }
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 800), new(480, 320) })
            {
                Resize(size); await NextFrameAsync(); await NextFrameAsync();
                Require(viewport.GetVisibleRect().Size == (Vector2)size, "actual requested viewport");
                foreach (var mode in new[] { "Dpad", "analog", "keyboard", "Tab" })
                {
                    WorldFocusForSelfTest(true);
                    _worldAdapter.Clear(); _worldReducer.SetStartupDevices(new[] { 3 }); _worldReducer.Quarantine();
                    scroll.ScrollVertical = 0; panel.Open(_worldSettings);
                    for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, new[] { pad }, .1);
                    await Visible(0, mode + " open/reopen");
                    for (var direction = 1; direction >= -1; direction -= 2)
                    for (var step = 0; step < last; step++)
                    {
                        var index = direction == 1 ? step + 1 : last - 1 - step;
                        if (mode == "Dpad")
                        {
                            var button = direction == 1 ? JoyButton.DpadDown : JoyButton.DpadUp;
                            viewport.PushInput(new InputEventJoypadButton { Device = 3, ButtonIndex = button, Pressed = true });
                            WorldFrameForSelfTest(none, new[] { pad with { MenuDown = direction == 1, MenuUp = direction == -1 } }, .1);
                            viewport.PushInput(new InputEventJoypadButton { Device = 3, ButtonIndex = button, Pressed = false });
                        }
                        else if (mode == "analog")
                        {
                            viewport.PushInput(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = direction });
                            WorldFrameForSelfTest(none, new[] { pad with { LeftStick = new(0, direction) } }, .1);
                            // Neither the event pulse plus first full frame nor
                            // additional held frames/neutral keyboard may repeat.
                            WorldFrameForSelfTest(none, new[] { pad with { LeftStick = new(0, direction) } }, 2);
                            await Visible(index, mode + " held");
                            viewport.PushInput(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftY, AxisValue = 0 });
                        }
                        else
                        {
                            var key = mode == "Tab" ? Key.Tab : direction == 1 ? Key.Down : Key.Up;
                            viewport.PushInput(new InputEventKey { Keycode = key, Pressed = true, ShiftPressed = mode == "Tab" && direction == -1 });
                            viewport.PushInput(new InputEventKey { Keycode = key, Pressed = false, ShiftPressed = mode == "Tab" && direction == -1 });
                        }
                        WorldFrameForSelfTest(none, new[] { pad }, .1);
                        await Visible(index, mode);
                    }
                    GD.Print($"ARC-GPAD-004 visible focus PASS {mode} down/up/reopen actual={viewport.GetVisibleRect().Size}; no helper scrolling, native dispatch, controller SIMULATION");
                }
            }
        }
        finally
        {
            panel.ZoomRequested -= Zoom; panel.FitRequested -= Action; panel.ResetRequested -= Action; panel.CloseRequested -= Action;
            panel.ResetSceneRequested -= Action; panel.ReloadRequested -= Action;
            panel.GameHourRequested -= Action; panel.LightingToggleRequested -= Action;
            Resize(new((int)original.X, (int)original.Y)); await NextFrameAsync(); await NextFrameAsync();
            _worldFocusSimulationForSelfTest = false;
            SetProcess(processing);
        }
    }
}
