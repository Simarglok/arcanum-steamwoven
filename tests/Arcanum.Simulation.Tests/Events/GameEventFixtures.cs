using System.Collections.Immutable;
using Arcanum.Simulation.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Arcanum.Simulation.Tests.Events;

/// <summary>Synthetic kinds only: each mirrors the shape of a source consumer, none carries its rules.</summary>
internal sealed class Recorder : IGameEventHandler
{
    public List<GameEventFiring> Fired { get; } = [];
    public Func<GameEventFiring, GameEventQueue, GameEventHandlerOutcome>? OnFire { get; set; }

    public GameEventHandlerOutcome Handle(GameEventFiring firing, GameEventQueue queue)
    {
        Fired.Add(firing);
        return OnFire?.Invoke(firing, queue) ?? GameEventHandlerOutcome.Completed;
    }

    public long[] Sequences => Fired.Select(f => f.Event.Sequence).ToArray();
    public long[] FirstValues => Fired.Select(f => f.Event.Payload.Values[0]).ToArray();
}

internal static class EventFixtures
{
    // AI-tick shape: one per NPC, replaced, active map only, persisted unless rebuilt.
    internal const string Tick = "test.tick";
    // Script-call shape: many per entity, map of the entity, value 0 = line.
    internal const string Call = "test.call";
    // Fatigue shape: one per (critter, sub-kind), the first stays; values = sub-kind, scheduled at.
    internal const string Fatigue = "test.fatigue";
    // Recharge shape: one per item, merged by adding the amount; global scope with an entity.
    internal const string Recharge = "test.recharge";
    // Global game-time event, value 0 = label.
    internal const string Global = "test.global";
    // Real-time host event, never saved.
    internal const string Ui = "test.ui";
    // Animation-time event.
    internal const string Anim = "test.anim";
    // At most one pending of the kind (no key slots).
    internal const string Once = "test.once";
    // Two entities, scoped to their map.
    internal const string Pair = "test.pair";

    internal static readonly GameEventClocks Zero = new(0, 0, 0);

    internal static GameEventRegistry Registry(Recorder recorder, int version = 1) => new(version,
    [
        new(Tick, GameEventDomain.Game, GameEventPersistence.SavedUnlessRebuilt, GameEventUniqueness.Replace,
            GameEventScopeRule.ActiveMapOnly, 1, 0, 1, 0, recorder),
        new(Call, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many,
            GameEventScopeRule.EntityMap, 1, 1, 0, 0, recorder),
        new(Fatigue, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.KeepExisting,
            GameEventScopeRule.EntityMap, 1, 2, 1, 1, recorder),
        new(Recharge, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Merge,
            GameEventScopeRule.Global, 1, 1, 1, 0, recorder,
            Merge: (pending, incoming) => pending with { Values = [pending.Values[0] + incoming.Values[0]] },
            Validate: p => p.Values[0] > 0),
        new(Global, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many,
            GameEventScopeRule.Global, 0, 1, 0, 0, recorder),
        new(Ui, GameEventDomain.Real, GameEventPersistence.NotSaved, GameEventUniqueness.Many,
            GameEventScopeRule.Global, 0, 1, 0, 0, recorder),
        new(Anim, GameEventDomain.Animation, GameEventPersistence.Saved, GameEventUniqueness.Many,
            GameEventScopeRule.Global, 0, 1, 0, 0, recorder),
        new(Once, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Replace,
            GameEventScopeRule.Global, 0, 0, 0, 0, recorder),
        new(Pair, GameEventDomain.Game, GameEventPersistence.Saved, GameEventUniqueness.Many,
            GameEventScopeRule.EntityMap, 2, 0, 0, 0, recorder),
    ]);

    internal static GameEventQueue Queue(Recorder recorder, GameEventQueueLimits? limits = null) =>
        new(Registry(recorder), limits);

    internal static GameEventEntity Npc(string code, int map = 1) => new(map, code);

    internal static GameEventPayload Values(params long[] values) =>
        new(ImmutableArray<GameEventEntity>.Empty, values.ToImmutableArray());

    internal static GameEventPayload With(GameEventEntity entity, params long[] values) => GameEventPayload.Of([entity], values);

    internal static GameEventClocks Game(long ms) => new(0, ms, 0);

    internal static GameEventPassRequest Pass(GameEventClocks clocks, int activeMap = 1, int budget = 500,
        Func<GameEventEntity, bool>? exists = null) => new(clocks, activeMap, budget, exists ?? (_ => true));

    internal static GameEvent Added(GameEventScheduleResult result)
    {
        Assert.AreEqual(GameEventFailure.None, result.Failure);
        Assert.IsNotNull(result.Event);
        return result.Event;
    }

    internal static void Refused(GameEventFailure failure, GameEventScheduleResult result)
    {
        Assert.AreEqual(GameEventScheduleDisposition.Rejected, result.Disposition);
        Assert.AreEqual(failure, result.Failure);
        Assert.IsNull(result.Event);
    }

    internal static GameEventPassResult Run(GameEventQueue queue, GameEventPassRequest request)
    {
        var result = queue.ProcessDue(request);
        Assert.AreEqual(GameEventFailure.None, result.Failure);
        return result;
    }
}
