namespace Arcanum.Simulation.Party;

/// <summary>
/// A follower's loyalty marks (ARC-RES-064 OL17): <see cref="CheckLeader"/> asks the next loyalty step to test the leader, and
/// the three test bits say what it tests. Values are Steamwoven identifiers, not the source's flag bits.
/// </summary>
[Flags]
public enum CompanionLoyalty
{
    None = 0,
    /// <summary>The source's check-leader NPC flag.</summary>
    CheckLeader = 1,
    /// <summary>Armed by a negative reaction change by the PC leader: test the reaction.</summary>
    TestReaction = 2,
    /// <summary>Armed by a rise of the PC's alignment: test whether the leader became too good.</summary>
    TestTooGood = 4,
    /// <summary>Armed by a fall of the PC's alignment: test whether the leader became too bad.</summary>
    TestTooBad = 8,
    All = CheckLeader | TestReaction | TestTooGood | TestTooBad,
}

/// <summary>The check-leader result (OL21): the warning a follower floats, or none. It never makes the follower leave.</summary>
public enum LeaderWarning { None = 0, TooGood = 1, TooBad = 2, LowReaction = 3 }

/// <summary>What a loyalty step did (OL18, OL20).</summary>
public enum LoyaltyStepKind
{
    /// <summary>The follower has a running action-class goal: no step (OL18).</summary>
    GoalRunning,
    /// <summary>The follower stays: the follow check passed, or it refused and the leave was refused.</summary>
    Stayed,
    /// <summary>The follow check refused and the unforced leave applied: the follower left, jilted.</summary>
    Left,
}

/// <summary>A first and last line number of a message range the host picks a line from (no game text in Steamwoven).</summary>
public readonly record struct LoyaltyLines(int First, int Last);

/// <summary>
/// Inputs of the check-leader test (OL21), all effective values supplied by the caller. <paramref name="Armed"/> are the
/// follower's marks when the step began; <paramref name="MindControlledByLeader"/> is the caller's resolution of the source's
/// mind-control exit (controlled by this leader, or by an unresolvable controller while led by a PC);
/// <paramref name="Thresholds"/> are the NPC's AI follow fields 5..7 (the level gate is not read).
/// </summary>
public sealed record LeaderCheckInput(
    CompanionLoyalty Armed,
    bool MindControlledByLeader,
    PersuasionTraining LeaderPersuasion,
    bool ForcedFollower,
    int Reaction,
    FollowThresholds Thresholds,
    int LeaderAlignment,
    int NpcAlignment);

/// <summary>
/// One loyalty step's outcome. <see cref="Consent"/> is the follow check's result (null when no step ran);
/// <see cref="LeaveRefused"/> is true when the check refused but the unforced leave did not apply; <see cref="Loyalty"/> is the
/// follower's marks after the step.
/// </summary>
public readonly record struct LoyaltyStepResult(LoyaltyStepKind Kind, FollowConsent? Consent, bool LeaveRefused, LeaderWarning Warning,
    CompanionLoyalty Loyalty)
{
    /// <summary>The refusal line range the host shows for a follower that left (OL20), else null.</summary>
    public LoyaltyLines? RefusalLines => Kind == LoyaltyStepKind.Left && Consent is { } reason ? CompanionLoyaltyRules.RefusalLines(reason) : null;
    /// <summary>The warning line range of <see cref="Warning"/> (OL21), else null.</summary>
    public LoyaltyLines? WarningLines => CompanionLoyaltyRules.WarningLines(Warning);
}

/// <summary>
/// The original's wait timer and follower loyalty (ARC-RES-064 rows OL17–OL26, ARC-WORLD-045) as pure functions over the caller's
/// effective values. The session owns the state (<c>GameSessionOwner.WaitParty</c>, <c>RunLoyaltyStep</c>); the host owns the
/// lines, the goals and the AI tick.
/// </summary>
public static class CompanionLoyaltyRules
{
    /// <summary>The wait field's durations in game milliseconds (table 0x5B5088): 0, 15 min, 2 h, 1 day, 7 days, 30 days.</summary>
    public static IReadOnlyList<long> WaitDurationsMs { get; } = Array.AsReadOnly(new long[]
        { 0, 15 * 60_000L, 2 * 3_600_000L, 24 * 3_600_000L, 7 * 24 * 3_600_000L, 30 * 24 * 3_600_000L });

    /// <summary>The charisma maximum the wait timeout's reaction change counts from (every race's, 0x4B0FD0).</summary>
    public const int MaxCharisma = 20;

    /// <summary>
    /// The wait timer of a follower that starts waiting (OL23): none while the leader's charisma is extraordinary or the NPC is
    /// mind controlled; otherwise the NPC's wait index, one step higher when the leader has any Persuasion training, picks the
    /// duration of <see cref="WaitDurationsMs"/>, and an index of 6 or more has none. A negative index has none either, where
    /// the original reads before its table. A duration of 0 is still an event: the queue stores it as 1 ms.
    /// </summary>
    [OriginalFunction(0x4AA7A0, OriginalFunctionScope.Partial)]
    public static long? WaitTimerMs(int waitIndex, PersuasionTraining leaderPersuasion, int leaderCharisma, bool mindControlled)
    {
        if (!Enum.IsDefined(leaderPersuasion)) throw new ArgumentOutOfRangeException(nameof(leaderPersuasion));
        if (leaderCharisma >= FollowConsentRules.ExtraordinaryCharisma || mindControlled) return null;
        var index = (long)waitIndex + (leaderPersuasion == PersuasionTraining.None ? 0 : 1);
        return index >= 0 && index < WaitDurationsMs.Count ? WaitDurationsMs[(int)index] : null;
    }

    /// <summary>The reaction change of a wait timeout (OL24): twice the leader's effective charisma below
    /// <see cref="MaxCharisma"/>, through the ordinary reaction change rules (0 does nothing). In 64 bits, so any charisma has
    /// its value; the caller refuses one beyond the reaction bounds.</summary>
    public static long ExpiryReactionDelta(int leaderCharisma) => 2 * ((long)leaderCharisma - MaxCharisma);

    /// <summary>The marks a negative reaction change by the NPC's PC leader adds (OL17).</summary>
    public static CompanionLoyalty ArmReaction(CompanionLoyalty loyalty) => Valid(loyalty) | CompanionLoyalty.CheckLeader | CompanionLoyalty.TestReaction;

    /// <summary>The marks a change of the PC's alignment adds to a follower in its array (OL17); no change adds none.</summary>
    public static CompanionLoyalty ArmAlignment(CompanionLoyalty loyalty, int previous, int current) => previous == current
        ? Valid(loyalty)
        : Valid(loyalty) | CompanionLoyalty.CheckLeader | (current > previous ? CompanionLoyalty.TestTooGood : CompanionLoyalty.TestTooBad);

    /// <summary>
    /// The check-leader test (OL21): no warning when the NPC is mind controlled by the leader, the leader has Persuasion Master or
    /// the NPC is a forced follower; otherwise a low reaction (reaction bit, reaction at most field 5 + 20), else by which
    /// alignment is higher a too-good (bit, gap above field 6 − 100) or too-bad (bit, gap above field 7 − 100) warning. As the
    /// executable, in 32-bit arithmetic that wraps. The test clears the three bits; <see cref="Step"/> drops them.
    /// </summary>
    [OriginalFunction(0x4ADB50, OriginalFunctionScope.Partial)]
    public static LeaderWarning CheckLeader(LeaderCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Valid(input.Armed);
        if (!Enum.IsDefined(input.LeaderPersuasion)) throw new ArgumentOutOfRangeException(nameof(input), "LeaderPersuasion");
        if (input.MindControlledByLeader || input.LeaderPersuasion == PersuasionTraining.Master || input.ForcedFollower) return LeaderWarning.None;
        var t = input.Thresholds;
        if (input.Armed.HasFlag(CompanionLoyalty.TestReaction) && input.Reaction <= unchecked(t.DislikeReaction + 20)) return LeaderWarning.LowReaction;
        if (input.LeaderAlignment > input.NpcAlignment)
        {
            if (input.Armed.HasFlag(CompanionLoyalty.TestTooGood) &&
                unchecked(input.LeaderAlignment - input.NpcAlignment) > unchecked(t.MaxAlignmentAbove - 100)) return LeaderWarning.TooGood;
        }
        else if (input.Armed.HasFlag(CompanionLoyalty.TestTooBad) &&
            unchecked(input.NpcAlignment - input.LeaderAlignment) > unchecked(t.MaxAlignmentBelow - 100)) return LeaderWarning.TooBad;
        return LeaderWarning.None;
    }

    /// <summary>
    /// The loyalty step of a follower's non-combat AI pass (OL18, OL20, OL21), after the host's waiting test and follow request:
    /// nothing while an action-class goal runs (<paramref name="actionGoalRunning"/>, the host's answer); a refused follow check
    /// (<paramref name="consent"/>, run with the charisma limit ignored) makes the follower leave unforced and jilted when
    /// <paramref name="mayLeaveUnforced"/>; otherwise (passed, or the leave refused) an armed follower clears its marks and runs
    /// <see cref="CheckLeader"/>. No reaction change.
    /// </summary>
    [OriginalFunction(0x4AC380, OriginalFunctionScope.Partial)]
    public static LoyaltyStepResult Step(bool actionGoalRunning, FollowConsent consent, bool mayLeaveUnforced, LeaderCheckInput check)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!Enum.IsDefined(consent)) throw new ArgumentOutOfRangeException(nameof(consent));
        var armed = Valid(check.Armed);
        if (actionGoalRunning) return new(LoyaltyStepKind.GoalRunning, null, false, LeaderWarning.None, armed);
        var refused = consent != FollowConsent.Accepted;
        if (refused && mayLeaveUnforced) return new(LoyaltyStepKind.Left, consent, false, LeaderWarning.None, armed);
        if (!armed.HasFlag(CompanionLoyalty.CheckLeader)) return new(LoyaltyStepKind.Stayed, consent, refused, LeaderWarning.None, armed);
        return new(LoyaltyStepKind.Stayed, consent, refused, CheckLeader(check), CompanionLoyalty.None);
    }

    /// <summary>The refusal line range of a follower that left (OL20): too good 1600, too evil 1700, dislike 1800, already
    /// grouped 2600, limit 2700, not allowed 2800, low level 2900 (100 lines each); none for an accepted check.</summary>
    public static LoyaltyLines? RefusalLines(FollowConsent reason) => reason switch
    {
        FollowConsent.TooGood => Hundred(1600),
        FollowConsent.TooBad => Hundred(1700),
        FollowConsent.Dislike => Hundred(1800),
        FollowConsent.AlreadyInGroup => Hundred(2600),
        FollowConsent.LimitReached => Hundred(2700),
        FollowConsent.NotAllowed => Hundred(2800),
        FollowConsent.LowLevel => Hundred(2900),
        FollowConsent.Accepted => null,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>The warning line range (OL21): too good 5200, too evil 5300, low reaction 5400 (100 lines each); none for none.</summary>
    public static LoyaltyLines? WarningLines(LeaderWarning warning) => warning switch
    {
        LeaderWarning.TooGood => Hundred(5200),
        LeaderWarning.TooBad => Hundred(5300),
        LeaderWarning.LowReaction => Hundred(5400),
        LeaderWarning.None => null,
        _ => throw new ArgumentOutOfRangeException(nameof(warning)),
    };

    /// <summary>Whether <paramref name="loyalty"/> holds only defined marks.</summary>
    public static bool IsValid(CompanionLoyalty loyalty) => (loyalty & ~CompanionLoyalty.All) == 0;

    private static CompanionLoyalty Valid(CompanionLoyalty loyalty) =>
        IsValid(loyalty) ? loyalty : throw new ArgumentOutOfRangeException(nameof(loyalty));

    private static LoyaltyLines Hundred(int first) => new(first, first + 99);
}
