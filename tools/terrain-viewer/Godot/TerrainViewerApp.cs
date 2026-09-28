using Godot;
using Arcanum.Data.Terrain;
using Arcanum.TerrainViewer.Core;
using System.Globalization;

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
    private TerrainViewerArguments? _arguments;
    private Task? _lastLoad;
    private TerrainLoadResult? _readyResult;
    private string? _readySourceId;

    public override void _Ready()
    {
        var canvas = new TerrainCanvas { Name = "TerrainCanvas" };
        AddChild(canvas);
        _canvas = canvas;
        var cameraNode = new Camera2D { Name = "TerrainCamera", PositionSmoothingEnabled = false };
        AddChild(cameraNode);
        _camera = new TerrainCameraController(cameraNode);
        GetViewport().SizeChanged += HandleViewportSizeChanged;
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
            _arguments = TerrainViewerArguments.Parse(args);
            _lastLoad = StartLoadAsync(_arguments);
        }
        catch (ArgumentException) { ShowStatus("Usage"); }
    }

    private async Task StartLoadAsync(TerrainViewerArguments arguments, bool orchestrateSmoke = true)
    {
        GD.Print($"ARC-RENDER-003 load stage=begin smoke={orchestrateSmoke}");
        _generation = _coordinator.BeginGeneration();
        var requestGeneration = _generation;
        _readyResult = null;
        _readySourceId = null;
        _canvas!.ClearScene();
        _cache.Clear();
        ShowStatus($"Editor diagnostic order | loading | source-id {arguments.SourceId}");
        try
        {
            var result = await _coordinator.LoadAsync(requestGeneration, arguments);
            GD.Print($"ARC-RENDER-003 load stage=decoded generation={requestGeneration}");
            if (!_coordinator.TryPublish(requestGeneration, () =>
                {
                    _cache.Upload(result.Textures);
                    _canvas.Publish(result.Plan, _cache);
                    _camera!.SetPlan(result.Plan, GetViewport().GetVisibleRect().Size);
                    _readyResult = result;
                    _readySourceId = arguments.SourceId;
                    RefreshReadyStatus();
                })) return;
            if (_nativeSmoke && orchestrateSmoke) await RunNativeSmokeAsync(result);
            GD.Print($"ARC-RENDER-003 load stage=complete generation={requestGeneration}");
        }
        catch (TerrainViewerException exception)
        {
            if (!_coordinator.TryPublish(requestGeneration, () =>
                {
                    _readyResult = null;
                    _readySourceId = null;
                    _canvas!.ClearScene();
                    _cache.Clear();
                    ShowStatus(exception.Error.ToString());
                })) return;
            if (_nativeSmoke) GetTree().Quit(1);
        }
        catch
        {
            if (!_coordinator.TryPublish(requestGeneration, () =>
                {
                    _readyResult = null;
                    _readySourceId = null;
                    _canvas!.ClearScene();
                    _cache.Clear();
                    ShowStatus(TerrainViewerErrorCode.RenderFailure.ToString());
                })) return;
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
        var geometrySignature = _canvas.GeometrySignature;
        await AssertRepresentativeProductionCanvasAsync(result);
        await SetSmokeWindowSizeAsync(new Vector2I(1280, 800));
        await NextFrameAsync();
        var initial = _camera!.State;
        var initialCameraPosition = _camera.Camera.Position;
        var initialHudPosition = _status!.GlobalPosition;
        GD.Print("ARC-RENDER-003 native-smoke stage=initial-readback");
        if (!await CaptureAndValidateSmokeFrameAsync(new Vector2I(1280, 800), commandCount, textureCount))
        {
            GetTree().Quit(0);
            return;
        }
        GD.Print("ARC-RENDER-003 native-smoke stage=input");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = true });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-003 native-smoke stage=input-keyboard-frame");
        var afterKeyboard = _camera.State;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = false });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-003 native-smoke stage=input-keyboard-release-frame");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-003 native-smoke stage=input-wheel-frame");
        if (_camera.State.FitMode || _camera.State.Zoom <= initial.Zoom || afterKeyboard.Y >= initial.Y
            || _camera.Camera.Position == initialCameraPosition || _camera.Camera.Zoom.X <= initial.Zoom
            || _status.GlobalPosition != initialHudPosition || !HasReadyHudContent(textureCount)) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        var beforeDrag = _camera.State;
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(20, -10) });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        await NextFrameAsync();
        GD.Print("ARC-RENDER-003 native-smoke stage=input-drag-frame");
        var manual = _camera.State;
        if (_camera.State.FitMode || manual.X == beforeDrag.X || manual.Y == beforeDrag.Y) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        await SetSmokeWindowSizeAsync(new Vector2I(1440, 900));
        GD.Print("ARC-RENDER-003 native-smoke stage=input-manual-resize");
        if (_camera.State != manual) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = true });
        await NextFrameAsync();
        if (!_camera.State.FitMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F, Pressed = true });
        await SetSmokeWindowSizeAsync(new Vector2I(1920, 1080));
        await NextFrameAsync();
        if (!_camera.State.FitMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        // A display clamp truthfully makes the requested-size evidence
        // NOT_VERIFIED, but must not short-circuit the independent lifecycle,
        // input, reload, and cleanup checks below.
        var exactSecondSize = await CaptureAndValidateSmokeFrameAsync(new Vector2I(1920, 1080), commandCount, textureCount);
        var fullscreen = false;
        GD.Print("ARC-RENDER-003 native-smoke stage=fullscreen");
        var initialMode = GetWindow().Mode;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.F11, Pressed = true });
        await NextFrameAsync();
        fullscreen = GetWindow().Mode != initialMode;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        await NextFrameAsync();
        var fullscreenRestored = !fullscreen || GetWindow().Mode == Window.ModeEnum.Windowed;
        if (!fullscreenRestored) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        for (var cycle = 0; cycle < 100; cycle++)
        {
            if (cycle % 10 == 0) GD.Print($"ARC-RENDER-003 native-smoke stage=cycle-{cycle}");
            var beforePan = _camera.State;
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = true });
            await NextFrameAsync();
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.W, Pressed = false });
            if (beforePan == _camera.State) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            var afterPan = _camera.State;
            var beforeZoom = _camera.Camera.Zoom;
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
            await NextFrameAsync();
            if (_camera.State.FitMode || _camera.Camera.Zoom.X <= beforeZoom.X || _camera.Camera.Position == new Vector2((float)beforePan.X, (float)beforePan.Y))
                throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            Input.ParseInputEvent(new InputEventKey { Keycode = Key.R, Pressed = true });
            await NextFrameAsync();
            if (!_camera.State.FitMode) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            var requestedSize = cycle % 2 == 0 ? new Vector2I(1280, 800) : new Vector2I(1440, 900);
            await SetSmokeWindowSizeAsync(requestedSize);
            var actualViewport = GetViewport().GetVisibleRect().Size;
            var expectedFit = CameraPolicy.Fit(result.Plan.WorldBounds.X, result.Plan.WorldBounds.Y, result.Plan.WorldBounds.Width,
                result.Plan.WorldBounds.Height, actualViewport.X, actualViewport.Y);
            if (!_camera.State.FitMode || _camera.Camera.Position != new Vector2((float)_camera.State.X, (float)_camera.State.Y)
                || _camera.Camera.Zoom != Vector2.One * (float)_camera.State.Zoom || _status!.GlobalPosition != initialHudPosition
                || _camera.State.Zoom != expectedFit.Zoom || _canvas.CommandCount != commandCount || _cache.Count != textureCount || !HasReadyHudContent(textureCount)
                || _canvas.GeometrySignature != geometrySignature || afterPan == _camera.State)
                throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        }
        var disposedBeforeReload = _cache.DisposedCount;
        GD.Print("ARC-RENDER-003 native-smoke stage=reload-input");
        var previousLoad = _lastLoad;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.L, Pressed = true });
        // ParseInputEvent queues _Input. Yield before reading _lastLoad so we
        // await the load started by the actual L route, rather than deadlocking
        // on this still-running native-smoke task from the initial load.
        await NextFrameAsync();
        if (ReferenceEquals(previousLoad, _lastLoad)) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        GD.Print("ARC-RENDER-003 native-smoke stage=reload-await");
        await (_lastLoad ?? Task.CompletedTask);
        await NextFrameAsync();
        GD.Print("ARC-RENDER-003 native-smoke stage=reload-complete");
        if (_cache.DisposedCount != disposedBeforeReload + textureCount || commandCount != CrashSiteProfile.CellCount || _canvas.CommandCount != commandCount
            || _cache.Count != textureCount || _canvas.GeometrySignature != geometrySignature || result.MaximumObservedConcurrency != 1
            || result.CheckedRgbaBytes <= 0 || result.ModuleFingerprint != result.ModuleFingerprintAfter || result.ResourceFingerprint != result.ResourceFingerprintAfter)
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        GD.Print($"ARC-RENDER-003 native-smoke result status={(exactSecondSize ? "PASS" : "NOT_VERIFIED")} fullscreen={(fullscreen ? "PASS" : "NOT_VERIFIED")} commands={commandCount} textures={textureCount} bytes={result.CheckedRgbaBytes} loadMs={result.ElapsedMilliseconds} concurrency={result.MaximumObservedConcurrency} fingerprints=equal reload=PASS cycles=100");
        GetTree().Quit(0);
    }

    private async Task AssertRepresentativeProductionCanvasAsync(TerrainLoadResult result)
    {
        var expected = new[]
        {
            (392, 4587, 0, new TerrainRect(-79, 280, 78, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(-79, 280, 78, 40), false),
            (573, 4587, 0, new TerrainRect(-2119, 1380, 78, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(-2119, 1380, 78, 40), true),
            (821, 4577, 0, new TerrainRect(-1639, 1300, 78, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(-1639, 1300, 78, 40), true),
            (18, 4490, 0, new TerrainRect(-719, 360, 78, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(-719, 360, 78, 40), false),
            (2075, 90, 0, new TerrainRect(201, 1180, 80, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(201, 1180, 78, 40), false),
            (2011, 90, 1, new TerrainRect(161, 1160, 80, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(161, 1160, 78, 40), false),
            (1947, 90, 2, new TerrainRect(121, 1140, 80, 40), new TerrainRect(0, 0, 78, 40), new TerrainRect(121, 1140, 78, 40), false),
        };
        foreach (var literal in expected)
        {
            var command = result.Plan.Commands.SingleOrDefault(candidate => candidate.Ordinal == literal.Item1);
            if (command is null || command.TextureKey.DatOrdinal != literal.Item2 || command.TextureKey.FrameIndex != literal.Item3
                || command.Footprint != literal.Item4 || command.Source != literal.Item5 || command.Destination != literal.Item6 || command.FlipX != literal.Item7
                || !result.Textures.TryGetValue(command.TextureKey, out var decoded) || !TryFindOpaqueDecodedPixel(decoded, command.Source, out var sourcePixel))
                throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            var viewport = new SubViewport { Size = new Vector2I(command.Destination.Width + 2, command.Destination.Height + 2), TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            var isolated = new TerrainCanvas { Position = new Vector2(1 - command.Destination.X, 1 - command.Destination.Y) };
            viewport.AddChild(isolated);
            AddChild(viewport);
            isolated.Publish(result.Plan.Isolate(command.Ordinal), _cache);
            await NextFrameAsync();
            using var image = viewport.GetTexture().GetImage();
            var offsetX = sourcePixel.X - command.Source.X;
            if (command.FlipX) offsetX = command.Source.Width - 1 - offsetX;
            var actual = image.GetPixel(1 + offsetX, 1 + sourcePixel.Y - command.Source.Y);
            viewport.QueueFree();
            if (Math.Round(actual.R * 255) != sourcePixel.R || Math.Round(actual.G * 255) != sourcePixel.G
                || Math.Round(actual.B * 255) != sourcePixel.B || Math.Round(actual.A * 255) != sourcePixel.A
                || Math.Round(image.GetPixel(0, 0).A * 255) != 0)
                throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            GD.Print($"ARC-RENDER-003 representative ordinal={literal.Item1} status=PASS");
        }
    }

    private static bool TryFindOpaqueDecodedPixel(DecodedTerrainTexture texture, TerrainRect source, out TerrainTexturePixel pixel)
    {
        pixel = default;
        for (var y = source.Y; y < source.Y + source.Height; y++)
        {
            for (var x = source.X; x < source.X + source.Width; x++)
            {
                var offset = ((y * texture.Width) + x) * 4;
                if (texture.Rgba[offset + 3] == 0) continue;
                pixel = new TerrainTexturePixel(x, y, texture.Rgba[offset], texture.Rgba[offset + 1], texture.Rgba[offset + 2], texture.Rgba[offset + 3]);
                return true;
            }
        }
        return false;
    }

    private async Task NextFrameAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task SetSmokeWindowSizeAsync(Vector2I expected)
    {
        DisplayServer.WindowSetSize(expected);
        await NextFrameAsync();
    }

    private async Task<bool> CaptureAndValidateSmokeFrameAsync(Vector2I expectedSize, int commandCount, int textureCount)
    {
        await NextFrameAsync();
        using var screenshot = GetViewport().GetTexture().GetImage();
        var actualSize = new Vector2I(screenshot.GetWidth(), screenshot.GetHeight());
        if (_nativeSmokeScreenshot is not null)
        {
            var requested = Path.GetFullPath(_nativeSmokeScreenshot);
            var directory = Path.GetDirectoryName(requested);
            if (string.IsNullOrWhiteSpace(directory)) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
            Directory.CreateDirectory(directory);
            var stem = Path.GetFileNameWithoutExtension(requested);
            var extension = Path.GetExtension(requested);
            var output = Path.Combine(directory, $"{stem}-requested-{expectedSize.X}x{expectedSize.Y}-actual-{actualSize.X}x{actualSize.Y}{extension}");
            if (screenshot.SavePng(output) != Error.Ok) throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        }
        if (actualSize != expectedSize)
        {
            GD.Print($"ARC-RENDER-003 native-smoke result status=NOT_VERIFIED requested={expectedSize.X}x{expectedSize.Y} actual={actualSize.X}x{actualSize.Y} reason=display-clamp");
            return false;
        }
        var stableCounts = _canvas!.CommandCount == commandCount && _cache.Count == textureCount;
        var hudReadable = !string.IsNullOrWhiteSpace(_status?.Text) && _status.GlobalPosition == new Vector2(16, 16) && HasReadyHudContent(textureCount);
        var landmarkFound = _canvas.TryGetOpaqueLandmark(out var landmark);
        var landmarkMatches = landmarkFound && TryAssertLandmark(screenshot, landmark);
        GD.Print($"ARC-RENDER-003 native-smoke readback stable={stableCounts} hud={hudReadable} terrain={landmarkMatches}");
        if (!stableCounts || !hudReadable || !landmarkMatches)
            throw new TerrainViewerException(new TerrainViewerError(TerrainViewerErrorCode.RenderFailure));
        return true;
    }

    private bool TryAssertLandmark(Image screenshot, TerrainCanvasLandmark landmark)
    {
        // Read TerrainCanvas's actual canvas transform rather than duplicating
        // Camera2D zoom/centering arithmetic in the smoke oracle.
        var point = _canvas!.GetGlobalTransformWithCanvas() * new Vector2(landmark.WorldX, landmark.WorldY);
        var x = (int)Math.Round(point.X);
        var y = (int)Math.Round(point.Y);
        for (var sampleY = y - 2; sampleY <= y + 2; sampleY++)
        {
            for (var sampleX = x - 2; sampleX <= x + 2; sampleX++)
            {
                if (sampleX < 0 || sampleY < 0 || sampleX >= screenshot.GetWidth() || sampleY >= screenshot.GetHeight()) continue;
                var actual = screenshot.GetPixel(sampleX, sampleY);
                if (Math.Round(actual.R * 255) == landmark.R && Math.Round(actual.G * 255) == landmark.G
                    && Math.Round(actual.B * 255) == landmark.B && Math.Round(actual.A * 255) == landmark.A) return true;
            }
        }
        return false;
    }

    public override void _Process(double delta)
    {
        var step = (float)(CameraPolicy.PanScreenPixelsPerSecond * delta);
        var before = _camera?.State;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) _camera?.Pan(new Vector2(0, -step));
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) _camera?.Pan(new Vector2(0, step));
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) _camera?.Pan(new Vector2(-step, 0));
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) _camera?.Pan(new Vector2(step, 0));
        if (before != _camera?.State) RefreshReadyStatus();
    }

    public override void _Input(InputEvent @event)
    {
        var before = _camera?.State;
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
            else if (key.Keycode == Key.L && _arguments is not null)
            {
                GD.Print("ARC-RENDER-003 input=L reload");
                _lastLoad = StartLoadAsync(_arguments, false);
            }
            else if (key.Keycode == Key.F11) GetWindow().Mode = GetWindow().Mode == Window.ModeEnum.Fullscreen ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
            else if (key.Keycode == Key.Escape && GetWindow().Mode == Window.ModeEnum.Fullscreen) GetWindow().Mode = Window.ModeEnum.Windowed;
        }
        if (before != _camera?.State) RefreshReadyStatus();
    }

    private void HandleViewportSizeChanged()
    {
        _camera?.Resize(GetViewport().GetVisibleRect().Size);
        RefreshReadyStatus();
    }

    private void RefreshReadyStatus()
    {
        if (_readyResult is null || _readySourceId is null || _camera is null) return;
        var result = _readyResult;
        ShowStatus($"Editor diagnostic order | 4096/3033/1063 | source-id {_readySourceId} | zoom {FormatZoom(_camera.State.Zoom)} | decoded {result.Textures.Count} | cache {_cache.Count} | {result.ElapsedMilliseconds}ms");
    }

    private bool HasReadyHudContent(int textureCount)
        => _status?.Text.Contains($"zoom {FormatZoom(_camera!.State.Zoom)}", StringComparison.Ordinal) == true
            && _status.Text.Contains($"cache {textureCount}", StringComparison.Ordinal);

    private static string FormatZoom(double zoom) => zoom.ToString("0.###", CultureInfo.InvariantCulture);
    private void ShowStatus(string text) { if (_status is not null) _status.Text = text; }
    public override void _ExitTree() { _cache.Dispose(); _coordinator.Dispose(); }
}
