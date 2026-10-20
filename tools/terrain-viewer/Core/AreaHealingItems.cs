using Arcanum.Data.Combat;
using Arcanum.Session;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-SKILLS-005: the bridge from an item's stored fields (ARC-DATA-021) to the session's <see cref="SessionHealingItem"/>: a generic
/// item (type 14) whose flags mark it a healing item (field 0xD3 bit 0x08), its usage bonus and remaining uses, its condition and
/// its script points. Anything else, or values the durability rules refuse, is null.
/// </summary>
public static class AreaHealingItems
{
    public const int GenericType = 14;

    public static SessionHealingItem? From(string entity, CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Object.Type != GenericType || fields.Generic is not { } generic || (generic.Flags & SessionHealingItem.HealingFlag) == 0) return null;
        return SessionItemConditions.FromCombatFields(fields) is { } condition
            ? new(entity, generic.Flags, generic.UsageBonus, generic.UsageCountRemaining, condition, fields.Object.ScriptPoints)
            : null;
    }
}
