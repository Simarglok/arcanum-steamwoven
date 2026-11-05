# Clocks and the frame step in the session (K15, MIG12, TM6, GR20–GR22)

Status: **concept written; S2 (ARC-DEV-121) landed the pure clock rules, S3 (ARC-DEV-122) the session owning the three clocks (schema 70).** This page was written as slice S1 of MIG12 (ARC-DEV-120, docs-only, no checks run). Concept document K15 of the
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
jump is visible to the later lists of the same sub-step (TL20) and a handler sees the clock at its own deadline. The bound of today's
`AreaEventPump` (a maximum of steps per advance and passes per step, late firing reported when the bound is hit) stays and moves with the pump
(S4). A jump made inside a pass is applied at once, and the pass continues against the new clocks.

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
and load lists the menu opens) raises it; it stops game and animation time and nothing else. Dialog, inventory, sheet, barter, logbook, world
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
| The viewer's dev palette and bug report are windows with no original counterpart and do not pause. | Under GQ3 A only the Esc menu pauses; the palette and report are tools, not play states. |
| A saved game keeps the pause and the world-map step is not a saved event. | The original does not save its pause counter (TL23) and does save the real-list kinds 12 and 14 (TL32); here a save during a journey or with the menu open restores the menu closed and the journey ended at the last step. A save is offered from the menu, which holds the pause, so the saved pause is the menu's own; both are viewer states the load re-creates. |
| The death cover and slide freeze all three clocks. | The original's slide show does not ping (TL25); today's viewer holds a Menu pause there. |

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
| S4 | ARC-DEV-123 | The frame step and the event pump in `Arcanum.World` with the participant port; the viewer calls one `FrameStep(hostMs, frozen)`; the change-of-place ping runs first (CD7 goes); the lighting hour from the pass. | World tests over synthetic participants (order, one stamp, bound, late firing, jump seen by later domains); smoke `walk`, `ai`, `debug-travel` and the map battery (`map`). |
| S5 | ARC-DEV-124 | The AI host and the movers on the clocks: `_nowMs` and `host.Pump(WorldMilliseconds())` go; runs step on world milliseconds, the AI tick on game time; the pump is gated by the pause. | Host tests (`AreaAiHostTests`, `AreaAiRouteTests`, follower and round tests); smoke `ai`, `ai-turn-based`, `ai-follower`, `follower-home` (the AI pump pings the change of place, DEV-116). |
| S6 | ARC-DEV-125 | The other consumers read the clocks: hero attack and cast, projectiles, spell missiles, eye candy, dying and lying animations, body moves, blood pools, scenery, the light animation (`Light` events on the animation domain); `WorldTime` leaves the rules. | Self-tests that assert time; smoke `cast`, `ranged`, `life`, `death`, `knockdown` stages touched; a grep check that no rule file reads `Time.GetTicks*`. |
| S7 | ARC-DEV-126 | The pause by GQ3 A: only the Esc menu pauses, the world gate no longer closes for the other windows, the death cover freezes; the hero's input gating by the stack stays. | self-tests `TerrainViewerWorldMainSettingsTest`, `TerrainViewerAreaSceneFlowTest`, `TerrainViewerAreaMainMenuTest`; smoke `menu`, `scene`, `inv-ui`, `barter`, `death`; a manual look at an NPC walking behind an open dialog. |
| S8 | ARC-DEV-127 | The real-time domain: the world-map journey as a chain of real events (62 ms steps), the turn-based step event on the real list; the delta loop and explicit waits go. | Session and viewer tests (a step behind the menu still fires, the time jump per step); smoke `worldmap`, `debug-travel`, `turn-based`, `ai-turn-based`. |
| S9 | ARC-DEV-128 | Re-check of B-bugs that depend on time, coverage and status rows, this document's status. | `./arcanum smoke pr` for the stages touched; the daily full chain covers the rest. |

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
   starts) needs a look. S7 lists them in its report.
2. **Frame time spread.** The 250 ms cut costs game time on a slow frame (a load, a long GC) as it does in the original; the viewer's long
   operations (map open, window rebuild) must report `Frozen` or the admitted time is lost. S4 lists the call sites.
3. **Light animation and sector light.** The light's own `_lightAnimationMs` and the sector light fold (MIG9 S6) use the clock hour; S6 moves the
   animation only, the hour stays the game clock's.
4. **Round and animation events.** TL38's "animation events due within the next second become due at once at a round end" is an inference;
   S3 reads it again in the executable when the queue gets the animation domain, and the result is classified before the first animation-domain
   kind is registered. S3 (ARC-DEV-122) gave the queue the animation clock but registered no animation-domain kind, so no event can fall due
   at a round's end yet; the reading moves to the slice that registers the first one (the `Light` events of S6 or the goal steps of MIG13).
5. **Hero walk.** The hero walks on a movement time (C3) that the world gate holds; the hero's goal moves to the one goal table in MIG13. In
   MIG12 the hero's route follows world milliseconds (S6) so it stops with the pause and runs with an open window; the route follower's own
   "held while modal" rule goes in S7.
6. **Determinism of tests.** Self-tests and smokes that today set a test time (`_worldTestTime`) switch to passing frame stamps; any
   assertion on a Godot-time constant needs a restatement in S5 and S6.

## 7. Not ready

Everything above is a design except the pure rules of S2 (`FrameClockKeeper`) and the session's ownership of S3: the session holds the
three clocks, admits the frame and saves the real and animation stamps (schema 70), but the frame step does not exist yet, the
viewer's pump steps only the game part of a frame at the deadlines (all three clocks together is S4), no host passes a frozen frame
and the death cover still holds a Menu pause (S7); the AI host,
projectiles, missiles, eye candy, attack and cast read Godot time; the Menu pause covers every window; no real or animation event exists; the
viewer keeps calling the frame from `AreaAmbientDriver`. The text of [the game clock policy](../world/game-clock-policy.md) and the TL rows' last column describe
that state until the slices land.
