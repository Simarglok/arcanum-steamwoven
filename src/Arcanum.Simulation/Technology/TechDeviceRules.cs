using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Equipment;

namespace Arcanum.Simulation.Technology;

/// <summary>
/// How a device works for a holder (ARC-TECHNOLOGY-008, ledger DV rows): whether anything refuses it for the holder's aptitude
/// outside combat (never for a technological item), the power the holder draws (which scales the item's magic adjustments and
/// a cell-powered spell's need), that power in percent, and, for a weapon, the chance that it fails critically in combat.
/// </summary>
public readonly record struct TechDeviceUse(bool Usable, int Power, int PowerRatio, int CombatFailureChance);

/// <summary>
/// ARC-TECHNOLOGY-008: magick against technology for a device's holder (contract docs/world/tech-devices-runtime.md). Pure rules
/// over the item's magick/tech complexity (negative: technological) and the holder's magick/tech aptitude (negative:
/// technologist).
/// </summary>
public static class TechDeviceRules
{
    /// <summary>
    /// A device for a critter holder. Outside combat nothing refuses a technological item to a mage (DV2: the use, the item
    /// trigger executor and the run creation read neither complexity nor aptitude for a technology record; the holder gate
    /// 0x456A10 refuses only a magickal item to a technologist, and only the AI's item scan asks it), so it is
    /// <see cref="TechDeviceUse.Usable"/> whatever the holder; a magickal item is usable when the gate allows it
    /// (<see cref="Magic.SpellInvocationRules.HolderCanUseItem"/>'s rule). The power is the item's for the holder (0x4614A0);
    /// a weapon's combat failure chance is <see cref="AttackRules.TechnologyFailureChance"/> (0x461700: a technological weapon in a
    /// mage's hands, rolled before the hit roll for any attack but a throw; a failure is a critical miss, DV4).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A complexity or aptitude outside the equipment limits.</exception>
    public static TechDeviceUse Use(int complexity, int aptitude, bool weapon)
    {
        var power = EquipmentRules.EffectivePower(complexity, aptitude);
        var usable = Magic.SpellInvocationRules.HolderCanUseItem(aptitude, complexity, () => power);
        return new(usable, power, EquipmentRules.EffectivePowerRatio(complexity, aptitude),
            weapon ? AttackRules.TechnologyFailureChance(complexity, aptitude) : 0);
    }
}
