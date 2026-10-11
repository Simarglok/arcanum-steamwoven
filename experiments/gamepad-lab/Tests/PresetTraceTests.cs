using Arcanum.Input;
using GamepadLab.Core;

namespace GamepadLab.Tests;

internal static class PresetTraceTests
{
    private static readonly ControllerButtons[] Faces = [ControllerButtons.A, ControllerButtons.X, ControllerButtons.Y, ControllerButtons.B];
    private static IntentFrame Frame(ControllerButtons down, ControllerButtons pressed = default, Vec2 right = default, bool complete = true, int device = 3) =>
        new(default, default, false, false, false, false, false, false, false, 0, 0, InputMethod.Gamepad, true, "TEST literal input") {
            ControllerDown = down, ControllerPressed = pressed, RadialSelection = right, GenuineCompleteObservation = complete, SourceDeviceId = device };
    public static void Run()
    {
        // Expected values are literal test-owned traces, not generated from production tables.
        string[][] contextual = [["interact", "inspect", "context", "back"], ["slot-01", "slot-02", "slot-03", "slot-04"],
            ["interact", "inspect", "context", "back"], ["slot-05", "slot-06", "slot-07", "slot-08"]];
        string[][] quick = [["slot-01", "slot-02", "slot-03", "slot-04"], ["slot-05", "slot-06", "slot-07", "slot-08"],
            ["interact", "inspect", "context", "back"], ["slot-05", "slot-06", "slot-07", "slot-08"]];
        foreach (var (id, expected) in new[] { ("contextual", contextual), ("quick-slots", quick) })
        for (var bank = 0; bank < 4; bank++)
        for (var f = 0; f < 4; f++)
        {
            var modifier = (bank % 2 == 1 ? ControllerButtons.LB : 0) | (bank >= 2 ? ControllerButtons.RB : 0);
            foreach (var order in new[] { 0, 1, 2 })
            {
                var input = new InputStateReducer(); input.SetStartupDevices([3]); var preset = new GamepadPresetReducer(); var state = new PresetState();
                var profile = BuiltInGamepadPresets.Get(id); var context = new InputContext { ExplicitGamepadBindingsEnabled = true };
                PresetStep Observe(ControllerButtons buttons) {
                    var accepted = input.Reduce(new RawInputFrame(3, default) { ControllerButtons = buttons }, context);
                    var step = preset.Reduce(state, accepted, profile, new(SelectedDeviceId: 3)); state = step.State; return step; }
                if (order == 0) { Observe(modifier & ControllerButtons.LB); Observe(modifier); }
                if (order == 1) { Observe(modifier & ControllerButtons.RB); Observe(modifier); }
                var step = Observe(modifier | Faces[f]);
                Assertions.Equal(step.DiscreteIntents.Count, 1, $"{id} bank{bank} face{f} one activation");
                var intent = step.DiscreteIntents[0]; Assertions.Equal(intent.SlotId ?? intent.ActionId, expected[bank][f], "literal face trace");
                Assertions.Equal(step.Highlight, bank >= 2, "RB highlight alias");
                for (var held = 0; held < 8; held++) Assertions.Equal(Observe(modifier | Faces[f]).DiscreteIntents.Count, 0, "held no repeat");
                Assertions.Equal(Observe(Faces[f]).DiscreteIntents.Count, 0, "modifier release no reactivation");
                Observe(default);
                var fresh = Observe(Faces[f]).DiscreteIntents.Single();
                Assertions.Equal(fresh.SlotId ?? fresh.ActionId, expected[0][f], "release enables fresh face");
            }
        }
        foreach (var id in new[] { "contextual", "quick-slots", "radial" })
        {
            var p = BuiltInGamepadPresets.Get(id); var reducer = new GamepadPresetReducer();
            var modal = reducer.Reduce(new(), Frame(ControllerButtons.A|ControllerButtons.B|ControllerButtons.LB, ControllerButtons.A|ControllerButtons.B), p, new(PresetLayer.Modal));
            Assertions.Equal(modal.DiscreteIntents.Single().ActionId, "modal-cancel", "modal simultaneous cancel priority");
            Assertions.True(!modal.DomainOwnership.Actor && !modal.DomainOwnership.Camera && !modal.DomainOwnership.Zoom, "modal world gate");
            var pending = reducer.Reduce(new(), Frame(ControllerButtons.A, ControllerButtons.A), p, new(PresetLayer.PendingAction));
            Assertions.Equal(pending.DiscreteIntents.Single().ActionId, "modal-confirm", "pending never consumes world slot");
            var menu = reducer.Reduce(new(), Frame(ControllerButtons.Menu|ControllerButtons.A, ControllerButtons.Menu|ControllerButtons.A), p, new());
            Assertions.Equal(menu.DiscreteIntents.Single().ActionId, "settings", "entry consumes lower edges");
        }
        Wheel(); SwappedWheel(); Quarantine();
        Console.WriteLine("Preset literal face/chord/held/modal/wheel/quarantine traces: PASS");
    }
    private static void Wheel()
    {
        var p = BuiltInGamepadPresets.Get("radial"); var r = new GamepadPresetReducer(); var state = new PresetState();
        PresetStep Step(ControllerButtons down, ControllerButtons pressed=default, Vec2 rs=default, bool complete=true, int device=3)
        { var s=r.Reduce(state,Frame(down,pressed,rs,complete,device),p,new(SelectedDeviceId:3)); state=s.State; return s; }
        var open=Step(ControllerButtons.LB,ControllerButtons.LB); Assertions.True(open.State.WheelOpen && open.DomainOwnership.Actor && !open.DomainOwnership.Camera,"wheel domain ownership");
        Assertions.Equal(Step(ControllerButtons.LB|ControllerButtons.A,ControllerButtons.A).DiscreteIntents.Single().ActionId,"wheel-no-selection","no arbitrary default");
        Step(ControllerButtons.LB,rs:new(0,-1)); Step(ControllerButtons.LB);
        var choice=Step(ControllerButtons.LB|ControllerButtons.A,ControllerButtons.A);
        Assertions.Equal(choice.DiscreteIntents.Single().SlotId!,"slot-01","up latches01 at neutral");
        Assertions.True(!choice.State.WheelOpen && choice.State.AwaitingCameraNeutral,"close retains RS ownership");
        Assertions.True(!Step(ControllerButtons.LB,complete:false).State.WheelOpen,"held LB cannot reopen");
        Assertions.True(!Step(default,rs:new(1,0)).DomainOwnership.Camera,"held RS cannot pan after close");
        Assertions.True(Step(default,device:-1).State.AwaitingCameraNeutral,"keyboard zeros not selected RS");
        Assertions.True(Step(default,complete:false).State.AwaitingCameraNeutral,"incomplete not neutral");
        Assertions.True(!Step(default).DomainOwnership.Camera && !state.AwaitingCameraNeutral,"genuine neutral handoff no pan on same observation");
        Assertions.True(Step(default,rs:new(1,0)).DomainOwnership.Camera,"fresh RS can pan");
        Step(ControllerButtons.LB,ControllerButtons.LB); Step(ControllerButtons.LB,rs:new(0,-1));
        Assertions.Equal(Step(ControllerButtons.A,ControllerButtons.A).DiscreteIntents.Single().ActionId,"wheel-cancel","release+A conservatively cancels");
        Step(default); Step(ControllerButtons.LB,ControllerButtons.LB); Step(ControllerButtons.LB,rs:new(0,-1));
        Assertions.Equal(Step(ControllerButtons.LB|ControllerButtons.B,ControllerButtons.B).DiscreteIntents.Single().ActionId,"wheel-cancel","B cancels");
        // Literal test-local direction centers for all ten and halfway clockwise boundaries.
        Vec2[] centers=[new(0,-1),new(.5877852523,-.8090169944),new(.9510565163,-.3090169944),new(.9510565163,.3090169944),new(.5877852523,.8090169944),new(0,1),new(-.5877852523,.8090169944),new(-.9510565163,.3090169944),new(-.9510565163,-.3090169944),new(-.5877852523,-.8090169944)];
        for(var i=0;i<10;i++) Assertions.Equal(GamepadPresetReducer.Sector(centers[i]),i,"literal sector center");
        Assertions.Equal(GamepadPresetReducer.Sector(new(.30901699437494745,-.9510565162951535)),1,"halfway clockwise");
        Assertions.Equal(GamepadPresetReducer.Sector(new(-.30901699437494745,-.9510565162951535)),0,"halfway wrap clockwise");
    }
    private static void SwappedWheel()
    {
        var profile = BuiltInGamepadPresets.Get("radial").CreateCopy().WithShoulderRoles("rb", "lb");
        var input = new InputStateReducer(); input.SetStartupDevices([3]);
        var reducer = new GamepadPresetReducer(); var state = new PresetState();
        var executor = new LabActionExecutor();
        var context = new InputContext(AdvanceTime: true) { ExplicitGamepadBindingsEnabled = true };
        long observation = 0;
        PresetStep Observe(ControllerButtons down, Vec2 right = default, int device = 3, bool complete = true)
        {
            var frame = input.Reduce(new RawInputFrame(device, default, right) { ControllerButtons = down,
                CompleteObservation = complete, FrameObservationId = ++observation }, context);
            var step = reducer.Reduce(state, frame, profile, new(SelectedDeviceId: 3)); state = step.State;
            foreach (var action in step.DiscreteIntents.Where(i => i.ActionId == "quick-slot"))
                executor.UseSlot(action.SlotId!, LabSlots.Default, action.SelectingEdgeId, [], default);
            return step;
        }
        var oldLb = Observe(ControllerButtons.LB);
        Assertions.True(oldLb.Highlight && !state.WheelOpen && oldLb.DiscreteIntents.Count == 0, "physical old LB highlights only, cannot open");
        Observe(ControllerButtons.None);
        foreach (var chord in new[] { ControllerButtons.None, ControllerButtons.LB })
        {
            var open = Observe(ControllerButtons.RB | chord);
            Assertions.True(state.WheelOpen && open.DomainOwnership.Actor && open.DomainOwnership.Zoom && !open.DomainOwnership.Camera
                && open.Highlight == (chord == ControllerButtons.LB), "literal RB and RB+LB open with live actor/highlight domains");
            Assertions.Equal(open.DiscreteIntents.Single().ActionId, "wheel-open", "one opener intent");
            Assertions.Equal(Observe(ControllerButtons.RB | chord | ControllerButtons.A).DiscreteIntents.Single().ActionId, "wheel-no-selection", "no-selection zero use");
            Assertions.Equal(executor.ResourceUses, 0, "no-selection not activation");
            Observe(ControllerButtons.RB | chord, new(0, -1)); Observe(ControllerButtons.RB | chord);
            Assertions.Equal(state.WheelSector!.Value, 0, "up01 latched while thumb moves to A");
            Assertions.Equal(Observe(chord | ControllerButtons.A).DiscreteIntents.Single().ActionId, "wheel-cancel", "RB release+A cancellation precedes confirm");
            Assertions.Equal(executor.ResourceUses, 0, "release precedence no resource");
            Observe(ControllerButtons.None);
        }
        Vec2[] centers = [new(0,-1),new(.5877852523,-.8090169944),new(.9510565163,-.3090169944),new(.9510565163,.3090169944),new(.5877852523,.8090169944),new(0,1),new(-.5877852523,.8090169944),new(-.9510565163,.3090169944),new(-.9510565163,-.3090169944),new(-.5877852523,-.8090169944)];
        string[] slots = ["slot-01","slot-02","slot-03","slot-04","slot-05","slot-06","slot-07","slot-08","slot-09","slot-10"];
        for (var n = 0; n < 10; n++)
        {
            Observe(ControllerButtons.None); Observe(ControllerButtons.RB); Observe(ControllerButtons.RB, centers[n]);
            Assertions.Equal(state.WheelSector!.Value, n, "literal physical RS sector after role swap");
            Observe(ControllerButtons.RB);
            var choice = Observe(ControllerButtons.RB | ControllerButtons.A);
            Assertions.Equal(choice.DiscreteIntents.Single().SlotId!, slots[n], "all ten literal IDs including09/10");
            Assertions.True(!state.WheelOpen && state.AwaitingCameraNeutral && !choice.DomainOwnership.Camera, "close preserves RS ownership");
            // A held event callback is not a complete neutral frame; keep RS handoff pending.
            Assertions.Equal(Observe(ControllerButtons.RB | ControllerButtons.A, complete: false).DiscreteIntents.Count, 0, "held A/RB no repeat/reopen");
            Assertions.True(!Observe(ControllerButtons.RB, new(1,0)).DomainOwnership.Camera, "held selected RS cannot pan");
            Observe(ControllerButtons.None, device: -1); Assertions.True(state.AwaitingCameraNeutral, "keyboard zero cannot hand off");
            Observe(ControllerButtons.RB, complete: false); Assertions.True(state.AwaitingCameraNeutral, "incomplete zero cannot hand off");
            var neutral = Observe(ControllerButtons.RB);
            Assertions.True(!state.AwaitingCameraNeutral && !neutral.DomainOwnership.Camera, "genuine selected neutral hands off without same-frame pan");
            Assertions.True(Observe(ControllerButtons.RB, new(1,0)).DomainOwnership.Camera && !state.WheelOpen, "fresh RS pan allowed without held opener reopening");
            executor.Cancel(1000 + n);
        }
        Assertions.Equal(executor.ResourceUses, 3, "only literal self slots01/05/09 activate; missing targets and ground preview do not");
        Observe(ControllerButtons.None); Observe(ControllerButtons.RB, new(0,-1));
        Assertions.Equal(Observe(ControllerButtons.RB | ControllerButtons.B | ControllerButtons.A).DiscreteIntents.Single().ActionId, "wheel-cancel", "B beats simultaneous A");
        Observe(ControllerButtons.None); Observe(ControllerButtons.RB);
        Assertions.Equal(Observe(ControllerButtons.None).DiscreteIntents.Single().ActionId, "wheel-cancel", "RB release closes unselected wheel");
        var modal = reducer.Reduce(new(), Frame(ControllerButtons.A | ControllerButtons.B, ControllerButtons.A | ControllerButtons.B), profile, new(PresetLayer.Modal));
        Assertions.Equal(modal.DiscreteIntents.Single().ActionId, "modal-cancel", "swapped modal fixed B priority");
        var menu = reducer.Reduce(new(), Frame(ControllerButtons.Menu | ControllerButtons.RB, ControllerButtons.Menu | ControllerButtons.RB), profile, new());
        Assertions.Equal(menu.DiscreteIntents.Single().ActionId, "settings", "Menu safety beats new opener");
        Console.WriteLine("Swapped Radial production reducer: physical RB/LB/chords/latch/release/A-B/ten sectors/RS handoff/resource oracle PASS");
    }
    private static void Quarantine()
    {
        var r=new InputStateReducer(); r.SetStartupDevices([3]); r.Quarantine();
        var context=new InputContext(AdvanceTime:true){ExplicitGamepadBindingsEnabled=true,WorldZoomEnabled=true};
        RawInputFrame Neutral(long frame,int device=3,bool complete=true)=>new(device,default){CompleteObservation=complete,FrameObservationId=frame};
        r.Reduce(Neutral(1),context); r.Reduce(Neutral(1),context);
        Assertions.True(!r.Reduce(Neutral(2) with {ControllerButtons=ControllerButtons.A},context).Accepted,"duplicate callbacks don't rearm");
        r.Reduce(Neutral(3),context); r.Reduce(Neutral(4,complete:false),context); r.Reduce(Neutral(5),context);
        Assertions.True(!r.Reduce(Neutral(6) with {ControllerButtons=ControllerButtons.A},context).Accepted,"incomplete breaks consecutive neutral");
        r.Reduce(Neutral(7,-1),context); r.Reduce(Neutral(8,5),context);
        Assertions.True(!r.Reduce(Neutral(9) with {ControllerButtons=ControllerButtons.A},context).Accepted,"other devices cannot rearm");
        r.Reduce(Neutral(10) with {LeftTrigger=1,RightTrigger=1},context); r.Reduce(Neutral(11),context);
        Assertions.True(!r.Reduce(Neutral(12) with {ControllerButtons=ControllerButtons.A},context).Accepted,"opposing triggers remain nonneutral");
        r.Reduce(Neutral(13),context); r.Reduce(Neutral(14),context);
        var accepted=r.Reduce(Neutral(15) with {ControllerButtons=ControllerButtons.A},context);
        Assertions.True(accepted.Accepted && accepted.ControllerPressed==ControllerButtons.A && !accepted.Confirm,"fresh selected edge only physical");
        Assertions.Equal(r.Reduce(Neutral(16) with {ControllerButtons=ControllerButtons.A},context).ControllerPressed,ControllerButtons.None,"event/poll same owner");
    }
}
