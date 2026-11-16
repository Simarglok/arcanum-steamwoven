namespace Arcanum.Session;

/// <summary>
/// ARC-SCRIPT-2375 (ledger IV24, CQ5, exe 0x447220 case 0x5C): an item's name number, the object field 0x16 of the original that a script's rename (92) writes with no type test. The map data gives every item its name number (the class the name
/// conditions compare, such as is named and the item finders; it is not display text); a renamed number is the object state of the item's directory entry, the field <see cref="Property"/>, a mobile object field of
/// <see cref="ObjectStateVocabulary"/> kept only while it differs from the map data's (as <c>npc.name</c> is for an NPC), so no schema step: an older save holds none. It follows the item from holder to holder and from map to map by its identity.
/// </summary>
public static class SessionItemName
{
    /// <summary>The object field that holds an item's renamed name number.</summary>
    public const string Property = "item.name";

    /// <summary>The map change key of the item's renamed name number.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}
