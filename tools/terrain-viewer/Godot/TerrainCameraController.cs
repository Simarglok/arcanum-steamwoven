using Godot;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;

namespace Arcanum.TerrainViewer;

public sealed class TerrainCameraController
{
    private readonly Camera2D _camera;
    private CameraState _state;
    private TerrainScenePlan? _plan;
    private Arcanum.Data.Terrain.TerrainRect? _bounds;
    private readonly CameraFollowEngine _follow = new();
    private CameraFollowState _followState = new();
    private Vec2 _center;
    private bool _world;
    public Vec2 WantedCenter { get; private set; }
    public bool BoundLimited => WantedCenter != _center;
    public CameraFollowState FollowState => _followState;
    /// <summary>ARC-DEV-287 (MIG13 tail T6): the view stays where it is while the actor is not walking: a knocked-back hero is not followed (the original's push calls no view-centring function, Tail T6). Held by
    /// <see cref="HoldWorld"/>, let go by <see cref="ReleaseWorld"/> (the hero's next step), a manual pan or a reset.</summary>
    public bool Held { get; private set; }
    public void HoldWorld() => Held = true;
    public void ReleaseWorld() => Held = false;

    public TerrainCameraController(Camera2D camera)
    {
        _camera = camera;
        _state = new CameraState(0, 0, 1, false);
    }

    public void SetPlan(TerrainScenePlan plan, Vector2 viewport)
    {
        _plan = plan; WindowFloored = false;
        _bounds = plan.WorldBounds;
        Fit(viewport);
    }

    public void SetPlan(DiagnosticScenePlan plan, Vector2 viewport)
    {
        _world = plan.IsWorldDiagnostic; WindowFloored = false;
        _plan = plan.Terrain;
        _bounds = plan.WorldBounds;
        Fit(viewport);
    }

    /// <summary>An AREA plan; <paramref name="window"/>: a map loaded as a window, whose zoom has a floor (ARC-RENDER-032) unless
    /// <paramref name="overview"/> (ARC-RENDER-034, the terrain overview's bounds) widens the bounds: then the view may reach the
    /// overview and the floor is the ordinary 1/8.</summary>
    public void SetAreaPlan(AreaScenePlan plan, Vector2 viewport, bool window, AreaTerrainOverview? overview = null)
    {
        _world = true; _plan = null; _overview = overview; _bounds = overview?.Bounds ?? plan.WorldBounds; WindowFloored = window && overview is null;
        Fit(viewport);
    }
    private AreaTerrainOverview? _overview;
    /// <summary>Whether the shown plan is a window whose zoom has a floor (ARC-RENDER-032).</summary>
    public bool WindowFloored { get; private set; }
    /// <summary>The smallest zoom for the viewport: the window floor of a window plan, else the ordinary 1/8.</summary>
    public double ZoomFloor(Vector2 viewport) => viewport.X <= 0 || viewport.Y <= 0 ? CameraPolicy.MinimumZoom
        : WindowFloored && _bounds is { } b ? WorldZoomPolicy.WindowFloor(viewport.X, viewport.Y, b.Width, b.Height)
        : _overview is { } overview ? overview.Floor(viewport.X, viewport.Y) : CameraPolicy.MinimumZoom;
    /// <summary>ARC-UI-2936: the factor between the player's zoom setting (<see cref="State"/>) and the camera's zoom on screen. 1 normally; in the Original look it is the virtual screen's whole factor over the setting's default,
    /// so the default setting shows the original's 800 × 400 view and the zoom option keeps working from there.</summary>
    public double ZoomScale { get; set; } = 1;
    /// <summary>ARC-UI-2936: screen pixels the camera's focus (the actor) stands above the viewport's centre: in the Original look the play view is the 400-unit band between the strips, whose middle is above the screen's.</summary>
    public double ViewShift { get; set; }
    /// <summary>ARC-UI-2936: the screen height of the play view the world is seen through (the 400-unit band between the strips in the Original look); 0 = the whole viewport. The map edge clamp uses it, so the hero can reach the
    /// map's edge inside the play view instead of stopping 100 world pixels short under a strip.</summary>
    public double PlayHeight { get; set; }
    private double Floored(double zoom, Vector2 viewport) => Math.Max(zoom, ZoomFloor(viewport) / ZoomScale);

    public void Fit(Vector2 viewport)
    {
        if (_bounds is null || viewport.X <= 64 || viewport.Y <= 64) return;
        var bounds = _bounds.Value;
        _state = CameraPolicy.Fit(bounds.X, bounds.Y, bounds.Width, bounds.Height, viewport.X, viewport.Y);
        _state = _state with { Zoom = Floored(_state.Zoom / ZoomScale, viewport) };
        _center = new(_state.X, _state.Y);
        WantedCenter = _center;
        Held = false;
        if (_world) _followState = new(CameraMode.Manual, false, false);
        Apply();
    }

    public void Pan(Vector2 screenDelta) { _state = CameraPolicy.Pan(_state, screenDelta.X, screenDelta.Y); Apply(); }
    public void Zoom(double factor) { _state = CameraPolicy.Zoom(_state, factor); Apply(); }
    public void Resize(Vector2 viewport) { if (_state.FitMode) Fit(viewport); }
    public void Reset(Vector2 viewport) => Fit(viewport);
    public CameraState State => _state;
    public Camera2D Camera => _camera;
    private CameraBounds Bounds(Vector2 viewport)
    {
        var b = _bounds!.Value;
        return new(new(b.X, b.Y), new((double)b.X + b.Width, (double)b.Y + b.Height), new(viewport.X, PlayHeight > 0 ? PlayHeight : viewport.Y), _state.Zoom * ZoomScale);
    }
    public void ResetWorld(Vec2 actor, Vector2 viewport, double zoom = 2)
    {
        _state = _state with { Zoom = Floored(WorldZoomPolicy.Absolute(_state.Zoom, zoom), viewport), FitMode = false };
        if (_bounds is null || viewport.X <= 0 || viewport.Y <= 0) return;
        Held = false;
        var step = _follow.ResetCentered(actor, Bounds(viewport));
        WantedCenter = actor;
        _followState = step.State; _center = step.Center; ApplyWorld();
    }
    /// <summary>ARC-RENDER-3129: a world view kept across a window move: the follow state, the hold, the zoom and where the view stood relative to the actor.</summary>
    public readonly record struct KeptView(CameraFollowState Follow, bool Held, double Zoom, Vec2 FromActor);

    /// <summary>The view as it stands (null before a world plan is shown). The original's view does not move when sectors load, so a window move keeps it where it is, relative to the actor.</summary>
    public KeptView? KeepView(Vec2 actor) => _world && _bounds is not null ? new(_followState, Held, _state.Zoom, _center - actor) : null;

    /// <summary>Puts a kept view back on the new window's plan: the same follow state, hold and zoom, and the centre at the same offset from the actor (clamped to the new bounds).</summary>
    public void RestoreView(KeptView kept, Vec2 actor, Vector2 viewport)
    {
        if (_bounds is null || viewport.X <= 0 || viewport.Y <= 0) return;
        _state = _state with { Zoom = Floored(kept.Zoom, viewport), FitMode = false };
        var wanted = actor + kept.FromActor;
        _followState = kept.Follow; Held = kept.Held;
        _center = Bounds(viewport).ClampCenter(wanted); WantedCenter = wanted;
        ApplyWorld();
    }
    public void SafetyResetWorld() => _followState = _follow.SafetyReset(_followState);
    public void UpdateWorld(Vec2 actor, Vec2 actorIntent, Vec2 pan, bool actorActive, bool manualActive, Vector2 viewport)
    {
        if (_bounds is null || viewport.X <= 0 || viewport.Y <= 0) return;
        if (Held)
        {
            // A pan takes the view for itself; otherwise it stays (only the bounds of a changed zoom or viewport still apply).
            if (!manualActive)
            {
                WantedCenter = _center; _center = Bounds(viewport).ClampCenter(_center); ApplyWorld();
                return;
            }
            Held = false;
        }
        var step = _follow.Update(_followState, actor, _center, actorIntent, pan, Bounds(viewport), actorActive, manualActive);
        WantedCenter = step.State.Mode == CameraMode.Follow && step.State.TrackingActor ? actor : _center - pan;
        _followState = step.State; _center = step.Center; ApplyWorld();
    }
    public void ResizeWorld(Vec2 actor, Vector2 viewport)
    {
        if (_bounds is null || viewport.X <= 0 || viewport.Y <= 0) return;
        WantedCenter = !Held && _followState.Mode == CameraMode.Follow && _followState.TrackingActor ? actor : _center;
        // A larger viewport raises a window's floor.
        _state = _state with { Zoom = Floored(_state.Zoom, viewport) };
        _center = Bounds(viewport).ClampCenter(WantedCenter);
        ApplyWorld();
    }
    public void ZoomWorld(double factor, Vec2 actor, Vector2 viewport)
    {
        SetWorldZoom(WorldZoomPolicy.Step(_state.Zoom, factor), actor, viewport);
    }
    public void SetWorldZoom(double zoom, Vec2 actor, Vector2 viewport)
    {
        if (!double.IsFinite(zoom)) return;
        _state = _state with { Zoom = Floored(WorldZoomPolicy.Absolute(_state.Zoom, zoom), viewport), FitMode = false };
        ResizeWorld(actor, viewport);
    }
    private void ApplyWorld()
    {
        _state = _state with { X = _center.X, Y = _center.Y, FitMode = false };
        Apply();
    }
    private void Apply()
    {
        var zoom = _state.Zoom * ZoomScale;
        _camera.Position = new Vector2((float)_state.X, (float)_state.Y); _camera.Zoom = Vector2.One * (float)zoom;
        _camera.Offset = new Vector2(0, (float)(ViewShift / zoom));
    }
    /// <summary>Applies a changed <see cref="ZoomScale"/> to the camera at once.</summary>
    public void ReapplyZoom() => Apply();
}
