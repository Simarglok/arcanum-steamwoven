namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-1772 (ledger CQ5, exe 0x447220 case 0x5C): an NPC's name number, the object field 0x16 of the original that a script's rename (92) writes. The map data gives every NPC its name number (the class the name conditions compare, such as is named, the dialog's
/// follower-named test and the item finders; it is not display text); a renamed number is the object state of the NPC's directory entry, the field <see cref="Property"/>, a mobile object field of <see cref="ObjectStateVocabulary"/>, kept only while it differs from the
/// map data's (as <c>npc.origin</c> and <c>npc.faction</c> are), so no schema step: an older save holds none.
/// </summary>
public static class SessionNpcName
{
    /// <summary>The object field that holds an NPC's renamed name number.</summary>
    public const string Property = "npc.name";

    /// <summary>The map change key of the NPC's renamed name number.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}
