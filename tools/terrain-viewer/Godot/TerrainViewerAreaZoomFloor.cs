using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-032 native observation (map battery): at the smoke's window size and at 2560 × 1440 (a large display), a map
// loaded as a window (the main world map) never zooms out past its floor: the view zoomed to the ordinary smallest zoom (1/8)
// before a resize is raised to the larger window's floor by the resize itself, a zoom asked below the floor stops at it, and
// at the floor the view lies inside the window's drawn bounds with the terrain still in reduced chunks. A whole map reaches
// 1/8. The window size and the zoom shown before are restored. A Steamwoven viewer rule: the original has no zoom.
// ARC-RENDER-034: with the window's terrain overview the floor is the ordinary 1/8 too, and at that farthest zoom the view lies
// inside the overview's bounds and every corner and edge midpoint of it falls on the window's core or an overview sector (no
// empty ground), past the window on a large display; the overview's draw and the first far frame are timed.
public partial class TerrainViewerApp
{
    private async Task RunAreaMapZoomFloorSmokeAsync()
    {
        static void Require(bool value, string code) { if (!value) { GD.Print("ARC-RENDER-032 smoke detail " + code); throw new InvalidOperationException("AREA_ZOOM_FLOOR_" + code.Split(' ')[0]); } }
        var camera = _camera!; var before = camera.State.Zoom; var window = _mapWindowCentre is not null;
        var gameViewport = GetViewport(); var original = gameViewport is SubViewport held ? held.Size : DisplayServer.WindowGetSize();
        var overview = window ? _mapOverview : null;
        // ARC-RENDER-034: a window always has its overview (the window plan already needs the grid and the types).
        Require(!window || overview is not null, $"NO_OVERVIEW {_mapOverviewFailure}");
        Require(camera.WindowFloored == (window && overview is null), $"FLAG window={window} overview={overview is not null}");
        var windowBounds = _readyArea!.Plan.WorldBounds;
        var bounds = overview?.Bounds ?? windowBounds;
        var floored = window && overview is null;
        var covered = new List<object>(); var sampledPixels = 0;
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
                var expected = floored ? WorldZoomPolicy.WindowFloor(viewport.X, viewport.Y, bounds.Width, bounds.Height)
                    : overview?.Floor(viewport.X, viewport.Y) ?? CameraPolicy.MinimumZoom;
                Require(floor == expected, $"FLOOR {floor} expected {expected}");
                if (floored && size.X >= 2560) Require(floor > CameraPolicy.MinimumZoom, $"NOT_RAISED {floor}");
                Require(camera.State.Zoom == floor, $"RESIZE_NOT_FLOORED {camera.State.Zoom} floor {floor}");
                // Asked below the floor: it stops there. The far frame after the resize is timed (with an overview the zoom may already
                // have been at the floor, so it can be a steady frame rather than the first one).
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
                if (overview is not null)
                {
                    // ARC-RENDER-034: the view's corners and edge midpoints fall on the core or an overview sector.
                    var centre = _mapWindowCentre!.Value;
                    var core = new[] { AreaTerrainOverview.Diamond(_readyArea.Plan.Chart, centre.X - 1, centre.Y - 1)[0],
                        AreaTerrainOverview.Diamond(_readyArea.Plan.Chart, centre.X - 1, centre.Y + 1)[1],
                        AreaTerrainOverview.Diamond(_readyArea.Plan.Chart, centre.X + 1, centre.Y + 1)[2],
                        AreaTerrainOverview.Diamond(_readyArea.Plan.Chart, centre.X + 1, centre.Y - 1)[3] };
                    var probes = new[] { (left, top), (left + width / 2, top), (left + width, top), (left + width, top + height / 2),
                        (left + width, top + height), (left + width / 2, top + height), (left, top + height), (left, top + height / 2) };
                    var empty = probes.Where(p => !InDiamond(core, p) && !overview.Sectors.Any(sector => InDiamond(sector.Corners, p))).ToArray();
                    Require(empty.Length == 0, $"EMPTY_GROUND {string.Join(";", empty.Select(p => $"{p.Item1:0},{p.Item2:0}"))}");
                    var past = left < windowBounds.X || top < windowBounds.Y || left + width > windowBounds.X + windowBounds.Width || top + height > windowBounds.Y + windowBounds.Height;
                    if (size.X >= 2560) Require(past, $"NOT_PAST_WINDOW view {width:0}x{height:0} window {windowBounds.Width}x{windowBounds.Height}");
                    Require(_overviewLayer is { Visible: true, Redraws: > 0 } && ReferenceEquals(_overviewLayer.Overview, overview), "OVERVIEW_NOT_DRAWN");
                    // Away from the map's edge the overview is whole (13 × 13 less the core), so the probes prove a convex cover.
                                        Require(overview.Sectors.Count == AreaTerrainOverview.MaxSectors - 9, $"OVERVIEW_PARTIAL {overview.Sectors.Count}");
                    // A pixel near the view's lower left corner, on an overview sector, shows that sector's colour.
                    var pixelAt = (left + width * 0.04, top + height * 0.96);
                    if (!InDiamond(core, pixelAt) && overview.Sectors.FirstOrDefault(sector => InDiamond(sector.Corners, pixelAt)) is { } under)
                    {
                        var image = GetViewport().GetTexture().GetImage();
                        var pixel = image.GetPixel((int)((pixelAt.Item1 - left) * zoom), (int)((pixelAt.Item2 - top) * zoom));
                        var want = new Color(under.Colour.R / 255f, under.Colour.G / 255f, under.Colour.B / 255f);
                        Require(Math.Abs(pixel.R - want.R) < .04 && Math.Abs(pixel.G - want.G) < .04 && Math.Abs(pixel.B - want.B) < .04,
                            $"OVERVIEW_PIXEL {pixel} want {want} sector {under.Sector.X},{under.Sector.Y}");
                        sampledPixels++;
                    }
                    Require(frameMs < CensusFrameBudgetMs, $"FRAME_BUDGET {frameMs:0}");
                    covered.Add(new { Viewport = new { viewport.X, viewport.Y }, PastWindow = past, Probes = probes.Length });
                }
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
            if (overview is not null)
                GD.Print("ARC-RENDER-034 native-smoke overview PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                    Map = _readyMapName, Sectors = overview.Sectors.Count, Fallback = overview.MissingColours, SampledPixels = sampledPixels,
                    Window = new { windowBounds.Width, windowBounds.Height }, Bounds = new { bounds.Width, bounds.Height }, Covered = covered,
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

    // Whether a point lies in a diamond given by its top, right, bottom and left corners (edges included).
    private static bool InDiamond(IReadOnlyList<(double X, double Y)> d, (double X, double Y) p)
    {
        static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        for (var i = 0; i < 4; i++) if (Cross(d[i], d[(i + 1) % 4], p) < -1e-6) return false;
        return true;
    }
}
