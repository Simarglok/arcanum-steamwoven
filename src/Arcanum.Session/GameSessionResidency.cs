using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-103 (MIG9 S3, K1 §3.2): the original's object type of a resident, numbered as the executable numbers it (RL1). The sector types are
/// wall, portal, scenery and trap; an object of a sector type is <i>static</i> unless its OF_DYNAMIC flag is set, and a static object is no resident.
/// </summary>
public enum SessionResidentKind
{
    Wall = 0, Portal = 1, Container = 2, Scenery = 3, Projectile = 4, Weapon = 5, Ammo = 6, Armor = 7, Gold = 8, Food = 9, Scroll = 10, Key = 11,
    KeyRing = 12, Written = 13, Generic = 14, Pc = 15, Npc = 16, Trap = 17,
}

/// <summary>ARC-DEV-103: where a resident's entry comes from.</summary>
public enum SessionResidentOrigin
{
    /// <summary>A base row of the open map's data (<see cref="SessionResidentRow"/>), as the session's changes leave it.</summary>
    MapData,
    /// <summary>A map object of another map's data that is on the open map now: an NPC whose companion cell is recorded here (<c>npc.cell</c>), or
    /// an item of another map the hero carries, placed here or gave to a holder here. Its base record is its home map's row.</summary>
    Visitor,
    /// <summary>An object the session made: a kit or made item, a junk pile, a pool of blood.</summary>
    Created,
    /// <summary>The hero (entity <see cref="SessionEventKinds.PlayerEntity"/>).</summary>
    Hero,
}

/// <summary>
/// ARC-DEV-103 (K1 §3.2): a sector of a map, the 64 × 64 cells whose cell coordinates shifted right by six are (<see cref="X"/>, <see cref="Y"/>), as
/// the original's sector id splits a location (RL3, RL4). The residency's own key: the sector model of MIG9 S2 holds its own sector values.
/// </summary>
public readonly record struct ResidentSector(int X, int Y)
{
    /// <summary>Cells per sector side as a shift (64).</summary>
    public const int Shift = 6;

    public static ResidentSector Of(CellCoord cell) => new(cell.X >> Shift, cell.Y >> Shift);
}

/// <summary>
/// ARC-DEV-103 (K1 §3.2): one mobile object of a map's data, as the host read it (World reads the rows; the session cannot reference it): its entity,
/// its type, and either its cell (on the ground) or the holder whose list holds it (OF_INVENTORY: no tile, its stored location is stale, RL10). A row
/// with neither is an item held by nothing the map lists (the data's detached items): it is a resident with no place. <see cref="Dynamic"/> is the
/// OF_DYNAMIC flag (0x2000), which only a sector type needs; <see cref="Off"/> and <see cref="Destroyed"/> are the data's OF_OFF (0x2) and
/// OF_DESTROYED (0x1) flags.
/// </summary>
public sealed record SessionResidentRow(string Entity, SessionResidentKind Kind, CellCoord? Cell, string? Holder = null)
{
    public bool Dynamic { get; init; }
    public bool Off { get; init; }
    public bool Destroyed { get; init; }
}

/// <summary>
/// ARC-DEV-103 (K1 §3.2): a resident of the open map now: its base row with the session's changes applied (where the NPC was left, a companion's cell,
/// a taken, placed, given, dropped, spilled or deleted item, the off and destroyed flags), a visitor, a created object or the hero. <see cref="Cell"/>
/// is null for an object a holder holds (<see cref="Holder"/>) and for a detached item. Off and destroyed residents stay residents (the original's
/// find index keeps them, RL4, RL10); <see cref="Excluded"/> is the game's query exclusion (OF_DESTROYED or OF_OFF, RL9).
/// </summary>
public sealed record SessionResident(string Entity, SessionResidentKind Kind, SessionResidentOrigin Origin, CellCoord? Cell, string? Holder,
    bool Off, bool Destroyed)
{
    /// <summary>The sector of the resident's cell, or null for a held or detached object.</summary>
    public ResidentSector? Sector => Cell is { } cell ? ResidentSector.Of(cell) : null;

    /// <summary>Whether the original's tile and rectangle queries leave it out in the game (exclusion mask 3: OF_DESTROYED or OF_OFF, RL9).</summary>
    public bool Excluded => Off || Destroyed;
}

/// <summary>
/// ARC-DEV-103 (K1 §3.2): the bound of a residency: base rows one map may give. A map with more is refused, never partly held (the resolver's rule).
/// The default keeps 1.5 times the largest installed map's mobile rows: the main world map's 7,399 (2.2 times; the census,
/// <c>docs/world/limits-census.md</c>).
/// </summary>
public sealed record ResidencyLimits(int MaxResidents = ResidencyLimits.DefaultMaxResidents)
{
    public const int DefaultMaxResidents = 16_384;
    public const int PolicyMaxResidents = 1 << 20;

    public bool Valid => MaxResidents is >= 1 and <= PolicyMaxResidents;
}

public enum ResidencyStatus
{
    /// <summary>The residency of the map is open (an open, or a query that answers).</summary>
    Open,
    /// <summary>No residency is open for the map (none was opened, the hero left it, or a restore made a new session).</summary>
    NotOpen,
    /// <summary>The map is not the hero's map: only the open map has residents (K1 §3.1).</summary>
    NotHeroMap,
    /// <summary>More rows than <see cref="ResidencyLimits.MaxResidents"/>; nothing is held.</summary>
    LimitExceeded,
    /// <summary>A row the residency cannot hold (see <see cref="ResidencyOpenResult.Detail"/>); nothing is held.</summary>
    InvalidRows,
}

/// <summary>What <see cref="GameSessionOwner.OpenResidency"/> did: the status, the base rows held and why not.</summary>
public sealed record ResidencyOpenResult(ResidencyStatus Status, int Rows, string Detail);

/// <summary>A residents query's answer: <see cref="ResidencyStatus.Open"/> with the residents in entity order (ordinal), or
/// <see cref="ResidencyStatus.NotOpen"/> with none.</summary>
public sealed record ResidentsQuery(ResidencyStatus Status, IReadOnlyList<SessionResident> Residents);

public sealed partial class GameSessionOwner
{
    /// <summary>The base rows of the open map and their sector index (derived data: not saved, no revision).</summary>
    private sealed class ResidencyState(SessionMapId map, Dictionary<string, SessionResidentRow> rows, Dictionary<ResidentSector, string[]> index,
        Dictionary<string, string[]> heldBy)
    {
        internal SessionMapId Map { get; } = map;
        internal Dictionary<string, SessionResidentRow> Rows { get; } = rows;
        internal Dictionary<ResidentSector, string[]> Index { get; } = index;
        internal Dictionary<string, string[]> HeldBy { get; } = heldBy;
    }

    private ResidencyState? _residency;

    // The per-query lookups of the hero's lists, the holder lists and the party (each a list or a find of its own).
    private sealed record OverlayLookups(IReadOnlyDictionary<string, SessionItemKind> Pack, IReadOnlyDictionary<string, SessionPlacedItem> Placed,
        IReadOnlyDictionary<string, SessionItemKind> Listed, IReadOnlySet<string> Party);

    // The mobile fields that move a resident or change its flags.
    private const string CompanionCellProperty = "npc.cell", AwayMapProperty = "npc.map", OffProperty = "obj.off", DestroyedFieldProperty = "obj.destroyed";

    private static readonly IReadOnlySet<string> ResidentFields = new HashSet<string>(StringComparer.Ordinal)
    {
        CompanionCellProperty, AwayMapProperty, SessionNpcPlacement.Property, OffProperty, DestroyedFieldProperty, ObjectConditionProperty, ItemDroppedProperty, SessionItemMove.Property,
    };

    /// <summary>
    /// ARC-DEV-103 (MIG9 S3, K1 §3.2, RL1, RL4): opens the residency of the open map from its base rows, the map's mobile objects as World read them
    /// (every object of its mobile files: NPCs, containers, items, projectiles and the sector types with OF_DYNAMIC). The session keeps the rows and
    /// a per-sector index of the rows on the ground; the residents a query answers are those rows with the session's changes applied at the time of
    /// the query, so they follow every change without a hook. A residency replaces the previous one; it is derived data (no revision, not saved),
    /// dropped when the hero's map changes and absent in a restored session. A refusal keeps the previous residency. Only the hero's map opens
    /// (<see cref="ResidencyStatus.NotHeroMap"/>). Refused rows (<see cref="ResidencyStatus.InvalidRows"/>): an entity that is no map object (a
    /// session counter's or a static's <c>sector:</c> id) or appears twice, a type outside the original's 18, a static row (a sector type without
    /// OF_DYNAMIC), a PC row (the hero is the session's own resident), a row with both a cell and a holder, a cell outside the session's packing, a
    /// held row that is no item, and a holder that is not a container or NPC row of the same map.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Limits outside their range (a programming error of the caller).</exception>
    public ResidencyOpenResult OpenResidency(SessionMapId map, IReadOnlyList<SessionResidentRow> rows, ResidencyLimits? limits = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rows);
        limits ??= new();
        if (!limits.Valid) throw new ArgumentOutOfRangeException(nameof(limits));
        if (!SessionValidation.Map(map) || map != _actor.Map) return new(ResidencyStatus.NotHeroMap, 0, $"map {map.RuntimeMap} is not the hero's map {_actor.Map.RuntimeMap}");
        if (rows.Count > limits.MaxResidents) return new(ResidencyStatus.LimitExceeded, 0, $"{rows.Count} rows pass the limit of {limits.MaxResidents}");
        var byEntity = new Dictionary<string, SessionResidentRow>(rows.Count, StringComparer.Ordinal);
        var position = new Dictionary<string, int>(rows.Count, StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row is null) return Invalid(i, "null row");
            if (!SessionValidation.Entity(row.Entity) || ObjectOriginOf(row.Entity) != SessionObjectOrigin.MapObject ||
                row.Entity.StartsWith(ObjectStateVocabulary.StaticEntityPrefix, StringComparison.Ordinal) || row.Entity == SessionEventKinds.PlayerEntity)
                return Invalid(i, "entity is no map object");
            if (!Enum.IsDefined(row.Kind)) return Invalid(i, "type outside the original's");
            if (row.Kind == SessionResidentKind.Pc) return Invalid(i, "a PC row");
            if (SectorType(row.Kind) && !row.Dynamic) return Invalid(i, "a static row");
            if (row.Cell is not null && row.Holder is not null) return Invalid(i, "a cell and a holder");
            if (row.Cell is { } cell && !CellFits(cell)) return Invalid(i, "cell outside the packing");
            if (row.Holder is not null && !ItemKind(row.Kind)) return Invalid(i, "a held row that is no item");
            if (!byEntity.TryAdd(row.Entity, row)) return Invalid(i, "entity twice");
            position[row.Entity] = i;
        }
        var heldBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var index = new Dictionary<ResidentSector, List<string>>();
        foreach (var row in byEntity.Values)
        {
            if (row.Holder is { } holder)
            {
                if (!byEntity.TryGetValue(holder, out var of) || of.Kind is not (SessionResidentKind.Container or SessionResidentKind.Npc))
                    return Invalid(position[row.Entity], "holder is no container or NPC row of the map");
                (heldBy.TryGetValue(holder, out var list) ? list : heldBy[holder] = []).Add(row.Entity);
            }
            else if (row.Cell is { } cell)
            {
                var sector = ResidentSector.Of(cell);
                (index.TryGetValue(sector, out var list) ? list : index[sector] = []).Add(row.Entity);
            }
        }
        static string[] Sorted(List<string> list) { var array = list.ToArray(); Array.Sort(array, StringComparer.Ordinal); return array; }
        _sectors = null;
        _residency = new(map, byEntity, index.ToDictionary(pair => pair.Key, pair => Sorted(pair.Value)),
            heldBy.ToDictionary(pair => pair.Key, pair => Sorted(pair.Value), StringComparer.Ordinal));
        return new(ResidencyStatus.Open, byEntity.Count, string.Empty);

        static ResidencyOpenResult Invalid(int index, string why) => new(ResidencyStatus.InvalidRows, 0, $"row {index}: {why}");
    }

    /// <summary>ARC-DEV-103: closes the open residency (the map closes). False when none was open.</summary>
    public bool CloseResidency()
    {
        ThrowIfDisposed();
        var was = _residency is not null;
        _residency = null;
        _sectors = null;
        _pendingHistory = null;
        return was;
    }

    /// <summary>ARC-DEV-103: the map whose residency is open, or null.</summary>
    public SessionMapId? QueryResidencyMap()
    {
        ThrowIfDisposed();
        return Residency(null)?.Map;
    }

    /// <summary>
    /// ARC-DEV-103 (K1 §3.2, RL1): every resident of the open map now, in entity order: the base rows as the session's changes leave them, the
    /// visitors, the objects the session made there, the items the hero and the map's holders hold, and the hero. A row whose object left the map
    /// (an NPC away, an item the hero placed on another map or that was deleted) is not among them.
    /// </summary>
    public ResidentsQuery QueryResidents(SessionMapId map)
    {
        ThrowIfDisposed();
        if (Residency(map) is not { } residency) return new(ResidencyStatus.NotOpen, []);
        var overlay = Overlay(residency);
        var all = new List<SessionResident>(residency.Rows.Count + overlay.Count + 1);
        foreach (var row in residency.Rows.Values)
            if (!overlay.ContainsKey(row.Entity)) all.Add(FromRow(row));
        all.AddRange(overlay.Values.OfType<SessionResident>());
        all.Add(HeroResident());
        return Ordered(all);
    }

    /// <summary>
    /// ARC-DEV-103 (K1 §3.2, RL9): the residents on the ground of one sector of the open map now, loaded or not: the original's query over mobile
    /// types only, which walks the find index and never loads a sector. Held and detached objects have no tile and are not in it; off and destroyed
    /// ones are (with <see cref="SessionResident.Excluded"/>), so a caller applies the game's exclusion itself.
    /// </summary>
    public ResidentsQuery QueryResidentsIn(SessionMapId map, ResidentSector sector)
    {
        ThrowIfDisposed();
        if (Residency(map) is not { } residency) return new(ResidencyStatus.NotOpen, []);
        return Located(residency, [sector], resident => resident.Sector == sector);
    }

    /// <summary>ARC-DEV-103 (RL9): the residents on the ground of the cells from <paramref name="min"/> to <paramref name="max"/> (both inclusive) of
    /// the open map now; an empty rectangle (a max below a min) answers none.</summary>
    public ResidentsQuery QueryResidentsInCells(SessionMapId map, CellCoord min, CellCoord max)
    {
        ThrowIfDisposed();
        if (Residency(map) is not { } residency) return new(ResidencyStatus.NotOpen, []);
        if (max.X < min.X || max.Y < min.Y) return new(ResidencyStatus.Open, []);
        bool Inside(SessionResident resident) => resident.Cell is { } c && c.X >= min.X && c.X <= max.X && c.Y >= min.Y && c.Y <= max.Y;
        ResidentSector low = ResidentSector.Of(new(Math.Max(min.X, 0), Math.Max(min.Y, 0))), high = ResidentSector.Of(new(Math.Max(max.X, 0), Math.Max(max.Y, 0)));
        var span = ((long)high.X - low.X + 1) * ((long)high.Y - low.Y + 1);
        // A rectangle wider than the index walks the index's own sectors instead of every sector it spans.
        IEnumerable<ResidentSector> sectors = span > residency.Index.Count
            ? residency.Index.Keys.Where(s => s.X >= low.X && s.X <= high.X && s.Y >= low.Y && s.Y <= high.Y)
            : Enumerable.Range(low.X, high.X - low.X + 1).SelectMany(x => Enumerable.Range(low.Y, high.Y - low.Y + 1).Select(y => new ResidentSector(x, y)));
        return Located(residency, sectors, Inside);
    }

    private ResidencyState? Residency(SessionMapId? map) =>
        _residency is { } residency && residency.Map == _actor.Map && (map is null || residency.Map == map) ? residency : null;

    private ResidentsQuery Located(ResidencyState residency, IEnumerable<ResidentSector> sectors, Func<SessionResident, bool> inside)
    {
        var overlay = Overlay(residency);
        var found = new List<SessionResident>();
        foreach (var sector in sectors)
            if (residency.Index.TryGetValue(sector, out var entities))
                foreach (var entity in entities)
                    if (!overlay.ContainsKey(entity) && FromRow(residency.Rows[entity]) is var resident && inside(resident)) found.Add(resident);
        found.AddRange(overlay.Values.OfType<SessionResident>().Where(inside));
        if (HeroResident() is var hero && inside(hero)) found.Add(hero);
        return Ordered(found);
    }

    private static ResidentsQuery Ordered(List<SessionResident> residents)
    {
        residents.Sort((a, b) => StringComparer.Ordinal.Compare(a.Entity, b.Entity));
        return new(ResidencyStatus.Open, residents.AsReadOnly());
    }

    private static SessionResident FromRow(SessionResidentRow row) =>
        new(row.Entity, row.Kind, SessionResidentOrigin.MapData, row.Cell, row.Holder, row.Off, row.Destroyed);

    private SessionResident HeroResident() =>
        new(SessionEventKinds.PlayerEntity, SessionResidentKind.Pc, SessionResidentOrigin.Hero, _actor.Cell, null, false, false);

    /// <summary>
    /// The residents the session's state moves away from their base row, or adds: entity to its resident now, or null for a base row whose object is
    /// not a resident of the map now. Computed from the changes at every query, not from the rows, so its cost follows what the session changed
    /// (linear in its object fields and lists) and never the map's size. Not kept between queries: a query inside a mutation that has not committed
    /// sees that mutation's state, which a cache keyed on the revision would not.
    /// </summary>
    private Dictionary<string, SessionResident?> Overlay(ResidencyState residency)
    {
        var map = residency.Map;
        var pack = new Dictionary<string, SessionItemKind>(StringComparer.Ordinal);
        foreach (var (entity, kind) in _inventory.Hero.PackKinds) pack.TryAdd(entity, kind);
        var placedItems = new Dictionary<string, SessionPlacedItem>(StringComparer.Ordinal);
        foreach (var placed in _inventory.Placed) placedItems.TryAdd(placed.Entity, placed);
        var listed = new Dictionary<string, SessionItemKind>(StringComparer.Ordinal);
        foreach (var held in _inventory.Lists.All) listed.TryAdd(held.Entity, held.Kind);
        var lookups = new OverlayLookups(pack, placedItems, listed, _party.Order.ToHashSet(StringComparer.Ordinal));
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in _maps.MobileFields())
            if (ResidentFields.Contains(field.Property)) candidates.Add(field.Entity);
        // The same properties of an entity that is no mobile identity (no oid:) stay among the map's own changes.
        foreach (var key in _maps.MapKeys(map))
            if (key.Scope == MapChangeScope.Entity && key.Entity is { } keyed && ResidentFields.Contains(key.Property)) candidates.Add(keyed);
        // ARC-WORLD-1436: an object whose "off" flag the session wrote (a toggle state of a data-off NPC) is a changed resident whatever else it did.
        foreach (var record in QueryAllObjectFlags())
            if (((record.Flags.ObjectSet | record.Flags.ObjectCleared) & ObjectOffFlag) != 0) candidates.Add(record.Entity);
        candidates.UnionWith(_durability.Destroyed);
        candidates.UnionWith(_inventory.Taken);
        candidates.UnionWith(pack.Keys);
        candidates.UnionWith(placedItems.Keys);
        candidates.UnionWith(listed.Keys);
        foreach (var pile in _inventory.Objects.Piles) { candidates.Add(pile.Entity); foreach (var item in pile.Items) candidates.Add(item.Entity); }
        foreach (var pool in _inventory.Objects.Pools) candidates.Add(pool.Entity);
        foreach (var created in _inventory.Objects.Created) candidates.Add(created.Entity);
        // A holder's change (a spill, a move away) moves the items its base row holds too.
        foreach (var holder in candidates.Where(residency.HeldBy.ContainsKey).ToArray()) candidates.UnionWith(residency.HeldBy[holder]);

        var overlay = new Dictionary<string, SessionResident?>(StringComparer.Ordinal);
        foreach (var entity in candidates)
        {
            var row = residency.Rows.GetValueOrDefault(entity);
            var resident = Resolve(residency, lookups, entity, row);
            if (row is not null) { if (resident != FromRow(row)) overlay[entity] = resident; }
            else if (resident is not null) overlay[entity] = resident;
        }
        // A held object is a resident while its holder is one: the items of an NPC away travel with it, and an item a holder elsewhere holds is not here.
        bool Present(string holder) => holder == SessionEventKinds.PlayerEntity ||
            (overlay.TryGetValue(holder, out var moved) ? moved is not null : residency.Rows.ContainsKey(holder));
        foreach (var (entity, resident) in overlay.ToArray())
            if (resident?.Holder is { } holder && !Present(holder))
            {
                if (residency.Rows.ContainsKey(entity)) overlay[entity] = null; else overlay.Remove(entity);
            }
        foreach (var (holder, items) in residency.HeldBy)
            if (overlay.TryGetValue(holder, out var gone) && gone is null)
                foreach (var item in items)
                    if (!overlay.TryGetValue(item, out var moved) || moved?.Holder == holder) overlay[item] = null;
        return overlay;
    }

    // One entity's resident now (null: not a resident of the map), in the directory's order of stores (Locate): deleted, the hero's pack, placed,
    // a holder's list, taken, then the base row as the map changes leave it.
    private SessionResident? Resolve(ResidencyState residency, OverlayLookups lookups, string entity, SessionResidentRow? row)
    {
        var map = residency.Map;
        var (off, destroyed) = ResidentFlags(map, entity, row);
        switch (ObjectOriginOf(entity))
        {
            case SessionObjectOrigin.JunkPile:
                return _inventory.Objects.Pile(entity) is { } pile && pile.Map == map
                    ? new(entity, SessionResidentKind.Container, SessionResidentOrigin.Created, pile.Cell, null, false, false) : null;
            case SessionObjectOrigin.BloodPool:
                return _inventory.Objects.Pool(entity) is { } pool && pool.Map == map && !BloodPoolRules.Decayed(pool.CreatedMs, _clock.QueryClockSnapshot().GameTimestampMs)
                    ? new(entity, SessionResidentKind.Scenery, SessionResidentOrigin.Created, pool.Cell, null, false, false) : null;
            // ARC-DEV-347: a created scenery or eye-candy object is a resident of the map it lies on, in the sector of its cell (so the sector's fold lists it).
            case SessionObjectOrigin.Scenery:
            case SessionObjectOrigin.EyeCandy:
                return _inventory.Objects.CreatedObject(entity) is { } created && created.Map == map
                    ? new(entity, SessionResidentKind.Scenery, SessionResidentOrigin.Created, created.Cell, null, false, false) : null;
            // ARC-DEV-359 (E1-c): a created critter is an NPC resident of the map it was made on, standing where the session last placed it (the companion cell, then the walked place, as a
            // map NPC's row rule says) and on its creation cell before any; it moves its sector with its recorded place, and the object-state fields it carries are its own (oid: entity).
            case SessionObjectOrigin.Critter:
                if (_inventory.Objects.CreatedObject(entity) is not { } critter || critter.Map != map) return null;
                if (Record(map, entity, AwayMapProperty) is { } gone && gone.Value != map.RuntimeMap && gone.Value is >= 1 and <= int.MaxValue) return null;
                var stands = Placed(map, entity, CompanionCellProperty) ?? (lookups.Party.Contains(entity) ? null : Placed(map, entity, SessionNpcPlacement.Property)) ?? critter.Cell;
                return new(entity, SessionResidentKind.Npc, SessionResidentOrigin.Created, stands, null, off, destroyed);
        }
        // A map object with no row here is another map's (carried, placed or given here); a counter's id is the session's own.
        var origin = row is not null ? SessionResidentOrigin.MapData
            : ObjectOriginOf(entity) == SessionObjectOrigin.MapObject ? SessionResidentOrigin.Visitor : SessionResidentOrigin.Created;
        if (_durability.Destroyed.Contains(entity)) return null;
        if (lookups.Pack.TryGetValue(entity, out var packed))
            return new(entity, row?.Kind ?? (SessionResidentKind)packed.Type, origin, null, SessionEventKinds.PlayerEntity, off, destroyed);
        if (lookups.Placed.TryGetValue(entity, out var placed))
        {
            if (placed.Map != map) return null;
            var kind = row?.Kind ?? (SessionResidentKind)placed.Kind.Type;
            return _inventory.Objects.PileOf(entity) is { } pile
                ? new(entity, kind, origin, null, pile.Entity, off, destroyed)
                : new(entity, kind, origin, placed.Cell, null, off, destroyed);
        }
        if (_inventory.NpcOf(entity) is { } holder)
        {
            var kind = row?.Kind ?? (lookups.Listed.TryGetValue(entity, out var made) ? (SessionResidentKind)made.Type : SessionResidentKind.Generic);
            return new(entity, kind, origin, null, holder, off, destroyed);
        }
        if (_inventory.Taken.Contains(entity)) return null;
        if (row is null) return Visitor(map, entity, off, destroyed);
        if (row.Kind == SessionResidentKind.Npc)
        {
            if (Record(map, entity, AwayMapProperty) is { } away && away.Value != map.RuntimeMap && away.Value is >= 1 and <= int.MaxValue) return null;
            // A companion stands on its companion cell, or on its T0 cell when none is recorded (the placement clears it there): a walked place it
            // was left on before it joined is not its place.
            var cell = Placed(map, entity, CompanionCellProperty) ?? (lookups.Party.Contains(entity) ? null : Placed(map, entity, SessionNpcPlacement.Property)) ?? row.Cell;
            return new(entity, row.Kind, origin, cell, row.Holder, off, destroyed);
        }
        if (_inventory.Objects.PileOf(entity) is { } heap && heap.Map == map) return new(entity, row.Kind, origin, null, heap.Entity, off, destroyed);
        if (row.Holder is { } of && residency.Rows[of] is var holderRow)
        {
            // A critical's drop from an NPC (ARC-COMBAT-009) and a spilled container's items (ARC-COMBAT-008) lie loose.
            if (holderRow.Kind == SessionResidentKind.Npc && Record(map, entity, ItemDroppedProperty) is { Map: var dropMap, Value: var drop and > 0 } && dropMap == map)
                return new(entity, row.Kind, origin, Moved(map, entity) ?? new((int)((drop - 1) & 0xFFFFFF), (int)(((drop - 1) >> 24) & 0xFFFFFF)), null, off, destroyed);
            if (holderRow.Kind == SessionResidentKind.Container && RecordedObject(_maps, map, of) is { Condition: SessionObjectCondition.Destroyed, Spill: { } spill })
                return new(entity, row.Kind, origin, Moved(map, entity) ?? spill, null, off, destroyed);
        }
        // ARC-MAGIC-1230: a pushed item of the map's data lies where the push left it.
        return new(entity, row.Kind, origin, row.Cell is not null ? Moved(map, entity) ?? row.Cell : null, row.Holder, off, destroyed);
    }

    // The cell a push left a map-data item on (an item of this map: the record names it).
    private CellCoord? Moved(SessionMapId map, string entity) =>
        Record(map, entity, SessionItemMove.Property) is { } record && record.Map == map ? SessionItemMove.Decode(record.Value) : null;

    // An NPC of another map's data standing here: its companion cell is recorded on this map (the transfer clears it on the map it leaves).
    private SessionResident? Visitor(SessionMapId map, string entity, bool off, bool destroyed) =>
        Placed(map, entity, CompanionCellProperty) is { } cell ? new(entity, SessionResidentKind.Npc, SessionResidentOrigin.Visitor, cell, null, off, destroyed) : null;

    private CellCoord? Placed(SessionMapId map, string entity, string property) =>
        Record(map, entity, property) is { } record && record.Map == map && SessionNpcPlacement.Decode(record.Value) is { } placed ? placed.World : null;

    // The recorded value of an entity's field and the map the record names: a mobile identity's one record wherever it names, else the open map's change.
    private (SessionMapId Map, long Value)? Record(SessionMapId open, string entity, string property)
    {
        if (ObjectStateVocabulary.Mobile(entity, property)) return _maps.RecordsOf(entity, property) is [var record] ? record : null;
        return _maps.Get(open, MapChangeKey.ForEntity(entity, property)) is { } value ? (open, value) : null;
    }

    // A recorded flag wins over the data's (value 1 set, any other value clear); the condition of a broken object sets them too.
    private const int ObjectOffFlag = 0x2;

    private (bool Off, bool Destroyed) ResidentFlags(SessionMapId map, string entity, SessionResidentRow? row)
    {
        bool? Field(string property) => Record(map, entity, property) is { } record ? record.Value == 1 : null;
        var condition = Record(map, entity, ObjectConditionProperty) is { } c && c.Value >= 0 ? (SessionObjectCondition)(c.Value & 0xFF) : (SessionObjectCondition?)null;
        // ARC-WORLD-1436: the session's object-flag change over the data's (a toggle state flips the flag of the original, so a data-off NPC a heartbeat switched on is on).
        var off = QueryObjectFlags(entity).Object((Field(OffProperty) ?? row?.Off ?? false) ? ObjectOffFlag : 0) is var flags && (flags & ObjectOffFlag) != 0;
        var destroyed = Field(DestroyedFieldProperty) ?? row?.Destroyed ?? false;
        return (off || condition == SessionObjectCondition.Off, destroyed || condition == SessionObjectCondition.Destroyed);
    }

    private static bool SectorType(SessionResidentKind kind) =>
        kind is SessionResidentKind.Wall or SessionResidentKind.Portal or SessionResidentKind.Scenery or SessionResidentKind.Trap;

    private static bool ItemKind(SessionResidentKind kind) => kind is >= SessionResidentKind.Weapon and <= SessionResidentKind.Generic;

    private static bool CellFits(CellCoord cell) => cell.X >= 0 && cell.X < SessionNpcPlacement.Axis && cell.Y >= 0 && cell.Y < SessionNpcPlacement.Axis;
}
