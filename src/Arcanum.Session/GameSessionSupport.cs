using Arcanum.Simulation;
using Arcanum.Simulation.Ai;

namespace Arcanum.Session;

public sealed partial class GameSessionOwner
{
    /// <summary>The bits <see cref="SetAiSupportBits"/> may set: the look-for bits and CHECK_WEAPON (an AI ranged attack without
    /// ammunition sets all three of ammunition, weapon and CHECK_WEAPON, 0x4ADFF0; an NPC that drops a weapon or armour gets the
    /// matching look-for bit, 0x461AB0).</summary>
    public const DangerFlags AiSupportSetBits = DangerFlags.LookForAmmo | DangerFlags.LookForWeapon | DangerFlags.LookForArmor | DangerFlags.CheckWeapon;

    /// <summary>The bits <see cref="ClearAiSupportBits"/> may clear: the look-for bits (the item search, 0x4A8E70) and CHECK_GRENADE
    /// (the grenade step, 0x4AC910).</summary>
    public const DangerFlags AiSupportClearBits = DangerFlags.LookForAmmo | DangerFlags.LookForWeapon | DangerFlags.LookForArmor | DangerFlags.CheckGrenade;

    /// <summary>
    /// The support bits <paramref name="bits"/> set on the NPC's danger record in one revision (none when they are set already);
    /// a bit outside <see cref="AiSupportSetBits"/> is <see cref="GameSessionFailure.InvalidDangerFacts"/>.
    /// </summary>
    public SessionDangerResult SetAiSupportBits(GameSessionVersion expected, string entity, DangerFlags bits) =>
        (bits & ~AiSupportSetBits) != 0 ? RejectDanger(GameSessionFailure.InvalidDangerFacts)
        : ChangeDanger(expected, entity, null, false, plan => plan.Update(entity, record => record with { Flags = record.Flags | bits }));

    /// <summary>
    /// The support bits <paramref name="bits"/> cleared on the NPC's danger record in one revision (none when they are clear); a
    /// bit outside <see cref="AiSupportClearBits"/> is <see cref="GameSessionFailure.InvalidDangerFacts"/>.
    /// </summary>
    public SessionDangerResult ClearAiSupportBits(GameSessionVersion expected, string entity, DangerFlags bits) =>
        (bits & ~AiSupportClearBits) != 0 ? RejectDanger(GameSessionFailure.InvalidDangerFacts)
        : ChangeDanger(expected, entity, null, false, plan => plan.Update(entity, record => record with { Flags = record.Flags & ~bits }));

    /// <summary>
    /// The redirect's writes for one NPC (0x4A94C0, after the attack event the host runs with <paramref name="target"/> as attacker):
    /// the combat focus and who-hit-me-last := the target, whatever the danger state, in one revision (none when both hold it).
    /// </summary>
    [OriginalFunction(0x4A94C0, OriginalFunctionScope.Partial)]
    public SessionDangerResult RedirectDanger(GameSessionVersion expected, string entity, SessionCombatant target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ChangeDanger(expected, entity, target, false, plan => plan.Update(entity, record => record with { Focus = target, HitBy = target }));
    }
}
