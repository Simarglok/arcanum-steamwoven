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
| B06 | Change (MIG7, MIG9) | Present | C: `AreaAiHost.RunWaypoints` returns `false` and `RunStandpoints` is empty, `src/Arcanum.World/AreaAiHost.cs:651-652`; the class header lists "routines and waypoints, saved NPC positions" as NOT READY (`:173-176`). |
| B07 | Change | **No defect found** (ARC-DEV-308, [re-check](#re-check-of-b07-by-the-stage-placement-census-arc-dev-308-sm07-2026-11-08)) | The reports' build predates the residency and saved NPC positions; on `main` the stage `placement-census` finds every row near the hero held on its row's cell, and the data put the three Lesser Boars together. |
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
| B19 | Stay | Present | C: `get area` on the start map returns `NotModelled` (`AreaUseScripts.cs:285`, `AreaConversation.cs:368,427`); action 29 (get reaction) is in no admission set (`ScrAdmission.cs`).  After this table: ARC-DEV-178 (2026-11-07) models get area on the start map (GA3, GA4) and admits get reaction in the dialog host (set S23, GR1, GR2); the real-data census admits more talk scripts; the town NPC talk itself still needs the smoke SM16. |
| B20 | Stay | Present | C: focus kind 18 is in no admission set; refused at `ScrAdmission.cs:412` (`FocusKindNotAdmitted`). After this table: ARC-DEV-157 (2026-11-06) read the log's "type 18" as the clause (loop for) over focus kind 8; the loop is admitted for tile and sector scripts, but script 2768 then stops at cast spell on (61, line 8): open until MT21. |
| B21 | Stay | Unverified | No code, test or doc names script 1643; the condition at its line 2 was not read. Probably the original's behaviour, as the plan says. Needs the executable reading. **Update 2026-11-08 (ARC-DEV-309):** read; the refusal is the original's flag gate and its printed line now reaches the message window ([ledger PW1–PW5](ledger/scripts.md#script-print-line-in-the-message-window-and-the-wards-gate-of-script-1643-arc-dev-309-b21)). |
| B22 | Stay | Present | C: action 113 (unfog town map) is in no admission set and not in the catalogue.  After this table: ARC-DEV-205 (2026-11-07) admits action 113 in the viewer's use, tile and sector sets (set S24, UF1–UF4) and keeps the town maps revealed whole in the session (schema 71); the town-map window and its per-cell fog are not modelled. The sector entry itself still needs the smoke SM16. |
| B23 | Stay | Present (identity unverified) | C: action 33 is not admitted (`ScrAdmission.cs`), so tile script 30286 refuses at its line 2; that the tile is the trash can's is not established. After this table: ARC-DEV-157 (2026-11-06) read action 33 as get stat (not give gold) and admits it (S22), answered for the PC (script 30286 asks alignment, stat 19); smoke SM16 is still to run. |
| B24 | Stay | Present | C: the Summon component is "not ready", `src/Arcanum.Session/SessionSpellCaster.cs:313`, `src/Arcanum.World/AreaSpellCasting.cs:86`; a test asserts the line. |
| B25 | Change (MIG9) | Present for NPCs that are not companions | R: `altar` PASS on `--sector 1452,1291`, as SM28 of the plan says (Virgil fires the tile script; on the default sector the stage fails). C: tile scripts fire only for a companion of the shown map (`AreaMapTransfers.cs:96-108`); the assassin's agent has no caller. |
| B26 | Stay | Present | C: no `XP` operation in the dialog grammar (`DialogConversationGrammar.cs`, `DialogOperationParser.cs`); `experience-runtime.md` lists it as not wired.  After this table: ARC-DEV-178 (2026-11-07) wires `xp n` (the quest experience of level n, DAC9) in the viewer's dialog host with a session hero. |

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
| B06 | Present | **Changed** (partly: the forced fold step exists, the unforced pass does not; the plan's own stage is not built) | C: `AreaAiHost.RunWaypoints` and `RunStandpoints` run the route steps of `NpcRouteRules` (`src/Arcanum.World/AreaAiHost.cs`, `AreaAiHostRoutes.cs`), reached from the first tick that carries the fold's wake-up mark (S4b, ARC-DEV-117, WS1–WS12). R: `AreaAiRouteTests` (waypoint list walked one point per fold, standpoint, rebuilt host continues from the saved place, former follower sent home) and `AreaAiHostRebindTests` PASS (49 of 49 viewer tests of the filter); smoke `ai`, `ai-follower`, `transfer` (with `carry`, `visitor-talk`, `visitor-fight`, `visitor-body`) PASS on the town sector `--sector 1409,1314` with `human-male`. **Not covered:** the unforced waypoint and standpoint pass (WS6, WS7; the original's animation-goal handler calls it, WS11), so an NPC already in view is put on a waypoint only when its sector folds (`SchedulerRules.cs:93-101`, the first tick with the wake-up mark) and does not walk a patrol between folds; SM05 (6 guard and civilian cells moving at least 3 cells in 90 s of game time, far NPCs moving after a window hop, cells after a quick load) is a manual check; the bed, sleep and wander of the day routine stay not implemented. |
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
| B01 | Present (cause), not reproduced | **Unverified** (the blocker is gone; the cause is partly addressed) | C: the report's path was behind B02, which no longer stops the town window, so a run can reach it now. Created items still keep a per-kind number: `made:n`, one create path since MIG6c (`CreateMadeItem`) and the hero's lists with an entity index since MIG6d-2 (`GameSessionHeroLists.cs`), but not one id space (GR3 stays). A taken item is `oid:`-keyed (the `junk` run takes an `oid:` item), and the viewer looks its row up in every attached map by that key (`AreaInventoryHost.Wearable`, `GridMoveItem`, `src/Arcanum.World/AreaInventoryHost.cs:364-395`); a made item has no row there ("prototype facts not read: NOT READY"). R: no stage follows an item through a window move (SM02 `inv-carry` does not exist). Unit: the 47 Session tests of the filter (`CreatedItemsState`, `PlacedItemsState`, `ObjectLifecycle`, `ObjectDirectory`, `JunkPileState`, `TeleportMapState`) PASS. What decides it: SM02 (take the passport and the matches, wear the boots, window move, quick save and load, the multiset of (prototype, count, place) unchanged). |
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

## Fix: B17 (ARC-DEV-171, 2026-11-07)

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B17 | Unverified | **Fixed** (both modes); not the hero's intelligence | R: the installed DLG 1696 (read in original mode) offers every hero a reply at every node: node 1 has reply 2 (IQ 5) and reply 3 (IQ −4), both to node 5, whose replies 6 and 7 (IQ 5) and 8 and 9 (IQ −4) hold the conditions `ru 2015` and `ru -2015`. C: the reply gate by intelligence is the one our runtime already had (executable 0x414F50, ledger IQ1: a non-negative IQ needs at least IQ, a negative one at most −IQ, dumb counts 1), so a low-intelligence hero was not hidden by it. What stopped the dialog was the condition `ru` (executable 0x4150D0 case 17 → 0x4C58D0, ledger RU1) on replies 6 to 9: the conversation runtime admitted `ru` and `rq` only as actions, so node 5's reply group was a frontier and the reply to node 1 was shown blocked for every hero (checked on the old code: a `TargetUnsupported` block on the condition of record 6; the log's `ClosedByHost turns=1` is the player's own Leave, so the talk could not go past its first turn). The conversation now admits `ru n` (the PC knows rumor n; `ru -n`: it does not) and `rq n` (rumor n is quelled; `rq -n`: it is not) behind the rumor port, which the AREA viewer fills from the session (ledger RU2). |

Checks: `DialogRumorConditionTests` (a synthetic dialog shaped like 1696 with a clever, a dull and a dumb hero, rumor known and unknown; failing before the change), `DialogRumorConditionRealDataTests` (opt-in `DAT_ROOT`: the installed 1696 for the same heroes, and the 223 installed `ru`/`rq` Player condition fields: 20 stop at a rumor operation, all damaged data, ledger RU4). The Crash Site stage `wreck` (named only) starts the conversation as before. Not changed: the other condition codes the installed dialogs hold that the subset leaves out (`ch`, `in`, `ia`, `ni`, `qa`, `qb`, `le`, `lc`, `pa`, `na`, `ma`, `pe`, `ar`, `al`, `fo`, `$$` and others); a dialog that holds one still stops at it.

## Fix: B29 (ARC-UI-071, 2026-11-07)

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B29 | Present | **Fixed** (both modes) | C: the conversation panel (`AreaConversationPanel`) was Godot `Label`s and `Button`s in the viewer's font. Now, once the viewer read the original's text fonts (ARC-DEV-156), every text of the panel (the header, the NPC line, each reply, the outcome, the effects ledger, the hint and the Leave button) is drawn in font art 229 with the original's blend (`AreaArtParagraph`: `AreaTextFontLayout.Place` breaks the text at the panel's content width as the text draw 0x550A10 does, `AreaArtTextDraw`, MW31), in the colours the labels had. A reply button holds its art text and takes its height. Without a game folder (the self-test) the labels and buttons stay. PROJECT choice: font 229 is the font of the original's dialog box (DB1) and of its message window texts (ML6); the font of the original's own talk window (the dialog UI 0x567E30 family) was not read, so the choice stays open (it is a diagnostic surface, "original runtime parity NOT_VERIFIED"). |

Checks: the viewer self-test (`ARC-UI-071 self-test PASS conversation panel in the original's font art`, synthetic glyphs: art text at the content width, buttons sized by their text, a reply in art mode, the labels back when the fonts go) and the `wreck` smoke, which now requires the panel to draw in the fonts exactly when they read and every shown text to have lines (`ARC-UI-071 conversation panel art text ...`), hero `human-male`, `Arcanum1-024-fixed`. A one-off screenshot of the smoke's panel showed the header, line, replies, Leave and hint in the original's font. The text is broken as the original does (MW26): a part with no space or hyphen before the width ends its draw, where the old labels broke inside a long word; the panel is at least 700 px wide and its texts have spaces, so this is not seen. The panel and its glyphs sit on whole pixels. The smoke does not read back text pixels against the `AreaArtTextRules` reference (the planned `dialog-art` stage, SM15, would).

## Fix and re-check by ARC-DEV-170 (2026-11-07)

B05 and B06, on `main` 4d7350b5 plus this change; hero `human-male`, `Arcanum1-024-fixed`, the owner's Mac.

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B05 | Changed (the carried half) | **Gone** (no remaining defect found; a regression stage added) | R: the new stage `follower-far` (SM01, `./arcanum smoke pr --stages follower-far`, `--debug-mode`) PASS: Virgil joins, the hero's window moves to the farthest area of the main map (area 33) through the debug travel's Teleport with the session carried, he is in the party, in the shown map's group before any placement, drawn and 1 cell from the hero after 100 frames; F7, F8 (the report's quick load there) and the same again at the same cell. C: the refusal of the report (`visitor not talkable ... no body kept this run`) is gone since MIG4 and the town window holds a follower the hero brought (MIG5, MIG9, DEV-153's stale hosts). **Limit (unchanged):** a save made before MIG4 carries no `npc.home` for its followers (the report's own saves), which MIG4 documents as outside what it repairs. |
| B06 | Changed (partly) | **Fixed** (patrol walks start; the bed, sleep and wander of the day routine stay NOT READY) | Cause found: the non-combat perform of a leaderless NPC called a routine that did nothing (`AreaAiHost.RunRoutine` was empty), so the unforced waypoint and standpoint steps never ran; the fold's forced step only put an NPC on a waypoint. Fix: the unforced steps (ledger WS14, read in the executable by one Opus reader) start goal 7 to the waypoint (the index moves on within 3 tiles of it) or the standpoint (hour 6 and 21 only on a 1 in 1000 roll); a walk already running is retargeted. R: `npc-resident` (SM05, `--sector 1409,1314`) PASS: of 6 route NPCs near the hero 2 started walks and moved 3 or more cells in 90 s of game time, and all 6 stand on their saved cell after F7/F8; unit tests `NpcRouteRulesTests` (unforced steps), `AreaAiRouteTests` (the walk to the waypoint and on to the next, a second pass changes nothing, the standpoint walk, another teleport map starts nothing) PASS. **Not covered:** the window hops of SM05 (far NPCs), the fidget and step apart after both steps answered 0, the bark. |

## Fix and re-check by ARC-DEV-203 (2026-11-07)

B44, B45 and B46, on `main` 5ae94f10 plus this change; hero `human-male`, `Arcanum1-024-fixed`, the owner's Mac.

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B45 | Unverified | **Fixed** (both modes; towards +X/+Y seen in the stage, towards −X/−Y by the unit test) | R: the new stage `indoor-fade` (SM18, `--sector 1409,1314`) took the first open window step reachable from the spawn that leaves an indoor tile for an outdoor one (towards +X, (90246,84152) to (90247,84152)) and sampled the hero's placement per 0.1 s frame: before the change the placement kept the old tile while the session had committed the far cell (frame 8 `placement (134, 120) is the committed cell (135, 120)`); now it is the committed cell in all 14 frames and the outdoor shade starts in the frame that commits it (8). C: `ToPlacement` took the tile as `floor` of the anchor in cell units, where a cell's centre is an integer, so the tile changed at the end of a step towards +X/+Y (the whole 1,125 ms vault) and at its start towards −X/−Y. The original changes tile half way (ledger HL1, `location_normalize` 0x4B9420). The placement also orders the hero among the bodies, so he now passes a wall or a body half way along a step too. Not changed: the original moves a vault's tile at its action frame (ledger HL2, F0), the session commits at half the vault. No window towards −X/−Y was found from the spawn, so that direction is covered by the unit test only. |
| B46 | Unverified | **Gone** (no reproduction on `main`; a regression stage added) | R: the new stage `fire-order` (SM19, `--sector 1410,1315`) reads the viewport back with and without each of up to 4 animated scenery nearest the hero (roofs hidden) on four frames of its loop: the scenery changes 1,048 to 7,990 pixels inside its sprite's rectangle every time, so no sprite drawn after it covers all of it (a sprite covering only part of one is not detected). Screenshots of the town's brick fireplaces (the burning ones and the torch-lit rooms) show the fire over the brick. C: the draw order of one tile and of neighbours follows the executable since ARC-DEV-173 (ledger SO1 to SO3, merged after the report), which is the likely fix; the cause was not isolated before it. |
| B44 | Unverified | **Original behaviour, kept** (owner, 2026-11-07: in the original the hero can also walk round the end of the corridor's north wall, which takes some effort) | R: a flood fill of the `crash site cave` map from its spawn (2087,2018) reaches 506 cells, as the earlier measurement did, but they are not all inside the walls: a ring one cell wide outside the wall lines is reachable too, through the corridor's east end (2095,2018), which no wall closes, round the end of the corridor's north wall. C: the rules the passability is built from equal the executable's (ledger PB1: tile mask, facade bit, tile-name flags; PB2: a wall blocks one edge by its art rotation rounded up to odd, the pass-wall flag skips it), and the ring's tiles are open in the original's own data, so the viewer does not differ from the original there. The data did not decide whether the original lets the hero out; the owner checked it in the original game and confirmed it does (2026-11-07), so no change was made. |

Checks: `AreaGridSessionTests.TheHeroPlacementTileIsTheCommittedCellAtEveryInstantOfAStepInBothDirections` (fails before the change); the stages `indoor-fade` (fails before the change) and `fire-order`. SM18 and SM19 of the [smoke plan](open-bugs-migration-smoke-plan.md) are built; its SM17 stays planned.

## Fix: B16 (ARC-DEV-204, 2026-11-07)

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B16 | Present | **Fixed** (both modes; movies NOT READY) | R: read in the retail 1.0.7.4 executable ([CP17](../architecture/change-of-place.md#the-b16-read-of-the-executable)): the "clock animation" is `loadindicator.art` (interface art 601, 30 frames), drawn in the top-right corner, two pixels in, one frame per loading step (tick 0x4BBC30, called from the sector loader 0x4D1A30 and the function at 0x4D2D75); the black is the closed old map (0x4106D0) and, for a request with flag 0x20, the blank (0x402FC0). The jump point and a script's plain teleport request flags 0 (no fade, sound or movie), so the owner's screenshot is the map load alone. The viewer now shows black with the clock while another map loads (a jump point, a use, a scripted teleport, the world-map landing), from the closing of the old map to the arrival; a script's fade-and-teleport (flags 0x6) holds the change for a 2 s fade-out (the original's fade blocks; decision PD9 for the length), plays its sound on the hero at its departure point (SN29) and fades in for 2 s after the change; the blank flag blacks the landing at once. **Not covered:** the movies (Bink; the step is met and reported on the log), the new game's own fade-out and two movies (flag 0x45), the 3 × 3 pre-cache, the Crash Site cave swap (both scenes are loaded, no load to show). |

Checks: `AreaTransitionScreenTests` (13: the fades, the blank flag, the load screen and its clock, bad time steps). The map smoke stage `transfer` (SM13 in the plan, the stage `loading-screen` is not added) now requires the load screen to have shown on the transfer to another map and to be gone with the map there (`ARC-DEV-204 load screen loads=1`), prints a capture of it with `ARC_AREA_LOAD_SCREEN_CAPTURE=PATH` (black screen, the clock in the top-right corner), and runs a fade-and-teleport on the map shown (`ARC-DEV-204 native-smoke fade PASS held=20 fadeIn=20 sounds=1 movies=1`: the change waits 20 smoke frames of a tenth of a second for the black screen, the plan's steps run in the original's order, the sound plays once, the fade-in takes 20 more).


## Fix: B03 (ARC-DEV-202, 2026-11-07)

B03 on `main` 9ca8c690 plus this change; hero `human-male`, `Arcanum1-024-fixed`, the owner's Mac. B30 is not built (below).

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B03 | Present (`WornItemUnsupported` on a looted body's worn item) | **Fixed** (both modes: a model gap, not a rule of the game) | X: Take all (0x579770) copies the body's whole list, worn items included, and tests no worn state; the transfer 0x461CA0 and the removal 0x466E00 (wield-off 25, remove-item 28) take a worn item off its slot ([ledger WL1–WL4](ledger/items.md#worn-items-of-a-looted-body-arc-dev-202)). C: `Take` now unwields on the loot path (insert check sees location −1, wield-off before remove-item with the body as triggerer); the negative `IN`, a purchase, a steal and `RemoveItemFromNpc` keep refusing a worn item. R: the new stage `loot-worn` (SM03, `./arcanum smoke pr --stages loot-worn`) kills the map's first NPC that wears an item (`oid:737e1861…`, 1 worn item) and loots it: moved=1, wornMoved=1; with the fix switched off the same stage fails on `WornItemUnsupported`. |
| B30 | Present | **Fixed by ARC-UI-072** (the doll view and its toggles; the two values and the weapon overlay of the original's doll stay open) | X: the original window has no sort buttons (the buttons its open makes are 340, the doll/bag toggles 338/339, 808 and two hot spots, IW30). What it has is the body's doll view on art 349 (IW29): that view and its toggles are the open part of B30, with the rectangles and covers read in WL4 (ARC-UI slice). |

| B30 | Present | **Present** (not built) | X: the original window has no sort buttons (the buttons its open makes are 340, the doll/bag toggles 338/339, 808 and two hot spots, IW30). What it has is the body's doll view on art 349 (IW29): that view and its toggles are the open part of B30, with the rectangles and covers read in WL4 (ARC-UI slice). |

## Fix: B25 for AI-host walkers (ARC-DEV-206, 2026-11-07)

B25 and GR12, on `main` 9ca8c690 plus this change; hero `human-male`, `Arcanum1-024-fixed --sector 1452,1291`, the owner's Mac.

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B25 | Present for NPCs that are not companions | **Fixed for the NPCs the AI host moves** (steps, knockback slides, route relocations); visitors from another map, jumps, teleport arrivals and created critters still notify nothing (GR12) | C: `AreaAiHost` tells the viewer the cell each NPC it moves is placed on (`AreaAiNoticeKind.Entered`; a follower is left to the companion drive) and the viewer runs the shown map's tile script for it with the NPC as the triggerer (`AiEntered`, the companions' `EnterByCritter`), as the original does after every move of a PC or NPC (ledger TR3, RL5, RL6). R: the new stage `npc-tile` (SM28 extended, `./arcanum smoke pr --stages npc-tile --hero human-male --sector 1452,1291`) PASS: the NPC named 6627 (the Molochean agent) walks onto tiles of script 1724 (global flag 2323, the triggerer named 6627, then destroy): with the flag clear the script runs and leaves it standing, with the flag set the script runs and destroys it. The agent's own heartbeat (script 1717) still stops at set day standpoint, get location and run to, so the stage orders the walk; the plan's full chain (the heartbeat runs him to the altar) stays out of reach. |

## Re-check after MIG13 (ARC-DEV-232, S15, 2026-11-07)

Verification only: no game source changed. Read on `main` as of 2026-11-07 (it holds the merged slices S13a (#750), S13c (#752), S13b (#753), S14a (#756), S14b (#760), S14c (#766), S14d (#772, the read only) and S14e (#775) and the smoke fix ARC-DEV-237 (#765)), hero `human-male`, `Arcanum1-024-fixed`, the owner's Mac. Run: `./arcanum smoke pr --hero human-male --stages cast,knockdown,turn-based,ai,ai-turn-based,ai-cast,pickpocket,life,floaters` PASS; `--stages ranged` PASS (after #765; the stun of a critical miss is the hero's run now); `--stages door-close --sector 1409,1314` PASS; `--stages npc-tile --sector 1452,1291` PASS. `ai-follower` prints `NO-FOLLOWER` (it needs a companion; it is not part of this run). The full map chain is the daily package build's (`./arcanum daily`), not run here. Unit and state tests: root checks and the viewer solution (1264 tests) pass.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B11 | Present (cause) | **Partly the original's own behaviour; the rest waits for the flag writer.** The hero's stun (57), knock-down (62), get-up (63) and dying are runs of the table (S13a, S13c); the rule of the hold is read (GT145–GT153): paralysis ends a goal-3 walk at once, stone interrupts nothing, an entangled hero already running keeps running; D8: Original keeps that, Steamwoven forbids moving while entangled. **Nothing writes the paralysed, stoned and entangled flags at run time yet** (the spell ObjFlag component is not ready), so the hold cannot be seen in play | C: `AreaHeroRunsHost` stun, down and get-up runs; the hold is not implemented. R: `knockdown` PASS (the hero's stun and fall as runs), `ranged` PASS (a critical-miss stun interrupts the swing). Stage SM09 `hold-hero` **not built**: it waits for the spell components that write the flags |
| B12 | Present (one half open) | **Present (one half open)**: the missile is a run (S8a) and follows its target at each leg; a multi-target spell with a target that moves between the cast and the hit was not tested (the older S8a row says the same) | R: `cast` PASS. SM08 (a three-wolf damage spell with one moving) **not built**; the plan runs it after MIG13 and again after MIG14 |
| B13 | Unverified | **Unverified**: dying is a run (goal 31) and a fallen body is drawn where its mover stands (S6); a spell kill of a running wolf was not run | R: `knockdown`, `life` PASS. SM11 `critter-fall` **not built** (sampling the draw cell of a spell-killed wolf for three seconds needs a stage of its own) |
| B14 | Fixed in the source (hero's throws); not seen with a real grenade | **Unchanged**: the shot-end item event fires the item's spell (S8b); the smoke lends a dagger an installed spell. An NPC's grenade and a grenade of the installed data thrown from the pack are not covered | R: `ranged` PASS (`shot-end-item-event`). SM12 `grenade` **not built** (needs a grenade in the pack and two NPCs) |
| B15 | Gone (reproduction not seen) | **Gone**: a door's own run opens it, waits 1.5 s and closes it; the Esc menu stops the timer (MIG12 pause) | R: `door-close` PASS, with the pause part |
| B33 | Stay | **Stays**: a left press on a companion is a talk (goal 21) and the order menu is the portrait's; the button mapping is not decided by the table. The executable has not been read for the companion click | C: S11a–e. SM24 `companion-click` **not built** (verify the executable first) |

| Plan stage | Status after MIG13 |
| --- | --- |
| SM06 `ai-react` (B08) | Built (ARC-DEV-264; the executable was read first, [hostility 7.7](npc-ai-hostility.md#77-a-spell-hit-arc-dev-264-read-on-2026-11-08-bug-b08)); red on `main` before ARC-DEV-281, green with it (see the last section) |
| SM08 `cast` (extend) | Existing `cast` PASS; the multi-target extension built as `cast-multi` (ARC-DEV-250, see the last section) |
| SM09 `hold-hero` | Built in ARC-DEV-304 |
| SM10 `door-close` | Built (ARC-DEV-138), PASS |
| SM11 `critter-fall` | Not built |
| SM12 `grenade` | Built (ARC-DEV-249), PASS (see the last section) |
| SM17 `route-barrel` | Not built (read-only state stage; B43 is expected fixed by ARC-WORLD-121 (the cave route approach), not re-verified here, and B44 needs research against the executable) |
| SM24 `companion-click` | Built by ARC-DEV-263 (B33), passes on `main` with the fix of ARC-DEV-280 |

**Gap register:** GR24 is done in its mechanics (the details are the S13c rows of goal-table.md; the hero's walk, attack, talk, use, pickup, throw, skill, cast, stun, knock-down and get-up are goals of the table; only the hold waits for the flag writer), GR25 was closed by S5b, GR26 is done for doors, projectiles, spell missiles, eye candy, burning scenery, dying, stun, knock-down and get-up (B12's half and the NPC flight of a grenade are the open details), GR27 stays open until S14f (the script casts, damage 60/138 and heal hp 75; ARC-DEV-241). TM7's far branch (the AI tick's level-4 interrupt) is done (S5).

## Re-check by the stage `route-barrel` (ARC-DEV-247, SM17, 2026-11-07)

Stage built; no game source changed. Run on `main` as of 2026-11-07 plus this stage, hero `human-male`, `crash site cave`, the owner's Mac: `./arcanum smoke pr --stages route-barrel --map "crash site cave" --hero human-male` PASS (3 barrels, 24 walks in the first run, the longest 51 steps, none longer than the shortest Open route plus the two steps of slack).

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B43 | Re-verify (expected fixed by ARC-WORLD-121) | **Gone** (reproduction not seen; the evidence is the session's own step source, so a step it wrongly calls Open would pass): from eight start cells at each of the cave's three barrels, the hero's click walks only over cells the map admits and Open steps, opens the search beside the barrel and takes no more steps than the shortest Open route plus two | R: `route-barrel` PASS. The reports' own start cells are not known, the stage spreads its starts over the reachable ground |
| B44 | Stay | **Stays**: the stage prints the ground reachable from the arrival cell (506 cells, x 17..47, y 13..43; the map mode lists no exit tile) as the state to compare with the original's map; the comparison needs research against the executable and is not made here | R: the `route-barrel ground` line of the run |

## Re-check by the stage `critter-fall` (ARC-DEV-248, SM11, 2026-11-07)

Stage built; no game source changed (the cast smoke's spell learning moved into a shared helper, unchanged). Run on `main` as of 2026-11-07 plus this stage, hero `human-male`, `Arcanum1-024-fixed`, the owner's Mac: `./arcanum smoke pr --stages critter-fall --hero human-male` PASS, and `--stages cast,critter-fall` PASS (the same animal killed by the cast stage earlier in the run, its old pool ignored).

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B13 | Unverified (a spell kill of a running wolf was not run) | **Gone** (reproduction not seen): an animal whose mover was mid-step when a spell killed it settled within a few frames on one cell for three seconds of game time, and the session's place and the pool of blood are on that cell (the drawn body takes its cell from the mover, so the check is that the mover does not go on) | R: `critter-fall` PASS (spell 55; in the first run the animal had run over two cells and died three frames after the click; it settled after three frames on the cell it was on). The report's own spell and distance are not known; the stage kills the weakest animal the hero can see from 7 to 9 cells, with one hit point left |


## Fix: B14 with a real grenade (ARC-DEV-259, 2026-11-08)

B14, on `main` fab1fbe9 plus this change; hero `human-male`, map `crash site cave`, the owner's Mac. The grenades of the installed data (items 15152 and 15172, spells 175 and 179, trigger 0x800000) lie in the cave's containers.

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B14 | Fixed in the source for the hero's throws; with a real grenade the item event fired but the outcome was `Refused`: no run, no damage, the grenade stayed on the ground | **Fixed with a real grenade.** Two gaps closed: the cast host found no object for a grenade lying on the ground (its map row still carried the carried flag 0x1000 the pick-up left, and a placed item was no cast object), so the item event could not name its source and parent; and the Destroy component every installed grenade ends its BEGIN with (0x451AF0, applied to self, which for a thrown item is the item itself) was not ready. Now a lying item is a cast object on its cell, the Destroy component deletes it (DS1–DS3) and the runs of a destroyed object end or lose their source (0x459740, DS4, parent ends by event, source cleared, other object ends by event) | R: the draft `grenade` stage (ARC-DEV-249, not merged: no PASS marker yet) picked the damaging grenade, threw it from the pack at an NPC tile: `started=1`, the spell's damage on the NPC in the radius (`Damaged` 28 on one NPC, whose life ended), eye candies on the creatures near, `Freed`, `placed=False destroyed=True`. Summon (type 18, both spells) still "not ready" (MT17); spell 179 (item 15172) has a damage of 0 to 0, so it only summons and destroys itself. Unit: `AreaSpellCastMapTests`, `SessionSpellDestroyTests`, `SpellRunSystemTests`; `FunctionRegistryTests` |

## Re-check by the stage `cast-multi` (ARC-DEV-250, SM08, 2026-11-08)

Stage built; no game source changed. Run on `main` as of 2026-11-08 plus this stage, hero `elf-female`, `Arcanum1-024-fixed`'s elven ruins map (the Crash Site has no three animals within two cells of one another), the owner's Mac: `./arcanum smoke pr --stages cast-multi --map "elven ruins" --hero elf-female --no-build` PASS.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B12 | Present (one half open): a multi-target spell with a target that moves between the cast and the hit was not tested | **Present (one half open)**, narrower: the spell's effect was not drawn on the first of the animals only. Spell 22 (damage, radius 2, lent to the kit weapon) cast at one of three running animals hit two of them once each, and each has its eye-candy effect added on it, so the effect is on the animal that is hit, not on its first cell. The third animal went from two cells to three cells away between the click and the hit and was not hit: the area is gathered around the **click's tile**, not the target's cell at the hit. Whether the original centres on the target's cell at the hit is not read | R: `cast-multi` PASS (`Moved` 3, `Hits` 1/1/0, `Effects` 2/2/0, `InsideRadiusChangedBetweenClickAndHit` 0). The report's own spell and number of enemies are not known; the plan's wolves and Crash Site are replaced by the data's animals on the elven ruins. The effect is read from the cast host's log, not from the drawn frame |

## B12 by ARC-DEV-327 (2026-11-10)

The question the `cast-multi` re-check left open, whether the original centres a radius spell on the target's cell at the hit, is read in the executable ([ledger RC1–RC5](ledger/spell-runs.md#centre-of-a-radius-cast-arc-dev-327)); no game source changed. The collection runs at the run's BEGIN processing and centres on the target object's location then, else on the run's target tile. A body clicked from farther away than the radius is cast at as a tile (the cursor's reach test is measured from the caster, SU17), so its centre is fixed at the click's tile; a body within the radius of the caster stays the target and the centre follows it. Steamwoven already does both.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B12 | Present (one half open): the area is gathered around the click's tile, whether the original does the same was not read | **Gone by the stage and the executable** (the reports' own spell and map are unknown): both symptoms are the original's behaviour or fixed. The spell's effect is added on every animal that is hit (`cast-multi`: hits 1/1/0, effects 2/2/0) and drawn at its cell at the hit, not at its first cell. The area is centred on the click's tile when the body is farther than the radius from the caster, as in the original, so an animal that ran out of that radius is not hit | R: `cast-multi` PASS on `main` 98b21fa89 (run 2026-11-10, `--stages cast-multi --map "elven ruins" --hero elf-female`) (`cast spell=22 target=tile …` in the log: the far click became a tile cast); unit test `Dev327TheRadiusGatherCentresOnTheClickedTileFromAfarAndOnTheObjectNowNearTheCaster`; exe: 0x4F40B0, 0x4F2680, 0x440B40. The report's own spell and map are not known |

## Re-check by the stage `grenade` (ARC-DEV-249, SM12, 2026-11-08)

Stage built after the real-grenade fix (ARC-DEV-259); no game source changed (the shot-end log line also prints the item spells' outcomes). Run on `main` as of 2026-11-08 plus this stage, hero `human-male`, `crash site cave`, the owner's Mac: `./arcanum smoke pr --stages grenade --map "crash site cave" --hero human-male --no-build` PASS.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B14 | Fixed with a real grenade (ARC-DEV-259, shown by the draft stage) | **Gone** for the hero's thrown grenade: item 15152 (spell 175) taken from a container, thrown from the cursor, started its spell at the flight's end, damaged one NPC (`Damaged` 28 and 0 on it) with eye candies on the creatures near, and was destroyed, neither on the ground nor in the pack. The plan's second NPC was not hurt: the grenade landed three cells beyond the aimed tile, outside its radius of the second NPC. Where a thrown item lands (scatter, range) is a separate question that this stage prints and does not judge | R: `grenade` PASS (`Stood` 2075,2016, `Aimed` 2078,2019, `Landed` 2075,2022, `Creatures` 1) |

## B33 by ARC-DEV-280 (2026-11-08)

The executable was read for the companion click (one reader, F1, [OL34](ledger/social.md)): the follower order menu opens on the right button's release over a portrait, a left release over a portrait is a click on the follower (a talk out of combat mode), and a click on the body opens no menu. The viewer opened the menu on the left button only; the portrait now follows the original (`AreaFollowerPortraits`, `FollowerPortraitClick`), and a release the menu does not take closes it. Native smoke: the `follow` stage of the Crash Site smoke opens Virgil's menu with a right click.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B33 | Present (original unverified) | **Fixed in the source**, the original's button mapping (the stage `companion-click`, SM24, follows in ARC-DEV-263 and checks the buttons on the portrait and the body; run against this change's source before and after, it fails before and passes after) | OL34 |

## Re-check by the stage `companion-click` (ARC-DEV-263, SM24, 2026-11-08)

Stage built on the fix of ARC-DEV-280; no game source changed. Run on `main` as of 2026-11-08 plus this stage, hero `human-male`, the Crash Site, the owner's Mac: `./arcanum smoke pr --stages companion-click --hero human-male` PASS. The first speaker of the map that is free (Virgil, script 1324) joins; the stage records 13 facts and fails if one differs from the original's buttons (ledger OL34): a right press alone opens no menu; a right release on his portrait opens it and a second closes it; a left click on his portrait with the menu open closes it and starts no talk; with it closed a left click opens no menu and talks to him; a left press on his body talks to him; a right click on his body opens no menu and starts no talk; a right release in the world, a right click on a menu item and a left click on an item that is not modelled each close the menu. Against the source before ARC-DEV-280 (the three portrait files of `main` before it) the stage failed on the right release, the left click with the menu open, the left click with it closed (it opened the menu and did not talk) and the right release in the world.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B33 | Fixed in the source (ARC-DEV-280) | **Gone**: the stage passes with the original's mapping, and fails on the old portrait code | `companion-click` PASS |

## Re-check by the stage `ai-react` (ARC-DEV-264, SM06, 2026-11-08)

The executable was read first (one Opus reader, F3, [hostility 7.7](npc-ai-hostility.md#77-a-spell-hit-arc-dev-264-read-on-2026-11-08-bug-b08)), then the stage was built, run, found red on `main` and the cause fixed by ARC-DEV-281 (#833). Run on `main` plus ARC-DEV-281 plus this stage, hero `human-male`, `Arcanum1-024-fixed` at the Crash Site, the owner's Mac: `./arcanum smoke pr --stages ai-react --hero human-male` PASS. Before ARC-DEV-281 (the viewer's wiring switched off, the stage otherwise the same) the stage failed: `danger=None` after the hit.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B08 | Unverified (the reaction to a spell hit from afar was not read) | **Fixed in the source** (ARC-DEV-281). The cause was not the distance: the notice rule of an aggressive spell (TA16) existed and nothing called it, so no spell hit ever ran the attack event. The original runs it before the component with no distance test, so the animal takes COMBAT_FOCUS on the hero at the hit. Spell 16 (aggressive, single target, range 99) lent to the kit weapon, cast at an animal 17 cells off: `danger=CombatFocus/hero`, and the animal came two cells toward the hero within 2.8 s of game time | R: `ai-react` PASS. The original stands still when it is hit from beyond 30 tiles of the player (its tick takes the far path): not tested, the screen shows no animal that far |

Found on the way (not part of B08):

- **No immediate AI pass after a hit.** The original's damage step runs the AI once for a surviving victim, with no tick and no distance test (0x4B52FC, also after a melee or ranged miss, 0x4B30F5, 0x4B3948). Steamwoven had none: a hit NPC acted at its next tick (up to 5 s). **Fixed by ARC-DEV-292** (`AreaAiHost.Struck`, [hostility 7.7](npc-ai-hostility.md#77-a-spell-hit-arc-dev-264-read-on-2026-11-08-bug-b08)); the stage `ai-react` now also asks for the pass at the hit.
- **A turn cut a step.** The host's turn toward the attacker snapped a walking NPC to its cell; with spells now running the attack event it showed in `critter-fall` (the animal stopped before the spell killed it). ARC-DEV-281 leaves a step in progress alone for the attack event's turn only; a goal's or an action's turn still cuts.
- **`critter-fall` fails after `ai-cast` in one run** (`--stages cast,ai,ai-cast,critter-fall`: "the animal ran over at least two cells before it died"), on `main` too (checked with the notice switched off); it passes alone. A stage-order dependency of the stage, not of B08.

## Re-check by the stage `inv-carry` (ARC-DEV-305, SM02, 2026-11-08)

Run on `main` plus this change, hero `human-male`, `Arcanum1-024-fixed` at the Crash Site, the owner's Mac: `./arcanum smoke pr --stages inv-carry`. The stage was first run without the fix (the viewer's inventory host built with no prototype reader) and failed with "the made item has wear facts (B36)": a made item (`made:1`) had no wear facts, so the window's wear step reported "has no wear values here". With the host reading a made item's facts from its prototype it passes.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B36 | Unverified (boots cannot be worn; the logs show a wear drop of `made:21` ending with no disposition) | **Fixed in the source** (ARC-DEV-305): `AreaInventoryHost.Wearable`, `UseFacts`, `HealingItem`, `Lockpick`, `ItemSpells` and `RepairBaseline` read the facts of a `made:` item the hero holds from its prototype, and its window line offers the same actions as a taken item's | The stage fails without the reader and passes with it; the unit tests `AreaMadeItemFactsTests` |
| B01 | Unverified (the case was never run) | **Not reproduced by the multiset**: the multiset of (prototype, units, place) of the hero's items, taken and made, is equal after the debug travel's window move and after F7, F8, and the window lists every held item; the window's text of the items was not compared yet and the temple map step (plan step 5) was not built: see [the ARC-DEV-314 re-check](#re-check-of-b01-by-the-stage-inv-carry-step-5-arc-dev-314-sm02-2026-11-10) | The stage's PASS line |

## Re-check of B01 by the stage `inv-carry` step 5 (ARC-DEV-314, SM02, 2026-11-10)

Run on `main` plus this change, hero `human-male`, `Arcanum1-024-fixed`, the owner's Mac: `./arcanum smoke pr --stages inv-carry --hero human-male`. The stage now also compares the window's text of every held item and goes through the report's own map change (the Shrouded Hills town window, an exit object into `shrouded hills-panarii temple lev 1`, F7 and F8 there, the exit back).

- **Reproduced (first run, without the fix).** After the debug travel's window move the held items were all still there (the multiset was equal), but the window's text of two taken items (generic items, each shown with its name and weight) changed to "[unknown object] (generic item)", with no name and no weight. The item itself (prototype, units, place) did not change; what the hero sees did, which is the report's "items turn into others".
- **Cause.** A window of the main map is runtime map 1 every time, so the inventory host built for the new window holds that window's rows only; the name and the weight of a taken item come from its object row, which stayed in the host of the window it was taken in. The resolver reads a map on demand by its number, which cannot tell two windows of map 1 apart. The made items were not affected (named by their kind).
- **Fix (source).** The host hands the rows and weights of the items the hero holds to the next host (`AreaInventoryHost.HeldRows`, `Retain`, `AreaHeldRow`); a row of a held map is not overridden; a second move keeps them. The rows are read from the item's own record, as the original names an object from its record wherever it is (no change to the saved game or to the original's behaviour). Unit tests `AreaInventoryHostHeldRowsTests`.
- **Result (second run, with the fix).** `ARC-DEV-305 native-smoke inv-carry PASS` with the checks before the move, after the window moved, after the quick load, in the town window, in the temple map, after the quick load in the temple map and back from the temple map (6 held items, 3 worn each time).

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B01 | Not reproduced by the multiset (the text was not compared) | **Fixed in the source** (ARC-DEV-314): reproduced as the window's "[unknown object]" for taken items after a window move; the temple map round trip passes | The stage's PASS line; `AreaInventoryHostHeldRowsTests` |

## Re-check of B07 by the stage `placement-census` (ARC-DEV-308, SM07, 2026-11-08)

The two reports (2026-10-31 12:48:49 and 12:51:38, build `a156e1435c` of 2026-10-29, hero in sector (1452,1289) and (1451,1289), game time 16:24 and 17:32 on day 1) say: near the first position the original has a boar next to the wolf, and in the second place three Lesser Boars stand together while one belongs on the other side. The log holds only damage lines. No cause was found in the placement: the data and the stage agree with what the second report shows. Run on `main` `656b8ba2f` plus this stage, hero `human-male`, `Arcanum1-024-fixed` at the Crash Site, the owner's Mac (`./arcanum smoke pr --stages placement-census --hero human-male`, PASS); the data census was taken by a throw-away real-data test against `/Applications/Arcanum`.

- **The data.** The map's mobile aggregate (7,399 rows, the file the original reads at a new game, [area inventory](area-inventory.md)) has 46 NPC rows within ten sectors of sector (1452,1289): 13 Ailing Wolves (the weak wolves of the wreck), 8 Wolves (about 170 cells to the south), **3 Lesser Boars**, and 22 people and other humanoids. The three Lesser Boars are placed together, within 18 cells of each other (sectors (1451,1290) and (1452,1290)), 43 to 61 cells west of the first report's hero cell and 8 to 11 cells from the second's; there is **no boar row east of the wreck** near the wolf rows there. The nearest other boar rows are tens of sectors away. The map patch (`rules/map.patch`) changes 10 objects of the map; none is an animal near the Crash Site. So three boars together is the data, and a boar near the first position is not in the data.
- **The reports' build.** `a156e1435c` is older than the residency and the saved NPC places (npc.pos #624, residency #640, #650, #652 of 2026-11-05, and the later goal runs): on that build NPCs were built from the window and carried no saved place, so a placement fault of that build cannot be told from the report and the earlier re-check (which said nothing merged since touches placement) did not see these merges.
- **`main` now (the stage).** At the map open and again after F7 and F8, 45 NPC rows (the 46th is off) lie within ten sectors of the hero: 42 are held by the AI host and every one stands on its row's cell; the host refuses a duplicate NPC when built; the three not held are three sectors out (by distance in sectors from the hero's, rows held of rows: 0: 20/20, 1: 14/14, 2: 8/8, 3: 0/3). Every Lesser Boar row is among the held (they are one sector from the hero). Nothing moves a placed animal before the AI's first tick.
- **What is left open.** What the reporter saw may be the original's animals walking: the reports are at 16:24 and 17:32 of game time, hours after the start, and the stage observes the places before the first tick, not what a wandering boar does over those hours; the original's wander of these animals was not read for this. If a boar's own position at the start is needed, it is one of the three rows. The census prints one row held that is flagged off (an Elf City Dweller row; RL9 excludes it from the original's queries): the host holds it and it is not judged here (not related to B07, not investigated).
- **Verdict.** B07: **no defect in placement found** on `main`; the report cannot be reproduced from the data. Nothing changes in the source. If the owner still sees a boar where the data has none, the next evidence is a new Ctrl+B report from a current build taken at the first minutes of a new game.

## Fix: B20, the ghost of Brego (ARC-DEV-298, 2026-11-08)

B20 on `main` af8eb52b plus this change; hero `human-male`, `crash site cave` (script 2768 on the tiles 2066..2070,2007), the owner's Mac.

| ID | Before | Now | Evidence |
| --- | --- | --- | --- |
| B20 | Present: the tile script 2768 of the cave was read as refused at "type 18", then (ARC-DEV-157) at cast spell on (61), "until MT21" | **Fixed.** The cast was done by ARC-DEV-241 (set S29); the refusal that was left lay one call deeper: the loop calls the ghost's dialog point (call-ex, 81), script 2789, whose line 9 is **float line (23)**, which the tile and sector set did not admit. The set now admits it, and a tile run floats the line over its speaker | R: the stage `tile-ghost` (`./arcanum smoke map "crash site cave" --stages tile-ghost --hero human-male`) PASS: with the story flag 2672 clear the script returns at line 3 and casts nothing (control); with it set the run goes loop for (18), cast spell on (61, line 8), call-ex (81, line 9) to 2789 and the line the ghost's line (DLG of 2789) is floated over the ghost (`ARC-DEV-298 float`). C: `PlacedAdmission` + float line, `AreaUseScriptHost.TakeFloats`, `TerrainViewerApp._tileScriptFloats`; unit test `A_tile_script_floats_a_line_over_the_pc_and_hands_it_out_once` |

The log's "type 18" of the report is the loop for action (18) seen through the old refusal on the list focus (a condition over everyone in the vicinity), as ARC-DEV-157 read it; the story gate (flags 2672 and 2645) is the original's, so a hero at the start of the game meets no ghost (the control).

The census of tile scripts (`AreaUseScriptsOriginalTests`) writes the first stops of the refused tile scripts, counted by kind, to the file named by `ARC_TILE_CENSUS` (no game data). At af8eb52b the first stops of the 1,612 refused were: script 30286 not modelled (584), action 22 (138), action 32 (98), change art (59, 91), condition 45 (89), condition 51 (85), action 45 (81), play script eye candy (120, 68), action 112 (62), condition 46 (57), loop over the followers (focus 2, 29 scripts), and 178 runs whose effects the census host cannot apply (it has no AI host or viewer session).

### B25 left open, and one part closed (ARC-DEV-298)

A companion the AI host pushes (`AreaAiHost.PushSlide`, a knockback; not its catch-up to the leader nor a route relocation, which the companion drive owns) now sends an `Entered` notice for its landing cell like a step and runs the shown map's tile script for it (the original runs the tile's script for any PC or NPC after every move, ledger TG2 and TR3). Still open: visitors from another map (their rows are not the shown map's; the runner needs the home map's NPC for the triggerer), a follower's own follow jump, teleport arrivals and created critters (no host path creates a critter on the shown map yet), the wading test and a new sector's script and point 27 for followers. The pushed-companion path has no smoke stage (it needs a companion pushed onto a script tile); `npc-tile` covers the shared notice path. A tile run's float line is read with the male text variant, as the use host's print line is (the talk host reads the hero's gender); the gender of the PC is not passed to the use host yet.

### B25 rest: guests, catch-up jump, arrival (ARC-DEV-328, 2026-11-10)

On `origin/main` 98b21fa89 plus this change, hero `human-male`, `Arcanum1-024-fixed`, the owner's Mac. The four remaining cases were read in the executable first ([ledger TG9](ledger/scripts.md)): every one of them reaches the notification 0x444270 (a PC or NPC runs the tile's script), through the relocation, the Teleported callback, creation 0x442260 or the follower catch-up 0x4AD4D0; the original has no visitor, so a carried companion runs it as any NPC.

| Case | Before | Now | Evidence |
| --- | --- | --- | --- |
| Guests from another map | Skipped (the home map was not the shown map) | **Fixed**: steps and the arrival cell of a carried companion run the tile's script, the triggerer named by its home map, its place the group's live cell | C: `AreaUseScriptHost.RunPlaced` takes a triggerer of another map while the viewer holds its live cell; test `A_tile_script_runs_for_a_companion_carried_from_another_map_only_while_it_stands_here`. R: `transfer` and `follower-home` PASS (no script tile at their arrival cells) |
| Companion catch-up jump | No notice | **Fixed**: the follow jump (companion drive) and `MoveToLeader` (AI host) tell the landing cell; the waypoint relocation of a companion too | C: `A_following_companion_more_than_thirty_tiles_off_catches_up...`, `A_companions_waypoint_relocation_tells_the_cell...`. R: `follower-far`, `ai-follower` PASS |
| Teleport arrivals | None | **Partly**: followers that travelled with the PC (the transfer's arrival cell). Not the leaderless ex-follower sent home (its script runs when its home map opens; needs a saved teleported marker), nor the PC's own arrival tile | exe TG9 |
| Created critters | None | **Settled by the executable, not built**: a created NPC runs its creation tile's script (0x442260 → 0x444270 mode 2); no host creates a critter on a map yet | exe TG9 |

No smoke stage has a script tile at a follower's arrival or catch-up cell, so the viewer glue (the arrival queue, the jump snapshot) has no live evidence of a firing script; `npc-tile` PASS shows the shared path is unchanged.

### B23 looked at on the way (ARC-DEV-298, T7 of the triage)

Not closed. The tile script 30286 of the report (584 tiles of the town, the biggest first stop of the census) is not the trash search: it reads the hero's stat 19 and plays an ambient sound scheme (action 87; scheme 1 or 3). The census host has no hero, so its get stat answers "not modelled" there; with a hero the script runs. The report's clicks left `portal use ... Locked` lines of a door (sector 1409,1314 entity 649) and no container line; the town window holds 27 containers (type 11, among them prototype 12080 at 90234,84157 and 90243,84157 beside the tile script's cells) and none with the generated-once flag 0x200, so the `container` stage reports NONE there (it needs such a crate). What is still unknown is what a click on a trash can resolves to (the object picked at its pixels). It needs a stage that clicks each container of the window through the production click path: ARC-DEV-299.

## Re-check after MIG14 (ARC-DEV-291, S10, 2026-11-08)

The last slice of [saved runs](../architecture/saved-runs.md) (MIG14, reopened by the owner on 2026-11-08 to save the action in flight as the original does). It re-reads B10–B12 only. No game source was changed. Runs were on `main` 34ad913f5 (S7–S9e merged, #852, #869–#876, #879) on the owner's Mac, hero `human-male`, `Arcanum1-024-fixed`:

- `./arcanum smoke pr --stages map,cast,ranged,pickpocket`: PASS
- `./arcanum smoke pr --stages ai-cast --no-build`: PASS
- `./arcanum smoke pr --stages saved-runs --sector 1409,1314 --no-build`: PASS

Every sub-step that saves in flight printed PASS: `saved-runs`, `saved-walk`, `saved-talk`, `saved-flight`, `saved-missile`, `saved-cast` (nodes 2 and 3), `saved-npc-cast`, `saved-attack`, `saved-throw`, `saved-pickup` and `saved-pickpocket`.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B10 | Changed: positions and the clocks are state, a run is not | **Gone by the stages** (SM30 as written not run): a quick load keeps the NPCs on their saved cells (`npc.pos`, MIG7c), the loaded-sector history (schema 68), the maintained spell in its slot (ARC-DEV-211), and now every run in flight: the NPCs' goal runs, the projectiles and spell missiles with their shots (schemas 80, 84), the NPCs' casts, and the hero's walk, states, attack, throw, cast, talk, use, pickup, lock pick and pick pocket. The quick-save gates for a playing cast and a flying missile are gone. | R: the eleven saved sub-steps above PASS. Each one saves mid-action, loads, and checks that the loaded run ends as the live one did. SM30 as written (a wolf running at the hero at the Crash Site) is still not run; the town's walking NPCs of `saved-runs` stand in for it. One limit stayed after S10, a player could not save a walking pick pocket (the open steal window took F7 and its close ended the run); ARC-DEV-323 read the original (F7 goes past the steal window, closes it and saves, and the close ends no goal, ledger SL12, SL13) and lifted it. |
| B11 | Partly the original's own behaviour; the rest waits for the flag writer | **Unchanged by MIG14** apart from the save: the hero's stun, knock-down, get-up and dying are saved runs now (S9a), so a load mid-stun keeps the stun. The hold itself (the ObjFlag and Effect components, T2 of the [triage](bug-reports-triage-2026-11-08.md)) is not MIG14's. | C: `HeroRunIsSavedKind` keeps goals 57, 62, 63 and 31; the components are still logged "not ready". Not run (no hold-effect stage). |
| B12 | Present (one half open): the area is gathered around the click's tile | **Present (one half open), unchanged by MIG14** apart from the save: a spell missile in flight and the cast in progress are saved, and on load they follow their target from the saved leg (`saved-missile`, `saved-cast`, `saved-npc-cast` PASS). The open half (the area centred on the click's tile, not on the target's cell at the hit) is not a save question. | R: `cast` PASS with `saved-missile` and `saved-cast`; `ai-cast` PASS with `saved-npc-cast`. `cast-multi` was not run again (no source change since ARC-DEV-250). |

## Re-check of B10 by the stage `save-resume` (ARC-DEV-325, SM30, 2026-11-10)

SM30 as written, built and run: the stage `save-resume` (hero `human-male`, `Arcanum1-024-fixed` at the Crash Site, AI on). The hero holds a maintained spell; the weakest animal of the wreck (prototype 28359, a wolf) is told of the hero's attack and runs at him from eleven cells; F7 saves in the middle of its third step, nine cells from him; twelve live frames bring it to six cells; F8 loads. No game source was changed.

| Bug | Before | Now | Evidence |
| --- | --- | --- | --- |
| B10 | Gone by the stages (SM30 as written not run) | **Gone** (reproduction not seen): after the load the spell holds slot 0 with its tick queued, the wolf stands on its saved cell (92995,82592), the loaded AI host's mover is on that cell too, its goal run (Attack, depth 3) is in the table under its handle, it still fights the hero, and it runs on over nine cells to the cell next to him | R: `./arcanum smoke pr --stages save-resume --hero human-male` PASS (distances in chart cells: 11 at the start, 9 at the save, 6 live after twelve frames, 1 after the load). Parity with the original runtime is not verified |
