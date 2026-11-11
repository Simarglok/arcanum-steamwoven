using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>ARC-WORLD-125: the use slot (attachment point 1) a script trap device carries and the placement copies: the script number, its 32 local flags and its four counters.</summary>
public readonly record struct SessionTrapScript(int Script, uint LocalFlags, uint Counters);

/// <summary>
/// The outcome of <see cref="GameSessionOwner.PlaceScriptTrapDevice"/>: the marker made on a tile (null on an object) or the target object armed, or the refusal.
/// </summary>
public sealed record SessionScriptTrapPlacement(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCreatedObject? Marker, string? Target, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>The map-change properties of a use slot's parts (the use point's <c>use.*</c> keys the objects' script writes use) and the trap state's.</summary>
    private const string UseScriptProperty = "use.script", UseFlagsProperty = "use.lf", UseCountersProperty = "use.counters";

    /// <summary>The bits <c>trap.state</c> writes for a trap the hero placed: spotted (1, the mark 0x4BC090 sets) and placed by the PC (4, object flag 0x1000000).</summary>
    private const long ScriptTrapPlacedBits = TrapSpottedBit | TrapPlacedBit;

    /// <summary>
    /// ARC-WORLD-125 (E6-c; 0x4BC2E0 object form, 0x4BC480 tile form; ledger IA13, TP2): the hero's trap device <paramref name="device"/> whose slot 1 is empty arms a script trap. In one revision: the device's use
    /// slot <paramref name="script"/> is written onto the target <paramref name="target"/> (a portal or a container; the host checked it holds no script, <see cref="TrapDeviceRules.ScriptOnObject"/>) or, for a tile,
    /// onto a marker of <paramref name="markerPrototype"/> made at <paramref name="cell"/> as a created scenery (the host checked the tile holds no trap object, <see cref="TrapDeviceRules.ScriptOnTile"/>); the object
    /// that carries the script gets the PC-placed and spotted marks (<see cref="TrapStateProperty"/> bits 4 and 1, which also clear an earlier removal), and the device is destroyed out of the pack. Exactly one of
    /// <paramref name="target"/> and a nonzero <paramref name="markerPrototype"/> names the form. Refused before any effect, the device kept: a stale version, an invalid form or script (a number outside the catalogue,
    /// 0: nothing a trap could run), a device the hero does not carry unworn or that is no generic item, a map or cell out of range, the created-object, durability or map-change budget, the revision bound.
    /// </summary>
    public SessionScriptTrapPlacement PlaceScriptTrapDevice(GameSessionVersion expected, string device, SessionMapId map, CellCoord cell, string? target, SessionTrapScript script, int markerPrototype)
    {
        ThrowIfDisposed();
        SessionScriptTrapPlacement Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, null, Version);
        if (!SessionValidation.Entity(device)) return Reject(GameSessionFailure.InvalidItem);
        if (target is not null && (!SessionValidation.Entity(target) || target == device || target == SessionEventKinds.AbsentRole) || target is null == (markerPrototype == 0) ||
            script.Script is < 1 or > Arcanum.Data.Scripts.ScrCatalogue.MaxScriptNumber || !SessionValidation.Map(map) || !DropCellFits(cell)) return Reject(GameSessionFailure.InvalidTrap);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (HeldKind(device) is not { } kind || _equipment.SlotOf(device) is not null) return Reject(GameSessionFailure.ItemNotAvailable);
        if (kind.Type != SessionItemType.Generic) return Reject(GameSessionFailure.InventoryMismatch);
        if (markerPrototype != 0 && CreateObjectRefusal(SessionCreatedKind.Scenery, markerPrototype, map, cell, 0, 0) is { } refused) return Reject(refused);
        if (NewDurabilityRecord(device, destroyed: true) && _durability.Count >= _limits.MaxDurabilityRecords) return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        var carrier = target ?? NextObjectEntity(SessionObjectOrigin.Scenery)!;
        var effective = EffectiveChanges(ScriptTrapEdits(map, carrier, script));
        if (!ChangesFit(effective)) return Reject(GameSessionFailure.BudgetExceeded);
        DestroyObject(device);
        SessionCreatedObject? marker = null;
        if (markerPrototype != 0) marker = CreateCreatedObject(SessionCreatedKind.Scenery, markerPrototype, map, cell, 0, 0, _clock.QueryClockSnapshot().GameTimestampMs);
        System.Diagnostics.Debug.Assert(marker is null || marker.Entity == carrier, "the predicted marker entity is the allocated one");
        foreach (var change in effective) _maps.Set(change.Map, change.Key, change.Value!.Value);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, marker, target, Version);
    }

    // The script copied onto the carrier (0x4078A0 on attachment point 1) and its trap marks (0x43D0E0 with 0x1000000, then 0x4BC090): every write is a value, so none is dropped as a no-op.
    private static MapChangeEdit[] ScriptTrapEdits(SessionMapId map, string carrier, SessionTrapScript script) =>
    [
        new(map, MapChangeKey.ForEntity(carrier, UseScriptProperty), script.Script),
        new(map, MapChangeKey.ForEntity(carrier, UseFlagsProperty), script.LocalFlags),
        new(map, MapChangeKey.ForEntity(carrier, UseCountersProperty), script.Counters),
        new(map, MapChangeKey.ForEntity(carrier, TrapStateProperty), ScriptTrapPlacedBits),
    ];

    /// <summary>
    /// ARC-WORLD-125: the script trap markers made on <paramref name="cell"/> of <paramref name="map"/> that still stand (created sceneries carrying a placed use script), oldest first. A trap object on the tile
    /// refuses another script trap there (<see cref="TrapDeviceRules.ScriptOnTile"/>, 0x4BCAB0); the host adds the map data's own trap objects.
    /// </summary>
    public IReadOnlyList<SessionCreatedObject> QueryScriptTrapMarkers(SessionMapId map, CellCoord cell)
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_inventory.Objects.Created.Where(made => made.Kind == SessionCreatedKind.Scenery && made.Map == map && made.Cell == cell &&
            _maps.Get(map, MapChangeKey.ForEntity(made.Entity, UseScriptProperty)) is not null &&
            // A marker whose trap was removed (a disarm, or a native script that removes itself) is a destroyed trap object: the tile is free again.
            ((_maps.Get(map, MapChangeKey.ForEntity(made.Entity, TrapStateProperty)) ?? 0) & TrapRemovedBit) == 0 &&
            ((_maps.Get(map, MapChangeKey.ForEntity(made.Entity, ObjectConditionProperty)) ?? 0) & 0xFF) != (long)SessionObjectCondition.Destroyed).ToArray());
    }

    /// <summary>
    /// ARC-WORLD-125: a placed script trap's use slot as the session holds it (<see cref="PlaceScriptTrapDevice"/>), for an object that has no data row (a marker): null when none was written. A written slot is
    /// read back as the same numbers; the host reads the object's slot from its data until a write.
    /// </summary>
    public SessionTrapScript? QueryPlacedTrapScript(SessionMapId map, string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Map(map) || !SessionValidation.Entity(entity)) return null;
        if (_maps.Get(map, MapChangeKey.ForEntity(entity, UseScriptProperty)) is not { } number || number < 1 || number > Arcanum.Data.Scripts.ScrCatalogue.MaxScriptNumber) return null;
        long Part(string property) => _maps.Get(map, MapChangeKey.ForEntity(entity, property)) is { } value and >= 0 and <= uint.MaxValue ? value : 0;
        return new((int)number, (uint)Part(UseFlagsProperty), (uint)Part(UseCountersProperty));
    }

    /// <summary>
    /// ARC-WORLD-125: the trap baseline of a marker (a trap object, type 17, which no map data row backs) from its placed use slot and the script file's header flags the host reads; null when no slot was
    /// placed on it. The marker's object flags start as a trap object's, don't-draw (0x100), which its spotted mark clears.
    /// </summary>
    public SessionTrapBaseline? QueryMarkerTrapBaseline(SessionMapId map, string entity, uint headerFlags)
    {
        ThrowIfDisposed();
        return QueryPlacedTrapScript(map, entity) is { } placed
            ? new(TrapCarrier.TrapObject, placed.Script, headerFlags, unchecked((int)placed.Counters), TrapRules.DontDrawFlag)
            : null;
    }
}
