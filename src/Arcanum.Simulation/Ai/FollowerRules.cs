using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Party;

namespace Arcanum.Simulation.Ai;

/// <summary>
/// Followers in combat (stage A7, research ARC-RES-067; slice I9, ARC-WORLD-091, re-read in the retail 1.0.7.4 executable): the
/// follower rule and the tell-followers walk of the attack event, the attack order's party filter and the two follower comment
/// lines (combat taunt, near death), as pure functions over an <see cref="IHostilityWorld"/> and an <see cref="IAiRollSource"/>.
/// The rules return what to do; the lock, the focus request and the complaint's reaction change are the session's
/// (<c>GameSessionOwner.ApplyFollowerRule</c>), the reaction to an attack is the caller's with its should-flee.
/// </summary>
public static class FollowerRules
{
    /// <summary>A complaint changes the follower's reaction toward its leader by this amount (through the reaction change rules,
    /// R2: nothing for an NPC leader).</summary>
    public const int ComplaintReaction = -5;

    /// <summary>The near-death line: a hit takes a follower of a PC from at least this hit-point percent to below it.</summary>
    public const int NearDeathPercent = 20;

    /// <summary>
    /// The follower rule (0x4A9C00) for <paramref name="follower"/> of <paramref name="leader"/> when <paramref name="target"/> is
    /// the other side of an attack on <paramref name="side"/>, first match: (1) the leader is the target → nothing; (2) a target that
    /// is no PC or NPC → nothing when accidental, the lock when ordered, else COMBAT_FOCUS on it; (3) the follower is the target or
    /// dead → nothing; (4) upset attacking (follower, target, leader) is not NONE → nothing on the victim's side, for a target in
    /// combat mode or an accidental hit, else the complaint (the line when the follower is active; FO10: the line output always
    /// exists, so the −5 always comes with it); (5) → nothing when accidental, the lock when ordered, else the reaction to an attack
    /// by the target. The world must know the follower and the target.
    /// </summary>
    [OriginalFunction(0x4A9C00, OriginalFunctionScope.Full)]
    public static FollowerRuleOutcome Rule(IHostilityWorld world, string follower, string leader, string target, FollowerSide side,
        bool accidental, bool ordered, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(side)) throw new ArgumentOutOfRangeException(nameof(side));
        var f = Get(world, follower);
        var t = Get(world, target);
        if (leader == t.Id) return new(f.Id, FollowerRuleStep.LeaderIsTarget);
        if (!t.IsCritter)
            return new(f.Id, accidental ? FollowerRuleStep.AccidentalObject : ordered ? FollowerRuleStep.Lock : FollowerRuleStep.Focus);
        if (f.Id == t.Id || f.Dead) return new(f.Id, FollowerRuleStep.SelfOrDead);
        var upset = HostilityRules.Upset(world, f.Id, t.Id, leader, profile);
        if (upset != UpsetReason.None)
        {
            if (side == FollowerSide.Victim || t.InCombatMode || accidental) return new(f.Id, FollowerRuleStep.UpsetIgnored, upset);
            return new(f.Id, FollowerRuleStep.Complain, upset, Active(f));
        }
        return new(f.Id, accidental ? FollowerRuleStep.Accidental : ordered ? FollowerRuleStep.Lock : FollowerRuleStep.ReactToAttack);
    }

    /// <summary>
    /// Tell followers (0x4A9B80): nothing when <paramref name="leader"/> is <paramref name="other"/>; otherwise the follower rule
    /// (never ordered) for each of <paramref name="followers"/> — the leader's direct followers in its follower array's order (not
    /// followers of followers, not waiting NPCs; the caller's list) — in that order.
    /// </summary>
    [OriginalFunction(0x4A9B80, OriginalFunctionScope.Full)]
    public static IReadOnlyList<FollowerRuleOutcome> TellFollowers(IHostilityWorld world, string leader, string other,
        IReadOnlyList<string> followers, FollowerSide side, bool accidental, CombatAnomalyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(followers);
        if (leader == other) return [];
        return followers.Select(follower => Rule(world, follower, leader, other, side, accidental, false, profile)).ToArray().AsReadOnly();
    }

    /// <summary>
    /// The attack order's party filter (broadcast type 10, 0x4C377D): outside the PC's combat mode a target whose PC leader is the PC
    /// (one of its followers, the ordered one included) is refused; a PC target is not (its PC leader is none).
    /// </summary>
    public static bool AttackOrderRefused(bool pcInCombatMode, bool targetLedByPc) => !pcInCombatMode && targetLedByPc;

    /// <summary>
    /// The upset line's message range (0x414370, read by this task): GOOD 4900–4999, ORIGIN 5000–5099, FACTION 5100–5199, PARTY
    /// 4800–4899, in the generated NPC line files; NONE has no line.
    /// </summary>
    [OriginalFunction(0x414370, OriginalFunctionScope.Partial)]
    public static LoyaltyLines? UpsetLines(UpsetReason reason) => reason switch
    {
        UpsetReason.None => null,
        UpsetReason.Good => new(4900, 4999),
        UpsetReason.Origin => new(5000, 5099),
        UpsetReason.Faction => new(5100, 5199),
        UpsetReason.Party => new(4800, 4899),
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>The comment kinds' message ranges: a critical hit 2400–2499, a stray hit 2500–2599.</summary>
    public static LoyaltyLines TauntLines(TauntKind kind) => kind switch
    {
        TauntKind.Critical => new(2400, 2499),
        TauntKind.Stray => new(2500, 2599),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The near-death line's message range, 5500–5599.</summary>
    public static LoyaltyLines NearDeathLines { get; } = new(5500, 5599);

    /// <summary>
    /// The taunt's kind for the end of one attack (0x4B6B90): with the setting on, a PC attacker, a struck object outside the PC's
    /// party and a hit, kind 0 for a critical hit on the aimed object, kind 1 for another object under record flag 0x8; else none.
    /// </summary>
    public static TauntKind? TauntKindOf(TauntFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (!facts.TauntsOn || !facts.AttackerIsPc || !facts.Struck || facts.StruckInPcParty || !facts.Hit) return null;
        if (facts.StruckIsAimed) return facts.Critical ? TauntKind.Critical : null;
        return facts.StrayFlag ? TauntKind.Stray : null;
    }

    /// <summary>
    /// The combat taunt (0x4AE9E0) of kind <paramref name="kind"/> among the PC's follower array <paramref name="followers"/> (in
    /// its order): <c>random(1, 2)</c> (<see cref="AiRollPurpose.TauntSpeak"/>) must not be 1; with no follower nothing; one follower
    /// is drawn by <c>random(0, n − 1)</c> (<see cref="AiRollPurpose.TauntFollower"/>, no draw for one) and must be active
    /// (0x45D790). FO17 (fixed in both settings): a null entry, which the original passes to the activity test unchecked, is skipped.
    /// </summary>
    [OriginalFunction(0x4AE9E0, OriginalFunctionScope.Partial)]
    public static FollowerComment? Taunt(TauntKind kind, IReadOnlyList<HostilityCritter?> followers, IAiRollSource rolls)
    {
        ArgumentNullException.ThrowIfNull(followers);
        ArgumentNullException.ThrowIfNull(rolls);
        var lines = TauntLines(kind);
        if (rolls.Roll(AiRollPurpose.TauntSpeak, 1, 2) == 1 || followers.Count == 0) return null;
        var drawn = followers.Count == 1 ? followers[0] : followers[rolls.Roll(AiRollPurpose.TauntFollower, 0, followers.Count - 1)];
        return drawn is not null && Active(drawn) ? new(drawn.Id, lines) : null;
    }

    /// <summary>
    /// The near-death line (0x4AEAB0, from the damage step): a hit with hit-point damage above 0 on a critter that has a PC leader,
    /// whose hit-point percent (0x4AB400) was at least 20 before and is below 20 after, makes it float 5500–5599 toward its PC
    /// leader. FO16 (fixed in both settings): a critter the hit killed floats nothing, where the original floats an empty line.
    /// </summary>
    [OriginalFunction(0x4AEAB0, OriginalFunctionScope.Partial)]
    public static FollowerComment? NearDeath(string critter, bool hasPcLeader, int damage, int percentBefore, int percentAfter, bool deadAfter)
    {
        ArgumentNullException.ThrowIfNull(critter);
        if (!hasPcLeader || damage <= 0 || deadAfter) return null;
        return percentBefore >= NearDeathPercent && percentAfter < NearDeathPercent ? new(critter, NearDeathLines) : null;
    }

    /// <summary>The activity test (0x45D790): not destroyed or off, not dead, unconscious, stunned or paralysed, or stoned.</summary>
    [OriginalFunction(0x45D790, OriginalFunctionScope.Full)]
    public static bool Active(HostilityCritter critter)
    {
        ArgumentNullException.ThrowIfNull(critter);
        return !critter.Destroyed && !critter.Off && !critter.Dead && !critter.Unconscious && !critter.Stunned && !critter.Stoned;
    }

    private static HostilityCritter Get(IHostilityWorld world, string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return world.Find(id) ?? throw new ArgumentException($"The world knows no object '{id}'.", nameof(id));
    }
}
