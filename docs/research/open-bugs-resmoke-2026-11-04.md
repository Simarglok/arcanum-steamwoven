# Open bugs: re-smoke on `main` after MIG1-MIG5 and MIG10

Status: **Verification result**, work item ARC-RES-090 (2026-11-04). Docs-only: no source change. It fixes nothing; the bug-fix hold
of the [architecture audit](architecture-audit-plan.md) stays on.

This is the re-check the [smoke plan](open-bugs-migration-smoke-plan.md) (ARC-RES-089, #601) asked for: each of its 46 bugs
(B01-B46) against `main` 8c88e906, which holds the record resolver and its consumers (MIG1 #598, MIG3 #604), carried companions
(MIG2 #602), the companion body from home (MIG4 #607), the one dialog host (MIG5 #610), transfer parity (MIG10 #597), the GI6, GR19
and clock-seed switches (#605, #606, #608) and the route-approach fix ARC-WORLD-121 (#585).

## Answer

- **Gone: 1** (B43, the barrel route, by a real-data regression on the reporters' six start cells; the live-critter half is not shown).
  **Changed: 1** (B05, the carried-companion half). **Present: 31** (23 with a reproduction or a refusal still in the source, 8 by cause only or with a narrower claim). **Unverified: 13** (no run or reading here decides; each says what would).
- The migrations merged so far are the resolver, the visitor and companion plumbing and the dialog host. They do not touch the
  causes of B01-B04 (MIG6), B06-B08 (MIG7, MIG9), B10 (MIG7, MIG9, MIG14), B09 and B16 (MIG11) or B11-B15 (MIG12-MIG14), so those rows are
  where the plan expected them. The plan's "Gone" set (B01, B02, B04, B05, B09, B10, B15) is not reached yet: only B05 moved, and only on the carried path.
- **B02 still reproduces, identically.** The town window of `main` gives `inventory-seed map=1 status=InventoryRefused detail=UnlistedInventoryItem at 2345`, the
  line of the reports, in the `town` step of the full chain and in the town hop of the `main` step.
- **Nothing found that a migration made worse** in the stages that exist: the Crash Site and `main` steps of the full chain pass, and so do `transfer`, `carry`,
  `visitor-talk`, `visitor-body`, `visitor-fight`, `quickload`, `ai-follower`, `wreck` and `altar`.
- **Four smoke failures on clean `main`** (none caused by this task): `life` (known; seen once here, ticket 4043: "the follower is drawn on screen with the bars"), and, each reproduced twice, the
  `hover` stage on the town and wild steps ("Scenery: the highlight is the body's frame drawn additive" and "no object kind is hovered") and the `dying` stage on
  the elven ruins ("the dying animation's frames were drawn: 5"). No earlier run of the same stages was kept to compare, so it is not known whether they predate the migrations. They are
  a finding for the coordinator, not a bug of the 46.

## Method and its limits

- **Runs.** The whole chain `./arcanum smoke full` twice (the first with the build), `./arcanum smoke pr --stages ...` for the stages the chain does not run on the
  Crash Site (`ai`, `ai-cast`, `ai-follower`, `premade`, `pickpocket`, `floaters`, `wreck`, `altar` on `--sector 1452,1291`), and the real-data test
  `AreaTransitionOriginalTests` (the cave regression of #585) with the installed game's inputs. The whole `TestCategory=RealData` set of the viewer tests ran once with the installed game's inputs (exit 0; most tests skipped for inputs that are not set).
  Logs were read from the launch queue's run logs.
- **Not done.** The plan's 25 new stages (SM01-SM30 less the extended ones) do not exist and this task adds none: a stage belongs to the PR of the slice that fixes its bug. So a bug with no existing
  stage is classed by the **cause in the current code**, which is said in the row. A cause read from code is a reading, not a reproduction.
- **Report saves.** The 44 report folders each hold a save (`save/slot0000.json`, schema 57 or 58). The viewer has no way to load a save without clicking through the main menu, so none was replayed. The
  saves matter for B05: every one lists Virgil as a follower and none carries `npc.home`, the fact MIG4 added, so an old save is outside what MIG4 repairs (its documented limit).
- **Report content.** The report folders hold original game content and stay out of git. Log lines are named by their ids and outcomes only.
- **Evidence tags.** R: a run of this task (smoke, test; ticket numbers of the queue's run logs in the notes). C: the current source (`file:line`). L: a log line of the report
  checked against the current source. "Since the reports" means commits after a156e1435c (2026-10-29).

## Results

Status words: **Present** (the symptom's cause is in the current source, or it reproduced), **Changed**, **Gone**, **Unverified**.
"Plan" is the expectation of the smoke plan (Gone / Change / Stay / Re-verify).

### G1. Object model

| ID | Plan | Status | Evidence |
| --- | --- | --- | --- |
| B01 | Gone (MIG6) | Present (cause), not reproduced | C: created items keep per-kind keys, `made:` counter, `src/Arcanum.Session/GameSessionInventory.cs:117`; MIG6 (one id space) is not merged. B02 below stops the town window first, so the chain cannot reach the report's path. No stage covers it. |
| B02 | Gone (MIG6) | **Present, reproduced** | R: `inventory-seed map=1 status=InventoryRefused detail=UnlistedInventoryItem at 2345` on the town window, in the `town` step (ticket 4038) and in the `main` step after the town hop (ticket 4035); L: same line in the reports. C: the refusal is `AreaInventory.Build`, `src/Arcanum.Data/Inventory/AreaInventory.cs:216`, untouched; MIG5 removed only the dialog's `IN` refusal. Crash Site (map 1) and cave (map 79) seeds are `Ready`. |
| B03 | Change | Present | C: worn items are refused, `GameSessionNpcInventory.cs:117` and `GameSessionPickPocket.cs:103` (`WornItemUnsupported`), unchanged since the reports. |
| B04 | Gone (MIG6) | Present (cause), not reproduced | R: the `junk` stage is `NONE (no container with two items)` at the Crash Site and the ruins step did not reach it (it stopped at `dying`). C: the pile is still a per-kind created object; MIG6 not merged. |

### G2. Residency and NPC state

| ID | Plan | Status | Evidence |
| --- | --- | --- | --- |
| B05 | Gone (MIG4, MIG5, MIG9) | **Changed** | R (carried path works): `transfer`, `carry` (Visiting, drawn), `visitor-talk` (map 79, home 1, script 1324 starts), `visitor-body` (stored cell read from the home map by a 3x3 window, 3.2 s), `quickload` (Companions=1, same map and other map) all PASS. C: the refusal of the report, `visitor not talkable ... no body kept this run`, no longer exists in the source (MIG4). **Not covered:** the report's own path (the hero's window moves to the town, then a quick load) has no stage, and the report's saves lack `npc.home` (limit named by MIG4); a companion left in a window the hero no longer holds is MIG9. |
| B06 | Change (MIG7, MIG9) | Present | C: `AreaAiHost.RunWaypoints` returns `false` and `RunStandpoints` is empty, `tools/terrain-viewer/Core/AreaAiHost.cs:651-652`; the class header lists "routines and waypoints, saved NPC positions" as NOT READY (`:173-176`). |
| B07 | Change | Unverified | No stage reads placed NPCs against the map rows (SM07 does not exist), and nothing merged since the reports touches placement. Needs the census of the plan. |
| B08 | Change | Unverified | `ai` and `ai-cast` PASS but do not cast from outside the near radius. Nothing merged touches the reaction to a spell hit. Needs the executable reading the plan names first. |
| B09 | Gone (MIG11) | Present | C: no code writes or reads a teleport map for a former follower (no match in `src` or `tools`); `EntityHomes.AwayProperty` (`npc.map`) is the only away fact (`src/Arcanum.World/EntityHomes.cs:14`). |
| B10 | Gone (MIG7, MIG9, MIG14) | Present | C: saved NPC positions and runs are NOT READY in the AI host header (`AreaAiHost.cs:173-176`); a companion's cell is a session fact (`npc.cell`), a placed NPC's is not. R: `quickload` checks the hero and the companion only. |

### G3. Goals, clocks and projectiles

| ID | Plan | Status | Evidence |
| --- | --- | --- | --- |
| B11 | Change (MIG13) | Present (cause) | C: the hero's actions are viewer hosts beside the NPC executor; MIG12-MIG14 are not merged. Not run (no hold-effect stage). |
| B12 | Change (MIG13, MIG14) | Present (cause) | C: spell effects are not runs of one table (GR26). The changes since the reports in `AreaSpellCastMap.cs` and `TerrainViewerSpellCast.cs` are the companion targets (MIG3) and the loaded-round rule (#608), not the effect. `cast` PASS draws one effect and does not test several targets. |
| B13 | Change (MIG13) | Present (cause) | C: dying is not a run (GR26), unchanged. R: the `dying` stage FAILS on the elven ruins (see the failures above), so the existing check of that animation is not green either. |
| B14 | Change (MIG13) | Present (cause) | C: projectiles are viewer objects; unchanged. R: `throw`, `scatter`, `boomerang` PASS cover flight and landing cells, not an explosion on landing. |
| B15 | Gone (MIG13) | Present | C: no door close timer in the source (no match for auto-close in `src`, `tools`); `TerrainViewerAreaPortals.cs` only opens and closes on use. |

### G4. Change of place

| ID | Plan | Status | Evidence |
| --- | --- | --- | --- |
| B16 | Change (MIG11) | Present | C: no loading presentation or change-of-place request in the source; MIG11 not merged. R: `transfer` PASS enters and leaves the cave without one. |

### G5. Script surface (script audit slices)

The scripting layer (`src/Arcanum.Scripting`, `ScrAdmission.cs`, `ScrInterpreter.cs`, `ScrCatalogue.cs`) has no commit since the reports; the dialog host moved (MIG5) but none of the refusals below is in it.

| ID | Plan | Status | Evidence |
| --- | --- | --- | --- |
| B17 | Stay | Unverified | R: `wreck` PASS starts script 1696. C: `ClosedByHost` is only the player's Leave (`DialogConversation.Close`, `AreaConversationPanel.Leave`); whether the reporter's close was a Leave or a reply with no option is not decidable from the log alone. The low-intelligence variant has no test. Needs the DLG 1696 reading. |
| B18 | Stay | Present | C: the examine script (point 0) has no host, `docs/research/script-trigger-audit.md:66,193` (slice 9), hover says it is not run (`TerrainViewerHotkeyHover.cs`). |
| B19 | Stay | Present | C: `get area` on the start map returns `NotModelled` (`AreaUseScripts.cs:285`, `AreaConversation.cs:368,427`); action 29 (get reaction) is in no admission set (`ScrAdmission.cs`). |
| B20 | Stay | Present | C: focus kind 18 is in no admission set; refused at `ScrAdmission.cs:412` (`FocusKindNotAdmitted`). |
| B21 | Stay | Unverified | No code, test or doc names script 1643; the condition at its line 2 was not read. Probably the original's behaviour, as the plan says. Needs the executable reading. |
| B22 | Stay | Present | C: action 113 (unfog town map) is in no admission set and not in the catalogue. |
| B23 | Stay | Present (identity unverified) | C: action 33 is not admitted (`ScrAdmission.cs`), so tile script 30286 refuses at its line 2; that the tile is the trash can's is not established. |
| B24 | Stay | Present | C: the Summon component is "not ready", `src/Arcanum.Session/SessionSpellCaster.cs:313`, `tools/terrain-viewer/Core/AreaSpellCasting.cs:86`; a test asserts the line. |
| B25 | Change (MIG9) | Present for NPCs that are not companions | R: `altar` PASS on `--sector 1452,1291`, as SM28 of the plan says (Virgil fires the tile script; on the default sector the stage fails). C: tile scripts fire only for a companion of the shown map (`AreaMapTransfers.cs:96-108`); the assassin's agent has no caller. |
| B26 | Stay | Present | C: no `XP` operation in the dialog grammar (`DialogConversationGrammar.cs`, `DialogOperationParser.cs`); `experience-runtime.md` lists it as not wired. |

### G6. Interface, messages and art fonts

| ID | Plan | Status | Evidence |
| --- | --- | --- | --- |
| B27 | Stay | Present | C: the message log and the hover text are Godot `Label`s (`AreaMessageLogView.cs:62`, font sizes at `:18,159`; `TerrainViewerAreaHover.cs:62`); `AreaArtTextDraw` serves menus, saves, the dialog box. No commit since the reports touches the font path (`TerrainViewerAreaHover.cs` has three, for the resolver and dialog host). R: the `hover` stage fails on the town and wild steps (see above), so its check is red there. |
| B28 | Stay | Present | C: floaters use `ThemeDB.FallbackFont`, size 14 (`AreaFloaterLayer.cs:17`). R: `floaters` PASS (it checks the floaters, not their font). |
| B29 | Stay | Present | C: the conversation panel is Godot labels (`AreaConversationPanel.cs:15-47`). |
| B30 | Stay | Present | C: no sort buttons or worn/bag switch (`AreaLootArt.cs:22,76` only Take all and the holder grid). |
| B31 | Stay | Unverified | C: dropping a held item on a hot key (digit key or a slot click) exists (`TerrainViewerHotkeys.cs:62-85`, `TerrainViewerHotkeyBar.cs:189-196`); a drag release inside the inventory is not a target (`AreaInventoryDrag.cs:24`). R: `hotkey-bar` PASS (item on entry). Whether the report meant the drag is not decidable. |
| B32 | Stay | Unverified | C: the read path exists (`TerrainViewerAreaItemUse.cs:25-27` to `TerrainViewerAreaReading.cs:55-90`). R: `reading` PASS. Whether item `made:21` is a written note is not known. |
| B33 | Stay | Present (original unverified) | C: the companion menu is a portrait button, left click only (`AreaFollowerPortraits.cs:160,209`); no right-click handler for a companion. The original's mapping needs the executable. |
| B34 | Stay | Present | C: a quick save sets the HUD status string only, no message is posted (`TerrainViewerAreaQuickSave.cs:117-123,300-304`). Whether the original posts one is unverified. |
| B35 | Stay | Present | C: as B34 for the load (`:173-180`). |

### G7. Hosts not wired or unverified

| ID | Plan | Status | Evidence |
| --- | --- | --- | --- |
| B36 | Stay | Unverified | C: the boots slot, the drop and the armour rule exist (`AreaInventoryDrag.cs:56,109`, `EquipmentRules.cs:44`). R: `body armour` PASS is a body armour, not boots. A re-check needs the reporter's hero. |
| B37 | Stay | Unverified | C: object hit points come from the prototype (`GameSessionObjects.cs:81,562`); nothing names the Control Box. Needs the prototype read. |
| B38 | Stay | Present | C: cue sounds play only on the hero's own attack (`TerrainViewerAudioCues.cs:193-231`, called from `TerrainViewerAttack.cs:430,718`); no sound on the hero's hurt or the hero's spell (`TerrainViewerSpellCast.cs` plays eye candy only). |
| B39 | Stay | Present | C: experience comes from weapon attacks only (`GameSessionAttack.cs:760-776`, `GameSessionNpcAttack.cs:282-300`); spell damage (`SessionSpellCaster.cs:923-935`) calls neither, and there is no other caller of `RecordNpcKill`. |
| B40 | Stay | Unverified | R: `container` PASS on a crate (`generation=Changed/Regenerated`), but the Crash Site chest is not that case. C: the `Unsupported` outcome comes from `PlanRestock` (`GameSessionRestock.cs:262-287`); which branch the chest took is not known. Needs a run on the chest. |
| B41 | Stay | Unverified | R: `spell-pane` PASS (colleges 0, 6, 11) and `hotkeys` PASS with a spell. Whether the reporter's hero had spells is not known. |
| B42 | Stay | Present | C: floaters are posted from attack results only (`TerrainViewerAreaFloaters.cs:56-69`); `docs/project/status/viewer.md:15` says so. |

### G8. Pathing, passability and render order

| ID | Plan | Status | Evidence |
| --- | --- | --- | --- |
| B43 | Re-verify | **Gone** (route selection, by the real-data regression); the critter half is not shown | R: `AreaTransitionOriginalTests` on the installed original passes (ticket 4051): from the six report start cells it routes to all three cave barrels and the exit with every step an open step. C: the fix chooses the target cell by the cost of a whole route (`AreaRouteApproachPlanner.cs:46`); live critters do not block outside combat (owner decision 2026-10-21, `AreaRouteSource`), and the regression runs with the critters passing, so the reporter's rat hypothesis is untested. |
| B44 | Stay | Unverified | C: the cave passability is built from the original (`AreaStepInspector.cs`, `AreaStepRules.cs`); the opt-in test only checks the arrival reaches the exit and stays inside a box (`AreaStepOriginalDataTests`), not the exit cells one by one. Needs the research the plan names. |
| B45 | Stay | Unverified | C: the hero's shade is the tile class under his cell plus local light, recomputed every draw (`AreaLocalLighting.cs:164-166`, `AreaAmbient.cs:22`); no test follows a hero out of a window. Cause not decidable from source. |
| B46 | Stay | Unverified | C: an animation frame keeps the body's draw-order key (`AreaScenePlan.cs:153-155,237-238`); equal keys fall to source index. R: `scenery animation` PASS on the main step (frames change and draw), but the `hover` stage on the town step fails on the scenery highlight (additive frame); whether that touches the fire is not known. No test orders a fire over its background. |

## Tally

| Status | Bugs | Count |
| --- | --- | ---: |
| Gone | B43 | 1 |
| Changed | B05 | 1 |
| Present | B02, B03, B06, B09, B10, B15, B16, B18, B19, B20, B22, B24, B26, B27, B28, B29, B30, B33, B34, B35, B38, B39, B42 (a reproduction or a refusal still in the source); B01, B04, B11, B12, B13, B14 (the cause is in the source, not reproduced); B23, B25 (narrower claim) | 31 |
| Unverified | B07, B08, B17, B21, B31, B32, B36, B37, B40, B41, B44, B45, B46 | 13 |
| Total | | 46 |

## Follow-ups this result points to (not started)

- **The four red smoke stages** (`life`, `hover` on town and wild, `dying` on ruins) belong to a fix task of their own; the daily full chain cannot pass until they do.
  Result (ARC-DEV-083): all four were bugs of the smoke checks, red since the stages were added (also on cee56c3e, before the migrations): `life`
  left the camera on another NPC, `hover` picked a body a roof covers, one with too few pixels, or an animated scenery, and counted a window
  with no bodies (the wilderness) as a failure, `dying` assumed a target that was not knocked out first. They also hid one real regression:
  the `town` step's carry check fails (`AREA_MAP_CARRY_FAILED`) because the companion body from home (MIG4) meets B02 on the town window; B02's fix
  (#614) removes that failure, after which the town step meets the combat stages whose fixtures are written for the Crash Site (ARC-DEV-089).
  Result (ARC-DEV-089): all bugs of the smoke checks, none of the game; the `town` step (and the Crash Site, wilderness and elven ruins batteries) pass.
  `attack`, `cast`, `turn-based` and `ai` took the weakest NPC of the map, which in a town may stand under a roof or walled in: they now take the
  first (weakest first) the hero can stand next to and see from there (`AreaSmokeFightableStartAsync`; no such NPC prints `NO-TARGET`, which the
  battery tolerates). `ai-cast`: a caster whose own spell is on the heal list lists only the spell (items only without a spell entry, 0x4CC580), so
  the NPC is tired to 40 % first (items first at or below half fatigue). `ai-follower`: a good follower complains instead of fighting when the hero
  attacks a good NPC (0x4ADCC0, the town's nearest), so the victim is the nearest NPC it is not upset by. `wreck` and `altar` print `NONE` where
  Virgil's home is not in the window. The `resources` sweep counts any ground body as planned: an item no holder lists lies on the ground under its own
  row key (ARC-DEV-085) with no T0 body in the window's plan (a limit: a wrongly dropped item now passes this sweep too). The hover scan of `HoverPointAsync` covers the body's own screen rectangle, not the whole
  viewport (a body that is not visible took over a minute).
- **The 13 unverified rows** need the executable reading the plan already names (B08, B17, B21, B44, B45), a stage the plan already describes (SM07 for B07, SM23 for B31, B32, B36 and B41, SM19 for B46), the reporter's save and hero (B36, B40) or the prototype read (B37). B33, B34 and B35 are Present, but whether the original behaves differently is also unverified and waits for the executable.
- **B02** is the one reproducible migration-relevant failure left in the object-model group; MIG6 is its planned slice, and the same seed refusal blocks the report's path of B01.

## Re-check after MIG9 (ARC-DEV-107, 2026-11-05)

Slice S7 of [residency](../architecture/residency.md#5-slices), on `main` 8b53a8dd (MIG9 S1–S6 merged, #631–#652). It re-reads B06, B10 and B25 only; the other rows above
stay as written. No source change; the bug-fix hold stays on. The plan's new stages SM05 `npc-resident` and SM30 `save-resume` still do not exist (a stage belongs to the PR
of the slice that fixes its bug), so B06 and B10 are classed by the existing stages, the unit tests and the current source; a reading is not a reproduction.

| ID | Before (04.11) | Now | Evidence |
| --- | --- | --- | --- |
| B06 | Present | **Changed** (partly: the forced fold step exists, the unforced pass does not; the plan's own stage is not built) | C: `AreaAiHost.RunWaypoints` and `RunStandpoints` run the route steps of `NpcRouteRules` (`tools/terrain-viewer/Core/AreaAiHost.cs`, `AreaAiHostRoutes.cs`), reached from the first tick that carries the fold's wake-up mark (S4b, ARC-DEV-117, WS1–WS12). R: `AreaAiRouteTests` (waypoint list walked one point per fold, standpoint, rebuilt host continues from the saved place, former follower sent home) and `AreaAiHostRebindTests` PASS (49 of 49 viewer tests of the filter); smoke `ai`, `ai-follower`, `transfer` (with `carry`, `visitor-talk`, `visitor-fight`, `visitor-body`) PASS on the town sector `--sector 1409,1314` with `human-male`. **Not covered:** the unforced waypoint and standpoint pass (WS6, WS7; the original's animation-goal handler calls it, WS11), so an NPC already in view is put on a waypoint only when its sector folds (`SchedulerRules.cs:93-101`, the first tick with the wake-up mark) and does not walk a patrol between folds; SM05 (6 guard and civilian cells moving at least 3 cells in 90 s of game time, far NPCs moving after a window hop, cells after a quick load) is a manual check; the bed, sleep and wander of the day routine stay not implemented. |
| B10 | Present | **Changed** (positions and the sector history are state; the run is not) | C: an NPC's last cell is its object state `npc.pos` (MIG7c, ARC-DEV-094) and the loaded-sector history is saved in schema 68 (S6, ARC-DEV-106), so a quick load and a window move no longer return NPCs to their T0 cells. R: `NpcPlacementStateTests` (a place survives a save and a load) and the S6 residency/history tests PASS (30 passed, 1 skipped, Session tests of the filter). **Still present:** goals, runs, animation and a maintained spell are not saved (MIG14, GR18); SM30's second half (the wolf continues its run after a load) cannot pass. A quick load by hand on a town save was not done in this task. |
| B25 | Present for NPCs that are not companions | **Present, unchanged** (needs its own slice) | R: `altar` PASS on `--sector 1452,1291` (Virgil, a companion, fires the altar tile script 1328 and opens his dialog 1324), as before. C: a tile script still runs only for a companion of the shown map: the only caller of `AreaMapTransfers.EnterByCritter` is `TerrainViewerCompanions.cs:249`; no AI-host mover, visitor, jump or teleport notifies a location (GR12). MIG9 S2–S6 added residency, fold, gating and saved history, not location notification. SM28's pass (the assassin's agent walks to the altar tile and the script fires) is not reachable. |
| B08 | Unverified | **Unverified** | The far branch and the heartbeat gating now exist (S5, RL16, RL17), but the reaction of an NPC to a spell hit from outside the near radius still needs the executable reading the plan names (SM06). |

Result: of the three, B06 and B10 move from Present to Changed (a partial fix each, named above); B25 stays Present. The counts of the table above (Present 31, Changed 1, Unverified 13)
become Present 29, Changed 3.

Follow-up this points to (not started): a slice that gives every relocation of an NPC the tile and sector scripts (GR12; location notification for AI-host movers, visitors, jumps and
teleports) with the assassin's agent as its stage (SM28 extended).

## Re-check after MIG12 (ARC-DEV-128, 2026-11-05)

Slice S9 of [clocks and the frame](../architecture/clocks-frame.md#5-slices), on `main` 68862062 (MIG12 S1–S8 merged, #647–#658). It re-reads the bugs the clocks and the pause could move
(B11–B15 of G3, and B06 and B10 whose runs now wait on the frame clocks); the other rows stay as written, and the MIG9 re-check above is unchanged. No source change; the bug-fix hold stays on.
The plan's [schedule](open-bugs-migration-smoke-plan.md#schedule-by-migration-step) names no bug stage after MIG12 (the pause part of SM10 waits for the door runs of MIG13), so the rows are classed by the
existing stages and the current source; a reading is not a reproduction.

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B12 | Present (cause) | **Present, the clock part of the cause is gone** | C: the effect is still not a run of one goal table (GR26, MIG13): the cast's eye candy and the missile are viewer hosts (`TerrainViewerSpellCast.cs`, `TerrainViewerSpellMissiles.cs`), and a missile follows a target that moved by a new leg, as before. What changed is the clock: the missile, the eye candy and the cast timing read `WorldMilliseconds()`, the frame clocks' world time (S6, ARC-DEV-125), so they stop with the Esc pause and are not moved by a jump (GR20). R: `cast` PASS, `frame clock` PASS; neither draws several enemies' effects, so the "first of several enemies only" symptom is neither reproduced nor excluded. |
| B13 | Present (cause) | **Present, the clock part of the cause is gone** (the dying clock is the frame clock) | C: the dying and lying animations and the body moves read the same world time (S6), so a pause no longer plays them on; a body is still drawn at the mover's pixel position and dying is still not a run (GR26, MIG13). R: the `dying` check of `map` ran (frames 5–10 drawn, 12 steps; it prints no PASS line); it does not compare the body's end cell with the pool's. |
| B15 | Present | **Present** | C: no door close timer exists in `src`, `tools` or the terrain viewer's portal host; the door animation stays on the host clock (§3.7 of the concept). The pause part of SM10 (the Esc menu stops the door's timer) cannot be checked until MIG13 gives the door a run on the animation list. |
| B11 | Present (cause) | **Present, unchanged** | C: the hero's actions are still viewer hosts beside the executor (MIG13); the hero's walk now stops with the pause (S6) and the hero's input is held behind every window and the menu (`WorldModal`, S7), which is not a hold effect on the hero. Not run (no hold-effect stage). |
| B14 | Present (cause) | **Present, the clock part of the cause is gone** | R: `throw`, `scatter`, `boomerang`, `projectile` PASS: flight and landing cells (`thrown-landing ... disposition=Changed`), not an explosion on landing. C: projectiles are viewer objects (GR4, GR26); their flight reads the frame clocks now (S6). |
| B06 | Changed (partly) | **Changed (partly), unchanged by MIG12** | C: the AI host's runs step on the frame's world milliseconds, not in a paused or frozen frame (S5); the unforced waypoint pass (WS6, WS7) that waits on the animation-goal handler is still MIG13's. R: `ai`, `ai-turn-based` PASS. |
| B10 | Changed | **Changed, unchanged by MIG12** | C: a run is still not saved (MIG14, GR18); the three clocks are saved (schema 70), so a load continues on the same clocks. SM30's second half stays out of reach. |

Evidence runs of this task (the owner's Mac, `./arcanum smoke pr --stages map,cast,worldmap,ai,ai-turn-based,turn-based`, hero `human-male` on `Arcanum1-024-fixed`): build, import, the
window run (the `map`, `cast`, `worldmap` and `turn-based` stages, with `dying`, `throw`, `scatter`, `boomerang`, `projectile` and `frame clock` inside the map stage) and the stages-alone `ai`/`ai-turn-based` run all PASS, `SMOKE PASS 4 of 4 steps` (the runner's own summary; the run logs are tickets 5219 and 5220), no FAIL line in either run log; `dying` has no PASS line of its own, its line is `smoke dying ... drawn=5,6,7,8,9,10 steps=12`. Native checks named in the
rows: `frame clock` (768 ms of world time over 48 frames of a walk; an Esc pause held over 120 frames (1920 ms) and the walk resumed over 43 frames), `worldmap` (43 steps, 43 hours, 2707 ms elapsed of which 2665 real, 30 steps behind the Esc pause), `dying`, `throw`, `cast`.

Result: no row changes status (the counts above stay as the MIG9 re-check left them: Present 29, Changed 3, Unverified 13, Gone 1). MIG12 removed the **time** part of the causes of B12, B13 and B14
(Godot's time, the pause that did not stop the consumers); what is left of each is the ownership of goals, missiles and projectiles (GR26, MIG13, MIG14). The coverage rows and the status
catalogue say what the clocks now do; the open work of B11–B15 starts with MIG13.
