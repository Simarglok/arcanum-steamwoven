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
        ResetWorldScene(); Neutral();
        GD.Print("ARC-WORLD-001 lifecycle matrix PASS SIMULATION active/inactive disconnect/reconnect/opposing-levels/drag/HUD/zoom/Fit/reset/complete adapter levels; physical NOT_READY");
    }
}
