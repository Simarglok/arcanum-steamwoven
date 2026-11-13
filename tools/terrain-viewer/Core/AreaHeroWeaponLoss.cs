using Arcanum.Session;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;
using Arcanum.Simulation.Items;

namespace Arcanum.TerrainViewer.Core;

/// <summary>
/// ARC-COMBAT-1738 (ledger OH3, PI5; the damage step 0x4B4390 and its item-loss branches, messages 0x13, 0x16 and 0x17): whether an attack that struck the hero took its wielded
/// weapon, which asks the hero's auto-switch re-wield (0x4B83E0). The cases are the "drop weapon" critical (a drop that happened), the "destroy weapon" and "explode weapon"
/// criticals (the weapon deleted) and the "damage weapon" critical that broke it. The arm-hit drops are another branch of the step and do not call the re-wield. Godot-free.
/// </summary>
public static class AreaHeroWeaponLoss
{
    /// <summary>True when the attack's recipient was the struck side (the hero, for a strike on the hero) and a step of the attack took that side's weapon.</summary>
    public static bool TookStruckWeapon(DamageRecipient? recipient, IReadOnlyList<SessionCriticalStep> criticals, SessionAttackWearResult? wear)
    {
        ArgumentNullException.ThrowIfNull(criticals);
        if (recipient != DamageRecipient.Target) return false;
        if (criticals.Any(step => step is { Kind: SessionCriticalStepKind.DropWeapon, Whose: DamageRecipient.Target, Outcome: SessionCriticalStepOutcome.Dropped })) return true;
        return wear?.Steps.Any(step => step is { Wearer: DamageRecipient.Target, Slot: EquipmentSlot.Weapon, Skip: SessionWearSkip.None }
            && (step.Kind == SessionWearStepKind.WeaponDestroy
                || step is { Kind: SessionWearStepKind.WeaponDamage, Item.Break: ItemBreakStep.Broken or ItemBreakStep.Deleted })) ?? false;
    }
}
