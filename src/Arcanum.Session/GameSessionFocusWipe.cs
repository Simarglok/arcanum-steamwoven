using Arcanum.Simulation;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Movement;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1146 (ledger RM7, the executable read of 0x45ED70 and 0x45ECE0, the event name "Combat-Focus Wipe", kind 26): the dead NPC's combat focus (object field 0x11B, the killer the death wrote) does not stay for ever. The death
/// queues the wipe ten game minutes on (<see cref="SessionEventKinds.FocusWipeDelayMs"/>); when it fires, a dead NPC with a stored focus clears it once the focus is 30 or more tiles away (the larger of the two coordinate differences,
/// 0x441AE0 → 0x4B96F0), and while the focus is nearer the event queues itself again, so it keeps watching until the killer leaves. A critter that is not dead (revived) or has no focus ends the event (the original answers 1).
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// The wipe event of a dead NPC (0x45ED70): <see cref="SessionEventKinds.FocusWipe"/> at <paramref name="delayMs"/> from <paramref name="now"/> on the NPC's map. Several may stand for one NPC (the original's queue has no key for the kind); a
    /// session at its event bound (the kind's own share or the queue's) or clock edge queues none (the focus then stays, a project bound).
    /// </summary>
    [OriginalFunction(0x45ED70, OriginalFunctionScope.Partial)]
    internal bool ScheduleFocusWipe(string entity, int map, long now, long delayMs = SessionEventKinds.FocusWipeDelayMs)
    {
        if (!SessionEventKinds.AiTickNpc(entity) || _events.QueryPending(SessionEventKinds.FocusWipe).Length >= SessionEventKinds.MaxFocusWipes || _events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - delayMs) return false;
        return _events.ScheduleAfter(SessionEventKinds.FocusWipe, GameEventPayload.Of([new(map, entity)], now, delayMs), delayMs, EventClocks(now)).Disposition != GameEventScheduleDisposition.Rejected;
    }

    /// <summary>The pending Combat-Focus Wipe events: the NPC, its map and the deadline, in queue order.</summary>
    public IReadOnlyList<(string Entity, SessionMapId Map, long DeadlineMs)> QueryFocusWipes()
    {
        ThrowIfDisposed();
        return Array.AsReadOnly(_events.QueryPending(SessionEventKinds.FocusWipe)
            .Select(e => (e.Payload.Entities[0].Code, new SessionMapId(e.Payload.Entities[0].Map), e.DeadlineMs)).ToArray());
    }

    /// <summary>
    /// The handler of the event (0x45ECE0). It needs the pass world's life facts of the NPC (a pass that cannot tell consumes the event, as a map that is not shown has no firing) and the world's cells (<see cref="ISessionBodyWorld"/>) for the
    /// distance: the dead NPC's cell, and the focus's, the hero's own (the actor's cell, the original compares the raw coordinates whatever map the hero is on) or a critter's of the same map. A focus the world cannot place (a critter that is gone
    /// or elsewhere) counts as far away: the reference is stale and goes. An NPC the world cannot place keeps the event (it is queued again, nothing else is done); a revived NPC ends it. A distance under <see cref="SessionEventKinds.FocusWipeTiles"/> queues the event again (a new
    /// revision for the queue only); otherwise the danger record's focus is cleared in one revision.
    /// </summary>
    [OriginalFunction(0x45ECE0, OriginalFunctionScope.Partial)]
    internal GameEventHandlerOutcome FireFocusWipe(GameEventFiring firing)
    {
        var at = firing.Event.Payload.Entities[0];
        var map = new SessionMapId(at.Map);
        if (_passWorld?.CritterFacts(map, at.Code) is not { } lifeFacts || !_life.Critters.TryGetValue(at.Code, out var saved)) return GameEventHandlerOutcome.Refused;
        var target = SessionLifeTarget.Critter(at.Code, lifeFacts, saved.State);
        if (Resolve(target, out var record, out var facts, out _) != GameSessionFailure.None) return GameEventHandlerOutcome.Refused;
        // 0x45D8D0: only a dead one (a missing object counts as dead too, and has no focus to read); a revived one is left.
        if (!LifeRules.Evaluate(record.State, facts!).IsDead) return GameEventHandlerOutcome.Completed;
        var held = _danger.Get(at.Code);
        if (held.Focus is not { } focus) return GameEventHandlerOutcome.Completed;
        var world = PassWorldAs<ISessionBodyWorld>();
        // A world that cannot place the NPC (the original always reads a location) keeps the event alive: it asks again ten minutes on.
        if (world?.BodyCell(map, at.Code) is not { } body)
        {
            ScheduleFocusWipe(at.Code, at.Map, firing.NowMs);
            return GameEventHandlerOutcome.Completed;
        }
        CellCoord? placed = focus.IsHero ? _actor.Cell : world.BodyCell(map, focus.Entity!);
        if (placed is { } there && Math.Max(Math.Abs(there.X - body.X), Math.Abs(there.Y - body.Y)) < SessionEventKinds.FocusWipeTiles)
        {
            // A queue that cannot take it leaves the focus standing (the bound is the session's, not the original's).
            ScheduleFocusWipe(at.Code, at.Map, firing.NowMs);
            return GameEventHandlerOutcome.Completed;
        }
        // 0x406F20 with the null handle; no record budget is needed (the record already exists).
        if (_revision == long.MaxValue) return GameEventHandlerOutcome.Refused;
        _danger.Put(held with { Focus = null });
        Revise();
        return GameEventHandlerOutcome.Completed;
    }
}
