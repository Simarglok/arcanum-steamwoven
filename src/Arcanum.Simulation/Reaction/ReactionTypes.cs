using Arcanum.Simulation.Character;

namespace Arcanum.Simulation.Reaction;

/// <summary>
/// The seven reaction categories in the source's order (love first). Values are Steamwoven identifiers; the source numbers
/// them love = 0 … hatred = 6 in the same order, and its display names are <c>mes\reaction.mes</c> entries 0..6.
/// </summary>
public enum ReactionCategory { Love, Amiable, Courteous, Neutral, Suspicious, Dislike, Hatred }

/// <summary>Which effective reaction a caller reads: the ordinary one, or the AI hostility variant whose race and beauty term
/// is floored at −49 (the source uses it where an NPC decides to fight the PC).</summary>
public enum ReactionQuery { Standard, Hostility }

/// <summary>
/// One change of a reaction effect: the PC's <c>reaction</c>, <c>goodreactionadj</c> or <c>badreactionadj</c> effects. Aggregated like stat effects: percent, multiply, divide, add, minimum, maximum.
/// </summary>
public readonly record struct ReactionModifier(StatModifierOperator Operator, int Value);

/// <summary>Magnitude and count bounds of reaction inputs and stored values (project policy; the source has none).</summary>
public static class ReactionLimits
{
    /// <summary>Largest magnitude of a stored or base reaction, a delta, a target, a reaction modifier, a reputation sum
    /// and an effect value. AI rows reach about ±1000; the bound keeps every sum far inside 32 bits.</summary>
    public const int MaxMagnitude = 1_000_000;

    /// <summary>Most effect changes in one list.</summary>
    public const int MaxModifiers = 64;

    public static bool InRange(long value) => value is >= -MaxMagnitude and <= MaxMagnitude;
}

/// <summary>
/// The NPC's side of a reaction query, supplied by its owner: the reaction base (instance field, prototype default 50), its
/// race, the aloof flag and whether it is monstrous (both skip the race and beauty term), whether this PC is on its enemy list, and whether this PC mind-controls it. Enemy list and mind control belong to the AI
/// and magic domains, which are not built yet, so their owners state them here.
/// </summary>
public sealed record ReactionNpcFacts(
    int BaseReaction,
    CharacterRace Race,
    bool Aloof,
    bool Monstrous,
    bool PcOnEnemyList = false,
    bool MindControlledByPc = false);

/// <summary>
/// The PC's side: its race (one of the eight PC races, Human..HalfOgre), its effective reaction modifier stat (beauty,
/// <see cref="CharacterStat.ReactionModifier"/>), the reputation sum for this NPC (the reputation owner computes it), its
/// <c>reaction</c> effects (the retail executable applies the PC's, not the NPC's as CE reads it), its good and bad
/// reaction-adjustment effects, and whether its charisma is extraordinary (20 or more).
/// </summary>
public sealed record ReactionPcFacts(
    CharacterRace Race,
    int ReactionModifier,
    int ReputationAdjustment,
    IReadOnlyList<ReactionModifier> ReactionEffects,
    IReadOnlyList<ReactionModifier> GoodReactionEffects,
    IReadOnlyList<ReactionModifier> BadReactionEffects,
    bool ExtraordinaryCharisma);

/// <summary>Why a reaction change did nothing; <see cref="None"/> when it applies.</summary>
public enum ReactionSkip
{
    None,
    /// <summary>A delta of 0.</summary>
    ZeroDelta,
    /// <summary>This PC mind-controls the NPC: its reaction does not change.</summary>
    MindControlled,
    /// <summary>A negative change while the PC has extraordinary charisma and leads the NPC.</summary>
    LeaderCharisma,
}

/// <summary>
/// The outcome of one reaction change. <see cref="Delta"/> is what is added to the stored value after the PC's good or bad
/// reaction effects (it may be 0 and still applies: the source writes the stored value, which makes the NPC remember the PC).
/// <see cref="LeaderCheck"/> is the source's "check leader" mark: a negative change while the NPC follows this PC asks the
/// follower loyalty pass to re-run the follow check.
/// </summary>
public readonly record struct ReactionAdjustment(ReactionSkip Skip, int Delta, bool LeaderCheck)
{
    public bool Applies => Skip == ReactionSkip.None;
}

/// <summary>The NPC's AI parameter row fields a PC's hit reads: parameter 9 (follower hit adjustment) and parameter 10
/// (the reaction a deliberate hit lowers a non-follower to; also the kill-on-sight reaction).</summary>
public readonly record struct ReactionHitFacts(int FollowerHitAdjustment, int HostileReaction);

/// <summary>
/// What a PC's hit on an NPC does to the NPC's reaction: the delta to apply through <see cref="ReactionRules.Adjust"/> (null:
/// none), and whether the NPC, which follows the attacker, must re-run its follow check (it may leave and be jilted).
/// </summary>
public readonly record struct ReactionHit(int? Delta, bool FollowCheck);
