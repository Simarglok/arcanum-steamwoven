using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    private bool RunWorldSettingsTest(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 2 || arguments[0] != "--world-settings-test") return false;
        var store = new DiagnosticInputSettingsStore();
        if (!store.UsesIsolatedTestStorage) throw new ArgumentException("Usage");
        if (arguments[1].StartsWith("zoom-", StringComparison.Ordinal)) return RunWorldZoomPreferenceTest(arguments[1]);
        var nondefault = new InputSettings(CameraDeadzone: .31, ActorDeadzone: .27, CameraSensitivity: 1.3, ActorSensitivity: .8,
            InvertCameraX: false, InvertCameraY: true);
        var stage = arguments[1]; var passed = false;
        if (stage == "first-launch")
        {
            passed = store.Load() == new InputSettings(InvertCameraX: false, InvertCameraY: false);
            InitializeWorld(); passed &= _worldSettings == new InputSettings(InvertCameraX: false, InvertCameraY: false);
        }
        else if (stage.StartsWith("write-", StringComparison.Ordinal) || stage.StartsWith("read-", StringComparison.Ordinal))
        {
            var bits = stage[(stage.IndexOf('-') + 1)..];
            if (bits is not ("00" or "01" or "10" or "11")) throw new ArgumentException("Usage");
            var stored = nondefault with { InvertCameraX = bits[0] == '1', InvertCameraY = bits[1] == '1',
                InvertActorX = bits[0] == '0', InvertActorY = bits[1] == '0' };
            if (stage.StartsWith("write-", StringComparison.Ordinal)) store.Save(stored);
            InitializeWorld(); passed = store.Load() == stored && _worldSettings == stored;
        }
        else if (stage == "write") { store.Save(nondefault); passed = store.Load() == nondefault; }
        else if (stage == "read") passed = store.Load() == nondefault;
        else if (stage == "reset") { store.Save(DiagnosticWorldPolicy.DefaultInputSettings); passed = store.Load() == new InputSettings(InvertCameraX: false, InvertCameraY: false); }
        else if (stage == "defaults") passed = store.Load() == new InputSettings(InvertCameraX: false, InvertCameraY: false);
        if (!passed) { GD.Print("ARC-WORLD-001 settings-test FAIL"); GetTree().Quit(1); }
        else { GD.Print($"ARC-WORLD-001 settings-test PASS {stage}"); GetTree().Quit(0); }
        return true;
    }
    private bool RunWorldZoomPreferenceTest(string stage)
    {
        if (!_worldZoomStore.UsesIsolatedTestStorage) throw new ArgumentException("Usage");
        SelfTestBootstrap = true; SetProcess(false);
        var fixture = TerrainViewerSelfTest.WorldFixture();
        PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result);
        var passed = false;
        if (stage is "zoom-first" or "zoom-defaults" or "zoom-malformed") passed = _camera!.State.Zoom == 2 && _worldPreferredZoom == 2;
        else if (stage == "zoom-write")
        {
            SetPreferredWorldZoom(3); ResetWorldScene();
            PublishSyntheticForSelfTest(fixture.Arguments, fixture.Result);
            passed = _camera!.State.Zoom == 3 && _worldZoomStore.Load() == 3;
        }
        else if (stage == "zoom-read")
        {
            passed = _camera!.State.Zoom == 3; FitWorldView();
            passed &= _worldPreferredZoom == 3 && _worldZoomStore.Load() == 3;
            ResetWorldScene(); WorldSettleForSelfTest(); passed &= _camera.State.Zoom == 3;
        }
        else if (stage == "zoom-reset")
        {
            ResetWorldSettings(); WorldSettleForSelfTest(); passed = _camera!.State.Zoom == 2 && _worldZoomStore.Load() == 2
                && _worldSettings == new InputSettings(InvertCameraX:false,InvertCameraY:false);
        }
        else if (stage == "zoom-failure")
        {
            SetPreferredWorldZoom(3); passed = _camera!.State.Zoom == 3 && _worldZoomPending && _worldZoomSaveFailed;
            ResetWorldScene(); WorldSettleForSelfTest(); passed &= _camera.State.Zoom == 3 && _worldZoomSaveFailed;
        }
        else if (stage == "zoom-pending")
        {
            // Levels below are simulated; do not depend on which sequential
            // validation host currently owns OS focus.
            WorldFocusForSelfTest(true);
            var pad = new RawInputFrame(3,default); var none = new RawInputFrame(-1,default);
            _worldReducer.SetStartupDevices(new[]{3}); ResetWorldScene();
            for(var i=0;i<3;i++) WorldFrameForSelfTest(none,new[]{pad},.1);
            WorldFrameForSelfTest(none,new[]{pad with {RightTrigger=1}},1);
            passed = _camera!.State.Zoom == 4 && _worldZoomPending && _worldZoomStore.Load() == 2;
            QuarantineWorld();
            for(var i=0;i<3;i++) WorldFrameForSelfTest(none,new[]{pad},.1);
            passed &= !_worldZoomPending && _worldZoomStore.Load() == 4;
            PublishSyntheticForSelfTest(fixture.Arguments,fixture.Result); passed &= _camera.State.Zoom == 4;
        }
        else if (stage == "zoom-read-pending") passed = _camera!.State.Zoom == 4;
        GD.Print($"ARC-GPAD-004 settings-test {(passed ? "PASS" : "FAIL")} {stage} real-host-store/production-ready/reset/Fit/quarantine/reload");
        GetTree().Quit(passed ? 0 : 1); return true;
    }
    private async Task RunWorldNativeSmokeAsync(TerrainLoadResult result)
    {
        if (DisplayServer.GetName() == "headless") throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure, Stage: TerrainViewerFailureStage.Readback));
        SetProcess(false); // Only the explicit native-smoke child owns injected input.
        // Publication and OS notifications already observed live timestamps.
        // Freeze the test clock at a NEW live receipt, not the older published
        // state watermark, before any asynchronous native readbacks begin.
        _worldTestTime = WorldTime;
        WorldFocusForSelfTest(true);
        if (result.ModuleFingerprint != result.ModuleFingerprintAfter || result.ResourceFingerprint != result.ResourceFingerprintAfter
            || result.ActorResourceFingerprint is null || result.ActorResourceFingerprint != result.ActorResourceFingerprintAfter
            || result.ActorPrototypeFingerprint is null || result.ActorPrototypeFingerprint != result.ActorPrototypeFingerprintAfter
            || (result.ResolvedCount, result.TileCount, result.FacadeCount, result.NormalizationRecoveredCount, result.NormalizationLookupCount) != (4096, 3033, 1063, 457, 86)
            || result.StaticCensusCount != 317 || result.StaticOutsideScopeCount != 312 || result.StaticPlan?.Commands.Count != 5
            || result.ActorPlan?.Profile.Id != ActorDiagnosticProfileId.HumanFixed
            || result.ActorTextures.Count != 55 || result.Textures.Count == 0 || _readyDiagnosticPlan?.IsWorldDiagnostic != true)
            throw new TerrainViewerException(new(TerrainViewerErrorCode.ActorPlan));
        await AssertRepresentativeProductionCanvasAsync(result);
        await AssertSelectedStaticProductionCanvasAsync(result);
        await SetSmokeWindowSizeAsync(new Vector2I(1280, 800));
        await NextFrameAsync();
        _camera!.ZoomWorld(4, WorldAnchor, WorldViewport);
        WorldControlMatrixForSelfTest();
        await WorldZoomMatrixForSelfTestAsync();
        WorldLifecycleMatrixForSelfTest();
        await WorldModalRoutingMatrixForSelfTestAsync();
        WorldPaletteChordMatrixForSelfTest();
        // Outside the bounded WORLD003 zone x4..8 y0..3, inside the sector.
        OriginalPassabilityOverlayCheck("world-diagnostic", new(20, 20), blockerReason: "ADMITTED_DESTINATION_SCENERY_BLOCKER");
        try { await WorldMouseSettingsMatrixForSelfTestAsync(); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("WORLD mouse settings:", StringComparison.Ordinal))
        {
            GD.Print("ARC-WORLD-001 native GUI assertion FAIL " + exception.Message);
            throw;
        }
        GD.Print("ARC-WORLD-001 native-smoke stage=actor-rasters");
        // Every admitted physical facing/action selection, on the actual original
        // catalogue/cache and canvas, after motion rather than preview hotkeys.
        var directions = new[] { new Vec2(0,-1), new(1,-1), new(1,0), new(1,1), new(0,1), new(-1,1), new(-1,0), new(-1,-1) };
        SelectSimulatedPadForSelfTest();
        for (var i = 0; i < 3; i++) WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .01);
        foreach (var direction in directions)
        {
            WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(direction) }, .01);
            await AssertActorRasterAsync(result, _readyActor!);
            WorldFrameForSelfTest(new(-1, default), new[] { StickLevel(default) }, .01);
            await AssertActorRasterAsync(result, _readyActor!);
        }
        foreach (var size in new[] { new Vector2I(1280,800), new(1440,900), new(800,600) })
        {
            await SetSmokeWindowSizeAsync(size); await NextFrameAsync();
            GD.Print($"ARC-WORLD-001 native-smoke stage=viewport requested={size.X}x{size.Y} actual={GetViewport().GetVisibleRect().Size.X}x{GetViewport().GetVisibleRect().Size.Y}");
            if (GetViewport().GetVisibleRect().Size != new Vector2(size.X, size.Y)) throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
            _camera.ResetWorld(WorldAnchor, WorldViewport); _camera.Fit(WorldViewport); RefreshReadyStatus(); await NextFrameAsync();
            using var image = GetViewport().GetTexture().GetImage();
            GD.Print($"ARC-WORLD-001 native-smoke stage=readback image={image.GetWidth()}x{image.GetHeight()} hud={HasReadyHudContent(_cache.Count)} limit={_hud!.Text.Contains("Bounded adjacent grid", StringComparison.Ordinal)}");
            if (image.GetWidth() != size.X || image.GetHeight() != size.Y || !HasReadyHudContent(_cache.Count)
                || !_hud!.Text.Contains("Bounded adjacent grid", StringComparison.Ordinal)) throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
            // The actual combined window must contain decoded terrain AND a
            // decoded static landmark. Dimensions/HUD alone are not rendering.
            if (!await CaptureAndValidateSmokeFrameAsync(size, 4102, _cache.Count))
                throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
            // Resolve the original actor texels at high zoom through the SAME
            // combined canvas/camera, not just the isolated raster above.
            // The bounded spawn (6,1) is not near the full-sector Fit center.
            // Explicitly center this actor readback; zoom in Manual preserves
            // its center and must not silently regain Follow in production.
            _camera.ResetWorld(WorldAnchor, WorldViewport);
            _camera.ZoomWorld(64, WorldAnchor, WorldViewport);
            await NextFrameAsync();
            using var actorImage = GetViewport().GetTexture().GetImage();
            var actor = _readyActor!;
            var decoded = result.ActorTextures[actor.TextureKey];
            var matched = false;
            for (var y = 1; y < decoded.Height - 1 && !matched; y++)
            for (var x = 1; x < decoded.Width - 1 && !matched; x++)
            {
                var offset = (y * decoded.Width + x) * 4;
                if (decoded.Rgba[offset + 3] != 255) continue;
                var wx = actor.Destination.X + (actor.FlipX ? decoded.Width - 1 - x : x);
                var wy = actor.Destination.Y + y;
                if ((wx == actor.AnchorX && Math.Abs(wy - actor.AnchorY) <= 5)
                    || (wy == actor.AnchorY && Math.Abs(wx - actor.AnchorX) <= 5)) continue;
                matched = TryAssertLandmark(actorImage, new(wx, wy, decoded.Rgba[offset], decoded.Rgba[offset + 1],
                    decoded.Rgba[offset + 2], 255, actor.TextureKey.ResourceOrdinal));
            }
            if (!matched) throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
            GD.Print("ARC-WORLD-001 native-smoke combined-window terrain/static/actor pixels=PASS");
        }
        await RunWorldHdSmokeAsync(result);
        await RunHumanReadbackAsync(result);
        await RunGridOriginalReadbackAsync(result);
        await WorldOriginalReloadReadbackAsync(result);
        var count = _cache.Count; var created = _cache.CreatedCount;
        ClearPublishedScene();
        if (_cache.Count != 0 || _cache.DisposedCount != created) throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
        GD.Print($"ARC-WORLD-001 native-smoke PASS terrain=4096/3033/1063 normalization=457/86 static=317/5/312 actor=55 profile=HumanFixed fingerprints=immutable rendered=PASS controls=SIMULATION textures={count} disposed={created} viewport=1280x800,1440x900,800x600 parity=NOT_READY");
        GetTree().Quit(0);
    }
    internal void WorldControlMatrixForSelfTest()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD simulation: " + why); }
        var none = new RawInputFrame(-1, default);
        // The actor moves by the selected pad's left stick (the keyboard has no actor
        // route in WORLD); arrows remain the keyboard camera route.
        var left = none with { KeyboardActor = new(-1, 1), KeyboardActorHeld = true };
        var right = none with { KeyboardCamera = new(1, 0), KeyboardCameraHeld = true };
        var both = left with { KeyboardCamera = new(1, 0), KeyboardCameraHeld = true };
        void Frame(RawInputFrame raw, double delta = .1, bool complete = true)
        {
            var (keyboard, pad) = SplitActorFrame(raw);
            WorldFrameForSelfTest(keyboard, new[] { pad }, delta, complete);
        }
        WorldFocusForSelfTest(true); SelectSimulatedPadForSelfTest();
        WorldFocusForSelfTest(true); Frame(none); Frame(none); Frame(none);
        var initial = _worldActor.State; var bounds = _readyDiagnosticPlan!.WorldBounds;
        _camera!.ZoomWorld(4, WorldAnchor, WorldViewport);
        Frame(right, 0); Require(_camera.FollowState.Mode == CameraMode.Manual, "dt0 right Manual");
        Frame(right); Require(_worldActor.State.Position == initial.Position, "camera-only displaced actor");
        var manual = _camera.State; Frame(left); Frame(left); Frame(left);
        Require(_worldActor.State.Position != initial.Position && _camera.State == manual, "actor movement must keep Manual camera fixed");
        Frame(both); Require(_camera.FollowState.Mode == CameraMode.Manual && !_camera.FollowState.RecenterArmed, "simultaneous priority");
        var afterBoth = _camera.State; Frame(left); Require(_camera.State == afterBoth, "release right while left held snapped camera");
        Frame(none); Require(_camera.State == afterBoth && _camera.FollowState.RecenterArmed, "neutral must arm without snapping");
        Frame(left); Require(_camera.FollowState.Mode == CameraMode.Follow, "fresh actor must resume Follow");
        Frame(right); Frame(none); Frame(both); Require(!_camera.FollowState.RecenterArmed, "simultaneous must cancel stale return");
        WorldSettleForSelfTest(); // Complete accepted motion, not a SafetyStop freeze.
        ResetWorldScene(); WorldSettleForSelfTest();
        var beforeSafety = _worldActor.State.Position; var beforeCenter = _camera.State;
        foreach (var focus in new[] { false, true })
        {
            WorldFocusForSelfTest(focus); Frame(both); Frame(none, .1, false);
            Require(_worldActor.State.Position == beforeSafety && _camera.State == beforeCenter, "focus held or incomplete levels moved");
        }
        Frame(left); Frame(none); Frame(left); Require(_worldActor.State.Position == beforeSafety, "one neutral frame rearmed");
        Frame(none); Frame(none); Require(_worldGate.IsOpen, "complete two neutral frames failed to rearm gate");
        Frame(left); Frame(left); Frame(left); Require(_worldActor.State.Position != beforeSafety, "rearmed known Open step failed");
        WorldSettleForSelfTest(); ToggleWorldSettings(); var modalPosition = _worldActor.State.Position; Frame(both);
        Require(_worldActor.State.Position == modalPosition && !_worldActor.IsPlaying, "settled modal cannot start motion");
        ToggleWorldSettings(); Frame(both); Require(_worldActor.State.Position == modalPosition, "modal-close held restarted");
        Frame(none); Frame(none); Frame(none);
        var actorBeforeCamera = _worldActor.State;
        foreach (var x in new[] { false, true }) foreach (var y in new[] { false, true })
        {
            _worldSettings = _worldSettings with { InvertCameraX = x, InvertCameraY = y };
            _camera.ResetWorld(WorldAnchor, WorldViewport);
            _camera.ZoomWorld(8, WorldAnchor, WorldViewport);
            var old = _camera.State;
            WorldFrameForSelfTest(none, new[] { new RawInputFrame(SimulatedPadId, default, new Vec2(1, 1)) }, .1);
            var now = _camera.State;
            Require((x ? now.X < old.X : now.X > old.X) && (y ? now.Y < old.Y : now.Y > old.Y), "independent stick image inversion axes");
            // KM-05: keyboard arrows keep one direction whatever the camera inversion.
            old = _camera.State;
            Frame(none with { KeyboardCamera = new(1, 1), KeyboardCameraHeld = true });
            now = _camera.State;
            Require(now.X > old.X && now.Y > old.Y, "arrows followed camera inversion");
        }
        Require(_worldActor.State.Position == actorBeforeCamera.Position && _readyDiagnosticPlan.WorldBounds == bounds && _cache.Count == _readyDecodedCount + _readyStaticDecodedCount + _readyActorPlan!.Profile.DistinctKeys, "camera/selection changed actor, bounds or cache");
        _worldSettings = DiagnosticWorldPolicy.DefaultInputSettings; ResetWorldScene(); Frame(none); Frame(none); Frame(none);
        _worldReducer.SetStartupDevices(Array.Empty<int>()); QuarantineWorld(); _worldAdapter.Clear();
        GD.Print("ARC-WORLD-001 control matrix PASS SIMULATION Manual/Follow/activity/neutral/focus/modal/inversion/bounds/cache; physical usability NOT_READY");
    }
}
