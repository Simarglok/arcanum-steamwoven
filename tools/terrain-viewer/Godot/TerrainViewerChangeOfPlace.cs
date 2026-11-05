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
