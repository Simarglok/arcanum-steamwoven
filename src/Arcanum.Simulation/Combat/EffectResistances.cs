using Arcanum.Simulation.Character;

namespace Arcanum.Simulation.Combat;

/// <summary>
/// The resistance changes of a critter's effect list (ARC-MAGIC-027, magic audit MA-2): the modifiers of each of the source's five resistances in list order
/// (normal 0, fire 1, electrical 2, poison 3, magic 4; <c>resistdamage</c>, <c>resistfire</c>, <c>resistelectrical</c>, <c>resistpoison</c>, <c>resistmagic</c>). The effective
/// resistance function (0x43D6D0) aggregates them over the stored value, the armour and the poison bonus (<see cref="CombatantRules.Resistances(DamageResistances, Arcanum.Simulation.Equipment.EquipmentResistances, int, bool, EffectResistances)"/>).
/// <see cref="Magic"/> is kept for the character sheet's Magick line: the magic save of a spell reads the stored resistance alone (ledger O9, RE1).
/// </summary>
public sealed record EffectResistances(IReadOnlyList<EffectModifier> Normal, IReadOnlyList<EffectModifier> Fire, IReadOnlyList<EffectModifier> Electrical,
    IReadOnlyList<EffectModifier> Poison, IReadOnlyList<EffectModifier> Magic)
{
    /// <summary>No resistance effect.</summary>
    public static EffectResistances None { get; } = new([], [], [], [], []);

    /// <summary>True when no resistance is changed.</summary>
    public bool IsEmpty => Normal.Count == 0 && Fire.Count == 0 && Electrical.Count == 0 && Poison.Count == 0 && Magic.Count == 0;
}
