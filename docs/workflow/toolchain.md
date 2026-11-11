# Toolchain and verification entry points

Configuration is authoritative: [global.json](../../global.json) pins .NET SDK `8.0.425` with roll-forward disabled; [Directory.Build.props](../../Directory.Build.props) targets `net8.0`; [Directory.Packages.props](../../Directory.Packages.props) centralizes package versions. Godot adapters/labs use `Godot.NET.Sdk/4.7.2`, Godot **4.7.2 .NET** and matching .NET export templates. Development currently targets macOS. Version changes require a separate scoped decision; do not replace retained shared installations implicitly.

The player's local HD interface pack (staging build, diff-only update, backup, rollback) has its own page: [HD pack workflow](hd-pack.md) (ARC-DEV-329, `tools/hd_pack/hd_pack.py`; tests `python3 -B -m unittest tools.hd_pack.test_hd_pack`).

## Process-local environment

Use the existing canonical tools. These paths are the established macOS convention, not an installation command. Operational installation/ownership records remain local, outside Git.

```sh
DOTNET_ROOT="$HOME/.local/share/dotnet"
DOTNET="$DOTNET_ROOT/dotnet"
GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot"
godot_canonical() {
  env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT="$DOTNET_ROOT" \
    PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin" "$GODOT" "$@"
}
"$DOTNET" --info
```

The sentinel prevents Godot editor shell startup from replacing the launch PATH. Before accepting an export, inspect the verbose editor build bridge: SDK `8.0.425` and the canonical dotnet executable must be selected, with no Homebrew build/publish path. A direct dotnet build alone does not prove editor/export selection. Use the component's export recipe, including its fresh build/import and exact-candidate evidence requirements.

For Gamepad, Display and the bounded Terrain endpoint, configure the ignored artifact root once with `tools/arcanum-local configure add-artifact-root --id ID PATH`, then export with `ARCANUM_LOCAL_ROOT_ID=ID`. Their validation commands acquire the exact candidate's `.export-lease.lock` before export and retain it through every successful gate, v2 manifest/evidence inventory validation, local registry mutation, and v2 current-pointer update. Do not call `builds register-export` as a substitute for an endpoint; that command validates an already complete candidate but cannot retroactively cover an export that ran without the lease. A failed validation does not register or select an artifact.

The terrain endpoint exports the viewer with the release template (`--export-release "macOS"`, ExportRelease assemblies). `Directory.Build.props` turns on compiler optimization for every project built in ExportRelease, because the plain SDK optimizes only `Release` and Godot would otherwise ship the referenced Data/Core/Simulation assemblies unoptimized. Source runs and the labs (`ExportDebug`) keep Debug assemblies.

Supply effective `TMPDIR` explicitly for these endpoints. Preflight proves export/temp/workspace/NuGet/Godot-cache destinations and checks the full budget per distinct device; it does not measure the configuration volume as a proxy. Output comes from a verified accepted/current payload plus 10% or `ARCANUM_OUTPUT_BYTES` and `ARCANUM_ESTIMATE_MANIFEST` for ONE complete configured payload, never aggregate history/source `du`. Validation exports/tests/codesigns without writing a manifest; the endpoint freezes baseline/periodic/final capacity observation, then invokes only the manifest writer. Temporary cache attribution remains unavailable unless all temporary writes are proved isolated; a fallback/history estimate is not an observation. Do not rerun a finalized same-head endpoint: it rejects intact. Inspect immutable artifacts read-only. See [local workflow](local-development.md) for exact observation/protection semantics.

If the editor bridge selects another executable or SDK, stop before export. Do not replace retained SDKs, change global shell startup files, use `TERM` as a workaround, or weaken macOS security. Resolve the mismatch within the accepted toolchain scope and local installation registry.

Godot references: [C# prerequisites](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/c_sharp_basics.html), [renderer selection](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html), [macOS export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_macos.html).

## Checking that a run started (ARC-DEV-029)

A run is evidence only when it ran what was intended. After starting any Godot or launcher run (viewer mode, native smoke, self-test, lab, export, `./arcanum ...`), check within the first minute that it started the intended mode, and stop it if it did not. A run started through the [launch queue](#launch-queue-arc-dev-033) may first wait for its turn: check its `ARCANUM_QUEUE` lines first, then these:

- **Started:** the log shows the mode's own startup and stage lines (a viewer prints `ARC-RENDER-004 load stage=begin` and a native smoke prints its stages as it goes; an export or a headless self-test prints its own progress lines), and a windowed run shows the viewer or lab window with its HUD. When the log is written only at exit (output redirected to a file can be buffered), check the process instead: its command line (`ps -o pid,args -p PID`) and its window.
- **Not started:** the Godot project manager or an empty editor window (Godot did not get `--path` or its arguments); an argument failure on the viewer's HUD (`failure stage=Arguments code=Usage`, drawn on the window and not printed to the log, so the stage lines above never appear); "unrecognized arguments" from the launcher; a log without the expected mode's lines and no viewer window. An interactive viewer that has finished loading may idle at low CPU; that is not a failed start.
- **Then:** stop only the process you started (check its PID and working directory first), fix the command and run it again. Never leave a stuck or wrong window on the owner's screen, and never report a result, PASS or failure, from a run that did not start the intended mode.

### The launcher's start check (ARC-DEV-059)

A quiet `./arcanum run` (the default; not `--verbose`, not a run nested in another one) does this check itself, so run
it blocking in one step and read its result; this satisfies the one-minute rule. From the moment its command starts
(after any wait for its turn), the wrapper reads the run's log as it fills, until the command's **start line**:

| Outcome | Wrapper output | Exit |
|---|---|---|
| A start line within the window (default 60 s) | `ARCANUM_QUEUE started ticket=N after=Ss line=LINE` on stderr; the run goes on to its end as before | the command's |
| A start failure line before it | `ARCANUM_QUEUE start-failed ticket=N line=LINE`; the command gets 5 s to exit by itself or print its start line (then the failure line was a harmless early line and the run goes on), then its process group is stopped | the command's own non-zero exit, or 125 |
| No start line in the window | the process group is stopped | 125 |
| The command ended non-zero before any start line | | the command's |
| The command ended with 0 before any start line | `ARCANUM_QUEUE note: ticket=N ended with 0 before a start line; its mode was not checked` (an import, the WORLD settings test): check its log yourself when the mode matters | 0 |
| The run's own limit (`--timeout`) came first | as for any timeout | 124 |

A failed start adds `start check: REASON` under the `FAIL` line on stdout, before the log lines. Fix the command and run
it again; a failed start is never a result of the change under test.

- **Start lines.** Each log line is checked in order (a line rewritten with `\r` counts as a line). For `window`, `headless`, `export` and `quick` runs: the viewer's `ARC-DEV-059 viewer start mode=M map=NAME main-menu=B smoke=B`
  (printed when its arguments resolved a mode; check that `M`, `NAME` and `smoke` are the intended ones, since a viewer
  that did not get its arguments starts from the launcher settings instead), `ARC-RENDER-004 load stage=begin` (a build
  from before ARC-DEV-059), the self-test's first `ARC-DEV-043 self-test stage`, the phase lines of the export
  endpoint (`export-endpoint: validate ...`), the terrain export (`validate-macos: step NAME`) and the player package
  (`package: ...`), never their failure lines, and Godot's import progress (`[  0% ] ...`, for `--import`). Godot's banner and the `ARC-DEV-047 build` line are not start lines:
  the project manager and a start that then fails print them too. `dotnet` runs have none (a build or test ends by
  itself and its quiet output may print nothing for minutes), so they are not checked.
- **Start failure lines.** An argparse usage error (`usage: `, `PROG: error: unrecognized arguments ...`), the viewer's
  `failure stage=Arguments` or `failure stage=GameFolder` (the viewer now also prints every failure it draws on its HUD
  as `ARC-DEV-059 viewer failure TEXT`, except in the self-test's bootstrap apps, whose failures are expected), the shell's or `env`'s own `command not found` or `No such file or directory`
  (`zsh: command not found: Godot`, `sh: ./x: No such file or directory`; an engine line about a missing file is not
  one), Python's `can't open file` and Godot's `Invalid project path` or `Couldn't load project`. A failure after the start line is the run's result, not a start failure.
- **World fault lines (ARC-DEV-065).** An `InvalidOperationException`, `ArgumentException`, `OverflowException`,
  `GridReplayCapacityException` or `TerrainViewerException` inside the viewer's world step (input, the frame's world advance,
  the AI host, combat) ends the run: `ARC-WORLD-003 lifecycle=observation-fault-disposal`, then the line
  `ARC-DEV-065 observation-fault type=T at M1 < M2 < ...` naming the exception's type and its methods, innermost first (no
  message, no file paths), then the HUD failure `failure stage=ActorPlan code=ActorPlan`; so a player's `godot.log` or a bug
  report's `log.txt` shows the failing code.
- **`--expect REGEX`** replaces the default start lines (a Python search on each log line), and gives a `dotnet` run a
  check. A command that runs longer than the window and whose output does not reach the run's log by then needs it,
  or `--start-within 0`: a wrapper that
  sends Godot's output to its own file (send it to stdout, or `tee` it, instead), the WORLD validation
  (`world_validate.py` prints only its summary at the end), a lab that prints none of the lines above.
- **`--start-within SEC`** changes the window; `0` turns the check off.

Tests: `python3 -B -m unittest tools.local_dev.test_start_check` (the start lines of each program and that the
programs still print them, failure lines, stopped and ended runs, `--expect`, `dotnet`, `--verbose`).

Known pitfalls:

- zsh, the default shell on the owner's Mac, does not split an unquoted `$ARGS` string into words: pass arguments as separate words or an array (or run the line under `bash`), and check the started command line.
- The exterior AREA mode rejects `--game-root` (a map-mode argument) with a Usage failure on its HUD and leaves its window open.
- A windowed native smoke needs its window visible: a hidden or covered window is one cause of a stop at the first pixel stage (`AREA_CLASS_PIXEL_NOT_OBSERVED`). Since ARC-DEV-045 a minimized window keeps drawing in an automated run ([minimized window draw](#minimized-window-draw-arc-dev-045)), so a minimized window is no longer this cause; a covered window may still be. Make the window visible and rerun; a stop there with the window visible and unobstructed is a result. Before ARC-RENDER-029 the exterior smoke also stopped there with its window focus simulated on (the camera follow moved the sampled view back to the actor); its samplers now hold the camera and refuse a moved view with a `..._CAMERA_MOVED` code ([map mode](../rendering/area-map-mode.md#camera-held-during-smoke-samples-arc-render-023)). A run with real window focus was not observed; if the stop persists with a visible window, compare with `main` before blaming the change.
- A native smoke owns its window focus: under the simulated focus (`_worldFocusSimulationForSelfTest`) the viewer ignores the window's real focus, and the actor's safety gate opens only while the smoke has set `_worldFocused = true`, so a smoke sets it (and the simulated pad) **before its first placement** and keeps it **through its last**, the target search of a combat stage (`AreaSmokeFightableStartAsync` places the hero) and a placement in a `finally` included. Until ARC-DEV-133 `cast` and `turn-based` set it after that search, and `frame clock` placed the hero again after restoring the window's own focus, so they passed only while the window really had the focus and stopped with `AREA_SMOKE_GATE_CLOSED` when it did not (the log showed `focused=False` with the simulation on, or off in the `finally`); the log line `ARC-DEV-133 smoke placement gate closed: focused=... window=... uiFocus=...` now names which condition keeps the gate shut.
- A command given to the owner is one you ran yourself, or one checked with the launcher's `--dry-run` (on `area`, `world` and `map`) or `--help` and [the launcher docs](local-development.md#project-launcher). A dry run checks the launcher's command and build, not the viewer's own arguments: a viewer argument is checked by a run.

## Smoke runner (ARC-DEV-061)

The viewer's smokes on the original data run through one shared runner; do not write a scratch smoke script (the queue
history held about 500 runs of such copies of one template).

    ./arcanum smoke pr [--stages S,...] [--map NAME] [--hero H] [--sector X,Y] [--main-menu] [--env NAME=VALUE] [--all] [--crash-site]
    ./arcanum smoke pr --auto [--base REF]     # the zones the diff against origin/main selects (ARC-DEV-396)
    ./arcanum smoke pr --zones core,walk,...   # named zones: core, walk, container-click, crash-site, census, complete
    ./arcanum smoke full                       # the full map smoke chain (the batteries)
    ./arcanum smoke complete                   # the nightly run: the chain, then every stage
    # pr and complete also take --retry / --no-retry (default: off for pr, on for complete)
    ./arcanum smoke map NAME [--stages S,...] [--hero H] [--sector X,Y] [--main-menu] [--env NAME=VALUE] [--all]
    ./arcanum smoke crash-site [--hero H] [--env NAME=VALUE]   # the Crash Site smoke alone
    ./arcanum smoke self-test [--headless] [--env NAME=VALUE]  # the viewer's synthetic --self-test
    # each also takes --no-build, --keep-settings and --dry-run; pr and map also --no-config-check

- **Click and walk changes** ([smoke scope](tasks.md#smoke-scope), ARC-DEV-239). A PR that changes click or walk handling
  always runs `./arcanum smoke pr --stages map` and `./arcanum smoke self-test` (both, every such PR).
- **Sampled stages (ARC-DEV-395, owner decision 2026-11-11).** `container-click` and `walk` took 675 s and 525 s of the
  one Godot slot in that report (600 s and 363 s on an idle Mac when re-measured here) ([smoke speed diagnosis](../research/smoke-speed-diagnosis-2026-11.md)), so by default they check a
  sample; `--all` (on `pr` and `map`, with a run of one of them by `--stages` or its zone; it sets the viewer's `ARC_SMOKE_ALL=1`) runs the
  full stage; the nightly `complete` run and `--zones complete` always run both in full. `container-click` clicks at most 8 containers of the window, spread over the (prototype, admitted open
  outcome) groups, the group of admitted rubbish bins and a locked container first (so at least one bin, and every bin only with `--all`) (`ARC-DEV-395 container-click SAMPLE n of N` names them); `walk` plans and walks the
  route to a goal about 10 window moves from the start (the full route has 71) instead of to the town sector. What the sample no longer proves: for
  `container-click`, the click path of the containers left out (a pick lost behind another object or a roof, a wrong
  hover, a refused or locked answer of that container) and the "every rubbish bin opens" rule over the whole window (it
  holds for the sampled bins); for `walk`, the walk's tail (the 60 or so window moves after the tenth, the arrival at
  the town, the planner on the full 88-sector route). Run `--all` when a change touches picking, containers or the click
  path (`container-click`), or the route planner, the follower, the window loader or the far end of the route (`walk`),
  and before reporting a stage that failed as sampled as fixed. The sampled run reads the same zones: `--auto` selects the `walk` and `container-click` zones as before and they run as a sample unless `--all`.
- **Scope** ([smoke scope](tasks.md#smoke-scope)). `pr` is the per-PR run: only the stages the change touches or is
  directly related to (`--stages`, the viewer's `ARC_AREA_MAP_SMOKE_ONLY` names; checked against its
  `MapSmokeStageNames` before anything waits for the queue) on `--map` and `--sector` when given, else on the
  configuration each stage needs (stage configurations below; most run on
  `Arcanum1-024-fixed` opened at the Crash Site), with the Crash Site smoke first only when the change is related to it
  (`--crash-site`); with neither it refuses (nothing to run). `full` is the chain of batteries: the Crash Site smoke, then the whole map battery (no stage list) on the main map with
  `--hero human-male` at the Crash Site, at the town sector (1409, 1314) and at the generated wilderness sector
  (1384, 239), and on `elven ruins` with `--hero elf-female` (the elf-male's boomerang cannot break a container for
  the junk pile stage). The daily package build (`./arcanum daily`, [daily package build](packaging.md#daily-package-build-arc-dev-062),
  when main changed since the last package) runs `complete`: that chain, then every stage ([smoke zones](#smoke-runner-arc-dev-061)).
- **Stage order matters (ARC-DEV-394).** The stages of one process run in the viewer's order, and a stage can leave the world's test
  time set (`RestoreWorldTestTime` keeps it at the session's time when that is ahead of the wall clock); while it is set,
  `ProcessWorld` returns early in a native smoke, so the frame never reaches `ProcessAreaAi`, and a later stage that waits for frames
  to sync the AI host sees nothing. A stage that needs the frame's AI work asks for it itself (`AreaAiHostNow()` in its wait, as
  `created-critter` does); a stage that passes alone and fails after others can be this. Seen after the long stage list of the smoke
  speed diagnosis and not fixed: `script-critters` (`the tile notifications of the creations ran`; it passes alone and fails after
  `spirit-talk,created-scenery`) and `summon-wall` (`the summoner standing in its own wall is spared`; the cause is not looked at).
- **Every requested stage runs (ARC-DEV-063).** `PASS` means each requested stage ran: a map smoke passes only when
  its log holds the `PASS` line of every stage in its list (`STAGE_PASS` in `tools/local_dev/smoke.py`, checked against
  the viewer's sources by the tests), the whole map battery (`full`, `map` without `--stages`) that of `map` and
  `resources`; a stage the viewer skipped (silently, as the AI stages without a hero, or with a `NONE`/`SKIP` line)
  makes the step `FAIL exit=3` with `FAIL exit=3 stages not run: ...` and up to three of the log's `NONE`/`SKIP`
  lines (the missing stages' own first). The battery's other stages may report `NONE` where the map lacks what they
  need (a whole map has no window to stream), so it vouches only for those two. Before
  anything runs: a stage that needs a hero (`map` and the whole battery, `cast`, `turn-based`, `knockdown`, `ranged`,
  `container`, `junk`, `barter`, `life`, `floaters`, `ai`, `ai-turn-based`, `ai-cast`, `ai-follower`, `premade`, `opening`, `pickpocket`, `loot-worn`, `inv-arrange`, `loot-room`) gets
  the full chain's hero of that map (`elf-female` on `elven ruins`), else `--hero human-male`, when no `--hero` is
  given, and says so (`SMOKE hero human-male (auto): ...`); with
  `--main-menu` (no hero) it is refused. A menu stage (`menu`, `scene`, `options`, `bar`, `death`, `perf-menu`) needs no
  `--main-menu`: it gets its own menu process. The stages the viewer runs only without `map` (`barter`, `menu`, `scene`, `options`, `plants`, `bar`,
  `life`, `floaters`, `death`, `ai`, `ai-turn-based`, `ai-cast`, `ai-follower`, `premade`, `opening`, `pickpocket`, `loot-worn`, `inv-arrange`, `loot-room`, `wreck`, `altar`, `npc-resident`, `saved-runs`, `npc-door`, `npc-tile`, `tile-ghost`, `container-click`, `fire-order`, `indoor-fade`, `npc-shot`, `critter-fall`, `save-resume`, `cast-multi`, `companion-click`, `ai-react`, `placement-census`, `spell-end`, `hold-hero`, `movement`, `spirit-talk`, `created-scenery`, `worn-light`, `critter-light`, `summon-flash`, `summon-wall`, `tech-craft`, `item-aim`, `perf`, `perf-menu`) get a run of their
  own (step `stages-alone`, or `map-alone`) when `map` is in the list too. `tech-craft` (ARC-DEV-362, technology audit slice T-a) needs the bullets the map's bodies hold, so it runs on the cave: `./arcanum smoke map "crash site cave" --stages tech-craft` (on a map with no bullets of the crafted gun's type it fails with that message; its steps are the helpers of `TerrainViewerTechCraftHarness.cs`). `item-aim` (ARC-DEV-365, technology audit slice T-g) opens the item cursor with a script trap device from the use icon and presses a tile (goal 41) and an NPC (goal 39); it needs a hero and a map with a living NPC. A run whose stages include `debug-travel`, `follower-far` or `inv-carry`
  starts the viewer with `--debug-mode` (the stage needs the debug mode, ARC-DEV-050; ARC-WORLD-120).
- **Stage configurations and grouping (ARC-DEV-391).** A viewer process fixes its map, start sector, hero, main menu and
  debug mode at launch, so only stages with the same configuration share a process. `STAGE_NEEDS` in
  `tools/local_dev/smoke.py` is the one table that says which configuration each of the viewer's stages runs in (default
  map and sector; whether another map or sector fails it, so `only_map`/`only_sector`; main menu, debug mode; a process of
  its own; portable). `--stages` reads it: with no `--map`, `--sector` or `--main-menu` each stage gets its own
  configuration, the stages are grouped into the fewest processes (one per configuration, in order of first appearance;
  the step is named `stages-main`, `stages-wild`, `stages-town`, `stages-shrine`, `stages-crash-site-cave` ... when there
  are several, `stages` when there is one), a *portable* stage (`map`, `resources`, `report`, `knockdown`, `inv-arrange`,
  `created-*`, `summon-*`, `item-aim`) rides in a process that already exists on its map (else it gets one), and the stages that end a game, move
  the window, leave state behind or run long (the menu family, `walk`, `census`, `container-click`, `perf`,
  `dead-window`, `critter-fall`, `tech-craft`, `debug-travel`, `follower-far`, `inv-carry`) each get a process of
  their own (`stages-main-walk`). `--map`, `--sector` and `--main-menu` pin that dimension for every stage; a pinned
  value a stage cannot pass in is refused before anything waits for the queue (`stage plants does not run at --sector
  1452,1291: it needs map ... at sector 1384,239`), and so is `--main-menu` with a stage that needs a hero or a session.
  `--no-config-check` runs the request as given (the grouping stays). The table holds only what is known: where a stage
  was only seen to pass in one place its default is that place and another one is not refused. `inv-carry` and
  `critter-light` have no configuration seen to pass yet (the diagnosis saw `NONE` at the Crash Site and in the town). A new stage needs a row
  (the tests fail without one); the zones below are built on this table.
- **Smoke zones (ARC-DEV-396).** Option A of the [smoke speed diagnosis](../research/smoke-speed-diagnosis-2026-11.md#zone-options),
  in `tools/local_dev/smoke_zones.py`. A zone is a set of stages a change can break together: `core` is every cheap stage (every
  stage no heavy zone holds, so a stage the viewer gains lands there until a heavy zone claims it; grouped by configuration into
  about twenty processes at this base), and the heavy zones are `walk` (the stage `walk`, about 1 minute as a sample, about 6 with `--all`), `container-click`
  (the stage of that name, about 2 minutes as a sample, about 10 with `--all`), `crash-site` (the Crash Site smoke, not a stage) and `census` (the stage `census`).
  `--zones core,walk` runs those; `complete` stands for all of them and runs what `./arcanum smoke complete` runs: the Crash Site
  smoke and the batteries of `full`, then every stage but `map` and `resources` (the batteries vouch for those) grouped as
  usual, the heavy stages last. That is what `./arcanum daily` runs, so the daily run now checks every stage the viewer accepts
  (before, 15 of 76), and a stage that fails on main (see the known failures) blocks the day's packages as any failure does.
  `--zones` and `--stages` combine (the union); `--map`, `--hero`, `--sector`, `--main-menu` and `--env` go with `--stages` only: with zones
  chosen they apply to the `--stages` group (its own step `stages`) and the zones' stages keep their configurations (step `zones-...`);
  without `--stages`, or with `complete`, they are refused. The batteries of `complete` vouch only for `map` and `resources`, so the
  other stages they also run (`cast`, `ranged`, ...) run again in their groups.
  **`--retry`** (ARC-DEV-396; default on for `complete` and for the zone `complete`, off for other `pr` runs; not after a viewer that did not start, exit 125): a viewer process ends at its first failing stage, so the
  stages after it never ran, and a stage can fail only because of what the stages before it left behind (the 2026-11-11 daily run:
  `ai-cast` and `npc-door` failed in their groups and passed alone). With `--retry`, a failed stage process is followed by one
  process per unfinished stage (steps `STEP-retry-STAGE`; the failing stage included); the summary says `passes alone, so it fails
  only after the other stages of STEP` or `fails alone too`. The failed step stays failed (the exit code and the daily gate do not
  change); the retries only add what ran, so no stage goes unchecked behind a failure and an order-dependent stage is named.
  **`--auto`** reads the diff: the merge base with `--base` (default `origin/main`; fetch first) to the working tree, untracked
  files included, and picks the zones by the table `TRIGGERS` (all matching rows of a path, as a union; a "no smoke" row such as `tests/` or
  `experiments/` beats the generic project-file rows `*.csproj`, `*.sln`, `*.props`, `*.tscn` and the lock files): ordinary game and
  viewer code (`src/Arcanum.Simulation|Session|World|Scripting|Input`, `tools/terrain-viewer`) selects `core`, the files of a
  heavy zone add that zone (route, movement, residency, window load and hero walk files: `walk`; picking, hover, roofs, locks and
  container loot: `container-click`; the Crash Site profile, transitions, portals and area start: `crash-site`; the census stage
  and the map row readers: `census`), a file of the shared data readers, the presentation, the save schema (`GameSaveLibrary`,
  `GameSessionSnapshot*`), the render and shared UI files or a project, package or build file selects `complete`, and so does a
  file no row names (a missing row is a safe miss, never a silent skip). A file no smoke can cover (documents, unit tests, research,
  HD, oracle and inspect tools, the launcher tooling but the runner itself, the HD review viewer) selects nothing; a diff of
  such files prints `no smoke zone covers this diff` and the run passes without building anything. The viewer's start and stage
  dispatch file and its stage list file also hold shared logic, so for them a change to a line that is more than a stage's
  registration adds `complete` or `walk`. `--auto` prints each zone with the files that chose it (the three first), the PR report
  names the zones (the last line `SMOKE zones: ...`), `--crash-site` and `--stages` add to them, and `--dry-run` shows the
  plan. A click or walk change still runs `--stages map` and `smoke self-test` as before (`core` holds `map`; the self-test is its
  own target). A new heavy stage or a new path gets a row in `TRIGGERS` in the same PR (the tests check that every row matches a
  tracked file and that every stage is in exactly one zone).
- **Known failures on main (ARC-DEV-391).** `tools/local_dev/smoke_known_failures.json` lists failures seen on clean
  `origin/main`: the stage (or `crash-site`), a regex for the line its log holds, the owning task (`ARC-TYPE-NNN` or
  `unassigned`) and the day. A failed step whose requested stage did not pass and whose log holds that line is reported
  `known on main, not this PR` (with the task), and the summary adds `only known failures on main` when every missing stage is
  listed; the step and the run still fail and the exit code is the same, so the PR report names the stage as a known
  failure and goes on ([smoke scope](tasks.md#smoke-scope)). A failure with another line, or another stage failing beside
  it, is not hidden. Add an entry only for a failure seen on clean main with its exact log line, and delete it in the PR
  that fixes the failure.
- **Targets of the combat stages (ARC-DEV-089).** The stages that fight an NPC (`attack` in `map`, `cast`, `turn-based`, `ai`) do not take the
  map's weakest NPC blindly: they take the first one (weakest first, at most 24 tried) the hero can stand next to and, with the hero placed there,
  see (`AreaSmokeFightableStartAsync`: a roof-covered or walled-in NPC is skipped), and print `NO-TARGET` when none is, which the battery tolerates (so a regression that hides every target shows only in a `--stages` run, which reports the stage as not run; a limit). A stage
  whose content is the Crash Site's (`wreck`, `altar`: Virgil's home and a script tile) prints `NONE` in any other window, not a failure; asked for with
  `--stages` on such a window the runner reports it as not run. `ai-follower` takes the first speaker of the map that can heal with the skill and fight (not undead, mechanical, invulnerable or nigh-invulnerable: the original's heal skill check and danger setter refuse those, ARC-DEV-129) and prints `NO-FOLLOWER` when there is none (the unlit mine `bessietoonesmine-fixed` has only an undead, invulnerable speaker), which a `--stages` run reports as not run. The AI stages put the hero's life back at their end, a dead hero resurrected, so a stage after them never starts with a dead hero. The viewer's `HoverPointAsync` scans only the screen rectangle of the body (its T0 command and the moving command drawn for it).
- **The Crash Site smoke** is the viewer's pinned Crash Site exterior regression (`--mode area` without
  `--area-map`, with the ARC-WORLD-009 cave input, so it also goes into the cave and back), with `--hero dwarf-male`;
  it needs the pinned Crash Site inputs that `./arcanum setup` recorded.
- **Steps.** Each step is its own quiet run of the [launch queue](#launch-queue-arc-dev-033) from this checkout: the
  viewer build (`dotnet restore` and `build` of `tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln`, kind
  `dotnet`; `--no-build` skips it when the checkout's viewer is already built), a project import (kind `quick`, beside the Godot slot)
  when the project was never imported (no `.godot` folder), then each smoke as a `window` run (the self-test with
  `--headless` as a `quick` run). The viewer arguments come from the launcher configuration
  (`launch.map_arguments`, `launch.terrain_arguments`), as for `./arcanum map`.
- **Start check.** A smoke's start line is the viewer's `ARC-DEV-059 viewer start mode=area map=NAME
  main-menu=B smoke=True` with the intended map (`-` for the Crash Site smoke) given as `--expect`
  ([start check](#the-launchers-start-check-arc-dev-059)), so a viewer that started another map, or not as a smoke,
  fails its start (exit 125); the self-test keeps the default start lines.
- **Own settings and environment.** Each smoke gets its own folder in a fresh `arcanum-smoke-*` folder under the system
  temporary folder, with every settings file pointed into it (`ALWAYS_RUN`, `AUDIO_VOLUMES`, `GAME_SETTINGS`,
  `LAUNCHER_SETTINGS`, `VISUAL_PROFILE`, `WORLD_DIAGNOSTIC_SETTINGS`, `WORLD_DIAGNOSTIC_ZOOM` `_TEST_PATH`); the
  folder is removed after a passing run and kept after a failure or with `--keep-settings`. The owner's variables
  (`DAT_ROOT`, `ARC_*`, `ARCANUM_*` except the queue's own, the settings paths) are dropped from the viewer's
  environment, and Godot starts as `godot_canonical` does (the sentinel, the pinned SDK, a plain `PATH`). `--env
  NAME=VALUE` adds one variable (a capture or probe switch) to the stages' smoke (not `PATH`, `DOTNET_ROOT`, `HOME`,
  `GODOT`, the stage list or the settings paths).
- **Limits.** A whole map battery (`full`, `map` without `--stages`) may run 60 minutes, any other step the queue's
  default for its kind (30 for a smoke).
- **Result.** A failed smoke does not stop the chain; a failed build or import, or a stopped run, does. Each step
  prints the queue's `PASS`/`FAIL` lines with its log, then `SMOKE summary` lists every step (`PASS exit=0` or
  `FAIL exit=E`, and the steps not run) and the last line is `SMOKE PASS N of N steps passed` or `SMOKE FAIL ...`;
  the exit is 0 only when every step passed. `--dry-run` prints each step's command and start line and runs nothing
  (it still checks the stage names and the configured inputs). Run `./arcanum smoke` on its own, never inside
  `./arcanum run` (it refuses: each smoke takes its own place and checks its own start).
- **Godot** is `/Applications/Godot_mono.app/Contents/MacOS/Godot`, or `$GODOT`. The import writes the viewer's
  untracked `*.cs.uid` files into the checkout; never commit them (stage files by name).

Measured speed of the smoke runner, the queue and each stage, and the options for faster runs and per-task zones: [smoke speed diagnosis](../research/smoke-speed-diagnosis-2026-11.md) (ARC-DEV-384).

Tests: `python3 -B -m unittest tools.local_dev.test_smoke` (the stage names from the viewer, the per-PR and full plans,
the settings folders and dropped variables, the start lines, the chain going on after a failure and ending on a failed
build or a stopped run, the stage table, the grouping and the early error, the known failures file, the launcher command) and
`tools.local_dev.test_smoke_zones` (the zone table, the trigger rows, the selection from a diff, the zone and complete plans).

## Timing probe and the `perf` stages (ARC-DEV-367)

The standing probe of the performance diagnosis (O10 of [load time and window responsiveness](../research/performance-diagnosis-2026-11.md)):
`PerfProbe` in `src/Arcanum.World/PerfProbe.cs`, godot-free so the load worker and the viewer use the same one. It measures; it is not
a budget or a regression gate. Other optimisations are shown with it: run before and after, compare the lines.

- **Switch.** Off by default (a call is a flag test, a lap allocates one small object, nothing is printed). On with `ARC_PERF_PROBE=1` (a player's or a
  `./arcanum map` run, or `--env ARC_PERF_PROBE=1` on a smoke) or when the stage list names `perf` or `perf-menu`.
- **API.** `PerfProbe.Mark("name")` (a point in time), `using (PerfProbe.Span("name")) ...` (milliseconds, the managed bytes the
  thread allocated and the gen0/gen1/gen2 collections inside it), `PerfProbe.Measure(name, work)`, and `PerfProbe.Lap("group")` with
  `lap.Step("a")` for consecutive steps (a span `group.a`, `group.b`, ... and one `group`). Names are dotted.
- **Lines** (grep `ARC-DEV-367 perf` in the run's log): `mark name=N t=MS` and `span name=N ms=MS alloc-kib=KIB gc=G0/G1/G2 t=MS`;
  `t` is milliseconds since the process started. The funnel is instrumented: `load.begin`, `load.worker.{stack,plan,transitions,open,
  read,actor,lighting,verify,scene}` and `load.worker` (the worker, a map load), `load.decoded`, `publish.{checks,state,transitions,
  conversations,companions,binders,ambient,hero-body,selection,menu}` inside `load.publish` (the main thread), `load.first-frame`
  (the first frame drawn after a publication), `window.open.MODE` (the interface stack's opener, the panel's own open included),
  `menu.open`, `menu.open.WINDOW`, `menu.first-frame`, `game.new`, `game.load`, `window.open.SaveList`.
  ARC-DEV-380 adds the laps inside the publication's selection step, printed for the first selection after a scene publication: `selection.{ambient,walls,roofs,combat,ground,portals,moving,canvas}`,
  `canvas.publish.{chunks,upright,index,view}` (a new plan) and `canvas.view.{cull,chunks,upload,hd,items}` (its first view): the canvas's own work; `roofs.{bind,follow,publish}`, `walls.{bind,recalc}`,
  `hero-swap.{upload,profile,plan,select}` (the body swap, not the selection it triggers) and `ground.{stamp,frames,busted,extra-art,items,blood,created,critters}` (each time the ground bodies are
  computed from nothing, so also after a reset; a step names the region that follows it, so a first open of the extra art inside a busted-object loop counts under `busted`).
- **`perf`** (a hero stage, own run; needs a window with a map exit: the Crash Site window has none, so
  `./arcanum smoke pr --stages perf --sector 1409,1314`): `startup` (process to load begin, worker, publication and first frame),
  one `window` line per open of each window key I C L T W M, six each through the production key route (`open-sync` is the key's
  call, `to-first-frame` runs to the end of the next drawn frame; iteration 0 is the cold open, 1-5 are warm) and a `windows` line
  per key with the cold values and the warm medians, then `map-change` for a trip out through an exit and back (`total`, the worker,
  the publication and load begin to first frame). `PASS` means every measurement was taken: a window that did not open or a window
  without an exit fails it. `total` of a map change includes the stage's own placing, click and waiting for the use to be accepted
  (the load's lines are the load); `M`, the spell pane, is not on the interface stack and has no `window.open` span; a headless run prints `NONE` (no frame is drawn).
- **`perf-menu`** (a menu stage, `--main-menu`): `startup` to the main menu's first frame (`start-to-menu`) and a new game from the
  menu. `./arcanum smoke pr --stages perf-menu --main-menu`.
- **Resident art across map loads (ARC-DEV-375).** A map load keeps decoded frames and visual records for the next one ([design](../world/area-loader.md#resident-frames-and-visuals-across-map-loads-arc-dev-375)).
  The load worker's read prints `load.worker.read.{strict,scene,copy}` spans and `ARC-DEV-375 visuals ...` / `ARC-DEV-375 frames resident-hits=H read=R ...` lines
  (hits against frames actually read, the store's size). `ARC_AREA_RESIDENT=0` in the environment (`--env ARC_AREA_RESIDENT=0` on a smoke) turns both stores off
  for a before/after comparison; nothing else depends on it. The editor `.mob` facts (ARC-DEV-388) have their own store, off with `ARC_AREA_RESIDENT_EDITOR=0` or `ARC_AREA_RESIDENT=0`, and the worker prints `ARC-DEV-388 editor-records served=B ms=T ...`.
- **Map transitions kept on the device (ARC-DEV-392).** The map's transitions and the map list's destinations are written to `map-cache/` in the device's
  Steamwoven folder (`~/Library/Application Support/Arcanum Steamwoven Game/map-cache/` on a Mac; not `user://`, which every smoke replaces with a temporary
  folder), so the next process, above all the next smoke run, skips the 1.8 s read ([design](../world/area-loader.md#map-transitions-kept-on-the-device-arc-dev-392)).
  The viewer's start line `ARC-DEV-392 map-cache folder=...` names the folder, and the load's `ARC-WORLD-031 load stage=transitions` line says `source=memory|device|content|content+stored`.
  `ARC_MAP_CACHE=0` (`--env ARC_MAP_CACHE=0` on a smoke) turns only this store off, `ARC_AREA_RESIDENT=0` turns it off with the resident stores, `ARC_MAP_CACHE_DIR=<folder>`
  names another folder; a smoke drops the owner's `ARC_*` variables, so pass them with `--env`. Deleting the folder only costs one read; at most 96 files are kept.
- **Reading the numbers.** The Mac is shared and a Debug source run is slower than the packaged build: the diagnosis took medians
  over several runs and looked at the load average. A packaged build needs the probe's switch in the environment (`ARC_PERF_PROBE=1`);
  HD runs need `ARC_HD_PACK` and settings files as the diagnosis's reproduction says.

Tests: `PerfProbeTests` (the probe off and on, the lap names, the thread's allocation, the environment switches) in
`tests/Arcanum.World.Tests`; `test_smoke` covers the stage kinds.

## Shared content index (ARC-DEV-372)

`ContentStack.Open` indexes every archive and walks the loose files of the game folder: about 87 MiB allocated and 0.3-0.4 s each (the allocation profile
of the performance diagnosis, O4), and the viewer used to open one per reader (the load worker's two, five in `StartAreaTransitions`, the portraits, twelve
background binders at once, the ground bodies, every window's art). The viewer's readers now call `ContentStack.OpenShared`:

- **Sharing is opt-in.** Off by default, so tests and tools open private indexes as before; the viewer turns it on at its start (`ContentStack.EnableSharing`,
  not in a self-test). With it on, `OpenShared(plan)` hands out a handle on the one index held for that mount plan (at most two plans are held), even when several threads
  ask together; each handle is disposed by its reader as before and the archives close with the last handle. A call with its own `ContentStackOptions` opens a private index.
- **Staleness.** The index is kept for the run (the original reads its static installation once). An archive whose size or modification time changed is read again;
  `ContentStack.ResetShared()` drops the held indexes (`PrototypeCacheMount.Use` calls it, since the launcher may have built files into that folder).
- **Measuring.** `./arcanum smoke pr --stages perf --sector 1409,1314 --env ARC_PERF_PROBE=1`; the publication's `transitions.NAME` steps print
  next to `publish.NAME`. Reading the numbers: the allocation columns are exact, the milliseconds move with the Mac's load.

## Launch queue (ARC-DEV-033)

Several threads work on the owner's Mac at once. Every Godot run, every export and every `dotnet` build or test goes
through one queue, so Godot windows never open over each other, timing-sensitive runs are not
starved, and an export gets a quiet Mac in its turn instead of waiting for one. Runs are ordered by arrival and their
declared length ([order](#queue-order-arc-dev-390)): a short run is not stuck behind a long one that arrived a little
before it. It replaces the earlier
`lockf /tmp/arcanum-godot-window.lock` rule, which was not a queue and covered windows only.

    ./arcanum run --kind window|headless|export|dotnet|quick [--estimate SEC] [--timeout MIN] [--label TEXT] [--expect REGEX] [--start-within SEC] [--verbose] -- COMMAND [ARGS...]
    ./arcanum queue                 # the runs holding a place, then the waiters in order
    ./arcanum queue --history N     # the last N finished runs: waited, ran, exit

| Kind | Use it for | Rule | Default limit |
|---|---|---|---|
| `window` | any Godot run with a window: native smokes, the viewer self-test, labs | the single Godot slot, in [queue order](#queue-order-arc-dev-390) | 30 min |
| `headless` | `Godot --headless` that runs for a while (a lab, a long probe) | the same Godot slot | 30 min |
| `quick` (ARC-DEV-390) | a Godot run with no window that ends in seconds: a project `--import`, the headless viewer self-test (`./arcanum smoke` uses it for both) | two quick places beside the Godot slot (it neither waits for nor holds the slot), one at a time per checkout; waits for an export that is running or next | 30 min |
| `export` | an export endpoint (`tools/arcanum-local ... export`, the validation commands that export) | the Godot slot; when it is next, new `dotnet` runs wait and running ones finish, then it runs alone | 180 min |
| `dotnet` | `dotnet restore/build/test`; give the root checks as one command (`sh -c '... && ... && ...'`) | two dotnet places (waits for a free one, [limits](#dotnet-memory-limits-arc-dev-300)); runs alongside Godot runs; waits for an export that is running or next | 45 min |

- **Order and gaps.** Places are numbered as runs arrive and ordered as [described below](#queue-order-arc-dev-390); a
  waiter checks the queue every 0.25 s, so the next run starts within that of the previous one ending. An export has
  no priority: it waits for its turn like any run, and its turn always comes. Other commands (git, Python self-tests, reading logs) are not queued.
- **Start check.** The wrapper prints `ARCANUM_QUEUE queued ticket=N kind=K ahead=A waiting-for=#M ...` while it waits
  (again when its position changes), `ARCANUM_QUEUE start ticket=N kind=K waited=Ss` when the command starts, and
  `ARCANUM_QUEUE end ticket=N exit=E ran=Ss` at the end, on stderr; a [quiet](#quiet-output-arc-dev-037) run also
  prints `ARCANUM_QUEUE log ticket=N PATH` before it waits. A quiet run then checks its command's start itself
  ([the launcher's start check](#the-launchers-start-check-arc-dev-059)); for a `--verbose` run, check within the
  first minute that one of the `queued` or `start` lines is there and, after `start`, the command itself as
  [above](#checking-that-a-run-started-arc-dev-029).
  A queued run is not stuck: `./arcanum queue` shows what it waits for.
- **Limits.** A run that exceeds its limit has its whole process group stopped (SIGTERM, then SIGKILL after 10 s) and
  exits 124 with `ARCANUM_QUEUE timeout`. `--timeout MIN` changes the limit, `--timeout 0` removes it. Stopping the
  wrapper (Ctrl-C, SIGTERM, SIGHUP, a stopped background task) stops its command and gives up its place; the wrapper
  exits with the command's exit code, or 128+N when stopped by signal N. A wrapper killed with SIGKILL cannot stop its
  command: a Godot command then keeps the place until it ends or its limit plus 10 s passes, when the next waiter
  (or `./arcanum queue`) stops its process group; a `dotnet` command keeps running outside the queue.
- **Crashes.** Each place is a ticket file in `~/Library/Application Support/Arcanum Steamwoven/run-queue/` (or
  `$ARCANUM_LOCAL_HOME/run-queue`) whose lock the wrapper holds; for Godot kinds the command holds it too, so a run
  whose wrapper was killed keeps its place until its command ends or runs out of time. The kernel drops the lock of a dead process, and a
  lock held only by a leftover descendant (a build server that inherited it) does not count, so a crashed run never
  blocks the queue. Process start times are compared in the C locale and UTC, so lanes with different locales agree;
  when `ps` cannot answer, the run counts as alive. Never delete ticket files by hand; `history.jsonl` there records every run.
- **Nesting.** A queued command that calls `./arcanum run` again (or `./arcanum area|world|map`) runs inside its
  parent's place when that place covers it: an export covers every kind, a `window` or `headless` run covers Godot,
  `quick` and `dotnet` runs, a `quick` run covers `quick` runs, a `dotnet` run covers `dotnet` runs. Any other nesting (an export inside a Godot run, a Godot
  run inside a `dotnet` run) would wait for its own parent and is refused: queue it on its own.
- **The owner's viewer.** `./arcanum area`, `world` and `map` take the Godot slot first in line (after the run holding
  it), with no time limit, and keep it until the viewer window closes; the command stays in the terminal until then.
  Ctrl-C there, or closing the terminal, gives the place up while the viewer stays open. `--no-queue` starts at
  once, and `--dry-run` never queues.
- **Lanes still on lockf.** While some threads still use the lockf rule, a run holding the Godot slot also takes
  `/tmp/arcanum-godot-window.lock`, so the two never overlap (a queued run then prints that it waits for a lockf user).

Tests: `python3 -B -m unittest tools.local_dev.test_run_queue` (arrival and length order, quick runs, gaps, export
quiet, crashed and killed runs, limits, lockf, nesting, locales); they use their own `ARCANUM_LOCAL_HOME` and lock file.

### Queue order (ARC-DEV-390)

[Smoke speed diagnosis](../research/smoke-speed-diagnosis-2026-11.md) measured 3/4 of a smoke call's time as waiting
for the single Godot slot behind a few runs of 5 to 11 minutes (`walk`, `container-click`); a first-come first-served
queue is the worst order for such a mix, and a project import waited for a window run to start 2.5 s of work.

- **Declared length.** Every run carries an estimate in seconds: `--estimate SEC` on `./arcanum run`, or the kind's default
  (`window` 60, `headless` 60, `export` 300, `dotnet` 60, `quick` 15) when none is given. `./arcanum smoke` declares its
  own per step (`smoke.estimate`: a viewer run is about 20 s plus 3 s per stage, the whole map battery 60 s, the Crash
  Site smoke 110 s, `walk` 540 s, `container-click` 700 s, `census` 900 s as an assumption). The estimate only orders
  the queue; it is not a limit (`--timeout` is) and a wrong one costs its run a different place, nothing else.
- **Order.** The key of a waiting run is its arrival time plus 3 times its estimate (`ORDER_WEIGHT`); the lowest key goes
  first, equal keys by number, and the owner's own viewer launches stay first of all. A 20 s run that arrives within
  about 30 minutes of a 10 minute run therefore starts before it, runs with equal estimates keep the arrival order, and
  no run is passed by a run that arrives more than 3 of its estimates after it, so none starves. The key is fixed when
  the run is queued, so every waiter computes the same order. `./arcanum queue` lists the waiters in this order.
- **Quick runs.** The project import of a new checkout and `./arcanum smoke self-test --headless` are `quick`: Godot
  without a window that ends in seconds, on its own checkout's project folder, so it runs beside the Godot slot (up to
  two at once) instead of waiting for it; a `window` or `headless` run of another checkout does not wait for a quick one,
  and an export does. Two sessions in one checkout still never open its project folder at once (the checkout is the folder
  holding `.git` above the one the run was queued from): a quick run waits for a running or earlier queued Godot or quick
  run of the same checkout and, held back so, takes no quick place from another checkout's run; a Godot run waits for a
  running or earlier queued quick run of its checkout. `quick` is for a command that is itself one Godot run: a
  `./arcanum run` inside a quick run is refused (it would wait for its own parent).
- **Old wrappers.** Another checkout that has not merged ARC-DEV-390 yet queues tickets without an estimate and waits by
  number only; while any such ticket waits, every wrapper orders by number too (the old rule), so an old and a new waiter
  can never wait for each other, and the order by length returns when none is queued. An old wrapper ignores `quick` runs
  (an old export may start while a 3 to 12 s quick run of another checkout ends); merge main before queueing.
- **Replay.** The 4,971 runs of 2026-11-07 to 11 replayed on one slot: first come first served 142 s mean wait (62
  person-hours in all); this order with the declared lengths about 83 s (about 38 hours), open-loop, so a
  projection; the long runs wait longer (a 10 minute run about 100 s more on average). `history.jsonl` now records each
  run's `estimateSeconds`.

### dotnet memory limits (ARC-DEV-300)

On 2026-11-08 the owner's Mac (24 GB) froze and rebooted while the packaged game was running: a snapshot taken 4 s
before the WindowServer watchdog showed 153 `dotnet` processes (about 17 GB; 147 orphaned MSBuild worker nodes and
compiler servers of many worktrees), the game itself 1.3 GB. The game's own crash (`SIGABRT` in `flushBuffer`) came
after the freeze. The rules that follow keep a lane's builds from leaving processes behind:

- **Two dotnet places.** At most `DOTNET_SLOTS` (2) `dotnet` runs of all lanes run at once; the next waits in arrival
  order (`ARCANUM_QUEUE queued ... waiting-for=#N`). Godot runs do not count against them. A nested run
  (`./arcanum run --kind dotnet` inside a window run) uses its parent's place. Run every `dotnet restore/build/test`
  through `./arcanum run --kind dotnet`; a `dotnet` started by hand is outside the limit.
- **No build server outlives its run.** The environment of every queued command sets `MSBUILDDISABLENODEREUSE=1`
  (no reused MSBuild worker nodes), `DOTNET_CLI_USE_MSBUILD_SERVER=0` and `UseSharedCompilation=false` (no shared
  Roslyn `VBCSCompiler`, about 1 GB and shared by every worktree); a Godot run's own C# build inherits them.
  [`Directory.Build.rsp`](../../Directory.Build.rsp) adds `-nodeReuse:false` to every MSBuild-based `dotnet` command (build,
  test, restore, publish) in this repository, queued or not.
- **Cleanup after the run.** When a `dotnet` run ends and no other `dotnet` run holds a place, the wrapper runs
  `dotnet build-server shutdown` with the pinned SDK (`DOTNET_ROOT`, from the repository's folder), which stops
  servers left by commands outside the queue. It runs after the run's PASS/FAIL line is printed, and a failure is only a note
  (`ARCANUM_QUEUE note: build servers not shut down: ...`), never a changed exit code. It skips when another `dotnet`
  run holds a place; a Godot run's own C# build is covered by the environment above. A wrapper killed with SIGKILL
  frees its place at once and runs no cleanup, so its leftovers wait for the next shutdown. Type it by hand as `$HOME/.local/share/dotnet/dotnet build-server shutdown` from the
  checkout: a bare `dotnet` may be Homebrew's 8.0.131 first on `PATH`, which `global.json` rejects (`sdk-not-found`).
- **Not limited.** `-maxcpucount` cannot be capped from `Directory.Build.rsp`: the `dotnet` CLI passes a bare
  `-maxcpucount` after it (probed: 14 nodes), so one build still uses all cores while it runs. The two places bound
  how many builds run at once.
- Tests that start the wrapper set `ARCANUM_QUEUE_BUILD_SERVER_SHUTDOWN=0`, so a test never stops the real servers.

### Command environment (ARC-DEV-041)

`./arcanum run` starts its command with `ARCANUM_QUEUE_TICKET` and `ARCANUM_QUEUE_KIND` (for nested runs) and:

- **The pinned SDK.** `DOTNET_ROOT` (or, when it is not set, `~/.local/share/dotnet`, as [above](#process-local-environment))
  is put first on `PATH` and set as `DOTNET_ROOT`, so `dotnet` in a command of any kind is the `global.json` SDK
  8.0.425, never another one earlier on `PATH` (Homebrew's `dotnet@8` 8.0.131 failed `dotnet build-server shutdown`
  with `sdk-not-found`). When the pinned SDK is missing, `PATH` is left as it is and a `dotnet` run prints
  `ARCANUM_QUEUE note: no pinned SDK at PATH` on stderr. A `DOTNET_ROOT` set to another SDK is used as given.
- **Unbuffered Python.** `PYTHONUNBUFFERED=1` (unless already set): a Python command, such as the export endpoint or
  the validate tools, writes each line when it prints it, not when it exits, so a quiet run's log fills while the run
  goes. `dotnet` writes its lines at once; Godot editor and debug runs flush each print (`flush_stdout_on_print.debug`)
  and its errors go to stderr, which is never buffered. A program that buffers its own stdout can still lag.
- **Export progress.** An export keeps each step's whole output in its evidence logs, so its own output used to be
  empty until the end. The export endpoint now prints `export-endpoint: validate TASK HEAD evidence=FOLDER`,
  `export-endpoint: finalize` and `export-endpoint: registered FOLDER` (or `... FAILED exit=E`) as each phase starts or
  ends, and the terrain export prints `validate-macos: step NAME` before each step and, when a step fails,
  `validate-macos: step NAME FAILED exit=E log=PATH` with the step log's last 20 lines, then stops. The start check of
  an export reads these lines in its run log; the lab exports (gamepad, display) print the phase lines only.

Tests: `python3 -B -m unittest tools.local_dev.test_run_log` (the log filling while the command runs, the pinned SDK
before another `dotnet` on `PATH`, a missing pinned SDK, the export's step lines) and the export endpoint test in
`tools.local_dev.test_local_dev` (phase lines).

### Self-test stage evidence (ARC-DEV-043)

A windowed WORLD validation step of an export once timed out (exit 124 after 60 s) where reruns took 10–11 s, and its
evidence log held only the validator's closed summary, so the stage it waited in was unknown.

- **Stage lines.** The viewer `--self-test` prints `ARC-DEV-043 self-test stage NAME begin t=MSms can-draw=B focused=B forced-draws=N`
  before each frame-bound stage (the area probes too), with the time since the self-test began and whether the window
  can draw and has focus and how many frames it drew itself because the window could not
  ([ARC-DEV-045](#minimized-window-draw-arc-dev-045)), and flushes stdout at once (a release export does not flush each print). The last such line
  in a stopped run names the stage it waited in.
- **Raw Godot output.** `world_validate.py --raw-log FILE` (only `--kind synthetic` and `--kind persistence`, which read
  no original data; refused for the original kinds, whose output never leaves memory) writes each child's whole output
  to `FILE`, created owner-only and never overwritten, framed by `world-validate child LABEL start timeout=Ns` (written
  before the child starts) and `world-validate child LABEL exit|timeout code=E elapsed=Ss` (or
  `world-validate child LABEL failure stage=S elapsed=Ss`, with no output, when the child cannot be started or read).
  It is also refused when an `ARC_*_PROBE` variable is set, since a probe (the conversation probe with its game root)
  can make a synthetic child read original data. The printed summary stays closed. The terrain export passes it for its headless, graphical and persistence steps, so the git-ignored evidence
  folder holds `NAME.godot.log` beside each such step log; when such a step fails, the export prints the raw log's last
  20 lines after the step log's.

Tests: `python3 -B -m unittest tools.local_dev.test_world_runner` (framing, owner-only mode, timeout and failure
lines, the persistence children in order, refusal for original kinds, probe variables and an existing file, no file
without the flag).

### Minimized window draw (ARC-DEV-045)

On macOS (Godot 4.7.2, compatibility renderer) a window that cannot draw (`DisplayServer.window_can_draw()` false, seen
when the window is minimized) keeps its process frames but the engine skips every draw: `frame_post_draw` never fires,
so each `FramePostDraw` await of a self-test or smoke waits for its timeout (the cause of the export's exit 124 found in
ARC-DEV-043). Two throttles make such a run slow as well: the engine sleeps the low-processor time (6.9 ms) after every
frame while no window can draw, and App Nap cut a minimized app from about 100 to 18 frames per second after about 30 s.

The viewer process that hosts a self-test, a native smoke or the WORLD settings validation (not a headless run, not an
interactive viewer, not the self-test's bootstrap apps) adds a `HiddenWindowDrawKeeper` node and prints
`ARC-DEV-045 hidden-window draw keeper on app-nap=held` (`unavailable` without the Objective-C runtime, `not-macos`
elsewhere). While the
window cannot draw, the node draws each frame itself (`RenderingServer.force_draw(false, delta)`) where the engine would:
after the frame's process callbacks, deferred calls, timers and tweens, from a callback posted to the Godot
synchronization context (a draw from `_process` missed the frame's deferred redraws, and the HD toggle check failed
with "a frame mixed Legacy and HD"). C# continuations posted earlier in the same frame run before this draw, where the
engine would draw before them, so their changes appear one frame earlier than in a visible run. It sets the low-processor sleep to 0 (the viewer never uses low-processor mode) and
holds a user-initiated activity (no App Nap; idle system sleep stays allowed) while it is in the tree.

Evidence (the owner's Mac, queued window runs that never took focus; probes outside git):

| Run | Window | Result |
| --- | --- | --- |
| GDScript probe, no keeper | can-draw false | `frame_post_draw` stopped: 2 timeouts of 3 s in 7 frames |
| GDScript probe, keeper | can-draw false | main-viewport and SubViewport pixels equal the colour set that frame, 40/40, and 20/20 after 120 s |
| Same, 120 s | can-draw false | ~108 frames/s falling to ~18/s after 30–45 s; with App Nap disabled ~103–106/s for 75 s |
| Viewer `--self-test`, no keeper | can-draw false from the start | stuck in its first frame-bound stage (`AreaRaster`) until the queue's 3 min limit |
| Viewer `--self-test`, keeper in `_process`, engine sleep | can-draw false from the start | 192 s to the HD toggle stage (visible: 7.6 s), then the mixed-frame failure |
| Viewer `--self-test`, posted draw, engine sleep | can-draw false from the start | 194 s to the hover stage, then `GRID_OBSERVATION_NONMONOTONIC` (its synthetic route runs on the wall clock) |
| Viewer `--self-test`, final keeper | can-draw false from the start | PASS in about 9 s (visible: about 9.4 s), 5586 forced draws |

How the window was made unable to draw without taking focus: inside the run, `window_set_mode(MINIMIZED)`, then after
5 s `window_set_mode(WINDOWED)`; `window_can_draw()` turned false within about 0.6 s and stayed false. Only
`window_set_mode(MINIMIZED)` reported the minimized mode but left `window_can_draw()` true and the frames drawn; hiding the
app (`NSRunningApplication hide`, `open -j`) and a covered window also left it true and drawing. So `can-draw=False` in a
stage line is the case this node covers; a window that reads true still draws by itself.

Not covered: Windows and Linux were not run; another OS window of the process that can draw while the main one cannot
would get a second draw in that frame (the viewer opens none).

### System pointer in automated runs (ARC-DEV-060)

The viewer's smokes and self-tests drive the pointer themselves: they push mouse events into the viewport
(`Viewport.push_input`) or, in the terrain smoke, parse them (`Input.parse_input_event`). The system pointer reached the
same GUI through the window, and on macOS the key window receives the pointer's moves anywhere on the screen. Godot also
replays a motion at the system pointer on every cursor shape change (`Input.set_default_cursor_shape`), and the AREA
hover changed the shape whenever the system pointer rested inside the window over the world. Either took the GUI's hover
off the control a test had just entered, one frame later: the hotkey bar smoke's `entering slot 2 shows the spell's hint:
Plain texts=2` (15 of 43 runs in the launch queue's run logs on the owner's Mac before the fix; the logs are outside
git) and the bar smoke's button hover and release failures.
Whether a run failed depended on where the owner's pointer was and whether it moved.

The process that hosts an automated run (as for the draw keeper above) adds an `AutomatedPointerGate` node and prints
`ARC-DEV-060 automated pointer gate window=0`. It takes the main window's input callback and passes every event as the
window would (the `window_input` signal for all but internal events, then the window's input), except mouse and gesture
events, which pass only when a test parsed them with the gate's test device (48; buffered motions merge without
comparing devices, so a parsed test motion must not directly follow a system one). The AREA hover of an automated run
reads no system pointer either (headless runs and the self-test's bootstrap apps too): it is over the world only where a
test places the pointer. The hotkey bar smoke checks
the gate: a mouse move parsed as the system's while slot 2 shows its hint keeps the hint (without the gate this fails
every time; with it the cast stage passed 6 of 6 and the bar stage 4 of 4).

Not covered: the system pointer crossing the window's edge during a step still reaches the window as a window event
(mouse enter or exit), which drops the GUI's hover; Windows and Linux were not run.

### Quiet output (ARC-DEV-037)

Owner decision 2026-10-27 (token audit item 4): every tool output stays in an agent session's context and is re-read
on each later step, so `./arcanum run` keeps the command's output out of it by default.

- **Log file.** The command's whole output (stdout and stderr together) goes to
  `~/Library/Application Support/Arcanum Steamwoven/run-logs/run-<ticket>-<kind>.log` (or `$ARCANUM_LOCAL_HOME/run-logs`);
  its stdin is closed. The wrapper prints `ARCANUM_QUEUE log ticket=N PATH` on stderr before it waits, so the log can
  be checked while the run goes; the log fills as the command prints (ARC-DEV-041,
  [command environment](#command-environment-arc-dev-041)). The newest 200 logs are kept; older ones are removed when a run is queued.
- **Summary.** The `ARCANUM_QUEUE` lines stay on stderr as [above](#launch-queue-arc-dev-033). When the command ends,
  stdout gets `PASS exit=0 ran=Ss lines=L log=PATH`, or `FAIL exit=E ran=Ss lines=L log=PATH` and the first error
  line with at most 40 lines around it (3 before, each cut to 300 characters), and the last error line when it lies
  beyond them (an early harmless Godot `ERROR:` does not hide the failure at the end). A failure with no error line shows the
  log's last 40 lines instead. A pass whose log has error lines (Godot can exit 0 after a `SCRIPT ERROR`) adds one
  `note:` line with their count and the first one. Error lines are Godot `ERROR:`, `SCRIPT ERROR:` and `USER ERROR:`,
  `dotnet` `error CS…`/`MSB…`/`NU…`/`NETSDK…`, failed tests and non-zero error counts, Python tracebacks and
  `…Error:` lines, .NET exceptions, `fatal:`/`error:` lines, `command not found`, `Segmentation fault`, `FAIL`/`FAILED`
  markers, `failure mode=`/`failure stage=` and `..._FAILED` codes; counts of zero errors are not.
- **Reading the log.** Check the start in the log (`rg -n -m 20 'stage|ERROR' PATH`, `tail -n 20 PATH`), and read
  more of it only in small ranges, as [small search and read output](tasks.md#small-search-and-read-output) says.
- **`--verbose`** streams the command's output to the terminal as before, with no log file, no summary and no
  [start check](#the-launchers-start-check-arc-dev-059). Use it only
  when the whole output is the point (a human watching a run). A nested run (`./arcanum run` inside a queued command)
  ignores the setting: its output goes wherever its parent's goes. `./arcanum area`, `world` and `map` are not quiet.
- **dotnet.** Inside the queue, give `dotnet build` and `dotnet test` `-v q --nologo`, and tests
  `--logger "console;verbosity=minimal"`, so the log itself stays small, for example:
  `./arcanum run --kind dotnet -- sh -c 'dotnet restore Arcanum.Steamwoven.sln --locked-mode && dotnet build --no-restore -v q --nologo && dotnet test --no-build -v q --nologo --logger "console;verbosity=minimal" --filter "TestCategory!=RealData"'`.

Tests: `python3 -B -m unittest tools.local_dev.test_run_log` (error lines, summary, kept logs, quiet and `--verbose`
wrapper runs).

## Root solution synthetic checks

ARC-WORLD-001 uses the terrain endpoint with
`ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-001`, a separate configured ignored
`artifacts/world-builds` root and explicit WORLD selection. It retains the
seven terrain gates plus focused tests and graphical original source/export
integration gates. Source corrections precede the one exact-head export;
later ordinary evidence stays outside the sealed bundle. See the
[WORLD policy](../rendering/terrain-viewer.md#integrated-world-diagnostic-policy).
ARC-GPAD-004 uses that same closed terrain endpoint with literal task
`ARCANUM_TERRAIN_EXPORT_TASK=ARC-GPAD-004`, never the gamepad-lab endpoint.
It additionally requires source/exported WORLD persistence and dual-hostfxr
gates, with isolated input and zoom paths; the task/root/head v2 binding,
capacity/lease/frozen-observation and finalized-rerun rejection are unchanged.

ARC-INPUT-001 (diagnostic palette) uses that same endpoint/root with literal
`ARCANUM_TERRAIN_EXPORT_TASK=ARC-INPUT-001` and exactly the ARC-RENDER-007 gate set;
the synthetic source/exported gates additionally require its palette markers
([contract](../input/diagnostic-palette.md)). ARC-WORLD-007 (LMB click-to-move) uses
literal `ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-007` with the same gate set; its route
matrix runs in the `area-grid` probes ([contract](../world/click-route-runtime.md)).
ARC-WORLD-009 (cave and return) uses literal `ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-009`
with the same gate set; its area-original gates load the cave input and require the
transition smoke ([viewer](../rendering/terrain-viewer.md#crash-site-cave-and-return-arc-world-009)).
ARC-RENDER-008 (passability overlay) uses literal `ARCANUM_TERRAIN_EXPORT_TASK=ARC-RENDER-008`
with the same gate set and, like ARC-WORLD-009, the cave input; its synthetic gates require the
overlay markers and the native WORLD/AREA observations must print the original overlay check
([contract](../rendering/passability-overlay.md)).
ARC-DEV-015 (universal map-mode build) uses literal `ARCANUM_TERRAIN_EXPORT_TASK=ARC-DEV-015` with the terrain gates,
the focused tests and the exported host check only: no gate reads the pinned Crash Site inputs, so it exports on any
installation ([local workflow](local-development.md#universal-map-mode-candidate-arc-dev-015)).
The bounded grid source candidate uses literal
`ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-003` in that same endpoint/root. It retains
the sixteen terrain/WORLD/GPAD/Human gates and adds source/exported original
grid structured reports; all eighteen require task/head/log-bound actual-exit
receipts. Use an owned local home and the independent ignored human reference,
never package original pixels. Before the one clean final-head export, run the
standalone viewer tests, root checks below, focused original bridge/Data tests,
headless and native graphical synthetic checks, original human/grid native
readback and isolated process-restart persistence. Exact ordinary launch and
post-runtime complete manifest/payload/evidence/signature integrity are later
separate observations, not inferred from export exit zero.

Research Python self-tests require a Python supporting their syntax (3.10+;
use the available current interpreter, not macOS system Python 3.9). The local
endpoint remains standard-library-only and uses its documented interpreter.

Run from repository root:

```sh
"$DOTNET" restore Arcanum.Steamwoven.sln --locked-mode
"$DOTNET" build Arcanum.Steamwoven.sln --no-restore
"$DOTNET" test Arcanum.Steamwoven.sln --no-build --filter "TestCategory!=RealData"
python3 -B tools/research/dat_probe.py --self-test
python3 -B tools/original-oracle/oracle.py --self-test
```

The `OriginalExe` tests are part of the solution run and stay inconclusive without
`ARCANUM_EXE` and the oracle's files ([function oracle](#function-oracle)).

The root solution includes Data, Presentation, Scripting, [headless Simulation movement](../world/grid-movement-runtime.md), [Session state](../world/game-session-runtime.md), the [world runtime](../world/record-resolver.md) (`Arcanum.World`), the [executable interpreter](../Arcanum.Executable.md), the ART viewer and terrain-inspect. It does **not** include the standalone terrain viewer/tests, research C# probes, or display/gamepad labs. Use their specific commands: [ART viewer](../Arcanum.Godot.md), [terrain viewer](../rendering/terrain-viewer.md), [display lab](../../experiments/display-ui-lab/README.md), [gamepad lab](../../experiments/gamepad-lab/README.md). Launch Godot projects with Godot; `dotnet run` applies to executable CLIs/harnesses, not the Godot adapter projects.

ARC-GPAD-005 uses the **same closed Gamepad endpoint**, not terrain:

```sh
ARCANUM_LOCAL_HOME=OWNED_HOME ARCANUM_LOCAL_ROOT_ID=gpad005 \
  ARCANUM_GAMEPAD_EXPORT_TASK=ARC-GPAD-005 TMPDIR=OWNED_TMP \
  experiments/gamepad-lab/tools/validate-macos.sh
```

Configure the separate ignored `artifacts/gamepad-builds` root in that owned home first; use a verified single-payload estimate if no accepted/current same-tool measurement exists. `ARC-GPAD-003` remains the default export task; other export choices reject, while readers retain 001/002/003. The 005 endpoint executes locked lab restores/Core `--all`, actual SDK/editor bridge, source and exported production-scene **54-tuple** GUI matrices plus native baselines, isolated fresh-process preferences/migration, shared input/WORLD headless+graphical/root regressions, lifecycle-policy tests, smoke/plist/codesign and shared fail-closed report validation. Its lease spans capacity sampling/freeze, manifest/inventories/seals and registration. Logs and all receipts must bind the actual exact clean committed candidate, build/runtime inputs and exit; an old dirty SOURCE pass cannot supply a new stable-head matrix. After registration, ordinary no-test/no-injection launch and postruntime signature/digest checks remain separate. See [presets](../input/gamepad-presets.md) and [local workflow](local-development.md#gamepad-presets-artifact-arc-gpad-005); GUI simulation is not physical Xbox Bluetooth/comfort validation.

Gamepad's existing universal macOS debug preset publishes both `osx-x64` and
`osx-arm64` with `ExportDebug`. Each of the Lab, Core and shared Input projects
has a separate committed `packages.ExportDebug.<RID>.lock.json`; the scoped
MSBuild target selects that exact project/RID path and requires locked restore.
Debug/editor locks remain separate (GodotSharpEditor is Debug-only in the pinned
SDK). The 005 receipt binds all six consumed lock paths/bytes/digests and actual
child MSBuild witnesses; SOURCE/EXPORT authority, writer and registration retain
the complete candidate-map guard. Missing, altered or cross-project pins reject.
No tracked lock is excluded or restored after a build/export.

## HD font tracing

ARC-DEV-355, [`tools/hd_font`](../../tools/hd_font/hd_font.py), page [HD font tracing](hd-font.md). Python tool, standard library plus
four locked packages (numpy, scipy, Pillow and the GPLv2+ potracer, wheel hashes in `tools/hd_font/requirements.lock`) installed into
`local/hd-font/venv` on the first real run; `--dry-run` needs none. It runs no Godot and no `dotnet`, so it does not use the launch queue.

```sh
python3 -B tools/hd_font/hd_font.py self-test    # task check: synthetic fonts drawn by the test, no game file; a failed install is a failed check
```

## Function census

ARC-RES-106, [`tools/research/exe_census.py`](../../tools/research/exe_census.py): lists the function starts of the pinned
`Arcanum.exe` into [`docs/verification/census.csv`](../verification/README.md#function-census) with `llvm-objdump` (Xcode's
copy, or `LLVM_OBJDUMP`); no Ghidra, no extra install, about two seconds, the disassembly is not stored.

```sh
python3 -B tools/research/exe_census.py --self-test
python3 -B tools/research/exe_census.py --exe "$DAT_ROOT/Arcanum.exe"
```

## Ghidra text export (ARC-DEV-377)

Checks against the original executable start with a plain text search instead of reading the file again: a headless Ghidra
export of the decompiled C of every function (6,711 of the pinned 1.0.7.4 `Arcanum.exe`, about 26 MB, under two minutes to
generate) lives in `research-private/ghidra-export/c/` on the owner's Mac. It is derived from the original executable, so it is never
committed, quoted or copied into git or a PR; a check still records only behaviour in prose (see the
[original executable check](../research/original-executable-check.md)). The decompiler's variables and types are a guess: confirm
anything that matters in the disassembly (`listing/text.asm`, [below](#exe-exports-beyond-the-decompiled-c-arc-dev-379)) or with the
[function oracle](#function-oracle). The first pass (ARC-DEV-377, 4,365 functions) was extended by ARC-DEV-379: the census starts
missing from the project, a wrongly non-returning function and the code Ghidra never disassembled were added, so `c/` covers every
[census](#function-census) start.

- `index.tsv`: address, name, size in bytes, status, file, signature, one row per function; names are Ghidra's (`FUN_<address>`, or
  library names such as `_malloc`), the project's own names are not applied. `extra/function-origins.tsv` says where each
  function came from (below).
- `funcs/<address>.c`: one file per function (eight lowercase hex digits); line 1 is the address, name and size, line 2 the callee
  names, then the C.
- Search with `rg`: callers of a function `rg -l "FUN_0040da50" research-private/ghidra-export/c/funcs`; a constant or string
  `rg -n "0x3ec" research-private/ghidra-export/c/funcs`; the function at an address `cat .../funcs/004a6900.c` or
  `awk -F'\t' '$1=="004a6900"' .../index.tsv`.
- Limits: 6 rows have status `fail` (2-3 byte fragments); functions that came from the gap scan (origin `gap-scan`) are orphan
  code that nothing in the image calls or points at, some of them pieces of a larger function, so read them as fragments; 13 bodies
  could not be extended without merging functions (the C runtime near the end of the code and a few game functions, listed in
  `extra/bodies.tsv` with `overlap`); the pre-ARC-DEV-379 export is kept as `c-first-pass/` until the owner deletes it.

## Exe exports beyond the decompiled C (ARC-DEV-379)

What a reader of the executable needs besides the C, all in `research-private/ghidra-export/` (never in git; `README.txt` there is the
index and is the first thing to read). The sources are Ghidra 12.2 (Apache-2.0) and `python3` scans of the file; nothing else was
installed (no radare2/rizin; `llvm-objdump` from Xcode only for the leftover gap ranges).

| Folder | What it holds | Use it for |
| --- | --- | --- |
| `listing/` | Ghidra listing of every block: `text.asm` (code, jump tables inline, `; FUNCTION` markers), `data.txt`, `rdata.txt`, `idata.txt`, `rsrc.txt`, `Headers.txt`, `reloc.txt`, `IIDKing.asm`; typed data and strings shown, untyped bytes as `db[N]` hex rows | the exact instructions behind a decompiled line (operand width, signedness); data tables and constants by address |
| `data/` | `sections.tsv`, `functions-all.tsv` (with its 139 thunks, which have no file in `c/`), `imports.tsv`, `strings.tsv` (strings Ghidra defined), `symbols.tsv`, `globals.tsv` (every referenced address outside the code with its readers, writers and pointer holders) | who reads or writes a global, who references a string, imported functions |
| `raw/` | `pe-info.txt`, `resources.tsv` and `resources/` (8 PE resources: icons, version info), `strings-raw.tsv` (every ASCII run of 4+ characters, with referencing dwords), `pointers.tsv` (every dword equal to a function entry), `ptr-tables.tsv` (runs of code pointers: jump and handler tables) | strings Ghidra did not define, handler/vtable-like tables, the header |
| `extra/` | `function-origins.tsv`, the logs of each creation step, `gaps.tsv` and `gaps.asm` (the 126 ranges, 711 bytes, of code that no function covers; `llvm-objdump` text of each), `noreturn.tsv` | what was added and what is still open |

The executable has no relocation table (the `.reloc` section is empty), so xrefs are Ghidra's plus the raw dword scans above; a code
address built in a register is invisible to both. `.rdata` is 16 KB, the tables live in `.data` and, for jump tables, inside `.text`.

How `c/` reaches every census start (ARC-DEV-379; the Ghidra project copy is `research-private/ghidra-proj-full`, the first-pass project
`ghidra-proj` is untouched; the scripts are in `ghidra-export/scripts/`, run with `run-full.sh [-ro] SCRIPT.java ARGS` through the headless
queue):

1. `CreateCensusFunctions.java census.csv out.tsv`: disassemble and create a function at each census start that is no function yet
   (1,474 created, 0 failed, 3 inside other functions; 1,452 are still separate functions after step 2, the rest were absorbed into larger bodies).
2. Ghidra had marked one function (`FUN_00406d40`, about 950 call sites) non-returning, which cut every caller after the call and left about
   240 KB of code undisassembled. `FixNoReturn.java 00406d40` clears the flag, `FixCallOverrides.java 00406d40` removes the "call,
   return" override of each call site, `RecomputeBodies.java` re-flows every function body (180 grew). Do not run `analyzeChanges` or
   the analysers afterwards: they infer the flag again.
3. `exe_tables.py`/`gap_ranges.py` find the remaining undisassembled code (a `db[N]` row of `text.asm` that is not padding): the gap
   starts some dword points at get a function (`CreateAtList.java`, 50), every other gap start gets one from `CreateGapFunctions.java`
   (929 created, 834 of them still separate functions at the end, 120 starts rejected as not code). What is left is in `extra/gaps.*`.

Regenerate every export from the project (about two minutes; `OUTDIR` must not exist; the `c/` files differ from a previous run only in the
order of the callee names on line 2):

```sh
./arcanum run --kind headless --expect 'ghidra full start' -- research-private/ghidra-export/export-all.sh OUTDIR
```

A Ghidra upgrade or a new analysis needs steps 1-3 again on a copy of the project, in that order.

## Exe exports: derived extras (ARC-DEV-385)

What the Ghidra export (the two sections above) still lacked, found by checking the executable's sections, the export and the install
folder, and what could be got without a paid tool. [`tools/research/exe_export_extras.py`](../../tools/research/exe_export_extras.py)
(python3 standard library only, about two seconds) reads the pinned `Arcanum.exe` (it refuses any other SHA-256), the Ghidra listing and
tables and the registry [`functions.csv`](../verification/functions.csv), and writes five folders next to `c/` in
`research-private/ghidra-export/`. They are derived from the original and never go into git, a PR or a quote; a check still records only
behaviour in prose.

```sh
python3 -B tools/research/exe_export_extras.py --self-test    # task check: synthetic image and listing, no game file
python3 -B tools/research/exe_export_extras.py --exe "$DAT_ROOT/Arcanum.exe" --export research-private/ghidra-export --dry-run
python3 -B tools/research/exe_export_extras.py --exe "$DAT_ROOT/Arcanum.exe" --export research-private/ghidra-export [--replace]
```

| Lack in the first exports | Found | Folder (`research-private/ghidra-export/`) |
| --- | --- | --- |
| The listing of one function next to its C (operand width, sign, wrap) means searching a 36 MB file | `listing/text.asm` split at the `; FUNCTION` lines | `asm/funcs/<addr>.asm`, 6,856 files, thunks included |
| Which source module and line a function comes from | the original's own `assert` calls push the file name (5 source files of the original: Anim.c, AnimPrivate.c, AnimFX.c, MagicTech.c, Intgame.c), the line, the kind text and the expression text; 332 sites | `source-map/asserts.tsv` (function, site, handler, file, line, expression), `files.tsv` (per file), `module-strings.tsv` (other strings that name a module) |
| Name tables as tables (field names, anim goals, damage types, script action names) | records that start with a text pointer: 46 tables, 1,575 rows; one of them is the 338 names `obj_f_begin`..`obj_f_max` (the object field order), others the anim-goal, script-action and effect names beside their handler functions | `tables/str-tables.tsv` (address, stride, rows), `str-table-rows.tsv` (row address, text, the other dwords resolved) |
| The decoded initial data of `.rdata`/`.data` (the listing shows untyped `db[N]` rows) | every non-zero aligned dword with its target (text, function, data, float, small number), zero runs collapsed, text bytes skipped | `tables/data-dwords.txt` |
| What the code stores into globals at start-up | `MOV [global], imm` into `.data`/`.rdata`: 1,156 stores of constants (the file's `.data` is 1.7 MB in memory but 221 KB on disk, the rest is zero-filled and set by code) | `tables/imm-stores.tsv` (function, site, width, global, value, target) |
| Calls the decompiler shows only as a variable | `CALL`/`JMP` through a register or memory: 1,634 sites (563 through a global slot, 291 through a jump table, 71 through an import, 709 through a register) | `tables/indirect-calls.tsv` (slot and import name where known) |
| The project's names next to Ghidra's | `docs/verification/functions.csv` joined by address: 3,614 rows | `names/functions-named.tsv`; `registry-not-function.tsv` lists the 21 registry addresses that are no function start in the export |
| The other binaries of the install | PE header, sections, imports and exports of the 11 DLL/ASI/EXE files beside the executable (hashes, sizes) | `other-binaries/index.tsv` and `<file>.txt` |

Limits, each checked and settled:

- **Run-time state is not in any file.** The zero-filled part of `.data` (about 1.5 MB) holds what is loaded from the DAT archives or
  computed while the game runs. `imm-stores.tsv` gets only the constants the code writes. For what code computes, run the function in the
  [oracle](#function-oracle) (single functions) or read the data files; a whole-process dump would need the game running under a
  Windows layer (CrossOver, a paid tool) and a debugger, neither of which the project uses, so it is not offered.
- **Resources:** the executable has 8 PE resources (6 icons, the group icon, the version block; no dialogs, menus, string tables or
  cursors), all already in `raw/resources/`. All game content (maps, protos, MES text, art, sound) lives in the DAT archives and
  `modules/`, which the [data probes](../research/data-census.md) read, not in the executable.
- **Types:** the project has no debug information (no PDB, no debug directory, no exports); Ghidra's struct types are only what its
  analysis guessed. Field order and names come from the `obj_f_*` table above and the project's records, field layouts from the research
  documents.
- **Code reached by a computed address** has no xref: the executable has no relocation table, so Ghidra and the scans are both blind to
  it; `indirect-calls.tsv` lists the sites, whose targets must be read from the table or registers that feed them.
- **String tables are a heuristic:** a table is a run of 3+ records at a fixed stride whose first dword points at a text, so a stride can
  be a multiple of the true one; records with a null first pointer inside a table are kept, trailing ones are cut. `data-dwords.txt` is the complete check.
- **Third-party binaries** (Miles Sound System `Mss32.dll` and the `.asi` codecs, Bink `binkw32.dll`, the DirectDraw compatibility
  `ddraw.dll`, WON `mm_won.dll`, `SierraPt.dll`, `msiexec.exe`) get the PE summary only, no decompilation: their behaviour is the
  vendors', and the game's calls into them are in `data/imports.tsv`. `RemoveProtos.exe` (a Troika tool by its name and strings, which mention the prototype folder) is the one original program besides the game; a Ghidra project of it is a task of its own if one needs it.

## Function oracle

ARC-DEV-007, [`tools/original-oracle`](../../tools/original-oracle/README.md), method in the
[original executable check](../research/original-executable-check.md#function-emulation-oracle).

| Tool | Version | Use |
| --- | --- | --- |
| [Unicorn](https://pypi.org/project/unicorn/) | 2.1.4, pinned with wheel hashes in `tools/original-oracle/requirements.lock` | Emulates single x86-32 functions; installed by `oracle.py` into `local/original-oracle/venv` (or `ARCANUM_ORACLE_VENV`) on first use. Bindings BSD, engine GPLv2; never vendored or referenced by a C# assembly. |
| [Ghidra](https://github.com/NationalSecurityAgency/ghidra/releases) | 12.2 (Apache-2.0, brew formula `ghidra`), with `openjdk@25` | Optional headless reading and function discovery; its project stays in `research-private/`. Not needed by the oracle. Text export of every function: [Ghidra text export](#ghidra-text-export-arc-dev-377). |

```sh
python3 -B tools/original-oracle/oracle.py --self-test   # root check: synthetic code, no game file
ARCANUM_EXE=/path/to/Arcanum.exe ./arcanum oracle         # every function: generate, then the OriginalExe tests
./arcanum oracle --function 0x4A6900                      # one function
```

`ARCANUM_EXE` is the installed retail executable (SHA-256 `9bd80511…3678d52`, the
[pinned 1.0.7.4 file](../research/original-executable-check.md#inputs)); `./arcanum oracle` also takes `--exe` or
finds `Arcanum.exe` in the configured game folder. Generated files go to `research-private/oracle/` of the
checkout (`oracle.py generate` also takes `--out` or `ARCANUM_ORACLE_DIR`; the tests read `ARCANUM_ORACLE_DIR`) and
never into git. `./arcanum oracle` sets `ARCANUM_ORACLE_REQUIRED=1`, so a missing file fails
the tests instead of leaving them inconclusive, and `ARCANUM_ORACLE_RECEIPTS=docs/verification/receipts`, where each
test writes its [receipt](../verification/README.md#oracle-receipts) to commit.

## Opt-in original data

Original inputs remain read-only and local. Set only the inputs needed for the selected check; never commit actual paths or content. Missing required inputs fail or make an invocation inconclusive, not verified. Use `--no-build` only after building the corresponding project/configuration.

| Check | Required local configuration | Narrow filter / source contract |
| --- | --- | --- |
| Executable interpreter | `ARCANUM_EXE`; the replays also the oracle files of 0x4A6980 and 0x4A68B0 (`ARCANUM_ORACLE_DIR`, default `research-private/oracle`) | Executable project, `TestCategory=OriginalExe`; [contract](../Arcanum.Executable.md#verification) |
| Prototype builder | `ARCANUM_EXE` and `ARCANUM_GAME_ROOT` (the installed game folder; inconclusive without it); the comparison also `ARCANUM_ORIGINAL_PROTO` (a folder of the 1,423 files the original wrote); `ARCANUM_PROTO_OUT` writes the built files for a look (refused inside a git repository) | Executable project, `FullyQualifiedName~PrototypeBuilderRealData`, `TestCategory=OriginalExe`; about one minute in Release (`-c Release`: Debug is several times slower), the builds run in parallel; [contract](../Arcanum.Executable.md#the-prototype-builder) |
| DAT R1 | `DAT_ROOT`, eight documented candidates | Data project, `FullyQualifiedName~DatArchiveTests&TestCategory=RealData`; [DAT API/evidence](../Arcanum.Data.md) |
| Resource precedence | `DAT_ROOT` = the installed game folder (with `modules/` and `data/`) | Data project, `FullyQualifiedName~ContentStackRealData&TestCategory=RealData`; probe `tools/research/Arcanum.ContentProbe` (`--self-test`, `--game-root`); [research/API](../research/content-precedence.md) |
| GOG installer unpack (ARC-DEV-074) | `ARC_GOG_INSTALLER` = the GOG offline installer; `innoextract` installed (`PATH` or a Homebrew folder); unpacks into a temporary folder, about 1.2 GB | Session project, `FullyQualifiedName~GogInstallerUnpackerTests&TestCategory=RealData`; [research](../research/gog-installer-unpack.md) |
| Map aggregate builder (ARC-DEV-082) | `ARCANUM_EXE` and `ARCANUM_GAME_ROOT` (an installation with the module and the prototype files); the comparison also `ARCANUM_ORIGINAL_MAPS` (the aggregates the original wrote, such as `modules/Arcanum/maps`); `ARCANUM_AGGREGATE_OUT` writes the built files for a look | Executable project, `FullyQualifiedName~MapAggregateBuilderRealData`, `TestCategory=OriginalExe`, `-c Release` (about 10 s); [contract](../Arcanum.Executable.md#the-map-aggregate-builder) |
| Prototype provision (ARC-DEV-069) | `ARCANUM_GAME_ROOT` = an installation the original ran (its `Arcanum.exe` is read; a copy made of links without `data/proto` and `modules/Arcanum/maps` is built from, in a temporary settings folder (prototypes and the map aggregates); `ARCANUM_ORIGINAL_PROTO` is not needed: the installation's own `data/proto` is compared by name and length) | Session project, `FullyQualifiedName~A_fresh_copy&TestCategory=OriginalExe`, `-c Release` (about 30 s); [research](../research/prototype-extraction.md#e4-results-arc-dev-069) |
| Technology census (ARC-DEV-363) | `DAT_ROOT` = the installed game folder | Session project, `FullyQualifiedName~TechCensusRealDataTests&TestCategory=RealData` (a few seconds); one state per technology record 140..222 (RUNS, HERO, PART, INERT) and the not-ready components on the hero and an NPC; a failure prints the changed rows to paste into the test; [technology audit](../research/technology-audit-res-104.md#7-smoke-and-test-coverage) |
| Spell census (ARC-DEV-356) | `DAT_ROOT` = the installed game folder | Session project, `FullyQualifiedName~SpellCensusRealDataTests&TestCategory=RealData` (a few seconds); a failure prints the changed rows to paste into the test; [census](../research/magic-audit-res-103.md#spell-census-ma-0) |
| DLG reader | `DAT_ROOT` | Data project, `TestCategory=DlgRealData`; [DLG reader](../formats/dlg-reader.md) |
| Dialog transition | `DAT_ROOT` | Scripting project, `TestCategory=DlgRuntimeRealData`; [runtime](../scripting/dialog-transition-runtime.md) |
| Terrain | `ARCANUM_TERRAIN_MODULE`, `ARCANUM_TERRAIN_RESOURCE` | Data project, `FullyQualifiedName~TerrainLoaderTests&TestCategory=RealData`; [loader](../world/terrain-loader.md) |
| Static objects | `ARCANUM_STATIC_MODULE`, `ARCANUM_STATIC_RESOURCE`, `ARCANUM_STATIC_PROTOTYPES` | Data project, `FullyQualifiedName~StaticObjectLoaderTests&TestCategory=RealData`; [loader](../world/static-object-loader.md) |
| Complete exterior area | `ARC_AREA_MODULE`, `ARC_AREA_RESOURCE1`, `ARC_AREA_RESOURCE2`, `ARC_AREA_MOBILE`, `ARC_AREA_PROTOTYPES`, `ARC_AREA_ORACLE` | Data project, `FullyQualifiedName~AreaOriginalTests&TestCategory=RealData`; [profile, independent oracle and measurements](../world/area-loader.md) |
| Crash Site cave | `ARC_AREA_MODULE`, `ARC_AREA_RESOURCE1`, `ARC_AREA_RESOURCE2`, `ARC_AREA_PROTOTYPES`, `ARC_CAVE_MOBILE` (the cave's game-generated object file next to `areaMobile`) | Data project, `FullyQualifiedName~AreaCaveOriginalTests&TestCategory=RealData`; [cave profile](../world/area-loader.md#crash-site-cave-profile) |
| Whole-core step assessment | The Area variables except the oracle, plus `ARC_AREA_EFFECTS`, `ARC_AREA_PATCH` | Data project, `FullyQualifiedName~AreaStepOriginalDataTests&TestCategory=RealData`; [API and observations](../world/area-step-assessment.md) |
| Function oracle | `ARCANUM_EXE` (the installed `Arcanum.exe`) and the oracle's files from `./arcanum oracle` (`ARCANUM_ORACLE_DIR`, default `research-private/oracle/`) | Simulation project, `TestCategory=OriginalExe`; [function oracle](#function-oracle) |
| Click route on the whole core | `ARC_AREA_ORIGINAL=1` and the area coordinator roles (module, resource1/2, mobile, prototypes, effects, patch) | Terrain Viewer tests, `FullyQualifiedName~AreaRouteOriginalTests&TestCategory=RealData`; [route core](../world/click-route-runtime.md) |

Example, with `DAT_ROOT` already set locally:

```sh
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build \
  --filter "FullyQualifiedName~DatArchiveTests&TestCategory=RealData"
```

A solution-wide `TestCategory=RealData` run selects all these input families; `DAT_ROOT` alone is insufficient. Viewers also require their documented explicit arguments and original-data fingerprint gates. Synthetic tests, original-data structural checks, graphical readback, human visual observations and physical device evidence are separate results. Headless dummy rendering cannot establish graphical pixel readback. Documentation refreshes validate links/configuration, not runtime behavior.
