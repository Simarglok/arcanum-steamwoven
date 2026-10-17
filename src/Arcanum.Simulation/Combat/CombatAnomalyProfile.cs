namespace Arcanum.Simulation.Combat;

/// <summary>
/// The two combat rule settings of owner decision D8: <see cref="Steamwoven"/> (the original with the recorded D2
/// fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour, bugs included). Each switch is one
/// bug of the original game that the attack resolver or the turn-based round touches, confirmed in the original executable and recorded in
/// docs/research/npc-combat-ai-plan.md#anomaly-decisions. CE-only reconstruction slips (A1, A3, A4, A5, A8) have no
/// switch: both settings behave as the original there. The profile holds no state, so it may change between attacks.
/// </summary>
public sealed record CombatAnomalyProfile
{
    /// <summary>
    /// Stable setting name, <c>steamwoven</c> or <c>original</c>. It identifies only the two built-in settings; a
    /// profile changed with <c>with</c> is a custom mix and should get its own name.
    /// </summary>
    public required string Name { get; init; }

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

    /// <summary>
    /// A9: when the skip loop of the next subturn runs past the last participant, the new round's first subturn is started
    /// twice (its action points granted and, for an NPC, its heartbeat script and AI run twice).
    /// </summary>
    public bool FirstSubturnStartedTwice { get; init; }

    /// <summary>
    /// A11: the stalled-actor check keeps the previous actor's action points as the reference when the actor changes, so
    /// the new actor's second check compares its action points with another critter's (see <see cref="TurnBasedStall{TActor}"/>).
    /// </summary>
    public bool StallCheckKeepsPreviousActionPoints { get; init; }

    /// <summary>
    /// A12: when an overexertion knocks the local PC out, its subturn ends at once and the next participant's subturn
    /// starts, and the overexertion then sets the action points, now the next participant's, to 0.
    /// </summary>
    public bool OverexertionKnockoutClearsNextActionPoints { get; init; }

    /// <summary>The original with every recorded D2 fix; the default setting.</summary>
    public static readonly CombatAnomalyProfile Steamwoven = new() { Name = "steamwoven" };

    /// <summary>The retail 1.0.7.4 behaviour: every switch keeps the original game's bug.</summary>
    public static readonly CombatAnomalyProfile Original = new()
    {
        Name = "original",
        InvocationModifierCountedTwice = true,
        ArmorClassEffectsCountedTwice = true,
        CriticalSelfHitRolledTwice = true,
        BackstabDamageOnSelfHit = true,
        FirstSubturnStartedTwice = true,
        StallCheckKeepsPreviousActionPoints = true,
        OverexertionKnockoutClearsNextActionPoints = true,
    };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<CombatAnomalyProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static CombatAnomalyProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
