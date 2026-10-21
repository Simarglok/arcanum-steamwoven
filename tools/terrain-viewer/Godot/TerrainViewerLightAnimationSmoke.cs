using Arcanum.Data.Lighting;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-031 native observation in map mode: on a map with an animated light, the animation time steps the light through
// its frames (ledger LT6, LT7). At three animation times one frame apart the lattice differs, and at the second and third a
// terrain pixel inside the posed light, where the pose changes the colour, reads back as the posed lattice predicts. Skips a
// map without an animated light that is on. Prints counts only.
public partial class TerrainViewerApp
{
    private Task<object> RunLightAnimationSmokeAsync() => WithAreaSmokeCameraHeld(ObserveLightAnimationAsync);

    private async Task<object> ObserveLightAnimationAsync()
    {
        if (_readyMapName is null || _areaAmbient is not { } driver || _readyArea?.Lighting is not { Local: { } all } lighting
            || all.AtHour(driver.Selection.Hour) is not { Field.Animated.Count: > 0 } local)
        {
            GD.Print("ARC-RENDER-031 native-smoke light animation SKIP (no animated light)");
            return new { LightAnimation = "skip" };
        }
        var area = _readyArea;
        var wasEnabled = driver.Enabled; var wasMs = _lightAnimationMs;
        driver.Enabled = true;
        try
        {
            // The first animated light that is on.
            var index = local.Field.Animated.FirstOrDefault(i => (local.Field.Placements[i].Light.Flags & StationaryLightFlags.Off) == 0, -1);
            if (index < 0)
            {
                GD.Print("ARC-RENDER-031 native-smoke light animation SKIP (every animated light is off)");
                return new { LightAnimation = "off" };
            }
            var light = local.Field.Placements[index].Light;
            var art = local.Field.Source.Frames[light.ArtNumber];
            var delay = Math.Max(1, 1000 / art.FramesPerSecond);
            var frames = new List<int>(); var observedSteps = new List<long>(); StationaryLightLattice? previous = null; var changed = 0;
            var unposed = local.View(area.Plan, lighting.Channels, driver.Effective).Lattice;
            foreach (var step in new long[] { 0, 1, 2 })
            {
                _lightAnimationMs = step * delay;
                PushAreaAmbient();
                var posed = local.AtAnimation((long)_lightAnimationMs);
                var poses = local.Field.PosesAt((long)_lightAnimationMs);
                frames.Add(poses[index].Frame);
                var pair = driver.Effective;
                var lattice = posed.View(area.Plan, lighting.Channels, pair).Lattice ?? throw new InvalidOperationException("LIGHT_ANIMATION_LATTICE_REQUIRED");
                if (previous is not null && (!lattice.Lighter.SequenceEqual(previous.Lighter) || !lattice.Darker.SequenceEqual(previous.Darker)
                    || lattice.FirstColumn != previous.FirstColumn || lattice.FirstRow != previous.FirstRow)) changed++;
                previous = lattice;
                // The light's rectangle at this pose (the frame and offsets it stands at now).
                var frame = art.Frames[poses[index].Frame];
                var unposedRect = local.Field.Placements[index].Rect;
                var rect = new Arcanum.Data.Terrain.TerrainRect(unposedRect.X + poses[index].OffsetX - light.OffsetX - frame.HotX + art.Frames[light.Frame].HotX,
                    unposedRect.Y + poses[index].OffsetY - light.OffsetY - frame.HotY + art.Frames[light.Frame].HotY, frame.Width, frame.Height);
                var centre = (X: rect.X + rect.Width / 2d, Y: rect.Y + rect.Height / 2d);
                foreach (var command in area.Plan.Commands.Where(c => c.Identity is null && lighting.Channels[c.Visual] != AreaAmbientChannel.Unlit)
                    .OrderBy(c => Math.Abs(c.Destination.X + c.Destination.Width / 2d - centre.X) + Math.Abs(c.Destination.Y + c.Destination.Height / 2d - centre.Y)).Take(16))
                {
                    var ambient = AreaLocalLighting.Ambient(lighting.Channels[command.Visual], pair);
                    // After the first step a pixel counts only where the pose changes the colour from the unposed lighting, so a
                    // canvas that kept drawing frame 0 cannot pass.
                    (double R, double G, double B) Posed(Vector2 world) => StationaryLightField.TerrainPixel(lattice, ambient, world.X, world.Y);
                    (double R, double G, double B) Unposed(Vector2 world) => StationaryLightField.TerrainPixel(unposed, ambient, world.X, world.Y);
                    bool Differs(Vector2 world) { var (a, b) = (Posed(world), Unposed(world)); return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 12; }
                    var plainColour = ambient;
                    if (!await ObserveTerrainLitAsync(area, command, plainColour, world => step == 0 || Differs(world) ? Posed(world) : (plainColour.R, plainColour.G, plainColour.B))) continue;
                    observedSteps.Add(step); break;
                }
            }
            if (frames.Distinct().Count() != 3) throw new InvalidOperationException("LIGHT_ANIMATION_FRAMES " + string.Join(",", frames));
            if (changed == 0) throw new InvalidOperationException("LIGHT_ANIMATION_LATTICE_UNCHANGED");
            if (!observedSteps.Contains(1) || !observedSteps.Contains(2)) throw new InvalidOperationException("LIGHT_ANIMATION_NOT_OBSERVED " + string.Join(",", observedSteps));
            GD.Print("ARC-RENDER-031 native-smoke light animation PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = area.Snapshot.CatalogueMap, Animated = local.Field.Animated.Count, Frames = frames, art.FramesPerSecond, DelayMs = delay,
                LatticeChanges = changed, Observed = observedSteps }));
            return new { LightAnimation = observedSteps.Count };
        }
        finally
        {
            driver.Enabled = wasEnabled; _lightAnimationMs = wasMs; _lightAnimationKey = null; PushAreaAmbient();
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
