using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.TerrainViewer.Tests;

[TestClass]
public sealed class DiagnosticWorldPolicyTests
{
    private static void Near(double expected, double actual) => Assert.AreEqual(expected, actual, 1e-9);
    private static DiagnosticActorState State(double x, double y, int facing = 0, ActorDiagnosticAction action = ActorDiagnosticAction.Idle)
        => new(new(x, y), facing, action);

    [TestMethod]
    public void WorldArgumentsAreClosedAndKeepStandaloneModesSeparate()
    {
        string[] complete = ["--mode", "world-diagnostic", "--module", "m", "--resource", "terrain-r",
            "--actor-resource", "actor-r", "--prototypes", "p", "--source-id", "fixture"];
        var args = TerrainViewerArguments.Parse(complete);
        Assert.AreEqual(TerrainViewerMode.WorldDiagnostic, args.Mode);
        Assert.AreEqual("terrain-r", args.ResourcePath); Assert.AreEqual("actor-r", args.ActorResourcePath);
        Assert.AreEqual(6L, args.ActorInput!.MapX); Assert.AreEqual(1L, args.ActorInput.MapY);
        Assert.AreEqual(5, args.ActorInput.Facing);
        Assert.AreEqual(ActorDiagnosticAction.Idle, args.ActorInput.Action); Assert.AreEqual(0, args.ActorInput.Frame);
        foreach (var option in new[] { "--module", "--resource", "--actor-resource", "--prototypes", "--source-id" })
        {
            var index = Array.IndexOf(complete, option);
            Assert.ThrowsExactly<ArgumentException>(() => TerrainViewerArguments.Parse(complete.Where((_,i) => i != index && i != index+1).ToArray()));
            Assert.ThrowsExactly<ArgumentException>(() => TerrainViewerArguments.Parse([..complete,option,"duplicate"]));
        }
        foreach (var option in new[] { "--actor-map-x", "--actor-map-y", "--actor-camera-origin-x", "--actor-camera-origin-y", "--actor-offset-x",
            "--actor-offset-y", "--actor-render-branch", "--actor-scale-percent", "--actor-shrunk", "--actor-action", "--actor-facing", "--actor-frame" })
            Assert.ThrowsExactly<ArgumentException>(() => TerrainViewerArguments.Parse([..complete,option,"0"]));
        foreach (var old in new[] { "terrain-only", "combined" })
            Assert.ThrowsExactly<ArgumentException>(() => TerrainViewerArguments.Parse(["--mode",old,..complete.Skip(2)]));
    }

    [TestMethod]
    public void ModeAwareUnionBudgetsUseLiteralIndependentCapsAndByteEdges()
    {
        foreach (var (mode, cap) in new[] { (TerrainViewerMode.TerrainOnly,4096), (TerrainViewerMode.CombinedStaticDiagnostic,4101),
            (TerrainViewerMode.ActorDiagnostic,4291), (TerrainViewerMode.WorldDiagnostic,4156) })
        {
            Assert.AreEqual(4L, TerrainCachePolicy.AddCheckedFrame(0,cap-1,1,1,mode));
            Assert.ThrowsExactly<TerrainViewerException>(() => TerrainCachePolicy.AddCheckedFrame(0,cap,1,1,mode));
            Assert.AreEqual(268435456L,TerrainCachePolicy.AddCheckedFrame(268435452,0,1,1,mode));
            Assert.ThrowsExactly<TerrainViewerException>(() => TerrainCachePolicy.AddCheckedFrame(268435456,0,1,1,mode));
            Assert.ThrowsExactly<TerrainViewerException>(() => TerrainCachePolicy.AddCheckedFrame(0,0,0,1,mode));
        }
    }

    [TestMethod]
    public void ProjectionAndInverseHaveIndependentLiteralOracles()
    {
        foreach (var (x, y, u, v) in new[] { (0d, 0d, 0d, 0d), (1d, 0d, -40d, 20d), (0d, 1d, 40d, 20d), (30d, 32d, 80d, 1240d) })
        {
            Assert.AreEqual(new Vec2(u, v), DiagnosticActorMotion.Project(new(x, y)));
            Assert.AreEqual(new DiagnosticSectorPosition(x, y), DiagnosticActorMotion.Unproject(new(u, v)));
        }
    }

    [TestMethod]
    public void SpeedPartitionsAndDiagonalAreProjectedNotCellNormalized()
    {
        foreach (var effort in new[] { .5, 1, 2 })
        {
            var full = new DiagnosticActorController(); var split = new DiagnosticActorController();
            full.Update(new(effort, 0), 1);
            foreach (var delta in new[] { .04, .06, .17, .23, .5 }) split.Update(new(effort, 0), delta);
            Near(30 - 3 * effort, full.State.Position.X); Near(32 + 3 * effort, full.State.Position.Y);
            Near(full.State.Position.X, split.State.Position.X); Near(full.State.Position.Y, split.State.Position.Y);
            Assert.AreEqual(full.CurrentFrame, split.CurrentFrame); Assert.AreEqual(2, full.State.Facing);
            var diagonal = new DiagnosticActorMotion().Update(State(30, 32), new(effort / Math.Sqrt(2), effort / Math.Sqrt(2)), 1);
            // Test-only independent arithmetic: screen distance 240*effort, inverse cell components 6-3 and 6+3.
            Near(30 + 3 * effort / Math.Sqrt(2), diagonal.State.Position.X);
            Near(32 + 9 * effort / Math.Sqrt(2), diagonal.State.Position.Y);
            var projected = DiagnosticActorMotion.Project(diagonal.State.Position) - new Vec2(80, 1240);
            Near(240 * effort, projected.Length); Assert.AreEqual(3, diagonal.State.Facing);
        }
    }

    [TestMethod]
    public void BoundariesSlideAndOrderedActionTimeAgreeAcrossPartitions()
    {
        var motion = new DiagnosticActorMotion();
        var slide = motion.Update(State(62, 62), new(0, 1), 1);
        Assert.AreEqual(new DiagnosticSectorPosition(63, 63), slide.State.Position);
        Assert.AreEqual(ActorDiagnosticAction.Idle, slide.State.Action);
        Near(1d / 6, slide.Segments[0].Seconds); Near(5d / 6, slide.Segments[1].Seconds);
        Assert.AreEqual(4, slide.State.Facing);
        var tangent = motion.Update(State(0, 32), new(1, 0), 1);
        Assert.AreEqual(new DiagnosticSectorPosition(0, 35), tangent.State.Position);
        Assert.AreEqual(ActorDiagnosticAction.Walk, tangent.State.Action);
        Assert.AreEqual(3, tangent.State.Facing); Assert.IsTrue(tangent.BoundaryLimited);
        var held = motion.Update(State(0, 63, 7), new(1, 0), 1);
        Assert.AreEqual(State(0, 63, 7), held.State); Assert.IsFalse(held.Moved);
        var inward = motion.Update(held.State, new(-1, 0), .1);
        Assert.IsTrue(inward.Moved); Assert.AreEqual(6, inward.State.Facing);
        // Reset position then reach the right/down corner at two different hit times.
        var longFrame = new DiagnosticActorController(); var shortFrames = new DiagnosticActorController();
        longFrame.Update(new(0, 1), 8);
        foreach (var delta in new[] { .04, .06, 4.5, 3.4 }) shortFrames.Update(new(0, 1), delta);
        Assert.AreEqual(new DiagnosticSectorPosition(63, 63), longFrame.State.Position);
        Assert.AreEqual(longFrame.State, shortFrames.State); Assert.AreEqual(longFrame.CurrentFrame, shortFrames.CurrentFrame);
        Assert.AreEqual(ActorDiagnosticAction.Idle, longFrame.State.Action);
        Assert.IsTrue(slide.Segments.Count <= 3);
    }

    [TestMethod]
    public void AllOctantsAndClockwiseHalfTiesUseLiteralOpaqueNumbers()
    {
        var vectors = new[] { new Vec2(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1) };
        var groups = new[] { 0, 7, 6, 5, 4, 5, 6, 7 };
        for (var facing = 0; facing < 8; facing++)
        {
            Assert.AreEqual(facing, DiagnosticActorMotion.MapFacing(vectors[facing]));
            var binding = ActorDiagnosticScenePlan.MapFacing(facing);
            Assert.AreEqual(groups[facing], binding.Group); Assert.AreEqual(facing is >= 1 and <= 3, binding.FlipX);
            var angle = (facing + .5) * Math.PI / 4;
            Assert.AreEqual((facing + 1) % 8, DiagnosticActorMotion.MapFacing(new(Math.Sin(angle), -Math.Cos(angle))));
            var before = angle - 1e-7;
            Assert.AreEqual(facing, DiagnosticActorMotion.MapFacing(new(Math.Sin(before), -Math.Cos(before))));
        }
    }

    [TestMethod]
    public void CadenceTransitionsFacingIntervalsZeroAndInvalidAreTransactional()
    {
        var actor = new DiagnosticActorController();
        actor.Update(new(1, 0), .04); Assert.AreEqual(0, actor.CurrentFrame);
        actor.Update(new(0, 1), .06); Assert.AreEqual(1, actor.CurrentFrame);
        actor.Update(new(0, 1), .7); Assert.AreEqual(0, actor.CurrentFrame);
        var state = actor.State;
        Assert.AreEqual(DiagnosticMotionStatus.InvalidInput, actor.Update(new(1, 0), double.NaN).Status);
        Assert.AreEqual(state, actor.State); Assert.AreEqual(0, actor.CurrentFrame);
        actor.Update(new(1, 0), 0); Assert.AreEqual(state, actor.State);
        actor.Update(default, 0); Assert.AreEqual(ActorDiagnosticAction.Idle, actor.State.Action); Assert.AreEqual(0, actor.CurrentFrame);
        actor.Update(default, 1d / 6); Assert.AreEqual(1, actor.CurrentFrame);
        actor.Update(default, 14d / 6); Assert.AreEqual(0, actor.CurrentFrame);
        actor.Update(new(1, 0), .04);
        state = actor.State;
        Assert.ThrowsExactly<InvalidOperationException>(() => actor.Update(new(1, 0), .06, _ => throw new InvalidOperationException("test selection")));
        Assert.AreEqual(state, actor.State); Assert.AreEqual(0, actor.CurrentFrame);
        actor.Update(new(1, 0), .06); Assert.AreEqual(1, actor.CurrentFrame);
        actor.SafetyStop(); Assert.IsFalse(actor.IsPlaying); Assert.AreEqual(0, actor.CurrentFrame);
    }

    [TestMethod]
    public void InvalidAndOverflowStepsNeverClampIntoPlausibleState()
    {
        var motion = new DiagnosticActorMotion(); var initial = motion.Reset();
        foreach (var vector in new[] { new Vec2(double.NaN, 0), new(0, double.PositiveInfinity), new(3, 0), new(double.MaxValue, 1) })
        {
            var result = motion.Update(initial, vector, 1); Assert.AreEqual(DiagnosticMotionStatus.InvalidInput, result.Status); Assert.AreEqual(initial, result.State);
        }
        foreach (var delta in new[] { double.NaN, double.NegativeInfinity, -1 }) Assert.AreEqual(DiagnosticMotionStatus.InvalidInput, motion.Update(initial, new(1, 0), delta).Status);
        Assert.AreEqual(DiagnosticMotionStatus.Overflow, motion.Update(initial, new(2, 0), double.MaxValue).Status);
        foreach (var invalid in new[] { State(-1, 1), State(64, 1), State(double.NaN, 1), State(1, 1, 8), State(1, 1, 0, (ActorDiagnosticAction)9), State(1, 1, 0, ActorDiagnosticAction.Run), State(1, 1, 0, ActorDiagnosticAction.Vault) })
            Assert.AreEqual(DiagnosticMotionStatus.InvalidState, motion.Update(invalid, new(1, 0), 1).Status);
    }

    [TestMethod]
    public void FractionalPlacementUsesToEvenAndNeverCameraOrigin()
    {
        foreach (var (y, expected) in new[] { (.0125, 0), (.0375, 2) })
        {
            var input = DiagnosticActorMotion.ToPlacement(State(0, y), 0);
            Assert.AreEqual(expected, input.InstanceOffsetX); Assert.AreEqual(0L, input.CameraOriginX);
        }
        var below = DiagnosticActorMotion.ToPlacement(State(.999999, 32), 0);
        var above = DiagnosticActorMotion.ToPlacement(State(1.000001, 32), 0);
        Assert.AreEqual(40 * (below.MapY - below.MapX) + below.InstanceOffsetX, 40 * (above.MapY - above.MapX) + above.InstanceOffsetX);
        var step = new DiagnosticActorMotion().Update(State(30, 32), new(1, 0), .0001);
        Assert.IsTrue(step.State.Position.X != 30); // sub-pixel authoritative displacement survives
        foreach (var (x, expected) in new[] { (.0125, 0), (.0375, -2) })
            Assert.AreEqual(expected, DiagnosticActorMotion.ToPlacement(State(x, 0), 0).InstanceOffsetX);
    }

    [TestMethod]
    public void AsymmetricHitsRetainLastMovingSegmentAndExactStopResetsClock()
    {
        var motion = new DiagnosticActorMotion();
        var step = motion.Update(State(62, 60), new(0, 1), 1);
        Assert.AreEqual(new DiagnosticSectorPosition(63, 63), step.State.Position);
        Assert.AreEqual(3, step.State.Facing); // Y-only slide projects down/right, not desired down
        Assert.AreEqual(3, step.Segments.Count);
        Near(1d / 6, step.Segments[0].Seconds);
        Near(1d / 3, step.Segments[1].Seconds);
        Near(.5, step.Segments[2].Seconds);
        Assert.AreEqual(ActorDiagnosticAction.Idle, step.State.Action);
        var exact = motion.Update(State(62, 62), new(0, 1), 1d / 6);
        Assert.AreEqual(ActorDiagnosticAction.Idle, exact.State.Action);
        Assert.AreEqual(0d, exact.Segments[^1].Seconds);
        foreach (var corner in new[] { State(0,0), State(0,63), State(63,0), State(63,63) })
        {
            var outward = new Vec2(40 * ((corner.Position.Y == 0 ? -1 : 1) - (corner.Position.X == 0 ? -1 : 1)),
                20 * ((corner.Position.Y == 0 ? -1 : 1) + (corner.Position.X == 0 ? -1 : 1))).Normalized();
            var stopped = motion.Update(corner, outward, 1);
            Assert.AreEqual(corner, stopped.State); Assert.IsFalse(stopped.Moved);
        }
    }

    [TestMethod]
    public void SharedSettingsAndPublicRecordShapesKeepVersionOneContract()
    {
        Assert.AreEqual(20, typeof(RawInputFrame).GetMethod("Deconstruct")!.GetParameters().Length);
        Assert.AreEqual(14, typeof(IntentFrame).GetMethod("Deconstruct")!.GetParameters().Length);
        const string defaults = "{\"version\":1,\"cameraDeadzone\":0.2,\"actorDeadzone\":0.2,\"cameraSensitivity\":1,\"actorSensitivity\":1,\"invertCameraX\":true,\"invertCameraY\":true,\"invertActorX\":false,\"invertActorY\":false}";
        Assert.AreEqual(defaults, InputSettingsCodec.Serialize(InputSettings.Default));
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.AreEqual(InputSettings.Default, new InputSettings(CameraDeadzone:value).Validated());
            Assert.AreEqual(InputSettings.Default, new InputSettings(ActorDeadzone:value).Validated());
            Assert.AreEqual(InputSettings.Default, new InputSettings(CameraSensitivity:value).Validated());
            Assert.AreEqual(InputSettings.Default, new InputSettings(ActorSensitivity:value).Validated());
        }
        foreach (var x in new[] { false, true }) foreach (var y in new[] { false, true })
        {
            var settings = new InputSettings(InvertCameraX:x, InvertCameraY:y);
            Assert.AreEqual(settings, InputSettingsCodec.DeserializeOrDefault(InputSettingsCodec.Serialize(settings)));
        }
    }

    [TestMethod]
    public void WorldDefaultsAndHostFallbackDoNotChangeSharedSavedAxes()
    {
        var world = new InputSettings(InvertCameraX: false, InvertCameraY: false);
        Assert.AreEqual(world, DiagnosticWorldPolicy.DefaultInputSettings);
        Assert.IsTrue(InputSettings.Default.InvertCameraX); Assert.IsTrue(InputSettings.Default.InvertCameraY);
        foreach (var json in new[] { null, "", "invalid", "null", "[]", "{\"version\":2}", "{\"cameraDeadzone\":\"bad\"}" })
        {
            Assert.AreEqual(world, InputSettingsCodec.DeserializeOrDefault(json, world));
            Assert.AreEqual(InputSettings.Default, InputSettingsCodec.DeserializeOrDefault(json));
        }
        Assert.AreEqual(world, InputSettingsCodec.DeserializeOrDefault("{}", world));
        Assert.AreEqual(world with { InvertCameraX = true }, InputSettingsCodec.DeserializeOrDefault("{\"invertCameraX\":true}", world));
        foreach (var x in new[] { false, true }) foreach (var y in new[] { false, true })
        {
            var stored = new InputSettings(CameraDeadzone: .31, InvertCameraX: x, InvertCameraY: y);
            Assert.AreEqual(stored, InputSettingsCodec.DeserializeOrDefault(InputSettingsCodec.Serialize(stored), world));
        }
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.AreEqual(world, new InputSettings(CameraSensitivity: invalid).Validated(world));
        Assert.AreEqual(world, new InputSettings(Version: 2).Validated(world));
    }

    [TestMethod]
    public void AcceptedActivityNeutralGateAndSharedSettingsAreIndependentOfNetMotion()
    {
        var reducer = new InputStateReducer();
        var raw = new RawInputFrame(-1, default) { KeyboardActorHeld = true, KeyboardCameraHeld = true };
        var intent = reducer.Reduce(raw, new InputContext(Delta: 0));
        Assert.AreEqual(default(Vec2), intent.Movement); Assert.IsTrue(intent.ActorActive); Assert.IsTrue(intent.CameraActive);
        Assert.IsTrue(InputStateReducer.HasAnyLevel(raw, new()));
        Assert.IsFalse(reducer.Reduce(raw, new InputContext(Modal: true)).ActorActive);
        // ARC-DEV-200: a modal host whose mode still scrolls the view (the dialog) keeps the camera and drops the hero's input and the zoom.
        var scrolling = new InputStateReducer().Reduce(raw with { KeyboardCamera = new(1, 0), KeyboardActor = new(0, 1), RightTrigger = 1 },
            new InputContext(Delta: .1, Modal: true) { ScrollUnderModal = true, KeyboardCameraFixedSign = true, WorldZoomEnabled = true });
        Assert.IsTrue(scrolling.CameraActive); Assert.AreNotEqual(default(Vec2), scrolling.CameraPan);
        Assert.IsFalse(scrolling.ActorActive); Assert.AreEqual(default(Vec2), scrolling.Movement); Assert.AreEqual(0, scrolling.ZoomDrive);
        var pad = new RawInputFrame(0, default) { RightTrigger = 1, CompleteObservation = true, FrameObservationId = 1 };
        InputStateReducer Gamepad() { var r = new InputStateReducer(); r.SetStartupDevices([0]); return r; }
        Assert.AreNotEqual(0, Gamepad().Reduce(pad, new InputContext(Delta: .1, AdvanceTime: true) { WorldZoomEnabled = true }).ZoomDrive, "the trigger drives the zoom without the modal");
        Assert.AreEqual(0, Gamepad().Reduce(pad, new InputContext(Delta: .1, Modal: true, AdvanceTime: true) { ScrollUnderModal = true, WorldZoomEnabled = true }).ZoomDrive,
            "a modal frame that scrolls drops the zoom drive");
        var closed = new InputStateReducer().Reduce(raw with { KeyboardCamera = new(1, 0) }, new InputContext(Delta: .1, Modal: true) { KeyboardCameraFixedSign = true });
        Assert.IsFalse(closed.CameraActive); Assert.AreEqual(default(Vec2), closed.CameraPan);
        var follow = new CameraFollowEngine(); var bounds = new CameraBounds(new(0, 0), new(1000, 1000), new(200, 200), 1);
        var manual = follow.Update(new(), new(500, 500), new(440, 440), default, default, bounds, true, true);
        Assert.AreEqual(CameraMode.Manual, manual.State.Mode); Assert.AreEqual(new Vec2(440, 440), manual.Center);
        var held = follow.Update(manual.State, new(560, 560), manual.Center, default, default, bounds, true, false);
        Assert.AreEqual(new Vec2(440, 440), held.Center); Assert.IsFalse(held.State.RecenterArmed);
        var neutral = follow.Update(held.State, new(560, 560), held.Center, default, default, bounds, false, false);
        var resumed = follow.Update(neutral.State, new(560, 560), neutral.Center, default, default, bounds, true, false);
        Assert.AreEqual(new Vec2(560, 560), resumed.Center);
        var gate = new DiagnosticInputSafetyGate(); gate.ObserveCompleteFrame(true, false, true, true); Assert.IsFalse(gate.IsOpen);
        gate.ObserveCompleteFrame(true, false, true, false); gate.ObserveCompleteFrame(true, false, true, true); Assert.IsFalse(gate.IsOpen);
        gate.ObserveCompleteFrame(true, false, true, true); Assert.IsTrue(gate.IsOpen);
        gate.Close(); gate.ObserveCompleteFrame(true, false, false, true); Assert.IsFalse(gate.IsOpen);
        Assert.AreEqual(InputSettings.Default, InputSettingsCodec.DeserializeOrDefault("{\"version\":2}"));
        Assert.AreEqual(InputSettings.Default, InputSettingsCodec.DeserializeOrDefault("invalid"));
        var settings = new InputSettings(CameraDeadzone: 0, ActorDeadzone: 1, CameraSensitivity: 0, ActorSensitivity: 3, InvertCameraX: false);
        Assert.AreEqual(new InputSettings(CameraDeadzone: .05, ActorDeadzone: .45, CameraSensitivity: .5, ActorSensitivity: 2, InvertCameraX: false),
            InputSettingsCodec.DeserializeOrDefault(InputSettingsCodec.Serialize(settings)));
        Assert.AreEqual("Arcanum.Input", typeof(InputStateReducer).Assembly.GetName().Name);
    }
}
