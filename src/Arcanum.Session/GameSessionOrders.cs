using Arcanum.Simulation;
using Arcanum.Simulation.Movement;
using Arcanum.Simulation.Party;

namespace Arcanum.Session;

/// <summary>
/// The host's facts of one follower an order may reach (ARC-WORLD-046): its name as the PC knows it, its Chebyshev tile distance to
/// the PC (<see cref="CompanionOrderRules.Reach"/>), whether it is mind controlled, its follow-check facts (their
/// <see cref="PartyConsentFacts.Reaction"/> also chooses the leave farewell) and its wait-timer facts.
/// </summary>
public sealed record CompanionOrderFacts(string Entity, string KnownName, long Distance, bool MindControlled, PartyConsentFacts Consent,
    PartyWaitFacts? Wait = null);

/// <summary>How an order is addressed: to every follower in reach (a preset or <c>#</c> text), or from a follower's portrait menu.</summary>
public enum CompanionOrderAddressing { All, Portrait }

/// <summary>
/// One order (ARC-WORLD-046). <see cref="Followers"/> are the host's facts of the followers it knows (array members and, for the
/// Steamwoven follow order, waiting ones); a follower without facts is out of reach. <see cref="Clicked"/> and <see cref="Label"/> are
/// the portrait's follower and its menu label (the Original addressing compares names); <see cref="Tile"/> is the tile a walk order
/// goes to (the pointer's or the picked one), in the host's cells.
/// </summary>
public sealed record CompanionOrderRequest(CompanionOrderType Type, CompanionOrderAddressing Addressing, IReadOnlyList<CompanionOrderFacts> Followers,
    string? Clicked = null, string Label = "", CellCoord? Tile = null);

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
    /// <summary>Attack, back off or curse: the NPC AI's combat stage (A7) handles it; nothing here.</summary>
    CombatOrder,
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
    /// <summary>The message range of <see cref="Line"/>, or null.</summary>
    public LoyaltyLines? Lines => Line is { } line ? CompanionOrderRules.Lines(line) : null;
}

/// <summary>An order's result: <see cref="Outcomes"/> per reached follower in the order the original walks them (empty when nobody was
/// reached), <see cref="GameSessionDisposition.Changed"/> when any follower's state changed.</summary>
public sealed record SessionCompanionOrderResult(GameSessionDisposition Disposition, GameSessionFailure Failure,
    IReadOnlyList<CompanionOrderOutcome> Outcomes, GameSessionVersion Version);

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// An out-of-combat order to the PC's followers (ARC-WORLD-046, ARC-RES-064 rows OL1–OL16). The receivers are the followers in the
    /// array, in its order, within 10 tiles of the PC (a waiting NPC is outside the array and gets nothing, OL16): every one for
    /// <see cref="CompanionOrderAddressing.All"/>, one for a portrait (its name addressing per the active rules, D4). Under the
    /// Steamwoven rules a follow order to all also reaches the waiting followers in reach, after the array (D1). Each follower is one
    /// step of its own; one that fails does not stop the others. The rules are the session's active ones, read at this call. Combat
    /// orders return <see cref="CompanionOrderEffect.CombatOrder"/> for the NPC AI's stage A7. Refusals of the whole
    /// order: an invalid or repeated entity or facts naming another leader (<see cref="GameSessionFailure.InvalidCompanion"/>,
    /// <see cref="GameSessionFailure.InvalidConsentFacts"/>), a portrait for an NPC that does not follow, a stale version.
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
            if (follower is null || !SessionValidation.Entity(follower.Entity) || follower.KnownName is null || !facts.TryAdd(follower.Entity, follower))
                return RejectOrder(GameSessionFailure.InvalidCompanion);
            if (follower.Consent is null || !ValidFacts(follower.Consent) || follower.Consent.OtherLeaderCharisma is not null ||
                (follower.Wait is { } wait && !Enum.IsDefined(wait.LeaderPersuasion))) return RejectOrder(GameSessionFailure.InvalidConsentFacts);
        }
        if (!IsCurrent(expected)) return RejectOrder(GameSessionFailure.StaleVersion);
        var profile = _rules.Orders;
        CompanionOrderCandidate Candidate(string entity) => new(entity, facts[entity].KnownName, facts[entity].Distance);
        var array = _party.Order.Where(facts.ContainsKey).Select(Candidate).ToArray();

        List<(string Entity, CompanionOrderType Type)> receivers;
        if (request.Addressing == CompanionOrderAddressing.Portrait)
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
        var outcomes = receivers.Select(r => Order(r.Entity, r.Type, facts[r.Entity], request.Tile, profile)).ToArray();
        return new(_revision == start ? GameSessionDisposition.NoChange : GameSessionDisposition.Changed, GameSessionFailure.None,
            Array.AsReadOnly(outcomes), Version);
    }

    // One follower's order (OL4–OL11): its line, then its state change through the party entries.
    private CompanionOrderOutcome Order(string entity, CompanionOrderType type, CompanionOrderFacts facts, CellCoord? tile,
        CompanionOrderRuleProfile profile)
    {
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
            case var combat when CompanionOrderRules.IsCombat(combat):
                return new(entity, type, CompanionOrderEffect.CombatOrder, null);
            default:
                return new(entity, type, CompanionOrderEffect.NoOrder, null);
        }
    }

    private SessionCompanionOrderResult RejectOrder(GameSessionFailure failure) =>
        new(GameSessionDisposition.Rejected, failure, Array.Empty<CompanionOrderOutcome>(), Version);
}
