using Arcanum.Simulation;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-140 (MIG13 S8b, docs/architecture/goal-table.md §3.5, TM4): a projectile of the ranged path in flight, a created object of the directory (<c>projectile:n</c>, <see cref="SessionObjectOrigin.Projectile"/>) that
/// owns its flight as a goal run (goal 36). It is the original's projectile prototype 5028 (object type 4, GT55) the attack dispatcher creates at the shooter's tile (0x4B24F0): a tile and pixel offsets on the map and no
/// owner (the shooter is the host's, slot 4 of the run). The hero's shot record (<see cref="SessionShot"/>) is a different thing: it carries the decided outcome, this object the flight.
/// ARC-DEV-288 (schema 80, the <c>flights</c> section): the projectile is saved with its run and its shot, as the original writes every object made in play to <c>mobile.mdy</c> with the
/// projectile's fields (SV5, SV6): <see cref="Flight"/> is what the host keeps of the flight (the line, the position, the carried flags), written at the launch and every tick.
/// </summary>
public sealed record SessionProjectile(string Entity, SessionMapId Map, CellCoord Cell, int OffsetX, int OffsetY)
{
    /// <summary>ARC-DEV-288: the flight as the host last wrote it; null until the host's first write (a projectile without it is not saved).</summary>
    public SessionProjectileFlight? Flight { get; init; }

    /// <summary>The projectiles the session holds at once: as many as the goal table has runs.</summary>
    public const int MaxProjectiles = 216;
    /// <summary>The entity prefix of a projectile the session created.</summary>
    public const string EntityPrefix = "projectile:";

    /// <summary>The projectile's number in its entity, or null when the entity is not a projectile's.</summary>
    public static int? Number(string? entity) =>
        entity is not null && entity.StartsWith(EntityPrefix, StringComparison.Ordinal) &&
        int.TryParse(entity.AsSpan(EntityPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) &&
        n >= 1 && entity == EntityPrefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture) ? n : null;
}

public sealed partial class GameSessionOwner
{
    // ARC-DEV-288: the projectiles of the active map, in the order they were created; saved with their runs and shots (the flights section).
    private readonly Dictionary<string, SessionProjectile> _projectileObjects = new(StringComparer.Ordinal);
    private int _lastProjectileNumber;

    /// <summary>
    /// ARC-DEV-140: creates a projectile on the active map at <paramref name="cell"/> (the attack dispatcher's launch, 0x4B24F0 → the network wrapper → 0x43CBA0; the cell must lie in 0 .. 2^24 − 1), and returns its entity.
    /// Null when the cell is out of range or the session holds <see cref="SessionProjectile.MaxProjectiles"/> already. A read-side object: no revision, no version change. Its flight is a goal run the host
    /// starts (<see cref="StartGoal"/>); a save keeps it once the host wrote its flight (<see cref="MoveProjectile"/>).
    /// </summary>
    [OriginalFunction(0x43CBA0, OriginalFunctionScope.Partial)]
    public string? CreateProjectile(CellCoord cell)
    {
        ThrowIfDisposed();
        if (!DropCellFits(cell) || _projectileObjects.Count >= SessionProjectile.MaxProjectiles || _lastProjectileNumber == int.MaxValue - 1) return null;
        var entity = SessionProjectile.EntityPrefix + (++_lastProjectileNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _projectileObjects.Add(entity, new(entity, _actor.Map, cell, 0, 0));
        return entity;
    }

    /// <summary>ARC-DEV-140: moves a projectile to <paramref name="cell"/> with the pixel offsets from that tile's centre (the ordinary object move 0x43E770 per tile, GT64). False for a projectile the session does not
    /// hold or a cell out of range. No revision. ARC-DEV-288: <paramref name="flight"/>, when given, replaces the flight the save keeps (null keeps the one written before).</summary>
    [OriginalFunction(0x43E770, OriginalFunctionScope.Partial)]
    public bool MoveProjectile(string entity, CellCoord cell, int offsetX, int offsetY, SessionProjectileFlight? flight = null)
    {
        ThrowIfDisposed();
        if (!_projectileObjects.TryGetValue(entity ?? "", out var projectile) || !DropCellFits(cell)) return false;
        _projectileObjects[entity!] = projectile with { Cell = cell, OffsetX = offsetX, OffsetY = offsetY, Flight = flight ?? projectile.Flight };
        return true;
    }

    /// <summary>ARC-DEV-140: destroys a projectile (the one destroy path, TM4: <see cref="DestroyObject"/>). False when the session holds none of that entity. The run that names it is the host's to end. No revision.</summary>
    public bool DestroyProjectile(string entity)
    {
        ThrowIfDisposed();
        return entity is not null && _projectileObjects.Remove(entity);
    }

    /// <summary>ARC-DEV-140: a projectile of the session by its entity, or null.</summary>
    public SessionProjectile? QueryProjectile(string entity)
    {
        ThrowIfDisposed();
        return entity is not null && _projectileObjects.TryGetValue(entity, out var projectile) ? projectile : null;
    }

    /// <summary>ARC-DEV-140: the projectiles the session holds, in the order they were created.</summary>
    public IReadOnlyList<SessionProjectile> QueryProjectiles()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_projectileObjects.Values.OrderBy(p => SessionProjectile.Number(p.Entity)).ToArray());
    }
}
