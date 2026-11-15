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
        // ARC-DEV-2005: the controller prompt strip (a bottom overlay on its own layer) follows the device in hand after a hysteresis on the host's clock, so under machine load it
        // showed or went away between the three captures (a 300x24 block of unstable pixels): the compare is of the world's walls only.
        EnsurePromptStrip(); var promptLayer = _promptLayer!; var promptShown = promptLayer.Visible; promptLayer.Visible = false;
        try
        {
            _camera.ResetWorld(WorldAnchor, WorldViewport, 2);
            using var faded = await SettledWallFrame();
            _canvas!.SetAreaWallFades(null);
            using var opaque = await SettledWallFrame();
            _canvas.SetAreaWallFades(state.Fades);
            using var again = await SettledWallFrame();
            var transform = AreaDrawItem.RasterTransform(GetViewport());
            var walls = _canvas.AreaVisibleCommands.Where(command => command.Identity is { } key && state.Fades.ContainsKey(key)).ToArray();
            int changed = 0, outside = 0, unstable = 0;
            int unstableMinX = int.MaxValue, unstableMinY = int.MaxValue, unstableMaxX = -1, unstableMaxY = -1;
            if (faded.GetFormat() != opaque.GetFormat() || faded.GetSize() != opaque.GetSize() || faded.GetSize() != again.GetSize())
                throw new InvalidOperationException("AREA_WALL_CAPTURE_FORMAT");
            byte[] a = faded.GetData(), b = opaque.GetData(), c = again.GetData();
            var width = faded.GetWidth(); var stride = a.Length / (width * faded.GetHeight());
            for (var pixel = 0; pixel < a.Length / stride; pixel++)
            {
                var at = pixel * stride;
                if (!a.AsSpan(at, stride).SequenceEqual(c.AsSpan(at, stride)))
                {
                    unstable++;
                    unstableMinX = Math.Min(unstableMinX, pixel % width); unstableMaxX = Math.Max(unstableMaxX, pixel % width);
                    unstableMinY = Math.Min(unstableMinY, pixel / width); unstableMaxY = Math.Max(unstableMaxY, pixel / width);
                }
                if (a.AsSpan(at, stride).SequenceEqual(b.AsSpan(at, stride))) continue;
                changed++;
                if (!walls.Any(command => AreaAlphaAt(command, _readyArea!.Frames, transform, pixel % width, pixel / width) != 0)) outside++;
            }
            if ((walls.Length == 0 || changed == 0 || outside != 0 || unstable != 0) && !string.IsNullOrWhiteSpace(capture))
            { faded.SavePng(capture + "-walls-fail-faded.png"); opaque.SavePng(capture + "-walls-fail-opaque.png"); again.SavePng(capture + "-walls-fail-again.png"); }
            if (walls.Length == 0 || changed == 0 || outside != 0 || unstable != 0)
                throw new InvalidOperationException($"AREA_WALL_FADE_NOT_OBSERVED visible={walls.Length} changed={changed} outside={outside} unstable={unstable} unstable-box={(unstable == 0 ? "-" : $"{unstableMinX},{unstableMinY}-{unstableMaxX},{unstableMaxY}")} view={width}x{faded.GetHeight()}");
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
        finally { _hud.Visible = true; promptLayer.Visible = promptShown; _camera.ResetWorld(WorldAnchor, WorldViewport, zoom); }
    }
    // ARC-DEV-2294: in the HD profile the check runs right after the switch, while the HD frames of the terrain chunks may still be decoding: the first
    // capture then drew the Legacy chunks and the third the HD ones, a few levels apart in one block of the lit terrain, so "unstable" failed (every run
    // where the decode had not ended within the three frames the check waited; a Legacy run passed). A capture now waits for the visual switch and every HD
    // decode to end (the wait of the HD smokes) and is taken only once two consecutive captures are identical (at most 12 more rounds; the last is
    // returned when it never settles, and the unstable test then still reports it).
    private async Task<Image> SettledWallFrame()
    {
        await WallFrames(); // the submission of the new state (it asks for the HD frames it needs) before the wait looks at them
        var waited = 0;
        while (_canvas!.PendingVisualProfile is not null || (_canvas.HdSource?.PendingCount ?? 0) > 0 || _canvas.AreaHdStats[HdFallbackReason.Pending] > 0)
        {
            if (++waited > 1800) throw new InvalidOperationException($"AREA_WALL_HD_NOT_SETTLED decoding={_canvas.HdSource?.PendingCount ?? 0} pending={_canvas.AreaHdStats[HdFallbackReason.Pending]}");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (waited > 0) GD.Print($"ARC-DEV-2294 wall capture waited {waited} frames for HD decodes");
        await WallFrames();
        var last = GetViewport().GetTexture().GetImage();
        for (var round = 1; round <= 12; round++)
        {
            await WallFrames();
            var next = GetViewport().GetTexture().GetImage();
            var same = next.GetSize() == last.GetSize() && next.GetFormat() == last.GetFormat() && next.GetData().AsSpan().SequenceEqual(last.GetData());
            last.Dispose();
            last = next;
            if (same) return last;
        }
        GD.Print("ARC-DEV-2294 wall capture did not settle in 12 rounds");
        return last;
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
