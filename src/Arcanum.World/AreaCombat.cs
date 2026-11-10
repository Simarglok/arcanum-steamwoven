using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// An NPC the hero can click in combat mode (ARC-COMBAT-005): its entity id, its row, whether it is dead now (its map data or
/// the session), whether it follows the hero (the original wants left Alt to attack a follower) and its combat values (or why
/// its stored fields give none).
/// </summary>
public sealed record AreaCombatTarget(string Entity, AreaObjectRecord Row, bool Dead, bool Follower, SessionCritterCombatBuild Build);

/// <summary>ARC-WORLD-106: an item an NPC wears now (<see cref="AreaCombatHost.Worn"/>): its slot, entity, combat fields and durability
/// values.</summary>
public readonly record struct AreaNpcWornItem(Arcanum.Simulation.Equipment.EquipmentSlot Slot, string Entity, CombatFields Fields, SessionWearItem Wear);

/// <summary>
/// ARC-COMBAT-005: the AREA viewer's combat targets. Every NPC of a map with an inventory seed (ARC-UI-004; every NPC of the map
/// is a holder there) is a target; its combat values come from its effective fields (<see cref="CombatFieldReader.Read"/>) and
/// the fields of the items it wears. Since ARC-WORLD-106 the worn items are the session's (<see cref="Worn"/>: the NPC's list as the
/// session shows it, with a regeneration's best-items record, the items given to or made for it and without the taken, dropped,
/// destroyed or broken ones), rebuilt when that set changes; <see cref="Critter(int, string)"/> keeps the map data's worn items for the
/// values that do not depend on them (wearer and best-items chooser). The same reader serves the pinned exterior, the cave and every
/// whole map. It holds no session state: life, party and wear come from the session it is given (the event world's questions from
/// <c>session</c>, the viewer's shown session).
/// </summary>
public sealed class AreaCombatHost : IAreaEventObjects
{
    private readonly AreaInventoryHost _inventory;
    private readonly RecordResolver _resolver;
    private readonly Func<int, CombatFields?>? _prototypes;
    private readonly Func<GameSessionOwner?>? _session;
    private readonly Dictionary<(int Map, string Entity), SessionCritterCombatBuild> _builds = [];
    // ARC-WORLD-106: the NPC's build over the session's worn set it was made for (slot:item:prototype, in slot order), for one session (a
    // loaded save may reuse a made item's number for another item); and the worn sets of one session revision, kept only when every
    // worn item had fields (a prototype may load later without a revision).
    private readonly Dictionary<(int Map, string Entity), (string Worn, SessionCritterCombatBuild Build)> _wornBuilds = [];
    private GameSessionOwner? _buildSession;
    private readonly Dictionary<(int Map, string Entity), IReadOnlyList<AreaNpcWornItem>?> _wornNow = [];
    private (GameSessionOwner Session, long Revision)? _wornStamp;
    // The combat fields of an item's object row, by entity (null: the row does not read), read once.
    private readonly Dictionary<string, CombatFields?> _rowFields = new(StringComparer.Ordinal);
    // The NPC holders of one map as the session last showed them, and the NPC holding each of their items (ARC-UI-031), rebuilt when
    // the session or its revision changes.
    // ARC-DEV-072: one entry per map (a visiting companion's holder lies on its home map, the shown map's NPCs on the shown one).
    private (GameSessionOwner Session, long Revision)? _holdersStamp;
    private readonly HashSet<int> _refusedAttach = [];
    private readonly Dictionary<int, (Dictionary<string, SessionHolderInventory> Holders, Dictionary<string, string> NpcItems)> _holders = [];

    /// <param name="inventory">The maps' seeds and object rows.</param>
    /// <param name="prototypes">ARC-WORLD-106: an item prototype's stored fields (the facts of an item the session made or a regeneration
    /// created for an NPC, which has no object row), or null while they are not loaded; null: such an item has no facts.</param>
    /// <param name="session">ARC-WORLD-106: the session the event world's questions (<see cref="Facts(int, string)"/>) read the NPC's wear
    /// from; null or no session: its map data's.</param>
    public AreaCombatHost(AreaInventoryHost inventory, Func<int, CombatFields?>? prototypes = null, Func<GameSessionOwner?>? session = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        _inventory = inventory;
        _resolver = inventory.Resolver;
        _prototypes = prototypes;
        _session = session;
    }

    public bool Covers(int runtimeMap) => _resolver.Holds(runtimeMap);

    /// <summary>ARC-MAGIC-014: the session entity id of a map object (its inventory seed's), or null.</summary>
    public string? Entity(int runtimeMap, AreaObjectKey key) =>
        Held(runtimeMap, out var map) && map.TryGetEntity(key, out var entity) ? entity : null;

    /// <summary>ARC-WORLD-039: whether an object of the map's T0 data has this session entity id (a deferred call's role).</summary>
    public bool HasObject(int runtimeMap, string entity) => Held(runtimeMap, out var map) && map.TryGetRow(entity, out _);

    /// <summary>ARC-WORLD-039: the NPC's life facts from its combat fields on that map, or null. ARC-WORLD-106: with the items the
    /// session's NPC wears (its normal resistance counts its armour) when the host was given the session; the map data's when that build
    /// fails (only the normal resistance depends on the worn items, and a null answer would drop the NPC's life timers).</summary>
    public LifeFacts? Facts(int runtimeMap, string entity) =>
        (_session?.Invoke() is { } session ? Build(session, runtimeMap, entity).Combat?.Facts : null) ?? Build(runtimeMap, entity).Combat?.Facts;

    /// <summary>ARC-DEV-076 (MIG3): <see cref="Facts(int, string)"/> by identity: the NPC's life facts wherever it stands (a companion on a
    /// map other than its home map answers as on its home map), or null without a record.</summary>
    public LifeFacts? Facts(string entity) =>
        Record(entity ?? throw new ArgumentNullException(nameof(entity))) is { } found ? Facts(found.HomeMap, entity) : null;

    /// <summary>ARC-DEV-076 (MIG3): whether any reachable map's stored objects hold this entity (a deferred call's role), wherever the
    /// object stands now; <see cref="HasObject(int, string)"/> asks one map's rows.</summary>
    public bool HasObject(string entity) => Record(entity ?? throw new ArgumentNullException(nameof(entity))) is not null;

    /// <summary>ARC-WORLD-103: the NPC's combat values from its combat fields on that map, or null. Its worn items are the map data's: the
    /// regeneration asks it for the wearer and the best-items chooser, which do not depend on them, while the session's lists change.</summary>
    public SessionCritterCombat? Critter(int runtimeMap, string entity) => Build(runtimeMap, entity).Combat;

    /// <summary>ARC-DEV-076 (MIG3): <see cref="Critter(int, string)"/> by identity.</summary>
    public SessionCritterCombat? Critter(string entity) => Build(entity ?? throw new ArgumentNullException(nameof(entity))).Combat;

    /// <summary>The NPC drawn as <paramref name="key"/> on <paramref name="runtimeMap"/>, or null for anything else.</summary>
    public AreaCombatTarget? Target(GameSessionOwner session, int runtimeMap, AreaObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Held(runtimeMap, out var map) || !map.TryGetEntity(key, out var entity)
            || !Holders(session, runtimeMap).TryGetValue(entity, out var holder) || holder.Kind != SessionHolderKind.Npc)
            return null;
        return new(entity, map.Row(entity), holder.Dead, session.QueryCompanion(entity).LedByPc, Build(session, map, entity));
    }

    /// <summary>
    /// ARC-DEV-076 (MIG3, TM3, FM3): the NPC <paramref name="entity"/> as a combat target by identity alone, wherever it stands now (a
    /// companion carried onto a map other than its home map too): its row, whether it is dead and follows the PC, and its combat values
    /// with the items the session says it wears (<see cref="Build(GameSessionOwner, string)"/>); null for an entity no reachable map
    /// lists, one that is no NPC holder, or one the resolver cannot read (<see cref="Record"/>).
    /// </summary>
    public AreaCombatTarget? Target(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        return Record(entity) is { } found ? Target(session, found) : null;
    }

    private AreaCombatTarget? Target(GameSessionOwner session, ResolvedRecord found)
    {
        var entity = found.Entity;
        if (Holders(session, found.HomeMap).GetValueOrDefault(entity) is not { Kind: SessionHolderKind.Npc } holder) return null;
        return new(entity, found.Row, holder.Dead, session.QueryCompanion(entity).LedByPc, Build(session, found.Map, entity));
    }

    /// <summary>ARC-DEV-076 (MIG3): the NPC drawn as <paramref name="key"/>, on whichever map's rows hold it: an object key names its map
    /// (<see cref="AreaObjectKey.RuntimeMap"/>), so a visiting companion's key resolves to the NPC through its home map's rows like a
    /// shown map object does; only a map this host holds answers (the hover asks every frame, so it reads no map: a visiting companion
    /// whose home is not held is asked by its entity, <see cref="Target(GameSessionOwner, string)"/>, which reads it once). The record
    /// is the key's own map's row.</summary>
    public AreaCombatTarget? Target(GameSessionOwner session, AreaObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Held(key.RuntimeMap, out var map) || !map.TryGetEntity(key, out var entity)) return null;
        return Target(session, new ResolvedRecord(entity, map.RuntimeMap, map, map.Row(entity)));
    }

    private Dictionary<string, SessionHolderInventory> Holders(GameSessionOwner session, int runtimeMap) => HolderIndex(session, runtimeMap).Holders;

    private (Dictionary<string, SessionHolderInventory> Holders, Dictionary<string, string> NpcItems) HolderIndex(GameSessionOwner session, int runtimeMap)
    {
        if (_holdersStamp is not { } stamp || !ReferenceEquals(stamp.Session, session) || stamp.Revision != session.Version.Revision)
        {
            _holders.Clear();
            _holdersStamp = (session, session.Version.Revision);
        }
        if (_holders.TryGetValue(runtimeMap, out var cached)) return cached;
        // ARC-DEV-072: a map the resolver read on demand has no seed in the session until it is first asked about (a given map's was
        // attached with the host, and a restore attaches them again).
        if (_inventory.AttachReadMap(session, runtimeMap) is { } refused && _refusedAttach.Add(runtimeMap))
            Console.WriteLine($"ARC-DEV-072 record-map-attach refused map={runtimeMap} failure={refused}");
        var holders = session.QueryMapInventory(new(runtimeMap))?.Holders.ToDictionary(holder => holder.Entity, StringComparer.Ordinal) ?? [];
        var npcItems = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var holder in holders.Values.Where(holder => holder.Kind == SessionHolderKind.Npc))
            foreach (var item in holder.Items) npcItems.TryAdd(item.Entity, holder.Entity);
        return _holders[runtimeMap] = (holders, npcItems);
    }

    // The map this host holds (given or read on demand), without reading one.
    private bool Held(int runtimeMap, out AreaInventoryMap map)
    {
        map = _resolver.Held(runtimeMap)!;
        return map is not null;
    }

    /// <summary>
    /// ARC-UI-031: the spell facts of an item an NPC of a map this host covers holds now, listed or worn, as the session shows the
    /// map's holders (a dead NPC's too: its dying fires its worn items, IC5): the item's fields (<see cref="ItemFields"/>: its object row,
    /// or since ARC-WORLD-106 a made item's prototype) through <see cref="SessionItemSpells.FromCombatFields"/> with the NPC as its holder
    /// (the parent field 0x58). The cast host's item lookup asks it for anything that is not the hero's; null for an item no NPC of that
    /// map holds (a container's, the hero's, a visiting companion's from its own map), a destroyed one, or one whose fields do not read as
    /// an item.
    /// </summary>
    public SessionItemSpells? NpcItemSpells(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        if (!Held(runtimeMap, out var map)) return null;
        var (holders, npcItems) = HolderIndex(session, runtimeMap);
        if (!npcItems.TryGetValue(entity, out var holder) || session.IsItemDestroyed(entity)
            || holders[holder].Items.FirstOrDefault(item => item.Entity == entity) is not { Entity: not null } listed || ItemFields(map, listed) is not { } fields)
            return null;
        return SessionItemSpells.FromCombatFields(entity, holder, fields);
    }

    /// <summary>ARC-DEV-076 (MIG3): <see cref="NpcItemSpells(GameSessionOwner, int, string)"/> by identity of the item: the spell facts of an
    /// item an NPC of any map this host holds has now (a visiting companion's holder entry lies on its home map), or null.</summary>
    public SessionItemSpells? NpcItemSpells(GameSessionOwner session, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        if (session.IsItemDestroyed(item)) return null;
        foreach (var map in _resolver.Maps)
            if (HolderIndex(session, map.RuntimeMap).NpcItems.ContainsKey(item)) return NpcItemSpells(session, map.RuntimeMap, item);
        // ARC-DEV-262: an item its NPC threw or a critical dropped lies loose and has left the NPC's list, but its spells stay its own (a thrown
        // grenade's explosion at the end of its flight): the holder is the NPC that held it.
        return session.QueryDroppedItem(item) is { } dropped && Held(dropped.Map.RuntimeMap, out var lying) && ItemFields(lying, dropped.Item) is { } fields
            ? SessionItemSpells.FromCombatFields(item, dropped.Holder, fields) : null;
    }

    /// <summary>
    /// ARC-WORLD-108: the items an NPC of a map this host covers holds now, listed or worn, in its list's order as the session shows the
    /// map's holders (the AI's inventory, fields 0xED/0xEE), each with its fields (<see cref="ItemFields"/>); a destroyed item and one
    /// without fields (NOT READY: an item without a row before the prototypes load) are left out. Empty for an NPC the session does
    /// not list.
    /// </summary>
    public IReadOnlyList<(string Entity, CombatFields Fields)> NpcItems(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        if (!Held(runtimeMap, out var map) || !Holders(session, runtimeMap).TryGetValue(entity, out var holder) || holder.Kind != SessionHolderKind.Npc)
            return [];
        var items = new List<(string, CombatFields)>();
        foreach (var item in holder.Items)
            if (!session.IsItemDestroyed(item.Entity) && ItemFields(map, item) is { Item: not null } fields) items.Add((item.Entity, fields));
        return items;
    }

    /// <summary>ARC-WORLD-108: the fields of an item an NPC of a map this host covers holds now (<see cref="ItemFields"/>), or null for
    /// an item no NPC of that map holds, a destroyed one, or one without fields.</summary>
    public CombatFields? NpcItemFields(GameSessionOwner session, int runtimeMap, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        if (!Held(runtimeMap, out var map)) return null;
        var (holders, npcItems) = HolderIndex(session, runtimeMap);
        return npcItems.TryGetValue(item, out var holder) && !session.IsItemDestroyed(item)
            && holders[holder].Items.FirstOrDefault(listed => listed.Entity == item) is { Entity: not null } found
            && ItemFields(map, found) is { Item: not null } fields ? fields : null;
    }

    /// <summary>ARC-WORLD-108: the NPC of a map this host covers that holds the item now (0x4612A0 for an NPC's item), or null.</summary>
    public string? NpcHolder(GameSessionOwner session, int runtimeMap, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        return _resolver.Holds(runtimeMap) && HolderIndex(session, runtimeMap).NpcItems.TryGetValue(item, out var holder) && !session.IsItemDestroyed(item)
            ? holder : null;
    }

    /// <summary>ARC-WORLD-036: an NPC's stored fields on a map this host covers (a follower's loaded sheet), or null.</summary>
    public CombatFields? Fields(int runtimeMap, string entity) =>
        Held(runtimeMap, out var map) && map.TryGetRow(entity, out var row) ? CombatFieldReader.Read(row).Fields : null;

    /// <summary>ARC-UI-042: the body armour an NPC wears on a map this host covers: since ARC-WORLD-106 the session's (<see cref="Worn"/>,
    /// the armour slot 1006) by its object row, or null (none, or a made armour without a row: <see cref="WornArmorEntity"/> names it). A hit on
    /// the NPC sounds as a hit on it (audio study SN23).</summary>
    public AreaObjectRecord? WornArmor(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Held(runtimeMap, out var map) || WornArmorEntity(session, runtimeMap, entity) is not { } armor) return null;
        if (map.TryGetRow(armor, out var row)) return row;
        return _resolver.Maps.Select(other => other.TryGetRow(armor, out var found) ? found : null).FirstOrDefault(found => found is not null);
    }

    /// <summary>ARC-DEV-315: the entity of the body armour (slot 1006) an NPC wears on a map this host covers, whether it has an object row or is an item
    /// the session made (no row: the reader takes its prototype, <see cref="AreaMadeItems"/>); null when none is worn, the map is not covered or the
    /// worn set does not read (<see cref="Worn"/>).</summary>
    public string? WornArmorEntity(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Held(runtimeMap, out _) || Worn(session, runtimeMap, entity) is not { } worn) return null;
        var armor = worn.FirstOrDefault(item => item.Slot == Arcanum.Simulation.Equipment.EquipmentSlot.Armor);
        return armor.Entity;
    }

    /// <summary>ARC-DEV-076 (MIG3): <see cref="WornArmor(GameSessionOwner, int, string)"/> by identity: the body armour the NPC wears now,
    /// whichever map it stands on.</summary>
    public AreaObjectRecord? WornArmor(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Record(entity ?? throw new ArgumentNullException(nameof(entity))) is { } found ? WornArmor(session, found.HomeMap, entity) : null;
    }

    /// <summary>ARC-DEV-315: <see cref="WornArmorEntity(GameSessionOwner, int, string)"/> by identity: the body armour the NPC wears now, whichever map it
    /// stands on.</summary>
    public string? WornArmorEntity(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Record(entity ?? throw new ArgumentNullException(nameof(entity))) is { } found ? WornArmorEntity(session, found.HomeMap, entity) : null;
    }

    /// <summary>
    /// ARC-COMBAT-008: the items in the NPC's wear slots with their durability values, for the attack's wear: <see cref="Worn"/>, each
    /// through <see cref="SessionWearItem.FromCombatFields"/>. Null when the map is not covered, the session does not list the NPC, a worn
    /// item does not read or two share a slot (the attack then wears none of the NPC's items).
    /// </summary>
    public IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? WornItems(GameSessionOwner session, int runtimeMap, string entity) =>
        Worn(session, runtimeMap, entity)?.ToDictionary(item => item.Slot, item => item.Wear);

    /// <summary>
    /// ARC-WORLD-106: the items the NPC wears now, in slot order: the items of its list as the session shows it (a regeneration's
    /// best-items record over its map data's inventory locations, ARC-WORLD-103; the items given to or made for it; never the taken, dropped
    /// or destroyed ones) whose location is a wear slot (1000..1008) and that are not broken (the original takes an item off at its first
    /// break; for an NPC that is the host's, so a broken item is no longer worn). An item's fields are <see cref="ItemFields"/>; one
    /// without any (an item without a row before the prototypes load: NOT READY) is skipped. Null when the map is not covered, the
    /// session does not list the NPC, a worn item's fields give no durability values, or two items share a slot. Kept per session
    /// revision.
    /// </summary>
    public IReadOnlyList<AreaNpcWornItem>? Worn(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_wornStamp is not { } stamp || !ReferenceEquals(stamp.Session, session) || stamp.Revision != session.Version.Revision)
        {
            _wornNow.Clear();
            _wornStamp = (session, session.Version.Revision);
        }
        if (_wornNow.TryGetValue((runtimeMap, entity), out var known)) return known;
        if (!Held(runtimeMap, out var map) || !Holders(session, runtimeMap).TryGetValue(entity, out var holder)) return null;
        var worn = new SortedDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, AreaNpcWornItem>();
        var complete = true;
        IReadOnlyList<AreaNpcWornItem>? Keep(IReadOnlyList<AreaNpcWornItem>? found)
        {
            if (complete) _wornNow[(runtimeMap, entity)] = found;
            return found;
        }
        foreach (var item in holder.Items)
        {
            var slot = (Arcanum.Simulation.Equipment.EquipmentSlot)item.Facts.InventoryLocation;
            if (!Enum.IsDefined(slot) || session.IsItemDestroyed(item.Entity)) continue;
            if (!ItemSource(map, item, out var fields)) { complete = false; continue; }
            if (fields?.Item is null || SessionWearItem.FromCombatFields(item.Entity, fields) is not { } wear) return Keep(null);
            if (session.QueryItemCondition(item.Entity, wear.Baseline) is not { Broken: false }) continue;
            if (!worn.TryAdd(slot, new(slot, item.Entity, fields, wear))) return Keep(null);
        }
        return Keep(Array.AsReadOnly(worn.Values.ToArray()));
    }

    /// <summary>
    /// ARC-WORLD-109: the fields of <paramref name="item"/> when the NPC <paramref name="holder"/> holds it now (its list as the session
    /// shows it, as <see cref="Worn"/> reads it; never a destroyed item), else null; null too for an item without fields.
    /// </summary>
    public CombatFields? HeldItemFields(GameSessionOwner session, int runtimeMap, string holder, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Held(runtimeMap, out var map) || !Holders(session, runtimeMap).TryGetValue(holder, out var inventory)
            || inventory.Kind != SessionHolderKind.Npc || session.IsItemDestroyed(item)) return null;
        return inventory.Items.FirstOrDefault(held => held.Entity == item) is { } found && ItemSource(map, found, out var fields) ? fields : null;
    }

    /// <summary>
    /// ARC-WORLD-106: an item's combat fields: its object row's on the map, else on another covered map (a taken item the hero gave
    /// away), else (an item the session made, an item a regeneration created, a hero kit item given away) its prototype's stored fields
    /// from <c>prototypes</c> (the item's kind names the prototype), as the regeneration's wielder reads such an item; null without any.
    /// </summary>
    public CombatFields? ItemFields(AreaInventoryMap map, SessionInventoryItem item)
    {
        ArgumentNullException.ThrowIfNull(map);
        return ItemSource(map, item, out var fields) ? fields : null;
    }

    // Whether the item has a source of fields (a row, which may not read, or a loaded prototype) and its fields.
    private bool ItemSource(AreaInventoryMap map, SessionInventoryItem item, out CombatFields? fields)
    {
        if (RowFields(map, item.Entity, out fields)) return true;
        foreach (var other in _resolver.Maps)
            if (!ReferenceEquals(other, map) && RowFields(other, item.Entity, out fields)) return true;
        fields = _prototypes?.Invoke(item.Facts.Kind.Prototype);
        return fields is not null;
    }

    private bool RowFields(AreaInventoryMap map, string entity, out CombatFields? fields)
    {
        fields = null;
        if (!map.TryGetRow(entity, out var row)) return false;
        if (!_rowFields.TryGetValue(entity, out fields)) _rowFields[entity] = fields = CombatFieldReader.Read(row).Fields;
        return true;
    }

    /// <summary>ARC-COMBAT-025: the stored material, resistances and invulnerable flag of a starting-kit item (entity <c>kit:N</c>) from its prototype's
    /// fields, for damage that reaches the item the hero wears (<see cref="SessionObjectFacts.ItemFacts"/>); null for an entity the kit does not
    /// hold, a prototype that is not loaded or one that is no item.</summary>
    public SessionObjectBaseline? KitItemFacts(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        return session.QueryHeroKit().FirstOrDefault(item => item.Entity == entity) is { } kit && _prototypes?.Invoke(kit.Kind.Prototype) is { } fields
            ? SessionObjectFacts.ItemFacts(fields) : null;
    }

    /// <summary>ARC-COMBAT-008: the hero's worn items with their durability values (each kit item's from
    /// <paramref name="kit"/>, by kit index), or null when one has none (the attack then wears none of the hero's items).</summary>
    public static IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? HeroWorn(GameSessionOwner session,
        IReadOnlyList<SessionWearItem?>? kit)
    {
        ArgumentNullException.ThrowIfNull(session);
        var worn = new Dictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>();
        foreach (var item in session.QueryEquipment().Worn)
        {
            if (!item.Entity.StartsWith("kit:", StringComparison.Ordinal) ||
                !int.TryParse(item.Entity.AsSpan(4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
                kit is null || number < 1 || number > kit.Count || kit[number - 1] is not { } wear || wear.Entity != item.Entity) return null;
            worn[item.Slot] = wear;
        }
        return worn;
    }

    /// <summary>
    /// ARC-DEV-072 (MIG1, TM3, FM3): an object's base record by identity alone: its row in the map whose stored objects hold it, wherever
    /// it stands now. It is found among the maps this host holds; otherwise the session names the home map of a travelling NPC
    /// (<see cref="EntityHomes"/>) and the resolver reads that map on demand, so a companion of a map this run never loaded resolves
    /// too. Null for an entity no reachable map lists, or whose home map cannot be read or passes the resolver's limits.
    /// </summary>
    public ResolvedRecord? Record(string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (_resolver.HeldHome(entity) is not null) return _resolver.Resolve(entity).Record;
        if (_session?.Invoke() is not { } session || EntityHomes.HomeOf(session, entity) is not { } home) return null;
        return _resolver.Resolve(entity, home).Record;
    }

    /// <summary>ARC-WORLD-036: the map whose stored objects hold the NPC (its home map, wherever it stands now), or null when
    /// no reachable map does. Since ARC-DEV-072 it is the resolver's (<see cref="Record"/>): a home map this run never loaded counts.</summary>
    public int? HomeMap(string entity) =>
        Record(entity)?.HomeMap;

    /// <summary>ARC-DEV-072: the NPC's stored fields by identity (<see cref="Record"/>), wherever it stands; null without a record.</summary>
    public CombatFields? Fields(string entity) =>
        Record(entity) is { } found && RowFields(found.Map, entity, out var fields) ? fields : null;

    /// <summary>ARC-DEV-072: <see cref="Build(int, string)"/> by identity: the NPC's combat values with its home map's worn items.</summary>
    public SessionCritterCombatBuild Build(string entity) =>
        Record(entity) is { } found ? Build(found.Map, entity) : new(SessionCritterCombatFailure.NotAnNpc, null);

    /// <summary>ARC-DEV-072: <see cref="Build(GameSessionOwner, int, string)"/> by identity: the NPC's combat values with the items the
    /// session's NPC wears now, whichever map it stands on.</summary>
    public SessionCritterCombatBuild Build(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Record(entity) is { } found ? Build(session, found.Map, entity) : new(SessionCritterCombatFailure.NotAnNpc, null);
    }

    /// <summary>ARC-DEV-072: the session's holder entry of an NPC or container by identity (its seed is the home map's), or null.</summary>
    public SessionHolderInventory? Holder(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Record(entity) is { } found && Holders(session, found.HomeMap).TryGetValue(entity, out var holder) ? holder : null;
    }

    /// <summary>ARC-DEV-072: <see cref="Worn(GameSessionOwner, int, string)"/> by identity: the items the NPC wears now, whichever map it
    /// stands on.</summary>
    public IReadOnlyList<AreaNpcWornItem>? Worn(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Record(entity) is { } found ? Worn(session, found.HomeMap, entity) : null;
    }

    /// <summary>ARC-DEV-075 (MIG2): <see cref="WornItems(GameSessionOwner, int, string)"/> by identity: the durability values of the items the
    /// NPC wears now, whichever map it stands on.</summary>
    public IReadOnlyDictionary<Arcanum.Simulation.Equipment.EquipmentSlot, SessionWearItem>? WornItems(GameSessionOwner session, string entity) =>
        Worn(session, entity)?.ToDictionary(item => item.Slot, item => item.Wear);

    /// <summary>ARC-DEV-075 (MIG2): <see cref="NpcItems(GameSessionOwner, int, string)"/> by identity: the items the NPC holds now, listed or
    /// worn, whichever map it stands on (its holder entry is the home map's).</summary>
    public IReadOnlyList<(string Entity, CombatFields Fields)> NpcItems(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Record(entity) is { } found ? NpcItems(session, found.HomeMap, entity) : [];
    }

    /// <summary>ARC-DEV-075 (MIG2): <see cref="HeldItemFields(GameSessionOwner, int, string, string)"/> by identity of the holder.</summary>
    public CombatFields? HeldItemFields(GameSessionOwner session, string holder, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Record(holder) is { } found ? HeldItemFields(session, found.HomeMap, holder, item) : null;
    }

    /// <summary>ARC-DEV-075 (MIG2): the NPC that holds the item now (0x4612A0 for an NPC's item), found among every map this host holds
    /// (a visiting companion's holder entry lies on its home map), or null.</summary>
    public string? NpcHolder(GameSessionOwner session, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        if (session.IsItemDestroyed(item)) return null;
        foreach (var map in _resolver.Maps)
            if (HolderIndex(session, map.RuntimeMap).NpcItems.TryGetValue(item, out var holder)) return holder;
        return null;
    }

    /// <summary>ARC-DEV-075 (MIG2): <see cref="NpcItemFields(GameSessionOwner, int, string)"/> by identity of the item: the fields of an item an
    /// NPC of any map this host holds has now, or null.</summary>
    public CombatFields? NpcItemFields(GameSessionOwner session, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(item);
        if (session.IsItemDestroyed(item)) return null;
        foreach (var map in _resolver.Maps)
            if (HolderIndex(session, map.RuntimeMap).NpcItems.ContainsKey(item)) return NpcItemFields(session, map.RuntimeMap, item);
        return null;
    }

    /// <summary>The NPC's combat values with its map data's worn items (the items its seed lists in a wear slot by their stored
    /// location), read once per entity and map: for the values that do not depend on what it wears (<see cref="Critter(int, string)"/>).</summary>
    public SessionCritterCombatBuild Build(int runtimeMap, string entity) => Held(runtimeMap, out var map)
        ? Build(map, entity) : new(SessionCritterCombatFailure.NotAnNpc, null);

    /// <summary>
    /// ARC-WORLD-106: the NPC's combat values with the items the session's NPC wears now (<see cref="Worn"/>, each in the slot the session
    /// names: <see cref="SessionCritterCombat.FromWorn"/>), rebuilt when that set changes: what it attacks with, defends with (armour class,
    /// resistances, light) and its life facts. An NPC the session does not list (no inventory for its map yet) keeps its map data's worn
    /// items; a worn item without durability values or two in one slot is <see cref="SessionCritterCombatFailure.InvalidWornItem"/>.
    /// </summary>
    public SessionCritterCombatBuild Build(GameSessionOwner session, int runtimeMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Held(runtimeMap, out var map) ? Build(session, map, entity) : new(SessionCritterCombatFailure.NotAnNpc, null);
    }

    private SessionCritterCombatBuild Build(GameSessionOwner session, AreaInventoryMap map, string entity)
    {
        if (!map.TryGetRow(entity, out _) || !Holders(session, map.RuntimeMap).ContainsKey(entity)) return Build(map, entity);
        if (!RowFields(map, entity, out var fields) || fields is null) return new(SessionCritterCombatFailure.NotAnNpc, null);
        if (Worn(session, map.RuntimeMap, entity) is not { } worn) return new(SessionCritterCombatFailure.InvalidWornItem, null);
        if (!ReferenceEquals(_buildSession, session)) { _wornBuilds.Clear(); _buildSession = session; }
        var key = string.Join('|', worn.Select(item => $"{(int)item.Slot}:{item.Entity}:{item.Fields.Object.Prototype}"));
        if (_wornBuilds.TryGetValue((map.RuntimeMap, entity), out var cached) && cached.Worn == key) return cached.Build;
        var build = SessionCritterCombat.FromWorn(entity, fields, worn.Select(item => KeyValuePair.Create(item.Slot, item.Fields)));
        _wornBuilds[(map.RuntimeMap, entity)] = (key, build);
        return build;
    }

    private SessionCritterCombatBuild Build(AreaInventoryMap map, string entity)
    {
        if (_builds.TryGetValue((map.RuntimeMap, entity), out var cached)) return cached;
        SessionCritterCombatBuild build;
        // ARC-DEV-076 (MIG3): an entity that is no holder of the map's seed (an item's row) is no NPC: the identity questions reach any entity.
        if (!RowFields(map, entity, out var fields) || fields is null || map.Seed.Holders.FirstOrDefault(h => h.Entity == entity) is not { } holder)
            build = new(SessionCritterCombatFailure.NotAnNpc, null);
        else
        {
            var carried = new List<CombatFields>();
            foreach (var item in holder.Items)
                if (RowFields(map, item, out var itemFields) && itemFields is not null) carried.Add(itemFields);
            build = SessionCritterCombat.From(entity, fields, carried);
        }
        _builds[(map.RuntimeMap, entity)] = build;
        return build;
    }

    /// <summary>The direction the hero faces when it attacks from <paramref name="from"/> toward <paramref name="to"/>: the
    /// neighbour direction of a step toward it, or null on the same cell.</summary>
    public static NeighborDirection? Facing(CellCoord from, CellCoord to)
    {
        var dx = Math.Sign((long)to.X - from.X); var dy = Math.Sign((long)to.Y - from.Y);
        return (dx, dy) switch
        {
            (-1, -1) => NeighborDirection.XMinusYMinus, (-1, 0) => NeighborDirection.XMinus, (-1, 1) => NeighborDirection.XMinusYPlus,
            (0, 1) => NeighborDirection.YPlus, (1, 1) => NeighborDirection.XPlusYPlus, (1, 0) => NeighborDirection.XPlus,
            (1, -1) => NeighborDirection.XPlusYMinus, (0, -1) => NeighborDirection.YMinus, _ => null,
        };
    }

    /// <summary>One log fragment for the critical effects of an attack (ARC-COMBAT-009): the table's effects and each item step.</summary>
    public static string Criticals(AttackResolution? resolution, IReadOnlyList<SessionCriticalStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var effects = resolution?.Damage is { } damage ? $"{damage.HitEffects}|{damage.MissEffects}" : "-";
        return effects + string.Concat(steps.Select(step => $",{step.Kind}:{step.Entity}:{step.Outcome}"));
    }

    /// <summary>One line for the status bar about an attack's result (no original text): the outcome, the damage and the
    /// target's hit points after it, a knockout or death, injuries and drops (ARC-COMBAT-009), and the experience it paid.</summary>
    public static string Describe(SessionAttackResult result, int? hitPoints, int? maxHitPoints)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Disposition == GameSessionDisposition.Rejected) return $"attack refused: {result.Failure}";
        if (result.Resolution is not { } resolution) return "attack: nothing happened";
        if (resolution.Refusal != AttackRefusal.None) return $"attack: {resolution.Refusal}";
        var where = resolution.Aimed ? $" (aimed at the {resolution.HitLocation.ToString().ToLowerInvariant()})" : string.Empty;
        var text = resolution.Outcome switch
        {
            AttackOutcome.Hit => "hit",
            AttackOutcome.CriticalHit => "critical hit",
            AttackOutcome.Miss => resolution.MissCause == AttackMissCause.Dodged ? "dodged" : "miss",
            AttackOutcome.CriticalMiss => "critical miss",
            _ => resolution.Outcome.ToString(),
        } + where;
        // ARC-DEV-261 (GT199): the victim's taking-damage script cancelled the damage step.
        if (result.Vetoed) return text + ": the target's script cancelled the damage";
        if (result.Life is { } life)
        {
            var self = result.Recipient == DamageRecipient.Attacker;
            text += self ? $": the hero takes {life.HitPointDamage} damage" : $": {life.HitPointDamage} damage";
            var fatigue = life.After.FatigueDamage - life.Before.FatigueDamage;
            if (fatigue > 0) text += $", {fatigue} fatigue";
            if (!self && hitPoints is { } hp && maxHitPoints is { } max) text += $", HP {Math.Max(hp, 0)}/{max}";
            if (life.Events.HasFlag(LifeEvents.Died)) text += self ? ", the hero dies" : ", killed";
            else if (life.Events.HasFlag(LifeEvents.LostConsciousness) || life.Events.HasFlag(LifeEvents.KnockedOut)) text += ", knocked out";
            else if (life.Events.HasFlag(LifeEvents.KnockedDown)) text += ", knocked down";
            if (life.Events.HasFlag(LifeEvents.Stunned)) text += ", stunned";
            if (life.Events.HasFlag(LifeEvents.Blinded)) text += ", blinded";
            if ((life.Events & (LifeEvents.CrippledArm | LifeEvents.CrippledBothArms | LifeEvents.CrippledLegs)) != 0) text += ", crippled";
            if (life.Events.HasFlag(LifeEvents.Scarred)) text += ", scarred";
        }
        else if (resolution.Damage is { Prevented: not DamagePrevention.None } prevented) text += $": no damage ({prevented.Prevented})";
        foreach (var step in result.Criticals)
            text += step.Outcome switch
            {
                SessionCriticalStepOutcome.Dropped => $", drops its {step.Slot?.ToString().ToLowerInvariant()}",
                SessionCriticalStepOutcome.HeroCellUnfit => $", would drop the {step.Slot?.ToString().ToLowerInvariant()} (no cell for it)",
                _ => string.Empty,
            };
        var experience = (result.HitAward?.Award ?? 0) + (result.KillAward?.Award ?? 0);
        if (experience != 0) text += $", +{experience} XP";
        if (result.StepFailure != GameSessionFailure.None) text += $" (step refused: {result.StepFailure})";
        return text;
    }
}

/// <summary>
/// ARC-COMBAT-005: one attack animation of the hero in real time (<see cref="RealTimeCombatRules"/> playback on the viewer's
/// clock): it starts on frame 0 and every due update is returned in order; the host runs the hit when an update is
/// <see cref="AttackUpdateKind.Hit"/> and stops on <see cref="AttackUpdateKind.Ended"/>.
/// </summary>
public sealed class AreaAttackPlayer
{
    public AreaAttackPlayer(AttackClip clip, long startMs)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Clip = clip; Playback = RealTimeCombatRules.Begin(clip); NextUpdateMs = startMs + RealTimeCombatRules.FirstUpdateDelayMs;
    }

    public AttackClip Clip { get; }
    public AttackPlayback Playback { get; private set; }
    public long NextUpdateMs { get; private set; }
    public bool Playing => Playback.Playing;

    /// <summary>Every update due by <paramref name="nowMs"/>, at most <paramref name="limit"/> (a long stall plays at most that
    /// many frames at once), each with the time it was due. <paramref name="canAct"/> is asked before each.</summary>
    public IReadOnlyList<(long AtMs, AttackUpdate Update)> Advance(long nowMs, Func<bool> canAct, int limit = 64)
    {
        ArgumentNullException.ThrowIfNull(canAct);
        var updates = new List<(long, AttackUpdate)>();
        while (Playback.Playing && NextUpdateMs <= nowMs && updates.Count < limit)
        {
            var at = NextUpdateMs;
            var update = RealTimeCombatRules.Update(Clip, Playback, canAct(), false, false);
            Playback = update.Playback;
            if (update.NextUpdateMs is { } delay) NextUpdateMs = at + delay;
            updates.Add((at, update));
        }
        return updates;
    }

    /// <summary>The hit found the target gone or out of reach: the animation ends without damage.</summary>
    public void Abort() => Playback = RealTimeCombatRules.Abort(Playback);
}
