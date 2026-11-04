namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-088 (MIG6d, TM2, GR17): the lists of made, given and gold items the session added to an NPC or to a container (a merchant's
/// substitute container a barter window filled), kept as directory entries: the entry of an item is its holder and its place in the
/// holder's list, found by the item's entity in one step; the list of a holder is the entries that name it, in the order they entered.
/// What the entry alone does not know stays in its owner's stores (kind and complexity on <see cref="SessionMadeItem"/>, ammunition
/// units in <see cref="InventoryDomain.Ammo"/>, the given item's origin in <see cref="InventoryDomain.Given"/>). It replaces the two stores
/// <c>Npcs</c> (holder to list) and <c>Containers</c> (which of those keys are containers), which found an item's holder by a scan of every list.
/// An item is held by one holder at a time; a holder with no item has no list, so no entry.
/// </summary>
internal sealed class HolderLists
{
    private sealed class Held
    {
        internal List<SessionMadeItem> Items { get; } = [];
        internal bool Container { get; set; }
    }

    private readonly SortedDictionary<string, Held> _lists = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _holderOf = new(StringComparer.Ordinal);

    /// <summary>The items held by every holder (the bound of <see cref="GameSessionLimits.MaxNpcItems"/>).</summary>
    internal int Count => _holderOf.Count;

    /// <summary>Whether the holder has a list: an NPC or a container that holds an added item.</summary>
    internal bool HasList(string holder) => _lists.ContainsKey(holder);

    /// <summary>Whether the holder's list is a container's (a barter window's sales).</summary>
    internal bool IsContainer(string holder) => _lists.TryGetValue(holder, out var held) && held.Container;

    /// <summary>The holder of the item, or null when no list holds it.</summary>
    internal string? HolderOf(string entity) => _holderOf.GetValueOrDefault(entity);

    /// <summary>The items of a holder's list in order (empty when it has none).</summary>
    internal IReadOnlyList<SessionMadeItem> Of(string holder) => _lists.TryGetValue(holder, out var held) ? held.Items : [];

    /// <summary>Every held item, holders in ordinal order and each list in order.</summary>
    internal IEnumerable<SessionMadeItem> All => _lists.Values.SelectMany(held => held.Items);

    /// <summary>The holders with a list, in ordinal order.</summary>
    internal IEnumerable<string> Holders => _lists.Keys;

    /// <summary>Puts an item at the end of a holder's list; <paramref name="container"/> marks the list as a container's. An item held
    /// already is a broken invariant of the caller (every move takes the item out of its list first).</summary>
    internal void Add(string holder, SessionMadeItem item, bool container)
    {
        if (!_holderOf.TryAdd(item.Entity, holder)) throw new InvalidOperationException("ENTITY_HELD_TWICE");
        if (!_lists.TryGetValue(holder, out var held)) _lists[holder] = held = new();
        held.Items.Add(item);
        if (container) held.Container = true;
    }

    /// <summary>Takes an item out of its holder's list; an emptied list is dropped with its container mark. False when no list held it.</summary>
    internal bool Remove(string entity)
    {
        if (!_holderOf.Remove(entity, out var holder)) return false;
        var held = _lists[holder];
        held.Items.RemoveAll(item => item.Entity == entity);
        if (held.Items.Count == 0) _lists.Remove(holder);
        return true;
    }

    /// <summary>The lists as the snapshot saves them: holders in ordinal order, each with its items in order.</summary>
    internal SessionNpcInventory[] Capture() =>
        [.. _lists.Select(pair => new SessionNpcInventory(pair.Key, Array.AsReadOnly(pair.Value.Items.ToArray())) { Container = pair.Value.Container })];
}
