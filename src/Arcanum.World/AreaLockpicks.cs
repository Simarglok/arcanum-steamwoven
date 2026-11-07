using Arcanum.Data.Combat;
using Arcanum.Session;

namespace Arcanum.World;

/// <summary>
/// ARC-SKILLS-006: the bridge from an item's stored fields (ARC-DATA-021) to the session's <see cref="SessionLockpick"/>: a generic
/// item (type 14) whose flags mark it lockpicks (field 0xD3 bit 0x02), its usage bonus, its condition and its script points.
/// Anything else, or values the durability rules refuse, is null.
/// </summary>
public static class AreaLockpicks
{
    public static SessionLockpick? From(string entity, CombatFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Object.Type != AreaHealingItems.GenericType || fields.Generic is not { } generic || (generic.Flags & SessionLockpick.LockpickFlag) == 0)
            return null;
        return SessionItemConditions.FromCombatFields(fields) is { } condition
            ? new(entity, generic.Flags, generic.UsageBonus, condition, fields.Object.ScriptPoints)
            : null;
    }
}
