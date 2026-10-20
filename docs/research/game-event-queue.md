# Game event queue on the session clock: source reading and slices

Status: **Research**, work item ARC-RES-046 (2026-10-16), the research part of
item 3 of the owner's top-20 task list (project planning, outside the
repository). It reads how the earlier source reading (F0) schedules, orders, fires, cancels
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

- **Source (S)**: an earlier source reading (provenance in git history),
  cited by the executable address of its function; level F0 (not checked in
  the executable) unless a ledger row is cited; for the meaning of the
  mechanism only. Several functions have no known name; a reading of their
  intent is marked **inference**.
  Original-executable parity is **NOT_VERIFIED** throughout, except the rows
  the [verification ledger](original-verification-ledger.md) checked (section 7).
- **Code (C)**: this repository at `origin/main` f500b76.
- **Project policy (P)**: a proposal for this project where the source leaves
  a choice or where this note recommends a departure; open until the slice
  that owns it decides, except OQ1..OQ3, which the owner decided in
  [section 9](#9-owner-decisions-2026-10-16) (Q1..Q11 in section 2 are
  scheduler claims, not questions).
- **Event** is one scheduled record: a kind, a deadline and up to four
  parameters. **Kind** is one of the 35 source types (`TIMEEVENT_TYPE_*`).
  **List** is the per-clock sorted queue that holds an event. **Ping** is one
  call of the source's per-frame time function. **Due** means the list's clock
  has reached the deadline.
- **Anomaly** is a branch that looks like a reconstruction slip or an
  unintended edge, numbered TE1..TE10 in [section
  7](#7-anomalies-recorded-not-adopted). Under owner decision
  [D2](npc-combat-ai-plan.md#owner-decisions-2026-10-16) the slice that
  implements the branch decides it; nothing is adopted silently.

No original file, probe or game data was read for this work item (cloud
session, no installed game).

## Answer

- **One scheduler, three clocks.** The source keeps three sorted linked lists,
  one per clock: real time, game time and "animation" time. The kind fixes the
  list (type table). Game time and animation time both run 8× the
  admitted real interval; turn-based combat freezes game time only, so
  animation events keep running in a round (ping (0x45B370)). Of the 35 kinds,
  23 run on game time, 10 on real time and 2 (animation goals, animated
  lights) on animation time.
- **Absolute deadlines, sorted insert.** Scheduling adds a delay to the list's
  current clock (or to a caller's base) and inserts the event **in front of**
  every queued event with the same deadline (add (0x45B800, 0x45B820, 0x45B860, 0x45B880, 0x45B8A0, 0x45B8C0), delay (0x45BA30)).
  A zero delay is stored as 1 ms (delay value (0x45A950, 0x45A970, 0x45A9B0, 0x45A9E0, 0x45AA10, 0x45AA50, 0x45AAA0)), so even an
  "immediate" event is due one millisecond later. There is no event identity:
  callers find their events again by kind plus a predicate over parameters.
- **Due processing in a ping.** Each ping first advances the clocks, then for
  each list in the order real, game, animation pops due events from the head
  and runs the kind's handler; the handler's result is ignored. An event whose
  object parameter names a destroyed object, or whose handle cannot be
  recovered from its saved object ID, is dropped without running; an object
  that was already lost when the event was loaded or re-inserted becomes a
  null parameter and the handler runs with it (ping (0x45B370),
  recover (0x45B610, 0x45B620), TE9). Events scheduled while a ping is running go to a
  side list and are merged after the ping, so they never fire in the ping that
  created them; "immediate" events skip the side list but are 1 ms in the
  future and wait for the next ping anyway (merge (0x45B750, 0x45B7A0)). A guard leaves
  a list once more than 500 events ran in one ping and keeps the rest for
  later pings.
- **Time jumps do not replay periods.** Sleep, world-map travel, a timed
  teleport, the end of a turn-based round (+1 s) and a script teleport with a
  time argument add a duration to game and animation time at once
  (jump (0x45C1C0, 0x45C200)). Sleep and travel (and the round end when the turn-based
  step ends it) jump inside a real-time handler, so the game events that
  became due fire in the same ping, because the game list is processed after
  the real list; a jump made between pings (an end-turn key press) lets them
  fire at the next ping. Events beyond the 500-event guard wait for later
  pings. All fire in deadline order, each seeing the final time. Periodic
  kinds do not catch up period by period: fatigue, resting and recharge scale
  the effect by the time elapsed since scheduling (fatigue (0x45E610),
  resting (0x45E910, 0x45EA00, 0x45EA80, 0x45EAB0), recharge (0x459590)); poison, decay and the focus
  wipe apply one step, late; all of them reschedule from *now*.
- **Save, map change, restart.** The global save writes the three clock values
  and, per list, the events of saveable kinds with their parameters by type;
  object parameters are written as object IDs (save (0x45AED0, 0x45B050),
  load (0x45B120, 0x45B200)). On a PC map change, events whose object parameters all
  travel with the PC move to the destination map's `TimeEvent.dat`, events
  that reference anything left behind are parked in the old map's file, and
  events without object parameters stay global; this parking ignores the save
  flag, so a travelling follower's AI tick is carried too (P3); seven kinds
  (animation, magic/tech, AI, combat, turn-based combat, world map,
  teleported) are cleared on map close (teleport (0x45C720, 0x45C7B0), map
  close (0x45BCE0), park (0x45C2F0, 0x45C500), map open (0x45C580, 0x45C610)). A parked
  event fires late, at the first ping after the PC returns. AI ticks are never
  saved: every NPC gets a new immediate tick when its sector's objects are
  folded in (AI tick (0x4AD730, 0x4AD790, 0x4AD7D0), fold (0x4F1CD0)).
- **Consumers.** The AI tick (250..5000 ms by distance, +0..5 s jitter on the
  tick after the first), deferred script calls (call-in multiplies its seconds
  by 8, read as real seconds by inference; call-at a time of day), magic
  maintenance and recharge, poison (15 s damage, 120 s recovery), fatigue
  recovery every 80 s, resting heal every game hour for free NPCs, body decay,
  item decay, combat-focus wipe of the dead, lock and jam resets, store
  restock, scenery respawn, traps, newspapers, random encounters, the
  companion wait timeout and the turn-based subturn step all live on this one
  mechanism ([section 3](#3-event-kinds-and-consumers-s)).
- **Steamwoven today (C).** The session owns the normalized game clock with
  pause reasons (`Arcanum.Simulation/Clock`, `GameSessionOwner.AdvanceGameTime`),
  the viewer's ambient host admits host time 5..250 ms × 8
  (`GameTimeFlow`, `AreaAmbient`) and the snapshot (schema 7) stores the clock
  and the RNG state.
  There is no event record, queue, pump or event section in the snapshot.
- **Proposal.** One session-owned, Godot-free queue of typed events on the
  game clock, ordered by (deadline, sequence), with a closed kind registry
  that fixes domain, persistence, uniqueness and map scope per kind, a pass
  that fires only due events of the active scope under a budget and reports
  typed outcomes, and an event section in the snapshot. Five slices E1..E5
  ([section 8](#8-proposed-implementation-slices-world)): the pure core E1
  touches no schema and can start now; the session integration E2 raises the
  snapshot schema and therefore waits for top-20 item 2 (life state) in the
  one-at-a-time schema lane; item 1 (session RNG, ARC-WORLD-019) is merged.

## 1. Clocks, admission and freezing (S)

The admission rules (ping intervals under 5 ms ignored, 250 ms clamp, the
nested pause counter, the 8× rate) are already fixed in the
[clock policy](../world/game-clock-policy.md#separate-time-domains-and-source-admission)
and in `GameTimeFlow`; this section adds only what the queue needs.

| ID | Claim | Source |
| --- | --- | --- |
| K1 | Three clock values, each a (day, millisecond) pair: real time starts at (0 days, 1 ms), game and animation time at the module start hour. The event record is that pair, a kind and four parameters (integer, object handle, location or float, chosen per kind). | types, record, init (0x45AD90, 0x45AE20, 0x45AE80, 0x45AEC0) |
| K2 | A ping adds the admitted interval to real time always; with the pause counter at 0 it adds 8× to animation time and, unless turn-based combat is active, 8× to game time. | ping (0x45B370) |
| K3 | In turn-based combat game-time events therefore wait, animation-time events (animation goals, animated lights) keep firing, and real-time events (the subturn step, UI) keep firing. | ping (0x45B370), type table |
| K4 | The end of a turn-based round adds 1 s to game and animation time (round end (0x4B7740)). The same jump function serves sleep (1 game hour per real-time step of 200 ms, healing per hour; sleep (0x57B710, 0x57B7F0)), world-map travel (travel (0x564C20)), a timed teleport (teleport (0x4D3460)), the script teleport's time argument (script teleport (0x447220)) and a debug key (+1 h, main loop (0x401560)). It ignores the pause counter and turn-based mode. | jump (0x45C1C0, 0x45C200) |
| K5 | The game's module ping runs every module's ping in registration order; the time-event module is one of them, so the queue fires inside the frame between other modules (map, sound, multiplayer pings). The main menu loop calls the time-event ping directly before the module ping. | modules, module ping (0x402580), menu loop (0x541590) |
| K6 | Game time read by gameplay is the game clock (quests, rumors, dialog stamps, reputation, curses and blessings store it); the scheduler is the only place that advances it. | e.g. quest 0x4C4E60, script 0x44C390 |

## 2. The scheduler (S)

| ID | Claim | Source |
| --- | --- | --- |
| Q1 | Scheduling by delay sets the deadline to the list's clock (or a non-zero caller base) plus the delay; scheduling by base uses the base as the deadline as is. Both return the deadline to the caller when asked. A kind number out of range or editor mode refuses; allocation failure exits the process. | add (0x45B800, 0x45B820, 0x45B860, 0x45B880, 0x45B8A0, 0x45B8C0), delay (0x45BA30) |
| Q2 | A delay is built from milliseconds with the day word 0; 0 ms becomes 1 ms. Addition carries into the day only for a sum strictly above 86,400,000 ms. | datetime helpers (0x45A950, 0x45A970, 0x45A9B0, 0x45A9E0, 0x45AA10, 0x45AA50, 0x45AAA0) |
| Q3 | Insert walks the sorted list and stops at the first event whose deadline is **not earlier**, so a new event goes in front of every event with the same deadline (ties fire newest first). | add (0x45B800, 0x45B820, 0x45B860, 0x45B880, 0x45B8A0, 0x45B8C0) |
| Q4 | While a ping is running, a non-immediate event goes to a side list of its clock; after the ping each side-list event is dropped if any of its object parameters is an invalid handle (no recovery is tried; the null handle counts as valid, handle check (0x4E5470)), otherwise it is inserted into its list. | add (0x45B800, 0x45B820, 0x45B860, 0x45B880, 0x45B8A0, 0x45B8C0), merge (0x45B750, 0x45B7A0) |
| Q5 | An "immediate" event (AI first tick, an animation goal in turn-based mode when it is not the actor's turn (otherwise the goal waits 5 ms), teleported object) is inserted straight into its list even during a ping, with the 1 ms deadline of Q2, so it is not due in the ping that created it either. | immediate (0x45B800, 0x45B820, 0x45B860, 0x45B880, 0x45B8A0, 0x45B8C0), ai immediate (0x4AD730, 0x4AD790, 0x4AD7D0), anim (0x44D730), map (0x410C50) |
| Q6 | A ping pops the head of each list while the list's clock is at or past its deadline, in the list order real, game, animation. Before the handler runs, every object parameter is revalidated: an invalid handle is recovered from the stored object ID; a failed recovery, a destroyed object or a map that is clearing its objects drops the event without running it; only a failed recovery prints a debug line. A null object ID (for example an object already lost at load or re-insert, TE9; a parameter that was null when scheduled takes the same branch) only nulls the handle, and the handler runs. | ping (0x45B370), recover (0x45B610, 0x45B620) |
| Q7 | The handler's boolean result is ignored. An optional per-kind exit hook would run after the handler; as read, no kind has one. | ping (0x45B370), type table |
| Q8 | A loop guard counts processed events across the lists of one ping; above 500 it logs the last kind, resets the count and leaves the current list. The next list starts counting from 0. | ping (0x45B370) |
| Q9 | Cancellation and queries go by kind plus a caller predicate over the parameters: remove all matches, remove the first match, test for any match, test for any event of the kind. Each of them walks the side list too. "Remove first" stops after one match in the main list **and** after one in the side list. | clear typed (0x45BD70, 0x45BE40, 0x45BF10, 0x45BFF0), any (0x45C0E0, 0x45C140) |
| Q10 | Uniqueness is the caller's business, by one of four patterns: replace (remove the NPC's AI tick, then add), keep the existing one (fatigue, resting, poison, the turn-based step, a magic run's tick: add only when none matches), merge into the existing one (recharge: the query predicate adds the new amount to the queued event's parameter) or allow many (script calls, traps, UI messages). | ai (0x4AD730, 0x4AD790, 0x4AD7D0), fatigue (0x45E820), poison (0x4B1350), tb step (0x4B7080), magic (0x4548D0), recharge (0x459590) |
| Q11 | Reset clears every list and restarts the clocks; it is the module reset, so a new game or a load starts from an empty queue. | init/reset (0x45AD90, 0x45AE20, 0x45AE80, 0x45AEC0), clear (0x45BC20) |

## 3. Event kinds and consumers (S)

Domain: **G** game, **R** real, **A** animation. Saved: in the global save
(the map-change parking of P3/P4 writes any kind still queued, saved or not).
Map close: cleared when a map closes. Delays are game milliseconds unless the
domain is R. The handler column names the owning module; the Steamwoven column
names the slice that would own the kind (P, [section
8](#8-proposed-implementation-slices-world)).

| Kind | Dom. | Saved | Map close | Scheduled by, delay | Steamwoven owner |
| --- | --- | --- | --- | --- | --- |
| Anim | A | yes | cleared | every animation goal step; base = the run's next ping time (anim (0x423C80)) | animation scheduler with real-time combat (top-20 item 9), domain A (E5) |
| Script | G | yes | — | call-in, call-at (script (0x447220)); fires `SAP_USE` of the named script from the stored line with the stored triggerer and attachee (handler (0x445140)); remove-call removes the first match on script and attachee (remove (0x447220)) | SCRIPT S5 on E2 |
| MagicTech | G | yes | cleared | maintain or END tick after `period × 1000 × 8` ms, delayed interrupt (1 ms), a 2 ms re-run of the run's current action (read as a deferred BEGIN, inference); keyed by run id, one per run (maintain (0x454790), unique (0x4548D0), interrupt (0x4571E0), re-run (0x455C30)) | MAGIC slices and durable effects (item 17) |
| Poison | G | yes | — | damage every 15 s, recovery every 120 s, one of each per critter (schedule (0x4B1350), handler (0x4B1170)) | life state (item 2) kinds on E2 |
| Resting | G | yes | — | 1 game hour after HP damage, one per critter; heals `heal rate × hours since scheduling` for an NPC without a PC leader (resting (0x45E910, 0x45EA00, 0x45EA80, 0x45EAB0)) | life state kinds |
| Fatigue | G | yes | — | recovery 80 s after fatigue damage (10 real s), damage by encumbrance (600 s, 60 s, 5 s for the three heaviest levels, none below); recovers `elapsed s / 80 × heal rate`; one per critter and type (schedule (0x45E820), handler (0x45E610), set (0x45D5A0)) | life state kinds |
| Aging, Combat, Ambient Lighting, Clock | G | yes | Combat cleared | no-op handlers or host callbacks (ambient light and the clock widget are host functions set at start; funcs (0x45AE20)) | ambient lighting: the source's hourly timer cadence is recorded in [clock policy P3](../world/game-clock-policy.md#p3--document-only-pure-hour-consumer), which proposes a pure hour query instead; the rest not needed |
| AI | G | **no** | cleared | NPC tick, replace pattern, delay `250 + 4750 × min(dist, 30) / 30` ms; an immediate first tick, then +0..5000 ms random on the tick it schedules (tick (0x4AD200), delay (0x4AD5D0, 0x4AD610), schedule (0x4AD730, 0x4AD790, 0x4AD7D0)) | ARC-RES-039 I6 on E2 |
| TB Combat | R | yes | cleared | the subturn step, 2 ms, at most one (tb step (0x4B7080), handler (0x4B7150)) | turn-based round (item 8); in Steamwoven a direct call, not a timer (P) |
| WorldMap, Sleeping, MainMenu, UI, MP Ctrl UI, Multiplayer, Debug, Bkg Anim, Fidget Anim | R | WorldMap only | WorldMap cleared | travel step 62 ms (travel (0x564C20)), sleep step 200 ms (sleep (0x57B710, 0x57B7F0)), UI messages, menu, background and fidget animations | host (Godot) timers, not session state (P) |
| NPC Wait Here | G | yes | — | companion wait timeout by wait index: 0, 15 min, 2 h, 1 day, 7 days, 30 days (wait (0x4AA7A0, 0x4AA8C0, 0x4AA990, 0x4AAA30)); anomaly O3 of [ARC-RES-024](companion-follow-evidence.md#open-questions) | party slice after O3 |
| Light | A | no | — | animated light frame, `1000 / fps` (light (0x4D8590, 0x4D8620, 0x4D8660)) | renderer (P) |
| Lock | G | yes | — | 1 h after an unlock of a locked object, 1 day after a jam of a free one; either event locks, unjams and closes the object (lock (0x441DD0, 0x441E90, 0x441F10); [ledger LK3–LK5](original-verification-ledger.md#doors-containers-and-locks-arc-res-065)) | lock state owner ARC-WORLD-050 |
| NPC Respawn | G | yes | — | store restock 12..24 h random, replace pattern (respawn (0x4640C0)) | barter slice |
| Recharge Magic-Item | G | yes | — | 60 s × 8 (read as 60 real seconds, inference), merged per item (recharge (0x459590)) | MAGIC slices |
| Decay Dead Bodie | G | yes | — | 1 day for a critter, 2 days for another object (decay (0x45EBE0)) | life state (death) |
| Item Decay | G | yes | — | 2 days for a dropped item; retried after 60 s while decay processing is switched off (item decay (0x467FC0, 0x468010, 0x468090, 0x468110)) | inventory owner |
| Combat-Focus Wipe | G | yes | — | 10 min after an NPC dies; clears the dead NPC's focus (its killer) once the killer is ≥ 30 cells away, else reschedules (wipe (0x45ECE0, 0x45ED70)) | AI slice I3 |
| Newspapers | G | yes | — | rotation every 24 h (newspapers) | later |
| Traps | G | yes | — | a delayed trap spell, `delay × 1000` ms (trap (0x4BC690)) | skills/traps slice |
| Fade | G | yes | — | the script teleport's delayed fade-in (fade (0x447220)) | host |
| Teleported | G | no | cleared | immediate, per object flagged as teleported when a map loads (map (0x410C50)) | M3 transition coordinator |
| Scenery Respawn | G | yes | — | `delay × 8000` ms, or `−delay` minutes, or 1 day + 1 ms when 0 (scenery (0x43F1C0)) | later |
| Random Encounter | G | yes | — | 300..700 game minutes (encounter (0x559640)) | world map |

Counts: 23 G, 10 R, 2 A; 24 saved, 11 not.

**Elapsed-time handlers.** Fatigue, resting and recharge put the game second
of scheduling into an integer parameter and compute the effect from the
difference to the current game time when they fire
(seconds since reference (0x45A7F0, 0x45A820, 0x45A840)); late firing (after a jump, a load or
a return to a parked map) therefore applies the whole elapsed span once.
Poison, item decay, body decay and the combat-focus wipe store the same
parameter but never read it: a late firing applies one step, late.
Neither group replays each missed period.

## 4. Persistence and map change (S)

| ID | Claim | Source |
| --- | --- | --- |
| P1 | The time-event module saves three clock pairs, then for each list a count and the events of saveable kinds: deadline, kind, and each parameter by the kind's type flags (integer 4 bytes, location 8, float 4, object through the object-ID writer). The count is patched after the walk. Side lists are not saved (a save runs between pings). | save (0x45AED0, 0x45B050) |
| P2 | Load reads the three clocks, then each event, and inserts it sorted. Each object parameter is resolved at once from its object ID (loading the sector at the stored location first); a failed lookup leaves a null handle, and the re-insert then stores a null object ID, so the event later fires with a null object (TE9). The module is registered after the map module, so map objects exist before the queue loads. | load (0x45B120, 0x45B200), handle reader (0x443AD0), modules |
| P3 | On a PC teleport to another map: animation and magic/tech events are cleared, then every event whose object parameters are **all** travelling with the PC is appended to the destination's `Save\Current\maps\<map>\TimeEvent.dat` and removed, **whatever its save flag**. This runs before the old map closes, so a travelling follower's AI tick (never saved otherwise) is carried to the destination and reloaded there; together with the new tick from folding the arrival sector this may give two ticks (inference, not traced). | teleport notify (0x45C720, 0x45C7B0), caller (0x4D3460) |
| P4 | When the old map closes: the seven kinds of the "map close" column are cleared; during a teleport every event that has an object parameter **not** travelling (a null handle counts as not travelling) is appended to the old map's file and removed. Events of kinds without object parameters stay in memory and keep running on any map. | map close (0x45BCE0), park (0x45C2F0, 0x45C500), clear objects (0x411520) |
| P5 | When a map opens in game: object handles of all queued events are force-recovered (a failure nulls the handle and the event later fires with a null object, TE9), then the map's file (if any) is loaded into the queue and deleted. Overdue events from it fire at the first ping. | map open (0x45C580, 0x45C610), caller (0x40FC70) |
| P6 | The magic module parks and restores its own active runs with the same map-file mechanism; the reading does not follow it further. | teleport notify (0x45C720, 0x45C7B0) |
| P7 | AI ticks are not saved and are cleared on map close (except those carried by P3); folding a sector's objects schedules an immediate first tick per non-destroyed NPC (its "first" marker compares the sector's stored time with now + 3 s, ARC-RES-042 H1), and creating an NPC schedules a normal one. A load therefore restarts every NPC's tick with fresh jitter. | type table, fold (0x441FC0), fold list (0x4F1CD0), create (0x442260) |

Consequence (inference): as read, an event belongs to the map of the objects it
names, not to the map where it was scheduled, and time on a parked map is not
frozen: deadlines are absolute, so returning after three days lets one day's
body decay fire at once and a resting NPC heal for the whole absence.

## 5. Dependent work: what each consumer needs from the queue (S → P)

| Consumer | Source behaviour it needs | Queue requirement |
| --- | --- | --- |
| AI tick, [ARC-RES-039 I6](npc-combat-ai-plan.md#implementation-slices-proposals-after-the-matching-research) | One tick per NPC (replace), distance-scaled delay, first-tick jitter, ticks run on game time, so in a round they wait until the +1 s round advance makes them due and then only reschedule (the tick re-checks turn-based mode; the round itself runs the heartbeat at subturn start, ARC-RES-042 H5), immediate tick when a sector loads, no save. | Replace-by-key, immediate (1 ms) scheduling, map scope "active map only", rebuild on activation, the active map's ticks persisted in the snapshot with a named "rebuild on load" switch for the source behaviour (owner decision OQ2), deterministic order; the random jitter comes from the session RNG (ARC-WORLD-019, merged) under a labelled roll. |
| SCR deferred calls, [ARC-RES-042 S5](scr-event-dispatcher.md#10-proposed-implementation-slices) | Call-in delay `seconds × 1000 × 8` (real seconds by inference), call-at delay to a time of day (TE1, TE2), remove-call removes the first call matching script and attachee, the call runs `SAP_USE` from the stored line with stored roles, saved. | Allow-many kind with a typed payload (script, line, triggerer, attachee), remove-first by key, persistence with entity references, scope by referenced entities. |
| Durable effects, top-20 item 17, and MAGIC slices | Timed spells: MAINTAIN / END ticks per run, one pending tick per run, delayed interrupt; recharge of worn mana items; effect expiry. [ARC-RES-043](magic-core-evidence.md) fixes the durations (real seconds × 8). | Keep-existing, merge (recharge) and replace-by-key, cancel by key when a run ends, persistence, elapsed-aware handlers. |
| Life state, top-20 item 2 | Fatigue recovery every 80 s, resting heal hourly for free NPCs, poison damage/recovery, body decay, combat-focus wipe. | Keep-existing by (critter, sub-kind); scheduling time in the payload and elapsed computation at fire time for fatigue and resting, one step per firing for poison and decay. Item 2 can ship the formulas as pure functions over elapsed time; the timer kinds land on E2. |
| Turn-based round, top-20 item 8 | Game time frozen in a round, +1 s per round, subturn step as a 2 ms real-time event. | The +1 s is an explicit clock advance followed by a pass; the subturn step is a direct call in the round owner, not a queued event (P). |
| Real-time combat, top-20 item 9 | Animation goals on animation time that runs while game time is frozen. | A second domain (A) with its own clock: slice E5, together with the animation scheduler. |
| Companion wait timeout | 15 min..30 days by wait index. | Waits for O3 of ARC-RES-024; then one keep-existing kind per follower. |

## 6. Steamwoven today (C)

| Area | State | Code |
| --- | --- | --- |
| Game clock | delivered: normalized signed64 game milliseconds since day 0 midnight, pause disposition with reasons None/User/Menu/TurnBased, ordinary advance stops while paused, explicit wait advances while paused, revision/generation tokens | `src/Arcanum.Simulation/Clock` ([runtime](../world/game-clock-runtime.md)) |
| Host admission | delivered: 5 ms minimum, 250 ms clamp, × 8 | `GameTimeFlow` |
| Session | owns the clock; `AdvanceGameTime` clears the recorded lighting when the hour changes; snapshot schema 7 carries the clock state and the session RNG (ARC-WORLD-019) | `src/Arcanum.Session/GameSessionOwner.cs`, `GameSessionTypes.cs` |
| Host pump | the area viewer's ambient host observes host time, advances the session clock and offers an explicit wait by hours | `tools/terrain-viewer/Core/AreaAmbient.cs` |
| Event queue | **absent**: no event record, kind registry, pass, map scope or snapshot section | — |
| Animation domain | absent (no second clock that runs in a round) | — |

## 7. Anomalies recorded, not adopted

**Original check (ARC-RES-048):** these rows were checked in the retail 1.0.7.4 executable; the verdicts in the [verification ledger](original-verification-ledger.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019) supersede the earlier readings below where they differ.

| ID | Source behaviour | Note / proposed default (P) |
| --- | --- | --- |
| TE1 | Call-in and the magic maintenance and END periods build `seconds × 1000` ms and then multiply only the millisecond word by 8 (call-in (0x447220), maintain (0x454790)). `seconds × 1000` overflows a signed int above 2,147,483 s; the × 8 wraps the 32-bit word above 536,870 s (about 6.2 real days, 49.7 game days); the unsigned millisecond sum of the add wraps a little earlier, from about 526,070 s depending on the time of day (datetime helpers (0x45A950, 0x45A970, 0x45A9B0, 0x45A9E0, 0x45AA10, 0x45AA50, 0x45AAA0)); a negative operand becomes a huge unsigned delay. The recharge delay uses the same pattern with a constant 60 s and cannot overflow. | Likely unintended edges, harmless for normal operands. Default: one checked signed64 delay `seconds × 8000`, typed refusal for negative or overflowing operands. Decided in ARC-SCRIPT-010: the Original setting keeps the 32-bit count (`CallDelayWraps`), Steamwoven computes without wrapping and reads a negative count as 0 (8 ms, as 0 seconds); the later time-of-day sum wrap is not modelled. |
| TE2 | Call-at subtracts the module start time (the start-hour key of the module's startup resource, 15:00 for the selected startup per the [clock policy](../world/game-clock-policy.md#source-clock-startup-and-calendar); 15 when the resource is absent, static default 10) from the current time before taking the time of day, so the operand counts seconds from the start hour, not from midnight, while the displayed clock counts from midnight (call-at (0x447220), start hour, startup (0x4B9C30)). A target equal to now gives delay 0 → 1 ms (fires now, not tomorrow). | Unknown whether slip or convention, but nearly moot: the [ARC-RES-045 census](scr-census.md#action-types-d) finds call-at in 1 line of the data and in no live line, against 304 call-in lines (209 live, 125 scripts). SCRIPT S5 may keep the source arithmetic behind its switch or refuse call-at; call-in carries TE1. |
| TE3 | Ties fire newest first for events scheduled outside a ping; events merged after a ping go in front of older equals, and among themselves keep creation order (Q3, Q4). | An implementation artefact, not a rule. Decided: FIFO by a persisted sequence number (owner decision OQ1). |
| TE4 | "Remove first" can remove two events, one from the main list and one from the side list (Q9). Remove-call during a ping can therefore cancel two calls. | Default: remove exactly the first match in queue order. |
| TE5 | The loop guard is shared across the three lists and resets when it trips, so the per-list budget depends on earlier lists (Q8). | Default: a fixed per-pass budget, reported in the pass result. |
| TE6 | A side-list event with a stale handle is dropped at merge without the object-ID recovery that a firing event gets (Q4 against Q6). | Default: one validation rule at fire time, with a typed "entity missing" outcome. |
| TE7 | Handler results are ignored and dropped events leave at most a debug line (Q6, Q7); nothing tells the caller or a trace that a timer did not run. | Default: typed outcomes in the pass result and the trace. |
| TE8 | As first read, the companion wait timer is scheduled only for an extraordinary-charisma leader and never gets its NPC parameter (wait (0x4AA7A0, 0x4AA8C0, 0x4AA990, 0x4AAA30)). | A reconstruction slip: the original schedules the timer when the leader's charisma is **not** extraordinary and the NPC is not mind-controlled, and it carries the NPC's handle (ledger row Wait timer (C12, O3, TE8)); this answers [ARC-RES-024 O3](companion-follow-evidence.md#open-questions). |
| TE9 | An object lost before the event fires — not found when the event was loaded (P2), or whose handle fails the force recovery at map open (P5) — turns into a null parameter, and the handler runs with a null object instead of the event being dropped like one whose object is destroyed (Q6). Most life-state handlers return early on a null object, but the deferred script call still runs `SAP_USE` with a null triggerer or attachee (script call (0x445140), handle reader (0x443AD0), recover (0x45B610, 0x45B620)). | Default: one rule, "entity missing" is a typed drop at fire time, whatever the path. |
| TE10 | The poison recovery handler writes the new level through the stat setter, which schedules a recovery event because none is pending (the firing one already left its list), and then adds another itself, so from the first tick on two recovery events run every 120 s (poison (0x4B1170), poison schedule (0x4B1350), stat write (0x4B0980)). Found by ARC-WORLD-024. | The original 1.0.7.4 executable does the same (`stat_poison_timeevent_process` 0x4B1170, `stat_base_set` 0x4B0980, `poison_timeevent_schedule` 0x4B1350; [ledger](original-verification-ledger.md#scripts-event-queue-and-randomness-arc-res-042-arc-res-045-arc-script-007-arc-res-046-arc-world-019)): a bug of the original. Decided in ARC-WORLD-024 under D2: Steamwoven applies one tick per period; the Original rule mode keeps two (switch `PoisonRecoveryTwice`). |

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
  persisted, the uniqueness policy (replace, keep existing, merge, many) and
  its key, and the scope rule (global, the map of its referenced entities,
  active map only; an active-map kind is rebuilt on activation and, for the
  AI tick, persisted while active, OQ2). An unknown kind, a missing handler or a
  payload that fails validation is refused at scheduling, never dropped later.
- **Pass.** `ProcessDue(expected version, budget)` reads the clock once,
  takes due events of the active scope in order, but never one scheduled in
  the same pass (Q4/Q5), validates its entity references, runs the kind's
  handler port with `{event, deadline, now}` and commits the removal with the
  handler's own session changes. Events of an inactive scope are skipped
  and stay pending. The result lists processed, dropped (with reason: entity
  missing, handler refused) and whether due events remain because the budget
  ran out (TE5, TE6, TE7, TE9). The source budget
  (500) is the proposed default; the pass consumes no randomness itself.
- **Time jumps.** Explicit waits, travel, a timed teleport and the turn-based
  round end advance the clock and then drain passes; long waits advance in
  the caller's steps (sleep: 1 h) with a drain after each step, as the source
  interleaves them. Handlers of periodic kinds compute from elapsed time and
  reschedule from now; nothing replays per period.
- **Pause.** Paused or turn-based game time does not advance by ordinary
  admission, but a pass still fires events that are already due (the [clock
  policy](../world/game-clock-policy.md#separate-time-domains-and-source-admission)
  already records that the source does not suppress due processing).

| Slice | Needs | Result |
| --- | --- | --- |
| E1 Queue core | clock runtime (delivered) | `Arcanum.Simulation` pure type: record, order, kind registry, schedule by delay and by absolute deadline, remove first / remove all / any by kind and key, uniqueness policies, scope eligibility, `ProcessDue` with budget and typed outcomes, explicit limits (proposed: 16,384 pending events, 500 per pass, payload bounds per kind). Synthetic tests: ties, same-pass exclusion, budget carry-over, uniqueness, refusal of unknown kinds, overflow, detached snapshots. **No session or schema change, so it can start now.** |
| E2 Session owner and snapshot | E1, top-20 item 2 merged (schema lane; item 1 is merged) | `GameSessionOwner` owns one queue; clock advances and explicit waits drain passes; map activation and the PC's map change apply the scope rules (events that only reference travelling entities follow the PC, others stay with their map and become eligible when it is active again, AI-style kinds of the map left are removed and rebuilt when a map is activated, so no such kind is carried by accident as in P3); one queue for all maps with the scope field, no per-map files (OQ3); snapshot event section that keeps the active map's AI ticks, restored on load unless the "rebuild on load" switch asks for the source rebuild (OQ2), with strict validation (unknown kind, duplicate or non-increasing sequence, negative deadline, duplicate unique key, unresolvable entity refused) and the next schema version. |
| E3 Host pump (done: ARC-WORLD-039, [host pump](../world/game-event-queue-runtime.md#host-pump-arc-world-039)) | E2 | The area viewer's ambient host calls the pass after each admitted advance and after each wait step; a diagnostic overlay line (pending count, next deadline, last pass outcomes). FR-RUNTIME-001 acceptance: one recorded sequence of admitted durations replayed at different frame rates gives equal snapshots. |
| E4 First kinds | E2 and the owning slice | Each consumer slice adds its kinds with handlers: the AI tick (I6, with the RNG jitter label), script call-in / call-at / remove-call (SCRIPT S5, TE1/TE2 decided there), the life-state timers (fatigue 80 s, resting 1 h, poison 15 s/120 s, body decay), effect expiry and magic ticks (item 17 and MAGIC), the companion wait timeout after O3. The queue slice itself ships only a synthetic kind. |
| E5 Animation domain | E2, animation scheduler (item 9) | A second clock that runs at the game rate while game time is frozen in a round, with its own list and pass, for animation goals and real-time combat cadence. |

E1 is implemented as ARC-WORLD-022 ([runtime](../world/game-event-queue-runtime.md)), which records the
decisions it took per row of section 7. E2 with the life-state timers is ARC-WORLD-024
([session wiring](../world/game-event-queue-runtime.md#session-wiring-arc-world-024)); it keeps no per-kind
ActiveMapOnly removal yet because no such kind exists before the AI tick.

Order: E1 now (no schema), E2 after item 2 in the schema lane (and
before item 6 if that is next in the lane), then E3; the E4 kinds arrive with
their owners (I6 and S5 first); E5 with real-time combat.

## 9. Owner decisions (2026-10-16)

The owner accepted the three recommendations; slices E1..E2 follow them.

| ID | Question | Decision |
| --- | --- | --- |
| OQ1 | Ties between events with the same deadline: first scheduled first (FIFO), or the source's newest first? | **FIFO by a persisted sequence.** The source order is an artefact of the list insert (TE3) and depends on whether the event was created inside a ping; FIFO is deterministic and easy to explain in a trace. |
| OQ2 | AI ticks are not saved in the source, so a quick load restarts every NPC's tick with fresh jitter. Persist the active map's AI ticks instead? | **Persist them.** A save then reloads into exactly the same timing, which the seeded replays of the AI plan (design rule 3) need; the source behaviour stays available as a named "rebuild on load" switch for comparison runs. (Note, E2 proposal unchanged: map change removes the ticks of the map left and rebuilds them on activation, without the source's P3 carry-over.) |
| OQ3 | Keep one queue with a map scope per event, or per-map event files like the source? | **One queue in the session snapshot with a scope field.** It keeps the source semantics (events follow the map of their objects, parked events fire late on return) without separate files and keeps a save atomic. |

## Unknowns

- **U1** Whether the original executable orders tied events and caps a ping
  as read here (Q3, Q8) was NOT_VERIFIED; since checked in ledger rows TE3
  and TE4–TE7 (Original).
- **U2** The intent of the call-at reference (TE2); the census finds no live
  call-at line, so it matters only for mods.
- **U3** How the magic module re-creates its tick events for runs parked with
  a map (P6) is not read.
- **U4** The exact first-tick marker of a folded sector (P7) is not read
  beyond ARC-RES-042 H1.
- **U5** Multiplayer paths (the client clock budget and the host's time
  packets in the ping) are out of scope.

## Method

Read (an earlier source reading, cited by executable address; level F0 unless
a ledger row is cited): the time-event functions (0x45A7C0–0x45CC10) and their types in full,
and the scheduling and handler sites of every kind in the AI, script,
critter, stat, combat, magic-tech, item, object, trap, light, newspaper,
teleport, map, sector object list, game library, object pool, animation,
sleep UI, world map UI, random-encounter, in-game UI, animation UI and main
menu functions (addresses in the tables above). Code
state from `origin/main` f500b76. Earlier research reused, not re-read:
ARC-RES-020 (clock), ARC-RES-024 (companion), ARC-RES-039 (AI plan),
ARC-RES-040 (load map), ARC-RES-042 (dispatcher), ARC-RES-043 (magic).
Earlier provenance is in git history; names in this note are descriptive.

