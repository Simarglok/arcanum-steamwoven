using Arcanum.Data.Area;
using Arcanum.Session;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

public enum MapResidentRowsStatus
{
    /// <summary>The rows were built.</summary>
    Ready,
    /// <summary>The map is a window of its map (the main world map): a window's rows are not the whole map's mobile objects (K1 §3.2).</summary>
    WindowOnly,
    /// <summary>The inventory belongs to another map than the snapshot or a row.</summary>
    MapMismatch,
    /// <summary>A mobile row the residency cannot represent (see the detail): a type outside the original's, a PC record, an object on the ground
    /// with no location.</summary>
    Refused,
}

/// <summary>What <see cref="MapResidentRows.Build(AreaInventoryMap, IEnumerable{AreaObjectRecord})"/> made: the rows, or why not (no exception for an
/// ordinary failure). <see cref="StaticRows"/> counts the mobile-file rows of a sector type without OF_DYNAMIC, which the original's static test calls
/// static (RL1) and which are left out.</summary>
public sealed record MapResidentRowsBuild(MapResidentRowsStatus Status, IReadOnlyList<SessionResidentRow>? Rows, string Detail, int StaticRows = 0);

/// <summary>
/// ARC-DEV-103 (MIG9 S3, K1 §3.2): the base rows of a map's residency, read by World from the map's object rows and pushed into the session (which
/// cannot reference World): every object of the map's mobile files (RL1: NPCs, containers, items, projectiles, and walls, portals, scenery and traps
/// with OF_DYNAMIC), each with its entity (the inventory's <c>oid:</c> identity), its type, its OF_OFF, OF_DESTROYED and OF_DYNAMIC flags, and its cell
/// on the ground or the holder whose list holds it (an OF_INVENTORY item no holder lists is detached: no place). The holders come from the map's
/// inventory seed, so the rows and the seed agree. A window of the main map is refused: its rows are the window's, not the map's (NOT READY: the main
/// map's whole mobile read). Derived data; no Godot.
/// </summary>
public static class MapResidentRows
{
    private const int ObjectFlagsSlot = 18;
    private const uint DestroyedFlag = 0x1, OffFlag = 0x2, InventoryFlag = 0x1000, DynamicFlag = 0x2000;

    /// <summary>The rows of a loaded map: its snapshot's object rows and its inventory (the same load).</summary>
    public static MapResidentRowsBuild Build(AreaSnapshot snapshot, AreaInventoryMap inventory)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(inventory);
        if (snapshot.Window is not null) return new(MapResidentRowsStatus.WindowOnly, null, "the snapshot is a window of its map");
        if (snapshot.RuntimeMap != inventory.RuntimeMap) return new(MapResidentRowsStatus.MapMismatch, null, $"snapshot map {snapshot.RuntimeMap}, inventory map {inventory.RuntimeMap}");
        return Build(inventory, snapshot.Objects);
    }

    /// <summary>The rows from a map's object rows (the snapshot's, statics included: only mobile-file rows are read) and its inventory seed.</summary>
    public static MapResidentRowsBuild Build(AreaInventoryMap inventory, IEnumerable<AreaObjectRecord> objects)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(objects);
        if (inventory.Seed.Window) return new(MapResidentRowsStatus.WindowOnly, null, "the inventory is a window of its map");
        var holderOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var holder in inventory.Seed.Holders)
            foreach (var item in holder.Items) holderOf.TryAdd(item, holder.Entity);
        var rows = new List<SessionResidentRow>();
        var statics = 0;
        foreach (var row in objects)
        {
            if (row.Key.Kind != AreaSourceKind.Mobile) continue;
            if (row.Key.RuntimeMap != inventory.RuntimeMap) return new(MapResidentRowsStatus.MapMismatch, null, $"a row of map {row.Key.RuntimeMap}, inventory map {inventory.RuntimeMap}");
            var entity = "oid:" + row.Key.ObjectIdSha256;
            if (row.Type is < 0 or > 17) return Refused(entity, $"type {row.Type}");
            var kind = (SessionResidentKind)row.Type;
            if (kind == SessionResidentKind.Pc) return Refused(entity, "a PC record");
            var flags = unchecked((uint)(row.EffectiveFields.FirstOrDefault(field => field.Slot == ObjectFlagsSlot)?.Number ?? 0));
            var dynamic = (flags & DynamicFlag) != 0;
            if (kind is SessionResidentKind.Wall or SessionResidentKind.Portal or SessionResidentKind.Scenery or SessionResidentKind.Trap && !dynamic) { statics++; continue; }
            CellCoord? cell = null;
            string? holder = null;
            if ((flags & InventoryFlag) != 0) holder = holderOf.GetValueOrDefault(entity);
            else if (row.WorldX is { } x && row.WorldY is { } y && x is >= 0 and < SessionNpcPlacement.Axis && y is >= 0 and < SessionNpcPlacement.Axis) cell = new((int)x, (int)y);
            else return Refused(entity, "on the ground with no location the session holds");
            rows.Add(new(entity, kind, cell, holder) { Dynamic = dynamic, Off = (flags & OffFlag) != 0, Destroyed = (flags & DestroyedFlag) != 0 });
        }
        return new(MapResidentRowsStatus.Ready, rows.AsReadOnly(), $"{rows.Count} mobile rows" + (statics > 0 ? $", {statics} static rows left out" : ""), statics);

        static MapResidentRowsBuild Refused(string entity, string why) => new(MapResidentRowsStatus.Refused, null, $"{entity}: {why}");
    }

    /// <summary>
    /// ARC-DEV-103: opens the session's residency of the loaded map from <see cref="Build(AreaSnapshot, AreaInventoryMap)"/>. A build that is not
    /// ready opens nothing and answers <see cref="ResidencyStatus.InvalidRows"/> with the build's reason. No caller yet: the viewer opens a residency
    /// from MIG9 S4 on (K1 §5).
    /// </summary>
    public static ResidencyOpenResult Open(GameSessionOwner session, AreaSnapshot snapshot, AreaInventoryMap inventory, ResidencyLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var build = Build(snapshot, inventory);
        return Open(session, new(inventory.RuntimeMap), build, limits);
    }

    /// <summary>ARC-DEV-103: opens the residency of <paramref name="map"/> from a build (a build that is not ready opens nothing).</summary>
    public static ResidencyOpenResult Open(GameSessionOwner session, SessionMapId map, MapResidentRowsBuild build, ResidencyLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(build);
        return build.Rows is { } rows && build.Status == MapResidentRowsStatus.Ready
            ? session.OpenResidency(map, rows, limits)
            : new(ResidencyStatus.InvalidRows, 0, $"{build.Status}: {build.Detail}");
    }
}
