namespace Arcanum.Simulation.Combat;

/// <summary>
/// Switches between the fixed default and the source (CE) behaviour of each confirmed CE slip the attack resolver
/// touches (owner decision D2, recorded in docs/research/npc-combat-ai-plan.md#anomaly-decisions). <see cref="Default"/>
/// is the source with the fixes; <see cref="Source"/> keeps every CE slip, for comparison with CE behaviour.
/// The profile holds no state, so it may change between attacks. Anomaly A8 has no source variant: CE reads an
/// unassigned value there, which no rule can reproduce, so both profiles use 0.
/// </summary>
public sealed record CombatAnomalyProfile
{
    /// <summary>A2: the bed's −30 invocation modifier is counted twice (−60).</summary>
    public bool InvocationModifierCountedTwice { get; init; }

    /// <summary>
    /// A6: AC-adjustment effects apply to the whole armour class and the effective AcAdjustment stat, which already
    /// carries them, is added on top (see <see cref="AttackRules.ArmorClass"/>).
    /// </summary>
    public bool ArmorClassEffectsCountedTwice { get; init; }

    /// <summary>
    /// A7: a critical self-hit rolls the critical-hit procedure twice (in the critical-miss branch and again when the
    /// damage is applied); the flags of both rolls add up.
    /// </summary>
    public bool CriticalSelfHitRolledTwice { get; init; }

    /// <summary>A10: the backstab damage bonus also applies when a critical miss turns the attack on the attacker.</summary>
    public bool BackstabDamageOnSelfHit { get; init; }

    /// <summary>The source with every recorded D2 fix.</summary>
    public static readonly CombatAnomalyProfile Default = new();

    /// <summary>CE behaviour for every switch.</summary>
    public static readonly CombatAnomalyProfile Source = new()
    {
        InvocationModifierCountedTwice = true,
        ArmorClassEffectsCountedTwice = true,
        CriticalSelfHitRolledTwice = true,
        BackstabDamageOnSelfHit = true,
    };
}
