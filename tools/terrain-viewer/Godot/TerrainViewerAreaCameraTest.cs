using Arcanum.Input;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    /// <summary>S4: camera/Fit/pan/zoom/persistence on the published AREA app.
    /// Expectations are literal policy facts (envelope center, minimum zoom,
    /// edge reachability), not values read back from the camera's own math.</summary>
    internal async Task<object> AreaCameraMatrixForSelfTestAsync(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        if (!SelfTestBootstrap || _readyArea is null || _readyMode != TerrainViewerMode.CrashSiteArea)
            throw new InvalidOperationException("AREA_CAMERA_TEST_READY_REQUIRED");
        var simulatedFocus = _worldFocusSimulationForSelfTest;
        _worldFocusSimulationForSelfTest = true;
        try { return await AreaCameraMatrixScriptAsync(arguments, result); }
        finally { _worldFocusSimulationForSelfTest = simulatedFocus; }
    }
    private async Task<object> AreaCameraMatrixScriptAsync(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        static void Require(bool value, string why) { if (!value) throw new InvalidOperationException("AREA camera: " + why); }
        var none = new RawInputFrame(-1, default);
        void Frame(RawInputFrame raw) => WorldFrameForSelfTest(raw, Array.Empty<RawInputFrame>(), .1);
        void Rearm() { QuarantineWorld(); Frame(none); Frame(none); Require(_worldGate.IsOpen, "two complete neutral frames"); }
        if (WorldModal) ToggleWorldSettings();
        GetViewport().GuiReleaseFocus(); _worldAdapter.Clear(); RetireWorldLifecycle();
        _worldSettings = DiagnosticWorldPolicy.DefaultInputSettings;
        _worldActor.Configure(result.TraversalSnapshot!, SimulationTime.At(0));
        _worldGridInput = new(); _worldTestTime = SimulationTime.At(0); _worldFocused = true;
        _worldReducer.SetStartupDevices(Array.Empty<int>());
        Rearm(); PublishWorldSelection();
        var bounds = _readyArea!.Plan.WorldBounds;
        var viewport = WorldViewport;
        var cell = _worldActor.Session!.State.CommittedCell;
        var actor = _readyActor!.Destination;
        var checks = new List<string>();

        // R uses the stored preference and enters Follow on the controlled actor.
        SetPreferredWorldZoom(3); FlushWorldZoom();
        ResetWorldScene(); WorldSettleForSelfTest();
        Require(_camera!.State.Zoom == 3 && _camera.FollowState.Mode == CameraMode.Follow && _worldZoomStore.Load() == 3,
            $"R preferred zoom/Follow zoom={_camera.State.Zoom} follow={_camera.FollowState}");
        checks.Add("reset-preferred-follow");

        // Fit inspects the whole admitted envelope, not one sector or the
        // visible sprites. This fixture envelope cannot fit at the minimum zoom.
        Require(bounds.Width * .125 > viewport.X - 64 || bounds.Height * .125 > viewport.Y - 64, "fixture envelope must exceed minimum-zoom Fit");
        FitWorldView(); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Require(_camera.State.Zoom == .125 && _camera.State.X == Math.Round(bounds.X + bounds.Width / 2d)
            && _camera.State.Y == Math.Round(bounds.Y + bounds.Height / 2d) && _camera.FollowState.Mode == CameraMode.Manual,
            $"Fit envelope center/minimum zoom state={_camera.State} bounds={bounds}");
        Require(GetSelfTestSnapshot().Hud.Contains("Fit limited by minimum zoom", StringComparison.Ordinal), "HUD Fit limitation");
        Require(_worldPreferredZoom == 3 && _worldZoomStore.Load() == 3, "Fit is session-only, not a preference");
        checks.Add("fit-envelope-minimum-zoom-session-only");

        // Keyboard camera pans through the production reducer reach every
        // envelope edge; the actor and its grid cell never move.
        Rearm();
        (double Left, double Right, double Top, double Bottom) Edges()
        {
            var z = _camera!.State.Zoom; var c = new Vector2((float)_camera.State.X, (float)_camera.State.Y);
            return ((bounds.X - c.X) * z + viewport.X / 2, (bounds.X + bounds.Width - c.X) * z + viewport.X / 2,
                (bounds.Y - c.Y) * z + viewport.Y / 2, (bounds.Y + bounds.Height - c.Y) * z + viewport.Y / 2);
        }
        var reached = new List<string>();
        foreach (var (name, direction) in new[] { ("+x", new Vec2(1,0)), ("-x", new Vec2(-1,0)), ("+y", new Vec2(0,1)), ("-y", new Vec2(0,-1)) })
        {
            var pan = none with { KeyboardCamera = direction, KeyboardCameraHeld = true };
            CameraState? previous = null;
            for (var i = 0; i < 400 && _camera.State != previous; i++) { previous = _camera.State; Frame(pan); }
            Require(_camera.State == previous, name + " pan did not settle at a bound");
            Frame(none);
            var e = Edges();
            if (Math.Abs(e.Left) <= 1) reached.Add("left");
            if (Math.Abs(e.Right - viewport.X) <= 1) reached.Add("right");
            if (Math.Abs(e.Top) <= 1) reached.Add("top");
            if (Math.Abs(e.Bottom - viewport.Y) <= 1) reached.Add("bottom");
            Require(_worldActor.Session!.State.CommittedCell == cell && _readyActor!.Destination == actor,
                name + " camera pan moved the actor or its cell");
        }
        Require(new[] { "left", "right", "top", "bottom" }.All(reached.Contains), "whole envelope reachable: " + string.Join(",", reached));
        checks.Add("pan-reaches-all-envelope-edges-actor-fixed");

        // Wheel zoom through the production callback, then the settle window
        // persists it (a discrete step at once); a fresh initialization reloads it for the next publish.
        ResetWorldScene(); WorldSettleForSelfTest(); Rearm();
        _Input(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = viewport - Vector2.One });
        var wheel = _camera.State.Zoom;
        // A discrete wheel step is saved at once; continuous drive waits for settle.
        Require(wheel > 3, $"wheel zoom={wheel}");
        Frame(none); Frame(none); Frame(none);
        Require(!_worldZoomPending && _worldZoomStore.Load() == wheel, "settled wheel zoom persisted");
        ExitWorld(); InitializeWorld();
        Require(_worldPreferredZoom == wheel, "reinitialization reloads the persisted zoom");
        PublishSyntheticForSelfTest(arguments, result);
        Require(_readyArea is not null && _camera.State.Zoom == wheel && _camera.FollowState.Mode == CameraMode.Follow,
            $"republished AREA uses persisted zoom state={_camera.State}");
        checks.Add("wheel-zoom-persisted-reinitialized-republished");
        SetPreferredWorldZoom(2); FlushWorldZoom();
        GD.Print("ARC-RENDER-007 AREA camera synthetic PASS " + string.Join("/", checks) + $" bounds={bounds}");
        return new { Schema = "arcanum-area-camera-synthetic-v1", Task = "ARC-RENDER-007", Outcome = "PASS",
            Bounds = new[] { bounds.X, bounds.Y, bounds.Width, bounds.Height }, Checks = checks, Physical = "NOT_VERIFIED" };
    }
}
