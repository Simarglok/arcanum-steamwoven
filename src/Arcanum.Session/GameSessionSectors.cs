using Arcanum.Simulation;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Residency;

namespace Arcanum.Session;

/// <summary>What <see cref="GameSessionOwner.OpenSectors"/> did.</summary>
public enum SectorsOpenStatus
{
    /// <summary>The sector cache of the open map is open, every sector unloaded.</summary>
    Open,
    /// <summary>No residency of the map is open (<see cref="GameSessionOwner.OpenResidency"/> first): the fold lists residents.</summary>
    NoResidency,
    /// <summary>The extent is not <see cref="SectorMapExtent.Valid"/>, or a resident on the ground lies outside it.</summary>
    InvalidExtent,
}

/// <summary>
/// ARC-DEV-104 (MIG9 S4, K1 §3.4, RL8): one sector a touch loaded and folded: the fold plan (<see cref="Marked"/>: the first ticks carry the
/// wake-up marker, because the sector had no eviction record or was away long enough), the sector the load evicted from a full cache, and the
/// NPCs that got their immediate first tick, in entity order. <see cref="Refused"/> are the NPCs whose tick the queue could not take (its budget).
/// </summary>
public sealed record SectorFold(ResidentSector Sector, bool Marked, ResidentSector? Evicted, IReadOnlyList<string> Npcs, IReadOnlyList<string> Refused);

/// <summary>
/// ARC-DEV-104: what <see cref="GameSessionOwner.TouchSectors"/> did: the failure (nothing changed), else the sectors it loaded and folded in
/// ascending sector id, the sectors it only refreshed (<see cref="Hits"/>) and the version after it. Inside an event pass the first ticks wait for
/// the pass's end (<see cref="Deferred"/>; <see cref="SessionEventPassResult.DeferredFoldTicks"/> reports them).
/// </summary>
public sealed record SectorTouchResult(GameSessionFailure Failure, GameSessionVersion Version)
{
    public IReadOnlyList<SectorFold> Folds { get; init; } = Array.Empty<SectorFold>();
    public IReadOnlyList<ResidentSector> Hits { get; init; } = Array.Empty<ResidentSector>();
    public bool Deferred { get; init; }
}

public sealed partial class GameSessionOwner
{
    // The sector cache of the residency's map (derived: not saved, no revision); null until OpenSectors, dropped with the residency.
    private (SessionMapId Map, SectorCacheState State)? _sectors;

    // The first ticks a fold inside an event pass queued, scheduled when the pass ends (ScheduleAiTick refuses inside a pass).
    private List<(int Map, string Entity, bool Marked)>? _passFoldTicks;

    /// <summary>
    /// ARC-DEV-104 (MIG9 S4, K1 §3.3, RL2, RL7): opens the sector cache of the open map, whose residency must be open, with every sector
    /// unloaded; the map's extent is in sectors (64 × 64 cells each). A cache replaces the previous one of the same map and starts with an
    /// empty eviction history (the original clears it at every map close but a game load's, CX6; a load's saved history is S6's). Derived
    /// data: no revision, not saved, dropped with the residency (a close, the hero's map change, a restore).
    /// </summary>
    public SectorsOpenStatus OpenSectors(SessionMapId map, SectorMapExtent extent, SectorCacheLimits? limits = null)
    {
        ThrowIfDisposed();
        if (Residency(map) is not { } residency) return SectorsOpenStatus.NoResidency;
        if (!extent.Valid) return SectorsOpenStatus.InvalidExtent;
        foreach (var sector in residency.Index.Keys)
            if (sector.X >= extent.SectorsX || sector.Y >= extent.SectorsY) return SectorsOpenStatus.InvalidExtent;
        var opened = SectorCacheRules.Open(SectorCacheState.Initial(limits ?? SectorCacheLimits.Default), extent);
        if (opened.Failure != SectorCacheFailure.None) return SectorsOpenStatus.InvalidExtent;
        _sectors = (map, opened.State);
        return SectorsOpenStatus.Open;
    }

    /// <summary>ARC-DEV-104: the sector cache of <paramref name="map"/> while its residency is open (the open map), or null.</summary>
    public SectorCacheState? QuerySectors(SessionMapId map)
    {
        ThrowIfDisposed();
        return Sectors(map)?.State;
    }

    /// <summary>
    /// ARC-DEV-104 (RL16's loaded test 0x4D04E0): whether <paramref name="sector"/> of the open map is loaded; null when the map has no sector
    /// cache (no residency, another map, or a host that opened none, such as the viewer's window of the main map).
    /// </summary>
    public bool? QuerySectorLoaded(SessionMapId map, ResidentSector sector)
    {
        ThrowIfDisposed();
        if (Sectors(map) is not { } sectors) return null;
        return sector.X >= 0 && sector.Y >= 0 && sectors.State.IsLoaded(SectorCacheRules.SectorId(sector.X, sector.Y));
    }

    /// <summary>
    /// ARC-DEV-104 (MIG9 S4, K1 §3.3, §3.4, RL8, RL11, RL17): touches the sectors of <paramref name="cells"/> on the open map at the session's game
    /// time, as the original's readers lock them (the 3 × 3 arrival pre-cache, the view rectangle drawn, a relocation's wading test, a tile query
    /// with a sector type): a loaded sector's age is refreshed, an unloaded one is loaded (evicting the least recent from a full cache) and
    /// <b>folded</b>. The fold lists the sector's residents (the sector index, RL9) and gives every resident NPC that is not destroyed, dead or alive,
    /// off or not, one immediate AI tick 1 ms from now that replaces its pending one (0x441FC0 → 0x4AD790). The tick carries the wake-up marker
    /// only when the fold plan says so: the sector had no eviction record, or was away 3,000 ms or more (RL8); under the Original setting also
    /// whenever the game day changed since the eviction (ledger CX4, CX7: the original's elapsed time has no borrow,
    /// <see cref="LifecycleRuleProfile.FoldOnDayChange"/>). The marker runs the tick's wake-up block (check wield, waypoints or standpoints,
    /// script point 10, <see cref="Arcanum.Simulation.Ai.AiSchedulerRules.Tick"/>). Sectors are locked as one batch in the order given (cells of
    /// one sector count once); the folds are reported in ascending sector id. Inside an event pass the first ticks are queued and scheduled
    /// when the pass ends. Cells outside the map's extent touch nothing; a negative cell is refused. One revision when a tick was scheduled.
    /// </summary>
    [OriginalFunction(0x4F1CD0, OriginalFunctionScope.Partial)]
    [OriginalFunction(0x441FC0, OriginalFunctionScope.Partial)]
    public SectorTouchResult TouchSectors(GameSessionVersion expected, SessionMapId map, IReadOnlyList<CellCoord> cells)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cells);
        if (!IsCurrent(expected)) return new(GameSessionFailure.StaleVersion, Version);
        if (Sectors(map) is not { } sectors || Residency(map) is not { } residency) return new(GameSessionFailure.InvalidMap, Version);
        if (cells.Any(c => c.X < 0 || c.Y < 0)) return new(GameSessionFailure.InvalidSectors, Version);
        var ids = new List<long>(cells.Count);
        var seen = new HashSet<long>();
        foreach (var cell in cells)
        {
            var sector = ResidentSector.Of(cell);
            if (sector.X >= sectors.State.Map!.Value.SectorsX || sector.Y >= sectors.State.Map.Value.SectorsY) continue;
            var id = SectorCacheRules.SectorId(sector.X, sector.Y);
            if (seen.Add(id)) ids.Add(id);
        }
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var locked = SectorCacheRules.LockAll(sectors.State, ids, now);
        if (locked.Outcomes.FirstOrDefault(o => o.Status == SectorLockStatus.Refused) is { Status: SectorLockStatus.Refused } refused)
            return new(refused.Failure == SectorCacheFailure.NoFreeSlot ? GameSessionFailure.InvalidSectors : GameSessionFailure.InvalidMap, Version);
        // A load schedules ticks, which needs a revision; a touch that only refreshes ages does not.
        if (_revision == long.MaxValue && locked.Outcomes.Any(o => o.Status == SectorLockStatus.Loaded))
            return new(GameSessionFailure.RevisionExhausted, Version);
        _sectors = (map, locked.State);
        var hits = locked.Outcomes.Where(o => o.Status == SectorLockStatus.Hit).Select(o => Sector(o.Sector)).ToArray();
        var loaded = locked.Outcomes.Where(o => o.Status == SectorLockStatus.Loaded).OrderBy(o => o.Sector).ToArray();
        if (loaded.Length == 0) return new(GameSessionFailure.None, Version) { Hits = hits };
        // One listing for every loaded sector: the residents the session's changes leave there now.
        var bySector = new Dictionary<ResidentSector, List<string>>();
        foreach (var resident in Located(residency, loaded.Select(o => Sector(o.Sector)), r => r.Sector is { } s && loaded.Any(o => Sector(o.Sector) == s)).Residents)
            if (resident.Kind == SessionResidentKind.Npc && !resident.Destroyed && SessionEventKinds.AiTickNpc(resident.Entity))
                (bySector.TryGetValue(resident.Sector!.Value, out var list) ? list : bySector[resident.Sector.Value] = []).Add(resident.Entity);
        var inPass = _passWorld is not null;
        var folds = new List<SectorFold>(loaded.Length);
        var scheduled = false;
        foreach (var outcome in loaded)
        {
            var sector = Sector(outcome.Sector);
            var marked = FoldMarked(outcome, now);
            var npcs = bySector.TryGetValue(sector, out var found) ? found : [];
            var refusedTicks = new List<string>();
            foreach (var npc in npcs)
            {
                if (inPass) { (_passFoldTicks ??= []).Add((map.RuntimeMap, npc, marked)); continue; }
                if (!AiTickFits(npc, now, 1)) { refusedTicks.Add(npc); continue; }
                ScheduleTick(map.RuntimeMap, npc, now, 1, marked);
                scheduled = true;
            }
            folds.Add(new(sector, marked, outcome.Evicted is { } evicted ? Sector(evicted) : null, npcs.AsReadOnly(), refusedTicks.AsReadOnly()));
        }
        if (scheduled) Revise();
        return new(GameSessionFailure.None, Version) { Folds = folds.AsReadOnly(), Hits = hits, Deferred = inPass && bySector.Count > 0 };
    }

    // The fold plan (RL8): no record always folds; a record folds after 3,000 ms of game time, and under the Original setting also on another
    // game day (the lock's elapsed time subtracts the day and millisecond words without a borrow, CX3, CX4, CX7).
    private bool FoldMarked(SectorLockOutcome outcome, long now) =>
        outcome.EvictedAtMs is not { } at || (_rules.Lifecycle.FoldOnDayChange ? outcome.Fold : SectorCacheRules.FoldDueAfter(at, now));

    private static ResidentSector Sector(long id) => new((int)(id & (SectorMapExtent.MaximumPerAxis - 1)), (int)(id >> 26));

    private (SessionMapId Map, SectorCacheState State)? Sectors(SessionMapId? map) =>
        _sectors is { } sectors && Residency(sectors.Map) is { } residency && residency.Map == sectors.Map && (map is null || sectors.Map == map)
            ? sectors : null;

    // The pass's end: the folds' queued first ticks are scheduled in the order queued (one revision when any was); returns (scheduled, refused).
    // A tick of the same NPC that came due in the same pass has fired before the flush replaces what it rescheduled (residency §4).
    private (int Scheduled, int Refused) FlushFoldTicks()
    {
        if (_passFoldTicks is not { Count: > 0 } queued) { _passFoldTicks = null; return (0, 0); }
        _passFoldTicks = null;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        int scheduled = 0, refused = 0;
        // An NPC the pass destroyed meanwhile still gets its tick here: the next pass's object check drops it, as for any tick of a gone object.
        foreach (var (map, entity, marked) in queued)
        {
            if (map != _actor.Map.RuntimeMap || _revision == long.MaxValue || !AiTickFits(entity, now, 1)) { refused++; continue; }
            ScheduleTick(map, entity, now, 1, marked);
            scheduled++;
        }
        if (scheduled > 0) Revise();
        return (scheduled, refused);
    }
}
