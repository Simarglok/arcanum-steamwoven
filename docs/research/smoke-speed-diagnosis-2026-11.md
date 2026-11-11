# Smoke speed diagnosis (ARC-DEV-384, 2026-11-11)

Measured diagnosis of `./arcanum smoke`: how long each configuration takes, where the time and the waiting go, and which
optimizations are worth their cost, including the owner's option of coverage zones chosen per task with the full run kept for
wide or unpredictable changes. It changes no code and is not a budget; every item below is a candidate that becomes its own
task through the coordinator, and gains marked "estimate" are projections, not measurements. The load of the viewer itself is
[load time and window responsiveness](performance-diagnosis-2026-11.md); this report does not repeat it.

## Scope and method

- **Machine.** One Apple M4 Pro Mac, Debug source viewer, Godot 4.7.2 .NET, original data of the GOG 1.0.7.4 installation,
  base `origin/main` of 2026-11-11 (`8830133d5`, later `e82bb0bba`).
- **Queue history.** The launch queue keeps `history.jsonl` (the last 5,000 tickets, enqueue, start and end time, kind, label,
  checkout). 4,971 runs of 2026-11-07 17:49 to 2026-11-11 15:29, about 140 checkouts (threads). The history stores only the
  first 400 characters of a command, so the stage list of a run (`ARC_AREA_MAP_SMOKE_ONLY`) is **not** in it: stage-level facts come
  from the own runs below, not from the history.
- **Own runs.** The baseline, the whole map battery, the Crash Site smoke, both self-tests and about 65 stages in about 30 runs, each
  through `./arcanum smoke`, with the stage times read from a watcher that stamped each `PASS` line of the log (1 s resolution).
  The Mac was shared with other threads (load average up to 18 earlier in the day, an empty queue at the end); the numbers rank
  causes, they are not contracts.
- **Replay.** The scheduling comparison replays the real arrivals and run lengths on a single slot; it is open-loop (a thread
  does not send its next run later because the last one waited), so it shows the order of magnitude, not an exact gain.

## Result in one table

| What | Measured |
| --- | --- |
| One smoke run of one cheap stage on the main map | 10–25 s of which about 10 s is the map load and the rest start and exit; the stage itself 0–2 s |
| The whole `map` battery on the main map (about 80 sub-smokes) | 25 s (it stops at `ranged`, a known failure on main, after the 25 s) |
| Slowest stages | `container-click` (town) 675 s, `walk` 525 s, the Crash Site smoke 98 s; every other measured stage 0–16 s |
| Self-test | 24 s with a window, 12 s `--headless` |
| Build before a smoke | cold (new checkout) 45 s here, median 21 s over 147 first builds; later builds median 12 s, p90 31 s |
| Project import (new checkout) | 2.5 s of work, **median 42 s and mean about 250 s of waiting** for the Godot slot (63 imports) |
| One `./arcanum smoke` call, enqueue of the build to the verdict | median **351 s**, p75 821 s, p90 1,726 s (421 calls); the runs themselves 84 s median; 42 % of calls ended in FAIL |
| Waiting against running (all Godot kinds, 4 days) | 62–64 person-hours waiting for 16–17 hours of slot use (the length table below counts 64 h and 16.7 h, the replay population 62 h; a few runs of difference between two snapshots of the history) |

So a smoke call spends about three quarters of its time waiting, not running; a run is mostly its own start (map load), not its
stages; and two stages plus a few long runs make most of the slot time that others wait behind.

## Where the time goes

### 1. The single slot and the queue

Every Godot run (window, headless, export) takes the one FIFO slot ([launch queue](../workflow/toolchain.md#launch-queue-arc-dev-033)).
Arrivals are bursty (up to 7 waiting at once in the morning measurements; 21 and 27 person-hours of waiting on 2026-11-10 and 11
against 2.8 and 4.5 hours of running).

| Group (4 days) | Runs | Run median / p90 | Slot hours | Wait median / p90 | Wait hours |
| --- | ---: | ---: | ---: | ---: | ---: |
| `smoke stages` (window) | 1,114 | 22 s / 47 s | 12.5 | 22 s / 469 s | 44.9 |
| `smoke self-test` (window / headless) | 141 / 18 | 22 s / 10 s | 1.2 | 37 s / 64 s | 6.5 |
| `smoke crash-site` | 35 | 68 s / 90 s | 0.7 | 23 s / 471 s | 1.2 |
| `smoke import` (headless) | 63 | 3 s / 4 s | 0.1 | 42 s / 720 s | 4.3 |
| `smoke build` (dotnet, 2 slots) | 876 | 12 s / 34 s | 4.0 | 0 s / 70 s | 3.9 |

Slot time and waiting are concentrated in the long runs:

| Run length | Runs | Slot hours (share) | Waiting caused behind it (hours) |
| --- | ---: | ---: | ---: |
| under 15 s | 395 | 1.1 (7 %) | 2.2 |
| 15–30 s | 734 | 4.3 (26 %) | 9.7 |
| 30–60 s | 324 | 3.8 (23 %) | 10.2 |
| 60–120 s | 86 | 1.9 (11 %) | 6.2 |
| 120–300 s | 10 | 0.4 (2 %) | 1.2 |
| over 300 s | **30 (2 %)** | **5.2 (31 %)** | **34.5 (54 %)** |

FIFO is the worst order for such a mix. Replaying the same arrivals and the same run lengths with another order of the single slot:

| Order | Mean wait | p90 wait | Total waiting |
| --- | ---: | ---: | ---: |
| FIFO (today) | 142 s | 483 s | 62 h |
| Shortest first with exact lengths (bound, not achievable) | 70 s | 181 s | 31 h |
| Two classes, runs under 60 s first | 78 s | 215 s | 35 h |
| Short first, a long run counts as arrived 10 min later (starvation guard) | 86 s | 260 s | 38 h |

An import waits as long as any window run, although it is a 2.5 s project import of its own checkout; a self-test `--headless`
is 12 s of work behind the same queue.

### 2. A run is mostly its start

A smoke is a process: Godot starts, the viewer loads the whole main map (about 10 s: map plan, transitions 1.8 s, strict
snapshot, frame copy; `ARC-WORLD-031 load stage=transitions ... cached=False ms=1821` is computed again in every new process: O5b of the
[load diagnosis](performance-diagnosis-2026-11.md) cached it once per process, so a fresh smoke process never hits that cache), then the stages run one after the other in that process, then it exits. The stages
are cheap once the map is loaded. Examples from the stamped logs (seconds since the previous `PASS`): the `map` battery's ~80
sub-smokes together 14 s of its 25 s run; the nine AI-side stages `opening`, `ai`, `ai-turn-based`, `ai-cast`, `ai-follower`, `premade`, `save-resume`, `ai-react`, `placement-census` 21 s in one process (`opening` 13 of it); `cast-multi` 9,
`tech-craft` 6, `plants` 10, `perf` 16, `debug-travel` + `follower-far` + `inv-carry` 23.

Consequence for zones: running ten cheap stages in one process costs about 10 + 10 s, running them as ten processes costs ten loads.
The history shows 1,129 runs under 30 s: most of the slot time of the short runs is a map load.

### 3. The few stages that are slow

| Stage | Run | What takes the time |
| --- | ---: | --- |
| `container-click` (town sector 1409,1314) | 675 s | clicks every container of the window as the player does and walks to each one in real time |
| `walk` | 525 s | a long walk across the main map by its window route: 72 window moves, each loaded in the background (`LoadMs` 1.5–6 s) |
| Crash Site smoke | 98 s | the pinned exterior plus the cave and back (fails on main today, COMBAT-026) |
| `opening` | 13 s | Virgil's heartbeat talk at the start |
| `perf`, `debug-travel`, `junk` map pile | 16, 16, 8 s | |

`census` (every map of the catalogue in turn) was not run; it is the other stage that cannot be cheap. The history does not store the stage lists of the 30
runs over 300 s, so it cannot show what they ran; this is consistent with them being calls that name `walk` or `container-click` (one such call holds the slot for 9 to 11
minutes), and a `--stages map` run seen at 560 s during the measurements ended in `walk`. At the time of the morning measurements 5 to 7 threads were waiting behind such a run.

### 4. The daily "full" run is not a full run

`./arcanum smoke full` and the daily package build run the Crash Site smoke and the `map` battery (with `resources`) on four
configurations. Of the 76 stage names the viewer accepts (`MapSmokeStageNames` at the measured base), the battery runs `map`, `resources`, nine more
(`cast`, `turn-based`, `knockdown`, `ranged`, `container`, `junk`, `hover`, `report`, `door-close`) and, on the window maps, `transfer`,
`roundtrip`, `stream` and `worldmap` (the viewer runs them whenever no stage list is given): 15 in all. The other 61 (all the AI stages,
`walk`, `census`, `container-click`, `npc-*`, `perf`, the menu stages and so on) run only when a task names them. The "full" run
therefore does not protect them: a wide change can break one of them and the daily run still passes.

### 5. Each stage has its own hidden configuration

A stage needs a map, a sector, a hero and sometimes `--main-menu` or `--debug-mode`; this lives in comments of
`TerrainViewerApp.cs` and in the runner's sets (`HERO_STAGES`, `MENU_STAGES`, `DEBUG_STAGES`, `ALONE_STAGES`), not in one table
that says "this stage runs on this configuration". Own runs hit it: `plants` needs the generated sector 1384,239, `npc-tile` and `altar`
sector 1452,1291, `tile-ghost` and `tech-craft` the cave, `cast-multi` the elven ruins, `scene` another map than the start map,
`inv-carry` a dead body with an item, `critter-light` a carried light. A thread that picks stages by hand rediscovers this by failing.

## Failures seen on clean main while measuring

Not investigated here (the rule is to name them and go on); recorded because a zone runner must know them.

| Stage / run | Failure |
| --- | --- |
| `ranged` and so `map` battery | `ARC-COMBAT-015 ranged smoke FAIL: the dagger is picked up and wielded again` (COMBAT-026 is fixing it) |
| Crash Site smoke | FAIL (exit 1, 98 s), the known one |
| `barter` (main map, Crash Site) | `InvalidOperationException` after `barter-art PASS` (frontier=InvalidOperationException, stage=Upload) |
| `created-critter` | `AI host holds the created critter` FAIL |
| `menu` then `options` (`--main-menu`) | `options smoke FAILED: the menu is open over the start map` (stage order or configuration; `menu` passed, `options` followed it in one process) |
| `scene` (`--main-menu` on the main map) | `the launch map ... is not the start map, and a new game is offered there` (a configuration that needs another map) |
| `inv-carry` (Crash Site), `critter-light` (town), `door-close` (Crash Site) | `NONE`, which the runner turns into FAIL exit 3: the map lacks what the stage needs (`door-close` passed on the town sector) |
| `critter-fall` | FAIL as part of a longer list (`ran over at least two cells before it died (1)`), PASS alone |

## Options

The owner's question is whether to give each part of the game its own coverage zone and run only the needed zones per task, keeping the
full run for wide or unpredictable changes. The data say the cost of a zone is almost all fixed (queue plus one map load), so
what zones buy is: fewer processes (grouping by configuration), keeping the three heavy stages off tasks that cannot affect them,
and a clear place for "which configuration does this stage need". They do not buy much by cutting cheap stages.

### Zone options

**A. Core plus heavy triggers plus a complete run (recommended).**
- `core`: every cheap stage, grouped into about 10 processes by configuration (main map Crash Site with the hero, the AI group, the town,
  1452,1291, the generated sector, the cave, the ruins, the main menu, debug mode, `perf`). Estimate: about 5 min of slot, about 8 min as the separate runs
  measured here. A task runs only the zones its change touches (below).
- Heavy zones, triggered by what they cover: `walk` (window moves, walk and path code, the loader), `container-click` (picking, clicks on
  containers, roofs), `crash-site` (the pinned exterior, the cave, their inputs), `census` (catalogue, map readers).
- `complete`: `core` plus all heavy zones, for wide or unpredictable changes and nightly instead of today's `full` (about 5 min plus
  about 20 min for the two long stages, plus the Crash Site smoke). Its pass line would check every stage, closing the hole in section 4.
- Selection: a table of path prefix to zone, and an unmapped file (or one in `src/Arcanum.Data`, the save schema, shared UI or
  render code) selects `complete`. `./arcanum smoke pr --auto` reads the diff against `origin/main`, prints the zones it chose and why, and the PR report
  names them.

**B. Fine zones per area of the game** (UI, combat, magic, AI, world, render, scripting, input, 8–10 of them, aligned to the FR groups of
`docs/requirements` that the task already names). Most selective and a good fit for the rule that a task names its FR groups; but because
cheap stages cost 0–2 s, it saves little compute over A (a zone is still one load), costs more upkeep (a stage belongs to several areas, the map
needs a table per FR group) and makes "forgot a zone" a new class of escape.

**C. No zones; fix the queue and the two slow stages only** (items Q1 to Q6 below). Cheapest to do; the per-call stage list stays a manual choice and the
section 4 hole stays.

### Independent items (any zone option)

| Item | What | Estimated effect | Size |
| --- | --- | --- | --- |
| Q1 | Queue order: short first with a starvation guard, the class declared by the runner (`self-test`, `import`, single stage, `container-click`/`walk`/Crash Site as long) | total waiting 62 h to about 35–38 h over the 4 days (replay, open-loop) | small, touches `run_queue.py` for every thread |
| Q2 | `smoke import` and the 12 s `self-test --headless` outside the Godot slot, as `dotnet` runs are | imports wait median 42 s, mean about 250 s, 4.3 h in 4 days: to about 0; fresh threads reach the first stage sooner | small |
| Q3 | History and runner record what ran: the label carries the stage list, map, sector (the command is cut at 400 characters), the viewer's `PASS` lines carry elapsed ms, the runner prints a per-stage time table | no direct speed; makes every later claim measurable | small |
| Q4 | A stage-to-configuration table in code (map, sector, hero, menu, debug mode), used by `--stages` to group stages into the fewest processes and to fail early on a mismatch | about 8 s saved per extra process avoided, no more "needs --sector" rediscovery | small to medium |
| Q5 | Known failures on `origin/main`: a small file of stage, symptom, owning task, read by the runner so a failing stage that is listed is reported as "known on main, not this PR" | no hunting and no reruns on clean main (the coordinator's list above is today's manual version) | small |
| Q6 | Make the two slow stages cheaper by default: `container-click` clicks a sample (for example 12 containers across the types) unless `--all`; `walk` stops after N window moves unless `--all` | 675 s to about 60 s, 525 s to about 150 s (estimates, depend on the sample) | medium; changes what a stage vouches for, owner decision |
| Q7 (**done: ARC-DEV-392**, [design](../world/area-loader.md#map-transitions-kept-on-the-device-arc-dev-392)) | A cross-process cache of the map's transitions (O5b of the load diagnosis is done per process only, so every smoke run still computes them: 1.8 s in the logs) | about 1.8 s of the 10 s load of every smoke run | small; needs a stamp so it cannot go stale |
| Q8 | The daily run switches from `full` to `complete` of option A (or to `full` plus the stages the battery lacks) | closes the section 4 hole; costs about 25 minutes of slot a day at night | follows A |

A large item, not recommended now: a warm viewer process that runs several stage groups without restarting (saves the 10 s load per group, but each group
then needs a reset of the session state, and the smokes are written to start from a fresh load).

## Recommendation

1. Do Q1, Q2 and Q3 first: they need no decision about coverage, cut the waiting (three quarters of every call) and make the rest measurable.
2. Take zone option A, with Q4 and Q5 as its base: it matches the owner's idea (zones by task, full for wide changes) and the measurements (the cost is in the
   processes and in two stages, not in the cheap stages), and Q8 closes the hole in the daily run.
3. Decide Q6 separately: it changes what two stages prove.
4. Leave option B for later; its extra granularity can be added to A's table without a new mechanism.

## Not measured

The `census` stage; the full `./arcanum smoke full` chain (the town, wilderness and ruins batteries separately); the history has no daily run in its 4 days;
stage lists of the 30 long runs (not stored); the effect of two concurrent Godot windows (a second slot for windowed runs is not proposed, because smokes drive the pointer and need
their window visible, [launch check](../workflow/toolchain.md#checking-that-a-run-started-arc-dev-029)); a Windows machine; the packaged build.

## Reproducing

The history is `~/Library/Application Support/Arcanum Steamwoven/run-queue/history.jsonl`; a run's own log is in `run-logs` beside it (the last 200). The tables are
sums and medians over its `startedAt`, `endedAt`, `enqueuedAt`, `kind`, `label`. The stage times came from a watcher that polled the newest logs and stamped each `native-smoke ... PASS`
line; with Q3 the viewer prints them itself.
