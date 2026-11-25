using Arcanum.Data.Area;

namespace Arcanum.Data.Messages;

/// <summary>Which field the shown name comes from.</summary>
public enum ObjectNameSource
{
    /// <summary><c>OBJ_F_DESCRIPTION</c>.</summary>
    Description,
    /// <summary><c>OBJ_F_ITEM_DESCRIPTION_UNKNOWN</c>, for an item that is not identified.</summary>
    UnknownItemDescription,
    /// <summary><c>OBJ_F_CRITTER_DESCRIPTION_UNKNOWN</c>, for an NPC the observer does not recognize.</summary>
    UnknownCritterDescription,
    /// <summary><c>OBJ_F_KEY_KEY_ID</c> in the key file.</summary>
    KeyName,
    /// <summary><c>OBJ_F_PC_PLAYER_NAME</c>, a string field that is not a message.</summary>
    PlayerName,
}

/// <summary>Whether the observer recognizes an NPC: it is the observer itself, or the
/// observer has met it. Reaction state is runtime data, so the caller supplies it.</summary>
public enum CritterRecognition { Recognized, NotRecognized }

public enum ObjectNameStatus
{
    Resolved,
    /// <summary>The chosen number has no text; <see cref="ObjectNameResolution.Lookup"/> says why.
    /// The original shows an empty name.</summary>
    NoText,
    /// <summary>A field the chosen branch reads has no int32 value.</summary>
    FieldUnresolved,
    /// <summary>A player character's name is a string field outside the message tables.</summary>
    PlayerNameNotModelled,
    /// <summary>A type number outside wall (0) .. trap (17).</summary>
    UnsupportedType,
}

/// <param name="Field">The effective-field slot that was read, or that could not be read.</param>
public sealed record ObjectNameResolution(ObjectNameStatus Status, ObjectNameSource? Source, int? Field, int? Number, DescriptionLookup? Lookup)
{
    public MesMessage? Message => Status == ObjectNameStatus.Resolved ? Lookup!.Message : null;
}

/// <summary>Reads one effective int32 field by slot, or null when it has no value.</summary>
public delegate int? ObjectFieldReader(int slot);

/// <summary>The name the original shows for an object when it is examined or hovered
/// (FR-ITEM-008). <c>OBJ_F_NAME</c> is a numeric name class used by name comparisons such as
/// dialog <c>IN</c>, not display text, so it is never looked up here. Chain and evidence:
/// docs/formats/mes-reader.md.</summary>
public static class ObjectDisplayName
{
    // Effective-field slots (dense, without the begin/end markers): the common block, then
    // the type's groups. Item fields start at 36 and a key's own group at 59.
    public const int NameSlot = 21, DescriptionSlot = 22;
    public const int ItemFlagsSlot = 36, ItemComplexitySlot = 45, ItemUnknownDescriptionSlot = 47, KeyIdSlot = 59;
    public const int CritterUnknownDescriptionSlot = 58;
    public const uint IdentifiedItemFlag = 0x1;
    private const int KeyType = 11, FirstItemType = 5, LastItemType = 14, PcType = 15, NpcType = 16, LastType = 17;

    /// <summary>Resolves an Area object's shown name from its effective fields (absent instance
    /// fields inherit the prototype value there).</summary>
    public static ObjectNameResolution Resolve(AreaObjectRecord record, ObjectDescriptionCatalog catalog, CritterRecognition recognition) => Resolve(record, catalog, recognition, false);

    /// <summary>
    /// ARC-SCRIPT-2811: <see cref="Resolve(AreaObjectRecord, ObjectDescriptionCatalog, CritterRecognition)"/> for an item the session has marked identified (the barter window's marks, Divine Magick's flag line), whose own stored flags the row does not
    /// carry: <paramref name="markedIdentified"/> reads the item flags with the identified bit set (the same field the name branch tests).
    /// </summary>
    public static ObjectNameResolution Resolve(AreaObjectRecord record, ObjectDescriptionCatalog catalog, CritterRecognition recognition, bool markedIdentified)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Resolve(record.Type, slot => Int32(record, slot) is { } value && markedIdentified && slot == ItemFlagsSlot ? unchecked((int)((uint)value | IdentifiedItemFlag)) : Int32(record, slot), catalog, recognition);
    }

    /// <summary>Resolves the shown name for an object of <paramref name="type"/> whose fields
    /// <paramref name="read"/> supplies. Only the fields of the selected branch are read.</summary>
    [OriginalFunction(0x441B60, OriginalFunctionScope.Partial)]
    public static ObjectNameResolution Resolve(int type, ObjectFieldReader read, ObjectDescriptionCatalog catalog, CritterRecognition recognition)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(catalog);
        if (recognition is not (CritterRecognition.Recognized or CritterRecognition.NotRecognized)) throw new ArgumentOutOfRangeException(nameof(recognition));
        if (type is < 0 or > LastType) return new(ObjectNameStatus.UnsupportedType, null, null, null, null);
        if (type == KeyType) return Field(read, KeyIdSlot, ObjectNameSource.KeyName, catalog.FindKey);
        if (type is >= FirstItemType and <= LastItemType)
        {
            if (read(ItemComplexitySlot) is not { } complexity) return Unresolved(null, ItemComplexitySlot);
            if (complexity > 0)
            {
                if (read(ItemFlagsSlot) is not { } flags) return Unresolved(null, ItemFlagsSlot);
                if ((unchecked((uint)flags) & IdentifiedItemFlag) == 0)
                    return Field(read, ItemUnknownDescriptionSlot, ObjectNameSource.UnknownItemDescription, catalog.Find);
            }
        }
        if (type == PcType) return new(ObjectNameStatus.PlayerNameNotModelled, ObjectNameSource.PlayerName, null, null, null);
        if (type == NpcType && recognition == CritterRecognition.NotRecognized)
            return Field(read, CritterUnknownDescriptionSlot, ObjectNameSource.UnknownCritterDescription, catalog.Find);
        return Field(read, DescriptionSlot, ObjectNameSource.Description, catalog.Find);
    }

    private static ObjectNameResolution Field(ObjectFieldReader read, int slot, ObjectNameSource source, Func<int, DescriptionLookup> find)
    {
        if (read(slot) is not { } number) return Unresolved(source, slot);
        var lookup = find(number);
        return new(lookup.Status == DescriptionLookupStatus.Found ? ObjectNameStatus.Resolved : ObjectNameStatus.NoText, source, slot, number, lookup);
    }

    private static ObjectNameResolution Unresolved(ObjectNameSource? source, int slot) => new(ObjectNameStatus.FieldUnresolved, source, slot, null, null);

    private static int? Int32(AreaObjectRecord record, int slot)
    {
        AreaFieldValue? found = null;
        foreach (var field in record.EffectiveFields)
        {
            if (field.Slot != slot) continue;
            if (found is not null) return null;
            found = field;
        }
        return found is { Presence: AreaPresence.Value, Number: { } n } && n is >= int.MinValue and <= int.MaxValue ? (int)n : null;
    }
}
