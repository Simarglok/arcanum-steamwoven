using System.Security.Cryptography;
using Arcanum.Data.Area;
using Arcanum.Data.Maps;
using Arcanum.Session;
using Arcanum.Simulation.Movement;

namespace Arcanum.World;

/// <summary>
/// ARC-DEV-118 (MIG9 S5b, K1 §6 risk 1): what the whole-map mobile read keeps of one object of a map's mobile aggregate: its entity (the viewer's
/// <c>oid:</c> identity), the original's type, its effective OF_* flags (the instance's value, else its prototype's), its serialized location
/// and the ids its holder list names (containers and NPCs), in list order.
/// </summary>
public sealed record MobileObjectFacts(string Entity, int Type, uint Flags, long? Location, IReadOnlyList<string> Held);

/// <summary>What <see cref="WholeMapMobileRows.Read"/> found: the facts of every mobile object, or why the map could not be read.</summary>
public sealed record WholeMapMobileRead(bool Ready, IReadOnlyList<MobileObjectFacts> Facts, string Detail);

/// <summary>
/// ARC-DEV-118 (MIG9 S5b, K1 §3.2, §6 risk 1): the whole mobile read of a map too large for a whole-map plan (the main world map, 7,399 mobile
/// rows in its game-generated aggregate). <see cref="MapResidentRows"/> reads a loaded map's rows through its snapshot and inventory seed, which
/// refuse a window and the main map's 5,380 items; this reader takes the aggregate itself (<see cref="WorldCatalog.ReadMobiles"/>) and builds the
/// same session rows from it: the type, the effective object flags, the location and the holder lists of the objects, with no inventory
/// seed. Items listed by a holder are held by it (the original finds a holder's items through its list, RL3); an item flagged OF_INVENTORY that
/// no holder lists is detached (no place); an item that is neither lies on its cell. Derived data, read once at a map open; no Godot.
/// </summary>
public static class WholeMapMobileRows
{
    private const int ObjectFlagsSlot = 18, LocationSlot = 1;
    private const int ContainerType = 2, PcType = 15, NpcType = 16, ContainerListSlot = 40, CritterListSlot = 56;
    private const uint DestroyedFlag = 0x1, OffFlag = 0x2, InventoryFlag = 0x1000, DynamicFlag = 0x2000;
    /// <summary>Longest holder list read (the area reader's own bound, <c>MaxListLength</c>).</summary>
    public const int MaxListLength = 512;

    private static bool IsItem(int type) => type is >= 5 and <= 14;

    /// <summary>The facts of every mobile object of <paramref name="map"/>'s aggregate, with <paramref name="patch"/> (<c>rules/map.patch</c>,
    /// <see cref="AreaMapPlans.ReadMapPatch"/>) applied as the original applies it when a map opens: it can set an object's OBJ_FLAGs (off, destroyed,
    /// dynamic, inventory). A refused record, an unreadable prototype, a malformed list or a map whose mobiles are not the game-generated aggregate
    /// answers not ready with the reason.</summary>
    public static WholeMapMobileRead Read(WorldCatalog catalog, WorldMap map, AreaMapPlans.MapPatchRead? patch = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(map);
        var read = patch is null ? catalog.ReadMobiles(map) : patch.ReadMobiles(catalog, map);
        if (read.Source != WorldMobileSource.Aggregate) return new(false, [], $"the mobiles are {read.Source}, not the game-generated aggregate");
        if (read.Issues.Count > 0) return new(false, [], $"aggregate issue {read.Issues[0].Code}");
        var flagsOfPrototype = new Dictionary<int, uint?>();
        Span<byte> raw = stackalloc byte[24];
        var facts = new List<MobileObjectFacts>(read.Objects.Count);
        foreach (var item in read.Objects)
        {
            uint flags;
            if (item.Fields.TryGetValue(ObjectFlagsSlot, out var own) && own is { Presence: WorldFieldPresence.Value, Number: { } instanceFlags }) flags = unchecked((uint)instanceFlags);
            else if (item.PrototypeNumber is { } number)
            {
                if (!flagsOfPrototype.TryGetValue(number, out var known))
                {
                    var prototype = catalog.ReadPrototype(number).Prototype;
                    known = prototype is not null && prototype.Fields.TryGetValue(ObjectFlagsSlot, out var field) && field is { Presence: WorldFieldPresence.Value, Number: { } value }
                        ? unchecked((uint)value) : null;
                    flagsOfPrototype[number] = known;
                }
                if (known is not { } fromPrototype) return new(false, [], $"prototype {number} gives no object flags");
                flags = fromPrototype;
            }
            else return new(false, [], "an object with neither flags nor a prototype");
            long? location = item.Fields.TryGetValue(LocationSlot, out var place) && place is { Presence: WorldFieldPresence.Value, Number: { } packed } ? packed : null;
            IReadOnlyList<string> held = [];
            if (item.Type is ContainerType or NpcType or PcType)
            {
                var slot = item.Type == ContainerType ? ContainerListSlot : CritterListSlot;
                if (item.Fields.TryGetValue(slot, out var list) && list.Presence == WorldFieldPresence.Array)
                {
                    if (list.Elements.Count > MaxListLength) return new(false, [], $"a holder list of {list.Elements.Count} elements");
                    var ids = new string[list.Elements.Count];
                    for (var index = 0; index < ids.Length; index++)
                    {
                        var element = list.Elements[index];
                        if (element.Key != index || element.Words.Count != 6 || element.Words.Any(word => word is < 0 or > uint.MaxValue)) return new(false, [], "a malformed holder list");
                        for (var w = 0; w < 6; w++) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(raw[(w * 4)..], (uint)element.Words[w]);
                        ids[index] = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
                    }
                    held = ids.Select(id => "oid:" + id).ToArray();
                }
            }
            facts.Add(new("oid:" + item.OwnIdSha256, item.Type, flags, location, held));
        }
        return new(true, facts.AsReadOnly(), $"{facts.Count} mobile objects");
    }

    /// <summary>The session's residency rows of the facts: statics (a sector type without OF_DYNAMIC) are left out and counted, a PC record, an
    /// unknown type, a duplicate entity, a list naming an object the map does not hold or a non-item, an item listed twice, a holder that is no
    /// container or NPC, or an object on the ground with no location the session holds refuses the build (typed, no exception).</summary>
    public static MapResidentRowsBuild Build(IReadOnlyList<MobileObjectFacts> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var byEntity = new Dictionary<string, MobileObjectFacts>(facts.Count, StringComparer.Ordinal);
        foreach (var fact in facts)
        {
            if (fact.Type is < 0 or > 17) return Refused(fact.Entity, $"type {fact.Type}");
            if (fact.Type == PcType) return Refused(fact.Entity, "a PC record");
            if (!byEntity.TryAdd(fact.Entity, fact)) return Refused(fact.Entity, "a duplicate object id");
        }
        var holderOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var holder in facts.Where(fact => fact.Held.Count > 0))
            foreach (var listed in holder.Held)
            {
                if (!byEntity.TryGetValue(listed, out var item)) return Refused(holder.Entity, $"lists {listed}, which the map does not hold");
                if (!IsItem(item.Type)) return Refused(holder.Entity, $"lists {listed}, which is no item");
                if (!holderOf.TryAdd(listed, holder.Entity)) return Refused(listed, "listed by two holders");
            }
        var rows = new List<SessionResidentRow>(facts.Count);
        var statics = 0;
        foreach (var fact in facts)
        {
            var kind = (SessionResidentKind)fact.Type;
            var dynamic = (fact.Flags & DynamicFlag) != 0;
            if (kind is SessionResidentKind.Wall or SessionResidentKind.Portal or SessionResidentKind.Scenery or SessionResidentKind.Trap && !dynamic) { statics++; continue; }
            CellCoord? cell = null;
            string? holder = null;
            if (holderOf.TryGetValue(fact.Entity, out var listedBy)) holder = listedBy;
            else if ((fact.Flags & InventoryFlag) != 0) { /* held by nothing the map lists: detached, no place (RL3) */ }
            else if (fact.Location is { } packed)
            {
                var place = WorldLocation.FromPacked(packed);
                if (place.X is >= 0 and < SessionNpcPlacement.Axis && place.Y is >= 0 and < SessionNpcPlacement.Axis) cell = new((int)place.X, (int)place.Y);
                else return Refused(fact.Entity, "on the ground with no location the session holds");
            }
            else return Refused(fact.Entity, "on the ground with no location the session holds");
            if (holder is not null && byEntity[holder].Type is not (ContainerType or NpcType)) return Refused(fact.Entity, "held by an object that is no container or NPC");
            rows.Add(new(fact.Entity, kind, cell, holder) { Dynamic = dynamic, Off = (fact.Flags & OffFlag) != 0, Destroyed = (fact.Flags & DestroyedFlag) != 0 });
        }
        return new(MapResidentRowsStatus.Ready, rows.AsReadOnly(), $"{rows.Count} mobile rows" + (statics > 0 ? $", {statics} static rows left out" : ""), statics);

        static MapResidentRowsBuild Refused(string entity, string why) => new(MapResidentRowsStatus.Refused, null, $"{entity}: {why}");
    }

    /// <summary>The rows of <paramref name="map"/>'s aggregate (<see cref="Read"/>, then <see cref="Build"/>); a read that is not ready is
    /// <see cref="MapResidentRowsStatus.Refused"/> with its reason.</summary>
    public static MapResidentRowsBuild Rows(WorldCatalog catalog, WorldMap map, AreaMapPlans.MapPatchRead? patch = null)
    {
        var read = Read(catalog, map, patch);
        return read.Ready ? Build(read.Facts) : new(MapResidentRowsStatus.Refused, null, read.Detail);
    }
}
