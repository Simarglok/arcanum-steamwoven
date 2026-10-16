# Game event queue on the session clock: source reading and slices

Status: **Research**, work item ARC-RES-046 (2026-10-16), the research part of
item 3 of the owner's top-20 task list (project planning, outside the
repository). It reads how the pinned source schedules, orders, fires, cancels
and saves its time events, how the three source clocks behave in real time and
in turn-based combat, and what each event kind is for. It maps the consumers
that wait for a queue (the AI tick of [ARC-RES-039](npc-combat-ai-plan.md)
slice I6, the deferred script calls of [ARC-RES-042](scr-event-dispatcher.md)
slice S5, the durable effects of top-20 item 17 and the life-state timers of
item 2) and proposes `WORLD` implementation slices. It changes no production
API, viewer, session schema, launcher or other research document.

Requirements: [FR-SCRIPT-003](../requirements/story-social-ai.md#fr-script-003)
(deferred calls that survive a save) and
[FR-RUNTIME-001](../requirements/world-travel-runtime.md#fr-runtime-001)
(the frame runs only the game work that is due, the same input and time give
the same state at any frame rate). The clock it builds on is the
[game clock policy](../world/game-clock-policy.md) (ARC-RES-020) and its
[runtime](../world/game-clock-runtime.md) (ARC-WORLD-004).

## Vocabulary and evidence discipline

- **Source (S)**: read from the pinned CE specification
  [`a7ff41b300ef712f0e7d088183a3d08957110cdd`][ce] (Sustainable Use License)
  at the linked line ranges, for the meaning of the mechanism only; nothing is
  copied. CE is a reconstruction lineage with decompiler names (`sub_…`); a
  reading of a name's intent is marked **inference**. Original-executable
  parity is **NOT_VERIFIED** throughout.
- **Code (C)**: this repository at `origin/main` fc6fbb0.
- **Project policy (P)**: a proposal for this project where the source leaves
  a choice or where this note recommends a departure; open until the slice
  that owns it, or until the owner decides the questions of
  [section 9](#9-questions-for-the-owner).
- **Event** is one scheduled record: a kind, a deadline and up to four
  parameters. **Kind** is one of the 35 source types (`TIMEEVENT_TYPE_*`).
  **List** is the per-clock sorted queue that holds an event. **Ping** is one
  call of the source's per-frame time function. **Due** means the list's clock
  has reached the deadline.
- **CE anomaly** is a branch that looks like a reconstruction slip or an
  unintended edge, numbered TE1..TE8 in [section 7](#7-ce-anomalies-recorded-not-adopted).
  Under owner decision [D2](npc-combat-ai-plan.md#owner-decisions-2026-10-16)
  the slice that implements the branch decides it; nothing is adopted silently.

No original file, probe or game data was read for this work item (cloud
session, no installed game).

## Answer

- **One scheduler, three clocks.** The source keeps three sorted linked lists,
  one per clock: real time, game time and "animation" time. The kind fixes the
  list ([type table][te-table]). Game time and animation time both run 8× the
  admitted real interval; turn-based combat freezes game time only, so
  animation events keep running in a round ([ping][te-ping]). Of the 35 kinds,
  23 run on game time, 10 on real time and 2 (animation goals, animated
  lights) on animation time.
- **Absolute deadlines, sorted insert.** Scheduling adds a delay to the list's
  current clock (or to a caller's base) and inserts the event **in front of**
  every queued event with the same deadline ([add][te-add], [delay][te-delay]).
  A zero delay is stored as 1 ms ([delay value][te-dt]), so even an
  "immediate" event is due one millisecond later. There is no event identity:
  callers find their events again by kind plus a predicate over parameters.
- **Due processing in a ping.** Each ping first advances the clocks, then for
  each list in the order real, game, animation pops due events from the head
  and runs the kind's handler; the handler's result is ignored. An event whose
  object parameter no longer resolves (destroyed, or recovery from its saved
  object ID fails) is dropped without running ([ping][te-ping],
  [recover][te-recover]). Events scheduled while a ping is running go to a
  side list and are merged after the ping, so they never fire in the ping that
  created them; "immediate" events skip the side list but are 1 ms in the
  future and wait for the next ping anyway ([merge][te-merge]). A guard stops
  a list after 500 events in one ping and leaves the rest for later pings.
- **Time jumps do not replay periods.** Sleep, world-map travel, a timed
  teleport, the end of a turn-based round (+1 s) and a script teleport with a
  time argument add a duration to game and animation time at once
  ([jump][te-inc]). The events that became due then fire on the following
  pings in deadline order, each seeing the final time. Periodic kinds do not
  catch up period by period: their handlers store the scheduling time in a
  parameter, scale the effect by the elapsed time (fatigue, resting, poison
  rate, recharge) and reschedule from *now* ([fatigue][cr-fatigue],
  [resting][cr-rest], [recharge][mt-recharge]).
- **Save, map change, restart.** The global save writes the three clock values
  and, per list, the events of saveable kinds with their parameters by type;
  object parameters are written as object IDs ([save][te-save],
  [load][te-load]). On a PC map change, events whose object parameters all
  travel with the PC move to the destination map's `TimeEvent.dat`, events
  that reference anything left behind are parked in the old map's file, and
  events without object parameters stay global; seven kinds (animation,
  magic/tech, AI, combat, turn-based combat, world map, teleported) are
  cleared on map close ([teleport][te-teleported], [map close][te-mapclose],
  [park][te-park], [map open][te-mapopen]). A parked event fires late, at the
  first ping after the PC returns. AI ticks are never saved: every NPC gets a
  new immediate tick when its sector's objects are folded in
  ([AI tick][ai-schedule], [fold][fold-list]).
- **Consumers.** The AI tick (250..5000 ms by distance, first tick +0..5 s
  jitter), deferred script calls (call-in counts real seconds × 8, call-at a
  time of day), magic maintenance and recharge, poison (15 s damage, 120 s
  recovery), fatigue recovery every 80 s, resting heal every game hour for
  free NPCs, body decay, item decay, combat-focus wipe of the dead, lock and
  jam resets, store restock, scenery respawn, traps, newspapers, random
  encounters, the companion wait timeout and the turn-based subturn step all
  live on this one mechanism ([section 3](#3-event-kinds-and-consumers-s)).
- **Steamwoven today (C).** The session owns the normalized game clock with
  pause reasons (`Arcanum.Simulation/Clock`, `GameSessionOwner.AdvanceGameTime`),
  the viewer's ambient host admits host time 5..250 ms × 8
  (`GameTimeFlow`, `AreaAmbient`) and the snapshot (schema 6) stores the clock.
  There is no event record, queue, pump or event section in the snapshot.
- **Proposal.** One session-owned, Godot-free queue of typed events on the
  game clock, ordered by (deadline, sequence), with a closed kind registry
  that fixes domain, persistence, uniqueness and map scope per kind, a pass
  that fires only due events of the active scope under a budget and reports
  typed outcomes, and an event section in the snapshot. Five slices E1..E5
  ([section 8](#8-proposed-implementation-slices-world)): the pure core E1
  touches no schema and can start now; the session integration E2 raises the
  snapshot schema and therefore waits for top-20 items 1 (RNG) and 2 (life
  state) in the one-at-a-time schema lane.

## 1. Clocks, admission and freezing (S)

The admission rules (ping intervals under 5 ms ignored, 250 ms clamp, the
nested pause counter, the 8× rate) are already fixed in the
[clock policy](../world/game-clock-policy.md#separate-time-domains-and-source-admission)
and in `GameTimeFlow`; this section adds only what the queue needs.

| ID | Claim | Source |
| --- | --- | --- |
| K1 | Three clock values, each a (day, millisecond) pair: real time starts at 0, game and animation time at the module start hour. The event record is that pair, a kind and four parameters (integer, object handle, location or float, chosen per kind). | [types][te-h-types], [record][te-h-event], [init][te-init] |
| K2 | A ping adds the admitted interval to real time always; with the pause counter at 0 it adds 8× to animation time and, unless turn-based combat is active, 8× to game time. | [ping 811–841][te-ping] |
| K3 | In turn-based combat game-time events therefore wait, animation-time events (animation goals, animated lights) keep firing, and real-time events (the subturn step, UI) keep firing. | [ping][te-ping], [type table][te-table] |
| K4 | The end of a turn-based round adds 1 s to game and animation time ([round end][cb-round]). The same jump function serves sleep (1 game hour per real-time step of 200 ms, healing per hour; [sleep][sleep]), world-map travel ([travel][wmap-travel]), a timed teleport ([teleport][tp-time]), the script teleport's time argument ([script teleport][scr-teleport]) and a debug key (+1 h, `main.c` 517). It ignores the pause counter and turn-based mode. | [jump][te-inc] |
| K5 | The game's module ping runs every module's ping in registration order; the time-event module is one of them, so the queue fires inside the frame between other modules (map, sound, multiplayer pings). The main menu loop calls the time-event ping directly before the module ping. | [modules][gamelib-modules], [module ping][gamelib-ping], [menu loop][menu-loop] |
| K6 | Game time read by gameplay is the game clock (quests, rumors, dialog stamps, reputation, curses and blessings store it); the scheduler is the only place that advances it. | e.g. [quest.c 436][quest-ts], [script.c 3371][script-cache-ts] |

## 2. The scheduler (S)

| ID | Claim | Source |
| --- | --- | --- |
| Q1 | Scheduling by delay sets the deadline to the list's clock (or a non-zero caller base) plus the delay; scheduling by base uses the base as the deadline as is. Both return the deadline to the caller when asked. A kind number out of range or editor mode refuses; allocation failure exits the process. | [add][te-add], [delay][te-delay] |
| Q2 | A delay is built from milliseconds with the day word 0; 0 ms becomes 1 ms. Addition carries into the day only for a sum strictly above 86,400,000 ms. | [datetime helpers][te-dt] |
| Q3 | Insert walks the sorted list and stops at the first event whose deadline is **not earlier**, so a new event goes in front of every event with the same deadline (ties fire newest first). | [add 1085–1091][te-add] |
| Q4 | While a ping is running, a non-immediate event goes to a side list of its clock; after the ping each side-list event is dropped if any of its object parameters is an invalid handle (no recovery is tried; the null handle counts as valid, [handle check][obj-valid]), otherwise it is inserted into its list. | [add 1079–1083][te-add], [merge][te-merge] |
| Q5 | An "immediate" event (AI first tick, animation goal, teleported object) is inserted straight into its list even during a ping, with the 1 ms deadline of Q2, so it is not due in the ping that created it either. | [immediate][te-add], [ai immediate][ai-schedule], [anim][anim-immediate], [map][map-teleported] |
| Q6 | A ping pops the head of each list while the list's clock is at or past its deadline, in the list order real, game, animation. Before the handler runs, every object parameter is revalidated: an invalid handle is recovered from the stored object ID; a failed recovery, a destroyed object or a map that is clearing its objects drops the event without running it (a debug line only). | [ping 843–897][te-ping], [recover][te-recover] |
| Q7 | The handler's boolean result is ignored. An optional per-kind exit hook would run after the handler; CE notes that no kind has one. | [ping 874–884][te-ping], [type table][te-table] |
| Q8 | A loop guard counts processed events across the lists of one ping; above 500 it logs the last kind, resets the count and leaves the current list. The next list starts counting from 0. | [ping 886–895][te-ping] |
| Q9 | Cancellation and queries go by kind plus a caller predicate over the parameters: remove all matches, remove the first match, test for any match, test for any event of the kind. Each of them walks the side list too. "Remove first" stops after one match in the main list **and** after one in the side list. | [clear typed][te-cleartyped], [any][te-any] |
| Q10 | Uniqueness is the caller's business, by one of four patterns: replace (remove the NPC's AI tick, then add), keep the existing one (fatigue, resting, poison, the turn-based step, a magic run's tick: add only when none matches), merge into the existing one (recharge: the query predicate adds the new amount to the queued event's parameter) or allow many (script calls, traps, UI messages). | [ai][ai-schedule], [fatigue][cr-fatigue-sched], [poison][st-poison-sched], [tb step][cb-tb-step], [magic][mt-unique], [recharge][mt-recharge] |
| Q11 | Reset clears every list and restarts the clocks; it is the module reset, so a new game or a load starts from an empty queue. | [init/reset][te-init], [clear][te-clear] |

## 3. Event kinds and consumers (S)

Domain: **G** game, **R** real, **A** animation. Saved: in the global save
and in the map files. Map close: cleared when a map closes. Delays are game
milliseconds unless the domain is R. The handler column names the owning
module; the Steamwoven column names the slice that would own the kind
(P, [section 8](#8-proposed-implementation-slices-world)).

| Kind | Dom. | Saved | Map close | Scheduled by, delay | Steamwoven owner |
| --- | --- | --- | --- | --- | --- |
| Anim | A | yes | cleared | every animation goal step; base = the run's next ping time ([anim][anim-sched]) | animation scheduler with real-time combat (top-20 item 9), domain A (E5) |
| Script | G | yes | — | call-in, call-at ([script][scr-call]); fires `SAP_USE` of the named script from the stored line with the stored triggerer and attachee ([handler][scr-process]); remove-call removes the first match on script and attachee ([remove][scr-remove]) | SCRIPT S5 on E2 |
| MagicTech | G | yes | cleared | maintain or END tick after `period × 1000 × 8` ms, delayed interrupt (1 ms), deferred BEGIN (2 ms); keyed by run id, one per run ([maintain][mt-maintain], [unique][mt-unique], [interrupt][mt-interrupt]) | MAGIC slices and durable effects (item 17) |
| Poison | G | yes | — | damage every 15 s, recovery every 120 s, one of each per critter ([schedule][st-poison-sched], [handler][st-poison]) | life state (item 2) kinds on E2 |
| Resting | G | yes | — | 1 game hour after HP damage, one per critter; heals `heal rate × hours since scheduling` for an NPC without a PC leader ([resting][cr-rest]) | life state kinds |
| Fatigue | G | yes | — | recovery 80 s after fatigue damage (10 real s), damage by encumbrance (600 s, 60 s, 5 s for the three heaviest levels, none below); recovers `elapsed s / 80 × heal rate`; one per critter and type ([schedule][cr-fatigue-sched], [handler][cr-fatigue], [set][cr-fatigue-set]) | life state kinds |
| Aging, Combat, Ambient Lighting, Clock | G | yes | Combat cleared | no-op handlers or host callbacks (ambient light and the clock widget are host functions set at start; [funcs][te-funcs]) | ambient lighting is host cadence already ([clock policy P3](../world/game-clock-policy.md#p3--document-only-pure-hour-consumer)); the rest not needed |
| AI | G | **no** | cleared | NPC tick, replace pattern, delay `250 + 4750 × min(dist, 30) / 30` ms, first tick +0..5000 ms random ([tick][ai-tick], [delay][ai-delay], [schedule][ai-schedule]) | ARC-RES-039 I6 on E2 |
| TB Combat | R | yes | cleared | the subturn step, 2 ms, at most one ([tb step][cb-tb-step], [handler][cb-tb]) | turn-based round (item 8); in Steamwoven a direct call, not a timer (P) |
| WorldMap, Sleeping, MainMenu, UI, MP Ctrl UI, Multiplayer, Debug, Bkg Anim, Fidget Anim | R | WorldMap only | WorldMap cleared | travel step 62 ms ([travel][wmap-travel]), sleep step 200 ms ([sleep][sleep]), UI messages, menu, background and fidget animations | host (Godot) timers, not session state (P) |
| NPC Wait Here | G | yes | — | companion wait timeout 15 min..30 days by wait index ([wait][ai-wait]); anomaly O3 of [ARC-RES-024](companion-follow-evidence.md#open-questions) | party slice after O3 |
| Light | A | no | — | animated light frame, `1000 / fps` ([light][light]) | renderer (P) |
| Lock | G | yes | — | relock 1 h after an unlock, unjam 1 day after a jam ([lock][lock]) | door/container slice |
| NPC Respawn | G | yes | — | store restock 12..24 h random, replace pattern ([respawn][respawn]) | barter slice |
| Recharge Magic-Item | G | yes | — | 60 s × 8, merged per item ([recharge][mt-recharge]) | MAGIC slices |
| Decay Dead Bodie | G | yes | — | 1 day for a critter, 2 days for another object ([decay][cr-decay]) | life state (death) |
| Item Decay | G | yes | — | 2 days for a dropped item, retried after 60 s ([item decay][item-decay]) | inventory owner |
| Combat-Focus Wipe | G | yes | — | 10 min after an NPC dies; clears the dead NPC's focus (its killer) once the killer is ≥ 30 cells away, else reschedules ([wipe][cr-wipe]) | AI slice I3 |
| Newspapers | G | yes | — | rotation every 24 h ([newspapers][newspaper]) | later |
| Traps | G | yes | — | a delayed trap spell, `delay × 1000` ms ([trap][trap]) | skills/traps slice |
| Fade | G | yes | — | the script teleport's delayed fade-in ([fade][scr-teleport]) | host |
| Teleported | G | no | cleared | immediate, per object flagged as teleported when a map loads ([map][map-teleported]) | M3 transition coordinator |
| Scenery Respawn | G | yes | — | `delay × 8000` ms, or `−delay` minutes, or 1 day + 1 ms when 0 ([scenery][scenery]) | later |
| Random Encounter | G | yes | — | 300..700 game minutes ([encounter][encounter]) | world map |

Counts: 23 G, 10 R, 2 A; 24 saved, 11 not.

**Elapsed-time handlers.** Resting, fatigue, poison recovery, item decay,
combat-focus wipe, recharge and body decay put the game second of scheduling
into an integer parameter and compute the effect from the difference to the
current game time when they fire ([seconds since reference][te-seconds]).
That is what makes late firing (after a jump, a load or a return to a parked
map) apply the whole elapsed span once instead of replaying each period.

## 4. Persistence and map change (S)

| ID | Claim | Source |
| --- | --- | --- |
| P1 | The time-event module saves three clock pairs, then for each list a count and the events of saveable kinds: deadline, kind, and each parameter by the kind's type flags (integer 4 bytes, location 8, float 4, object through the object-ID writer). The count is patched after the walk. Side lists are not saved (a save runs between pings). | [save][te-save] |
| P2 | Load reads the three clocks, then each event, and inserts it sorted; object handles are recovered later, when the event fires (Q6). The module is registered after the map module, so map objects exist before the queue loads. | [load][te-load], [modules][gamelib-modules] |
| P3 | On a PC teleport to another map: animation and magic/tech events are cleared, then every event whose object parameters are **all** travelling with the PC is appended to the destination's `Save\Current\maps\<map>\TimeEvent.dat` and removed. | [teleport notify][te-teleported], [caller][tp-notify] |
| P4 | When the old map closes: the seven kinds of the "map close" column are cleared; during a teleport every event that has an object parameter **not** travelling (a null handle counts as not travelling) is appended to the old map's file and removed. Events of kinds without object parameters stay in memory and keep running on any map. | [map close][te-mapclose], [park][te-park], [clear objects][map-clear] |
| P5 | When a map opens in game: object handles of all queued events are force-recovered, then the map's file (if any) is loaded into the queue and deleted. Overdue events from it fire at the first ping. | [map open][te-mapopen], [caller][map-open] |
| P6 | The magic module parks and restores its own active runs with the same map-file mechanism; the reading does not follow it further. | [teleport notify][te-teleported] |
| P7 | AI ticks are not saved and are cleared on map close; folding a sector's objects schedules an immediate first tick per non-destroyed NPC (its "first" marker compares the sector's stored time with now + 3 s, ARC-RES-042 H1), and creating an NPC schedules a normal one. A load therefore restarts every NPC's tick with fresh jitter. | [type table][te-table], [fold][fold], [fold list][fold-list], [create][create-ai] |

Consequence (inference): in CE an event belongs to the map of the objects it
names, not to the map where it was scheduled, and time on a parked map is not
frozen: deadlines are absolute, so returning after three days lets one day's
body decay fire at once and a resting NPC heal for the whole absence.

## 5. Dependent work: what each consumer needs from the queue (S → P)

| Consumer | Source behaviour it needs | Queue requirement |
| --- | --- | --- |
| AI tick, [ARC-RES-039 I6](npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research) | One tick per NPC (replace), distance-scaled delay, first-tick jitter, ticks keep firing in turn-based mode but do nothing there (the tick re-checks turn-based and only reschedules; the round itself runs the heartbeat at subturn start, ARC-RES-042 H5), immediate tick when a sector loads, no save. | Replace-by-key, immediate (1 ms) scheduling, map scope "active map only", rebuild on activation, deterministic order; the random jitter comes from the session RNG (top-20 item 1) under a labelled roll. |
| SCR deferred calls, [ARC-RES-042 S5](scr-event-dispatcher.md#10-proposed-implementation-slices) | Call-in delay `seconds × 1000 × 8`, call-at delay to a time of day (TE1, TE2), remove-call removes the first call matching script and attachee, the call runs `SAP_USE` from the stored line with stored roles, saved. | Allow-many kind with a typed payload (script, line, triggerer, attachee), remove-first by key, persistence with entity references, scope by referenced entities. |
| Durable effects, top-20 item 17, and MAGIC slices | Timed spells: MAINTAIN / END ticks per run, one pending tick per run, delayed interrupt; recharge of worn mana items; effect expiry. [ARC-RES-043](magic-core-evidence.md) fixes the durations (real seconds × 8). | Keep-existing, merge (recharge) and replace-by-key, cancel by key when a run ends, persistence, elapsed-aware handlers. |
| Life state, top-20 item 2 | Fatigue recovery every 80 s, resting heal hourly for free NPCs, poison damage/recovery, body decay, combat-focus wipe. | Keep-existing by (critter, sub-kind), scheduling time in the payload, elapsed computation at fire time. Item 2 can ship the formulas as pure functions over elapsed time; the timer kinds land on E2. |
| Turn-based round, top-20 item 8 | Game time frozen in a round, +1 s per round, subturn step as a 2 ms real-time event. | The +1 s is an explicit clock advance followed by a pass; the subturn step is a direct call in the round owner, not a queued event (P). |
| Real-time combat, top-20 item 9 | Animation goals on animation time that runs while game time is frozen. | A second domain (A) with its own clock: slice E5, together with the animation scheduler. |
| Companion wait timeout | 15 min..30 days by wait index. | Waits for O3 of ARC-RES-024; then one keep-existing kind per follower. |

## 6. Steamwoven today (C)

| Area | State | Code |
| --- | --- | --- |
| Game clock | delivered: normalized signed64 game milliseconds since day 0 midnight, pause disposition with reasons None/User/Menu/TurnBased, ordinary advance stops while paused, explicit wait advances while paused, revision/generation tokens | `src/Arcanum.Simulation/Clock` ([runtime](../world/game-clock-runtime.md)) |
| Host admission | delivered: 5 ms minimum, 250 ms clamp, × 8 | `GameTimeFlow` |
| Session | owns the clock; `AdvanceGameTime` clears the recorded lighting when the hour changes; snapshot schema 6 carries the clock state | `src/Arcanum.Session/GameSessionOwner.cs`, `GameSessionTypes.cs` |
| Host pump | the area viewer's ambient host observes host time, advances the session clock and offers an explicit wait by hours | `tools/terrain-viewer/Core/AreaAmbient.cs` |
| Event queue | **absent**: no event record, kind registry, pass, map scope or snapshot section | — |
| Animation domain | absent (no second clock that runs in a round) | — |

## 7. CE anomalies recorded, not adopted

| ID | Source behaviour | Note / proposed default (P) |
| --- | --- | --- |
| TE1 | Call-in and the magic maintenance and recharge delays build `seconds × 1000` ms and then multiply only the millisecond word by 8 ([call-in][scr-call], [maintain][mt-maintain], [recharge][mt-recharge]). `seconds × 1000` overflows a signed int above 2,147,483 s; the × 8 wraps the 32-bit word above 536,870 s (about 6.2 real days, 49.7 game days); a negative operand becomes a huge unsigned delay. | Likely unintended edges, harmless for normal operands. Default: one checked signed64 delay `seconds × 8000`, typed refusal for negative or overflowing operands. |
| TE2 | Call-at subtracts the module start time (15:00 for the selected startup) from the current time before taking the time of day, so the operand counts seconds from the start hour, not from midnight, while the displayed clock counts from midnight ([call-at][scr-call], [start hour][te-start]). A target equal to now gives delay 0 → 1 ms (fires now, not tomorrow). | Unknown whether slip or convention: decide in SCRIPT S5 with the S0 census of call-at operands and, if possible, one original run. Until then the source arithmetic is the documented behaviour. |
| TE3 | Ties fire newest first for events scheduled outside a ping; events merged after a ping go in front of older equals, and among themselves keep creation order (Q3, Q4). | An implementation artefact, not a rule. Default: FIFO by a persisted sequence number (question Q1). |
| TE4 | "Remove first" can remove two events, one from the main list and one from the side list (Q9). Remove-call during a ping can therefore cancel two calls. | Default: remove exactly the first match in queue order. |
| TE5 | The loop guard is shared across the three lists and resets when it trips, so the per-list budget depends on earlier lists (Q8). | Default: a fixed per-pass budget, reported in the pass result. |
| TE6 | A side-list event with a stale handle is dropped at merge without the object-ID recovery that a firing event gets (Q4 against Q6). | Default: one validation rule at fire time, with a typed "entity missing" outcome. |
| TE7 | Handler results are ignored and dropped events leave only a debug line (Q6, Q7); nothing tells the caller or a trace that a timer did not run. | Default: typed outcomes in the pass result and the trace. |
| TE8 | The companion wait timer is scheduled only for an extraordinary-charisma leader and never gets its NPC parameter ([wait][ai-wait]). | Already open as [ARC-RES-024 O3](companion-follow-evidence.md#open-questions); listed here for completeness. |

## 8. Proposed implementation slices (`WORLD`)

All slices are proposals; codes are allocated when each task is created.
Shared rules for every slice, in the project style (immutable records,
explicit limits, typed results, Godot-free assemblies):

- **Record.** `GameEvent {Sequence, Kind, Domain, DeadlineMs, Scope, Payload}`
  with one typed payload record per kind instead of four untyped parameters;
  entity references are session entity codes plus map, never live handles.
- **Order.** `(DeadlineMs, Sequence)`; `Sequence` is a persisted, strictly
  increasing signed64 counter (TE3). A delay of 0 is stored as 1 ms, as in the
  source (Q2), so "immediate" stays "next pass"; the delay is a checked
  signed64 in the clock's millisecond domain.
- **Kind registry.** Closed and versioned: per kind the domain, whether it is
  persisted, the uniqueness policy (replace, keep existing, merge, many) and its key,
  and the scope rule (global, the map of its referenced entities, active map
  only and rebuilt). An unknown kind, a missing handler or a payload that
  fails validation is refused at scheduling, never dropped later.
- **Pass.** `ProcessDue(expected version, budget)` reads the clock once,
  takes due events of the active scope in order, but never one scheduled in
  the same pass (Q4/Q5), validates its entity references, runs the kind's
  handler port with `{event, deadline, now}` and commits the removal with the
  handler's own session changes. The result lists processed, dropped (with
  reason: entity missing, scope inactive, handler refused) and whether due
  events remain because the budget ran out (TE5, TE6, TE7). The source budget
  (500) is the proposed default; the pass consumes no randomness itself.
- **Time jumps.** Explicit waits, travel, a timed teleport and the turn-based
  round end advance the clock and then drain passes; long waits advance in
  the caller's steps (sleep: 1 h) with a drain after each step, as the source
  interleaves them. Handlers of periodic kinds compute from elapsed time and
  reschedule from now; nothing replays per period.
- **Pause.** Paused or turn-based game time does not advance by ordinary
  admission, but a pass still fires events that are already due (the
  [clock policy](../world/game-clock-policy.md#separate-time-domains-and-source-admission)
  already records that the source does not suppress due processing).

| Slice | Needs | Result |
| --- | --- | --- |
| E1 Queue core | clock runtime (delivered) | `Arcanum.Simulation` pure type: record, order, kind registry, schedule by delay and by absolute deadline, remove first / remove all / any by kind and key, uniqueness policies, scope eligibility, `ProcessDue` with budget and typed outcomes, explicit limits (proposed: 16,384 pending events, 500 per pass, payload bounds per kind). Synthetic tests: ties, same-pass exclusion, budget carry-over, uniqueness, refusal of unknown kinds, overflow, detached snapshots. **No session or schema change, so it can start now.** |
| E2 Session owner and snapshot | E1, top-20 items 1 and 2 merged (schema lane) | `GameSessionOwner` owns one queue; clock advances and explicit waits drain passes; map activation and the PC's map change apply the scope rules (events that only reference travelling entities follow the PC, others stay with their map and become eligible when it is active again, AI-style kinds are removed and rebuilt); snapshot event section with strict validation (unknown kind, duplicate or non-increasing sequence, negative deadline, duplicate unique key, unresolvable entity refused) and the next schema version. |
| E3 Host pump | E2 | The area viewer's ambient host calls the pass after each admitted advance and after each wait step; a diagnostic overlay line (pending count, next deadline, last pass outcomes). FR-RUNTIME-001 acceptance: one recorded sequence of admitted durations replayed at different frame rates gives equal snapshots. |
| E4 First kinds | E2 and the owning slice | Each consumer slice adds its kinds with handlers: the AI tick (I6, with the RNG jitter label), script call-in / call-at / remove-call (SCRIPT S5, TE1/TE2 decided there), the life-state timers (fatigue 80 s, resting 1 h, poison 15 s/120 s, body decay), effect expiry and magic ticks (item 17 and MAGIC), the companion wait timeout after O3. The queue slice itself ships only a synthetic kind. |
| E5 Animation domain | E2, animation scheduler (item 9) | A second clock that runs at the game rate while game time is frozen in a round, with its own list and pass, for animation goals and real-time combat cadence. |

Order: E1 now (no schema), E2 after items 1 and 2 in the schema lane (and
before item 6 if that is next in the lane), then E3; the E4 kinds arrive with
their owners (I6 and S5 first); E5 with real-time combat.

## 9. Questions for the owner

| ID | Question | Recommendation |
| --- | --- | --- |
| Q1 | Ties between events with the same deadline: first scheduled first (FIFO), or the source's newest first? | FIFO by sequence. The source order is an artefact of the list insert (TE3) and depends on whether the event was created inside a ping; FIFO is deterministic and easy to explain in a trace. |
| Q2 | AI ticks are not saved in the source, so a quick load restarts every NPC's tick with fresh jitter. Persist the active map's AI ticks instead? | Persist them. A save then reloads into exactly the same timing, which the seeded replays of the AI plan (design rule 3) need; the source behaviour stays available as a named "rebuild on load" switch for comparison runs. |
| Q3 | Keep one queue with a map scope per event, or per-map event files like the source? | One queue in the session snapshot with a scope field. It keeps the source semantics (events follow the map of their objects, parked events fire late on return) without separate files and keeps a save atomic. |

## Unknowns

- **U1** Whether the original executable orders tied events and caps a ping
  like CE (Q3, Q8) is NOT_VERIFIED.
- **U2** The intent of the call-at reference (TE2) and the operand range
  scripts use (S0 census on the Mac).
- **U3** How the magic module re-creates its tick events for runs parked with
  a map (P6) is not read.
- **U4** The exact first-tick marker of a folded sector (P7) is not read
  beyond ARC-RES-042 H1.
- **U5** Multiplayer paths (the client clock budget and the host's time
  packets in the ping) are out of scope.

## Method

Read at the pin: `timeevent.h`, `timeevent.c` in full, and the scheduling and
handler sites of every kind in `ai.c`, `script.c`, `critter.c`, `stat.c`,
`combat.c`, `magictech.c`, `item.c`, `object.c`, `trap.c`, `light.c`,
`newspaper.c`, `teleport.c`, `map.c`, `sector_object_list.c`, `gamelib.c`,
`obj_pool.c`, `anim.c`, `anim_private.c`, `ui/sleep_ui.c`, `ui/wmap_ui.c`,
`ui/wmap_rnd.c`, `ui/intgame.c`, `ui/anim_ui.c` and `ui/mainmenu_ui.c`. Code
state from `origin/main` fc6fbb0. Earlier research reused, not re-read:
ARC-RES-020 (clock), ARC-RES-024 (companion), ARC-RES-039 (AI plan),
ARC-RES-040 (load map), ARC-RES-042 (dispatcher), ARC-RES-043 (magic). No CE
code or structure was copied; names in this note are descriptive.

[ce]: https://github.com/alexbatalov/arcanum-ce/tree/a7ff41b300ef712f0e7d088183a3d08957110cdd
[te-h-types]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.h#L10-L54
[te-h-event]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.h#L72-L95
[te-table]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L92-L128
[te-start]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L130-L134
[te-seconds]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L221-L238
[te-dt]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L282-L346
[te-init]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L506-L567
[te-funcs]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L534-L550
[te-save]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L569-L676
[te-load]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L678-L761
[te-ping]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L800-L915
[te-recover]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L923-L969
[te-merge]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L971-L1008
[te-add]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1010-L1111
[te-delay]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1119-L1151
[te-clear]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1217-L1246
[te-mapclose]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1248-L1272
[te-cleartyped]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1274-L1460
[te-any]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1462-L1520
[te-inc]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1522-L1539
[te-park]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1555-L1677
[te-mapopen]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1679-L1753
[te-teleported]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/timeevent.c#L1755-L1873
[gamelib-modules]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L148-L160
[gamelib-ping]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/gamelib.c#L493-L503
[menu-loop]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/mainmenu_ui.c#L1741-L1747
[obj-valid]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/obj_pool.c#L735-L743
[quest-ts]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/quest.c#L436
[script-cache-ts]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L3371
[ai-wait]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L1456-L1561
[ai-tick]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2859-L2938
[ai-delay]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L2998-L3012
[ai-schedule]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/ai.c#L3035-L3065
[fold]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4150-L4158
[create-ai]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4284-L4288
[fold-list]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/sector_object_list.c#L456-L471
[scr-process]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L559-L574
[scr-teleport]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2455-L2496
[scr-call]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L1981-L2014
[scr-remove]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/script.c#L2665-L2670
[cr-fatigue-set]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L462-L483
[cr-fatigue]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1304-L1371
[cr-fatigue-sched]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1378-L1405
[cr-rest]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1421-L1526
[cr-decay]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1562-L1587
[cr-wipe]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/critter.c#L1613-L1663
[st-poison]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L1007-L1082
[st-poison-sched]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/stat.c#L1092-L1135
[cb-tb-step]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3041-L3076
[cb-tb]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3079-L3103
[cb-round]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/combat.c#L3322-L3329
[mt-maintain]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3929-L3938
[mt-unique]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L3971-L3984
[mt-interrupt]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L5406-L5430
[mt-recharge]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/magictech.c#L6584-L6606
[respawn]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L2167-L2189
[item-decay]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/item.c#L4487-L4555
[scenery]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L2586-L2601
[lock]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/object.c#L4060-L4136
[trap]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/trap.c#L440-L451
[light]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/light.c#L380-L424
[newspaper]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/newspaper.c#L225-L238
[encounter]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_rnd.c#L1444-L1452
[anim-sched]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim.c#L3841-L3849
[anim-immediate]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/anim_private.c#L749-L758
[map-teleported]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1333-L1354
[map-clear]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L1570-L1580
[map-open]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/map.c#L836-L840
[tp-time]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L184-L189
[tp-notify]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/game/teleport.c#L217-L222
[sleep]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/sleep_ui.c#L558-L660
[wmap-travel]: https://github.com/alexbatalov/arcanum-ce/blob/a7ff41b300ef712f0e7d088183a3d08957110cdd/src/ui/wmap_ui.c#L3941-L3964
