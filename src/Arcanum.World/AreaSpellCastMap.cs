using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Magic;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-014: the AREA map as a cast reads it (<see cref="ISpellCastMap"/>), for any map the viewer opens. Objects are the map's
/// rows — an object with a session entity (an NPC, a container, an item: the inventory seed) under that id, any other under
/// <c>row:</c> kind and source index — on their world cells (a carried item, object flag 0x1000, on none), the hero (<see cref="SessionSpellCaster.Hero"/>) on its live cell, and (ARC-DEV-259) an item the session lists as lying on the map, from the session's entry for it. An NPC's facts
/// are its combat values (<see cref="AreaCombatHost"/>) and stored fields with its life in the session; the line of fire is the
/// original's (<see cref="LineOfFireRules.Trace"/>, ARC-COMBAT-012) over the map's live <see cref="AreaLineOfFire"/>.
/// Stand-ins, each documented in the contract: the sector kind is one for the whole map, a path exists to every admitted tile, NPCs other than
/// the companions stand on their map cells (a companion where it stands, none while on another map: ARC-MAGIC-021), and an NPC wields no weapon for a spell's context. Damage to objects other than critters reaches doors, windows, containers, scenery and traps (ARC-DEV-255) and items lying on the ground or worn (ARC-COMBAT-025).
/// </summary>
public sealed class AreaSpellCastMap : ISpellCastMap
{
    private const int ObjectFlagsSlot = 18, SpellFlagsSlot = 19, PcType = 15, NpcType = 16;
    private const int DestroyedOrOff = 0x3;

    private readonly GameSessionOwner _session;
    private readonly int _map;
    private readonly AreaStepSnapshot _steps;
    private readonly AreaCombatHost _combat;
    private readonly Func<CellCoord?> _heroCell;
    private readonly Func<AreaLineOfFire?> _lineOfFire;
    private readonly Func<AreaObjectKey, bool> _gone;
    private readonly Func<IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?>> _placed;
    private readonly Func<string, bool> _held;
    private readonly Func<IReadOnlyList<(string Entity, AreaObjectRecord Row)>>? _visitors;
    private readonly Func<AreaObjectRecord, AreaObjectAttackTarget?>? _objects;
    private readonly Func<IReadOnlyList<SessionWearItem?>?>? _heroKit;
    // ARC-COMBAT-025: the holder of each item a worn-items query listed (the acid's steps ask the item's combat values and damage by its id after it).
    private readonly Dictionary<string, string> _wornHolder = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AreaObjectRecord> _rows = new(StringComparer.Ordinal);
    // The rows by their world cell, and each row's stored fields read once.
    private readonly Dictionary<(long X, long Y), List<string>> _cells = [];
    private readonly Dictionary<string, CombatFields?> _fields = new(StringComparer.Ordinal);
    private readonly List<string> _npcs = [];
    // The ids of the rows in the cell index, by their key.
    private readonly Dictionary<AreaObjectKey, string> _keys = [];
    // ARC-DEV-076: the visiting companions now listed, by their row's key (they stand only where `placed` says, never on a home-map cell).
    private readonly Dictionary<AreaObjectKey, string> _visitorKeys = [];

    /// <param name="session">The session (life, companions, the hero).</param>
    /// <param name="runtimeMap">The runtime map the rows belong to.</param>
    /// <param name="steps">The map's step evidence (its admitted tiles are the map).</param>
    /// <param name="rows">The map's object rows.</param>
    /// <param name="combat">The NPCs' combat values of the map.</param>
    /// <param name="heroCell">The hero's cell now (null: not on the map).</param>
    /// <param name="lineOfFire">The map's line of fire now (null: none, every line is clear).</param>
    /// <param name="gone">Whether the session destroyed or switched off a row (none when null).</param>
    /// <param name="placed">ARC-MAGIC-021: the rows that stand elsewhere than their map cell now (the companions: their live cell, or
    /// null while on another map), asked at each query (none when null).</param>
    /// <param name="held">ARC-UI-016: whether the hero holds the item a row's id names now (taken from the map, listed or worn); such a
    /// row is no map object, so the cast host's item lookup answers for it as an item in the hero's inventory (none when null).</param>
    /// <param name="objects">ARC-DEV-255: the damage baseline of a row that is a portal, container, scenery or trap, as the object-attack host
    /// names it now (null for any other row, or when it is destroyed or off), so spell, script, burn and trap damage reach doors and containers
    /// through <see cref="GameSessionOwner.DamageObject"/> (none when null: such damage is not ready).</param>
    /// <param name="heroKit">ARC-COMBAT-025: the durability values of the hero's starting kit (<see cref="AreaStartingHero.KitDurability"/>), so the items the hero
    /// wears answer an acid and take its damage (none when null: the hero's items are not reached).</param>
    /// <param name="visitors">ARC-DEV-076 (MIG3): the NPCs standing on this map whose rows belong to another map (the companions carried
    /// from their home map), each with its entity and its row, asked at each query. They are targets, casters' party and critters like
    /// the map's own NPCs, found by identity through the combat host's resolver; they stand where <paramref name="placed"/> says (none:
    /// not on the map), never on their home-map cell (none when null).</param>
    public AreaSpellCastMap(GameSessionOwner session, int runtimeMap, AreaStepSnapshot steps, IEnumerable<AreaObjectRecord> rows, AreaCombatHost combat,
        Func<CellCoord?> heroCell, Func<AreaLineOfFire?> lineOfFire, Func<AreaObjectKey, bool>? gone = null,
        Func<IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?>>? placed = null, Func<string, bool>? held = null,
        Func<IReadOnlyList<(string Entity, AreaObjectRecord Row)>>? visitors = null, Func<AreaObjectRecord, AreaObjectAttackTarget?>? objects = null,
        Func<IReadOnlyList<SessionWearItem?>?>? heroKit = null)
    {
        _visitors = visitors;
        _objects = objects;
        _heroKit = heroKit;
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _map = runtimeMap;
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        ArgumentNullException.ThrowIfNull(rows);
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
        _heroCell = heroCell ?? throw new ArgumentNullException(nameof(heroCell));
        _lineOfFire = lineOfFire ?? throw new ArgumentNullException(nameof(lineOfFire));
        _gone = gone ?? (_ => false);
        _placed = placed ?? (() => new Dictionary<AreaObjectKey, (long X, long Y)?>());
        _held = held ?? (_ => false);
        foreach (var row in rows)
        {
            // A carried item (object flag 0x1000) stands in no sector list, whatever location it stores.
            if (row.WorldX is not { } x || row.WorldY is not { } y) continue;
            var id = Id(row);
            _rows[id] = row;
            if ((Flags(row, ObjectFlagsSlot) & SpellTargetObject.Inventory) != 0) continue;
            if (!_cells.TryGetValue((x, y), out var list)) _cells[(x, y)] = list = [];
            list.Add(id);
            _keys[row.Key] = id;
            if (row.Type == NpcType) _npcs.Add(id);
        }
    }

    /// <summary>The cast's id of a map row: its session entity, else <c>row:</c> kind and source index.</summary>
    public string Id(AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        // ARC-DEV-255: a static row of a sector is told by its sector (the entity a script names it by, AreaUseScriptHost.EntityId): the source
        // index alone repeats from sector to sector, so two statics shared one id and a damage hit the wrong one.
        return _combat.Entity(_map, row.Key) ?? _visitorKeys.GetValueOrDefault(row.Key)
            ?? (row.Key.Kind == AreaSourceKind.Static && row.Key.Sector is not null ? AreaUseScriptHost.EntityId(row) : $"row:{row.Key.Kind}:{row.Key.SourceIndex}");
    }

    // ARC-DEV-076 (MIG3): the companions carried here from another map join the rows by their entity (never the cell index: their row's
    // cell is on their home map; the placed rows say where they stand).
    private void SyncVisitors()
    {
        if (_visitors is null) return;
        var now = _visitors();
        // A companion that is no longer carried here leaves the rows (it would otherwise stand on its home-map cell).
        foreach (var (key, entity) in _visitorKeys.ToArray())
            if (!now.Any(visitor => visitor.Entity == entity))
            {
                _visitorKeys.Remove(key); _rows.Remove(entity); _keys.Remove(key); _npcs.Remove(entity);
            }
        foreach (var (entity, row) in now)
        {
            if (_rows.ContainsKey(entity)) continue;
            _rows[entity] = row;
            _keys[row.Key] = entity;
            _visitorKeys[row.Key] = entity;
            _npcs.Add(entity);
        }
    }

    /// <summary>ARC-MAGIC-021: the cast's id of a row by its key, or null (a carried item). ARC-DEV-076 (MIG3): a visiting companion's
    /// row, another map's, is found too (by its entity).</summary>
    public string? IdOf(AreaObjectKey key)
    {
        SyncVisitors();
        return _keys.GetValueOrDefault(key);
    }

    /// <summary>ARC-MAGIC-015: the map row behind a cast's object id, or null (the hero, or no such row).</summary>
    public AreaObjectRecord? Row(string id)
    {
        SyncVisitors();
        return _rows.GetValueOrDefault(id ?? throw new ArgumentNullException(nameof(id)));
    }

    /// <summary>The map location of a cell.</summary>
    public static SpellLocation Location(CellCoord cell) => new(checked((uint)cell.X), checked((uint)cell.Y));

    // ---- ISpellTargetWorld ----

    public SpellTargetObject? Object(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id == SessionSpellCaster.Hero) return HeroObject();
        if (LyingItem(id) is { } lying) return lying;
        SyncVisitors();
        // ARC-UI-031: a carried item (object flag 0x1000) is no map object either: the cast host's item lookup places it at its holder.
        return !_held(id) && _rows.TryGetValue(id, out var row) && (Flags(row, ObjectFlagsSlot) & SpellTargetObject.Inventory) == 0
            && Cell(row, _placed()) is { } cell ? RowObject(id, row, cell) : DroppedItem(id);
    }

    // ARC-DEV-262: an NPC's seed item that lies on this map where it was dropped or thrown (its row still carries the carried flag the NPC's list
    // gave it) is a map object on that cell, like the hero's lying item: a grenade an NPC threw explodes there and destroys itself.
    private SpellTargetObject? DroppedItem(string id)
    {
        if (_session.QueryDroppedItem(id) is not { } dropped || dropped.Map.RuntimeMap != _map || _session.QueryItemInFlight(id)) return null;
        return new(id, (int)dropped.Item.Facts.Kind.Type, Location(dropped.Cell), HitPoints: 1);
    }

    // ARC-DEV-259 (B14): an item of the hero's the session lists as placed on this map, and not in flight, is a map object on its cell: a thrown
    // grenade is its own parent (0x450A50) and source when its shot's end fires the item event (0x4CBDB0), so the cast host must find it. The
    // item's map row may still carry the carried flag (0x1000) the hero's pick-up left, and a kit or made item has no row at all, so the
    // object comes from the session's placed entry (kind and cell), never from the row's flags.
    private SpellTargetObject? LyingItem(string id)
    {
        foreach (var placed in _session.QueryPlacedItems())
        {
            if (placed.Entity != id) continue;
            if (placed.Map.RuntimeMap != _map || _session.QueryItemInFlight(id) || _session.IsItemDestroyed(id)) return null;
            return new(id, (int)placed.Kind.Type, Location(placed.Cell), HitPoints: 1);
        }
        return null;
    }

    // A row's cell now: a placed row's live cell (none while on another map), any other its map cell.
    private (long X, long Y)? Cell(AreaObjectRecord row, IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?> placed) =>
        placed.TryGetValue(row.Key, out var live) ? live : _visitorKeys.ContainsKey(row.Key) ? null : (row.WorldX!.Value, row.WorldY!.Value);

    public IReadOnlyList<SpellTargetObject> ObjectsInSquare(SpellLocation centre, int radius, int typeMask)
    {
        var found = new List<SpellTargetObject>();
        if (radius < 0) return found;
        SyncVisitors();
        if ((typeMask & (1 << PcType)) != 0 && HeroObject() is { } hero
            && Math.Max(Math.Abs((long)hero.Location.X - centre.X), Math.Abs((long)hero.Location.Y - centre.Y)) <= radius) found.Add(hero);
        var placed = _placed();
        for (long y = (long)centre.Y - radius; y <= (long)centre.Y + radius; y++)
            for (long x = (long)centre.X - radius; x <= (long)centre.X + radius; x++)
                Add(found, x, y, typeMask, placed);
        AddPlaced(found, placed, typeMask, cell => Math.Max(Math.Abs(cell.X - centre.X), Math.Abs(cell.Y - centre.Y)) <= radius);
        return found;
    }

    public IReadOnlyList<SpellTargetObject> ObjectsAt(SpellLocation tile, int typeMask)
    {
        var found = new List<SpellTargetObject>();
        SyncVisitors();
        if ((typeMask & (1 << PcType)) != 0 && HeroObject() is { } hero && hero.Location == tile) found.Add(hero);
        var placed = _placed();
        Add(found, tile.X, tile.Y, typeMask, placed);
        AddPlaced(found, placed, typeMask, cell => cell.X == tile.X && cell.Y == tile.Y);
        return found;
    }

    // The rows on a map cell, without the placed ones (they are added where they stand, after the cell's own rows).
    private void Add(List<SpellTargetObject> found, long x, long y, int typeMask, IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?> placed)
    {
        if (!_cells.TryGetValue((x, y), out var ids)) return;
        foreach (var id in ids)
            if (!placed.ContainsKey(_rows[id].Key) && Matches(_rows[id], typeMask) && !_held(id)) found.Add(RowObject(id, _rows[id], (x, y)));
    }

    private void AddPlaced(List<SpellTargetObject> found, IReadOnlyDictionary<AreaObjectKey, (long X, long Y)?> placed, int typeMask,
        Func<(long X, long Y), bool> at)
    {
        foreach (var entry in placed)
            if (entry.Value is { } cell && at(cell) && _keys.TryGetValue(entry.Key, out var id) && Matches(_rows[id], typeMask)) found.Add(RowObject(id, _rows[id], cell));
    }

    private static bool Matches(AreaObjectRecord row, int typeMask) => row.Type is >= 0 and < 32 && (typeMask & (1 << row.Type)) != 0;

    public bool TileBlocked(SpellLocation tile) => !_steps.IsAdmitted(new(tile.X, tile.Y));

    public bool PathExists(SpellTargetObject source, SpellLocation tile) => !TileBlocked(tile);

    public int SectorKind(SpellLocation location) => 0;

    /// <summary>The original's direction of a tile offset (0x4B8D50): the octant of the line, a 2:1 slope rule deciding between a
    /// straight and a diagonal step; no offset answers 5.</summary>
    public static int Direction(long dx, long dy)
    {
        int sx = Math.Sign(dx), sy = Math.Sign(dy);
        if (Math.Abs(dx) > 2 * Math.Abs(dy)) sy = 0;
        if (Math.Abs(dy) > 2 * Math.Abs(dx)) sx = 0;
        return (sx, sy) switch
        {
            (-1, -1) => 0, (-1, 0) => 1, (-1, 1) => 2, (0, 1) => 3, (1, 1) => 4, (1, 0) => 5, (1, -1) => 6, (0, -1) => 7, _ => 5,
        };
    }

    public int Direction(SpellLocation from, SpellLocation to) => Direction((long)to.X - from.X, (long)to.Y - from.Y);

    public IReadOnlyList<SpellTargetObject> Party(SpellTargetObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Id != SessionSpellCaster.Hero) return [];
        SyncVisitors();
        return _npcs.Where(id => _session.QueryCompanion(id).LedByPc).Select(Object).OfType<SpellTargetObject>().ToArray();
    }

    // ---- ISpellCastMap ----

    public SpellLineOfFire LineOfFire(SpellObjectFacts parent, SpellLocation tile)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (_lineOfFire() is not { } map) return new(0, null);
        var trace = LineOfFireRules.Trace(new(checked((int)parent.Location.X), checked((int)parent.Location.Y)), 0, 0,
            new(checked((int)tile.X), checked((int)tile.Y)), map, _session.Rules.Combat);
        var blocker = trace.Blocker is { } row && row.WorldX is { } x && row.WorldY is { } y
            ? new SpellObjectFacts(Id(row), row.Type, new(checked((uint)x), checked((uint)y)), Flags(row, ObjectFlagsSlot), Flags(row, SpellFlagsSlot))
            : null;
        return new(trace.LineFailed ? LineOfFireRules.FailedLineCost : trace.Cost, blocker);
    }

    public SpellCastCritter? Critter(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        SyncVisitors();
        if (entity == SessionSpellCaster.Hero || !_rows.TryGetValue(entity, out var row) || row.Type != NpcType) return null;
        // ARC-MAGIC-021: a companion on another map is not on this one.
        if (Cell(row, _placed()) is null) return null;
        // ARC-DEV-076 (MIG3): by identity (a carried companion's record is its home map's).
        if (_combat.Build(_session, entity).Combat is not { } combat || Fields(entity, row) is not { } fields) return null;
        var resistances = fields.Object.Resistances;
        return new(combat, resistances.Count > 4 ? resistances[4] : 0, unchecked((int)fields.Object.Flags), unchecked((int)fields.Object.SpellFlags),
            unchecked((int)(fields.Critter?.Flags ?? 0)), unchecked((int)(fields.Critter?.Flags2 ?? 0)))
        {
            // ARC-WORLD-102: the college ranks and mastery its fields store.
            Book = fields.Critter is { } critter ? SpellBookRules.FromFields(critter.SpellTech) : null,
        };
    }

    public CombatWeapon WieldedWeapon(string entity) => CombatWeapon.Unarmed();

    public IReadOnlyList<SpellAcidItem> WornItems(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var worn = Worn(entity);
        foreach (var item in worn) _wornHolder[item.Wear.Entity] = entity;
        return worn.Select(item => new SpellAcidItem(item.Wear.Entity, (int)item.Slot, false, Math.Clamp(item.Facts.Resistances.Normal, 0, 95))).ToArray();
    }

    // The items a critter wears now with their durability values and stored facts, in slot order. An NPC's are the session's (ARC-DEV-076, MIG3: by identity,
    // from the worn items' own fields, so a carried companion's items answer as on its home map). The hero's are its starting kit's (ARC-COMBAT-025:
    // the prototype's fields): none while it wears anything else, as the attack's wear takes none then (HeroWorn). An item whose facts do not read is left out.
    private WornItem[] Worn(string entity)
    {
        var items = new List<WornItem>();
        if (entity == SessionSpellCaster.Hero)
        {
            if (AreaCombatHost.HeroWorn(_session, _heroKit?.Invoke()) is { } heroWorn)
                foreach (var (slot, wear) in heroWorn.OrderBy(p => (int)p.Key))
                    if (_combat.KitItemFacts(_session, wear.Entity) is { } facts) items.Add(new(slot, wear, facts));
        }
        else if (_combat.Worn(_session, entity) is { } worn)
            foreach (var item in worn.OrderBy(item => (int)item.Slot))
                if (SessionObjectFacts.ItemFacts(item.Fields) is { } facts) items.Add(new(item.Slot, item.Wear, facts));
        return [.. items];
    }

    private readonly record struct WornItem(Arcanum.Simulation.Equipment.EquipmentSlot Slot, SessionWearItem Wear, SessionObjectBaseline Facts);

    // ARC-DEV-255 (GT195): a door, window, container, scenery or trap row takes damage through the session's object rules (material reduction,
    // hit points, the break at 0). A row the object-attack host does not name now (an item, an NPC, a wall, a destroyed or off object) takes none.
    public (Combatant Combatant, AttackTargetKind Kind)? ObjectCombat(string id) =>
        ObjectTarget(id) is { } target ? _session.QueryObjectCombat(target)
        : ItemTarget(id) is { } item ? _session.QueryItemCombat(item.Wear, item.Facts) : null;

    public bool DamageObject(string id, AttackDamage damage)
    {
        ArgumentNullException.ThrowIfNull(damage);
        if (ObjectTarget(id) is { } target)
        {
            var result = _session.DamageObject(_session.Version, target, damage.HitPointDamage);
            if (result.Hit is not { } hit) return false;
            _objectHits.Add(new(id, hit, result.Host));
            return result.Disposition == GameSessionDisposition.Changed;
        }
        // ARC-COMBAT-025 (OD1–OD4, OD12): an item takes the same damage through its durability.
        if (ItemTarget(id) is not { } item) return false;
        var damaged = _session.DamageItemUnattacked(_session.Version, item.Wear, item.Facts, damage.HitPointDamage, item.OnGround);
        if (damaged.Item is not { } outcome) return false;
        _itemHits.Add(new(id, outcome, damaged.Host));
        return damaged.Disposition == GameSessionDisposition.Changed;
    }

    private readonly List<AreaObjectHit> _objectHits = [];

    /// <summary>ARC-DEV-255: the hits the damage reached objects with since the last call (id, outcome, host steps), for the host to log and
    /// show; the objects' state itself is the session's.</summary>
    public IReadOnlyList<AreaObjectHit> TakeObjectHits()
    {
        var hits = _objectHits.ToArray();
        _objectHits.Clear();
        return hits;
    }

    private readonly List<AreaItemHit> _itemHits = [];

    /// <summary>ARC-COMBAT-025: the items lying on <paramref name="cell"/> that damage reaches now (types 5..14 of the cell's rows, not taken or deleted, not in
    /// a container or a junk pile), by cast id in row order, for the burn of a burning object's tile (<see cref="AreaFireBurner"/>).</summary>
    public IReadOnlyList<string> GroundItemsAt(CellCoord cell) =>
        _cells.TryGetValue((cell.X, cell.Y), out var ids)
            ? ids.Where(id => _rows[id].Type is >= 5 and <= 14 && !_gone(_rows[id].Key) && !_held(id) && ItemTarget(id) is { OnGround: true }).ToArray() : [];

    /// <summary>ARC-COMBAT-025: the hits the damage reached items with since the last call (id, durability result, host steps), for the host to log and
    /// show; the items' state itself is the session's.</summary>
    public IReadOnlyList<AreaItemHit> TakeItemHits()
    {
        var hits = _itemHits.ToArray();
        _itemHits.Clear();
        return hits;
    }

    // An item the damage reaches: lying on the shown map's ground (a row the object-attack host names as an item), or worn by a critter whose worn items a
    // query listed (listed again now, so an item taken off since is no target). Items in a pack, a container or a junk pile are not reached by areas.
    private (SessionWearItem Wear, SessionObjectBaseline Facts, bool OnGround)? ItemTarget(string id)
    {
        if (_objects is not null && _rows.TryGetValue(id, out var row) && row.Type is not (NpcType or PcType) &&
            _objects(row) is { Item: { } lying, ItemFacts: { } lyingFacts })
            return (lying, lyingFacts, true);
        if (!_wornHolder.TryGetValue(id, out var holder)) return null;
        foreach (var item in Worn(holder))
            if (item.Wear.Entity == id) return (item.Wear, item.Facts, false);
        return null;
    }

    private SessionObjectTarget? ObjectTarget(string id) =>
        _objects is not null && _rows.TryGetValue(id, out var row) && row.Type is not (NpcType or PcType) ? _objects(row)?.Object : null;

    // ---- facts ----

    private CombatFields? Fields(string id, AreaObjectRecord row)
    {
        if (!_fields.TryGetValue(id, out var fields)) _fields[id] = fields = CombatFieldReader.Read(row).Fields;
        return fields;
    }

    private SpellTargetObject? HeroObject()
    {
        if (_heroCell() is not { } cell || _session.QueryHeroLife() is not { } life) return null;
        var state = life.Record.State;
        return new(SessionSpellCaster.Hero, PcType, Location(cell), HitPoints: life.Status.HitPoints, HitPointDamage: state.HitPointDamage,
            Alignment: _session.QueryHero()?.Alignment ?? 0, Poison: state.PoisonLevel);
    }

    private SpellTargetObject RowObject(string id, AreaObjectRecord row, (long X, long Y) cell)
    {
        var location = new SpellLocation(checked((uint)cell.X), checked((uint)cell.Y));
        var objectFlags = Flags(row, ObjectFlagsSlot) | (_gone(row.Key) ? DestroyedOrOff : 0);
        if (row.Type == NpcType && Critter(id) is { } npc && _session.QueryLife(npc.Combat.Target).View is { } life)
            return new(id, NpcType, location, objectFlags, npc.SpellFlags, npc.CritterFlags, npc.CritterFlags2, life.Status.HitPoints,
                life.Record.State.HitPointDamage, Poison: life.Record.State.PoisonLevel,
                PcLeader: _session.QueryCompanion(id).LedByPc ? SessionSpellCaster.Hero : null);
        return new(id, row.Type, location, objectFlags, Flags(row, SpellFlagsSlot), HitPoints: 1);
    }

    private static int Flags(AreaObjectRecord row, int slot) =>
        unchecked((int)(row.EffectiveFields.FirstOrDefault(f => f.Slot == slot && f.Presence == AreaPresence.Value)?.Number ?? 0));
}

/// <summary>ARC-DEV-255: one hit that reached a door, window, container, scenery or trap: its cast id, the outcome and the host's steps.</summary>
public sealed record AreaObjectHit(string Id, ObjectHitOutcome Hit, SessionObjectHostSteps Host);

/// <summary>ARC-COMBAT-025: one hit that reached an item (lying on the ground or worn): its cast id, the durability result and the host's steps.</summary>
public sealed record AreaItemHit(string Id, ItemDurabilityResult Item, SessionObjectHostSteps Host);
