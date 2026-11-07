using Arcanum.Data.Area;
using Arcanum.Data.Inventory;
using Arcanum.Data.Traversal;
using Arcanum.Session;
using Arcanum.Simulation.Items;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

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
/// ("oid:" and the object id's hash).
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
    /// <summary>ARC-DEV-085 (MIG6b): the entities of the area's items flagged as held that no holder of the area lists. They are in no seed list and
    /// have no row; the original finds them through nothing (RL3), so the session neither places nor lends them.</summary>
    public IReadOnlyList<string> Detached { get; init; } = [];
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
                    { AmmoType = item.AmmoType, NoDecay = (item.ItemFlags & Arcanum.Simulation.Items.ItemDecayRules.NoDecayFlag) != 0, MagicWeight = item.MagicWeight,
                      KeyId = item.Type == AreaItemType.Key ? item.KeyId : null },
                item.InInventory ? null : new CellCoord(checked((int)item.WorldX!.Value), checked((int)item.WorldY!.Value)))).ToArray())
        {
            // ARC-COMBAT-024: a window of the main map lists only its own objects, not the items the PC took elsewhere on the map.
            Window = snapshot.Window is not null,
        };
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
        var detached = inventory.Detached.Select(item => Id(item.Key)).ToArray();
        return new(AreaInventoryMapStatus.Ready, new(snapshot.RuntimeMap, seed, rows, dead.Count, unknown, substitutes, restock, persistent) { Detached = detached },
            $"{inventory.Items.Count} items, {inventory.Holders.Count} holders, {dead.Count} dead NPCs, {unknown} without life evidence" +
            (detached.Length > 0 ? $", {detached.Length} held but unlisted (detached)" : ""));
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
