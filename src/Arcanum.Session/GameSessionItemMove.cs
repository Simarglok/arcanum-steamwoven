using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1230 (UO5, UO6 of ARC-RES-1109): the place a pushed item of the map's data was left on, as the object state of its directory entry: the cell the item lies on now where its data puts it elsewhere (the original writes the item's
/// location field, 2, through the object move 0x43E770, which marks the object modified, so a map-data item keeps it as a difference record of the map's mobile file, PS20, PS21). The value is a cell packed as a dropped item's
/// (<see cref="GameSessionOwner.ItemDroppedValue"/>). No record is the place the map's data holds (its seed cell, its container's spill cell or the cell a critical dropped it on). It is a mobile object field of
/// <see cref="ObjectStateVocabulary"/>; an item the hero or a script placed keeps its cell in its own placed record, which the push rewrites instead.
/// </summary>
public static class SessionItemMove
{
    /// <summary>The object field that holds the cell a pushed item lies on.</summary>
    public const string Property = "item.moved";

    /// <summary>The map change key of the item's moved cell.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);

    /// <summary>The cell a record's value names, or null when the value is none or not a packed cell.</summary>
    public static CellCoord? Decode(long value) => value > 0 ? new CellCoord((int)((value - 1) & 0xFFFFFF), (int)(((value - 1) >> 24) & 0xFFFFFF)) : null;
}

/// <summary>ARC-MAGIC-1230: an item lying on the ground of the hero's map now: the entity, its map and cell, the item type and the weight the spell area test reads (field 0x59, the weight the session keeps for the object, else its seed's).</summary>
public sealed record SessionLyingItem(string Entity, SessionMapId Map, CellCoord Cell, SessionItemType Type, int Weight);

public sealed partial class GameSessionOwner
{
    /// <summary>The cell a pushed map-data item lies on, or null when no push left it elsewhere.</summary>
    internal CellCoord? MovedCell(SessionMapId map, string entity) =>
        _maps.Get(map, SessionItemMove.Key(entity)) is { } value ? SessionItemMove.Decode(value) : null;

    /// <summary>
    /// ARC-MAGIC-1230 (UO2, UO5): the item <paramref name="entity"/> if it lies on the ground of the hero's map now (loose: not in a pack, a holder, a junk pile or in flight, not destroyed), with its cell and its weight; null otherwise. The
    /// ground is the map seed's loose items (with the cell a push left them on), the items of a broken container, the items a critical dropped and the hero's placed items.
    /// </summary>
    public SessionLyingItem? QueryGroundItem(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return LyingItem(entity);
    }

    /// <summary>
    /// ARC-MAGIC-1230: the map-data items of the hero's map that a push left on another cell than their data holds, by entity (the spell area queries place a row there, not on its map cell). Items the hero or a script placed are not here: their
    /// cell is their own record's.
    /// </summary>
    public IReadOnlyDictionary<string, CellCoord> QueryMovedItems()
    {
        ThrowIfDisposed();
        var map = _actor.Map;
        var moved = new Dictionary<string, CellCoord>(StringComparer.Ordinal);
        foreach (var field in _maps.MobileFields())
            if (field.Property == SessionItemMove.Property && field.Map == map && SessionItemMove.Decode(field.Value) is { } cell) moved[field.Entity] = cell;
        foreach (var key in _maps.MapKeys(map))
            if (key.Scope == MapChangeScope.Entity && key.Property == SessionItemMove.Property && key.Entity is { } entity
                && _maps.Get(map, key) is { } value && SessionItemMove.Decode(value) is { } cell) moved[entity] = cell;
        return moved;
    }

    private SessionLyingItem? LyingItem(string entity)
    {
        var map = _actor.Map;
        if (_durability.Destroyed.Contains(entity) || ItemInFlight(entity) || QueryMapInventory(map) is not { } view) return null;
        foreach (var ground in view.Ground)
        {
            if (ground.Item.Entity != entity) continue;
            var facts = ground.Item.Facts;
            // The weight the session tracks for the object comes first (a script's change, the facts the object brought), the seed's otherwise.
            var weight = _inventory.Weights.TryGetValue(entity, out var tracked) ? tracked.Weight : facts.Weight;
            return new(entity, map, ground.Cell, facts.Kind.Type, weight);
        }
        return null;
    }

    /// <summary>
    /// ARC-MAGIC-1230 (UO3, UO5; the object move 0x43E770): puts the item <paramref name="entity"/>, lying on the hero's map, on <paramref name="cell"/>. A placed item (the hero's, a script-made one) takes the cell in its own record; a map-data item
    /// keeps a record of the cell (<see cref="SessionItemMove"/>), whether it lay on its seed cell, in a broken container's spill or where a critical dropped it. Nothing else changes: the move sets no flag and touches no hit points
    /// (the original's move writes the location and notifies, UO4), the item joins no junk pile and schedules no decay. One revision. Refused: an entity or cell the session cannot hold (<see cref="GameSessionFailure.InvalidItem"/>, the cell outside
    /// 0 .. 2^24 − 1), an item that does not lie on the hero's map (<see cref="GameSessionFailure.ItemNotAvailable"/>), the version, the budgets. The same cell is NoChange.
    /// </summary>
    public GameSessionMutationResult MoveGroundItem(GameSessionVersion expected, string entity, CellCoord cell)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity) || !DropCellFits(cell)) return Reject(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (LyingItem(entity) is not { } lying) return Reject(GameSessionFailure.ItemNotAvailable);
        if (lying.Cell == cell) return NoChange();
        if (_inventory.PlacedItem(entity) is { } placed)
        {
            if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
            _inventory.Hero.ReplacePlaced(placed with { Cell = cell });
            Revise();
            return new(GameSessionDisposition.Changed, GameSessionFailure.None, Version);
        }
        return ApplyChanges(Version, null, [new(lying.Map, SessionItemMove.Key(entity), ItemDroppedValue(cell))]);
    }
}
