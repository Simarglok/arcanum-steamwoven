using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-032 native observation (map battery): at the smoke's window size and at 2560 × 1440 (a large display), a map
// loaded as a window (the main world map) never zooms out past its floor: the view zoomed to the ordinary smallest zoom (1/8)
// before a resize is raised to the larger window's floor by the resize itself, a zoom asked below the floor stops at it, and
// at the floor the view lies inside the window's drawn bounds with the terrain still in reduced chunks. A whole map reaches
// 1/8. The window size and the zoom shown before are restored. A Steamwoven viewer rule: the original has no zoom.
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
        async Task Resize(Vector2I size)
        {
            if (gameViewport is SubViewport sub) sub.Size = size; else DisplayServer.WindowSetSize(size);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        try
        {
            foreach (var size in new[] { original, new Vector2I(2560, 1440) })
            {
                // Zoomed out as far as the current size allows, then resized: the resize raises a window to its new floor.
                camera.SetWorldZoom(CameraPolicy.MinimumZoom, WorldAnchor, WorldViewport);
                await Resize(size);
                var viewport = WorldViewport;
                Require(viewport == new Vector2(size.X, size.Y), $"VIEWPORT {viewport} asked {size}");
                var floor = camera.ZoomFloor(viewport);
                var expected = window ? WorldZoomPolicy.WindowFloor(viewport.X, viewport.Y, bounds.Width, bounds.Height) : CameraPolicy.MinimumZoom;
                Require(floor == expected, $"FLOOR {floor} expected {expected}");
                if (window && size.X >= 2560) Require(floor > CameraPolicy.MinimumZoom, $"NOT_RAISED {floor}");
                Require(camera.State.Zoom == floor, $"RESIZE_NOT_FLOORED {camera.State.Zoom} floor {floor}");
                // Asked below the floor: it stops there. The first far frame after the resize is timed with its upload.
                var clock = System.Diagnostics.Stopwatch.StartNew();
                camera.SetWorldZoom(CameraPolicy.MinimumZoom / 2, WorldAnchor, viewport);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var frameMs = clock.Elapsed.TotalMilliseconds;
                var zoom = camera.State.Zoom;
                Require(zoom == floor, $"ZOOM {zoom} floor {floor}");
                double width = viewport.X / zoom, height = viewport.Y / zoom;
                double left = camera.State.X - width / 2, top = camera.State.Y - height / 2;
                // A window's view lies inside its drawn bounds (one pixel of rounding).
                var inside = left >= bounds.X - 1 && top >= bounds.Y - 1 && left + width <= bounds.X + bounds.Width + 1 && top + height <= bounds.Y + bounds.Height + 1;
                Require(!window || inside, $"OUTSIDE view {left:0},{top:0} {width:0}x{height:0} bounds {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
                Require(_canvas!.AreaVisibleCount > 0, $"NOTHING_DRAWN size {size} zoom {zoom}");
                // At a window's floor (a power of two) the terrain stays in its reduced chunks, not one item per tile.
                Require(!window || !_canvas.AreaTerrainChunksEnabled || _canvas.AreaChunkCount > 0, $"NOT_CHUNKED level {_canvas.AreaChunkLevel}");
                observed.Add(new { Viewport = new { viewport.X, viewport.Y }, Floor = Math.Round(floor, 4), Zoom = Math.Round(zoom, 4),
                    View = new { Width = Math.Round(width), Height = Math.Round(height) }, Inside = inside, Visible = _canvas.AreaVisibleCount,
                    Chunks = _canvas.AreaChunkCount, ChunkLevel = _canvas.AreaChunkLevel, MappingRows = _canvas.AreaMappingRows,
                    TextureBytes = (long)Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed),
                    Objects = (long)Performance.GetMonitor(Performance.Monitor.ObjectCount), FirstFrameMs = Math.Round(frameMs, 1) });
            }
            GD.Print("ARC-RENDER-032 native-smoke zoom floor PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Window = window, Bounds = new { bounds.Width, bounds.Height }, Observed = observed,
                Parity = "NOT_APPLICABLE (the original has no zoom)" }));
        }
        finally
        {
            await Resize(original);
            camera.SetWorldZoom(before, WorldAnchor, WorldViewport);
        }
        // The later stages (pixel samplers, HUD) run at the smoke's own size.
        Require(WorldViewport == new Vector2(original.X, original.Y), $"NOT_RESTORED {WorldViewport} original {original}");
    }
}
