using Godot;
using Arcanum.TerrainViewer.Core;
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

    public TerrainCameraController(Camera2D camera)
    {
        _camera = camera;
        _state = new CameraState(0, 0, 1, false);
    }

    public void SetPlan(TerrainScenePlan plan, Vector2 viewport)
    {
        _plan = plan;
        _bounds = plan.WorldBounds;
        Fit(viewport);
    }

    public void SetPlan(DiagnosticScenePlan plan, Vector2 viewport)
    {
        _world = plan.IsWorldDiagnostic;
        _plan = plan.Terrain;
        _bounds = plan.WorldBounds;
        Fit(viewport);
    }

    public void SetAreaPlan(AreaScenePlan plan, Vector2 viewport)
    {
        _world = true; _plan = null; _bounds = plan.WorldBounds; Fit(viewport);
    }

    public void Fit(Vector2 viewport)
    {
        if (_bounds is null || viewport.X <= 64 || viewport.Y <= 64) return;
        var bounds = _bounds.Value;
        _state = CameraPolicy.Fit(bounds.X, bounds.Y, bounds.Width, bounds.Height, viewport.X, viewport.Y);
        _center = new(_state.X, _state.Y);
        WantedCenter = _center;
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
        return new(new(b.X, b.Y), new((double)b.X + b.Width, (double)b.Y + b.Height), new(viewport.X, viewport.Y), _state.Zoom);
    }
    public void ResetWorld(Vec2 actor, Vector2 viewport, double zoom = 2)
    {
        _state = _state with { Zoom = WorldZoomPolicy.Absolute(_state.Zoom, zoom), FitMode = false };
        if (_bounds is null || viewport.X <= 0 || viewport.Y <= 0) return;
        var step = _follow.ResetCentered(actor, Bounds(viewport));
        WantedCenter = actor;
        _followState = step.State; _center = step.Center; ApplyWorld();
    }
    public void SafetyResetWorld() => _followState = _follow.SafetyReset(_followState);
    public void UpdateWorld(Vec2 actor, Vec2 actorIntent, Vec2 pan, bool actorActive, bool manualActive, Vector2 viewport)
    {
        if (_bounds is null || viewport.X <= 0 || viewport.Y <= 0) return;
        var step = _follow.Update(_followState, actor, _center, actorIntent, pan, Bounds(viewport), actorActive, manualActive);
        WantedCenter = step.State.Mode == CameraMode.Follow && step.State.TrackingActor ? actor : _center - pan;
        _followState = step.State; _center = step.Center; ApplyWorld();
    }
    public void ResizeWorld(Vec2 actor, Vector2 viewport)
    {
        if (_bounds is null || viewport.X <= 0 || viewport.Y <= 0) return;
        WantedCenter = _followState.Mode == CameraMode.Follow && _followState.TrackingActor ? actor : _center;
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
        _state = _state with { Zoom = WorldZoomPolicy.Absolute(_state.Zoom, zoom), FitMode = false };
        ResizeWorld(actor, viewport);
    }
    private void ApplyWorld()
    {
        _state = _state with { X = _center.X, Y = _center.Y, FitMode = false };
        Apply();
    }
    private void Apply() { _camera.Position = new Vector2((float)_state.X, (float)_state.Y); _camera.Zoom = Vector2.One * (float)_state.Zoom; }
}
