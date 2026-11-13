using Arcanum.Data.Area;
using Arcanum.Data.Characters;

namespace Arcanum.Data.Objects;

/// <summary>Why <see cref="CreatedObjectRows.Critter"/> built no row.</summary>
public enum CreatedRowFailure
{
    None,
    /// <summary>The prototype number is outside a prototype file's six digits, or the entity, map or cell is invalid.</summary>
    InvalidArguments,
    /// <summary>The lookup has no file of that number.</summary>
    PrototypeMissing,
    /// <summary>The file is ambiguous, too large or not a complete v119 prototype of that number.</summary>
    PrototypeInvalid,
    /// <summary>The prototype is not a critter's (object type 16): a created critter is an NPC.</summary>
    NotACritter,
}

/// <summary>The outcome of <see cref="CreatedObjectRows.Critter"/>: the row, or why none.</summary>
public sealed record CreatedObjectRowResult(CreatedRowFailure Failure, AreaObjectRecord? Row);

/// <summary>
/// ARC-MAGIC-053 (E1-e, ledger SUM5): what the Summon effect (0x450C40) writes on the NPC it makes besides its flags: its experience worth and pool are cleared (fields 285 and 286: a summoned creature gives and takes no
/// experience) and, when the run has a parent, the parent's alignment (stat 19) is copied (<see cref="Alignment"/>, null when the parent's is not known); ARC-MAGIC-1169 (E1-f): when the parent is an NPC its faction (field 292, the NPC block's slot 81) is copied too
/// (<see cref="Faction"/>, null for a parent that is no NPC: a PC summoner leaves the prototype's faction).
/// </summary>
public sealed record CreatedCritterSummon(int? Alignment, int? Faction = null);

/// <summary>
/// ARC-DEV-359 (E1-c): the base record of an object the session created from a prototype, built by the rule a map object's record is built by
/// (<c>AreaBuilder.AddObject</c>: the prototype's fields, each replaced by the object's own where it has one). A created object has no map file, so its own
/// fields are the ones the original's create writes (OM13, OM16): its location (field 1, the cell packed as a map object stores it: x in the low half, y in the high half), OF_DYNAMIC, and for
/// an NPC its two standpoints and NPC flag 0x2; everything else it reads from its prototype until written (SA2, SA4). The teleport destination OM13 also names is not written (the field is not identified
/// in this repository; the session's own teleport map, `npc.teleport`, is a record of its own). The row is the one a map's NPC has, so every consumer that asks the record resolver, the
/// combat host or the AI host for a row by identity gets a created critter's as it gets a map NPC's. Pure: the lookup is the game's prototype reader
/// (<see cref="Content.ContentPrototypes.Read"/>), the session reads no game data.
/// </summary>
public static class CreatedObjectRows
{
    /// <summary>The object type of a critter (NPC) in the original's numbering.</summary>
    public const int NpcType = 16;

    /// <summary>The id the row's key carries for a created entity: the entity without its <c>oid:</c> prefix (a map object's key carries the hash of its own id there, and
    /// <c>"oid:" + ObjectIdSha256</c> is the entity of every row the world reads, so the created entity round-trips through the same rule).</summary>
    public static string KeyId(string entity) => entity[Prefix.Length..];

    private const string Prefix = "oid:";

    /// <summary>The source id the key of a created object's row carries (no module file names it).</summary>
    public const string Source = "created";

    /// <summary>
    /// The row of a critter of prototype file <paramref name="prototype"/> (the six-digit file number, the basic prototype number + 20) standing on cell
    /// (<paramref name="cellX"/>, <paramref name="cellY"/>) of map <paramref name="runtimeMap"/> under the entity <paramref name="entity"/> (an <c>oid:</c> entity). The effective fields are
    /// the prototype's with the object's own fields written over (the direct fields). A prototype that is not a critter's is refused, whatever else it holds.
    /// </summary>
    public static CreatedObjectRowResult Critter(Func<int, PrototypeFile> lookup, int prototype, string entity, int runtimeMap, int cellX, int cellY, CreatedCritterSummon? summon = null)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(entity);
        if (prototype is < 1 or > 999_999 || !entity.StartsWith(Prefix, StringComparison.Ordinal) || entity.Length == Prefix.Length || runtimeMap < 1 || cellX < 0 || cellY < 0)
            return new(CreatedRowFailure.InvalidArguments, null);
        var status = PrototypeRecords.Read(lookup, prototype, out var record);
        if (status != PrototypeFileStatus.Found)
            return new(status == PrototypeFileStatus.Missing ? CreatedRowFailure.PrototypeMissing : CreatedRowFailure.PrototypeInvalid, null);
        if (record!.Type != NpcType) return new(CreatedRowFailure.NotACritter, null);

        // What the original's create writes (OM13, 0x4058E0; finish OM16, 0x442260): the location, the object flag OF_DYNAMIC, and for an NPC the day and night standpoints (both the
        // creation cell) and NPC flag 0x2 (ONF_WAYPOINTS_DAY). The location is the cell as a map object stores it (x low, y high). Everything else is read through the prototype.
        var location = (long)(uint)cellX | ((long)(uint)cellY << 32);
        var effective = new SortedDictionary<int, AreaFieldValue>();
        foreach (var (slot, value) in record.Fields)
            effective[slot] = new AreaFieldValue(slot, (AreaPresence)((int)value.Presence + 1), AreaValueOrigin.Prototype, value.Number,
                Array.AsReadOnly(value.Elements.Select(element => new AreaSparseElement(element.Key, Array.AsReadOnly(element.Words.ToArray()))).ToArray()), value.Fingerprint);
        var direct = new SortedDictionary<int, AreaFieldValue>();
        void Own(int slot, long number) => direct[slot] = effective[slot] = new AreaFieldValue(slot, AreaPresence.Value, AreaValueOrigin.Instance, number, [], null);
        long Base(int slot) => effective.TryGetValue(slot, out var held) && held.Presence == AreaPresence.Value ? held.Number ?? 0 : 0;
        Own(LocationSlot, location);
        Own(FlagsSlot, Base(FlagsSlot) | DynamicFlag);
        Own(NpcFlagsSlot, Base(NpcFlagsSlot) | WaypointsDayFlag);
        Own(StandpointDaySlot, location);
        Own(StandpointNightSlot, location);
        if (summon is not null)
        {
            // SUM5: the experience worth and pool cleared; the parent's alignment copied into the stat array's element 19; an NPC parent's faction copied (field 292).
            Own(ExperienceWorthSlot, 0);
            Own(ExperiencePoolSlot, 0);
            if (summon.Faction is { } faction) Own(FactionSlot, faction);
            if (summon.Alignment is { } alignment && effective.TryGetValue(StatSlot, out var stats) && stats.Presence == AreaPresence.Array)
            {
                var elements = stats.Elements.Where(element => element.Key != AlignmentStat).Append(new AreaSparseElement(AlignmentStat, [unchecked((long)(uint)alignment)])).OrderBy(element => element.Key).ToArray();
                direct[StatSlot] = effective[StatSlot] = new AreaFieldValue(StatSlot, AreaPresence.Array, AreaValueOrigin.Instance, null, Array.AsReadOnly(elements), null);
            }
        }

        var key = new AreaObjectKey(Source, runtimeMap, AreaSourceKind.Mobile, null, 0, 0, KeyId(entity), 0);
        var row = new AreaObjectRecord(key, 119, record.Type, prototype, record.Own.Sha256, record.Own.Sha256, "", "", AreaScope.Core, location, cellX, cellY,
            AreaDisposition.BodyEligibleConditionally, Array.AsReadOnly(direct.Values.ToArray()), Array.AsReadOnly(effective.Values.ToArray()), true, true, true);
        return new(CreatedRowFailure.None, row);
    }

    /// <summary>
    /// ARC-WORLD-1526 (ledger GS5, PO4, VB4-9): the row of a critter that is a COPY of the object <paramref name="template"/> (a monster generator's copy: the original duplicates the whole object, all its instance fields, scripts,
    /// name, stats, flags and held items, where <see cref="Critter"/> reads the prototype only). The copy's fields are the template's: the direct fields stay direct (the instance's own over its prototype's), the effective fields are
    /// the template's effective ones. What the original then writes on the copy is the unmarking of the generator (NPC flag 0x80000, and with it the object flags 0x400002, cleared), its location, both standpoints (fields 0x121 and 0x122,
    /// only for a critter) and OF_DYNAMIC (bit 0x2000 of the object flags); the NPC flag 0x2 that <see cref="Critter"/>'s create path (OM13) writes is NOT written: a copy has the flags of its template. The template's held items are
    /// not part of the row (an open part of GS5); the restock facts (inventory source, field 0xEF) are the template's. A template that is not a critter's row is refused with <see cref="CreatedRowFailure.NotACritter"/>.
    /// </summary>
    public static CreatedObjectRowResult CritterFrom(AreaObjectRecord template, string entity, int runtimeMap, int cellX, int cellY)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(entity);
        if (!entity.StartsWith(Prefix, StringComparison.Ordinal) || entity.Length == Prefix.Length || runtimeMap < 1 || cellX < 0 || cellY < 0) return new(CreatedRowFailure.InvalidArguments, null);
        if (template.Type != NpcType) return new(CreatedRowFailure.NotACritter, null);

        var location = (long)(uint)cellX | ((long)(uint)cellY << 32);
        var direct = new SortedDictionary<int, AreaFieldValue>();
        var effective = new SortedDictionary<int, AreaFieldValue>();
        foreach (var field in template.EffectiveFields) effective[field.Slot] = field;
        foreach (var field in template.DirectFields) direct[field.Slot] = field;
        long Base(int slot) => effective.TryGetValue(slot, out var held) && held.Presence == AreaPresence.Value ? held.Number ?? 0 : 0;
        void Own(int slot, long number) => direct[slot] = effective[slot] = new AreaFieldValue(slot, AreaPresence.Value, AreaValueOrigin.Instance, number, [], null);
        Own(LocationSlot, location);
        // the generator unmark (ledger VB4-9) touches only an NPC that carries the generator bit (it returns 0 and touches nothing otherwise): the object flags 0x400002 go with the bit.
        var marked = (Base(NpcFlagsSlot) & GeneratorNpcFlag) != 0;
        Own(FlagsSlot, (marked ? Base(FlagsSlot) & ~GeneratorObjectFlags : Base(FlagsSlot)) | DynamicFlag);
        Own(NpcFlagsSlot, Base(NpcFlagsSlot) & ~GeneratorNpcFlag);
        Own(StandpointDaySlot, location);
        Own(StandpointNightSlot, location);

        var key = new AreaObjectKey(Source, runtimeMap, AreaSourceKind.Mobile, null, 0, 0, KeyId(entity), 0);
        var row = new AreaObjectRecord(key, template.Version, template.Type, template.PrototypeNumber, template.PrototypeReferenceSha256, template.PrototypeOwnSha256, "", "", AreaScope.Core, location, cellX, cellY,
            AreaDisposition.BodyEligibleConditionally, Array.AsReadOnly(direct.Values.ToArray()), Array.AsReadOnly(effective.Values.ToArray()), true, true, true);
        return new(CreatedRowFailure.None, row);
    }

    /// <summary>ARC-WORLD-1526: the monster generator's marks a copy loses (0x4BA720): NPC flag 0x80000 and the object flags 0x400002 (off, invulnerable).</summary>
    public const long GeneratorNpcFlag = 0x80000, GeneratorObjectFlags = 0x400002;

    /// <summary>The slots of an object's location, its object flags, an NPC's flags and its day and night standpoints.</summary>
    public const int LocationSlot = 1, FlagsSlot = 18, NpcFlagsSlot = 69, StandpointDaySlot = 78, StandpointNightSlot = 79;

    /// <summary>ARC-MAGIC-053: the slots of an NPC's experience worth and pool (fields 285 and 286 of the executable), of the critter's stat array and the alignment's element in it (stat 19).</summary>
    public const int ExperienceWorthSlot = 74, ExperiencePoolSlot = 75, FactionSlot = 81, StatSlot = 38, AlignmentStat = 19;

    /// <summary>OF_DYNAMIC (object flag 0x2000), which the original's create finish sets on the new object (OM16); and ONF_WAYPOINTS_DAY (NPC flag 0x2), which its create sets on an NPC (OM13).</summary>
    public const long DynamicFlag = 0x2000, WaypointsDayFlag = 0x2;
}
