using Arcanum.Simulation;
using Arcanum.Simulation.Character;
using Arcanum.Simulation.Combat;
using Arcanum.Simulation.Events;
using Arcanum.Simulation.Items;

namespace Arcanum.Session;

/// <summary>
/// ARC-MAGIC-1568: a pass world that knows the stored base stats of an NPC (the 28 entries of <c>obj_f_critter_stat_base_idx</c>, with what a spell wrote into them laid over: the viewer's combat host). The NPC's encumbrance
/// fatigue timer asks it for the strength when it fires, which gives the encumbrance level the timer is scheduled again by. A pass world without it lets the timer end after one drain.
/// </summary>
public interface ISessionNpcStatsWorld : ISessionEventWorld
{
    IReadOnlyList<int>? StoredStats(SessionMapId map, string entity);
}

/// <summary>ARC-MAGIC-1651: why an NPC's encumbrance timer chain ended although the original would have gone on (it reads the level from the live object): the session could not read the NPC's level, or could not schedule the next timer.</summary>
public enum SessionNpcEncumbranceEndReason
{
    /// <summary>The pass world is no <see cref="ISessionNpcStatsWorld"/> (it cannot give the stored stats).</summary>
    NoStatsWorld,
    /// <summary>The world gave no stored stats, or not the 28 entries: the NPC has no combat row there.</summary>
    NoStoredStats,
    /// <summary>The level is unreadable: the map's seed does not list the NPC as a holder, or its list or stats do not fit the rules (<see cref="GameSessionOwner.QueryNpcCarry"/>).</summary>
    LevelUnreadable,
    /// <summary>The level has a drain interval but the next timer could not be scheduled (queue full, life record budget, timers on another map).</summary>
    NotScheduled,
}

/// <summary>ARC-MAGIC-1651: an NPC's encumbrance timer fired and its chain ended for a <see cref="Reason"/> (a chain that ends because the level fell below 4 is the rule and is not reported).</summary>
public sealed record SessionNpcEncumbranceEnd(GameEvent Event, SessionMapId Map, string Entity, SessionNpcEncumbranceEndReason Reason);

/// <summary>
/// ARC-MAGIC-1568 (the NPC's form of the encumbrance fatigue timer; ledger WE8): the recalculation 0x45F820 schedules the fatigue event 0x45E820 in mode 1 for any critter whose new level has a drain interval, the hero or an NPC;
/// the match 0x45E8D0 compares the mode and the object, so one such event is pending for an NPC and a second schedule adds nothing. The session keeps the NPC's event as its own kind
/// (<see cref="SessionEventKinds.NpcEncumbrance"/>) on the NPC's map, among the timers that live on the one map of the NPC. The timer needs the NPC's life state, so scheduling gives an NPC without a life record the record of its loaded state.
/// </summary>
public sealed partial class GameSessionOwner
{
    /// <summary>
    /// Whether the NPC's encumbrance timer can be scheduled now on <paramref name="map"/>: one is pending already (it is kept), or the queue and the life record budget have room, the NPC's life state is valid and its other timers
    /// have no other map. The caller asks before it writes anything the timer belongs with.
    /// </summary>
    internal bool CanScheduleNpcEncumbrance(SessionLifeTarget target, SessionMapId map)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsHero || !NpcEffectEntity(target.Entity)) return false;
        if (IsPending(SessionEventKinds.NpcEncumbrance, target.Entity)) return true;
        if (Resolve(target, out _, out _, out var recorded) != GameSessionFailure.None) return false;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        if (_events.Count >= _events.Limits.MaxPending || _events.NextSequence == long.MaxValue || now > long.MaxValue - MaxEncumbranceDelayMs) return false;
        if (!recorded && _life.Critters.Count >= _limits.MaxLifeRecords) return false;
        // The NPC's timers lie on one map: one that lies on another map is not moved here (a life step moves them, the strength write does not), so the schedule is not ready.
        return StrayMaps(target.Entity!, map.RuntimeMap).Length == 0;
    }

    private static long MaxEncumbranceDelayMs => Enumerable.Range(0, CarryRules.Levels).Max(level => CarryRules.DrainIntervalMs(level) ?? 0);

    /// <summary>
    /// The schedule of the NPC's encumbrance timer (mode 1): after <paramref name="delayMs"/>, nothing while one is pending (WEA2). An NPC without a life record gets the record of its loaded state. Not scheduled when <see cref="CanScheduleNpcEncumbrance"/> says no. No revision: it is part of the change that caused it.
    /// </summary>
    /// <remarks>The fatigue event schedule 0x45E820 with its match 0x45E8D0; the hero's form is in <c>GameSessionEncumbrance</c>.</remarks>
    internal void ScheduleNpcEncumbranceDrain(SessionLifeTarget target, SessionMapId map, long delayMs)
    {
        if (!CanScheduleNpcEncumbrance(target, map) || IsPending(SessionEventKinds.NpcEncumbrance, target.Entity)) return;
        if (Resolve(target, out var record, out _, out var recorded) != GameSessionFailure.None) return;
        var now = _clock.QueryClockSnapshot().GameTimestampMs;
        var scheduled = _events.ScheduleAfter(SessionEventKinds.NpcEncumbrance, new([new GameEventEntity(map.RuntimeMap, target.Entity!)], [now]), delayMs, EventClocks(now));
        if (scheduled.Disposition == GameEventScheduleDisposition.Added && !recorded) _life.Critters[target.Entity!] = record;
    }

    /// <summary>
    /// The NPC's encumbrance fatigue timer (mode 1 of the fatigue timer, ledger WE8): nothing for an NPC the host no longer reports, one without a life record or a dead one; otherwise its steps are
    /// <c>max(1, elapsed s / 80)</c> since it was scheduled and <see cref="CarryRules.Drain"/> of the NPC's current fatigue is added to its fatigue damage (nothing below 5 fatigue, never below 4), as the hero's does. It does not
    /// check the level first; afterwards it is scheduled again with the drain interval of the level then (none below level 4). The level is the one <see cref="QueryNpcCarry"/> reads from the NPC's list in the map's seed and
    /// the stored stats the pass world gives (<see cref="ISessionNpcStatsWorld"/>); a world that cannot give them, or an NPC the seed does not list, schedules no next one and the pass reports the end (<see cref="SessionEventPassResult.NpcEncumbranceEnds"/>).
    /// </summary>
    /// <remarks>The fatigue timer 0x45E610, mode 1.</remarks>
    internal GameEventHandlerOutcome FireNpcEncumbranceDrain(GameEventFiring firing)
    {
        var at = firing.Event.Payload.Entities[0];
        var map = new SessionMapId(at.Map);
        if (_passWorld?.CritterFacts(map, at.Code) is not { } facts || !_life.Critters.TryGetValue(at.Code, out var saved)) return GameEventHandlerOutcome.Refused;
        var target = SessionLifeTarget.Critter(at.Code, facts, saved.State);
        if (Resolve(target, out var record, out var lifeFacts, out _) != GameSessionFailure.None) return GameEventHandlerOutcome.Refused;
        var status = LifeRules.Evaluate(record.State, lifeFacts!);
        if (status.IsDead) return GameEventHandlerOutcome.Completed;
        var scheduledAt = firing.Event.Payload.Values[0];
        var steps = scheduledAt <= firing.NowMs ? LifeRules.FatigueRecoverySteps(scheduledAt, firing.NowMs) : 1;
        var drain = CarryRules.Drain(status.Fatigue, steps);
        if (drain > 0)
        {
            var result = Life(Version, target, true, (state, _, _) => new LifeOutcome(state,
                state with { FatigueDamage = (int)Math.Min((long)state.FatigueDamage + drain, LifeLimits.MaxDamage) }, LifeEvents.None, 0, DeathAnimation.None), eventMap: at.Map);
            _passFirings?.Add(new(firing.Event, result));
        }
        // The level now: the NPC's list in the seed against the carry weight its stored strength gives. A chain that ends for want of that reading is reported (ARC-MAGIC-1651), one that ends below level 4 is the rule.
        var reason = SessionNpcEncumbranceEndReason.NoStatsWorld;
        if (PassWorldAs<ISessionNpcStatsWorld>() is { } statsWorld)
        {
            reason = SessionNpcEncumbranceEndReason.NoStoredStats;
            if (statsWorld.StoredStats(map, at.Code) is { Count: SessionCritterIndexedWrites.StatCount } stats)
            {
                reason = SessionNpcEncumbranceEndReason.LevelUnreadable;
                if (QueryNpcCarry(map, target, stats, stats[0]) is { } carry)
                {
                    if (CarryRules.DrainIntervalMs(carry.Level) is not { } delay) return GameEventHandlerOutcome.Completed;
                    ScheduleNpcEncumbranceDrain(target, map, delay);
                    if (IsPending(SessionEventKinds.NpcEncumbrance, at.Code)) return GameEventHandlerOutcome.Completed;
                    reason = SessionNpcEncumbranceEndReason.NotScheduled;
                }
            }
        }
        _passNpcEncumbranceEnds?.Add(new(firing.Event, map, at.Code, reason));
        return GameEventHandlerOutcome.Completed;
    }
}
