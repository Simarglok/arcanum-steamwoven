using Arcanum.Data.Area;
using Arcanum.Data.Inventory;
using Arcanum.Data.Messages;
using Arcanum.Data.Traversal;
using Arcanum.Scripting.Dialog;
using Arcanum.Session;
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
    public AreaInventoryMap(int runtimeMap, SessionMapInventorySeed seed, IReadOnlyDictionary<string, AreaObjectRecord> rows, int deadNpcs = 0)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(rows);
        if (runtimeMap < 1) throw new ArgumentOutOfRangeException(nameof(runtimeMap));
        RuntimeMap = runtimeMap; Seed = seed; DeadNpcs = deadNpcs;
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
    public AreaObjectRecord Row(string entity) => _rows[entity];
    public bool TryGetRow(string entity, out AreaObjectRecord row) => _rows.TryGetValue(entity, out row!);
    public bool TryGetEntity(AreaObjectKey key, out string entity) => _entities.TryGetValue(key, out entity!);

    /// <summary>
    /// Builds the seed from the strict Area snapshot: every holder and item of the ARC-DATA-012 view, life from the step
    /// rules (an NPC is dead only when a step into its cell meets its <c>CRITTER_DEAD</c> evidence; no evidence counts as
    /// alive, so it is never looted). A refused view or a PC record (none exists at T0) gives no map.
    /// </summary>
    public static AreaInventoryMapBuild Build(AreaSnapshot snapshot, AreaStepSnapshot steps, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(steps);
        var read = AreaInventoryReader.Read(snapshot, token);
        if (read.Failure != AreaInventoryFailure.None || read.Inventory is not { } inventory)
            return new(AreaInventoryMapStatus.InventoryRefused, null, $"{read.Failure} at {read.SourceIndex?.ToString() ?? "-"}");
        if (inventory.Holders.Any(holder => holder.Type == AreaInventoryHolderType.Pc))
            return new(AreaInventoryMapStatus.PcRecordPresent, null, "a PC record lies in the map");
        var dead = inventory.Holders.Where(holder => holder.Type == AreaInventoryHolderType.Npc && Dead(steps, holder))
            .Select(holder => holder.SourceIndex).ToHashSet();
        static string Id(AreaObjectKey key) => "oid:" + key.ObjectIdSha256;
        var seed = new SessionMapInventorySeed(
            inventory.Holders.Select(holder => new SessionSeedHolder(Id(holder.Key), (SessionHolderKind)holder.Type, dead.Contains(holder.SourceIndex),
                holder.SubstituteInventory is not null, holder.Scripts.Select(script => script.Sap).ToArray(),
                inventory.ListOf(holder.SourceIndex).Select(item => Id(item.Key)).ToArray())).ToArray(),
            inventory.Items.Select(item => new SessionSeedItem(Id(item.Key),
                new(new((SessionItemType)item.Type, item.Prototype, item.Name, item.Description), item.Weight, item.Complexity,
                    item.InventoryLocation, item.Quantity, item.Scripts.Select(script => script.Sap).ToArray()),
                item.InInventory ? null : new CellCoord(checked((int)item.WorldX!.Value), checked((int)item.WorldY!.Value)))).ToArray());
        var objects = snapshot.Objects.ToDictionary(row => row.Key);
        var rows = inventory.Holders.Select(holder => holder.Key).Concat(inventory.Items.Select(item => item.Key)).ToDictionary(Id, key => objects[key], StringComparer.Ordinal);
        return new(AreaInventoryMapStatus.Ready, new(snapshot.RuntimeMap, seed, rows, dead.Count),
            $"{inventory.Items.Count} items, {inventory.Holders.Count} holders, {dead.Count} dead NPCs");
    }

    /// <summary>Life from the step rules: the NPC's own evidence as the destination of a step into its cell.</summary>
    public static bool Dead(AreaStepSnapshot steps, AreaInventoryHolder holder)
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
        return false;
    }
}

public sealed record AreaInventoryMapBuild(AreaInventoryMapStatus Status, AreaInventoryMap? Map, string Detail);

public enum AreaInventoryTargetKind { GroundItem, DeadNpc }

/// <summary>A clickable inventory object of the current map: an item on its ground, or a dead NPC to loot.</summary>
public sealed record AreaInventoryTarget(AreaInventoryTargetKind Kind, string Entity, AreaObjectRecord Row);

public sealed record AreaInventoryLine(string Entity, string Text);

/// <summary>What the inventory window shows: the PC's list and, when looting, the dead NPC's current list.</summary>
public sealed record AreaInventoryView(IReadOnlyList<AreaInventoryLine> Pc, string? Holder, string? HolderTitle, IReadOnlyList<AreaInventoryLine> Loot);

/// <summary>
/// ARC-UI-004: the AREA viewer's side of the session inventory. It attaches the loaded maps' seeds to a session (again
/// after every restore), answers which drawn object a left click would pick up or loot, which drawn items the PC has
/// taken (no longer drawn or picked), runs the transfers with the viewer's insert check and builds the window's lines
/// with ARC-DATA-013 display names. It holds no session state: every answer reads the session it is given.
/// </summary>
public sealed class AreaInventoryHost
{
    /// <summary>Chebyshev reach of a pickup or loot click. Source (S): opening a container, PC or NPC moves within
    /// range 1 (CE <c>sub_428930</c> in <c>anim.c</c>); the pickup goal's own move-near range is not pinned, so pickup
    /// uses the same reach (project choice).</summary>
    public const int Reach = 1;

    /// <summary>PROJECT diagnostic insert check: every item fits. Carry weight, Technophobia and grid room need the
    /// created character, which the viewer does not have yet (NOT READY).</summary>
    public static SessionInsertCheck DiagnosticInsertCheck { get; } = _ => SessionInsertVerdict.Accepted;

    private readonly Dictionary<int, AreaInventoryMap> _maps;

    public AreaInventoryHost(IEnumerable<AreaInventoryMap> maps, SessionInsertCheck? insertCheck = null)
    {
        ArgumentNullException.ThrowIfNull(maps);
        _maps = [];
        foreach (var map in maps)
            if (map is null || !_maps.TryAdd(map.RuntimeMap, map)) throw new ArgumentException("AREA_INVENTORY_MAP_DUPLICATE", nameof(maps));
        InsertCheck = insertCheck ?? DiagnosticInsertCheck;
    }

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
    /// item, a living NPC, a container, an object of a map without a seed).</summary>
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
        return session.PickUpItem(session.Version, item, InsertCheck);
    }

    public InventoryMutationResult Loot(GameSessionOwner session, string holder, string item)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.LootItem(session.Version, holder, item, InsertCheck);
    }

    /// <summary>The dialog's negative IN through the session (<see cref="SessionDialogInventoryPort"/>) with this insert check.</summary>
    public SessionDialogInventoryPort DialogPort(GameSessionOwner session, Func<DialogParticipantId, string?> entityOf) =>
        new(session, AreaConversationState.PcParticipant, entityOf, InsertCheck);

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
    public AreaInventoryView View(GameSessionOwner session, ObjectDescriptionCatalog? catalog, int runtimeMap, string? holder = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        var pc = session.QueryPcInventory().Select(item => new AreaInventoryLine(item.Entity, Line(Name(item.Entity, catalog), item.Kind.Type, Weight(item.Entity)))).ToArray();
        if (holder is null) return new(Array.AsReadOnly(pc), null, null, Array.Empty<AreaInventoryLine>());
        var list = session.QueryMapInventory(new(runtimeMap))?.Holders.FirstOrDefault(h => h.Entity == holder)?.Items ?? [];
        var loot = list.Select(item => new AreaInventoryLine(item.Entity, Line(Name(item.Entity, catalog), item.Facts.Kind.Type, item.Facts.Weight)
            + (item.Facts.Worn ? " (worn)" : string.Empty))).ToArray();
        return new(Array.AsReadOnly(pc), holder, Name(holder, catalog), Array.AsReadOnly(loot));
    }

    private int? Weight(string entity)
    {
        foreach (var map in _maps.Values)
            if (map.Seed.Items.FirstOrDefault(item => item.Entity == entity) is { } found) return found.Facts.Weight;
        return null;
    }

    private static string Line(string name, SessionItemType type, int? weight) =>
        $"{name} ({AreaHoverIndex.TypeName((int)type)}{(weight is { } w ? $", weight {w}" : string.Empty)})";

    /// <summary>Plain text for a transfer refusal shown in the window and the status line.</summary>
    public static string Refusal(GameSessionFailure failure) => failure switch
    {
        GameSessionFailure.ItemScriptUnsupported => "the item runs a script on this transfer (not supported here)",
        GameSessionFailure.HolderScriptUnsupported => "the body runs a use script (not supported here)",
        GameSessionFailure.ItemTypeUnsupported => "gold, ammunition and keys are not modelled",
        GameSessionFailure.WornItemUnsupported => "worn items are not modelled",
        GameSessionFailure.ItemNotAvailable => "the item is no longer there",
        GameSessionFailure.HolderNotLootable => "only a dead NPC can be looted",
        GameSessionFailure.InsertRefused => "the PC cannot take it",
        GameSessionFailure.BudgetExceeded => "the PC's list is full",
        GameSessionFailure.InventoryUnavailable => "no inventory on this map",
        _ => failure.ToString(),
    };
}
