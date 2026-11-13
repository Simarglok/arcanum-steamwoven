using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Combat;
using Arcanum.Data.Inventory;
using Arcanum.Data.Messages;
using Arcanum.Data.Scripts;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

public enum AreaInventoryTargetKind { GroundItem, DeadNpc, JunkPile, Container }

/// <summary>A clickable inventory object of the current map: an item on its ground, a dead NPC to loot, (ARC-WORLD-063) a junk
/// pile the session made, opened as a container, or (ARC-WORLD-101) a container of the map that has not broken.</summary>
public sealed record AreaInventoryTarget(AreaInventoryTargetKind Kind, string Entity, AreaObjectRecord Row);

public sealed record AreaInventoryLine(string Entity, string Text)
{
    /// <summary>ARC-SKILLS-005: the PC can use this item to heal (a healing item it holds).</summary>
    public bool Healing { get; init; }
    /// <summary>ARC-UI-049: a written item the PC holds; its use opens the reading window (or registers a schematic).</summary>
    public bool Written { get; init; }
    /// <summary>ARC-SKILLS-006: the PC can pick a lock with this item (lockpicks it holds).</summary>
    public bool Lockpick { get; init; }
    /// <summary>ARC-SKILLS-007: the item is damaged or broken, so the hero's Repair skill has something to do.</summary>
    public bool Repairable { get; init; }
    /// <summary>ARC-UI-025: a right click on the item would use it: a held item, not worn and without a wear location, with
    /// <c>OIF_CAN_USE_BOX</c> (IW5); the turn rule is the app's.</summary>
    public bool Usable { get; init; }
    /// <summary>ARC-UI-015: the wear slots the item can be put into (its wear location; a ring either ring slot); empty for an item
    /// that is worn now, worn nowhere, or whose wear facts the viewer does not have.</summary>
    public IReadOnlyList<EquipmentSlot> WearSlots { get; init; } = Array.Empty<EquipmentSlot>();
    /// <summary>ARC-UI-029: a gold or ammunition stack's kind and units (a move of more than one unit to another holder asks how
    /// many, ledger QW1); null for any other item.</summary>
    public SessionItemKind? StackKind { get; init; }
    public int Units { get; init; }
    /// <summary>ARC-UI-1884: an ammunition stack's type (0..3 arrows, bullets, charges, fuel), which the quantity pane's icon follows; null for gold and every other item.</summary>
    public int? StackAmmoType { get; init; }
}

/// <summary>ARC-UI-019: one item of the hero's grid as the window draws it: its entity, slot (row × 10 + column), size in cells and name.</summary>
public sealed record AreaGridItem(string Entity, int Slot, int Width, int Height, string Name);

/// <summary>ARC-UI-015: one wear slot of the paper doll: the slot and the worn item's entity and name, or none.</summary>
public sealed record AreaDollSlot(EquipmentSlot Slot, string? Entity, string? Name)
{
    /// <summary>ARC-UI-072: the worn item's kind in a looted body's doll (its inventory art comes from it); null in the hero's doll and for an empty slot.</summary>
    public SessionItemKind? Kind { get; init; }
}

/// <summary>What the inventory window shows: the PC's list and, when looting, the dead NPC's current list.</summary>
public sealed record AreaInventoryView(IReadOnlyList<AreaInventoryLine> Pc, string? Holder, string? HolderTitle, IReadOnlyList<AreaInventoryLine> Loot)
{
    /// <summary>ARC-SKILLS-010: the window shows a living NPC in steal mode (mode 3): a take of a listed item is a theft, the hero's
    /// items can be planted, and nothing is taken at once.</summary>
    public bool Steal { get; init; }

    /// <summary>ARC-RES-1327 (ledger XR3, XR16): the window is the container window See Contents opens (mode 4): read-only, nothing in it is
    /// picked, moved or taken, and it has no Take all button and no drop or use box.</summary>
    public bool Contents { get; init; }

    /// <summary>ARC-WORLD-027: the PC's gold (the hero's coins; 0 without a hero).</summary>
    public int Gold { get; init; }
    /// <summary>ARC-UI-015: the hero's nine wear slots in slot order (empty without a hero).</summary>
    public IReadOnlyList<AreaDollSlot> Doll { get; init; } = Array.Empty<AreaDollSlot>();
    /// <summary>ARC-UI-015: the hero's carried weight, carry weight and encumbrance level (null without a hero).</summary>
    public SessionHeroCarry? Carry { get; init; }
    /// <summary>ARC-UI-019: the PC's grid items in slot order.</summary>
    public IReadOnlyList<AreaGridItem> Grid { get; init; } = Array.Empty<AreaGridItem>();
    /// <summary>ARC-UI-019: the names of held items that are neither worn nor in the grid (no room), in list order.</summary>
    public IReadOnlyList<string> Unplaced { get; init; } = Array.Empty<string>();
    /// <summary>ARC-UI-029: the hero's ammunition counts by type (arrows, bullets, charge cells, fuel): its first stack of each type,
    /// 0 without one, as the original's window prints them after the gold (ledger IW9).</summary>
    public IReadOnlyList<int> Ammo { get; init; } = new int[SessionAmmoRecord.AmmoTypes];
    /// <summary>ARC-UI-051: the hero's effective speed (stat 11), the window's third line (IW24); null without a hero or when the
    /// stats do not read.</summary>
    public int? Speed { get; init; }
    /// <summary>ARC-UI-052: the looted holder's items at their grid locations (its worn items left out), in list order; empty in the
    /// player's own window.</summary>
    public IReadOnlyList<AreaLootGridItem> LootGrid { get; init; } = Array.Empty<AreaLootGridItem>();
    /// <summary>ARC-UI-072 (B30): the looted body's nine wear slots in slot order with the worn item of each (the doll view, IW29); empty for a
    /// container or a junk pile.</summary>
    public IReadOnlyList<AreaDollSlot> BodyDoll { get; init; } = Array.Empty<AreaDollSlot>();
    /// <summary>ARC-UI-1121 (B30, IW29, IW41–IW43): the numbers the doll view prints for the looted body and whether its weapon is too heavy for it (the viewer reads them from
    /// the combat host, <see cref="AreaCombatHost.DollRatings"/>); null when the combat host does not know the body (the doll view then prints nothing).</summary>
    public SessionDollRatings? BodyRatings { get; init; }
    /// <summary>ARC-UI-1267 (IW25, IW41, IW43): the numbers the hero's own window prints under its doll and whether its weapon is too heavy for it (the viewer reads them from
    /// the combat host, <see cref="AreaCombatHost.HeroDollRatings"/>); null when the combat host does not know the hero (the own window then prints nothing).</summary>
    public SessionDollRatings? HeroRatings { get; init; }
    /// <summary>ARC-UI-052: the interface art of the looted holder's image (<see cref="AreaLootArtLayout.HolderImage"/>); null when its facts
    /// do not decide it.</summary>
    public int? HolderImage { get; init; }
    /// <summary>ARC-UI-052: the looted holder is a container (a junk pile too): its grid scrolls further.</summary>
    public bool HolderContainer { get; init; }
}

/// <summary>
/// ARC-UI-004: the AREA viewer's side of the session inventory. It attaches the loaded maps' seeds to a session (again
/// after every restore), answers which drawn object a left click would pick up or loot, which drawn items the PC has
/// taken (no longer drawn or picked), runs the transfers with the viewer's insert check and builds the window's lines
/// with ARC-DATA-013 display names. It holds no session state: every answer reads the session it is given.
/// </summary>
public sealed class AreaInventoryHost
{
    /// <summary>Chebyshev reach of a pickup or loot click. Source (S): opening a container, PC or NPC moves within
    /// range 1 (the source's 0x428930); the pickup goal's own move-near range is not pinned, so pickup
    /// uses the same reach (project choice).</summary>
    public const int Reach = 1;

    /// <summary>PROJECT diagnostic insert check: every item fits. Carry weight, Technophobia and grid room need the
    /// created character, which the viewer does not have yet (NOT READY).</summary>
    public static SessionInsertCheck DiagnosticInsertCheck { get; } = _ => SessionInsertVerdict.Accepted;

    private readonly Dictionary<int, AreaInventoryMap> _maps;
    private readonly ScriptContent? _scripts;
    private readonly RecordResolver _resolver;
    private readonly Func<int, CombatFields?>? _prototypeFields;
    private readonly Dictionary<string, AreaHeldRow> _held = new(StringComparer.Ordinal);

    /// <param name="scripts">ARC-SCRIPT-013: the game's script content for an item's insert-item script (borrowed); without
    /// it such an item is refused, as before.</param>
    /// <param name="records">ARC-DEV-072 (MIG1): where the base records of maps this host does not hold are read from, by identity
    /// (<see cref="Resolver"/>); null: only these maps resolve.</param>
    /// <param name="limits">ARC-DEV-072: the resolver's bounds (maps held, rows per map).</param>
    /// <param name="prototypeFields">ARC-DEV-305: the combat fields of an item prototype by file number (null when it does not read): the facts of an item
    /// the session made, which has no object row in any map. Without it a made item has no wear, use, healing, lockpick, spell or condition facts, as before.</param>
    public AreaInventoryHost(IEnumerable<AreaInventoryMap> maps, SessionInsertCheck? insertCheck = null, ScriptContent? scripts = null,
        IMapRecordSource? records = null, RecordResolverLimits? limits = null, Func<int, CombatFields?>? prototypeFields = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = [];
        foreach (var map in maps)
            if (map is null || !_maps.TryAdd(map.RuntimeMap, map)) throw new ArgumentException("AREA_INVENTORY_MAP_DUPLICATE", nameof(maps));
        InsertCheck = insertCheck ?? DiagnosticInsertCheck;
        _scripts = scripts;
        _resolver = new(_maps.Values, records, limits);
        _prototypeFields = prototypeFields;
    }

    /// <summary>
    /// ARC-DEV-305 (B36, B01): the facts of an item the hero holds that the session made (<c>made:</c> entity): the combat fields of its
    /// prototype and its saved kind. The original makes such an object as a copy of its prototype, which holds its wear, use, healing,
    /// lockpick, spell and condition values until something writes them. The session holds the writes (condition, charges, scripts) apart
    /// from these. Null for anything that is not a made item the hero holds, or when the prototype does not read.
    /// </summary>
    private (SessionItemKind Kind, CombatFields Fields)? MadeFacts(GameSessionOwner session, string entity)
    {
        if (_prototypeFields is null || !entity.StartsWith("made:", StringComparison.Ordinal)) return null;
        var made = session.QueryMadeItems().FirstOrDefault(item => item.Entity == entity);
        return made.Entity is not null && _prototypeFields(made.Kind.Prototype) is { } fields ? (made.Kind, fields) : null;
    }

    /// <summary>ARC-DEV-072 (MIG1, TM3): the record resolver over this host's maps: an object's base record by identity, from these
    /// maps or from the home map it reads on demand.</summary>
    public RecordResolver Resolver => _resolver;

    /// <summary>
    /// ARC-DEV-366: the effect rule of a number, for an item script's add-effect action (the viewer sets the hero's effect tables, which it
    /// holds from the character start). Read on each use, so a table loaded after this host exists still answers; null: no rules.
    /// </summary>
    public Func<int, EffectRule?>? EffectRules { get; set; }

    /// <summary>The item script host over <paramref name="session"/>, or null without script content.</summary>
    public AreaItemScriptHost? ItemScripts(GameSessionOwner session) => _scripts is null ? null : new(session, _maps.Values, _scripts, number => EffectRules?.Invoke(number));

    public IReadOnlyCollection<AreaInventoryMap> Maps => _maps.Values;
    public SessionInsertCheck InsertCheck { get; }
    public bool Covers(int runtimeMap) => _maps.ContainsKey(runtimeMap);
    /// <summary>ARC-WORLD-101: the inventory map of a runtime map, or null.</summary>
    public AreaInventoryMap? Map(int runtimeMap) => _maps.GetValueOrDefault(runtimeMap);

    /// <summary>Attaches every map's seed; the first refusal is returned (a restored save that disagrees with these maps
    /// is <see cref="GameSessionFailure.InventoryMismatch"/>).</summary>
    public GameSessionFailure Attach(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var map in _maps.Values.OrderBy(map => map.RuntimeMap))
        {
            var attached = session.AttachMapInventory(new(map.RuntimeMap), map.Seed);
            if (attached.Disposition == GameSessionDisposition.Rejected) return attached.Failure;
        }
        // ARC-DEV-072: the maps read on demand are attached too, but a refusal of one of them (a map that is not shown) does not refuse the
        // session: that map's NPCs then have no holder entry, as if it had never been read, and the combat host says so once.
        foreach (var map in _resolver.ReadMaps)
            if (session.AttachMapInventory(new(map.RuntimeMap), map.Seed).Disposition == GameSessionDisposition.Rejected)
                Console.WriteLine($"ARC-DEV-072 record-map-attach refused map={map.RuntimeMap}");
        return GameSessionFailure.None;
    }

    /// <summary>
    /// ARC-DEV-354: the items whose row this host keeps when it can read it, with the map the session names as each one's origin: the items the PC
    /// holds, the items the hero gave an NPC (one taken from the world) and the hero's items placed on a map this host holds. The original names an
    /// object and reads its fields from its own record wherever it is (PS17, PS20: inventories are object fields, every item a record of its own).
    /// </summary>
    private IEnumerable<(string Entity, int Origin)> Carried(GameSessionOwner session)
    {
        foreach (var item in session.QueryPcInventory()) yield return (item.Entity, item.Origin.RuntimeMap);
        foreach (var given in session.QueryGivenItems())
            if (given.Origin is { } origin) yield return (given.Entity, origin.RuntimeMap);
        foreach (var placed in session.QueryPlacedItems())
            if (placed.Origin is { } origin && _maps.ContainsKey(placed.Map.RuntimeMap)) yield return (placed.Entity, origin.RuntimeMap);
    }

    /// <summary>
    /// ARC-DEV-354: the rows of the carried items (<see cref="Carried"/>) that neither a held map nor <see cref="Retain"/> gave this host, read by
    /// identity through the resolver from the map the session names as their origin: a window of the main map other than the shown one is read around
    /// the object's stored cell (<see cref="AreaMobileCells"/>), any other map whole. A fresh run (or a load into another window or map) has no rows
    /// handed over, so an item the hero took in another window of the main map had no row at all: no name ("[unknown object]"), no facts, no use.
    /// An item whose row still does not read stays without one, and the log says how many (ARC-DEV-1151: the next host of a map change is told and does not ask
    /// again, <see cref="UnreadCarried"/>; a load the host keeps passes <paramref name="retryUnread"/> and asks again). The viewer calls it once the session it adopts is the running one on this host (a new host after <see cref="Attach"/>, or a load
    /// this host keeps), not from <see cref="Attach"/>: a load to another map attaches its session to the host it is about to drop first, which would
    /// read the same windows for nothing, and the record source's callbacks read the running session. Returns how many rows were read and how many not.
    /// </summary>
    public (int Read, int Missing) ReadCarriedRows(GameSessionOwner session, bool retryUnread = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (retryUnread) _unread.Clear();
        int read = 0, missing = 0, skipped = 0;
        foreach (var (entity, origin) in Carried(session))
        {
            if (RowOf(entity) is not null || session.IsItemDestroyed(entity)) continue;
            // ARC-DEV-1151: a row an earlier host already failed to read from this origin is not asked again at a map change (see UnreadCarried).
            if (_unread.Contains((entity, origin))) { skipped++; continue; }
            if (_resolver.Resolve(entity, origin).Record is { } found)
            {
                _held[entity] = new(found.Row, found.Map.Seed.Items.FirstOrDefault(item => item.Entity == entity) is { } listed ? listed.Facts.Weight : null);
                read++;
            }
            else { _unread.Add((entity, origin)); missing++; }
        }
        if (read + missing + skipped > 0) Console.WriteLine($"ARC-DEV-354 carried-rows read={read} missing={missing}" + (skipped > 0 ? $" known-unread={skipped}" : string.Empty));
        return (read, missing);
    }

    // ARC-DEV-1151: the carried items (with the origin map asked) whose row did not read, kept with the host and handed to the next host at a map change.
    private readonly HashSet<(string Entity, int Origin)> _unread = [];

    /// <summary>
    /// ARC-DEV-1151 (the RowOn remainder of ARC-DEV-354): the carried items whose row this host asked for and did not get, with the origin map asked, still
    /// carried and still without a row. Handed to the next host by <see cref="RetainUnread"/> at a map change, so one window read a map change for such an
    /// item (the unreadable row asked again by every host) stops; a fresh run or a load (the state is new) asks once again.
    /// </summary>
    public IReadOnlyCollection<(string Entity, int Origin)> UnreadCarried(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Carried(session).Where(carried => _unread.Contains(carried) && RowOf(carried.Entity) is null && !session.IsItemDestroyed(carried.Entity)).Distinct().ToList();
    }

    /// <summary>ARC-DEV-1151: the unread carried items of the host this one replaces (<see cref="UnreadCarried"/>), not asked again by <see cref="ReadCarriedRows"/>.</summary>
    public void RetainUnread(IEnumerable<(string Entity, int Origin)> unread)
    {
        ArgumentNullException.ThrowIfNull(unread);
        foreach (var item in unread) _unread.Add(item);
    }

    /// <summary>ARC-DEV-072: a map the resolver read on demand has no seed in the session until it is first asked about (a given map's is
    /// attached with the host, and a restore attaches them again): attaches it now. The session's refusal, or null when there was nothing to
    /// attach (a given map, one already attached, one not read) or the seed was accepted.</summary>
    public GameSessionFailure? AttachReadMap(GameSessionOwner session, int runtimeMap)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_resolver.IsRead(runtimeMap) || session.QueryMapInventory(new(runtimeMap)) is not null || _resolver.Held(runtimeMap) is not { } read) return null;
        return session.AttachMapInventory(new(runtimeMap), read.Seed) is { Disposition: GameSessionDisposition.Rejected } refused ? refused.Failure : null;
    }

    /// <summary>
    /// ARC-DEV-227 (the dialog's PA condition, ledger PN2): an NPC's name number (object field 22, a script's rename over the map data's) and whether its own spell flags carry the mind-controlled bit (field 20, bit 27), read from
    /// the record the resolver finds by identity (a travelling companion's home map is read on demand, as <see cref="AreaCombat.Record"/> does); null when no record is found. The flags are the
    /// stored ones: a mind control cast in play is not modelled.
    /// </summary>
    public (int Name, bool MindControlled)? FollowerNameFacts(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        var record = _resolver.Resolve(entity).Record;
        if (record is null && EntityHomes.RecordedHomeOf(session, entity) is { } home) record = _resolver.Resolve(entity, home).Record;
        return record is null ? null : (AreaUseScriptHost.NameOf(session, record.Row), AreaUseScriptHost.IsMindControlled(record.Row));
    }

    /// <summary>ARC-DEV-087 (MIG6e): attaches the seed of the map that lists <paramref name="entity"/> when the resolver read it on demand and the
    /// session holds none yet, so an item moves between the object and the hero's list (the negative IN of a companion carried here from its
    /// home map). A refusal is named in the log; the move then finds no holder and is refused as before.</summary>
    public void AttachHolderMap(GameSessionOwner session, string entity)
    {
        if (_resolver.HeldHome(entity) is { } map && AttachReadMap(session, map) is { } refused)
            Console.WriteLine($"ARC-DEV-087 holder-map-attach refused map={map} failure={refused}");
    }

    /// <summary>The ground item, dead NPC or (ARC-WORLD-101) standing container a press on <paramref name="key"/> acts on; null for
    /// anything else (a taken item, a living NPC, a broken container, an object of a map without a seed). A ground item the session will
    /// refuse (gold, ammunition, a key ring, a pickup or insert script) is still a target: the click reports the refusal, as a locked container's
    /// open does.</summary>
    public AreaInventoryTarget? Target(GameSessionOwner session, int runtimeMap, AreaObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(session);
        // ARC-DEV-386 (E1-c4): a critter the session created on the map is found by its row's key in its one-row map.
        var map = _maps.GetValueOrDefault(runtimeMap);
        string? entity = null;
        if (map is null || !map.TryGetEntity(key, out entity!))
        {
            map = _resolver.CreatedMapsOn(runtimeMap).FirstOrDefault(created => created.TryGetEntity(key, out _));
            if (map is null || !map.TryGetEntity(key, out entity!)) return null;
        }
        if (session.QueryMapInventory(new(runtimeMap)) is not { } current) return null;
        if (current.Ground.Any(ground => ground.Item.Entity == entity)) return new(AreaInventoryTargetKind.GroundItem, entity, map.Row(entity));
        return current.Holders.FirstOrDefault(holder => holder.Entity == entity) switch
        {
            { Kind: SessionHolderKind.Npc, Dead: true } => new(AreaInventoryTargetKind.DeadNpc, entity, map.Row(entity)),
            { Kind: SessionHolderKind.Container } when AreaContainerLoot.Lootable(session, map, entity) => new(AreaInventoryTargetKind.Container, entity, map.Row(entity)),
            _ => null,
        };
    }

    /// <summary>The drawn objects of this map the PC has taken: they are neither drawn nor picked any more. ARC-WORLD-123: so are the seed
    /// items the session deleted by taking them (a key folded into the ring, an ammunition stack merged into a held one).</summary>
    public IReadOnlySet<AreaObjectKey> Taken(GameSessionOwner session, int runtimeMap)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map)) return new HashSet<AreaObjectKey>();
        return session.QueryPcInventory().Where(item => item.Origin.RuntimeMap == runtimeMap && map.TryGetRow(item.Entity, out _))
            .Select(item => map.Row(item.Entity).Key)
            .Concat(map.Seed.Items.Where(item => session.IsItemDestroyed(item.Entity) && map.TryGetRow(item.Entity, out _)).Select(item => map.Row(item.Entity).Key))
            .ToHashSet();
    }

    /// <summary>ARC-WORLD-063: the drawn objects of this map that lie in a junk pile the session made: the pile is drawn instead.</summary>
    public IReadOnlySet<AreaObjectKey> Piled(GameSessionOwner session, int runtimeMap)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map) || session.QueryMapInventory(new(runtimeMap)) is not { } current) return new HashSet<AreaObjectKey>();
        return current.JunkPiles.SelectMany(pile => pile.Items).Where(item => map.TryGetRow(item.Entity, out _)).Select(item => map.Row(item.Entity).Key).ToHashSet();
    }

    public InventoryMutationResult PickUp(GameSessionOwner session, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.PickUpItem(session.Version, item, InsertCheck, ItemScripts(session));
    }

    public InventoryMutationResult Loot(GameSessionOwner session, string holder, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        // ARC-MAGIC-1743: a worn item of a looted NPC runs its wield-off script over the NPC's own effects and stats when the area gives them.
        return session.LootItem(session.Version, holder, item, InsertCheck, ItemScripts(session), npcWearer: NpcWearer?.Invoke(session.QueryActor().Map, holder));
    }

    /// <summary>ARC-MAGIC-1743: the item script host, life target and stored stats of an NPC as the wearer of the item event scripts of a looted worn item (<see cref="Arcanum.Session.SessionNpcWieldScripts"/>); null while unset.</summary>
    public Func<SessionMapId, string, SessionNpcWieldScripts?>? NpcWearer { get; set; }

    /// <summary>The dialog's negative IN through the session (<see cref="SessionDialogInventoryPort"/>) with this insert check.</summary>
    public SessionDialogInventoryPort DialogPort(GameSessionOwner session, Func<DialogParticipantId, string?> entityOf) =>
        new(session, AreaConversationState.PcParticipant, entityOf, InsertCheck, ItemScripts(session), entity => AttachHolderMap(session, entity));

    /// <summary>ARC-DEV-199: the dialog's non-negative <c>IN</c> action through the session (<see cref="SessionDialogItemTakePort"/>): the hero's item goes to the speaker under the diagnostic NPC insert check
    /// (every item fits, as the barter's sales do); a follower carried from another map has its home map's seed attached before its list is read.</summary>
    public SessionDialogItemTakePort TakeItemDialogPort(GameSessionOwner session, Func<DialogParticipantId, string?> entityOf) =>
        new(session, AreaConversationState.PcParticipant, entityOf, DiagnosticInsertCheck, entity => AttachHolderMap(session, entity), NpcCell);

    // ARC-DEV-238: the cell of an NPC on the shown maps (its map row's, as the substitute container's range reads it), for the item a refused hand-over lays down.
    private CellCoord? NpcCell(string npc)
    {
        foreach (var map in _maps.Values.Concat(_resolver.CreatedMap(npc) is { } created ? [created] : []))
            if (map.TryGetRow(npc, out var row) && row.WorldX is { } x && row.WorldY is { } y && x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue)
                return new((int)x, (int)y);
        return null;
    }

    /// <summary>ARC-DEV-194: the dialog's <c>in</c> and <c>ni</c> conditions through the session (<see cref="SessionDialogItemHoldPort"/>); a follower carried from another map has its
    /// home map's seed attached before its list is read, and an NPC's substitute container is the one of <see cref="SubstituteContainer"/>.</summary>
    public SessionDialogItemHoldPort ItemHoldDialogPort(GameSessionOwner session, Func<DialogParticipantId, string?> entityOf) =>
        new(session, AreaConversationState.PcParticipant, entityOf, entity => SubstituteContainer(session, entity), entity => AttachHolderMap(session, entity));

    /// <summary>
    /// ARC-DEV-194 (BT11, 0x45F650): the substitute container of an NPC: it has none at T0 and does not follow the hero now (no leader), the area names a container of its inventory
    /// source (field 0x126) and the container lies within <see cref="AreaBarterMerchant.SubstituteRange"/> tiles of the NPC's cell (Chebyshev). The cell is the NPC's map row, not
    /// where the AI has walked it. Null for an NPC with none.
    /// </summary>
    public string? SubstituteContainer(GameSessionOwner session, string npc)
    {
        foreach (var map in _maps.Values)
            if (map.Substitute(npc) is { } substitute && map.TryGetRow(npc, out var row) && row.WorldX is { } x && row.WorldY is { } y
                && x is >= int.MinValue and <= int.MaxValue && y is >= int.MinValue and <= int.MaxValue
                && !session.QueryCompanion(npc).LedByPc
                && Arcanum.Simulation.Ai.AiExecutorRules.Distance(new((int)x, (int)y), substitute.Cell) <= AreaBarterMerchant.SubstituteRange)
                return substitute.Entity;
        return null;
    }

    /// <summary>ARC-DEV-179: the dialog's <c>$$</c> condition and action through the session (<see cref="SessionDialogGoldPort"/>): a new gold
    /// pile of an NPC without one is of <paramref name="pileKind"/> and takes the diagnostic NPC insert check (every item fits, as the barter's
    /// coins do); a follower carried from another map has its home map's seed attached before its gold is read.</summary>
    public SessionDialogGoldPort GoldDialogPort(GameSessionOwner session, Func<DialogParticipantId, string?> entityOf, SessionItemKind pileKind, bool shortPayerPaysNothing) =>
        new(session, AreaConversationState.PcParticipant, entityOf, pileKind, DiagnosticInsertCheck, shortPayerPaysNothing, entity => AttachHolderMap(session, entity));

    /// <summary>The shown name of an item or holder of any loaded map (the hover tooltip's rule); a taken item keeps its
    /// row. Unknown entities and nameless objects get a bracketed type label, never original text. An NPC is named by the original's
    /// examine rule for the local PC (ARC-DEV-297, NR2: the window's name 0x441B60 takes the PC as observer): its own description when the
    /// hero remembers it, else the not-yet-met one; <paramref name="recognition"/> is that test (null: never recognized).</summary>
    public string Name(string entity, ObjectDescriptionCatalog? catalog, Func<AreaObjectRecord, CritterRecognition>? recognition = null)
    {
        if (RowOf(entity) is { } row)
            return AreaHoverIndex.Text(row, catalog, recognition?.Invoke(row) ?? CritterRecognition.NotRecognized) ?? $"[{AreaHoverIndex.TypeName(row.Type)}: unnamed]";
        return "[unknown object]";
    }

    /// <summary>ARC-DEV-354: the row this host knows for <paramref name="entity"/> without reading anything: a held map's, else a carried item's
    /// (<see cref="Retain"/>, <see cref="ReadCarriedRows"/>); null when it knows none.</summary>
    public AreaObjectRecord? KnownRow(string entity) => RowOf(entity ?? throw new ArgumentNullException(nameof(entity)));

    /// <summary>The row of an entity among the held maps, else among the rows of items the hero carried out of a window of the same map (<see cref="Retain"/>).</summary>
    private AreaObjectRecord? RowOf(string entity)
    {
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row)) return row;
        // ARC-DEV-386: a created critter's row, so its loot window and name read it as a map NPC's.
        if (_resolver.CreatedMap(entity) is { } created && created.TryGetRow(entity, out var made)) return made;
        return _held.TryGetValue(entity, out var held) ? held.Row : null;
    }

    /// <summary>
    /// ARC-DEV-314 (B01): the rows and weights of the items the hero holds that this host can read now (the held maps' rows, or those it was given by
    /// <see cref="Retain"/>), to hand to the next host. A window of the main map is a map of its own (runtime map 1 every time), so the window the hero took an item from is not
    /// held after the window moves: its row, which gives the item's shown name and weight, would be lost with the host (the hero's window listed "[unknown object]"). The original names
    /// an object from its own record wherever it is.
    /// </summary>
    public IReadOnlyDictionary<string, AreaHeldRow> HeldRows(GameSessionOwner session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var rows = new Dictionary<string, AreaHeldRow>(StringComparer.Ordinal);
        // ARC-DEV-354: every carried item (given and placed ones too), not only those the PC holds.
        foreach (var (entity, _) in Carried(session))
            if (!session.IsItemDestroyed(entity) && RowOf(entity) is { } row) rows[entity] = new(row, Weight(entity));
        return rows;
    }

    /// <summary>ARC-DEV-314: rows <see cref="HeldRows"/> of the host this one replaces; a row of a held map is not overridden.</summary>
    public void Retain(IReadOnlyDictionary<string, AreaHeldRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var (entity, held) in rows) _held.TryAdd(entity, held);
    }

    /// <summary>ARC-UI-051: the hero's effective speed as the character sheet reads it (poison and both crippled legs count); null
    /// without a hero or a living record, or when the stats do not read.</summary>
    internal static int? HeroSpeed(GameSessionOwner session)
    {
        if (session.QueryHero() is null || session.QueryHeroLife() is not { } life) return null;
        var state = life.Record.State;
        var query = session.QueryHeroStats(session.HeroStatContext(state.PoisonLevel, state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)));
        return query?.Snapshot is { } stats ? stats.GetEffective(CharacterStat.Speed) : null;
    }

    /// <summary>The window's lines: the PC's list in order and, for <paramref name="holder"/> on <paramref name="runtimeMap"/>,
    /// that holder's current list.</summary>
    public AreaInventoryView View(GameSessionOwner session, ObjectDescriptionCatalog? catalog, int runtimeMap, string? holder = null,
        IReadOnlyList<SessionHealingItem?>? kitHealing = null, IReadOnlyList<SessionLockpick?>? kitLockpicks = null,
        IReadOnlyList<SessionWearItem?>? kitDurability = null, IReadOnlyList<SessionWearableItem?>? kitWear = null,
        IReadOnlyList<AreaItemUseFacts?>? kitUse = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        // ARC-WORLD-027: the hero's starting kit precedes the items taken from the world, as in the session's PC list; an
        // item the hero wears (ARC-WORLD-028 session equipment) is marked.
        var worn = session.QueryEquipment().Worn.Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        string Worn(string entity) => worn.Contains(entity) ? " (worn)" : string.Empty;
        // ARC-WORLD-066: an ammunition stack shows its units.
        string Units(string entity) => session.QueryAmmo(entity) is { } stack ? $" ×{stack.Units}" : string.Empty;
        var pc = session.QueryHeroKit().Select(item => new AreaInventoryLine(item.Entity, Line(KitName(item, catalog), item.Kind.Type, null) + " (starting kit)" + Worn(item.Entity))
            { Healing = HealingItem(session, item.Entity, kitHealing) is not null, Lockpick = Lockpick(session, item.Entity, kitLockpicks) is not null, Written = item.Kind.Type == SessionItemType.Written,
              Repairable = Repairable(session, item.Entity, kitDurability), WearSlots = Slots(item.Entity), Usable = Usable(item.Entity) })
            .Concat(session.QueryPcInventory().Select(item => new AreaInventoryLine(item.Entity, Line(Name(item.Entity, catalog), item.Kind.Type, Weight(item.Entity)) + Units(item.Entity) + Worn(item.Entity))
            { Healing = HealingItem(session, item.Entity, kitHealing) is not null, Lockpick = Lockpick(session, item.Entity, kitLockpicks) is not null, Written = item.Kind.Type == SessionItemType.Written,
              Repairable = Repairable(session, item.Entity, kitDurability), WearSlots = Slots(item.Entity), Usable = Usable(item.Entity) }))
            // ARC-WORLD-055: the items the hero made or took from an NPC that held made items follow, named by their kind.
            .Concat(session.QueryMadeItems().Select(item => new AreaInventoryLine(item.Entity, Line(KindName(item.Kind, catalog), item.Kind.Type, null) + Units(item.Entity) + " (made)" + Worn(item.Entity))
            // ARC-DEV-305 (B36): a made item's facts come from its prototype, so its line offers the same actions as a taken item's.
            { Healing = HealingItem(session, item.Entity, kitHealing) is not null, Lockpick = Lockpick(session, item.Entity, kitLockpicks) is not null, Written = item.Kind.Type == SessionItemType.Written,
              Repairable = Repairable(session, item.Entity, kitDurability), WearSlots = Slots(item.Entity), Usable = Usable(item.Entity) }))
            .Select(line => session.QueryAmmo(line.Entity) is { } stack ? line with { StackKind = HeldKind(line.Entity), Units = stack.Units, StackAmmoType = stack.AmmoType } : line)
            .ToArray();
        SessionItemKind? HeldKind(string entity) => session.QueryHeroKit().Select(item => (item.Entity, item.Kind)).Concat(session.QueryPcInventory().Select(item => (item.Entity, item.Kind)))
            .Concat(session.QueryMadeItems().Select(item => (item.Entity, item.Kind))).Where(pair => pair.Entity == entity).Select(pair => (SessionItemKind?)pair.Kind).FirstOrDefault();
        var gold = session.QueryGold();
        // ARC-UI-015: the paper doll lists the nine wear slots in slot order (the original's locations 1000..1008).
        var wornBySlot = session.QueryEquipment().Worn.ToDictionary(item => item.Slot, item => item.Entity);
        var doll = session.QueryHero() is null ? Array.Empty<AreaDollSlot>() : Enum.GetValues<EquipmentSlot>()
            .Select(slot => wornBySlot.TryGetValue(slot, out var entity) ? new AreaDollSlot(slot, entity, ItemName(session, entity, catalog)) : new AreaDollSlot(slot, null, null))
            .ToArray();
        var carry = session.QueryHeroCarry();
        // ARC-UI-019: the grid in slot order, with the items that found no room.
        var heroGrid = session.QueryHeroGrid();
        var grid = heroGrid.Items.Select(item => new AreaGridItem(item.Entity, item.Slot, item.Width, item.Height, ItemName(session, item.Entity, catalog))).ToArray();
        var unplaced = heroGrid.Unplaced.Select(entity => ItemName(session, entity, catalog)).ToArray();
        // ARC-UI-029: the four ammunition counts follow the gold (IW9: the stack each type's critter field names, the first here).
        var heroAmmo = session.QueryHeroAmmo();
        var ammo = Enumerable.Range(0, SessionAmmoRecord.AmmoTypes).Select(type => heroAmmo.FirstOrDefault(stack => stack.AmmoType == type).Units).ToArray();
        var speed = HeroSpeed(session);
        if (holder is null) return new(Array.AsReadOnly(pc), null, null, Array.Empty<AreaInventoryLine>())
            { Gold = gold, Doll = doll, Carry = carry, Grid = grid, Unplaced = unplaced, Ammo = ammo, Speed = speed };
        var current = session.QueryMapInventory(new(runtimeMap));
        // ARC-WORLD-063: a junk pile the session made lists its items in the pile's order.
        var pile = current?.JunkPiles.FirstOrDefault(p => p.Entity == holder);
        var list = pile?.Items ?? current?.Holders.FirstOrDefault(h => h.Entity == holder)?.Items ?? [];
        // ARC-WORLD-055: a made item the session added to the NPC has no area row; its kind names it (so does a kit or made item of the
        // hero's in a junk pile, ARC-WORLD-063).
        var kit = session.QueryPlacedItems().Where(item => item.Source == SessionPlacedSource.Kit).Select(item => item.Entity).ToHashSet(StringComparer.Ordinal);
        string? Rowless(string entity) => entity.StartsWith("made:", StringComparison.Ordinal) ? " (made)" : kit.Contains(entity) ? " (starting kit)" : null;
        var loot = list.Select(item => new AreaInventoryLine(item.Entity, (Rowless(item.Entity) is { } label
                ? Line(KindName(item.Facts.Kind, catalog) + label, item.Facts.Kind.Type, null) : Line(Name(item.Entity, catalog), item.Facts.Kind.Type, item.Facts.Weight))
            + (Stack(item.Facts) ? $" ×{item.Facts.Quantity}" : string.Empty) + (pile is null && item.Facts.Worn ? " (worn)" : string.Empty))
            { StackKind = Stack(item.Facts) ? item.Facts.Kind : null, Units = Stack(item.Facts) ? item.Facts.Quantity ?? 0 : 0, StackAmmoType = Stack(item.Facts) ? item.Facts.AmmoType : null }).ToArray();
        // ARC-UI-052: the loot window's grid shows the holder's items at their locations, its worn ones left out (0x577C6C).
        // A junk pile's item lies in the slot the session gave it in the pile (its facts keep the location it was dropped from). ARC-UI-055:
        // another item takes its size from the session (its inventory art's cells, 0x461780), drawn at most a container grid's size.
        var pileSlots = pile is null ? null : session.QueryJunkPiles().FirstOrDefault(p => p.Entity == holder)?.Items.ToDictionary(entry => entry.Entity, StringComparer.Ordinal);
        var lootGrid = AreaLootArtLayout.Arrange(list.Select(item => (Entry: item, Pile: pileSlots?.GetValueOrDefault(item.Entity)))
            .Select(pair => (pair.Entry, pair.Pile, Cells: pair.Pile is { } placed ? (placed.Width, placed.Height) : LootCells(session.QueryItemCells(pair.Entry.Entity))))
            .Select(row => new AreaLootGridItem(row.Entry.Entity, row.Pile?.Slot ?? row.Entry.Facts.InventoryLocation, row.Cells.Item1, row.Cells.Item2,
                Rowless(row.Entry.Entity) is not null ? KindName(row.Entry.Facts.Kind, catalog) : Name(row.Entry.Entity, catalog), row.Entry.Facts.Kind)).ToArray());
        // ARC-UI-072: the body's worn items by slot (its record over its map data, ARC-WORLD-103: the session's lists show it).
        var bodyDoll = pile is not null || current?.Holders.FirstOrDefault(h => h.Entity == holder) is not { } wearer || wearer.Kind != SessionHolderKind.Npc
            ? Array.Empty<AreaDollSlot>()
            : Enum.GetValues<EquipmentSlot>().Select(slot => wearer.Items.FirstOrDefault(item => item.Facts.Worn && item.Facts.InventoryLocation == (int)slot) is { } worn
                ? new AreaDollSlot(slot, worn.Entity, Rowless(worn.Entity) is not null ? KindName(worn.Facts.Kind, catalog) : Name(worn.Entity, catalog)) { Kind = worn.Facts.Kind } : new AreaDollSlot(slot, null, null)).ToArray();
        var (image, container) = pile is not null
            ? (AreaLootArtLayout.HolderImage(CombatFieldReader.ContainerType, 0, -1, ObjectDamageRules.JunkPilePrototype), true)
            : HolderLook(runtimeMap, holder);
        return new(Array.AsReadOnly(pc), holder, pile is not null ? JunkPileTitle : Name(holder, catalog), Array.AsReadOnly(loot))
            { Gold = gold, Doll = doll, Carry = carry, Grid = grid, Unplaced = unplaced, Ammo = ammo, Speed = speed, LootGrid = lootGrid, HolderImage = image,
              HolderContainer = container, BodyDoll = bodyDoll };

        // ARC-UI-029: a gold pile shows its coins as an ammunition stack shows its units.
        static (int, int) LootCells((int Width, int Height) cells) =>
            (Math.Clamp(cells.Width, 1, InventoryGridRules.Columns), Math.Clamp(cells.Height, 1, InventoryGridRules.ContainerRows));
        static bool Stack(SessionItemFacts facts) => facts.AmmoType is not null || facts.Kind.Type == SessionItemType.Gold && facts.Quantity is not null;

        IReadOnlyList<EquipmentSlot> Slots(string entity) => worn.Contains(entity) ? Array.Empty<EquipmentSlot>() : WearSlots(Wearable(session, entity, kitWear));
        bool Usable(string entity) => !worn.Contains(entity) && UseFacts(session, entity, kitUse) is { CanUseBox: true, Wearable: false };
    }

    /// <summary>ARC-UI-064: the image of a seed holder (the barter window draws its merchant's, not its substitute container's); null when its
    /// row or fields do not read.</summary>
    public int? HolderImage(int runtimeMap, string holder) => HolderLook(runtimeMap, holder ?? throw new ArgumentNullException(nameof(holder))).Image;

    /// <summary>ARC-UI-052: the loot window's image of a seed holder (<see cref="AreaLootArtLayout.HolderImage"/>) from its object row: a
    /// critter's flags and race, a container's description (its prototype file less 20, as the session's junk piles count it); and whether
    /// it is a container. No image when its row or fields do not read.</summary>

    private (int? Image, bool Container) HolderLook(int runtimeMap, string holder)
    {
        AreaObjectRecord? row = null;
        if (_maps.TryGetValue(runtimeMap, out var map) && map.TryGetRow(holder, out var own)) row = own;
        else if (_resolver.CreatedMap(holder) is { } created && created.RuntimeMap == runtimeMap && created.TryGetRow(holder, out var made)) row = made;
        if (row is null) return (null, false);
        var container = row.Type == CombatFieldReader.ContainerType;
        if (container) return (AreaLootArtLayout.HolderImage(row.Type, 0, -1, row.PrototypeNumber - 20), true);
        return CombatFieldReader.Read(row).Fields is { Critter: { } critter } fields
            ? (AreaLootArtLayout.HolderImage(fields.Object.Type, critter.Flags, critter.BaseStats.Count > 27 ? critter.BaseStats[27] : -1, 0), false)
            : (null, false);
    }

    /// <summary>
    /// ARC-UI-025: the use facts of an item the PC holds (listed or worn): a kit item's from <paramref name="kitUse"/> (the hero load's, by
    /// kit number), a taken item's from its object row's fields (<see cref="AreaItemUseFacts.From"/>; ARC-DEV-306: the row of an item taken
    /// on another map is read through the resolver). A made item's from its prototype (ARC-DEV-305, <see cref="MadeFacts"/>). Null for an
    /// item the session no longer has or whose fields do not read.
    /// </summary>
    public AreaItemUseFacts? UseFacts(GameSessionOwner session, string entity, IReadOnlyList<AreaItemUseFacts?>? kitUse)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity))
            return KitNumber(entity) is { } number && kitUse is not null && number <= kitUse.Count ? kitUse[number - 1] : null;
        if (MadeFacts(session, entity) is { } made) return AreaItemUseFacts.From(entity, made.Fields);
        return TakenRow(session, entity) is { } row && CombatFieldReader.Read(row).Fields is { } fields ? AreaItemUseFacts.From(entity, fields) : null;
    }

    /// <summary>
    /// ARC-DEV-306 (B32): the object row of an item the PC took from a map: among the held maps first, then the map the session names as the
    /// item's origin, which the resolver reads on demand (<see cref="RecordResolver"/>), so an item taken on another map is still read by
    /// identity (a note taken on the main map and used in the cave). Null for an item the PC does not hold, or one no map lists.
    /// </summary>
    private AreaObjectRecord? TakenRow(GameSessionOwner session, string entity)
    {
        var taken = session.QueryPcInventory().FirstOrDefault(item => item.Entity == entity);
        if (taken.Entity is null) return null;
        if (RowOf(entity) is { } row) return row;
        return _resolver.Resolve(entity, taken.Origin.RuntimeMap).Record?.Row;
    }

    /// <summary>
    /// ARC-DEV-316: the stored row of <paramref name="entity"/>, found among the held maps or read on demand by the resolver from
    /// <paramref name="originMap"/>, the map the session names as the item's origin (the use script host's answer for an item taken on another
    /// map; the same lookup as <see cref="TakenRow"/>, ARC-DEV-336: the rows handed over by <see cref="Retain"/> answer before the resolver). Null when no map lists the entity or the origin map cannot be read.
    /// </summary>
    public AreaObjectRecord? RowOn(int originMap, string entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        // ARC-DEV-336 (B51): the held maps and the rows the hero carried out of an earlier window come first (a window of the main map is runtime map 1 each
        // time, so the resolver, which holds one window of it, cannot answer for an item taken in another window).
        return RowOf(entity) ?? _resolver.Resolve(entity, originMap).Record?.Row;
    }

    /// <summary>
    /// ARC-UI-015: the wear facts of an item the PC holds, for <see cref="GameSessionOwner.Equip"/>: a kit item's from
    /// <paramref name="kitWear"/> (the hero load's, by kit number, as the best-items start reads them), a taken item's from its object
    /// row's fields (<see cref="SessionEquipmentFacts.Wearable"/> with the saved kind). a made item's from its prototype (ARC-DEV-305).
    /// Null for an item worn nowhere, an item the session no longer has, or a session without a hero.
    /// </summary>
    public SessionWearableItem? Wearable(GameSessionOwner session, string entity, IReadOnlyList<SessionWearableItem?>? kitWear)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null || session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity))
            return KitNumber(entity) is { } number && kitWear is not null && number <= kitWear.Count ? kitWear[number - 1] : null;
        if (MadeFacts(session, entity) is { } made) return SessionEquipmentFacts.Wearable(made.Kind, made.Fields);
        var taken = session.QueryPcInventory().FirstOrDefault(item => item.Entity == entity);
        if (taken.Entity is null) return null;
        return TakenRow(session, entity) is { } row && CombatFieldReader.Read(row).Fields is { } fields ? SessionEquipmentFacts.Wearable(taken.Kind, fields) : null;
    }

    /// <summary>
    /// ARC-UI-019: the facts of an item the PC holds for a move in its grid (<see cref="GameSessionOwner.MoveHeroItem"/>): a kit item's from
    /// <paramref name="kitMove"/> (the hero load's, by kit number), a taken item's from its object row's fields with the saved kind, a made
    /// item's from its prototype's fields when the host has a prototype reader (ARC-DEV-305), else its kind with no no-drop flag and no scripts. Null for an item the session no
    /// longer has or whose facts do not read.
    /// </summary>
    public SessionGridMoveItem? GridMoveItem(GameSessionOwner session, string entity, IReadOnlyList<SessionGridMoveItem?>? kitMove)
    {
        ArgumentNullException.ThrowIfNull(session);
        // ARC-UI-090: the hero's gold pile moves like an item with no no-drop flag and no scripts (ledger HG1).
        if (entity == SessionHeroGrid.GoldEntity) return session.QueryGold() > 0 ? new(SessionHeroGrid.GoldKind, false, []) : null;
        if (session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity))
            return KitNumber(entity) is { } number && kitMove is not null && number <= kitMove.Count ? kitMove[number - 1] : null;
        // ARC-DEV-305: a made item's no-drop flag and script points come from its prototype when the host reads it; without a reader it has none, as before.
        var made = session.QueryMadeItems().FirstOrDefault(item => item.Entity == entity);
        if (made.Entity is not null) return MadeFacts(session, entity) is { } madeFacts ? GridMoveFacts(madeFacts.Kind, madeFacts.Fields) : new(made.Kind, false, []);
        var taken = session.QueryPcInventory().FirstOrDefault(item => item.Entity == entity);
        if (taken.Entity is null) return null;
        return TakenRow(session, entity) is { } row && CombatFieldReader.Read(row).Fields is { } fields ? GridMoveFacts(taken.Kind, fields) : null;
    }

    /// <summary>ARC-UI-019: an item's grid move facts from its combat fields: the no-drop item flag (0x20) and its script points.</summary>
    public static SessionGridMoveItem GridMoveFacts(SessionItemKind kind, CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new(kind, fields.Item is { } stored && (stored.Flags & NoDropFlag) != 0, fields.Object.ScriptPoints);
    }

    private const uint NoDropFlag = 0x20;

    /// <summary>ARC-UI-015: the slots an item can be put into: its wear location (<see cref="EquipmentRules.WearSlot"/>), for a ring
    /// either ring slot (the original's doll has two ring locations, 1001 and 1002, and the wear check maps both to the ring's).</summary>
    public static IReadOnlyList<EquipmentSlot> WearSlots(SessionWearableItem? item) => item is null || EquipmentRules.WearSlot(item.Item) is not { } slot
        ? Array.Empty<EquipmentSlot>() : slot == EquipmentSlot.Ring1 ? [EquipmentSlot.Ring1, EquipmentSlot.Ring2] : [slot];

    // kit:1 is the first kit item; a kit entity keeps its number when an earlier kit item is gone.
    private static int? KitNumber(string entity) =>
        entity.StartsWith("kit:", StringComparison.Ordinal) && int.TryParse(entity.AsSpan(4), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= 1 ? number : null;

    /// <summary>ARC-UI-015: the shown name of an item the PC holds: a kit or made item's by its kind, a taken item's by its row.</summary>
    public string ItemName(GameSessionOwner session, string entity, ObjectDescriptionCatalog? catalog)
    {
        ArgumentNullException.ThrowIfNull(session);
        // ARC-UI-090: the hero's gold pile has no map row to name it; the project labels it with its coins.
        if (entity == SessionHeroGrid.GoldEntity) return $"gold ×{session.QueryGold()}";
        var kit = session.QueryHeroKit().FirstOrDefault(item => item.Entity == entity);
        if (kit.Entity is not null) return KitName(kit, catalog);
        var made = session.QueryMadeItems().FirstOrDefault(item => item.Entity == entity);
        if (made.Entity is not null) return KindName(made.Kind, catalog);
        return Name(entity, catalog);
    }

    /// <summary>ARC-UI-015: a wear slot's shown name.</summary>
    public static string SlotName(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Ring1 => "ring 1",
        EquipmentSlot.Ring2 => "ring 2",
        _ => slot.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// ARC-UI-015: plain text for a wear or take-off from the window: what moved (and the occupant that went back to the pack), or
    /// the refusal; the lines the move's scripts printed follow.
    /// </summary>
    public static string WearText(EquipmentMutationResult result, string name, EquipmentSlot slot, bool takeOff, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        var printed = result.Messages.Count == 0 ? string.Empty : " — " + string.Join(" ", result.Messages.Select(message => message.Text.Trim()));
        if (result.RolledBack) return $"not {(takeOff ? "taken off" : "worn")}: {Refusal(GameSessionFailure.InsertRefused, result.Verdict)}; both items went back{printed}";
        if (result.Disposition == GameSessionDisposition.NoChange) return (takeOff ? $"nothing worn as {SlotName(slot)}" : $"{name} is already worn as {SlotName(slot)}") + printed;
        if (result.Disposition == GameSessionDisposition.Rejected) return $"not {(takeOff ? "taken off" : "worn")}: {WearRefusal(result)}{printed}";
        if (takeOff) return $"took off {name}{printed}";
        return $"wore {name} as {SlotName(slot)}" + (result.Displaced is { } displaced ? $"; {nameOf(displaced)} went back to the pack" : string.Empty) + printed;
    }

    /// <summary>ARC-UI-019: plain text for a grid move or a take-off into a grid cell: where the item went, or why it stayed (a refused
    /// release leaves it where it was).</summary>
    public static string GridText(EquipmentMutationResult result, string name, int slot)
    {
        var printed = result.Messages.Count == 0 ? string.Empty : " — " + string.Join(" ", result.Messages.Select(message => message.Text.Trim()));
        var cell = $"row {slot / SessionHeroGrid.Columns + 1}, column {slot % SessionHeroGrid.Columns + 1}";
        if (result.RolledBack) return $"{name} stayed where it was: {Refusal(GameSessionFailure.InsertRefused, result.Verdict)}{printed}";
        if (result.Disposition == GameSessionDisposition.NoChange) return $"{name} is already at {cell}{printed}";
        if (result.Disposition == GameSessionDisposition.Changed) return $"moved {name} to {cell}{printed}";
        return $"{name} stayed where it was: " + (result.Failure == GameSessionFailure.GridCellsTaken ? $"no room for it at {cell}" : WearRefusal(result)) + printed;
    }

    private static string WearRefusal(EquipmentMutationResult result) => result.Failure switch
    {
        GameSessionFailure.WearRefused => result.Refusal switch
        {
            EquipmentRefusal.NotWieldable => "the hero cannot wield it",
            EquipmentRefusal.WieldMagicItems => "the hero's Magick Allergy refuses magickal items",
            EquipmentRefusal.Broken => "the item is broken",
            EquipmentRefusal.WrongType => "the item does not belong in that slot",
            EquipmentRefusal.NoFreeHand => "a two-handed weapon and a shield do not go together",
            EquipmentRefusal.Crippled => "a crippled arm prevents it",
            EquipmentRefusal.WrongSize => "the armour is the wrong size",
            EquipmentRefusal.WrongGender => "the armour is for the other gender",
            EquipmentRefusal.NotDroppable => "the item cannot be moved (no-drop)",
            var other => other.ToString(),
        },
        GameSessionFailure.EquipmentUnavailable => "no hero",
        GameSessionFailure.ItemEffectUnsupported => "the item carries item spells (not supported here)",
        GameSessionFailure.InvalidItem => "the item's wear values are not readable",
        var failure => Refusal(failure, result.Verdict),
    };

    /// <summary>
    /// ARC-SKILLS-005: the healing-item facts of an item the PC holds: a kit item's from <paramref name="kitHealing"/> (the hero
    /// load's, by kit entity), a taken item's from its object row's fields (<see cref="AreaHealingItems.From"/>); null for anything
    /// else, an item the session no longer has, or a session without a hero (nobody to use it).
    /// </summary>
    public SessionHealingItem? HealingItem(GameSessionOwner session, string entity, IReadOnlyList<SessionHealingItem?>? kitHealing)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null || session.IsItemDestroyed(entity)) return null;
        // A kit entity keeps its number when an earlier kit item is gone, so the facts are found by entity.
        if (session.QueryHeroKit().Any(item => item.Entity == entity)) return kitHealing?.FirstOrDefault(item => item?.Entity == entity);
        if (MadeFacts(session, entity) is { } made) return AreaHealingItems.From(entity, made.Fields);
        if (!session.QueryPcInventory().Any(item => item.Entity == entity)) return null;
        return TakenRow(session, entity) is { } row && CombatFieldReader.Read(row).Fields is { } fields ? AreaHealingItems.From(entity, fields) : null;
    }

    /// <summary>
    /// ARC-UI-049: the written facts of an item the PC holds: a taken item's from its object row's fields (instance over prototype; ARC-DEV-306: the row of an item taken on another map is read through the resolver), a
    /// kit or made item's from its prototype in <paramref name="data"/>; null for anything else or an item the session no longer has.
    /// </summary>
    public SessionWrittenFacts? WrittenFacts(GameSessionOwner session, string entity, AreaReadingData? data)
    {
        ArgumentNullException.ThrowIfNull(session);
        // ARC-SCRIPT-1279 (WO6): a start line a script wrote (set written start, 94) replaces the data's.
        var facts = WrittenFactsOfData(session, entity, data);
        return facts is { } read && session.QueryItemFieldWrites(entity).WrittenStart is { } start ? read with { TextStartLine = start } : facts;
    }

    private SessionWrittenFacts? WrittenFactsOfData(GameSessionOwner session, string entity, AreaReadingData? data)
    {
        if (session.IsItemDestroyed(entity)) return null;
        var kit = session.QueryHeroKit().FirstOrDefault(item => item.Entity == entity);
        if (kit.Entity is not null) return data is not null && data.Prototypes.TryGetValue(kit.Kind.Prototype, out var fromKit) ? fromKit : null;
        var made = session.QueryMadeItems().FirstOrDefault(item => item.Entity == entity);
        if (made.Entity is not null) return data is not null && data.Prototypes.TryGetValue(made.Kind.Prototype, out var fromMade) ? fromMade : null;
        return TakenRow(session, entity) is { } row && CombatFieldReader.Read(row).Fields is { } fields ? SessionWrittenFacts.FromCombatFields(fields) : null;
    }

    /// <summary>ARC-SKILLS-006: the lockpick facts of an item the PC holds, found as <see cref="HealingItem"/> finds a healing item's
    /// (<see cref="AreaLockpicks.From"/>); null for anything else, an item the session no longer has, or a session without a hero.</summary>
    public SessionLockpick? Lockpick(GameSessionOwner session, string entity, IReadOnlyList<SessionLockpick?>? kitLockpicks)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null || session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity)) return kitLockpicks?.FirstOrDefault(item => item?.Entity == entity);
        if (MadeFacts(session, entity) is { } made) return AreaLockpicks.From(entity, made.Fields);
        if (!session.QueryPcInventory().Any(item => item.Entity == entity)) return null;
        return TakenRow(session, entity) is { } row && CombatFieldReader.Read(row).Fields is { } fields ? AreaLockpicks.From(entity, fields) : null;
    }

    /// <summary>ARC-UI-016: the spell facts of an item the hero holds (the cast host's item lookup): a kit item's from
    /// <paramref name="kitItemSpells"/> (the hero load's, by kit entity), a taken item's from its object row
    /// (<see cref="SessionItemSpells.FromCombatFields"/>), whether listed or worn; null for anything else, an item the session no
    /// longer has, or a session without a hero. ARC-DEV-140 (B14): with <paramref name="thrownOnMap"/> (the cast host's lookup only) also a taken item the hero threw, which lies on the map now: the item is its own holder; the item-use callers leave it off.</summary>
    public SessionItemSpells? ItemSpells(GameSessionOwner session, string entity, IReadOnlyList<SessionItemSpells?>? kitItemSpells, bool thrownOnMap = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null || session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity)) return kitItemSpells?.FirstOrDefault(item => item?.Entity == entity);
        // ARC-DEV-140 (B14): a thrown item lies on the map where its projectile ended and the shot's end names it (0x4CBDB0): its own spells fire with the item as their parent (0x450A50: an item on the map is its own parent).
        // ARC-DEV-305: a made item the hero holds is held, not on the map; its spells come from its prototype.
        if (MadeFacts(session, entity) is { } made) return SessionItemSpells.FromCombatFields(entity, SessionSpellCaster.Hero, made.Fields);
        var onMap = !session.QueryPcInventory().Any(item => item.Entity == entity) && !session.QueryEquipment().Worn.Any(item => item.Entity == entity);
        if (onMap && !thrownOnMap) return null;
        if (onMap && !session.QueryPlacedItems().Any(item => item.Entity == entity)) return null;
        // ARC-DEV-305: a made item the hero threw lies on the map as a placed item with no row; its spells come from its prototype too.
        if (onMap && entity.StartsWith("made:", StringComparison.Ordinal) && _prototypeFields is not null
            && session.QueryPlacedItems().First(item => item.Entity == entity) is { Source: SessionPlacedSource.Made or SessionPlacedSource.Created } placed
            && _prototypeFields(placed.Kind.Prototype) is { } placedFields)
            return SessionItemSpells.FromCombatFields(entity, entity, placedFields);
        if (RowOf(entity) is { } spellRow && CombatFieldReader.Read(spellRow).Fields is { } fields)
            return SessionItemSpells.FromCombatFields(entity, onMap ? entity : SessionSpellCaster.Hero, fields);
        return null;
    }

    /// <summary>ARC-SKILLS-007: the T0 condition of an item the PC holds: a kit item's from <paramref name="kitDurability"/> (the hero
    /// load's, by kit entity), a taken item's from its object row (<see cref="SessionItemConditions.FromCombatFields"/>); null for an
    /// item without one, an item the session no longer has, or a session without a hero.</summary>
    public ItemCondition? RepairBaseline(GameSessionOwner session, string entity, IReadOnlyList<SessionWearItem?>? kitDurability)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null || session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity)) return kitDurability?.FirstOrDefault(item => item?.Entity == entity)?.Baseline;
        if (MadeFacts(session, entity) is { } made) return SessionItemConditions.FromCombatFields(made.Fields);
        if (!session.QueryPcInventory().Any(item => item.Entity == entity)) return null;
        return TakenRow(session, entity) is { } row && CombatFieldReader.Read(row).Fields is { } fields ? SessionItemConditions.FromCombatFields(fields) : null;
    }

    // An item whose current condition (the session's record over its baseline) is damaged or broken.
    private bool Repairable(GameSessionOwner session, string entity, IReadOnlyList<SessionWearItem?>? kitDurability) =>
        RepairBaseline(session, entity, kitDurability) is { } baseline && session.QueryItemCondition(entity, baseline) is { } condition
        && (condition.Damage > 0 || condition.Broken);

    /// <summary>A kit item's shown name: the text of its prototype's description number (the identified name; the kit's
    /// identification state is not modelled). No catalog, an absent or undecodable text gives a bracketed label.</summary>
    public static string KitName(SessionKitItem item, ObjectDescriptionCatalog? catalog) => KindName(item.Kind, catalog);

    /// <summary>ARC-WORLD-063: the window title of a junk pile the session made (a project label; the pile has no map row to name it).</summary>
    public const string JunkPileTitle = "[junk pile]";

    /// <summary>An item's shown name from its saved kind: the text of its description number, as <see cref="KitName"/>.</summary>
    public static string KindName(SessionItemKind kind, ObjectDescriptionCatalog? catalog)
    {
        var type = AreaHoverIndex.TypeName((int)kind.Type);
        if (catalog is null) return $"[{type}: name unavailable]";
        var found = catalog.Find(kind.Description);
        if (found.Status != DescriptionLookupStatus.Found || found.Message is null) return $"[{type}: unnamed]";
        return MesText.Decode(found.Message, AreaHoverIndex.TextProfile) is { Status: MesDecodeStatus.Decoded, Text: { Length: > 0 } decoded }
            ? decoded : $"[{type}: name not decodable]";
    }

    private int? Weight(string entity)
    {
        foreach (var map in _maps.Values)
            if (map.Seed.Items.FirstOrDefault(item => item.Entity == entity) is { } found) return found.Facts.Weight;
        return _held.TryGetValue(entity, out var held) ? held.Weight : null;
    }

    private static string Line(string name, SessionItemType type, int? weight) =>
        $"{name} ({AreaHoverIndex.TypeName((int)type)}{(weight is { } w ? $", weight {w}" : string.Empty)})";

    /// <summary>ARC-SCRIPT-013: the lines the item's insert-item script printed, as a suffix for the status line and the
    /// window's message (empty when none).</summary>
    public static string Printed(InventoryMutationResult result) =>
        result.Messages.Count == 0 ? string.Empty : " — " + string.Join(" ", result.Messages.Select(message => message.Text.Trim()));

    /// <summary>ARC-SCRIPT-041: whether the transfer moved the item (a gate script's skip default leaves it, maybe with flag writes).</summary>
    public static bool Moved(InventoryMutationResult result) => result.Disposition == GameSessionDisposition.Changed && result.Item is not null;

    /// <summary>Plain text for a transfer that did not move the item: a gate script that declined it (ARC-SCRIPT-041, with what it
    /// printed), or the refusal.</summary>
    public static string NotMoved(InventoryMutationResult result) => result.DeclinedBy is { } point
        ? (point == SessionScriptPoint.Get ? "the item's get script declined it" : "the PC's transfer script declined it") + Printed(result)
        : Refusal(result.Failure, result.Verdict);

    /// <summary>Plain text for a transfer refusal shown in the window and the status line; an insert check's Technophobia answer (ARC-WORLD-069) names the background's refusal
    /// (<c>mes\item.mes</c> 115 in the original).</summary>
    public static string Refusal(GameSessionFailure failure, SessionInsertVerdict? verdict = null) => failure switch
    {
        GameSessionFailure.InsertRefused when verdict == SessionInsertVerdict.TechnologyRefused => "the hero's Technophobia refuses technological items",
        GameSessionFailure.InsertRefused when verdict == SessionInsertVerdict.TooHeavy => "the PC cannot carry that much weight",
        GameSessionFailure.InsertRefused when verdict == SessionInsertVerdict.NoRoom => "no free room in the grid",
        // ARC-WORLD-1283: the remove check's refusal (0x466DA0, code 11; the PC's message is item.mes 111, InventoryMutationResult.PcMessage), shown in the viewer's own wording.
        GameSessionFailure.ItemNotDroppable => "the item cannot be dropped, so it cannot change hands",
        GameSessionFailure.ItemScriptUnsupported => "the item runs a script on this transfer that is not supported here",
        GameSessionFailure.HolderScriptUnsupported => "the body runs a use script (not supported here)",
        GameSessionFailure.ItemTypeUnsupported => "gold, key rings and keys without a hero's ring are not modelled",
        GameSessionFailure.KeyRingFull => "the key ring is full",
        GameSessionFailure.WornItemUnsupported => "worn items are not modelled",
        GameSessionFailure.ItemNotAvailable => "the item is no longer there",
        GameSessionFailure.HolderNotLootable => "only a dead NPC or a standing container can be looted",
        GameSessionFailure.InsertRefused => "the PC cannot take it",
        GameSessionFailure.BudgetExceeded => "the PC's list is full",
        GameSessionFailure.InventoryUnavailable => "no inventory on this map",
        // ARC-UI-029: the stack moves after the quantity window.
        GameSessionFailure.GoldUnavailable => "there is no hero to hold gold",
        GameSessionFailure.GoldShort or GameSessionFailure.AmmoShort => "fewer units are left than asked",
        GameSessionFailure.UnknownHolder => "the holder is not known here",
        _ => failure.ToString(),
    };
}
