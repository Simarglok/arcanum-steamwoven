using Arcanum.Simulation.Party;

namespace Arcanum.Simulation.Ai;

/// <summary>Which side of an attack event a leader's followers are told about (0x4A9B80): the attacker's (the other is the victim)
/// or the victim's (the other is the attacker).</summary>
public enum FollowerSide
{
    Attacker,
    Victim,
}

/// <summary>Which case of the follower rule (0x4A9C00) applied, in the original's order; each names what the caller does.</summary>
public enum FollowerRuleStep
{
    /// <summary>Case 1: the leader is the other side: nothing.</summary>
    LeaderIsTarget,
    /// <summary>Case 2: a non-critter target of an accidental attack: nothing.</summary>
    AccidentalObject,
    /// <summary>Case 2 or 5, ordered: the target lock on the target (0x4AFBD0).</summary>
    Lock,
    /// <summary>Case 2: a non-critter target: the setter asked for COMBAT_FOCUS on it (the follower bashes it).</summary>
    Focus,
    /// <summary>Case 3: the follower is the target or dead: nothing.</summary>
    SelfOrDead,
    /// <summary>Case 4 without a complaint: upset, but the victim's side, a target in combat mode or an accidental hit.</summary>
    UpsetIgnored,
    /// <summary>Case 4: upset on the attacker's side: the "upset" line for <see cref="FollowerRuleOutcome.Upset"/> when the follower
    /// is active, then its reaction toward the leader changed by <see cref="FollowerRules.ComplaintReaction"/>.</summary>
    Complain,
    /// <summary>Case 5, accidental: nothing.</summary>
    Accidental,
    /// <summary>Case 5: the follower's reaction to an attack by the target (0x4AA620, <see cref="HostilityRules.ReactToAttack"/>).</summary>
    ReactToAttack,
}

/// <summary>
/// The follower rule's answer for one follower: the case (<see cref="Step"/>), the upset reason of case 4 (None otherwise) and,
/// for a complaint, whether the follower is active (0x45D790) and so floats the line; the reaction change applies either way.
/// </summary>
public readonly record struct FollowerRuleOutcome(string Follower, FollowerRuleStep Step, UpsetReason Upset = UpsetReason.None, bool Speaks = false)
{
    /// <summary>The upset line's message range of a complaint that speaks (0x414370), else null.</summary>
    public LoyaltyLines? Line => Step == FollowerRuleStep.Complain && Speaks ? FollowerRules.UpsetLines(Upset) : null;
}

/// <summary>Which follower comment the end of an attack asks for (0x4B6B90 → 0x4AE9E0): kind 0, a critical hit on the aimed object;
/// kind 1, a hit that struck another object under attack-record flag 0x8.</summary>
public enum TauntKind
{
    Critical,
    Stray,
}

/// <summary>
/// The end of one resolved attack as the combat taunt reads it: the "combat taunts" setting (single player), whether the attacker
/// is the PC, whether an object was struck and is in the PC's own party (its PC leader is the attacker), the hit (attack-record flag
/// 0x2), the critical (0x4), whether the struck object is the aimed one, and record flag 0x8 (written only by the critical-miss turn 0x4B6410, ARC-AI-1639).
/// </summary>
public sealed record TauntFacts
{
    public bool TauntsOn { get; init; }
    public bool AttackerIsPc { get; init; }
    public bool Struck { get; init; }
    public bool StruckInPcParty { get; init; }
    public bool Hit { get; init; }
    public bool Critical { get; init; }
    public bool StruckIsAimed { get; init; }
    public bool StrayFlag { get; init; }
}

/// <summary>A follower comment: the speaker (the drawn follower, or the hurt one) toward its PC, and the message range.</summary>
public readonly record struct FollowerComment(string Speaker, LoyaltyLines Lines);
