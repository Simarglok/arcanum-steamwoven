using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Data.Inventory;
using Arcanum.Data.Messages;
using Arcanum.Data.Scripts;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.TerrainViewer.Core;

public enum AreaInventoryMapStatus { Ready, InventoryRefused, PcRecordPresent }

/// <summary>
/// The session inventory seed of one loaded area map (ARC-WORLD-014) with the object rows the AREA viewer needs to
/// find, place and name its items and holders (ARC-UI-004). <see cref="Build"/> uses the conversation host's entity ids
/// (<see cref="AreaConversationState.EntityId"/>).
/// </summary>
public sealed class AreaInventoryMap
{
    private readonly Dictionary<string, AreaObjectRecord> _rows;
    private readonly Dictionary<AreaObjectKey, string> _entities;

    /// <param name="rows">The object row of every seed entity (exactly those); one row per entity.</param>
    public AreaInventoryMap(int runtimeMap, SessionMapInventorySeed seed, IReadOnlyDictionary<string, AreaObjectRecord> rows, int deadNpcs = 0,
        int npcsWithoutEvidence = 0)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(rows);
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
                    { AmmoType = item.AmmoType, NoDecay = (item.ItemFlags & Arcanum.Simulation.Items.ItemDecayRules.NoDecayFlag) != 0 },
                item.InInventory ? null : new CellCoord(checked((int)item.WorldX!.Value), checked((int)item.WorldY!.Value)))).ToArray());
        var objects = snapshot.Objects.ToDictionary(row => row.Key);
        var rows = inventory.Holders.Select(holder => holder.Key).Concat(inventory.Items.Select(item => item.Key)).ToDictionary(Id, key => objects[key], StringComparer.Ordinal);
        return new(AreaInventoryMapStatus.Ready, new(snapshot.RuntimeMap, seed, rows, dead.Count, unknown),
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

public enum AreaInventoryTargetKind { GroundItem, DeadNpc }

/// <summary>A clickable inventory object of the current map: an item on its ground, or a dead NPC to loot.</summary>
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
}

/// <summary>What the inventory window shows: the PC's list and, when looting, the dead NPC's current list.</summary>
public sealed record AreaInventoryView(IReadOnlyList<AreaInventoryLine> Pc, string? Holder, string? HolderTitle, IReadOnlyList<AreaInventoryLine> Loot)
{
    /// <summary>ARC-WORLD-027: the PC's gold (the hero's coins; 0 without a hero).</summary>
    public int Gold { get; init; }
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
        IReadOnlyList<SessionWearItem?>? kitDurability = null)
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
              Repairable = Repairable(session, item.Entity, kitDurability) })
            .Concat(session.QueryPcInventory().Select(item => new AreaInventoryLine(item.Entity, Line(Name(item.Entity, catalog), item.Kind.Type, Weight(item.Entity)) + Units(item.Entity) + Worn(item.Entity))
            { Healing = HealingItem(session, item.Entity, kitHealing) is not null, Lockpick = Lockpick(session, item.Entity, kitLockpicks) is not null, Written = item.Kind.Type == SessionItemType.Written,
              Repairable = Repairable(session, item.Entity, kitDurability) }))
            // ARC-WORLD-055: the items the hero made or took from an NPC that held made items follow, named by their kind.
            .Concat(session.QueryMadeItems().Select(item => new AreaInventoryLine(item.Entity, Line(KindName(item.Kind, catalog), item.Kind.Type, null) + Units(item.Entity) + " (made)" + Worn(item.Entity))
            { Written = item.Kind.Type == SessionItemType.Written }))
            .ToArray();
        var gold = session.QueryGold();
        if (holder is null) return new(Array.AsReadOnly(pc), null, null, Array.Empty<AreaInventoryLine>()) { Gold = gold };
        var list = session.QueryMapInventory(new(runtimeMap))?.Holders.FirstOrDefault(h => h.Entity == holder)?.Items ?? [];
        // ARC-WORLD-055: a made item the session added to the NPC has no area row; its kind names it.
        static bool Made(string entity) => entity.StartsWith("made:", StringComparison.Ordinal);
        var loot = list.Select(item => new AreaInventoryLine(item.Entity, Line(Made(item.Entity)
                ? KindName(item.Facts.Kind, catalog) + " (made)" : Name(item.Entity, catalog), item.Facts.Kind.Type, Made(item.Entity) ? null : item.Facts.Weight)
            + (item.Facts.AmmoType is not null ? $" ×{item.Facts.Quantity}" : string.Empty) + (item.Facts.Worn ? " (worn)" : string.Empty))).ToArray();
        return new(Array.AsReadOnly(pc), holder, Name(holder, catalog), Array.AsReadOnly(loot)) { Gold = gold };
    }

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
        GameSessionFailure.ItemScriptUnsupported => "the item runs a script on this transfer that is not supported here",
        GameSessionFailure.HolderScriptUnsupported => "the body runs a use script (not supported here)",
        GameSessionFailure.ItemTypeUnsupported => "gold and keys are not modelled",
        GameSessionFailure.WornItemUnsupported => "worn items are not modelled",
        GameSessionFailure.ItemNotAvailable => "the item is no longer there",
        GameSessionFailure.HolderNotLootable => "only a dead NPC can be looted",
        GameSessionFailure.InsertRefused => "the PC cannot take it",
        GameSessionFailure.BudgetExceeded => "the PC's list is full",
        GameSessionFailure.InventoryUnavailable => "no inventory on this map",
        _ => failure.ToString(),
    };
}
