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
        // Characterization of the actual GPAD001 baseline (c0bcbb0): its positive
        // pan translation made the image move right/down. The owner superseded the
        // former false/false compatibility clause: true retains that visual sign,
        // while false reverses only the corresponding camera axis.
        const int historicalImageSign = 1;
        var frame = new RawInputFrame(2, new(.8, .8), new(.8, .8));
        foreach (var axes in new[] { (true, true), (true, false), (false, true), (false, false) })
        {
            var reducer = Armed(2); var intent = reducer.Reduce(frame, new InputContext(InvertCameraX: axes.Item1, InvertCameraY: axes.Item2));
            Assertions.True(intent.Movement.X > 0 && intent.Movement.Y > 0, "camera inversion never changes actor direction");
            Assertions.True(Math.Sign(intent.CameraPan.X) == (axes.Item1 ? historicalImageSign : -historicalImageSign) && Math.Sign(intent.CameraPan.Y) == (axes.Item2 ? historicalImageSign : -historicalImageSign), "each camera inversion combination has the owner-confirmed image sign");
        }
        var ownerDefault = Armed(2).Reduce(frame, new InputContext(InvertCameraX: true, InvertCameraY: true));
        var ownerReversed = Armed(2).Reduce(frame, new InputContext(InvertCameraX: false, InvertCameraY: false));
        Assertions.True(Math.Sign(ownerDefault.CameraPan.X) == historicalImageSign && Math.Sign(ownerDefault.CameraPan.Y) == historicalImageSign, "owner true defaults retain the characterized GPAD001 image-right/down sign");
        Assertions.True(Math.Sign(ownerReversed.CameraPan.X) == -historicalImageSign && Math.Sign(ownerReversed.CameraPan.Y) == -historicalImageSign, "false defaults reverse the characterized image sign on both axes");
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
        Assertions.True(nearby.State.Goal is not null && nearby.State.Goal!.Value.X == 40 && nearby.State.ReplanCount == 1, "nearby goal is an actor-relative stored destination rather than a live direction marker");
        var unchangedGoal = engine.Update(nearby.State, new(1, 0), .05, MovementMode.NearbyGoal, policy, Open);
        Assertions.Equal(unchangedGoal.State.ReplanCount, 2, "goal refreshes once when its cadence and actor-relative hysteresis gates both open");
        var blocked = engine.Update(new MovementState(new(15, 0), new(1, 0)), new(1, 0), .1, MovementMode.NearbyGoal, policy, Wall);
        Assertions.Equal(blocked.State.Outcome, MovementOutcome.Blocked, "wall produces bounded blocked state"); Assertions.True(blocked.State.Goal is null, "blocked goal is cancelled rather than endlessly replanned");
        var heldBlocked = engine.Update(blocked.State, new(1, 0), .1, MovementMode.NearbyGoal, policy, Wall);
        Assertions.True(!heldBlocked.AttemptedMotion && heldBlocked.State.ReplanCount == blocked.State.ReplanCount, "held blocked intent cannot replan indefinitely");
        var redirected = engine.Update(heldBlocked.State, new(0, 1), .1, MovementMode.NearbyGoal, policy, Open);
        Assertions.True(redirected.AttemptedMotion && redirected.State.Outcome != MovementOutcome.Blocked, "materially changed intent safely resumes after block");
        var neutral = engine.Update(nearby.State, default, .1, MovementMode.NearbyGoal, policy, Open);
        Assertions.Equal(neutral.State.Outcome, MovementOutcome.Idle, "neutral stops and clears destination");
        Assertions.True(neutral.State.Goal is null && neutral.State.BlockedIntent is null, "neutral clears both destination and retry latch");

        // Nearby Goal deliberately differs from Direct Intent: direction changes do
        // not replace a stored destination before cadence/hysteresis permit it, then
        // the actor steers toward the replacement destination. A nearby stop is an
        // observable Arrived state rather than an unreachable enum member.
        var semanticPolicy = policy with { GoalCadenceSeconds = .15, GoalHysteresis = 12, StopDistance = 3 };
        var nearbyRight = engine.Update(new MovementState(default, new(1, 0)), new(1, 0), .05, MovementMode.NearbyGoal, semanticPolicy, Open);
        var nearbyTurnBeforeCadence = engine.Update(nearbyRight.State, new(0, 1), .05, MovementMode.NearbyGoal, semanticPolicy, Open);
        Assertions.True(nearbyTurnBeforeCadence.State.Heading.X > .99 && nearbyTurnBeforeCadence.State.Position.X > nearbyRight.State.Position.X, "Nearby Goal keeps steering toward its stored target before cadence");
        var directTurn = engine.Update(nearbyRight.State with { Goal = null, GoalAgeSeconds = 0 }, new(0, 1), .05, MovementMode.DirectIntent, semanticPolicy, Open);
        Assertions.True(directTurn.State.Heading.Y > .99 && directTurn.State.Position.Y > nearbyRight.State.Position.Y, "Direct Intent changes direction immediately");
        var nearbyTurnAfterCadence = engine.Update(nearbyTurnBeforeCadence.State, new(0, 1), .1, MovementMode.NearbyGoal, semanticPolicy, Open);
        Assertions.True(nearbyTurnAfterCadence.State.Heading.Y > .99 && nearbyTurnAfterCadence.State.ReplanCount > nearbyTurnBeforeCadence.State.ReplanCount, "Nearby Goal replaces its destination only after cadence/hysteresis permit it");
        var arrived = engine.Update(new MovementState(default, new(1, 0), new(2, 0), 0), new(0, 1), .1, MovementMode.NearbyGoal, semanticPolicy, Open);
        Assertions.Equal(arrived.State.Outcome, MovementOutcome.Arrived, "Nearby Goal reports an explicit arrival within stop distance");

        // Actor sensitivity is effort, not a reducer-only diagnostic: both movement
        // modes must preserve it through the engine's actual displacement.
        foreach (var mode in new[] { MovementMode.DirectIntent, MovementMode.NearbyGoal })
        {
            var low = engine.Update(new MovementState(default, new(1, 0)), new(.5, 0), .1, mode, policy, Open);
            var high = engine.Update(new MovementState(default, new(1, 0)), new(2, 0), .1, mode, policy, Open);
            Assertions.Near(low.State.Position.X, 5, .001, $"{mode} retains 0.5 actor effort through displacement");
            Assertions.Near(high.State.Position.X, 20, .001, $"{mode} retains 2.0 actor effort through displacement");
            Assertions.Near(high.State.Position.X / low.State.Position.X, 4, .001, $"{mode} actor sensitivity has a 4:1 displacement ratio");

            var one = engine.Update(new MovementState(default, new(1, 0)), new(1, 0), .1, mode, policy, Open);
            var split = engine.Update(new MovementState(default, new(1, 0)), new(1, 0), .04, mode, policy, Open);
            split = engine.Update(split.State, new(1, 0), .06, mode, policy, Open);
            Assertions.Near(split.State.Position.X, one.State.Position.X, .001, $"{mode} open movement is frame-delta consistent");
        }

        // Default 240 px/s / 96 px lookahead policy must retain movement distance
        // through long frames. This specifically guards Nearby Goal from treating
        // its marker as a movement cap and hiding a 0.5/2.0 effort ratio.
        var defaultPolicy = new MovementPolicy();
        foreach (var effort in new[] { .5, 2d })
        {
            var oneLarge = engine.Update(new MovementState(default, new(1, 0)), new(effort, 0), .5, MovementMode.NearbyGoal, defaultPolicy, Open);
            var splitLarge = new MovementState(default, new(1, 0));
            for (var frameIndex = 0; frameIndex < 5; frameIndex++)
                splitLarge = engine.Update(splitLarge, new(effort, 0), .1, MovementMode.NearbyGoal, defaultPolicy, Open).State;
            Assertions.Near(oneLarge.State.Position.X, splitLarge.Position.X, .001, $"default Nearby Goal effort {effort:F1} is frame-partition independent");
            Assertions.Near(oneLarge.State.Position.X, defaultPolicy.Speed * effort * .5, .001, $"default Nearby Goal effort {effort:F1} retains requested displacement");
            Assertions.True(oneLarge.State.Goal is Vec2 goal && Vec2.Distance(goal, oneLarge.State.Position) > defaultPolicy.StopDistance && Vec2.Distance(goal, oneLarge.State.Position) <= defaultPolicy.Lookahead, $"default Nearby Goal retains a bounded stored destination at effort {effort:F1}");
        }

        // The endpoint after 30 px is legal, but the 10–20 px strip is not. An
        // endpoint-only callback would tunnel through it; the bounded sweep must stop
        // at the last safe sampled position and latch held intent.
        bool ThinWall(Vec2 point, double radius) => point.X < 10 || point.X > 20;
        var tunnelPolicy = policy with { MaximumCollisionSamples = 16, MaxCollisionSampleDistance = 4 };
        foreach (var mode in new[] { MovementMode.DirectIntent, MovementMode.NearbyGoal })
        {
            var swept = engine.Update(new MovementState(default, new(1, 0)), new(1, 0), .3, mode, tunnelPolicy, ThinWall);
            Assertions.Equal(swept.State.Outcome, MovementOutcome.Blocked, $"{mode} sweep detects a thin obstacle before a legal endpoint");
            Assertions.True(swept.State.Position.X < 10 && swept.State.Position.X >= 0, $"{mode} sweep retains last safe position");
            var heldSweep = engine.Update(swept.State, new(1, 0), .3, mode, tunnelPolicy, ThinWall);
            Assertions.True(!heldSweep.AttemptedMotion && heldSweep.State.Position == swept.State.Position, $"{mode} held blocked sweep remains bounded");
        }

        // Camera follow is a separate transition table: it consumes the reducer's
        // post-deadzone vectors, so neutral here is also the drift-safe policy
        // neutral. The image-pan vector keeps the historical visual sign while the
        // tracked center moves in the opposite world direction.
        var camera = new CameraFollowEngine();
        var bounds = new CameraBounds(new(0, 0), new(1000, 1000), new(200, 200), 1);
        var center = new Vec2(500, 500);
        var cameraState = new CameraFollowState();
        var manual = camera.Update(cameraState, new(520, 500), center, new(1, 0), new(60, 0), bounds);
        Assertions.Equal(manual.State.Mode, CameraMode.Manual, "right-stick pan immediately takes camera out of Follow");
        Assertions.Near(manual.Center.X, 440, .001, "manual image pan moves camera center oppositely");
        var heldActor = camera.Update(manual.State, new(540, 500), manual.Center, new(1, 0), default, bounds);
        Assertions.Equal(heldActor.State.Mode, CameraMode.Manual, "right release while left remains held does not recenter");
        Assertions.Near(heldActor.Center.X, 440, .001, "manual camera remains fixed while left is held");
        var neutralCamera = camera.Update(heldActor.State, new(540, 500), heldActor.Center, default, default, bounds);
        Assertions.True(neutralCamera.State.RecenterArmed && neutralCamera.State.Mode == CameraMode.Manual, "both neutral domains arm but do not snap the camera");
        var resumed = camera.Update(neutralCamera.State, new(560, 500), neutralCamera.Center, new(1, 0), default, bounds);
        Assertions.Equal(resumed.State.Mode, CameraMode.Follow, "new actor-only neutral-to-active edge resumes Follow");
        Assertions.Near(resumed.Center.X, 560, .001, "Follow recenters directly and deterministically on the moving actor");
        var interrupted = camera.Update(resumed.State, new(580, 500), resumed.Center, new(1, 0), new(40, 0), bounds);
        Assertions.Equal(interrupted.State.Mode, CameraMode.Manual, "new right-stick input interrupts Follow even with actor input");
        Assertions.True(!interrupted.State.RecenterArmed, "simultaneous pan disarms stale recenter eligibility");
        var blockedFollow = camera.Update(resumed.State, new(560, 500), resumed.Center, new(1, 0), default, bounds);
        Assertions.Equal(blockedFollow.Center, new Vec2(560, 500), "held actor input follows actual actor position even when collision blocked movement");
        var smallWorld = camera.ResetCentered(new(10, 20), new CameraBounds(new(0, 0), new(100, 80), new(200, 200), 1));
        Assertions.Equal(smallWorld.Center, new Vec2(50, 40), "world smaller than viewport clamps camera to map midpoint");
        var edge = camera.ResetCentered(new(2, 998), bounds);
        Assertions.Equal(edge.Center, new Vec2(100, 900), "camera bounds account for viewport extent at map edges");
        var quarantined = camera.SafetyReset(interrupted.State);
        var afterSafety = camera.Update(quarantined, new(580, 500), interrupted.Center, default, default, bounds);
        Assertions.Equal(afterSafety.State.Mode, CameraMode.Follow, "modal/focus/disconnect safety reset returns status to Follow");
        Assertions.Equal(afterSafety.Center, interrupted.Center, "safety reset with no fresh actor input does not snap camera on reconnect");
        var afterSafetyMove = camera.Update(afterSafety.State, new(600, 500), afterSafety.Center, new(1, 0), default, bounds);
        Assertions.Equal(afterSafetyMove.Center, new Vec2(600, 500), "fresh actor input resumes safety-reset Follow");
    }

    private static InputStateReducer Armed(int device)
    {
        var reducer = new InputStateReducer(); reducer.SetStartupDevices(new[] { device }); return reducer;
    }
}