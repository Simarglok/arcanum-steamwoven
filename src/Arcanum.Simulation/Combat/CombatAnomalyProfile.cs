namespace Arcanum.Simulation.Combat;

/// <summary>
/// The two combat rule settings of owner decision D8: <see cref="Steamwoven"/> (the original with the recorded D2
/// fixes, the default) and <see cref="Original"/> (the retail 1.0.7.4 behaviour, bugs included). Each switch is one
/// bug of the original game that the attack resolver or the turn-based round touches, confirmed in the original executable and recorded in
/// docs/research/npc-combat-ai-plan.md#anomaly-decisions. Where only the reconstruction read differently (A1, A3, A4,
/// A5, A8) there is no switch: both settings behave as the original there. The profile holds no state, so it may change between attacks.
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

    /// <summary>
    /// AW13 and AW14 (owner decision 2026-10-20): a corner step of a line of fire sums both routes around the corner, so the corner
    /// tile's cover counts twice, and the last step also skips the side tiles' objects (see <see cref="LineOfFireRules.StepCost{TObject}"/>);
    /// on a sound line the corner tile's soundproofing counts twice (see <see cref="Ai.AwarenessRules.SoundStepCost{TObject}"/>).
    /// </summary>
    public bool CornerStepCountsBothRoutes { get; init; }

    /// <summary>
    /// CL4 (owner decision 2026-10-20): a surrender leaves combat mode and then writes back critter flags read before, so the
    /// combat-mode bit is set again while the rest of leaving combat mode (the target unlock) stays done. Read by the session's
    /// danger setter (<c>GameSessionOwner.SetDangerState</c>, ARC-WORLD-079), which applies <see cref="Ai.DangerTransition.LeavesCombatMode"/>.
    /// </summary>
    public bool SurrenderKeepsCombatModeFlag { get; init; }

    /// <summary>
    /// FO14 (owner decision 2026-10-21): the setter keeps the target lock when it applies a state other than COMBAT_FOCUS, so a
    /// locked NPC whose flight ended in NONE ignores every later fight request (see <see cref="Ai.DangerRules.Set"/>).
    /// </summary>
    public bool LockedNoneStateFights { get; init; }

    /// <summary>
    /// HS9 (owner decision 2026-10-20): upset attacking returns PARTY when the target's leader equals the leader argument also when
    /// both are null, so a leaderless NPC's scan never helps an ally against a PC or a leaderless NPC and a leaderless guard never
    /// turns on a leaderless aggressor (see <see cref="Ai.HostilityRules.Upset"/>, ARC-WORLD-080).
    /// </summary>
    public bool UpsetPartyMatchesMissingLeader { get; init; }

    /// <summary>
    /// AW16 (owner decision 2026-10-20): an NPC inside both alert squares of an attack is handled twice, repeating its attacked
    /// reaction and target-choice roll (see <see cref="Ai.HostilityRules.NoiseWitnesses{T}"/>, ARC-WORLD-080).
    /// </summary>
    public bool WitnessHandledTwice { get; init; }

    /// <summary>
    /// SP16 (owner decision 2026-10-20): in the danger update's enemy-list path the setter receives the pass record's target after
    /// the spell choice, which a summon or self-aimed spell replaced with the NPC itself (see <see cref="Ai.HostilityRules.FocusTarget"/>).
    /// </summary>
    public bool EnemyListFocusTakesSpellTarget { get; init; }

    /// <summary>
    /// FO7 (owner decision 2026-10-21): no leave clears the no-attack flag, so a follower told to stop and then dismissed, cursed,
    /// jilted or left by an expired wait never fights again until it rejoins (the session's party leaves, ARC-WORLD-080).
    /// </summary>
    public bool NoAttackSurvivesLeaving { get; init; }

    /// <summary>
    /// FO8 (owner decision 2026-10-21): the spell or item chosen for the danger update's target is kept when setter rule 4 refuses
    /// the fight (no-attack flag or the "NPC fighting" switch off; see <see cref="Ai.HostilityRules.KeepsChosenAction"/>).
    /// </summary>
    public bool NoAttackKeepsChosenAction { get; init; }

    /// <summary>
    /// MR3 (owner decision 2026-10-20): a leaderless NPC counts as a party of one in the flee test, its own followers ignored,
    /// while each of its followers counts the whole group (see <see cref="Ai.MoraleRules.Party"/>, ARC-WORLD-082).
    /// </summary>
    public bool NpcLeaderPartyCountsAlone { get; init; }

    /// <summary>
    /// MR7 (owner decision 2026-10-20): the "flee spell used" bit is cleared only by the flee-spell step, so a flight left before
    /// that step makes the next flight skip its flee spell or item once (see <see cref="Ai.DangerRules.Set"/>, ARC-WORLD-082).
    /// </summary>
    public bool StaleSpellFleeSkipsFleeSpell { get; init; }

    /// <summary>
    /// MR9 (owner decision 2026-10-20): perform fleeing starts the flee goal even when the setter refused the surrender
    /// (invulnerable, or a leader of extraordinary charisma; see <see cref="Ai.MoraleRules.StartsFleeGoal"/>, ARC-WORLD-082).
    /// </summary>
    public bool FleeGoalAfterRefusedSurrender { get; init; }

    /// <summary>
    /// CL5 (owner decision 2026-10-20): the gate's blindness, crippling and stun recoveries write back the critter flags read before
    /// the pass's combat scripts, undoing a change those scripts made (see <see cref="Ai.AiSchedulerRules.Gate"/>, ARC-WORLD-084).
    /// </summary>
    public bool GateRecoveryWritesStaleFlags { get; init; }

    /// <summary>
    /// EX9 (owner decision 2026-10-21; ARC-COMBAT-015): the ranged approach's callback (0x425590) stops at a clear shot only when the
    /// attack has become impossible, so an attacker walking to a target that stands still closes to the adjacent tile before it shoots.
    /// Steamwoven stops the approach at the first tile from which the attack goal's reach test passes
    /// (<see cref="LineOfFireRules.InReach{TObject}"/>: within the weapon's range and a clear shot).
    /// </summary>
    public bool RangedApproachSkipsClearShot { get; init; }

    /// <summary>
    /// EX6 (owner decision 2026-10-21; ARC-WORLD-086): a goal run that ends through an end step in a round charges an NPC the
    /// round's pending cost, the last action-point check's value, so a throw, an item use or an aborted action after a walk pays its
    /// last step again (see <see cref="Ai.AiExecutorRules.Step"/>). Steamwoven charges nothing at the end step.
    /// </summary>
    public bool EndStepRechargesPendingCost { get; init; }

    /// <summary>
    /// EX12 (owner decision 2026-10-21; ARC-WORLD-086): the move-away goal that the spread-out goal pushes sets NO_FLEE for good when
    /// its escape line bends after a partial straight line or fails, as a flight's does (see <see cref="Ai.AiExecutorRules.Step"/>).
    /// Steamwoven sets no NO_FLEE from a spread-out move-away; flights keep it.
    /// </summary>
    public bool SpreadOutSetsNoFlee { get; init; }

    /// <summary>
    /// SP11 (owner decision 2026-10-20; ARC-WORLD-088): the AI list cache keeps, per critter, only the list requested when its slot
    /// was taken, frozen until an inventory change, an exhausted listed item, a reset or round-robin reuse, and builds every other list
    /// afresh (see <see cref="Ai.AiListCache.Request"/>). Steamwoven builds each list fresh on every request.
    /// </summary>
    public bool AiListCacheKeepsFirstList { get; init; }

    /// <summary>
    /// SP15 (owner decision 2026-10-20; ARC-WORLD-088): the grenade's friendly-fire scan asks for NPCs only, so a PC in the blast never
    /// stops a throw (see <see cref="Ai.AiGroundRules.Grenade"/>). Steamwoven scans PCs and NPCs with the same protect rule.
    /// </summary>
    public bool GrenadeIgnoresPcsInBlast { get; init; }

    /// <summary>
    /// SP17 (owner decision 2026-10-20; ARC-WORLD-088): the heal ladder's fatigue-recovery rung tests the healer's fatigue but is tried
    /// for every heal target, leader and followers included (see <see cref="Ai.AiSupportRules.Heal"/>). Steamwoven tries it only when
    /// the target is the healer.
    /// </summary>
    public bool HealFatigueRungForOthers { get; init; }

    /// <summary>
    /// P3 (owner decision 2026-10-20; ARC-WORLD-088): the AI redirect compares the spell target's intelligence, not each candidate's, with
    /// the component's <c>MinIQ:</c>, so it redirects every candidate or none (see <see cref="Ai.AiGroundRules.Redirect"/>). Steamwoven
    /// compares each candidate's.
    /// </summary>
    public bool RedirectTestsTargetIntelligence { get; init; }

    /// <summary>
    /// TT4 (ARC-COMBAT-023, under the owner's rule on gameplay bugs of 2026-10-28): the throw goal's charge (0x433270) exempts the local
    /// PC and nothing else of a throw charges it, so in a round the PC throws any number of items for no action points (see
    /// <see cref="TurnBasedRules.ThrowActionCost"/>). Steamwoven charges the PC the goal's 2, as every other thrower.
    /// </summary>
    public bool PcThrowCostsNothing { get; init; }

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
        CornerStepCountsBothRoutes = true,
        SurrenderKeepsCombatModeFlag = true,
        LockedNoneStateFights = true,
        UpsetPartyMatchesMissingLeader = true,
        WitnessHandledTwice = true,
        EnemyListFocusTakesSpellTarget = true,
        NoAttackSurvivesLeaving = true,
        NoAttackKeepsChosenAction = true,
        RangedApproachSkipsClearShot = true,
        NpcLeaderPartyCountsAlone = true,
        StaleSpellFleeSkipsFleeSpell = true,
        FleeGoalAfterRefusedSurrender = true,
        GateRecoveryWritesStaleFlags = true,
        EndStepRechargesPendingCost = true,
        SpreadOutSetsNoFlee = true,
        AiListCacheKeepsFirstList = true,
        GrenadeIgnoresPcsInBlast = true,
        HealFatigueRungForOthers = true,
        RedirectTestsTargetIntelligence = true,
        PcThrowCostsNothing = true,
    };

    /// <summary>Both settings, default first.</summary>
    public static IReadOnlyList<CombatAnomalyProfile> All { get; } = new[] { Steamwoven, Original };

    /// <summary>The setting with this name, or null when there is none.</summary>
    public static CombatAnomalyProfile? FromName(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
