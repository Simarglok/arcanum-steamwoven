using Arcanum.Data.Lighting;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-030 native observation in map mode: the map's object lights (built for the current hour) are part of the terrain
// lattice; near object lights that are on, terrain pixels read back as source × composed light / 255 where the light changes
// the colour visibly beyond the plain ambient. Additive and dark (shade) lights are both counted. Skips a map without object
// lights. Prints counts only.
public partial class TerrainViewerApp
{
    private Task<object> RunMapObjectLightSmokeAsync() => WithAreaSmokeCameraHeld(ObserveMapObjectLightsAsync);

    private async Task<object> ObserveMapObjectLightsAsync()
    {
        if (_readyMapName is null || _areaAmbient is not { } driver || _readyArea?.Lighting is not { Local: { } all } lighting)
        {
            GD.Print("ARC-RENDER-030 native-smoke object lights SKIP (no map lighting)");
            return new { ObjectLights = "skip" };
        }
        var area = _readyArea;
        // The map smoke observes unlit pixels; this one lights the map and restores the setting.
        var wasEnabled = driver.Enabled;
        driver.Enabled = true; PushAreaAmbient();
        try { return await Observe(driver, area, all, lighting); }
        finally
        {
            // The later stages (the positional sound cues among them) expect the view back on the actor.
            driver.Enabled = wasEnabled; PushAreaAmbient();
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom); PublishWorldSelection();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task<object> Observe(AreaAmbientDriver driver, AreaLoadResult area, AreaLocalLighting all, AreaLightingPlan lighting)
    {
        var local = all.AtHour(driver.Clock.Hour);
        if (local.ObjectLightCount == 0)
        {
            GD.Print($"ARC-RENDER-030 native-smoke object lights SKIP (map {_readyMapName} has none at hour {driver.Clock.Hour})");
            return new { ObjectLights = "none" };
        }
        var pair = driver.Effective;
        var view = local.View(area.Plan, lighting.Channels, pair);
        var lattice = view.Lattice ?? throw new InvalidOperationException("OBJECT_LIGHTS_LATTICE_REQUIRED");
        int observed = 0, dark = 0, tried = 0;
        foreach (var (light, rect) in local.Field.Placements)
        {
            if (light.Owner is null || (light.Flags & StationaryLightFlags.Off) != 0) continue;
            if (observed >= 3 || tried >= 60) break;
            tried++;
            var centre = (X: rect.X + rect.Width / 2d, Y: rect.Y + rect.Height / 2d);
            var candidates = area.Plan.Commands.Where(command => command.Identity is null && lighting.Channels[command.Visual] != AreaAmbientChannel.Unlit)
                .OrderBy(command => Math.Abs(command.Destination.X + command.Destination.Width / 2d - centre.X) + Math.Abs(command.Destination.Y + command.Destination.Height / 2d - centre.Y))
                .Take(12).ToArray();
            foreach (var command in candidates)
            {
                var ambient = AreaLocalLighting.Ambient(lighting.Channels[command.Visual], pair);
                if (!await ObserveTerrainLitAsync(command, ambient, world => StationaryLightField.TerrainPixel(lattice, ambient, world.X, world.Y))) continue;
                observed++;
                if ((light.Flags & StationaryLightFlags.Dark) != 0) dark++;
                break;
            }
        }
        if (observed == 0) throw new InvalidOperationException($"OBJECT_LIGHTS_TERRAIN_NOT_OBSERVED tried={tried}");
        GD.Print("ARC-RENDER-030 native-smoke object lights PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = area.Snapshot.CatalogueMap, Hour = driver.Clock.Hour, Lights = local.LightCount, ObjectLights = local.ObjectLightCount,
            NightOnlyDiffers = !ReferenceEquals(all.AtHour(0), all.AtHour(12)), Observed = observed, Dark = dark, Tried = tried }));
        return new { ObjectLights = observed };

        async Task<bool> ObserveTerrainLitAsync(AreaDrawCommand command, Arcanum.Data.Lighting.AmbientColor plain, Func<Vector2, (double R, double G, double B)> tintAt)
        {
            _camera!.ResetWorld(new GamepadLab.Core.Vec2(command.Destination.X + command.Destination.Width / 2d,
                command.Destination.Y + command.Destination.Height / 2d), WorldViewport, 1);
            var cameraView = _camera.State;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (_camera.State != cameraView) throw new InvalidOperationException($"OBJECT_LIGHTS_CAMERA_MOVED {cameraView} {_camera.State}");
            return TryAreaPixel(command, area.Frames, _canvas!.AreaVisibleCommands, image, GetViewport(), _hud!.Rect,
                _readyActor is { } actor ? new Rect2(actor.Destination.X - 5, actor.Destination.Y - 5, actor.Destination.Width + 10, actor.Destination.Height + 10) : null,
                1, out _, out _, plain, tintAt);
        }
    }
}
