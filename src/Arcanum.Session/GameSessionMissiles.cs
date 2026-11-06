using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-139 (MIG13 S8, docs/architecture/goal-table.md §3.5, TM4): a spell missile, a created object of the directory (<c>missile:n</c>, <see cref="SessionObjectOrigin.Missile"/>) that
/// owns its flight as a goal run. It is the original's projectile prototype 5028 (object type 4, GT55) the casting release creates (SM2): a tile and pixel offsets on the map, no owner (the caster
/// is the host's, slot 4 of the run). It is not saved: a snapshot holds no missile and no schema changed; MIG14 saves it with its run, and until then a restored session has none and a save in flight
/// loses it, as before this slice (the original saves a projectile in flight, GT64).
/// </summary>
public sealed record SessionMissile(string Entity, SessionMapId Map, CellCoord Cell, int OffsetX, int OffsetY)
{
    /// <summary>The missiles the session holds at once: as many as the goal table has runs.</summary>
    public const int MaxMissiles = 216;
    /// <summary>The entity prefix of a missile the session created.</summary>
    public const string EntityPrefix = "missile:";

    /// <summary>The missile's number in its entity, or null when the entity is not a missile's.</summary>
    public static int? Number(string? entity) =>
        entity is not null && entity.StartsWith(EntityPrefix, StringComparison.Ordinal) &&
        int.TryParse(entity.AsSpan(EntityPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
        n >= 1 && entity == EntityPrefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture) ? n : null;
}

public sealed partial class GameSessionOwner
{
    // Not part of any snapshot (the unsaved origin of TM4): the missiles of the active map, in the order they were created.
    private readonly Dictionary<string, SessionMissile> _missileObjects = new(StringComparer.Ordinal);
    private int _lastMissileNumber;

    /// <summary>
    /// ARC-DEV-139: creates a spell missile on the active map at <paramref name="cell"/> (the casting release's launch, 0x4248A0 → 0x43CBA0; the cell must lie in 0 .. 2^24 − 1), and returns its entity. Null
    /// when the cell is out of range or the session holds <see cref="SessionMissile.MaxMissiles"/> already. A read-side object: no revision, no version change, nothing saved. Its flight is a goal run the
    /// host starts (<see cref="StartGoal"/>).
    /// </summary>
    public string? CreateMissile(CellCoord cell)
    {
        ThrowIfDisposed();
        if (!DropCellFits(cell) || _missileObjects.Count >= SessionMissile.MaxMissiles || _lastMissileNumber == int.MaxValue - 1) return null;
        var entity = SessionMissile.EntityPrefix + (++_lastMissileNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _missileObjects.Add(entity, new(entity, _actor.Map, cell, 0, 0));
        return entity;
    }

    /// <summary>ARC-DEV-139: moves a missile to <paramref name="cell"/> with the pixel offsets from that tile's centre (the ordinary object move 0x43E770 per step, GT64). False for a missile the session does not
    /// hold or a cell out of range. No revision.</summary>
    public bool MoveMissile(string entity, CellCoord cell, int offsetX, int offsetY)
    {
        ThrowIfDisposed();
        if (!_missileObjects.TryGetValue(entity ?? "", out var missile) || !DropCellFits(cell)) return false;
        _missileObjects[entity!] = missile with { Cell = cell, OffsetX = offsetX, OffsetY = offsetY };
        return true;
    }

    /// <summary>ARC-DEV-139: destroys a missile (the one destroy path, TM4: <see cref="DestroyObject"/>). False when the session holds none of that entity. The run that names it is the host's to end. No revision.</summary>
    public bool DestroyMissile(string entity)
    {
        ThrowIfDisposed();
        return entity is not null && _missileObjects.Remove(entity);
    }

    /// <summary>ARC-DEV-139: a missile of the session by its entity, or null.</summary>
    public SessionMissile? QueryMissile(string entity)
    {
        ThrowIfDisposed();
        return entity is not null && _missileObjects.TryGetValue(entity, out var missile) ? missile : null;
    }

    /// <summary>ARC-DEV-139: the missiles the session holds, in the order they were created.</summary>
    public IReadOnlyList<SessionMissile> QueryMissiles()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_missileObjects.Values.OrderBy(m => SessionMissile.Number(m.Entity)).ToArray());
    }
}
