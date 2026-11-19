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
    /// <summary>ARC-SCRIPT-2752: the lockpicks without their use script point, for the use-item-with-skill goal whose use script is the one that asked for it (the session refuses lockpicks with a use script, since it runs none).</summary>
    public static SessionLockpick WithoutUseScript(SessionLockpick lockpick)
    {
        ArgumentNullException.ThrowIfNull(lockpick);
        return lockpick.ScriptPoints is { } points && points.Contains(SessionLockpick.UseScript)
            ? lockpick with { ScriptPoints = [.. points.Where(p => p != SessionLockpick.UseScript)] } : lockpick;
    }

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
