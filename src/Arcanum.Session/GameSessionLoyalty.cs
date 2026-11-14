using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Party;
using Arcanum.Simulation.Reaction;

namespace Arcanum.Session;

/// <summary>
/// The facts of a wait timer (ARC-WORLD-045, OL23): the NPC's wait field (300), and the leader's Persuasion training,
/// effective charisma and whether the NPC is mind controlled. With a hero the session replaces the leader's training and
/// charisma by the hero's (as for the follow check).
/// </summary>
public sealed record PartyWaitFacts(int WaitIndex, PersuasionTraining LeaderPersuasion, int LeaderCharisma, bool MindControlled);

/// <summary>
/// What a host knows for a wait timeout (ARC-WORLD-045): the NPC's and, without a hero, the PC's reaction facts. A pass given
/// another world, or a null answer, jilts the NPC without its reaction change and says so (<see cref="SessionWaitFiring"/>).
/// </summary>
public interface ISessionPartyEventWorld : ISessionEventWorld
{
    ReactionNpcFacts? ReactionNpcFacts(SessionMapId map, string entity);
    /// <summary>The PC's reaction facts; asked only when the session has no hero.</summary>
    ReactionPcFacts? ReactionPcFacts();
}

/// <summary>
/// A wait timer that came due in a pass (OL24): the NPC stopped waiting, jilted, and the PC no longer leads it.
/// <see cref="LeaderCharisma"/> is the charisma its reaction change was computed from; <see cref="Reaction"/> is the reaction change's decision when it ran;
/// <see cref="ReactionFailure"/> says why it did not (the host gave no facts: <see cref="GameSessionFailure.InvalidReactionFacts"/>;
/// a stored value beyond the bounds: <see cref="GameSessionFailure.InvalidReactionAmount"/>; no room for a new reaction record:
/// <see cref="GameSessionFailure.BudgetExceeded"/>).
/// </summary>
public sealed record SessionWaitFiring(GameEvent Event, string Entity, int LeaderCharisma,
    ReactionAdjustment? Reaction, GameSessionFailure ReactionFailure);

/// <summary>
/// Outcome of a loyalty step (<see cref="GameSessionOwner.RunLoyaltyStep"/>). <see cref="Step"/> is null when no step ran
/// because the NPC does not follow the PC (free, or waiting: the source's pass stops before it) or the call was refused.
/// </summary>
public sealed record SessionLoyaltyResult(GameSessionDisposition Disposition, GameSessionFailure Failure, LoyaltyStepResult? Step,
    GameSessionVersion Version);

/// <summary>
/// Outcome of the follow check after the PC hit its own follower (<see cref="GameSessionOwner.CheckFollowerAfterHit"/>, the attack event's
/// follower branch). <see cref="Consent"/> is the check's result (null when no check ran); <see cref="Left"/> says the follower left,
/// jilted, after a refusal; <see cref="LeaveRefused"/> says it was refused but the leave was not (the leader's extraordinary charisma, or
/// mind control), so it stays. The host floats <see cref="RefusalLines"/> after a leave.
/// </summary>
public sealed record SessionFollowerHitCheck(GameSessionDisposition Disposition, GameSessionFailure Failure, FollowConsent? Consent, bool Left,
    bool LeaveRefused, GameSessionVersion Version)
{
    /// <summary>The refusal line range of a follower that left, else null.</summary>
    public LoyaltyLines? RefusalLines => Left && Consent is { } reason ? CompanionLoyaltyRules.RefusalLines(reason) : null;
}

/// <summary>
/// ARC-WORLD-045: the wait timer and follower loyalty of the original (ARC-RES-064 rows OL17–OL26). A follower told to wait
/// with <see cref="PartyWaitFacts"/> gets the original's game-time event (<see cref="SessionEventKinds.PartyWait"/>) on the map
/// it waits on; every way out of waiting removes it; when it comes due the NPC stops waiting, jilted, after the reaction change
/// <c>2 × (charisma − 20)</c> while the PC still leads it. Loyalty marks are armed by a negative reaction change by the PC leader
/// (<see cref="GameSessionOwner.AdjustReaction"/> and the other reaction changes) and by a change of the PC's alignment
/// (<see cref="RecordPcAlignmentChange"/>); the host runs <see cref="RunLoyaltyStep"/> on a follower's non-combat AI pass.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The loyalty step of a follower's non-combat AI pass (OL18–OL21): the follow check with the charisma limit ignored; a
    /// refusal makes the follower leave, jilted, unless the leave is refused (the leader's extraordinary charisma, or mind
    /// control); otherwise an armed follower clears its marks and the check-leader test gives a warning or none.
    /// <paramref name="actionGoalRunning"/> is the host's answer to "does the NPC run an action-class goal" (overlay goals do not
    /// count, a follow goal does): then no step runs (decision D5, both settings as the original). With a hero the PC's
    /// persuasion training and charisma are the hero's. The host shows the result's line ranges. One revision when the record
    /// changed. Facts naming another leader are <see cref="GameSessionFailure.InvalidConsentFacts"/>.
    /// </summary>
    public SessionLoyaltyResult RunLoyaltyStep(GameSessionVersion expected, string entity, PartyConsentFacts facts, bool actionGoalRunning)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(facts);
        if (!SessionValidation.Entity(entity)) return RejectLoyalty(GameSessionFailure.InvalidCompanion);
        if (!ValidFacts(facts) || facts.OtherLeaderCharisma is not null) return RejectLoyalty(GameSessionFailure.InvalidConsentFacts);
        if (!IsCurrent(expected)) return RejectLoyalty(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        // CF9: the loyalty step (0x4AC380) returns early for a set WAIT bit (a follower that joined with the flag set).
        if (current.Standing != CompanionStanding.Following || current.WaitFlag) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, Version);
        var consent = Consent(facts, current.Forced, ignoreLimit: true);
        var (persuasion, charisma) = Leader(facts.PcPersuasion, facts.PcCharisma);
        // ARC-WORLD-067: the leader check's warning reads the hero's alignment, as the follow check above does.
        var alignment = HeroPartySide()?.Alignment ?? facts.PcAlignment;
        var step = CompanionLoyaltyRules.Step(actionGoalRunning, consent, FollowConsentRules.MayLeaveUnforced(charisma, facts.MindControlledByPc),
            new(current.Loyalty, facts.MindControlledByPc, persuasion, current.Forced, facts.Reaction, facts.Thresholds, alignment,
                facts.NpcAlignment));
        // OL20: the leave clears the array entry, the leader and forced, then marks it jilted; its marks stay (OL22).
        var next = step.Kind == LoyaltyStepKind.Left
            ? current with { Standing = CompanionStanding.Free, Forced = false, Jilted = true }
            : current with { Loyalty = step.Loyalty };
        if (next == current) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, step, Version);
        if (_revision == long.MaxValue) return RejectLoyalty(GameSessionFailure.RevisionExhausted);
        _party.Put(next, false);
        // ARC-SKILLS-008 (ST2): a follower that leaves is no longer concealed.
        if (next.Standing == CompanionStanding.Free)
        {
            _combat.Concealed.Remove(next.Entity);
            ClearNoAttackOnLeave(next.Entity);
            ClearTempusFugitOnLeave(next.Entity);
        }
        Revise();
        return new(GameSessionDisposition.Changed, GameSessionFailure.None, step, Version);
    }

    /// <summary>
    /// ARC-AI-1515 (attack event 0x4A9650, follower branch of the PC attacker): the follow check (0x4AD950) of a follower the PC hit, with the
    /// follower limit ignored (its last argument 1); a refusal makes it leave unforced (0x45DFC0) and then sets the JILTED bit, so it
    /// is free and jilted, unless the leave is refused (the leader's extraordinary charisma, or mind control); the caller then floats the
    /// refusal line (<see cref="SessionFollowerHitCheck.RefusalLines"/>), or, when the follower stays, the accidental-attack line one time
    /// in three. A waiting NPC is checked too (it still has the PC as leader); a free one is NoChange with no check. Unlike
    /// <see cref="RunLoyaltyStep"/> no loyalty mark is read or cleared. With a hero the PC's side is the hero's. One revision when the
    /// record changed. Facts naming another leader are <see cref="GameSessionFailure.InvalidConsentFacts"/>.
    /// </summary>
    public SessionFollowerHitCheck CheckFollowerAfterHit(GameSessionVersion expected, string entity, PartyConsentFacts facts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(facts);
        if (!SessionValidation.Entity(entity)) return RejectFollowerHit(GameSessionFailure.InvalidCompanion);
        if (!ValidFacts(facts) || facts.OtherLeaderCharisma is not null) return RejectFollowerHit(GameSessionFailure.InvalidConsentFacts);
        if (!IsCurrent(expected)) return RejectFollowerHit(GameSessionFailure.StaleVersion);
        var current = _party.Get(entity);
        if (current.Standing == CompanionStanding.Free) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, null, false, false, Version);
        var consent = Consent(facts, current.Forced, ignoreLimit: true);
        if (consent == FollowConsent.Accepted) return new(GameSessionDisposition.NoChange, GameSessionFailure.None, consent, false, false, Version);
        var (_, charisma) = Leader(facts.PcPersuasion, facts.PcCharisma);
        var left = LeaveParty(expected, entity, new PartyLeave(Force: false, LeaderCharisma: charisma, MindControlled: facts.MindControlledByPc, Jilt: true));
        if (left.Disposition == GameSessionDisposition.Rejected)
            return left.Failure == GameSessionFailure.LeaveRefused
                ? new(GameSessionDisposition.NoChange, GameSessionFailure.None, consent, false, true, Version)
                : RejectFollowerHit(left.Failure);
        return new(left.Disposition, GameSessionFailure.None, consent, true, false, left.Version);
    }

    private SessionFollowerHitCheck RejectFollowerHit(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, false, false, Version);

    /// <summary>
    /// The PC's alignment changed from <paramref name="previous"/> to <paramref name="current"/> (OL17, the stat setter): each
    /// follower in the array (not a waiting one) is armed with check-leader and the too-good mark when it rose or the too-bad
    /// mark when it fell. The alignment itself is the caller's. One revision; no change or no follower is NoChange.
    /// </summary>
    public GameSessionMutationResult RecordPcAlignmentChange(GameSessionVersion expected, int previous, int current)
    {
        ThrowIfDisposed();
        if (!IsCurrent(expected)) return Reject(GameSessionFailure.StaleVersion);
        var armed = _party.Order.Select(entity => _party.Records[entity])
            .Select(record => record with { Loyalty = CompanionLoyaltyRules.ArmAlignment(record.Loyalty, previous, current) })
            .Where(record => record != _party.Records[record.Entity]).ToArray();
        if (armed.Length == 0) return NoChange();
        if (_revision == long.MaxValue) return Reject(GameSessionFailure.RevisionExhausted);
        foreach (var record in armed) _party.Put(record, false);
        return Changed();
    }

    /// <summary>The NPC's pending wait timer: its deadline, or null when it has none.</summary>
    public long? QueryWaitTimer(string entity)
    {
        ThrowIfDisposed();
        if (!SessionValidation.Entity(entity)) throw new ArgumentException("Invalid entity id.", nameof(entity));
        return _events.QueryPending().FirstOrDefault(e => e.Kind == SessionEventKinds.PartyWait && SessionEventKinds.NamesNpc(e, entity))?.DeadlineMs;
    }

    // The wait timer of a follower that starts waiting (OL23), or null. A crafted save may still hold one for the NPC: it belongs
    // to an earlier wait and is removed first, so a new wait always starts its own.
    private long? WaitDelay(PartyWaitFacts? timer, out int charisma)
    {
        charisma = 0;
        if (timer is null) return null;
        (var persuasion, charisma) = Leader(timer.LeaderPersuasion, timer.LeaderCharisma);
        return CompanionLoyaltyRules.WaitTimerMs(timer.WaitIndex, persuasion, charisma, timer.MindControlled);
    }

    // ARC-SCRIPT-1136 (CF8): the wait of an NPC with no leader reads no leader: its index alone picks the timer (the leader's stat test and Persuasion training read a null handle's slot, inferred 0 both), the mind-control test stands.
    private static long? LeaderlessWaitDelay(PartyWaitFacts? timer) =>
        timer is null ? null : CompanionLoyaltyRules.WaitTimerMs(timer.WaitIndex, PersuasionTraining.None, 0, timer.MindControlled);

    private bool WaitTimerFits(long delayMs, string entity)
    {
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var pending = _events.QueryPending();
        var own = pending.Count(e => e.Kind == SessionEventKinds.PartyWait && SessionEventKinds.NamesNpc(e, entity));
        return pending.Count(e => e.Kind == SessionEventKinds.PartyWait) - own < SessionEventKinds.MaxWaitTimers &&
            _events.Count - own < _events.Limits.MaxPending && _events.NextSequence < long.MaxValue && now <= long.MaxValue - Math.Max(delayMs, 1);
    }

    // Scheduled on the actor's map, where a follower stands; checked by WaitTimerFits first, so a failure here is a defect.
    private void ScheduleWaitTimer(string entity, long delayMs, int charisma)
    {
        CancelWaitTimers(entity);
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var scheduled = _events.ScheduleAfter(SessionEventKinds.PartyWait,
            GameEventPayload.Of([new(_actor.Map.RuntimeMap, entity)], now, delayMs, charisma), delayMs, EventClocks(now));
        if (scheduled.Disposition != GameEventScheduleDisposition.Added) throw new InvalidOperationException("An admitted wait timer could not be scheduled.");
    }

    // OL25: every way out of waiting (unwait, join, leave, death) removes the NPC's pending timers, wherever they are parked.
    private void CancelWaitTimers(string entity) => _events.RemoveAll(SessionEventKinds.PartyWait, e => SessionEventKinds.NamesNpc(e, entity));

    // A wait timer came due (OL24, OL26: it fires on the NPC's map, also overdue at the first pass after the PC returns). The
    // reaction change runs while the PC still leads the NPC, so a negative one arms its marks (OL22); then the NPC is free and
    // jilted. One revision. The reaction part needs the host's facts: without them the NPC is jilted alone and the firing says so.
    [OriginalFunction(0x4AA990, OriginalFunctionScope.Partial)]
    internal GameEventHandlerOutcome FireWaitTimer(GameEventFiring firing)
    {
        var e = firing.Event;
        var entity = e.Payload.Entities[0];
        var current = _party.Get(entity.Code);
        // ARC-SCRIPT-1136 (CF9): the handler (0x4AA990) acts only for a non-null leader, so the timer of a free NPC with the wait flag runs out and nothing happens: the flag stays, no jilting, no reaction change.
        if (current.Standing == CompanionStanding.Free && current.WaitFlag) return GameEventHandlerOutcome.Completed;
        // Every way out of waiting removes the timer and a restore admits one only for a waiting NPC or a free one with the flag, so this is a defect guard; at
        // revision exhaustion the queue has already taken the event, so the NPC then waits on without a timer.
        if (current.Standing != CompanionStanding.Waiting || _revision == long.MaxValue) return GameEventHandlerOutcome.Refused;
        var charisma = _hero is null ? (int)e.Payload.Values[2] : Leader(PersuasionTraining.None, 0).Charisma;
        var world = _passWorld as ISessionPartyEventWorld;
        var npc = world?.ReactionNpcFacts(new(entity.Map), entity.Code) is { } given ? Listed(entity.Code, given) : null;
        var pc = QueryHeroReactionFacts() ?? world?.ReactionPcFacts();
        ReactionAdjustment? adjustment = null;
        SessionReaction? reaction = null;
        var failure = GameSessionFailure.InvalidReactionFacts;
        var delta = CompanionLoyaltyRules.ExpiryReactionDelta(charisma);
        if (!ReactionLimits.InRange(delta)) failure = GameSessionFailure.InvalidReactionAmount;
        else if (npc is not null && pc is not null && ResolveReaction(entity.Code, npc, pc, out var record) == GameSessionFailure.None)
        {
            failure = PlanReaction(record, record.Stored(npc.BaseReaction), (int)delta, npc, pc, true, true, out var planned, out var updated);
            adjustment = planned;
            var recorded = _reactions.Records.TryGetValue(entity.Code, out var known);
            if (failure == GameSessionFailure.None && (recorded ? known != updated : !updated.IsBaseline))
            {
                if (!recorded && _reactions.Records.Count >= _limits.MaxReactionRecords) failure = GameSessionFailure.BudgetExceeded;
                else reaction = updated;
            }
        }
        var loyalty = failure == GameSessionFailure.None && adjustment is { LeaderCheck: true }
            ? CompanionLoyaltyRules.ArmReaction(current.Loyalty) : current.Loyalty;
        if (reaction is { } write)
        {
            if (write.IsBaseline) _reactions.Records.Remove(write.Entity);
            else _reactions.Records[write.Entity] = write;
        }
        _party.Put(current with { Standing = CompanionStanding.Free, Forced = false, Jilted = true, Loyalty = loyalty }, false);
        // ARC-SKILLS-008 (ST2): the waiting follower that leaves is no longer concealed.
        _combat.Concealed.Remove(current.Entity);
        ClearNoAttackOnLeave(current.Entity);
        ClearTempusFugitOnLeave(current.Entity);
        Revise();
        _passWaits?.Add(new(e, entity.Code, charisma, failure == GameSessionFailure.None ? adjustment : null, failure));
        return GameEventHandlerOutcome.Completed;
    }

    // The leader's side: the hero's Persuasion training and effective charisma, else the caller's.
    private (PersuasionTraining Persuasion, int Charisma) Leader(PersuasionTraining persuasion, int charisma) => HeroPartySide() is { } pc
        ? (pc.Persuasion, pc.Charisma)
        : (persuasion, charisma);

    private SessionLoyaltyResult RejectLoyalty(GameSessionFailure failure) => new(GameSessionDisposition.Rejected, failure, null, Version);
}
