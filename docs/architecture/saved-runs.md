# Saved runs, spell runs, projectiles and missiles (K18, MIG14, TM8, GR18)

Status: **S1 (ARC-DEV-207, the concept and the slice plan, docs-only) S2 (ARC-DEV-208, the saved run table, schema 72), S3 (ARC-DEV-209, NPCs open doors) and S4 (ARC-DEV-210, the settled spell runs, schema 73) have landed.** Design of MIG14 ([target model](target-model.md#migration-order), element TM8, decision GQ4 chosen
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

A new `goals` section in the snapshot (schema **72**, upgrade step from 71 adds an empty section: a save from before has no runs and loads as it did). It holds, in slot order:

- the table's `nextRunId`;
- every run that is not done and not owned by the hero (§3.4): slot, run id, owner, flags, path flags, due time, pace, and the frames bottom first (every `GoalFrame` field: goal, node, target,
  item, tile, range, sound, skill, spell run, path, path index, planned target tile, rest, frame flags, animation, frame index, count, path-pending, near, eye candy, fire round, rotation);
- nothing else of the table: the stepping slot is empty at a save point and the exhaustion flag is recomputed.

The codec validates shapes and ranges (slot below 216, at most eight frames, every goal defined, entity strings in the session's grammar, cells inside the map's cell range, flag bits only the defined ones
that a saved run may carry); the owner's `Restore` checks the rest. An owner that is not on the map is not checked by the session (the host's step ends the run of a gone object, as a destroyed NPC's); **until S4 and S5 land, a run owned by `missile:n` or `projectile:n` and a run whose frame names a spell run (`GoalFrame.SpellRun`, goals 26, 27, 83) are not written** (their objects and spell runs are not saved yet, so the frames would dangle), and a hero's missile loses its caster's run in the meantime (owner `pc`, §3.4), a frame's target is an entity the session knows or the run is cut, the due time is not
behind the animation clock by more than the load's slack (a due time in the past simply fires at once, as the original's event queue does).

### 3.2 Steps come from the runs

The `goal.step` kind stays `NotSaved` (the risk-7 rule of the goal table: the animation events are re-created from the saved runs). `Restore` allocates the table, then schedules one
`goal.step` per restored run in slot order with the delay `DueMs - AnimationMs` clamped to at least 1 ms (`ScheduleGoalStep` takes a relative delay and writes the restored animation clock as payload value 2, so a due time in the past fires at once) so the equal-deadline order is the slot order, before the first pass of the frame step; `GoalsReconciled` then finds every run
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

- **Spell runs** (S4, ARC-DEV-210, landed). The session's spell-run table (`GameSessionOwner._spellRuns`), the maintain bar and the last charged cost are the `spells` section (schema 73), and the Game-domain `magic.run` event kind
  (payload `[scheduled game ms, run slot, code]`) is made saved. **Only the settled runs are written**: the runs whose BEGIN has run and that need no host (Maintain and End actions, and the Begin action with the Processed flag: a timed run with a trigger count stays there until its first tick); a run whose BEGIN has not run (the casting goal has not released it, or the parentless run's BEGIN event is pending) or that waits for a host callback is cut with its bar slot and its events, and the goals that name it stay cut too. The overlays a settled run started (eye candy, hover) are cut with the other overlay runs (S5), so the restored run ticks to its END without them. The caster's `_released`, `_missiles` and `_itemChains` are therefore not saved in this slice (the cut runs are the ones they describe); the viewer's gates keep refusing a save while the hero casts or a missile flies.
- **Missile objects** (S5, ARC-DEV-211). The session objects `missile:n` become saved: position (map, cell, offsets), the flight line, pace, step counters and the `_lastMissileNumber` counter, together with the cut runs of S4: the Begin and
  Callback runs, the casting goals that name them (26, 27, 83) and the missile run (goal 28, owner `missile:n`), with the caster's `_released`, `_missiles` and `_itemChains` and the viewer's `_castFacing`, `_noRelease`, `_queuedCasts`, `_launched`
  rebuilt from them. The viewer's `SpellMissile` holds a live caster: it becomes an entity reference the host resolves after the load. A missile run names its spell run (`GoalFrame.SpellRun`), so the missile cannot be saved before the cast.
- **Projectiles** (S6, ARC-DEV-212, with the hero's runs). Every `projectile:n` is the hero's shot or throw (`AreaProjectileHost` has one creator, `FlyShot`): its `PendingShot` holds the hero's attack facts
  (`SessionHeroAttack`: the whole `Combatant`, the weapon, the skill) and the decided `AttackResolution`, so a faithful save needs the hero's combat records in the codec; it belongs with the hero's runs and waits for them.
- Each slice that needs a section of its own raises the schema (S4: 73) and adds its upgrade step in `GameSessionSnapshotUpgrade.cs`.

### 3.6 NPCs open doors (GT49)

Independent of saving but listed under MIG14 in the audit (B10's neighbour, the goal table's row for door passage): the original pushes goal 11 for every walker that meets a closed portal
(GT49, the S7 read); the NPC planner and its step test read the T0 passage rule and never push it, so an NPC passes a closed door the rule admits without opening it, or never sees a door the
session closed. S3 gives the NPC walk the hero's door push over the same node ports (`DoorNode` is already written for the opener goals) and gives the NPC planner the live passages
(`AreaPortals.Passages`), as the hero's and the map stream's planners already have.

## 4. Slices

| Slice | Code | Content | Depends on | Smoke |
|---|---|---|---|---|
| S1 | ARC-DEV-207 | This concept, docs-only | — | none (docs-only, no checks) |
| S2 | ARC-DEV-208 | The `goals` section (schema 72): capture, codec, upgrade step, restore, steps re-created, the hosts' restore; NPC, door and eye-candy runs; the hero's runs, flight runs (`missile:`, `projectile:`) and runs with a spell-run frame are not written | S1 | `ai`, `door-close`, a quick save and load stage |
| S3 | ARC-DEV-209, **done** | NPCs open doors (GT49): door push in the NPC walk; no schema (the NPC planner and step test read the live passages) | S1 (independent of S2) | `npc-door`, `door-close`, `ai` |
| S4 | ARC-DEV-210, **done** | The settled spell runs, the maintain bar and the last cost saved (`spells`, schema 73), `magic.run` made a saved kind; runs before BEGIN or waiting for a callback are cut | S2 | `cast`, `ai-cast`, `saved-runs` |
| S5 | ARC-DEV-211, **done** | The SM30 half for spells (B10): the stage `saved-runs` holds a maintained spell of the hero in a slot of the bar across F7/F8; no code change in the session | S2, S4 | `saved-runs`, `cast`, `maintain-bar` |
| S6 | ARC-DEV-212 and later codes | The cast in progress and the missiles in flight (Begin and Callback runs, casting goals 26/27/83, missile objects and goal 28/78 runs, the caster's marks), the hero's runs (cast, throw, attack, pick pocket, lockpick, states, walk) and the hero's projectiles (shots with the hero's attack facts), cut into codes by the owner's split rule; B11–B12 re-check; the viewer's two save gates go with them | S2–S5 and MIG13 S13 (merged) | the stages of the bug rows |

Order: S2 and S3 are independent in purpose (S3 edits the NPC walk nodes and the planner, S2 the session, the codec and the hosts' restore) but both touch `AreaAiHost`, so whichever merges second merges `main` first; S4 needs S2's section; S5 needs S4 (the
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

## 6. Landed and not ready

**S2 (ARC-DEV-208, schema 72).** `GameSessionSnapshot.Goals` (`SessionGoalState`: the next run id and the runs in slot order) is the `goals` section; `GoalRunTable.Restore` builds the table from the saved runs
(the stepping marker and the exhaustion flag clear); `GameSessionOwner.CaptureGoals` writes the runs that are not done, not being stepped and of a saved kind (not the hero's, not owned by `missile:n` or
`projectile:n`, no frame naming a spell run (`SpellRun`, an eye candy with a tag)); `Restore` validates the section (`ImportGoals`: distinct slots, entity grammar, path index inside the path, the same kind test as
the capture) and `RestoreGoalSteps` queues one `goal.step` per run in slot order with the delay `DueMs - AnimationMs` (at least 1 ms), which also writes the new due time into the run. The codec writes every
`GoalFrame` field (ranges and the canonical slot order checked on decode); the upgrade step 71 → 72 adds the empty section. The `goal.step` kind stays `NotSaved`; the equal-deadline order is the slot order. A
restored session's event sequence counter and next run id are not byte-identical to the saved one (a restored run's step takes a new sequence number), so the round-trip tests compare the snapshots without
those two counters. The viewer needs no change to read a restored table (hosts take runs from `QueryGoals`), and the quick-save gates for the hero's cast and a flying missile stay until S4–S6. The new
smoke stage `saved-runs` (F7, F8 with walking town NPCs) checks the table after the load.

- **Not saved yet:** the hero's runs, the flights (`missile:n`, `projectile:n`, goals 28 and 36), the runs that name a spell run that is cut (S5, S6), and, until their hosts are rebuilt by a load, the overlays (eye candy 45–48, 58, 59, 75, 76 and the hover 44) and the knockback slide (43): their viewer hosts are built by the first play or push, so a saved run would be given up at its first step (found in the review of S2); the NPCs' walks, attacks, critter states, the doors' and the scenery loops' runs are saved. A map change still ends every run (GS19). A due time further off than one step can wait (60 s of real time, 480 000 ms of animation time) is refused on restore.
- **Open (settled in the slices that need them):** a walk step under way at the save (risk 2); the viewer hosts' per-run extras (`_arts`, `_attackPlays`) rebuild lazily from the frames: the art of an NPC
  with a restored `Playing` frame is the first frame the host shows after the load.
- **NPCs open doors (S3, ARC-DEV-209, no schema, no new read of the executable: GT49).** `ExecutorRules` runs goals 11, 12, 49 and 50 for any owner over the door ports (`IAiGoalWorld.Doors`, the hero's `DoorNode`; goal 12 charges the opener 2 action points in a round as the hero's use does); the walk node (`WalkNode`) asks `IAiGoalWorld.ClosedDoorOn(owner, next)` at the start of each tile step and, for a closed idle door on the step, pushes goal 11 with a 50 ms restart (`AiExecutorReason.DoorPush`); when 12 pops the walk resumes at once, so the walker steps in while the door's own run (70) still opens it. A refused attempt ends the whole walk (GT39). The viewer answers the question in `AreaAiHost.ClosedDoorBetween` from the step's T0 assessment and the shown map's portals (`AreaPortals.CrossedClosed`), and gives the door host as the door ports. The tile-step test of the walk runs first (a living critter on the step, then a step that cannot start, GT49 nodes 4 to 6), then the door test (node 7). The planner and the step test of the AI host read `AreaPortals.Passages` (the live state of every door and window), so a door the session made jammed, held or locked is routed around. **Limits:** the window branch of the walk (node 6, goal 14) is not modelled, and a door that refuses the walker only after the planner chose it (a lock the planner cannot see, such as a script lock) ends the walk run, which the next AI pass plans again; the smoke stage `npc-door` (`ARC-DEV-209 native-smoke npc-door PASS`, named only, hero, AI on, `--sector 1409,1314`) sends a free NPC across a closed door and checks the opener goals, the door run, the entry and the open door.
- **The settled spell runs (S4, ARC-DEV-210, schema 73).** `GameSessionOwner.CaptureSpells` writes the runs in the Maintain or End action or in Begin with BEGIN processed (`SpellRunIsSaved`), the bar slots that show them and the last cost; `ImportSpells` checks the shapes
  (`SpellRunTable.Restore`, `MaintainBar.Restore`, entity strings of the objects a run names, only saved actions, no showing slot without its run); the `magic.run` events are saved except those of a run that is cut (`KeepEvent`) and `ImportEvents`
  checks the rest (set at or before the saved clock, slot in the saved table, global scope). The upgrade step 72 → 73 adds the empty section. **Limit:** a run cut at the save is gone after the load (its effect, if it had begun,
  is not undone by a missing END; the item chains of item runs restart empty). The smoke stages `cast`, `ai-cast` and `saved-runs` show no regression; the maintained-spell round trip is covered by session tests
  (`SessionSpellRunSavedTests`) and, since S5, by the viewer stage `saved-runs` (a maintained spell started through the cast host without a casting goal).
- **SM30 for spells (S5, ARC-DEV-211, no code change in the session).** The stage `saved-runs` casts a maintained spell of the hero (the first maintained college spell of the data, taken from the maintain-bar stage's helper), lets it hold a bar slot for five seconds, saves with F7, loads with F8 and requires the loaded run to equal the saved one, the same slot and the next tick queued; with the NPC runs of S2 it closes the spell half of B10 (the plan's wolf at the Crash Site is replaced by the town's walking NPCs, see the smoke plan).
- **The cast in progress, the missiles in flight, the hero's runs and projectiles (S6)** are not started: every hero run has a viewer holder (`SpellCastGoal`, `AttackGoal`, the throw and pick pocket windows, `AreaHeroKnockdown`) whose state a load would have to rebuild, so they are cut into codes of their own.
