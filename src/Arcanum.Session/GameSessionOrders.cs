using Arcanum.Simulation;
using Arcanum.Simulation.Ai;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>
/// The host's facts of one follower an order may reach (ARC-WORLD-046): its name as the PC knows it, its Chebyshev tile distance to
/// the PC (<see cref="CompanionOrderRules.Reach"/>), whether it is mind controlled, its follow-check facts (their
/// <see cref="PartyConsentFacts.Reaction"/> also chooses the leave farewell, their <see cref="PartyConsentFacts.PcCharisma"/> the
/// curse's leave), its wait-timer facts and its combat-order facts (ARC-WORLD-091): the setter's facts of an attack order's lock,
/// the reaction facts of a complaint (toward the hero) and of a stop's peace with the hero's side, and the combat-mode facts of a
/// stop (<see cref="SessionPeaceFacts"/>; none: the defaults).
/// </summary>
public sealed record CompanionOrderFacts(string Entity, string KnownName, long Distance, bool MindControlled, PartyConsentFacts Consent,
    PartyWaitFacts? Wait = null, SessionPeaceFacts? Combat = null);

/// <summary>How an order is addressed: to every follower in reach (a preset), from a follower's portrait menu, or as typed text
/// (ARC-UI-2305: <see cref="CompanionOrderRequest.Text"/> is read for its <c>#</c> or name prefix and its order word, <see cref="CompanionOrderRules.TypedReceivers"/>; the request's <see cref="CompanionOrderRequest.Type"/> is then ignored).</summary>
public enum CompanionOrderAddressing { All, Portrait, Text }

/// <summary>
/// One order (ARC-WORLD-046). <see cref="Followers"/> are the host's facts of the followers it knows (array members and, for the
/// Steamwoven follow order, waiting ones); a follower without facts is out of reach. <see cref="Clicked"/> and <see cref="Label"/> are
/// the portrait's follower and its menu label (the Original addressing compares names); <see cref="Tile"/> is the tile a walk order
/// goes to (the pointer's or the picked one), in the host's cells.
/// </summary>
public sealed record CompanionOrderRequest(CompanionOrderType Type, CompanionOrderAddressing Addressing, IReadOnlyList<CompanionOrderFacts> Followers,
    string? Clicked = null, string Label = "", CellCoord? Tile = null)
{
    /// <summary>ARC-UI-2305: the typed text of <see cref="CompanionOrderAddressing.Text"/> (the line as typed) and the order words it is
    /// read against (the host's tables).</summary>
    public string? Text { get; init; }

    public BroadcastKeywords? Keywords { get; init; }

    /// <summary>The attack order's target: the object the interface highlights or the menu's cursor picked (0x43C570); none: the order
    /// does nothing (ARC-WORLD-091).</summary>
    public SessionCombatant? Target { get; init; }

    /// <summary>The world the attack order's ordered attack event and follower rule read (ids are entity ids,
    /// <see cref="AiSchedulerRules.Pc"/> the hero); an attack order with a target needs it.</summary>
    public IHostilityWorld? World { get; init; }

    /// <summary>When set, each reached follower's one AI pass after its order (<see cref="GameSessionOwner.RunAiPass"/>, call
    /// 0x4C389D) runs through this host; none: the host runs it.</summary>
    public ISessionAiWorld? Ai { get; init; }

    /// <summary>ARC-WORLD-117: the host's part of each reached follower's order, called with its outcome right after the order and
    /// before its AI pass (<see cref="Ai"/>): a back off's animation interrupt (<see cref="CompanionOrderOutcome.Stop"/>, the
    /// original's stop attacking interrupts inside the order, before the pass reads the follower's animations). None: nothing.</summary>
    public Action<CompanionOrderOutcome>? Ordered { get; init; }
}

/// <summary>What an order did to one follower.</summary>
public enum CompanionOrderEffect
{
    /// <summary>No order for the follower (none, join, disband, follow other, or a word that named no order): only its AI pass.</summary>
    NoOrder,
    /// <summary>Leave or wait to a mind-controlled follower: nothing.</summary>
    MindControlled,
    /// <summary>Leave: a forced leave after the farewell.</summary>
    Left,
    /// <summary>Wait: the ordinary wait with its timer, after the acknowledgement; the host interrupts its animations.</summary>
    Waited,
    /// <summary>Follow accepted by an array member: the acknowledgement and an unwait that never acts (OL6).</summary>
    Acknowledged,
    /// <summary>Follow refused by the follow check: the "cannot" line only.</summary>
    Refused,
    /// <summary>Follow to a waiting follower (Steamwoven, D1), accepted: it is back in the array.</summary>
    Unwaited,
    /// <summary>Move: the host starts the "please move" goal and shows the acknowledgement when it starts, "cannot" otherwise.</summary>
    MoveRequested,
    /// <summary>Spread out: the acknowledgement, the spread flag set (see <see cref="CompanionOrderOutcome.StartSpreadGoal"/>).</summary>
    SpreadSet,
    /// <summary>Stay close: the acknowledgement, the spread flag cleared.</summary>
    SpreadCleared,
    /// <summary>Walk here: the acknowledgement; the host runs the follower to <see cref="CompanionOrderRequest.Tile"/> and lets it follow
    /// again after.</summary>
    WalkRequested,
    /// <summary>Walk here without a tile: nothing.</summary>
    NoTile,
    /// <summary>Attack without a target: nothing (ARC-WORLD-091).</summary>
    NoTarget,
    /// <summary>Attack outside the PC's combat mode on a target in the PC's own party: refused, nothing.</summary>
    PartyTarget,
    /// <summary>Attack: NO_ATTACK cleared, and the ordered attack event stopped early (<see cref="CompanionOrderOutcome.AttackStop"/>)
    /// or the follower rule did nothing (<see cref="CompanionOrderOutcome.Rule"/>).</summary>
    AttackIgnored,
    /// <summary>Attack: NO_ATTACK cleared and the target lock asked for; <see cref="CompanionOrderOutcome.Locked"/> says whether the
    /// setter then reported COMBAT_FOCUS on the target.</summary>
    AttackLocked,
    /// <summary>Attack: NO_ATTACK cleared; the follower is upset by the target and complains (the upset line when it speaks, −5
    /// toward the hero).</summary>
    Complained,
    /// <summary>Back off or stop: the acknowledgement and stop attacking (<see cref="CompanionOrderOutcome.Stop"/>).</summary>
    Stopped,
    /// <summary>Curse: the unforced leave was refused (an extraordinary leader or mind control): nothing.</summary>
    LeaveRefused,
    /// <summary>Curse: the follower left; it floats the race insult toward the PC and the host runs the PC's attack event on it
    /// (<see cref="CompanionOrderOutcome.CurseAttack"/>).</summary>
    Cursed,
    /// <summary>The session refused the follower's state change (<see cref="CompanionOrderOutcome.Failure"/>): no line.</summary>
    Failed,
}

/// <summary>
/// One reached follower's result: the order type it read, the effect, the line range the host floats over it (none when
/// <see cref="Line"/> is null; a follower that cannot speak says nothing, the host's test), the follow check's result, whether the host
/// starts the spread-out goal (the flag was clear) and the refusal of a failed state change.
/// </summary>
public sealed record CompanionOrderOutcome(string Entity, CompanionOrderType Type, CompanionOrderEffect Effect, CompanionOrderLine? Line,
    FollowConsent? Consent = null, bool StartSpreadGoal = false, GameSessionFailure Failure = GameSessionFailure.None)
{
    /// <summary>The message range of <see cref="Line"/>, or null; a complaint's upset line is <see cref="Rule"/>'s.</summary>
    public LoyaltyLines? Lines => Line is { } line ? CompanionOrderRules.Lines(line) : null;

    /// <summary>Attack: where the ordered attack event stopped before the follower rule (none when the rule ran).</summary>
    public AttackEventStop? AttackStop { get; init; }

    /// <summary>Attack: the follower rule's answer (its <see cref="FollowerRuleOutcome.Line"/> is the complaint's upset line).</summary>
    public FollowerRuleOutcome? Rule { get; init; }

    /// <summary>Attack: whether the lock was set.</summary>
    public bool Locked { get; init; }

    /// <summary>Attack: the lock's setter step (transition and enter-combat hook for the host).</summary>
    public SessionDangerResult? Danger { get; init; }

    /// <summary>A complaint's reaction change toward the hero.</summary>
    public ReactionAdjustment? Reaction { get; init; }

    /// <summary>Back off or stop: what stop attacking did (the host's interrupts follow from it).</summary>
    public SessionPeaceStep? Stop { get; init; }

    /// <summary>Curse: the race insult line (0x413D40: base 1000 of the race-line tables, chosen by the host) floats toward the PC.</summary>
    public bool Insult { get; init; }

    /// <summary>Curse: the attack event the host runs next, the hero on the former follower, loudness 1, no flags (0x4C3703).</summary>
    public CompanionOrderAttack? CurseAttack { get; init; }

    /// <summary>The follower's AI pass after its order, when the request named a host (<see cref="CompanionOrderRequest.Ai"/>).</summary>
    public SessionAiResult? Pass { get; init; }
}

/// <summary>An attack event an order leaves to the host: attacker, victim, loudness and flags (<see cref="HostilityRules.AttackEvent"/>).</summary>
public sealed record CompanionOrderAttack(SessionCombatant Attacker, SessionCombatant Victim, Loudness Loudness, AttackEventFlags Flags);

/// <summary>An order's result: <see cref="Outcomes"/> per reached follower in the order the original walks them (empty when nobody was
/// reached), <see cref="GameSessionDisposition.Changed"/> when any follower's state changed.</summary>
public sealed record SessionCompanionOrderResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    IReadOnlyList<CompanionOrderOutcome> Outcomes, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// An order to the PC's followers (ARC-WORLD-046, ARC-RES-064 rows OL1–OL16; combat orders ARC-WORLD-091, ARC-RES-067 rows
    /// FO1–FO5 and FO9). The receivers are the followers in the
    /// array, in its order, within 10 tiles of the PC (a waiting NPC is outside the array and gets nothing, OL16): every one for
    /// <see cref="CompanionOrderAddressing.All"/>, one for a portrait (its name addressing per the active rules, D4). Under the
    /// Steamwoven rules a follow order to all also reaches the waiting followers in reach, after the array (D1). Each follower is one
    /// step of its own; one that fails does not stop the others. The rules are the session's active ones, read at this call. The
    /// combat orders: attack (the request's target; outside the hero's combat mode a target in the hero's party is refused; NO_ATTACK
    /// cleared, then the ordered attack event and the follower rule through <see cref="CompanionOrderRequest.World"/>: the lock or the
    /// complaint), back off and stop (the acknowledgement and <see cref="StopAttacking"/>) and curse (an unforced leave; when it left,
    /// the insult and the hero's attack event on it for the host to run). With <see cref="CompanionOrderRequest.Ai"/> each reached
    /// follower gets one AI pass right after its order, whatever the order did (FO1). Refusals of the whole
    /// order: an invalid or repeated entity or facts naming another leader (<see cref="GameSessionFailure.InvalidCompanion"/>,
    /// <see cref="GameSessionFailure.InvalidConsentFacts"/>), an invalid target handle or combat facts
    /// (<see cref="GameSessionFailure.InvalidDangerTarget"/>, <see cref="GameSessionFailure.InvalidDangerFacts"/>), a portrait for an
    /// NPC that does not follow, a stale version.
    /// </summary>
    [OriginalFunction(0x4C31A0, OriginalFunctionScope.Partial)]
    public SessionCompanionOrderResult IssueCompanionOrder(GameSessionVersion expected, CompanionOrderRequest request)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Type) || !Enum.IsDefined(request.Addressing) || request.Followers is null || request.Label is null)
            return RejectOrder(GameSessionFailure.InvalidCompanion);
        var facts = new Dictionary<string, CompanionOrderFacts>(StringComparer.Ordinal);
        foreach (var follower in request.Followers)
        {
            // An empty name would be a prefix of every portrait text (OL15), and a negative distance has no meaning.
            if (follower is null || !SessionValidation.Entity(follower.Entity) || string.IsNullOrEmpty(follower.KnownName) || follower.Distance < 0
                || !facts.TryAdd(follower.Entity, follower))
                return RejectOrder(GameSessionFailure.InvalidCompanion);
            if (follower.Consent is null || !ValidFacts(follower.Consent) || follower.Consent.OtherLeaderCharisma is not null ||
                (follower.Wait is { } wait && !Enum.IsDefined(wait.LeaderPersuasion))) return RejectOrder(GameSessionFailure.InvalidConsentFacts);
            if (follower.Combat is { } combat && !ValidPeaceFacts(combat)) return RejectOrder(GameSessionFailure.InvalidDangerFacts);
        }
        if (!ValidDangerHandle(request.Target)) return RejectOrder(GameSessionFailure.InvalidDangerTarget);
        if (!IsCurrent(expected)) return RejectOrder(GameSessionFailure.StaleVersion);
        var profile = _rules.Orders;
        CompanionOrderCandidate Candidate(string entity) => new(entity, facts[entity].KnownName, facts[entity].Distance);
        var array = _party.Order.Where(facts.ContainsKey).Select(Candidate).ToArray();

        List<(string Entity, CompanionOrderType Type)> receivers;
        if (request.Addressing == CompanionOrderAddressing.Text)
        {
            if (request.Text is not { } text || request.Keywords is not { } keywords) return RejectOrder(GameSessionFailure.InvalidCompanion);
            receivers = CompanionOrderRules.TypedReceivers(array, text, keywords).ToList();
        }
        else if (request.Addressing == CompanionOrderAddressing.Portrait)
        {
            if (request.Clicked is not { } clicked || !SessionValidation.Entity(clicked)) return RejectOrder(GameSessionFailure.InvalidCompanion);
            if (_party.Get(clicked).Standing != CompanionStanding.Following) return RejectOrder(GameSessionFailure.NotInParty);
            receivers = CompanionOrderRules.PortraitReceiver(array, clicked, request.Type, request.Label, profile) is { } one ? [one] : [];
        }
        else
        {
            receivers = CompanionOrderRules.AllInReach(array).Select(entity => (entity, request.Type)).ToList();
            // D1: the Steamwoven follow order also reaches the waiting followers in reach, after the array, in ordinal order.
            if (request.Type == CompanionOrderType.Follow && !profile.FollowOrderNeverUnwaits)
                receivers.AddRange(CompanionOrderRules.AllInReach(_party.Records.Values.Where(r => r.Standing == CompanionStanding.Waiting && facts.ContainsKey(r.Entity))
                    .OrderBy(r => r.Entity, StringComparer.Ordinal).Select(r => Candidate(r.Entity))).Select(entity => (entity, request.Type)));
        }

        var start = _revision;
        var outcomes = new List<CompanionOrderOutcome>(receivers.Count);
        foreach (var (entity, type) in receivers)
        {
            var outcome = Order(entity, type, facts[entity], request, profile);
            request.Ordered?.Invoke(outcome);
            // FO1: one AI pass per reached follower right after its own order, before the next follower's order.
            if (request.Ai is { } ai) outcome = outcome with { Pass = RunAiPass(Version, entity, ai) };
            outcomes.Add(outcome);
        }
        return new(_revision == start ? GameSessionDisposition.NoChange : GameSessionDisposition.Changed, GameSessionFailure.None,
            outcomes.AsReadOnly(), Version);
    }

    // One follower's order (OL4–OL11): its line, then its state change through the party entries.
    private CompanionOrderOutcome Order(string entity, CompanionOrderType type, CompanionOrderFacts facts, CompanionOrderRequest request,
        CompanionOrderRuleProfile profile)
    {
        var tile = request.Tile;
        CompanionOrderOutcome Changed(GameSessionMutationResult result, CompanionOrderEffect effect, CompanionOrderLine line, bool spread = false) =>
            result.Disposition == GameSessionDisposition.Rejected
                ? new(entity, type, CompanionOrderEffect.Failed, null, Failure: result.Failure)
                : new(entity, type, effect, line, StartSpreadGoal: spread);
        var current = _party.Get(entity);
        switch (type)
        {
            case CompanionOrderType.Leave:
                if (facts.MindControlled) return new(entity, type, CompanionOrderEffect.MindControlled, null);
                return Changed(LeaveParty(Version, entity, new(Force: true)), CompanionOrderEffect.Left,
                    CompanionOrderRules.Farewell(facts.Consent.Reaction, profile));
            case CompanionOrderType.Wait:
                if (facts.MindControlled) return new(entity, type, CompanionOrderEffect.MindControlled, null);
                return Changed(WaitParty(Version, entity, facts.Wait), CompanionOrderEffect.Waited, CompanionOrderLine.Acknowledge);
            case CompanionOrderType.Follow when current.Standing == CompanionStanding.Waiting:
            {
                var unwait = UnwaitParty(Version, entity, facts.Consent, false);
                if (unwait.Disposition != GameSessionDisposition.Rejected)
                    return new(entity, type, CompanionOrderEffect.Unwaited, CompanionOrderLine.Acknowledge, unwait.Consent);
                return unwait.Failure == GameSessionFailure.ConsentRefused
                    ? new(entity, type, CompanionOrderEffect.Refused, CompanionOrderLine.Cannot, unwait.Consent)
                    : new(entity, type, CompanionOrderEffect.Failed, null, unwait.Consent, Failure: unwait.Failure);
            }
            case CompanionOrderType.Follow:
            {
                // OL6: the charisma limit is ignored only for a forced follower; the unwait after an acceptance never acts.
                var consent = Consent(facts.Consent, current.Forced, current.Forced);
                return consent == FollowConsent.Accepted
                    ? new(entity, type, CompanionOrderEffect.Acknowledged, CompanionOrderLine.Acknowledge, consent)
                    : new(entity, type, CompanionOrderEffect.Refused, CompanionOrderLine.Cannot, consent);
            }
            case CompanionOrderType.Move:
                return new(entity, type, CompanionOrderEffect.MoveRequested, null);
            case CompanionOrderType.StayClose:
                return Changed(SetSpreadOut(Version, entity, false), CompanionOrderEffect.SpreadCleared, CompanionOrderLine.Acknowledge);
            case CompanionOrderType.SpreadOut:
                return Changed(SetSpreadOut(Version, entity, true), CompanionOrderEffect.SpreadSet, CompanionOrderLine.Acknowledge, !current.SpreadOut);
            case CompanionOrderType.WalkHere:
                return tile is null ? new(entity, type, CompanionOrderEffect.NoTile, null)
                    : new(entity, type, CompanionOrderEffect.WalkRequested, CompanionOrderLine.Acknowledge);
            case CompanionOrderType.Attack:
                return AttackOrder(entity, facts, request.Target, request.World);
            case CompanionOrderType.BackOff:
            {
                var stop = StopAttacking(Version, new SessionStopAttackingRequest
                {
                    Combatant = SessionCombatant.Critter(entity),
                    Facts = new Dictionary<string, SessionPeaceFacts>(StringComparer.Ordinal) { [entity] = facts.Combat ?? new() },
                });
                return stop.Disposition == GameSessionDisposition.Rejected
                    ? new(entity, type, CompanionOrderEffect.Failed, null, Failure: stop.Failure)
                    : new(entity, type, CompanionOrderEffect.Stopped, CompanionOrderLine.Acknowledge) { Stop = stop.Steps.Single() };
            }
            case CompanionOrderType.Curse:
            {
                // FO2: a mind-controlled follower ignores it; the leave is unforced (an extraordinary leader refuses it).
                if (facts.MindControlled) return new(entity, type, CompanionOrderEffect.MindControlled, null);
                var leave = LeaveParty(Version, entity, new(Force: false, LeaderCharisma: facts.Consent.PcCharisma));
                if (leave.Failure == GameSessionFailure.LeaveRefused) return new(entity, type, CompanionOrderEffect.LeaveRefused, null);
                if (leave.Disposition == GameSessionDisposition.Rejected)
                    return new(entity, type, CompanionOrderEffect.Failed, null, Failure: leave.Failure);
                return new(entity, type, CompanionOrderEffect.Cursed, null)
                {
                    Insult = true,
                    CurseAttack = new(SessionCombatant.Hero, SessionCombatant.Critter(entity), CurseLoudness, AttackEventFlags.None),
                };
            }
            default:
                return new(entity, type, CompanionOrderEffect.NoOrder, null);
        }
    }

    /// <summary>The curse's attack event is raised at loudness 1 (FO2).</summary>
    public const Loudness CurseLoudness = Loudness.Normal;

    // FO3, FO4, FO9: the attack order to one follower: the target and the party filter, then in one revision NO_ATTACK cleared and the
    // ordered attack event, which runs only the follower rule (the lock or the complaint).
    private CompanionOrderOutcome AttackOrder(string entity, CompanionOrderFacts facts, SessionCombatant? target, IHostilityWorld? world)
    {
        const CompanionOrderType type = CompanionOrderType.Attack;
        if (target is null) return new(entity, type, CompanionOrderEffect.NoTarget, null);
        if (FollowerRules.AttackOrderRefused(_hero is not null && _combat.HeroCombatMode, !target.IsHero && LedByPc(target.Entity!)))
            return new(entity, type, CompanionOrderEffect.PartyTarget, null);
        if (world is null) return AllowedButFailed(entity, GameSessionFailure.InvalidDangerFacts);
        var combat = facts.Combat ?? new();
        var plan = new DangerPlan(this);
        plan.AllowAttacking(SessionCombatant.Critter(entity));
        var victim = target.Entity ?? AiSchedulerRules.Pc;
        var attack = HostilityRules.AttackEvent(world, entity, victim, AttackEventFlags.Ordered);
        FollowerRuleOutcome? rule = null;
        SessionDangerResult? danger = null;
        ReactionAdjustment? reaction = null;
        if (attack.Stop == AttackEventStop.Ordered && attack.OrderedLeader is { } leader)
        {
            rule = FollowerRules.Rule(world, entity, leader, victim, FollowerSide.Attacker, false, true, _rules.Combat);
            var failure = PlanFollowerRule(plan, rule.Value, leader == AiSchedulerRules.Pc, target, combat.Setter, combat.Reaction, null,
                out danger, out reaction);
            if (failure != GameSessionFailure.None) return AllowedButFailed(entity, failure);
        }
        var committed = plan.Commit(new SessionDangerResult(GameSessionDisposition.NoChange, GameSessionFailure.None, Version));
        if (committed.Disposition == GameSessionDisposition.Rejected)
            return new(entity, type, CompanionOrderEffect.Failed, null, Failure: committed.Failure);
        var effect = rule?.Step switch
        {
            FollowerRuleStep.Lock => CompanionOrderEffect.AttackLocked,
            FollowerRuleStep.Complain => CompanionOrderEffect.Complained,
            _ => CompanionOrderEffect.AttackIgnored,
        };
        return new(entity, type, effect, null)
        {
            AttackStop = rule is null ? attack.Stop : null, Rule = rule, Locked = danger?.Locked == true,
            Danger = danger is null ? null : danger with { Disposition = committed.Disposition, Version = committed.Version }, Reaction = reaction,
        };
    }

    // FO9: past the party filter the stop is lifted before the event, so a step the session cannot make (no world, missing facts)
    // still clears NO_ATTACK; the outcome reports the failure.
    private CompanionOrderOutcome AllowedButFailed(string entity, GameSessionFailure failure)
    {
        var plan = new DangerPlan(this);
        plan.AllowAttacking(SessionCombatant.Critter(entity));
        var committed = plan.Commit(new SessionDangerResult(GameSessionDisposition.NoChange, GameSessionFailure.None, Version));
        return new(entity, CompanionOrderType.Attack, CompanionOrderEffect.Failed, null,
            Failure: committed.Disposition == GameSessionDisposition.Rejected ? committed.Failure : failure);
    }

    private SessionCompanionOrderResult RejectOrder(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, Array.Empty<CompanionOrderOutcome>(), Version);
}
