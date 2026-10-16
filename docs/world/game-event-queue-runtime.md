# Game event queue runtime

Status: **Implemented bounded component.** ARC-WORLD-022 is slice E1 of the
[event queue research](../research/game-event-queue.md) (ARC-RES-046, top-20 item 3): a pure, Godot-free and
session-free queue in `Arcanum.Simulation.Events` (kinds, ordering, scheduling, cancellation, relocation, the due pass
and a detached snapshot with strict restore). ARC-WORLD-024 is slice E2 with the life-state timers of ARC-COMBAT-002:
the [game session](game-session-runtime.md) owns one queue, saves it in snapshot schema 9 and runs a pass on its
clock ([session wiring](#session-wiring-arc-world-024)). **Not wired yet**: no host calls the pass (E3), and the other
gameplay kinds (AI tick, script calls, effects) belong to their slices (E4).

Requirements served in part: [FR-SCRIPT-003](../requirements/story-social-ai.md#fr-script-003) (deferred calls that
survive a save: the queue is saved with the session; no script-call kind yet) and
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
| `Relocate(fromMap, toMap, travels)` | ARC-WORLD-024: moves every event whose entities are all on `fromMap` and all travel to `toMap` (entity maps and, when scoped, the scope map), keeping kind, deadline, sequence and values. Atomic: a key collision moves nothing (`DuplicateKey`); maps below 1 are `InvalidScope`; from a handler `PassInProgress`. |
| `ProcessDue(GameEventPassRequest)` | One pass with the three clocks, the active map (0: none, only global events), a budget of 1..`MaxPerPass` and an entity-existence check. Returns `GameEventPassResult(Failure, Outcomes, BudgetExhausted)`. A pass started from a handler is `PassInProgress`; a bad request is `InvalidPass`. Each event leaves the queue before its handler runs; a handler may schedule and cancel; a handler exception (or one from the entity check) propagates with its event consumed, and the outcomes of the earlier events of the pass are not returned. The pass draws no randomness. |
| `QueryPending()` | All pending events, in queue order (domain, deadline, sequence). |
| `CaptureSnapshot()` | `GameEventQueueSnapshot(RegistryVersion, NextSequence, Events)`: events of saved kinds in queue order. Detached immutable records. Throws `InvalidOperationException` during a pass. |
| `GameEventQueue.Restore(registry, snapshot, profile, limits)` | Strict: registry version, next sequence at least 1, at most `MaxPending` events, each of a known saved kind with its kind's domain, a unique sequence below the next one, a nonnegative deadline, a valid payload with the scope map it implies, queue order, and no duplicate key of a unique kind (checked even among events the rebuild switch drops). Failures `InvalidSnapshot`, `RegistryMismatch`, `InvalidSequence`, `QueueFull`, `UnknownKind`, `NotPersistedKind`, `InvalidDeadline`, `InvalidPayload`, `InvalidScope`, `DuplicateKey`, with the first offending event; a failure builds nothing. Under a profile with `RebuildOnLoad` the `SavedUnlessRebuilt` events are returned in `Rebuild`. |
| `GameEventQueueProfile` | The two rule settings of owner decision D8, shaped like `CombatAnomalyProfile`: `Steamwoven` (default, name `steamwoven`) and `Original` (name `original`, `RebuildOnLoad`), with `All` and `FromName`. It holds no state and is not in the snapshot (D4), so any snapshot restores under either. Tie order has no switch: FIFO under both (OQ1). |

Entity references are `GameEventEntity(Map, Code)`: the runtime map number and the stable entity code the session uses,
never a live handle. Payloads carry entities and signed 64-bit values in the kind's fixed slot counts; a consumer slice
wraps them in its own typed record. Whether an entity still exists is the caller's answer at pass time.

Calls are caller-serialized like every owner in the project. A handler gets the event, its domain's time and the
pass's three clocks (`GameEventFiring`, so it can reschedule in any domain) and the queue; it may schedule and cancel,
but not start a pass or take a snapshot. Merge functions, value checks and match predicates must not call the queue;
the queries copy the pending events before running a predicate.

Programming errors throw instead of returning a typed failure: an invalid registry or limits, a query on an
unregistered kind, a snapshot taken from a handler. Everything that depends on data or timing (scheduling, passes,
restore of saved data) returns a typed result.

Deliberate differences from the shared rules of research section 8 (E2 kept them, see the session wiring):

- `ProcessDue` takes no expected version and the queue has no revision token. The session pass takes none either, like
  a clock advance; each life step a timer makes is versioned as any session step.
- An event leaves the queue before its handler runs, rather than in one commit with the handler's session changes. A
  session handler whose life step is refused is reported as `HandlerRefused`, and the timer is consumed.
- Cancellation and queries go by kind and a predicate over the payload; a uniqueness key is a predicate on its slots.
  There is no keyed lookup method.

## Session wiring (ARC-WORLD-024)

`GameSessionOwner` owns one queue with the registry `SessionEventKinds` (version 1). Its kinds are the life-state timers
that ARC-COMBAT-002 left for the queue. All run on game time, are saved, and keep one pending event per critter and kind
(the source checks for an existing one too). Each event's only value is the game time it was set at.

| Kind | Who | Set when | Delay | When it fires |
| --- | --- | --- | --- | --- |
| `life.hero.fatigue`, `life.npc.fatigue` | hero / NPC | fatigue damage is written with a positive value | 80 s | a living critter with fatigue damage recovers `HealRate × max(1, elapsed s / 80)` ([life state](life-state-runtime.md)); a positive result sets the next |
| `life.hero.poison-damage`, `life.npc.poison-damage` | hero / NPC | the poison level is written with a positive value | 15 s | while poisoned: the poison damage tick (which may kill), then the next tick |
| `life.hero.poison-recovery`, `life.npc.poison-recovery` | hero / NPC | as above | 120 s | while poisoned: the level loses PoisonRecovery; while it stays positive the next tick |
| `life.npc.resting` | NPC | hit-point damage is written with a positive value | 1 h | a living NPC without a PC leader (free, not following or waiting) heals `HealRate × max(1, elapsed h)` hit points and three times that fatigue; positive writes set their timers again |

A positive write is a step that changes the stored value to a positive one, or the timer's own handler writing what it
found positive (the source writes those back even when the heal rate is 0). The hero gets no resting timer: the source
schedules one but its handler heals only NPCs. Sources: [fatigue][cr-fatigue], [resting][cr-rest], [poison][st-poison],
[poison schedule][st-poison-sched], [hit-point write][hp-set], [poison write][stat-set].

Rules of the wiring:

- **Scheduling with the step.** Every life call (`ApplyDamage`, `Kill`, `Heal`, the ticks) sets its timers in the same
  step. The step counts the new events first; if they do not fit in the queue (16,384 pending) the whole step is refused
  (`EventBudgetExceeded`) with nothing drawn or changed.
- **Map of an NPC timer.** The map the NPC is on: the actor's map when a life call sets it (the host acts on NPCs of the
  map the PC is on), the timer's own map when a handler sets the next one.
- **Pass.** `ProcessEvents(world, budget)` runs one pass at the session's game time with the actor's map as the active
  map. Hero timers run on any map; an NPC timer waits while its map is not active and fires late on return, with the
  elapsed time. The host's `ISessionEventWorld.CritterFacts(map, entity)` gives the NPC's life facts; an NPC it does not
  report, or one without a session life record, is dropped (`EntityMissing`). Each fired timer is one life step, so a
  changed record adds one revision; the pass itself takes no expected version, like a clock advance. A pass while paused
  or in turn-based mode still fires due timers. Real and animation time are 0 in a session pass: no session kind uses them.
- **Map change.** `PlaceActor` and `ApplyChanges` to another map move the pending timers of the PC's followers to that
  map (`GameEventQueue.Relocate`; an event moves only when every entity it names is on the old map and travels). A key
  collision on the new map refuses the whole step (`EventConflict`). Waiting and free NPCs keep their timers on their map.
- **Long waits.** A wait that advances many hours and then runs one pass fires each timer once, late: fatigue and resting
  count the elapsed time, a poison tick applies one step. Stepping a long wait (the source sleeps one hour per step) is
  the host pump's job (E3).
- **Profile.** A restore uses the event profile of the options' rule mode (`GameRules.For(options.Rules).Events`,
  ARC-WORLD-023). No session kind is `SavedUnlessRebuilt` yet (the AI tick arrives with I6), so both modes restore the
  same timers.

Anomaly **TE10** (new, recorded in the [research note](../research/game-event-queue.md#7-ce-anomalies-recorded-not-adopted)):
the source's poison recovery handler writes the new level, which schedules a recovery event because none is pending
(the firing one already left the list), and then adds another one itself, so after the first tick two recovery events
run every 120 s and the level falls twice as fast. The project keeps one (one per critter and kind), under both rule
settings; whether the original executable does the same is NOT_VERIFIED.

### Snapshot schema 9

`events` is encoded between `life` and `lighting`:

```json
"events":{"registry":1,"nextSequence":3,"pending":[
  {"sequence":1,"kind":"life.npc.fatigue","deadlineMs":54080000,"entities":[{"map":79,"entity":"oid:wolf"}],"values":[54000000]},
  {"sequence":2,"kind":"life.npc.resting","deadlineMs":57600000,"entities":[{"map":79,"entity":"oid:wolf"}],"values":[54000000]}]}
```

Pending events are in queue order. Decoding checks shapes and ranges: registry and next sequence at least 1, at most
16,384 events, 4 entities and 8 values each, a kind spelled as a code, maps at least 1 and entity ids
(`OutOfRange`, `BudgetExceeded`, missing, unknown and wrongly typed fields). Restore refuses (`InvalidEvents`) another
registry version, an unknown kind, a timer whose deadline is not its kind's delay after its scheduling time or that was
set after the saved clock, a hero timer without a hero, an NPC timer without the NPC's life record, and everything the
queue's own restore refuses (sequences, order, payloads, keys); more events than the queue holds is
`EventBudgetExceeded`.

**Migration decision: none.** Schemas 1..8 are `UnsupportedSchema`. A schema-8 quick save loads as "written by an
incompatible version" with the running session kept, and the next F7 overwrites it. No host kept timers before, so
nothing is lost.

## Tests

`tests/Arcanum.Simulation.Tests/Events` (synthetic kinds shaped like the source consumers, no game data): FIFO ties
across a save, the 1 ms immediate delay, same-pass exclusion of handler-scheduled events (also at a past absolute
deadline), budget carry-over across domains, domain order and frozen game time, a time jump without replay, map scope
and the inactive map, missing entities and handler refusals, the four uniqueness policies and a bad merge, removal and
queries, nested passes and handler exceptions, every scheduling refusal, registry and limit errors, the checked
real-seconds delay, detached snapshots, restore under both settings and the profile names, every restore refusal and the
last sequence number, and relocation (only events whose entities all travel, scope and key index follow, an atomic
collision refusal). `tests/Arcanum.Session.Tests/SessionEventTests`: hero fatigue recovery and a long wait, poison
damage and recovery with one timer of each kind, NPC timers that wait for their map and followers that carry theirs,
resting skipped for a follower and a dead NPC, a missing NPC dropped, a pass while paused, a full queue refusing a step
whole, the canonical `events` text, restore under both settings and the same firing afterwards, decode and restore
refusals.

[cr-fatigue]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1304-L1371
[cr-rest]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1421-L1526
[st-poison]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L1007-L1082
[st-poison-sched]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L1092-L1135
[hp-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L1608-L1628
[stat-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L774-L783
