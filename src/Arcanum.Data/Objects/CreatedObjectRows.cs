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
    public static CreatedObjectRowResult Critter(Func<int, PrototypeFile> lookup, int prototype, string entity, int runtimeMap, int cellX, int cellY)
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

        var key = new AreaObjectKey(Source, runtimeMap, AreaSourceKind.Mobile, null, 0, 0, KeyId(entity), 0);
        var row = new AreaObjectRecord(key, 119, record.Type, prototype, record.Own.Sha256, record.Own.Sha256, "", "", AreaScope.Core, location, cellX, cellY,
            AreaDisposition.BodyEligibleConditionally, Array.AsReadOnly(direct.Values.ToArray()), Array.AsReadOnly(effective.Values.ToArray()), true, true, true);
        return new(CreatedRowFailure.None, row);
    }

    /// <summary>The slots of an object's location, its object flags, an NPC's flags and its day and night standpoints.</summary>
    public const int LocationSlot = 1, FlagsSlot = 18, NpcFlagsSlot = 69, StandpointDaySlot = 78, StandpointNightSlot = 79;

    /// <summary>OF_DYNAMIC (object flag 0x2000), which the original's create finish sets on the new object (OM16); and ONF_WAYPOINTS_DAY (NPC flag 0x2), which its create sets on an NPC (OM13).</summary>
    public const long DynamicFlag = 0x2000, WaypointsDayFlag = 0x2;
}
