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
        var held = none with { KeyboardActor = new(1,0), KeyboardCamera = new(1,0), KeyboardActorHeld = true, KeyboardCameraHeld = true };
        void Frame(RawInputFrame keys, params RawInputFrame[] pads) => WorldFrameForSelfTest(keys, pads, .1);
        void Neutral(params RawInputFrame[] pads) { Frame(none,pads); Frame(none,pads); Frame(none,pads); }
        ResetWorldScene(); Neutral(); _camera!.ZoomWorld(8, WorldAnchor, WorldViewport);
        Frame(held); var anchor = _worldActor.State.Position; var center = _camera.State;
        Frame(none with { KeyboardCameraHeld = true });
        Require(_camera.FollowState.Mode == CameraMode.Manual && !_camera.FollowState.RecenterArmed, "opposing held pan armed return");
        _worldReducer.SetStartupDevices(new[] { 3 }); QuarantineWorld();
        var padNeutral = new RawInputFrame(3,default);
        var padHeld = new RawInputFrame(3,new(1,0),new(1,0));
        Frame(held,padHeld); Frame(none,padHeld);
        Require(_worldActor.State.Position == anchor && _camera.State == center, "acquisition held moved");
        Neutral(padNeutral); Frame(none,padHeld);
        Require(_worldActor.State.Position != anchor && _camera.FollowState.Mode == CameraMode.Manual, "active simultaneous independent intents");
        var beforeInactive = (_worldActor.State, _camera.State, _camera.FollowState);
        WorldConnectionChanged(9,false);
        Require((_worldActor.State, _camera.State, _camera.FollowState) == beforeInactive, "inactive disconnect changed scene");
        WorldConnectionChanged(3,false); anchor = _worldActor.State.Position; center = _camera.State;
        Require(!_worldActor.IsPlaying && !_worldGate.IsOpen, "active disconnect did not stop");
        WorldConnectionChanged(3,true); Frame(none,padHeld); Frame(none,padHeld);
        Require(_worldActor.State.Position == anchor && _camera.State == center, "reconnect held moved");
        Frame(none,padNeutral); Frame(none with { KeyboardActorHeld = true },padNeutral); Frame(none,padNeutral); Frame(none,padHeld);
        Require(_worldActor.State.Position == anchor, "opposing keyboard or one neutral sample opened reconnect");
        Neutral(padNeutral); Frame(none,padHeld with { RightStick = default });
        Require(_worldActor.State.Position != anchor && _camera.FollowState.Mode == CameraMode.Follow, "fresh actor after reconnect did not follow");
        _worldReducer.SetStartupDevices(Array.Empty<int>()); ResetWorldScene(); Neutral();
        _camera.ZoomWorld(8,WorldAnchor,WorldViewport);
        var actorBefore = _worldActor.State;
        var beforeHud = _camera.State;
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = _hud!.Rect.Position + Vector2.One * 2 });
        Require(_camera.State == beforeHud, "HUD wheel zoomed world");
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(WorldViewport.X-2,WorldViewport.Y-2) });
        Frame(none);
        Require(_camera.FollowState.Mode == CameraMode.Manual && !_camera.FollowState.RecenterArmed, "zero-delta active drag not Manual");
        _Input(new InputEventMouseMotion { Position = new(WorldViewport.X-2,WorldViewport.Y-2), Relative = new(10,5) }); Frame(none);
        Require(_worldActor.State.Position == actorBefore.Position, "drag displaced actor");
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new(WorldViewport.X-2,WorldViewport.Y-2) });
        Frame(none); var panCenter = _camera.State; var panState = _camera.FollowState;
        _camera.ZoomWorld(.5,WorldAnchor,WorldViewport);
        Require(_camera.FollowState == panState && _worldActor.State.Position == actorBefore.Position, "zoom changed state");
        _Input(new InputEventKey { Keycode = Key.F, Pressed = true });
        Require(!_worldActor.IsPlaying && !_worldGate.IsOpen && _camera.FollowState.Mode == CameraMode.Manual && !_camera.FollowState.RecenterArmed, "Fit lifecycle");
        var preferences = _worldSettings;
        _Input(new InputEventKey { Keycode = Key.R, Pressed = true });
        Require(_worldActor.State.Position == new DiagnosticSectorPosition(30,32) && _worldActor.State.Facing == 0 && _worldActor.CurrentFrame == 0
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
        var moving = none with { KeyboardActor = new(1,0), KeyboardActorHeld = true };
        var pad = new RawInputFrame(3, default);
        foreach (var manual in new[] { false, true })
        foreach (var failure in new[] { "incomplete", "missing-active", "invalid-pad", "invalid-keyboard", "unfocused", "invalid-delta" })
        {
            _worldReducer.SetStartupDevices(new[] { 3 }); ResetWorldScene();
            for (var i = 0; i < 3; i++) WorldFrameForSelfTest(none, new[] { pad }, .1);
            _camera!.ZoomWorld(8, WorldAnchor, WorldViewport);
            WorldFrameForSelfTest(moving, new[] { pad }, .37);
            Require(_worldActor.IsPlaying && _worldActor.State.Action == ActorDiagnosticAction.Walk, "precondition moving");
            if (manual)
            {
                WorldFrameForSelfTest(moving with { KeyboardCamera = new(1,0), KeyboardCameraHeld = true }, new[] { pad }, .1);
                WorldFrameForSelfTest(none, new[] { pad }, .1);
                Require(_camera.FollowState.Mode == CameraMode.Manual && _camera.FollowState.RecenterArmed, "precondition armed Manual");
            }
            var position = _worldActor.State.Position; var center = _camera.State;
            _worldDrag = new(100,100); _dragging = true; _worldMenuAnalogDirection = 1;
            WorldFrameForSelfTest(failure == "invalid-keyboard" ? moving with { KeyboardActor = new(double.NaN,0), Settings = true }
                : failure == "unfocused" ? moving with { Focused = false, Settings = true } : moving with { Settings = true },
                failure == "missing-active" ? Array.Empty<RawInputFrame>()
                : new[] { failure == "invalid-pad" ? pad with { LeftStick = new(double.NaN,0) } : pad },
                failure == "invalid-delta" ? double.NaN : 2, failure != "incomplete");
            Require(_worldActor.State.Position == position && _camera.State == center && !WorldModal
                && !_worldActor.IsPlaying && _worldActor.State.Action == ActorDiagnosticAction.Idle && _worldActor.CurrentFrame == 0
                && !_camera.FollowState.RecenterArmed && !_worldGate.IsOpen && !_dragging && _worldDrag == default && _worldMenuAnalogDirection == 0,
                failure + " did not stop/quarantine before actions");
            WorldFrameForSelfTest(moving, new[] { pad }, .1); WorldFrameForSelfTest(moving, new[] { pad }, .1);
            WorldFrameForSelfTest(none, new[] { pad }, .1); WorldFrameForSelfTest(moving, new[] { pad }, .1);
            Require(_worldActor.State.Position == position && _camera.State == center && !_worldGate.IsOpen, "held or single neutral reopened");
            WorldFrameForSelfTest(none, new[] { pad }, .1); WorldFrameForSelfTest(none, new[] { pad }, .1);
            Require(_worldGate.IsOpen && _worldActor.State.Position == position, "two full neutral frames required");
            WorldFrameForSelfTest(moving, new[] { pad }, .1);
            Require(_worldActor.State.Position != position, "fresh activation did not recover");
        }
        _worldReducer.SetStartupDevices(Array.Empty<int>()); ResetWorldScene();
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
            Require(WorldModal, "actual F1 did not open"); Frame(none); Frame(none); Frame(none);
            var spins = _worldPanel!.FindChildren("*","SpinBox",true,false).Cast<SpinBox>().ToArray();
            Require(spins.Length == 4 && GetViewport().GuiGetFocusOwner() == spins[0].GetLineEdit(), "initial native numeric focus");
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
