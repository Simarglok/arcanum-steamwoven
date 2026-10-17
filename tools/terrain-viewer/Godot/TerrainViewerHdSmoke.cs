using System.Diagnostics;
using Arcanum.Data.Hd;
using Arcanum.TerrainViewer.Core;
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
            $"bodies={bodies.Length} (walls {walls}) actor={actor.Length} chunks={chunks.Length}");
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

    private static HdNearestPack.Frame[] ActorHdFrames(TerrainLoadResult result)
    {
        var frames = result.ActorTextures.Values.Where(texture => texture.HdQuery is not null)
            .Select(texture => new HdNearestPack.Frame(texture.HdQuery!, texture.Rgba)).ToArray();
        if (frames.Length != result.ActorTextures.Count) throw new InvalidOperationException("HD_SMOKE_ACTOR_IDENTITY");
        return frames;
    }

    private async Task HdIdentitySmokeAsync(string domain, HdNearestPack.Frame[] frames, IReadOnlyList<(string Name, Vec2 Center, double Zoom)> views,
        Func<HdSubmissionStats> stats, string census)
    {
        if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("HD_SMOKE_NATIVE_REQUIRED");
        var canvas = _canvas!; var hud = _hud!;
        var (profile, previous, budget) = (canvas.VisualProfile, canvas.HdSource, canvas.HdBudget);
        var root = System.IO.Directory.CreateTempSubdirectory("arc-hd-smoke-").FullName;
        var hudVisible = hud.Visible;
        HdFrameSource? source = null;
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
            var lines = new List<string>(); var pixels = 0L; long firstBindMs = -1;
            foreach (var (name, center, zoom) in views)
            {
                _camera!.ResetWorld(center, WorldViewport, zoom);
                canvas.SetVisualProfile(VisualProfile.Legacy, source, ceiling);
                await NextFrameAsync(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var legacyImage = GetViewport().GetTexture().GetImage();
                var legacy = legacyImage.GetData();
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
                var current = stats();
                if (current.Hd == 0 || current.Fallback != 0) throw new InvalidOperationException($"HD_SMOKE_COUNTS:{name}:{current.Format()}");
                if (!legacy.AsSpan().SequenceEqual(hdPixels))
                {
                    var differing = 0; for (var at = 0; at < legacy.Length; at += 4) if (!legacy.AsSpan(at, 4).SequenceEqual(hdPixels.AsSpan(at, 4))) differing++;
                    throw new InvalidOperationException($"HD_SMOKE_RASTER:{name}:differing={differing}");
                }
                pixels += legacy.Length / 4;
                lines.Add($"{name} {current.Format()} bind={bind.ElapsedMilliseconds}ms");
            }
            GD.Print($"ARC-RENDER-014 native-smoke {domain} HD identity PASS views={views.Count} pixels={pixels} pack frames={frames.Length} ({census}) "
                + $"png={packBytes} bytes write={write.ElapsedMilliseconds}ms first-bind={firstBindMs}ms HD textures={canvas.HdResidentCount}/{canvas.HdResidentBytes} bytes "
                + $"Legacy area GPU={_cache.AreaCount}/{_cache.AreaRgbaBytes} bytes | " + string.Join(" | ", lines));
        }
        finally
        {
            hud.Visible = hudVisible;
            canvas.SetVisualProfile(profile, previous, budget);
            source?.Dispose();
            System.IO.Directory.Delete(root, true);
        }
    }
}
