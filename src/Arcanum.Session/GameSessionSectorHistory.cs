using System.Collections.Immutable;
using Arcanum.Data.Lighting;
using Arcanum.Simulation.Residency;

namespace Arcanum.Session;

/// <summary>ARC-DEV-106: one record of the sector eviction history as saved: the sector's id (<c>X | Y &lt;&lt; 26</c>) and the game time (ms) at which
/// it left the cache.</summary>
public readonly record struct SessionSectorEviction(long Sector, long EvictedAtMs);

/// <summary>
/// ARC-DEV-106 (MIG9 S6, K1 §3.6, ledger CX10; schema 68, the <c>residency</c> section): the eviction history of the open map's sector cache, as
/// a save writes it (the original's sector module, PS8, 0x4D1150): the records oldest first, every sector the cache holds at the save having one
/// stamped with the save's time. The history is the only part of the cache that survives a save (the slots and the ages do not, K1 §3.3); a load
/// hands it to the cache the next time the map's cache opens (<see cref="GameSessionOwner.OpenSectors"/>), so a sector that was loaded at the save
/// is not "away" when it loads again. <see cref="Map"/> is the map the history belongs to, always the hero's; it is null exactly when the history
/// is empty (a game saved with no cache open, and a game of an older schema).
/// </summary>
public sealed record SessionResidencyState(SessionMapId? Map, IReadOnlyList<SessionSectorEviction> History)
{
    public static SessionResidencyState Empty { get; } = new(null, []);

    public bool IsEmpty => Map is null && History.Count == 0;
}

/// <summary>
/// ARC-DEV-106 (MIG9 S6, RL19, ledger SS8, LT4): the light state of one loaded sector: the hour its scenery and lights were last brought up to
/// date for (the fold that loaded it, then the hourly update of the loaded sectors) and whether that hour is light day (hours 6 to 17). The
/// session records it; the presentation draws it. Derived data, never saved.
/// </summary>
public readonly record struct SectorLight(ResidentSector Sector, int Hour, bool Day);

public sealed partial class GameSessionOwner
{
    // ARC-DEV-106: the eviction history a restore found, for the map's cache to take when it opens (null: none, or already taken). It is the
    // history of the hero's map and is dropped when the hero leaves it, as the original clears the history at any other map close.
    private (SessionMapId Map, ImmutableArray<SectorEvictionRecord> History)? _pendingHistory;

    /// <summary>
    /// ARC-DEV-106 (RL19, SS8): the light state of every loaded sector of <paramref name="map"/>'s cache, ascending sector id, or null when the
    /// map has no sector cache. A sector's hour is the clock's hour at the fold that loaded it; the hourly update moves every loaded sector on
    /// to the new hour when the clock enters it, so a loaded sector always shows the clock's hour, and a sector that was away folds to the hour
    /// it loads at.
    /// </summary>
    public IReadOnlyList<SectorLight>? QuerySectorLights(SessionMapId map)
    {
        ThrowIfDisposed();
        if (Sectors(map) is not { } held) return null;
        return [.. held.LightHours.OrderBy(pair => pair.Key).Select(pair => Light(Sector(pair.Key), pair.Value))];
    }

    private static SectorLight Light(ResidentSector sector, int hour) => new(sector, hour, ObjectLightReader.IsDayHour(hour));

    // The hourly ambient lighting event (RL19): the loaded sectors follow the clock's hour.
    private void RefreshSectorLights(int hour)
    {
        if (_sectors is not { } held || held.LightHours.All(pair => pair.Value == hour)) return;
        _sectors = held with { LightHours = held.LightHours.SetItems(held.LightHours.Select(pair => new KeyValuePair<long, int>(pair.Key, hour))) };
    }

    // The history a save writes for the open map (CX10): the cache's, every loaded sector without a record stamped with now; the session's own
    // history changes too only under the Original setting. With no cache open it is the history a restore left unclaimed.
    private SessionResidencyState CaptureResidency()
    {
        if (Sectors(null) is { } held)
        {
            var stamp = SectorCacheRules.StampLoaded(held.State, _clock.QueryClockSnapshot().GameTimestampMs);
            if (stamp.Failure != SectorCacheFailure.None) return SessionResidencyState.Empty;
            if (_rules.Lifecycle.SaveStampsLoadedSectors && stamp.Stamped.Length > 0) _sectors = held with { State = stamp.State };
            return ResidencyOf(held.Map, stamp.State.History);
        }
        return _pendingHistory is { } pending ? ResidencyOf(pending.Map, pending.History) : SessionResidencyState.Empty;
    }

    private static SessionResidencyState ResidencyOf(SessionMapId map, ImmutableArray<SectorEvictionRecord> history) =>
        history.IsEmpty ? SessionResidencyState.Empty : new(map, [.. history.Select(r => new SessionSectorEviction(r.Sector, r.EvictedAtMs))]);

    // A restore's history: empty, or the hero's map's and a history the cache rules take (CX10's load, 0x4D12B0, with the typed file's checks).
    private static GameSessionFailure ImportResidency(SessionResidencyState saved, SessionMapId heroMap, long clockMs,
        out (SessionMapId Map, ImmutableArray<SectorEvictionRecord> History)? pending)
    {
        pending = null;
        if (saved.Map is null) return saved.History.Count == 0 ? GameSessionFailure.None : GameSessionFailure.InvalidSnapshot;
        if (saved.History.Count == 0 || saved.Map != heroMap || !SessionValidation.Map(heroMap)) return GameSessionFailure.InvalidSnapshot;
        var records = saved.History.Select(r => new SectorEvictionRecord(r.Sector, r.EvictedAtMs)).ToArray();
        // The cache's size is the opening host's; the file is bounded by the largest cache the rules allow and the open bounds it to its own.
        SectorCacheLimits.TryCreate(SectorCacheLimits.MaximumSlots, out var largest);
        var restored = SectorCacheRules.RestoreHistory(largest!, records, clockMs);
        if (restored.Failure != SectorCacheFailure.None) return GameSessionFailure.InvalidSnapshot;
        pending = (heroMap, restored.State.History);
        return GameSessionFailure.None;
    }
}
