using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-094 (MIG7c, TM2, GR10): where a map NPC stands after the AI host walked or turned it, as the object state of its directory entry
/// (the field <see cref="SessionNpcPlacement.Property"/>, a mobile object field of <see cref="ObjectStateVocabulary"/>): the map it was recorded on
/// (the map whose data lists the NPC), the world cell and the facing. An NPC with no record stands where its map data puts it.
/// </summary>
public readonly record struct SessionNpcPlace(string Entity, SessionMapId Map, CellCoord World, NeighborDirection Facing);

/// <summary>
/// ARC-DEV-094 (MIG7c): the packing and the key of an NPC's recorded place. The cell and the facing are one value, so a placement is one atomic
/// change (the packing is the one of a companion's <c>npc.cell</c>: <c>x &lt;&lt; 32 | y &lt;&lt; 3 | facing</c>, both axes below 2^28). The
/// property is its own, not <c>npc.cell</c>: that one names the companions the roster stands on a map, and a walking resident is no companion.
/// </summary>
public static class SessionNpcPlacement
{
    /// <summary>The object field that holds a map NPC's place (the cell and facing it was left on).</summary>
    public const string Property = "npc.pos";

    /// <summary>The companions' <c>npc.cell</c> value uses the same packing.</summary>
    public const long Axis = 1L << 28;

    /// <summary>Whether a cell and facing fit the packing.</summary>
    public static bool Fits(CellCoord world, NeighborDirection facing) =>
        world.X >= 0 && world.X < Axis && world.Y >= 0 && world.Y < Axis && Enum.IsDefined(facing);

    public static long Encode(CellCoord world, NeighborDirection facing)
    {
        if (!Fits(world, facing)) throw new ArgumentOutOfRangeException(nameof(world));
        return ((long)world.X << 32) | ((long)world.Y << 3) | (long)facing;
    }

    /// <summary>The cell and facing a value packs, or null for a value outside the packing.</summary>
    public static (CellCoord World, NeighborDirection Facing)? Decode(long value)
    {
        long x = value >> 32, y = (value & 0xFFFFFFFFL) >> 3, facing = value & 7;
        if (value < 0 || x >= Axis || y >= Axis) return null;
        return (new CellCoord((int)x, (int)y), (NeighborDirection)facing);
    }

    /// <summary>The map change key of the NPC's recorded place.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-DEV-094 (MIG7c, GR10): records where the NPC stands now (the AI host's step or turn): the object field of its entry, on the map whose data
    /// lists it. A value already recorded is no change. The place is saved with the session, so a load, a map change or a window move stands the NPC
    /// where it was left; an entity that is not a mobile object (`oid:`) or a cell or facing outside the packing is refused (<see cref="GameSessionFailure.InvalidChangeKey"/>).
    /// </summary>
    public GameSessionMutationResult RecordNpcPlace(GameSessionVersion expected, SessionMapId map, string entity, CellCoord world, NeighborDirection facing)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        if (!SessionValidation.Entity(entity) || !entity.StartsWith(ObjectStateVocabulary.EntityPrefix, StringComparison.Ordinal) || !SessionNpcPlacement.Fits(world, facing)) return Reject(GameSessionFailure.InvalidChangeKey);
        return SetMapChange(expected, map, SessionNpcPlacement.Key(entity), SessionNpcPlacement.Encode(world, facing));
    }

    /// <summary>The place recorded for the NPC, or null when none is (it stands on its map data's cell) or the value is unreadable.</summary>
    public SessionNpcPlace? QueryNpcPlace(string entity)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(entity);
        foreach (var (map, value) in QueryEntityChanges(entity, SessionNpcPlacement.Property))
            return SessionNpcPlacement.Decode(value) is { } placed ? new SessionNpcPlace(entity, map, placed.World, placed.Facing) : null;
        return null;
    }

    /// <summary>The places recorded on one map, by entity (ordinal). A value outside the packing is left out.</summary>
    public IReadOnlyList<SessionNpcPlace> QueryNpcPlaces(SessionMapId map)
    {
        ThrowIfDisposed();
        return QueryMapChanges(map)
            .Where(change => change.Key.Scope == MapChangeScope.Entity && change.Key.Property == SessionNpcPlacement.Property && change.Key.Entity is not null)
            .OrderBy(change => change.Key.Entity, StringComparer.Ordinal)
            .Select(change => (Change: change, Placed: SessionNpcPlacement.Decode(change.Value)))
            .Where(pair => pair.Placed is not null)
            .Select(pair => new SessionNpcPlace(pair.Change.Key.Entity!, map, pair.Placed!.Value.World, pair.Placed.Value.Facing)).ToArray();
    }
}
