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
    private bool _nativeSmoke;
    private string? _nativeSmokeScreenshot;

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

        var args = OS.GetCmdlineUserArgs().ToList();
        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            TerrainViewerSelfTest.Run(this);
            return;
        }
        try
        {
            (_nativeSmoke, _nativeSmokeScreenshot) = TakeNativeSmokeArguments(args);
            _ = StartLoadAsync(TerrainViewerArguments.Parse(args));
        }
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
            if (_nativeSmoke) await RunNativeSmokeAsync(result);
        }
        catch (TerrainViewerException exception)
        {
            if (requestGeneration != _generation) return;
            ShowStatus(exception.Error.ToString());
            if (_nativeSmoke) GetTree().Quit(1);
        }
        catch
        {
            ShowStatus(TerrainViewerErrorCode.RenderFailure.ToString());
            if (_nativeSmoke) GetTree().Quit(1);
        }
    }

    private static (bool Enabled, string? ScreenshotPath) TakeNativeSmokeArguments(List<string> args)
    {
        var enabled = args.Remove("--native-smoke");
        var screenshotIndex = args.IndexOf("--native-smoke-screenshot");
        if (screenshotIndex < 0) return (enabled, null);
        if (!enabled || screenshotIndex + 1 >= args.Count) throw new ArgumentException("Usage");
        var screenshotPath = args[screenshotIndex + 1];
        args.RemoveAt(screenshotIndex + 1);
        args.RemoveAt(screenshotIndex);
        return (enabled, screenshotPath);
    }

    private async Task RunNativeSmokeAsync(TerrainLoadResult result)
    {
        var commandCount = _canvas!.CommandCount;
        var textureCount = _cache.Count;
        DisplayServer.WindowSetSize(new Vector2I(1280, 800));
        await NextFrameAsync();
        var initial = _camera!.State;
        await CaptureAndValidateSmokeFrameAsync("1280x800", commandCount, textureCount);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = false });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        await NextFrameAsync();
        if (_camera.State.FitMode || _camera.State.Zoom <= initial.Zoom) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(20, -10) });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        await NextFrameAsync();
        if (_camera.State.FitMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = true });
        await NextFrameAsync();
        if (!_camera.State.FitMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, Pressed = true });
        DisplayServer.WindowSetSize(new Vector2I(1920, 1080));
        await NextFrameAsync();
        if (!_camera.State.FitMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        await CaptureAndValidateSmokeFrameAsync("1920x1080", commandCount, textureCount);
        var initialMode = GetWindow().Mode;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F11, Pressed = true });
        await NextFrameAsync();
        if (GetWindow().Mode == initialMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await NextFrameAsync();
        if (GetWindow().Mode != Window.ModeEnum.Windowed) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        for (var cycle = 0; cycle < 100; cycle++)
        {
            _camera.Zoom(cycle % 2 == 0 ? 1.25 : 0.8);
            _camera.Reset(GetViewport().GetVisibleRect().Size);
        }
        if (commandCount != CrashSiteProfile.CellCount || _canvas.CommandCount != commandCount || _cache.Count != textureCount || result.MaximumObservedConcurrency != 1)
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        GD.Print("ARC-RENDER-003 native smoke PASS 1280x800 1920x1080 input/readback/fullscreen commands=4096 concurrency=1");
        GetTree().Quit(0);
    }

    private async Task NextFrameAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task CaptureAndValidateSmokeFrameAsync(string size, int commandCount, int textureCount)
    {
        await NextFrameAsync();
        using var screenshot = GetViewport().GetTexture().GetImage();
        var rgba = screenshot.GetData().ToArray();
        if (screenshot.GetWidth() <= 0 || screenshot.GetHeight() <= 0 || !HasOpaquePixel(rgba)
            || _canvas!.CommandCount != commandCount || _cache.Count != textureCount || string.IsNullOrWhiteSpace(_status?.Text))
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        if (_nativeSmokeScreenshot is null) return;
        var requested = Path.GetFullPath(_nativeSmokeScreenshot);
        var directory = Path.GetDirectoryName(requested);
        if (string.IsNullOrWhiteSpace(directory)) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(requested);
        var extension = Path.GetExtension(requested);
        var output = Path.Combine(directory, $"{stem}-{size}{extension}");
        if (screenshot.SavePng(output) != Error.Ok) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
    }

    private static bool HasOpaquePixel(byte[] rgba)
    {
        for (var offset = 3; offset < rgba.Length; offset += 4) if (rgba[offset] != 0) return true;
        return false;
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
            else if (key.Keycode == Key.F11) GetWindow().Mode = GetWindow().Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
            else if (key.Keycode == Key.Escape && GetWindow().Mode == Window.ModeEnum.Fullscreen) GetWindow().Mode = Window.ModeEnum.Windowed;
        }
    }


    private void ShowStatus(string text) { if (_status is not null) _status.Text = text; }
    public override void _ExitTree() { _cache.Dispose(); _coordinator.Dispose(); }
}
