using Arcanum.Simulation.Party;

namespace Arcanum.Session;

/// <summary>
/// What the host knows for the move list of a PC's change of place (ARC-DEV-073, migration step MIG10; research FM1, FM2, RL22, RL23):
/// whether a direct follower is unconscious (0x45D800: not undead and no fatigue left; null answers none), the follower array of an
/// NPC for the followers of followers (null answers none; ARC-WORLD-1840: the session holds NPC leaders (<see cref="GameSessionOwner.QueryFollowers"/>), but the viewer carries only the
/// hero's companions, so the host passes the relation only when it can move what it names), the wait timer facts of a follower ordered to wait (null: no timer) and the request's "no wait orders" flag
/// (0x100, the teleport spells').
/// </summary>
public sealed record SessionTransferFacts
{
    public Func<string, bool>? NoFatigue { get; init; }
    public Func<string, IReadOnlyList<string>>? FollowersOf { get; init; }
    public Func<string, PartyWaitFacts?>? WaitFacts { get; init; }
    public bool NoWaitOrders { get; init; }
}

/// <summary>A follower the change of place orders to wait (the original's wait, 0x4AA7A0): the wait timer's facts, or null for none.</summary>
public sealed record SessionWaitOrder(string Entity, PartyWaitFacts? Timer);

/// <summary>
/// The move list of a PC's change of place (<see cref="TransferMoveListRules"/>): the direct followers it orders to wait and the
/// followers that travel with the PC (a node after its own followers). <see cref="GameSessionOwner.ApplyChanges"/> applies it with
/// the change of place, in one revision.
/// </summary>
public sealed record SessionMoveList(IReadOnlyList<SessionWaitOrder> Waiting, IReadOnlyList<string> Travelling)
{
    public static SessionMoveList Empty { get; } = new([], []);
}

public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The move list of the PC's change of place now (research FM1, FM2, RL22, RL23): unless the request carries "no wait orders", every
    /// direct follower the host reports unconscious is to wait; the other followers and, recursively, their followers travel. A
    /// query: nothing is written. Pass the result to <see cref="ApplyChanges"/> with the change of place.
    /// </summary>
    public SessionMoveList QueryTransferMoveList(SessionTransferFacts facts)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(facts);
        var list = TransferMoveListRules.Build(_party.Order, facts.NoFatigue ?? (_ => false), facts.FollowersOf, facts.NoWaitOrders);
        return new(list.Waiting.Select(entity => new SessionWaitOrder(entity, facts.WaitFacts?.Invoke(entity))).ToArray(), list.Travelling);
    }

    // The staged result of a move list against the state (checked before any write): the wait orders with their timer delays and the
    // danger records FM6 clears for the travellers. A list that does not match the party, or a timer that does not fit, refuses the
    // whole step.
    private sealed class TransferPlan
    {
        internal required IReadOnlyList<(string Entity, long? DelayMs, int Charisma)> Waits { get; init; }
        internal required IReadOnlyList<SessionDanger> Cleared { get; init; }
        internal required IReadOnlyCollection<string> Travelling { get; init; }
        internal bool Changes => Waits.Count > 0 || Cleared.Count > 0;
    }

    private GameSessionFailure PlanTransfer(SessionMoveList list, out TransferPlan? plan)
    {
        plan = null;
        if (list.Waiting is null || list.Travelling is null) return GameSessionFailure.InvalidCompanion;
        var order = _party.Order;
        var waiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var wait in list.Waiting)
        {
            if (wait is null || !SessionEventKinds.AiTickNpc(wait.Entity) || !order.Contains(wait.Entity) || !waiting.Add(wait.Entity))
                return GameSessionFailure.InvalidCompanion;
            if (wait.Timer is not null && !Enum.IsDefined(wait.Timer.LeaderPersuasion)) return GameSessionFailure.InvalidConsentFacts;
        }
        var travelling = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in list.Travelling)
            if (!SessionEventKinds.AiTickNpc(entity) || waiting.Contains(entity) || !travelling.Add(entity)) return GameSessionFailure.InvalidCompanion;
        // Every direct follower that is not told to wait travels.
        if (order.Any(entity => !waiting.Contains(entity) && !travelling.Contains(entity))) return GameSessionFailure.InvalidCompanion;
        var waits = new List<(string, long?, int)>();
        foreach (var wait in list.Waiting)
        {
            var delay = WaitDelay(wait.Timer, out var charisma);
            waits.Add((wait.Entity, delay, charisma));
        }
        var timers = waits.Count(wait => wait.Item2 is not null);
        if (timers > 0 && !WaitTimersFit(waits.Where(wait => wait.Item2 is not null).Select(wait => (wait.Item1, wait.Item2!.Value)).ToArray()))
            return GameSessionFailure.EventBudgetExceeded;
        // FM6, both cases: focus, who hit me last, the enemy list and fleeing-from are cleared; the state bits stay (they are the
        // danger word, not links). The substitute inventory is the barter store's, which the session does not keep per critter.
        var cleared = new List<SessionDanger>();
        foreach (var entity in travelling)
        {
            var record = _danger.Get(entity);
            if (record.Focus is null && record.FleeingFrom is null && record.HitBy is null && record.Enemies.Count == 0) continue;
            cleared.Add(record with { Focus = null, FleeingFrom = null, HitBy = null, Enemies = [] });
        }
        plan = new() { Waits = waits, Cleared = cleared, Travelling = travelling };
        return GameSessionFailure.None;
    }

    // Whether all the wait timers of one step fit together (WaitTimerFits for one).
    private bool WaitTimersFit(IReadOnlyList<(string Entity, long DelayMs)> timers)
    {
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var pending = _events.QueryPending();
        var own = pending.Count(e => e.Kind == SessionEventKinds.PartyWait && timers.Any(t => SessionEventKinds.NamesNpc(e, t.Entity)));
        return pending.Count(e => e.Kind == SessionEventKinds.PartyWait) - own + timers.Count <= SessionEventKinds.MaxWaitTimers &&
            _events.Count - own + timers.Count <= _events.Limits.MaxPending && _events.NextSequence < long.MaxValue - timers.Count &&
            timers.All(t => now <= long.MaxValue - Math.Max(t.DelayMs, 1));
    }

    // The writes of a planned step, after every refusal: the wait orders (on the map the PC leaves, so a timer parks there) and the
    // cleared danger records.
    private void CommitTransfer(TransferPlan plan)
    {
        foreach (var (entity, delay, charisma) in plan.Waits)
        {
            // A waiting standing is the WAIT bit itself, so a follower that carried the flag (ARC-SCRIPT-1136) has no separate flag after.
            _party.Put(_party.Get(entity) with { Standing = CompanionStanding.Waiting, Forced = false, WaitFlag = false }, false);
            ClearTempusFugitOnLeave(entity);
            if (delay is { } wait) ScheduleWaitTimer(entity, wait, charisma);
        }
        foreach (var record in plan.Cleared) _danger.Put(record);
    }
}
