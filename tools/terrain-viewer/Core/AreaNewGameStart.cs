using Arcanum.Session;
using Arcanum.Simulation.ChangeOfPlace;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-DEV-115 (MIG11 slice S4, RL29, ledger OS5): the new game's teleport of the hero to the start map's map-list location is the
/// session's change of place. The original's start routine (0x5412E0) fades out itself and then requests the hero's teleport to the type-1
/// map with flags 0x45 (both movies and the fade-in, <see cref="AreaChangeOfPlace.NewGameFlags"/>); the request is for the local player
/// character, so every plan step the session owns runs: the move list, the wall-check reset, the placement, the identity kept. The
/// viewer's new character that went through the opening shop has its session on the shop map, so its plan writes the travellers on the start
/// map with the destination ready at once (the start scene is shown already); a pre-made hero or a new character without the shop is on the
/// start map and the plan is a same-map relocation. The movies, the fades and the pre-cache are not drawn or run (NOT READY: presentation, B16; the pre-cache is TM5's).
/// </summary>
public static class AreaNewGameStart
{
    /// <summary>Runs the start's request on <paramref name="owner"/>. Null on success (the hero stands at <paramref name="start"/> on
    /// <paramref name="startMap"/>), else why the session refused it; a refused request leaves the session on its map (GI7).
    /// <paramref name="facing"/> is the hero's facing on arrival (null keeps its own).</summary>
    public static string? Run(GameSessionOwner owner, SessionMapId startMap, CellCoord start, NeighborDirection? facing = null) =>
        Run(owner, startMap, start, facing, out _);

    /// <summary><see cref="Run(GameSessionOwner, SessionMapId, CellCoord, NeighborDirection?)"/> with the change's report (null when the
    /// request was not accepted): the request, the steps run and how it ended.</summary>
    public static string? Run(GameSessionOwner owner, SessionMapId startMap, CellCoord start, NeighborDirection? facing, out ChangeOfPlaceRun? run)
    {
        ArgumentNullException.ThrowIfNull(owner);
        run = null;
        var submitted = owner.RequestHeroChangeOfPlace(ChangeOfPlaceCaller.NewGame, startMap.RuntimeMap, start, AreaChangeOfPlace.NewGameFlags, 0,
            map => map == startMap.RuntimeMap);
        if (!submitted.Accepted) return $"the start's change of place was not accepted ({submitted.Status} {submitted.Failure})";
        run = owner.ProcessChangeOfPlace(new Host(startMap, start, facing ?? owner.QueryActor().Facing));
        return run.Status == ChangeOfPlaceRunStatus.Completed ? null
            : $"the start's change of place did not complete ({run.Status} {run.Failure} at {run.StoppedAt})";
    }

    // The scene is shown already: the destination is ready at once, the hero arrives on the start cell and nothing else is the viewer's.
    private sealed class Host(SessionMapId startMap, CellCoord start, NeighborDirection facing) : IChangeOfPlaceHost
    {
        public bool DestinationListed(int map) => map == startMap.RuntimeMap;

        public SessionTransferFacts TransferFacts(ChangeOfPlaceRequest request) => new();

        public ChangeOfPlaceReadiness ReadyDestination(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) =>
            ChangeOfPlaceReadiness.Ready;

        public ChangeOfPlaceArrival Arrive(ChangeOfPlaceRequest request, SessionMapId destination, SessionMoveList moveList) => new(start, facing, []);

        public bool Run(ChangeOfPlaceStep step, ChangeOfPlaceRequest request) => true;
    }
}
