using Arcanum.Data.Traversal;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using Godot;

namespace Arcanum.TerrainViewer;

public partial class TerrainViewerApp
{
    // ARC-RENDER-011: the original wall check for the published map, from the walls validated at load. One check per
    // map (a map change or a quick load starts from an empty set, as the source's flush on map load); it is
    // recalculated when the actor's committed tile changes.
    private AreaWallCheck? _areaWallCheck;
    private AreaLoadResult? _areaWallArea;
    private AreaStepCell? _areaWallCell;

    private void FollowAreaWalls()
    {
        if (_readyArea is not { } area || _worldActor.Session is not AreaGridSession session || _canvas is null) return;
        // ARC-DEV-380: the first call of a map is timed (steps print as walls.NAME).
        using var lap = !ReferenceEquals(_areaWallArea, area) && PerfProbe.Enabled ? PerfProbe.Lap("walls") : null;
        lap?.Step("bind");
        if (!ReferenceEquals(_areaWallArea, area))
        {
            _areaWallCheck = new AreaWallCheck(area.Walls); _areaWallArea = area; _areaWallCell = null;
            _canvas.SetAreaWallFades(null);
        }
        if (_areaWallCheck!.WallCount == 0) return;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        if (_areaWallCell == cell) return;
        _areaWallCell = cell;
        lap?.Step("recalc");
        _canvas.SetAreaWallFades(_areaWallCheck.Recalculate(cell.X, cell.Y).Fades);
    }

    private void StopAreaWalls() { _areaWallCheck = null; _areaWallArea = null; _areaWallCell = null; }

    private string AreaWallStatus() => _areaWallCheck is not { WallCount: > 0 } check ? "cave walls drawn"
        : $"cave walls drawn, wall check {check.State.Fades.Count} faded (block {check.State.BlockX},{check.State.BlockY})";

    // Native observation (ARC-RENDER-011), on a map with walls: the wall check has faded walls around the actor, and
    // the same zoom-2 view drawn with that set and with every wall opaque differs, only on opaque texels of faded walls.
    private async Task CheckAreaWallFadeAsync(string? capture)
    {
        var check = _areaWallCheck ?? throw new InvalidOperationException("AREA_WALL_CHECK_MISSING");
        var state = check.State;
        if (state.Fades.Count == 0) throw new InvalidOperationException("AREA_WALL_FADE_EMPTY");
        var zoom = _camera!.State.Zoom; _hud!.Visible = false;
        try
        {
            _camera.ResetWorld(WorldAnchor, WorldViewport, 2);
            await WallFrames();
            using var faded = GetViewport().GetTexture().GetImage();
            _canvas!.SetAreaWallFades(null); await WallFrames();
            using var opaque = GetViewport().GetTexture().GetImage();
            _canvas.SetAreaWallFades(state.Fades); await WallFrames();
            using var again = GetViewport().GetTexture().GetImage();
            var transform = AreaDrawItem.RasterTransform(GetViewport());
            var walls = _canvas.AreaVisibleCommands.Where(command => command.Identity is { } key && state.Fades.ContainsKey(key)).ToArray();
            int changed = 0, outside = 0, unstable = 0;
            if (faded.GetFormat() != opaque.GetFormat() || faded.GetSize() != opaque.GetSize() || faded.GetSize() != again.GetSize())
                throw new InvalidOperationException("AREA_WALL_CAPTURE_FORMAT");
            byte[] a = faded.GetData(), b = opaque.GetData(), c = again.GetData();
            var width = faded.GetWidth(); var stride = a.Length / (width * faded.GetHeight());
            for (var pixel = 0; pixel < a.Length / stride; pixel++)
            {
                var at = pixel * stride;
                if (!a.AsSpan(at, stride).SequenceEqual(c.AsSpan(at, stride))) unstable++;
                if (a.AsSpan(at, stride).SequenceEqual(b.AsSpan(at, stride))) continue;
                changed++;
                if (!walls.Any(command => AreaAlphaAt(command, _readyArea!.Frames, transform, pixel % width, pixel / width) != 0)) outside++;
            }
            if (walls.Length == 0 || changed == 0 || outside != 0 || unstable != 0)
                throw new InvalidOperationException($"AREA_WALL_FADE_NOT_OBSERVED visible={walls.Length} changed={changed} outside={outside} unstable={unstable}");
            // ARC-UI-005: a faded wall is see-through for the pick (original OWAF_TRANS_LEFT/RIGHT); drawn opaque it is picked.
            var wallKeys = state.Fades.Keys.ToHashSet();
            var wallPixels = new List<Vector2>();
            for (var y = 0; y < faded.GetHeight() && wallPixels.Count < 400; y += 3)
            for (var x = 0; x < width && wallPixels.Count < 400; x += 3)
                if (walls.Any(command => AreaAlphaAt(command, _readyArea!.Frames, transform, x, y) != 0)) wallPixels.Add(new(x + .5f, y + .5f));
            var pickedFaded = wallPixels.Count(p => AreaObjectAtScreen(p) is { } key && wallKeys.Contains(key));
            _canvas.SetAreaWallFades(null);
            var pickedOpaque = wallPixels.Count(p => AreaObjectAtScreen(p) is { } key && wallKeys.Contains(key));
            _canvas.SetAreaWallFades(state.Fades);
            if (wallPixels.Count == 0 || pickedFaded != 0 || pickedOpaque == 0)
                throw new InvalidOperationException($"AREA_FADED_WALL_PICK pixels={wallPixels.Count} faded={pickedFaded} opaque={pickedOpaque}");
            GD.Print($"ARC-UI-005 native-smoke faded-wall pick PASS pixels={wallPixels.Count} pickedWhenFaded=0 pickedWhenOpaque={pickedOpaque}");
            if (!string.IsNullOrWhiteSpace(capture)
                && (faded.SavePng(capture + "-walls-faded.png") != Error.Ok || opaque.SavePng(capture + "-walls-opaque.png") != Error.Ok))
                throw new InvalidOperationException("AREA_WALL_CAPTURE_FAILED");
            GD.Print("ARC-RENDER-011 native-smoke wall fade PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Block = new[] { state.BlockX, state.BlockY }, Regions = state.Regions.ToString(), Faded = state.Fades.Count,
                VisibleFaded = walls.Length, ChangedPixels = changed, state.SharedEdgeTiles, Parity = "NOT_VERIFIED" }));
        }
        finally { _hud.Visible = true; _camera.ResetWorld(WorldAnchor, WorldViewport, zoom); }
    }
    private async Task WallFrames()
    {
        for (var frame = 0; frame < 3; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }
}
