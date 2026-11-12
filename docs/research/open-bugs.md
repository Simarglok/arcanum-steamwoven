# Open bugs register (Ctrl+B player reports, B01-B55)

Status: **Register**. It replaces the five documents `open-bugs-migration-smoke-plan`, `open-bugs-resmoke-2026-11-04`,
`bug-reports-triage-2026-11-08`, `bug-reports-triage-2026-11-10` and `full-smoke-2026-11-06` (deleted; the history is in git).
B-ids cited in the source, the tests and the docs resolve here. State is as of `origin/main` 2026-11-11. The original
mechanics behind each fix are in [docs/research/ledger](ledger/) under the task codes named in the code comments.

## Still open

None: B47, the last open report, is closed by ARC-MAGIC-053 (below); the owner's in-game comparison is listed in [owner-recheck-list](../project/owner-recheck-list.md).

## Closed with a residual (uncertain: the docs do not say whether the residual is wanted)

| ID | Residual | FR |
| --- | --- | --- |
| B11 | The hero's hold is built (ARC-DEV-302/303/304, stage `hold-hero`). Not exercised: Original mode keeps the steps of a goal 4, 55 or 56 run of an entangled hero (owner decision D8); the stage produces no such run | FR-MAGIC-007 |
| B25 | Tile scripts run for every moved critter except the followers of a same-map teleport, and the arrival sector's script and point 27 for followers (ARC-DEV-351, exe read TG9 in [ledger/scripts.md](ledger/scripts.md)); created critters are done by ARC-DEV-371 | n/r |
| B07 | No defect found (ARC-DEV-308: the data and the stage agree). Reopen only on a new report from a current build taken in the first minutes of a new game Data: 46 NPC rows within ten sectors of (1452,1289): 13 Ailing Wolves, 8 Wolves and 3 Lesser Boars together (within 18 cells, 43-61 cells west of the first report's hero cell), no boar row east of the wreck. The reports' build predates npc.pos and the residency (PR #624, #640, #650, #652), and the stage looks at places before the first tick, not at hours of wandering (the original's wander of these animals was not read) | n/r |

## Closed (id: merged task codes)

Every B-id not in the tables above is closed. A code in brackets was a part of the fix or its regression stage.

- **Object model and residency:** B01 ARC-DEV-314 (ARC-DEV-305, ARC-DEV-315, stage `inv-carry`); B02 ARC-DEV-085, ARC-DEV-155;
  B03 ARC-DEV-202; B04 ARC-DEV-310; B05 ARC-DEV-170 (stage `follower-far`); B06 ARC-DEV-170; B08 ARC-DEV-281 (ARC-DEV-264,
  stage `ai-react`); B09 ARC-DEV-116 (ARC-DEV-155, stage `follower-home`); B10 ARC-DEV-325 (ARC-DEV-291, stage `save-resume`).
- **Goals, spells, projectiles:** B11 ARC-DEV-302, ARC-DEV-303, ARC-DEV-304 (read ARC-DEV-188, ARC-DEV-301, ARC-DEV-331);
  B12 ARC-DEV-327 (ARC-DEV-250, stage `cast-multi`; the rest is the original's rule); B13 ARC-DEV-248 (ARC-DEV-137, stage
  `critter-fall`); B14 ARC-DEV-259 (ARC-DEV-140, ARC-DEV-249, stage `grenade`); B15 ARC-DEV-138 (stage `door-close`).
- **Change of place:** B16 ARC-DEV-204.
- **Scripts and dialogs:** B17 ARC-DEV-171; B18 ARC-DEV-326; B19 ARC-DEV-178; B20 ARC-DEV-298 (the ghost's drawing: B53);
  B21 ARC-DEV-309, ARC-DEV-312 (the refusal is the original's rule, gate flag 2365); B22 ARC-DEV-205; B23 ARC-DEV-157,
  ARC-DEV-299 (stage `container-click`; the 8 roofed house containers are the original's rule, ARC-DEV-330); B24 ARC-DEV-304,
  ARC-DEV-324; B25 ARC-DEV-206, ARC-DEV-298, ARC-DEV-328, ARC-DEV-351, ARC-DEV-371 (created critters; the residual is above); B26 ARC-DEV-178.
- **Interface, messages, fonts:** B27 ARC-DEV-156; B28 ARC-DEV-156; B29 ARC-UI-071; B30 ARC-UI-072, ARC-UI-1121 (the doll's two values and the weapon fill), ARC-UI-1267 (the same in the hero's own window; the sort button is B54);
  B31 ARC-DEV-306; B32 ARC-DEV-306 (ARC-DEV-316, ARC-DEV-336, ARC-DEV-354); B33 ARC-DEV-280 (ARC-DEV-263, stage
  `companion-click`); B34 and B35 ARC-DEV-168.
- **Hosts:** B36 ARC-DEV-305 (ARC-DEV-315); B37 ARC-UI-107; B38 ARC-DEV-296; B39 ARC-DEV-295; B40 ARC-DEV-161; B41 ARC-DEV-306
  (stage `inv-ui`); B42 ARC-DEV-296.
- **Pathing, render:** B43 ARC-WORLD-121 (ARC-DEV-247, stage `route-barrel`); B44 not a bug, the original's behaviour (owner,
  2026-11-07; ARC-DEV-203): a flood fill from the cave spawn (2087,2018) reaches 506 cells, including a ring one cell outside the walls through the corridor's east end (2095,2018) round the end of the north wall; PB1 and PB2 equal the executable, the ring's tiles are open in the original's data, and the owner confirmed that the original lets the hero walk out there too; B45 ARC-DEV-203 (stage `indoor-fade`); B46 gone, no reproduction (ARC-DEV-203, stage `fire-order`).
- **Second triage (B47-B54):** B47 (Fireflash, Wall of Fire) ARC-DEV-352 (stages `summon-flash`, `summon-wall`), (Summon Undead) ARC-MAGIC-053 (stage `summon-undead`); B48
  ARC-DEV-333 (stage `loot-room`); B49 ARC-DEV-334; B50 ARC-DEV-335 (hero), ARC-DEV-353 (companions and NPCs); B51
  ARC-DEV-336 (ARC-DEV-354, fresh load); B52 ARC-DEV-337; B53 ARC-DEV-338 (stage `tile-ghost`); B54 ARC-DEV-339 (stage
  `inv-arrange`).
- **Third report (B55):** B55 (Wall of Fire look and piece count) ARC-MAGIC-054 (a created scenery takes its prototype's blit flags: additive 0x10), ARC-MAGIC-055 (the cast map's tile-blocked test is the terrain verdict); the retail game makes 5 pieces (owner, 2026-11-12), so the count was right and the 3 pieces came from a community build of the game; [note](wall-of-fire-look-magic-054.md).

## Triage work lists and stage ids

The T-slices and F-fixes that the ledger names (for example "triage T6", "F8") were the work lists of the deleted triages; their results are the task codes above.

The smoke stages SM01-SM30 named in code and docs map to the stage names in [area-map-mode](../rendering/area-map-mode.md)
and `docs/workflow/toolchain.md`; the numbers were the plan's and are not reused.
