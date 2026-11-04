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
    // ARC-RENDER-024: Stream moves the window of a map too large to load whole (the main map) around the walking actor.
    private enum MapHandoffKind { Transfer, QuickLoad, Stream }
    private AreaMapTransfers? _mapTransfers;
    private MapTransferHandoff? _mapHandoff;
    // Tiles the actor stepped into since the last frame, in order (a frame can complete several steps; ledger TR3 fires on each).
    private readonly Queue<(AreaStepCell From, AreaStepCell To)> _mapEntered = new();
    private string? _lastPublishedFailure;

    /// <summary>A load of another map in progress: the session the destination adopts (null on an unlit map without one),
    /// the map it leaves and the destination's name, the facing, and the origin map and cell to fall back to.
    /// <see cref="Loading"/> is the map being loaded: the destination, or the origin again after the destination failed
    /// (<see cref="Failure"/>). ARC-WORLD-038: a quick load to another map carries the loaded session and keeps the running
    /// one as <see cref="Previous"/>, which the origin adopts again on a fallback (with <see cref="OriginFacing"/>), and which
    /// is released only once the destination is published with the loaded session; a transfer carries one session for both.</summary>
    private sealed record MapTransferHandoff(GameSessionOwner? Session, int FromMap, string To, NeighborDirection Facing,
        TerrainViewerArguments Origin, AreaStepCell OriginCell, string Loading, string? Failure = null, GameSessionOwner? Previous = null,
        MapHandoffKind Kind = MapHandoffKind.Transfer, NeighborDirection? OriginFacing = null,
        IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)>? Companions = null, SessionMoveList? MoveList = null)
    {
        public bool Fallback => Failure is not null;
        /// <summary>The facing the actor arrives with on the map being loaded.</summary>
        public NeighborDirection ArrivalFacing => Fallback && OriginFacing is { } origin ? origin : Facing;
        /// <summary>The session the map being loaded adopts.</summary>
        public GameSessionOwner? Adopted => Fallback && Previous is not null ? Previous : Session;
        /// <summary>The session that is released once the map being loaded has adopted <see cref="Adopted"/>.</summary>
        public GameSessionOwner? Released => Previous is null ? null : Fallback ? Session : Previous;
    }

    // A window move (ARC-RENDER-024) loads in the background and leaves the shown map playable.
    private bool AreaTransferPending => _areaTransitions?.Pending is not null || _mapTransfers?.Pending is not null || _mapHandoff is { Kind: not MapHandoffKind.Stream };

    private bool IsAreaTrigger(AreaObjectKey key) =>
        _mapTransfers?.Site(key) is not null || _mapTransfers?.ScriptedSite(key) is not null || _areaTransitions?.Triggers().Any(t => t.Trigger.Key == key) == true;

    /// <summary>The session the arriving map adopts: the carried one of a transfer to this map, else null.</summary>
    private GameSessionOwner? AdoptHandoffSession(TerrainViewerArguments arguments)
    {
        if (_mapHandoff is not { } handoff || !string.Equals(handoff.Loading, arguments.AreaMapName, StringComparison.Ordinal)) return null;
        return handoff.Adopted;
    }

    private void StartMapTransfers(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        _mapTransfers = null; _mapEntered.Clear(); _useScriptHost = null;
        var area = result.Area!; var steps = result.AreaStepSnapshot!; var chart = area.Plan.Chart;
        var handoff = _mapHandoff; _mapHandoff = null;
        // A carried session that this map did not adopt (another load published meanwhile) is released.
        if (handoff?.Adopted is { } stray && !ReferenceEquals(stray, _areaSession)) { stray.Dispose(); handoff.Released?.Dispose(); handoff = null; }
        // ARC-WORLD-038: on a fallback the loaded session ends here; the running session a quick load replaced ends only once
        // the destination is published with the loaded one (LoadTransferAsync), so a refusal while binding it can fall back.
        if (handoff is { Fallback: true }) handoff.Released?.Dispose();
        if (handoff is not null && _areaSession is { } session)
        {
            // The carried session records the actor on the arrival map in one step.
            var arrival = chart.ToWorld(chart.Spawn);
            // ARC-WORLD-037: the companions that follow the PC arrive on the same cell in the same session step.
            var here = new SessionMapId(area.Snapshot.RuntimeMap); var cell = new CellCoord(checked((int)arrival.X), checked((int)arrival.Y));
            var companions = handoff.Fallback || handoff.Companions is not { } leaving ? []
                : leaving.SelectMany(c => AreaCompanionGroup.Arrive(c.Template, new(handoff.FromMap), here, cell, c.Facing)).ToArray();
            // ARC-DEV-073: the move list taken at the request (wait orders, FM6, the travellers' events) goes with the placement.
            var placed = session.ApplyChanges(session.Version, new ActorPlacement(here, cell, handoff.ArrivalFacing), companions,
                handoff.Fallback || handoff.Companions is null ? null : handoff.MoveList);
            GD.Print("ARC-WORLD-031 transfer arrived " + System.Text.Json.JsonSerializer.Serialize(new {
                Kind = handoff.Kind.ToString(), From = handoff.FromMap, To = area.Snapshot.RuntimeMap, Arrival = arrival, handoff.Fallback,
                Session = placed.Disposition.ToString(), placed.Failure, Revision = session.Version.Revision }));
            if (placed.Disposition == GameSessionDisposition.Rejected && handoff.Kind == MapHandoffKind.Stream)
                _mapStreamStatus = $"window moved but the session refused the actor's placement ({placed.Failure})";
            else if (placed.Disposition == GameSessionDisposition.Rejected) handoff = handoff with { Failure = handoff.Failure ?? $"session refused the arrival ({placed.Failure})" };
            // A quick load asked for the saved cell; the map stands the actor on its start when the cell is not admitted.
            if (handoff is { Kind: MapHandoffKind.QuickLoad, Fallback: false } && arguments.AreaMapArrival is { } asked && (asked.X != arrival.X || asked.Y != arrival.Y))
                SetQuickStatus($"F8 quick loaded, but the saved cell ({asked.X},{asked.Y}) is not admitted here: the actor stands on the map's start ({arrival.X},{arrival.Y})");
        }
        if (result.MapTransitions is not { } transitions || result.MapDestinations is not { } destinations)
        {
            _areaUseStatus = $"map {arguments.AreaMapName}: transitions not read ({result.MapTransitionsFailure ?? "no map list row"})";
            return;
        }
        _mapTransfers = new(new(area.Snapshot, steps, chart), transitions, destinations, result.MapTemplateScripts);
        // ARC-SCRIPT-015: a guarded use site runs its use script on the session; the hosts are read at the click.
        _mapTransfers.ScriptRunner = (_, row, refusal) => UseScriptHost(area.Snapshot) is { } host
            ? Trapped(Awarded(host, host.Run(row, _areaSession!.Rules.Scripts, refusal))) : new(AreaScriptedUseOutcome.Refused, null, [], null, "no game session");
        // ARC-SCRIPT-017: every tile and sector script the player enters runs on the session the same way.
        _mapTransfers.PlacedRunner = (placed, refusal) => UseScriptHost(area.Snapshot) is { } host
            ? Awarded(host, host.RunPlaced(placed, _areaSession!.Rules.Scripts, refusal)) : new(AreaScriptedUseOutcome.Refused, null, [], null, "no game session");
        // ARC-SCRIPT-046: a tile script a companion stepped onto runs with it as the triggerer; its teleport is not modelled.
        _mapTransfers.CritterTileRunner = (placed, entity) => UseScriptHost(area.Snapshot) is { } host
            ? Awarded(host, host.RunPlaced(placed, _areaSession!.Rules.Scripts, _ => "a teleport from a tile an NPC entered is not modelled",
                new Arcanum.Scripting.Scr.ScrObjectRef(area.Snapshot.RuntimeMap, entity)))
            : new(AreaScriptedUseOutcome.Refused, null, [], null, "no game session");
        var kind = handoff?.Kind == MapHandoffKind.QuickLoad ? "quick load" : "transfer";
        var arrived = handoff is null || handoff.Kind == MapHandoffKind.Stream ? "" : handoff.Fallback ? $"; {kind} to '{handoff.To}' failed ({handoff.Failure}), back where it started"
            : handoff.Kind == MapHandoffKind.Transfer ? $"; arrived from map {handoff.FromMap}" : $"; quick load from map {handoff.FromMap}";
        _areaUseStatus = $"map {arguments.AreaMapName}: {_mapTransfers.Triggers.Count} exit objects, {_mapTransfers.TriggerCells.Count} exit tiles{arrived}";
        GD.Print("ARC-WORLD-031 transfers-ready " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = arguments.AreaMapName, area.Snapshot.RuntimeMap, _mapTransfers.Exits, Objects = _mapTransfers.Triggers.Count,
            Tiles = _mapTransfers.TriggerCells.Count, Scripted = _mapTransfers.ScriptedTriggers.Count, _mapTransfers.Unmatched, Destinations = destinations.Count, Issues = transitions.Issues.Count }));
    }

    /// <summary>A guarded left press on a map-mode trigger object: one use request, or a walk within use range first.</summary>
    private bool TryAreaMapUse(Vector2 screen)
    {
        if (_mapTransfers is null || _worldActor.Session is not AreaGridSession session || AreaObjectAtScreen(screen) is not { } hit
            || (_mapTransfers.Site(hit) is null && _mapTransfers.ScriptedSite(hit) is null)) return false;
        var actor = session.Chart.ToWorld(session.State.CommittedCell);
        CancelTalkApproach("a use click replaced it"); CancelInventoryApproach("a use click replaced it");
        // An exit map mode cannot take is refused at once, without walking there (a scripted use's script names its exit).
        if (_mapTransfers.Site(hit) is { } site && _mapTransfers.Refusal(site) is { } refusal)
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
            ? $"use accepted: to '{pending.To.Name}' ({pending.To.Catalogue}) at ({pending.Arrival.X},{pending.Arrival.Y})" + ScriptedLines() : "use accepted",
        AreaUseOutcome.TooFar => $"too far: {result.Distance} cells (use range {AreaUseRule.UseRange})",
        AreaUseOutcome.ScriptEnded => "use script: " + result.Reason + ScriptedLines(),
        _ => $"use refused: {result.Outcome} {result.Reason}" + ScriptedLines(),
    };

    // ARC-SCRIPT-017: one use-script host per shown map, session and script content (entry runs come every step).
    private (AreaSnapshot Snapshot, GameSessionOwner Session, Arcanum.Data.Scripts.ScriptContent? Content, AreaInventoryHost? Inventory, AreaUseScriptHost Host)? _useScriptHost;

    // ARC-SCRIPT-019: a run's completed quest levels the followers and shows the level-up messages, as a dialog's does.
    private AreaScriptedUse Awarded(AreaUseScriptHost host, AreaScriptedUse ran)
    {
        foreach (var award in host.TakeAwards()) LevelAreaFollowers(award);
        // ARC-WORLD-081: the items the run created (or did not, at the Steamwoven limit or a refusal) are logged.
        foreach (var created in host.TakeCreations())
            GD.Print("ARC-WORLD-081 created " + System.Text.Json.JsonSerializer.Serialize(new { created.Prototype, Near = created.Near.Code,
                Disposition = created.Result.Disposition.ToString(), Failure = created.Result.Failure.ToString(), created.Result.Item }));
        return ran;
    }

    private AreaUseScriptHost? UseScriptHost(AreaSnapshot snapshot)
    {
        if (_areaSession is not { } owner) return null;
        var content = _conversationContent?.Load.Content;
        if (_useScriptHost is { } cached && ReferenceEquals(cached.Snapshot, snapshot) && ReferenceEquals(cached.Session, owner)
            && ReferenceEquals(cached.Content, content) && ReferenceEquals(cached.Inventory, _inventoryHost)) return cached.Host;
        // ARC-WORLD-081: the world actions read the live actor, the session's removed objects, the module's prototypes and the shown map.
        var world = new AreaScriptWorld(
            () => _plantSmokePcCell ?? (_worldActor.Session is AreaGridSession grid ? LiveCell(grid) : null),
            row => _areaSession is { } now && ObjectTargets()?.Gone(now).Contains(row.Key) == true,
            number => PrototypeItems()?.ScriptItem(number),
            AreaDropMapNow,
            // ARC-WORLD-114: a heartbeat script's kill, checked before the run applies anything.
            KillByScript, CanKillByScript,
            // ARC-SCRIPT-047: the critters in a run's vicinity stand where the AI or the party walked them.
            ScriptCritterCell,
            // ARC-SCRIPT-050: get area reads the shown map's map-list row.
            () => _mapTransfers?.Transitions is { Entry: { } entry } transitions ? (entry.Area, transitions.IsStartMap) : null,
            // ARC-SCRIPT-052: a tile script's stop attacking reads the AI host's facts and interrupts the stopped NPC's run, as an order's stop does.
            npc => AreaAiHostNow()?.OrderFacts(npc) ?? new(), step => AreaAiHostNow()?.ApplyStop(step));
        var host = new AreaUseScriptHost(owner, snapshot.RuntimeMap, snapshot.Objects, _inventoryHost?.Maps ?? [], content, world);
        // ARC-WORLD-110: a use meets the object's trap first; the walk and the steps meet the same host's traps.
        _areaTraps = new AreaTraps(owner, host, snapshot.Objects, world.Gone, AreaTrapEffectsNow);
        host.Traps = _areaTraps.Use;
        _useScriptHost = (snapshot, owner, content, _inventoryHost, host);
        return host;
    }

    private static CellCoord? LiveCell(AreaGridSession grid)
    {
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        return new CellCoord(checked((int)cell.X), checked((int)cell.Y));
    }

    // ARC-SCRIPT-015: the floating lines the last scripted use printed (shown on the status line).
    private string ScriptedLines() => _mapTransfers?.LastScripted is { Lines.Count: > 0 } ran ? " — \"" + string.Join(" / ", ran.Lines) + "\"" : "";

    /// <summary>Per frame in map mode: drops a transfer on reset or reload, asks a walk-to-use on arrival, fires a jump point
    /// or tile script the actor entered, and applies a pending transfer once the actor is settled. True when the map or the
    /// actor's place changed.</summary>
    private bool ProcessAreaMapTransfer()
    {
        // A window move in flight (ARC-RENDER-024) keeps the shown map playable; a transfer it meets supersedes it.
        if (_mapTransfers is null || _mapHandoff is { Kind: not MapHandoffKind.Stream }) return false;
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
        // The original fires a jump point or tile script on every tile the player enters (ledger TR3), in step order; since
        // ARC-SCRIPT-017 every tile script runs through the session, then the script of a sector the step enters.
        // ARC-SCRIPT-046: a conversation a run opened holds the steps still queued for a later frame.
        while (!ConversationOpen && _mapEntered.TryDequeue(out var step))
        {
            var queued = false;
            // The status line shows a transfer, a refused exit and a run that printed; refusals and silent runs are logged
            // only, and nothing replaces an accepted transfer while it is pending. A jump point's line carries the printed
            // lines of the tile script run with it.
            void Note(AreaMapUseResult entered)
            {
                var trigger = entered.Site!.Trigger;
                var shown = entered.Outcome is AreaUseOutcome.Accepted or AreaUseOutcome.DestinationUnavailable
                    || (entered.Outcome == AreaUseOutcome.ScriptEnded && _mapTransfers!.LastScripted is { Lines.Count: > 0 });
                if (shown && !queued && (_mapTransfers!.Pending is null || entered.Outcome == AreaUseOutcome.Accepted))
                {
                    _areaUseStatus = (trigger switch
                    {
                        Arcanum.Data.Maps.MapTransitionTrigger.JumpPoint => "jump point: ",
                        Arcanum.Data.Maps.MapTransitionTrigger.SectorEntry => "sector script: ",
                        _ => "tile script: ",
                    }) + (entered.Outcome == AreaUseOutcome.ScriptEnded ? "ran" + ScriptedLines() : DescribeMapUse(entered));
                    RefreshAreaStatus();
                }
                GD.Print("ARC-WORLD-031 enter " + System.Text.Json.JsonSerializer.Serialize(new { Trigger = trigger.ToString(), Cell = step.To,
                    Outcome = entered.Outcome.ToString(), entered.Reason }));
                queued |= entered.Outcome == AreaUseOutcome.Accepted;
            }
            foreach (var entered in _mapTransfers.Enter(step.To)) Note(entered);
            // ARC-SCRIPT-046: a run that asked for a conversation opens it (a later one is not opened while it is).
            if (_mapTransfers.LastScripted?.Dialog is { } tileTalk) BeginScriptedConversation(tileTalk);
            if (_mapTransfers.EnterSector(step.From, step.To) is { } sector)
            {
                Note(sector);
                if (_mapTransfers.LastScripted?.Dialog is { } sectorTalk) BeginScriptedConversation(sectorTalk);
            }
            if (queued) { StopWorldRoute("ROUTE_REPLACED_BY_TRANSFER"); _mapEntered.Clear(); }
        }
        if (_mapTransfers.Pending is null) ProcessAreaMapStream(session);
        if (_mapTransfers.Pending is not { } transfer || WorldModal || session.State.Phase != MovementPhase.Settled) return false;
        _mapTransfers.Clear();
        // A map change leaves combat mode, as the source's teleport does (ARC-COMBAT-004).
        LeaveCombatModeForMapChange();
        if (transfer.SameMap)
        {
            var map = _mapTransfers.Map;
            if (_areaSession is { } owner)
            {
                // ARC-WORLD-037: the companions that follow the PC are moved to the arrival with it.
                var here = new SessionMapId(map.RuntimeMap); var cell = new CellCoord(checked((int)transfer.Arrival.X), checked((int)transfer.Arrival.Y));
                var leaving = LeavingCompanions();
                var companions = leaving.Companions.SelectMany(c => AreaCompanionGroup.Arrive(c.Template, here, here, cell, c.Facing)).ToArray();
                var placed = owner.ApplyChanges(owner.Version, new ActorPlacement(here, cell, session.State.Facing), companions, leaving.MoveList);
                if (placed.Disposition == GameSessionDisposition.Rejected)
                {
                    _areaUseStatus = $"transfer refused by the session ({placed.Failure})"; RefreshAreaStatus();
                    return false;
                }
            }
            _areaRoofDeparture = session.Chart.ToWorld(session.State.CommittedCell);
            // ARC-WORLD-032: a walk to talk does not survive the move (the actor stands in a new grid session).
            CancelTalkApproach("a transfer moved the actor");
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
        var leaving = LeavingCompanions();
        LeaveFollowersCombatMode(leaving.MoveList);
        _mapHandoff = new(_areaSession, transfer.FromMap, transfer.To.Name, session.State.Facing, origin, session.Chart.ToWorld(session.State.CommittedCell), transfer.To.Name,
            Companions: leaving.Companions, MoveList: leaving.MoveList);
        // The destination opens at the arrival: an opening sector of this map (a window move or --area-map-sector) is not its.
        var next = origin with { AreaMapName = transfer.To.Name, AreaMapArrival = new(transfer.Arrival.X, transfer.Arrival.Y), AreaMapSector = null };
        _areaUseStatus = $"loading '{transfer.To.Name}' ({transfer.To.Catalogue})";
        GD.Print("ARC-WORLD-031 transfer load " + System.Text.Json.JsonSerializer.Serialize(new { transfer.FromMap, To = transfer.To, transfer.Arrival, Trigger = transfer.Site.Trigger.ToString() }));
        _lastLoad = LoadTransferAsync(next);
    }

    private async Task LoadTransferAsync(TerrainViewerArguments next)
    {
        _arguments = next; _lastPublishedFailure = null;
        var started = _mapHandoff;
        await StartLoadAsync(next, false);
        if (_mapHandoff is null && started?.Previous is { } previous)
        {
            // ARC-WORLD-038: the destination consumed the handoff. With the loaded session in place the running one ends;
            // otherwise publishing it failed after the handoff was taken (a binding refused the loaded session), and the
            // origin is loaded again with the running session.
            if (ReferenceEquals(_areaSession, started.Session) && _readyMapName == next.AreaMapName) { previous.Dispose(); return; }
            // Another load published meanwhile and released both sessions (StartMapTransfers): nothing falls back.
            if (_lastPublishedFailure is null) return;
            _mapHandoff = started;
        }
        if (_mapHandoff is not { } handoff) return;
        if (_exiting || handoff.Fallback)
        {
            // Exiting, or the origin did not load again either: the carried sessions end here.
            if (handoff.Fallback) GD.Print("ARC-WORLD-031 transfer fallback failed " + _lastPublishedFailure);
            _mapHandoff = null; handoff.Session?.Dispose(); handoff.Previous?.Dispose();
            return;
        }
        // The destination did not publish: the origin map is loaded again with the actor where the transfer started.
        var failure = _lastPublishedFailure ?? "not published";
        GD.Print($"ARC-WORLD-031 {handoff.Kind} failed to={handoff.To} failure={failure}; reloading the origin");
        if (handoff.Kind == MapHandoffKind.QuickLoad) SetQuickStatus($"F8 quick load FAILED: '{handoff.To}' did not load ({failure}); the running game is kept");
        var origin = handoff.Origin with { AreaMapArrival = new(handoff.OriginCell.X, handoff.OriginCell.Y) };
        _mapHandoff = handoff with { Loading = handoff.Origin.AreaMapName!, Failure = failure };
        _arguments = origin;
        await StartLoadAsync(origin, false);
        if (_mapHandoff is { } failed) { _mapHandoff = null; failed.Session?.Dispose(); failed.Previous?.Dispose(); }
    }

    /// <summary>Per advance in map mode: every step the actor started into a tile, in order, for the jump point and tile
    /// script check of the next frame.</summary>
    private void NoteMapEntries(GridAdvance advance)
    {
        if (_mapTransfers is null) return;
        foreach (var evidence in advance.AreaEvidence)
            if ((evidence.Live?.Disposition ?? evidence.Assessment.Disposition) == Arcanum.Data.Traversal.CardinalTraversalDisposition.Open)
                _mapEntered.Enqueue((_mapTransfers.Map.Chart.ToWorld(evidence.Key.From), _mapTransfers.Map.Chart.ToWorld(evidence.Key.To)));
    }

    // Native observation (ARC-WORLD-031, map mode, after the resource stages): stand next to an exit object whose
    // destination map mode opens, click an opaque pixel of its sprite through the production ground-click seam, let the
    // frames apply the transfer and, for another map, await its load; the actor stands on the authored arrival and the
    // carried session records it there. Then, when the arrival map has a jump point, walk onto it by a ground click.
    // ARC-WORLD-038 (with a session): F7 next to the exit and F8 a step away come back to the saved cell on this map; after
    // a transfer to another map (and its jump smoke), F8 loads the saved map again with the actor on the saved cell. Uses a
    // temporary slot.
    private async Task RunAreaMapTransferSmokeAsync()
    {
        var simulatedFocus = _worldFocusSimulationForSelfTest; var focused = _worldFocused; var testTime = _worldTestTime;
        _worldFocusSimulationForSelfTest = true;
        string? quickDirectory = null;
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
            var from = _readyMapName;
            AreaSmokePlace(map, near);
            // ARC-WORLD-038: F7 next to the exit, a step away, F8 back onto the saved cell under a new session.
            object? sameMapLoad = null;
            if (_areaSession is not null)
            {
                quickDirectory = System.IO.Directory.CreateTempSubdirectory("arc-world-038-").FullName;
                _saveLibraryForSelfTest = new(quickDirectory);
                QuickKey(Key.F7);
                if (_quickSaveStatus?.StartsWith("F7 quick saved", StringComparison.Ordinal) != true) throw new InvalidOperationException("AREA_MAP_QUICK_SAVE " + _quickSaveStatus);
                var away = Enum.GetValues<NeighborDirection>().Select(d => RoutePlanner.Neighbor(near, d))
                    .First(c => map.Chart.Contains(c) && map.Steps.IsAdmitted(map.Chart.ToWorld(c)));
                AreaSmokePlace(map, away);
                if (((AreaGridSession)_worldActor.Session!).State.CommittedCell != away) throw new InvalidOperationException("AREA_MAP_QUICK_SAVE_NOT_MOVED");
                var running = _areaSession;
                for (var i = 0; i < 3; i++) AreaSmokeFrame();
                QuickKey(Key.F8);
                if (_quickSaveStatus?.StartsWith("F8 quick loaded", StringComparison.Ordinal) != true || ReferenceEquals(running, _areaSession)
                    || ((AreaGridSession)_worldActor.Session!).State.CommittedCell != near || !SameMapName(_readyMapName, from))
                    throw new InvalidOperationException("AREA_MAP_QUICK_LOAD_SAME_MAP " + _quickSaveStatus);
                sameMapLoad = new { Map = from, Cell = map.Chart.ToWorld(near), Away = map.Chart.ToWorld(away) };
                for (var i = 0; i < 3; i++) AreaSmokeFrame();
            }
            var session = _areaSession;
            var command = _readyArea!.Plan.Commands.Single(c => c.Identity == exit);
            AreaSmokeClick(await AreaSmokeTriggerPointAsync(command));
            if (transfers.Pending is not { } pending) throw new InvalidOperationException("AREA_MAP_TRANSFER_USE_NOT_ACCEPTED " + _areaUseStatus);
            AreaSmokeFrame();
            if (!pending.SameMap)
            {
                if (_mapHandoff is null && SameMapName(_readyMapName, from)) throw new InvalidOperationException("AREA_MAP_TRANSFER_LOAD_NOT_STARTED " + _areaUseStatus);
                await (_lastLoad ?? Task.CompletedTask);
            }
            if (!SameMapName(_readyMapName, pending.To.Name) || _worldActor.Session is not AreaGridSession arrived) throw new InvalidOperationException("AREA_MAP_TRANSFER_NOT_PUBLISHED " + _areaUseStatus);
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
            ReportAreaMapCarry(from!, pending.SameMap);
            await RunAreaMapJumpSmokeAsync();
            // ARC-WORLD-038: on the destination (after its jump smoke), F8 loads the save's map again and stands the actor on
            // the saved cell.
            if (sameMapLoad is not null && !pending.SameMap && _areaSession is { } carried)
            {
                for (var i = 0; i < 3; i++) AreaSmokeFrame();
                QuickKey(Key.F8);
                if (_mapHandoff is null) throw new InvalidOperationException("AREA_MAP_QUICK_LOAD_NOT_STARTED " + _quickSaveStatus);
                await (_lastLoad ?? Task.CompletedTask);
                // The saved cell in world cells: a window map (ARC-RENDER-024) loads again around the saved cell, on its own chart.
                if (!SameMapName(_readyMapName, from) || _worldActor.Session is not AreaGridSession back || back.Chart.ToWorld(back.State.CommittedCell) != map.Chart.ToWorld(near)
                    || _areaSession is null || ReferenceEquals(_areaSession, carried) || _areaSession.QueryActor().Map.RuntimeMap != map.RuntimeMap
                    || _areaSession.QueryActor().Cell.X != map.Chart.ToWorld(near).X || _areaSession.QueryActor().Cell.Y != map.Chart.ToWorld(near).Y)
                    throw new InvalidOperationException($"AREA_MAP_QUICK_LOAD_OTHER_MAP map={_readyMapName} status={_quickSaveStatus} use={_areaUseStatus}");
                // ARC-WORLD-037: a companion the follow smoke joined comes back with the save.
                if (_smokeCompanion is { } joined && _areaCompanions?.Companions.Any(c => c.Entity == joined) != true)
                    throw new InvalidOperationException("AREA_MAP_QUICK_LOAD_COMPANION_MISSING " + _areaCompanions?.Status());
                GD.Print("ARC-WORLD-038 native-smoke quickload PASS " + System.Text.Json.JsonSerializer.Serialize(new {
                    SameMap = sameMapLoad, OtherMap = new { From = pending.To.Name, To = from, Cell = map.Chart.ToWorld(near) }, Session = _areaSession.Version.Revision,
                    Companions = _areaCompanions?.Companions.Count ?? 0 }));
            }
            else GD.Print($"ARC-WORLD-038 native-smoke quickload {(sameMapLoad is null ? "NONE (no session)" : "SAME-MAP-ONLY " + System.Text.Json.JsonSerializer.Serialize(sameMapLoad))}");
        }
        finally
        {
            ReleaseSimulatedPadForSelfTest(); _worldFocusSimulationForSelfTest = simulatedFocus;
            _worldFocused = focused; RestoreWorldTestTime(testTime);
            _saveLibraryForSelfTest = null; _quickKeys.Reset();
            if (quickDirectory is not null) try { System.IO.Directory.Delete(quickDirectory, recursive: true); } catch (System.IO.IOException) { }
        }
    }

    // Map folder names compare without case: a destination spells its folder as the catalogue does, the command line as
    // the user typed it (ARC-RENDER-024: the main map, Arcanum1-024-fixed, comes back as arcanum1-024-fixed).
    private static bool SameMapName(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

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
            if (cell != start && transfers.CellSite(map.Chart.ToWorld(cell)) is { Trigger: Arcanum.Data.Maps.MapTransitionTrigger.JumpPoint } jumpSite
                && transfers.Refusal(jumpSite) is null)
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
        // ARC-WORLD-037: a follower the follow smoke joined jumps with the PC.
        if (_smokeCompanion is not null)
        {
            for (var frame = 0; frame < 3; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
            ReportAreaMapCarry(_readyMapName!, site.DestinationRuntime == map.RuntimeMap);
        }
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
