namespace Arcanum.Simulation.Ai;

/// <summary>The AI parameter slots the morale rules read (the NPC's row, slice I1): 0 flee hit-point percent, 1 flee when
/// outnumbered by, 2 flee when outlevelled by, 3 stand while the target's hit-point percent is below, 15 combat distance
/// (tiles).</summary>
public readonly record struct MoraleParameters(int FleeHpPercent, int FleeOutnumbered, int FleeOutlevelled, int StandWhileTargetWeakPercent,
    int CombatDistance);

/// <summary>
/// What the morale rules ask of the world besides the hostility questions. <see cref="MoraleParameters"/> is the NPC's AI row;
/// <see cref="Followers"/> is a critter's recursive follower list (0x441260): its follower array with each follower's own list
/// appended, followers of followers included, unfiltered. Only the count and the members' levels are read, so the order does not
/// matter; every id it returns must be known to <see cref="IHostilityWorld.Find"/>.
/// </summary>
public interface IMoraleWorld : IHostilityWorld
{
    MoraleParameters MoraleParameters(string npc);
    IReadOnlyList<string> Followers(string id);
}

/// <summary>A party as the flee test counts it (0x4AE020): its size and the sum of each member's own level (stat 17).</summary>
public readonly record struct PartyStrength(int Size, int LevelSum);

/// <summary>Why should-flee (0x4AB2F0) answered as it did: the first test that matched, in the original's order (design rule 4).</summary>
public enum FleeReason
{
    /// <summary>No test matched: fight.</summary>
    Fight,
    /// <summary>1: the NPC has NO_FLEE (critter flag 0x8000000): fight.</summary>
    NoFlee,
    /// <summary>2: the NPC is mind-controlled (spell flag 0x8000000): fight.</summary>
    MindControlled,
    /// <summary>3: the target's hit-point percent is below parameter 3: fight.</summary>
    TargetWeak,
    /// <summary>4: the NPC's own hit-point percent is at most parameter 0: flee.</summary>
    LowHealth,
    /// <summary>5: the target's party size minus its own is at least parameter 1: flee.</summary>
    Outnumbered,
    /// <summary>6: the target's party level sum minus its own is at least parameter 2: flee.</summary>
    Outlevelled,
}

/// <summary>A should-flee answer: the reason and, once test 5 was reached, both parties as counted.</summary>
public readonly record struct FleeDecision(FleeReason Reason, PartyStrength? Own, PartyStrength? Target)
{
    /// <summary>Whether the NPC flees (the setter is asked for FLEE from the target).</summary>
    public bool Flees => Reason is FleeReason.LowHealth or FleeReason.Outnumbered or FleeReason.Outlevelled;
}

/// <summary>The reaction to an attack (<see cref="HostilityRules.ReactToAttack"/>) with the should-flee answer of its weighing,
/// none when it did not weigh.</summary>
public readonly record struct MoraleReaction(AttackReaction Reaction, FleeDecision? Flee);

/// <summary>What the flee-spell step (0x4AAF50) did.</summary>
public enum FleeSpellOutcome
{
    /// <summary>SPELL_FLEE was set: it is cleared and nothing is chosen (the pass after a flee spell or item).</summary>
    ClearsFlag,
    /// <summary>The chooser picked an entry of the flee list for the NPC itself: SPELL_FLEE is set and the entry is the pass's action.</summary>
    Chosen,
    /// <summary>The chooser picked nothing: the pass flees (perform fleeing).</summary>
    NoneChosen,
}

/// <summary>One flee-spell step: the outcome and the danger bits after it.</summary>
public readonly record struct FleeSpellStep(FleeSpellOutcome Outcome, DangerFlags Flags);

/// <summary>Stop fleeing (0x4AF170) for one object: whether the setter is asked for NONE (an NPC in FLEE; SURRENDER is kept) and
/// whether the NPC's flee goals (type 25) are interrupted (the same case: only an NPC that was in FLEE).</summary>
public readonly record struct StopFleeingStep(bool RequestsNone, bool InterruptsFleeGoals);

/// <summary>Where a back-off run (0x4AC7B0) stands before its path request.</summary>
public enum BackOffRunStep
{
    /// <summary>BACKING_OFF is not set: nothing.</summary>
    NotBackingOff,
    /// <summary>The NPC is at least the range from its target: BACKING_OFF is cleared and the pass does nothing more.</summary>
    Reached,
    /// <summary>The NPC asks the "away" path (<see cref="MoraleRules.AwayOffsets"/>) and runs to its end; with no path BACKING_OFF
    /// is cleared (MR15).</summary>
    Runs,
}

/// <summary>A back-off run's step and its range: parameter 15, or 5 when that is at most 1.</summary>
public readonly record struct BackOffRun(BackOffRunStep Step, int Range);

/// <summary>
/// The kill notification (0x4AA1B0) of a victim and its killer as data: the victim's stop fleeing and, for a PC killer, the NPCs
/// that start backing off, in the square query's order. The victim's stop waiting (0x4AA8C0), which follows its stop fleeing
/// for every victim, is the caller's.
/// </summary>
public sealed record KillNotificationPlan(StopFleeingStep VictimStopsFleeing, IReadOnlyList<string> BacksOff);
