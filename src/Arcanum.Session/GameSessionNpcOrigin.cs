namespace Arcanum.Session;

/// <summary>
/// ARC-DEV-230 (schema 74, ledger NQ6): an NPC's origin, the object field 0x123 of the original that the dialog action <c>or</c> sets (0x45D2D0). The map data gives every NPC its
/// origin; a set origin is the object state of the NPC's directory entry, the field <see cref="Property"/>, a mobile object field of <see cref="ObjectStateVocabulary"/>, kept only
/// while it differs from the map data's (as <c>npc.faction</c> is). The origin and the faction select the reputation effects of an NPC's reaction (RP5).
/// </summary>
public static class SessionNpcOrigin
{
    /// <summary>The object field that holds an NPC's set origin.</summary>
    public const string Property = "npc.origin";

    /// <summary>The map change key of the NPC's set origin.</summary>
    public static MapChangeKey Key(string entity) => MapChangeKey.ForEntity(entity, Property);
}
