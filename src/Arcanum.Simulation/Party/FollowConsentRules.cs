namespace Arcanum.Simulation.Party;

/// <summary>
/// Result of the follow check: <see cref="Accepted"/> or one of the seven source refusal reasons, in the source's
/// check order. Values are Steamwoven identifiers, not source result codes.
/// </summary>
public enum FollowConsent
{
    Accepted,
    /// <summary>The NPC already has another leader whose charisma is at least the PC's.</summary>
    AlreadyInGroup,
    /// <summary>The NPC's reaction to the PC is at or below the AI row's dislike threshold.</summary>
    Dislike,
    /// <summary>The PC's alignment is above the NPC's by more than the AI row allows.</summary>
    TooGood,
    /// <summary>The PC's alignment is below the NPC's by more than the AI row allows.</summary>
    TooBad,
    /// <summary>The NPC is at least the AI row's level gate above the PC.</summary>
    LowLevel,
    /// <summary>The PC may lead no followers. Unreachable with a source-clamped max followers (at least 1).</summary>
    NotAllowed,
    /// <summary>The PC already leads at least max followers non-forced followers.</summary>
    LimitReached,
}

/// <summary>Persuasion training of the PC; only Expert (+1 follower) and Master (always accepted) matter here.</summary>
public enum PersuasionTraining { None, Apprentice, Expert, Master }

/// <summary>
/// Follow fields 5..8 of the NPC's AI parameter row: refuse at reaction ≤ <paramref name="DislikeReaction"/>,
/// when the PC's alignment is above the NPC's by more than <paramref name="MaxAlignmentAbove"/> or below it by more
/// than <paramref name="MaxAlignmentBelow"/>, and when the NPC's level is at least the PC's plus <paramref name="LevelGate"/>.
/// </summary>
public readonly record struct FollowThresholds(int DislikeReaction, int MaxAlignmentAbove, int MaxAlignmentBelow, int LevelGate);

/// <summary>
/// Explicit inputs of one follow check, all effective values supplied by the caller. <paramref name="MindControlledByPc"/>
/// is the caller's resolution of the source's mind-control branch (controlled by this PC, or by an unresolvable
/// controller while led by a PC). <paramref name="OtherLeaderCharisma"/> is the charisma of the NPC's current
/// leader when that leader is not this PC, else null. <paramref name="MaxFollowers"/> is the PC's effective max
/// followers stat (see <see cref="FollowConsentRules.MaxFollowers"/>). <paramref name="NonForcedFollowers"/> counts
/// the PC's non-forced followers in its follower array (waiting followers are outside it).
/// <paramref name="IgnoreCharismaLimit"/> skips the follower-limit steps (the first operand of JO/UW).
/// </summary>
public sealed record FollowConsentInput(
    bool MindControlledByPc,
    PersuasionTraining PcPersuasion,
    int PcCharisma,
    int PcAlignment,
    int PcLevel,
    int? OtherLeaderCharisma,
    bool ForcedFollower,
    int Reaction,
    FollowThresholds Thresholds,
    int NpcAlignment,
    int NpcLevel,
    int MaxFollowers,
    int NonForcedFollowers,
    bool IgnoreCharismaLimit);

/// <summary>
/// The source follow check (ARC-RES-024 C2–C4) as a pure function. Numbers and order are source claims read in CE at
/// <see cref="SourceRevision"/>; original executable parity is NOT_VERIFIED.
/// </summary>
public static class FollowConsentRules
{
    public const string SourceRevision = "a7ff41b300ef712f0e7d088183a3d08957110cdd";
    /// <summary>Charisma from which an unforced leave is refused (the source's extraordinary threshold).</summary>
    public const int ExtraordinaryCharisma = 20;
    public const int MinMaxFollowers = 1;
    public const int MaxMaxFollowers = 7;

    /// <summary>
    /// Max followers without stat effects: charisma / 4 (integer), +1 at Persuasion Expert or above, clamped to 1..7.
    /// A caller with effects on the stat applies them before the clamp and passes its own value.
    /// </summary>
    public static int MaxFollowers(int charisma, PersuasionTraining persuasion)
    {
        if (charisma < 0) throw new ArgumentOutOfRangeException(nameof(charisma));
        if (!Enum.IsDefined(persuasion)) throw new ArgumentOutOfRangeException(nameof(persuasion));
        var value = charisma / 4 + (persuasion >= PersuasionTraining.Expert ? 1 : 0);
        return Math.Clamp(value, MinMaxFollowers, MaxMaxFollowers);
    }

    /// <summary>First matching step wins: mind control, Persuasion Master, other leader, forced, reaction, alignment, level, limit.</summary>
    public static FollowConsent Evaluate(FollowConsentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Enum.IsDefined(input.PcPersuasion)) throw new ArgumentOutOfRangeException(nameof(input), "PcPersuasion");
        if (input.NonForcedFollowers < 0) throw new ArgumentOutOfRangeException(nameof(input), "NonForcedFollowers");

        if (input.MindControlledByPc) return FollowConsent.Accepted;
        if (input.PcPersuasion == PersuasionTraining.Master) return FollowConsent.Accepted;
        if (input.OtherLeaderCharisma is { } leader && leader >= input.PcCharisma) return FollowConsent.AlreadyInGroup;
        if (input.ForcedFollower) return FollowConsent.Accepted;

        var t = input.Thresholds;
        if (input.Reaction <= t.DislikeReaction) return FollowConsent.Dislike;
        // Widened so extreme caller values cannot overflow.
        var gap = (long)input.PcAlignment - input.NpcAlignment;
        if (gap > t.MaxAlignmentAbove) return FollowConsent.TooGood;
        if (-gap > t.MaxAlignmentBelow) return FollowConsent.TooBad;
        if (input.NpcLevel >= (long)input.PcLevel + t.LevelGate) return FollowConsent.LowLevel;

        if (!input.IgnoreCharismaLimit)
        {
            if (input.MaxFollowers <= 0) return FollowConsent.NotAllowed;
            if (input.NonForcedFollowers >= input.MaxFollowers) return FollowConsent.LimitReached;
        }
        return FollowConsent.Accepted;
    }

    /// <summary>
    /// Whether an unforced leave is allowed (ARC-RES-024 C6): refused while the leader's charisma is extraordinary
    /// or the NPC is mind controlled. A forced leave is always allowed.
    /// </summary>
    public static bool MayLeaveUnforced(int leaderCharisma, bool mindControlled) =>
        leaderCharisma < ExtraordinaryCharisma && !mindControlled;
}
