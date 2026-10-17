using Arcanum.TerrainViewer.Core;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-018: native observation of the body blends a map draws (multiply, constant alpha, wading). For each kind
// the map has, one opaque source pixel of an unscaled body that nothing else covers is read twice: with the body left
// out (the colour beneath) and drawn. The drawn pixel must be the original's blend of the two: multiply s·d/255,
// constant alpha d + (s − d)·a/255, wading rows at alpha 92 and the rows above them the source itself. Lighting is off
// during the map smoke, so every channel is neutral.
public partial class TerrainViewerApp
{
    private const int AreaBlendTolerance = 3;

    private async Task RunAreaBlendSmokeAsync(AreaLoadResult area)
    {
        var bodies = area.Plan.Commands.Where(command => command.Identity is not null && command.Source.Width == command.Destination.Width
            && command.Source.Height == command.Destination.Height).ToArray();
        var kinds = new (string Name, Func<AreaDrawCommand, bool> Of)[]
        {
            ("multiply", command => command.Multiply),
            ("constant-alpha", command => !command.Multiply && !command.Additive && command.Alpha != 255),
            ("wading", command => command.WadeRows > 0 && !command.Multiply && !command.Additive && command.Alpha == 255),
        };
        var observed = new List<object>();
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
        GD.Print(observed.Count == 0 ? "ARC-RENDER-018 native-smoke blend NONE (no multiply, constant-alpha or wading body)"
            : "ARC-RENDER-018 native-smoke blend PASS " + System.Text.Json.JsonSerializer.Serialize(observed));
    }

    // One pixel of the command: null when no pixel of it is uncovered, opaque and distinguishable from both inputs.
    private async Task<object?> AreaBlendSample(AreaLoadResult area, AreaDrawCommand command)
    {
        var d = command.Destination;
        _camera!.ResetWorld(new Vec2(d.X + d.Width / 2d, d.Y + d.Height / 2d), WorldViewport, 1);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var ordered = _canvas!.AreaVisibleCommands.ToList();
        var index = ordered.IndexOf(command);
        if (index < 0) { AreaBlendDetail(command, "not visible"); return null; }
        var frame = area.Frames[command.Frame];
        var transform = AreaRasterTransform(GetViewport());
        var hud = _hud!.Rect; var size = GetViewport().GetVisibleRect().Size;
        var actor = _readyActor?.Destination;
        // Candidate pixels: opaque source, inside the viewport and outside the HUD, not under any later command or the actor.
        var pixels = new List<(int X, int Y, int Sx, int Sy)>();
        // A wading body is read in its wading rows (only they are blended); every other body on a two-pixel lattice.
        var rows = command.WadeRows > 0 && command.Alpha == 255 ? Enumerable.Range(d.Height - command.WadeRows, command.WadeRows)
            : Enumerable.Range(0, (d.Height + 1) / 2).Select(row => row * 2);
        foreach (var y in rows)
        for (var x = 0; x < d.Width && pixels.Count < 256; x += 2)
        {
            var sx = command.FlipX ? d.Width - 1 - x : x; var sy = command.FlipY ? d.Height - 1 - y : y;
            if (frame.Rgba[((command.Source.Y + sy) * frame.Width + command.Source.X + sx) * 4 + 3] != 255) continue;
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
            if (!covered) pixels.Add(((int)screen.X, (int)screen.Y, command.Source.X + sx, command.Source.Y + sy));
        }
        if (pixels.Count == 0) { AreaBlendDetail(command, "no uncovered opaque pixel"); return null; }
        Color[] beneath, drawn;
        _canvas.AreaSmokeOmitted = command.Identity;
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using (var image = GetViewport().GetTexture().GetImage()) beneath = pixels.Select(p => image.GetPixel(p.X, p.Y)).ToArray();
        }
        finally { _canvas.AreaSmokeOmitted = null; }
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using (var image = GetViewport().GetTexture().GetImage()) drawn = pixels.Select(p => image.GetPixel(p.X, p.Y)).ToArray();
        for (var i = 0; i < pixels.Count; i++)
        {
            var (x, y, sx, sy) = pixels[i];
            var o = (sy * frame.Width + sx) * 4;
            int[] s = [frame.Rgba[o], frame.Rgba[o + 1], frame.Rgba[o + 2]];
            int[] b = [beneath[i].R8, beneath[i].G8, beneath[i].B8];
            int[] r = [drawn[i].R8, drawn[i].G8, drawn[i].B8];
            // The destination row of this pixel decides the wading part.
            var row = (int)Math.Floor((y + .5 - transform.Origin.Y) / transform.Y.Y) - command.Destination.Y;
            var wading = command.WadeRows > 0 && row >= command.Destination.Height - command.WadeRows;
            var alpha = wading ? AreaScenePlan.WadingAlpha : command.Alpha;
            int Expected(int c) => command.Multiply ? s[c] * b[c] / 255 : b[c] + (s[c] - b[c]) * alpha / 255;
            var expected = new[] { Expected(0), Expected(1), Expected(2) };
            // Only a pixel whose blend differs from both inputs tells the blend apart from drawing or omitting the source.
            var telling = Enumerable.Range(0, 3).Any(c => Math.Abs(expected[c] - s[c]) > 16 && Math.Abs(expected[c] - b[c]) > 16);
            if (command.WadeRows > 0 && !command.Multiply && command.Alpha == 255 && !wading) continue;
            if (!telling) continue;
            if (Enumerable.Range(0, 3).Any(c => Math.Abs(expected[c] - r[c]) > AreaBlendTolerance))
                throw new InvalidOperationException($"AREA_BLEND_PIXEL_MISMATCH {command.Identity} at {x},{y}: source {string.Join('/', s)} "
                    + $"beneath {string.Join('/', b)} drawn {string.Join('/', r)} expected {string.Join('/', expected)}");
            // A wading body's rows above the water line are the source itself.
            if (command.WadeRows > 0 && !await AreaWadingUpperIsSource(area, command, ordered, index, frame, transform))
            { AreaBlendDetail(command, "no uncovered upper pixel"); return null; }
            return new { command.Identity, Pixel = new { X = x, Y = y }, Source = s, Beneath = b, Drawn = r, Expected = expected,
                Alpha = command.Multiply ? (int?)null : alpha, Wading = wading };
        }
        AreaBlendDetail(command, $"no telling pixel among {pixels.Count}");
        return null;
    }

    private static void AreaBlendDetail(AreaDrawCommand command, string reason)
        => GD.Print($"ARC-RENDER-018 smoke detail {command.Identity?.Sector}/{command.Identity?.SourceIndex}: {reason}");

    // An opaque, uncovered pixel above the wading rows draws the unlit source unchanged; false when none is uncovered.
    private async Task<bool> AreaWadingUpperIsSource(AreaLoadResult area, AreaDrawCommand command, List<AreaDrawCommand> ordered, int index,
        DecodedAreaFrame frame, Transform2D transform)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var d = command.Destination; var hud = _hud!.Rect; var size = GetViewport().GetVisibleRect().Size;
        for (var y = 0; y < d.Height - command.WadeRows; y++)
        for (var x = 0; x < d.Width; x++)
        {
            var sx = command.Source.X + (command.FlipX ? d.Width - 1 - x : x); var sy = command.Source.Y + (command.FlipY ? d.Height - 1 - y : y);
            var o = (sy * frame.Width + sx) * 4;
            if (frame.Rgba[o + 3] != 255) continue;
            var wx = d.X + x; var wy = d.Y + y;
            var screen = transform * new Vector2(wx + .5f, wy + .5f);
            if (screen.X < 0 || screen.Y < 0 || screen.X >= size.X || screen.Y >= size.Y || hud.HasPoint(screen)) continue;
            if (_readyActor?.Destination is { } a && wx >= a.X - 5 && wy >= a.Y - 5 && wx < a.X + a.Width + 5 && wy < a.Y + a.Height + 5) continue;
            if (ordered.Skip(index + 1).Any(later => wx >= later.Destination.X && wy >= later.Destination.Y
                && wx < (long)later.Destination.X + later.Destination.Width && wy < (long)later.Destination.Y + later.Destination.Height)) continue;
            var pixel = image.GetPixel((int)screen.X, (int)screen.Y);
            if (Math.Abs(pixel.R8 - frame.Rgba[o]) > AreaBlendTolerance || Math.Abs(pixel.G8 - frame.Rgba[o + 1]) > AreaBlendTolerance
                || Math.Abs(pixel.B8 - frame.Rgba[o + 2]) > AreaBlendTolerance)
                throw new InvalidOperationException($"AREA_BLEND_WADING_UPPER_MISMATCH {command.Identity} at {(int)screen.X},{(int)screen.Y}");
            return true;
        }
        return false;
    }
}
