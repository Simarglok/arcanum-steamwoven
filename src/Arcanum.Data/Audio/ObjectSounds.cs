using Arcanum.Data.Area;
using Arcanum.Data.Characters;
using Arcanum.Data.Objects;

namespace Arcanum.Data.Audio;

/// <summary>
/// The stored fields an object's event sounds are chosen by (ARC-WORLD-041, docs/audio/sound-cues.md; checked in the 1.0.7.4
/// executable, docs/research/audio-evidence.md SN23–SN25): the type, the sound base (<c>OBJ_F_SOUND_EFFECT</c>), the material,
/// the current art (a critter's armour for footsteps) and the object flags. <see cref="Item"/> is set for items (types 5..14).
/// Raw values only; the sound rules are <c>Arcanum.Simulation.Sound.SoundCueRules</c>.
/// </summary>
public sealed record ObjectSoundFields(int Type, int Prototype, int SoundEffect, int Material, uint CurrentArt, uint Flags)
{
    public ObjectSoundItemFields? Item { get; init; }
}

/// <summary>An item's fields for its pickup, drop and hit sounds: the stored weight, the unknown-description number (gold is
/// told by it) and, for ammunition (type 6), the stack quantity.</summary>
public sealed record ObjectSoundItemFields(int Weight, int UnknownDescription, int? AmmoQuantity);

public enum ObjectSoundFailure
{
    None,
    PrototypeMissing,
    PrototypeAmbiguous,
    /// <summary>Too large, unreadable, or not a complete v119 prototype record of that number.</summary>
    PrototypeInvalid,
    /// <summary>Not an object type 0..17.</summary>
    TypeUnsupported,
    /// <summary>A field is absent or not a plain 32-bit number (<see cref="ObjectSoundRead.Detail"/> names its slot).</summary>
    FieldUnresolved,
}

/// <param name="Detail">The v119 slot of a field failure, the prototype number of a prototype failure, the type of a type failure.</param>
public sealed record ObjectSoundRead(ObjectSoundFailure Failure, ObjectSoundFields? Fields, int? Detail = null);

/// <summary>
/// ARC-WORLD-041: reads <see cref="ObjectSoundFields"/> from a prototype file by number (in play the lookup is
/// <see cref="Content.ContentPrototypes.Read"/>, base sources only) or from an Area object's effective fields (an instance's
/// own field over its prototype's), in the v119 slot layout of the combat field reader (docs/formats/combat-fields.md):
/// common slots 0 current art, 18 flags, 29 material, 32 sound; item slots 38 weight, 47 unknown description; ammunition
/// slot 60 quantity. Fail-closed: a field that is not a plain number refuses the object.
/// </summary>
public static class ObjectSoundReader
{
    public const int WeaponType = 5, AmmoType = 6, GenericType = 14, LastType = 17;
    private const int CurrentArtSlot = 0, FlagsSlot = 18, MaterialSlot = 29, SoundSlot = 32, WeightSlot = 38, UnknownDescriptionSlot = 47,
        AmmoQuantitySlot = 60;

    public static ObjectSoundRead ReadPrototype(Func<int, PrototypeFile> lookup, int prototype)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (prototype is < 0 or > 999_999) return new(ObjectSoundFailure.PrototypeMissing, null, prototype);
        var status = PrototypeRecords.Read(lookup, prototype, out var record);
        if (status != PrototypeFileStatus.Found)
            return new(status == PrototypeFileStatus.Missing ? ObjectSoundFailure.PrototypeMissing
                : status == PrototypeFileStatus.Ambiguous ? ObjectSoundFailure.PrototypeAmbiguous : ObjectSoundFailure.PrototypeInvalid, null, prototype);
        var fields = record!.Fields;
        return Read(record.Type, prototype, slot => fields.TryGetValue(slot, out var value) && value.Presence == ObjectWirePresence.Value ? value.Number : null);
    }

    public static ObjectSoundRead Read(AreaObjectRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var fields = new Dictionary<int, AreaFieldValue>();
        foreach (var value in record.EffectiveFields) fields.TryAdd(value.Slot, value);
        return Read(record.Type, record.PrototypeNumber,
            slot => fields.TryGetValue(slot, out var value) && value.Presence == AreaPresence.Value ? value.Number : null);
    }

    private static ObjectSoundRead Read(int type, int prototype, Func<int, long?> number)
    {
        if (type is < 0 or > LastType) return new(ObjectSoundFailure.TypeUnsupported, null, type);
        int? failed = null;
        int Int(int slot)
        {
            if (number(slot) is { } n && n is >= int.MinValue and <= uint.MaxValue) return unchecked((int)n);
            failed ??= slot;
            return 0;
        }
        var fields = new ObjectSoundFields(type, prototype, Int(SoundSlot), Int(MaterialSlot), unchecked((uint)Int(CurrentArtSlot)), unchecked((uint)Int(FlagsSlot)));
        if (type is >= WeaponType and <= GenericType)
            fields = fields with { Item = new(Int(WeightSlot), Int(UnknownDescriptionSlot), type == AmmoType ? Int(AmmoQuantitySlot) : null) };
        return failed is { } slot ? new(ObjectSoundFailure.FieldUnresolved, null, slot) : new(ObjectSoundFailure.None, fields);
    }
}
