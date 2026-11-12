using System.Buffers.Binary;
using System.Collections.ObjectModel;
using Arcanum.Data.Area;
using Arcanum.Data.Objects;

namespace Arcanum.Data.Inventory;

/// <summary>Item object types (5..14) of the v119 schema.</summary>
public enum AreaItemType { Weapon = 5, Ammo = 6, Armor = 7, Gold = 8, Food = 9, Scroll = 10, Key = 11, KeyRing = 12, Written = 13, Generic = 14 }

/// <summary>Object types that hold an inventory list: containers, the PC and NPCs.</summary>
public enum AreaInventoryHolderType { Container = 2, Pc = 15, Npc = 16 }

/// <summary>Typed refusal of <see cref="AreaInventoryReader"/>. Every value other than None means no view was built.</summary>
public enum AreaInventoryFailure
{
    None,
    SnapshotNotStrict,
    LimitExceeded,
    /// <summary>A mandatory effective field is absent, null or not a plain number (or a list is neither array, empty nor null).</summary>
    FieldUnresolved,
    /// <summary>Two mobile records share one object ID or source index.</summary>
    DuplicateIdentity,
    /// <summary>An item or holder type outside the normal-game aggregate (a sector-file object); not admitted.</summary>
    StaticInventoryObjectUnsupported,
    /// <summary>A list whose keys are not dense 0..n-1 or whose element is not a six-word object ID.</summary>
    ListMalformed,
    /// <summary>The declared inventory count differs from the list length.</summary>
    CountMismatch,
    /// <summary>A list element names no mobile record of this snapshot.</summary>
    ListedObjectUnresolved,
    /// <summary>A list element names a mobile record that is not an item.</summary>
    ListedObjectNotItem,
    ItemListedTwice,
    /// <summary>A listed item's parent field does not name its listing holder.</summary>
    ParentMismatch,
    /// <summary>A listed item without the inventory object flag.</summary>
    InventoryFlagMismatch,
    /// <summary>Retired by ARC-DEV-085 (MIG6b): an item with the inventory object flag that no holder lists is no longer a refusal; it is a
    /// <see cref="AreaInventory.Detached"/> item. The value stays so the numbering of the others does not move.</summary>
    UnlistedInventoryItem,
    /// <summary>A ground (unlisted) item whose parent field names an object.</summary>
    GroundItemParented,
    /// <summary>A ground item without a serialized world location.</summary>
    GroundItemUnlocated,
    /// <summary>A critter gold handle that does not name a gold item in that critter's own list.</summary>
    GoldHandleMismatch,
    /// <summary>A key ring with a non-empty key list; key-ring contents are not modelled.</summary>
    KeyRingContentsUnsupported,
}

/// <summary>One script attachment: the SAP slot and the script number (attachments with script 0 are omitted).</summary>
public sealed record AreaInventoryScript(int Sap, int Script);

/// <summary>A serialized object handle: the SHA-256 of its raw 24-byte ID and, when that ID names a mobile
/// record of this snapshot, its aggregate source index.</summary>
public sealed record AreaInventoryHandle(string ObjectIdSha256, int? SourceIndex);

/// <summary>One item of the area at T0. <see cref="Holder"/> and <see cref="ListIndex"/> are set exactly for listed
/// (inventory) items; a ground item has neither and carries its world location. All numbers are effective
/// field values (instance, else authenticated prototype). <see cref="Name"/> is the numeric name class
/// (<c>OBJ_F_NAME</c>), not display text; <see cref="Quantity"/> exists for gold and ammunition, <see cref="KeyId"/> for keys, and
/// (ARC-WORLD-066) <see cref="AmmoType"/> for ammunition (field 0x8F; 0..3 in the original's tables, not checked here).</summary>
public sealed record AreaInventoryItem(AreaObjectKey Key, AreaItemType Type, int Prototype, AreaScope Scope, long? WorldX, long? WorldY,
    uint ObjectFlags, uint ItemFlags, int Name, int Description, int UnknownDescription, int Complexity, int Weight, int Worth,
    int InventoryLocation, int? Quantity, int? KeyId, IReadOnlyList<AreaInventoryScript> Scripts, int? Holder, int? ListIndex, int? AmmoType = null)
{
    /// <summary>ARC-WORLD-085: the magic weight adjustment (field 0x5A), which the holder's power scales into the item's weight.</summary>
    public int MagicWeight { get; init; }

    /// <summary>ARC-WORLD-1299: the object's spell flags (field 0x14, slot 19): bit 0x4000 marks a summoned item, which a dead body's inventory walk destroys (0x463860). A field that does not read as a number reads as no flag.</summary>
    public uint SpellFlags { get; init; }

    public int SourceIndex => Key.SourceIndex;
    public bool InInventory => Holder is not null;
    /// <summary>Identified when its complexity is not positive or the identified item flag is set.</summary>
    public bool Identified => Complexity <= 0 || (ItemFlags & AreaInventoryReader.IdentifiedItemFlag) != 0;
}

/// <summary>ARC-DEV-085 (MIG6b): an item that carries the inventory object flag (OF_INVENTORY) but that no holder of the loaded area lists. The
/// original never lists such an item in a sector and never finds it through a holder's list, so it is held by nothing and stands nowhere
/// (docs/world/area-inventory-view.md). <see cref="Parent"/> is its parent field (null when the field is null; its
/// <see cref="AreaInventoryHandle.SourceIndex"/> is set when that object is in the area, and then its list does not hold this item).</summary>
public sealed record AreaInventoryDetachedItem(AreaObjectKey Key, AreaItemType Type, int Prototype, AreaScope Scope, int InventoryLocation, AreaInventoryHandle? Parent)
{
    public int SourceIndex => Key.SourceIndex;
}

/// <summary>One inventory holder of the area at T0 with its list in serialized order (<see cref="Items"/> are item
/// source indices). Container fields exist only for containers; gold, leader and substitute-inventory handles
/// only for critters (leader and substitute for NPCs). Life state is not part of this view.</summary>
public sealed record AreaInventoryHolder(AreaObjectKey Key, AreaInventoryHolderType Type, int Prototype, AreaScope Scope, long? WorldX, long? WorldY,
    uint ObjectFlags, int Description, int InventorySource, IReadOnlyList<int> Items, IReadOnlyList<AreaInventoryScript> Scripts,
    uint? ContainerFlags, int? ContainerKeyId, int? GoldItem, AreaInventoryHandle? Leader, AreaInventoryHandle? SubstituteInventory)
{
    public int SourceIndex => Key.SourceIndex;
}

/// <summary>The reconciled read-only T0 inventory graph of one strict Area snapshot (ARC-DATA-012).</summary>
public sealed class AreaInventory
{
    private readonly IReadOnlyDictionary<int, AreaInventoryItem> _items;
    private readonly IReadOnlyDictionary<int, AreaInventoryHolder> _holders;

    internal AreaInventory(AreaSnapshot snapshot, IEnumerable<AreaInventoryHolder> holders, IEnumerable<AreaInventoryItem> items,
        IEnumerable<AreaInventoryDetachedItem>? detached = null)
    {
        CatalogueMap = snapshot.CatalogueMap; RuntimeMap = snapshot.RuntimeMap; Profile = snapshot.Profile;
        Holders = Array.AsReadOnly(holders.OrderBy(h => h.SourceIndex).ToArray());
        Items = Array.AsReadOnly(items.OrderBy(i => i.SourceIndex).ToArray());
        Detached = Array.AsReadOnly((detached ?? []).OrderBy(i => i.SourceIndex).ToArray());
        // Source indices are unique: the reader refuses duplicates before construction.
        _holders = new ReadOnlyDictionary<int, AreaInventoryHolder>(Holders.ToDictionary(h => h.SourceIndex));
        _items = new ReadOnlyDictionary<int, AreaInventoryItem>(Items.ToDictionary(i => i.SourceIndex));
    }

    public int CatalogueMap { get; }
    public int RuntimeMap { get; }
    public string Profile { get; }
    /// <summary>Every holder of the loaded area (core and context), by source index.</summary>
    public IReadOnlyList<AreaInventoryHolder> Holders { get; }
    /// <summary>Every item of the loaded area (core and context), by source index.</summary>
    public IReadOnlyList<AreaInventoryItem> Items { get; }
    /// <summary>ARC-DEV-085: the items flagged as held that no holder of the loaded area lists, by source index. They are not in <see cref="Items"/>
    /// (not on the ground, not in a list), so no consumer places or lends them.</summary>
    public IReadOnlyList<AreaInventoryDetachedItem> Detached { get; }

    public bool TryGetItem(int sourceIndex, out AreaInventoryItem item) => _items.TryGetValue(sourceIndex, out item!);
    public bool TryGetHolder(int sourceIndex, out AreaInventoryHolder holder) => _holders.TryGetValue(sourceIndex, out holder!);

    /// <summary>The holder's items in list order; empty for an unknown holder.</summary>
    public IReadOnlyList<AreaInventoryItem> ListOf(int holderSourceIndex) =>
        _holders.TryGetValue(holderSourceIndex, out var holder) ? Array.AsReadOnly(holder.Items.Select(i => _items[i]).ToArray()) : Array.Empty<AreaInventoryItem>();
}

public sealed record AreaInventoryReadResult(AreaInventoryFailure Failure, AreaInventory? Inventory, int? SourceIndex = null);

/// <summary>
/// ARC-DATA-012: the Godot-free T0 inventory view over a strict Area snapshot, step 1 of the
/// ARC-RES-023 proposal (docs/research/crash-site-item-inventory.md). It reads only the snapshot's
/// effective fields; no archive, no transfer, no script. Field mapping (locators in that
/// document): holder lists are the container/critter <c>*_INVENTORY_LIST_IDX</c> object-ID arrays with
/// their <c>*_INVENTORY_NUM</c> counts; items carry <c>OBJ_F_ITEM_PARENT</c>, <c>OBJ_F_ITEM_INV_LOCATION</c>
/// and the common inventory flag. Identity is the SHA-256 of the raw 24-byte object ID, the same digest
/// the snapshot keys each record by. Fail-closed reconciliation over every mobile record: each list
/// element resolves to exactly one item record, whose parent names its holder and which carries the
/// inventory flag; no flagged item is unlisted; no item is listed twice; counts equal list lengths;
/// ground items are unparented and located; a critter gold handle names a gold item in its own list.
/// NOT READY: life state, display text, chest generation, transfers and the dialog IN command.
/// </summary>
public static class AreaInventoryReader
{
    public const int MaxItems = 2048;
    public const int MaxHolders = 2048;
    public const int MaxListLength = 512;
    internal const uint InventoryObjectFlag = 0x1000, IdentifiedItemFlag = 0x1;

    // Effective-field slots: common block, then the type's first and second group.
    private const int ObjectFlagsSlot = 18, SpellFlagsSlot = 19, NameSlot = 21, DescriptionSlot = 22, ScriptsSlot = 31;
    private const int ContainerFlagsSlot = 36, ContainerKeySlot = 38, ContainerCountSlot = 39, ContainerListSlot = 40, ContainerSourceSlot = 41;
    private const int CritterGoldSlot = 50, CritterCountSlot = 55, CritterListSlot = 56, CritterSourceSlot = 57;
    private const int NpcLeaderSlot = 70, NpcSubstituteSlot = 83;
    private const int ItemFlagsSlot = 36, ItemParentSlot = 37, ItemWeightSlot = 38, ItemMagicWeightSlot = 39, ItemWorthSlot = 40, ItemLocationSlot = 43,
        ItemComplexitySlot = 45, ItemUnknownDescriptionSlot = 47, ItemSecondGroupSlot = 59;
    private const int ContainerType = 2, PcType = 15, NpcType = 16;

    private sealed class Refusal(AreaInventoryFailure failure, int? sourceIndex) : Exception
    {
        public AreaInventoryFailure Failure { get; } = failure;
        public int? SourceIndex { get; } = sourceIndex;
    }

    public static AreaInventoryReadResult Read(AreaSnapshot snapshot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.StrictlyPublished) return new(AreaInventoryFailure.SnapshotNotStrict, null);
        try { return new(AreaInventoryFailure.None, Build(snapshot, token)); }
        catch (Refusal refusal) { return new(refusal.Failure, null, refusal.SourceIndex); }
    }

    private static bool IsItem(int type) => type is >= 5 and <= 14;
    private static bool IsHolder(int type) => type is ContainerType or PcType or NpcType;

    private static AreaInventory Build(AreaSnapshot snapshot, CancellationToken token)
    {
        if (snapshot.Objects.FirstOrDefault(o => o.Key.Kind != AreaSourceKind.Mobile && (IsItem(o.Type) || IsHolder(o.Type))) is { } stray)
            throw new Refusal(AreaInventoryFailure.StaticInventoryObjectUnsupported, stray.Key.SourceIndex);
        var mobiles = snapshot.Objects.Where(o => o.Key.Kind == AreaSourceKind.Mobile).ToArray();
        var byId = new Dictionary<string, AreaObjectRecord>(StringComparer.Ordinal);
        var indices = new HashSet<int>();
        foreach (var o in mobiles)
            if (!byId.TryAdd(o.Key.ObjectIdSha256, o) || !indices.Add(o.Key.SourceIndex)) throw new Refusal(AreaInventoryFailure.DuplicateIdentity, o.Key.SourceIndex);
        var itemRows = mobiles.Where(o => IsItem(o.Type)).ToArray();
        var holderRows = mobiles.Where(o => IsHolder(o.Type)).ToArray();
        if (itemRows.Length > MaxItems) throw new Refusal(AreaInventoryFailure.LimitExceeded, null);
        if (holderRows.Length > MaxHolders) throw new Refusal(AreaInventoryFailure.LimitExceeded, null);

        // Lists first: every element resolves to exactly one item record, listed once.
        var lists = new Dictionary<AreaObjectRecord, AreaObjectRecord[]>(ReferenceEqualityComparer.Instance);
        var listedBy = new Dictionary<AreaObjectRecord, (AreaObjectRecord Holder, int Index)>(ReferenceEqualityComparer.Instance);
        foreach (var holder in holderRows)
        {
            token.ThrowIfCancellationRequested();
            var container = holder.Type == ContainerType;
            var ids = ObjectIds(holder, container ? ContainerListSlot : CritterListSlot);
            if (Number(holder, container ? ContainerCountSlot : CritterCountSlot) != ids.Length) throw new Refusal(AreaInventoryFailure.CountMismatch, holder.Key.SourceIndex);
            var list = new AreaObjectRecord[ids.Length];
            for (var index = 0; index < ids.Length; index++)
            {
                if (!byId.TryGetValue(ids[index], out var item)) throw new Refusal(AreaInventoryFailure.ListedObjectUnresolved, holder.Key.SourceIndex);
                if (!IsItem(item.Type)) throw new Refusal(AreaInventoryFailure.ListedObjectNotItem, holder.Key.SourceIndex);
                if (!listedBy.TryAdd(item, (holder, index))) throw new Refusal(AreaInventoryFailure.ItemListedTwice, item.Key.SourceIndex);
                list[index] = item;
            }
            lists.Add(holder, list);
        }

        var items = new List<AreaInventoryItem>(itemRows.Length);
        var detached = new List<AreaInventoryDetachedItem>();
        foreach (var o in itemRows)
        {
            token.ThrowIfCancellationRequested();
            var objectFlags = (uint)Number(o, ObjectFlagsSlot);
            var flagged = (objectFlags & InventoryObjectFlag) != 0;
            var parent = Handle(o, ItemParentSlot);
            int? holderIndex = null, listIndex = null;
            if (listedBy.TryGetValue(o, out var by))
            {
                if (parent != by.Holder.Key.ObjectIdSha256) throw new Refusal(AreaInventoryFailure.ParentMismatch, o.Key.SourceIndex);
                if (!flagged) throw new Refusal(AreaInventoryFailure.InventoryFlagMismatch, o.Key.SourceIndex);
                holderIndex = by.Holder.Key.SourceIndex; listIndex = by.Index;
            }
            else
            {
                if (flagged)
                {
                    // ARC-DEV-085 (MIG6b): held by nothing the area lists. The original never lists an OF_INVENTORY item in a sector (RL3) and finds
                    // a holder's items through its list, so the item is inert; it stays out of the view's items and the seed builds.
                    detached.Add(new(o.Key, (AreaItemType)o.Type, o.PrototypeNumber, o.Scope, Number(o, ItemLocationSlot),
                        parent is null ? null : new(parent, byId.TryGetValue(parent, out var parentRow) ? parentRow.Key.SourceIndex : null)));
                    continue;
                }
                if (parent is not null) throw new Refusal(AreaInventoryFailure.GroundItemParented, o.Key.SourceIndex);
                if (o.WorldX is null || o.WorldY is null) throw new Refusal(AreaInventoryFailure.GroundItemUnlocated, o.Key.SourceIndex);
            }
            var type = (AreaItemType)o.Type;
            if (type == AreaItemType.KeyRing)
            {
                var keys = Field(o, ItemSecondGroupSlot + 1) ?? throw new Refusal(AreaInventoryFailure.FieldUnresolved, o.Key.SourceIndex);
                if (keys.Presence is not (AreaPresence.Null or AreaPresence.Empty)) throw new Refusal(AreaInventoryFailure.KeyRingContentsUnsupported, o.Key.SourceIndex);
            }
            items.Add(new(o.Key, type, o.PrototypeNumber, o.Scope, o.WorldX, o.WorldY, objectFlags, (uint)Number(o, ItemFlagsSlot),
                Number(o, NameSlot), Number(o, DescriptionSlot), Number(o, ItemUnknownDescriptionSlot), Number(o, ItemComplexitySlot),
                Number(o, ItemWeightSlot), Number(o, ItemWorthSlot), Number(o, ItemLocationSlot),
                type is AreaItemType.Gold or AreaItemType.Ammo ? Number(o, ItemSecondGroupSlot + 1) : null,
                type == AreaItemType.Key ? Number(o, ItemSecondGroupSlot) : null, Scripts(o), holderIndex, listIndex,
                type == AreaItemType.Ammo ? Number(o, ItemSecondGroupSlot + 2) : null) { MagicWeight = Number(o, ItemMagicWeightSlot), SpellFlags = SpellFlagsOf(o) });
        }

        var holders = new List<AreaInventoryHolder>(holderRows.Length);
        foreach (var o in holderRows)
        {
            token.ThrowIfCancellationRequested();
            var container = o.Type == ContainerType;
            var list = lists[o];
            int? gold = null;
            if (!container && Handle(o, CritterGoldSlot) is { } goldId)
            {
                var index = Array.FindIndex(list, i => i.Key.ObjectIdSha256 == goldId);
                if (index < 0 || list[index].Type != (int)AreaItemType.Gold) throw new Refusal(AreaInventoryFailure.GoldHandleMismatch, o.Key.SourceIndex);
                gold = list[index].Key.SourceIndex;
            }
            AreaInventoryHandle? Resolve(int slot) => Handle(o, slot) is { } id ? new(id, byId.TryGetValue(id, out var row) ? row.Key.SourceIndex : null) : null;
            holders.Add(new(o.Key, (AreaInventoryHolderType)o.Type, o.PrototypeNumber, o.Scope, o.WorldX, o.WorldY, (uint)Number(o, ObjectFlagsSlot),
                Number(o, DescriptionSlot), Number(o, container ? ContainerSourceSlot : CritterSourceSlot),
                Array.AsReadOnly(list.Select(i => i.Key.SourceIndex).ToArray()), Scripts(o),
                container ? (uint)Number(o, ContainerFlagsSlot) : null, container ? Number(o, ContainerKeySlot) : null, gold,
                o.Type == NpcType ? Resolve(NpcLeaderSlot) : null, o.Type == NpcType ? Resolve(NpcSubstituteSlot) : null));
        }
        return new AreaInventory(snapshot, holders, items, detached);
    }

    private static AreaFieldValue? Field(AreaObjectRecord o, int slot) => o.EffectiveFields.FirstOrDefault(f => f.Slot == slot);

    // ARC-WORLD-1299: a spell flags field that is not a plain number reads as clear (the facts only decide a dead body's item walk; the item is never refused for it).
    private static uint SpellFlagsOf(AreaObjectRecord o) =>
        Field(o, SpellFlagsSlot) is { Presence: AreaPresence.Value, Number: { } n } && n is >= int.MinValue and <= uint.MaxValue ? unchecked((uint)n) : 0;

    private static int Number(AreaObjectRecord o, int slot) =>
        Field(o, slot) is { Presence: AreaPresence.Value, Number: { } n } && n is >= int.MinValue and <= int.MaxValue
            ? (int)n : throw new Refusal(AreaInventoryFailure.FieldUnresolved, o.Key.SourceIndex);

    // An object handle: null when serialized as null, else the digest of its raw ID.
    private static string? Handle(AreaObjectRecord o, int slot) => Field(o, slot) switch
    {
        { Presence: AreaPresence.Null } => null,
        { Presence: AreaPresence.Value, Fingerprint: { } id } => id,
        _ => throw new Refusal(AreaInventoryFailure.FieldUnresolved, o.Key.SourceIndex),
    };

    // An object-ID array in key order: dense keys 0..n-1, six 32-bit words per element.
    private static string[] ObjectIds(AreaObjectRecord o, int slot)
    {
        var field = Field(o, slot);
        if (field is { Presence: AreaPresence.Null or AreaPresence.Empty }) return [];
        if (field is not { Presence: AreaPresence.Array }) throw new Refusal(AreaInventoryFailure.FieldUnresolved, o.Key.SourceIndex);
        if (field.Elements.Count > MaxListLength) throw new Refusal(AreaInventoryFailure.LimitExceeded, o.Key.SourceIndex);
        var ids = new string[field.Elements.Count];
        Span<byte> raw = stackalloc byte[24];
        for (var i = 0; i < ids.Length; i++)
        {
            var element = field.Elements[i];
            if (element.Key != i || element.Words.Count != 6 || element.Words.Any(w => w is < 0 or > uint.MaxValue))
                throw new Refusal(AreaInventoryFailure.ListMalformed, o.Key.SourceIndex);
            for (var w = 0; w < 6; w++) BinaryPrimitives.WriteUInt32LittleEndian(raw[(w * 4)..], (uint)element.Words[w]);
            ids[i] = ObjectWireReader.Hash(raw);
        }
        return ids;
    }

    private static IReadOnlyList<AreaInventoryScript> Scripts(AreaObjectRecord o)
    {
        var field = Field(o, ScriptsSlot);
        if (field is null) throw new Refusal(AreaInventoryFailure.FieldUnresolved, o.Key.SourceIndex);
        if (field.Presence is AreaPresence.Null or AreaPresence.Empty) return Array.Empty<AreaInventoryScript>();
        if (field.Presence != AreaPresence.Array || field.Elements.Any(e => e.Words.Count != 3)) throw new Refusal(AreaInventoryFailure.FieldUnresolved, o.Key.SourceIndex);
        return Array.AsReadOnly(field.Elements.Where(e => e.Words[2] != 0).Select(e => new AreaInventoryScript(e.Key, unchecked((int)e.Words[2]))).ToArray());
    }
}
