namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-090 (MIG6d-2, TM2, GR17): the hero's own lists as directory entries, one store with an index by entity. An entry is an item the
/// hero owns in one place: <see cref="HeroPlace.Kit"/> (the starting kit), <see cref="HeroPlace.Pc"/> (a taken map item, with its origin map),
/// <see cref="HeroPlace.Made"/> (a made item) or <see cref="HeroPlace.Placed"/> (an item the hero put on the ground, or a script created
/// there, with its map and cell); the places an item can lie in are found by the item's entity in one step, so no caller scans four lists.
/// Beside them the given records (ARC-WORLD-071): the kit and taken items the hero gave to NPCs, whose list entry is in <see cref="HolderLists"/>
/// and whose origin only this record keeps; a record is found by the entity too. It replaces the stores <c>Kit</c>, <c>Pc</c>, <c>Made</c>,
/// <c>Placed</c> and <c>Given</c> of <see cref="InventoryDomain"/>. What the entry alone does not know stays in its owner's stores (weight facts,
/// ammunition units, the taken mark, the grid slot). An item is in one place at a time; a second entry of an entity is a broken invariant of the
/// caller (every move takes the item out of its place first), and a restore already refuses it before it reaches the lists.
/// </summary>
internal sealed class HeroLists
{
    private readonly List<SessionKitItem> _kit = [];
    private readonly List<SessionPcItem> _pc = [];
    private readonly List<SessionMadeItem> _made = [];
    private readonly List<SessionPlacedItem> _placed = [];
    private readonly Dictionary<string, HeroPlace> _placeOf = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, SessionGivenItem> _given = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Arcanum.Simulation.Movement.CellCoord> _lastDrops = new(StringComparer.Ordinal);

    /// <summary>ARC-SCRIPT-2594 (IV8): how many last-drop tiles the session keeps at once (the placed rows' bound: the hero's items and the script-created ones).</summary>
    internal const int MaxLastDrops = GameSessionLimits.PolicyMaxPcItems + GameSessionLimits.PolicyMaxCreatedItems;

    /// <summary>
    /// ARC-SCRIPT-2594 (IV8): the tile an item last lay on after the session dropped it (or a script created it there) and a pickup took it up again, by entity in ordinal order. The original keeps the location field of a
    /// held item (only a drop, an animation parameter and a cross-map teleport rewrite it), so an insert-item script's vicinity scans centre on this tile. The tile of an item still on the ground is its placed cell, not here.
    /// </summary>
    internal IReadOnlyDictionary<string, Arcanum.Simulation.Movement.CellCoord> LastDrops => _lastDrops;

    /// <summary>Puts a saved last-drop tile back (the restore); false for an entity that already has one.</summary>
    internal bool RestoreLastDrop(string entity, Arcanum.Simulation.Movement.CellCoord cell) => _lastDrops.TryAdd(entity, cell);

    /// <summary>Forgets the last-drop tile of an item that ceased to exist.</summary>
    internal void ForgetLastDrop(string entity) => _lastDrops.Remove(entity);

    /// <summary>The starting kit in kit-number order as the hero keeps it.</summary>
    internal IReadOnlyList<SessionKitItem> Kit => _kit;

    /// <summary>The taken map items in the order they were taken.</summary>
    internal IReadOnlyList<SessionPcItem> Pc => _pc;

    /// <summary>The made items in the order they entered.</summary>
    internal IReadOnlyList<SessionMadeItem> Made => _made;

    /// <summary>The items the hero placed on the ground or a script created there, in the order they were placed.</summary>
    internal IReadOnlyList<SessionPlacedItem> Placed => _placed;

    /// <summary>The place of the item among the hero's lists, or null when none holds it.</summary>
    internal HeroPlace? PlaceOf(string entity) => _placeOf.TryGetValue(entity, out var place) ? place : null;

    /// <summary>Whether the item is in the hero's pack: the kit, the taken or the made items (a worn item too; a placed one is on the ground).</summary>
    internal bool InPack(string entity) => _placeOf.TryGetValue(entity, out var place) && place != HeroPlace.Placed;

    /// <summary>The kit item with this entity, or null.</summary>
    internal SessionKitItem? KitItem(string entity) => PlaceOf(entity) == HeroPlace.Kit ? _kit.Find(item => item.Entity == entity) : null;

    /// <summary>The taken item with this entity, or null.</summary>
    internal SessionPcItem? PcItem(string entity) => PlaceOf(entity) == HeroPlace.Pc ? _pc.Find(item => item.Entity == entity) : null;

    /// <summary>The made item with this entity, or null.</summary>
    internal SessionMadeItem? MadeItem(string entity) => PlaceOf(entity) == HeroPlace.Made ? _made.Find(item => item.Entity == entity) : null;

    /// <summary>The placed item with this entity, or null.</summary>
    internal SessionPlacedItem? PlacedItem(string entity) => PlaceOf(entity) == HeroPlace.Placed ? _placed.Find(item => item.Entity == entity) : null;

    /// <summary>The kind of a pack item (kit, taken or made), or null when the item is not in the pack.</summary>
    internal SessionItemKind? PackKind(string entity) => PlaceOf(entity) switch
    {
        HeroPlace.Kit => KitItem(entity)!.Value.Kind,
        HeroPlace.Pc => PcItem(entity)!.Value.Kind,
        HeroPlace.Made => MadeItem(entity)!.Value.Kind,
        _ => null,
    };

    /// <summary>Every pack item (kit, then taken, then made) as entity and kind.</summary>
    internal IEnumerable<(string Entity, SessionItemKind Kind)> PackKinds =>
        _kit.Select(item => (item.Entity, item.Kind)).Concat(_pc.Select(item => (item.Entity, item.Kind))).Concat(_made.Select(item => (item.Entity, item.Kind)));

    /// <summary>Puts a kit item at the end of the kit.</summary>
    internal void AddKit(SessionKitItem item) { Enter(item.Entity, HeroPlace.Kit); _kit.Add(item); }

    /// <summary>Puts a kit item at its kit number's place (the kit stays in number order).</summary>
    internal void InsertKit(SessionKitItem item)
    {
        var number = HeroDomain.KitNumber(item.Entity);
        var at = _kit.FindIndex(kit => HeroDomain.KitNumber(kit.Entity) > number);
        Enter(item.Entity, HeroPlace.Kit);
        _kit.Insert(at < 0 ? _kit.Count : at, item);
    }

    /// <summary>Puts a taken item at the end of the taken items.</summary>
    internal void AddPc(SessionPcItem item) { Enter(item.Entity, HeroPlace.Pc); _pc.Add(item); }

    /// <summary>Puts a made item at the end of the made items.</summary>
    internal void AddMade(SessionMadeItem item) { Enter(item.Entity, HeroPlace.Made); _made.Add(item); }

    /// <summary>Puts an item on the ground at the end of the placed items.</summary>
    internal void AddPlaced(SessionPlacedItem item) { Enter(item.Entity, HeroPlace.Placed); _placed.Add(item); _lastDrops.Remove(item.Entity); }

    /// <summary>Replaces a placed item's record (a thrown item that landed) keeping its place in the order; false when it is not placed.</summary>
    internal bool ReplacePlaced(SessionPlacedItem item)
    {
        var at = PlaceOf(item.Entity) == HeroPlace.Placed ? _placed.FindIndex(placed => placed.Entity == item.Entity) : -1;
        if (at < 0) return false;
        _placed[at] = item;
        return true;
    }

    /// <summary>Takes an item out of whichever of the hero's lists holds it; the place it left, or null when none held it.</summary>
    internal HeroPlace? Remove(string entity)
    {
        if (!_placeOf.Remove(entity, out var place)) return null;
        switch (place)
        {
            case HeroPlace.Kit: _kit.RemoveAll(item => item.Entity == entity); break;
            case HeroPlace.Pc: _pc.RemoveAll(item => item.Entity == entity); break;
            case HeroPlace.Made: _made.RemoveAll(item => item.Entity == entity); break;
            default:
                // ARC-SCRIPT-2594 (IV8): an item that leaves the ground (a pickup, an NPC's pickup or a give, a destroy) keeps the tile it lay on as its location field; a destroy forgets it again.
                if (_placed.Find(item => item.Entity == entity) is { } lying && (_lastDrops.Count < MaxLastDrops || _lastDrops.ContainsKey(entity))) _lastDrops[entity] = lying.Cell;
                _placed.RemoveAll(item => item.Entity == entity);
                break;
        }
        return place;
    }

    /// <summary>The kit, taken and made items, and the placed ones that are not script-created (the bound of
    /// <see cref="GameSessionLimits.MaxPcItems"/>): what the hero owns.</summary>
    internal int Held => _kit.Count + _pc.Count + _made.Count + _placed.Count(item => item.Source != SessionPlacedSource.Created);

    /// <summary>The items scripts created on the ground that lie there still.</summary>
    internal int Created => _placed.Count(item => item.Source == SessionPlacedSource.Created);

    /// <summary>Whether the hero gave the item to an NPC: it has a given record.</summary>
    internal bool IsGiven(string entity) => _given.ContainsKey(entity);

    /// <summary>The given record of the item, or null.</summary>
    internal SessionGivenItem? GivenOf(string entity) => _given.TryGetValue(entity, out var given) ? given : null;

    /// <summary>Records that the hero gave the item to an NPC (the record of an item given before is replaced).</summary>
    internal void SetGiven(SessionGivenItem given) => _given[given.Entity] = given;

    /// <summary>Takes the given record of the item; the record, or null when there was none.</summary>
    internal SessionGivenItem? TakeGiven(string entity) => _given.Remove(entity, out var given) ? given : null;

    /// <summary>The given records in entity order.</summary>
    internal IEnumerable<SessionGivenItem> GivenAll => _given.Values;

    /// <summary>The number of given records.</summary>
    internal int GivenCount => _given.Count;

    private void Enter(string entity, HeroPlace place)
    {
        if (!_placeOf.TryAdd(entity, place)) throw new InvalidOperationException("ENTITY_HELD_TWICE");
    }
}

/// <summary>ARC-DEV-090: where among the hero's lists an item is (<see cref="HeroLists"/>).</summary>
internal enum HeroPlace
{
    Kit,
    Pc,
    Made,
    Placed,
}
