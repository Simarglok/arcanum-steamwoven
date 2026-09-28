using GamepadLab.Core;

namespace GamepadLab.Tests;

internal static class InputStateTests
{
    public static void Run()
    {
        Assertions.Equal(LabSettings.Default.InvertCameraX, true, "fresh settings invert camera X");
        Assertions.Equal(LabSettings.Default.InvertCameraY, true, "fresh settings invert camera Y");
        Assertions.Equal(LabSettingsCodec.DeserializeOrDefault(null), LabSettings.Default, "missing settings uses v1 inverted defaults");
        Assertions.Equal(LabSettingsCodec.DeserializeOrDefault("not json"), LabSettings.Default, "corrupt settings falls back safely");
        var persisted = new LabSettings(InvertCameraX: false, InvertCameraY: true, CameraDeadzone: .3, ActorSensitivity: 1.5);
        Assertions.Equal(LabSettingsCodec.DeserializeOrDefault(LabSettingsCodec.Serialize(persisted)), persisted, "versioned settings round trip");
        Assertions.Equal(LabSettingsCodec.DeserializeOrDefault("{\"version\":99}"), LabSettings.Default, "unknown version falls back");
        Assertions.Equal(LabSettingsCodec.DeserializeOrDefault("{\"movementMode\":999}"), LabSettings.Default, "undefined movement mode falls back safely");

        Assertions.Equal(Vec2.CircularDeadzone(new(.19, 0), .2), default(Vec2), "one circular deadzone removes drift");
        Assertions.Near(Vec2.CircularDeadzone(new(.6, 0), .2).X, .5, .0001, "deadzone radial rescale");
        Assertions.Near(new Vec2(1, 1).Normalized().Length, 1, .0001, "diagonal normalization");
        var frame = new RawInputFrame(2, new(.8, .8), new(.8, .8));
        foreach (var axes in new[] { (true, true), (true, false), (false, true), (false, false) })
        {
            var reducer = Armed(2); var intent = reducer.Reduce(frame, new InputContext(InvertCameraX: axes.Item1, InvertCameraY: axes.Item2));
            Assertions.True(intent.Movement.X > 0 && intent.Movement.Y > 0, "camera inversion never changes actor direction");
            Assertions.True(Math.Sign(intent.CameraPan.X) == (axes.Item1 ? 1 : -1) && Math.Sign(intent.CameraPan.Y) == (axes.Item2 ? 1 : -1), "each camera inversion combination has explicit sign");
        }
        var simultaneous = Armed(2).Reduce(new RawInputFrame(2, new(.8, 0), new(0, .8)), new InputContext(Delta: .1));
        Assertions.True(simultaneous.Movement.X > 0 && simultaneous.CameraPan.Y > 0 && !simultaneous.Confirm, "sticks operate independently without interaction pulse");
        var deltaOne = Armed(2).Reduce(new RawInputFrame(2, default, new(1, 0)), new InputContext(Delta: .1)).CameraPan.X;
        var deltaSplit = Armed(2).Reduce(new RawInputFrame(2, default, new(1, 0)), new InputContext(Delta: .04)).CameraPan.X + Armed(2).Reduce(new RawInputFrame(2, default, new(1, 0)), new InputContext(Delta: .06)).CameraPan.X;
        Assertions.Near(deltaOne, deltaSplit, .001, "camera update is frame-rate independent");
        Assertions.Near(Armed(2).Reduce(new RawInputFrame(2, default, new(1, 0)), new InputContext(Zoom: 2)).CameraPan.X, 520d / 60 / 2, .001, "camera pan scales with zoom only");
        var lowSensitivity = Armed(2).Reduce(new RawInputFrame(2, new(1, 0), new(1, 0)), new InputContext(ActorSensitivity: .5, CameraSensitivity: .5, Delta: .1));
        var highSensitivity = Armed(2).Reduce(new RawInputFrame(2, new(1, 0), new(1, 0)), new InputContext(ActorSensitivity: 2, CameraSensitivity: 2, Delta: .1));
        Assertions.Near(lowSensitivity.Movement.X, .5, .001, "controller actor sensitivity is linear at 0.5");
        Assertions.Near(highSensitivity.Movement.X, 2, .001, "controller actor sensitivity is linear at 2.0");
        Assertions.Near(highSensitivity.CameraPan.X / lowSensitivity.CameraPan.X, 4, .001, "controller camera sensitivity is linear from 0.5 to 2.0");
        var keyboardLow = new InputStateReducer().Reduce(new RawInputFrame(-1, default, KeyboardActor: new(1, 0), KeyboardCamera: new(1, 0)), new InputContext(ActorSensitivity: .5, CameraSensitivity: .5, Delta: .1));
        var keyboardHigh = new InputStateReducer().Reduce(new RawInputFrame(-1, default, KeyboardActor: new(1, 0), KeyboardCamera: new(1, 0)), new InputContext(ActorSensitivity: 2, CameraSensitivity: 2, Delta: .1));
        Assertions.Near(keyboardLow.Movement.X, .5, .001, "keyboard actor sensitivity is linear at 0.5");
        Assertions.Near(keyboardHigh.Movement.X, 2, .001, "keyboard actor sensitivity is linear at 2.0");
        Assertions.Near(keyboardHigh.CameraPan.X / keyboardLow.CameraPan.X, 4, .001, "keyboard camera sensitivity is linear from 0.5 to 2.0");

        var edges = Armed(2); var edgeContext = new InputContext();
        Assertions.True(edges.Reduce(new RawInputFrame(2, default, Confirm: true), edgeContext).Confirm, "controller confirm is a rising-edge pulse");
        Assertions.True(!edges.Reduce(new RawInputFrame(2, default, Confirm: true), edgeContext).Confirm, "held confirm does not repeat");
        edges.Reduce(new RawInputFrame(2, default), edgeContext);
        Assertions.True(edges.Reduce(new RawInputFrame(2, default, Settings: true), edgeContext).Settings, "release clears stale edge state");
        edges.Reset(); edges.Reduce(new RawInputFrame(2, default), edgeContext); edges.Reduce(new RawInputFrame(2, default), edgeContext);
        Assertions.True(edges.Reduce(new RawInputFrame(2, default, Context: true), edgeContext).Context, "reset also clears stale held edge state");
        var method = Armed(2); method.Reduce(new RawInputFrame(2, default, Confirm: true), edgeContext);
        method.Reduce(new RawInputFrame(-1, default, MouseLogicalDistance: 2), edgeContext); method.Reduce(new RawInputFrame(-1, default, MouseLogicalDistance: 2), edgeContext); method.Reduce(new RawInputFrame(-1, default, MouseLogicalDistance: 2), edgeContext);
        Assertions.Equal(method.Method, InputMethod.KeyboardMouse, "six logical mouse pixels switch provenance");
        method.Reduce(new RawInputFrame(2, default), new InputContext(Delta: .151, AdvanceTime: true)); method.Reduce(new RawInputFrame(2, default, Confirm: true), edgeContext);
        method.Reduce(new RawInputFrame(-1, default, MouseLogicalDistance: 3), edgeContext); method.Reduce(new RawInputFrame(-1, default), new InputContext(Delta: .151, AdvanceTime: true)); method.Reduce(new RawInputFrame(-1, default, MouseLogicalDistance: 3), edgeContext);
        Assertions.Equal(method.Method, InputMethod.Gamepad, "mouse movement outside 150ms window does not oscillate provenance");

        var repeat = new MenuRepeat(); Assertions.Equal(repeat.Update(1, 0, 0), 1, "digital menu repeat starts immediately"); Assertions.Equal(repeat.Update(1, 0, .34), 0, "digital menu repeat honours initial delay"); Assertions.Equal(repeat.Update(1, 0, .01), 1, "digital menu repeat fires at threshold");
        var analogRepeat = new MenuRepeat(); Assertions.Equal(analogRepeat.Update(0, .64, 0), 0, "analog menu repeat ignores below threshold"); Assertions.Equal(analogRepeat.Update(0, .65, 0), 1, "analog menu repeat engages at threshold"); Assertions.Equal(analogRepeat.Update(0, .45, 0), 0, "analog menu repeat releases at threshold");

        var device = Armed(2); Assertions.True(!device.Reduce(new RawInputFrame(5, new(1, 0)), new InputContext()).Accepted, "nonactive device cannot move actor");
        device.FocusChanged(false); Assertions.True(!device.Reduce(new RawInputFrame(2, default, new(.8, 0)), new InputContext()).Accepted, "held right stick cannot bypass focus quarantine");
        device.Reduce(new RawInputFrame(2, default, default), new InputContext()); device.Reduce(new RawInputFrame(2, default, default), new InputContext());
        Assertions.True(device.Reduce(new RawInputFrame(2, new(.8, 0), new(.8, 0)), new InputContext()).Accepted, "both-stick neutral sequence rearms input");
        var modal = Armed(2).Reduce(new RawInputFrame(2, new(1, 0), new(1, 0), Confirm: true), new InputContext(Modal: true));
        Assertions.True(modal.Movement == default && modal.CameraPan == default && modal.Confirm, "modal suppresses both world domains while retaining UI confirm");
        var disconnected = Armed(2); disconnected.Disconnect(2); Assertions.True(!disconnected.Reduce(new RawInputFrame(2, new(1, 0)), new InputContext()).Accepted, "disconnect clears old-device intent");

        var engine = new MovementEngine(); var policy = new MovementPolicy(Speed: 100, Lookahead: 40, GoalCadenceSeconds: .1, GoalHysteresis: 4);
        bool Open(Vec2 point, double radius) => true; bool Wall(Vec2 point, double radius) => point.X < 20;
        var direct = engine.Update(new MovementState(default, new(1, 0)), new(1, 0), .1, MovementMode.DirectIntent, policy, Open);
        Assertions.Near(direct.State.Position.X, 10, .001, "direct intent advances at explicit prototype speed");
        var nearby = engine.Update(new MovementState(default, new(1, 0)), new(1, 0), .1, MovementMode.NearbyGoal, policy, Open);
        Assertions.True(nearby.State.Goal is not null && nearby.State.Goal!.Value.X == 40 && nearby.State.ReplanCount == 1, "nearby goal is actor-relative and bounded");
        var unchangedGoal = engine.Update(nearby.State, new(1, 0), .05, MovementMode.NearbyGoal, policy, Open);
        Assertions.Equal(unchangedGoal.State.ReplanCount, 1, "goal cadence suppresses same-target replan");
        var blocked = engine.Update(new MovementState(new(15, 0), new(1, 0)), new(1, 0), .1, MovementMode.NearbyGoal, policy, Wall);
        Assertions.Equal(blocked.State.Outcome, MovementOutcome.Blocked, "wall produces bounded blocked state"); Assertions.True(blocked.State.Goal is null, "blocked goal is cancelled rather than endlessly replanned");
        var heldBlocked = engine.Update(blocked.State, new(1, 0), .1, MovementMode.NearbyGoal, policy, Wall);
        Assertions.True(!heldBlocked.AttemptedMotion && heldBlocked.State.ReplanCount == blocked.State.ReplanCount, "held blocked intent cannot replan indefinitely");
        var redirected = engine.Update(heldBlocked.State, new(0, 1), .1, MovementMode.NearbyGoal, policy, Open);
        Assertions.True(redirected.AttemptedMotion && redirected.State.Outcome != MovementOutcome.Blocked, "materially changed intent safely resumes after block");
        var neutral = engine.Update(nearby.State, default, .1, MovementMode.NearbyGoal, policy, Open);
        Assertions.Equal(neutral.State.Outcome, MovementOutcome.Idle, "neutral stops and clears destination");
        Assertions.True(neutral.State.Goal is null && neutral.State.BlockedIntent is null, "neutral clears both destination and retry latch");
    }

    private static InputStateReducer Armed(int device)
    {
        var reducer = new InputStateReducer(); reducer.SetStartupDevices(new[] { device }); return reducer;
    }
}