using Arcanum.Data.Area;
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

    /// <param name="scripts">ARC-SCRIPT-013: the game's script content for an item's insert-item script (borrowed); without
    /// it such an item is refused, as before.</param>
    /// <param name="records">ARC-DEV-072 (MIG1): where the base records of maps this host does not hold are read from, by identity
    /// (<see cref="Resolver"/>); null: only these maps resolve.</param>
    /// <param name="limits">ARC-DEV-072: the resolver's bounds (maps held, rows per map).</param>
    public AreaInventoryHost(IEnumerable<AreaInventoryMap> maps, SessionInsertCheck? insertCheck = null, ScriptContent? scripts = null,
        IMapRecordSource? records = null, RecordResolverLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = [];
        foreach (var map in maps)
            if (map is null || !_maps.TryAdd(map.RuntimeMap, map)) throw new ArgumentException("AREA_INVENTORY_MAP_DUPLICATE", nameof(maps));
        InsertCheck = insertCheck ?? DiagnosticInsertCheck;
        _scripts = scripts;
        _resolver = new(_maps.Values, records, limits);
    }

    /// <summary>ARC-DEV-072 (MIG1, TM3): the record resolver over this host's maps: an object's base record by identity, from these
    /// maps or from the home map it reads on demand.</summary>
    public RecordResolver Resolver => _resolver;

    /// <summary>The item script host over <paramref name="session"/>, or null without script content.</summary>
    public AreaItemScriptHost? ItemScripts(GameSessionOwner session) => _scripts is null ? null : new(session, _maps.Values, _scripts);

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
    /// ARC-DEV-227 (the dialog's PA condition, ledger PN2): an NPC's stored name number (object field 22) and whether its own spell flags carry the mind-controlled bit (field 20, bit 27), read from
    /// the record the resolver finds by identity (a travelling companion's home map is read on demand, as <see cref="AreaCombat.Record"/> does); null when no record is found. The flags are the
    /// stored ones: a mind control cast in play is not modelled.
    /// </summary>
    public (int Name, bool MindControlled)? FollowerNameFacts(GameSessionOwner session, string entity)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entity);
        var record = _resolver.Resolve(entity).Record;
        if (record is null && EntityHomes.HomeOf(session, entity) is { } home) record = _resolver.Resolve(entity, home).Record;
        return record is null ? null : (AreaUseScriptHost.NameOf(record.Row), AreaUseScriptHost.IsMindControlled(record.Row));
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
        if (!_maps.TryGetValue(runtimeMap, out var map) || !map.TryGetEntity(key, out var entity)
            || session.QueryMapInventory(new(runtimeMap)) is not { } current) return null;
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
        return session.LootItem(session.Version, holder, item, InsertCheck, ItemScripts(session));
    }

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
        foreach (var map in _maps.Values)
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
    /// row. Unknown entities and nameless objects get a bracketed type label, never original text.</summary>
    public string Name(string entity, ObjectDescriptionCatalog? catalog)
    {
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row)) return AreaHoverIndex.Text(row, catalog) ?? $"[{AreaHoverIndex.TypeName(row.Type)}: unnamed]";
        return "[unknown object]";
    }

    /// <summary>ARC-UI-051: the hero's effective speed as the character sheet reads it (poison and both crippled legs count); null
    /// without a hero or a living record, or when the stats do not read.</summary>
    internal static int? HeroSpeed(GameSessionOwner session)
    {
        if (session.QueryHero() is null || session.QueryHeroLife() is not { } life) return null;
        var state = life.Record.State;
        var query = session.QueryHeroStats(new(0, state.PoisonLevel, state.Conditions.HasFlag(LifeConditions.CrippledLegsBoth)));
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
            { Written = item.Kind.Type == SessionItemType.Written }))
            .Select(line => session.QueryAmmo(line.Entity) is { } stack ? line with { StackKind = HeldKind(line.Entity), Units = stack.Units } : line)
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
            { StackKind = Stack(item.Facts) ? item.Facts.Kind : null, Units = Stack(item.Facts) ? item.Facts.Quantity ?? 0 : 0 }).ToArray();
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
        if (!_maps.TryGetValue(runtimeMap, out var map) || !map.TryGetRow(holder, out var row)) return (null, false);
        var container = row.Type == CombatFieldReader.ContainerType;
        if (container) return (AreaLootArtLayout.HolderImage(row.Type, 0, -1, row.PrototypeNumber - 20), true);
        return CombatFieldReader.Read(row).Fields is { Critter: { } critter } fields
            ? (AreaLootArtLayout.HolderImage(fields.Object.Type, critter.Flags, critter.BaseStats.Count > 27 ? critter.BaseStats[27] : -1, 0), false)
            : (null, false);
    }

    /// <summary>
    /// ARC-UI-025: the use facts of an item the PC holds (listed or worn): a kit item's from <paramref name="kitUse"/> (the hero load's, by
    /// kit number), a taken item's from its object row's fields (<see cref="AreaItemUseFacts.From"/>). Null for a made item (its prototype's
    /// facts are not read here: NOT READY), an item the session no longer has or whose fields do not read.
    /// </summary>
    public AreaItemUseFacts? UseFacts(GameSessionOwner session, string entity, IReadOnlyList<AreaItemUseFacts?>? kitUse)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity))
            return KitNumber(entity) is { } number && kitUse is not null && number <= kitUse.Count ? kitUse[number - 1] : null;
        if (!session.QueryPcInventory().Any(item => item.Entity == entity)) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields) return AreaItemUseFacts.From(entity, fields);
        return null;
    }

    /// <summary>
    /// ARC-UI-015: the wear facts of an item the PC holds, for <see cref="GameSessionOwner.Equip"/>: a kit item's from
    /// <paramref name="kitWear"/> (the hero load's, by kit number, as the best-items start reads them), a taken item's from its object
    /// row's fields (<see cref="SessionEquipmentFacts.Wearable"/> with the saved kind). Null for an item worn nowhere, a made item (its
    /// prototype's facts are not read here: NOT READY), an item the session no longer has, or a session without a hero.
    /// </summary>
    public SessionWearableItem? Wearable(GameSessionOwner session, string entity, IReadOnlyList<SessionWearableItem?>? kitWear)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null || session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity))
            return KitNumber(entity) is { } number && kitWear is not null && number <= kitWear.Count ? kitWear[number - 1] : null;
        var taken = session.QueryPcInventory().FirstOrDefault(item => item.Entity == entity);
        if (taken.Entity is null) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields) return SessionEquipmentFacts.Wearable(taken.Kind, fields);
        return null;
    }

    /// <summary>
    /// ARC-UI-019: the facts of an item the PC holds for a move in its grid (<see cref="GameSessionOwner.MoveHeroItem"/>): a kit item's from
    /// <paramref name="kitMove"/> (the hero load's, by kit number), a taken item's from its object row's fields with the saved kind, a made
    /// item's kind with no no-drop flag and no scripts (its prototype's facts are not read here: NOT READY). Null for an item the session no
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
        var made = session.QueryMadeItems().FirstOrDefault(item => item.Entity == entity);
        if (made.Entity is not null) return new(made.Kind, false, []);
        var taken = session.QueryPcInventory().FirstOrDefault(item => item.Entity == entity);
        if (taken.Entity is null) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields) return GridMoveFacts(taken.Kind, fields);
        return null;
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
        if (!session.QueryPcInventory().Any(item => item.Entity == entity)) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields) return AreaHealingItems.From(entity, fields);
        return null;
    }

    /// <summary>
    /// ARC-UI-049: the written facts of an item the PC holds: a taken item's from its object row's fields (instance over prototype), a
    /// kit or made item's from its prototype in <paramref name="data"/>; null for anything else or an item the session no longer has.
    /// </summary>
    public SessionWrittenFacts? WrittenFacts(GameSessionOwner session, string entity, AreaReadingData? data)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsItemDestroyed(entity)) return null;
        var kit = session.QueryHeroKit().FirstOrDefault(item => item.Entity == entity);
        if (kit.Entity is not null) return data is not null && data.Prototypes.TryGetValue(kit.Kind.Prototype, out var fromKit) ? fromKit : null;
        var made = session.QueryMadeItems().FirstOrDefault(item => item.Entity == entity);
        if (made.Entity is not null) return data is not null && data.Prototypes.TryGetValue(made.Kind.Prototype, out var fromMade) ? fromMade : null;
        if (!session.QueryPcInventory().Any(item => item.Entity == entity)) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields) return SessionWrittenFacts.FromCombatFields(fields);
        return null;
    }

    /// <summary>ARC-SKILLS-006: the lockpick facts of an item the PC holds, found as <see cref="HealingItem"/> finds a healing item's
    /// (<see cref="AreaLockpicks.From"/>); null for anything else, an item the session no longer has, or a session without a hero.</summary>
    public SessionLockpick? Lockpick(GameSessionOwner session, string entity, IReadOnlyList<SessionLockpick?>? kitLockpicks)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null || session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity)) return kitLockpicks?.FirstOrDefault(item => item?.Entity == entity);
        if (!session.QueryPcInventory().Any(item => item.Entity == entity)) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields) return AreaLockpicks.From(entity, fields);
        return null;
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
        var onMap = !session.QueryPcInventory().Any(item => item.Entity == entity) && !session.QueryEquipment().Worn.Any(item => item.Entity == entity);
        if (onMap && !thrownOnMap) return null;
        if (onMap && !session.QueryPlacedItems().Any(item => item.Entity == entity)) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields)
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
        if (!session.QueryPcInventory().Any(item => item.Entity == entity)) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields) return SessionItemConditions.FromCombatFields(fields);
        return null;
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
        return null;
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
