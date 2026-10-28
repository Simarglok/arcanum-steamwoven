using System.Buffers.Binary;
using System.Security.Cryptography;
using Arcanum.Data.Area;
using Arcanum.Data.Inventory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Data.Tests.Inventory;

// ARC-DATA-012: the T0 inventory view over a strict-shaped synthetic snapshot. Invented object IDs, numbers
// and field values throughout; expected identities are hashed here, not by a production helper.
[TestClass]
public sealed class AreaInventoryTests
{
    private const string Source = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const int Npc = 16, Container = 2, Pc = 15, Armor = 7, Gold = 8, Food = 9, KeyRing = 12, Generic = 14;
    private const uint Inventory = 0x1000;

    // A raw 24-byte object ID of kind 2 whose payload carries n; the record key is its SHA-256.
    private static byte[] Raw(int n)
    {
        var raw = new byte[24]; raw[0] = 2; raw[4] = 0xEE; // padding bytes are part of the raw identity
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(8), n); BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(20), ~n);
        return raw;
    }
    private static string Id(int n) => Convert.ToHexString(SHA256.HashData(Raw(n))).ToLowerInvariant();
    private static IReadOnlyList<long> Words(int n) => Enumerable.Range(0, 6).Select(w => (long)BinaryPrimitives.ReadUInt32LittleEndian(Raw(n).AsSpan(w * 4))).ToArray();

    private static AreaFieldValue Value(int slot, long n) => new(slot, AreaPresence.Value, AreaValueOrigin.Instance, n, [], null);
    private static AreaFieldValue Null(int slot) => new(slot, AreaPresence.Null, AreaValueOrigin.Prototype, null, [], null);
    private static AreaFieldValue Handle(int slot, int? id) => id is { } n ? new(slot, AreaPresence.Value, AreaValueOrigin.Instance, null, [], Id(n)) : Null(slot);
    private static AreaFieldValue List(int slot, params int[] ids) => ids.Length == 0
        ? new(slot, AreaPresence.Empty, AreaValueOrigin.Instance, null, [], null)
        : new(slot, AreaPresence.Array, AreaValueOrigin.Instance, null, ids.Select((id, k) => new AreaSparseElement(k, Words(id))).ToArray(), null);
    private static AreaFieldValue Scripts(params (int Sap, int Script)[] scripts) =>
        new(31, AreaPresence.Array, AreaValueOrigin.Instance, null, scripts.Select(s => new AreaSparseElement(s.Sap, [0, 0, s.Script])).ToArray(), null);

    private static AreaObjectRecord Row(int index, int id, int type, IEnumerable<AreaFieldValue> fields, AreaScope scope = AreaScope.Core,
        long? x = 100, long? y = 200, AreaSourceKind kind = AreaSourceKind.Mobile) =>
        new(new(Source, 7, kind, kind == AreaSourceKind.Static ? 1 : null, index, index * 1000L, Id(id), 300), 119, type, 9000 + index, Source, Source, Source, Source,
            scope, x is null ? null : x + (y << 32), x, y, AreaDisposition.BodyEligibleConditionally, [], fields.OrderBy(f => f.Slot).ToArray(), true, true, true);

    private static List<AreaFieldValue> ItemFields(int type, uint flags, int? parent, int name, int location = 0, int complexity = 0, uint itemFlags = 0, int? quantity = null)
    {
        var fields = new List<AreaFieldValue>
        {
            Value(18, flags), Value(21, name), Value(22, name + 10), Value(36, itemFlags), Handle(37, parent), Value(38, 5), Value(39, 0), Value(40, 7),
            Value(43, location), Value(45, complexity), Value(47, name + 20), Null(31),
        };
        if (quantity is { } q) fields.Add(Value(60, q));
        return fields;
    }
    private static AreaObjectRecord Item(int index, int id, int type, int? parent, int name, int location = 0, int complexity = 0, uint itemFlags = 0,
        int? quantity = null, uint? flags = null, long? x = 100, AreaSourceKind kind = AreaSourceKind.Mobile, params AreaFieldValue[] extra) =>
        Row(index, id, type, ItemFields(type, flags ?? (parent is null ? 0u : Inventory), parent, name, location, complexity, itemFlags, quantity)
            .Where(f => !extra.Any(e => e.Slot == f.Slot)).Concat(extra), x: x, kind: kind);

    private static AreaObjectRecord Critter(int index, int id, int[] list, int? count = null, int? gold = null, int? leader = null, int? substitute = null,
        int type = Npc, int source = 0, params AreaFieldValue[] extra) =>
        Row(index, id, type, new[] { Value(18, 0), Value(22, 31000 + index), Null(31), Handle(50, gold), Value(55, count ?? list.Length), List(56, list), Value(57, source), Handle(70, leader), Handle(83, substitute) }
            .Where(f => (type == Npc || f.Slot is not (70 or 83)) && !extra.Any(e => e.Slot == f.Slot)).Concat(extra));

    private static AreaObjectRecord Chest(int index, int id, int[] list, int source = 69, AreaSourceKind kind = AreaSourceKind.Mobile) =>
        Row(index, id, Container, [Value(18, 0), Value(22, 4000), Null(31), Value(36, 0x209), Value(38, 0), Value(39, list.Length), List(40, list), Value(41, source)], kind: kind);

    private static AreaSnapshot Snapshot(IEnumerable<AreaObjectRecord> objects, bool strict = true)
    {
        var plan = new AreaSourcePlan(5051, 7, new AreaSectorCoordinate(1, 1), 1, "maps/test", [new AreaInputIdentity(new(AreaSourceRole.Module), 1, Source)], []);
        var snapshot = new AreaSnapshot(new object(), 1, plan, [], [], objects, [], new AreaReconciliation(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), []);
        if (strict) snapshot.MarkStrictlyPublished();
        return snapshot;
    }

    // NPC 10 (id 1) lists ring 11 (id 2, name 2804), gold 13 (id 4) and food 12 (id 3) in that order; its gold handle names
    // the gold and its substitute inventory names chest 20 (id 9). A ground food item 30 (id 5) and an unrelated record follow.
    private static List<AreaObjectRecord> World() =>
    [
        Critter(10, 1, [2, 4, 3], gold: 4, substitute: 9, source: 84, extra: Scripts((9, 1324), (0, 0))),
        Item(11, 2, Armor, 1, 2804, extra: Scripts((0, 1142))),
        Item(12, 3, Food, 1, 3600, location: 2, complexity: 5, itemFlags: 1),
        Item(13, 4, Gold, 1, 3200, quantity: 21),
        Chest(20, 9, []),
        Item(30, 5, Food, null, 3608, complexity: -3, x: 92930),
        Critter(40, 6, [], type: Pc),
        Row(50, 7, 3, [Value(18, 0)]),
    ];

    private static AreaInventory Read(IEnumerable<AreaObjectRecord> objects)
    {
        var result = AreaInventoryReader.Read(Snapshot(objects));
        Assert.AreEqual(AreaInventoryFailure.None, result.Failure, $"refused at {result.SourceIndex}");
        return result.Inventory!;
    }

    [TestMethod]
    public void View_keeps_list_order_parents_handles_and_item_fields()
    {
        var inventory = Read(World());
        Assert.AreEqual((5051, 7), (inventory.CatalogueMap, inventory.RuntimeMap));
        CollectionAssert.AreEqual(new[] { 10, 20, 40 }, inventory.Holders.Select(h => h.SourceIndex).ToArray());
        CollectionAssert.AreEqual(new[] { 11, 12, 13, 30 }, inventory.Items.Select(i => i.SourceIndex).ToArray());

        Assert.IsTrue(inventory.TryGetHolder(10, out var npc));
        Assert.AreEqual(AreaInventoryHolderType.Npc, npc.Type);
        CollectionAssert.AreEqual(new[] { 11, 13, 12 }, npc.Items.ToArray(), "list order, not record order");
        CollectionAssert.AreEqual(new[] { 11, 13, 12 }, inventory.ListOf(10).Select(i => i.SourceIndex).ToArray());
        Assert.AreEqual((84, 13, 31010), (npc.InventorySource, npc.GoldItem, npc.Description));
        Assert.IsNull(npc.Leader);
        Assert.AreEqual(new AreaInventoryHandle(Id(9), 20), npc.SubstituteInventory);
        CollectionAssert.AreEqual(new[] { new AreaInventoryScript(9, 1324) }, npc.Scripts.ToArray(), "script 0 attachments are omitted");
        Assert.IsNull(npc.ContainerFlags);

        Assert.IsTrue(inventory.TryGetItem(11, out var ring));
        Assert.AreEqual((AreaItemType.Armor, 2804, 2814, 2824, 10, 0), (ring.Type, ring.Name, ring.Description, ring.UnknownDescription, ring.Holder, ring.ListIndex));
        Assert.AreEqual((5, 7, 0, Inventory), (ring.Weight, ring.Worth, ring.InventoryLocation, ring.ObjectFlags));
        Assert.IsTrue(ring.InInventory && ring.Identified);
        Assert.AreEqual(Id(2), ring.Key.ObjectIdSha256);
        Assert.AreEqual(1142, ring.Scripts.Single().Script);
        Assert.IsNull(ring.Quantity); Assert.IsNull(ring.KeyId);

        Assert.IsTrue(inventory.TryGetItem(12, out var food));
        Assert.AreEqual((2, 2, true), (food.ListIndex, food.InventoryLocation, food.Identified), "identified by flag despite complexity 5");
        Assert.IsTrue(inventory.TryGetItem(13, out var gold));
        Assert.AreEqual((AreaItemType.Gold, 21, 1), (gold.Type, gold.Quantity, gold.ListIndex));

        Assert.IsTrue(inventory.TryGetItem(30, out var ground));
        Assert.IsFalse(ground.InInventory);
        Assert.AreEqual(((int?)null, (int?)null, 92930L, true), (ground.Holder, ground.ListIndex, ground.WorldX!.Value, ground.Identified));

        Assert.IsTrue(inventory.TryGetHolder(20, out var chest));
        Assert.AreEqual((AreaInventoryHolderType.Container, 0x209u, 0, 69, 0), (chest.Type, chest.ContainerFlags!.Value, chest.ContainerKeyId!.Value, chest.InventorySource, chest.Items.Count));
        Assert.IsNull(chest.GoldItem);
        Assert.IsTrue(inventory.TryGetHolder(40, out var pc));
        Assert.AreEqual(AreaInventoryHolderType.Pc, pc.Type);
        Assert.IsNull(pc.SubstituteInventory);
        Assert.AreEqual(0, inventory.ListOf(99).Count);
        Assert.IsFalse(inventory.TryGetItem(50, out _), "scenery is neither item nor holder");
    }

    [TestMethod]
    public void Unloaded_leader_and_substitute_handles_keep_their_identity_without_an_index()
    {
        var world = World();
        world[0] = Critter(10, 1, [2, 4, 3], gold: 4, leader: 77, substitute: 78);
        var npc = Read(world).Holders.First();
        Assert.AreEqual(new AreaInventoryHandle(Id(77), null), npc.Leader);
        Assert.AreEqual(new AreaInventoryHandle(Id(78), null), npc.SubstituteInventory);
    }

    private static void Refused(AreaInventoryFailure failure, int? at, IEnumerable<AreaObjectRecord> objects, bool strict = true)
    {
        var result = AreaInventoryReader.Read(Snapshot(objects, strict));
        Assert.AreEqual((failure, at), (result.Failure, result.SourceIndex));
        Assert.IsNull(result.Inventory);
    }
    private static List<AreaObjectRecord> With(int position, AreaObjectRecord row) { var world = World(); world[position] = row; return world; }

    [TestMethod]
    public void List_graph_violations_are_typed_refusals()
    {
        Refused(AreaInventoryFailure.SnapshotNotStrict, null, World(), strict: false);
        Refused(AreaInventoryFailure.CountMismatch, 10, With(0, Critter(10, 1, [2, 4, 3], count: 2, gold: 4)));
        Refused(AreaInventoryFailure.ListedObjectUnresolved, 10, With(0, Critter(10, 1, [2, 4, 3, 66], gold: 4)));
        Refused(AreaInventoryFailure.ListedObjectNotItem, 10, With(0, Critter(10, 1, [2, 4, 3, 7], gold: 4)));
        // A second holder listing the ring: refused at the item, whichever holder lists it second.
        Refused(AreaInventoryFailure.ItemListedTwice, 11, With(4, Chest(20, 9, [2])));
        Refused(AreaInventoryFailure.ParentMismatch, 11, With(1, Item(11, 2, Armor, 9, 2804)));
        Refused(AreaInventoryFailure.ParentMismatch, 11, With(1, Item(11, 2, Armor, null, 2804, flags: Inventory)));
        Refused(AreaInventoryFailure.InventoryFlagMismatch, 11, With(1, Item(11, 2, Armor, 1, 2804, flags: 0)));
        Refused(AreaInventoryFailure.UnlistedInventoryItem, 30, With(5, Item(30, 5, Food, null, 3608, flags: Inventory)));
        Refused(AreaInventoryFailure.GroundItemParented, 30, With(5, Item(30, 5, Food, 1, 3608, flags: 0)));
        Refused(AreaInventoryFailure.GroundItemUnlocated, 30, With(5, Item(30, 5, Food, null, 3608, x: null)));
        // A list element padding byte differs: a different raw ID, so it names no record. The control
        // with the same words unflipped lists the ring.
        List<AreaObjectRecord> Padded(bool flip)
        {
            var words = Words(2).ToArray(); if (flip) words[1] ^= 1;
            var world = World();
            world[0] = Critter(10, 1, [], extra: [Value(55, 1), new(56, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(0, words)], null)]);
            world.RemoveAt(3); world.RemoveAt(2); // food and gold; the ring (id 2) stays
            return world;
        }
        CollectionAssert.AreEqual(new[] { 11 }, Read(Padded(false)).Holders.First().Items.ToArray());
        Refused(AreaInventoryFailure.ListedObjectUnresolved, 10, Padded(true));
    }

    [TestMethod]
    public void Handles_shapes_and_unadmitted_objects_are_typed_refusals()
    {
        Refused(AreaInventoryFailure.GoldHandleMismatch, 10, With(0, Critter(10, 1, [2, 4, 3], gold: 2)));
        Refused(AreaInventoryFailure.GoldHandleMismatch, 10, With(0, Critter(10, 1, [2, 4, 3], gold: 9)));
        Refused(AreaInventoryFailure.DuplicateIdentity, 60, World().Append(Row(60, 7, 3, [Value(18, 0)])));
        Refused(AreaInventoryFailure.StaticInventoryObjectUnsupported, 61, World().Append(Item(61, 61, Generic, null, 5600, kind: AreaSourceKind.Static)));
        Refused(AreaInventoryFailure.StaticInventoryObjectUnsupported, 63, World().Append(Chest(63, 63, [], kind: AreaSourceKind.Static)));
        Refused(AreaInventoryFailure.DuplicateIdentity, 50, World().Append(Row(50, 64, 3, [Value(18, 0)])));
        Refused(AreaInventoryFailure.KeyRingContentsUnsupported, 62, World().Append(Item(62, 62, KeyRing, null, 4800, extra: List(60, 3))));
        Assert.AreEqual(AreaInventoryFailure.None, AreaInventoryReader.Read(Snapshot(World().Append(Item(62, 62, KeyRing, null, 4800, extra: List(60))))).Failure);

        // A missing mandatory number, a list that is a plain value, and a non-dense list key.
        Refused(AreaInventoryFailure.FieldUnresolved, 30, With(5, Row(30, 5, Food, ItemFields(Food, 0, null, 3608).Where(f => f.Slot != 21))));
        Refused(AreaInventoryFailure.FieldUnresolved, 10, With(0, Row(10, 1, Npc, [Value(18, 0), Value(22, 1), Handle(50, null), Value(55, 0), Value(56, 0), Value(57, 0), Handle(70, null), Handle(83, null)])));
        var sparse = new AreaFieldValue(56, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(1, Words(2))], null);
        Refused(AreaInventoryFailure.ListMalformed, 10, With(0, Row(10, 1, Npc, [Value(18, 0), Value(22, 1), Handle(50, null), Value(55, 1), sparse, Value(57, 0), Handle(70, null), Handle(83, null)])));
        var shortElement = new AreaFieldValue(56, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(0, [1, 2, 3])], null);
        Refused(AreaInventoryFailure.ListMalformed, 10, With(0, Row(10, 1, Npc, [Value(18, 0), Value(22, 1), Handle(50, null), Value(55, 1), shortElement, Value(57, 0), Handle(70, null), Handle(83, null)])));

        // A handle with a value but no identity, and script elements that are not three words.
        Refused(AreaInventoryFailure.FieldUnresolved, 30, With(5, Item(30, 5, Food, null, 3608, extra: new AreaFieldValue(37, AreaPresence.Value, AreaValueOrigin.Instance, 1, [], null))));
        Refused(AreaInventoryFailure.FieldUnresolved, 30, With(5, Item(30, 5, Food, null, 3608,
            extra: new AreaFieldValue(31, AreaPresence.Array, AreaValueOrigin.Instance, null, [new AreaSparseElement(0, [1, 2])], null))));
        Refused(AreaInventoryFailure.FieldUnresolved, 30, With(5, Row(30, 5, Food, ItemFields(Food, 0, null, 3608).Where(f => f.Slot != 31))));
        var many = Enumerable.Range(1000, AreaInventoryReader.MaxListLength + 1).ToArray();
        Refused(AreaInventoryFailure.LimitExceeded, 10, With(0, Critter(10, 1, many)));
        var crowd = Enumerable.Range(0, AreaInventoryReader.MaxItems + 1).Select(i => Item(100 + i, 100 + i, Food, null, 3600));
        Refused(AreaInventoryFailure.LimitExceeded, null, crowd);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AreaInventoryReader.Read(Snapshot(World()), cancelled.Token));
    }

    [TestMethod]
    public void A_snapshot_without_items_reads_as_an_empty_view()
    {
        var inventory = Read([Row(1, 1, 3, [Value(18, 0)])]);
        Assert.AreEqual((0, 0), (inventory.Items.Count, inventory.Holders.Count));
    }
}
