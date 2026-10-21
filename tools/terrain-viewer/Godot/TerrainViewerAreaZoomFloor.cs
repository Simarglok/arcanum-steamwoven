using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-032 native observation (map battery): asked for the ordinary smallest zoom (1/8), a map loaded as a window
// (the main world map) stops at its floor, where the view holds no more than the window's drawn bounds and lies inside them;
// a whole map still reaches 1/8. The zoom shown before is restored. A Steamwoven viewer rule: the original has no zoom.
public partial class TerrainViewerApp
{
    private async Task RunAreaMapZoomFloorSmokeAsync()
    {
        static void Require(bool value, string code) { if (!value) { GD.Print("ARC-RENDER-032 smoke detail " + code); throw new InvalidOperationException("AREA_ZOOM_FLOOR_" + code.Split(' ')[0]); } }
        var camera = _camera!; var before = camera.State.Zoom; var window = _mapWindowCentre is not null;
        var gameViewport = GetViewport(); var original = gameViewport is SubViewport held ? held.Size : DisplayServer.WindowGetSize();
        Require(camera.WindowFloored == window, $"FLAG window={window}");
        var bounds = _readyArea!.Plan.WorldBounds;
        var observed = new List<object>();
        try
        {
            // The smoke's own window, then a large one (as on a large display): a window's floor rises with it.
            foreach (var size in new[] { original, new Vector2I(2560, 1440) })
            {
                if (gameViewport is SubViewport sub) sub.Size = size; else DisplayServer.WindowSetSize(size);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                var viewport = WorldViewport;
                Require(viewport == new Vector2(size.X, size.Y), $"VIEWPORT {viewport} asked {size}");
                var floor = camera.ZoomFloor(viewport);
                var expected = window ? WorldZoomPolicy.WindowFloor(viewport.X, viewport.Y, bounds.Width, bounds.Height) : CameraPolicy.MinimumZoom;
                Require(floor == expected, $"FLOOR {floor} expected {expected}");
                if (window && size.X >= 2560) Require(floor > CameraPolicy.MinimumZoom, $"NOT_RAISED {floor}");
                camera.SetWorldZoom(CameraPolicy.MinimumZoom, WorldAnchor, viewport);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var zoom = camera.State.Zoom;
                Require(zoom == floor, $"ZOOM {zoom} floor {floor}");
                double width = viewport.X / zoom, height = viewport.Y / zoom;
                double left = camera.State.X - width / 2, top = camera.State.Y - height / 2;
                // A window's view lies inside its drawn bounds (one pixel of rounding).
                var inside = left >= bounds.X - 1 && top >= bounds.Y - 1 && left + width <= bounds.X + bounds.Width + 1 && top + height <= bounds.Y + bounds.Height + 1;
                Require(!window || inside, $"OUTSIDE view {left:0},{top:0} {width:0}x{height:0} bounds {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
                Require(_canvas!.AreaVisibleCount > 0, $"NOTHING_DRAWN size {size} zoom {zoom} centre {camera.State.X},{camera.State.Y} camera {camera.Camera.GlobalPosition} {camera.Camera.Zoom} bounds {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height} canvas {GetViewport().GetCanvasTransform()}");
                observed.Add(new { Viewport = new { viewport.X, viewport.Y }, Floor = Math.Round(floor, 4), Zoom = Math.Round(zoom, 4),
                    View = new { Width = Math.Round(width), Height = Math.Round(height) }, Inside = inside, Visible = _canvas.AreaVisibleCount });
            }
            GD.Print("ARC-RENDER-032 native-smoke zoom floor PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Window = window, Bounds = new { bounds.Width, bounds.Height }, Observed = observed,
                Parity = "NOT_APPLICABLE (the original has no zoom)" }));
        }
        finally
        {
            if (gameViewport is SubViewport restore) restore.Size = original; else DisplayServer.WindowSetSize(original);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            camera.SetWorldZoom(before, WorldAnchor, WorldViewport);
        }
    }
}
