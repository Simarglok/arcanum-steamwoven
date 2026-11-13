using Arcanum.Data.Area;
using Arcanum.Data.Combat;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-MAGIC-1551 (FR-MAGIC-007): an NPC's stored fields as a reader of its raw stored stats sees them once a TraitIdx spell wrote into them (<see cref="SessionCritterIndexedWrites"/>, laid over the row's
/// fields by <see cref="SessionCritterIndexedWrites.Apply"/>). One helper for the readers that take an NPC's fields from its map row and not through the combat host (which lays the written entries over by
/// itself since ARC-MAGIC-1475): the greeting facts, the follow candidate's level and alignment, the hover window. The entity is the row's identity (<see cref="AreaConversationState.EntityId"/>), the one the
/// session keeps the written entries on, wherever the NPC stands.
/// </summary>
public static class AreaWrittenCritterFields
{
    /// <summary>The row's stored fields with the entries written into its NPC laid over them; null when the row's fields do not read. The stored fields themselves without a session or nothing written.</summary>
    public static CombatFields? Read(GameSessionOwner? session, AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Over(session, row, CombatFieldReader.Read(row).Fields);
    }

    /// <summary>Already-read stored fields of the row with the written entries laid over them (the fields themselves without a session or nothing written, or null for null).</summary>
    public static CombatFields? Over(GameSessionOwner? session, AreaObjectRecord row, CombatFields? fields)
    {
        ArgumentNullException.ThrowIfNull(row);
        return session is null || fields is null ? fields : session.QueryCritterIndexedWrites(AreaConversationState.EntityId(row)).Apply(fields);
    }

    /// <summary>A key that names the entries written into the row's NPC (empty without a session or when none is written), for a cache of values built from them.</summary>
    public static string Signature(GameSessionOwner? session, AreaObjectRecord row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return session?.QueryCritterIndexedWrites(AreaConversationState.EntityId(row)).Signature ?? "";
    }
}
