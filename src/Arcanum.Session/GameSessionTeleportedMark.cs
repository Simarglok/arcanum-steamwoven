using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-351 (schema 86, ledger TG9, FM1, RL22, RL27): the original's flag OF_TELEPORTED (bit 0x40000000 of the object's flag word, field 0x13), the one
/// mark a change of place to another map writes on every object it carries (the PC, its followers, an NPC sent away alone, 0x4D39A0 writes it on the copy
/// it appends to the destination's <c>mobile.mdy</c>), as the object state of its directory entry: the field <see cref="Property"/>, a mobile object field
/// of <see cref="ObjectStateVocabulary"/>. The map the record names is the map the object stands on; the value is the placement it was written on
/// (<see cref="SessionNpcPlacement.Encode"/>: the world cell and the facing), because the notification the open of that map makes runs the script of the
/// tile the object was written on. The map's open takes the mark off every object that holds it, and each gets one notification
/// (post-load 0x410C50, a Teleported event 1 ms later, the callback 0x43EAC0 → 0x444270 with mode 2). No record is the flag clear.
/// </summary>
public static class SessionTeleportedMark
{
    /// <summary>The object field that holds the mark.</summary>
    public const string Property = "obj.teleported";

    /// <summary>The map change key of the object's mark.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}

/// <summary>An object that carries the mark on a map: its entity and the placement it was written on.</summary>
public readonly record struct SessionTeleportedObject(string Entity, CellCoord Cell, NeighborDirection Facing);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-DEV-351 (RL27, TG9): the objects that carry the mark and stand on <paramref name="map"/>, by entity (ordinal), each with the placement the mark
    /// holds. A mark whose value cannot be read as a placement is left out (it is still taken off by <see cref="TakeTeleportedMarks"/>). A read only.
    /// </summary>
    public IReadOnlyList<SessionTeleportedObject> QueryTeleportedMarks(SessionMapId map)
    {
        ThrowIfDisposed();
        return MarksOn(map).Where(mark => mark.Cell is not null).Select(mark => new SessionTeleportedObject(mark.Entity, mark.Cell!.Value, mark.Facing)).ToArray();
    }

    /// <summary>
    /// ARC-DEV-351 (RL27, TG9): the open of <paramref name="map"/> takes the mark off every object that carries it there, in one revision, and returns those it
    /// could read (the objects the open notifies, in entity order). Nothing is written when no object carries a mark on the map
    /// (<see cref="GameSessionDisposition.NoChange"/>); the returned list is empty then. A stale version or a full revision counter is refused with nothing taken.
    /// </summary>
    public (GameSessionMutationResult Result, IReadOnlyList<SessionTeleportedObject> Objects) TakeTeleportedMarks(GameSessionVersion expected, SessionMapId map)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map)) return (Reject(GameSessionFailure.InvalidMap), []);
        var marks = MarksOn(map);
        if (marks.Length == 0) return (IsCurrent(expected) ? NoChange() : Reject(GameSessionFailure.StaleVersion), []);
        var result = ApplyChanges(expected, null, marks.Select(mark => new MapChangeEdit(map, SessionTeleportedMark.Key(mark.Entity), null)).ToArray());
        if (result.Disposition == GameSessionDisposition.Rejected) return (result, []);
        return (result, marks.Where(mark => mark.Cell is not null).Select(mark => new SessionTeleportedObject(mark.Entity, mark.Cell!.Value, mark.Facing)).ToArray());
    }

    private (string Entity, CellCoord? Cell, NeighborDirection Facing)[] MarksOn(SessionMapId map) =>
        _maps.ForMap(map).Where(change => change.Key.Property == SessionTeleportedMark.Property && ObjectStateVocabulary.Mobile(change.Key))
            .Select(change => (Entity: change.Key.Entity!, Placed: SessionNpcPlacement.Decode(change.Value)))
            .Select(mark => (mark.Entity, Cell: mark.Placed is { } placed ? (CellCoord?)placed.World : null, Facing: mark.Placed?.Facing ?? default))
            .OrderBy(mark => mark.Entity, StringComparer.Ordinal).ToArray();
}
