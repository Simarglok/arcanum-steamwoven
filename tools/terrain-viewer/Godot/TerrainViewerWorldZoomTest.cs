using Arcanum.Input.Godot;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    internal async Task WorldZoomMatrixForSelfTestAsync()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD zoom: " + why); }
        static bool Near(double a,double b) => Math.Abs(a-b)<1e-10;
        WorldZoomExtremeFrameMatrixForSelfTest();
        var none = new RawInputFrame(-1,default); var pad = new RawInputFrame(3,default);
        void Frame(RawInputFrame raw,double dt=.1,bool complete=true) => WorldFrameForSelfTest(none,new[]{raw},dt,complete);
        void Rearm() { Frame(pad); Frame(pad); Frame(pad); }
        _worldReducer.SetStartupDevices(new[]{3}); ResetWorldScene(); Rearm();
        var bounds = _readyDiagnosticPlan!.WorldBounds; var count = _cache.Count; var created = _cache.CreatedCount;
        SetPreferredWorldZoom(2); _camera!.ResetWorld(WorldAnchor,WorldViewport,2);
        foreach (var parts in new[]{new[]{1d},new[]{.04,.06,.17,.23,.5}})
        {
            SetPreferredWorldZoom(2); ResetWorldScene(); Rearm();
            foreach(var dt in parts) Frame(pad with {RightTrigger=1},dt);
            Require(Near(_camera.State.Zoom,4),"RT partition doubling");
        }
        SetPreferredWorldZoom(2); Frame(pad with {LeftTrigger=1},1); Require(_camera.State.Zoom==1,"LT halving");
        Frame(pad with {LeftTrigger=1,RightTrigger=1},1); Require(_camera.State.Zoom==1,"opposites cancel");
        Frame(pad with {RightTrigger=.1},1); Require(_camera.State.Zoom==1,"deadzone");
        var beforeEvent = _camera.State;
        for(var i=0;i<10;i++) GetViewport().PushInput(new InputEventJoypadMotion {Device=3,Axis=JoyAxis.TriggerRight,AxisValue=1});
        Require(_camera.State==beforeEvent,"event-rate displacement");
        Frame(pad with {RightTrigger=1},1); Require(_camera.State.Zoom==2,"one observed frame after event pulses");
        foreach(var cause in new[]{"focus","incomplete","disconnect","invalid","missing","delta"})
        {
            SetPreferredWorldZoom(2); ResetWorldScene(); Rearm(); Frame(pad with {RightTrigger=1},.1);
            var zoom=_camera.State.Zoom;
            if(cause=="focus") WorldFocusForSelfTest(false);
            else if(cause=="disconnect") { WorldConnectionChanged(3,false); WorldConnectionChanged(3,true); }
            else if(cause=="missing") WorldFrameForSelfTest(none,Array.Empty<RawInputFrame>(),.1);
            else Frame(cause=="invalid" ? pad with {RightTrigger=double.NaN} : pad,.1,cause!="incomplete");
            if(cause=="delta") Frame(pad,double.NaN);
            WorldFocusForSelfTest(true);
            Frame(pad with {RightTrigger=1}); Frame(pad with {LeftTrigger=1,RightTrigger=1});
            Frame(pad); Frame(pad with {RightTrigger=1});
            Require(_camera.State.Zoom==zoom&&!_worldGate.IsOpen,"quarantine/individual nonneutral/two-frame gate "+cause);
            Rearm(); Frame(pad with {RightTrigger=1}); Require(_camera.State.Zoom>zoom,"recovery "+cause);
        }
        // Actual linked native bridge replacement is complete/transactional.
        var adapter = new GodotInputAdapter();
        var mapped=adapter.Map(new InputEventJoypadMotion{Device=3,Axis=JoyAxis.TriggerLeft,AxisValue=.55f},true);
        Require(Near(mapped.LeftTrigger,.55f),"native LT transport");
        Require(adapter.RefreshControllerLevels(3,a=>a==JoyAxis.TriggerRight?1:0,_=>false,true)
            && adapter.SampleController(3,true)!.RightTrigger==1 && adapter.SampleController(3,true)!.LeftTrigger==0,"native full replacement");
        var prior=adapter.SampleController(3,true);
        Require(!adapter.RefreshControllerLevels(3,a=>a==JoyAxis.TriggerRight?1.1f:0,_=>false,true)&&adapter.SampleController(3,true)==prior,"invalid native trigger transaction");
        // Same authoritative inputs/time with and without camera zoom.
        string RunActor(bool zoom)
        {
            // Independent equal receipt timestamps, NOT equal camera delta.
            var snapshot = _worldActor.CardinalSession!.Snapshot;
            _worldActor.Configure(snapshot, Arcanum.Simulation.Movement.SimulationTime.At(0));
            _worldGridInput = new(); _worldTestTime = Arcanum.Simulation.Movement.SimulationTime.At(0);
            RetireWorldLifecycle(); _worldAdapter.Clear(); QuarantineWorld();
            SetPreferredWorldZoom(2); Rearm();
            var events = new List<Arcanum.Simulation.Movement.MovementEvent>();
            var evidence = new List<GridStepEvidence>();
            foreach(var dt in new[]{.04,.06,.17,.23,.5})
            {
                Frame(pad with {LeftStick=new(1,0),RightTrigger=zoom?1:0},dt);
                var advance = _worldActor.Session!.LastAdvance!;
                events.AddRange(advance.Movement.Events); evidence.AddRange(advance.Evidence);
            }
            return System.Text.Json.JsonSerializer.Serialize(new {
                snapshot = Arcanum.Simulation.Movement.MovementRuntime.QuerySnapshot(_worldActor.Session!.State),
                events, evidence, frame = _worldActor.CurrentFrame, placement = _readyActorInput
            });
        }
        Require(RunActor(false)==RunActor(true),"equal-receipt full runtime snapshot/events/keys/evidence/animation/placement");
        WorldSettleForSelfTest(pad);
        Require(_readyDiagnosticPlan.WorldBounds==bounds&&_cache.Count==count&&_cache.CreatedCount==created,"immutable envelope/cache");
        SetPreferredWorldZoom(2); ResetWorldScene(); Rearm();
        _camera.UpdateWorld(WorldAnchor,default,new(12,5),false,true,WorldViewport);
        var manual=_camera.FollowState; var center=_camera.State;
        Frame(pad with {RightTrigger=1},.1);
        Require(_camera.FollowState==manual&&_camera.State.X==center.X&&_camera.State.Y==center.Y,"zoom-only manual anchor/return arm");
        // ARC-INPUT-001: the world mouse/keyboard route has no zoom or drag. Wheel,
        // +/- and LMB drag leave camera, zoom and Follow unchanged even with an
        // open gate; exact zoom is the palette's job (below).
        Rearm(); SetPreferredWorldZoom(.9); var free=_camera.State; var freeFollow=_camera.FollowState;
        foreach (var button in new[]{MouseButton.WheelUp,MouseButton.WheelDown})
            _Input(new InputEventMouseButton{ButtonIndex=button,Pressed=true,Position=WorldViewport-Vector2.One});
        _Input(new InputEventKey{Keycode=Key.Plus,Pressed=true}); _Input(new InputEventKey{Keycode=Key.Minus,Pressed=true});
        _Input(new InputEventKey{Keycode=Key.Equal,Pressed=true,ShiftPressed=true});
        _Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=WorldViewport/2});
        _Input(new InputEventMouseMotion{Position=WorldViewport/2+new Vector2(40,20),Relative=new(40,20)});
        Frame(pad);
        _Input(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=WorldViewport/2+new Vector2(40,20)});
        Frame(pad);
        Require(_camera.State==free&&_camera.FollowState==freeFollow&&_worldPreferredZoom==.9,"world wheel/+/-/LMB drag changed camera");
        SetPreferredWorldZoom(3); FitWorldView(); Require(_worldPreferredZoom==3&&_worldZoomStore.Load()==3&&_worldFitView,"Fit preference isolation");
        ResetWorldScene(); WorldSettleForSelfTest(pad); Require(_camera.State.Zoom==3&&_camera.FollowState.Mode==CameraMode.Follow,"R preference precedence");
        if(!WorldModal) ToggleWorldSettings();
        var actorPosition=_worldActor.State.Position; var settings=_worldSettings;
        var zoomControl=_worldPanel!.GetNodeOrNull<SpinBox>("WorldZoom") ?? _worldPanel.FindChild("WorldZoom",true,false) as SpinBox;
        Require(zoomControl is not null,"zoom editor");
        var buttons=_worldPanel.FindChildren("*","Button",true,false).OfType<Button>().ToArray();
        var close=buttons.Single(b=>b.Text.StartsWith("Close",StringComparison.Ordinal));
        var scroll=_worldPanel.FindChildren("*","ScrollContainer",true,false).OfType<ScrollContainer>().Single();
        var operations=0; void Count(double _) => operations++;
        _worldPanel.ZoomRequested+=Count;
        async Task Click(Control control)
        {
            scroll.EnsureControlVisible(control); await NextFrameAsync();
            scroll.EnsureControlVisible(control); await NextFrameAsync();
            var p=control.GetGlobalRect().GetCenter();
            Require(scroll.GetGlobalRect().HasPoint(p)&&GetViewport().GetVisibleRect().HasPoint(p),"native zoom control visible");
            GetViewport().PushInput(new InputEventMouseMotion{Position=p});
            GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=p});
            GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=p});
            await NextFrameAsync();
        }
        try
        {
            foreach(var (title,value) in new[]{("100% (1.00)",1d),("200% (2.00)",2d),("300% (3.00)",3d)})
            {
                close.GrabFocus(); operations=0; await Click(buttons.Single(b=>b.Text==title));
                Require(WorldModal&&_camera.State.Zoom==value&&_camera.Camera.Zoom==Vector2.One*(float)value&&_worldZoomStore.Load()==value&&operations==1,"native preset once "+title);
            }
            await Click(zoomControl!); var editor=zoomControl!.GetLineEdit(); editor.GrabFocus(); editor.SelectAll(); operations=0;
            void KeyEvent(Key k,bool down,uint unicode=0) => GetViewport().PushInput(new InputEventKey{Keycode=k,Pressed=down,Unicode=unicode});
            foreach(var (k,u) in new[]{(Key.Key1,49u),(Key.Period,46u),(Key.Key6,54u)}) {KeyEvent(k,true,u); KeyEvent(k,false);}
            KeyEvent(Key.Backspace,true); KeyEvent(Key.Backspace,false); KeyEvent(Key.Key7,true,55); KeyEvent(Key.Key7,false);
            KeyEvent(Key.Enter,true); KeyEvent(Key.Enter,false); await NextFrameAsync();
            Require(Near(_camera.State.Zoom,1.7)&&operations==1&&Near(_worldZoomStore.Load(),1.7),"native zoom numeric commit once");
            KeyEvent(Key.Tab,true);KeyEvent(Key.Tab,false); Require(GetViewport().GuiGetFocusOwner()!=editor,"zoom Tab");
            await WorldSettingsFocusMatrixForSelfTestAsync();
            Rearm(); var modalZoom=_camera.State.Zoom; Frame(pad with {RightTrigger=1},1);
            Require(_camera.State.Zoom==modalZoom&&zoomControl.Value==modalZoom,"modal trigger leaked");
            zoomControl.GetLineEdit().GrabFocus(); Frame(pad); Frame(pad with {Next=true});
            Require(Near(_camera.State.Zoom,1.75),"controller shoulder numeric adjust");
            Frame(pad); var preset=buttons.Single(b=>b.Text=="200% (2.00)"); preset.GrabFocus();
            GetViewport().PushInput(new InputEventJoypadButton{Device=3,ButtonIndex=JoyButton.A,Pressed=true});
            Frame(pad with {Confirm=true},1);
            GetViewport().PushInput(new InputEventJoypadButton{Device=3,ButtonIndex=JoyButton.A,Pressed=false});
            Require(_camera.State.Zoom==2,"controller preset");
            Require(_worldActor.State.Position==actorPosition&&_worldSettings==settings&&!_worldActor.IsPlaying,"modal zoom changed actor/input settings");
        }
        finally { _worldPanel.ZoomRequested-=Count; if(WorldModal) ToggleWorldSettings(); }
        _worldAdapter.Clear(); _worldReducer.SetStartupDevices(Array.Empty<int>()); SetPreferredWorldZoom(2); ResetWorldScene();
        for(var i=0;i<3;i++) WorldFrameForSelfTest(none,Array.Empty<RawInputFrame>(),.1);
        GD.Print("ARC-GPAD-004 zoom matrix PASS native GUI preset/editor/keyboard/gamepad SIMULATION; held frame/rate/partitions/gates/opposites/actor invariants/wheel/Fit/prefs/nearest camera ratio; physical Xbox/QHD NOT_VERIFIED");
    }
}
