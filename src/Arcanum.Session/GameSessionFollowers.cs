using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>
/// The writes of one follower rule answer (ARC-WORLD-091, <see cref="FollowerRules.Rule"/>) for the follower
/// <see cref="FollowerRuleOutcome.Follower"/> of <see cref="Leader"/> against <see cref="Target"/>: the setter's facts of a lock or
/// focus request (<see cref="Setter"/>), the hostility-variant reaction facts when the target is the hero (<see cref="Reaction"/>,
/// setter rule 8) and the follower's reaction facts toward the hero for a complaint (<see cref="Complaint"/>).
/// </summary>
public sealed record SessionFollowerRuleRequest
{
    public required FollowerRuleOutcome Outcome { get; init; }
    public required SessionCombatant Leader { get; init; }
    public required SessionCombatant Target { get; init; }
    public DangerSetterFacts Setter { get; init; } = new();
    public SessionDangerReaction? Reaction { get; init; }
    public SessionPeaceReaction? Complaint { get; init; }
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The follower rule's writes (0x4A9C00), in one revision: <see cref="FollowerRuleStep.Lock"/> runs the target lock
    /// (<see cref="LockDangerTarget"/>), <see cref="FollowerRuleStep.Focus"/> asks the setter for COMBAT_FOCUS on the target (no
    /// sound), <see cref="FollowerRuleStep.Complain"/> changes the follower's reaction toward a hero leader by
    /// <see cref="FollowerRules.ComplaintReaction"/> through the reaction change rules (nothing for an NPC leader, R2; a drop arms the
    /// follower's leader check); the other steps write nothing (the reaction to an attack is the caller's, with its should-flee). The
    /// upset line is the host's (<see cref="FollowerRuleOutcome.Line"/>). A complaint toward the hero without
    /// <see cref="SessionFollowerRuleRequest.Complaint"/>, or a lock or focus on the hero without
    /// <see cref="SessionFollowerRuleRequest.Reaction"/>, is <see cref="GameSessionFailure.InvalidDangerFacts"/>.
    /// </summary>
    public SessionDangerResult ApplyFollowerRule(GameSessionVersion expected, SessionFollowerRuleRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        var follower = request.Outcome.Follower;
        if (!SessionValidation.Entity(follower) || request.Leader is null || request.Target is null || !ValidDangerHandle(request.Leader) ||
            !ValidDangerHandle(request.Target) || !Enum.IsDefined(request.Outcome.Step) || !Enum.IsDefined(request.Outcome.Upset))
            return RejectDanger(GameSessionFailure.InvalidDangerTarget);
        if (request.Reaction is { } reaction && (!ReactionRules.IsValid(reaction.Npc) || !ReactionRules.IsValid(reaction.Pc) ||
                !ReactionLimits.InRange(reaction.HostileReaction)) || request.Complaint is { } complaint && !ValidReaction(complaint))
            return RejectDanger(GameSessionFailure.InvalidDangerFacts);
        if (!IsCurrent(expected)) return RejectDanger(GameSessionFailure.StaleVersion);
        var plan = new DangerPlan(this);
        var failure = PlanFollowerRule(plan, request.Outcome, request.Leader.IsHero, request.Target, request.Setter, request.Complaint,
            request.Reaction, out var danger, out var adjustment);
        if (failure != GameSessionFailure.None) return RejectDanger(failure);
        var result = danger ?? new SessionDangerResult(GameSessionDisposition.NoChange, GameSessionFailure.None, Version);
        return plan.Commit(result with { Reaction = adjustment ?? result.Reaction });
    }

    // The follower rule's writes into the plan: the lock, the focus request or the complaint's reaction change toward the hero.
    private GameSessionFailure PlanFollowerRule(DangerPlan plan, FollowerRuleOutcome rule, bool leaderIsHero, SessionCombatant target,
        DangerSetterFacts setter, SessionPeaceReaction? complaint, SessionDangerReaction? reaction, out SessionDangerResult? danger,
        out ReactionAdjustment? adjustment)
    {
        danger = null;
        adjustment = null;
        var request = new SessionDangerRequest
        {
            Entity = rule.Follower, State = DangerState.CombatFocus, Target = target, Facts = setter, Reaction = reaction,
        };
        switch (rule.Step)
        {
            case FollowerRuleStep.Lock:
                danger = plan.Lock(request);
                return danger.Failure;
            case FollowerRuleStep.Focus:
                danger = plan.Set(request, false);
                return danger.Failure;
            case FollowerRuleStep.Complain when leaderIsHero:
                if (complaint is null) return GameSessionFailure.InvalidDangerFacts;
                return plan.PlanReactionChange(rule.Follower, complaint.Npc, complaint.Pc, FollowerRules.ComplaintReaction, out adjustment);
            default:
                return GameSessionFailure.None;
        }
    }

    private static bool ValidReaction(SessionPeaceReaction reaction) =>
        reaction.Npc is not null && reaction.Pc is not null && ReactionRules.IsValid(reaction.Npc) && ReactionRules.IsValid(reaction.Pc);

    // An order's combat facts: the setter's facts, valid reaction facts and valid combat-mode facts when given.
    private bool ValidPeaceFacts(SessionPeaceFacts facts) =>
        (facts.Reaction is not { } reaction || ValidReaction(reaction)) &&
        (facts.CombatMode is not { Facts: { } combat } || ValidFacts(combat));
}
