using Godot;
using Arcanum.TerrainViewer.Core;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp : Node
{
    private readonly TerrainLoadCoordinator _coordinator = new();
    private readonly TerrainTextureCache _cache = new();
    private TerrainCanvas? _canvas;
    private TerrainCameraController? _camera;
    private Label? _status;
    private long _generation;
    private bool _dragging;

    public override void _Ready()
    {
        var canvas = new TerrainCanvas { Name = "TerrainCanvas" };
        AddChild(canvas);
        _canvas = canvas;
        var cameraNode = new Camera2D { Name = "TerrainCamera", PositionSmoothingEnabled = false };
        AddChild(cameraNode);
        _camera = new TerrainCameraController(cameraNode);
        GetViewport().SizeChanged += () => _camera?.Resize(GetViewport().GetVisibleRect().Size);
        var layer = new CanvasLayer { Name = "Hud" };
        var status = new Label { Position = new Vector2(16, 16), Text = "Editor diagnostic order | loading" };
        layer.AddChild(status);
        AddChild(layer);
        _status = status;

        var args = OS.GetCmdlineUserArgs();
        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            TerrainViewerSelfTest.Run(this);
            return;
        }
        try { _ = StartLoadAsync(TerrainViewerArguments.Parse(args)); }
        catch (ArgumentException) { ShowStatus("Usage"); }
    }

    private async Task StartLoadAsync(TerrainViewerArguments arguments)
    {
        _generation = _coordinator.BeginGeneration();
        var requestGeneration = _generation;
        _canvas!.ClearScene();
        _cache.Clear();
        ShowStatus($"Editor diagnostic order | loading | source-id {arguments.SourceId}");
        try
        {
            var result = await _coordinator.LoadAsync(requestGeneration, arguments);
            if (result.Generation != _generation) return;
            _cache.Upload(result.Textures);
            _canvas.Publish(result.Plan, _cache);
            _camera!.SetPlan(result.Plan, GetViewport().GetVisibleRect().Size);
            ShowStatus($"Editor diagnostic order | 4096/3033/1063 | source-id {arguments.SourceId} | decoded {result.Textures.Count} | {result.ElapsedMilliseconds}ms");
        }
        catch (TerrainViewerException exception) { if (requestGeneration == _generation) ShowStatus(exception.Error.ToString()); }
        catch { ShowStatus(TerrainViewerErrorCode.RenderFailure.ToString()); }
    }

    public override void _Process(double delta)
    {
        var step = (float)(CameraPolicy.PanScreenPixelsPerSecond * delta);
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) _camera?.Pan(new Vector2(0, -step));
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) _camera?.Pan(new Vector2(0, step));
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) _camera?.Pan(new Vector2(-step, 0));
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) _camera?.Pan(new Vector2(step, 0));
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed) _camera?.Zoom(1.25);
            else if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed) _camera?.Zoom(0.8);
            else if (button.ButtonIndex == MouseButton.Left) _dragging = button.Pressed;
        }
        else if (@event is InputEventMouseMotion motion && _dragging) _camera?.Pan(-motion.Relative);
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode is Key.R or Key.F) _camera?.Reset(GetViewport().GetVisibleRect().Size);
            else if (key.Keycode == Key.Minus) _camera?.Zoom(0.8);
            else if (key.Keycode == Key.Plus) _camera?.Zoom(1.25);
            else if (key.Keycode == Key.F11) GetWindow().Mode = Window.ModeEnum.Fullscreen;
            else if (key.Keycode == Key.Escape && GetWindow().Mode == Window.ModeEnum.Fullscreen) GetWindow().Mode = Window.ModeEnum.Windowed;
        }
    }


    private void ShowStatus(string text) { if (_status is not null) _status.Text = text; }
    public override void _ExitTree() { _cache.Dispose(); _coordinator.Dispose(); }
}
