using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// A pool of blood the session left (ARC-RENDER-041; the original's scenery of basic prototype 4028, ledger BP1–BP9): its entity
/// (<c>pool:n</c>), the critter whose death left it (an NPC entity, or <see cref="SessionEventKinds.PlayerEntity"/>) and that death's
/// game time, its map, cell and pixel offsets (the critter's), and the game time it was created.
/// </summary>
public sealed record SessionBloodPool(string Entity, string Critter, long DeathTimeMs, SessionMapId Map, CellCoord Cell, int OffsetX, int OffsetY, long CreatedMs)
{
    /// <summary>The pools the session holds at once (project bound; the original has none, and a pool lasts two game days).</summary>
    public const int MaxPools = 4096;
    /// <summary>The entity prefix of a pool the session left.</summary>
    public const string EntityPrefix = "pool:";

    /// <summary>The pool's number in its entity, or null when the entity is not a pool's.</summary>
    public static int? Number(string? entity) =>
        entity is not null && entity.StartsWith(EntityPrefix, StringComparison.Ordinal) &&
        int.TryParse(entity.AsSpan(EntityPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
        n >= 1 && entity == EntityPrefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture) ? n : null;
}

/// <summary>The outcome of <see cref="GameSessionOwner.LeaveBloodPool"/>: the pool left (Changed), the one this death already left
/// (NoChange, no refusal), or none (NoChange, <see cref="Refusal"/> says why).</summary>
public sealed record SessionBloodPoolResult(GameSessionDisposition Disposition, GameSessionFailure Failure, SessionBloodPool? Pool,
    BloodPoolRefusal Refusal, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// ARC-RENDER-041: the host's dying critter leaves a pool of blood when its dying animation has played (0x42FED0 → 0x432D90,
    /// ledger BP1–BP7): <see cref="BloodPoolRules.Leaves"/> decides from the host's facts, and the pool lies at
    /// <paramref name="cell"/> of <paramref name="map"/> with the critter's pixel offsets. <paramref name="critter"/> is an NPC with
    /// a recorded death, or <see cref="SessionEventKinds.PlayerEntity"/> once the hero has died; each recorded death leaves one pool at
    /// most while that pool is kept (a repeated call answers it, NoChange; once it has decayed and been dropped, a call leaves a new
    /// one, so the host asks once per death). The pool lasts two game days (<see cref="BloodPoolRules.Decayed"/>);
    /// a decayed pool is dropped when the next one is left. Beyond <see cref="SessionBloodPool.MaxPools"/> pools the call is refused
    /// (project bound), as is a call once the pool number mark (the highest number ever given, kept in the snapshot) reaches the integer bound. The creation's eye candy, the network message and the editor's case are not modelled.
    /// </summary>
    public SessionBloodPoolResult LeaveBloodPool(GameSessionVersion expected, string critter, SessionMapId map, CellCoord cell,
        int offsetX, int offsetY, BloodPoolFacts facts)
    {
        ThrowIfDisposed();
        SessionBloodPoolResult Reject(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, BloodPoolRefusal.None, new(_identity, _revision));
        if (!SessionValidation.Map(map) || cell.X < 0 || cell.Y < 0) return Reject(GameSessionFailure.InvalidMap);
        long? death = critter == SessionEventKinds.PlayerEntity ? _life.Hero?.DeathTimeMs
            : SessionValidation.Entity(critter) && _life.Critters.TryGetValue(critter, out var record) ? record.DeathTimeMs : null;
        if (death is not { } deathMs) return Reject(GameSessionFailure.InvalidLifeTarget);
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        if (_inventory.BloodPools.Find(pool => pool.Critter == critter && pool.DeathTimeMs == deathMs) is { } left)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, left, BloodPoolRefusal.None, new(_identity, _revision));
        var refusal = BloodPoolRules.Leaves(facts);
        if (refusal != BloodPoolRefusal.None)
            return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, refusal, new(_identity, _revision));
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var live = _inventory.BloodPools.Count(pool => !BloodPoolRules.Decayed(pool.CreatedMs, now));
        if (live >= SessionBloodPool.MaxPools) return Reject(GameSessionFailure.BudgetExceeded);
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);

        // ARC-DEV-084 (TM1): an id is never reassigned, so a decayed pool's number is not taken again.
        if (NextObjectNumber(SessionObjectOrigin.BloodPool) == int.MaxValue) return Reject(GameSessionFailure.BudgetExceeded);

        foreach (var decayed in _inventory.BloodPools.Where(pool => BloodPoolRules.Decayed(pool.CreatedMs, now)).Select(pool => pool.Entity).ToArray()) DestroyObject(decayed);
        var pool = CreateBloodPool(critter, deathMs, map, cell, offsetX, offsetY, now);
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, pool, BloodPoolRefusal.None, new(_identity, _revision));
    }

    // ARC-RENDER-041: the saved pools within the bound, each a pool entity (pool:n) once, left by the hero (only in a session with one)
    // or an NPC, at most once per recorded death, on a valid map and cell, created at or after that death and not after the saved clock.
    private static GameSessionFailure ImportBloodPools(IReadOnlyList<SessionBloodPool> saved, bool hero, long clockMs, InventoryDomain inventory)
    {
        if (saved.Count > SessionBloodPool.MaxPools) return GameSessionFailure.BudgetExceeded;
        var entities = new HashSet<string>(StringComparer.Ordinal);
        var deaths = new HashSet<(string, long)>();
        foreach (var pool in saved)
        {
            if (pool is null || SessionBloodPool.Number(pool.Entity) is null || !entities.Add(pool.Entity) ||
                (pool.Critter == SessionEventKinds.PlayerEntity ? !hero : !SessionValidation.Entity(pool.Critter) || pool.Critter == SessionEventKinds.AbsentRole) ||
                !deaths.Add((pool.Critter, pool.DeathTimeMs)) || !SessionValidation.Map(pool.Map) || pool.Cell.X < 0 || pool.Cell.Y < 0 ||
                pool.DeathTimeMs < 0 || pool.CreatedMs < pool.DeathTimeMs || pool.CreatedMs > clockMs)
                return GameSessionFailure.InvalidSnapshot;
            inventory.BloodPools.Add(pool);
        }
        return GameSessionFailure.None;
    }

    /// <summary>ARC-RENDER-041: the pools of blood lying on <paramref name="map"/> now (not decayed), in the order they were left.</summary>
    public IReadOnlyList<SessionBloodPool> QueryBloodPools(SessionMapId map)
    {
        ThrowIfDisposed();
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        return Array.AsReadOnly(_inventory.BloodPools.Where(pool => pool.Map == map && !BloodPoolRules.Decayed(pool.CreatedMs, now)).ToArray());
    }
}
