using Arcanum.Simulation.Events;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Traps;

namespace Arcanum.Session;

/// <summary>
/// ARC-WORLD-124 (ledger TP3): a trap device fuse that came due. <see cref="Entity"/> is the marker the placement made on the tile (destroyed by the handler,
/// <see cref="MarkerDestroyed"/>) or, for a placement on an object, the destroyed device item that names the map; the cast host runs <see cref="Spell"/> at <see cref="Cell"/> with no source and
/// no caster after the pass. <see cref="Postponed"/>: the pass had no cast host, so the fuse was scheduled again a minute on instead and nothing else happened.
/// </summary>
public sealed record SessionTrapDeviceFiring(GameEvent Event, SessionMapId Map, string Entity, int Spell, CellCoord Cell, bool MarkerDestroyed)
{
    public bool Postponed { get; init; }
}

/// <summary>ARC-WORLD-124: marks an event world that runs trap fuses (the cast host's pass world): a fuse that comes due in a pass without one waits a minute.</summary>
public interface ISessionTrapDeviceWorld
{
}

/// <summary>The outcome of <see cref="GameSessionOwner.PlaceTrapDevice"/>: the marker made on a tile (null on an object), or the refusal.</summary>
public sealed record SessionTrapDevicePlacement(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionCreatedObject? Marker, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-WORLD-124: why <see cref="PlaceTrapDevice"/> would refuse before any effect (a map or cell outside the ranges, the created-object or fuse budget, the revision), or null when it would not.
    /// </summary>
    public GameSessionFailure? QueryTrapDeviceRefusal(SessionMapId map, CellCoord cell, int spell, int markerPrototype)
    {
        ThrowIfDisposed();
        return TrapDeviceRefusal(map, cell, spell, markerPrototype);
    }

    private GameSessionFailure? TrapDeviceRefusal(SessionMapId map, CellCoord cell, int spell, int markerPrototype)
    {
        if (!SessionValidation.Map(map) || !DropCellFits(cell) || spell is < 0 or >= Arcanum.Simulation.Magic.SpellPaymentRules.Spells) return GameSessionFailure.InvalidTrap;
        if (markerPrototype != 0 && CreateObjectRefusal(SessionCreatedKind.Scenery, markerPrototype, map, cell, 0, 0) is { } refused) return refused;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (_events.QueryPending(SessionEventKinds.TrapDevice).Length >= SessionEventKinds.MaxTrapDevices || _events.Count >= _events.Limits.MaxPending ||
            _events.NextSequence == long.MaxValue || now > long.MaxValue - TrapDeviceRules.FuseMs) return GameSessionFailure.EventBudgetExceeded;
        return _revision == long.MaxValue ? GameSessionFailure.RevisionExhausted : null;
    }

    /// <summary>
    /// ARC-WORLD-124 (E6-b; 0x4BC2E0 object form, 0x4BC480 tile form, 0x4BC690 timer; ledger IA13, TP2, TP3): the hero's trap device <paramref name="device"/> with a spell in its slot 1 is used on
    /// <paramref name="cell"/> of <paramref name="map"/>. In one revision: the device is destroyed out of the hero's pack with no check of the target (any object, a critter included), the marker
    /// of <paramref name="markerPrototype"/> (the tile form; 0 none: another record, or an object target) is made there as a scenery object, and the fuse (<see cref="SessionEventKinds.TrapDevice"/>,
    /// <see cref="TrapDeviceRules.FuseMs"/>) is scheduled. The alarm of record 176 is the AI host's, the cast at the end of the fuse the cast host's. Refused before any effect: the version, a device
    /// the hero does not carry unworn or that is not a generic item, the bounds of <see cref="TrapDeviceRefusal"/>, a full durability budget.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4BC690, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    public SessionTrapDevicePlacement PlaceTrapDevice(GameSessionVersion expected, string device, SessionMapId map, CellCoord cell, int spell, int markerPrototype)
    {
        ThrowIfDisposed();
        SessionTrapDevicePlacement Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, Version);
        if (!SessionValidation.Entity(device)) return Reject(GameSessionFailure.InvalidItem);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (HeldKind(device) is not { } kind || _equipment.SlotOf(device) is not null) return Reject(GameSessionFailure.ItemNotAvailable);
        if (kind.Type != SessionItemType.Generic) return Reject(GameSessionFailure.InventoryMismatch);
        if (TrapDeviceRefusal(map, cell, spell, markerPrototype) is { } refused) return Reject(refused);
        if (NewDurabilityRecord(device, destroyed: true) && _durability.Count >= _limits.MaxDurabilityRecords) return Reject(GameSessionFailure.BudgetExceeded);
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        DestroyObject(device);
        SessionCreatedObject? marker = null;
        if (markerPrototype != 0) marker = CreateCreatedObject(SessionCreatedKind.Scenery, markerPrototype, map, cell, 0, 0, now);
        var scheduled = _events.ScheduleAfter(SessionEventKinds.TrapDevice, FusePayload([new(map.RuntimeMap, marker?.Entity ?? device)], now, TrapDeviceRules.FuseMs, spell, cell, marker is not null),
            TrapDeviceRules.FuseMs, EventClocks(now));
        if (scheduled.Disposition != GameEventScheduleDisposition.Added) throw new InvalidOperationException("An admitted trap fuse could not be scheduled.");
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, marker, Version);
    }

    private static GameEventPayload FusePayload(GameEventEntity[] entities, long now, long delayMs, int spell, CellCoord cell, bool marker) =>
        GameEventPayload.Of(entities, now, delayMs, spell, cell.X, cell.Y, marker ? 1 : 0);

    /// <summary>
    /// ARC-WORLD-124 (0x4BC780, ledger TP3): the fuse's handler. The marker made on the tile is destroyed and the firing is reported for the cast host's source-less run at the tile (the
    /// handler does not start spell runs itself: the run rules refuse a host port called from a pass). A pass without a cast host schedules the fuse again a minute on and reports it postponed.
    /// </summary>
    [Arcanum.Simulation.OriginalFunction(0x4BC780, Arcanum.Simulation.OriginalFunctionScope.Partial)]
    internal GameEventHandlerOutcome FireTrapDevice(GameEventFiring firing)
    {
        var payload = firing.Event.Payload;
        var entity = payload.Entities[0];
        var map = new SessionMapId(entity.Map);
        var spell = (int)payload.Values[2];
        var cell = new CellCoord((int)payload.Values[3], (int)payload.Values[4]);
        var marker = payload.Values[5] == 1;
        if (PassWorldAs<ISessionTrapDeviceWorld>() is null)
        {
            var now = _clock.QueryClockSnapshot().GameTimestampMs;
            if (_events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - SessionEventKinds.TrapDeviceRetryDelayMs)
                return GameEventHandlerOutcome.Refused;
            var again = _events.ScheduleAfter(SessionEventKinds.TrapDevice, FusePayload(payload.Entities.ToArray(), now, SessionEventKinds.TrapDeviceRetryDelayMs, spell, cell, marker),
                SessionEventKinds.TrapDeviceRetryDelayMs, EventClocks(now));
            if (again.Disposition != GameEventScheduleDisposition.Added) return GameEventHandlerOutcome.Refused;
            _passTrapDevices?.Add(new(firing.Event, map, entity.Code, spell, cell, false) { Postponed = true });
            return GameEventHandlerOutcome.Completed;
        }
        var destroyed = false;
        if (marker && _inventory.Objects.CreatedObject(entity.Code) is not null)
        {
            if (_revision == long.MaxValue) return GameEventHandlerOutcome.Refused;
            DestroyObject(entity.Code);
            Revise();
            destroyed = true;
        }
        _passTrapDevices?.Add(new(firing.Event, map, entity.Code, spell, cell, destroyed));
        return GameEventHandlerOutcome.Completed;
    }

    /// <summary>ARC-WORLD-124: the pending trap fuses, in queue order.</summary>
    public IReadOnlyList<GameEvent> QueryTrapDevices()
    {
        ThrowIfDisposed();
        return _events.QueryPending(SessionEventKinds.TrapDevice);
    }
}
