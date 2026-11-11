# Clocks and the frame step in the session (K15, MIG12, TM6, GR20–GR22)

Status: **MIG12 finished (S1–S9 done). S2 (ARC-DEV-121) landed the pure clock rules, S3 (ARC-DEV-122) the session owning the three clocks (schema 70), S4 (ARC-DEV-123) the frame step and the event pump in `Arcanum.World`, S5 (ARC-DEV-124) the AI host as a participant and the NPC change-of-place ping as the `placePing`, S6 (ARC-DEV-125) the viewer's world time on the frame clocks, S7 (ARC-DEV-126) the pause by GQ3 A and the frozen frame, S8 (ARC-DEV-127) the real-time domain, S9 (ARC-DEV-128) the re-check of the time-dependent bugs and the closing of the status rows.** This page was written as slice S1 of MIG12 (ARC-DEV-120, docs-only, no checks run). Concept document K15 of the
[architecture audit](../research/architecture-audit-plan.md) and design of MIG12 ([target model](target-model.md#migration-order), element TM6,
decision GQ3 chosen A). Behaviour of the original stays the rows TL1–TL46 of the [original time loop](original-time-loop.md) (read from the
executable); this page fixes how the session represents them, in which assembly each part lives, the order of the code slices and what each must
show. It follows the style of [residency](residency.md), [change of place](change-of-place.md) and [interface modes](interface-modes.md).

Requirements (reference only, no coverage row changes): [FR-LIGHT-001](../requirements/lighting-time.md#fr-light-001) (the one game clock),
[FR-RUNTIME-001](../requirements/world-travel-runtime.md#fr-runtime-001), [FR-TRAVEL-002](../requirements/world-travel-runtime.md#fr-travel-002) (S8) and the
clock and pause rows of [the game clock policy](../world/game-clock-policy.md). Each code slice updates the coverage rows it changes in its own PR.

## 1. The problem in one paragraph

The session has **one** clock, the game clock (`GameClockOwner`, saved), and one admission adapter (`GameTimeFlow`, not saved, owned by the
viewer's `AreaAmbientDriver`). The three clocks of the original exist only as the three *domains* of the event queue: the session passes
`GameEventClocks(0, game, 0)`, and all 17 session event kinds are game-domain. Everything that moves over time in play runs on other time bases:
the AI host keeps its own `_nowMs`, and the hero's attack and cast, projectiles, spell missiles, eye candy, dying and lying animations, body moves,
blood pools and scenery read `WorldTime` (Godot `Time.GetTicksUsec`, frozen once per frame) through `WorldMilliseconds()`; the hero's walk runs on a
movement time; the world-map journey, the light animation and the death panel run on the frame delta or their own counters. `WorldModal` raises a
Menu pause on the game clock for **any** window (palette, every window, save list, main menu, death cover, bug report), but the other time bases
are not paused with it: each host gates itself, and the AI pump has no gate at all (GR20, GR21, GR22). The result is that the NPC's runs advance on
Godot time while the session's clock stands, no run can be saved consistently with the clock it waits on (MIG14), and a world-map step behind the
Esc menu differs from the original's.

## 2. What the original does, in the rows MIG12 follows

| Row | Fact the design keeps |
| --- | --- |
| TL2, TL3, TL5 | One loop, one stamp per frame (`timeGetTime`); the module ping hands that one stamp to every module. Input read in a frame is acted on at its end and first drawn in the next. |
| TL14 | The time-event ping: interval = stamp − reference; under 5 ms nothing changes and the reference stays (short frames add up); otherwise the reference moves **first**, then the interval is cut to 250 ms (the excess is lost). Real clock +i always; game and animation clock +8i unless paused; game alone not in a round. |
| TL15 | Three clocks of {day, ms}; real starts at (0, 1); game and animation start at the start hour (15:00 by the installed data); a zero duration is 1 ms. |
| TL19–TL22 | Three sorted lists, one per clock; the ping advances all three clocks first, then fires real → game → animation against the clocks as they are when each list's turn comes, so a time jump made by a real-time handler is seen by the later lists in the same ping. Repeating events re-arm from "now". |
| TL23, TL24 | One pause raiser: the Esc menu (not the options screen). It stops game and animation time, not real time: sleep and travel steps, UI timers and the turn-based step event run behind it. Dialog, inventory, sheet, barter, logbook, world map, sleep, loading and losing focus do not pause. |
| TL25, TL46 | Loops that do not ping (message box, movie, slide show, fades, loading) freeze all three clocks; the next ping credits at most 250 ms. |
| TL26–TL28 | Jumps (sleep +1 h a step every 200 ms, world-map travel ⌊tiles/64⌋ h a step every 62 ms, a round's +1 s, a timed teleport, one script action) add to game and animation time together, ignore pause and rounds, fire nothing themselves; due events fire at the next list pass. Sleep and travel are chains of **real-time** events. |
| TL32 | 35 kinds: 10 on the real list, 2 on the animation list (goal steps `Anim`, `Light`), 23 on the game list. |
| TL38 | A round stops the game clock only (+1 s at each round end, to game and animation); animation and real keep running. |
| TL40, TL42 | Goal steps run on the animation clock; the AI tick is a game-list event. |

## 3. Design

### 3.1 Three clocks in the session

The session owns three timestamps: **real**, **game** and **animation**, 64-bit milliseconds (`ClockCalendar` already derives day, hour and calendar
from a 64-bit game stamp; the original's {day, ms} pair and its unnormalised carry at exactly 86,400,000 ms are not reproduced, as `GameClockOwner`
already decides). Real starts at 1 ms, game and animation at the profile's start time. The game clock keeps its token, revision and
`PauseDisposition` as they are (`GameClockOwner` stays the authority of the game clock and its calendar); the real and animation clocks are plain
monotone stamps beside it in a Godot-free `FrameClocks` value in `Arcanum.Simulation`, owned and mutated only through the session (no token of
their own: no caller outside the session may move them, and the session is single-threaded).

**Admission** is one function of one host stamp, generalising `GameTimeFlow` (same 5 ms floor with the reference left in place, same
reference-then-cut order, same 250 ms cut, first observation and a backwards stamp admit nothing):

```
admit(hostMs, pause, round) -> (realMs = i, gameMs = 8i if !pause && !round, animationMs = 8i if !pause)
```

`GameTimeFlow` becomes the real-interval admitter inside the session; the viewer's driver stops owning it. The reference is not saved (a load starts
a fresh reference, so a load credits nothing, as the first observation does).

**Frozen modes.** The host passes `Frozen = true` for a frame in which the original does not ping: the death slide show and cover, a movie, a
message box, a fade, loading. No stamp is observed and the reference stays, so the next frame credits at most 250 ms (TL25). The viewer's other
overlays that have no counterpart in the original (the dev palette, the bug report) do not freeze anything: they are Steamwoven tools and
pause only by the rule in §3.4.

**Jumps.** `JumpClocks(ms)` adds to game **and** animation together, ignores pause and round, and fires nothing itself (TL26; a duration of 0 is 1
ms). It is the one path for the world-map step, a round's second, a timed teleport and a script's time action; today's `ExplicitWait` advance of
`GameClockOwner.AdvanceGameTime` is the game half and stays the single writer of the game stamp (revision bump, capacity rules), so the jump calls it and adds the
same amount to the animation stamp in the same call.

### 3.2 The one event queue with three live domains

The queue core already has the three domains and the fixed pass order (real → game → animation, `GameEventClocks`, `PassOrder`). The session passes
all three clocks instead of `(0, game, 0)`. The 17 session kinds stay on the game domain, which is where TL32 puts the kinds that exist in the original
(poison, fatigue, resting, locks, respawn, decay, restock, the AI tick, script calls, party wait, spell run); the others (hero-effect end,
encumbrance) are the project's own and keep their game domain. Two domains gain their first kinds in later slices, not here:

- **Animation domain**: the goal steps and the `Light` events, with the goal table (MIG13) and the light animation (S6). Until then the
  animation clock has no event consumer; it has *time* consumers (§3.3).
- **Real domain**: the world-map journey step (62 ms), UI timers and the turn-based step event (§3.5).

**Pass semantics.** The original fires each list against the final clock of the ping. The project keeps its decision GI5 (events at their own
deadlines, FIFO ties GI3): the frame's admitted interval is divided at the next due time of any domain and every sub-step moves the three clocks
together (real +Δ, game +8Δ, animation +8Δ with the same pause and round flags), then fires real → game → animation, so a real-time handler's
jump is visible to the later lists of the same sub-step (TL20) and a handler sees the clock at its own deadline. The bound of the former
`AreaEventPump` (a maximum of steps per advance and passes per step, late firing reported when the bound is hit) stays and moved with the pump
(S4: `FrameStepLimits`, steps counted per frame). A jump made inside a pass is applied at once, and the pass continues against the new clocks.

### 3.3 The frame step and who reads time

One **frame step** in `Arcanum.World` (TM11) runs once per frame, in the order of TM6:

1. the change-of-place ping (TM12: `ProcessChangeOfPlace`; the viewer's own ping in the frame of the request, CD7 of
   [change of place](change-of-place.md), goes away);
2. admission and the event pass over the three domains (§3.1, §3.2), which also fires the lighting hour event;
3. the **frame participants**, in a fixed order, each given the clocks of this frame: today the AI host pump, the hero's and the others' goal
   runners, projectiles, missiles, eye candy, scenery. They are registered through one port (`IFrameParticipant`: `Step(FrameClocks, FrameFlags)`)
   so the hosts that still live in `tools/terrain-viewer/Core` (MIG15 moves them) are called without the world runtime referencing them;
4. the viewer then draws, plays audio and reads input; input taken during the frame is applied at the next step (TL2).

**Nothing in the rules reads Godot time.** The viewer's only call is `FrameStep(hostMs, frozen)` with `Time.GetTicksMsec()` (a test passes its own
stamp, so every synthetic test is deterministic). `WorldTime`, `WorldMilliseconds()`, `_nowMs` and the frame `delta` leave the rules; the delta
stays for drawing-only animations that the original also drives by frame (zoom, panels).

**The unit of animation time.** The original's animation clock runs 8× real and every goal step is timed against it. The viewer's tuned speeds,
walk durations and projectile speeds are in real milliseconds. The participants therefore read **world milliseconds**, a fourth stamp beside the three clocks: the sum of the *admitted* animation intervals ÷ 8 (kept
as a count of 1/8 ms, so nothing is rounded away). It equals real time while unpaused and not frozen, stops with the pause, not with a round, and
is **not moved by jumps**: a round's second, a world-map step or a script's time action move the animation clock (so animation events and the
calendar see them, TL26) but must not fast-forward a walk, a projectile or a goal run by minutes. In the original a goal step fires once after a
jump and a missed period is not replayed (TL22); a participant gets the same by reading world milliseconds. World milliseconds are run-local (not
saved: a load starts them at 0 and every participant's due time is run-local until MIG14 saves the runs). Nothing is retuned; MIG13 may restate
the goal tables in animation milliseconds when it builds the one goal table.

### 3.4 The pause (GQ3 A)

The pause rule is the original's in both rule modes and has **no switch** (owner decision at GQ3, revised to A): only the Esc menu (and the save
and load lists, the palette's too) raises it; it stops game and animation time and nothing else. Dialog, inventory, sheet, barter, logbook, world
map and the other windows of [interface modes](interface-modes.md) do **not** pause, and the gate that today closes the world for every window
changes with it.

- `GameClockOwner.SetPaused` keeps the single `PauseDisposition`; the only reason set in play is `Menu`, raised while the main menu panel or the save and load list it opens is open and released when they close, from the frame step, not from
  `AreaAmbientDriver`. The main menu, the save list, the palette, the bug report and the death cover are panels **outside** the interface stack
  ([interface modes](interface-modes.md)); the pause source is the viewer's flag of the menu and its lists (S7 reads it through one
  `MenuOpen` value handed to the frame step), and moving the menu onto the stack is not part of MIG12. The options screen (the O key) does **not**
  pause (TL23, mode 4), and neither does the viewer's settings window. The reasons `User` and
  `TurnBased` stay unused; `TurnBased` is *not* a pause (a round is a flag, §3.1).
- The round flag is the session's (`_combat.Round != null`), read by admission: no game time in a round, animation and real go on.
- The pause is saved as today (a quick save from the menu stores a Menu pause; a load starts with the menu closed and releases it, as the original
  closes the menu after the load).
- What the viewer does while a window is open is no longer a time rule: the **hero's input** is gated by the stack (S4 of interface modes), the
  walk and the world go on, and an NPC may attack the hero while the sheet is open, as in the original. This is the visible change of the whole
  MIG12 and it comes in its own slice (S7) after every consumer already reads the session clocks.

### 3.5 Real-time events behind the pause

Because real time runs behind the Esc menu, the things the original runs on the real list move onto the session's real domain, so the menu does
not stop them and a window does not accelerate them:

- the **world-map journey**: a chain of real events, a first step 50 ms after the start, then one every 62 ms, each with its time jump (TL28), replacing the viewer's `SyncAreaWorldMap`
  delta loop and explicit waits;
- **UI timers** the viewer keeps on the frame delta that the original keeps on the real list (the fidget chain, floating-text and bubble
  timers stay viewer-side presentation: they do not affect rules);
- the **turn-based step event** (TL39: the stall detector and the step that ends a round when nothing is left to do), which today runs from the
  frame; the round's end jump then fires the game events that became due in the same pass (TL38).

**As built (S8, ARC-DEV-127).** The two chains are the session's real-domain events `travel.step` and `combat.step` (`SessionEventKinds`; not saved, one
pending of each, `ScheduleRealStep` / `QueryRealStep` / `CancelRealStep` on the session). Their handlers only report the step that fell due
(`SessionEventPassResult.RealSteps`): the session's own operations are refused inside a pass, and the step's work (a time jump, a discovery,
the round's subturns and the AI host) is exactly such work. The frame step runs the registered `IRealStepHost` for each reported step right
after the pass, outside it, schedules the step again from now when the host returns a delay (the original's handler adds its next event the same
way) and runs one more pass, so a jump the step made fires the events it made due in the same frame (TL20, which the pass alone could not give:
a pass reads its clocks once). A step's chain ends when the host returns null. The world-map journey schedules its first step 50 ms after the
start and each step asks for the next one 62 ms on; the viewer cancels a pending step whenever the journey ends. The turn-based event is asked for
by the viewer's frame (the original's callback hangs on an animation slot being freed, which the viewer does not have: the same cadence as the
per-frame step it replaces) and its host returns no repeat; a map drawn unlit has the frame driver of S5 (`AreaFrameDriver`), so the journey and the round's event
run there the same way; only a scene with no session keeps the old per-frame round step and refuses the journey.

Sleep and rest (K7) do not exist yet; when they do they are one more real-time chain (TL27: 200 ms, +1 h a step) on this domain, which is the
reason K7 follows MIG12 (GQ-review option A).

### 3.6 Saved state

The snapshot gains the real and animation stamps in the `clock` section (it holds the game stamp, the pause and the revision today), as
the original's save stores all three pairs (TL34). **Schema 70**, with a step in `GameSessionSnapshotUpgrade.cs`: real = 1, animation = the saved
game stamp (the nearest consistent value: the two moved together in every saved game until now, except in rounds, where the animation clock was
never advanced and the difference is not recoverable). The admission reference is not saved. The queue stays saved as it is (all kinds are game-domain events;
later real and animation kinds are saved only when they are saved kinds: the world-map step is not, a difference listed in §4; the animation `Anim` kind is MIG14's).

### 3.7 What stays outside

- **Presentation clocks.** Frame-delta animations that only draw (zoom, panel slide, death panel, light shimmer until S6, art animation frames)
  stay on the frame delta. Only a time that decides a rule, a position that a rule reads or a state that is saved belongs to the session.
- **The turn-based round machinery** (gates, AP budget) is TL36–TL45 and the combat session; MIG12 only supplies the clocks and the real-time
  step event.
- **The one goal table** (MIG13) and saved runs (MIG14): MIG12 gives their clock; it does not unify the runners.
- **The sector source thread.** The frame step runs on the main thread only; a worker may read data but never touches a clock.

## 4. Deliberate differences from the original

| Difference | Why |
| --- | --- |
| Events fire at their own deadlines inside a frame (bounded sub-steps), not all against the ping's final clock. | Decision GI5, kept by TM6: the same game time gives the same state however frames split it (`AreaEventsTests`); the visible difference is only for events due in one frame. |
| Equal due times fire in schedule order, not newest first; no 501st-event cut. | GI3 (FIFO ties); the 500-event guard exists for the original's loops; the project's bound reports late firing instead of dropping the rest. |
| The clocks are 64-bit millisecond stamps; the original's unnormalised day carry at exactly 86,400,000 ms is not reproduced. | A normalised stamp has no observable effect except the hour reading 0 a frame late; the quirk is a bug of the game. |
| The interval carries the host's `timeGetTime`-like stamp as a 64-bit millisecond count, with no wrap. | The original's 32-bit wrap is every 49.7 days of host uptime; no run is that long. |
| World milliseconds (admitted animation time ÷ 8, jumps excluded) for the viewer's tuned speeds. | Nothing is retuned in MIG12; the original's walk and goal step durations are restated by MIG13. |
| The viewer's dev palette and bug report are windows with no original counterpart and do not pause (they still close the hero's input, and the palette stops the hero's route). The save list the palette opens pauses like the menu's. | Under GQ3 A only the Esc menu and its lists pause; the palette and report are tools, not play states. |
| The turn-based step runs at the head of the frame (the frame step's pass), not inside the world's frame after the attack and the AI; the viewer asked for the event every frame a round runs and did not feed TL39's stall detector (the host's `CompleteCombatActions` twice-unchanged rule did); ARC-DEV-136 (MIG13 S5b) replaced that: the goal table's frees call the round's callback, which asks for the event and feeds the stall detector, and the viewer's frame only follows the round. | The viewer had no animation-slot hook to hang the callback on; the cadence was the one the per-frame step had until ARC-DEV-136 (MIG13 S5b) hung the callback on the goal table's frees. The step is skipped in a frame with a transfer, reset or reload pending, as the world's frame was, and a loaded round takes its load rule first (GR19). A host exception now leaves the frame loop's try (the whole frame is skipped). |
| The world-map journey keeps the ambient light updating (TL28 switches it off until arrival). | Unchanged viewer behaviour; the lighting follows each hour the jumps change. Recorded here, not a flaw of the original. Since S7 Escape no longer ends the journey (it opens the menu over the map, as in the original); the journey ends by its landing, W or a controller cancel. |
| A real-time step reported by a pass is run after it (and the pass' game list has run), not before the game list as the original's real list is. | The host acts on the session, which refuses its operations inside a pass; the events a step's jump makes due fire in a pass that follows at once, so the set of events fired and their order by deadline are the original's, only an event due at exactly the sub-step's boundary may fire before the step instead of after it. |
| A saved game keeps the pause and the world-map step is not a saved event. | The original does not save its pause counter (TL23) and does save the real-list kinds 12 and 14 (TL32); here a save during a journey or with the menu open restores the menu closed and the journey ended at the last step. A save is offered from the menu, which holds the pause, so the saved pause is the menu's own; both are viewer states the load re-creates. |
| The death cover and slide freeze all three clocks. | The original's slide show does not ping (TL25); since S7 the viewer's cover is a frozen frame (no stamp observed), not a Menu pause. The fade-out and the dialog box under it are covered by the same flag. |
| A hero's walk, an attack in flight or a queued action is dropped when a window opens (the arrival actions "walk to talk/use/pick a pocket", the attack approach, a spell cursor, a held throw); the stick and clicks are closed behind every window. | The hero's input gate is the interface mode stack's (SA44, MIG16 S4); the original's windows take the walk's own goal along. This was a pause side effect before S7 and is now only the input gate; each is listed in the S7 report and none is retuned here. |

No switch is added: GQ3 is decided (A, in both rule modes). Where a slice's read of the executable shows a behaviour that is a bug of the game
rather than a rule, it is classified by the [original executable check](../research/original-executable-check.md) and put under the D8
"Original"/"Steamwoven" setting in that slice's PR (candidates already known: the unnormalised carry above; the animation events that fall due
at once at a round's end, TL38, an inference of the original).

## 5. Slices

The codes of S2–S9 are reserved by the coordinator; schema 70 is reserved for S3.

| Slice | Code | Result | Tests and evidence |
| --- | --- | --- | --- |
| S1 | ARC-DEV-120 | This document; the MIG12 row and the K15 line updated. | none (docs-only) |
| S2 | ARC-DEV-121, **done** | `FrameClocks` (the value a participant reads), `FrameClockKeeper` (real and animation stamps, admission, jump, world milliseconds), `FrameFlags`, `FrameAdmission` and typed `FrameClockFailure` results in `Arcanum.Simulation.Clock`; `GameTimeFlow.ObserveInterval` is the real-interval admitter; no caller. The keeper does not hold the game stamp (`GameClockOwner` stays its only writer): `Advance` reports the game part, `PlanJump` validates a jump without changing anything, so S3 applies game and animation in one call without a half-applied jump (`ClocksAt(gameMs)` composes the participants' value). Originally planned as:  `FrameClocks`, the admission with pause, round and frozen flags, the jump and the world milliseconds (admitted only, jumps excluded) in `Arcanum.Simulation`; `GameTimeFlow` generalised; no caller. | Unit tests: 5 ms hold and sum, 250 ms cut, ×8, pause stops game and animation only, round stops game only, frozen credits at most 250 ms later, jump adds both and ignores pause and leaves the world milliseconds alone, first and backwards stamps, limits and typed refusals, determinism. |
| S3 | ARC-DEV-122, **done** | Landed: `AdvanceFrame(hostMs, frozen)` admits with the session's pause and round and hands the game part to the viewer's pump (which steps it at the deadlines until S4), `JumpClocks` and every `ExplicitWait` jump game and animation together (the animation half checked first), `QueryFrameClocks`, `ResetFrameReference` (the driver calls it on bind, so a new scene's first frame admits nothing, as before), the queue gets `GameEventClocks(real, game, animation)`, the driver's `GameTimeFlow` is gone, schema 70 (`clock.realMs`, `clock.animationMs`; step 69 → 70 real 1, animation = game stamp). Planned as: the session owns the three clocks: `AdvanceFrame(admitted)` and `JumpClocks` on `GameSessionOwner`, the queue gets all three clocks, the driver's flow moves into the session, schema 70 and its step. **Session core and save schema: Opus.** | Session and snapshot tests (round trip, the 69 → 70 step by hand, refusals, queue due times in each domain); `AreaAmbientTests` rewritten over the session. |
| S4 | ARC-DEV-123, **done** | Landed: `FrameStep` in `Arcanum.World` (`Run(hostMs, frozen)`, `Jump(ms)`, the participant port `IFrameParticipant`, the optional change-of-place ping that opens a frame, `FrameStepLimits`, `FrameStepReport` with the clocks and `HourChanged`) replaces the viewer's `AreaEventPump`; the session's `BeginFrame` returns an open `SessionFrame` of `8i` units that is spent in steps moving real, game and animation together at the event deadlines (`UnitsUntil` over the three domains, a carry for the real clock's sub-millisecond share), the pause and the round read at every step; the public `AdvanceFrame` and `AdvanceGameTime` are gone, so ordinary game time moves only in a frame and a jump has its own call (`JumpClocks`); `AreaAmbientDriver.Tick` is one `FrameStep.Run`. **Left for later slices:** the change-of-place ping is the port only (the hero's hosts carry request context, so CD7 stays until MIG11's hosts are made pending requests; the NPC homecoming ping moves with the AI host in S5), and no participant is registered yet (S5, S6). Planned as: The frame step and the event pump in `Arcanum.World` with the participant port; the viewer calls one `FrameStep(hostMs, frozen)`; the change-of-place ping runs first (CD7 goes); the lighting hour from the pass. | World tests over synthetic participants (order, one stamp, bound, late firing, jump seen by later domains); smoke `walk`, `ai`, `debug-travel` and the map battery (`map`). Landed: `FrameStepTests` (World), the frame-handle tests of `FrameClockStateTests`, `Admit`/`Step`/`UnitsUntil` tests of `FrameClocksTests`, `AreaEventsTests` over the new step. |
| S5 | ARC-DEV-124, **done** | Landed: `AreaAiFrameParticipant` (viewer core) is registered with each ambient driver's `FrameStep`; once per frame after the event passes it touches the view's sectors and calls `AreaAiHost.Pump(clocks.WorldMs)`, not in a paused frame (TL23; a round does not hold it, TL38) and not in a frozen one (TL25); `AreaAiHost.NowMs` is the clock of the last pump, which the viewer's mover drawing, the attack plays and the ai smoke read instead of Godot time; the viewer's `ProcessAreaAi` now only handles what the runs did (notices, round log, status line, bodies); the NPC change-of-place ping (`ProcessNpcChangeOfPlace`) is the frame step's `placePing`, so a request made in a frame is carried out at the start of the next (the original's order). No Original/Steamwoven switch: the pause and round rules are the original's and not a flaw of it. Limits: what an AI notice starts in a consumer (an NPC's spell missile and its eye candy) is stamped on the S6 world clock, not the host's; the hero's CD7 ping stays; a map drawn unlit has no ambient driver, so `AreaFrameDriver` is its frame source (the same `FrameStep`, the same Menu pause, the S6 tap and the AI participant, the ping), its session clock now flows and its world time follows the frames; no pump runs while a map transfer is in flight (its frames are the transfer's), and until S7 any open window pauses the runs. The view's sector touch stays in the viewer's `ProcessAreaAi`. Planned as: The AI host and the movers on the clocks: `_nowMs` and `host.Pump(WorldMilliseconds())` go; runs step on world milliseconds, the AI tick on game time; the pump is gated by the pause. | Host tests (`AreaAiHostTests`, `AreaAiRouteTests`, follower and round tests); smoke `ai`, `ai-turn-based`, `ai-follower`, `follower-home` (the AI pump pings the change of place, DEV-116). |
| S6 | ARC-DEV-125, **done** | Landed: `FrameClockTap` (an `IFrameParticipant` in the viewer's Core, registered on each ambient driver's frame step, after the passes) carries the clocks and flags of the last frame, and `WorldTimeline` turns its world milliseconds into the viewer's one world time (microseconds, monotone: a new tap or session continues from the last value, so no deadline a consumer stored goes stale, and the host's time is the fallback only where no frame runs: no lighting driver, a WORLD diagnostic scene, before the first frame, and an automated run unless a smoke asks). `WorldTime` (the hero's route and walk) and `WorldMilliseconds()` (the hero's attack and cast, projectiles, spell missiles, eye candy, the dying and lying animations, body moves, the hero's knockdown, scenery, and the pool of blood, whose start was a host millisecond count) read it, so they stop with the pause, are cut with a long frame like the original's ping and are not moved by a jump; the light animation is the tap's world milliseconds since its map's lighting bound, no longer the frame delta. The AI host moved onto the frame clocks in S5 (`AreaAiHost.NowMs`); what an AI notice starts in a consumer (an NPC's spell missile and its eye candy) is stamped on the world clock the consumer runs on. A map without a lighting driver is bound by S5's `AreaFrameDriver`, which registers the tap there. The door animation (`TerrainViewerAreaPortals.cs`) and the other drawing-only animations stay on the host clock (§3.7). **Left for later slices:** the consumers are still called from the viewer's world step in their fixed order, between the hero's own steps and the AI pump (they read the clocks but are not separate participants: the AI pump sits between them, so the order moves with S5, and the goal runners with MIG13 and MIG15), and no `Light` event kind exists on the animation domain (the light is a presentation-only animation; the first animation-domain kind comes with the goal steps of MIG13, with the TL38 reading of §6.4). Planned as: The other consumers read the clocks: hero attack and cast, projectiles, spell missiles, eye candy, dying and lying animations, body moves, blood pools, scenery, the light animation (`Light` events on the animation domain); `WorldTime` leaves the rules. | Landed: `FrameWorldClockTests` (the tap over a real `FrameStep`: 5 ms floor, 250 ms cut, pause, jump; the timeline: continuity over a new tap, never back, 125 µs an eighth), `tools/local_dev/test_world_clock_sync.py` (no rule file reads `Time.GetTicks*`; the host's microsecond clock is read only by the clock source; every driver registers the tap; the AI host keeps its clock), and in the map stage the native check `frame clock` (literal host stamps: 16 ms frames, frames under 5 ms, the hero's route walks and arrives on the frame clock, a 1920 ms Esc pause stops the world time and the hero without a refused observation and the walk resumes, a five-minute jump leaves the world time alone); the existing smokes keep driving their own test time. |
| S7 | ARC-DEV-126, **done** | Landed: the viewer hands each driver's `Tick` one `menuOpen` value, `WorldMenuPause` (the main menu panel or a save or load list; the options screen the menu leads to is under it), instead of `WorldModal`, so a dialog, inventory, sheet, barter, logbook, schematic, reading, world map, the palette and the bug report no longer raise a pause and the world (the AI runs, the hero's walk, projectiles, the clocks) goes on behind them; `WorldModal` stays the hero's **input** gate and the cursor's, so the stick, the clicks and the keys of the hero are closed behind every window and behind the pause itself (risk 2a). The death cover (`HeroDeathCovering`: the fade, the slide show, the box) is a **frozen frame** (`WorldFrozenFrame`, TL25): no stamp is observed, no clock moves, no pause is raised, and the first frame after it credits at most the 250 ms cut. `ModalPaused` of both drivers is now `MenuPaused`. Esc on the open world map opens the in-play menu (window 4, as since ARC-DEV-111) and the journey's steps go on behind its pause; the panel's text and the comments that said Escape ends the journey are corrected. **No flaw of the original is involved** (the pause rule is TL23/TL24 as read; nothing is behind an Original switch). Planned as: The pause by GQ3 A: only the Esc menu pauses, the world gate no longer closes for the other windows, the death cover freezes; the hero's input gating by the stack stays. **Session core: Opus.** | `AreaAiFrameTests` (a frozen frame is no pause, moves no clock and runs no goal step, and the next frame credits the 250 ms cut; the open menu pauses), `AreaAmbientTests`; smokes: the clock smoke (the palette and a dialog no longer pause, game time flows 8x behind them), the frame clock smoke (an open inventory leaves the world time running, the Esc menu stops it and the next frame credits one frame), `death` (the cover moves no clock and raises no pause), `worldmap` (Escape opens the menu over the map and the journey's jumps run behind it, ordinary game time flows otherwise), plus `menu`, `scene`, `inv-ui`, `barter`. |
| S8 | ARC-DEV-127, **done** | Landed: the session's real-domain kinds `travel.step` and `combat.step` (not saved, `KeepExisting`), `ScheduleRealStep`/`QueryRealStep`/`CancelRealStep`, `SessionEventPassResult.RealSteps`; `IRealStepHost` and `FrameStep.RealSteps` (the host runs after a pass, the step is scheduled again from now, one more pass follows so the jump's due events fire in the frame; `FrameStepReport.RealSteps`/`RealStepsUnhandled`); the viewer's `AreaRealStepHost` (world-map travel step: 50 ms after the start, then 62 ms, a jump per step, behind the Menu pause; turn-based pass asked for every frame a round runs, until ARC-DEV-136 asked for it from the goal table's callback); `SyncAreaWorldMap`'s delta loop, `WorldMapStepsPerFrame`, `AdvanceWorldMapClock` and the round's per-frame call are gone; the worldmap and turn-based smokes tick the session clock with their own stamps. **No flaw of the original is involved** (the 50/62 ms and 2 ms delays and the jump rules are TL26/TL28/TL39 as read; no Original switch). Planned as: The real-time domain: the world-map journey as a chain of real events (62 ms steps), the turn-based step event on the real list; the delta loop and explicit waits go. | Session and viewer tests (a step behind the menu still fires, the time jump per step); smoke `worldmap`, `debug-travel`, `turn-based`, `ai-turn-based`. Landed: `RealStepStateTests` (Session), `RealStepTests` (World), `AreaWorldMapTests` journey over the frame step; the four smokes pass. |
| S9 | ARC-DEV-128, **done** | Landed (docs only, no source change): the re-check of the bugs the clocks could move ([result](../research/open-bugs.md): B12, B13, B15 and B11, B14 read on `main` after S8, B06 and B10 not touched by it), GR20, GR21 and GR22 closed in the gap register, MIG12 closed in the target model, this document's status and §7, and the stale "animation clock not in the session" cell of FR-WORLD-015. Planned as: re-check of B-bugs that depend on time, coverage and status rows, this document's status. | The smokes of the stages the clocks touch (`map` with its native frame-clock check, `cast`, `worldmap`, `ai`, `ai-turn-based`, `turn-based`) re-run on the final `main` as evidence for the re-check; the daily full chain covers the rest. |

S2 and S3 change the clock authority and the saved state, S3 and S4 change the session core and threading, so they take the larger review model
of [the review-model rule](../workflow/tasks.md#review-model); every code slice merges its own PR and updates the coverage rows it changes.
S5 to S8 are independent of each other after S4 except that S7 comes after S5 and S6 (the Esc menu must stop every consumer, and a consumer that
still reads Godot time would run behind it). **Lanes of MIG9:** S5 rewrites `_nowMs` and `Pump` in `AreaAiHost.cs`, which DEV-119 (MIG9 S5c,
the host built per open map and whether it moves into `Arcanum.World`) also changes, so S5 starts after DEV-119 has merged; S4 puts the event pump
and the frame step in `Arcanum.World` beside DEV-118's `ISectorTerrain` (different files; the two coordinate at merge). S2 and S3 touch neither lane.

## 6. Risks and open questions

1. **Behaviour change of S7.** After it, the world runs behind an open dialog, inventory or sheet; the hero's walk, an NPC's attack and a
   projectile continue. This is the original's rule and the owner's decision (GQ3 A), but the viewer's windows were built for a stopped world:
   every window that assumes it (the barter and the dialog's pickpocket, the inventory's use of an item in flight, a held item while a round
   starts) needs a look. S7 lists them in its report. **Looked at in S7:** the NPC the hero talks with was held still only by the pause (the AI host's `ConversationCallback` was false and its `EndConversation` did nothing, so a partner that fell into combat focus or fled stayed on the gate's Conversation stop until the dialog closed; closed by ARC-DEV-177, the callback is always set as in the original and the gate's call ends the dialog at the frame's next safe point (a dialog under a barter is suspended and the call does nothing); open: an attack on the hero ends its dialog in the original, and between a dialog's close and the barter it queues the merchant may take one gate step; both belong to MIG13's goal work): the viewer never named the talk partner to the AI host (`AreaAiScene.ConversationPartner` was unset), so S7 names it (the open dialog's speaker, and the barter's merchant, whose dialog is suspended under it with the partner kept in the original, GT139) and the gate's Conversation stop holds it, as the original's does; every other NPC walks, fights and leaves behind an open window; a pickpocket window has no partner in the original and none here; an item in the hero's hand is put back when a window opens (`HeldThrow`, unchanged), and a round that starts behind a window is the existing round start; an open window does not stop a projectile in flight, a spell missile or a dying animation. The hero's own walk is stopped by the window's opening (its input gate), not held. No window is retuned; a native look at an NPC walking behind an open dialog stays manual.
2. **Frame time spread.** The 250 ms cut costs game time on a slow frame (a load, a long GC) as it does in the original; the viewer's long
   operations (map open, window rebuild) must report `Frozen` or the admitted time is lost. S4 listed the call sites: the only production caller of the frame is the viewer's frame loop (`TerrainViewerApp` `_Process` through `TickAreaClock`, which the automated runs replace by literal stamps); a long operation inside a frame (map open, window rebuild) costs at most the 250 ms cut on the next frame, as the original's loading does (TL46), so no site passes `Frozen` yet; the death cover and slide show take it in S7 (done). A map open or window rebuild still passes no `Frozen`; it costs the 250 ms cut on the next frame as the original's loading does.
2a. **Input behind a pause (S7).** Behind a pause the world time stands, so a stick or click that still reached the actor would queue at one time stamp until the actor's input capacity: today every pause the viewer raises is the modal gate (`WorldModal`), which also blanks the stick, so none does. S7 keeps `WorldModal` as the hero's input gate for every window and for the pause itself (the menu and the lists are in it), so a stick or click never reaches the actor behind a pause.
3. **Light animation and sector light.** The light's own `_lightAnimationMs` and the sector light fold (MIG9 S6) use the clock hour; S6 moved the
   animation only (it is the frame clocks' world milliseconds since the map's lighting bound), the hour stays the game clock's.
4. **Round and animation events.** TL38's "animation events due within the next second become due at once at a round end" is an inference;
   S3 reads it again in the executable when the queue gets the animation domain, and the result is classified before the first animation-domain
   kind is registered. S3 (ARC-DEV-122) gave the queue the animation clock but registered no animation-domain kind, so no event can fall due
   at a round's end yet; the reading moves to the slice that registers the first one (the `Light` events of S6 or the goal steps of MIG13).
   **Settled by MIG13 S3 (ARC-DEV-132):** the first animation-domain kind is `goal.step`; the executable's round end adds its second to both clocks through
   the ordinary jump, so a step due inside it fires once at the next pass, a rule of the game with no switch ([goal table GT18](goal-table.md#the-s3-read-of-the-executable)).
   The `Light` events stay on the viewer.
5. **Hero walk.** The hero walks on a movement time (C3) that the world gate holds; the hero's goal moves to the one goal table in MIG13. In
   MIG12 the hero's route follows world milliseconds (S6, done) so it stops with the pause and runs with an open window; the route follower's own
   "held while modal" rule: S7 found none in the follower itself (the route drives to the world time whatever is open; the pause stops it by stopping the world time, and opening the menu or the palette stops it by the quarantine as before), so nothing is removed.
6. **Determinism of tests.** Self-tests and smokes that today set a test time (`_worldTestTime`) switch to passing frame stamps; any
   assertion on a Godot-time constant needs a restatement in S5 and S6. S6 kept the smokes' own test time first (an automated run drives
   `_worldTestTime`, then the frame clocks only when a smoke asks) and added one native check that drives literal frame stamps; the AI
   smokes move with S5.

## 7. Not ready

MIG12 is finished: the three clocks and the frame step are in the session and the world runtime, and the viewer's consumers read them.

- **Done.** The session holds the real, game and animation clocks, admits the frame and saves them (schema 70); the world runtime's `FrameStep` spends
  each frame at the event deadlines on the three domains together, and ordinary game time moves only inside a frame (S2–S4). The viewer's clock tap
  (S6) and the AI host (S5, `AreaAiFrameParticipant`, with the NPC change-of-place ping as the `placePing`) are the two registered participants; the
  hero's walk, attack, cast, projectiles, missiles, eye candy, animations, scenery, pools and the light animation read the world time the tap
  carries. The pause is the Esc menu's alone and the death cover is a frozen frame (S7). The real list has the world-map step and the turn-based
  event (S8).
- **Not ready (belongs to later migrations, not to MIG12).** The consumers are called from the viewer's world step in a fixed order and are not
  separate participants; the animation domain's first kind, `goal.step`, came with MIG13 S3 (ARC-DEV-132, the TL38 reading of §6.4 settled) and has no
  host yet, and the one goal table's hosts and the hero's goal come with the later MIG13 slices, saved runs with MIG14, the remaining hosts with MIG15. The hero's change-of-place request is still pinged by the
  viewer in the frame of the request (CD7), until MIG11's hosts are made pending requests. No site passes a frozen frame for a map open or a window
  rebuild (they cost the 250 ms cut, as in the original, TL46). Sleep and the UI timers as real-time chains wait for K7 (GR23). The door animation and
  the other drawing-only animations stay on the host clock (§3.7). The text of [the game clock policy](../world/game-clock-policy.md) is research and
  states the rules, not the viewer's wiring.
