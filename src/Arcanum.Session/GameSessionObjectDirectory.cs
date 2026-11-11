using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-084 (MIG6, TM1): where an object's identity comes from. One id space: an entity string names exactly one object whatever its
/// origin, and the origin is read from the entity itself. <see cref="MapObject"/> is every entity that no session counter made (the
/// <c>oid:</c> identity of an object of the map data); the others are the session's own counters (<c>kit:n</c>, <c>made:n</c>,
/// <c>junk:n</c>, <c>pool:n</c>). ARC-DEV-139: <c>missile:n</c> is a spell missile in flight, a created object that no save keeps (<see cref="SessionMissile"/>).
/// ARC-DEV-140: <c>projectile:n</c> is a projectile of the ranged path in flight, an object of the same kind (<see cref="SessionProjectile"/>).
/// ARC-DEV-347 (E1-a): <c>scenery:n</c> and <c>eyecandy:n</c> are scenery and eye-candy objects the session created from a prototype on a map (<see cref="SessionCreatedObject"/>), saved with the objects section; the two share one counter.
/// </summary>
public enum SessionObjectOrigin { MapObject, Kit, Made, JunkPile, BloodPool, Missile, Projectile, Scenery, EyeCandy, Critter }

/// <summary>ARC-DEV-084: where an object is now (the directory's one answer to "where is X").</summary>
public enum SessionObjectPlace
{
    /// <summary>The session deleted the item (a deleted-item record exists).</summary>
    Destroyed,
    /// <summary>In the hero's pack (kit, taken or made list), not worn.</summary>
    HeroPack,
    /// <summary>Worn or wielded by the hero in <see cref="SessionObjectEntry.Slot"/>.</summary>
    HeroWorn,
    /// <summary>In the list of an NPC (<see cref="SessionObjectEntry.Holder"/>): a made or given item the session added.</summary>
    NpcList,
    /// <summary>In the list of a container the session filled (a barter window's sale).</summary>
    ContainerList,
    /// <summary>On the ground of <see cref="SessionObjectEntry.Map"/> at <see cref="SessionObjectEntry.Cell"/>.</summary>
    Ground,
    /// <summary>In the junk pile <see cref="SessionObjectEntry.Pile"/>, which lies at <see cref="SessionObjectEntry.Cell"/>.</summary>
    JunkPile,
    /// <summary>Still in the list of the holder its map data names (an attached seed lists it), neither taken nor dropped.</summary>
    SeedHolder,
}

/// <summary>
/// ARC-DEV-084: one row of the object directory: the object, how its id was made, and its place. The fields a place does not use are
/// null. A pool or a junk pile is itself on the ground; an item in a pile reports the pile.
/// </summary>
public sealed record SessionObjectEntry(string Entity, SessionObjectOrigin Origin, SessionObjectPlace Place, SessionMapId? Map = null,
    CellCoord? Cell = null, string? Holder = null, EquipmentSlot? Slot = null, string? Pile = null);

public sealed partial class GameSessionOwner
{
    /// <summary>The origin an entity string names (see <see cref="SessionObjectOrigin"/>).</summary>
    public static SessionObjectOrigin ObjectOriginOf(string entity)
    {
        if (HeroDomain.KitNumber(entity) is not null) return SessionObjectOrigin.Kit;
        if (SessionMadeItem.Number(entity) is not null) return SessionObjectOrigin.Made;
        if (SessionJunkPile.Number(entity) is not null) return SessionObjectOrigin.JunkPile;
        if (SessionBloodPool.Number(entity) is not null) return SessionObjectOrigin.BloodPool;
        if (SessionMissile.Number(entity) is not null) return SessionObjectOrigin.Missile;
        if (SessionProjectile.Number(entity) is not null) return SessionObjectOrigin.Projectile;
        if (SessionCreatedObject.Number(entity, SessionCreatedObject.SceneryPrefix) is not null) return SessionObjectOrigin.Scenery;
        if (SessionCreatedObject.Number(entity, SessionCreatedObject.EyeCandyPrefix) is not null) return SessionObjectOrigin.EyeCandy;
        // ARC-DEV-359: a created critter's entity starts with oid: (its per-object state is keyed so) and is read before the map object it would otherwise be taken for.
        if (SessionCreatedObject.Number(entity, SessionCreatedObject.CritterPrefix) is not null) return SessionObjectOrigin.Critter;
        return SessionObjectOrigin.MapObject;
    }

    /// <summary>
    /// ARC-DEV-084 (MIG6, TM1, GR3): where an object is now, by its entity, across every store the session keeps: the deleted items, the
    /// hero's lists and worn slots, the items the hero placed or scripts created, the NPC and container lists, the junk piles, the
    /// pools of blood and (for a map object) the attached seeds. Null when the session knows nothing of it: an entity no store holds, or
    /// a map object whose map's seed is not attached and that the session never moved (the directory does not read the game's data), or an item the
    /// session marked taken that no list holds (unreachable by the session's own moves). A
    /// read of the current state, in no revision; it never throws for an unknown entity.
    /// </summary>
    public SessionObjectEntry? QueryObject(string entity)
    {
        ThrowIfDisposed();
        return SessionValidation.Entity(entity) ? Locate(entity) : null;
    }

    /// <summary>
    /// ARC-DEV-084: the objects of one origin the session created or moved, each with its place, in entity order: for the kit, made items,
    /// junk piles and pools the objects the session made; for <see cref="SessionObjectOrigin.MapObject"/> the map objects the session moved
    /// (taken by the hero, placed, given, deleted). A per-kind store read as a directory query.
    /// </summary>
    public IReadOnlyList<SessionObjectEntry> QueryObjects(SessionObjectOrigin origin)
    {
        ThrowIfDisposed();
        IEnumerable<string> candidates = origin switch
        {
            SessionObjectOrigin.Kit => _inventory.Kit.Select(item => item.Entity)
                .Concat(_inventory.Placed.Where(item => item.Source == SessionPlacedSource.Kit).Select(item => item.Entity))
                .Concat(_inventory.Hero.GivenAll.Select(given => given.Entity).Where(entity => HeroDomain.KitNumber(entity) is not null)).Concat(_durability.Destroyed),
            SessionObjectOrigin.Made => _inventory.Made.Select(item => item.Entity)
                .Concat(_inventory.Lists.All.Select(item => item.Entity))
                .Concat(_inventory.Placed.Select(item => item.Entity)).Concat(_durability.Destroyed),
            SessionObjectOrigin.JunkPile => _inventory.Objects.Piles.Select(pile => pile.Entity),
            SessionObjectOrigin.BloodPool => _inventory.Objects.Pools.Select(pool => pool.Entity),
            SessionObjectOrigin.Missile => _missileObjects.Keys,
            SessionObjectOrigin.Projectile => _projectileObjects.Keys,
            SessionObjectOrigin.Scenery or SessionObjectOrigin.EyeCandy or SessionObjectOrigin.Critter => _inventory.Objects.Created.Select(made => made.Entity),
            _ => _inventory.Pc.Select(item => item.Entity).Concat(_inventory.Taken).Concat(_durability.Destroyed)
                .Concat(_inventory.Placed.Select(item => item.Entity)).Concat(_inventory.Hero.GivenAll.Select(given => given.Entity)),
        };
        return Array.AsReadOnly(candidates.Where(entity => ObjectOriginOf(entity) == origin).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(Locate).OfType<SessionObjectEntry>().ToArray());
    }

    /// <summary>
    /// ARC-DEV-088 (MIG6d, TM2): the stock of one holder, an NPC's or a container's (a merchant's substitute container included), as the
    /// directory answers it: the items its map data lists that the hero took, a critical dropped or a script moved nowhere (place
    /// <see cref="SessionObjectPlace.SeedHolder"/>, in the seed's order), then the items the session added to its list (place
    /// <see cref="SessionObjectPlace.NpcList"/> or <see cref="SessionObjectPlace.ContainerList"/>, in the order they entered). The
    /// seed items of a holder whose map has no seed attached are not known. Empty for an entity that holds nothing; a read of the
    /// current state, in no revision.
    /// </summary>
    public IReadOnlyList<SessionObjectEntry> QueryHeldBy(string holder)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(holder)) return [];
        var held = new List<SessionObjectEntry>();
        foreach (var (_, seed) in _inventory.Seeds.OrderBy(pair => pair.Key.RuntimeMap))
        {
            if (!seed.Holders.TryGetValue(holder, out var seedHolder)) continue;
            foreach (var entity in seedHolder.Items)
                if (Locate(entity) is { Place: SessionObjectPlace.SeedHolder } entry && entry.Holder == holder) held.Add(entry);
        }
        foreach (var item in _inventory.Lists.Of(holder))
            if (Locate(item.Entity) is { } entry) held.Add(entry);
        return Array.AsReadOnly(held.ToArray());
    }

    /// <summary>
    /// ARC-DEV-084 (TM1): the one allocator of a session counter's numbers. Made numbers follow the made items' rule
    /// (<see cref="NextMadeNumber"/>: one past every number any store, deleted record or regeneration discard holds); junk and pool
    /// numbers are one past the saved high-water mark and every number held now. Nothing is written.
    /// </summary>
    internal int NextObjectNumber(SessionObjectOrigin origin) => origin switch
    {
        SessionObjectOrigin.Made => (int)Math.Min(int.MaxValue, NextMadeNumber()),
        SessionObjectOrigin.JunkPile => (int)Math.Min(int.MaxValue, 1L + Math.Max(_inventory.Objects.LastJunkNumber,
            _inventory.Objects.Piles.Select(pile => SessionJunkPile.Number(pile.Entity) ?? 0).DefaultIfEmpty(0).Max())),
        SessionObjectOrigin.BloodPool => (int)Math.Min(int.MaxValue, 1L + Math.Max(_inventory.Objects.LastPoolNumber,
            _inventory.Objects.Pools.Select(pool => SessionBloodPool.Number(pool.Entity) ?? 0).DefaultIfEmpty(0).Max())),
        // ARC-DEV-139: a missile's number is never given again in the session's life (the counter is not saved; nothing it named survives a restore).
        SessionObjectOrigin.Missile => _lastMissileNumber + 1,
        SessionObjectOrigin.Projectile => _lastProjectileNumber + 1,
        // ARC-DEV-347: scenery and eye-candy objects share one counter (one past the saved mark and every number held now).
        SessionObjectOrigin.Scenery or SessionObjectOrigin.EyeCandy or SessionObjectOrigin.Critter => (int)Math.Min(int.MaxValue, 1L + Math.Max(_inventory.Objects.LastCreatedNumber,
            _inventory.Objects.Created.Select(made => SessionCreatedObject.Number(made.Entity) ?? 0).DefaultIfEmpty(0).Max())),
        _ => throw new ArgumentOutOfRangeException(nameof(origin)),
    };

    /// <summary>ARC-DEV-084: <see cref="NextObjectNumber"/> and, for junk and pool numbers, the high-water mark moved to it, so the number is
    /// never given again (a made number is given again to nobody by its own rule). The caller keeps the revision.</summary>
    internal int AllocateObjectNumber(SessionObjectOrigin origin)
    {
        var number = NextObjectNumber(origin);
        if (origin == SessionObjectOrigin.JunkPile) _inventory.Objects.LastJunkNumber = number;
        else if (origin == SessionObjectOrigin.BloodPool) _inventory.Objects.LastPoolNumber = number;
        else if (origin is SessionObjectOrigin.Scenery or SessionObjectOrigin.EyeCandy or SessionObjectOrigin.Critter) _inventory.Objects.LastCreatedNumber = number;
        return number;
    }

    private SessionObjectEntry? Locate(string entity)
    {
        var origin = ObjectOriginOf(entity);
        if (origin == SessionObjectOrigin.JunkPile)
            return _inventory.Objects.Pile(entity) is { } pile ? new(entity, origin, SessionObjectPlace.Ground, pile.Map, pile.Cell) : null;
        if (origin is SessionObjectOrigin.Scenery or SessionObjectOrigin.EyeCandy or SessionObjectOrigin.Critter)
            // ARC-DEV-359: a created critter stands where the session last placed it on its map (the AI host's step), else on its creation cell.
            return _inventory.Objects.CreatedObject(entity) is { } made
                ? new(entity, origin, SessionObjectPlace.Ground, made.Map, origin == SessionObjectOrigin.Critter && QueryNpcPlace(entity) is { } at && at.Map == made.Map ? at.World : made.Cell) : null;
        if (origin == SessionObjectOrigin.Missile)
            return _missileObjects.TryGetValue(entity, out var missile) ? new(entity, origin, SessionObjectPlace.Ground, missile.Map, missile.Cell) : null;
        if (origin == SessionObjectOrigin.Projectile)
            return _projectileObjects.TryGetValue(entity, out var projectile) ? new(entity, origin, SessionObjectPlace.Ground, projectile.Map, projectile.Cell) : null;
        if (origin == SessionObjectOrigin.BloodPool)
        {
            if (_inventory.Objects.Pool(entity) is not { } pool) return null;
            var decayed = BloodPoolRules.Decayed(pool.CreatedMs, _clock.QueryClockSnapshot().GameTimestampMs);
            return decayed ? new(entity, origin, SessionObjectPlace.Destroyed) : new(entity, origin, SessionObjectPlace.Ground, pool.Map, pool.Cell);
        }

        // An item. The deleted record wins: a deleted item is in no list any more.
        if (_durability.Destroyed.Contains(entity)) return new(entity, origin, SessionObjectPlace.Destroyed);
        if (_inventory.Hero.InPack(entity))
            return _equipment.SlotOf(entity) is { } slot
                ? new(entity, origin, SessionObjectPlace.HeroWorn, Slot: slot)
                : new(entity, origin, SessionObjectPlace.HeroPack);
        if (_inventory.PlacedItem(entity) is { } placed) return OnGround(entity, origin, placed.Map, placed.Cell);
        if (_inventory.NpcOf(entity) is { } holder)
            return new(entity, origin, _inventory.Lists.IsContainer(holder) ? SessionObjectPlace.ContainerList : SessionObjectPlace.NpcList, Holder: holder);
        if (_inventory.Taken.Contains(entity)) return null; // taken, in no list: no move of the session leaves this
        foreach (var (map, seed) in _inventory.Seeds.OrderBy(pair => pair.Key.RuntimeMap))
        {
            if (!seed.Items.ContainsKey(entity)) continue;
            if (LooseCell(map, seed, entity) is { } cell) return OnGround(entity, origin, map, cell);
            if (seed.HolderOf.TryGetValue(entity, out var seedHolder)) return new(entity, origin, SessionObjectPlace.SeedHolder, map, Holder: seedHolder);
        }
        return null;
    }

    private SessionObjectEntry OnGround(string entity, SessionObjectOrigin origin, SessionMapId map, CellCoord cell) =>
        _inventory.Objects.PileOf(entity) is { } pile
            ? new(entity, origin, SessionObjectPlace.JunkPile, pile.Map, pile.Cell, Pile: pile.Entity)
            : new(entity, origin, SessionObjectPlace.Ground, map, cell);
}
