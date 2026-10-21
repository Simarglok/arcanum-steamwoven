using Arcanum.Data.Lighting;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-031 native observation in map mode: on a map with an animated light, the animation time steps the light through
// its frames (ledger LT6, LT7). At three animation times one frame apart the lattice differs, and a terrain pixel inside the
// animated light reads back as the lattice of that time predicts. Skips a map without an animated light. Prints counts only.
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
            var index = local.Field.Animated[0];
            var light = local.Field.Placements[index].Light;
            var art = local.Field.Source.Frames[light.ArtNumber];
            var delay = Math.Max(1, 1000 / art.FramesPerSecond);
            var frames = new List<int>(); var observed = 0; StationaryLightLattice? previous = null; var changed = 0;
            foreach (var step in new long[] { 0, 1, 2 })
            {
                _lightAnimationMs = step * delay;
                PushAreaAmbient();
                var posed = local.AtAnimation(_lightAnimationMs);
                var pose = local.Field.PosesAt(_lightAnimationMs)[index];
                frames.Add(pose.Frame);
                var pair = driver.Effective;
                var lattice = posed.View(area.Plan, lighting.Channels, pair).Lattice ?? throw new InvalidOperationException("LIGHT_ANIMATION_LATTICE_REQUIRED");
                if (previous is not null && (!lattice.Lighter.SequenceEqual(previous.Lighter) || !lattice.Darker.SequenceEqual(previous.Darker)
                    || lattice.FirstColumn != previous.FirstColumn || lattice.FirstRow != previous.FirstRow)) changed++;
                previous = lattice;
                // A terrain pixel near the light's rectangle at this frame.
                var rect = local.Field.Placements[index].Rect;
                var centre = (X: rect.X + rect.Width / 2d, Y: rect.Y + rect.Height / 2d);
                foreach (var command in area.Plan.Commands.Where(c => c.Identity is null && lighting.Channels[c.Visual] != AreaAmbientChannel.Unlit)
                    .OrderBy(c => Math.Abs(c.Destination.X + c.Destination.Width / 2d - centre.X) + Math.Abs(c.Destination.Y + c.Destination.Height / 2d - centre.Y)).Take(12))
                {
                    var ambient = AreaLocalLighting.Ambient(lighting.Channels[command.Visual], pair);
                    if (!await ObserveTerrainLitAsync(area, command, ambient, world => StationaryLightField.TerrainPixel(lattice, ambient, world.X, world.Y))) continue;
                    observed++; break;
                }
            }
            if (frames.Distinct().Count() != 3) throw new InvalidOperationException("LIGHT_ANIMATION_FRAMES " + string.Join(",", frames));
            if (changed == 0) throw new InvalidOperationException("LIGHT_ANIMATION_LATTICE_UNCHANGED");
            if (observed == 0) throw new InvalidOperationException("LIGHT_ANIMATION_NOT_OBSERVED");
            GD.Print("ARC-RENDER-031 native-smoke light animation PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = area.Snapshot.CatalogueMap, Animated = local.Field.Animated.Count, Frames = frames, art.FramesPerSecond, DelayMs = delay,
                LatticeChanges = changed, Observed = observed }));
            return new { LightAnimation = observed };
        }
        finally
        {
            driver.Enabled = wasEnabled; _lightAnimationMs = wasMs; PushAreaAmbient();
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
