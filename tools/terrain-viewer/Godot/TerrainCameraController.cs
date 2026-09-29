using Godot;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public sealed class TerrainCameraController
{
    private readonly Camera2D _camera;
    private CameraState _state;
    private TerrainScenePlan? _plan;
    private Arcanum.Data.Terrain.TerrainRect? _bounds;

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
        _plan = plan.Terrain;
        _bounds = plan.WorldBounds;
        Fit(viewport);
    }

    public void Fit(Vector2 viewport)
    {
        if (_bounds is null) return;
        var bounds = _bounds.Value;
        _state = CameraPolicy.Fit(bounds.X, bounds.Y, bounds.Width, bounds.Height, viewport.X, viewport.Y);
        Apply();
    }

    public void Pan(Vector2 screenDelta) { _state = CameraPolicy.Pan(_state, screenDelta.X, screenDelta.Y); Apply(); }
    public void Zoom(double factor) { _state = CameraPolicy.Zoom(_state, factor); Apply(); }
    public void Resize(Vector2 viewport) { if (_state.FitMode) Fit(viewport); }
    public void Reset(Vector2 viewport) => Fit(viewport);
    public CameraState State => _state;
    public Camera2D Camera => _camera;
    private void Apply() { _camera.Position = new Vector2((float)_state.X, (float)_state.Y); _camera.Zoom = Vector2.One * (float)_state.Zoom; }
}
