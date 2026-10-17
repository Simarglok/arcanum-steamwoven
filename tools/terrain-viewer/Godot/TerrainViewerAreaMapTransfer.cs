using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-WORLD-031: map-to-map transitions in map mode (--area-map). The map's unconditional transitions come from the
// content stack (ARC-DATA-025), so any map works the same way: a use click on a trigger object (within use range,
// walking there first when it is farther), entering a jump point or an unconditional tile script. A transfer on the same
// map moves the actor to the arrival; a transfer to another map loads that map through the ordinary load path and places
// the actor on the authored arrival, carrying the game session over (its actor placement moves to the new map in one
// session step, once the new map is published). When the destination cannot be loaded, the origin map is loaded again
// with the actor where it stood. A reset or reload drops a queued transfer.
public partial class TerrainViewerApp
{
    private AreaMapTransfers? _mapTransfers;
    private MapTransferHandoff? _mapHandoff;
    // Tiles the actor stepped into since the last frame, in order (a frame can complete several steps; ledger TR3 fires on each).
    private readonly Queue<AreaStepCell> _mapEntered = new();
    private string? _lastPublishedFailure;

    /// <summary>A transfer to another map in progress: the session it carries (null on an unlit map without one), the
    /// transfer, the facing, and the origin map and cell to fall back to. <see cref="Loading"/> is the map being loaded:
    /// the destination, or the origin again after the destination failed (<see cref="Failure"/>).</summary>
    private sealed record MapTransferHandoff(GameSessionOwner? Session, AreaMapTransfer Transfer, NeighborDirection Facing,
        TerrainViewerArguments Origin, AreaStepCell OriginCell, string Loading, string? Failure = null)
    {
        public bool Fallback => Failure is not null;
    }

    private bool AreaTransferPending => _areaTransitions?.Pending is not null || _mapTransfers?.Pending is not null || _mapHandoff is not null;

    private bool IsAreaTrigger(AreaObjectKey key) =>
        _mapTransfers?.Site(key) is not null || _areaTransitions?.Triggers().Any(t => t.Trigger.Key == key) == true;

    /// <summary>The session the arriving map adopts: the carried one of a transfer to this map, else null.</summary>
    private GameSessionOwner? AdoptHandoffSession(TerrainViewerArguments arguments)
    {
        if (_mapHandoff is not { } handoff || !string.Equals(handoff.Loading, arguments.AreaMapName, StringComparison.Ordinal)) return null;
        return handoff.Session;
    }

    private void StartMapTransfers(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        _mapTransfers = null; _mapEntered.Clear();
        var area = result.Area!; var steps = result.AreaStepSnapshot!; var chart = area.Plan.Chart;
        var handoff = _mapHandoff; _mapHandoff = null;
        // A carried session that this map did not adopt (another load published meanwhile) is released.
        if (handoff?.Session is { } stray && !ReferenceEquals(stray, _areaSession)) { stray.Dispose(); handoff = null; }
        if (handoff is not null && _areaSession is { } session)
        {
            // The carried session records the actor on the arrival map in one step.
            var arrival = chart.ToWorld(chart.Spawn);
            var placed = session.ApplyChanges(session.Version, new ActorPlacement(new(area.Snapshot.RuntimeMap),
                new(checked((int)arrival.X), checked((int)arrival.Y)), handoff.Facing), []);
            GD.Print("ARC-WORLD-031 transfer arrived " + System.Text.Json.JsonSerializer.Serialize(new {
                From = handoff.Transfer.FromMap, To = area.Snapshot.RuntimeMap, Arrival = arrival, handoff.Fallback,
                Session = placed.Disposition.ToString(), placed.Failure, Revision = session.Version.Revision }));
            if (placed.Disposition == GameSessionDisposition.Rejected) handoff = handoff with { Failure = handoff.Failure ?? $"session refused the arrival ({placed.Failure})" };
        }
        if (result.MapTransitions is not { } transitions || result.MapDestinations is not { } destinations)
        {
            _areaUseStatus = $"map {arguments.AreaMapName}: transitions not read ({result.MapTransitionsFailure ?? "no map list row"})";
            return;
        }
        _mapTransfers = new(new(area.Snapshot, steps, chart), transitions, destinations);
        var arrived = handoff is null ? "" : handoff.Fallback ? $"; transfer to '{handoff.Transfer.To.Name}' failed ({handoff.Failure}), back where it started"
            : $"; arrived from map {handoff.Transfer.FromMap}";
        _areaUseStatus = $"map {arguments.AreaMapName}: {_mapTransfers.Triggers.Count} exit objects, {_mapTransfers.TriggerCells.Count} exit tiles{arrived}";
        GD.Print("ARC-WORLD-031 transfers-ready " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = arguments.AreaMapName, area.Snapshot.RuntimeMap, _mapTransfers.Exits, Objects = _mapTransfers.Triggers.Count,
            Tiles = _mapTransfers.TriggerCells.Count, _mapTransfers.Unmatched, Destinations = destinations.Count, Issues = transitions.Issues.Count }));
    }

    /// <summary>A guarded left press on a map-mode trigger object: one use request, or a walk within use range first.</summary>
    private bool TryAreaMapUse(Vector2 screen)
    {
        if (_mapTransfers is null || _worldActor.Session is not AreaGridSession session || AreaObjectAtScreen(screen) is not { } hit || _mapTransfers.Site(hit) is null) return false;
        var actor = session.Chart.ToWorld(session.State.CommittedCell);
        CancelTalkApproach("a use click replaced it"); CancelInventoryApproach("a use click replaced it");
        // An exit map mode cannot take is refused at once, without walking there.
        if (_mapTransfers.Refusal(_mapTransfers.Site(hit)!) is { } refusal)
        {
            _areaUseAfterRoute = null;
            _areaUseStatus = $"use refused: {AreaUseOutcome.DestinationUnavailable} {refusal}"; RefreshAreaStatus();
            return true;
        }
        var result = _mapTransfers.RequestUse(hit, actor);
        _areaUseAfterRoute = null;
        if (result.Outcome == AreaUseOutcome.Accepted) StopWorldRoute("ROUTE_REPLACED_BY_USE");
        var target = _mapTransfers.Object(hit);
        if (result.Outcome == AreaUseOutcome.TooFar && WorldRoute(session) is { } route
            && ApproachCell(session, target.WorldX!.Value, target.WorldY!.Value, AreaUseRule.UseRange, UsableFrom(hit)) is { } goal)
        {
            StopWorldRoute("ROUTE_REPLACED_BY_USE");
            route.SetTarget(goal, WorldTime); _areaUseAfterRoute = hit;
            _worldRouteDiagnostic = DescribeWorldRoute(route); _worldRouteClick = $"{goal.X},{goal.Y} walk to use";
            _areaUseStatus = $"walking to use: {result.Distance} cells away, goal {goal.X},{goal.Y}";
            RefreshAreaStatus();
            return true;
        }
        _areaUseStatus = DescribeMapUse(result);
        GD.Print("ARC-WORLD-031 use " + System.Text.Json.JsonSerializer.Serialize(new { Outcome = result.Outcome.ToString(), result.Distance, result.Reason, Actor = actor,
            To = result.Site?.DestinationCatalogue, Arrival = result.Site?.Arrival }));
        RefreshAreaStatus();
        return true;
    }

    /// <summary>A cell from which the use rule accepts a use of the trigger (so a walk-to-use ends where the use runs).</summary>
    private Func<CellCoord, bool> UsableFrom(AreaObjectKey trigger)
    {
        var transfers = _mapTransfers!; var row = transfers.Object(trigger);
        return cell => AreaUseRule.Check(transfers.Map, trigger, new(row.WorldX!.Value, row.WorldY!.Value), transfers.Map.Chart.ToWorld(cell)).Outcome == AreaUseOutcome.Accepted;
    }

    private string DescribeMapUse(AreaMapUseResult result) => result.Outcome switch
    {
        AreaUseOutcome.Accepted => _mapTransfers!.Pending is { } pending
            ? $"use accepted: to '{pending.To.Name}' ({pending.To.Catalogue}) at ({pending.Arrival.X},{pending.Arrival.Y})" : "use accepted",
        AreaUseOutcome.TooFar => $"too far: {result.Distance} cells (use range {AreaUseRule.UseRange})",
        _ => $"use refused: {result.Outcome} {result.Reason}",
    };

    /// <summary>Per frame in map mode: drops a transfer on reset or reload, asks a walk-to-use on arrival, fires a jump point
    /// or tile script the actor entered, and applies a pending transfer once the actor is settled. True when the map or the
    /// actor's place changed.</summary>
    private bool ProcessAreaMapTransfer()
    {
        if (_mapTransfers is null || _mapHandoff is not null) return false;
        if (_worldLifecycleSession is not null || _worldResetPending || _worldReloadPending)
        {
            CancelAreaUseApproach("reset/reload"); CancelTalkApproach("reset/reload"); CancelInventoryApproach("reset/reload");
            if (_mapTransfers.Clear() is { } dropped) { _areaUseStatus = $"transfer to '{dropped.To.Name}' cancelled by reset/reload"; RefreshAreaStatus(); }
            // A reset puts the actor back on its spawn without entering it, as a load does.
            _mapEntered.Clear();
            return false;
        }
        if (_worldActor.Session is not AreaGridSession session) return false;
        if (_areaUseAfterRoute is { } key && session.State.Phase == MovementPhase.Settled && ReferenceEquals(_worldRouteSession, session)
            && _worldRoute is { } route && route.Status != RouteFollowStatus.Following)
        {
            _areaUseAfterRoute = null;
            if (route.Status == RouteFollowStatus.Arrived && (!_worldFocused || WorldModal || WorldUiFocus)) _areaUseStatus = "walk to use dropped: input quarantined/modal";
            else if (route.Status == RouteFollowStatus.Arrived)
                _areaUseStatus = DescribeMapUse(_mapTransfers.RequestUse(key, session.Chart.ToWorld(session.State.CommittedCell)));
            else _areaUseStatus = "walk to use stopped: " + (route.StopReason ?? route.Status.ToString());
            RefreshAreaStatus();
        }
        // The original fires a jump point or tile script on every tile the player enters (ledger TR3), in step order.
        while (_mapEntered.TryDequeue(out var cell))
        {
            if (_mapTransfers.Enter(cell) is not { } entered) continue;
            _areaUseStatus = (entered.Site!.Trigger == Arcanum.Data.Maps.MapTransitionTrigger.JumpPoint ? "jump point: " : "tile script: ") + DescribeMapUse(entered);
            GD.Print("ARC-WORLD-031 enter " + System.Text.Json.JsonSerializer.Serialize(new { Trigger = entered.Site.Trigger.ToString(), Cell = cell,
                Outcome = entered.Outcome.ToString(), entered.Reason }));
            RefreshAreaStatus();
            if (entered.Outcome == AreaUseOutcome.Accepted) { StopWorldRoute("ROUTE_REPLACED_BY_TRANSFER"); _mapEntered.Clear(); }
        }
        if (_mapTransfers.Pending is not { } transfer || WorldModal || session.State.Phase != MovementPhase.Settled) return false;
        _mapTransfers.Clear();
        // A map change leaves combat mode, as the source's teleport does (ARC-COMBAT-004).
        LeaveCombatModeForMapChange();
        if (transfer.SameMap)
        {
            var map = _mapTransfers.Map;
            if (_areaSession is { } owner)
            {
                var placed = owner.ApplyChanges(owner.Version, new ActorPlacement(new(map.RuntimeMap),
                    new(checked((int)transfer.Arrival.X), checked((int)transfer.Arrival.Y)), session.State.Facing), []);
                if (placed.Disposition == GameSessionDisposition.Rejected)
                {
                    _areaUseStatus = $"transfer refused by the session ({placed.Failure})"; RefreshAreaStatus();
                    return false;
                }
            }
            ConfigureAreaActor(map, map.Chart.ToChart(transfer.Arrival), session.State.Facing);
            _mapEntered.Clear();
            _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
            PublishWorldSelection();
            _areaUseStatus = $"moved on this map to ({transfer.Arrival.X},{transfer.Arrival.Y})";
            GD.Print("ARC-WORLD-031 transfer same-map " + System.Text.Json.JsonSerializer.Serialize(new { transfer.FromMap, transfer.Arrival, Trigger = transfer.Site.Trigger.ToString() }));
            RefreshAreaStatus();
            return true;
        }
        BeginMapLoadTransfer(transfer, session);
        return true;
    }

    private void BeginMapLoadTransfer(AreaMapTransfer transfer, AreaGridSession session)
    {
        var origin = _arguments!;
        _mapHandoff = new(_areaSession, transfer, session.State.Facing, origin, session.Chart.ToWorld(session.State.CommittedCell), transfer.To.Name);
        var next = origin with { AreaMapName = transfer.To.Name, AreaMapArrival = new(transfer.Arrival.X, transfer.Arrival.Y) };
        _areaUseStatus = $"loading '{transfer.To.Name}' ({transfer.To.Catalogue})";
        GD.Print("ARC-WORLD-031 transfer load " + System.Text.Json.JsonSerializer.Serialize(new { transfer.FromMap, To = transfer.To, transfer.Arrival, Trigger = transfer.Site.Trigger.ToString() }));
        _lastLoad = LoadTransferAsync(next);
    }

    private async Task LoadTransferAsync(TerrainViewerArguments next)
    {
        _arguments = next; _lastPublishedFailure = null;
        await StartLoadAsync(next, false);
        if (_mapHandoff is not { } handoff) return;
        if (_exiting || handoff.Fallback)
        {
            // Exiting, or the origin did not load again either: the carried session ends here.
            if (handoff.Fallback) GD.Print("ARC-WORLD-031 transfer fallback failed " + _lastPublishedFailure);
            _mapHandoff = null; handoff.Session?.Dispose();
            return;
        }
        // The destination did not publish: the origin map is loaded again with the actor where the transfer started.
        var failure = _lastPublishedFailure ?? "not published";
        GD.Print($"ARC-WORLD-031 transfer failed to={handoff.Transfer.To.Name} failure={failure}; reloading the origin");
        var origin = handoff.Origin with { AreaMapArrival = new(handoff.OriginCell.X, handoff.OriginCell.Y) };
        _mapHandoff = handoff with { Loading = handoff.Origin.AreaMapName!, Failure = failure };
        _arguments = origin;
        await StartLoadAsync(origin, false);
        if (_mapHandoff is { } failed) { _mapHandoff = null; failed.Session?.Dispose(); }
    }

    /// <summary>Per advance in map mode: every step the actor started into a tile, in order, for the jump point and tile
    /// script check of the next frame.</summary>
    private void NoteMapEntries(GridAdvance advance)
    {
        if (_mapTransfers is null) return;
        foreach (var evidence in advance.AreaEvidence)
            if ((evidence.Live?.Disposition ?? evidence.Assessment.Disposition) == Arcanum.Data.Traversal.CardinalTraversalDisposition.Open)
                _mapEntered.Enqueue(_mapTransfers.Map.Chart.ToWorld(evidence.Key.To));
    }

    // Native observation (ARC-WORLD-031, map mode, after the resource stages): stand next to an exit object whose
    // destination map mode opens, click an opaque pixel of its sprite through the production ground-click seam, let the
    // frames apply the transfer and, for another map, await its load; the actor stands on the authored arrival and the
    // carried session records it there. Then, when the arrival map has a jump point, walk onto it by a ground click.
    private async Task RunAreaMapTransferSmokeAsync()
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        try
        {
            _worldFocused = true; SelectSimulatedPadForSelfTest();
            if (_mapTransfers is not { } transfers) { GD.Print("ARC-WORLD-031 native-smoke transfer NONE " + _areaUseStatus); return; }
            var exits = transfers.Triggers.Where(key => transfers.Refusal(transfers.Site(key)!) is null).ToArray();
            // Prefer another map, then a move on this one.
            var key = exits.OrderBy(k => transfers.Site(k)!.DestinationRuntime == transfers.Map.RuntimeMap ? 1 : 0)
                .Select(k => (Key: k, Cell: UsableCell(transfers.Map, k))).FirstOrDefault(p => p.Cell is not null);
            var exit = key.Key;
            if (key.Cell is not { } near) { GD.Print($"ARC-WORLD-031 native-smoke transfer NO-EXIT map={_readyMapName} objects={transfers.Triggers.Count} opened={exits.Length}"); await RunAreaMapJumpSmokeAsync(); return; }
            var map = transfers.Map; var row = transfers.Object(exit); var site = transfers.Site(exit)!;
            var from = _readyMapName; var session = _areaSession;
            AreaSmokePlace(map, near);
            var command = _readyArea!.Plan.Commands.Single(c => c.Identity == exit);
            AreaSmokeClick(await AreaSmokeTriggerPointAsync(command));
            if (transfers.Pending is not { } pending) throw new InvalidOperationException("AREA_MAP_TRANSFER_USE_NOT_ACCEPTED " + _areaUseStatus);
            AreaSmokeFrame();
            if (!pending.SameMap)
            {
                if (_mapHandoff is null && _readyMapName == from) throw new InvalidOperationException("AREA_MAP_TRANSFER_LOAD_NOT_STARTED " + _areaUseStatus);
                await (_lastLoad ?? Task.CompletedTask);
            }
            if (_readyMapName != pending.To.Name || _worldActor.Session is not AreaGridSession arrived) throw new InvalidOperationException("AREA_MAP_TRANSFER_NOT_PUBLISHED " + _areaUseStatus);
            var at = arrived.Chart.ToWorld(arrived.State.CommittedCell);
            if (at != pending.Arrival) throw new InvalidOperationException($"AREA_MAP_TRANSFER_ARRIVAL {at} {pending.Arrival}");
            if (session is not null && (!ReferenceEquals(_areaSession, session) || _areaSession.QueryActor().Map.RuntimeMap != pending.To.Runtime
                || _areaSession.QueryActor().Cell.X != pending.Arrival.X || _areaSession.QueryActor().Cell.Y != pending.Arrival.Y))
                throw new InvalidOperationException("AREA_MAP_TRANSFER_SESSION_NOT_CARRIED");
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            if (_canvas!.AreaVisibleCount == 0) throw new InvalidOperationException("AREA_MAP_TRANSFER_NOTHING_DRAWN");
            var capture = System.Environment.GetEnvironmentVariable("ARC_AREA_MAP_TRANSFER_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture))
            {
                using var image = GetViewport().GetTexture().GetImage();
                if (image.SavePng(capture) != Error.Ok) throw new InvalidOperationException("AREA_MAP_TRANSFER_CAPTURE_FAILED");
            }
            GD.Print("ARC-WORLD-031 native-smoke transfer PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                From = from, To = pending.To.Name, pending.To.Catalogue, site.Script, Arrival = pending.Arrival, SameMap = pending.SameMap,
                Session = _areaSession?.Version.Revision, Carried = session is not null, Exits = exits.Length, Visible = _canvas.AreaVisibleCount }));
            await RunAreaMapJumpSmokeAsync();
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
        }
    }

    // On the map shown, a jump point reachable from the actor: a ground click on its tile walks there, and entering it moves
    // the actor to the jump point's arrival.
    private async Task RunAreaMapJumpSmokeAsync()
    {
        if (_mapTransfers is not { } transfers || _worldActor.Session is not AreaGridSession session) return;
        var map = transfers.Map;
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        var start = session.State.CommittedCell;
        var seen = new Dictionary<CellCoord, CellCoord> { [start] = start }; var queue = new Queue<CellCoord>([start]);
        CellCoord? goal = null;
        while (queue.TryDequeue(out var cell) && seen.Count < 50000)
        {
            if (cell != start && transfers.CellSite(map.Chart.ToWorld(cell)) is { Trigger: Arcanum.Data.Maps.MapTransitionTrigger.JumpPoint })
            { goal = cell; break; }
            foreach (var direction in Enum.GetValues<NeighborDirection>())
            {
                var next = RoutePlanner.Neighbor(cell, direction);
                if (seen.ContainsKey(next) || !routes.Contains(next) || routes.Assess(cell, direction) != RouteStepVerdict.Open) continue;
                seen.Add(next, cell); queue.Enqueue(next);
            }
        }
        if (goal is not { } jump) { GD.Print($"ARC-WORLD-031 native-smoke jump NONE map={_readyMapName} jump-cells={transfers.TriggerCells.Count}"); return; }
        // A fresh session on the smoke's test clock, a few open steps before the jump point on the found path (within the
        // click route's reach).
        var near = jump;
        for (var back = 0; back < 6 && seen[near] != near; back++) near = seen[near];
        AreaSmokePlace(map, near);
        session = (AreaGridSession)_worldActor.Session!;
        var site = transfers.CellSite(map.Chart.ToWorld(jump))!;
        if (WorldRoute(session) is not { } route) throw new InvalidOperationException("AREA_MAP_JUMP_NO_ROUTE");
        route.SetTarget(jump, WorldTime);
        var frames = 0;
        while (frames < 600 && _readyMapName is not null && _worldActor.Session is AreaGridSession walking
            && walking.Chart.ToWorld(walking.State.CommittedCell) != new AreaStepCell(site.Arrival.X, site.Arrival.Y)) { AreaSmokeFrame(); frames++; }
        if (_mapHandoff is not null) await (_lastLoad ?? Task.CompletedTask);
        if (_worldActor.Session is not AreaGridSession landed || landed.Chart.ToWorld(landed.State.CommittedCell) != new AreaStepCell(site.Arrival.X, site.Arrival.Y))
        {
            GD.Print($"ARC-WORLD-031 smoke detail jump goal={map.Chart.ToWorld(jump)} arrival={site.Arrival} frames={frames} at={(_worldActor.Session as AreaGridSession)?.State.CommittedCell} route={_worldRouteDiagnostic} status={_areaUseStatus}");
            throw new InvalidOperationException("AREA_MAP_JUMP_NOT_APPLIED");
        }
        GD.Print("ARC-WORLD-031 native-smoke jump PASS " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = _readyMapName, From = map.Chart.ToWorld(jump), site.Arrival, site.DestinationCatalogue, Frames = frames }));
    }

    // A cell reachable from the chart's start from which the use rule accepts a use of the trigger.
    private CellCoord? UsableCell(AreaMapRuntime map, AreaObjectKey trigger)
    {
        var usable = UsableFrom(trigger);
        var routes = new AreaRouteSource(map.Steps, map.Chart);
        var start = map.Chart.Spawn;
        var seen = new HashSet<CellCoord> { start }; var queue = new Queue<CellCoord>([start]);
        while (queue.TryDequeue(out var cell))
        {
            if (usable(cell)) return cell;
            foreach (var direction in Enum.GetValues<NeighborDirection>())
            {
                var next = RoutePlanner.Neighbor(cell, direction);
                if (seen.Contains(next) || !routes.Contains(next) || routes.Assess(cell, direction) != RouteStepVerdict.Open) continue;
                seen.Add(next); queue.Enqueue(next);
            }
        }
        return null;
    }
}
