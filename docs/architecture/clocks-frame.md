# Clocks and the frame step in the session (K15, MIG12, TM6, GR20–GR22)

Status: **MIG12 finished (S1–S9 done, ARC-DEV-120..128).** The session owns the real, game and animation clocks (schema 70), the frame step and the event pump live in `Arcanum.World`, the AI host and the viewer's world time run on the frame clocks, the pause is the Esc menu's alone and the death cover is a frozen frame, and the real list carries the world-map and turn-based steps. Concept document K15 of the
[architecture audit](gap-register.md) and design of MIG12 ([target model](target-model.md#migration-order), element TM6,
decision GQ3 chosen A). Behaviour of the original stays the rows TL1–TL46 of the [original time loop](original-time-loop.md) (read from the
executable); this page fixes how the session represents them, in which assembly each part lives, the order of the code slices and what each must
show. It follows the style of [residency](residency.md), [change of place](change-of-place.md) and [interface modes](interface-modes.md).

Requirements (reference only, no coverage row changes): [FR-LIGHT-001](../requirements/lighting-time.md#fr-light-001) (the one game clock),
[FR-RUNTIME-001](../requirements/world-travel-runtime.md#fr-runtime-001), [FR-TRAVEL-002](../requirements/world-travel-runtime.md#fr-travel-002) (S8) and the
clock and pause rows of [the game clock runtime](../world/game-clock-runtime.md). Each code slice updates the coverage rows it changes in its own PR.

## 1. The problem in one paragraph

Before MIG12 the session had one clock, the game clock, and everything else in play ran on other time bases (the AI host's own `_nowMs`, Godot
time frozen per frame, the frame delta, private counters), while `WorldModal` paused only the game clock for any window (GR20, GR21, GR22), so no
run could be saved consistently with the clock it waits on (MIG14). MIG12 closed that: the one clock set and the one frame step of §3.

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

All slices are merged; each is one PR with its own tests and smokes (the smoke stages are in the PR and the task notes in git history). S2 and S3
changed the clock authority and the saved state, S3 and S4 the session core and threading, so they took the larger review model.

| Slice | Code | Result |
| --- | --- | --- |
| S1 | ARC-DEV-120 | This document (docs-only). |
| S2 | ARC-DEV-121 | `FrameClocks`, `FrameClockKeeper` (real and animation stamps, admission, jump, world milliseconds), `FrameFlags`, `FrameAdmission` and typed `FrameClockFailure` in `Arcanum.Simulation.Clock`; the keeper does not hold the game stamp (`GameClockOwner` stays its only writer), `PlanJump` validates a jump without changing anything. |
| S3 | ARC-DEV-122 | The session owns the three clocks: `AdvanceFrame`, `JumpClocks` (every `ExplicitWait` jumps game and animation together), `QueryFrameClocks`, `ResetFrameReference`, the queue's `GameEventClocks(real, game, animation)`; schema 70 (`clock.realMs`, `clock.animationMs`; step 69 → 70: real 1, animation = game stamp). |
| S4 | ARC-DEV-123 | `FrameStep` in `Arcanum.World` (participant port `IFrameParticipant`, the optional change-of-place ping that opens a frame, `FrameStepLimits`, `FrameStepReport`); the session's `BeginFrame` returns a `SessionFrame` of `8i` units spent in steps at the event deadlines over the three domains; no public ordinary advance, a jump has its own call. |
| S5 | ARC-DEV-124 | `AreaAiFrameParticipant`: once per frame after the event passes, not in a paused or frozen frame (TL23, TL25), a round does not hold it (TL38); `AreaAiHost.NowMs` is the last pump's clock; the NPC change-of-place ping is the frame step's `placePing`. The hero's CD7 ping stays in the viewer. |
| S6 | ARC-DEV-125 | `FrameClockTap` and `WorldTimeline`: the viewer's one monotone world time (µs) from the frame clocks; hero route, attack, cast, projectiles, missiles, eye candy, dying and lying animations, scenery and blood pools read it; the light animation is the tap's world milliseconds. The door animation and drawing-only animations stay on the host clock (§3.7). |
| S7 | ARC-DEV-126 | Pause by GQ3 A: only the Esc menu and its lists pause (`WorldMenuPause`); `WorldModal` stays the hero's input gate; the death cover is a frozen frame; Esc on the world map opens the menu. |
| S8 | ARC-DEV-127 | Real-domain kinds `travel.step` and `combat.step` (not saved, `KeepExisting`), `IRealStepHost`, `FrameStep.RealSteps`, the viewer's `AreaRealStepHost` (world-map step 50 ms after the start, then 62 ms, a jump per step, behind the pause). |
| S9 | ARC-DEV-128 | Re-check of the bugs the clocks could move ([result](../research/open-bugs.md)); GR20–GR22 and MIG12 closed. |

**Lane note (MIG9).** S5 rewrote `_nowMs` and `Pump` in `AreaAiHost.cs` after DEV-119 (the host built per open map) and S4 put the event pump and
frame step in `Arcanum.World` beside DEV-118's `ISectorTerrain`; the AI host's `Pump` rewrite stayed on the host in place and MIG15 moves it with its owner.

## 6. Risks and open questions

Most of the original risks closed with the slices; what still holds:

1. **A world behind an open window.** The world runs behind a dialog, inventory or sheet (GQ3 A). The NPC the hero talks with is held by the AI
   gate's Conversation stop (the open dialog's speaker, and a barter's merchant, whose dialog is suspended under it, GT139; ARC-DEV-177 made the
   callback always set); a pickpocket window has no partner. An item in the hero's hand is put back when a window opens (`HeldThrow`); an open
   window does not stop a projectile in flight, a spell missile or a dying animation. Open, belonging to MIG13's goal work: an attack on the hero
   ends its dialog in the original, and between a dialog's close and the barter it queues the merchant may take one gate step. A native look at an
   NPC walking behind an open dialog stays manual.
2. **Frame time spread.** The 250 ms cut costs game time on a slow frame as in the original (TL46). The only production caller of the frame is the
   viewer's frame loop (`TerrainViewerApp` `_Process` through `TickAreaClock`; automated runs use literal stamps); a map open or window rebuild
   passes no `Frozen` and costs at most the cut on the next frame; the death cover and slide show take `Frozen`.
2a. **Input behind a pause.** `WorldModal` stays the hero's input gate for every window and for the pause itself, so a stick or click never queues
   at one time stamp behind a pause.
3. **Light animation and sector light.** The light's own `_lightAnimationMs` is the frame clocks' world milliseconds since the map's lighting
   bound; the hour and the sector light fold (MIG9 S6) stay the game clock's.
4. **Round and animation events.** TL38's "animation events due within the next second become due at once at a round end" was an inference.
   **Settled by MIG13 S3 (ARC-DEV-132):** the first animation-domain kind is `goal.step`; the executable's round end adds its second to both clocks
   through the ordinary jump, so a step due inside it fires once at the next pass, a rule of the game with no switch
   ([goal table GT18](goal-table.md#the-s3-read-of-the-executable)). The `Light` events stay on the viewer.
5. **Hero walk.** The hero's route follows world milliseconds, so it stops with the pause and runs behind an open window; the follower has no
   "held while modal" rule of its own (opening the menu or the palette stops it by the quarantine). The hero's goal moves to the goal table in MIG13.

## 7. Not ready

Belongs to later migrations, not to MIG12: the consumers are called from the viewer's world step in a fixed order and are not separate
participants; the animation domain's first kind, `goal.step` (MIG13 S3), and the one goal table's hosts and the hero's goal come with MIG13's later
slices, saved runs with MIG14, the remaining hosts with MIG15. The hero's change-of-place request is still pinged by the viewer in the frame of
the request (CD7) until MIG11's hosts are made pending requests. No site passes a frozen frame for a map open or a window rebuild (TL46). Sleep and
the UI timers as real-time chains wait for K7 (GR23). The door animation and the other drawing-only animations stay on the host clock (§3.7). The
text of [the game clock runtime](../world/game-clock-runtime.md) states the rules, not the viewer's wiring.
