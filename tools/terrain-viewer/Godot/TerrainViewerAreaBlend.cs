using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-018: native observation of the body blends a map draws (multiply, constant alpha, wading). For each kind
// the map has, one unscaled body (not a wall, so no wall fade) is read twice: with the body left out (the colour
// beneath) and drawn. An opaque source pixel that nothing else covers and whose blend differs from both inputs must be
// the original's blend of the two within 3/255: multiply s·d/255, constant alpha and wading rows d + (s − d)·a/255
// (the shader's /255 differs from the original's /256 by at most 1/255). An upright wading body's rows above the water
// line must also show the source itself where it differs from the colour beneath. Lighting is off during the map
// smoke, so every channel is neutral. The samples hold the camera (ARC-RENDER-023) and refuse a pair whose view moved.
public partial class TerrainViewerApp
{
    private const int AreaBlendTolerance = 3;
    private const int AreaBlendTelling = 16;

    private async Task RunAreaBlendSmokeAsync(AreaLoadResult area)
    {
        var bodies = area.Plan.Commands.Where(command => command.Identity is not null && command.ArtClass != 1
            && command.Source.Width == command.Destination.Width && command.Source.Height == command.Destination.Height).ToArray();
        var kinds = new (string Name, Func<AreaDrawCommand, bool> Of)[]
        {
            ("multiply", command => command.Multiply),
            ("constant-alpha", command => command.Unlit && !command.Multiply && !command.Additive && command.WadeRows == 0),
            ("wading", command => command.WadeRows > 0 && !command.Unlit && !command.Additive && !command.Multiply),
        };
        var observed = new List<object>();
        await WithAreaSmokeCameraHeld(async () =>
        {
            foreach (var (name, of) in kinds)
            {
                var candidates = bodies.Where(of).Take(32).ToArray();
                if (candidates.Length == 0) continue;
                object? sample = null;
                foreach (var command in candidates)
                    if ((sample = await AreaBlendSample(area, command)) is not null) break;
                if (sample is null) throw new InvalidOperationException($"AREA_BLEND_{name.ToUpperInvariant().Replace('-', '_')}_NOT_OBSERVED");
                observed.Add(new { Kind = name, Candidates = candidates.Length, Sample = sample });
            }
            return observed;
        });
        GD.Print(observed.Count == 0 ? "ARC-RENDER-018 native-smoke blend NONE (no multiply, constant-alpha or wading body)"
            : "ARC-RENDER-018 native-smoke blend PASS " + System.Text.Json.JsonSerializer.Serialize(observed));
    }

    // One telling pixel of the command (and, for an upright wading body, one above its water line); null when none is
    // observable, an exception when an observed pixel is wrong.
    private async Task<object?> AreaBlendSample(AreaLoadResult area, AreaDrawCommand command)
    {
        var d = command.Destination;
        _camera!.ResetWorld(new Vec2(d.X + d.Width / 2d, d.Y + d.Height / 2d), WorldViewport, 1);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var ordered = _canvas!.AreaVisibleCommands.ToList();
        var index = ordered.IndexOf(command);
        if (index < 0) { AreaBlendDetail(command, "not visible"); return null; }
        var frame = AreaFramesOf(area)[command.Frame];
        var transform = AreaRasterTransform(GetViewport());
        var hud = _hud!.Rect; var size = GetViewport().GetVisibleRect().Size;
        var actor = _readyActor?.Destination;
        // Uncovered opaque destination pixels (x, y local) with their screen pixel and source offset.
        var pixels = new List<(int X, int Y, int Px, int Py, int Offset)>();
        for (var y = 0; y < d.Height; y++)
        for (var x = 0; x < d.Width; x++)
        {
            var sx = command.Source.X + (command.FlipX ? d.Width - 1 - x : x); var sy = command.Source.Y + (command.FlipY ? d.Height - 1 - y : y);
            var offset = (sy * frame.Width + sx) * 4;
            if (frame.Rgba[offset + 3] != 255) continue;
            var wx = d.X + x; var wy = d.Y + y;
            var screen = transform * new Vector2(wx + .5f, wy + .5f);
            if (screen.X < 0 || screen.Y < 0 || screen.X >= size.X || screen.Y >= size.Y || hud.HasPoint(screen)) continue;
            if (actor is { } a && wx >= a.X - 5 && wy >= a.Y - 5 && wx < a.X + a.Width + 5 && wy < a.Y + a.Height + 5) continue;
            var covered = false;
            for (var later = index + 1; later < ordered.Count && !covered; later++)
            {
                var o = ordered[later].Destination;
                covered = wx >= o.X && wy >= o.Y && wx < (long)o.X + o.Width && wy < (long)o.Y + o.Height;
            }
            if (!covered) pixels.Add((x, y, (int)screen.X, (int)screen.Y, offset));
        }
        if (pixels.Count == 0) { AreaBlendDetail(command, "no uncovered opaque pixel"); return null; }
        Image beneath, drawn;
        var view = _camera!.State;
        _canvas.AreaSmokeOmitted = command.Identity;
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            beneath = GetViewport().GetTexture().GetImage();
        }
        finally { _canvas.AreaSmokeOmitted = null; }
        var beneathView = _camera.State;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        drawn = GetViewport().GetTexture().GetImage();
        // Both reads must show the view the pixels were chosen in.
        if (beneathView != view || _camera.State != view)
        {
            beneath.Dispose(); drawn.Dispose();
            throw new InvalidOperationException($"AREA_BLEND_CAMERA_MOVED {command.Identity}: {view} {beneathView} {_camera.State}");
        }
        using (beneath) using (drawn)
        {
            object? Observe((int X, int Y, int Px, int Py, int Offset) p, int alpha, bool multiply, bool source = false)
            {
                int[] s = [frame.Rgba[p.Offset], frame.Rgba[p.Offset + 1], frame.Rgba[p.Offset + 2]];
                var bc = beneath.GetPixel(p.Px, p.Py); var rc = drawn.GetPixel(p.Px, p.Py);
                int[] b = [bc.R8, bc.G8, bc.B8]; int[] r = [rc.R8, rc.G8, rc.B8];
                int[] e = [.. Enumerable.Range(0, 3).Select(c => multiply ? s[c] * b[c] / 255 : b[c] + (s[c] - b[c]) * alpha / 255)];
                // Only a pixel whose expectation differs from both inputs tells the blend apart from drawing or omitting the source
                // (for the source rows above a water line: from omitting it).
                if (!Enumerable.Range(0, 3).Any(c => Math.Abs(b[c] - s[c]) > AreaBlendTelling
                    && (source || (Math.Abs(e[c] - s[c]) > AreaBlendTelling && Math.Abs(e[c] - b[c]) > AreaBlendTelling)))) return null;
                if (Enumerable.Range(0, 3).Any(c => Math.Abs(e[c] - r[c]) > AreaBlendTolerance))
                    throw new InvalidOperationException($"AREA_BLEND_PIXEL_MISMATCH {command.Identity} at {p.Px},{p.Py}: source {string.Join('/', s)} "
                        + $"beneath {string.Join('/', b)} drawn {string.Join('/', r)} expected {string.Join('/', e)} alpha {alpha}");
                return new { Pixel = new { X = p.Px, Y = p.Py }, Source = s, Beneath = b, Drawn = r, Expected = e };
            }
            var waterLine = d.Height - command.WadeRows;
            object? blended = null, upper = null;
            foreach (var p in pixels.Where(p => command.WadeRows == 0 || p.Y >= waterLine))
                if ((blended = Observe(p, command.WadeRows > 0 ? AreaScenePlan.WadingAlpha : command.Alpha, command.Multiply)) is not null) break;
            if (blended is null) { AreaBlendDetail(command, $"no telling pixel among {pixels.Count}"); return null; }
            if (command.WadeRows > 0 && waterLine > 0)
            {
                foreach (var p in pixels.Where(p => p.Y < waterLine))
                    if ((upper = Observe(p, 255, false, source: true)) is not null) break;
                if (upper is null) { AreaBlendDetail(command, "no telling pixel above the water line"); return null; }
            }
            return new { command.Identity, command.Alpha, command.Multiply, command.WadeRows, Blended = blended, Upper = upper };
        }
    }

    private static void AreaBlendDetail(AreaDrawCommand command, string reason)
        => GD.Print($"ARC-RENDER-018 smoke detail {command.Identity?.Sector}/{command.Identity?.SourceIndex}: {reason}");
}
