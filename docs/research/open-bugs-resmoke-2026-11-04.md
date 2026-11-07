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
- **B02 still reproduces, identically** (superseded: gone since MIG6b, see the [re-check after MIG6 and MIG11 S5](#re-check-after-mig6-and-mig11-s5-arc-dev-155-2026-11-06) below). As written on 2026-11-04, the town window of `main` gives `inventory-seed map=1 status=InventoryRefused detail=UnlistedInventoryItem at 2345`, the
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
| B20 | Stay | Present | C: focus kind 18 is in no admission set; refused at `ScrAdmission.cs:412` (`FocusKindNotAdmitted`). After this table: ARC-DEV-157 (2026-11-06) read the log's "type 18" as the clause (loop for) over focus kind 8; the loop is admitted for tile and sector scripts, but script 2768 then stops at cast spell on (61, line 8): open until MT21. |
| B21 | Stay | Unverified | No code, test or doc names script 1643; the condition at its line 2 was not read. Probably the original's behaviour, as the plan says. Needs the executable reading. |
| B22 | Stay | Present | C: action 113 (unfog town map) is in no admission set and not in the catalogue. |
| B23 | Stay | Present (identity unverified) | C: action 33 is not admitted (`ScrAdmission.cs`), so tile script 30286 refuses at its line 2; that the tile is the trash can's is not established. After this table: ARC-DEV-157 (2026-11-06) read action 33 as get stat (not give gold) and admits it (S22), answered for the PC (script 30286 asks alignment, stat 19); smoke SM16 is still to run. |
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

## Re-check after MIG13 S6 (ARC-DEV-137, 2026-11-06)

| Bug | Status | After S6 |
| --- | --- | --- |
| B13 | Present (cause), reclassified | Dying is a run now (goal 31, `AiExecutorRules` and the session's table): the NPC's dying animation steps on the world clock and the blood pool waits for the run to end, so the clock and the run agree (the `dying` check of `map` passed: frames 5–10 drawn, 12 steps). The body's end cell against the pool's cell was not compared again; the status stays Present until that comparison is made. |

## Re-check after MIG13 S7 (ARC-DEV-138, 2026-11-06)

| Bug | Status | After S7 |
| --- | --- | --- |
| B15 | Present → **Gone** (reproduction not seen) | A door's change is its own goal run on the animation list (goals 70, 72, 71 of the session's table): after its last open frame the door waits 1.5 s, tests the doorway (no close in a round or with a living critter on its tile) and closes by itself 1.5 s later unless the portal's flag 0x100 keeps it open. Smoke SM10 as the new stage `door-close` (hero `human-male`, `Arcanum1-024-fixed`, `--sector 1409,1314`, `--stages door-close`) passed: `sector:88181048705:186` opened over 4 frames at a 125 ms pace, stood open a second later, stood still behind five seconds of Esc menu (the next frame credited one frame), and closed 3,400 ms after its last open frame with the pause excluded (the original's two waits of 1.5 s plus four close steps). The portal stage of `map` (a step through a door, the press that closes it, the refused door) passed on the same runs. |

## Re-check after MIG13 S8a (ARC-DEV-139, 2026-11-06)

| Bug | Status | After S8a |
| --- | --- | --- |
| B12 | Present (cause) → **Present (one half open)** | The spell missile is a goal run (28 with 78 per leg) on a created object that no save keeps, so its follow of a moved target is the node's retarget at each leg's end: it flies to the tile the target stands on then (`AreaMissileHostTests`, a moved target; the cast smoke passed with one leg of the planned steps). The other symptom, a spell effect drawn on the first of several targets only, is not tested: the cast smoke casts at one target and the several-target cast has no missile of its own in the viewer. The cast's eye candy is still a viewer host (S9). |
| B14 | Present (cause) → **Present, reclassified** | The S8 read (GT63) answers the open question: the original explodes a thrown grenade, through the item's own spell ("attacks a location", event 0x800000, fired by the projectile's crossing 0x4B2870 at the strike, the destination or the range end), so the missing explosion is a gap of Steamwoven, not a bug of the game. It is ARC-DEV-140 (S8b, the projectile 36), which this slice did not change. |

Smokes of this slice on the owner's Mac, hero `human-male`, `Arcanum1-024-fixed`: `cast` (with the missile of a missile spell: one leg of the planned nine steps, arrived, drawn faced), `ai`, `ranged`, `turn-based` PASS. The cast smoke now drives the animation clock itself (a native smoke does not tick it): the missile loops call `SpellSmokeTick`.

## Re-check after MIG13 S8b (ARC-DEV-140, 2026-11-06)

| Bug | Status | After S8b |
| --- | --- | --- |
| B14 | Present, reclassified → **Fixed in the source (hero's throws); not seen with a real grenade** | A thrown grenade explodes where the throw ends: the projectile is a goal 36 run on a created `projectile:n` object, and the end of a strike, a range end or a destination end fires the item's attacks-a-location event (0x4CBDB0, `QueryShotEndItemEvents`) through the cast host, whose item lookup now knows the taken item the hero threw (lying on the map, itself its holder, 0x450A50), so the item's own spell (trigger 0x800000) starts as a run at that tile. The goal's own end (a point off the map, a throw at the thrower's own tile) fires none, as in the original (GT53, GT63). The state tests start the grenade's spell with a map-item grenade (`AttackItemTriggerStateTests`, `CombatItemTriggerStateTests`); the new native smoke part `shot-end-item-event` (inside `ranged`) lends the thrown dagger an installed spell with the 0x800000 trigger (spell 148) and requires the event at the landing tile for every throw that ends on a crossing, the run started for the lent dagger (`started=1`). **Not covered:** an NPC's grenade (the AI host has no NPC flight yet, `Grenade` answers `NoGrenade`) and a real grenade of the installed data thrown from the pack (the smoke has no grenade item; the lent spell is a real record). |

Smokes of this slice on the owner's Mac, hero `human-male`, `Arcanum1-024-fixed`: `ranged` (the bow shot, the throws and scatter, the boomerang with its turn and return, the cursor throws, the shot-end item event), `cast`, `ai`, `turn-based` PASS. The ranged smoke now drives the animation clock itself (a native smoke does not tick it): its flight loops call `SpellSmokeTick`.

## Re-check after MIG6 and MIG11 S5 (ARC-DEV-155, 2026-11-06)

Verification only: no source change, no fix; the bug-fix hold of the [architecture audit](architecture-audit-plan.md) is the coordinator's to lift. The first table above was taken before MIG6 (#613-#621, 2026-11-04) and MIG11 S5 (#646), so the five rows it classed
as waiting on them are read again on `main` c7a5e805 (#678). No stage was added (a stage belongs to the PR of the slice that fixes its bug); B01, B04 and B13 are therefore classed by the existing stages, the
unit tests and the current source, and a reading is not a reproduction.

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B02 | Present, reproduced | **Gone** | R: the town window of the sector run (`--sector 1409,1314`, `human-male`) seeds `inventory-seed map=1 status=Ready detail=342 items, 98 holders, 2 dead NPCs, ..., 1 held but unlisted (detached)` where it gave `InventoryRefused ... UnlistedInventoryItem at 2345` (ticket 5704); the stages `map`, `transfer` (with its `carry` check), `roundtrip` and `follower-home` PASS on it. A second run from the Crash Site (`map`, `stream`, `walk`, `worldmap`) moves the window through the town and seeds every window `Ready` (the moved windows detach 3, 15, 7, 7 and 7 held items; ticket 5715). Neither run has a `InventoryRefused` or `UnlistedInventoryItem` line (0 of 2071 and 0 of 22172 log lines). C: `UnlistedInventoryItem` is only a member of the enum (`src/Arcanum.Data/Inventory/AreaInventory.cs:41`); no code returns it. MIG6b (ARC-DEV-085, #614) turned the refusal into a detached item that the seed leaves out of `Items`. Unit: `AreaInventoryTests` 8 of 8 PASS. **Limit:** the stages do not check that a detached item (15 in one window) is missing from what the player sees; the report's naked hero is not replayed. |
| B01 | Present (cause), not reproduced | **Unverified** (the blocker is gone; the cause is partly addressed) | C: the report's path was behind B02, which no longer stops the town window, so a run can reach it now. Created items still keep a per-kind number: `made:n`, one create path since MIG6c (`CreateMadeItem`) and the hero's lists with an entity index since MIG6d-2 (`GameSessionHeroLists.cs`), but not one id space (GR3 stays). A taken item is `oid:`-keyed (the `junk` run takes an `oid:` item), and the viewer looks its row up in every attached map by that key (`AreaInventoryHost.Wearable`, `GridMoveItem`, `tools/terrain-viewer/Core/AreaInventoryHost.cs:364-395`); a made item has no row there ("prototype facts not read: NOT READY"). R: no stage follows an item through a window move (SM02 `inv-carry` does not exist). Unit: the 47 Session tests of the filter (`CreatedItemsState`, `PlacedItemsState`, `ObjectLifecycle`, `ObjectDirectory`, `JunkPileState`, `TeleportMapState`) PASS. What decides it: SM02 (take the passport and the matches, wear the boots, window move, quick save and load, the multiset of (prototype, count, place) unchanged). |
| B04 | Present (cause), not reproduced | **Unverified** (probably the original's rule, not a defect) | C: MIG6c (ARC-DEV-086, #616) read the report's log: Take all with the loot window open leaves the pile (the original's JP5), and the close of the window dissolves it (`CloseJunkPile`, JP4/JP5; `docs/world/inventory-runtime.md:389`, called from `TerrainViewerAreaInventory.cs:528`). R: `junk` PASS on `elven ruins` with `elf-female` (the chain's pairing; ticket 5711): a broken container spilled 3 items into a pile `junk:1`, one taken, closed with 2 left and the pile kept, as the rule says. The stage does not Take all, so the report's path is not run end to end; the Crash Site was not run (the stage was `NONE` there on 2026-11-04: no container with two items). Unit: `JunkPileState`, `ObjectLifecycle`, `ObjectDirectory` PASS (47 of 47 with the above) and `JunkPileRulesTests` PASS (27 of 27 with `ChangeOfPlaceRulesTests`). What decides it: the extension of `junk` that SM04 names (empty the pile by Take all, leave and return). |
| B09 | Present | **Gone** | R: `follower-home` PASS (ticket 5704): the companion carried to map 18 and left as a former follower is sent home by the route step: `ARC-DEV-116 homecoming ... to map 1 cell 90229,84175`, route `standpoint RequestTeleport to map 1 (90229,84175): queued`. C: MIG11 S5 (ARC-DEV-116, #646) added the teleport map `npc.teleport` (schema 69, `GameSessionTeleportMap.cs`), the former follower's request in `AreaAiHostRoutes.cs`, and the change of place the request runs through. Unit: `TeleportMapState` and `ChangeOfPlaceRules` PASS (see above). **Limit:** one fixture (a companion in the sector fold's forced step); a companion that stays on the hero's map after being dismissed, and the original's unforced pass, are outside B09 as the register wrote it (GR15). |
| B13 | Present (cause) | **Unverified** (the plan's causes are addressed; the report's case is not run) | C: dying is a run (goal 31, MIG13 S6), a fallen body is drawn where its mover stands (`AiMoverCommands`, `tools/terrain-viewer/Godot/TerrainViewerAi.cs:521-530`, ARC-WORLD-111), and the pool takes the session cell. R: the `dying` check of `map` ran in both runs above and passed its rules (frames 5-9 in 7 steps, ticket 5704; frames 5-10 in 12 steps, ticket 5715); `blood-pool` put the pool on the body's session cell (`pool.Cell == facts.Cell`, the session cell of both, not the drawn cell). Not run: a running wolf killed by a spell (the report) and a comparison of the drawn cell with the pool's; SM11 `critter-fall` does not exist. What decides it: SM11. |

Smokes of this task on the owner's Mac (`human-male`, `Arcanum1-024-fixed`, `elf-female` on `elven ruins`): `./arcanum smoke pr --stages map,transfer,roundtrip,follower-home --sector 1409,1314` PASS (3 of 3 steps, ticket 5704);
`./arcanum smoke pr --stages junk --map "elven ruins" --hero elf-female` PASS (ticket 5711); `./arcanum smoke pr --stages map,stream,worldmap,walk,dead-window` ran its window to the end (exit 0, 698 s, ticket 5715): `map`, `stream`, `walk` and `worldmap` PASS, and the runner
reported the step `FAIL exit=3` only because `dead-window` printed `NONE` ("the window moved 3 times: run the stage on its own", the stage's own rule for a stage list that moves the window; none of the five rows needs it). Unit tests of the rows (the owner's pinned SDK through the queue): Session (47), Data `AreaInventoryTests` (8) and Simulation (27) PASS.
Each of the three window runs ends with 3 `ERROR:` lines, all Godot shutdown leak notes (a texture leaked, `RenderingServer` null in `~ImageTexture`, an RID leaked).

Result: B02 and B09 leave Present for **Gone** (reproduction not seen, the code that produced the refusal or lacked the field is replaced and a stage passes); B01, B04 and B13 leave Present (cause) for **Unverified**, each with the stage that would decide it named (SM02, an extended `junk`, SM11).
The table's counts are not recomputed here (the re-checks after MIG9, MIG12 and MIG13 each moved rows, and B14 and B12 are worded as halves); the coordinator's triage of 2026-11-06 is the current list.

## Re-check after ARC-DEV-156 (2026-11-06)

| Bug | Status | After ARC-DEV-156 |
| --- | --- | --- |
| B27 | Present → **Fixed** | The message window's texts and a pane's hover name are drawn in the original's font arts (27 title, 229 text; ledger ML6, HK21) through `AreaArtTextDraw` with the MW31 blend, in the entry's colour; they are broken, cut and right-aligned as the text draw 0x550A10 does (ML4), and the floating tooltip (without the bars) uses font 229. Without a game folder (a self-test) the viewer's font stays. The line height is the font's tallest glyph (PROJECT), at least one line is always drawn. Both rule modes draw the same text. |
| B28 | Present → **Fixed** | The floaters are drawn in font art 229 in the floater's colour with the fade as the blit's opacity (CH10, CH11); their line height is the font's tallest glyph + 2 (PROJECT: the height getter 0x4D4E20 reads was not read). |

Smokes of this slice on the owner's Mac, hero `human-male`, `Arcanum1-024-fixed`: `floaters` (alone; the log shows the fonts read, title height 19, text height 15) PASS, `hover` PASS, `bar` (from the main menu, with the message log smoke, whose long text now uses wide capitals because the narrower font art fits the old one in the wide layout) PASS. Open: the floaters layer takes its font when it is created, so a module change in a running viewer keeps the first module's font.

## Fix: B40 and B37 (ARC-DEV-161, 2026-11-06)

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B40 | Unverified | **Fixed** (both modes) | R: the Crash Site chests are source indices 4453 (prototype 3055, inventory source 69) and 6536 (prototype 3067, source 56). Set 56 read; set 69 (`M1 Treasure Set: -100,250 ...`) was `Malformed` in `InventorySourceRules.Read` (its coin minimum is negative and one item pair ends in a comma), so `PlanRestock` returned `Unsupported` and the chest kept its empty list (`generation=NoChange/Unsupported`), for every hero (the report's "custom hero" was the hero it happened with, not the cause). C: the loot line is parsed by `strtok` and `atoi` in the executable (0x4BF7B0, ledger BT9: coins and rates are not checked, negatives kept), so the original reads all of these sets whole; the reader's "plain digit run" admission was the project's own. The coin draw already handles a negative minimum (0x4CEF00, `Math.Max(coins, 0)`, RK6). Fix: the pair parse is the loot line's `strtok` walk; a negative coin minimum, a space after a comma and a trailing comma on the right side of a pair read; what `atoi` reads only in part stays `Malformed`. Census (`Original_restock_census`, opt-in): 122 of the 123 installed sets read (was 84); set 94 is the one left. |
| B37 | Unverified | **Not reproduced** (the data gives the box 200 hit points) | R: the Control Box is one scenery on `shrouded hills-panarii temple lev 1` (description 30605 in `gamedesc.mes`, Static source index 89, prototype 4032), 200 hit points, no damage, metal, resistances 0, no invulnerable flag, a destroyed art and one script point, 30 (bust). The viewer's attack targets read it as a scenery baseline of 200 hit points (`AreaControlBoxRealDataTests`, opt-in). Nothing in the session lacks the hit points; what the reporter saw (no log) is not known, so the row stays open until a run with a log shows it. |

Checks of the row: `AreaRestockCensusTests.Original_restock_census` (the set census, and the chest sets 56, 69, 70 and 71) also had its map counts out of date before this task (the 2026-10-28 numbers: 423 containers, 136 with a source, 1985 NPCs, 2172 items, 39 persistent, four merchants; now 434, 139, 2120, 2362, 42 and eight, four of them on the `qintarra` maps, which load now); they are updated here. A kit set with a negative coin minimum gives the hero 0 gold (`PremadeHero`: only a positive count gives coins, RK6). The 38 sets that now read also serve the NPCs with a source of their own (238), which regenerate from them (ARC-WORLD-103): their inventories change in a restock or redress run.

## Fix: B34 and B35 (ARC-DEV-168, 2026-11-07)

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B34 | Present (original unverified) | **Fixed** (both modes) | R: read in the retail 1.0.7.4 executable (SHA-256 checked; ledger SL9, SL11). F7 posts a note before the save (`MainMenu.mes` 5060) and one after it (5062), both type 6 entries of the message window (shown only when nothing was stored for 3 s, never stored); the save window's save posts a stored type 4 entry, 5002 on success and 5003 on failure. The viewer now posts the same; the texts are the player's `MainMenu.mes` (the project's own wording when it does not read). The original's F7 ignores the save's result and says "completed" after a failed save: a technical display bug, so the viewer posts the failure result (5003) there in both modes (no Original switch). Not modelled, as before: the dead-PC and other-turn refusals of F7 (SL1). |
| B35 | Present (original unverified) | **Fixed** (both modes) | R: SL10. Every load (F8, the load window, the main menu's last save) posts a stored type 4 entry, 5000 when it read and 5001 when the version or the file was refused; F8 also posts the notes 5061 before and 5063 after (the second is held back by the 3 s rule after a stored result). With no save listed F8 posts the two notes and no result. The viewer posts them at the moment the session is replaced, or, for a save on another map, when that map is published (5001 when it did not load); a load the viewer refuses before reading (a reset, or a map load in progress) posts nothing. The death reload is not one of the original's callers of the load function and posts none. |

Checks: `AreaSaveMessagesTests` (numbers, entry types, the project's wording, the 3 s rule against a result). The Crash Site smoke's quick save stage (ARC-WORLD-011, in `--crash-site`) now checks the completion note after F7 (not stored), the load result as the window's one stored entry after F8 from the cave, and the failure result after F8 on a damaged slot. This replaces the planned stage `save-load-msg` (SM14) for the quick keys; the save window's own message is covered by the unit tests only.

## Fix and re-check by ARC-DEV-170 (2026-11-07)

B05 and B06, on `main` 4d7350b5 plus this change; hero `human-male`, `Arcanum1-024-fixed`, the owner's Mac.

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B05 | Changed (the carried half) | **Gone** (no remaining defect found; a regression stage added) | R: the new stage `follower-far` (SM01, `./arcanum smoke pr --stages follower-far`, `--debug-mode`) PASS: Virgil joins, the hero's window moves to the farthest area of the main map (area 33) through the debug travel's Teleport with the session carried, he is in the party, in the shown map's group before any placement, drawn and 1 cell from the hero after 100 frames; F7, F8 (the report's quick load there) and the same again at the same cell. C: the refusal of the report (`visitor not talkable ... no body kept this run`) is gone since MIG4 and the town window holds a follower the hero brought (MIG5, MIG9, DEV-153's stale hosts). **Limit (unchanged):** a save made before MIG4 carries no `npc.home` for its followers (the report's own saves), which MIG4 documents as outside what it repairs. |
| B06 | Changed (partly) | **Fixed** (patrol walks start; the bed, sleep and wander of the day routine stay NOT READY) | Cause found: the non-combat perform of a leaderless NPC called a routine that did nothing (`AreaAiHost.RunRoutine` was empty), so the unforced waypoint and standpoint steps never ran; the fold's forced step only put an NPC on a waypoint. Fix: the unforced steps (ledger WS14, read in the executable by one Opus reader) start goal 7 to the waypoint (the index moves on within 3 tiles of it) or the standpoint (hour 6 and 21 only on a 1 in 1000 roll); a walk already running is retargeted. R: `npc-resident` (SM05, `--sector 1409,1314`) PASS: of 6 route NPCs near the hero 2 started walks and moved 3 or more cells in 90 s of game time, and all 6 stand on their saved cell after F7/F8; unit tests `NpcRouteRulesTests` (unforced steps), `AreaAiRouteTests` (the walk to the waypoint and on to the next, a second pass changes nothing, the standpoint walk, another teleport map starts nothing) PASS. **Not covered:** the window hops of SM05 (far NPCs), the fidget and step apart after both steps answered 0, the bark. |
