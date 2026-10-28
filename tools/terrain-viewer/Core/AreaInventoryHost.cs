using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Inventory;
using Arcanum.Data.Messages;
using Arcanum.Data.Scripts;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

public enum AreaInventoryMapStatus { Ready, InventoryRefused, PcRecordPresent }

/// <summary>ARC-WORLD-090: the substitute container of an NPC without a leader at T0 (NPC field 0x126, a container holder of the same
/// area): its entity, its inventory source (container field 0x3D, whose buy line the dealer test retries, BT8) and its cell.</summary>
public sealed record AreaSubstituteContainer(string Entity, int InventorySource, CellCoord Cell);

/// <summary>ARC-WORLD-096: a holder's restock fields at T0: its inventory source (NPC field 0xEF, container field 0x3D; 0 is none) and a
/// container's flags (field 0x38; 0 for an NPC).</summary>
public readonly record struct AreaRestockSeed(int InventorySource, uint ContainerFlags);

/// <summary>
/// The session inventory seed of one loaded area map (ARC-WORLD-014) with the object rows the AREA viewer needs to
/// find, place and name its items and holders (ARC-UI-004). <see cref="Build"/> uses the conversation host's entity ids
/// (<see cref="AreaConversationState.EntityId"/>).
/// </summary>
public sealed class AreaInventoryMap
{
    private readonly Dictionary<string, AreaObjectRecord> _rows;
    private readonly Dictionary<AreaObjectKey, string> _entities;

    private readonly Dictionary<string, AreaSubstituteContainer> _substitutes;
    private readonly Dictionary<string, AreaRestockSeed> _restock;
    private readonly HashSet<string> _persistent;

    /// <param name="rows">The object row of every seed entity (exactly those); one row per entity.</param>
    /// <param name="substitutes">ARC-WORLD-090: the substitute container of each NPC entity that has one, a container of the seed.</param>
    /// <param name="restock">ARC-WORLD-096: the restock fields of seed holders (a holder left out has no source and no flags).</param>
    /// <param name="persistent">ARC-WORLD-096: the seed items whose item flags carry the persistent flag 0x2000.</param>
    public AreaInventoryMap(int runtimeMap, SessionMapInventorySeed seed, IReadOnlyDictionary<string, AreaObjectRecord> rows, int deadNpcs = 0,
        int npcsWithoutEvidence = 0, IReadOnlyDictionary<string, AreaSubstituteContainer>? substitutes = null,
        IReadOnlyDictionary<string, AreaRestockSeed>? restock = null, IEnumerable<string>? persistent = null)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(rows);
        _substitutes = new(substitutes ?? new Dictionary<string, AreaSubstituteContainer>(), StringComparer.Ordinal);
        _restock = new(restock ?? new Dictionary<string, AreaRestockSeed>(), StringComparer.Ordinal);
        _persistent = new(persistent ?? [], StringComparer.Ordinal);
        if (!_restock.Keys.ToHashSet(StringComparer.Ordinal).IsSubsetOf(seed.Holders.Select(holder => holder.Entity)))
            throw new ArgumentException("AREA_INVENTORY_RESTOCK_NOT_IN_SEED", nameof(restock));
        if (!_persistent.IsSubsetOf(seed.Items.Select(item => item.Entity)))
            throw new ArgumentException("AREA_INVENTORY_PERSISTENT_NOT_IN_SEED", nameof(persistent));
        if (_substitutes.Any(pair => !seed.Holders.Any(holder => holder.Entity == pair.Key && holder.Kind == SessionHolderKind.Npc) ||
                !seed.Holders.Any(holder => holder.Entity == pair.Value.Entity && holder.Kind == SessionHolderKind.Container)))
            throw new ArgumentException("AREA_INVENTORY_SUBSTITUTE_NOT_IN_SEED", nameof(substitutes));
        if (runtimeMap < 1) throw new ArgumentOutOfRangeException(nameof(runtimeMap));
        RuntimeMap = runtimeMap; Seed = seed; DeadNpcs = deadNpcs; NpcsWithoutEvidence = npcsWithoutEvidence;
        var wanted = seed.Items.Select(item => item.Entity).Concat(seed.Holders.Select(holder => holder.Entity)).ToHashSet(StringComparer.Ordinal);
        if (rows.Count != wanted.Count || !wanted.All(rows.ContainsKey)) throw new ArgumentException("AREA_INVENTORY_ROWS_DIFFER", nameof(rows));
        _rows = new(rows, StringComparer.Ordinal); _entities = [];
        foreach (var (entity, row) in _rows)
            if (row is null || !_entities.TryAdd(row.Key, entity)) throw new ArgumentException("AREA_INVENTORY_DUPLICATE_ROW", nameof(rows));
    }

    public int RuntimeMap { get; }
    public SessionMapInventorySeed Seed { get; }
    /// <summary>NPC holders the step rules show dead (<c>CRITTER_DEAD</c>), with or without items.</summary>
    public int DeadNpcs { get; }
    /// <summary>NPC holders on an admitted cell whose step evidence names no life fact (counted as alive).</summary>
    public int NpcsWithoutEvidence { get; }
    public AreaObjectRecord Row(string entity) => _rows[entity];
    /// <summary>ARC-WORLD-090: the NPC's substitute container, or null.</summary>
    public AreaSubstituteContainer? Substitute(string npc) => _substitutes.GetValueOrDefault(npc);
    /// <summary>ARC-WORLD-090: the NPCs with a substitute container.</summary>
    public int Substitutes => _substitutes.Count;
    /// <summary>ARC-WORLD-096: the restock fields of a seed holder (none: no source, no flags).</summary>
    public AreaRestockSeed RestockSeed(string holder) => _restock.GetValueOrDefault(holder);
    /// <summary>ARC-WORLD-096: whether a seed item carries the persistent item flag 0x2000 (field 0x57).</summary>
    public bool Persistent(string item) => _persistent.Contains(item);

    public bool TryGetRow(string entity, out AreaObjectRecord row) => _rows.TryGetValue(entity, out row!);
    public bool TryGetEntity(AreaObjectKey key, out string entity) => _entities.TryGetValue(key, out entity!);

    /// <summary>
    /// Builds the seed from the strict Area snapshot: every holder and item of the ARC-DATA-012 view, life from the step
    /// rules (an NPC is dead only when a step into its cell meets its <c>CRITTER_DEAD</c> evidence; no evidence counts as
    /// alive, so it is never looted). A refused view, a PC record (none exists at T0), or data the mapping cannot represent
    /// (duplicate object ids or step evidence, a coordinate beyond the session's cell range) gives no map, never an exception.
    /// </summary>
    public static AreaInventoryMapBuild Build(AreaSnapshot snapshot, AreaStepSnapshot steps, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(steps);
        try { return BuildChecked(snapshot, steps, token); }
        catch (Exception exception) when (exception is InvalidOperationException or OverflowException or ArgumentException or KeyNotFoundException)
        {
            return new(AreaInventoryMapStatus.InventoryRefused, null, "mapping refused: " + exception.GetType().Name);
        }
    }

    private static AreaInventoryMapBuild BuildChecked(AreaSnapshot snapshot, AreaStepSnapshot steps, CancellationToken token)
    {
        var read = AreaInventoryReader.Read(snapshot, token);
        if (read.Failure != AreaInventoryFailure.None || read.Inventory is not { } inventory)
            return new(AreaInventoryMapStatus.InventoryRefused, null, $"{read.Failure} at {read.SourceIndex?.ToString() ?? "-"}");
        if (inventory.Holders.Any(holder => holder.Type == AreaInventoryHolderType.Pc))
            return new(AreaInventoryMapStatus.PcRecordPresent, null, "a PC record lies in the map");
        var life = inventory.Holders.Where(holder => holder.Type == AreaInventoryHolderType.Npc).ToDictionary(holder => holder.SourceIndex, holder => Life(steps, holder));
        var dead = life.Where(pair => pair.Value == true).Select(pair => pair.Key).ToHashSet();
        var unknown = life.Count(pair => pair.Value is null);
        static string Id(AreaObjectKey key) => "oid:" + key.ObjectIdSha256;
        var seed = new SessionMapInventorySeed(
            inventory.Holders.Select(holder => new SessionSeedHolder(Id(holder.Key), (SessionHolderKind)holder.Type, dead.Contains(holder.SourceIndex),
                holder.SubstituteInventory is not null, holder.Scripts.Select(script => script.Sap).ToArray(),
                inventory.ListOf(holder.SourceIndex).Select(item => Id(item.Key)).ToArray())).ToArray(),
            inventory.Items.Select(item => new SessionSeedItem(Id(item.Key),
                new(new((SessionItemType)item.Type, item.Prototype, item.Name, item.Description), item.Weight, item.Complexity,
                    item.InventoryLocation, item.Quantity, item.Scripts.Select(script => script.Sap).ToArray())
                    { AmmoType = item.AmmoType, NoDecay = (item.ItemFlags & Arcanum.Simulation.Items.ItemDecayRules.NoDecayFlag) != 0, MagicWeight = item.MagicWeight },
                item.InInventory ? null : new CellCoord(checked((int)item.WorldX!.Value), checked((int)item.WorldY!.Value)))).ToArray());
        var objects = snapshot.Objects.ToDictionary(row => row.Key);
        var rows = inventory.Holders.Select(holder => holder.Key).Concat(inventory.Items.Select(item => item.Key)).ToDictionary(Id, key => objects[key], StringComparer.Ordinal);
        // ARC-WORLD-090: an NPC's substitute container (0x45F650 asks for no leader first) that is a container of this area.
        var substitutes = new Dictionary<string, AreaSubstituteContainer>(StringComparer.Ordinal);
        foreach (var npc in inventory.Holders.Where(holder => holder.Type == AreaInventoryHolderType.Npc && holder.Leader is null))
            if (npc.SubstituteInventory?.SourceIndex is { } index && inventory.TryGetHolder(index, out var box) && box.Type == AreaInventoryHolderType.Container &&
                box.WorldX is { } x && box.WorldY is { } y)
                substitutes[Id(npc.Key)] = new(Id(box.Key), box.InventorySource, new(checked((int)x), checked((int)y)));
        // ARC-WORLD-096: the restock fields and the persistent items (RK1, RK5).
        var restock = inventory.Holders.Where(holder => holder.InventorySource != 0 || holder.ContainerFlags is > 0)
            .ToDictionary(holder => Id(holder.Key), holder => new AreaRestockSeed(holder.InventorySource, holder.ContainerFlags ?? 0), StringComparer.Ordinal);
        var persistent = inventory.Items.Where(item => RestockRules.Keeps(item.ItemFlags)).Select(item => Id(item.Key));
        return new(AreaInventoryMapStatus.Ready, new(snapshot.RuntimeMap, seed, rows, dead.Count, unknown, substitutes, restock, persistent),
            $"{inventory.Items.Count} items, {inventory.Holders.Count} holders, {dead.Count} dead NPCs, {unknown} without life evidence");
    }

    /// <summary>Life from the step rules: the NPC's own evidence as the destination of a step into its cell. True for
    /// <c>CRITTER_DEAD</c>, false for other evidence or an NPC off the admitted cells (outside the core, never reached),
    /// null when an admitted cell has no step that names it.</summary>
    public static bool? Life(AreaStepSnapshot steps, AreaInventoryHolder holder)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(holder);
        if (holder.WorldX is not { } x || holder.WorldY is not { } y) return false;
        var cell = new AreaStepCell(x, y);
        if (!steps.IsAdmitted(cell)) return false;
        foreach (var direction in Enum.GetValues<CardinalTraversalDirection>())
        {
            var from = direction switch
            {
                CardinalTraversalDirection.XMinusYMinus => new AreaStepCell(x + 1, y + 1), CardinalTraversalDirection.XMinus => new(x + 1, y),
                CardinalTraversalDirection.XMinusYPlus => new(x + 1, y - 1), CardinalTraversalDirection.YPlus => new(x, y - 1),
                CardinalTraversalDirection.XPlusYPlus => new(x - 1, y - 1), CardinalTraversalDirection.XPlus => new(x - 1, y),
                CardinalTraversalDirection.XPlusYMinus => new(x - 1, y + 1), _ => new(x, y + 1),
            };
            if (!steps.IsAdmitted(from)) continue;
            var evidence = steps.Assess(steps.Request(from, cell, direction)).Objects
                .SingleOrDefault(o => o.Role == AreaStepObjectRole.Destination && o.Key == holder.Key);
            if (evidence is not null) return evidence.ReasonCode == "CRITTER_DEAD";
        }
        return null;
    }
}

public sealed record AreaInventoryMapBuild(AreaInventoryMapStatus Status, AreaInventoryMap? Map, string Detail);

public enum AreaInventoryTargetKind { GroundItem, DeadNpc, JunkPile }

/// <summary>A clickable inventory object of the current map: an item on its ground, a dead NPC to loot, or (ARC-WORLD-063) a junk
/// pile the session made, opened as a container.</summary>
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
    /// <summary>ARC-UI-025: a right press on the item would use it: a held item, not worn and without a wear location, with
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
public sealed record AreaDollSlot(EquipmentSlot Slot, string? Entity, string? Name);

/// <summary>What the inventory window shows: the PC's list and, when looting, the dead NPC's current list.</summary>
public sealed record AreaInventoryView(IReadOnlyList<AreaInventoryLine> Pc, string? Holder, string? HolderTitle, IReadOnlyList<AreaInventoryLine> Loot)
{
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

    /// <param name="scripts">ARC-SCRIPT-013: the game's script content for an item's insert-item script (borrowed); without
    /// it such an item is refused, as before.</param>
    public AreaInventoryHost(IEnumerable<AreaInventoryMap> maps, SessionInsertCheck? insertCheck = null, ScriptContent? scripts = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = [];
        foreach (var map in maps)
            if (map is null || !_maps.TryAdd(map.RuntimeMap, map)) throw new ArgumentException("AREA_INVENTORY_MAP_DUPLICATE", nameof(maps));
        InsertCheck = insertCheck ?? DiagnosticInsertCheck;
        _scripts = scripts;
    }

    /// <summary>The item script host over <paramref name="session"/>, or null without script content.</summary>
    public AreaItemScriptHost? ItemScripts(GameSessionOwner session) => _scripts is null ? null : new(session, _maps.Values, _scripts);

    public IReadOnlyCollection<AreaInventoryMap> Maps => _maps.Values;
    public SessionInsertCheck InsertCheck { get; }
    public bool Covers(int runtimeMap) => _maps.ContainsKey(runtimeMap);

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
        return GameSessionFailure.None;
    }

    /// <summary>The ground item or dead NPC a press on <paramref name="key"/> acts on; null for anything else (a taken
    /// item, a living NPC, a container, an object of a map without a seed). A ground item the session will refuse (gold,
    /// ammunition, a key, a pickup or insert script) is still a target: the click reports the refusal.</summary>
    public AreaInventoryTarget? Target(GameSessionOwner session, int runtimeMap, AreaObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map) || !map.TryGetEntity(key, out var entity)
            || session.QueryMapInventory(new(runtimeMap)) is not { } current) return null;
        if (current.Ground.Any(ground => ground.Item.Entity == entity)) return new(AreaInventoryTargetKind.GroundItem, entity, map.Row(entity));
        return current.Holders.FirstOrDefault(holder => holder.Entity == entity) is { Kind: SessionHolderKind.Npc, Dead: true }
            ? new(AreaInventoryTargetKind.DeadNpc, entity, map.Row(entity)) : null;
    }

    /// <summary>The drawn objects of this map the PC has taken: they are neither drawn nor picked any more.</summary>
    public IReadOnlySet<AreaObjectKey> Taken(GameSessionOwner session, int runtimeMap)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_maps.TryGetValue(runtimeMap, out var map)) return new HashSet<AreaObjectKey>();
        return session.QueryPcInventory().Where(item => item.Origin.RuntimeMap == runtimeMap && map.TryGetRow(item.Entity, out _))
            .Select(item => map.Row(item.Entity).Key).ToHashSet();
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
        new(session, AreaConversationState.PcParticipant, entityOf, InsertCheck, ItemScripts(session));

    /// <summary>The shown name of an item or holder of any loaded map (the hover tooltip's rule); a taken item keeps its
    /// row. Unknown entities and nameless objects get a bracketed type label, never original text.</summary>
    public string Name(string entity, ObjectDescriptionCatalog? catalog)
    {
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row)) return AreaHoverIndex.Text(row, catalog) ?? $"[{AreaHoverIndex.TypeName(row.Type)}: unnamed]";
        return "[unknown object]";
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
        if (holder is null) return new(Array.AsReadOnly(pc), null, null, Array.Empty<AreaInventoryLine>())
            { Gold = gold, Doll = doll, Carry = carry, Grid = grid, Unplaced = unplaced, Ammo = ammo };
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
        return new(Array.AsReadOnly(pc), holder, pile is not null ? JunkPileTitle : Name(holder, catalog), Array.AsReadOnly(loot))
            { Gold = gold, Doll = doll, Carry = carry, Grid = grid, Unplaced = unplaced, Ammo = ammo };

        // ARC-UI-029: a gold pile shows its coins as an ammunition stack shows its units.
        static bool Stack(SessionItemFacts facts) => facts.AmmoType is not null || facts.Kind.Type == SessionItemType.Gold && facts.Quantity is not null;

        IReadOnlyList<EquipmentSlot> Slots(string entity) => worn.Contains(entity) ? Array.Empty<EquipmentSlot>() : WearSlots(Wearable(session, entity, kitWear));
        bool Usable(string entity) => !worn.Contains(entity) && UseFacts(session, entity, kitUse) is { CanUseBox: true, Wearable: false };
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
    /// longer has, or a session without a hero.</summary>
    public SessionItemSpells? ItemSpells(GameSessionOwner session, string entity, IReadOnlyList<SessionItemSpells?>? kitItemSpells)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.QueryHero() is null || session.IsItemDestroyed(entity)) return null;
        if (session.QueryHeroKit().Any(item => item.Entity == entity)) return kitItemSpells?.FirstOrDefault(item => item?.Entity == entity);
        if (!session.QueryPcInventory().Any(item => item.Entity == entity) && !session.QueryEquipment().Worn.Any(item => item.Entity == entity)) return null;
        foreach (var map in _maps.Values)
            if (map.TryGetRow(entity, out var row) && CombatFieldReader.Read(row).Fields is { } fields)
                return SessionItemSpells.FromCombatFields(entity, SessionSpellCaster.Hero, fields);
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
        GameSessionFailure.ItemTypeUnsupported => "gold and keys are not modelled",
        GameSessionFailure.WornItemUnsupported => "worn items are not modelled",
        GameSessionFailure.ItemNotAvailable => "the item is no longer there",
        GameSessionFailure.HolderNotLootable => "only a dead NPC can be looted",
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
