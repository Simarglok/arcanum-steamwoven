# Game event queue runtime (core)

Status: **Implemented bounded component**, ARC-WORLD-022, slice E1 of the
[event queue research](../research/game-event-queue.md) (ARC-RES-046, top-20 item 3). It is a pure, Godot-free and
session-free queue in `Arcanum.Simulation.Events`: kinds, ordering, scheduling, cancellation, the due pass and a
detached snapshot with strict restore. **Not wired**: the game session does not own a queue, no snapshot schema carries
it, no host calls a pass and no gameplay kind exists yet. Those are slices E2 (session owner and snapshot section,
after ARC-COMBAT-002 in the schema lane), E3 (host pump) and E4 (the consumers' kinds).

Requirements served in part: [FR-SCRIPT-003](../requirements/story-social-ai.md#fr-script-003) (deferred calls that
survive a save: the queue and its snapshot, not yet saved with the session) and
[FR-RUNTIME-001](../requirements/world-travel-runtime.md#fr-runtime-001) (only due work runs, independent of frame
rate: the pass, not yet pumped by a frame).

## What it follows

The source behaviour is read in the research note; this component takes these decisions from it:

| Rule | Decision | Source of the decision |
| --- | --- | --- |
| Three clocks | Every kind has a domain: real, game or animation time. A pass handles the domains in the order real, game, animation, each against its own clock value, which the caller passes in `GameEventClocks`. The queue owns no clock, so turn-based freezing of game time is the caller's: with game time held and animation time running, game events wait and animation events fire. | K1..K3, Q6 |
| Absolute deadlines | Scheduling by delay adds the delay to the domain's clock; scheduling at a deadline uses it as is. A delay of 0 is stored as 1 ms (an immediate event is due at the next pass at the earliest). Delay and deadline are checked signed 64-bit milliseconds; a negative delay or deadline and an overflowing sum are refused. | Q1, Q2 |
| Equal deadlines | First scheduled fires first, by a strictly increasing sequence number that the snapshot persists. The source's newest-first order is not reproduced. | owner decision OQ1, TE3 |
| One queue | All maps share one queue; each event carries a scope map (0 for a global kind, else the map of its entities). An event of a map that is not active stays pending and fires late when the map is active again. | owner decision OQ3 |
| Same-pass exclusion | Events scheduled while a pass runs (by a handler) are not eligible in that pass, whatever their deadline: the pass admits only sequence numbers below the one it started with. | Q4, Q5 |
| Budget | A pass takes at most its budget of events off the queue (fired or dropped), one budget for all three domains, and reports whether eligible due events were left. Default limit 500, the source's guard. | Q8, TE5 |
| Missing entities | One rule at fire time: an event that names an entity the caller reports missing is dropped with outcome `EntityMissing`, and its handler does not run. | Q6, TE6, TE9 |
| Outcomes | Every event a pass takes is reported: `Fired`, `HandlerRefused` or `EntityMissing`. | Q7, TE7 |
| Cancellation | By kind and a predicate: any, remove all, and remove first, which removes exactly the first match in queue order. | Q9, TE4 |
| Uniqueness | Per kind: many, replace, keep existing, merge, over a key made of leading payload slots. | Q10 |
| AI ticks persisted | A kind may be `SavedUnlessRebuilt`: always captured. Restore under the `Steamwoven` setting (the default) keeps their timing; under the `Original` setting (`RebuildOnLoad`) it leaves them out and returns them so the owner schedules fresh ones, as the source does on load. | owner decisions OQ2, D8, P7 |
| Real-second delays | `GameEventDelays.TryFromRealSeconds`: one checked product `seconds × 8000`, refused for negative or overflowing operands, instead of the source's 32-bit word arithmetic. | TE1 |

Not decided here: the call-at reference time (TE2) belongs to SCRIPT S5; the companion wait timer (TE8) to the party
slice after ARC-RES-024 O3; the map-change rules (which events follow the PC, which kinds are removed and rebuilt) to
E2. Time jumps need nothing extra: the caller advances its clocks and runs passes, and each due event fires once, late,
seeing the final time.

## API

| Entry | Contract |
| --- | --- |
| `GameEventRegistry(version, kinds)` | The closed, versioned kind set. A `GameEventKindDefinition` names the kind (1..64 ASCII letters, digits and `. _ : -`) and fixes its domain, persistence (`Saved`, `NotSaved`, `SavedUnlessRebuilt`), uniqueness (`Many`, `Replace`, `KeepExisting`, `Merge`), scope rule (`Global`, `EntityMap`, `ActiveMapOnly`), exact entity and value slot counts, key slots (the first `KeyEntities` entities and `KeyValues` values; none for `Many`; both 0 means one event of the kind at most), its handler, a merge function exactly for `Merge`, and an optional value check. A scoped kind needs at least one entity. Bad definitions throw `ArgumentException`. |
| `GameEventQueue(registry, limits)` | An empty queue. `GameEventQueueLimits` defaults: 16,384 pending events, 500 per pass, 4 entity and 8 value slots per event. Limits out of range, or a kind wider than them, throw `ArgumentException`. |
| `ScheduleAfter(kind, payload, delayMs, now)` / `ScheduleAt(kind, payload, deadlineMs)` | `GameEventScheduleResult`: `Added`, `Replaced` (new deadline and sequence; `Previous` is the old event), `KeptExisting` (the pending event, nothing added), `Merged` (the pending event with the merged payload, same deadline and sequence) or `Rejected` with a failure: `UnknownKind`, `InvalidPayload` (slot counts, map below 1, entity code, value check), `InvalidScope` (a scoped event's entities on two maps), `InvalidDelay`, `InvalidDeadline`, `InvalidClocks`, `TimeOverflow`, `InvalidMerge` (the merge result fails the checks or changes the key or map), `QueueFull`, `SequenceExhausted`. A refusal changes nothing. Kept and merged requests take no sequence number; a replacement needs no free slot. |
| `Any(kind, match)` / `RemoveFirst(kind, match)` / `RemoveAll(kind, match)` | Queries and cancellation in queue order. An unregistered kind throws `ArgumentException`. |
| `ProcessDue(GameEventPassRequest)` | One pass with the three clocks, the active map (0: none, only global events), a budget of 1..`MaxPerPass` and an entity-existence check. Returns `GameEventPassResult(Failure, Outcomes, BudgetExhausted)`. A pass started from a handler is `PassInProgress`; a bad request is `InvalidPass`. Each event leaves the queue before its handler runs; a handler may schedule and cancel; a handler exception propagates with its event consumed. The pass draws no randomness. |
| `QueryPending()` | All pending events, in queue order (domain, deadline, sequence). |
| `CaptureSnapshot()` | `GameEventQueueSnapshot(RegistryVersion, NextSequence, Events)`: events of saved kinds in queue order. Detached immutable records. Throws `InvalidOperationException` during a pass. |
| `GameEventQueue.Restore(registry, snapshot, profile, limits)` | Strict: registry version, next sequence at least 1, at most `MaxPending` events, each of a known saved kind with its kind's domain, a unique sequence below the next one, a nonnegative deadline, a valid payload with the scope map it implies, queue order, and no duplicate key of a unique kind (checked even among events the rebuild switch drops). Failures `InvalidSnapshot`, `RegistryMismatch`, `InvalidSequence`, `QueueFull`, `UnknownKind`, `NotPersistedKind`, `InvalidDeadline`, `InvalidPayload`, `InvalidScope`, `DuplicateKey`, with the first offending event; a failure builds nothing. Under a profile with `RebuildOnLoad` the `SavedUnlessRebuilt` events are returned in `Rebuild`. |
| `GameEventQueueProfile` | The two rule settings of owner decision D8, shaped like `CombatAnomalyProfile`: `Steamwoven` (default, name `steamwoven`) and `Original` (name `original`, `RebuildOnLoad`), with `All` and `FromName`. It holds no state and is not in the snapshot (D4), so any snapshot restores under either. Tie order has no switch: FIFO under both (OQ1). |

Entity references are `GameEventEntity(Map, Code)`: the runtime map number and the stable entity code the session uses,
never a live handle. Payloads carry entities and signed 64-bit values in the kind's fixed slot counts; a consumer slice
wraps them in its own typed record. Whether an entity still exists is the caller's answer at pass time.

Calls are caller-serialized like every owner in the project. A handler gets the event and its domain's time
(`GameEventFiring`) and the queue; it may not start a pass or take a snapshot.

## Tests

`tests/Arcanum.Simulation.Tests/Events` (synthetic kinds shaped like the source consumers, no game data): FIFO ties
across a save, the 1 ms immediate delay, same-pass exclusion of handler-scheduled events (also at a past absolute
deadline), budget carry-over across domains, domain order and frozen game time, a time jump without replay, map scope
and the inactive map, missing entities and handler refusals, the four uniqueness policies and a bad merge, removal and
queries, nested passes and handler exceptions, every scheduling refusal, registry and limit errors, the checked
real-seconds delay, detached snapshots, restore under both settings and the profile names, every restore refusal and the
last sequence number.
