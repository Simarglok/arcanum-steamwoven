# Full smoke on main, 2026-11-06 (ARC-DEV-162)

Scope: the full chain (`./arcanum daily`), every stage on its own, the self-test, and the Terrain Viewer tests with the
original data, all on `origin/main` at `e36139c9` (merge of #686, which includes #682 to #686), on the owner's Mac
(Godot 4.7.2 .NET, Apple M4 Pro, installed game at the default location). No product code was changed. One smoke
script line was fixed (section 4.1); everything else is classified and proposed as separate tasks.

Requirements: this is a verification run, it changes no functional requirement row.

## 1. Summary

| # | Failure | Kind | Since | Proposed task |
| --- | --- | --- | --- | --- |
| F1 | Map smoke (`main`, `town`, `wild`, `ruins`) fails at the first route click, `AREA_MAP_SMOKE_ROUTE_NOT_SET` | smoke bug (stale expectation) | #680 (first commit `2e9af58e`) | fixed here (4.1) |
| F2 | Combat-mode smoke on `wild` and `ruins`: the hero stops 4 of about 12 cells short after a Ctrl click | probable product bug (walk run ends early) | #680, hidden behind F1 | T1 |
| F3 | `./arcanum smoke self-test`: hover, "the left click set the hovered cell as goal" | smoke or fixture, cause not found | #676 (`1cbec2d6`) | T2 |
| F4 | `self-test`, hover: "the tooltip does not cover the pointer" | smoke or product, hidden behind F3 | #682 (`e2f6963d`) | T2 |
| F5 | Viewer test `In_a_turn_based_round_the_college_npc_casts_in_its_own_subturn_and_pays_4_action_points`: paid 0, expected 4 | stale test measurement (resolved in ARC-DEV-165) | #664 (ARC-DEV-134) | T3, done |
| F6 | Viewer test `A_turn_set_that_interrupts_the_last_run_calls_the_rounds_animation_callback`: 7, expected 8 | stale test expectation (resolved in ARC-DEV-165) | #666 (ARC-DEV-136) | T3, done |
| F7 | Viewer census `Original_buy_lists_and_the_merchants_of_the_maps`: (0, 8), expected (0, 4) | stale test number | #614 or #615 (ARC-DEV-085 / ARC-DEV-083) | T4 |
| F8 | Viewer census `Every_guarded_use_site_and_every_tile_and_sector_script_runs_or_is_refused_by_its_set`: `(TileEntry, NoTeleport)` 1180, expected 823 | stale test numbers | #629 (ARC-DEV-100, script globals) | T4 |

What passes: the root checks, the Crash Site smoke, the `main` and `town` map batteries (after F1), every stage on its
own (section 3), `scene` on a non-start map, and the merchant restock census (it failed on main before #686, it passes
now). 1200 of 1204 viewer tests pass with the original data.

## 2. The run

1. `./arcanum daily --force` on `e36139c9`: `dotnet` (locked restore, build, tests), `dat-probe` and `oracle`
   self-tests PASS (the oracle loaded Unicorn); `smoke` FAIL: build, import and Crash Site PASS, `main`, `town`, `wild`
   and `ruins` FAIL, each in 7 to 11 s, all with `AREA_MAP_SMOKE_ROUTE_NOT_SET`. No package was made.
2. With the F1 fix: `./arcanum smoke full`: `main` PASS (82 s), `town` PASS (126 s), `wild` and `ruins` FAIL in the
   combat-mode smoke (F2). The map battery stops at its first failing stage, so the stages after the combat mode
   (attack, cast, turn-based, ...) did not run on `wild` and `ruins` in the chain; they ran on the main map (section 3).
3. Every stage of the viewer on its own (section 3), then `./arcanum smoke self-test`.
4. The Terrain Viewer test project with the original data (the environment of the opt-in table in
   `docs/workflow/toolchain.md`, paths set in a scratch script outside git): 4 failed, 1200 passed, 0 skipped, 40 min.

Rules followed: `altar` at sector 1452,1291; `plants` at 1384,239; `door-close` and `ai-follower` at 1409,1314;
`floaters`, `opening`, `wreck` alone; `junk` on `elven ruins` with the elf-female hero; Godot windows one at a time
through the launch queue.

## 3. Stages on their own

All of these PASS (exit 0, the stage's own PASS line present): `altar`, `plants`, `door-close`, `ai-follower`,
`floaters`, `opening`, `wreck`, `junk`, `cast`, `turn-based`, `knockdown`, `ranged`, `container`, `barter`, `life`,
`ai`, `ai-turn-based`, `ai-cast`, `premade`, `pickpocket`, `debug-travel`, `report`, `hover`, `dead-window`, `inv-ui`,
`follower-home`, `resources`, `transfer`, `roundtrip`, `stream`, `worldmap`, `walk`, `menu`, `options`, `bar`, `death`.
`scene` PASSES on a map that is not the start map (`crash site cave`); on the start map it refuses by design
(`the launch map is not the start map`), which is not a failure of the smoke.

The "known suspects" from the thread brief:

- **door-close reports NONE** and **junk reports NONE**: only inside the full chain on the main map at the Crash Site
  (no closed door the player can open there; no container with two items). Both PASS on their own at the right place
  (door-close at 1409,1314, junk on `elven ruins` with the elf-female hero). A limit of the chain, which vouches only for
  `map` and `resources` (ARC-DEV-063), not a regression.
- **self-test hover**: fails, F3 and F4.
- **three real-data censuses**: barter and cell scripts fail (F7, F8), the merchant restock census passes after #686.

`NONE`/`SKIP` lines in the main-map battery (map content limits, not failures): blend, light animation, roofs, portal,
door-close, expert-bow, junk-pile, jump, restock substitute container.

## 4. Failures

### 4.1 F1: map smoke, route not set (fixed here)

Symptom: `ARC-RENDER-017 smoke detail click 94,88 goal set route Idle target=none plan=none segments=0`, then
`frontier=AREA_MAP_SMOKE_ROUTE_NOT_SET`, on all four maps, before any other stage.

Bisect (`./arcanum smoke map Arcanum1-024-fixed --hero human-male`): PASS at `c7a5e805` (#678) and `31184f49` (#679),
FAIL at `2e9af58e`, the first commit of #680 (ARC-DEV-149, hero walk nodes on the goal table), and at every later one.

Cause: a smoke bug, not a product bug. Since #680 a ground click starts the hero's walk as a run on the table, which
gives the route its steps while the area clock ticks; `RunAreaMapSmokeAsync` still expected the route follower to hold
the target right after the click and stepped frames without ticking the area clock. #680 adapted the gait, combat-mode,
turn-based and hover smokes but not this stage, and a per-PR run (`--stages` of the touched stages) does not include
`map`.

Fix (this PR, `TerrainViewerAreaMap.cs`): check `HeroWalkGoal == goal.Cell` instead of the route target, and tick the
area clock 0.1 s per frame while the walk run is on the table, as the gait smoke does. After it the `main` and `town`
batteries PASS. (The stage's `AreaSmokeFrame` still counts whole seconds per frame while the clock is ticked in tenths,
as before; the gait and combat smokes use tenths. Not a failure, noted for T1.)

### 4.2 F2: the hero stops short on `wild` and `ruins` (probable product bug)

Symptom (first observed on `e36139c9`): `ARC-COMBAT-004 combat-mode smoke FAIL: arrived: Arrived target=(91,95) ... plan=none segments=0` on `wild`
(main map, sector 1384,239) and `ruins`. `main` and `town` pass.

Evidence (wild): the Ctrl click on a cell about 12 cells away (world 88595,15320, from the start 88607,15327) was
accepted and the walk goal was the clicked cell (`HeroWalkGoal == far` passes). The hero ended at 88603,15327, four
cells along the way; the walk goal then reads that cell, `HeroWalking` is false. A probe of 300 more frames with the
clock ticking changed nothing, the chart object is the same (the window did not move). On `ruins` the same shape:
goal (94,112), stopped at (86,110).

Why it is probably a product bug and not the smoke (an inference, not measured): the check was loosened by #680 itself (it compared the route status before, now
the cell), the gait smoke in the `main` and `town` batteries passes, and a player's click walk on these maps would
likely stop in the same place. It was hidden in the chain behind F1, so it was first observed on `e36139c9`; it was not bisected (the stage cannot
run before F1 is fixed), and "since #680" is the likely start, not a measured one. Not found: why the run ends
after the first given step on these two maps and not on `main` and `town` (generated wilderness and a cave-like map;
what they share is not yet known).

Repro: `./arcanum smoke map Arcanum1-024-fixed --hero human-male --sector 1384,239` (fails in 15 s at
`AREA_COMBAT_MODE_SMOKE_FAILED`) and `./arcanum smoke map "elven ruins" --hero elf-female`.

Limit: with F2 the stages after the combat mode did not run in the chain on `wild` and `ruins`; they are verified on the
main map only.

### 4.3 F3 and F4: self-test hover

`./arcanum smoke self-test` (the synthetic fixture, no original data) FAILS with
`RenderFailure synthetic=InvalidOperationException:AREA hover: ...`.

- F3, "the left click set the hovered cell as goal": PASS at `68aa117a` (#669), `df54d1a6` (#674) and `0c1cf397`
  (#675), FAIL at `1cbec2d6` (#676, ARC-DEV-146, the walk as a run on the table) and at every later commit including
  after #680 changed that line from the route target to `HeroWalkGoal`. The left click on the hovered body does not
  leave a walk goal in the synthetic fixture. Not found whether the walk starter refuses there (the movement gate) or the
  fixture lacks the table tick.
- F4, "the tooltip does not cover the pointer": appears at `e2f6963d` (#682, ARC-DEV-156, hover text in the original's
  font, rounded tooltip position). It was masked by F3 before #682 (the test stops at the first failing check); it
  needs its own look once F3 is fixed, it may be a real overlap or a stale expectation after the font change.

Repro: `./arcanum smoke self-test` (10 s).

### 4.4 F5 and F6: AI cast tests (viewer tests with the original data)

- F5 `AreaAiCastTests.In_a_turn_based_round_the_college_npc_casts_in_its_own_subturn_and_pays_4_action_points`: the
  casting goal's start paid 0 action points, expected 4 (`turn-based cast 51 paid 0 cast release run=0`). PASS at
  `ffa4df41` (#662), FAIL at `3938b9f0` (#664, ARC-DEV-134, the AI host on the session's goal table; `eb2e5890`, #663,
  is the only other merge in between and is a smoke-focus change) and later.
- F6 `AreaAiHostTests.A_turn_set_that_interrupts_the_last_run_calls_the_rounds_animation_callback`: callbacks 7,
  expected 8. PASS at `56602c86` (#665), FAIL at `635a55b7` (#666, ARC-DEV-136, rounds on the table).

Both tests ran in the same viewer test project, and the `ai-turn-based`/`ai-cast` smokes on the main map PASS, so the
play path may be fine and the tests stale, or a college NPC's turn-based cast does not pay its points. Not decided at
the time of the run; decided below (ARC-DEV-165).

**Resolved by ARC-DEV-165: both are stale tests, no product change.**

- F5: the caster does pay the 4 points. A debug trace of the round in this task (original data, Crash Site, numbers only, not
  kept in git) showed the college NPC at 5 action
  points at the start of its second subturn and at 1 after the frame in which its casting goal started and released
  (0x429B50 charges the goal's object 4 points in its own subturn, ledger CG2/EX19/AG3; the host's `StartCast` hands the
  result to the round). The test read the round twice in a row after the frame, so it always saw a difference of 0 (the
  test was written when the AI host pumped inside it, before #664); it now reads the round before the frame and after it.
- F6: the turn set did free the animal's last run (significant goals 1 to 0) and the table did call the round's callback.
  The next participant is the hero, active and with its action points, and 0x4B7080 returns at once for a PC-type object
  with points left (TL39, GT24, read in the executable in ARC-DEV-136), so nothing is counted, fed or requested. The test
  expected the counter to move as for an NPC participant, which the synthetic `AreaAiHostRoundTests` case covers; the real
  data test (renamed `A_turn_set_that_interrupts_the_last_run_frees_it_and_the_callback_keeps_the_heros_subturn`) now asserts
  the free, the hero's kept subturn and that no event was asked for.

### 4.5 F7 and F8: stale census numbers

- F7 barter census: `(own, throughContainer)` is (0, 8), the test says (0, 4).
- F8 tile and sector script census: `(TileEntry, NoTeleport)` is 1180, the test says 823 (the test message lists the other
  counters too, e.g. `(SectorEntry, NoTeleport)` 16 and `(TileEntry, EffectsRejected)` 130, without their expected
  values here; the full counts are in the test output).

Bisect (the two tests only, original data): barter PASS at `9671c53a` (#613), FAIL at `0a1e49d8` (#615), so the
change is #614 (ARC-DEV-085, town seed with an unlisted holder) or #615 (ARC-DEV-083, red smoke stages); more holders
in the seed fits a count that doubled. Cell scripts PASS at `46904175` (#628) and FAIL at `d07ea71d` (#629,
ARC-DEV-100, script globals), so #629 moved the numbers. Both
tests kept failing on every later commit, so no PR since then saw them go red: they run only with the original data.
The merchant restock census failed on main before #686 and passes at `e36139c9`.

## 5. Proposed tasks (one PR each)

- **T1 (product): the hero's click walk ends early on `wild` and `ruins`** (follow-up of ARC-DEV-149). Find why the
  walk run ends after the first given step; keep the combat-mode check as the regression test. Also run the whole
  `wild` and `ruins` batteries after the fix, since the stages after the combat mode have not run there.
- **T2 (smoke): `self-test` hover** (F3, F4). Find why the click leaves no walk goal in the synthetic fixture (a gate
  refusal or a missing tick), then re-check the tooltip position after the font change.
- **T3 (product or test): turn-based college cast and the round callback** (F5, F6) against the rules of ARC-DEV-134
  and ARC-DEV-136. Done in ARC-DEV-165: both were stale tests (section 4.4).
- **T4 (tests): update the barter and cell script census numbers** (F7, F8) with the reason for each change, or find
  the change that moved them if it was not intended.
- **T5 (process, small): per-PR smoke scope.** F1 and F2 show that a change to the hero walk, route or click path has
  to run `--stages map` (or the full chain) and the self-test, not only the stages it touches; and the daily build
  found these only after seven merges (#680 to #686). Suggest naming the click-walk path in the smoke scope rule, and running
  `./arcanum smoke self-test` for every viewer change (it takes 10 s).

## 6. Checks run for this PR

The PR changes one smoke script line and this document. Checks: the commands of section 2 (daily root checks PASS),
`./arcanum smoke full` after the fix, the stages of section 3, `./arcanum smoke self-test` (FAIL, F3/F4), and the
viewer tests with the original data (4 failures, F5 to F8). The independent review of the diff is noted in the PR.
