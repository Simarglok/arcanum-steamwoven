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
- **The 13 unverified rows** need the executable reading the plan already names (B08, B17, B21, B44, B45), a stage the plan already describes (SM07 for B07, SM23 for B31, B32, B36 and B41, SM19 for B46), the reporter's save and hero (B36, B40) or the prototype read (B37). B33, B34 and B35 are Present, but whether the original behaves differently is also unverified and waits for the executable.
- **B02** is the one reproducible migration-relevant failure left in the object-model group; MIG6 is its planned slice, and the same seed refusal blocks the report's path of B01.
