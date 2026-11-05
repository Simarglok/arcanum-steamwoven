# Open bugs: root-cause groups, migration map and smoke plan

Status: **Research / test plan**, work item ARC-RES-089 (2026-11-04). Docs-only: no code changes, no checks run.
It collects every open player bug, groups it by root cause, maps each bug to the gaps
(GR) and migration slices (MIG1-MIG16) of the [gap register](../architecture/gap-register.md) and
[target model](../architecture/target-model.md) that are likely to touch it, and plans the smoke re-checks that run
after each migration step. It fixes nothing: the bug-fix hold of the [architecture audit](architecture-audit-plan.md)
stays on.
The re-check of its 46 bugs on `main` after MIG1-MIG5 and MIG10 (ARC-RES-090) is in [open-bugs-resmoke-2026-11-04.md](open-bugs-resmoke-2026-11-04.md).
The re-check of B06, B10 and B25 after MIG9 (ARC-DEV-107) is the last section of the same file.

Requirements (reference only, no coverage row changes): the groups of the gap register, above all
[FR-WORLD-003](../requirements/world.md#fr-world-003), [FR-WORLD-010](../requirements/world.md#fr-world-010),
[FR-WORLD-013](../requirements/world.md#fr-world-013),
[FR-RUNTIME-001](../requirements/world-travel-runtime.md#fr-runtime-001),
[FR-LIFECYCLE-003](../requirements/ui-media-lifecycle.md#fr-lifecycle-003),
[FR-SCRIPT-002](../requirements/story-social-ai.md#fr-script-002),
[FR-UI-008](../requirements/ui-media-lifecycle.md#fr-ui-008) and
[FR-SCENE-014](../requirements/scene.md#fr-scene-014).

## Answer

- **46 open bugs** in eight root-cause groups. They come from 44 player reports in `artifacts/packages/macos/reports/`
  that are not in `reports/archive/` (36 distinct bugs after merging repeats, for example five barrel reports are one bug),
  from the pending triage items (3 that no report covers; two more are the other half of B17 and B18) and
  from 7 bugs that only the repo docs and merged-PR notes record.
- **Expected to disappear with the migrations: 7** (B01, B02, B04, B05, B09, B10, B15; four of them, B01, B02, B04 and B05, come from player reports, B09, B10 and B15 are recorded in the register only). **Re-verify, probably fixed already: 1** (B43, addressed by #585 after the reports).
  **Expected to change, may disappear or leave a residual fix: 10** (B03, B06, B07, B08, B11, B12, B13, B14, B16, B25). **Stay and need their own fix: 28.**
- The migrations act on the object model (G1), residency (G2), goals (G3) and change-of-place (G4) groups. Scripts (G5), interface (G6),
  hosts (G7) and pathing and render (G8) are own tasks whichever way the migrations go, except B25 (NPC tile scripts, with MIG9) and the re-check B43.
- **Smoke plan:** 30 entries (SM01-SM30): 5 entries extend an existing stage and 25 are new stages (6 distinct existing stages are extended: `junk`, `cast`, `container`, `floaters`, `hover`, `altar`);
  a schedule says which run after which MIG step. SM07 and SM17 are read-only and give the state before the migrations.
- **Read the executable first** ([original executable check](original-executable-check.md)) before a fix is planned for: B03, B08, B11, B14, B17, B21, B33, B34, B35, B40, B44, B45.

## Sources and method

- **Reports.** All 44 report folders in `artifacts/packages/macos/reports/` outside `archive/` (2026-10-29 17:16 to
  2026-10-31 13:03), read as the player's text, the game-state table and the last lines of `log.txt`. The folders hold original
  game content and are not committed; here a report is named by its folder (`10-29_21-07-37` is `2026-10-29_21-07-37`), and log lines are quoted only as ids and outcomes.
- **Builds.** The reports are from `main` 3d22813594, 717dc1aa45, cce1629223 and a156e1435c (all 2026-10-29). Since a156e1435c the
  only merged bug fix is ARC-WORLD-121 (#585, cave route approach). The rest of `main` is audit research, tooling and two
  migration slices: MIG10 (ARC-DEV-073, transfer parity) and MIG1 (ARC-DEV-072, the record resolver in `src/Arcanum.World`; its
  consumers still ask by shown map and move in MIG2-MIG5, so MIG1 alone changes no bug here). The state is `main` at a5245f4f
  (2026-11-04 16:14). So every report is still open unless a row says otherwise.
- **After MIG5 (ARC-DEV-081).** The dialog host is one (a carried companion is one more speaker of the shown map's host) and its negative `IN` refusal
  `refuseUncoveredInventory` is gone: the session refuses an entity the hero's map seed does not list. That is the dialog's `IN` only. The
  `inventory-seed ... UnlistedInventoryItem` refusal of B02 and B05 comes from the seed build (`AreaInventory`), which MIG5 does not touch (MIG6, one
  id space), so those two rows keep their expected slices; the `transfer` smoke's visitor talk and the unit tests cover MIG5 (SM02's first half is the seed, not the talk).
- **Pending triage.** The triage file of 2026-10-29 17:00 was not readable here. Its pending items are taken from the project
  notes: A2 message panel and hover fonts (art 27/229), A3 floaters (art 229), B1 conversation window, the low-intelligence
  reply of DLG 1696 and the examine item G2. Their numbering follows those notes and should be checked against the file.
- **Docs.** Other open bugs come from the gap register (GR10, GR15, GR18, GR26), the script audit (slices 2-13),
  the notes of the merged PRs #551, #478 and #581 (C1) and the experience "Not ready" list. There is no separate alpha-gap planning file in the repo.
- **Evidence.** Each row says whether its cause is seen in the log, in a doc, or only inferred; an inferred cause is not a
  finding. Nothing here is a reading of the original executable.

## Groups by root cause

| Group | Root cause | Gap rows | Bugs | Gone | Change | Stay | Re-verify |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| G1 | Object model: identity, record sources, inventory seeds, created objects | GR1-GR4, GR11, GR17, GR30 | 4 | 3 | 1 | 0 | 0 |
| G2 | Residency and NPC state: only the shown window lives | GR8-GR10, GR12, GR13, GR15 | 6 | 3 | 3 | 0 | 0 |
| G3 | Goals, clocks and projectiles: two goal machines, viewer-owned hero actions | GR20, GR24-GR27 | 5 | 1 | 4 | 0 | 0 |
| G4 | Change of place: no single request, no loading presentation | GR16 | 1 | 0 | 1 | 0 | 0 |
| G5 | Script surface and story conditions (script audit slices) | GR7, GR27-GR29 (partly); script-trigger-audit slices 2-9 | 10 | 0 | 1 | 9 | 0 |
| G6 | Interface, messages and art fonts not built or not yet in the original's look | GR35 only for modes (MIG16); otherwise outside the register | 9 | 0 | 0 | 9 | 0 |
| G7 | Hosts not wired or unverified: combat, items, audio, magic | outside the register | 7 | 0 | 0 | 7 | 0 |
| G8 | Pathing, passability and render order | GR8/TM5 only for mobile-object blocking; otherwise outside the register | 4 | 0 | 0 | 3 | 1 |
| | **Total** | | **46** | **7** | **10** | **28** | **1** |

- **G1.** Items and bodies are found through the shown window's rows and per-kind keys, not as objects with one identity; seeds are rebuilt for the loaded window only.
- **G2.** Only the shown window has hosts and positions; NPC cells and runs are not state; no waypoint, standpoint or far-path step; no homecoming.
- **G3.** The hero's actions, spell missiles, projectiles, dying and doors are not runs of one table; the hero cannot be stopped as an NPC is.
- **G4.** Each caller moves things itself; the request's fade, blank and clock flags do not exist.
- **G5.** A script refused by the admission sets, a command not modelled, or a story condition; independent of the object model except attachments (MIG8) and tile scripts of moved critters (MIG9).
- **G6.** Windows, buttons, messages and font blends that no audit row covers; each is its own UI slice.
- **G7.** A rule exists in the domain but the viewer host is missing, or the behaviour was never checked against the executable.
- **G8.** Route planning and cave passability, indoor darkening and draw order of animated scenery.

**Reading the expectation.** *Gone*: the migration builds the mechanism the bug lacks, so the symptom cannot stay (it is
re-checked, not trusted). *Change*: the migration rebuilds the code under the symptom; it may vanish or leave a residual fix.
*Stay*: no migration slice touches the cause; an own task is needed. *Re-verify*: probably fixed already.

## Bug catalogue

A source like `10-29_21-07-37` is the report folder `2026-10-29_21-07-37`; `triage` is a pending triage item; `docs` is a repo document and `PR #n note` the description of a merged PR. Smoke ids are in the [smoke plan](#smoke-plan).

### G1. Object model: identity, record sources, inventory seeds, created objects

| ID | Bug (source) | Evidence | Gap / migration | Expected | Own fix | Smoke |
| --- | --- | --- | --- | --- | --- | --- |
| B01 | Items taken from the Crash Site corpses (passport, matches) turn into spider models, a ring turns into another ring, after the hero left the Crash Site window (`10-29_21-09-10, 10-29_21-10-07`) | Log: wear and drop lines carry the created-item key `made:21`. Docs: the seed is rebuilt after each window change (GR11) and keys are per kind (GR3). Both reports were taken inside the temple map; that the move to the town started it is a session inference, and that a per-kind index is reread against another store is an inference too; neither is confirmed in code. | GR3, GR11, GR2; MIG1 (done), MIG5, MIG6 | Gone | No, if MIG6 gives one id space; a regression test for the id after a window move | SM02 |
| B02 | After the move to the town the inventory is unavailable and the hero is naked; the clothes come back inside the temple (`10-29_21-07-06, 10-29_21-08-25`) | Log of both reports (and of 10-29_21-13-22 and 10-29_21-17-14): `inventory-seed map=1 status=InventoryRefused detail=UnlistedInventoryItem` on the town window. Docs: the seed lists the loaded window's items only (GR11). That the worn set returns inside the temple because that map has its own seed is an inference. | GR11, GR1; MIG1 (done), MIG5, MIG6 | Gone | No | SM02 |
| B03 | Kite Shaman has no dagger in his inventory; Take all does not move his items to the hero (`10-31_12-45-24`) | Log: `loot-all-item disposition=Rejected failure=WornItemUnsupported` (docs: looting a worn item is not supported, equipment-runtime). Part is a missing rule (worn loot), part is the holder's base items by record (GR1). | GR1, GR4; MIG1, MIG6 (items as objects with a parent) | Change | Yes: worn-item looting rule and the NPC row check; the worn/bag tabs are B30 | SM03 |
| B04 | A pile of items on the ground stays after it is searched empty (`10-29_21-47-12`) | Log: `loot-all taken=3`, then repeated `taken=0` on the same holder; the pile is a created object with a per-kind store (rows S10 and S11 of the [Steamwoven model](../architecture/steamwoven-model.md)). | GR4, GR17; MIG6 (one destroy path, OM17-OM19) | Gone | No; extend the `junk` stage | SM04 |

### G2. Residency and NPC state: only the shown window lives

| ID | Bug (source) | Evidence | Gap / migration | Expected | Own fix | Smoke |
| --- | --- | --- | --- | --- | --- | --- |
| B05 | After a quick load in the town, Virgil is not in the party and not near (`10-29_21-13-22`) | Log: `transfer arrived Kind=QuickLoad From=1 To=1 Session=NoChange`, the seed refusal of B02, `visitor not talkable ... no body kept this run` and `companions bound []` after the load: the GR2 symptom is seen. Inference: the companion stood in the Crash Site window, which a town window does not hold (GR8), and nothing brings a follower to the leader (far path, GR13). | GR2, GR8, GR9, GR13, GR10; MIG4, MIG5 (done, ARC-DEV-081: it removed the dialog's `refuseUncoveredInventory`; the seed refusal named in this row is the seed build's and stays for MIG6), MIG9 (MIG1 and MIG10 done: resolver and transfer parity only) | Gone | No | SM01 |
| B06 | Town NPCs stand in place, guards do not patrol (`10-29_21-07-37 (first part)`) | Docs: the heartbeat is wired for the shown map only, with no waypoint or standpoint step (GR13); positions are reset at every window change (GR10). The dialog half of the same report is B19. | GR9, GR10, GR13; MIG7, MIG9 | Change | Maybe: the waypoint data needs a check once MIG9 lands | SM05 |
| B07 | The boar that stands near the wolf in the original is missing; elsewhere three Lesser Boars stand together while one belongs on the other side (`10-31_12-48-49, 10-31_12-51-38`) | No log evidence of a cause. Hypotheses: placed NPCs moved by their runs before the report (no state, GR10), or a placement or generator difference. Unverified; needs a census of placed NPCs against the map rows (SM07) and the original's positions. | GR10, GR9 possible; MIG7, MIG9 possible | Change | Yes if the census shows a data or placement difference | SM07 |
| B08 | After a ranged spell from far away the NPC does not attack the hero and stands still (`10-31_12-40-53`) | Log: casts of the same spell with `Refusal = None`; no AI reaction line for the target that was hit by spells only (another target that was also meleed does log `ai-hit`). The heartbeat takes the near/far branch by distance (RL16) and Steamwoven runs one branch for the shown map. Unverified which reaction the original has to a spell hit. | GR13, GR25; MIG9, MIG13 | Change | Verify in the executable first (reaction to a spell hit from outside the near radius) | SM06 |
| B09 | A dismissed or lost companion never goes home (recorded, no player report) (`docs: GR15`) | Gap register GR15: no teleport-map field, no forced waypoint pass. | GR15; MIG11 (V6) | Gone | No | SM29 |
| B10 | A quick load or a window move puts every NPC back on its first cell and a maintained spell ends (recorded, no player report) (`docs: GR10, GR18`) | Gap register GR10 (positions are not state) and GR18 (runs lost on load). | GR10, GR18; MIG7, MIG9, MIG14 | Gone | No | SM30 |

### G3. Goals, clocks and projectiles: two goal machines, viewer-owned hero actions

| ID | Bug (source) | Evidence | Gap / migration | Expected | Own fix | Smoke |
| --- | --- | --- | --- | --- | --- | --- |
| B11 | A spell that holds the hero in place does not stop him from running (`10-29_17-44-57`) | Docs: the hero's actions are viewer hosts beside the NPC executor, so a stun, knock-down or hold cannot interrupt them (GR24). Which spell: not named in the report. | GR24, GR25; MIG13 | Change | Maybe: the effect itself (hold flag) needs a check once the hero is in the goal table | SM09 |
| B12 | The spell animation plays on the first of two to four enemies only; and on the point where the enemy stood at the start of the fight, not where it is when it takes damage (`10-29_21-43-18, 10-29_21-46-22`) | Docs: spell missiles and eye candy are not runs of the goal table (GR26); NPC positions are run-local (GR10). The second symptom reads a stale cell. | GR26, GR10, GR20; MIG7, MIG12, MIG13, MIG14 | Change | Yes if the multi-target path still draws one effect after MIG13 | SM08 |
| B13 | After a spell kill the wolf's body "flies" toward the hero and lies on the cell where it would have started its melee attack; the pool of blood stays halfway (`10-31_12-49-43`) | Log: `blood-pool ... cell=92941,82559` (the pool is on the session cell). Inference: the body is drawn at the mover's pixel position; dying is not a run (GR26). | GR26, GR10; MIG7, MIG13 | Change | Maybe | SM11 |
| B14 | Thrown grenades land on the ground and do not explode; they can be picked up again (`10-29_21-49-38`) | Docs: projectiles are viewer objects, not runs or created objects (GR4, GR26). The explode-on-land effect of a thrown item is not a recorded row. Unverified. | GR4, GR26; MIG6, MIG13 | Change | Yes: the item trigger on landing is a rule to read in the executable | SM12 |
| B15 | Doors never close by themselves (recorded, no player report) (`docs: GR26, FR-SCENE-014`) | Gap register GR26: no door run (open, wait, close). | GR26; MIG13 | Gone | No | SM10 |

### G4. Change of place: no single request, no loading presentation

| ID | Bug (source) | Evidence | Gap / migration | Expected | Own fix | Smoke |
| --- | --- | --- | --- | --- | --- | --- |
| B16 | No loading screens (black screen with the clock animation in the corner) (`10-31_13-03-23`) | Log: `transfer arrived Kind=Transfer From=79 To=1` with no loading presentation. The original's change-of-place request has fade, movie and blank flags (RL21-RL23). The screen itself is presentation and is a separate UI task. | GR16; MIG11 | Change | Yes: the presentation (art, clock animation) | SM13 |

### G5. Script surface and story conditions (script audit slices)

| ID | Bug (source) | Evidence | Gap / migration | Expected | Own fix | Smoke |
| --- | --- | --- | --- | --- | --- | --- |
| B17 | The dialog at the plane wreck is incomplete (possibly because of the hero's low intelligence); one turn, then it closes (`10-29_17-16-19, 10-29_17-19-14; triage: low-INT reply of DLG 1696`) | Log: the conversation of script 1696 ends `end=ClosedByHost turns=1 effects=0`; the earlier one (script 1324) ends `ReturnToScript turns=7`. Whether the original shows a low-intelligence variant or other replies is unverified. | none of MIG1-MIG16 (MIG8 only if the reply condition reads dialog state, GR7) | Stay | Yes: read DLG 1696's reply conditions in the executable, then the dialog host | SM16 |
| B18 | The object description at the plane differs from the original (`10-29_17-16-19; triage: examine G2`) | Docs: the examine script (point 0) is slice 9 of the script audit and not done; the report does not say which line differs. | script-trigger-audit slice 9; none of MIG | Stay | Yes (slice 9) | SM16 |
| B19 | Town NPC dialogs do not work (`10-29_21-07-37 (second part); log of 10-29_21-17-14`) | Log: `conversation status=EntryNotAdmitted detail=script 1934: NotModelled (get area on the start map, GA3)` and `script 1817 ... ActionNotAdmitted type 29`. GA3 (the town map of the location's sector) is not modelled. | script slice 2 (get area, get reaction); MIG8 for attachments only | Stay | Yes | SM16 |
| B20 | The ghost of Brego in the cave gives no dialog (`10-29_21-48-14`) | Log: `enter TileEntry Outcome=ScriptRefused script 2768 SCR line 6: FocusKindNotAdmitted type 18`. | script slice 8 (tile commands); focus kind 18 is in no listed slice; MIG9 only for the NPC-entered case | Stay | Yes | SM16 |
| B21 | The entrance to the elves' town does not react to a click (`10-29_21-26-10`) | Log: `use ScriptEnded ... script 1643 ReturnSkipDefault at SCR line 2: no teleport`. The script refuses by a condition the reporter suspects is a story condition (the guard's talk not done). Probably the original's behaviour; unverified. | none | Stay | Verify first (condition of script 1643 in the executable); likely not a bug | SM16 |
| B22 | The sector script 3036 (town-map unfog) is refused at every entry of the wreck sector (`PR #551 note (ARC-SCRIPT-046); log of 10-31_12-49-43`) | Log: `enter SectorEntry ... script 3036 ... ActionNotAdmitted type 113` (unfog town map; needs a town-map knowledge state and a schema step). The PR #551 note records it as an own slice that was not done. | script slice (unfog); MIG9 sector entry (RL6) | Stay | Yes | SM16 |
| B23 | Searching trash cans in the town gives no reaction (`10-29_21-16-23`) | Log: `enter TileEntry ... script 30286 SCR line 2: ActionNotAdmitted type 33` (action 33 is give gold in the audit census) near the cells; unverified that this tile is the trash can's. The click left `portal use ... result=Locked` lines and no container use line. | a use-set or tile-command refusal (script slices 7, 8); none of MIG | Stay | Yes, after the click is traced | SM16 |
| B24 | The "summon the spirit of the dead" spell on a corpse gives no spirit dialog or lines (`10-29_18-04-32`) | Log: the cast (spell 56) starts; nothing after it. Unverified what the spell creates; summons are not modelled (GR31, whose slices are K5 and D3, not a MIG slice). | GR31 (K5, D3); not a MIG slice | Stay | Yes: read the spell, then the spirit's talk | SM26 |
| B25 | Tile scripts of NPC movers never fire (the assassin's agent at the altar; recorded in the audit) (`PR #581 note (ARC-SCRIPT-052) C1; audit slice 3`) | PR #581 note: the agent's heartbeat needs ARC-SCRIPT-051 (C1) and his tile script needs NPC-mover tile scripts (slice 3 of the script audit); location notification is hero-only (GR12). | GR12, GR13; MIG9 plus script slices 3 and C1 | Change | Yes | SM28 |
| B26 | The dialog `XP` action is not wired (recorded) (`docs: experience-runtime "Not ready"`) | Docs only. | none | Stay | Yes | SM16 |

### G6. Interface, messages and art fonts not built or not yet in the original's look

| ID | Bug (source) | Evidence | Gap / migration | Expected | Own fix | Smoke |
| --- | --- | --- | --- | --- | --- | --- |
| B27 | The message panel and hover text use the old font blend (art 27/229) (`triage A2`) | ARC-UI-070 (PR #579) built `AreaArtTextDraw` for the menus and the dialog box (ledger MW31); the message log and hover (art 27/229) are its named next users. | none | Stay | Yes (A2) | SM15 |
| B28 | Combat floaters use the old font (art 229) (`triage A3`) | As B27; ledger CH10: the floaters use font art 229. | none | Stay | Yes (A3) | SM15 |
| B29 | The conversation window uses the old font blend (`triage B1`) | As B27. | none | Stay | Yes (B1) | SM15 |
| B30 | The inventory has no sort buttons and, for a searched NPC, no worn/bag switch (`10-29_21-36-31`) | Log: nothing. Docs: loot window layout is partial; worn tab needs B03. | none (MIG16 changes only the mode stack) | Stay | Yes | SM23 |
| B31 | Items cannot be moved from the inventory to the quick bar (`10-29_21-48-45`) | Log: only `drop kind=PutBack` lines; no hot-key line. Hot-key bar rules exist (ARC-UI-022). | MIG16 only touches the target modes | Stay | Yes | SM23 |
| B32 | A written note cannot be read from the inventory (`10-29_21-50-47`) | Log: only a `drop kind=Wear item=made:21` line. Reading window exists (ARC-UI-049); the inventory use path to it is not shown. | MIG16 target modes | Stay | Yes | SM23 |
| B33 | The context menu of a companion opens on the left button, not the right as in the original (`10-31_12-42-01`) | No log. Needs the executable: companion click handling (companion orders, SA rows). | MIG13 rebuilds the click path (GS25-GS27) but does not decide the button mapping | Stay | Verify in the executable first | SM24 |
| B34 | No message at a successful save (`10-31_12-53-43`) | Log: `quick-save Saved=true`; no message line follows. Whether the original posts a message is unverified. | none | Stay | Verify first, then yes | SM14 |
| B35 | No message at a successful load (`10-31_12-54-07`) | As B34. | none | Stay | Verify first, then yes | SM14 |

### G7. Hosts not wired or unverified: combat, items, audio, magic

| ID | Bug (source) | Evidence | Gap / migration | Expected | Own fix | Smoke |
| --- | --- | --- | --- | --- | --- | --- |
| B36 | Boots cannot be worn (`10-29_18-02-58`) | Log of 10-29_21-09-10: `drop kind=Wear item=made:21 ... slot=Boots` was accepted in a later session, so the report may be build-specific; unverified on current main. | none | Stay | Re-check on main first | SM23 |
| B37 | The Control Box has no hit points (`10-29_21-44-16`) | No log. The object's damage record is `object.damage` under the home map (GR5); the original gives the box hit points for its quest. Unverified whether the prototype has them. | GR5; MIG7 moves the key to the entry | Stay | Yes (read the prototype first) | SM25 |
| B38 | No sound when the hero takes damage; no sound for the hero's spell (`10-29_17-46-09, 10-31_12-47-56`) | Docs: the cue rules exist (ARC-WORLD-041: hit, miss, critical) and the viewer plays positional sounds (ARC-UI-042). Log: casts and hits are logged but no sound line follows; the host path for the hero's damage and the spell sound is unverified. | none | Stay | Yes: find the missing host path | SM22 |
| B39 | No experience after killing an NPC; the experience bar does not move (`10-31_12-42-50`) | Docs claim hits and kills give experience since ARC-COMBAT-005; the host is partial and tables come with `--hero` only. The kill here was by a spell. Unverified which path misses. | none | Stay | Yes, after the path is found | SM21 |
| B40 | The Crash Site chest is empty for a custom hero (contents should be generated) (`10-31_12-43-44`) | Log: `container open ... generation=NoChange/Unsupported`. The one-time generation (`GenerateContainerOnce`) is Unsupported for this holder. The owner suspects the hero's name feeds the roll. | none (GR11 is the seed, not this) | Stay | Yes: read 0x463E20 for the Unsupported case | SM20 |
| B41 | The spell menu does not open and quick-bar casting does not work (`10-29_21-20-33`) | Log: only script lines. The spell pane shows colleges the hero has a rank in (UI-053); a hero without spells has no pane. Unverified: hero had spells or not. | none | Stay | Re-check on main first | SM23 |
| B42 | No damage floaters for spell, trap and script damage (recorded) (`PR #478 note (ARC-UI-021)`) | The ARC-UI-021 note lists spell, trap and script damage floaters as not done. | none | Stay | Yes | SM27 |

### G8. Pathing, passability and render order

| ID | Bug (source) | Evidence | Gap / migration | Expected | Own fix | Smoke |
| --- | --- | --- | --- | --- | --- | --- |
| B43 | Clicking a barrel to search it routes the hero away through the wall texture before he reaches the barrel; fine when he stands next to it (five reports) (`10-31_12-55-03, 12-55-56, 12-59-21, 13-00-50, 13-02-37`) | Reports are from main a156e14; ARC-WORLD-121 (#585, merged 2026-10-31, after the reports) fixed the cave route approach selection. The reporter's hypothesis is that rat cells show as blocked at entry (mobile objects blocking). | GR8 mobile-object blocking; MIG9 possible | Re-verify | No, unless the re-check fails | SM17 |
| B44 | The cave's exit has room to walk out beyond the walls; the passability map needs a check against the original (`10-31_13-01-22`) | Docs: passability is built from the original data (area-passability); the report suggests a mismatch near the exit. | none | Stay | Yes: research against the executable | SM17 |
| B45 | The hero stays dark as indoors for too long after jumping from a window (`10-29_21-15-08`) | Log: lighting lines only. The wall-fade/indoor test rule (scene requirements, wall fade and indoor shading) is the likely owner. | none | Stay | Yes | SM18 |
| B46 | In the town, the fire of a fireplace is hidden behind its background sprite (`10-29_21-17-14`) | Draw order of animated scenery against its background sprite (animated scenery frames, ARC-RENDER). | none | Stay | Yes | SM19 |

## Smoke plan

### Conventions

- Runs go through `./arcanum smoke pr --stages STAGE[,STAGE...] [--map NAME] [--hero H] [--sector X,Y]` and `./arcanum smoke full`
  ([smoke runner](../workflow/toolchain.md#smoke-runner-arc-dev-061), [smoke scope](../workflow/tasks.md#smoke-scope)).
  A new stage that cannot run together with `map` is added to `ALONE_STAGES` (it then runs on its own when `map` is also requested); every stage prints one `PASS` marker line and fails the
  smoke when it is skipped (ARC-DEV-063).
- A **new** stage needs: its name in the viewer's `MapSmokeStageNames`, its marker in `STAGE_PASS` of `tools/local_dev/smoke.py`
  (and in `ALONE_STAGES`, `HERO_STAGES` and, for a stage that debug-travels such as SM01, SM02 and SM05, `DEBUG_STAGES`, which is what starts the viewer with `--debug-mode`), the literal marker in the C# source (the test checks it), and a line in the
  viewer docs. A marker usually reads `ARC-<task> native-smoke <stage> PASS` (the existing ones vary, for example `wreck smoke PASS`). The stages here take no marker from this plan; the PR
  that builds one names it.
- Smokes use pre-made heroes (`--hero`); a bug that depends on a custom hero (B40, the hero's name in a roll) is also an
  **owner re-check by hand** on a save of that hero.
- A stage is added by the PR of the slice expected to fix its bug; it is never added only to show a failing state. Before the
  slice, the evidence is the report. The two read-only stages SM07 and SM17 are the exception and show the state now.
- The pass conditions below follow the original's behaviour where a bug says "as the original"; each such condition is fixed by
  the executable reading named in the row, not by this plan.

### Stages

#### SM01 `follower-far`

- **Bugs:** B05. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages follower-far` (starts the viewer with `--debug-mode` for the travel step).
- **Steps:** 1. Let Virgil join (the `transfer` stage's setup). 2. Debug-travel the hero to the town window (sector 1409,1314). 3. Quick save, quick load. 4. Run 10 s of game time.
- **Pass:** Virgil is in the party list, has a drawn body, stands within 6 cells of the hero, and the log has no `visitor not talkable ... no body kept this run` line, no `InventoryRefused` line and `companions bound` lists him.
- **Run after:** MIG9 (the existing `transfer` and `ai-follower` cover the carried case after MIG2-MIG5).

#### SM02 `inv-carry`

- **Bugs:** B01, B02. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` (Crash Site, then the town window), then `shrouded hills-panarii temple lev 1`. **Command:** `--stages inv-carry`.
- **Steps:** 1. Take the passport and the matches from the dead gnome, wear the boots. 2. Record each item's prototype, count and place. 3. Debug-travel to the town (window rebuild). 4. Quick save and load. 5. Enter the temple map and come back.
- **Pass:** After every step the multiset of (prototype, count, place) is equal to step 2; the inventory window opens; no `InventoryRefused`; worn slots unchanged.
- **Run after:** MIG5 (seed by resolver, MIG1 is done), final after MIG6 (one id space).

#### SM03 `loot-worn`

- **Bugs:** B03. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages loot-worn`.
- **Steps:** 1. Open the dead Kite Shaman. 2. Switch between the worn and bag views (after B30 is built). 3. Take all.
- **Pass:** The NPC holds the dagger its map row lists (the resolver's row, not the window's seed); Take all moves it to the hero; the loot window empties.
- **Run after:** MIG6 (items as objects with a parent); B30's UI first.

#### SM04 `junk` (extend)

- **Bugs:** B04. **Kind:** extend, existing, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages junk`.
- **Steps:** Add: empty a ground pile by Take all, leave the sector and return. The chain runs `junk` on `elven ruins` with `elf-female` because a pile could not be broken at the Crash Site with the other heroes; use that map unless a breakable pile exists at the Crash Site.
- **Pass:** The emptied pile is gone from the directory and from the draw; no empty pile after the return.
- **Run after:** MIG6.

#### SM05 `npc-resident`

- **Bugs:** B06. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed`, `--sector 1409,1314` (town). **Command:** `--stages npc-resident`.
- **Steps:** 1. Record 6 guard and civilian cells. 2. Wait 90 s game time. 3. Debug-travel two windows away and back. 4. Quick save, quick load.
- **Pass:** At least one NPC with a waypoint moved at least 3 cells in step 2; NPCs outside the window kept moving in step 3 (cells differ from their first cells and from their step 2 cells); after step 4 every NPC is on its saved cell.
- **Run after:** MIG7 (positions as state), MIG9 (patrol, far NPCs).

#### SM06 `ai-react`

- **Bugs:** B08. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site, wolves. **Command:** `--stages ai-react`.
- **Steps:** 1. Cast a damage spell at a wolf from the longest legal range, then, if the range allows, from beyond the 30-tile near radius of the heartbeat (RL16). 2. Wait 20 s of game time.
- **Pass:** The target turns hostile to the hero and leaves its place within 20 s, as the executable's reaction says (the pass rule is fixed by the executable reading, B08).
- **Run after:** after the executable reading; then MIG9, MIG13.

#### SM07 `placement-census`

- **Bugs:** B07. **Kind:** new, ALONE, hero stage (a quick load needs a session). **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed`, `--sector 1452,1289`. **Command:** `--stages placement-census`.
- **Steps:** 1. List the map rows of placed NPCs within 10 sectors by prototype and cell with the Data reader. 2. List the AI host's NPCs after the map opens and after a quick load.
- **Pass:** Every row appears in the host once, on its row cell, before any run moved it; the boar rows near the wreck are present. Differences are printed as a table.
- **Run after:** now (read-only), again after MIG9.

#### SM08 `cast` (extend)

- **Bugs:** B12. **Kind:** extend, existing, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages cast`.
- **Steps:** Add: a multi-target damage spell on 3 wolves, one of which has moved between the cast and the hit.
- **Pass:** One effect per hit target, each at the target's cell at the hit, none at its first cell.
- **Run after:** MIG13 (missiles as runs); again after MIG14.

#### SM09 `hold-hero`

- **Bugs:** B11. **Kind:** new, ALONE, hero stage (on the `knockdown` harness). **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages hold-hero`.
- **Steps:** 1. Apply the hold effect to the hero. 2. Issue a walk and a run click.
- **Pass:** The hero's cell does not change while the effect lasts; the click is dropped (the original's rule is read first, B11); after the effect the walk works.
- **Run after:** MIG13.

#### SM10 `door-close`

- **Bugs:** B15. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed`, `--sector 1409,1314`. **Command:** `--stages door-close`.
- **Steps:** 1. Open a door. 2. Wait 3 s of animation time. 3. Open the Esc menu for 5 s and close it.
- **Pass:** The door closes by itself at the original's interval unless the portal flag says it stays; the Esc pause stops the timer.
- **Run after:** MIG13 (needs MIG12 for the pause).

#### SM11 `critter-fall`

- **Bugs:** B13. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed`, wolves at the Crash Site. **Command:** `--stages critter-fall`.
- **Steps:** 1. Kill a running wolf by a spell. 2. Sample the body's draw cell for 3 s.
- **Pass:** The body, the pool and the session cell are one cell; the draw position does not move after the fall begins.
- **Run after:** MIG13 (dying as a run).

#### SM12 `grenade`

- **Bugs:** B14. **Kind:** new, ALONE, hero stage (beside `ranged`). **Hero:** `human-male`.
- **Map and start:** `crash site cave` or the Crash Site. **Command:** `--stages grenade`.
- **Steps:** 1. Give the hero a grenade. 2. Use it on a ground cell next to two NPCs.
- **Pass:** The item is used up, the explosion effect plays, both NPCs take damage as the item's record says, nothing is left to pick up.
- **Run after:** own fix first; recheck after MIG13.

#### SM13 `loading-screen`

- **Bugs:** B16. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` to `crash site cave` and back. **Command:** `--stages loading-screen`.
- **Steps:** Use the cave entrance and the exit.
- **Pass:** On each change the world is not drawn for at least one frame before the new map; the overlay and the clock animation show; the arrival fade runs.
- **Run after:** after MIG11 and the presentation task.

#### SM14 `save-load-msg`

- **Bugs:** B34, B35. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages save-load-msg`.
- **Steps:** Quick save, then quick load.
- **Pass:** The message log gets the original's lines, if the executable posts them (B34 first); the newest entry is the save or load line.
- **Run after:** independent of MIG.

#### SM15 `dialog-art` and extends of `hover`, `floaters`

- **Bugs:** B27, B28, B29. **Kind:** new ALONE (`dialog-art`), two extends. **Hero:** `human-male` (`dialog-art`); the `hover` and `floaters` heroes as they are.
- **Map and start:** `Arcanum1-024-fixed`; `bates mansion lev 2` for `floaters`. **Command:** `--stages dialog-art`, `--stages hover`, `--stages floaters`.
- **Steps:** Open a talk; read back the text pixels of the conversation window, the message panel and a floater.
- **Pass:** Worst difference to the `AreaArtTextRules` reference is 2 levels or less, as the menu stage does.
- **Run after:** after the A2, A3 and B1 slices; independent of MIG.

#### SM16 `scripts-listed`

- **Bugs:** B17, B18, B19, B20, B21, B22, B23, B26. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed`: wreck sector (1451,1289), town (1409,1314), elves' town (1545,656); `crash site cave`. **Command:** `--stages scripts-listed`.
- **Steps:** Table-driven: for each listed row (the dialog 1696 talk, the plane's examine, the town talks, the cave tile, the elves' gate, sector 3036, the trash can) run the dispatch.
- **Pass:** Each row prints Ran, or Refused with the reason the original has; no `NotAdmitted` or `NotModelled` for a row marked as expected to run.
- **Run after:** after each script slice; once more after MIG8 and MIG9.

#### SM17 `route-barrel`

- **Bugs:** B43, B44. **Kind:** new, ALONE, hero stage (beside `walk`). **Hero:** `human-male`.
- **Map and start:** `crash site cave`. **Command:** `--stages route-barrel`.
- **Steps:** From 8 start cells (the report cells) click-search each of 3 barrels; also place the hero at the exit.
- **Pass:** Each route ends beside the barrel using only cells the map allows; the search opens; the path is at most the shortest route plus 2 cells; no cell outside the walls is reachable at the exit (B44 compares with the original's map).
- **Run after:** now on main (after #585), again after MIG9 and MIG13.

#### SM18 `indoor-fade`

- **Bugs:** B45. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed`, `--sector 1409,1314`. **Command:** `--stages indoor-fade`.
- **Steps:** Jump from a building window and sample the hero's shade per frame.
- **Pass:** The shade returns to outdoor within the number of frames the original's rule gives once the hero crosses the wall plane (fixed by reading the rule, B45).
- **Run after:** independent of MIG.

#### SM19 `fire-order`

- **Bugs:** B46. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed`, `--sector 1410,1315`. **Command:** `--stages fire-order`.
- **Steps:** Read back the pixels of the fireplace's fire over its background.
- **Pass:** The fire's brightest pixels are visible over the background sprite on every animation frame.
- **Run after:** independent of MIG.

#### SM20 `container` (extend)

- **Bugs:** B40. **Kind:** extend, existing, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages container`.
- **Steps:** Add: first open of the Crash Site chest.
- **Pass:** `generation=Changed` (not `NoChange/Unsupported`); contents equal the roll the executable gives for a pre-made hero. A custom hero is an owner re-check by hand (smokes use pre-made heroes only).
- **Run after:** independent of MIG.

#### SM21 `xp-kill`

- **Bugs:** B39. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages xp-kill`.
- **Steps:** Kill a wolf by melee, a second by a spell, a third by Virgil.
- **Pass:** XP rises by the kill award each time (the hero's by Virgil's too); the sheet and the bar change; a second kill of the same body gives nothing.
- **Run after:** independent of MIG.

#### SM22 `sfx-hit`

- **Bugs:** B38. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages sfx-hit`.
- **Steps:** Take a wolf's bite; cast a spell.
- **Pass:** The log shows the sound ids the executable plays for the hit and for the spell.
- **Run after:** independent of MIG.

#### SM23 `inv-ui`

- **Bugs:** B30, B31, B32, B36, B41. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages inv-ui`.
- **Steps:** 1. Wear boots. 2. Drag an item and a spell to the quick bar and press the key. 3. Use a note. 4. Click the sort buttons and, on a looted NPC, the worn/bag switch. 5. Open the spell pane with and without spells.
- **Pass:** Boots worn; the key uses the item and casts the spell; the reading window opens; the sort order changes; the switch shows the two lists; the pane shows only colleges with a rank.
- **Run after:** after the UI slices; again after MIG16.
- **State (ARC-DEV-111, 2026-11-05):** the stage `inv-ui` exists and runs the interface-stack part (window keys replace each other, Escape and the menu over windows and a target, the number row over a window); the item steps above are not in it yet.

#### SM24 `companion-click`

- **Bugs:** B33. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site, Virgil joined. **Command:** `--stages companion-click`.
- **Steps:** Left click and right click Virgil.
- **Pass:** The menu opens on the button the executable says (B33 first); the other button does what the original's does.
- **Run after:** after the executable reading; again after MIG13.

#### SM25 `object-hp`

- **Bugs:** B37. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `shrouded hills-panarii temple lev 1`. **Command:** `--stages object-hp`.
- **Steps:** Hit the Control Box, quick save, quick load, hit it to zero.
- **Pass:** The box has hit points from its prototype; damage survives the load; at zero it is destroyed and its script point runs.
- **Run after:** after MIG7 (state on the entry).

#### SM26 `summon-spirit`

- **Bugs:** B24. **Kind:** new, ALONE, hero stage. **Hero:** `human-male` with the college (debug grant).
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages summon-spirit`.
- **Steps:** Cast the spirit summon on a corpse; talk to the result.
- **Pass:** A summoned object exists in the directory with a summon link; its talk opens as the executable says.
- **Run after:** after MIG6 and MIG8.

#### SM27 `floaters` (extend)

- **Bugs:** B42. **Kind:** extend, existing, hero stage. **Hero:** `human-male`.
- **Map and start:** `bates mansion lev 2`. **Command:** `--stages floaters`.
- **Steps:** Add: a spell hit, a trap hit and a script hit.
- **Pass:** A floater with the right text for each, as the executable's order says.
- **Run after:** independent of MIG.

#### SM28 `altar` (extend)

- **Bugs:** B25. **Kind:** extend, existing, ALONE (not a hero stage). **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed`, `--sector 1452,1291`. **Command:** `--stages altar`.
- **Steps:** Add: the agent's heartbeat runs him to the altar tile; he steps on it.
- **Pass:** The tile script fires for the NPC; the agent is destroyed at the tile (the original chain).
- **Run after:** after SCRIPT-051 and slice 3, with MIG9.

#### SM29 `follower-home`

- **Bugs:** B09. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` then `crash site cave`. **Command:** `--stages follower-home`.
- **Steps:** Take Virgil into the cave, dismiss him, leave the cave, wait for the map open and for 3 s of sector absence.
- **Pass:** He stands on his home map's waypoint through his teleport map.
- **Run after:** MIG11. **Built by ARC-DEV-116** as the stage `follower-home` (named only): the transfer stage's carry of Virgil into the cave, then his release and the forced pass driven by the host's own steps (no 3 s wait), a per-frame ping; `./arcanum smoke pr --stages follower-home`.

#### SM30 `save-resume`

- **Bugs:** B10. **Kind:** new, ALONE, hero stage. **Hero:** `human-male`.
- **Map and start:** `Arcanum1-024-fixed` at the Crash Site. **Command:** `--stages save-resume`.
- **Steps:** 1. Cast a maintained spell. 2. Let a wolf run to the hero. 3. Quick save mid-run, quick load.
- **Pass:** The spell is still maintained; the wolf is on its saved cell and continues its run.
- **Run after:** first half after MIG7, second after MIG14.

### Schedule by migration step

| When | Stages | What it checks |
| --- | --- | --- |
| Now (read-only, no migration) | SM07, SM17 | State before the migrations: the census of placed NPCs and the barrel routes on main (after #585). Both only read; the coordinator decides whether the hold covers adding them. |
| Independent of every MIG (own fixes) | SM14, SM15, SM18, SM19, SM20, SM21, SM22, SM27, SM16 | Run when the slice that fixes the bug lands. SM16 is re-run after each script slice. |
| After MIG1 (done, ARC-DEV-072) and MIG2-MIG5 | existing `transfer`, `ai-follower`; SM02 (first half) after MIG5 | Carried companion, seed by resolver. MIG1 alone moves no consumer, so the checks wait for MIG2-MIG5. |
| After MIG6 | SM02 (final), SM03, SM04, SM26 | One id space, one create and destroy path. |
| After MIG7 | SM05 (save part), SM25, SM30 (first half), SM11 (cells as state) | Positions and object state on the entry. |
| After MIG8 | SM16 again, SM26 | Script attachments on the object, global and PC variables. |
| After MIG9 | SM01, SM05, SM06, SM07, SM16 (again), SM17, SM28 | Residency, heartbeat everywhere, location notification. |
| After MIG10 (done, ARC-DEV-073) | existing `transfer`, `ai-follower` | No new bug maps to MIG10 alone; keep its existing stages. |
| After MIG11 | SM29, SM13 | Change-of-place request and homecoming. |
| After MIG12 (done, ARC-DEV-128: [re-check](open-bugs-resmoke-2026-11-04.md#re-check-after-mig12-arc-dev-128-2026-11-05), no row moved) | none of the bug stages | Clocks and the pause; the pause part of SM10 waits for the door runs of MIG13. |
| After MIG13 | SM06, SM08, SM09, SM10 (with the pause part), SM11, SM12, SM17 (again), SM24 | Goal table, hero input through requests. |
| After MIG14 | SM08, SM30 (second half) | Saved runs, spell runs and missiles. |
| After MIG15 | SM01, SM05, SM17 and the stages of the groups whose hosts moved | Hosts move. The daily chain (`./arcanum daily`) runs the full map battery but none of the new stages, so they are named explicitly. |
| After MIG16 | SM23 | Interface mode stack. |

Each migration PR also runs the stages of the slice it touches by the smoke-scope rule, and the daily package build
runs the full chain; the plan adds the bug stages to those, not instead of them.

## What is not covered

- **Unverified causes.** B01, B07, B08, B11, B12, B13, B14, B21, B24, B36, B37, B38, B39, B40, B41 and B43 have a hypothesis, not a finding.
  Their "Expected" column is therefore the plan's best reading and may move after the first reproduction.
- **Needs the executable first:** B03, B08, B11, B14, B17, B21, B33, B34, B35, B40, B44, B45. No fix and no final smoke pass rule is set for them here.
- **Custom-hero bugs** (B40) cannot be reproduced by a pre-made-hero smoke alone.
- **The triage file** was not available (see Sources); A2, A3 and B1 are as the project notes describe them.
- **No bug from the archived reports** is listed again, except B22, which the PR #551 note records as an own slice that was not done.
