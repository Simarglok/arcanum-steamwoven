using Arcanum.Input;
using Arcanum.TerrainViewer.Core;
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
        var nondefault = new InputSettings(CameraDeadzone: .31, ActorDeadzone: .27, CameraSensitivity: 1.3, ActorSensitivity: .8,
            InvertCameraX: false, InvertCameraY: true);
        var stage = arguments[1]; var passed = false;
        if (stage == "write") { store.Save(nondefault); passed = store.Load() == nondefault; }
        else if (stage == "read") passed = store.Load() == nondefault;
        else if (stage == "reset") { store.Save(InputSettings.Default); passed = store.Load() == InputSettings.Default; }
        else if (stage == "defaults") passed = store.Load() == InputSettings.Default;
        if (!passed) { GD.Print("ARC-WORLD-001 settings-test FAIL"); GetTree().Quit(1); }
        else { GD.Print($"ARC-WORLD-001 settings-test PASS {stage}"); GetTree().Quit(0); }
        return true;
    }
    private async Task RunWorldNativeSmokeAsync(TerrainLoadResult result)
    {
        if (DisplayServer.GetName() == "headless") throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure, Stage: TerrainViewerFailureStage.Readback));
        SetProcess(false); // Only the explicit native-smoke child owns injected input.
        WorldFocusForSelfTest(true);
        if (result.ModuleFingerprint != result.ModuleFingerprintAfter || result.ResourceFingerprint != result.ResourceFingerprintAfter
            || result.ActorResourceFingerprint is null || result.ActorResourceFingerprint != result.ActorResourceFingerprintAfter
            || result.ActorPrototypeFingerprint is null || result.ActorPrototypeFingerprint != result.ActorPrototypeFingerprintAfter
            || (result.ResolvedCount, result.TileCount, result.FacadeCount, result.NormalizationRecoveredCount, result.NormalizationLookupCount) != (4096, 3033, 1063, 457, 86)
            || result.StaticCensusCount != 317 || result.StaticOutsideScopeCount != 312 || result.StaticPlan?.Commands.Count != 5
            || result.ActorTextures.Count != 184 || result.Textures.Count == 0 || _readyDiagnosticPlan?.IsWorldDiagnostic != true)
            throw new TerrainViewerException(new(TerrainViewerErrorCode.ActorPlan));
        await AssertRepresentativeProductionCanvasAsync(result);
        await AssertSelectedStaticProductionCanvasAsync(result);
        await SetSmokeWindowSizeAsync(new Vector2I(1280, 800));
        await NextFrameAsync();
        _camera!.ZoomWorld(4, WorldAnchor, WorldViewport);
        WorldControlMatrixForSelfTest();
        WorldLifecycleMatrixForSelfTest();
        GD.Print("ARC-WORLD-001 native-smoke stage=actor-rasters");
        // Every admitted physical facing/action selection, on the actual original
        // catalogue/cache and canvas, after motion rather than preview hotkeys.
        var directions = new[] { new Vec2(0,-1), new(1,-1), new(1,0), new(1,1), new(0,1), new(-1,1), new(-1,0), new(-1,-1) };
        foreach (var direction in directions)
        {
            WorldFrameForSelfTest(new(-1, default, KeyboardActor: direction), Array.Empty<RawInputFrame>(), .01);
            await AssertActorRasterAsync(result, _readyActor!);
            WorldFrameForSelfTest(new(-1, default), Array.Empty<RawInputFrame>(), .01);
            await AssertActorRasterAsync(result, _readyActor!);
        }
        foreach (var size in new[] { new Vector2I(1280,800), new(1440,900), new(800,600) })
        {
            await SetSmokeWindowSizeAsync(size); await NextFrameAsync();
            GD.Print($"ARC-WORLD-001 native-smoke stage=viewport requested={size.X}x{size.Y} actual={GetViewport().GetVisibleRect().Size.X}x{GetViewport().GetVisibleRect().Size.Y}");
            if (GetViewport().GetVisibleRect().Size != new Vector2(size.X, size.Y)) throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
            _camera.ResetWorld(WorldAnchor, WorldViewport); RefreshReadyStatus(); await NextFrameAsync();
            using var image = GetViewport().GetTexture().GetImage();
            GD.Print($"ARC-WORLD-001 native-smoke stage=readback image={image.GetWidth()}x{image.GetHeight()} hud={HasReadyHudContent(_cache.Count)} limit={_hud!.Text.Contains("collision not evaluated", StringComparison.Ordinal)}");
            if (image.GetWidth() != size.X || image.GetHeight() != size.Y || !HasReadyHudContent(_cache.Count)
                || !_hud!.Text.Contains("collision not evaluated", StringComparison.Ordinal)) throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
            // The actual combined window must contain decoded terrain AND a
            // decoded static landmark. Dimensions/HUD alone are not rendering.
            if (!await CaptureAndValidateSmokeFrameAsync(size, 4102, _cache.Count))
                throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
            // Resolve the original actor texels at high zoom through the SAME
            // combined canvas/camera, not just the isolated raster above.
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
                    decoded.Rgba[offset + 2], 255, 643));
            }
            if (!matched) throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
            GD.Print("ARC-WORLD-001 native-smoke combined-window terrain/static/actor pixels=PASS");
        }
        var count = _cache.Count; var created = _cache.CreatedCount;
        ClearPublishedScene();
        if (_cache.Count != 0 || _cache.DisposedCount != created) throw new TerrainViewerException(new(TerrainViewerErrorCode.RenderFailure));
        GD.Print($"ARC-WORLD-001 native-smoke PASS terrain=4096/3033/1063 normalization=457/86 static=317/5/312 actor=184 fingerprints=immutable rendered=PASS controls=SIMULATION textures={count} disposed={created} viewport=1280x800,1440x900,800x600 parity=NOT_READY");
        GetTree().Quit(0);
    }
    internal void WorldControlMatrixForSelfTest()
    {
        if (!SelfTestBootstrap && !_nativeSmoke) throw new InvalidOperationException("Explicit test launch required.");
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("WORLD simulation: " + why); }
        var none = new RawInputFrame(-1, default);
        var left = none with { KeyboardActor = new(1, 0), KeyboardActorHeld = true };
        var right = none with { KeyboardCamera = new(1, 0), KeyboardCameraHeld = true };
        var both = left with { KeyboardCamera = new(1, 0), KeyboardCameraHeld = true };
        void Frame(RawInputFrame raw, double delta = .1, bool complete = true) => WorldFrameForSelfTest(raw, Array.Empty<RawInputFrame>(), delta, complete);
        WorldFocusForSelfTest(true); Frame(none); Frame(none); Frame(none);
        var initial = _worldActor.State; var bounds = _readyDiagnosticPlan!.WorldBounds;
        _camera!.ZoomWorld(4, WorldAnchor, WorldViewport);
        Frame(right, 0); Require(_camera.FollowState.Mode == CameraMode.Manual, "dt0 right Manual");
        Frame(right); Require(_worldActor.State.Position == initial.Position, "camera-only displaced actor");
        var manual = _camera.State; Frame(left);
        Require(_worldActor.State.Position != initial.Position && _camera.State == manual, "actor movement must keep Manual camera fixed");
        Frame(both); Require(_camera.FollowState.Mode == CameraMode.Manual && !_camera.FollowState.RecenterArmed, "simultaneous priority");
        var afterBoth = _camera.State; Frame(left); Require(_camera.State == afterBoth, "release right while left held snapped camera");
        Frame(none); Require(_camera.State == afterBoth && _camera.FollowState.RecenterArmed, "neutral must arm without snapping");
        Frame(left); Require(_camera.FollowState.Mode == CameraMode.Follow, "fresh actor must resume Follow");
        Frame(right); Frame(none); Frame(both); Require(!_camera.FollowState.RecenterArmed, "simultaneous must cancel stale return");
        var beforeSafety = _worldActor.State.Position; var beforeCenter = _camera.State;
        foreach (var focus in new[] { false, true })
        {
            WorldFocusForSelfTest(focus); Frame(both); Frame(none, .1, false);
            Require(_worldActor.State.Position == beforeSafety && _camera.State == beforeCenter, "focus held or incomplete levels moved");
        }
        Frame(left); Frame(none); Frame(left); Require(_worldActor.State.Position == beforeSafety, "one neutral frame rearmed");
        Frame(none); Frame(none); Frame(left); Require(_worldActor.State.Position != beforeSafety, "complete two neutral frames failed to rearm");
        ToggleWorldSettings(); var modalPosition = _worldActor.State.Position; Frame(both);
        Require(_worldActor.State.Position == modalPosition && !_worldActor.IsPlaying, "modal must safety-stop");
        ToggleWorldSettings(); Frame(both); Require(_worldActor.State.Position == modalPosition, "modal-close held restarted");
        Frame(none); Frame(none); Frame(none);
        var actorBeforeCamera = _worldActor.State;
        foreach (var x in new[] { false, true }) foreach (var y in new[] { false, true })
        {
            _worldSettings = _worldSettings with { InvertCameraX = x, InvertCameraY = y };
            _camera.ResetWorld(WorldAnchor, WorldViewport);
            _camera.ZoomWorld(8, WorldAnchor, WorldViewport);
            var old = _camera.State;
            Frame(none with { KeyboardCamera = new(1, 1), KeyboardCameraHeld = true });
            var now = _camera.State;
            Require((x ? now.X < old.X : now.X > old.X) && (y ? now.Y < old.Y : now.Y > old.Y), "independent image inversion axes");
        }
        Require(_worldActor.State.Position == actorBeforeCamera.Position && _readyDiagnosticPlan.WorldBounds == bounds && _cache.Count == _readyDecodedCount + _readyStaticDecodedCount + 184, "camera/selection changed actor, bounds or cache");
        _worldSettings = InputSettings.Default; ResetWorldScene(); Frame(none); Frame(none); Frame(none);
        GD.Print("ARC-WORLD-001 control matrix PASS SIMULATION Manual/Follow/activity/neutral/focus/modal/inversion/bounds/cache; physical usability NOT_READY");
    }
}
