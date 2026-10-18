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
| `RelocateEntities(kind, fromMap, toMap, travels)` | ARC-SCRIPT-010: for a global, any-number kind, moves each entity on `fromMap` that travels, the event's other entities stay; deadline and sequence kept. Other kinds `UnknownKind`, maps below 1 `InvalidScope`, from a handler `PassInProgress`. |
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

**Deferred SCR calls (ARC-SCRIPT-010).** The kind `scr.call` (added to registry version 1 without a new version: the
life kinds are unchanged, a save without a deferred call is byte-identical, and a build without the kind refuses a save
that holds one as an unknown kind) is global, game time, saved, any number per key, at most
`SessionEventKinds.MaxScriptCalls` (1,024) on top of the life timers. Its entities are the triggerer and the attachee
(the player as `SessionEventKinds.PlayerEntity` `pc`; an absent role is the marker `-` on the map active when it was
scheduled), its values the game time it was set at, the delay, the script and the line. When the actor changes map, a
call's player and follower roles move with it one by one (`GameEventQueue.RelocateEntities`; the original keeps their
handles across maps), its other roles keep their map.
`ScheduleScriptCall(expected, call, delayMs)` (1 ms up to `MaxScriptCallDelayMs`) and `RemoveScriptCall(expected,
script, attachee)` (the first pending call of that script and attachee in queue order, the attachee compared by
entity id whatever map is stored, as the original compares the handle) each move one revision; `QueryScriptCalls()`
lists the pending ones. In a pass, a call whose objects the host does not report is dropped (an NPC with a life record
is asked through `CritterFacts`, any other object through `ISessionScriptWorld.ObjectExists`, which means present on its
stored map; the original drops an object that is not loaded), the others go to `ISessionScriptWorld.RunScriptCall`, which runs the
[SCR dispatcher](../scripting/scr-dispatcher-runtime.md#deferred-calls-arc-script-010-slice-s5) and applies its effects;
a host that is only an `ISessionEventWorld` consumes a due call unrun. `SessionEventPassResult.ScriptCalls` reports each.
A restore checks a saved call as a life timer (set no later than the saved clock, exactly its delay before its deadline)
and its two roles; whether its objects still exist is decided when it fires.

**Timed effects (ARC-WORLD-029).** The kind `effect.hero.end` (also added to registry version 1 without a new version) is
the END of a hero's timed effect: global, game time, saved, any number, at most `SessionEffectLimits.MaxTimedEffects` (64)
on top of the life timers and calls. No entities; values: the game time it was set at, the duration, the effect number and
the count of entries it removes. When due it removes the first entries of that number, one revision, and
`SessionEventPassResult.Effects` reports it; `EndHeroTimedEffect` runs it early and cancels it. A restore checks it as a
call (set no later than the saved clock, exactly its duration before its deadline) and refuses one without a hero or of an
innate number. See [durable effects](durable-effects-runtime.md).

A positive write is a step that changes the stored value to a positive one, or the timer's own handler writing what it
found positive (the source writes those back even when the heal rate is 0). **Deliberate difference:** the source's
damage step writes hit-point damage and the poison level on every hit, even with an unchanged value, so a hit that
changes neither still sets a missing resting or poison timer; the session sets timers only on a change. It matters
only for a critter that already has damage or poison but no pending timer: an NPC whose map data loads it wounded or
poisoned, or a follower whose resting timer ended while it had a PC leader and who then left the party. The next
change of the value sets the timer. The hero gets no resting timer: the source schedules one but its handler heals
only NPCs. Sources: fatigue (0x45E610), resting (0x45E910, 0x45EA00, 0x45EA80, 0x45EAB0), poison (0x4B1170),
poison schedule (0x4B1350), hit-point write (0x43D530), poison write (0x4B0980).

Rules of the wiring:

- **Scheduling with the step.** Every life call (`ApplyDamage`, `Kill`, `Heal`, the ticks) sets its timers in the same
  step. The session's queue holds 33,859 events (`SessionEventKinds.MaxPending`: four per NPC life record, 8,192 records
  since ARC-DATA-029, and three for the hero, plus the 1,024 deferred script calls and 64 timed-effect ENDs), so the
  timers of a step always fit; a step whose timers would not fit, or whose deadline would pass the clock's range, is
  refused whole (`EventBudgetExceeded`) with nothing drawn or changed.
- **Map of an NPC timer.** The map the NPC is on: the actor's map when a life call sets it (the host acts on NPCs of the
  map the PC is on), the timer's own map when a handler sets the next one. An NPC keeps all its timers on one map and
  one per kind whatever the map: a life step on another map than its pending timers' (a move the session did not see,
  such as a script teleport) takes them along to that map in the same step. Restore refuses an NPC with timers on two
  maps.
- **Pass.** `ProcessEvents(world, budget)` runs one pass at the session's game time with the actor's map as the active
  map. Hero timers run on any map; an NPC timer waits while its map is not active and fires late on return, with the
  elapsed time. The host's `ISessionEventWorld.CritterFacts(map, entity)` gives the NPC's life facts; an NPC it does not
  report, or one without a session life record, is dropped (`EntityMissing`). Each fired timer is one life step, so a
  changed record adds one revision; the pass itself takes no expected version, like a clock advance. A pass can change
  the saved queue without moving any revision (a dropped or ended timer, a timer set again after a step that changed
  nothing), so the revision is not a "nothing to save" signal. A pass while paused
  or in turn-based mode still fires due timers. Real and animation time are 0 in a session pass: no session kind uses them.
- **Map change.** `PlaceActor` and `ApplyChanges` to another map move the pending timers of the PC's followers to that
  map (`GameEventQueue.Relocate`; an event moves only when every entity it names is on the old map and travels). A key
  collision on the new map refuses the whole step (`EventConflict`; the one-map-per-NPC rule leaves no way to reach it),
  and a call from the host's world port during a pass is `InvalidEventPass` (so is a life step from it that would move
  an NPC's timers). Waiting and free NPCs keep their timers
  on their map. The source makes unconscious followers wait before the PC leaves; the session moves the timers of every
  follower in the party list, so a host that leaves an unconscious follower behind makes it wait first.
- **Long waits.** A wait that advances many hours and then runs one pass fires each timer once, late: fatigue and resting
  count the elapsed time, a poison tick applies one step. Stepping a long wait (the source sleeps one hour per step) is
  the host pump's job (E3).
- **Rule mode.** A restore uses the event profile of the options' rule mode (`GameRules.For(options.Rules).Events`,
  ARC-WORLD-023); no session kind is `SavedUnlessRebuilt` yet (the AI tick arrives with I6), so both modes restore the
  same timers. The poison recovery timer reads the session's current rules when it fires (`PoisonRecoveryTwice`, TE10).
- **Poison recovery value.** A recovery timer carries a second value: 1 when a poison write set it, 2 when the recovery
  tick set the next one (it then stands for the source's two events). In the Steamwoven mode a timer applies one tick;
  in the Original mode a timer with 2 applies the tick twice, the second only while poison remains. The value does not
  depend on the mode, so switching modes converts nothing.

Anomaly **TE10** (new, recorded in the [research note](../research/game-event-queue.md)
and the [verification ledger](../research/original-verification-ledger.md)): the poison recovery handler
writes the new level, which schedules a recovery event because none is pending (the firing one already left the list),
and then adds another one itself, so after the first tick two recovery events run every 120 s and the level falls
twice as fast. This was read in the retail 1.0.7.4 executable (the poison event handler 0x4B1170,
the base stat setter 0x4B0980 and the poison event scheduler 0x4B1350, ledger TE10): a bug of the original. Decision of this task under
D2: the Steamwoven mode applies one tick per period, the Original mode keeps two (rule switch `PoisonRecoveryTwice`).

### Snapshot schema 9

`events` is encoded between `life` and `lighting` (since schema 10 `reactions` sits between `life` and `events`,
[NPC reaction](npc-reaction-runtime.md#snapshot-schema-10)):

```json
"events":{"registry":1,"nextSequence":3,"pending":[
  {"sequence":1,"kind":"life.npc.fatigue","deadlineMs":54080000,"entities":[{"map":79,"entity":"oid:wolf"}],"values":[54000000]},
  {"sequence":2,"kind":"life.npc.resting","deadlineMs":57600000,"entities":[{"map":79,"entity":"oid:wolf"}],"values":[54000000]}]}
```

Pending events are in queue order. Decoding checks shapes and ranges: registry and next sequence at least 1, at most
33,859 events, 4 entities and 8 values each, a kind spelled as a code, maps at least 1 and entity ids
(`OutOfRange`, `BudgetExceeded`, missing, unknown and wrongly typed fields). Restore refuses (`InvalidEvents`) another
registry version, an unknown kind, a value count other than the kind's (2 for poison recovery, else 1), a recovery
value other than 1 or 2, an NPC with timers on two maps, a timer whose deadline is not its kind's delay after its scheduling time or that was
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
resting skipped for a follower and a dead NPC, a missing NPC dropped, a pass while paused, a queue that holds every
timer the life records can need (and restores full), a dead critter's fatigue timer ending, an NPC's timers following
it to another map, a map transfer step that carries followers' timers but not a waiting NPC's, a callback from the
world port that cannot move the PC, the TE10 switch in both modes, the canonical `events` text, restore under both
modes and the same firing afterwards, decode and restore refusals.

