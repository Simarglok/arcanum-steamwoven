using Arcanum.Simulation.Character;

namespace Arcanum.Simulation.Reaction;

/// <summary>
/// The NPC→PC reaction rules (ARC-RES-028 R1, R2, R4, R5, R8) as pure functions over explicit inputs, checked
/// against the retail 1.0.7.4 executable (ARC-WORLD-026, verification ledger):
/// the effective value 0x4C0D00 with its term 0x4C1500 and table 0x5B684C, the change
/// 0x4C0DE0, the categories 0x4C0FC0, the conversation start 0x4C1020, the
/// quest changes in 0x4C4E60 and the hit branch of 0x4A9650. The stored value and
/// the memory of who the NPC met are state of the session, not of these rules.
/// </summary>
public static class ReactionRules
{
    /// <summary>The prototype default of the reaction base, the value of a query on a non-critter pair, and the floor of the
    /// reaction of an NPC this PC mind-controls.</summary>
    public const int Neutral = 50;

    /// <summary>The floor of the race and beauty term in the hostility variant.</summary>
    public const int HostilityTermFloor = -49;

    /// <summary>The conversation-start value when the PC spends the "force good reaction" fate point.</summary>
    public const int ForcedGoodReaction = 100;

    /// <summary>Quest accepted: the reaction is raised to at least this value. Quest completed: <see cref="CompletedQuestBonus"/>.</summary>
    public const int AcceptedQuestReaction = 41;
    public const int CompletedQuestBonus = 10;

    /// <summary>The change when the PC hits an NPC that does not follow it by accident.</summary>
    public const int AccidentalHit = -10;

    /// <summary>Number of PC races (the table's columns): Human through HalfOgre.</summary>
    public const int PcRaceCount = 8;

    // Rows: NPC race (11, Human..Orc); columns: PC race (8, Human..HalfOgre). Source table 0x5B684C, equal
    // value for value to the retail table at 0x5B684C.
    private static readonly int[,] RaceTable =
    {
        { 0, 0, 5, 5, 0, 10, -5, -5 },
        { -5, 10, -10, -5, 0, 5, -10, -15 },
        { -10, -20, 0, 0, -10, 10, -15, -10 },
        { 0, 0, 0, 0, 0, 5, -5, -5 },
        { 0, 0, 0, 0, 5, 5, -10, 5 },
        { 0, 0, 20, 15, 0, 10, -10, -10 },
        { 0, -20, -15, -10, -10, 0, 10, 0 },
        { 0, -20, -15, -5, 20, 10, 0, 5 },
        { -30, -30, 5, -20, -30, -5, -30, -30 },
        { -10, -30, -20, -15, -20, -20, 0, -10 },
        { -20, -30, -40, -30, -20, -20, 10, 0 },
    };

    /// <summary>The race term of an NPC race toward a PC race. A PC race outside the eight PC races throws: the source indexes
    /// its 8-column table without a check (latent bug LM12, fixed in both rule modes).</summary>
    [OriginalFunction(0x4C1500, OriginalFunctionScope.Partial)]
    public static int RaceReaction(CharacterRace npc, CharacterRace pc)
    {
        if (!CharacterStatRules.IsDefined(npc)) throw new ArgumentOutOfRangeException(nameof(npc));
        if (!IsPcRace(pc)) throw new ArgumentOutOfRangeException(nameof(pc));
        return RaceTable[(int)npc, (int)pc];
    }

    public static bool IsPcRace(CharacterRace race) => race is >= CharacterRace.Human and <= CharacterRace.HalfOgre;

    /// <summary>Category by the source thresholds: ≤ 0 hatred, ≤ 20 dislike, ≤ 40 suspicious, ≤ 60 neutral, ≤ 80 courteous,
    /// ≤ 100 amiable, above love.</summary>
    [OriginalFunction(0x4C0FC0, OriginalFunctionScope.Full)]
    public static ReactionCategory Category(int reaction) => reaction switch
    {
        <= 0 => ReactionCategory.Hatred,
        <= 20 => ReactionCategory.Dislike,
        <= 40 => ReactionCategory.Suspicious,
        <= 60 => ReactionCategory.Neutral,
        <= 80 => ReactionCategory.Courteous,
        <= 100 => ReactionCategory.Amiable,
        _ => ReactionCategory.Love,
    };

    public static bool IsValid(ReactionNpcFacts? npc) => npc is not null && ReactionLimits.InRange(npc.BaseReaction) &&
        CharacterStatRules.IsDefined(npc.Race);

    public static bool IsValid(ReactionPcFacts? pc) => pc is not null && IsPcRace(pc.Race) &&
        ReactionLimits.InRange(pc.ReactionModifier) && ReactionLimits.InRange(pc.ReputationAdjustment) &&
        IsValid(pc.ReactionEffects) && IsValid(pc.GoodReactionEffects) && IsValid(pc.BadReactionEffects);

    // A divisor of 0 would fault the original; it is refused in both rule modes.
    private static bool IsValid(IReadOnlyList<ReactionModifier>? effects)
    {
        if (effects is null || effects.Count > ReactionLimits.MaxModifiers) return false;
        foreach (var effect in effects)
            if (!CharacterStatRules.IsDefined(effect.Operator) || !ReactionLimits.InRange(effect.Value) ||
                effect.Operator == StatModifierOperator.Divide && effect.Value == 0) return false;
        return true;
    }

    /// <summary>
    /// The effective reaction (R1): 0 when the PC is on the NPC's enemy list; otherwise the stored value, plus the race term
    /// and the PC's reaction modifier (both skipped for an aloof or monstrous NPC; floored at −49 together in the hostility
    /// variant), plus the reputation sum, then the PC's <c>reaction</c> effects (the retail executable passes the PC to the
    /// effect lookup, ledger R1, so both rule modes use the PC), and finally at least 50 when this PC
    /// mind-controls the NPC. There is no clamp: values below 0 and above 100 occur. Invalid facts throw.
    /// </summary>
    [OriginalFunction(0x4C0D00, OriginalFunctionScope.Full)]
    public static int Effective(int stored, ReactionNpcFacts npc, ReactionPcFacts pc, ReactionQuery query = ReactionQuery.Standard)
    {
        if (!IsValid(npc)) throw new ArgumentException("Invalid NPC reaction facts.", nameof(npc));
        if (!IsValid(pc)) throw new ArgumentException("Invalid PC reaction facts.", nameof(pc));
        if (!ReactionLimits.InRange(stored)) throw new ArgumentOutOfRangeException(nameof(stored));
        if (!Enum.IsDefined(query)) throw new ArgumentOutOfRangeException(nameof(query));
        if (npc.PcOnEnemyList) return 0;
        long term = 0;
        if (!npc.Aloof && !npc.Monstrous)
        {
            term = RaceReaction(npc.Race, pc.Race) + (long)pc.ReactionModifier;
            if (query == ReactionQuery.Hostility && term < HostilityTermFloor) term = HostilityTermFloor;
        }
        var value = ApplyEffects(stored + term + pc.ReputationAdjustment, pc.ReactionEffects);
        if (value < Neutral && npc.MindControlledByPc) value = Neutral;
        return value;
    }

    /// <summary>
    /// A reaction change by <paramref name="delta"/> (R2). Nothing happens for a delta of 0, while this PC mind-controls the
    /// NPC, or for a negative delta while the PC has extraordinary charisma and leads the NPC (<paramref name="ledByPc"/>:
    /// the NPC follows or waits for this PC). Otherwise the delta passes through the PC's bad (negative) or good (positive)
    /// reaction effects and is added to the stored value; a negative change of a follower of this PC also asks for the
    /// leader check. Invalid facts or a delta outside <see cref="ReactionLimits.MaxMagnitude"/> throw.
    /// </summary>
    [OriginalFunction(0x4C0DE0, OriginalFunctionScope.Full)]
    public static ReactionAdjustment Adjust(int delta, ReactionNpcFacts npc, ReactionPcFacts pc, bool ledByPc)
    {
        if (!IsValid(npc)) throw new ArgumentException("Invalid NPC reaction facts.", nameof(npc));
        if (!IsValid(pc)) throw new ArgumentException("Invalid PC reaction facts.", nameof(pc));
        if (!ReactionLimits.InRange(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
        if (delta == 0) return new(ReactionSkip.ZeroDelta, 0, false);
        if (npc.MindControlledByPc) return new(ReactionSkip.MindControlled, 0, false);
        if (delta < 0 && pc.ExtraordinaryCharisma && ledByPc) return new(ReactionSkip.LeaderCharisma, 0, false);
        var adjusted = ApplyEffects(delta, delta < 0 ? pc.BadReactionEffects : pc.GoodReactionEffects);
        return new(ReactionSkip.None, adjusted, delta < 0 && ledByPc);
    }

    /// <summary>"Set the reaction to <paramref name="target"/>" (dialog <c>RE v</c>, script set-to): the delta
    /// <c>target − effective</c>, which then goes through <see cref="Adjust"/>, so PC reaction effects can still change it.</summary>
    public static long DeltaTo(int target, int effective) => (long)target - effective;

    /// <summary>The stored value a conversation starts from: the stored value, or 100 when it is below 100 and the PC spent its
    /// "force good reaction" fate point (R5).</summary>
    [OriginalFunction(0x4C1020, OriginalFunctionScope.Partial)]
    public static int ConversationStart(int stored, bool forceGoodReaction) =>
        forceGoodReaction && stored < ForcedGoodReaction ? ForcedGoodReaction : stored;

    /// <summary>
    /// The reaction effect of a PC's hit on an NPC (R8, 0x4A9650). An NPC that follows the attacker
    /// (<paramref name="ledByPc"/>) gets parameter 9 added on a deliberate hit in the Original setting and subtracted in
    /// Steamwoven (A2), nothing on an accidental one, and must re-run its follow check either way. Any other NPC loses 10 on
    /// an accidental hit, and on a deliberate one is lowered to parameter 10 when its hostility-variant reaction
    /// (<paramref name="stored"/> under the facts) is above it. The deltas still go through <see cref="Adjust"/>.
    /// </summary>
    [OriginalFunction(0x4A9650, OriginalFunctionScope.Partial)]
    public static ReactionHit PcHit(int stored, ReactionNpcFacts npc, ReactionPcFacts pc, ReactionHitFacts ai, bool ledByPc,
        bool deliberate, ReactionRuleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!ReactionLimits.InRange(ai.FollowerHitAdjustment) || !ReactionLimits.InRange(ai.HostileReaction))
            throw new ArgumentOutOfRangeException(nameof(ai));
        if (ledByPc)
        {
            int? change = deliberate ? (profile.FollowerHitRaisesReaction ? ai.FollowerHitAdjustment : -ai.FollowerHitAdjustment) : null;
            return new(change, true);
        }
        if (!deliberate) return new(AccidentalHit, false);
        var hostile = Effective(stored, npc, pc, ReactionQuery.Hostility);
        return hostile > ai.HostileReaction ? new(checked((int)DeltaTo(ai.HostileReaction, hostile)), false) : new(null, false);
    }

    /// <summary>Effect aggregation in the source order (<see cref="EffectModifiers.Apply"/>, ARC-WORLD-029): percent, multiply,
    /// divide, add, minimum, maximum, with −1 as the bounds' "unset" mark.</summary>
    public static int ApplyEffects(long value, IReadOnlyList<ReactionModifier> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        return EffectModifiers.Apply(value, effects.Select(e => new EffectModifier(e.Operator, e.Value)).ToArray());
    }
}
