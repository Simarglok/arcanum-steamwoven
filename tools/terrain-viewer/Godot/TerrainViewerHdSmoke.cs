using System.Diagnostics;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

/// <summary>ARC-RENDER-014 native smoke on the installed original: a nearest 4× pack (Legacy hard
/// edge) of every character and object frame the view can draw is written to a temporary
/// directory outside the repository and deleted afterwards. Under profile Hd the whole window
/// must equal the Legacy readback pixel for pixel (ARC-RENDER-013 consequence 4) with no
/// fallback, and the real HD usage is reported (counts and bytes only, never pixels or paths).</summary>
public partial class TerrainViewerApp
{
    private async Task RunAreaHdSmokeAsync(AreaLoadResult area, TerrainLoadResult result)
    {
        // ARC-RENDER-016: walls (ART class 1) and the level-0 terrain chunks are HD candidates too.
        var bodies = area.Plan.Commands.Where(command => command.Identity is not null).Select(command => command.Frame).Distinct()
            .Select(key => new HdNearestPack.Frame(HdFrameQueries.FromArea(key, area.Frames[key]), area.Frames[key].Rgba)).ToArray();
        var actor = ActorHdFrames(result);
        var widest = area.Plan.Commands.Where(command => command.Identity is not null)
            .MaxBy(command => (long)command.Destination.Width * command.Destination.Height)!;
        var views = new (string Name, Vec2 Center, double Zoom)[]
        {
            ("actor-1", WorldAnchor, 1), ("actor-2", WorldAnchor, 2), ("actor-0.5", WorldAnchor, .5),
            ("widest-body-1", new(widest.Destination.X + widest.Destination.Width / 2d, widest.Destination.Y + widest.Destination.Height / 2d), 1),
        };
        // Every chunk a view can show (a world rectangle one chunk wider than the view on each side):
        // a chunk missing from the pack would fall back and fail the identity check.
        var terrain = AreaTerrainChunks.TryCreate(area.Plan, area.Frames) ?? throw new InvalidOperationException("HD_SMOKE_NO_CHUNKS");
        var viewport = WorldViewport;
        var chunks = views.SelectMany(view => terrain.Intersecting(view.Center.X - viewport.X / (2 * view.Zoom) - AreaTerrainChunks.ChunkSize,
                view.Center.Y - viewport.Y / (2 * view.Zoom) - AreaTerrainChunks.ChunkSize,
                viewport.X / view.Zoom + 2 * AreaTerrainChunks.ChunkSize, viewport.Y / view.Zoom + 2 * AreaTerrainChunks.ChunkSize))
            .Distinct().Select(chunk => HdNearestPack.Chunk(terrain, chunk)).ToArray();
        var walls = area.Plan.Commands.Where(command => command.Identity is not null && command.ArtClass == 1).Select(command => command.Frame).Distinct().Count();
        await HdIdentitySmokeAsync("AREA", bodies.Concat(actor).Concat(chunks).ToArray(), views, () => _canvas!.AreaHdStats,
            $"bodies={bodies.Length} (walls {walls}) actor={actor.Length} chunks={chunks.Length}",
            // Identity alone would also pass if chunks were silently not candidates: a level-0 chunked view must bind HD chunks.
            name => _canvas!.AreaChunkCount > 0 && _canvas.AreaChunkLevel == 0 && _canvas.HdTerrainVisibleCount == 0 ? "HD_SMOKE_NO_HD_CHUNKS:" + name : null);
    }

    private async Task RunWorldHdSmokeAsync(TerrainLoadResult result)
    {
        var statics = result.StaticTextures.Values.Where(texture => texture.HdQuery is not null)
            .Select(texture => new HdNearestPack.Frame(texture.HdQuery!, texture.Rgba)).ToArray();
        if (statics.Length != result.StaticTextures.Count) throw new InvalidOperationException("HD_SMOKE_STATIC_IDENTITY");
        var actor = ActorHdFrames(result);
        var views = new (string Name, Vec2 Center, double Zoom)[] { ("actor-1", WorldAnchor, 1), ("actor-2", WorldAnchor, 2), ("actor-4", WorldAnchor, 4) };
        await HdIdentitySmokeAsync("WORLD", statics.Concat(actor).ToArray(), views, () => _canvas!.WorldHdStats, $"statics={statics.Length} actor={actor.Length}");
    }

    // ARC-UI-039: the hero bodies read this run are drawn actor frames too.
    private HdNearestPack.Frame[] ActorHdFrames(TerrainLoadResult result)
    {
        var textures = result.ActorTextures.Values.Concat(_heroBodies.Values.SelectMany(body => body.Textures.Values))
            .DistinctBy(texture => texture.Key).ToArray();
        var frames = textures.Where(texture => texture.HdQuery is not null)
            .Select(texture => new HdNearestPack.Frame(texture.HdQuery!, texture.Rgba)).ToArray();
        if (frames.Length != textures.Length) throw new InvalidOperationException("HD_SMOKE_ACTOR_IDENTITY");
        return frames;
    }

    private async Task HdIdentitySmokeAsync(string domain, HdNearestPack.Frame[] frames, IReadOnlyList<(string Name, Vec2 Center, double Zoom)> views,
        Func<HdSubmissionStats> stats, string census, Func<string, string?>? viewCheck = null)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("HD_SMOKE_NATIVE_REQUIRED");
        var canvas = _canvas!; var hud = _hud!;
        var (profile, previous, budget) = (canvas.VisualProfile, canvas.HdSource, canvas.HdBudget);
        // ARC-RENDER-2175: the sprite path is dormant. Without the developer override (ARC_HD_WORLD_SPRITES=1) this stage checks the gate
        // (every candidate draws Legacy as WorldDormant, nothing decoded); the identity oracle then runs on the path with the gate opened.
        var gateByEnvironment = canvas.WorldSpritesFromPack;
        var root = System.IO.Directory.CreateTempSubdirectory("arc-hd-smoke-").FullName;
        var hudVisible = hud.Visible;
        HdFrameSource? source = null;
        _relitLookHeld = true; SyncRelitLook(); // ARC-RENDER-2076: the identity oracle compares HD with Legacy, so the relit look (the one intended difference) is held off meanwhile
        try
        {
            var write = Stopwatch.StartNew();
            HdNearestPack.Write(System.IO.Path.Combine(root, "pack"), "smoke-nearest-4x", 4, frames);
            write.Stop();
            var packBytes = new System.IO.DirectoryInfo(root).EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(file => file.Length);
            var open = HdPack.Open(System.IO.Path.Combine(root, "pack"));
            if (open.Status != HdPackOpenStatus.Opened) throw new InvalidOperationException("HD_SMOKE_PACK:" + open.Status);
            source = new HdFrameSource(open.Pack);
            var ceiling = HdDeviceBudget.Ceiling(4);
            hud.Visible = false;
            var lines = new List<string>(); var gateChecks = new List<string>(); var pixels = 0L; long firstBindMs = -1;
            // ARC-RENDER-029: the views hold the camera, as the blend samples do (ARC-RENDER-023).
            await WithAreaSmokeCameraHeld(async () =>
            {
                foreach (var (name, center, zoom) in views)
                {
                    _camera!.ResetWorld(center, WorldViewport, zoom);
                    var view = _camera.State;
                    canvas.SetVisualProfile(VisualProfile.Legacy, source, ceiling);
                    await NextFrameAsync(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var legacyImage = GetViewport().GetTexture().GetImage();
                    var legacy = legacyImage.GetData();
                    if (!gateByEnvironment)
                    {
                        canvas.WorldSpritesFromPack = false; var completedBefore = source.CompletedCount; // an earlier view's decodes stay counted
                        canvas.SetVisualProfile(VisualProfile.Hd, source, ceiling);
                        await NextFrameAsync(); await NextFrameAsync();
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var gated = stats();
                        if (gated.Hd != 0 || gated.Fallback == 0 || gated[HdFallbackReason.WorldDormant] != gated.Fallback || source.PendingCount != 0 || source.CompletedCount != completedBefore)
                            throw new InvalidOperationException($"HD_SMOKE_GATE:{name}:{gated.Format()}");
                        using var gatedImage = GetViewport().GetTexture().GetImage();
                        if (!legacy.AsSpan().SequenceEqual(gatedImage.GetData())) throw new InvalidOperationException($"HD_SMOKE_GATE_RASTER:{name}");
                        gateChecks.Add($"{name} {gated.Format()}");
                        canvas.SetVisualProfile(VisualProfile.Legacy, source, ceiling);
                    }
                    canvas.WorldSpritesFromPack = true;
                    var bind = Stopwatch.StartNew();
                    canvas.SetVisualProfile(VisualProfile.Hd, source, ceiling);
                    await NextFrameAsync();
                    while (source.PendingCount > 0 || stats()[HdFallbackReason.Pending] > 0)
                    {
                        if (bind.ElapsedMilliseconds > 60_000) throw new InvalidOperationException("HD_SMOKE_PENDING:" + name);
                        await NextFrameAsync();
                    }
                    bind.Stop(); if (firstBindMs < 0) firstBindMs = bind.ElapsedMilliseconds;
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var hdImage = GetViewport().GetTexture().GetImage();
                    var hdPixels = hdImage.GetData();
                    // Both reads must show the view just set.
                    if (_camera.State != view) throw new InvalidOperationException($"HD_SMOKE_CAMERA_MOVED:{name}:{view}:{_camera.State}");
                    var current = stats();
                    if (current.Hd == 0 || current.Fallback != 0) throw new InvalidOperationException($"HD_SMOKE_COUNTS:{name}:{current.Format()}");
                    if (viewCheck?.Invoke(name) is { } failed) throw new InvalidOperationException(failed);
                    if (!legacy.AsSpan().SequenceEqual(hdPixels))
                    {
                        var differing = 0; for (var at = 0; at < legacy.Length; at += 4) if (!legacy.AsSpan(at, 4).SequenceEqual(hdPixels.AsSpan(at, 4))) differing++;
                        throw new InvalidOperationException($"HD_SMOKE_RASTER:{name}:differing={differing}");
                    }
                    pixels += legacy.Length / 4;
                    lines.Add($"{name} {current.Format()} bind={bind.ElapsedMilliseconds}ms");
                }
                // ARC-RENDER-2175: ARC_HD_GATE_CAPTURE=DIR (an absolute folder outside the repository) with ARC_HD_PACK keeps three PNGs of the first view for the
                // before/after comparison: Legacy, the dormant sprite path of the startup pack, and the gated HD look (relit look on). Local files, never committed.
                if (domain == "AREA" && OS.GetEnvironment("ARC_HD_GATE_CAPTURE") is { Length: > 0 } captureDir && System.IO.Path.IsPathRooted(captureDir) && previous?.Pack is not null)
                {
                    var (_, center0, zoom0) = views[0];
                    _camera!.ResetWorld(center0, WorldViewport, zoom0);
                    System.IO.Directory.CreateDirectory(captureDir);
                    async Task Shot(string file, VisualProfile shotProfile, bool sprites)
                    {
                        canvas.WorldSpritesFromPack = sprites;
                        canvas.SetVisualProfile(shotProfile, previous, budget ?? ceiling);
                        SyncRelitLook(); PushAreaAmbient();
                        await NextFrameAsync();
                        for (var wait = 0; (previous.PendingCount > 0 || stats()[HdFallbackReason.Pending] > 0) && wait < 3600; wait++) await NextFrameAsync();
                        await NextFrameAsync(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        using var image = GetViewport().GetTexture().GetImage();
                        image.SavePng(System.IO.Path.Combine(captureDir, file));
                        GD.Print($"ARC-RENDER-2175 capture {file} profile={shotProfile} sprites-from-pack={sprites} {stats().Format()}");
                    }
                    _relitLookHeld = false;
                    await Shot("legacy.png", VisualProfile.Legacy, false);
                    await Shot("before-hd-sprites.png", VisualProfile.Hd, true);
                    await Shot("after-gated.png", VisualProfile.Hd, false);
                    _relitLookHeld = true; SyncRelitLook(); PushAreaAmbient();
                }
                return lines.Count;
            });
            if (gateChecks.Count > 0) GD.Print($"ARC-RENDER-2175 native-smoke {domain} HD world gate PASS (world art Legacy, nothing decoded) | " + string.Join(" | ", gateChecks));
            GD.Print($"ARC-RENDER-014 native-smoke {domain} HD identity PASS views={views.Count} pixels={pixels} pack frames={frames.Length} ({census}) "
                + $"png={packBytes} bytes write={write.ElapsedMilliseconds}ms first-bind={firstBindMs}ms HD textures={canvas.HdResidentCount}/{canvas.HdResidentBytes} bytes "
                + $"(terrain chunks {canvas.HdTerrainResidentCount}/{canvas.HdTerrainResidentBytes} bytes) "
                + $"Legacy area GPU={_cache.AreaCount}/{_cache.AreaRgbaBytes} bytes | " + string.Join(" | ", lines));
        }
        finally
        {
            hud.Visible = hudVisible;
            canvas.WorldSpritesFromPack = gateByEnvironment;
            canvas.SetVisualProfile(profile, previous, budget);
            _relitLookHeld = false;
            source?.Dispose();
            System.IO.Directory.Delete(root, true);
        }
    }
}
