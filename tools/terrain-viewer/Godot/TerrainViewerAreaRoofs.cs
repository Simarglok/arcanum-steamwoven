using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
using GamepadLab.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-RENDER-022: the roofs of a whole map (ARC-RENDER-021) drawn by the original's rules (ledger RF1–RF10): an unfilled
// roof is drawn after every body, tinted by the outdoor light; the player's tile change recalculates the block under it
// and the fill floods through one roof, so entering a building hides its roof; a same-map transfer first unfills the roof
// of the tile it leaves; every wall in the wall check fades the roof block of its tile; an unfaded roof covers the indoor
// tiles beneath it (drawn black, as the original leaves them undrawn) and a roof faded or not covers the bodies there
// (not drawn; the pick does not test the cover, RF10); a click on a drawn roof pixel picks no object. A map change and a
// reset scene start from the map's stored state.
public partial class TerrainViewerApp
{
    private AreaRoofGrid? _areaRoofGrid;
    private AreaLoadResult? _areaRoofArea;
    private AreaStepCell? _areaRoofCell;
    private AreaStepCell? _areaRoofDeparture;
    private HashSet<AreaObjectKey> _areaRoofWalls = new();
    private long _areaRoofRevision = -1;
    private string? _areaRoofCompanionCells;
    // ARC-WORLD-2792: the AI host's walked NPCs are covered where they stand now; their cover flags (not their cells) decide a new publish, so a step inside one room costs nothing.
    private string? _areaRoofMoverCells, _areaRoofPublishedMoverCells;
    private (AreaAiHost? Host, long Movers, long Roofs, string Companions)? _areaRoofMoverStamp;
    private Dictionary<(long, long), AreaRoofDraw> _areaRoofDraws = new();
    private Dictionary<(long, long), AreaDrawCommand[]> _areaRoofTileCommands = new();
    private Dictionary<(long, long), AreaObjectKey[]> _areaRoofBodies = new();
    private Dictionary<(long, long), uint> _areaRoofTileArt = new();
    private Dictionary<AreaObjectKey, AreaObjectRecord> _areaRoofObjects = new();

    private void FollowAreaRoofs()
    {
        if (_readyArea is not { } area || _worldActor.Session is not AreaGridSession session || _canvas is null) return;
        // ARC-DEV-380: the first call of a map is timed (steps print as roofs.NAME).
        using var lap = !ReferenceEquals(_areaRoofArea, area) && PerfProbe.Enabled ? PerfProbe.Lap("roofs") : null;
        lap?.Step("bind");
        if (!ReferenceEquals(_areaRoofArea, area)) BindAreaRoofs(area);
        lap?.Step("follow");
        if (_areaRoofGrid is not { } grid) return;
        var cell = session.Chart.ToWorld(session.State.CommittedCell);
        // A same-map transfer unfills the roof of the tile it leaves, then the move recalculates the new tile (RF8), also
        // when it lands where it left.
        var departed = _areaRoofDeparture is not null;
        if (_areaRoofDeparture is { } departure) { grid.FillOff(departure.X, departure.Y); _areaRoofDeparture = null; }
        if (departed || _areaRoofCell != cell)
        {
            grid.Recalc(cell.X, cell.Y);
            _areaRoofCell = cell;
        }
        // Each wall the wall check holds (one whose flags forbid fading included) fades the roof block of its tile while it
        // is there (RF9).
        var walls = _areaWallCheck?.State.HeldWalls.Where(key => _areaRoofObjects.TryGetValue(key, out var row) && row.WorldX is not null && row.WorldY is not null)
            .ToHashSet() ?? [];
        foreach (var key in walls.Except(_areaRoofWalls)) grid.AddWallFade(_areaRoofObjects[key].WorldX!.Value, _areaRoofObjects[key].WorldY!.Value);
        foreach (var key in _areaRoofWalls.Except(walls)) grid.ReleaseWallFade(_areaRoofObjects[key].WorldX!.Value, _areaRoofObjects[key].WorldY!.Value);
        _areaRoofWalls = walls;
        // ARC-WORLD-037: a companion is covered where it stands now, not on its T0 tile.
        var companions = _areaCompanions?.Companions.Select(c => (c.Key, Cell: session.Chart.ToWorld(c.Grid.State.CommittedCell))).ToArray() ?? [];
        var signature = string.Join(";", companions.Select(c => $"{c.Key.SourceIndex}@{c.Cell.X},{c.Cell.Y}"));
        // ARC-WORLD-2792 (ledger RF10): an NPC the AI host walked is covered where it stands now, not on its T0 tile: one that came out of a roofed building is drawn, one that went in is hidden.
        var host = _aiHost is { } ai && ai.Scene.RuntimeMap == CurrentAreaMap ? ai : null;
        var moverStamp = (host, host?.Movers.Version ?? -1, grid.Revision, signature);
        if (_areaRoofMoverStamp != moverStamp)
        {
            _areaRoofMoverStamp = moverStamp;
            // The key is in the string: a window move re-keys a mover (AreaAiHost.Rebind) without changing its covered state, and the cover set is by key.
            var held = companions.Select(c => c.Key).ToHashSet();
            var cells = new System.Text.StringBuilder();
            if (host is not null)
                foreach (var m in host.Movers.All)
                    if (!held.Contains(m.Key))
                        cells.Append(m.Entity).Append('@').Append(m.Key.SourceIndex).Append(':').Append(m.Key.Offset).Append(grid.IsCovered(m.Cell.X, m.Cell.Y, true, AreaRoofIndoor) ? "+;" : "-;");
            _areaRoofMoverCells = cells.ToString();
        }
        lap?.Step("publish");
        if (grid.Revision != _areaRoofRevision || signature != _areaRoofCompanionCells || _areaRoofMoverCells != _areaRoofPublishedMoverCells)
        {
            _areaRoofCompanionCells = signature; _areaRoofPublishedMoverCells = _areaRoofMoverCells;
            PublishAreaRoofs(area, grid, companions, host);
        }
    }

    private void BindAreaRoofs(AreaLoadResult area)
    {
        _areaRoofArea = area; _areaRoofCell = null; _areaRoofDeparture = null; _areaRoofWalls = new(); _areaRoofRevision = -1; _areaRoofCompanionCells = null; _areaRoofMoverCells = null; _areaRoofPublishedMoverCells = null; _areaRoofMoverStamp = null;
        _areaRoofGrid = area.Plan.Roofs.Count == 0 ? null : new AreaRoofGrid(area.Plan.Roofs.Select(roof => roof.Roof));
        _areaRoofDraws = area.Plan.Roofs.ToDictionary(roof => (roof.Roof.BlockX >> 2, roof.Roof.BlockY >> 2));
        _areaRoofTileCommands = area.Plan.Commands.Where(command => command.Identity is null).GroupBy(command => ((long)command.MapX, (long)command.MapY))
            .ToDictionary(group => group.Key, group => group.ToArray());
        _areaRoofBodies = area.Snapshot.Objects.Where(row => row.WorldX is not null && row.WorldY is not null)
            .GroupBy(row => (row.WorldX!.Value, row.WorldY!.Value)).ToDictionary(group => group.Key, group => group.Select(row => row.Key).ToArray());
        _areaRoofTileArt = area.Snapshot.Terrain.Where(tile => tile.Scope == AreaScope.Core).ToDictionary(tile => (tile.WorldX, tile.WorldY), tile => tile.RawArt);
        _areaRoofObjects = area.Snapshot.Objects.ToDictionary(row => row.Key);
        _canvas?.SetAreaRoofs(null, null, null);
    }

    /// <summary>A reset scene stands the actor on its start as a load does: the roofs start again from the map's stored
    /// state at the next frame.</summary>
    private void ResetAreaRoofs() => _areaRoofArea = null;

    private void StopAreaRoofs()
    {
        _areaRoofGrid = null; _areaRoofArea = null; _areaRoofCell = null; _areaRoofDeparture = null; _areaRoofWalls = new();
        _areaRoofDraws = new(); _areaRoofTileCommands = new(); _areaRoofBodies = new(); _areaRoofTileArt = new(); _areaRoofObjects = new();
    }

    /// <summary>Tile type 0 as the cover test reads it: tile art with bit 8 clear, facade art with bit 25 clear, any other
    /// art (a missing tile included) (RF4).</summary>
    private bool AreaRoofIndoor(long x, long y)
    {
        if (!_areaRoofTileArt.TryGetValue((x, y), out var art)) return true;
        return (art >> 28) switch { 0 => (art & 0x100) == 0, 11 => (art & 0x2000000) == 0, _ => true };
    }

    private void PublishAreaRoofs(AreaLoadResult area, AreaRoofGrid grid, IReadOnlyList<(AreaObjectKey Key, AreaStepCell Cell)> companions, AreaAiHost? host)
    {
        var shades = area.Snapshot.RoofShades ?? throw new InvalidOperationException("AREA_ROOF_SHADES_REQUIRED");
        var roofs = new List<(AreaDrawCommand, Vector4?)>(); var tiles = new List<AreaDrawCommand>(); var bodies = new HashSet<AreaObjectKey>();
        foreach (var draw in area.Plan.Roofs)
        {
            var (fill, fade) = grid.State(draw.Roof);
            if (fill) continue;
            Vector4? corners = null;
            if (fade)
            {
                var (tl, tr, br, bl) = AreaRoofGrid.FadeCorners(draw.Roof.Piece, shades);
                corners = new Vector4(tl, tr, br, bl) / 255f;
            }
            roofs.Add((draw.Command, corners));
            // The tiles this block can cover: those three tiles before it on both axes.
            for (var y = draw.Roof.BlockY - 3; y <= draw.Roof.BlockY; y++)
            for (var x = draw.Roof.BlockX - 3; x <= draw.Roof.BlockX; x++)
            {
                if (grid.IsCovered(x, y, true, AreaRoofIndoor) && _areaRoofBodies.TryGetValue((x, y), out var keys)) bodies.UnionWith(keys);
                if (grid.IsCovered(x, y, false, AreaRoofIndoor) && _areaRoofTileCommands.TryGetValue((x, y), out var terrain)) tiles.AddRange(terrain);
            }
        }
        // A companion is covered where it stands now (ARC-WORLD-037), and so is an NPC the AI host walked or made (ARC-WORLD-2792); a companion's own place wins over a mover entry of the same body.
        var movers = host is null ? [] : host.Movers.All.Where(m => companions.All(c => c.Key != m.Key)).Select(m => (m.Key, (long)m.Cell.X, (long)m.Cell.Y));
        AreaRoofCover.Place(bodies, movers.Concat(companions.Select(c => (c.Key, c.Cell.X, c.Cell.Y))), grid, AreaRoofIndoor);
        _areaRoofRevision = grid.Revision;
        _canvas!.SetAreaRoofs(roofs.AsReadOnly(), tiles.Distinct().ToArray(), bodies);
    }

    /// <summary>The original picks no object where a drawn roof pixel is: the roof block of the tile under the point moved
    /// 120 down, when it is not filled and its frame is opaque there.</summary>
    private bool AreaRoofHit(Vector2 screen)
    {
        if (_areaRoofGrid is not { } grid || _readyArea is not { } area || _canvas is null || _worldActor.Session is not AreaGridSession session) return false;
        var chart = _canvas.GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (AreaCellPicking.Pick(new Vec2(chart.X, chart.Y + 120)) is not { } cell) return false;
        var world = session.Chart.ToWorld(cell);
        if (grid.HitCandidate(world.X, world.Y) is not { } roof || !_areaRoofDraws.TryGetValue((roof.BlockX >> 2, roof.BlockY >> 2), out var draw)) return false;
        var transform = AreaDrawItem.RasterTransform(GetViewport());
        return AreaAlphaAt(draw.Command, area.Frames, transform, (int)Math.Floor(screen.X), (int)Math.Floor(screen.Y)) != 0;
    }

    private string AreaRoofStatus() => _areaRoofGrid is not { } grid ? ""
        : $" | roofs {grid.Count} blocks, {grid.Records.Count(r => grid.State(r).Fill)} hidden, {grid.Records.Count(r => grid.State(r).Fade && !grid.State(r).Fill)} faded";

    // Native observation (ARC-RENDER-022, map mode): next to a roof, outside it, the roof is drawn over the view; a click on
    // an opaque roof pixel over a pickable body picks nothing (checked when the view has one); standing on a tile whose
    // block band holds it fills that roof (it floods through the roof and is not drawn any more).
    private async Task RunAreaRoofSmokeAsync()
    {
        if (_areaRoofGrid is null || _readyArea is not { } area || _worldActor.Session is not AreaGridSession session)
        { GD.Print($"ARC-RENDER-022 native-smoke roofs NONE map={_readyMapName}"); return; }
        var map = new AreaMapRuntime(area.Snapshot, session.Snapshot, session.Chart);
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        // From the spawn, the first reachable tile whose recalculation fills a roof, and the last reachable tile without a
        // roof met before it on the search.
        var start = session.State.CommittedCell;
        var seen = new HashSet<Arcanum.Simulation.Movement.CellCoord> { start }; var queue = new Queue<Arcanum.Simulation.Movement.CellCoord>([start]);
        Arcanum.Simulation.Movement.CellCoord? inside = null, outside = null;
        while (queue.TryDequeue(out var cell) && seen.Count < 60000)
        {
            var world = map.Chart.ToWorld(cell);
            if (_areaRoofGrid.Art(world.X, world.Y) is null) outside = cell;
            else
            {
                var probe = new AreaRoofGrid(area.Plan.Roofs.Select(roof => roof.Roof)); probe.Recalc(world.X, world.Y);
                if (probe.Revision > 0 && outside is not null && Math.Max(Math.Abs(cell.X - outside.Value.X), Math.Abs(cell.Y - outside.Value.Y)) <= 12) { inside = cell; break; }
            }
            foreach (var direction in Enum.GetValues<Arcanum.Simulation.Movement.NeighborDirection>())
            {
                var next = Arcanum.Simulation.Navigation.RoutePlanner.Neighbor(cell, direction);
                if (seen.Contains(next) || !routes.Contains(next) || routes.Assess(cell, direction) != Arcanum.Simulation.Navigation.RouteStepVerdict.Open) continue;
                seen.Add(next); queue.Enqueue(next);
            }
        }
        if (inside is not { } under || outside is not { } open)
        { GD.Print($"ARC-RENDER-022 native-smoke roofs NO-REACHABLE-ROOF map={_readyMapName} searched={seen.Count}"); return; }
        _hud!.Visible = false;
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true; _worldFocused = true; SelectSimulatedPadForSelfTest();
        try
        {
            // An earlier stage may have walked the PC under a roof (ARC-COMBAT-006: the hero's combat-mode run, for one); a placement
            // is a fresh start, so the roofs start again from the map's stored state.
            AreaSmokePlace(map, open); ResetAreaRoofs(); PublishWorldSelection(); _camera!.ResetWorld(WorldAnchor, WorldViewport, 1);
            await RoofFrames();
            var drawnOutside = _canvas!.AreaVisibleRoofCount;
            using var before = GetViewport().GetTexture().GetImage();
            var transform = AreaDrawItem.RasterTransform(GetViewport());
            var roofCommands = _areaRoofDraws.Values.Where(d => !_areaRoofGrid.State(d.Roof).Fill).Select(d => d.Command).ToArray();
            // A roof pixel the hit test accepts, preferably one over a body the pick would find without the roof.
            Vector2? roofPixel = null, overBody = null;
            for (var y = 0; y < before.GetHeight() && overBody is null; y += 3)
            for (var x = 0; x < before.GetWidth() && overBody is null; x += 3)
            {
                var point = new Vector2(x + .5f, y + .5f);
                if (!roofCommands.Any(c => AreaAlphaAt(c, area.Frames, transform, x, y) != 0) || !AreaRoofHit(point)) continue;
                roofPixel ??= point;
                if (AreaObjectAtScreen(point, roofs: false) is not null) overBody = point;
            }
            if (drawnOutside == 0 || roofPixel is null) throw new InvalidOperationException($"AREA_ROOF_NOT_DRAWN drawn={drawnOutside}");
            if (overBody is { } pixel && AreaObjectAtScreen(pixel) is not null) throw new InvalidOperationException("AREA_ROOF_PICKED_THROUGH");
            var hiddenBefore = _areaRoofGrid.Records.Count(r => _areaRoofGrid.State(r).Fill);
            AreaSmokePlace(map, under); PublishWorldSelection();
            var hiddenInside = _areaRoofGrid.Records.Count(r => _areaRoofGrid.State(r).Fill);
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_ROOF_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                _camera.ResetWorld(WorldAnchor, WorldViewport, 1); await RoofFrames();
                using var hidden = GetViewport().GetTexture().GetImage();
                if (before.SavePng(capture + "-outside.png") != Error.Ok || hidden.SavePng(capture + "-inside.png") != Error.Ok)
                    throw new InvalidOperationException("AREA_ROOF_CAPTURE_FAILED");
            }
            if (hiddenInside <= hiddenBefore) throw new InvalidOperationException($"AREA_ROOF_NOT_FILLED before={hiddenBefore} inside={hiddenInside}");
            GD.Print("ARC-RENDER-022 native-smoke roofs PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                Map = _readyMapName, Blocks = _areaRoofGrid.Count, Outside = map.Chart.ToWorld(open), Inside = map.Chart.ToWorld(under),
                DrawnOutside = drawnOutside, HiddenBefore = hiddenBefore, HiddenInside = hiddenInside, PickOverBody = overBody is not null,
                Parity = "NOT_VERIFIED" }));
        }
        finally
        {
            _hud.Visible = true; ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    private async Task RoofFrames()
    {
        for (var frame = 0; frame < 3; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }
}
