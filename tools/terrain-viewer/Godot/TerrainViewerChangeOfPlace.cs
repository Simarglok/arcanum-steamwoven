using Arcanum.Session;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Movement;
using Arcanum.TerrainViewer.Core;
using Godot;

namespace Arcanum.TerrainViewer;

// ARC-DEV-113 (MIG11 slice S2, TM12): the hero's transfers are the session's change of place. A transfer the viewer queued (a jump
// point, a tile or sector script's teleport, a use, the Crash Site cave's endpoints) becomes the session's request once the actor is
// settled, and the same frame's ping (ProcessChangeOfPlace; MIG12 moves it into the frame step) runs its plan: the session advances the
// clock, builds the move list and writes the payload, and the hosts below carry out the viewer's part of each step. The viewer draws no
// fade, plays no transfer sound or movie and has no pre-cache, wall-check or magic-and-technology AI table to reset: those steps answer
// yes and do nothing (FR-WORLD-015). Another map of map mode is readied by loading its scene, so the change waits for the arrival
// (ResumeMapChange) and writes none of its payload when the load fails (GI7; a fade-and-teleport's clock advance on the old map
// stays, as in the original, and the companions' places recorded on leaving stay recorded).
public partial class TerrainViewerApp
{
    // A settled map-mode transfer: the session's request, then this frame's ping. True when the actor moved or a load started.
    private bool RequestMapChange(GameSessionOwner owner, AreaMapTransfer transfer, AreaGridSession grid)
    {
        var transfers = _mapTransfers!;
        var (flags, seconds) = AreaChangeOfPlace.Flags(transfer.Fade);
        var submitted = owner.RequestHeroChangeOfPlace(AreaChangeOfPlace.Caller(transfer.Site.Trigger), transfer.To.Runtime,
            new(checked((int)transfer.Arrival.X), checked((int)transfer.Arrival.Y)), flags, seconds, transfers.Destinations.ContainsKey);
        if (!submitted.Accepted) return RefuseMapChange(transfer, $"{submitted.Status} {submitted.Failure}");
        var run = owner.ProcessChangeOfPlace(new MapChangeHost(this, transfer, grid));
        LogChangeOfPlace(run);
        if (run.Status == ChangeOfPlaceRunStatus.AwaitingDestination) return true;
        if (run.Status != ChangeOfPlaceRunStatus.Completed || !transfer.SameMap) return RefuseMapChange(transfer, $"{run.Status} {run.Failure} at {run.StoppedAt}");
        MoveOnThisMap(transfer, grid);
        return true;
    }

    /// <summary>
    /// ARC-DEV-115 (MIG11 slice S4, RL24, RL29, ledger WT12): the world-map landing is the session's change of place for the start map: the
    /// request is the hero's (caller <see cref="ChangeOfPlaceCaller.WorldMapLanding"/>, blank-screen flag alone), pinged in this frame.
    /// The viewer opens the start map by loading its scene around the landing, as before; the session's part is the move list and the
    /// payload. On the start map (<paramref name="sameMap"/>) the plan is a relocation: the session writes the hero on the landing cell (the
    /// original places it without a passability check, W9) and the followers' wait orders, then the window loads around the landing and
    /// stands the actor and the followers on the cell it admits, as a window move does (CD8's rule; a failed load puts the hero back on its
    /// origin cell and leaves the followers where they were). On another map (a debug teleport) the plan writes the travellers there: the
    /// scene load is the destination readied (CD6) and the arrival resumes the change, as a jump point's does. The sector cache the original
    /// empties before the request (RL24) is not here: the viewer opens none (K1 §3.3), so there is nothing to empty; the session's cache
    /// is emptied by the first host that opens one. The blanking, the pre-cache and the other presentation are NOT READY (B16).
    /// </summary>
    private void RequestWorldMapLanding(GameSessionOwner owner, AreaGridSession grid, TerrainViewerArguments origin, TerrainViewerArguments next,
        string target, int startRuntime, CellCoord landing, bool sameMap)
    {
        var submitted = owner.RequestHeroChangeOfPlace(ChangeOfPlaceCaller.WorldMapLanding, startRuntime, landing, AreaChangeOfPlace.LandingFlags, 0,
            map => map == startRuntime);
        if (!submitted.Accepted) { RefuseLanding($"{submitted.Status} {submitted.Failure}"); return; }
        GD.Print("ARC-DEV-115 landing request " + System.Text.Json.JsonSerializer.Serialize(new { To = startRuntime, Cell = new { landing.X, landing.Y }, Effects = submitted.Effects.ToString(), SameMap = sameMap }));
        var host = new LandingHost(this, grid, origin, next, target, startRuntime);
        var run = owner.ProcessChangeOfPlace(host);
        LogChangeOfPlace(run);
        if (run.Status == ChangeOfPlaceRunStatus.AwaitingDestination) return;
        if (run.Status != ChangeOfPlaceRunStatus.Completed) { RefuseLanding($"{run.Status} {run.Failure} at {run.StoppedAt}"); return; }
        // The start map is the map shown: the session wrote the landing; the window loads around it (the viewer's stand-up, CD8).
        BeginLandingLoad(grid, origin, next, target, host.Leaving, changeOfPlace: false);
    }

    /// <summary>
    /// ARC-DEV-116 (MIG11 slice S5, RL21, FM10, ledger WS6): the ping of an NPC's change of place, once per frame after the AI pump (the original's
    /// teleport ping runs on the frame after the request, so the request a tick made in a pass is carried out outside it). The host first makes the next
    /// request it holds (Steamwoven; <see cref="AreaAiHost.FlushHomecomings"/>); a pending request that is not the hero's runs through
    /// <see cref="HomecomingHost"/>: a former follower on a map that is not its teleport map is moved there (its placement edits, the away change, its
    /// events and links through the session's mover payload) and leaves the shown scene when the group is bound again.
    /// </summary>
    private void ProcessNpcChangeOfPlace(AreaAiHost host)
    {
        if (_areaSession is not { } owner) return;
        host.FlushHomecomings();
        if (owner.ChangeOfPlaceInProgress || owner.QueryChangeOfPlace() is not { IsPlayerCharacter: false } request) return;
        var run = owner.ProcessChangeOfPlace(new HomecomingHost(this));
        LogChangeOfPlace(run);
        if (run.Status != ChangeOfPlaceRunStatus.Completed)
        {
            GD.Print($"ARC-DEV-116 homecoming of {request.Entity} not done: {run.Status} {run.Failure} at {run.StoppedAt}");
            return;
        }
        GD.Print($"ARC-DEV-116 homecoming of {request.Entity} to map {request.DestinationMap} cell {request.Destination.X},{request.Destination.Y}");
        BindAreaCompanions();
        RefreshAreaStatus();
    }

    // Whether the viewer can name a map: the shown one, a map of the transfer table (map mode) or one of the Crash Site's pair.
    private bool MapListedNow(int map) =>
        map == CurrentAreaMap || _mapTransfers?.Destinations.ContainsKey(map) == true || _areaTransitions?.Maps.Contains(map) == true;

    // An NPC's change of place without the hero: the companion of the shown group is carried to its destination in the session (its placement there,
    // its away change); the destination is not opened (CP11) and the hero stays where it is.
    private sealed class HomecomingHost(TerrainViewerApp app) : IChangeOfPlaceHost
    {
        public bool DestinationListed(int map) => app.MapListedNow(map);

        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) => new();

        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) => ChangeOfPlaceReadiness.Ready;

        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            var companion = app._areaCompanions?.Companions.FirstOrDefault(c => c.Entity == request.Entity);
            // A mover the shown group does not hold has no body edits to write: the change stops (no placement is guessed).
            if (companion is null) return new(request.Destination, NeighborDirection.XPlus, null!);
            return new(request.Destination, companion.Grid.State.Facing,
                AreaCompanionGroup.Arrive(companion.Template, new(app.CurrentAreaMap), destination, request.Destination, companion.Grid.State.Facing).ToArray());
        }

        public bool Run(ChangeOfPlaceStep step, ChangeOfPlaceRequest request) => true;
    }

    private void RefuseLanding(string why)
    {
        _worldMapStatus = $"landing refused by the session ({why})";
        GD.Print("ARC-DEV-115 landing refused " + why);
        RefreshAreaStatus();
    }

    // The load of the start map around the landing. With changeOfPlace the session's change waits for it (the arrival resumes it,
    // ResumeMapChange); without, the change already ran on this map and the load stands the actor on the cell the window admits.
    private void BeginLandingLoad(AreaGridSession grid, TerrainViewerArguments origin, TerrainViewerArguments next, string target,
        IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)>? companions, bool changeOfPlace)
    {
        _mapHandoff = new(_areaSession, CurrentAreaMap, target, grid.State.Facing, origin, grid.Chart.ToWorld(grid.State.CommittedCell), target,
            Companions: companions, MoveList: null, ChangeOfPlace: changeOfPlace);
        _lastLoad = LoadTransferAsync(next);
    }

    private bool RefuseMapChange(AreaMapTransfer transfer, string why)
    {
        _areaUseStatus = $"transfer to '{transfer.To.Name}' refused by the session ({why})";
        GD.Print("ARC-DEV-113 change of place refused " + why);
        RefreshAreaStatus();
        return false;
    }

    private static void LogChangeOfPlace(ChangeOfPlaceRun run) =>
        GD.Print("ARC-DEV-113 change of place " + System.Text.Json.JsonSerializer.Serialize(new {
            Status = run.Status.ToString(), Caller = run.Request?.Caller.ToString(), To = run.Request?.DestinationMap, Flags = run.Request?.Flags.ToString(),
            Steps = run.Ran.Select(step => step.ToString()).ToArray(), StoppedAt = run.StoppedAt?.ToString(), Failure = run.Failure.ToString(), run.RestoreDraw }));

    /// <summary>The map published for a change of place resumes it: the travellers are written here when this is its destination (a
    /// published map that is not the request's destination stops the change, and the origin is loaded again). On the origin loaded again
    /// (a failed load) the change stops with none of its payload written and null is returned: the actor is stood on its origin cell as
    /// before (<see cref="PlaceArrival"/>). A destination whose change no longer waits is refused, never placed without the move list.</summary>
    private GameSessionMutationResult? ResumeMapChange(GameSessionOwner session, MapTransferHandoff handoff, SessionMapId here, CellCoord cell)
    {
        var request = session.ChangeOfPlaceInProgress ? session.QueryChangeOfPlace() : null;
        if (handoff.Fallback)
        {
            if (request is not null) LogChangeOfPlace(session.ResumeChangeOfPlace(new MapArrivalHost(handoff, here, cell), ready: false));
            return null;
        }
        if (request is null)
        {
            GD.Print("ARC-DEV-113 change of place refused: no change waits for the arrival");
            return new(GameSessionDisposition.Rejected, GameSessionFailure.ChangeOfPlaceStopped, session.Version);
        }
        var revision = session.Version.Revision;
        var run = session.ResumeChangeOfPlace(new MapArrivalHost(handoff, here, cell), ready: request.DestinationMap == here.RuntimeMap);
        LogChangeOfPlace(run);
        return run.Status == ChangeOfPlaceRunStatus.Completed
            ? new(session.Version.Revision == revision ? GameSessionDisposition.NoChange : GameSessionDisposition.Changed, GameSessionFailure.None, session.Version)
            : new(GameSessionDisposition.Rejected, run.Failure == GameSessionFailure.None ? GameSessionFailure.ChangeOfPlaceStopped : run.Failure, session.Version);
    }

    // A map a load or a window move brings (not a change of place): the carried session records the actor on the arrival map in one step.
    private static GameSessionMutationResult PlaceArrival(GameSessionOwner session, MapTransferHandoff handoff, SessionMapId here, CellCoord cell)
    {
        // ARC-WORLD-037: the companions that follow the PC arrive on the same cell in the same session step.
        var companions = handoff.Fallback || handoff.Companions is not { } leaving ? []
            : leaving.SelectMany(c => AreaCompanionGroup.Arrive(c.Template, new(handoff.FromMap), here, cell, c.Facing)).ToArray();
        // ARC-DEV-073: the move list taken at the request (wait orders, FM6, the travellers' events) goes with the placement.
        return session.ApplyChanges(session.Version, new ActorPlacement(here, cell, handoff.ArrivalFacing), companions,
            handoff.Fallback || handoff.Companions is null ? null : handoff.MoveList);
    }

    /// <summary>The companions that leave this map in the session's move list, with their facing, their placement recorded first.</summary>
    private IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)> LeavingCompanions(SessionMoveList moveList)
    {
        if (_areaCompanions is not { } group || _areaSession is null) return [];
        RecordAreaCompanions("leaving the map");
        return group.Leaving(moveList);
    }

    // The map-mode change from the map shown: a same-map payload stands the companions that travel on the arrival with the hero;
    // another map is readied by loading it.
    private sealed class MapChangeHost(TerrainViewerApp app, AreaMapTransfer transfer, AreaGridSession grid) : IChangeOfPlaceHost
    {
        public bool DestinationListed(int map) => app._mapTransfers?.Destinations.ContainsKey(map) == true;

        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) => app.TransferFacts();

        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            app.BeginMapLoadTransfer(transfer, grid, moveList);
            return ChangeOfPlaceReadiness.Pending;
        }

        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            // ARC-WORLD-037: the companions that follow the PC are moved to the arrival with it.
            var companions = app.LeavingCompanions(moveList).SelectMany(c => AreaCompanionGroup.Arrive(c.Template, destination, destination, request.Destination, c.Facing));
            return new(request.Destination, grid.State.Facing, companions.ToArray());
        }

        // The viewer's part of a same-map change is the actor's new grid session once the run returns (MoveOnThisMap).
        public bool Run(ChangeOfPlaceStep step, ChangeOfPlaceRequest request) => true;
    }

    // ARC-DEV-115: the landing from the shown map. Another map (a debug teleport) is readied by loading the start map's scene; the start
    // map itself takes the relocation, with the followers that travel recorded for the stand-up after the load.
    private sealed class LandingHost(TerrainViewerApp app, AreaGridSession grid, TerrainViewerArguments origin, TerrainViewerArguments next, string target, int startRuntime)
        : IChangeOfPlaceHost
    {
        public IReadOnlyList<(AreaCompanionTemplate Template, NeighborDirection Facing)>? Leaving { get; private set; }

        public bool DestinationListed(int map) => map == startRuntime;

        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) => app.TransferFacts();

        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            app.LeaveFollowersCombatMode(moveList);
            app.BeginLandingLoad(grid, origin, next, target, app.LeavingCompanions(moveList), changeOfPlace: true);
            return ChangeOfPlaceReadiness.Pending;
        }

        // The start map: the hero on the landing cell, the followers stay in their rows until the window stands them (a failed load must
        // not have moved them).
        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            Leaving = app.LeavingCompanions(moveList);
            return new(request.Destination, grid.State.Facing, []);
        }

        public bool Run(ChangeOfPlaceStep step, ChangeOfPlaceRequest request) => true;
    }

    // The arrival of another map in map mode: the load flushed, closed and opened the scene, moved the view and bound the actor; the hero
    // stands on the cell the map admitted for the arrival (its start when the authored one is not admitted) with the companions carried.
    private sealed class MapArrivalHost(MapTransferHandoff handoff, SessionMapId here, CellCoord cell) : IChangeOfPlaceHost
    {
        public bool DestinationListed(int map) => map == here.RuntimeMap;

        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) => new();

        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) =>
            destination == here ? ChangeOfPlaceReadiness.Ready : ChangeOfPlaceReadiness.Failed;

        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            var companions = handoff.Companions is not { } leaving ? []
                : leaving.SelectMany(c => AreaCompanionGroup.Arrive(c.Template, new(handoff.FromMap), here, cell, c.Facing)).ToArray();
            return new(cell, handoff.ArrivalFacing, companions);
        }

        public bool Run(ChangeOfPlaceStep step, ChangeOfPlaceRequest request) => true;
    }

    // The Crash Site's exterior and cave are both loaded: the destination is always ready, the open switches the coordinator's map, and
    // the payload carries the companions that follow (ARC-WORLD-017) and leaves the others where they stand.
    private sealed class CaveChangeHost(TerrainViewerApp app, AreaGridSession grid) : IChangeOfPlaceHost
    {
        public IReadOnlyList<AreaCompanion>? Carried { get; private set; }
        public SessionMoveList? MoveList { get; private set; }

        public bool DestinationListed(int map) => app._areaTransitions?.Maps.Contains(map) == true;

        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) => app.TransferFacts();

        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) =>
            ChangeOfPlaceReadiness.Ready;

        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList)
        {
            MoveList = moveList;
            if (app._areaCompanions is not { } group) return new(request.Destination, grid.State.Facing, []);
            var carry = group.Carry(destination, request.Destination, moveList);
            Carried = carry.Carried;
            return new(request.Destination, grid.State.Facing, carry.Changes);
        }

        // The open is the coordinator's map switch; the scene swap follows once the run returns (ProcessAreaTransition). No step fails, so
        // a change that wrote its payload always runs to its end and the coordinator follows the session.
        public bool Run(ChangeOfPlaceStep step, ChangeOfPlaceRequest request)
        {
            if (step == ChangeOfPlaceStep.OpenDestination) app._areaTransitions?.Complete();
            return true;
        }
    }
}
