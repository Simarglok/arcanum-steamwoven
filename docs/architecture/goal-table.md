# One goal table for every animated object (K14, MIG13, TM7, TM14, GR24–GR27)

Status: **S1 (ARC-DEV-130, the concept and the slice plan, docs-only), S2 (ARC-DEV-131, the pure goal rules in `Arcanum.Simulation.Goals`) and S3 (ARC-DEV-132, the session's table, the `goal.step` kind and the frame step's goal-step host port) have landed; no host or viewer code of MIG13 has.** Concept document K14 of the
[architecture audit](../research/architecture-audit-plan.md) and design of MIG13 ([target model](target-model.md#migration-order), elements TM7, TM14 and the
goal part of TM11; decisions GQ5 chosen A, GQ4 chosen A for MIG14). Behaviour of the original stays the rows GS1–GS32 of the [original's goals](original-goals.md) and
TL38–TL45 of the [original time loop](original-time-loop.md) (read from the executable); this page fixes how the session represents them, in which assembly each
part lives, the order of the code slices and what each must show. It follows the style of [clocks and the frame](clocks-frame.md), [residency](residency.md)
and [change of place](change-of-place.md).

Requirements (S1 changed no coverage row; S2 updates the FR-RUNTIME-002 row, S3 the FR-RUNTIME-001 and FR-RUNTIME-002 rows, the others stay reference only until a host calls the rules):
[FR-RUNTIME-002](../requirements/world-travel-runtime.md#fr-runtime-002) (the actor's goals and interrupts),
[FR-RUNTIME-003](../requirements/world-travel-runtime.md#fr-runtime-003) (the action frame fires once),
[FR-NAV-001](../requirements/world-travel-runtime.md#fr-nav-001), [FR-COMBAT-003](../requirements/combat-magic-tech.md#fr-combat-003),
[FR-COMBAT-006](../requirements/combat-magic-tech.md#fr-combat-006), [FR-COMBAT-012](../requirements/combat-magic-tech.md#fr-combat-012),
[FR-AI-001](../requirements/story-social-ai.md#fr-ai-001), [FR-AI-003](../requirements/story-social-ai.md#fr-ai-003) (doors for NPCs),
[FR-SCENE-014](../requirements/scene.md#fr-scene-014) (a door opens and closes), [FR-DIALOG-001](../requirements/story-social-ai.md#fr-dialog-001),
[FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002), [FR-SOCIAL-004](../requirements/story-social-ai.md#fr-social-004),
[FR-LIGHT-002](../requirements/lighting-time.md#fr-light-002) (time flow; the `Light` event). Each code slice updates the coverage rows it changes in its own PR.

## 1. The problem in one paragraph

The original has **one** goal system: the hero, NPCs, doors, projectiles, spell missiles, eye candy and burning scenery all own runs of one table of 216 slots, stepped by
one handler on the animation clock; the hero's walk, attack, talk, use, pickup, throw, skill and cast are goals of the same 87 types with the same nodes, priorities
and interrupts (GS1–GS3, GS25–GS32). Steamwoven has **five** machines. The NPC executor (`AiExecutorRules`, `AiGoalRun`, `AreaAiHost._runs`) holds one run per NPC of the
shown map, with priorities for 26 goal types, no overlays, no alias table, an interrupt helper that always answers true (`InterruptLowerAnimations`) and a
"priority 3 or less" interrupt (`AreaAiHost.Interrupt`). The hero's actions are viewer hosts beside it: the route follower and grid actor on a movement time
(`RouteFollower`, `DiagnosticGridActor`), `AttackGoal`, `SpellCastGoal` with its queue, the held throw, and five "after route" intents (`_talkAfterRoute`,
`_areaUseAfterRoute`, `_inventoryAfterRoute`, `_pickPocketAfterRoute`, `_portalUseAfterRoute`) that stand in for the goals talk, use, loot, pickup and the door goals.
Doors, projectiles, spell missiles, eye candy, scenery animation, dying, stun, knock-down and get-up are viewer objects and timers, not runs. The consequences
(GR24–GR27, GR33): a stun, knock-down or spell cannot interrupt a hero's action the way it interrupts an NPC's (B11); a walk can replace an attack; talk, use and loot are
not interruptible goals; doors never close by themselves (B15, FR-SCENE-014); the cast effect, the dying body and the thrown item are not runs of one table
(B12–B14); the hero's place reaches the session only at commit points; every hero action is a second implementation of an NPC action. MIG12 gave the clock
(the animation domain of the event queue exists and has no kind); MIG13 gives the table.

## 2. What the original does, in the rows MIG13 follows

| Row | Fact the design keeps |
| --- | --- |
| GS1, GS2 | One table of 216 slots, a 12-byte handle (slot, run id, change counter 0 in single player), lowest free slot, the free slot's id −1; a full table fails the start, sets a flag and the next goal step interrupts every run at level 3. |
| GS3, GS4 | Any object can own runs and one object can own several: a **main run** (bottom goal without the no-interrupt flag) and **overlays**. Some queries look at the object's first run only (runs goal N, the idle test), the others at all runs. |
| GS5, GS6 | A goal frame is the goal type and typed parameters (objects, locations, integers, frame flags); a run holds up to eight frames and flags (done, action frame reached and handled, running, no-walk 0x100, flight 0x400, near-occupied 0x4000, killed 0x8000 …). |
| GS7 | Goal steps are `Anim` events on the animation clock: the first frame 5 ms after the start (1 ms into the live list in a round for a non-current owner, TL41), the next at the run's pace; every end of a run removes the slot's events. |
| GS8, GS9 | 87 goal types (85 with descriptors): priority 1–5, no-interrupt (15 types), carried across a teleport (26), not counted (5), up to three aliases. |
| GS10 | A teleport of the hero writes the runs of travelling owners whose bottom goal has the carry flag to the destination, then interrupts all runs at level 6; walks, attacks, talks, uses and casts never cross a map. The loader was not read (Q1). |
| GS11–GS15 | A start builds a frame, interrupts the object's runs at a level (the goal's priority, or a fixed 3 for the walks, runs, follow, attack, throw and spread out; stun 4 then 1, projectile 4 then 5, knock-down 5 then 3), refuses duplicates for attack, throw, follow and spread out, is gated (start gate: AP and fatigue for an NPC only; movement gate: current participant in a round, not an NPC in a reaction), allocates a slot and queues the first frame. Priority decides replacement as well as interruption. A running walk is retargeted in place, a repeated attack is inserted at the bottom of the stack. |
| GS16–GS21 | The interrupt rule reads the **current** goal's descriptor: level 6 stops all; below 6 a no-interrupt goal is left running (success), priority 5 refuses, priorities 2 and 3 end at level ≥ priority, 1 and 4 at level > priority. An interrupt always ends the whole run with every end node; the sweeps are round start (3), table exhaustion (3), map reset and close (6), teleport (6); a kill ends runs with no rule. |
| GS22–GS24 | A run ends from inside by the end step, by a bottom pop or by a node that kills it; only the stepper's own end calls the subturn hook and the idle restart, so a run stopped from outside is followed by no decision and no fidget. |
| GS25–GS28 | The hero's input: tile click walks (or runs by the always-run rule), object click builds the goal itself with no start gate (attack 18, or 19 with Shift; talk 21; use 38 and 81; pickup 15; throw 37; trap walk 3), spell, skill and item cursors go to the shared starters, the right button's release interrupts at level 3 and turns the hero; in a round the click is taken only while the hero is idle. The hero differs inside the shared code only at the gates, the always-run rule, follow and pickup. |
| GS29 | Talk, use, loot and doors are goals that act in their last node: the dialog, the loot window or the use script opens there; a door's own run plays goal 70, waits 1.5 s and closes through 72 and 71 unless the portal's flag 0x100 is set. |
| GS30, GS31 | Scripts start few goals (perform animation, walk to, run to, use item, steal, get up, the casts). The engine starts stun, knock-down, dying, get-up, knockback, projectile, missile, eye candy, floating text, burning scenery and the fidget; no code starts the hit reactions 29 and 30. |
| TL38–TL45 | The animation clock keeps running in a round and gets the round's 1 s too; goal steps are animation events; a round start interrupts every run at level 3; the TB callback waits for the significant goals (priority ≥ 2, no no-interrupt flag, not "not counted") to finish; a critter that is not the current participant keeps its goals playing free of AP and the gated starters refuse it. |

## 3. Design

### 3.1 The run table in the session

**One table, owned by the session.** A Godot-free value in `Arcanum.Simulation` (new namespace `Goals`: the pure tables, descriptors and decision rules, as the other rule sets are, GQ6 A) holds the 216 slots, the run id counter and the rules that read it; the starters that change the table and the stepper live in `Arcanum.World` and the session (TM11); the session
owns the one instance and mutates it only through its own methods (as it owns `FrameClockKeeper`: no caller outside the session writes a run). A run is an immutable record
of an **owner** (the object's session entity string, so an NPC, the hero, a static portal by its positional identity or a created object; a run never holds a reference), its
**handle** (slot and run id, no change counter), its flags, its **frames** (at most eight, bottom first), the due time of its next step and its pace. Allocation takes the lowest free
slot; a freed slot's id is cleared so an old handle never matches (GS1).

**The frame is typed.** The original's 21 parameter slots become the fields the goals read: the target, the destination tile, the item, the skill, the range, the spell run,
the frame flags (not counted 0x80, follow 0x1000), the path segment and the rest time. S2 added the widened `GoalFrame` and `GoalRun` and the 87-type `GoalType` (the original's numbers and
names, the 26 existing values of `AiGoalType` unchanged) beside `AiGoalFrame`, `AiGoalRun` and `AiGoalType`; S4 replaces those three by them. Every field is a plain value (entity strings, cells, ints) so MIG14 can save a stack
unchanged.

**Descriptors are a static table** (`GoalDescriptors`): per type the priority 1–5, the no-interrupt, carry and not-counted flags and the aliases (GS8, GS9), checked against the executable
in S2. The 15 no-interrupt types start without any interrupt, run beside the main run and survive every interrupt below level 6 (GS3, GS18).

**Queries.** The table answers what the original's helpers answer, each on the set of runs the original reads: the main run of an object and its bottom goal's priority, "runs goal N" and
the idle test on the first run only, "an equal goal is queued" over every run with the alias rule, "a busy run" (carry flag), "the first run attacks", the **significant goal count** of the round
(TL39) and an owner's runs in slot order (GS4, GS20). Because overlays take any free slot, the first-run shadowing of GS4 is reproduced (an eye-candy run in a lower slot hides the walk from "runs
goal N"); it is a rule of the original and S2 keeps it as a named test.

### 3.2 Goals on the animation clock

**One kind, `goal.step`, in the animation domain** of the session's event queue (the first kind of that domain; MIG12 S3 gave the clock and no kind). Entity: the run's owner; values: the slot and
the run id. A step is due at the animation clock's value when it was queued plus the pace (real milliseconds ×8, TL40). The next step is queued **relative to the clock at firing** (the original's default without `-animcatchup`), so a jump of the clock (a round's second, a world-map step, a script's
time action) fires each pending step once and never replays the missed ones (TL22); the AI host's `Math.Max(due, now)` is the same rule. The first frame is 5 ms on the animation clock after the start, or 1 ms into the live list for a non-current
owner in a round (GS7, TL41). Every end of a run removes the run's pending event by slot **and** id (the original compares only the slot and the in-use bit, GS7: a deliberate, safer difference,
no visible effect because the original removes the events at every end). A map close ends the map's runs at level 6 and removes their `goal.step` events (GS19), so no goal event is ever parked; the kind is
**not saved** until MIG14 (as `travel.step` and the spell run: a load starts with no run, as the host's runs do today).

**Who fires it.** The frame step reports the due `goal.step` events of a pass to one host, in order, at their deadlines with the clocks at the due time, and runs another pass after the host has
rescheduled (the pattern of `IRealStepHost`, S8 of MIG12): an `IGoalStepHost` in the world runtime steps the run with the stepper of §3.4 and answers the next delay or the end. Until MIG15 the
node code needs ports whose implementations live in the viewer's Core (`IAiGoalWorld`, declared in `Arcanum.Simulation.Ai` and implemented by `AreaAiHost`), so the host object stays there and the **stepper** (dispatch by goal type, the 100-node
limit, the end handling, the hooks, the reschedule) lives in `Arcanum.World`. The AI host's own pump (`Pump(nowMs)`, `_nowMs`) and the viewer's calls to the hero's hosts leave the frame loop: one
mechanism steps every run, in deadline order.

**Round and animation time (TL38).** The animation clock keeps running in a round and takes the round's +1 s jump; animation events due within it become due at once. This is an inference of
the original that S3 reads again in the executable and classifies before the kind is registered (the open reading of [clocks §6.4](clocks-frame.md#6-risks-and-open-questions));
a bug of the game goes under the D8 "Original"/"Steamwoven" setting. **S3 read it (GT18): it is so, and a rule of the game.** The round end adds 1,000 ms to the game clock and then to the
animation clock through the same jump as every other (TL26), so each step due inside the second fires once at the next pass; the session's round end already jumps both clocks
(`ClockAdvanceReason.ExplicitWait`, MIG12 S3), so both settings follow it and no switch is added.

**The `Light` kind.** TL32 puts two kinds on the animation list: the goal step and `Light`. The viewer's light animation reads the frame clocks' world time since MIG12 S6 and was left without an
event; S3 registers `Light` only if the read of the viewer's `_lightAnimationMs` shows it is cheap, otherwise it stays on the viewer (risk 4, [FR-LIGHT-002](../requirements/lighting-time.md#fr-light-002)).
**S3 decision: `Light` stays on the viewer.** The read shows the viewer's light animation is already a pure function of the frame clocks' world time since the map's lighting bound
(`TickLightAnimation`: one key per frame, a new view only when a light's frame changes), it decides nothing the session keeps, and a session kind would add a chain of events per animated
light with no state to own; it is a presentation clock (§3.9), and no coverage row changes.

### 3.3 Starting, replacing and interrupting (rules, S2; wiring, S4–S5)

**Starters are pure.** A starter takes the owner, the goal, its parameters and a gate port and returns a typed result: `Started(handle)`, or a refusal with the reason (`Gate`, `Interrupt`,
`Duplicate`, `TableFull`, `Loading`, `Destroyed`). The start itself compares no priority (GS11); replacement is decided before it by the starter's interrupt level (GS12, GS13: the table of fixed levels,
the "27 sites refuse, three start anyway" rule), the duplicate refusal (attack, throw, follow, spread out) and the gates (GS15, TL41). **Retarget in place** (a running walk's new destination overwrites the old
and re-plans, GS14) and **insert at the bottom** (a repeated attack, a queued cast) are table operations. The insert on a full stack is refused with a typed result (the original writes a ninth frame, reachability not
shown, GS14, Q8): a deliberate difference.

**Interrupts are the original's rule.** `CanInterrupt(currentGoal, level)` is the table of GS16 and GS18; the per-object walk stops at the first refusal and leaves earlier runs ended (GS17); the sweeps
(GS19) and the kill (GS21) are separate functions. Ending a run runs the **end node of every goal on the stack**, top down, through a port the node code implements (the host's `EndDroppedRun` and
`FreeQueuedCasts` are the first of them); an end from outside runs them once with **no subturn hook, no idle restart and no AP charge** (GS24), the stepper's own end calls the hook and the restart (GS23).
Whether the second end-node pass after an end step has a visible effect is open (Q2): S5 reads it.

**The NPC decision layer stays.** `AiExecutorRules`' decision functions (which goal to start, the pass choices, the attack and flee reasons) ask the table through a port instead of owning a run; the
original's AI pre-check (an NPC is decided for only while its main run's bottom goal has priority 2 or less, GS20) is a table query.

### 3.4 One stepper

`AiExecutorRules.Step` interprets the nodes of 26 goal types; the stepper takes it over as the node interpreter of those goals and gains the nodes of the others as the slices add them: stun, knock-down,
get-up, dying (S6); the door goals 11, 12, 49, 50, 70, 71, 72 (S7); the projectile 36, the missile 28 and the knockback 43 (S8); the eye candy and floating text 44–48, 58, 59, 75, 76, burning scenery 77 and the
scenery restart (S9); the fidget 2, the rotate 53, the animate loop 1 and unconceal 54 that the idle restart and the dialog start (S5), and the script actions (S14: perform animation 0 and 60, walk to 7, run to 55, use item 40), need; and the goals the executor
lacks for the click: 3 (shared with NPCs, GS32), 21, 38 and 81 (S10, S11). A goal type with no node yet is a typed "unknown goal" end, never a silent skip. The presentation
**reads** the run (goal, node, path, due time) to draw and play sound; it no longer owns an animation timer for a rule (the drawing-only frame loops of art stay on the host, §3.9).

### 3.5 Engine goals, doors, projectiles, eye candy (GR26; S6–S9)

- **Critter states (S6).** Damage starts the stun 57 or the knock-down 62 by the damage result's flags (GS31), unconsciousness and the slip on a marked tile knock down, fatigue recovery and healing start
  the get-up 63, death starts dying 31 whose node creates the decoration. The viewer's fallen poses and the NPC lying animation (`AreaFallenPoses`) read the run, and the art choice of `AreaBodyMoves` follows the run's goal. B13 (the dying body
  and the pool) is closed or reclassified here.
- **Doors (S7).** A portal owns runs: use goals 38 and 81 turn into 11 or 49 on a portal, attempts 12 and 50 start the door's own run 70 or 71, 70 becomes 72 after 1.5 s unless the portal's flag 0x100 is set,
  72 retries every 1.5 s (and in a round for a non-current opener the close fails and repeats, TL41). A static portal's identity is positional (MIG7b), so it is a valid owner. Whether a walker that opens a door
  while passing gets goals 70–72 is open (Q5): S7 reads it before the walk's door step changes. B15 and FR-SCENE-014 close here; the pause part of the smoke SM10 is checked after S7.
- **Projectiles and missiles (S8).** A projectile (goal 36) and a spell missile (goal 28) are runs **on a created object**, so S8 gives the directory the kinds `projectile` and `missile` (TM4's creation path)
  as objects that no save keeps yet (MIG14 saves them; a save in flight loses them, as today). The thrown item's landing and the "explode on landing" effect are started by the node (B14), the missile's chase
  of a moved target is the node's retarget (B12). Knockback 43 is a goal of the target.
- **Eye candy, floating text, burning scenery (S9).** Eye candy and floating text (44–48, 58, 59, 75, 76) are overlays (no-interrupt, priority 5) owned by the object they play on, started from the 17 sites of GS31; the combat floaters (`AreaCombatFloaters`) and the
  eye candy host (`TerrainViewerEyeCandy`) read them. Burning scenery 77 is an ordinary priority-1 run (not counted, no no-interrupt flag, GS9). It and the scenery animation restart (kill, then start, GS21) replace `AreaSceneryAnimation`'s own timers.

### 3.6 The hero (TM14, TM11, GQ5 A; S10–S13)

**The hero is an entry like any NPC** (the player's entity), and **its actions are runs of the table**. Its location is written at every step (RL5), which retires the commit points of S4 and the
viewer-owned grid position as the truth of the hero's cell (GR33); the session answers "where is the hero" at every moment.

**The request boundary (GQ5 A).** `Arcanum.World` gets one typed request per player action, applied at once in single player, with no queue, returning a typed result: `WalkRequest` (tile, run), `ObjectClickRequest`
(target, Shift, Alt, the cursor mode from the interface stack), `ThrowRequest`, `UseItemRequest`, `SkillRequest`, `CastRequest`, `StopRequest` (the right button's release: interrupt at level 3 and turn toward the cursor)
and `FaceRequest`. The viewer turns clicks, keys and the stick into requests and draws; the rules inside change state directly, as the original's single-player branch does (SA34). The request set grows with
each slice; the ones outside goals (item moves, combat mode, raising a stat, follower orders) belong to later work. The pure rules are `HeroClickRules` (the decision table of GS26, the start branch of GS27 including
the auto-attack option, the round's "only while idle" rule and the run-flag setters) and the always-run rule (GS25), in `Arcanum.Simulation.Goals` (pure rules; `Arcanum.World` applies them).

**The hero's goals.** The tile click is goal 3 or 4 (retarget in place, GS14); the object click builds the goal itself, with no start gate: attack 18 or 19, talk 21, use 38 and 81, pickup 15, throw 37, the trap walk
(GS26). Talk, use and loot are goals whose **last node acts** through a port: `OpenDialog(target)` (script point 9 then the dialog), `OpenLoot`, `FireUseScript` (point 1), the door goals of §3.5. The five after-route
intents of the viewer disappear: the approach is the goal's own node 8, an interrupt (a stun, a new click, a window that takes the hero's goal along as the original's windows do, S7 of MIG12) ends the run and the intent
with it. Spell, skill and item cursors start the shared starters (the casting run, the skill starter, the item-use starters); the hero's `AttackGoal`, `SpellCastGoal` and held throw retire in S12.

**Walking.** The hero uses the NPC walk goal and the same mover (`AreaAiMovers`), so a stun or knock-down can stop the hero as it stops an NPC once S13 starts the hero's stun on the table (B11). The viewer's grid actor and its movement time (C3) leave the hero's rule
path; the stick and the gamepad become a held `WalkRequest` (the original's mode 3 repeats the walk and marks the run, partial, Q7). The hero's gait, smoothing and the stick's feel are presentation of the same steps (risk 3).

**In a round.** The click is taken only on the hero's turn and only while the hero is idle; the hero passes the start gate (it tests only NPCs), is checked by the movement gate and pays AP in the round host as now
(GS15, GS27, TL44). The round host (G6) stays as the turn machine and reads the table for the wait on animations (§3.7).

### 3.7 Rounds on the table (S5)

The table counts significant goals (priority ≥ 2, no no-interrupt flag, not "not counted", TL39) and calls the round's "all animations done" callback when a slot is freed with the count at zero; this
replaces the host's `CompleteCombatActions` twice-unchanged rule, which stays only as the stall detector TL39 describes. Round start interrupts every run at level 3 (the current goals that are "not counted" are
left), each participant again, and set-turn interrupts the previous participant unless it is the hero (GS19, GS20, TL36, TL37). A critter that is not the current participant keeps its running goals and is refused by the
gated starters; goals started for it get their first frame at 1 ms into the live list (TL41). The subturn hook after a stepper's end is the round host's next frame as now (`FidgetAfterHook`).

### 3.8 Scripts (S14)

The script effects that start goals (perform animation 62, walk to 69, run to 74, get up 76, use item 79, steal 115, the casts 48, 61, 118, 121, 122, 136, kill 58 through the death path and dying 31, the two eye-candy actions) become requests applied by
the world runtime with the starters of §3.3 and their own gates (62 and the casts have none); stop attacking 109 (level 3) and become prone 93 (level 4, no goal) are interrupts. No script action calls the attack,
follow, flee, throw, pickup, wander or fidget starters (GS30). GR27 closes here.

### 3.9 Maps, saving, what stays outside

- **Maps.** A run belongs to its owner's map. A map close or reset interrupts every run at level 6 (GS19). The carry flag of GS10 and the destination loader are open (Q1): until it is read the runs of a travelling owner
  end with the map change, as today (the walk, attack, talk, use and cast never cross in the original either; the animations, hit reactions, dying, door runs, eye candy and floating text do).
- **Saving.** MIG13 saves nothing new: a save keeps no run, no goal stack, no projectile and no missile, a load starts with none, as before (MIG14, GQ4 A, saves the table, the spell runs and the objects in flight in
  the next schema). The frame values are plain so MIG14 needs no redesign. If a slice must raise the schema above 70 it adds the upgrade step in `GameSessionSnapshotUpgrade.cs` in the same PR.
- **Presentation clocks** (zoom, panel slide, the art's frame loops that decide nothing, light shimmer if `Light` stays on the viewer) stay on the host as MIG12 §3.7 says. The door's frames are the exception:
  they follow the door's run.
- **Hosts that stay in the viewer** until MIG15: the implementations of the node ports (`IAiGoalWorld` and its descendants), the art and sound choices, the mover's drawing.
- **The turn machine** (`CombatDomain`) and the AP budget stay (G6); K7 (sleep and UI timers as real-time chains) and the spell run's own saved state (MIG14) are not touched.

## 4. Deliberate differences from the original

| Difference | Why |
| --- | --- |
| Typed frame fields instead of the 21 eight-byte parameter slots; the run's work area and the unsaved sound handle are not modelled. | Same behaviour, saveable values; the original's slots are an implementation detail. |
| A run's id is checked by the event as well as its slot (the original compares only the slot and the in-use bit). | A safer match with no visible effect, because the original removes a run's events at every end. |
| Run ids start at 1 and count up (the original seeds its counter randomly, 0..10,000). | Determinism of tests and saves; ids are never shown. |
| No change counter in the handle. | Single player; the counter is 0 there (GS1). |
| An insert at the bottom of a full stack is refused with a typed result. | The original writes a ninth frame past the stack; reachability was not shown (GS14, Q8). |
| A goal with no node yet ends as a typed "unknown goal" instead of running nothing. | Typed failure at the boundary; each slice adds the nodes it owns. |
| Table exhaustion (read again in S2, GT12): the start fails with a typed result, the table sets its flag, and the next step of any run interrupts every run at level 3 (`SweepIfExhausted`). Both rule settings do it; no switch. | The allocator's own log calls it a warning, the flag has one setter and one reader, and the sweep is a designed recovery, so it is a rule of the game, not a bug. 216 slots are never reached by a map's critters in practice. |
| The significant-goal count is derived from the runs (`SignificantGoalCount`), not kept as a counter. | The original counts at pushes and uncounts at the free, each time testing the run's current goal (GT11); the pushes and pops are the stepper's (S4, S5). The two agree unless a run holds counted goals under a "not counted" goal (open question Q3, no case known). |
| A goal with no descriptor (13, 22) cannot start (a typed `NoDescriptor` refusal) and cannot be on a run's stack at any depth (`GoalRun.WithFrames`, `Allocate` and `InsertAtBottom` refuse it), so the interrupt rule always has a descriptor to read for the current goal. | The original reads the descriptor first and has no starter for either goal (GS32; Q5 asks whether 11 or 12 push 13). What the original's rule would do with a stacked goal that has no descriptor is not read; Q5 decides whether the door goals ever need one. |
| An owner with no run is not idle (`IsIdle`). | The original addresses memory before the run table there (GT7); not shown to be reachable. |
| A run always holds a goal; the original's empty run (stack top −1) is never kept. | A bottom pop ends the run, and the iterator skips empty runs anyway (GT4). |
| Events fire at their own deadlines inside a frame (GI5), not all against the ping's final clock. | Decision GI5 kept by MIG12; the order of two goal steps in one frame is deadline order, not the host's fixed order. |
| Requests are applied at once and return a typed result; no client request is sent (the network branch of every rule is not followed). | GQ5 A and SA34. |
| The hero's gait and the stick's feel are presentation on top of the goal's steps. | The original draws the same steps; the feel is tuned in the viewer. |

No switch is added by S1 or S2. Where a slice's read of the executable shows a behaviour that is a bug of the game rather than a rule, it is classified by the
[original executable check](../research/original-executable-check.md) and put under the D8 "Original"/"Steamwoven" setting in that slice's PR (candidates still known: the second end-node pass, the animation
events that fall due at once at a round's end, TL38; the table exhaustion was classified a rule in S2).

## 5. Slices

S1 is this document. The codes of S2–S15 are handed out by the coordinator one by one, each checked unused; no code is reserved here. A slice is one PR and updates the coverage rows it changes; the
smoke stages are the ones the change touches (`./arcanum smoke pr --stages …`, one Godot window at a time), the full map chain only in the daily package. The reviews are Opus (rule 2 of the review-model
rule): every slice compares the original, changes the session core or is over 800 lines, except a slice that is only a wording or a row.

| Slice | Code | Result | Tests and evidence |
| --- | --- | --- | --- |
| S1 | ARC-DEV-130, **this document** | The concept K14 and the plan; the MIG13 row and the K14 line updated. | none (docs-only) |
| S2 | ARC-DEV-131, **done** | Goal rules, no caller: `GoalType` (all 87, the original's numbers and names), `GoalDescriptors`, the interrupt rule and levels (GS16–GS19), the start and replacement rules with the fixed levels, duplicates and gates as ports (GS11–GS15), retarget and insert (GS14), the pure `GoalRunTable` (216 slots, handles, lowest free slot, the GS4 queries, the significant-goal count). Reads the executable for the 87 names and the descriptor table, GS2's sweep, Q8. **Landed:** all of it in `Arcanum.Simulation.Goals` (`GoalType`, `GoalDescriptors`, `GoalFrame`/`GoalRun`, `GoalInterruptRules`, `GoalRunTable` with its interrupts, sweeps and kill, `GoalStartRules` with `GoalStarters`); the read is [the S2 read](#the-s2-read-of-the-executable) (names, descriptors, the alias direction, GS2 classified a rule; Q8 not read: the typed refusal needs no answer). | 61 synthetic tests (a priority table test from GS9, the aliases and their direction, the first-run shadowing, the fixed levels, the level matrix, the sweeps, the kill, the retarget, the refusals); no smoke (the executable is read, not run) |
| S3 | ARC-DEV-132, **done** | **Landed:** `GameSessionGoals.cs` (the table in the session: `StartGoal`, `InterruptGoal`, `InterruptGoals`, `KillGoal`, `KillGoals`, `QueryGoals`, `QueryGoalStep`; the step protocol `BeginGoalStep`/`EndGoalStep` with `SessionGoalStepAnswer`; a map change ends every run at level 6, `CloseGoalMap`; an NPC that departs to another map loses its runs; before every pass a run whose step went missing or left the active map is killed), the kind `goal.step` (animation domain, scoped to the owner's map, one per run, not saved, `MaxGoalSteps` = 216, `ChecksOwnEntities`: the pass's existence test is skipped and the handler checks the run; the first step 5 ms, the next the delay ×8 from now), `SessionEventPassResult.GoalSteps`, `IGoalStepHost` and `FrameStep.GoalSteps` (no host: a due step ends its run; a host that throws abandons the pass's remaining steps, `SessionGoalStepAnswer.Abandon`), `GoalEndKind.Finished`; the TL38 read (GT18, a rule) and `Light` kept on the viewer (§3.2); [the S3 read](#the-s3-read-of-the-executable). No host and no caller in the game yet; schema 70. Planned as: The session owns the table and the `goal.step` kind on the animation domain (not saved): start, interrupt, kill, query and the event's scheduling and removal; `IGoalStepHost` in `FrameStep` (a pass reports the due steps, the host steps them, one more pass follows); the TL38 reading and classification; the `Light` decision. No host yet. | Session tests (events at deadlines, removal by slot and id, map scope, no schema change), frame-step tests with a fake host; the `map` frame-clock check. Landed: `GoalTableStateTests` (Session, 16), the departure case of `ChangeOfPlaceMoverTests` (both settings), `GoalStepTests` (World, 9). Smoke `map` failed on this branch and on `main` 97373513 in the same place before the frame-clock check (the placement after the attack smoke, `AREA_SMOKE_GATE_CLOSED`), so it is no evidence for or against S3 |
| S4 | ARC-DEV-134, **done** | **Landed:** `AreaAiHost._runs` and `Pump` are gone: an NPC's run is a run of the session's table, started by the host through `StartGoal` (the executor's starters decided the replacement, so the host ends the old run itself and starts with the `NoInterrupt` plan and open ports; the starters' own plans and levels are S5's), dropped through an interrupt at level 6 (every goal yields to it) with the host's end nodes, its queued cast written beneath the stack through the new `ReplaceGoalFrames` (GS14; refused for the run being stepped), and stepped by the frame step: the host is the `IGoalStepHost` (the viewer binds `AreaAiGoalSteps`, which asks for the shown map's host and lets a run wait a frame with none) and its per-frame part is `Frame` (the movers settle, the leaves and the round are observed after the last step). The one stepper `GoalStepper` lives in `Arcanum.World`: it dispatches a run by its current goal to the executor's node interpreter (`AiExecutorRules.InterpretedGoals`, the 26 goals; any other goal ends as `GoalStepperEnd.UnknownGoal` with its end nodes), keeps the interpreter's 100-node limit and ends, calls the end hooks (`IGoalStepHooks`: the end nodes of a dropped run, the queued casts an end step leaves, the idle restart) and answers the next frame delay (0 is the next frame). `AiGoalType`, `AiGoalFrame`, `AiGoalRun` and `AiRunFlags` are replaced by `GoalType`, `GoalFrame`, `GoalRun` and `GoalRunFlags`; a starter proposes a run with an unallocated handle; the overlay's reason is carried by the caller (the table's run keeps none). The facade `RunOf` (the owner's first run in slot order), `AnimationRunning`, `AnimationPriority` (the descriptors' priorities, equal to the host's former table for the 26 goals) and `RunsBesides` read the table. **A change of S3's contract:** a start, an interrupt, a kill and a stack change are admitted inside an event pass (the AI tick's perform starts goals there, as the original's heartbeat does); their events are the next pass's and a reported step whose run ended is skipped; only opening and closing a step stay outside a pass. **Changed order (risk 2):** a run's steps fire at their deadlines on the animation clock inside the frame's passes, in deadline order, instead of one pump at the frame's end (at most one step per run per frame); the first step comes 5 ms of animation time after the start (the next pass) instead of at the same frame's pump; a round end's second makes the steps due inside it fire once (TL38); the former 64-step cap per frame is the frame step's limits. The alias and equal-goal tests of the executor's starters still read the NPC's one run (`current`, which is its first run): the host starts no overlay, so reading all runs changes nothing yet and is S5's with the starters' plans. Schema 70, no setting. | `GoalStepperTests` (World, 3), the in-pass start (`AiSchedulerTests`) and the stack replacement (`GoalTableStateTests`) in Session, the executor's interpreted goals (`GoalDescriptorsTests`), the viewer's AI tests stepped through the frame step (`AreaAiTestSteps`); smoke `ai`, `ai-cast`, `ai-follower`, `ai-turn-based`, `walk` |
| S5 | next | Real interrupts, end nodes and rounds: GS16–GS24 in the host (the always-true `InterruptLowerAnimations` goes), end nodes on every end, the hooks only on the stepper's path, kill, the sweeps, round start at level 3, the significant-goal count and the TB callback (TL39), TL41's first frame, the AI pre-check as a query; the second end-node pass read (Q2). GR25 closes. | Round tests; smoke `turn-based`, `ai-turn-based`, `ranged`, `ai` |
| S6 | next | Critter states as runs: stun 57, knock-down 62, get-up 63, dying 31 for NPCs; fallen poses and body moves read the run; B13 re-checked. | Smoke `knockdown`, `death`, `ai`, `ranged` |
| S7 | next | Doors as runs: the door goals, the 1.5 s close, flag 0x100, the round rule; Q5 read; B15 and FR-SCENE-014 close; SM10's pause part checked. | Portal unit tests; `TerrainViewerAreaPortalSmoke` and the new door-close check of the smoke plan's SM10 |
| S8 | next | Projectiles, spell missiles and knockback as runs on created objects (kinds `projectile`, `missile`, unsaved until MIG14); B12 and B14 re-checked. | Smoke `cast`, `ranged` |
| S9 | next | Eye candy, floating text, burning scenery and the scenery restart as overlays and runs. | Smoke `floaters`, `cast`; `TerrainViewerSceneryAnimationSmoke` |
| S10 | next | The hero's request boundary and the tile click: `WalkRequest`, `StopRequest`, `FaceRequest`, the hero's walk goal on the NPC mover, the location written at every step (commit points retire), the always-run rule, retarget in place; Q7's mode 3 read. | Request tests; smoke `walk`, `transfer`, `roundtrip` |
| S11 | next | The hero's object click (GS26, GS27): talk, use, loot, pickup, door and trap goals; the five after-route intents go; the talk partner's `ConversationCallback` hold-out closes; B33 re-checked. | Smoke `container`, `barter`, `pickpocket`, `ai`, `census` |
| S12 | next | The hero's combat actions as shared goals: attack, throw, skill, item use, cast; `AttackGoal`, `SpellCastGoal`, the held throw and the hero's flying bodies retire. | Smoke `cast`, `ranged`, `turn-based`, `pickpocket` |
| S13 | next | The hero's states on the table: knock-down, stun, dying and get-up for the hero; the death flow reads them; the round's hero rules (TL44) complete; B11 closes. | Smoke `knockdown`, `death`, `turn-based` |
| S14 | next | Scripts start goals (GS30): the script effects as requests; stop attacking and become prone as interrupts; GR27 closes. | Interpreter tests; smoke `ai` |
| S15 | next | The re-check: the bug rows B11–B15, B33 and the smokes SM06, SM08–SM12, SM17 and SM24 of the [smoke plan](../research/open-bugs-migration-smoke-plan.md); GR24–GR27 (and TM7's part of GR13, the far branch's level-4 interrupt) and the MIG13 rows closed; this document's status. | Smokes of the stages the slices touched; the full map chain in the daily package |

**Order and lanes.** S2 → S3 → S4 → S5 is one line (each needs the one before). S6–S9 need S5 and are independent of each other in files except the viewer's shared hosts; S10 needs S4 and S5 but not S6–S9; S11 needs S7 and S10; S12 needs S8 and
S11; S13 needs S6 and S12; S14 needs S5, S6, S9 and S10; S15 is last. `AreaAiHost.cs` is rewritten by S4, S5 and S6 and also holds the NPC's door use and ranged shots, so S6 runs before S7–S9 and no other lane edits it in that time; S3 shares `FrameStep` and the event registry with MIG14's future
work only. MIG15 (the hosts into the world runtime) runs alongside and closes after MIG14: a slice that leaves a port in the viewer's Core says so in its report. No slice starts before the previous one of its line has merged.

## 6. Risks and open questions

1. **Size of S4 and S5.** `AreaAiHost` is about 2,250 lines and reads `_runs` from many places (`RunOf`, `Interrupt`, `AnimationRunning`, the casts, the follower catch-up, the round host). S4 moves the storage first
   behind today's calls and changes no behaviour; S5 then makes the rules real. If S4's diff passes about 800 lines of rules the slice is cut again by the owner's split rule.
2. **Frame order.** Today the AI pump runs once per frame after the passes, and the hero's attack, walk and cast run in the viewer's world step in a fixed order. As goal steps are events at their deadlines the order
   of two steps inside one frame becomes deadline order. Smokes that assume the old order (`ai`, `cast`, `ranged`) are restated, not retuned, and the change is listed in S4's report.
3. **The hero's feel.** The grid actor, the stick and the route follower carry tuned input smoothing; the hero as a goal with per-tile steps must not change the walking speed, the gamepad stick or the click-to-walk
   response. S10 keeps the speed rules of `MovementRuntime` and runs the `walk` stage and a manual run of the hero before it merges; a held stick is a design call for S10 (Q7 open) and may keep the grid actor as the stick's mover
   until the mode 3 reading is done.
4. **`Light` and the round's animation catch-up (TL38).** Settled in S3: the round's second is a jump of game and animation time, a rule (GT18); `Light` stays on the viewer (§3.2).
5. **Projectiles as objects.** S8 needs a transient directory origin for created objects that no save keeps; the snapshot must skip them without a version change and MIG14 replaces the skip. The directory's wipe at a map change
   and a load must remove them.
6. **Original questions not yet read.** Q1 (the carried runs' loader), Q2 (the second end-node pass), Q3 (frame flag 0x80), Q5 (door goals during a walk), Q6 (spell data goals), Q7 (mode 3, the Alt action, the called shot, the use
   node's turn test), Q8 ([original goals](original-goals.md#open-questions)). Each is read in the slice that needs it and classified before the rule is written; a question that cannot be settled gets a documented default and a switch.
7. **MIG14 compatibility.** Frames must be savable (entity strings, cells, ints, no objects) and the handles stable; a slice that adds a field a save could not hold says so. The animation events are not saved, so MIG14 must
   re-create them from the saved runs' due times.
8. **Network.** The original's host and client branches of every starter are not followed (GS11, GS17, R6 rows); the request boundary is the place K11 adds them.
9. **Time.** The hero's actions run on animation time now: since MIG12 S7 an open window drops them through the input gate; on the table each window interrupts the hero's run at the original's level (dialog start 3, world map 4, character editor 4, GS20; MIG16 S4 owns the gate). S11 and S12 list each window that assumed a held hero.

## 7. Not ready

S1 landed this document and the update of the K14 line and the MIG13 row. S2 landed the pure rules: `GoalType`, `GoalDescriptors`, `GoalFrame`, `GoalRun`, `GoalRunTable`, `GoalInterruptRules`, `GoalStartRules` and `GoalStarters`
in `Arcanum.Simulation.Goals`, with 61 synthetic tests and 23 rows of the [function registry](../verification/functions.csv). S3 landed the session's one table and the `goal.step` kind on the animation domain, the step
protocol and the frame step's `IGoalStepHost` port, with 27 synthetic tests and one more registry row (0x423C80). S4 put the AI host on the table: the NPCs' runs are session runs stepped by the world
runtime's `GoalStepper` through the viewer's node ports; no save, schema or setting changed (schema 70).

- **Not started.** Everything after S4. The hero is still a set of viewer hosts, doors, projectiles, missiles and eye candy are still viewer objects,
  stun and knock-down still cannot interrupt a hero's action (B11), doors still never close (B15), and the AI host starts one run per NPC with no start plan of its own (the executor's starters decide the replacement and the
  host ends the old run itself at level 6; the always-true `InterruptLowerAnimations` stays until S5). The node ports (`IAiGoalWorld`) and the end hooks are still the viewer's `AreaAiHost` (MIG15). The rules end runs and report what ended; running the
  end nodes, the round's callback and the gates' answers are the callers' (S4, S5); the session removes the events of every run that ends. The end nodes of the runs a start's interrupts, an
  interrupt, a kill or a map change end are reported (`GoalRunEnd`) and run by nothing yet (S5); the 1 ms first frame of a non-current owner in a round (TL41), the table-exhaustion sweep at the head
  of a step and the round-start sweep are S5's, and so is clearing the exhaustion flag at a map close. Whether the original interrupts or kills an NPC's runs when it is teleported
  to another map is not read: S3 kills them (as 0x423FF0 does for an object removed with its map), since a run belongs to the active map.
- **Not decided here.** The stick as a held request (S10), the transient object origin (S8): each slice settles its own and records it. The table exhaustion sweep was decided in S2 (a rule, both settings).
- **Not part of MIG13.** Saved runs, spell runs, projectiles and missiles in the snapshot (MIG14); moving the remaining hosts into the world runtime (MIG15); sleep and the UI timers as real-time chains (K7);
  the item, stat, combat-mode and follower-order requests of the boundary; the hero's change-of-place request as a pending request (MIG11's CD7).

## The S2 read of the executable

Behaviour read from the retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`, `Version.txt` "VERSION:1.0.7.4", the owner's Mac installation; nothing was copied or run) statically with `llvm-objdump` and from the file
image (the name table and the descriptor table), by the reader of ARC-DEV-131 over the rows GS1–GS32 of [the original's goals](original-goals.md), each of which two readers had read: a row of this table that agrees with a GS row is
F2, and the reading adds what the GS rows left open. Addresses are the executable's; behaviour is in prose. The registry rows of the implementing methods cite this section.

| ID | Function (address) | Original 1.0.7.4 | Level | Steamwoven (S2) |
| --- | --- | --- | --- | --- |
| GT1 | goal-name table 0x5A164C | 87 pointers to NUL-terminated names, index 0..86, then a NULL; the names are the `ag_` names in the comments of `GoalType`. | F2 (GS8) | `GoalType` (numbers), `GoalTypes.OriginalName` |
| GT2 | descriptor table 0x5B03D0 | 87 pointers then −1; the entries of 13 and 22 are NULL, the other 85 are read entry by entry: priority (+4), no-interrupt (+8), carry (+0xC), "not counted" (+0x10 bit 0), the alias list (+0x14, three entries, −1 ends), the node count (+0, 1..14, read by no code). They are **identical to GS9**: priorities 1 (4 types), 2 (16), 4 (4), 5 (14), 3 (47), the 15 no-interrupt types, the 5 not-counted, the 26 carried, every alias list in the executable's order. | F2 (GS9) | `GoalDescriptors`; the table is a test against GS9 |
| GT3 | "runs goal N" 0x44E830, "equal goal queued" 0x44E710, "first run attacks" 0x44E940 | All three take the **queried** goal's descriptor, walk the candidates (the goal itself, then its alias list, up to three) and compare each with the run's **bottom** goal. The lists are not symmetric (3 lists 7 and 4, 7 lists 4, 3 and 55, 65 lists 7 and 7 does not list 65, 18 lists only 19, 19 lists 18 and 20), so the direction matters: the run is "the same activity" when its bottom goal is in the queried goal's list. 0x44E830 and 0x44E940 look at the first run only; 0x44E710 walks every run and compares the frame's parameter slots 0..5, a mismatch at the sixth not being tested (five object parameters); the starters' duplicate tests call it through the forwarder 0x44E6F0, which passes no out-handle (the caller then learns only whether such a run exists). 0x44E940 also compares the targets through their leaders (0x45DDA0) and has a party-wide branch. | F2 (GS4; the direction was open) | `GoalDescriptors.IsSameActivity(queried, bottom)`; `RunRunningGoal`, `EqualGoalQueued` (target and item), `FirstRunAttacks` (leader port, no party branch) |
| GT4 | run iterator 0x44D2F0 and 0x44D340 | Slot order over the 216 slots; a slot qualifies when its run flag 1 (in use) is set and 2 (done) is clear, its stack top is 0 or more, its slot index is not −1 and its owner equals the object. | F2 (GS4) | `GoalRunTable.RunsOf` |
| GT5 | main run 0x423300, its priority 0x4233D0 | The first qualifying run whose bottom goal's descriptor has no-interrupt 0 is the main run; the priority query answers that descriptor's priority, 0 when there is none. | F2 (GS3, GS4) | `MainRunOf`, `MainRunPriority` |
| GT6 | busy run 0x44E8C0, exact bottom goal 0x44E650 | Busy: the first qualifying run whose bottom goal's carry flag is 0. Exact: the first qualifying run whose bottom goal is N, no alias (a continuation after a given run starts at the next slot without the owner and in-use filter, not modelled). | F2 (GS4, GS10) | `BusyRun`, `RunWithBottomGoal` |
| GT7 | idle test 0x423470 | On the owner's **first** run: stack top 0 (one goal), its current goal 0, 1 or 2, and the animation of the object's art id (field 1 through 0x503E20) is 0. With no run the first-run index is −1, which addresses memory before the table (undefined in the original). | F2 (GS4); the art condition and the no-run case are new | `IsIdle(owner, artAnimationIsIdle)`; no run is not idle |
| GT8 | interrupt rule 0x44E2C0 | The current goal is the frame at the stack top. Order: a run not in use answers 0; during a game reset (0x402CA0) or with an empty stack the run is interrupted with no test; level 6 skips the next two tests; a no-interrupt goal answers 1 and is left; priority 5 answers 0; priority 3 interrupts at level 3 or more, 2 at 2 or more, every other (1 and 4) above its priority. An interrupt marks the run done (0x2); for the slot in the stepping marker 0x5A5978 it answers 1 and stops; otherwise it removes the run's events by slot (0x45BF10), runs the end node of every frame from the top down (none during a reset) and frees the slot (0x44D180 → 0x44CF20). | F2 (GS16, GS18) | `GoalInterruptRules.Verdict`, `GoalRunTable.Interrupt` (the end nodes and events are the caller's) |
| GT9 | per-owner interrupt 0x424070 | The level must be 0..6; a non-zero quiet argument lowers it to 0; every run of the owner in iterator order is interrupted by 0x44E2C0 and the first answer 0 ends the walk with 0 (earlier runs stay ended); an owner with no run answers 1. The network branch comes first and is not followed. | F2 (GS17) | `InterruptOwner(owner, level, quiet)` |
| GT10 | sweeps 0x424250, 0x424290, 0x424310 | 0x424250: while the in-use count is above 0, every slot in use at level 6, a refusal answers 0. 0x424290(level): every slot in use at the level, a refusal only logs. 0x424310: every slot in use whose current goal is not "not counted" (0x44C9A0) at level 3, a refusal logs. | F2 (GS19) | `ClearAll`, `InterruptAll`, `InterruptForRoundStart` |
| GT11 | counting 0x44C840, 0x44C8F0, 0x44C9A0; the free 0x44CF20 | A goal counts (uncounts) when its descriptor has priority 2 or more and no no-interrupt flag and the run's **current** frame is not "not counted" (descriptor +0x10 bit 0 or frame flag 0x80). The free first clears the interface's attack target when the bottom goal is 18 or 19 for the hero, uncounts every frame from the bottom up (each time testing the current frame), clears the slot, decrements the in-use count and, whenever the counter is then 0 and the callback 0x5E34F8 is set, calls it: after **every** free. | F2 (GS22, GS23) | `GoalRun.CurrentGoalNotCounted`, `GoalDescriptors.IsSignificant`, `SignificantGoalCount` (derived, §4), `GoalFreeResult.AnimationsDone` |
| GT12 | allocator 0x44CCB0; stepper head 0x423566 (GS2) | The lowest slot with run flag 1 clear: slot index, run id (the counter, then +1), change counter 0, flags 1 (in use), the owner, current-frame pointer and due time cleared, stack top −1, the in-use count +1. With no free slot it writes a warning to the log that says the animation slots ran out, sets 0x5E34F4 and answers 0 (so the start fails and nothing is queued). The stepper reads 0x5E34F4 first: when set it runs 0x424290(3), clears the flag and then steps its own run. **Classified a rule, not a bug:** the log calls it a warning, the flag has exactly one setter and one reader, and the sweep is a designed recovery; both rule settings follow it, no switch. | F2 (GS1, GS2) | `Allocate`, `ExhaustionPending`, `SweepIfExhausted` |
| GT13 | kill 0x423FF0, 0x44E160 | The per-object kill is skipped while 0x4A7FB0 answers non-zero (the light-scheme hour); else every run of the owner goes to 0x44E160: not in use answers 0; the run gets run flags done and killed (0x8002); the stepped slot answers 1 (the stepper ends it); otherwise events are removed, every frame's end node is called from the top down (they do nothing for a killed run) and the slot is freed; an answer 0 stops the walk. | F2 (GS21) | `Kill`, `KillRun` (the light-scheme gate is the caller's) |
| GT14 | walk starter 0x433640, single-player branch | After the movement gate (0x4339A0), a hero that the always-run rule (0x4304C0) turns into a runner at encumbrance below 4 goes to the run starter 0x433C80. Otherwise 0x44E830(owner, goal 3): with no match it builds the frame without an interrupt, interrupts the owner at level 3 (0x424070, quiet 0) and starts; with a match it compares the **bottom frame's** destination (slot 5, at +0x68 and +0x6C of the run) with the new one: equal answers 1 with no change, different sets the run's path flag 0x4 (+0xC00) and overwrites that destination, answering 1. | F2 (GS14: the third reading) | `GoalStartRules.RetargetWalk`, `GoalRunTable.RetargetDestination` (the bottom frame's `Tile`, the run's `Replan` path flag) |
| GT15 | Q8, not read | Whether the single-player insert beneath a full stack (GS14) is reachable, and how a run whose object no longer resolves is freed when no sweep comes, were **not read** in S2. The first needs no answer for the rule (the insert is refused with a typed result in any case, §4); the second is the stepper's (S4, S5). Q8 stays open in [the original's goals](original-goals.md#open-questions). | not read | `InsertAtBottom` refuses a ninth goal |
| GT16 | GS3 against GS12, GS17, GT8, GT9 | GS3 says a no-interrupt goal "starts at interrupt level 0, which stops nothing", so that it runs beside the main run. Read with the rules, level 0 **ends** nothing, but the verdict is still asked of every run of the owner: a run whose current goal is not an overlay refuses level 0 (its priority is 1 or more and level 0 is not above it), the walk stops there and, at the 27 refusing sites, the start is refused. So an overlay started through 0x44D4E0 (`GoalStarters.Generic`) beside a running main run is refused, and the overlays that must run beside one are the starters that build through 0x44D500 with no interrupt at all (GS3's last sentence; `GoalStarters.NoInterrupt`). S2 codes GS12, GS17 and GT8 as they read; which starter of S7, S9 or S10 uses which plan is that slice's reading of its own call sites. | F2 (GS3 loosely worded, GS12) | `GoalStarters.Generic`, `GoalStarters.NoInterrupt`, `GoalRunTable.InterruptOwner` |

## The S3 read of the executable

Read by ARC-DEV-132 from the same retail 1.0.7.4 `Arcanum.exe` (SHA-256 `9bd80511…78d52`, the owner's Mac installation) statically with `llvm-objdump`, into the session scratchpad; nothing was copied or run.
Behaviour is in prose; the registry rows cite this section.

| ID | Function (address) | Original 1.0.7.4 | Level | Steamwoven (S3) |
| --- | --- | --- | --- | --- |
| GT17 | first frame in 0x44D730, next frame 0x423C80 | The start builds the pair {0, 5} with no conversion and adds the Anim event by delay (0x45B800), so the first step is 5 ms of **animation** time ahead; in a round, for an owner that is not the current participant, it goes straight into the live list through 0x45B820 instead (TL41). The next frame shifts the frame delay left by three (real ms ×8) and, unless the `-animcatchup` flag 0x5DE6D0 is set, adds it from the clock now (0x45B880); with the flag, from the previous due time (0x45BA30). | F2 (GS7, TL40, TL41) | `FirstGoalStepDelayMs` = 5, `GoalStepClockRate` = 8, the repeat from now in `EndGoalStep`; the round's 1 ms path and `-animcatchup` are not followed (S5; no option) |
| GT18 | round end 0x4B7740, jump 0x45C200 | The round end builds {0, 1,000} (0x45A950) and calls the jump 0x45C200, which adds the pair to the game clock 0x5E8600 and then to the animation clock 0x5E8608 (0x45AA50 each, with carry) and tests nothing; it then sets up the next round (0x4B73A0). The animation list is processed after the real and game lists against its clock as it is then (TL20), so a step due within the second fires once in the same ping when the round ends inside the turn-based handler. TL38's inference is confirmed. A rule of the game: every jump moves the two clocks together (TL26). | F2 (TL26, TL38) | The session's round end is the same jump (`ClockAdvanceReason.ExplicitWait` moves game and animation); a pending step due inside it fires at the next pass; both settings; no switch |
