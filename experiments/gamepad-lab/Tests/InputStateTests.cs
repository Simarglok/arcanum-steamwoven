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
        var neutral = engine.Update(nearby.State, default, .1, MovementMode.NearbyGoal, policy, Open);
        Assertions.Equal(neutral.State.Outcome, MovementOutcome.Idle, "neutral stops and clears destination");
    }

    private static InputStateReducer Armed(int device)
    {
        var reducer = new InputStateReducer(); reducer.SetStartupDevices(new[] { device }); return reducer;
    }
}