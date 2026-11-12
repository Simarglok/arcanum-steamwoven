using Arcanum.Data.Area;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Navigation;
using Arcanum.TerrainViewer.Core;
using Arcanum.World;
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
    private readonly Queue<MapEntry> _mapEntered = new();
    // ARC-DEV-351: a tile the hero entered (a step, a push) or was placed on (an arrival). RequestsRefused: the notification of a same-map change of place runs inside it, while the
    // hero's own request is pending, so the jump point and the tile's teleport ask for nothing (ledger TG9, RL21).
    // ARC-MAGIC-036: TileScriptOnly is a tile the spell Movement component's path walk (0x455350) stepped the hero onto: only its tile script runs (0x4C0980), no jump point and no sector entry.
    private readonly record struct MapEntry(AreaStepCell From, AreaStepCell To, bool RequestsRefused = false, bool TileScriptOnly = false);
    // ARC-DEV-328: the followers that arrived with the actor, whose arrival tile's script has not run yet (entity, home map, the cell).
    private readonly List<(string Entity, int HomeMap, AreaStepCell Cell)> _arrivalTilePending = [];
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
        IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)>? Companions = null, SessionMoveList? MoveList = null,
        bool ChangeOfPlace = false, AreaLoadMessageRoute LoadMessages = AreaLoadMessageRoute.None)
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
    private bool AreaTransferPending => _areaTransitions?.Pending is not null || _mapTransfers?.Pending is not null || _mapHandoff is { Kind: not MapHandoffKind.Stream } || TransitionFadePending;

    private bool IsAreaTrigger(AreaObjectKey key) =>
        _mapTransfers?.Site(key) is not null || _mapTransfers?.ScriptedSite(key) is not null || _areaTransitions?.Triggers().Any(t => t.Trigger.Key == key) == true;

    /// <summary>The session the arriving map adopts: the carried one of a transfer to this map, else null.</summary>
    private GameSessionOwner? AdoptHandoffSession(TerrainViewerArguments arguments)
    {
        if (_mapHandoff is not { } handoff || !string.Equals(handoff.Loading, arguments.AreaMapName, StringComparison.Ordinal)) return null;
        return handoff.Adopted;
    }

    /// <summary>ARC-DEV-351 (RL27, TG9): the open of <paramref name="here"/> takes the change-of-place mark off every object that carries it there and queues the notification (the
    /// script of the tile it was written on) of each readable one for the AI frame, named by its home map when it is not this map's.</summary>
    private void TakeTeleportedMarks(GameSessionOwner session, SessionMapId here)
    {
        var (result, objects) = session.TakeTeleportedMarks(session.Version, here);
        if (result.Disposition == GameSessionDisposition.Rejected) { GD.Print("ARC-DEV-351 teleported marks refused " + result.Failure); return; }
        foreach (var marked in objects)
        {
            // Named by its home map even when that is this map: a notified object is the session's, so its death is the session's to tell (RunNpcTilePending), not the AI host's rows (a former
            // follower whose home cell lies outside the loaded window is no row of the host, but the original loads every mobile object at the open, RL27).
            var home = EntityHomes.HomeOf(session, marked.Entity) ?? here.RuntimeMap;
            _arrivalTilePending.Add((marked.Entity, home, new AreaStepCell(marked.Cell.X, marked.Cell.Y)));
            GD.Print($"ARC-DEV-351 teleported {marked.Entity} on map {here.RuntimeMap} at {marked.Cell.X},{marked.Cell.Y}");
            _teleportedNoticesForSmoke.Add((marked.Entity, here.RuntimeMap, new AreaStepCell(marked.Cell.X, marked.Cell.Y)));
            if (_teleportedNoticesForSmoke.Count > 64) _teleportedNoticesForSmoke.RemoveAt(0);
        }
    }

    private void StartMapTransfers(TerrainViewerArguments arguments, TerrainLoadResult result)
    {
        _mapTransfers = null; _mapEntered.Clear(); _useScriptHost = null; _arrivalTilePending.Clear();
        var area = result.Area!; var steps = result.AreaStepSnapshot!; var chart = area.Plan.Chart;
        var handoff = _mapHandoff; _mapHandoff = null;
        // ARC-DEV-204 (B16): a publish nobody asked for (a reload) drops a hero change that waited for its fade-out.
        if (handoff is null && TransitionFadePending) ClearTransition();
        // ARC-DEV-204 (B16): the map of a transfer is there (or the origin again after a failed load): the clock goes; the black stays until the
        // change has run to its end (the fade-in) or gives the draw back below.
        var transferShown = handoff is { Kind: MapHandoffKind.Transfer };
        if (transferShown) EndTransitionLoad();
        // A carried session that this map did not adopt (another load published meanwhile) is released.
        if (handoff?.Adopted is { } stray && !ReferenceEquals(stray, _areaSession)) { stray.Dispose(); handoff.Released?.Dispose(); handoff = null; }
        // ARC-WORLD-038: on a fallback the loaded session ends here; the running session a quick load replaced ends only once
        // the destination is published with the loaded one (LoadTransferAsync), so a refusal while binding it can fall back.
        if (handoff is { Fallback: true }) handoff.Released?.Dispose();
        // ARC-DEV-351 (TG9): the cell the hero was placed on by a transfer that came here (null: it was not placed by one).
        AreaStepCell? heroArrival = null;
        if (handoff is not null && _areaSession is { } session)
        {
            // The carried session records the actor on the arrival map in one step.
            var arrival = chart.ToWorld(chart.Spawn);
            // ARC-WORLD-037: the companions that follow the PC arrive on the same cell in the same session step.
            var here = new SessionMapId(area.Snapshot.RuntimeMap); var cell = new CellCoord(checked((int)arrival.X), checked((int)arrival.Y));
            // ARC-DEV-113 (MIG11 S2): the session's change of place waits for this map: it writes the travellers here now, or, on the
            // origin loaded again after a failure, stops before anything was written (the session never left the origin).
            var failedBefore = handoff.Fallback;
            var resumed = handoff.ChangeOfPlace ? ResumeMapChange(session, handoff, here, cell) : null;
            var placed = resumed ?? PlaceArrival(session, handoff, here, cell);
            GD.Print("ARC-WORLD-031 transfer arrived " + System.Text.Json.JsonSerializer.Serialize(new {
                Kind = handoff.Kind.ToString(), From = handoff.FromMap, To = area.Snapshot.RuntimeMap, Arrival = arrival, handoff.Fallback,
                Session = placed.Disposition.ToString(), placed.Failure, Revision = session.Version.Revision, ChangeOfPlace = resumed is not null }));
            // ARC-DEV-351 (TG9): the hero is one of the objects the change of place carried (RL22), so the open's notification below runs the arrival tile's script for it too.
            if (handoff is { Kind: MapHandoffKind.Transfer, Fallback: false } && placed.Disposition != GameSessionDisposition.Rejected) heroArrival = new AreaStepCell(cell.X, cell.Y);
            if (placed.Disposition == GameSessionDisposition.Rejected && handoff.Kind == MapHandoffKind.Stream)
                _mapStreamStatus = $"window moved but the session refused the actor's placement ({placed.Failure})";
            else if (placed.Disposition == GameSessionDisposition.Rejected) handoff = handoff with { Failure = handoff.Failure ?? $"session refused the arrival ({placed.Failure})" };
            // ARC-DEV-168 (B35): a load of another map says it read, or failed on a refused arrival, once its map is there (a fallback after
            // the destination did not load was told in the transfer's failure branch).
            if (handoff is { Kind: MapHandoffKind.QuickLoad } loaded && !failedBefore) PostLoadMessages(!loaded.Fallback, loaded.LoadMessages);
            // A quick load asked for the saved cell; the map stands the actor on its start when the cell is not admitted.
            if (handoff is { Kind: MapHandoffKind.QuickLoad, Fallback: false } && arguments.AreaMapArrival is { } asked && (asked.X != arrival.X || asked.Y != arrival.Y))
                SetQuickStatus($"F8 quick loaded, but the saved cell ({asked.X},{asked.Y}) is not admitted here: the actor stands on the map's start ({arrival.X},{arrival.Y})");
        }
        if (transferShown) ReleaseTransition();
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
        _mapTransfers.CritterTileRunner = (placed, critter) => UseScriptHost(area.Snapshot) is { } host
            ? Awarded(host, host.RunPlaced(placed, _areaSession!.Rules.Scripts, _ => "a teleport from a tile an NPC entered is not modelled", critter))
            : new(AreaScriptedUseOutcome.Refused, null, [], null, "no game session");
        // ARC-DEV-351 (B25, GR12, ledger TG9, RL27): the open of a map notifies every object that carries the mark of a change of place to this map (post-load 0x410C50, the
        // Teleported callback 0x43EAC0 → 0x444270, mode 2): a follower that travelled with the hero, and a former follower sent home alone while this map was closed. The notice runs
        // the script of the tile each was written on; the runs wait for the map's AI host (ProcessAreaAi). A window move is not an open. Taken only once the map's transfers exist (they carry the notices' runner); a map whose transitions were not read leaves the marks for its next open.
        if (_areaSession is { } opened && handoff?.Kind != MapHandoffKind.Stream)
            TakeTeleportedMarks(opened, new SessionMapId(area.Snapshot.RuntimeMap));
        var kind = handoff?.Kind == MapHandoffKind.QuickLoad ? "quick load" : "transfer";
        var arrived = handoff is null || handoff.Kind == MapHandoffKind.Stream ? "" : handoff.Fallback ? $"; {kind} to '{handoff.To}' failed ({handoff.Failure}), back where it started"
            : handoff.Kind == MapHandoffKind.Transfer ? $"; arrived from map {handoff.FromMap}" : $"; quick load from map {handoff.FromMap}";
        // ARC-DEV-351 (TG9, RL6): the hero's own arrival is notified like the others'; its jump point and tile script run for it (a transfer from another map: the Teleported event comes 1 ms
        // after the change of place ended, so a transfer it asks for is taken; one from this map (the landing) was made inside the change, where the request is refused).
        if (heroArrival is { } landed) _mapEntered.Enqueue(new(landed, landed, RequestsRefused: handoff!.FromMap == area.Snapshot.RuntimeMap));
        _areaUseStatus = $"map {arguments.AreaMapName}: {_mapTransfers.Triggers.Count} exit objects, {_mapTransfers.TriggerCells.Count} exit tiles{arrived}";
        GD.Print("ARC-WORLD-031 transfers-ready " + System.Text.Json.JsonSerializer.Serialize(new {
            Map = arguments.AreaMapName, area.Snapshot.RuntimeMap, _mapTransfers.Exits, Objects = _mapTransfers.Triggers.Count,
            Tiles = _mapTransfers.TriggerCells.Count, Scripted = _mapTransfers.ScriptedTriggers.Count, _mapTransfers.Unmatched, Destinations = destinations.Count, Issues = transitions.Issues.Count }));
    }

    /// <summary>
    /// A guarded left press on a map-mode trigger object. ARC-DEV-174 (MIG13 S11b): the click is an <see cref="ObjectClickRequest"/>, the hero's use goal 38 of the goal table (<see cref="StartUseClick"/>): its steps test the range,
    /// walk the hero within use range (goal 8) and ask for the use in the last node, which <see cref="ApplyUse"/> applies at the frame's next safe point.
    /// </summary>
    private bool TryAreaMapUse(Vector2 screen)
    {
        if (_mapTransfers is null || _worldActor.Session is not AreaGridSession || AreaObjectAtScreen(screen) is not { } hit
            || (_mapTransfers.Site(hit) is null && _mapTransfers.ScriptedSite(hit) is null)) return false;
        CancelTalkApproach("a use click replaced it"); CancelInventoryApproach("a use click replaced it");
        // An exit map mode cannot take is refused at once, without walking there (a scripted use's script names its exit).
        if (_mapTransfers.Site(hit) is { } site && _mapTransfers.Refusal(site) is { } refusal)
        {
            CancelAreaUseApproach("a refused use click replaced it");
            _areaUseStatus = $"use refused: {AreaUseOutcome.DestinationUnavailable} {refusal}"; RefreshAreaStatus();
            return true;
        }
        return StartUseClick(hit);
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
        // ARC-DEV-298 (B20): the float lines of the run (the ghost of Brego's) over their speakers, floated by the next frame.
        foreach (var line in host.TakeFloats()) _tileScriptFloats.Enqueue((line.Speaker == host.Pc ? HeroFloater : line.Speaker.Code, line.Text));
        NoteScriptCreations(host.TakeCreations());
        return ran;
    }

    /// <summary>
    /// ARC-WORLD-081: the items a run created (or did not, at the Steamwoven limit or a refusal) are logged. ARC-DEV-371 (E1-c3, ledger TG9, OM16): a critter it created is notified of its location at once
    /// (0x442260 ends with the location notification, mode 2, for a type 15 or 16 object, unconditionally), so the script of the tile it stands on runs with it as the triggerer (the AI frame runs the notice
    /// as it runs an NPC's step: after an open conversation, once the AI host holds the critter). The original runs it inside the action, before the script's next one; here it follows the run.
    /// </summary>
    private void NoteScriptCreations(IReadOnlyList<AreaScriptCreation> created)
    {
        foreach (var made in created)
        {
            GD.Print("ARC-WORLD-081 created " + System.Text.Json.JsonSerializer.Serialize(new { made.Prototype, Near = made.Near.Code,
                Disposition = made.Result.Disposition.ToString(), Failure = made.Result.Failure.ToString(), made.Result.Item, made.Critter }));
            if (made.Critter is not { } entity || made.Cell is not { } cell) continue;
            QueueCreationNotice(entity, cell);
        }
    }

    // The creation notices queued, for the smokes (entity, cell).
    private readonly List<(string Entity, AreaStepCell Cell)> _creationNoticesForSelfTest = [];

    /// <summary>
    /// ARC-DEV-402 (TG9): the location notification of one created critter (the creation finish 0x442260 ends with mode 2 of 0x444270 for any type 15 or 16 object), queued as an AI-host mover's step notice is: the script of the
    /// tile it stands on runs with it as the triggerer. Used by every creation path that makes a critter on the shown map (the script action 52, an item that became a critter).
    /// </summary>
    private void QueueCreationNotice(string entity, CellCoord cell)
    {
        _creationNoticesForSelfTest.Add((entity, new(cell.X, cell.Y)));
        if (_creationNoticesForSelfTest.Count > 64) _creationNoticesForSelfTest.RemoveAt(0);
        if (_npcTilePending.Count >= 256) _npcTilePending.Dequeue();
        _npcTilePending.Enqueue((entity, null, new(cell.X, cell.Y)));
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
            npc => AreaAiHostNow()?.OrderFacts(npc) ?? new(), step => AreaAiHostNow()?.ApplyStop(step),
            // ARC-DEV-178: get area on the start map places an object by its location, which needs the world-map data's areas and town maps.
            // The data load runs in the background from the module's bind; a talk that comes before it is done is reported as not ready.
            cell => { AdoptWorldMapData(); return _worldMapData is { Data: { } mapData } ? AreaStartMapAreas.AreaAt(mapData, snapshot.Sectors, cell) : null; },
            // ARC-DEV-231 (MIG13 S14a): a script's walk to (69) and run to (74) start the NPC's goal 7 / 55 in the AI host.
            (npc, cell, run) => AreaAiHostNow()?.ScriptWalk(npc, cell, run) ?? "no AI host",
            // ARC-DEV-233 (S14b): a script's heal fatigue goes through the session's life step.
            HealFatigueByScript,
            // ARC-DEV-233 (S14b): a script's perform animation (62) starts goal 0 or 60 in the AI host.
            (npc, animation) => AreaAiHostNow()?.ScriptAnimate(npc, animation) ?? "no AI host",
            // ARC-DEV-234 (S14c): a script's try to steal 100 coins starts the thief's pickpocket skill goal in the AI host.
            (thief, victim) => AreaAiHostNow()?.ScriptSteal(thief, victim) ?? "no AI host",
            // ARC-DEV-236 (S14e): a script's become prone interrupts the NPC's runs at level 4 and lays it in the fall pose.
            npc => AreaAiHostNow()?.ScriptProne(npc) ?? "no AI host",
            // ARC-DEV-241 (S14f): a script's cast runs the cast host's script entry point; an NPC caster's casting goal starts in the AI host.
            (caster, target, spell, free, unresistable) => caster == Arcanum.Simulation.Ai.AiSchedulerRules.Pc
                ? CastByScriptAsHero(target, spell, free, unresistable) : AreaAiHostNow()?.ScriptCast(caster, target, spell, free, unresistable) ?? "no AI host",
            // ARC-DEV-243 (S14g): a script's damage and heal of hit points go through the cast host's damage step and the session's life step.
            DamageByScript, HealHitPointsByScript,
            // ARC-DEV-261 (GT198): a destroyed object's taking-damage script is asked all the same; the answer cannot change the outcome.
            code => TakingDamageGoesOn(code, null),
            // ARC-DEV-371 (E1-c3): a script's create object of a critter prototype makes a critter; a created critter's row is the session's (the maps' rows hold none).
            ScriptCritterPrototype, entity => CreatedCrittersRow(entity),
            // ARC-DEV-386 (E1-c4): the creation's finish generates the new critter's inventory (queued for the frame, see TakeCreatedInventories).
            CritterMade: entity => QueueCreatedInventory(entity));
        var inventory = _inventoryHost;
        var host = new AreaUseScriptHost(owner, snapshot.RuntimeMap, snapshot.Objects, inventory?.Maps ?? [], content, world)
        {
            // ARC-DEV-316: an item taken on another map runs its use script on the row of its home map, read on demand.
            OtherMapRow = inventory is null ? null : obj => inventory.RowOn(obj.Map, obj.Code),
        };
        // ARC-WORLD-110: a use meets the object's trap first; the walk and the steps meet the same host's traps.
        _areaTraps = new AreaTraps(owner, host, snapshot.Objects, world.Gone, AreaTrapEffectsNow);
        host.Traps = _areaTraps.Use;
        // ARC-DEV-371: creations a replaced host had not handed over yet keep their tile notification when it is the same map's.
        if (_useScriptHost is { } replaced && replaced.Snapshot.RuntimeMap == snapshot.RuntimeMap && ReferenceEquals(replaced.Session, owner)) NoteScriptCreations(replaced.Host.TakeCreations());
        _useScriptHost = (snapshot, owner, content, _inventoryHost, host);
        return host;
    }

    private static CellCoord? LiveCell(AreaGridSession grid)
    {
        var cell = grid.Chart.ToWorld(grid.State.CommittedCell);
        return new CellCoord(checked((int)cell.X), checked((int)cell.Y));
    }

    // ARC-SCRIPT-015: the floating lines the last scripted use printed (shown on the status line).
    /// <summary>ARC-DEV-309 (B21): the lines the last scripted run printed go to the message window with the entry type the script named, as
    /// the original does (ledger IS2, PW1, ML1-ML2); the status line keeps showing them as well. Call once per request, after it ran.</summary>
    private void PostScriptedLines()
    {
        if (_mapTransfers?.LastScripted is not { Printed.Count: > 0 } ran) return;
        foreach (var entry in ScriptLineEntries(ran.Printed)) PostMessage(entry, "script line");
    }

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
            ClearTransition();
            if (_mapTransfers.Clear() is { } dropped) { _areaUseStatus = $"transfer to '{dropped.To.Name}' cancelled by reset/reload"; RefreshAreaStatus(); }
            // A reset puts the actor back on its spawn without entering it, as a load does.
            _mapEntered.Clear();
            return false;
        }
        if (_worldActor.Session is not AreaGridSession session) return false;
        // The original fires a jump point or tile script on every tile the player enters (ledger TR3), in step order; since
        // ARC-SCRIPT-017 every tile script runs through the session, then the script of a sector the step enters.
        // ARC-SCRIPT-046: a conversation a run opened holds the steps still queued for a later frame.
        RunMapEnteredSteps();
        // ARC-DEV-204: a change that waits for its fade-out is the only one: another request met meanwhile is dropped, not queued behind it.
        if (TransitionFadePending) { _mapTransfers.Clear(); return false; }
        if (_mapTransfers.Pending is null) ProcessAreaMapStream(session);
        if (_mapTransfers.Pending is not { } transfer || WorldModal || session.State.Phase != MovementPhase.Settled) return false;
        _mapTransfers.Clear();
        // A map change leaves combat mode, as the source's teleport does (ARC-COMBAT-004).
        LeaveCombatModeForMapChange();
        // ARC-DEV-113 (MIG11 S2): with a session the transfer is the session's change of place, requested and run by this frame's ping.
        if (_areaSession is { } owner) return RequestMapChange(owner, transfer, session);
        if (transfer.SameMap) { MoveOnThisMap(transfer, session); return true; }
        BeginMapLoadTransfer(transfer, session);
        return true;
    }

    /// <summary>
    /// The tiles the hero entered, in step order: each one's tile script (and, for a full entry, jump point and the script of the sector it enters) runs through the map-transfer step. ARC-MAGIC-038 runs it
    /// at once for the tiles an immediate relocation walks, before the hero is placed (the original's order, 0x455350 then 0x43E770).
    /// </summary>
    private void RunMapEnteredSteps()
    {
        if (_mapTransfers is null) return;
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
            if (step.From == step.To) GD.Print($"ARC-DEV-351 hero arrival tile {step.To.X},{step.To.Y} requestsRefused={step.RequestsRefused}");
            foreach (var entered in _mapTransfers.Enter(step.To, step.RequestsRefused, !step.TileScriptOnly)) Note(entered);
            PostScriptedLines();
            // ARC-SCRIPT-046: a run that asked for a conversation opens it (a later one is not opened while it is).
            if (_mapTransfers.LastScripted?.Dialog is { } tileTalk) BeginScriptedConversation(tileTalk);
            if (!step.TileScriptOnly && _mapTransfers.EnterSector(step.From, step.To) is { } sector)
            {
                Note(sector);
                PostScriptedLines();
                if (_mapTransfers.LastScripted?.Dialog is { } sectorTalk) BeginScriptedConversation(sectorTalk);
            }
            if (queued) { StopWorldRoute("ROUTE_REPLACED_BY_TRANSFER"); _mapEntered.Clear(); }
        }
    }

    /// <summary>The actor stands on the same-map arrival of <paramref name="transfer"/> (the session, when there is one, placed it).</summary>
    private void MoveOnThisMap(AreaMapTransfer transfer, AreaGridSession session)
    {
        var map = _mapTransfers!.Map;
        _areaRoofDeparture = session.Chart.ToWorld(session.State.CommittedCell);
        // ARC-WORLD-032: a walk to talk does not survive the move (the actor stands in a new grid session).
        CancelTalkApproach("a transfer moved the actor");
        ConfigureAreaActor(map, map.Chart.ToChart(transfer.Arrival), session.State.Facing);
        _mapEntered.Clear();
        // ARC-DEV-351 (TG9, RL21, RL24): the relocation's notification runs the arrival cell's script for the hero inside the change of place, where its requests are refused.
        _mapEntered.Enqueue(new(transfer.Arrival, transfer.Arrival, RequestsRefused: true));
        _worldFitView = false; _camera!.ResetWorld(WorldAnchor, WorldViewport, _worldPreferredZoom);
        PublishWorldSelection();
        _areaUseStatus = $"moved on this map to ({transfer.Arrival.X},{transfer.Arrival.Y})";
        GD.Print("ARC-WORLD-031 transfer same-map " + System.Text.Json.JsonSerializer.Serialize(new { transfer.FromMap, transfer.Arrival, Trigger = transfer.Site.Trigger.ToString() }));
        RefreshAreaStatus();
    }

    /// <summary>Starts the load of another map. ARC-DEV-113: with <paramref name="moveList"/> it is the session's change of place readying
    /// its destination: the move list is the session's, and the arrival resumes the change (<see cref="ResumeMapChange"/>).</summary>
    private void BeginMapLoadTransfer(AreaMapTransfer transfer, AreaGridSession session, SessionMoveList? moveList = null)
    {
        var origin = _arguments!;
        // ARC-DEV-204 (B16): the old map is closed and the new one opens sector by sector: black with the original's clock until it is there.
        BeginTransitionLoad();
        var leaving = moveList is null ? LeavingCompanions() : (LeavingCompanions(moveList), MoveList: null);
        LeaveFollowersCombatMode(moveList ?? leaving.MoveList);
        _mapHandoff = new(_areaSession, transfer.FromMap, transfer.To.Name, session.State.Facing, origin, session.Chart.ToWorld(session.State.CommittedCell), transfer.To.Name,
            Companions: leaving.Companions, MoveList: leaving.MoveList, ChangeOfPlace: moveList is not null);
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
            ClearTransition();
            _mapHandoff = null; handoff.Session?.Dispose(); handoff.Previous?.Dispose();
            return;
        }
        // The destination did not publish: the origin map is loaded again with the actor where the transfer started.
        var failure = _lastPublishedFailure ?? "not published";
        GD.Print($"ARC-WORLD-031 {handoff.Kind} failed to={handoff.To} failure={failure}; reloading the origin");
        if (handoff.Kind == MapHandoffKind.QuickLoad)
        {
            SetQuickStatus($"F8 quick load FAILED: '{handoff.To}' did not load ({failure}); the running game is kept");
            PostLoadMessages(false, handoff.LoadMessages);
        }
        var origin = handoff.Origin with { AreaMapArrival = new(handoff.OriginCell.X, handoff.OriginCell.Y) };
        _mapHandoff = handoff with { Loading = handoff.Origin.AreaMapName!, Failure = failure };
        _arguments = origin;
        await StartLoadAsync(origin, false);
        if (_mapHandoff is { } failed) { ClearTransition(); _mapHandoff = null; failed.Session?.Dispose(); failed.Previous?.Dispose(); }
    }

    /// <summary>Per advance in map mode: every step the actor started into a tile, in order, for the jump point and tile
    /// script check of the next frame.</summary>
    private void NoteMapEntries(GridAdvance advance)
    {
        if (_mapTransfers is null) return;
        foreach (var evidence in advance.AreaEvidence)
            if ((evidence.Live?.Disposition ?? evidence.Assessment.Disposition) == Arcanum.Data.Traversal.CardinalTraversalDisposition.Open)
                _mapEntered.Enqueue(new(_mapTransfers.Map.Chart.ToWorld(evidence.Key.From), _mapTransfers.Map.Chart.ToWorld(evidence.Key.To)));
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
            var loadScreensBefore = _transitionLoads;
            AreaSmokeClick(await AreaSmokeTriggerPointAsync(command));
            for (var wait = 0; wait < 40 && transfers.Pending is null; wait++) AreaSmokeFrame(); // ARC-DEV-174: the click starts the use goal; its last node asks for the use within a few table steps
            if (transfers.Pending is not { } pending) throw new InvalidOperationException("AREA_MAP_TRANSFER_USE_NOT_ACCEPTED " + _areaUseStatus);
            AreaSmokeFrame();
            if (!pending.SameMap)
            {
                if (_mapHandoff is null && SameMapName(_readyMapName, from)) throw new InvalidOperationException("AREA_MAP_TRANSFER_LOAD_NOT_STARTED " + _areaUseStatus);
                // ARC-DEV-204 (B16): the load screen as the player sees it, while the destination loads (when named).
                await CaptureLoadScreenAsync();
                await (_lastLoad ?? Task.CompletedTask);
            }
            if (!SameMapName(_readyMapName, pending.To.Name) || _worldActor.Session is not AreaGridSession arrived) throw new InvalidOperationException("AREA_MAP_TRANSFER_NOT_PUBLISHED " + _areaUseStatus);
            var at = arrived.Chart.ToWorld(arrived.State.CommittedCell);
            if (at != pending.Arrival) throw new InvalidOperationException($"AREA_MAP_TRANSFER_ARRIVAL {at} {pending.Arrival}");
            if (session is not null && (!ReferenceEquals(_areaSession, session) || _areaSession.QueryActor().Map.RuntimeMap != pending.To.Runtime
                || _areaSession.QueryActor().Cell.X != pending.Arrival.X || _areaSession.QueryActor().Cell.Y != pending.Arrival.Y))
                throw new InvalidOperationException("AREA_MAP_TRANSFER_SESSION_NOT_CARRIED");
            // ARC-DEV-204 (B16, SM13): a change to another map showed the load screen (black with the clock) and the clock is gone with the map there.
            if (!pending.SameMap && (_transitionLoads <= loadScreensBefore || _transitionScreen.Loading))
                throw new InvalidOperationException($"AREA_MAP_TRANSFER_LOAD_SCREEN loads={_transitionLoads - loadScreensBefore} loading={_transitionScreen.Loading}");
            GD.Print($"ARC-DEV-204 load screen loads={_transitionLoads - loadScreensBefore} clockFramesDrawn={_transitionClockFramesDrawn} shade={_transitionScreen.Shade}");
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
            if (_areaSession is not null && !pending.SameMap) RunAreaTransitionFadeSmoke(pending);
            await RunAreaMapJumpSmokeAsync();
            // ARC-DEV-116 (MIG11 S5, B09): only when named, the companion carried here is released and goes home on the fold's forced pass.
            // ARC-DEV-351: the return trip puts the hero back on the origin map, so the quick load below (made for a hero on the destination) is not run after it.
            var returnedHome = false;
            if (MapSmokeStages()?.Contains("follower-home") == true) { SendAreaMapFormerFollowerHome(); returnedHome = await RunFormerFollowerReturnSmokeAsync(); }
            // ARC-WORLD-038: on the destination (after its jump smoke), F8 loads the save's map again and stands the actor on
            // the saved cell.
            if (returnedHome) GD.Print("ARC-WORLD-038 native-smoke quickload SKIPPED (the follower-home return trip brought the hero back to the origin map)");
            else if (sameMapLoad is not null && !pending.SameMap && _areaSession is { } carried)
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

    // ARC-DEV-204 (B16, SM13): a script's fade-and-teleport on the shown map (the flags 0x6: the fade-out and the fade-in), made from the transfer
    // just taken but to the map shown, so it is a relocation onto the arrival cell: the fade-out starts at the request and holds the change
    // for its 2 s (20 smoke frames), the screen is black when it runs, the fade-in takes 2 s more, and nothing else of the hero moves meanwhile.
    private void RunAreaTransitionFadeSmoke(AreaMapTransfer taken)
    {
        static void Require(bool value, string why) { if (value) return; GD.Print("ARC-DEV-204 fade smoke: FAIL " + why); throw new InvalidOperationException("ARC-DEV-204 fade smoke: " + why); }
        for (var i = 0; i < 3; i++) AreaSmokeFrame();
        var owner = _areaSession!; var grid = (AreaGridSession)_worldActor.Session!;
        var transfer = taken with { FromMap = taken.To.Runtime, Fade = new Arcanum.Scripting.Scr.ScrTeleportFade(0, InterfaceSoundIds[0], 1) };
        var soundsBefore = _transitionSoundsPlayed; var moviesBefore = _transitionMoviesSkipped;
        Require(transfer.SameMap && owner.QueryChangeOfPlace() is null && !_transitionScreen.Covering, "a clean start");
        Require(RequestMapChange(owner, transfer, grid), "the request was taken");
        Require(_transitionScreen.FadingOut && TransitionFadePending && AreaTransferPending, "the fade-out started and holds the change");
        Require(owner.QueryChangeOfPlace() is null, "the session has no request while the fade-out runs");
        var held = 0;
        while (TransitionFadePending && held < 40) { AreaSmokeFrame(); held++; }
        Require(!TransitionFadePending && held is >= 19 and <= 21, $"the change ran when the screen was black, after {held} frames (about 20)");
        Require(owner.QueryChangeOfPlace() is null && !owner.ChangeOfPlaceInProgress, "the change ran to its end");
        // The sound plays on the hero at its departure point after the fade-out (SN29); the movie is NOT READY and says so once.
        Require(_audio is null || _transitionSoundsPlayed == soundsBefore + 1, "the teleport sound played on the hero: " + _cueNotice);
        Require(_transitionMoviesSkipped == moviesBefore + 1, "the movie step was met once and not shown");
        Require(_transitionScreen.Shade == AreaTransitionShade.FadingIn, "the fade-in runs after the change: " + _transitionScreen.Shade);
        var fadeIn = 0;
        while (_transitionScreen.Covering && fadeIn < 40) { AreaSmokeFrame(); fadeIn++; }
        Require(!_transitionScreen.Covering && fadeIn is >= 19 and <= 21, $"the fade-in took {fadeIn} frames (about 20)");
        GD.Print($"ARC-DEV-204 native-smoke fade PASS held={held} fadeIn={fadeIn} sounds={_transitionSoundsPlayed} movies={_transitionMoviesSkipped}");
    }

    // ARC-DEV-204 (B16): one drawn frame of the load screen (black with the clock in the top-right corner) to ARC_AREA_LOAD_SCREEN_CAPTURE, when set
    // and the load is still going; the clock's frame number is printed with it.
    private async Task CaptureLoadScreenAsync()
    {
        var path = System.Environment.GetEnvironmentVariable("ARC_AREA_LOAD_SCREEN_CAPTURE");
        if (string.IsNullOrWhiteSpace(path) || !_transitionScreen.Loading) return;
        for (var frame = 0; frame < 2; frame++)
        {
            TickTransitionScreen(400);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        if (!_transitionScreen.Loading) { GD.Print("ARC-DEV-204 load screen capture skipped: the load was over"); return; }
        using var image = GetViewport().GetTexture().GetImage();
        GD.Print($"ARC-DEV-204 load screen capture {(image.SavePng(path) == Error.Ok ? "saved" : "FAILED")} clockFrame={_transitionPanel?.ClockFrame} alpha={_transitionPanel?.VeilAlpha}");
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
