# Saved runs, spell runs, projectiles and missiles (K17, MIG14, TM8, GR18)

Status: **S1 (ARC-DEV-207, the concept and the slice plan, docs-only).** Design of MIG14 ([target model](target-model.md#migration-order), element TM8, decision GQ4 chosen
A: the snapshot holds the run table with every goal stack, the spell runs with their maintained marks and the projectiles and missiles in flight). Behaviour of the original stays
the rows PS11, PS12, PS15, PS18, PS26 and PS31 of the [original's persistence](original-persistence.md) and GS1–GS10 of the [original's goals](original-goals.md); this page fixes how the
session saves them, in which assembly each part lives, the order of the code slices and what each must show. It follows the style of [the goal table](goal-table.md), [clocks and the frame](clocks-frame.md) and
[residency](residency.md).

Requirements: [FR-LIFECYCLE-003](../requirements/ui-media-lifecycle.md#fr-lifecycle-003) (one consistent save payload),
[FR-RUNTIME-002](../requirements/world-travel-runtime.md#fr-runtime-002) (the actor's goals), [FR-AI-003](../requirements/story-social-ai.md#fr-ai-003) (doors for NPCs),
[FR-SCENE-014](../requirements/scene.md#fr-scene-014) (a door opens and closes), [FR-COMBAT-003](../requirements/combat-magic-tech.md#fr-combat-003) and
[FR-MAGIC-012](../requirements/combat-magic-tech.md#fr-magic-012) (maintained spells). Each code slice updates the coverage rows it changes in its own PR.

## 1. The problem

A save keeps no goal run, no spell run, no projectile and no missile. A quick load or a map change therefore ends every NPC's action (NPCs restart their decisions), closes no pending door
(a door open at the save stays open until used), ends a maintained spell, loses a shot or a missile in flight and, today, the viewer refuses to save while the hero casts or a missile flies
(`TerrainViewerAreaQuickSave.cs`, the bug report). The original saves the animation module's run table with every goal stack (PS15), the spell-run table (PS11) and the animation and
magic events of the time queue (PS12); the objects in flight are objects and so are saved whole (PS20). MIG13 made every run a plain value (`GoalRun`, `GoalFrame`: entity strings, cells,
integers, no objects), so the table can be written as it is.

## 2. What exists after MIG13

- `GameSessionOwner._goals` is the one `GoalRunTable` (216 slots, each a `GoalRun` with its handle `{slot, runId}`, its owner entity, up to eight `GoalFrame`s, flags, `DueMs`, `PaceMs`);
  `NextRunId`, the stepping slot and the exhaustion flag are table state. The `goal.step` event (Animation domain, entity = owner, values `[slot, runId, scheduled animation ms]`) is the
  run's next step; its deadline is `GoalRun.DueMs` on the animation clock, an absolute time that the saved clocks (schema 70) already carry, so it needs no rebasing.
- `goal.step`, `spell.run`, the real-time kinds and the TB step are kinds with `Persistence = NotSaved`; `GameEventQueue.Restore` refuses a `NotSaved` kind. `GoalsReconciled` kills any
  run on the active map that has no pending step, so a restore must put runs and steps back together.
- Hosts keep a few stores outside the table (§3.3). Missiles and projectiles are session objects of the origins `missile:n` and `projectile:n` kept outside the saved stores, with
  their flight (line, pace, steps) and, for a shot, `PendingShot` in the host or the session; their counters restart at 1.

## 3. Design

### 3.1 The saved section

A new `runs` section in the snapshot (schema **72**, upgrade step from 71 adds an empty section: a save from before has no runs and loads as it did). It holds, in slot order:

- the table's `nextRunId`;
- every run that is not done and not owned by the hero (§3.4): slot, run id, owner, flags, path flags, due time, pace, and the frames bottom first (every `GoalFrame` field: goal, node, target,
  item, tile, range, sound, skill, spell run, path, path index, planned target tile, rest, frame flags, animation, frame index, count, path-pending, near, eye candy, fire round, rotation);
- nothing else of the table: the stepping slot is empty at a save point and the exhaustion flag is recomputed.

The codec validates shapes and ranges (slot below 216, at most eight frames, every goal defined, entity strings in the session's grammar, cells inside the map); the owner's `Restore` checks
the rest: the owner exists on the active map (a run of an object the save does not know is dropped), a frame's target is an entity the session knows or the run is cut, the due time is not
behind the animation clock by more than the load's slack (a due time in the past simply fires at once, as the original's event queue does).

### 3.2 Steps come from the runs

The `goal.step` kind stays `NotSaved` (the risk-7 rule of the goal table: the animation events are re-created from the saved runs). `Restore` allocates the table, then schedules one
`goal.step` per restored run at its `DueMs`, in slot order so the equal-deadline order is the slot order, before the first pass of the frame step; `GoalsReconciled` then finds every run
stepped. A run that was mid-step at the save (the stepping slot is set only inside a step, which a save never interrupts) cannot occur.

### 3.3 Host state is rebuilt, not saved

| Store (viewer) | After a load |
|---|---|
| `AreaAiHost._arts` (animation and frame of an NPC) | rebuilt from `GoalFrame.Animation` / `FrameIndex`; an NPC with no animation run takes the art of its life state |
| `AreaAiMovers._movers` cell and facing | already saved as `npc.pos` (MIG7c); a slide in progress is lost: the NPC stands on the cell its step committed (the original writes the location at the step's start, 0x4305D0) and the run continues from its path index |
| `_routeIndex`, `_routes`, `_ownHomecomings` | recovered from the saved place (`RouteIndexOf`) |
| `_attackPlays` (start on the world clock) | restarted from the frame: the attack animation replays from its start |
| `_castFacing`, `_noRelease`, `_queuedCasts`, `_launched` | rebuilt from the cast frames and the spell run (S4); a cast whose spell run is not saved is cut |
| `AreaDoorHost` | has no store: door state is the session's openable frame plus the door's runs |
| `_passChoices`, `_usedItems`, `_listCache`, `_shown`, `_traces` | caches, dropped |

Every viewer start time is on the run-local world clock (`FrameClocks.WorldMs`, restarts at 0): none of it is saved, a restored host takes the new world time as the start of what it replays.

### 3.4 What is not saved yet

The hero's runs (owner `pc`) are not written: the hero's walk follows a route in the viewer, and S12 and S13 of MIG13 move the hero's cast, throw, pick pocket, skill and states onto the table. A load kills
the hero's runs as today (a load starts with none for the hero). **S6 saves the hero's runs after MIG13 S13 has merged.** A companion is an NPC and is saved like one.

### 3.5 Projectiles, missiles and spell runs

- **Objects in flight** (S4). The session objects `missile:n` and `projectile:n` become saved: position (map, cell, offsets), the flight line, pace, step counters, the shot (`PendingShot`: the attack
  resolution, the hit, the thrown item that is hidden while in flight, the boomerang flags) and the `_nextShot`, `_lastMissileNumber`, `_lastProjectileNumber` counters. The viewer's `AreaProjectile` and
  `SpellMissile` hold live objects (`AreaObjectRecord`, `Critter`, `Caster`): they become entity or key references that the host resolves after the load. The owner's flight run (goals 28, 36) is in the
  run section (S2) and rebuilds the host entry; the object, not the host, is the truth.
- **Spell runs** (S5). `SessionSpellRuns` (`_spellRuns`, the maintain bar) and the caster's `_released`, `_missiles` and `_itemChains` are saved with the `spell.run` events (the event kind is made
  saved, the Game-domain payload `[scheduled game ms, run slot, code]` is already valid). A maintained mark is saved with its run (PS11, PS26).
- Both slices raise the schema again only if they need a section of their own (73, 74); each adds its upgrade step in `GameSessionSnapshotUpgrade.cs`.

### 3.6 NPCs open doors (GT49)

Independent of saving but listed under MIG14 in the audit (B10's neighbour, the goal table's row for door passage): the original pushes goal 11 for every walker that meets a closed portal
(GT49, the S7 read); the NPC planner and its step test read the T0 passage rule and never push it, so an NPC passes a closed door the rule admits without opening it, or never sees a door the
session closed. S3 gives the NPC walk the hero's door push over the same node ports (`DoorNode` is already written for the opener goals) and gives the NPC planner the live passages
(`AreaPortals.Passages`), as the hero's and the map stream's planners already have.

## 4. Slices

| Slice | Code | Content | Depends on | Smoke |
|---|---|---|---|---|
| S1 | ARC-DEV-207 | This concept, docs-only | — | none (docs-only, no checks) |
| S2 | ARC-DEV-208 | The `runs` section (schema 72): capture, codec, upgrade step, restore, steps re-created, the viewer's save gate narrowed; NPC, door and eye-candy runs; the hero's runs dropped | S1 | `ai`, `door-close`, a quick save and load stage |
| S3 | ARC-DEV-209 | NPCs open doors (GT49): door push in the NPC walk, live passages in the planner; no schema | S1 (independent of S2) | `door-close`, `ai` |
| S4 | ARC-DEV-210 | Missiles and projectiles in flight saved (objects, flight, shots) | S2 | `ranged`, `cast` |
| S5 | ARC-DEV-211 | Spell runs and maintained marks saved; the cast-while-saving gates go | S2, S4 | `cast`, `ai-cast` |
| S6 | ARC-DEV-212 | The hero's runs saved; B10 and B11–B12 re-check | S2–S5 and MIG13 S13 | the stages of the bug rows |

Order: S2 and S3 are independent of each other (S3 edits the NPC walk nodes and the planner, S2 the session, the codec and the hosts' restore); S4 needs S2's section; S5 needs S4 (the
missile's caster run); S6 is last and waits for the other thread. No slice edits the goal table files S12–S15 of MIG13 edit (`ExecutorHero*`, the hero hosts, `goal-table.md`'s S-rows) except the
one-line status of its own row; a slice that must touch them merges `main` first. A slice cut larger than about 800 lines of rules is cut again by the owner's split rule.

## 5. Risks and open questions

1. **Schema.** Another thread may raise the schema; each slice merges `main` before the bump and adds its upgrade step (Opus review: schema/session core).
2. **A step under way.** The NPC's walk step is a slide between two cells; the cell is committed at the step's start. A save during a slide restores the NPC on the committed cell with the run at its
   path index; whether the next node re-issues the step or ends the walk is settled in S2 with a test (the original restores the same: the location is the committed one).
3. **Equal deadlines.** The events fire in sequence order; restoring in slot order changes the order of equal-due runs against the original's list order (not saved either; PS12 sorts by due time only).
4. **Rounds.** A save inside a turn-based round restarts combat from the critters around the hero with the order rebuilt (PS13); the saved runs of that round carry on over the rebuilt order. The
   interplay (the callback "all animations done" after a load) is checked in S2's smoke.
5. **The hero's runs** wait for S6; the viewer's gates that refuse a save while the hero casts or a missile flies are narrowed in S2, S4 and S5 and gone in S6.
6. **Network.** Not touched (K11): the runs are plain values a client could receive.

## 6. Not ready

This page only fixes the design; no code, schema or setting changed (schema 71).
