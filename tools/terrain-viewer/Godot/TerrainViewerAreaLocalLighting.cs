using Arcanum.Data.Lighting;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-012 native observation in the cave: after the transfer the cave's own map-default scheme is
// applied, and terrain and bodies around its stationary lights read back as source × composed light / 255
// at pixels where the lights change the colour visibly beyond the plain ambient. Then one more hour: the
// lights (flags 0, own colour) keep their lattice and terrain is re-observed over the new ambient.
public partial class TerrainViewerApp
{
    // ARC-RENDER-029: the samples hold the camera, as the blend samples do (ARC-RENDER-023).
    private Task<object> RunCaveLightingSmokeAsync() => WithAreaSmokeCameraHeld(ObserveCaveLightingAsync);
    private async Task<object> ObserveCaveLightingAsync()
    {
        if (!InCave || _areaAmbient is not { Enabled: true } driver || _readyArea?.Lighting is not { Local: { } local } lighting)
            throw new InvalidOperationException("CAVE_LIGHTING_REQUIRED");
        var area = _readyArea;
        if (driver.Selection.SchemeId != lighting.Source.MapDefaultScheme || !driver.Selection.FromMapDefault || local.LightCount == 0)
            throw new InvalidOperationException("CAVE_LIGHTING_SCHEME_OR_LIGHTS");
        var hours = new List<object>();
        StationaryLightLattice? firstLattice = null;
        foreach (var advance in new[] { 0, 6 })
        {
            if (advance > 0) { driver.AdvanceHours(advance, ActiveAreaSector()); PushAreaAmbient(); }
            var pair = driver.Effective;
            var view = local.View(area.Plan, lighting.Channels, pair);
            var lattice = view.Lattice ?? throw new InvalidOperationException("CAVE_LIGHTING_LATTICE_REQUIRED");
            if (firstLattice is null) firstLattice = lattice;
            else if (!lattice.Lighter.SequenceEqual(firstLattice.Lighter) || !lattice.Darker.SequenceEqual(firstLattice.Darker))
                throw new InvalidOperationException("CAVE_LIGHTING_LATTICE_CHANGED_WITH_HOUR");
            var terrainLit = 0; var lights = new List<int>();
            foreach (var (light, rect) in local.Field.Placements)
            {
                if ((light.Flags & StationaryLightFlags.Off) != 0) continue;
                var centre = (X: rect.X + rect.Width / 2d, Y: rect.Y + rect.Height / 2d);
                var candidates = area.Plan.Commands.Where(command => command.Identity is null && lighting.Channels[command.Visual] != AreaAmbientChannel.Unlit)
                    .OrderBy(command => Math.Abs(command.Destination.X + command.Destination.Width / 2d - centre.X) + Math.Abs(command.Destination.Y + command.Destination.Height / 2d - centre.Y))
                    .Take(24).ToArray();
                foreach (var command in candidates)
                {
                    var ambient = AreaLocalLighting.Ambient(lighting.Channels[command.Visual], pair);
                    if (await ObserveLitAsync(command, ambient, world => StationaryLightField.TerrainPixel(lattice, ambient, world.X, world.Y)))
                    { terrainLit++; lights.Add(light.Index); break; }
                }
            }
            if (terrainLit == 0) throw new InvalidOperationException($"CAVE_LIGHTING_TERRAIN_NOT_OBSERVED_HOUR{driver.Clock.Hour:00}");
            // Bodies lit beyond their tile ambient: one constant-colour body and one wall gradient when present.
            var bodies = new Dictionary<string, bool>();
            foreach (var wall in new[] { false, true })
            {
                // Lit means different from the same rule without any light.
                AreaLitColors Unlit(Arcanum.World.AreaDrawCommand command) => local.Body(command, lighting.Channels[command.Visual], pair, lit: false);
                var lit = area.Plan.Commands.Where(command => command.Identity is not null && view.Bodies.TryGetValue(command.Visual, out var colors)
                    && colors.Gradient == wall && colors != Unlit(command)).Take(24).ToArray();
                if (lit.Length == 0) continue;
                var found = false;
                foreach (var command in lit)
                {
                    var colors = view.Bodies[command.Visual]; var d = command.Destination;
                    // A wall's unlit form is constant; the observation must differ from it beyond the tolerance.
                    var baseline = Unlit(command).Left;
                    if (await ObserveLitAsync(command, baseline, world =>
                    {
                        var t = Math.Clamp((world.X - d.X) / Math.Max(d.Width - 1d, 1d), 0, 1);
                        return (colors.Left.R + (colors.Right.R - colors.Left.R) * t, colors.Left.G + (colors.Right.G - colors.Left.G) * t,
                            colors.Left.B + (colors.Right.B - colors.Left.B) * t);
                    })) { found = true; break; }
                }
                bodies[wall ? "wall" : "body"] = found;
            }
            if (bodies.Count == 0 || bodies.Values.Any(found => !found)) throw new InvalidOperationException($"CAVE_LIGHTING_BODY_NOT_OBSERVED_HOUR{driver.Clock.Hour:00}");
            hours.Add(new { driver.Clock.Hour, driver.Selection.SchemeId, TerrainLit = terrainLit, Lights = lights, Bodies = bodies });
        }
        GD.Print("ARC-RENDER-012 native-smoke cave lighting PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = area.Snapshot.CatalogueMap, Lights = local.LightCount, Frames = local.Field.Source.Frames.Count, Hours = hours }));
        return new { CaveLighting = true, Hours = hours };

        async Task<bool> ObserveLitAsync(Arcanum.World.AreaDrawCommand command, AmbientColor plain, Func<Vector2, (double R, double G, double B)> tintAt)
        {
            _camera!.ResetWorld(new GamepadLab.Core.Vec2(command.Destination.X + command.Destination.Width / 2d,
                command.Destination.Y + command.Destination.Height / 2d), WorldViewport, 1);
            var cameraView = _camera.State;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (_camera.State != cameraView) throw new InvalidOperationException($"CAVE_LIGHTING_CAMERA_MOVED {cameraView} {_camera.State}");
            return TryAreaPixel(command, AreaFramesOf(area), _canvas!.AreaVisibleCommands, image, GetViewport(), _hud!.Rect,
                _readyActor is { } actor ? new Rect2(actor.Destination.X - 5, actor.Destination.Y - 5, actor.Destination.Width + 10, actor.Destination.Height + 10) : null,
                1, out _, out _, plain, tintAt, _canvas.AreaVisibleCovers);
        }
    }
}
