namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-091 (MIG6d-3, TM2, GR4, GR17): the saved records of the objects the session itself creates and destroys outside the item
/// lists: the junk piles and the pools of blood, with the high-water marks of their numbers. It is one snapshot section
/// (<see cref="GameSessionSnapshot.Objects"/>, schema 61) captured from <see cref="ObjectRecords"/>, the directory's store of those
/// records; schema 60 saved the same rows inside <see cref="SessionInventoryState"/> and the 60 to 61 upgrade step moves them.
/// </summary>
public sealed class SessionObjectRecords
{
    /// <summary>No junk pile, no pool and no number given.</summary>
    internal static SessionObjectRecords Empty { get; } = new([], [], 0, 0);

    internal SessionObjectRecords(SessionJunkPile[] junkPiles, SessionBloodPool[] bloodPools, int lastJunkNumber, int lastPoolNumber,
        SessionObjectField[]? state = null)
    {
        State = Array.AsReadOnly(state ?? []);
        JunkPiles = Array.AsReadOnly(junkPiles);
        BloodPools = Array.AsReadOnly(bloodPools);
        LastJunkNumber = lastJunkNumber;
        LastPoolNumber = lastPoolNumber;
    }

    /// <summary>ARC-WORLD-063: the junk piles the session made, in the order they were made.</summary>
    public IReadOnlyList<SessionJunkPile> JunkPiles { get; }
    /// <summary>ARC-RENDER-041: the pools of blood the session left, in the order they were left (a decayed one stays until the next
    /// pool is left).</summary>
    public IReadOnlyList<SessionBloodPool> BloodPools { get; }
    /// <summary>ARC-DEV-084 (MIG6, TM1): the highest junk pile number the session ever gave (0 when none), so a new pile never takes the
    /// number of one that was dissolved: an object id is never reassigned.</summary>
    public int LastJunkNumber { get; }
    /// <summary>ARC-DEV-084 (MIG6, TM1): the highest blood pool number the session ever gave (0 when none), so a new pool never takes the
    /// number of one that decayed.</summary>
    public int LastPoolNumber { get; }
    /// <summary>ARC-DEV-092 (MIG7a, TM2, GR5, schema 62): the recorded fields of mobile map-data objects, by entity then property (ordinal): the
    /// state of the directory entries, kept by the object's identity and not under the map it stands on. Schema 61 saved these as entity changes in
    /// <see cref="GameSessionSnapshot.Maps"/>; the 61 to 62 upgrade step moves them.</summary>
    public IReadOnlyList<SessionObjectField> State { get; }

    internal SessionObjectRecords WithState(SessionObjectField[] state) => new([.. JunkPiles], [.. BloodPools], LastJunkNumber, LastPoolNumber, state);
}

/// <summary>
/// ARC-DEV-091 (MIG6d-3, TM2, GR4, GR17): the object directory's records of the junk piles and the pools of blood, found by entity in one
/// step. It replaces the two lists <c>JunkPiles</c> and <c>BloodPools</c> and the two marks of <see cref="InventoryDomain"/>, which found
/// the pile of an item by a scan of every pile. A pile is a record of its own (entity, map, cell, items in the pile's order); an item
/// is in at most one pile, and the record index answers "which pile holds this item". Records keep the order they were made in.
/// Mutations go through here only, so the item index never drifts from the piles.
/// </summary>
internal sealed class ObjectRecords
{
    private readonly List<SessionJunkPile> _piles = [];
    private readonly Dictionary<string, SessionJunkPile> _pileByEntity = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pileOfItem = new(StringComparer.Ordinal);
    private readonly List<SessionBloodPool> _pools = [];
    private readonly Dictionary<string, SessionBloodPool> _poolByEntity = new(StringComparer.Ordinal);

    /// <summary>The highest junk pile number ever given (the saved mark): a number is never given again.</summary>
    internal int LastJunkNumber { get; set; }
    /// <summary>The highest blood pool number ever given.</summary>
    internal int LastPoolNumber { get; set; }

    /// <summary>The junk piles in the order they were made.</summary>
    internal IReadOnlyList<SessionJunkPile> Piles => _piles;
    /// <summary>The pools of blood in the order they were left.</summary>
    internal IReadOnlyList<SessionBloodPool> Pools => _pools;
    /// <summary>The items all junk piles hold (the bound of <see cref="SessionJunkPile.MaxItems"/>).</summary>
    internal int PileItems => _pileOfItem.Count;

    /// <summary>The junk pile with this entity, or null.</summary>
    internal SessionJunkPile? Pile(string entity) => _pileByEntity.GetValueOrDefault(entity);

    /// <summary>The junk pile that holds the item, or null.</summary>
    internal SessionJunkPile? PileOf(string entity) => _pileOfItem.TryGetValue(entity, out var pile) ? _pileByEntity[pile] : null;

    /// <summary>The pool of blood with this entity, or null (a decayed pool is still here until it is destroyed).</summary>
    internal SessionBloodPool? Pool(string entity) => _poolByEntity.GetValueOrDefault(entity);

    /// <summary>Adds a pile at the end. A pile entity or an item that is recorded already is a broken invariant of the caller.</summary>
    internal void AddPile(SessionJunkPile pile)
    {
        if (_pileByEntity.ContainsKey(pile.Entity)) throw new InvalidOperationException("JUNK_PILE_TWICE");
        if (pile.Items.Any(item => _pileOfItem.ContainsKey(item.Entity))) throw new InvalidOperationException("ITEM_PILED_TWICE");
        _piles.Add(pile);
        _pileByEntity[pile.Entity] = pile;
        foreach (var item in pile.Items) _pileOfItem[item.Entity] = pile.Entity;
    }

    /// <summary>Replaces the record of a pile (same entity, same place in the order) with new items.</summary>
    internal void ReplacePile(SessionJunkPile pile)
    {
        var at = _piles.FindIndex(held => held.Entity == pile.Entity);
        if (at < 0) throw new InvalidOperationException("JUNK_PILE_GONE");
        if (pile.Items.Any(item => _pileOfItem.TryGetValue(item.Entity, out var holder) && holder != pile.Entity))
            throw new InvalidOperationException("ITEM_PILED_TWICE");
        foreach (var item in _piles[at].Items) _pileOfItem.Remove(item.Entity);
        _piles[at] = pile;
        _pileByEntity[pile.Entity] = pile;
        foreach (var item in pile.Items) _pileOfItem[item.Entity] = pile.Entity;
    }

    /// <summary>Removes a pile's record; its items stay wherever their own records put them. False when there is none.</summary>
    internal bool RemovePile(string entity)
    {
        if (!_pileByEntity.Remove(entity, out var pile)) return false;
        _piles.RemoveAll(held => held.Entity == entity);
        foreach (var item in pile.Items) _pileOfItem.Remove(item.Entity);
        return true;
    }

    /// <summary>Takes the item out of its junk pile; returns the pile as it is now, or null when no pile held it.</summary>
    internal SessionJunkPile? LeaveItem(string entity)
    {
        if (PileOf(entity) is not { } pile) return null;
        var left = pile with { Items = Array.AsReadOnly(pile.Items.Where(item => item.Entity != entity).ToArray()) };
        ReplacePile(left);
        return left;
    }

    /// <summary>Adds a pool at the end; a pool entity recorded already is a broken invariant of the caller.</summary>
    internal void AddPool(SessionBloodPool pool)
    {
        if (!_poolByEntity.TryAdd(pool.Entity, pool)) throw new InvalidOperationException("BLOOD_POOL_TWICE");
        _pools.Add(pool);
    }

    /// <summary>Removes a pool's record; false when there is none.</summary>
    internal bool RemovePool(string entity)
    {
        if (!_poolByEntity.Remove(entity)) return false;
        _pools.RemoveAll(held => held.Entity == entity);
        return true;
    }

    /// <summary>The records as the snapshot saves them.</summary>
    internal SessionObjectRecords Capture() => new([.. _piles], [.. _pools], LastJunkNumber, LastPoolNumber);
}
