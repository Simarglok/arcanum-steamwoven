# Toolchain and verification entry points

Configuration is authoritative: [global.json](../../global.json) pins .NET SDK `8.0.425` with roll-forward disabled; [Directory.Build.props](../../Directory.Build.props) targets `net8.0`; [Directory.Packages.props](../../Directory.Packages.props) centralizes package versions. Godot adapters/labs use `Godot.NET.Sdk/4.7.2`, Godot **4.7.2 .NET** and matching .NET export templates. Development currently targets macOS. Version changes require a separate scoped decision; do not replace retained shared installations implicitly.

The player's local HD interface pack (staging build, diff-only update, backup, rollback) has its own page: [HD pack workflow](hd-pack.md) (ARC-DEV-329, `tools/hd_pack/hd_pack.py`; tests `python3 -B -m unittest tools.hd_pack.test_hd_pack`).
The Steamwoven kit manifest (ARC-DEV-2454, `tools/steamwoven_ui/kit.json`, made from the [census](../ui/steamwoven-interface-census.md) by `python3 -B tools/steamwoven_ui/build_kit.py`, `--check` fails on drift) is validated by `python3 -B tools/steamwoven_ui/kit_check.py` (`KIT CHECK PASS`); tests `python3 -B -m unittest tools.steamwoven_ui.test_kit_check`. These three are task checks, run by a change to the manifest, the census tables or these tools, and are not in the root check list. It holds identifiers, sizes and verdicts only, never pixels.
The Steamwoven context tone check (ARC-RES-2455, `tools/steamwoven_ui/tone_check.py`, [SWI-GEN-9](../ui/steamwoven-interface-trd.md); masks `tools/steamwoven_ui/prompts/tone_masks.json`) is standard library only: `python3 -B tools/steamwoven_ui/tone_check.py batch --originals local/steamwoven-ui/refs --results local/steamwoven-ui/outbox/style-anchors-contexts` (about 5 s per screen; exit 1 = a region over a limit) and `... fit SCREEN ORIGINAL RESULT OUTPUT --material paper`; tests `python3 -B -m unittest tools.steamwoven_ui.test_tone_check` (about 55 s, a task check, not in `run_tests.py`). The glyph tools (ARC-UI-3058, standard library only; [what they do](../ui/steamwoven-kit-pilot.md)): `tools/steamwoven_ui/glyph_cells.py mask|cells|plate` draws a button glyph by rule from the original's frame and the kit plate with a soft shadow, `glyph_tone.py measure|fit|report` fits the tone; their synthetic-image tests are in `run_tests.py` (`test_steamwoven_glyph_cells.py`, `test_steamwoven_glyph_tone.py`).
The Steamwoven material cut-outs and tiles (ARC-RES-2455, `tools/steamwoven_ui/cutouts.py`, [SWI-GEN-10](../ui/steamwoven-interface-trd.md); list `tools/steamwoven_ui/prompts/cutouts.json`) are standard library only: `check`, `cut` (the cut-outs, the context crop of each small one, the provenance record), `tiles-inbox` (then `codex_batch.py run style-anchors-tiles`) and `tile-check`; tests `python3 -B -m unittest tools.steamwoven_ui.test_cutouts` (task check, under a second).
The Steamwoven kit assembly tool (ARC-DEV-2972, `tools/steamwoven_ui/assemble.py`, [page](../ui/steamwoven-kit-assembly.md)) is standard library only; its tests `python3 -B -m unittest tools.steamwoven_ui.test_assemble` also run in the batched `tools/local_dev/run_tests.py`. Its sibling `tools/steamwoven_ui/pack_plan.py` (ARC-UI-3077, standard library only, tests `python3 -B -m unittest tools.steamwoven_ui.test_pack_plan`, batched the same way) writes the pack importer's plan and alpha masks from the assembled frames ([workflow](hd-pack.md#kit-assembled-frames-in-the-pack-arc-ui-3077)).
The Steamwoven kit fit tool (ARC-DEV-2459, `tools/steamwoven_ui/fit.py`, [section](#steamwoven-kit-fit-arc-dev-2459)) is standard library only; its tests `python3 -B -m unittest tools.steamwoven_ui.test_fit` (about 5 s) also run in the batched `tools/local_dev/run_tests.py`.

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
- A stage that fails only after another one is a leaked-state trap, not a product failure (ARC-DEV-401): check the reducer arming, the simulated pad, `_worldTestTime` and the pointer first. The input reducer is armed only by two complete neutral frames after a quarantine, and an earlier stage leaves it quarantined (pad release, focus change), so the `inv-ui` scroll steps feed neutral frames first; before this the group `inv-ui,loot-room,pickpocket` stopped with `the arrow key scrolled the view under the dialog` while `inv-ui` alone passed. In an automated run the window's real mouse enter/exit notifications are ignored and `WorldPointerForSelfTest(position)` puts the test pointer inside the window (`HoverAtForSelfTest(null)` still leaves it outside until a `HoverReleaseForSelfTest()`).
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
    # each also takes --no-build, --keep-settings, --take-focus and --dry-run; pr and map also --no-config-check

- **Smoke windows, focus and sound (ARC-DEV-403)** <a id="smoke-windows-and-focus-arc-dev-403"></a>. A smoke opens a real Godot
  window, and Godot makes itself the foreground application when it starts (it asks for it through `TransformProcessType`,
  `setActivationPolicy:`, `activateIgnoringOtherApps:` and `makeKeyAndOrderFront:`), which took the keyboard focus from the
  owner on every smoke, and every lane's smoke does it again. Window flags do not stop that (Godot's `no_focus` and
  `unfocusable` act after the application is already active; a minimized window stops drawing). So on macOS each
  `./arcanum smoke` builds one small native library from `tools/local_dev/focus_guard_native/arcanum_nofocus.m` (clang,
  about a second, into the run's settings folder, ad-hoc signed) and loads it into the Godot process of each window
  smoke with `DYLD_INSERT_LIBRARIES` (the Godot app's entitlements allow it; a headless run does not need it). The library
  turns those calls into a background-only process: it is never the active application, shows no Dock icon and no window
  on the screen, and keeps drawing, so the viewer's own frame readbacks (viewport textures) work as before; the smokes
  drive the viewer with synthetic input and simulate the window focus themselves. Only smoke runs load it, no player
  package or hand-started viewer does. What it does not cover: a check that reads the real screen or needs a visible
  window (none of the smokes does); watch a smoke by hand with `--take-focus`, which runs the old way. Off macOS the run is unchanged and silent; without
  clang or when the build fails, it prints `SMOKE note: ...; the smoke windows may take the focus` and goes on without
  the guard. The guard needs the Godot build's `allow-dyld-environment-variables` entitlement (the app in `/Applications` has it; a
  `GODOT=` override without it silently runs unguarded), and a `--env DYLD_INSERT_LIBRARIES=...` replaces it. Smokes of
  other checkouts get the guard once their branch has this change.
  **Sound.** An automated run of the viewer (a smoke, a windowed self-test) mutes the Master audio bus at start (the log
  line `ARC-DEV-403 automated run: Master bus muted`), so the tests are not heard: the audio device stays the real one,
  the sounds still start and play, and the audio checks (`audible=True`, the loop and voice "is playing" checks) hold, as
  they would not under the Dummy driver. `--env ARC_SMOKE_SOUND=1` on a smoke lets it be heard.
  `--env ARC_SMOKE_MODULE=NAME` (ARC-RENDER-3107) names another module archive of the same installation (`<game>/modules/NAME.dat`, for example `Vormantown`) for the viewer's `--module`;
  the launcher reads it, the file must exist and not be a link (it is not pinned by size and hash like the configured module), and the viewer's area load still opens the Arcanum module only (the Vormantown maps do not load yet, see the town-map note).
- **Click and walk changes** ([smoke scope](tasks.md#smoke-scope), ARC-DEV-239). A PR that changes click or walk handling
  always runs `./arcanum smoke pr --stages map` and `./arcanum smoke self-test` (both, every such PR).
- **Sampled stages (ARC-DEV-395, owner decision 2026-11-11; `slides` since ARC-DEV-3116, 2026-11-24).** `container-click` and `walk` took 675 s and 525 s of the
  one Godot slot in that report (600 s and 363 s on an idle Mac when re-measured here) (ARC-DEV-384, deleted; the result is in this section), so by default they check a
  sample; `--all` (on `pr` and `map`, with a run of one of them by `--stages` or its zone; it sets the viewer's `ARC_SMOKE_ALL=1`) runs the
  full stage; the nightly `complete` run and `--zones complete` always run both in full. `container-click` clicks at most 8 containers of the window, spread over the (prototype, admitted open
  outcome) groups, the group of admitted rubbish bins and a locked container first (so at least one bin, and every bin only with `--all`) (`ARC-DEV-395 container-click SAMPLE n of N` names them); `walk` plans and walks the
  route to a goal about 10 window moves from the start (the full route has 71) instead of to the town sector. What the sample no longer proves: for
  `container-click`, the click path of the containers left out (a pick lost behind another object or a roof, a wrong
  hover, a refused or locked answer of that container) and the "every rubbish bin opens" rule over the whole window (it
  holds for the sampled bins); for `walk`, the walk's tail (the 60 or so window moves after the tenth, the arrival at
  the town, the planner on the full 88-sector route). Run `--all` when a change touches picking, containers or the click
  path (`container-click`), or the route planner, the follower, the window loader or the far end of the route (`walk`),
  and before reporting a stage that failed as sampled as fixed. The sampled run reads the same zones: `--auto` selects the `walk` and `container-click` zones as before and they run as a sample unless `--all`. `slides` is the third sampled stage (ARC-DEV-3116, owner decision 2026-11-24: the installed endings' movies play 148 s of its 230 s in real time): its PR run ends each installed movie by the real key-skip path of the movie cover once the movie's checks are done (frames drawn, clocks frozen, audio, frame size), about 90 s; it still asserts the queue, the end game, the slides and the reset, and the movies count as skipped, none as failed. With `--all`, `--env ARC_SMOKE_ALL=1`, the nightly `complete` run and so `./arcanum daily`, every movie plays to its own last frame and the stage asserts they ended by themselves (played, none skipped, none failed), about 230 s. The stage prints `slides smoke mode skip|full|movies passed over` and its PASS line carries `Mode`. What the skip run no longer proves: the movie's last frames and its natural end (the full run does). Without ffmpeg the movies are passed over in both modes, as before.
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
  `created-critter` does, and `script-critters` runs `ProcessAreaAi` in its settle frames for the tile notifications, ARC-DEV-405); a stage
  that passes alone and fails after others can be this. The test time is not reset between stages: its floor is what keeps the next
  observation from being refused as late (`RestoreWorldTestTime`, ARC-WORLD-025). `summon-wall` and `summon-undead` read the hero's hit points before
  and after the burn and failed once in the long list (`25 -> 30`, not reproduced alone or in shorter chains); it now checks the
  hit-point damage the hero carries, which the wall's burn would raise (ARC-DEV-405).
- **Synthetic frame clock (ARC-DEV-1251).** `./arcanum smoke self-test` failed now and then with `GRID_OBSERVATION_NONMONOTONIC` in the
  hover and inventory stages, only on a busy machine. The cause: a synthetic frame (`WorldFrameForSelfTest`) was stamped at the actor
  session's time plus a tenth of a second, and that session time is a wall-clock stamp taken when the session was made; before the first
  frame the world still reads the wall clock, so `OpenHoverGate`'s pad selection (a quarantine) was observed at the later wall time, and
  when the setup took more than 0.1 s between the two, the first frame was older than the queue's newest observation. The frame's time
  is now `SelfTestFrameClock.Next`: a step after the test time, or after `GridInputQueue.Newest` when that is later. `OpenHoverGate`
  waits 150 ms in a self-test so the order shows on every run, not only under load. A new self-test stage that observes before its first
  synthetic frame needs nothing more.
- **Literal frame time under load (ARC-DEV-2005).** The Crash Site smoke failed now and then, only while other builds and smokes ran, with
  `GRID_OBSERVATION_NONMONOTONIC` (`ARC-DEV-065 observation-fault ... WorldFrameForSelfTest`, then `viewer failure stage=ActorPlan`, and the
  `ARC-RENDER-007 frontier=InvalidOperationException` / `native-smoke failure stage=Upload` pair every smoke fault ends with). It was an ordering
  bug that the machine's load only exposed, not a wall-clock stall: `AreaSmokeClick` stamped the click with `SimulationTime.At(_areaSmokeTime)`,
  and a scaled stage (`_areaSmokeTime *= 20`) counts twentieths of a second, so the attack stage's click read 1347 as 1347 seconds while its
  frames were at 67. Whether that stamp reached the grid queue depended on the input safety gate and the UI focus at that moment (a quarantine
  or a callback observation), which the machine's timing decides; when it did, every later literal frame was older than the queue's newest
  observation. The fixes: a click keeps the stage's own test time (`_worldTestTime ??= ...`); and a literal frame time is lifted to what the queue
  or the session has already seen when it lags by at most 5 s (`SelfTestFrameClock.Frame`), so a wait loop that moved the test time on or any
  other small skew between a helper and a stage's frames no longer fails a run (each lift prints `ARC-DEV-2005 frame-lifted`, the first five); a lag over 5 s (a wrong scale) stays refused. A smoke fault now
  prints `ARC-DEV-2005 clock-state` (the stamp, test, session and newest times and the last eight observations) and
  `ARC-DEV-2005 frontier-fault` (the message when it is an `AREA_`/`GRID_` code of letters, digits and a few marks, and the methods; smoke launches only), so the next failure names itself. A failed Crash Site smoke whose
  log holds neither line and no GRID_ code is a different failure: read its first `FAIL` line, do not rerun it blind.
  The same Crash Site run failed a second way under load, in the wall-fade check after the change of place into the cave
  (`AREA_WALL_FADE_NOT_OBSERVED ... unstable=7200 unstable-box=490,770-789,793`): the 300x24 block was the controller prompt strip, an
  overlay on its own canvas layer that follows the device in hand after a hysteresis on the host's clock, so it appeared or went away between
  the check's three captures when frames ran slow. The check now hides that layer (as it hides the HUD) and reports the box of unstable pixels.
  A third way (ARC-DEV-2294, from 2026-11-15, after the relit look of ARC-RES-2000 and the HD profile in the stage) failed on every run with the same numbers
  (`unstable=92006 unstable-box=17,313-601,799`: 92006 pixels of lit terrain, inside a 584x486 box, a few levels apart, `outside` 0 or 90480 with the third capture
  differing from the first only there): the stage runs the check right after the switch to HD, while the HD frames of the terrain chunks were still
  decoding, so the first capture drew the Legacy chunks and the third the HD ones; a Legacy run, or an HD run whose decodes had ended, passed (so it looked like a
  time of day). Each capture of the check now waits for the visual switch and every HD decode to end (the wait of the HD smokes, printed as
  `ARC-DEV-2294 wall capture waited N frames for HD decodes`; `AREA_WALL_HD_NOT_SETTLED` after 1800 frames) and is taken once two consecutive captures are
  identical; a failing fade compare (`AREA_WALL_FADE_NOT_OBSERVED`) with `ARC_AREA_TRANSITION_CAPTURE` set saves the three captures as `-walls-fail-faded|opaque|again.png`.
- **Stage boundary reset (ARC-DEV-408, ARC-DEV-1187).** A stage that passes alone and fails after others had a second cause besides the test
  time: what the earlier stages left in the shared session. `TerrainViewerSmokeStageSet` calls `SmokeStageBoundary`
  (`TerrainViewerSmokeStageReset.cs`) at every change of stage in the viewer. The first call with a hero and a session saves a baseline into a
  private save folder (the production save path) and takes the roll sequence's position (`GameSessionOwner.QueryRandom`); every later call
  loads that baseline back through the production quick-load path and prints `ARC-DEV-408 stage-reset PREVIOUS -> NEXT: session(loaded from
  the baseline), random(...)`. The load puts back everything the session holds: the hero (cell, combat mode, life), every NPC and its life
  (a killed or wounded NPC), the party (the barter merchant joins in `life` and `opening`; `companion-click` and `ai-follower` need a free
  speaker, `pickpocket` a living one), doors, containers, clocks. The roll sequence is restored after the load (`RestoreRandom`), because a
  load does not give the baseline's position back. Limits: (1) what the viewer holds outside the session (its windows, caches, the world
  map's open state) is not reset; (2) the load runs only while the shown window still holds the baseline's hero cell (a map change or a
  window move by `transfer`, `roundtrip`, `stream` publishes the scene on a later frame, which a stage boundary cannot wait for); then the
  narrow reset runs instead (the hero's combat mode, the hero's cell when it is on the shown map, the party members a stage took in, the roll
  position; the NPCs a stage killed, wounded or moved stay as they are), so a stage that needs the start window runs before the stages that move it (`creature-item` runs right after `resources` when that stage runs too: with
  the AI on, the first window's hero is safe, a moved window left it among wolves that killed it); (3) a loaded session is not the fresh
  one in what the viewer computes per frame: the town map's tile under the hero is revealed once per session instance, which cost every
  loaded session one more revision on its first frame (`inv-arrange` counts the revisions of a press), so the baseline takes that reveal
  before it is saved and every load repeats it at once (`RevealWalkedTownMapCell`; it needs the town files open, which is not certain at the very first boundary). A new kind of leak outside the session is one more line in `SmokeStageBoundary`, not a
  change to one stage. `placement-census` also leaves out an NPC that follows the hero (a companion body is not an NPC the host places).
  `ARC_SMOKE_STAGE_RESET=0` (as `--env`) switches the reset off, to tell a leak from a stage's own failure; `=narrow` selects the narrow
  reset only. A process stops at its first failing stage, so a leak fixed here can show the next one that was hidden behind it.
  **The test clock (ARC-DEV-2259).** The reset does not clear the test clock (`_worldTestTime`): a stage that runs frames itself (`life` and others) leaves it set, and
  while it is set a native smoke's `ProcessWorld` returns at once, so no per-frame step runs between a stage's own frames (the scenery step, which builds the
  loaded session's host and reads the art fields, among them). `object-art` failed only in the `stages-main-alone` chain for that reason (`the view draws
  the new art where the T0 body stood`); the stage now calls `ProcessAreaSceneryAnimation` itself in its settle, as the scenery smokes and `barter`
  do (ARC-DEV-2042). A stage that reads the result of a per-frame viewer step must drive that step itself, never rely on `ProcessWorld` running.
- **A stage waits for a state, not for a time (ARC-DEV-1193).** `bar` failed by chance (`feedback settles to Hover with combat
  still on`) because it waited a fixed 0.15 s after a quick click and then read the button: the panel keeps Down for 80 ms
  counted from the first frame actually drawn, and on a loaded machine that draw comes late (the failing run showed 24 ms since
  the draw after the 150 ms wait; reproduced by keeping every CPU core busy). The stage now polls frames until the expected
  frame shows, up to 2 s, and prints a line when that took 200 ms or more; a Down that never ends still fails. A new stage that
  expects feedback after an input waits for that state the same way; to reproduce a load-dependent failure, run the stage in a loop
  with every core busy.
- **Every requested stage runs (ARC-DEV-063).** `PASS` means each requested stage ran: a map smoke passes only when
  its log holds the `PASS` line of every stage in its list (`STAGE_PASS` in `tools/local_dev/smoke.py`, checked against
  the viewer's sources by the tests), the whole map battery (`full`, `map` without `--stages`) that of `map` and
  `resources`; a stage the viewer skipped (silently, as the AI stages without a hero, or with a `NONE`/`SKIP` line)
  makes the step `FAIL exit=3` with `FAIL exit=3 stages not run: ...` and up to three of the log's `NONE`/`SKIP`
  lines (the missing stages' own first). A stage whose own `PASS` line is not the whole check also needs the lines of `STAGE_ALSO_PASS` (ARC-DEV-1556): `transfer` needs the visitor-fight `PASS` after its own, so a `NONE` or `NO-TARGET` fight line fails it. The battery's other stages may report `NONE` where the map lacks what they
  need (a whole map has no window to stream), so it vouches only for those two. Before
  anything runs: a stage that needs a hero (`map` and the whole battery, `cast`, `turn-based`, `knockdown`, `ranged`,
  `container`, `junk`, `hover`, `barter`, `life`, `floaters`, `ai`, `ai-turn-based`, `ai-cast`, `ai-follower`, `premade`, `opening`, `pickpocket`, `loot-worn`, `inv-arrange`, `loot-room`, `transfer`, `follower-home` and the rest of `HERO_STAGES` in `tools/local_dev/smoke.py`) gets
  the full chain's hero of that map (`elf-female` on `elven ruins`), else `--hero human-male`, when no `--hero` is
  given, and says so (`SMOKE hero human-male (auto): ...`); with
  `--main-menu` (no hero) it is refused. A menu stage (`menu`, `scene`, `options`, `bar`, `death`, `perf-menu`) needs no
  `--main-menu`: it gets its own menu process. The stages the viewer runs only without `map` (`barter`, `menu`, `scene`, `options`, `plants`, `bar`,
  `life`, `floaters`, `death`, `ai`, `ai-turn-based`, `ai-cast`, `ai-follower`, `premade`, `opening`, `script-triggers`, `pickpocket`, `loot-worn`, `inv-arrange`, `loot-room`, `wreck`, `altar`, `npc-resident`, `saved-runs`, `npc-door`, `npc-roof`, `npc-tile`, `npc-wander`, `tile-ghost`, `container-click`, `fire-order`, `indoor-fade`, `npc-shot`, `npc-object`, `npc-throw`, `critter-fall`, `save-resume`, `cast-multi`, `companion-click`, `ai-react`, `please-move`, `placement-census`, `spell-end`, `ai-cast-pass`, `hold-hero`, `spell-effects`, `movement`, `spirit-talk`, `healer-talk`, `kerghan-talk`, `sleep`, `bed`, `bed-blow`, `created-scenery`, `object-art`, `worn-light`, `critter-light`, `eye-light`, `summon-flash`, `summon-wall`, `summon-undead`, `tech-craft`, `tech-products`, `item-aim`, `disarm`, `perf`, `perf-menu`) get a run of their
  own (step `stages-alone`, or `map-alone`) when `map` is in the list too. `healer-talk` (ARC-SCRIPT-1790; the Crash Site, `--hero`) talks to the start window's installed healer (Virgil) through the AREA conversation host: the DLG is opened at his h: hub node as a script's dialog action opens it, the healer menu, spell 60 and its paid quote follow, the cast starts through the AI host with the free flag (no PC leads him) and the hero's hit points rise while the talk is still open. A second pass (ARC-SCRIPT-1996) joins Virgil to the hero and opens the same node again: the h: reply is offered for a companion, the pick of the spell casts at once with no price screen, no gold paid and no free flag, and the hit points rise. `kerghan-talk` (ARC-SCRIPT-2504; own process on Kerghan's castle map around his sector, `./arcanum smoke pr --stages kerghan-talk --hero human-male` runs `void-kerghans castle --sector 1,10`) talks to the installed Kerghan (dialog script 1105) through the AREA conversation host: the story talk opens at his greeting and seven replies, taken by response id, end on script line 100; the window closes at once in the Original setting (the diagnostic chrome keeps Close, ARC-SCRIPT-2761) and the script resumes there, the game clock moves by 10,800 s and movie 6 plays through the real decoder when ffmpeg is installed (a key ends it) or is passed over, said so, when it is not; the talk after the task is opened at DLG node 618 (the stage sets global flag 2674 as node 612's reply 614 does; that reply needs an item named 5616) and its four replies end on script line 80: the close runs the change of shape (Kerghan destroyed, a created critter of prototype 27368 on his tile held by the AI host, the deferred call of line 86 waiting and naming it), the clock moves to the call's due time and the call asks for movie 3 (checked when the decoder plays it) and for the end of the game (the stage ends there; the ending is `slides`' business). `tech-craft` (ARC-DEV-362, technology audit slice T-a) needs the bullets the map's bodies hold, so it runs on the cave: `./arcanum smoke map "crash site cave" --stages tech-craft` (on a map with no bullets of the crafted gun's type it fails with that message; its steps are the helpers of `TerrainViewerTechCraftHarness.cs`). `bed` (ARC-WORLD-1162) runs on the cave (`./arcanum smoke map "crash site cave" --stages bed --hero human-male`): the cave's beds counted by script, a bed with no script refuses (message 8), an occupied one (10), the scripted bed seats the hero and a one-hour rest in it heals twice the ground's, the close empties the bed. `bed-blow` (ARC-COMBAT-2167, `./arcanum smoke map "crash site cave" --stages bed-blow --hero human-male`) seats the cave's living non-companion NPC with the most hit points in a drawn, pickable, attackable cave bed (the occupancy seat and the sleeping flags, as the host's standpoint seat writes them), has the hero in combat mode Alt-click the bed from a neighbouring cell, and checks the hero's first blow takes the sleeper out of the bed (the occupied frame goes) and strikes it through the combat path with the check modifier -30, then that the next blow goes the object path on the empty bed (no second critter strike, the goal goes on; the bed's damage changes unless the data makes it invulnerable, as the cave's is, where the object path itself is all that is observed); NONE without a reachable bed or a living NPC with combat values. `tech-products` (ARC-MAGIC-051, technology audit slice T-s) is `tech-craft`'s sibling on the same cave (`./arcanum smoke map "crash site cave" --stages tech-products`): it buys Therapeutics degrees 1 and 2 and Chemistry degree 1, assembles their first records, eats the two stat potions (the hero's effect list grows, record 158 adds +2 strength), uses the poison on a living NPC through the item cursor, reads the module's found schematic documents of those two disciplines through the inventory's Read button (each registers and is destroyed, a second copy of a known one is kept) and assembles the first found record the hero's expertise reaches from the window's Found tab, buys intelligence and Electric degrees up to 6, assembles the power cells (Chemistry degree 2), the powered shield (Electric degree 3: worn, its 15 s deferred call takes one cell and schedules the next) and the maintained armour (degree 6, record 168: a cell a tick), has the cave NPC assemble a learned record from its own list into its own list through the window queued the way the dialog's `su` queues it (the stage gives that NPC a Herbology degree and intelligence 8: the cave's NPCs have no degree, so this shows the window and session path for a crafter, not a real crafter), throws an assembled Explosives degree-1 grenade (record 219) at the cave NPC (a flight that lands off the tile is aimed again, up to three grenades; the item event starts the run and a cave NPC is damaged or resists), and F7/F8 keep effects, items, worn items, cells and the NPC's list (same harness helpers; it needs a living NPC in the cave). `take-return` (ARC-WORLD-3122; the Crash Site, `--hero` and `--debug-mode`, own process) takes ground items and loots body items, gives one to a living NPC, goes through a map exit and back twice and moves the window away and back through the world map; the taken items must stay hidden, unpickable and off the session's ground. `item-aim` (ARC-DEV-365, technology audit slice T-g) opens the item cursor with a script trap device from the use icon and presses a tile (goal 41) and an NPC (goal 39); it needs a hero and a map with a living NPC. `disarm` (ARC-SKILLS-1156, E6-a second half) arms the hotkey bar's Disarm traps entry (skill cursor, interface mode 2), presses a container the stage makes trapped (the native script 30001 written as a map change) with the trap unknown (nothing rolled) and known (a fated press removes it); it needs a hero and a map with a container drawn in its window (NONE without one, or when the content's script 30001 is no trap script). A run whose stages include `debug-travel`, `follower-far` or `inv-carry`
  starts the viewer with `--debug-mode` (the stage needs the debug mode, ARC-DEV-050; ARC-WORLD-120).
- **Stage configurations and grouping (ARC-DEV-391).** A viewer process fixes its map, start sector, hero, main menu and
  debug mode at launch, so only stages with the same configuration share a process. `STAGE_NEEDS` in
  `tools/local_dev/smoke.py` is the one table that says which configuration each of the viewer's stages runs in (default
  map and sector; whether another map or sector fails it, so `only_map`/`only_sector`; main menu, debug mode; a process of
  its own; portable). `--stages` reads it: with no `--map`, `--sector` or `--main-menu` each stage gets its own
  configuration, the stages are grouped into the fewest processes (one per configuration, in order of first appearance;
  the step is named `stages-main`, `stages-wild`, `stages-town`, `stages-shrine`, `stages-crash-site-cave` ... when there
  are several, `stages` when there is one), a *portable* stage (`map`, `resources`, `report`, `knockdown`, `inv-arrange`,
  `created-*`, `summon-*`, `disarm`) rides in a process that already exists on its map (else it gets one), and the stages that end a game, move
  the window, leave state behind or run long (the menu family, `walk`, `census`, `container-click`, `perf`,
  `dead-window`, `critter-fall`, `tech-craft`, `tech-products`, `debug-travel`, `follower-far`, `teleport-follow`, `talk-teleport`, `inv-carry`) each get a process of
  their own (`stages-main-walk`). `--map`, `--sector` and `--main-menu` pin that dimension for every stage; a pinned
  value a stage cannot pass in is refused before anything waits for the queue (`stage plants does not run at --sector
  1452,1291: it needs map ... at sector 1384,239`), and so is `--main-menu` with a stage that needs a hero or a session.
  `--no-config-check` runs the request as given (the grouping stays). The table holds only what is known: where a stage
  was only seen to pass in one place its default is that place and another one is not refused. `junk` runs on the
  ruins (the main map has no container with two items), `critter-light` on `tulla interior - battle - conveyance` (an NPC
  there carries a light; `NONE` on the main map, the town, the ruins and the cave), and `ai-cast` and `npc-door` each get a
  process of their own (they failed after the other stages of their group and passed alone, 2026-11-11). A new stage needs a row
  (the tests fail without one); the zones below are built on this table.
- **Stage times (ARC-DEV-399, Q3 of the diagnosis).** The viewer's map smoke times each stage it runs
  (`TerrainViewerSmokeStageSet`: the chain's own question "does this stage run" is the stage's start, the next one its end) and prints
  `ARC-DEV-399 stage-time NAME ms=N` as each ends (a failing run prints the interrupted stage's too; a whole-battery run with no
  `--stages` times its `map`, `resources`, `transfer`, `roundtrip`, `stream` and `worldmap` parts, `map` being about eighty sub-smokes in one). After
  each step the runner (`tools/local_dev/smoke_history.py`) reads those lines from the step's log and appends one line to
  `smoke-stages.jsonl` in the launch queue's folder, next to `history.jsonl` and keyed by the same ticket number (`seq`): the step
  name, kind, checkout, the requested stages, `stageMs` (each stage's milliseconds in run order), `waitSeconds` (enqueue to start:
  the queue wait), `ranSeconds`, `outsideStagesSeconds` (the process start, the map load and the exit, which no stage clock covers),
  the exit code. The summary of `./arcanum smoke` prints the same as `SMOKE times`; `python3 -B -m tools.local_dev.smoke_history [N]`
  prints the last N recorded steps. The queue itself (`run_queue.py`) and its `history.jsonl` are unchanged (its labels stay
  `smoke STEP-NAME`; the stage list lives in this file), and a failure to write the record is a note, never a failed smoke. The
  file keeps the newest 5,000 lines once it passes 2 MB. `stages` lists the requested stages (for a battery run the PASS names the step requires), `stageMs` the parts the viewer timed, so
  for a battery step the two differ; a stage's clock also holds the short work after it up to the next stage's start; a step stopped by a timeout or a signal prints no stage times (`stageMs` is empty). Only the map smokes print stage times: the Crash Site smoke, the self-test, the build and the import show their
  wait and run time only.
- **Smoke zones (ARC-DEV-396).** Option A of the smoke speed diagnosis (ARC-DEV-384, deleted),
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
  only after the other stages of STEP` or `fails alone too`. The failed step stays failed
  unless every unfinished stage passed alone: then it is **order-dependent** (owner decision 2026-11-12), the summary shows it as
  `ORDER` with an `order-dependent:` line naming the step and its stages, the run and `./arcanum daily` pass (the daily report adds
  `smoke order-dependent ...` for a fix thread, and no package is blocked), and the final line says `(N order-dependent, each stage
  passes alone)`. A stage that fails alone too, a group that timed out (exit 124) and a failure with nothing unfinished still fail the run as before; the
  `map-smoke` line of BUILD.txt then ends with `order-dependent (STEP)`. No stage goes
  unchecked behind a failure and an order-dependent stage is named.
  **`--auto`** reads the diff: the merge base with `--base` (default `origin/main`; fetch first) to the working tree, untracked
  files included, and picks the zones by the table `TRIGGERS` (all matching rows of a path, as a union; a "no smoke" row such as `tests/` or
  `experiments/` beats the generic project-file rows `*.csproj`, `*.sln`, `*.props`, `*.tscn` and the lock files): ordinary game and
  viewer code (`src/Arcanum.Simulation|Session|World|Scripting|Input`, `tools/terrain-viewer`) selects `core`, the files of a
  heavy zone add that zone (route, movement, residency, window load and hero walk files: `walk`; picking, hover, roofs, locks and
  container loot: `container-click`; the Crash Site profile, transitions, portals and area start: `crash-site`; the census stage
  and the map row readers: `census`), a file of the shared data readers, the presentation, the save schema (`GameSaveLibrary`,
  `GameSessionSnapshot*`), the render and shared UI files or a project, package or build file selects `complete`, and so does a
  file no row names (a missing row is a safe miss, never a silent skip; at a PR `complete` runs as core + the Crash Site smoke, see the PR floor below). A file no smoke can cover (documents, unit tests, research,
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
  see (`AreaSmokeFightableStartAsync`: a roof-covered or walled-in NPC is skipped; a stage that also places the hero next to the NPC, `turn-based`, needs a pickable pixel from that adjacent cell too, ARC-UI-1425), and print `NO-TARGET` when none is, which the battery tolerates (so a regression that hides every target shows only in a `--stages` run, which reports the stage as not run; a limit). A stage
  whose content is the Crash Site's (`wreck`, `altar`: Virgil's home and a script tile) prints `NONE` in any other window, not a failure; asked for with
  `--stages` on such a window the runner reports it as not run. `ai-follower` takes the first speaker of the map that can heal with the skill and fight (not undead, mechanical, invulnerable or nigh-invulnerable: the original's heal skill check and danger setter refuse those, ARC-DEV-129) and prints `NO-FOLLOWER` when there is none (the unlit mine `bessietoonesmine-fixed` has only an undead, invulnerable speaker), which a `--stages` run reports as not run. Its last part (ARC-AI-1622) force-joins the first living NPC without a dialog script that has combat values (what Dominate Will does) and requires it to be a group companion the AI host runs that walks up to the hero; it prints `ARC-AI-1622 native-smoke ai-led-npc NONE` when the map has no such NPC. The AI stages put the hero's life back at their end, a dead hero resurrected, so a stage after them never starts with a dead hero. The viewer's `HoverPointAsync` scans only the screen rectangle of the body (its T0 command and the moving command drawn for it).
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
- **Census captures** (ARC-UI-2489, the windows of the [Steamwoven interface census](../ui/steamwoven-interface-census.md#screenshots); each is a
  local PNG through `--env`, never committed; run with `--env ARC_AREA_WINDOW_ART=legacy` and the HD option off for the Original look; all but
  the cursor sheet read the viewport, so the smoke's diagnostic text overlay is in the frame):

  | Window | Switch | Stage and configuration | File |
  | --- | --- | --- | --- |
  | H10 load indicator (clock art 601 on the load screen) | `ARC_AREA_LOAD_SCREEN_CAPTURE=PATH.png` | `transfer` (hero, main map); saves only while the load is still going, else prints `load screen capture skipped` | `PATH.png` |
  | H13 cursors | `ARC_AREA_CURSOR_CAPTURE=PATH.png` | `hover` (hero, main map); the cursor art sheet composed from the game's art (the framebuffer holds no cursors): one row per art, its frames left to right, a red cross on each hot point; prints `cursor sheet NONE` without the game folder | `PATH.png` |
  | H14 hidden-interface compact UI | `ARC_AREA_LIFE_CAPTURE=PREFIX` | `life` (hero, main map); the bars off, only the compact life panel | `PREFIX-compact.png` |
  | G04 See Contents (mode 4) | `ARC_AREA_CONTENTS_CAPTURE=PATH.png` | `item-aim` (hero, `--sector 1409,1314`, the town); the Identify-container step; nothing saved, silently, when the town has no container or the window is not in the art look (run with `ARC_AREA_WINDOW_ART=legacy`) | `PATH.png` |
  | G10 NPC character sheet of the dialog | `ARC_AREA_SHEET_CAPTURE=PATH.png` | `map` (hero, main map; not together with `bar`, which reads the same variable as a folder) | `PATH-npc.png`, `PATH-npc-spells.png`, `PATH-npc-schemes.png` |
  | M02 single-player menu, M03 new-game menu | `ARC_AREA_MENU_CAPTURE=PATH.png` | `menu` (`--main-menu`, main map) | `PATH.png` (main window), `PATH-before.png`, `PATH-single.png`, `PATH-newgame.png` |
  | M11 quit box | `ARC_AREA_QUIT_CAPTURE=PATH.png` | `scene` (`--main-menu`, `--map "crematorium-lev 1"`) | `PATH.png` |

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

Measured speed of the smoke runner, the queue and each stage, and the options for faster runs and per-task zones: the smoke speed diagnosis (ARC-DEV-384, deleted); its results are the sections above.

- **PR floor check (ARC-DEV-2223, owner decision 2026-11-15).** `./arcanum smoke verify-pr [--base REF]` (`tools/local_dev/smoke_verify.py`)
  starts no smoke and needs no Godot or queue place: it takes the diff against the merge base with `origin/main` (fetch first), selects the
  zones as `pr --auto` does and lists the units a PR must have run: each stage of each zone (`core` is every cheap stage), the Crash Site
  smoke when `crash-site` is selected, and for click or walk files (the `walk` and `container-click` rows) the `map` stage and `smoke
  self-test`. `complete` is taken as **core + the Crash Site smoke** at a PR (also in `pr --auto` itself: it runs those two, while `--zones
  complete` by hand still runs everything); the heavy zones and the map batteries are the daily build's (the [rolling main smoke](#rolling-smoke-on-main-arc-dev-2226)
  runs `core` on `origin/main` every couple of hours). It reads `smoke-stages.jsonl` of the queue folder. Each step's line there now holds `asked` (the stages the step
  was asked for; a battery asks for none) and `tree`, the git tree hash of the checkout's working files when the step ended (HEAD plus every
  changed and untracked file, made in a private temporary index, the real index untouched). A unit has a passing run when a step of this
  checkout ended with exit 0 and asked for the stage (or is the step `crash-site` or `self-test`) and its `tree` (taken when the step started)
  equals the current one, or no file that chose a zone differs between the two trees (a merge of main or an edited document or test does not
  void a run; a change to a file that selected a zone does). A step that failed only with failures of `smoke_known_failures.json`
  (`knownOnly` in the line) counts, with a note to name it in the PR; the stages a failed process still passed before it stopped (`passed`) count, so a failure in the middle of a group voids only the failing stage and the ones after it (run `pr --auto --retry` to run those one to a process; a stage that fails only after others, as `object-art` in #2015, then shows as `order-dependent` and passes alone); a pinned run (`custom`: `--map`, `--hero`, `--sector`, `--main-menu`,
  `--env`, `--no-config-check`, `smoke map NAME`) never counts, since it is not the stage on its own configuration. A battery step (`full`,
  `complete`) asked for `map` and `resources`, so it counts for those two. A step of the journal without `tree` (older lines) does not count. A path no `TRIGGERS` row names selects `complete` (fail closed) and
  is named in the output of both `pr --auto` and `verify-pr`. Exit 0 prints `SMOKE verify-pr PASS`; exit 1 lists the missing or stale units
  (first twelve) and what to run; exit 2 is a git or stage-list error. Threads run it before every code merge ([PR floor](tasks.md#smoke-scope)).

Tests: `python3 -B -m unittest tools.local_dev.test_smoke` (the stage names from the viewer, the per-PR and full plans,
the settings folders and dropped variables, the start lines, the chain going on after a failure and ending on a failed
build or a stopped run, the stage table, the grouping and the early error, the known failures file, the launcher command) and
`tools.local_dev.test_smoke_zones` (the zone table, the trigger rows, the selection from a diff, the zone and complete plans) and
`tools.local_dev.test_smoke_verify` (the floor check: a missing stage, a run on older files, an unmapped path, the complete selection, the self-test rule, the working-tree hash in a real temporary repository).

## Timing probe and the `perf` stages (ARC-DEV-367)

The standing probe of the performance diagnosis (O10 of [load time and window responsiveness](../project/status/tools.md)):
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
- **`soak`** (ARC-DEV-1398, a hero stage, own run, the zone `soak`; like `perf` it needs a window with a map exit:
  `./arcanum smoke pr --zones soak` or `--stages soak`; the stage pins the town sector 1409,1314 itself, so no `--sector`): a leak check. One cycle opens and closes the windows I C L T W M and S
  (the sleep window may refuse, which is counted, not a failure), takes a quick save (F7) and loads it (F8), then leaves the map by an exit and
  comes back (two whole map loads); the cycles go on for `ARC_SOAK_STAGE_MINUTES` (6 by default, 4 to 120, so the verdict has readings after the 120 s warm-up; set it with `--stages soak --env ARC_SOAK_STAGE_MINUTES=30`, the only way: a shell variable
  is dropped and `--env` does not go with `--zones`; the step's timeout and estimate follow it). Before each cycle a full collection runs and the counters are read (the same reading as the viewer's resource line, below);
  at the end the stage prints `ARC-DEV-2279 soak summary: ...` (each counter's first and last reading after the first 120 s, the change, and the maximum)
  and fails when a counter grew past its allowance (`ViewerSoakLimits` in `tools/terrain-viewer/Core/ViewerSoak.cs`: live .NET heap and Godot static memory
  48 MiB, render textures and video memory 64 MiB, working set 256 MiB, objects 2000, resources and nodes 400, orphan nodes 20, threads 6, open files 16; loose on
  purpose, the numbers to tune). The zone runs only when named: a diff never selects it and `complete` (the daily build) leaves it out. Window moves of the
  main map are not in the loop (the `walk` stage walks them). A headless run prints `NONE`. The viewer's own log carries the resource line in every run
  ([diagnosis](mac-crash-forensics.md#resource-line-in-the-log-and-the-bug-report-arc-dev-1398)).
- **Resident art across map loads (ARC-DEV-375).** A map load keeps decoded frames and visual records for the next one ([design](../world/area-loader.md#resident-frames-and-visuals-across-map-loads-arc-dev-375)).
  The load worker's read prints `load.worker.read.{strict,scene,copy}` spans and `ARC-DEV-375 visuals ...` / `ARC-DEV-375 frames resident-hits=H read=R ...` lines
  (hits against frames actually read, the store's size). `ARC_AREA_RESIDENT=0` in the environment (`--env ARC_AREA_RESIDENT=0` on a smoke) turns both stores off
  for a before/after comparison; nothing else depends on it. The editor `.mob` facts (ARC-DEV-388) have their own store, off with `ARC_AREA_RESIDENT_EDITOR=0` or `ARC_AREA_RESIDENT=0`, and the worker prints `ARC-DEV-388 editor-records served=B ms=T ...`.
- **Dialog diagnostics (ARC-UI-2268).** A normal viewer shows the dialog as the original's reply band only (no header, outcome, effects ledger, hint or Leave). `ARC_DIALOG_DIAGNOSTICS=1` in the environment (`--env ARC_DIALOG_DIAGNOSTICS=1` on a smoke) draws the diagnostic panel with the plain reply stack and all of them again; nothing else depends on it.
- **Map transitions kept on the device (ARC-DEV-392).** The map's transitions and the map list's destinations are written to `map-cache/` in the device's
  Steamwoven folder (`~/Library/Application Support/Arcanum Steamwoven Game/map-cache/` on a Mac; not `user://`, which every smoke replaces with a temporary
  folder), so the next process, above all the next smoke run, skips the 1.8 s read ([design](../world/area-loader.md#map-transitions-kept-on-the-device-arc-dev-392)).
  The viewer's start line `ARC-DEV-392 map-cache folder=...` names the folder, and the load's `ARC-WORLD-031 load stage=transitions` line says `source=memory|device|content|content+stored`.
  `ARC_MAP_CACHE=0` (`--env ARC_MAP_CACHE=0` on a smoke) turns only this store off, `ARC_AREA_RESIDENT=0` turns it off with the resident stores, `ARC_MAP_CACHE_DIR=<folder>`
  names another folder; a smoke drops the owner's `ARC_*` variables, so pass them with `--env`. Deleting the folder only costs one read; at most 96 files are kept.
- **Animated-light line (ARC-RENDER-3126).** With the probe on, the viewer prints one line a second on every map, lit or not (`lights=0` without animated lights; not in a smoke):
  `ARC-DEV-367 perf light-anim switch=on|off lights=N frames=F fps-avg=A fps-engine=E pushes=P push-ms-total=T push-ms-max=M push-alloc-kib=K view-rebuilds=R view-ms-total=V view-ms-max=W gc=G0/G1/G2 t=MS`
  (`lights` = the map's animated lights, `pushes` = the lighting views the animated lights pushed in the window, `push-*` the main-thread time and
  managed bytes of `PushAreaAmbient` for them, which only stores the view; `view-*` the canvas's rebuilds of its visible view in the window (the lattice and body colours, made at the next draw, on the main thread) and `fps-avg` the frames counted in the window). `ARC_LIGHT_ANIM=0` is a diagnostic switch: the animation
  time still runs but no new pose is pushed (`switch=off`); any other value or none keeps the default behaviour. It is read once at start.
  `LightAnimationProbe` in `src/Arcanum.World`, tests `LightAnimationProbeTests`.
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
    ./arcanum queue --pause         # no waiting run starts (running ones finish), for every checkout, until --resume (ARC-DEV-1344)
    ./arcanum queue --resume
    ./arcanum queue --playing [--for MIN]   # the owner is playing: no window run starts until --not-playing or the limit (default 4 h; ARC-DEV-2272)
    ./arcanum queue --not-playing   # (the packaged game running turns the mode on by itself: ARC-DEV-2291)
    ./arcanum queue --app           # start the menu-bar view of the queue now (a queued run starts it by itself)
    ./arcanum queue --wait [RUN_ID | --all] [--timeout MIN]   # block until that run (or the whole queue) has ended (ARC-DEV-1983)

| Kind | Use it for | Rule | Default limit |
|---|---|---|---|
| `window` | any Godot run with a window: native smokes, the viewer self-test, labs | a [Godot lane](#godot-lanes-arc-dev-400): the heavy lane when it declares more than 90 s, else a short lane, in [queue order](#queue-order-arc-dev-390) | 30 min |
| `headless` | `Godot --headless` that runs for a while (a lab, a long probe) | the same lanes | 30 min |
| `quick` (ARC-DEV-390) | a Godot run with no window that ends in seconds: a project `--import`, the headless viewer self-test (`./arcanum smoke` uses it for both) | two quick places beside the Godot lanes (it neither waits for nor holds a lane), one at a time per checkout; waits for an export that is running or next | 30 min |
| `export` | an export endpoint (`tools/arcanum-local ... export`, the validation commands that export) | alone: it waits for the running Godot runs of every lane; when it is next, new `dotnet` runs wait and running ones finish, then it runs alone | 180 min |
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
- **Waiting for a run (ARC-DEV-1983).** `./arcanum queue --wait RUN_ID` blocks until ticket `RUN_ID` (the number `./arcanum queue`
  lists and the `ARCANUM_QUEUE log|start ticket=N` line shows) has ended, prints the same quiet result as the run itself (`PASS`/`FAIL`
  with `ticket=N`, the exit code, the log path and the first error) and exits with the run's exit code; a run that ended earlier answers
  at once from `history.jsonl`. `./arcanum queue --wait` (or `--all`) blocks until no run holds a place or waits (the pause is not a
  run). Inside a queued run's command the wait is refused (exit 1): that run holds its place, so the runs behind it would wait for it
  while it waits for them. `--timeout MIN` gives up after MIN minutes with exit **123**
  and `TIMEOUT` on stdout without stopping anything (a run's own limit exits 124; a command that itself exits 122 or 123 cannot be
  told from these two, read the `PASS`/`FAIL`/`TIMEOUT`/`unknown` word, a run that is neither queued nor in the history
  exits 122, bad arguments 2, Ctrl-C 130). A waiter takes no place and changes no lane or ordering rule; it only reads the ticket
  files once a second (like every reader it drops dead tickets) and spawns no process (libproc, ARC-DEV-1401). Use it when a run was started in the background (`./arcanum run
  ... &`, or a Bash `run_in_background`) or the thread must wait for other threads' runs; never a `sleep` or polling loop. A thread
  waiting for its own foreground `./arcanum run` needs nothing: that call already blocks.
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
  the start time is read through macOS libproc (`proc_pidinfo`), so a waiter spawns no process while it polls (ARC-DEV-1401;
  `ps`, cached 30 s, answers only where libproc is missing); when the time cannot be read, the run counts as alive, and a zombie (already dead, its lock released) counts as gone. Never delete ticket files by hand; `history.jsonl` there records every run.
- **Nesting.** A queued command that calls `./arcanum run` again (or `./arcanum area|world|map`) runs inside its
  parent's place when that place covers it: an export covers every kind, a `window` or `headless` run covers Godot,
  `quick` and `dotnet` runs, a `quick` run covers `quick` runs, a `dotnet` run covers `dotnet` runs. Any other nesting (an export inside a Godot run, a Godot
  run inside a `dotnet` run) would wait for its own parent and is refused: queue it on its own.
- **The owner's viewer.** `./arcanum area`, `world` and `map` take a Godot lane (a short one, as a `window` run with the default
  estimate) first in line (after the runs holding its lane), with no time limit, and keep it until the viewer window closes; the command stays in the terminal until then.
  Ctrl-C there, or closing the terminal, gives the place up while the viewer stays open. `--no-queue` starts at
  once, and `--dry-run` never queues.
- **Lanes still on lockf.** While some threads still use the lockf rule, a Godot run also takes
  `/tmp/arcanum-godot-window.lock` (shared, so the runs of the lanes hold it side by side), and a `lockf` user (exclusive)
  and the queue's Godot runs never overlap (a queued run then prints that it waits for a lockf user).

Tests: `python3 -B -m unittest tools.local_dev.test_run_queue` (arrival and length order, lanes and their accounting, quick runs, gaps, export
quiet, crashed and killed runs, limits, lockf, nesting, locales); they use their own `ARCANUM_LOCAL_HOME` and lock file.

### Owner is playing mode (ARC-DEV-2272)

Godot needs the keyboard focus, and a smoke window of an agent thread takes it from the packaged game the owner is playing
(owner report 2026-11-15, character creation). Headless, off-screen and background windows were tried and failed, so the answer
is that **no window run starts while the owner plays**; the pause stops everything, this mode stops only the runs that open a window.

    ./arcanum queue --playing              # on; ends by itself after 4 h
    ./arcanum queue --playing --for 90     # on for 90 minutes (also 30m, 2h; 0 or off: no limit)
    ./arcanum queue --not-playing          # off at once: the waiting window runs start in their order

The menu-bar app has the same switch (`Owner is playing`, icon `gamecontroller` while on). The command prints the state and the time
left and `./arcanum queue` then starts with a `PLAYING` line; the switch takes effect at the next poll (a quarter of a second) of every waiter.

- **Held:** every ordinary `window` run: smoke stages (`./arcanum smoke`, `smoke pr`, `smoke complete`), labs, an ad-hoc
  `./arcanum run --kind window`, and the rolling `main-smoke` steps. A waiter says
  `waiting-for=owner playing (for ..., ends in ...; no window run starts, headless/quick/dotnet go on; ./arcanum queue --not-playing)`,
  `./arcanum queue` shows `waiting ... (owner playing)`, and a smoke prints `SMOKE waiting: owner is playing (...)` before the step.
- **Not held:** `headless`, `quick`, `dotnet` and `export` runs (so builds, tests, imports and the headless self-test of every thread go
  on), the owner's own launches (`./arcanum area|world|map`, `package`: the front of the queue) and a run that already holds its
  window or a nested run inside it (it finishes; the mode does not stop a window that is open; a nested window inside a `headless` run is not held either). A held window run is not counted as
  waiting for a Godot place, so the runs queued behind it are not held by it.
- **`daily` and `main-smoke`** do not wait for hours: they print `SKIP owner is playing`, exit 0 and **record nothing** (no package, no
  green or red, `--force` does not override it), so the commit is picked up by the next run after the mode ended and no skipped run
  can be read as a green one.
- **Limit.** The manual mode expires after 4 h (`--for` sets it, `0`/`off` removes it): the holder process ends by itself at the
  deadline, nothing has to enforce it, and a crash of the holder or a reboot ends the mode too. `--playing` again renews the limit.
- **Old wrappers.** The mode is one valid ticket `t-playing.json` (seq 0, kind `window`, lane `single`, already started, flag
  `playing`) held by a `/usr/bin/lockf` process like the pause. A wrapper of this version holds back only the window runs behind it.
  A wrapper of another checkout from **before this change** does not know the flag and reads it as a running exclusive Godot run, so
  it holds back every Godot kind (`window`, `headless`, `export`) of that checkout, but not `dotnet` or `quick` runs, and also the
  owner's own launches from that checkout, until that checkout has merged main. Nothing else is needed for the mode to hold there.
- **Automatic mode (ARC-DEV-2291).** While a process of the packaged game runs, the queue treats the mode as on, and releases it when
  the game quits: start `artifacts/packages/macos/Arcanum Steamwoven.app` and a queued window run says
  `waiting-for=owner playing (game running for ...; no window run starts, headless/quick/dotnet go on; quit the game)`; quit the game and it
  starts within a few seconds (a minute at most). The game is any process whose program lies under the main checkout's
  `artifacts/packages/macos` (also from a worktree: the main checkout is read from the `.git` file), found through libproc
  (`proc_listpids`, `proc_pidpath`, no process spawned: about 3 ms, cached 2 s per process). The rule is the same as the manual
  switch's: window runs only, the owner's front launches and runs already holding their window go on, `daily` and `main-smoke`
  print `SKIP owner is playing (game running ...)`. The state is the game's process itself, so nothing is written to the queue folder and
  no limit applies; it counts once beside a manual switch (which keeps its limit and its `--not-playing`). `--not-playing` does not end it
  (the command says so); quit the game. `ARCANUM_QUEUE_AUTO_PLAYING=0` turns it off for a command or wrapper it is set on, and
  `ARCANUM_QUEUE_GAME_ROOTS` (a `:` list of folders) names other game folders; both are for tests and rare cases. Under
  `ARCANUM_LOCAL_HOME` (the tests' own queue folder) the mode reads nothing unless the roots are named. The menu-bar app reads the
  same process (`--game-root`, given by `menubar.py` and `queue --app`; the roots are fixed when the app starts) and shows `OWNER PLAYING (game running)` (its menu item does nothing while only the game holds the mode); `ArcanumQueue --game-root DIR --game-status`
  prints `game since <epoch>` or `no game`. A game started from another path (a copy, another machine) is not seen: use the manual switch.
  A wrapper from before this change does not see the automatic mode (it is in memory, not a file) and runs window runs as before.

Tests: `python3 -B -m unittest tools.local_dev.test_run_queue` (`playing` tests: admission rule, real wrappers, the command, the
limit, the automatic game mode with a stand-in game built by clang), `test_smoke`, `test_daily`, `test_main_smoke`, `test_menubar`; the app: `ArcanumQueue --set-playing` / `--clear-playing` do what its menu item does.

### Queue menu-bar app (ARC-DEV-1344)

A small macOS menu-bar item shows the queue and pauses it. It is `tools/menubar/ArcanumQueue.swift`: one AppKit file, no
dependencies, compiled on this Mac by `swiftc` (Xcode command line tools, the way the focus guard needs `clang`) into
`~/Library/Application Support/Arcanum Steamwoven/menubar/ArcanumQueue-<hash of the source>`; the binary is never in git and a
changed source is built again by the next start (about 30 s the first time, in the background). A Swift app was chosen over a
Python helper because it needs nothing installed (system Python has no `rumps`/PyObjC), has no interpreter running for the
display and reads the queue's own files, so it works from whichever checkout started it and when that checkout is gone.

- **Task code.** Every run row (the menu's and `./arcanum queue`'s) shows the task code `ARC-<TYPE>-<issue>` it belongs to: the one in its
  label, else the one in the branch its checkout has now, read from the checkout's `HEAD` file (a worktree's `.git` file names its git
  folder), so a run of any wrapper version shows it and nothing is added to the ticket; a branch with no code (`claude/project-thread-...`)
  shows none.
- **Layout.** What stays the same is on top (Pause, `Godot runs at once`, `Load guard`, Quit); below it what changes: the counts, the
  Mac's load and RAM, then the running and the waiting runs.
- **Shows.** The icon says what the queue holds (`tray`, `tray.full` with `running·waiting`, `pause.circle` while paused). The menu lists the
  running runs, then the waiting ones in queue order: number, kind and lane, label, how long it has run or waited, the checkout
  (the tooltip has the full command and pid). It reads the tickets the way the queue does (the flock the run holds plus a live
  process), every second, and never writes a ticket (it only probes their locks for an instant).
- **Cost.** While the menu is open (every 2 s) the app takes one `ps` snapshot and adds to each running run the CPU (percent of
  one core, from the change of the tree's accumulated CPU time, so it shows from the second sample on) and RAM (resident set)
  of its command's process tree: the process group the queue started it in plus descendants that left it. The header line shows
  the Mac's load average, cores and RAM in use (active, wired and compressed pages). Nothing is sampled while the menu is closed.
- **Pause.** The menu's `Pause` item and `./arcanum queue --pause` / `--resume` set and clear one thing: the file `t-pause.json` in the
  queue folder, an *unreadable ticket* held by a small `/usr/bin/lockf` process. The queue has always counted a locked ticket it
  cannot read (another version's format) as a running export, which blocks everything, so **every wrapper of every checkout, old
  ones too, waits behind the pause** (checked with the code from before this change); no merge of main is needed for it to hold. Runs
  already running finish and a nested run still runs inside its parent's place; every kind waits, the owner's viewer launches and
  package builds too. A waiter says `waiting-for=queue PAUSED (./arcanum queue --resume)` (old wrappers: `waiting-for=#0 export ...
  "unreadable ticket"`), `./arcanum queue` prints `PAUSED` and `waiting ... (paused)`, and the first run waiting starts within the poll
  interval after the resume, in its place in the order. Resume removes the file and the holder ends within a second; the holder is
  independent of the app and the shell that started it, but it dies with the machine's processes (a reboot, a kill of the holder),
  which ends the pause, so a pause never outlives them. It has no limit: a forgotten pause holds every waiting run (they say so on
  stderr; the icon shows the pause).
- **Limits.** The menu's `Godot runs at once` and `Load guard` submenus set the owner's caps of
  [ARC-DEV-1353](#godot-lanes-arc-dev-400) (`./arcanum queue --set-lanes N|default`, `--set-max-load X|off`): they write the same
  `settings.json` of the queue folder (`shortLanes`, `maxLoad`), under the queue lock and atomically, so the CLI, the app and every
  wrapper agree and the next run sees it. A check on the menu is the stored value; "default" is no stored value (the app's
  default of 2 short lanes copies `DEFAULT_SHORT_LANES`). `ARCANUM_QUEUE_LANES` in a wrapper's environment still wins there.
- **Start.** `run_queue.run` calls `menubar.ensure_running()` after it takes a ticket: when the app does not hold `menubar/app.lock` (an
  flock it holds for its whole life, so a crash frees it) a detached helper builds the app if needed and starts it; a second helper
  or a second app ends at once. Nothing here can fail or delay a run: a missing compiler or a failed build is written to
  `menubar/menubar.log` only. The app leaves by itself after `--idle-minutes` (15) with no run and no pause, and `Quit` does the
  same; the next run starts it again. `ARCANUM_QUEUE_MENUBAR=0` never starts it; a run under `ARCANUM_LOCAL_HOME` (the tests'
  own home) does not, unless `ARCANUM_QUEUE_MENUBAR=1`; other systems than macOS do not.

Tests: `python3 -B -m unittest tools.local_dev.test_menubar tools.local_dev.test_run_queue` (the pause tests are in the latter); the app itself is checked
by running it (`./arcanum queue --app`, then a queued run and a screenshot of the menu); `ArcanumQueue --set-pause` / `--clear-pause` / `--set-playing` / `--clear-playing` / `--set-lanes` / `--set-max-load` do what its menu items do and exit, to check that the flag it writes is the one the queue reads.

### Owner QA page (ARC-DEV-2187)

`./arcanum qa` is the owner's local check page (slice 2a of epic #2185; `tools/local_dev/qa.py` and `qa_page.html`, Python standard
library only). It lists the steps of [the owner re-check list](owner-checks.md) and records the owner's result per step.

- **Start.** `./arcanum qa` fetches `origin main`, reads the list with `git show origin/main:docs/project/owner-recheck-list.md`
  through the parser of ARC-DEV-2186 (no checkout is written: run from any checkout, also a worktree), binds `127.0.0.1` only on a
  free port (`--port N`), answers one ping through its own socket, prints `qa: started: URL` and opens the page (`--no-open`
  prints the URL only). `--check` does the start and the ping and stops (the start check: it must print `qa: started` and
  `check passed` within a minute); `--dry-run` reads the list and shows the paths, the counts and the unsent issues, starts no
  server, writes nothing and calls no `gh`; `--no-fetch` and `--ref REF` read the list as it is; `--data-dir` and `--reports-dir`
  replace the two folders below. A page and every `/api` call need the random token in the printed URL, the `X-QA-Token` header on a
  POST, and a `127.0.0.1` or `localhost` Host header, so another web page or a rebound DNS name cannot post a result.
- **Page.** Sections with the step, its expected result, progress per section, filters (all, open, new since the last visit, one
  section) and per step `same` / `differs` / `cannot test` / `open`, a note and the report dropdown. A step the list already marks
  (`same (...)`) shows that until the owner records a result. Keys: `j`/`k` step, `s` `d` `c` `o` status, `n` note, `r` reload the
  list. "New since the last visit" is the step ids that were not in `local/qa/state.json` when the page started.
- **Reports.** The dropdown lists `artifacts/packages/macos/reports/` of the main checkout (name, local time, the first line of the
  player's text from `report.md`; nothing else of a report is read); the newest report taken after the step was opened on the page
  is preselected. A result stores the folder name only. A report whose `report.md` head has the line `Check: ARC-TYPE-NNN#k`
  (ARC-DEV-2189, below) is listed under that step first, marked `★`, and the newest one is preselected for the step, whatever
  its time; a report tagged for another step is never preselected by time.
- **Active check (ARC-DEV-2189).** Opening a step on the page (click, `j`/`k`) writes its id as one line into
  `local/qa/active-check.txt` of the main checkout (git-ignored; the viewer's Ctrl+B reads it and tags its report, see the
  [bug report](../rendering/terrain-viewer.md)). The file is removed when the page starts and when it stops (Ctrl-C), and the viewer
  ignores one older than 12 hours. `POST /api/active {"step": ID or null}` writes or clears it (only a step of the list).
- **Results.** `local/qa/results.jsonl` of the main checkout (git-ignored with the rest of `local/`): one JSON event per line,
  appended under a file lock, the latest event of a step wins, an unreadable line is skipped and counted. A result can be changed
  later; the page and `qa sync` read the same file, so a restart loses nothing.
- **Differs.** Marking `differs` runs `gh issue create` at once on the owner's Mac: the title starts with the step id, the body has
  the section, step, expected result, the owner's note and the report folder's *name*; labels are `bug`, `type:*` from the section's
  task code and the `area:*` label of issue `NNN` when that issue's title starts with the code. Screenshots, saves, logs and the
  player's report text are never put into an issue. The issue number is stored for the step: a repeat, a later change to
  `same` and back, or a second click never creates another issue. A failure (no `gh`, no network) keeps the result, shows the error
  with a Retry button, and `./arcanum qa sync` (`--dry-run` first) creates the issues of the `differs` steps that have none.
- **List snapshot and overlay (ARC-DEV-2190).** The page writes `local/qa/checks.json` (text only: id, section title, step, expected,
  list status, the list's commit) whenever the serving page reads the list (start, Reload; ARC-DEV-2236: `qa sync`, `--check` and `overlay off` never replace it, `overlay on` writes one only when none exists; `qa sync` names a differs step that left the list), so the
  in-game overlay lists the steps without Python. `./arcanum qa overlay on|off` writes or removes the marker `local/qa/owner-qa.on`
  (the environment variable `ARC_OWNER_QA=1` also turns the overlay on); a build without the marker or the variable, such as a
  player's, is unchanged. The game appends its results to the same `results.jsonl` in the same format plus `build` (the build's
  commit) and `src: game`; the running page creates the issue of a new `differs` every 20 s (a failed attempt waits for `qa sync`),
  and `qa sync` does it on demand, once per step either way. See the [viewer](../rendering/terrain-viewer.md#owner-checks-overlay-arc-dev-2190).
- **Answered checks (ARC-DEV-2768).** A step is answered when its status is not `open` (a mark, or the list's own), or a report folder is
  recorded in its result (the overlay's Send, or a Ctrl+B report tagged with the step, which the game records in `results.jsonl` with the
  status the step shows), or a report whose `Check:` line names it is in the reports folder (a report sent while no QA folder was found).
  The page lists the answered steps in a collapsed "Answered (n)" group at the end, each with the day of the mark or report; the
  progress bars and the open count count them as checked. **Reopen** (button or `o`) appends an `open` result with `"reopen":true`,
  no report and the note kept: the step is listed again, and reports older than that event no longer answer it. `POST /api/reopen`.
- **Stale list and stale check (ARC-DEV-3130).** A `checks.json` older than 72 hours (its `ts`, else the file time) is the list of an earlier
  build: the overlay shows no steps and says so, clears `active-check.txt` and picks no current step (the game used to rewrite the file at
  every start with the first unanswered step of that old list, so a check of 2026-11-15 was stamped on every later report). A Ctrl+B
  report is tagged with the active step only when the folder's list is fresh and has the step and the step was not answered more than 12 hours ago (a mark or report of the last 12 hours still tags, so marking then describing with Ctrl+B works); a bare
  active file with no `checks.json` beside it (an automated run) is taken as it is. `./arcanum qa refresh` (`--dry-run` first)
  rebuilds `checks.json` from the ref without starting the page and removes an `active-check.txt` that names a step the list does not
  have; `overlay on` rebuilds a missing or stale snapshot too. The page rewrites it as before at start and Reload.
- **Later.** The write-back of statuses to the list works from the same results file.

Tests: `python3 -B -m unittest tools.local_dev.test_qa` (synthetic list, fake `gh`, a real localhost socket); the page itself is
checked by opening it (`./arcanum qa`) and ticking a step.

### Queue order (ARC-DEV-390)

Smoke speed diagnosis measured 3/4 of a smoke call's time as waiting
for the single Godot slot behind a few runs of 5 to 11 minutes (`walk`, `container-click`); a first-come first-served
queue is the worst order for such a mix, and a project import waited for a window run to start 2.5 s of work.

- **Declared length.** Every run carries an estimate in seconds: `--estimate SEC` on `./arcanum run`, or the kind's default
  (`window` 60, `headless` 60, `export` 300, `dotnet` 60, `quick` 15) when none is given. `./arcanum smoke` declares its
  own per step (`smoke.estimate`: a viewer run is about 20 s plus 3 s per stage, the whole map battery 60 s, the Crash
  Site smoke 110 s, `walk` 540 s, `container-click` 700 s, `census` 900 s as an assumption). The estimate only orders
  the queue; it is not a limit (`--timeout` is) and a wrong one costs its run a different place, nothing else.
- **Order.** The key of a waiting run is its arrival time plus 3 times its estimate (`ORDER_WEIGHT`); the lowest key goes
  first, equal keys by number, and the owner's own viewer launches stay first of all; `./arcanum main-smoke` queues its steps after every other run (`LOW`, [rolling smoke](#rolling-smoke-on-main-arc-dev-2226)). A 20 s run that arrives within
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

### Godot lanes (ARC-DEV-400)

The measurement of ARC-DEV-397 (2026-11-11, in its thread; not kept in git) showed the single Godot slot is not a
resource limit: up to four smoke windows ran side by side on the owner's Mac (about 650 to 800 % CPU, 3 GB, 10 of 10
four-window repeats green at load 12 to 40), while a replay of the 94 hours of history on one slot gave 76 hours of
waiting against about 3 hours with a heavy lane and two short lanes. The owner approved the lanes (2026-11-11).

- **Three lanes.** A `window` or `headless` run declaring more than `HEAVY_SECONDS` (90 s: `walk`, `container-click`, the
  Crash Site smoke) takes the one **heavy** lane; any other takes one of the `DEFAULT_SHORT_LANES` (2) **short** lanes.
  The lane is fixed when the run is queued (from `--estimate` or the kind's default) and written into its ticket; the
  [queue order](#queue-order-arc-dev-390) still decides who is next within a lane. A run waits for the runs of its own lane, for
  an exclusive run (below) that is running or ahead of it, and for runs of its own checkout, but no longer for the other
  lane: a long run does not block a short one and the reverse. After about a day of the history the short lanes are
  to be raised to 3 (`DEFAULT_SHORT_LANES`, or at once with the variable below); the history shows the red-run counts.
- **A package build goes first.** `./arcanum package` (so `daily` too) queues its export with the owner's front priority, like
  `./arcanum area|world|map`: it waits for the runs already holding a place and for nothing that arrives later. Before
  this, an export was ordered by its arrival plus 3 of its 300 s estimates, so smokes arriving within 15 minutes kept
  passing it (ticket 12945 waited with 4 to 9 runs ahead of it, 2026-11-11).
- **An export runs alone.** It waits for every running Godot run of every lane (and those ahead of it in the order),
  and Godot runs that are later in the order wait for it, as before; it still waits for running `dotnet` and `quick`
  runs. Exports are one at a time.
- **One checkout, one run.** Two Godot runs of one checkout (the folder holding `.git` above the folder the run was queued
  from) never run at once, in any lane, and neither do a Godot run and a `quick` run of it: they share the checkout's
  project folder. A run held back by its own checkout's run takes no place of another checkout's run in its lane.
  So the lanes run the threads of different checkouts side by side; they do not speed up one thread's own chain.
- **Rollback.** `ARCANUM_QUEUE_LANES=0` (or `off`) makes the wrapper queue its Godot runs as the old single slot: such a run
  is recorded with lane `single` and runs alone, and every other wrapper waits for it and it waits for them. Any other
  value that is not a whole number from 0 to 8 does the same and prints a note. `ARCANUM_QUEUE_LANES=3` makes 3 short
  lanes. The variable is read by each wrapper, and every wrapper counts the short places from its own value, so keep it the same
  in every lane's shell: set it in the shell profile to roll back for all of them
  (a single wrapper that sets it only serialises its own runs); a ticket from a wrapper from before the lanes is also read as `single`.
- **Owner's caps (ARC-DEV-1353).** The variable above is per wrapper; one setting for every thread lives in the queue folder
  (`settings.json`, read by every wrapper at every poll, never created by a read). `./arcanum queue --set-lanes N` stores
  the number of short lanes (0 to 8; `0` is one Godot run at a time, `1` is one short plus the one heavy lane, so two at
  once, `default` removes the setting); a wrapper's own `ARCANUM_QUEUE_LANES` still wins in that wrapper. `./arcanum queue
  --set-max-load X` (`off` removes it) is a load guard: a Godot or `quick` run whose turn has come does not start while another
  Godot run is running and the one-minute load average is above X, and prints `waiting-for=load below X`; a lone run always
  starts, so nothing waits for ever. `./arcanum queue` prints the caps in force on its last line. The caps hold for every
  checkout that has this code; a checkout from before ARC-DEV-1353 ignores the file until it merges `origin/main`. The caps
  count Godot lanes only: `dotnet` runs (2 places) and `quick` runs (2 places) come on top, so a machine under strain
  takes `--set-lanes 0` or `1` and a guard around 12 to 16.
- **Same environment.** The lanes need no change to a command: every lane's smoke still has its own settings and saves
  per process (the system pointer is gated, ARC-DEV-060). `lockf` users keep excluding the queue ([above](#launch-queue-arc-dev-033)); the queue's runs hold that lock shared, so with
  overlapping lane runs a `lockf` user may wait for as long as the lanes stay busy (a retired rule).
- **Red runs per lane.** The history of every Godot run (`history.jsonl`, and `./arcanum queue --history N`) records its
  `lane`, the other Godot runs running at its start and end (`peersStart`, `peersEnd`) and the load average at its start
  and end (`loadStart`, `loadEnd`). `./arcanum queue --lanes [--history N]` counts per lane the runs and the red ones
  (a non-zero exit that is not the wrapper being stopped with SIGHUP, SIGINT or SIGTERM), split into runs that ran alone
  and runs that ran beside others, and lists the latest red runs with those conditions, so a red run at a high load
  (the measurement had one self-test red at load 37 that was never explained) is compared with the same lane's
  red rate alone. Runs from before the lanes are put in the lane their length would take, without a peer count, as the baseline.
  Stages and timings are not recorded here (ARC-DEV-399).

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

The bar smoke's first button hover (the Character button, `ARC-UI-018 bar smoke FAIL hover: '' frame 0` once in a rolling
run, ARC-UI-3108) no longer trusts the hover state read right after the move: it waits up to 30 more frames, pushing the same
motion again (nudged by a pixel and back), and prints `ARC-UI-3108 bar smoke Character hover seen after N extra frames`.
The top bar HD check prints its own failing sentence (`ARC-UI-116 top bar HD smoke FAIL ...`) instead of the frontier
fault's `(withheld)` (ARC-DEV-3093; the kit/Steamwoven bar smoke passes with the installed `interface-redraw-4x`, which
carries the top bar frames, 2026-11-24).

Not covered: the system pointer crossing the window's edge during a step still reaches the window as a window event
(mouse enter or exit), which drops the GUI's hover; Windows and Linux were not run.

### Rolling smoke on main (ARC-DEV-2226)

Owner decision 2026-11-15 (smoke stability measure 2, [baseline](smoke-stability.md)): a red `main` was found by other
threads' PR runs (57 of 61 cases) or by the daily 12 to 24 hours later, with a hundred commits in the suspect range. The
rolling smoke finds it within about two hours and says which merges to look at.

    ./arcanum main-smoke               # fetch; if main moved since the last green or red run: core smoke on a fresh worktree of it
    ./arcanum main-smoke --dry-run     # fetch, say whether it would run and print the command; run nothing else
    ./arcanum main-smoke --force       # run although main has not moved; --no-issue leaves GitHub alone; --zones Z changes the set
    ./arcanum main-smoke --print-launchd   # the launchd agent (every 2 hours), for the owner to load

It is `tools/local_dev/main_smoke.py` (the flag: `red_flag.py`; tests `tools.local_dev.test_main_smoke`).

- **What runs.** A fresh detached worktree of `origin/main` in an `arcanum-main-smoke-*` folder of the temporary folder
  (never the owner's checkout; leftovers of a killed run are removed under the command's lock, one run at a time per
  repository), and in it `./arcanum smoke pr --zones core,crash-site --retry --low-priority` with a limit of 180
  minutes for the whole command: the `core` zone (every cheap stage, [smoke zones](#smoke-runner-arc-dev-061)) and the
  Crash Site smoke; the heavy zones (`walk`, `container-click`, `census`) and the complete run stay with the daily.
  `--retry` runs the stages of a failed process alone, so the log knows which fail alone too. The run skips (exit 0)
  when the last run of this command on that commit was green or red; an inconclusive one is tried again.
- **Low priority.** `--low-priority` of `./arcanum smoke` gives every step of the run `run_queue.LOW`, behind every
  ordinary run and the owner's launches ([queue order](#queue-order-arc-dev-390)); each step is its own ticket, so a
  PR run waits for at most the step that is running. A queue busy all day delays the rolling run, nothing else.
- **Result.** Green: the commit becomes the last green one and the flag is cleared (and the open issue gets a comment).
  Red (a summary that names a failed step; a failed viewer build or import counts): the state keeps the first red commit
  of the streak, the one open issue (state, else an open one titled `Red on main (rolling smoke)`) is rewritten or
  created, with labels `bug`, `type:dev`, `status:ready` and an `area:*` by the first failing stage (`AREAS` in
  `main_smoke.py`, `area:scene` when none fits), the failing stages (a stage that failed alone too is named, not its
  process), the first red commit, the last green commit and the first-parent merge range between them (newest first,
  at most 60 lines and a compare link), and the first errors; the flag is written. A failed step whose own exit is a queue timeout (124) or a viewer that did not start (125) is not
  counted (logged as `transient`), and a run with no other failed step is inconclusive. Inconclusive (stopped, ran out of the
  180 minutes, no summary, a worktree that could not be made): recorded, flag and issue untouched. If `gh` fails the
  flag is still written, without an issue number, and the next run files it (for the same commit without running the smoke again). Exit codes: 0 green or skip, 1 red,
  2 inconclusive or a refusal.
- **The flag.** `red-on-main.json` in the `main-smoke` folder of the launcher home (`~/Library/Application Support/Arcanum
  Steamwoven/main-smoke`, beside `run-queue`; `ARCANUM_LOCAL_HOME` moves both), so every checkout and worktree of the
  Mac reads the same one. `./arcanum smoke pr` (any of its forms, `--auto` included) prints `SMOKE red on main since SHA,
  #N (...)` before it runs and again after a failed run: a failure of those stages that also fails on a clean `origin/main`
  is that issue's, so a thread cites it instead of running the smoke on main again. The next green run deletes it.
- **Flake log.** Each run appends to `flake-log.jsonl` (same folder) one line per stage that failed or passed alone after
  failing in its process: `outcome` `passes-alone`, `fails-alone` or `failed` (no alone re-run exists, as for the Crash Site
  smoke). It is the input of the later flake register (measure 4), not a decision on its own.
- **Files.** `state.json` (last run, last green, the red streak and its issue number), `red-on-main.json`,
  `flake-log.jsonl`, `logs/` (the last run's `report.txt` and `smoke.log`, replaced by the next run), all in that folder.
- **Schedule.** Not started by the repository: the daily's agent is `~/Library/LaunchAgents/com.arcanum.steamwoven.daily.plist`
  (03:00), and this one is its sibling `com.arcanum.steamwoven.main-smoke.plist`: `./arcanum main-smoke --print-launchd >
  ~/Library/LaunchAgents/com.arcanum.steamwoven.main-smoke.plist`, then `launchctl bootstrap gui/$(id -u)
  ~/Library/LaunchAgents/com.arcanum.steamwoven.main-smoke.plist` (stop it with `launchctl bootout gui/$(id -u)/com.arcanum.steamwoven.main-smoke`).
  It runs every 2 hours (`StartInterval` 7200), from a fresh worktree of `origin/main` under `caffeinate` as the daily's
  does, logs to `~/Library/Logs/arcanum-main-smoke.log`, and a run that finds the lock taken or main unchanged ends at once.
  The owner starts it (human gate of the issue).

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
- **Killed by a signal (ARC-DEV-2360).** A command a signal ended has the exit code `-N` when the command itself died
  on it (`-11` is SIGSEGV) and `128+N` when a shell or wrapper passes its child's status on (139). For a crash signal
  (SIGSEGV, SIGABRT, SIGBUS, SIGILL, SIGFPE) the summary adds a `note:` line after `FAIL` with the signal's name and the
  last `PASS` line's number, and says so when only Godot's shutdown leak lines (`Leaked unsafe reference`,
  `ObjectDB instances were leaked`) follow it: this looks like a crash at engine shutdown. It never says the checks
  passed (the log may hold an earlier failed stage); an error line after the last `PASS` line, no `PASS` line or no leak
  line gets its own wording, and another signal (SIGKILL, SIGTERM, SIGINT) is "not a crash signal". The exit code stays
  as it was and the run is still red: retry once, and a repeat is a real failure. Seen once in 31 headless
  self-tests (2026-11-16, run #30494; the Mac was busy with other Godot runs); 12 reruns in a busy queue passed. The
  cause is not found: that log held two leaked `InputEventKey` objects (F8, the self-test's quick-key route) reported
  by the mono module at exit and none of the 30 other logs did, which fits a Godot shutdown crash but is a
  hypothesis, so the issue is recorded as not reproducible.
- **Godot finalizer-thread crash (ARC-DEV-2412).** Godot 4.7.2 .NET sometimes dies on the .NET finalizer thread inside a
  Godot type's `Finalize` (the log's C# backtrace ends `Godot.StringName.Dispose` / `Godot.StringName.Finalize`, once with
  `Godot.DisposablesTracker.UnregisterDisposable` after a fatal `System.AccessViolationException`), as `handle_crash:
  Program crashed with signal 11` and exit `-6`. It was seen once in a `pr --auto` run on the cave stage (#2405 merge,
  2026-11-16), then reproduced: the cave stage (`./arcanum smoke map "crash site cave" --stages tile-ghost,route-barrel,
  grenade`) alone passed 10 of 10, and with four such loops running at once it crashed in 5 of 16 runs, then in 3 of
  16 and 3 of 10 in later batches (at different points of the stage, from 26 s to 80 s in; the stage's checks are not at
  fault). The line `The caller thread can't call the function propagate_notification() on this node` that precedes the
  crash comes from the engine's crash handler (the crash handler line follows it in every such log, and the backtrace of the crashing
  thread is the finalizer thread's; by the engine's design the handler notifies the scene tree from the crashing thread); no viewer code calls the scene tree off the main thread (the viewer's background work is the HD frame decode,
  the chunk composes and the window art warm-up, all plain managed code or Godot `Image` objects). The viewer has no
  `unsafe` code and creates no `StringName` itself (every one is the implicit string conversion of an engine call). The
  .NET settings that did not remove it in the same stress: a one-gigabyte first-generation budget
  (`DOTNET_GCgen0size`), `DOTNET_gcConcurrent=0`, `DOTNET_TieredPGO=0`, `DOTNET_TieredCompilation=0` (each 0 to 1 crash in
  8 to 16 runs against about 3 in 10 without it, too few runs to tell a small effect). Note that .NET reads
  `DOTNET_*` names case-sensitively (`DOTNET_GCgen0size`, not `DOTNET_GCGEN0SIZE`), and `smoke --env` accepts upper-case names
  only (`parse_env` in `tools/local_dev/smoke.py`), so an upper-case form of such a name is ignored without a message. The root cause is in the engine or the .NET
  runtime, so the smoke runner works around it: a step whose log shows a crash (the crash handler's line or a fatal
  `AccessViolationException`) together with a Godot type's `Finalize` in the backtrace is run once more, says so as
  `engine crash: STEP died in Godot's finalizer thread ... and ran once more: PASS|FAIL` in the summary, and a second crash
  is a failure like any other (no per-stage retry of that step follows it). A log that printed a failure of its own before the
  crash (`FAIL`, `failure mode=`, `..._FAILED`, `frontier=`), a stopped run and a timeout are not rerun. The run summary's `note:` line also names this crash. Reproduce it with four parallel
  loops of the stage command above (`--no-build` after the first run), and keep the stage's own checks as the failing
  evidence: a crash without a Godot `Finalize` in its backtrace is not retried.
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

## All launcher tests in one command (ARC-DEV-1565, ARC-DEV-2225)

`python3 -B tools/local_dev/run_tests.py` from the repository root runs every `tools/local_dev/test_*.py` module, one process per
module, four at a time (`--jobs N`; module names after the options run only those, `test_run_queue` or
`tools.local_dev.test_run_queue`). It is quiet: one line per module, the whole output of a failing one, the last line
`LOCAL_DEV TESTS PASS|FAIL`, exit 1 when any module failed. The modules are independent (each makes its own temp folders and
queue home), which is why the pool is safe; one serial `python3 -B -m unittest discover -s tools/local_dev -t . -p 'test_*.py'`
(`-t .` names the modules `tools.local_dev.test_*`, which their imports need) runs the same tests in about 250 s, the pool in
about 90 s (the slowest module, `test_run_queue`, is the limit). The per-module commands above stay the quick way to run one.
The `test_run_queue` tests that need a run to hold its place while others queue (ARC-DEV-2254, issue #2254: under load the
fixed-length runs ended before the waiters registered) holds that run with `held()` until `release()` instead of timing it, so a
loaded Mac cannot fail them; a new test of that kind does the same.

**Root checks (owner decision 2026-11-15, ARC-DEV-2225, measure 3 of the [smoke-stability package](smoke-stability.md)):**
`run_tests.py` and `./arcanum smoke self-test --headless` are part of the root checks of CLAUDE.md section 2, because bugs of the
smoke runner, the queue tests and the self-test reached `main` while no PR ran them. `run_tests.py` goes into the one batched
`./arcanum run --kind dotnet -- sh -c '... && python3 -B tools/local_dev/run_tests.py'` step and passes inside it (a test that
calls `smoke.run` clears the queue ticket variable, the guard itself stays). The smoke self-test cannot be nested in
`./arcanum run` (`smoke` refuses a ticket variable; each smoke takes its own `quick` place and checks its own start), so it is a
second command beside the batch. Added time per PR: about 90 s for the tests and about 15 s for the self-test (plus the viewer
build when the checkout has none). A new `tools/local_dev` test that waits for fixed seconds must be made fast (hold and release
instead of sleeping) or the PR says why it is slow. The daily build runs its own copy of the root checks and does not yet include
these two (follow-up issue #2237).

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
python3 -B tools/local_dev/run_tests.py
./arcanum smoke self-test --headless
python3 -B tools/docs_check.py
```

`tools/docs_check.py` (ARC-DEV-406) needs only Python and the tracked files: it fails on a relative Markdown link whose file
or heading does not exist, on a `docs/...` path that code, tests, tables or a page name and that does not exist, and on a page
under `docs/` that no page links to, and on a row of the [owner re-check list](owner-checks.md) with a bad step id, a missing column or an
unknown status (ARC-DEV-2186); `--self-test` runs it on a synthetic tree. A change that moves, renames or deletes a
page runs it and fixes every finding in the same PR.

The `OriginalExe` tests are part of the solution run and stay inconclusive without
`ARCANUM_EXE` and the oracle's files ([function oracle](#function-oracle)).

The root solution includes Data, Presentation, Scripting, [headless Simulation movement](../world/grid-movement-runtime.md), [Session state](../world/game-session-runtime.md), the [world runtime](../world/record-resolver.md) (`Arcanum.World`), the [executable interpreter](../project/components/Arcanum.Executable.md), the ART viewer and terrain-inspect. It does **not** include the standalone terrain viewer/tests, research C# probes, or display/gamepad labs. Use their specific commands: [ART viewer](../project/components/Arcanum.Godot.md), [terrain viewer](../rendering/terrain-viewer.md), [display lab](../../experiments/display-ui-lab/README.md), [gamepad lab](../../experiments/gamepad-lab/README.md). Launch Godot projects with Godot; `dotnet run` applies to executable CLIs/harnesses, not the Godot adapter projects.

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

## Steamwoven prompt library (ARC-RES-2455)

[`tools/steamwoven_ui/prompt_library.py`](../../tools/steamwoven_ui/prompt_library.py), the prompts in `tools/steamwoven_ui/prompts/` (text only; design in the
[TRD](../ui/steamwoven-interface-trd.md#prompt-library-and-handoff-arc-res-2455)). Standard-library Python (ImageMagick `magick` only for `refs`); it runs no
Godot and no `dotnet`, so it does not use the launch queue. It reads and writes `local/steamwoven-ui/` of the main checkout (a worktree has no `local/`),
never git.

```sh
python3 -B tools/steamwoven_ui/prompt_library.py check        # task check: library, files, kinds against the TRD
python3 -B tools/steamwoven_ui/prompt_library.py self-test    # task check: synthetic inbox and provenance
python3 -B -m unittest tools.steamwoven_ui.test_prompt_library
python3 -B tools/steamwoven_ui/prompt_library.py refs         # 800 x 600 layout references from the census screenshots
python3 -B tools/steamwoven_ui/prompt_library.py inbox style-anchors-contexts
python3 -B tools/steamwoven_ui/prompt_library.py provenance style-anchors-contexts --generator NAME --generator-version V
```

## Steamwoven kit bake (ARC-DEV-2460)

[`tools/steamwoven_ui/bake.py`](../../tools/steamwoven_ui/bake.py) makes the four step sets (x1, x2/3, x1/2, x1/3; folder names `x1`, `x2-3`, `x1-2`, `x1-3`) from the kit masters
([TRD SWI-RUN-1](../ui/steamwoven-interface-trd.md#bake-and-runtime)); the standard-library PNG reader and writer is [`pngio.py`](../../tools/steamwoven_ui/pngio.py), shared with the assembly tool (8-bit RGBA masters only; canonical output).
No numpy, Godot or `dotnet`, so it does not use the launch queue. It reads and writes `local/steamwoven-ui/` of the main
checkout, never git.

- **Where things live:** masters `local/steamwoven-ui/masters/<part id>.png` (the fit tool's output, #2459); baked sets `local/steamwoven-ui/baked/<step>/<part id>.png` with
  `<step>/manifest.json` (per part: master size and SHA-256, baked size, bytes, file SHA-256 and a SHA-256 of the raw pixels; plus a hash of the part list).
  `baked/` is derived and fully reproducible from `masters/`, so it is **not backed up**: a changed master is rebaked, a part whose
  master hash is unchanged is reused (`unchanged` in the log), and `--prune` removes only baked files this tool wrote whose master is gone.
- **Backup** (a convention set by this page; no tool does it, the owner or an agent copies by hand; same spirit as the [HD pack](hd-pack.md) rules, kit decision SWI-PRV-1): the masters (and the prompt library's provenance records beside them) are the work that nothing
  reproduces, so they are copied, never moved, to a folder **outside** `hd-packs/`, next to the HD backups: `~/Library/Application Support/Arcanum Steamwoven Game/steamwoven-ui-backups/<date>/`.
  After a copy to another device, rebake there and compare the `pixels_sha256` values of the manifests (they do not depend on the zlib build; the file hashes do).
- **Install:** the tool writes nothing outside `local/steamwoven-ui/baked/`. The runtime reads the sets in place (viewer of the owner's checkout); where a packaged game on a
  player's own device keeps them is `user://steamwoven-ui/baked` (beside `user://hd-packs`; the player bakes or copies their own, the kit is never shipped or committed, SWI-PRV-2); the viewer's lookup order and the smoke that checks it are in the [TRD](../ui/steamwoven-interface-trd.md#the-kit-runtime-arc-ui-2461) (ARC-UI-2461).
- **Exactness:** a master side must be a multiple of 6 px (so of 2 and 3); every baked length is the master length times the step, exactly; x1 is the master copied pixel for pixel; each
  other pixel is the exact area average of the master pixels it covers in integer weights, taken in linear light (16-bit table) with premultiplied alpha, so no dark fringe and no
  colour from transparent pixels; a fully transparent result has colour 0 (not at x1, which is the master unchanged). Two runs give identical bytes. Speed: pure Python, under a minute for all four sets of one 2880 x 2160 master (a full-screen plate); a
  second bake reuses what is unchanged.
- **Checks** against `kit.json`: the id must be a manifest part and a master of `units` size basis must have the manifest size (`provisional` only warns; `--no-kit` skips this for scratch masters).

```sh
python3 -B tools/steamwoven_ui/bake.py bake --dry-run          # plan: what would be baked, nothing written
python3 -B tools/steamwoven_ui/bake.py bake [--steps x1-2,x1-3] [--only ID,...] [--prune]
python3 -B tools/steamwoven_ui/bake.py verify                   # files, hashes and sizes of the sets against the manifests and masters
python3 -B tools/steamwoven_ui/bake.py self-test                # task check: synthetic master, no file
python3 -B -m unittest tools.steamwoven_ui.test_bake            # task check (also in the batched run_tests.py set)
```

## Steamwoven kit fit (ARC-DEV-2459)

[`tools/steamwoven_ui/fit.py`](../../tools/steamwoven_ui/fit.py) makes the kit master of a manifest part from a generated image ([TRD SWI-GEN-4](../ui/steamwoven-interface-trd.md), SWI-KIT-2, SWI-KIT-4, SWI-VER-3):
the crop and scale to the part's master size, the alpha (real alpha, or a keyed flat background), the edge decontamination, the alpha clean-up, the low-frequency colour lock to the approved material sheet and the
provenance record. Standard library only (`pngio.py`), so no launch queue; it reads and writes `local/steamwoven-ui/` of the main checkout, never git. Same inputs, same bytes. The steps, limits and the two metrics
(edge halo, high-frequency energy) are in the module text of `fit.py`; the numbers are `HALO_MEAN_DE` 3, `HALO_P95_DE` 6 (CIE76 against the nearest opaque interior), `HF_TOLERANCE` 10 % (in L\*a\*b\*), `LOCK_MAX_DE` 15.

```sh
python3 -B tools/steamwoven_ui/fit.py fit PART_ID --batch B --prompt ID --material wood,brass --dry-run   # plan and gates; writes nothing (always first)
python3 -B tools/steamwoven_ui/fit.py fit PART_ID --batch B --prompt ID --material wood,brass [--key border|#RRGGBB] [--crop X,Y,W,H] [--size WxH] [--replace]
python3 -B tools/steamwoven_ui/fit.py fit PART_ID --source FILE --provenance FILE      # a file from outside the outbox (or --no-provenance for a hand-made source)
python3 -B tools/steamwoven_ui/fit.py fit PART_ID --source FILE --provenance FILE --halo-limits 8,20   # a recorded override of the halo gate for a textured or hair-thin cut-out (ARC-UI-2483), finite and capped at 20,45; the safety net still recolours the band
python3 -B tools/steamwoven_ui/fit.py self-test                                         # task check: synthetic disc, no file
python3 -B -m unittest tools.steamwoven_ui.test_fit                                     # task check (also in the batched run_tests.py set)
```

- **Inputs:** `--batch/--prompt` read `outbox/<batch>/<prompt>.png` and its generator record `provenance/<batch>/<prompt>.json` (the record's `image_sha256` must match the file, SWI-GEN-6); `--source` takes any PNG with `--provenance FILE`.
  The part must be a manifest row; its `size` is the master size (a part with an open size takes `--size`, a different `--size` for a sized part is refused); sides are multiples of 6 px. The source has the master's aspect within 1 % (SWI-GEN-8) or `--crop X,Y,W,H` (source pixels) says what to take.
- **Alpha:** a source with real alpha keeps it (the requested form, TRD common rule 7). `--key border` keys a flat background (colour = median of the border, refused when the border is not flat) and `--key #RRGGBB` a given colour; without either an alpha-less source stays opaque.
  The keyed edge zone takes the colour of the nearest interior pixels and an alpha from its mix with the background; edge pixels of any source take the colour of the nearest opaque pixels when they are farther than 3 dE from it, so no colour of the generator's matte stays;
  alpha of 252 or more counts as opaque art; fully transparent pixels are stored as 0 0 0 0, as the bake does. A part that touches the border cannot be keyed.
- **Colour lock:** `--material` names materials of the approved sheet (`wood`, `black`, `paper`, `parchment`, `cloth`, `brass`, `iron`, `glass`: the `material` of the cut-outs in `cutouts.provenance.json`). The sheet's sha256 must be the approved one (TRD SWI-GEN-11, constant `APPROVED_SHEET_SHA256` in `fit.py`;
  a new approval changes both). A part of two or more materials is locked region by region. A shift above 15 dE is a wrong generation (regenerate; `--force-lock` overrides); `--lock-strength 0..1` scales the shift.
- **Outputs and gates:** `masters/<part id>.png` (default `local/steamwoven-ui/masters`, what `bake.py` reads; an existing master needs `--replace`) and `provenance/fit/<part id>.json` (SWI-GEN-6: the generator record, source hash and crop, keying, lock with the sheet hash, metrics, master hash).
  A master is written only when the size, the halo gate (measured before the final safety net, `halo_final` is reported beside it), the orphan share (10 %), a non-empty master (0.1 % opaque) and the high-frequency gate hold (exit 1, nothing written, the report says which). Exit 2 = an input could not be used. The manifest's `status` and `provenance` fields of the part (`fit:<part id>`) are set by the part's batch PR, not by the tool.
- **Speed:** pure Python; a part at 1000 x 1000 takes seconds, a full 2880 x 2160 screen from a 1448 x 1086 generation with a two-material lock about 70 s; the dry run costs the same as the real run.

## Steamwoven Codex batch runner (ARC-DEV-3001)

[`tools/steamwoven_ui/codex_batch.py`](../../tools/steamwoven_ui/codex_batch.py) runs one inbox batch of the Steamwoven kit through the owner's subscription
Codex CLI on the Mac (`codex exec` for the first item, `codex exec resume <thread_id>` for the rest, one session per batch) and writes
`local/steamwoven-ui/outbox/<batch>/` with a provenance record per image; design, `batch.json` format and rules:
[TRD](../ui/steamwoven-interface-trd.md#codex-batch-runner-arc-dev-3001). Standard-library Python; it runs no Godot and no `dotnet`, so it does not use the launch
queue; it pins the model `gpt-6.1-sol` at reasoning effort `medium` on every call (owner decision 2026-11-20, [#3043](https://github.com/Simarglok/arcanum-steamwoven/issues/3043); `--model` overrides only with the owner's approval); it spends the owner's Codex subscription quota, so a real run needs the task's or the owner's word (the skill
[`steamwoven-codex-images`](../../.claude/skills/steamwoven-codex-images/SKILL.md) says how). Its tests use a fake `codex` and run in the batched `tools/local_dev/run_tests.py`.

```sh
python3 -B -m unittest tools.steamwoven_ui.test_codex_batch                          # task check: fake codex, no quota
python3 -B tools/steamwoven_ui/codex_batch.py check                                  # codex version, image_generation on, subscription login
python3 -B tools/steamwoven_ui/codex_batch.py run BATCH --dry-run [--only ID,ID]     # the commands, nothing is sent
python3 -B tools/steamwoven_ui/codex_batch.py run BATCH [--only ID,ID] [--timeout S] [--overwrite] [--crop-band]
python3 -B tools/steamwoven_ui/codex_batch.py crop IMAGE --size WxH --out OUT.png [--aspect W:H] [--fuzz PERCENT]   # one padded strip canvas to its band (ARC-UI-3083, ImageMagick)
```

## Steamwoven surface pipeline (ARC-UI-3100)

Method A ([SWI-GEN-5](../ui/steamwoven-interface-trd.md), [study](../ui/steamwoven-surface-method-study.md)) as a pipeline step: `tools/steamwoven_ui/surface_study.py` builds the zone inbox of any screen
(`zones-inbox --batch B --zones window --screen-name NAME`) and puts the zone results together (`assemble --zone top-strip=F --zone window=F --zone bottom-strip=F --out A.png`), and
`tools/steamwoven_ui/surface_compose.py build --original O.png --a A.png --out OUT.png --work DIR [--spec SPEC.json] [--provenance FILE]` composes the wood of the Codex result over the
original (the original's pixels are the stand-in for the non-wood parts), lays the kit parts of the spec over it, erases the boxes the game draws text into, measures the joins and the tone, and writes the record
`local/steamwoven-ui/provenance/surface/<zone id>.json` ([fields and gates](../ui/steamwoven-surface-method-study.md#production-pipeline-step-arc-ui-3100)); exit 0 = both gates hold, 1 = a gate failed (the picture and the record are still written), 2 = an error. The zone spec (JSON) also takes `island_min`, `fill_boxes`, `dot_bands` and `halo_boxes` (ARC-UI-3164: drop small wood-weight islands, put the Codex wood between the items of a tone-mask box, keep the original's rows of dots only in given rows, ARC-UI-3101: the Codex wood everywhere in a box outside the part's own light shape, so the old shadow round a part is no halo, and `disc_boxes` (x0, y0, x1, y1, dx0, dy0, dx1, dy1: the same for a lens, an oval or a round button, outside the exact ellipse of the rim box and 0 inside it (the oval's own corners come with its kit part, ARC-UI-3205), ARC-UI-3248 `part_boxes` (x0, y0, x1, y1 and an optional closing radius: a part that is no ellipse and must stay whole, found by its gold; wood outside it, the original's pixels inside it)); [study](../ui/steamwoven-surface-method-study.md)) and `text_boxes`, `paper_boxes` and `wood_boxes` (ARC-UI-3165, owner condition 1: the text, digits and counters of a window the game draws itself are taken out of the surface, by their ground, by the page's own local light, or by the Codex wood). The specs of the five window families are committed in `tools/steamwoven_ui/zone_specs/ctx-<family>.json` (`--spec`).
Standard-library Python plus ImageMagick (`magick`), no Godot and no `dotnet`, so no launch queue; the tests (`python3 -B -m unittest tools.steamwoven_ui.test_surface_compose tools.steamwoven_ui.test_surface_study`, about 3 s) run in the batched `tools/local_dev/run_tests.py`;
a real inventory screen takes about 3 minutes. All images stay under `local/steamwoven-ui/`.


## Steamwoven strips and end caps, batch B-BOARD (ARC-UI-2927)

Production of the four strip masters (`board.inttop`, `board.intbotom`, `board.charcreatetopbar`, `board.charcreatebottombar`, 2880 x 150 and 2880 x 570 px) and the four end caps (`cap.*`, 72 x 150 and 72 x 570 px) of the Steamwoven kit
([TRD](../ui/steamwoven-interface-trd.md), SWI-LAY-12). Steps, all local (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick, no Godot and no `dotnet`, so no launch queue:

```sh
python3 -B tools/steamwoven_ui/board_build.py refs                    # the originals at the master size, the references of the prompts (board-*, cap-*)
python3 -B tools/steamwoven_ui/prompt_library.py inbox B-BOARD        # the inbox: prompts, references, batch.json
python3 -B tools/steamwoven_ui/codex_batch.py run B-BOARD --crop-band # the owner's Codex subscription (gpt-6.1-sol); a result the runner refuses stays in codex-work/B-BOARD/
python3 -B tools/steamwoven_ui/board_build.py crop                    # the strip band of a refused padded canvas into the outbox (vertical stretch recorded, at most 12 %)
python3 -B tools/steamwoven_ui/board_build.py strips [--dry-run]      # compose (surface_compose.py build per pair), gate, original alpha, fit -> masters/ and provenance/fit/
python3 -B tools/steamwoven_ui/board_build.py caps --variant plain    # the cap bars keyed from the sheet, the soft shadow, mirror, fit (--soft-shadow)
```

The strip heights are the grid ones, 150 and 570 px (decided by this batch, [TRD](../ui/steamwoven-interface-trd.md#open-questions)). Each strip is one Codex item at the generator's size (method A); the generator draws a 19.2:1 strip at about 20:1 inside a
padded canvas (the play screen's top strip is therefore asked at 2880 x 144 and resized back to 150 px), so the band is cropped and stretched by a recorded few percent (only the wood is taken from it). `surface_compose.py build` puts the generated wood into the original's wood, every other pixel (metal, rails, sockets, the
original's rows of dots) stayed the original's as the stand-in of the hardware batch ([#2481](https://github.com/Simarglok/arcanum-steamwoven/issues/2481); the two play-screen strips lost it to the hardware parts, see below), and the original's transparent openings (sockets, portrait holes) stay transparent (alpha of the original frame).
The strips of one pair stand beside the inventory screen's window zone in the compose step, a neighbour that is not their own, so the seam at a join is gated **not worse than the same join of the original composition** (the 1.8 / 1.5 seam of the
window batches is measured there, with the generated window zone) and the tone check must pass; all numbers are in `provenance/surface/board-<pair>.json` (`board_gate`). A cap is the brass bar of one strip end (24 px, drawn by Codex on a flat
ground and keyed by trimming) with a soft shadow laid outward by the tool (48 px, dark brown, cosine fade, alpha 0.55 at the bar), the right cap its mirror; `fit.py --soft-shadow` does not count the shadow's semi-transparent pixels away from the art as orphans.
Tests: `python3 -B -m unittest tools.steamwoven_ui.test_board_build tools.steamwoven_ui.test_fit` (about 3 s; the first also runs in `tools/local_dev/run_tests.py`).


## Steamwoven hardware of the play-screen strips, batch B-HARDWARE slice 1 (ARC-UI-2481)

The metal of the two play-screen strips (`board.inttop` art 185, `board.intbotom` art 184) as 18 kit parts `hardware.hud_*` (top: two plates, the slot frame, ring, clock, infinity, hourglass and rivet; bottom: the vial and its
mirror, the cylinder and its mirror, wires, ring, dial, gem, slot frame, rail), made by `tools/steamwoven_ui/hardware_build.py` ([TRD](../ui/steamwoven-interface-trd.md), SWI-LAY-12, SWI-KIT). The menu strips are slice 2 (below); the rest of B-HARDWARE (the other
frames, slots and pop-up panels of the batch) is not in these slices. Steps, all local (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick, no Godot and no `dotnet`, so no launch queue; the Codex runs follow the `steamwoven-codex-images` skill (`gpt-6.1-sol` only):

```sh
python3 -B tools/steamwoven_ui/hardware_build.py refs --sheet hud-bottom   # per sheet (hud-bottom, hud-top, hud-top-parts): the original's shapes cut out as the references (original, material, context)
python3 -B tools/steamwoven_ui/prompt_library.py inbox B-HARDWARE-HUD      # the inbox: the three sheet prompts, references, batch.json
python3 -B tools/steamwoven_ui/codex_batch.py run B-HARDWARE-HUD           # the owner's Codex subscription; a refused sheet stays in codex-work/ and is judged by hand
python3 -B tools/steamwoven_ui/hardware_build.py cut                       # each detail cut from its sheet cell (shape mask, tone fit to the original, brass chroma mask), placements.json
python3 -B tools/steamwoven_ui/hardware_build.py fit                       # fit.py gates (alpha, halo, orphans) -> masters/hardware.hud_*.png, provenance/fit/
python3 -B tools/steamwoven_ui/hardware_build.py strips                    # the strip arts without the original's metal (the erase mask filled with the wood beside it) and the preview with the parts laid
python3 -B tools/steamwoven_ui/hardware_build.py art                       # fit.py on the two strip arts -> masters/board.inttop.png, board.intbotom.png
```

Each part is generated once, on a sheet of details on a transparent ground (the original's muted dusty tone, brass trim, no added ornament, no synthetic stains), and is cut out by the original's own shape; a part that repeats is one master laid at several places, and
the right vial and cylinder are mirrored in the build (`mirror_of`) and relit: each pixel is multiplied by the slow light (alpha-weighted luminance, box radius 10 px) of the left part over that of its mirror, limited to 0.65 to 1.5, so the light still falls from the top left while the form and the sharp detail stay the mirrored ones. Per-part flags tune a redraw to the original after the tone fit: `gain` (a luminance multiplier), `squeeze` (a nine-slice that thins a frame's border to the original's width) and `solid` with `darken_neutral` (an opaque dark body and a dark rim under a glass that the redraw made pale, as the hourglass). `placements.json` gives each place on the 6 px master grid (the part's canvas corner, up to ±3 px from the original's place); the runtime table `SteamwovenKitTable.Strips` lists the same places in the same draw order
(`test_hardware_build.py` reads the table from the C# file and fails when its part ids, their counts, their order or the 6 px grid differ; the C# tests and the kit smoke hold the part sizes, which the manifest leaves open). The strip arts the local build writes are the wood with the original's alpha openings and no metal (the erased original pixels are filled with the wood beside them, mirrored so the grain stays horizontal); the strip seam gate stays "not worse
than the original composition's (+0.05)", measured with `surface_metrics.py --join y=150 --join y=1590` on the composition with the new strips (built 4.630 / 3.286 against the original 4.611 / 3.267). A strip is drawn from the kit only with every layer in the set, so a kit without the 18
parts keeps the pack's strips. Look capture: `./arcanum smoke pr --stages bar --env ARC_AREA_BAR_STEAMWOVEN=1 --env ARC_HD_PACK=<pack> --env ARC_STEAMWOVEN_VARIANTS=kit --env ARC_STEAMWOVEN_SIZES=3840x2160,1920x1080 --env ARC_AREA_BAR_CAPTURE=<prefix> --env ARC_STEAMWOVEN_KIT_LOOK=<folder holding only the masters of the two strips, their four caps and the hardware parts>`
puts the real masters in place of the flat colours (the strip, cap and hardware colour checks are skipped, the other parts keep theirs, so the folder must hold no others). Tests: `python3 -B -m unittest tools.steamwoven_ui.test_hardware_build` (also in `tools/local_dev/run_tests.py`).

### Slice 2: the metal of the menu screens' two strips (ARC-UI-3260)

The strips of the two-strip menu screens (`board.charcreatetopbar` art 336, `board.charcreatebottombar` art 335: the save and load windows, the pre-made browser, the new character screen, the creation editor and the opening shop) get the
same treatment by the same tool: four new parts and one shared. Top strip: `hardware.menu_plate_left` (the iron end section, no openings; the HUD top plate's silhouette) and `hardware.menu_plate_right` (its mirror, relit to the top left,
`mirror_of`; its place mirrors the left one's about the strip's middle, `footprint` is the original's right section that the strip art loses), and the HUD's `hardware.hud_top_rivet` laid at the same four places (`layers_of` follows the parts' `instances`, so one part
can stand on two strips). Bottom strip: `hardware.menu_window` (the thin iron and brass frame of the message window, `keep_hole`) and `hardware.menu_ring` (the brass setting of a round button socket, one master at two places, `keep_hole`).
`brass_match` (L*, chroma, hue) brings a part's brass to the HUD strips' brass (owner check of 2026-11-25: one material; the window frame's tone fit alone had left it more golden, chroma 41 against 30 to 36, hue 82 against 76 to 78). `keep_hole` keeps a ring's or a frame's inside out of the erase (the socket and the window's dark field are the strip's own, not the original's metal) and the part's inside is transparent, so what the screen draws there (the button, the message
box) shows through. Sheets `menu-top` and `menu-bottom` (batch `B-HARDWARE-MENU`: `prompt_library.py inbox B-HARDWARE-MENU`, `codex_batch.py run B-HARDWARE-MENU`), the other steps as above (`cut` without `--sheet`, because the rivet's new places are written by the cut of its own sheet `hud-top-parts`, and `strips` refuses a strip whose parts have no places; then `fit`, `strips`, `art`). The runtime lays them
through `SteamwovenKitScreenSpec.TopLayers` and `BottomLayers` (`SteamwovenKitTable.MenuTopLayers`, `MenuBottomLayers`): a layer's master position and size scale by the strip's own ratio on each axis (the screen's strips are drawn stretched into their 41 and
159 units), and a screen is drawn from the kit only when every layer is in the set, so a kit without the four parts keeps the screen's own arts whole.


### Slice 4: the pop-up panel bases and frames (ARC-UI-3278)

The 16 pop-up panel arts of [#3278](https://github.com/Simarglok/arcanum-steamwoven/issues/3278) (the 410 x 107 "Rot" family 7, 200, 274, 288, 290, 291, 354, 564, 642, 842 and the framed panels 822, 298, 826, 292, 565, 328) are kit parts `hardware.*` (the manifest rows of the arts, status master), each the art's whole rectangle at the kit's 3.6 scale, made by `tools/steamwoven_ui/popup_kit.py` (no Codex) by the owner's rule "pack-first" ([TRD](../ui/steamwoven-interface-trd.md)). Art 128 (`skill_ro`, an olive noise art, no code or window draws it) is no part (#3307).

* **A pack frame of the art itself** (12 arts): the frame of the art in the player's old HD pack (`interface-redraw-4x/interface/<folder>/<name>-f000.png`, 4 times the original; `--pack` overrides the default install path) resampled to the master (premultiplied, linear light; what the original leaves open is open in the frame before the resample, so no colour bleeds), the ALPHA analytic (the original's transparent places blurred once, enlarged with a cubic filter, cut with a steep threshold; the pack's own alpha is not used), the edge band decontaminated (`fit.py`). A pack frame is refused when its biggest brass frame is not where the original's is (`FIT_TOLERANCE` 3 original px: the screens draw text and slots in the original's rectangles).
* **A frame composed from the pack's other frames** (4 arts, `Spec.compose`, a list of `Piece`s: a rectangle of a pack frame enlarged to a rectangle of the new art, optionally turned (`transpose`), mirrored (`flip_x`), faded at an edge or cut as a disc): 291 (mess_rot's left part, bars and the left part mirrored to the right), 354 and 642 (mess_rot's top and bottom bars, the side bars from them turned, the margins from the menu plaque's stone; 642 also a mess_rot socket ring with the original's face copied in (`pack_copies`) and the original's baked blue input field kept (`fills`)), 826 (sleep_base's left part and right end spliced, the seam faded). The composed frame goes through the same steps and the same fit gate as a pack frame.
* **What follows for both:** the pack's flat black fields take the original's tone back (the windows by the original's dark class, `fills` boxes: art 328's grey box, 298's window), its black slots the original's stone (`stone_wells`: 274 and 564), and the pack's brass is lifted to the approved brass of the kit (HUD ring and clock frame #3259, menu window #3260; per channel at most 1.3, only when the mean is more than 6 % under the approved tone in luminance; the gain is in the provenance record). Art 564's baked sign in the picture field is kept (the original has it too). No shadow is baked in (it is cast where the part is laid, #3249).
* **Rule build** (`render`, no part of the kit any more): every pixel of the original is classified, the iron is new, the brass is shaded by a height field; `--alt-dir DIR` writes it there for the composite's alternative column.

Gates: size = the manifest's on the 6 px grid, at most 2 % of the pixels where the transparent places differ from the original's, at most 1.2 % translucent pixels, no magenta, the edge halo gate of `fit.py`; a rule build also has no black border pixel.

```sh
python3 -B tools/steamwoven_ui/popup_kit.py plan                                 # the 16 parts and their manifest rows
python3 -B tools/steamwoven_ui/popup_kit.py build [--only ID,...] [--dry-run] [--pack DIR] [--alt-dir DIR]   # masters/hardware.<id>.png and provenance/rule/<id>.json (about 35 s)
python3 -B tools/steamwoven_ui/popup_kit.py sheet --out FILE [--only ...] [--zoom 2] [--alt-dir DIR] [--alt-label TEXT]   # composite original | kit master | alternative, English labels (ImageMagick)
```

The build needs the approved HUD ring, clock frame and menu window masters (`local/steamwoven-ui/masters/`, the brass tone) and the census frames. Tests: `python3 -B -m unittest tools.steamwoven_ui.test_popup_kit` (also in `tools/local_dev/run_tests.py`). The runtime does not lay these masters yet (the popup windows draw their own arts until a later slice).

## Steamwoven hardware: rings, slots and small frames, batch B-HARDWARE slice 3 (ARC-UI-3261)

33 small hardware parts made BY RULE from the original's own frames by `tools/steamwoven_ui/hardware_kit.py` (no Codex; [TRD](../ui/steamwoven-interface-trd.md), SWI-KIT): the eight lens rings (arts 198, 222, 231, 232, 233, 367, 670, 746), the ten hotkey slot insets (172 to 181), the five maintain slot woods (188 to 192) and plugs (628 to 632), the portrait highlight (646), the portrait select (332) and the roller template (314). Local only (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick for the composite, no Godot and no `dotnet`, so no launch queue:

```sh
python3 -B tools/steamwoven_ui/hardware_kit.py plan                                  # the parts and their manifest sizes
python3 -B tools/steamwoven_ui/hardware_kit.py rule [--only PART,...] [--dry-run]     # masters/hardware.*.png and provenance/rule/*.json (the gates run first)
python3 -B tools/steamwoven_ui/hardware_kit.py sheet --out FILE [--zoom N]            # before / after composite, English labels
python3 -B tools/steamwoven_ui/hardware_kit.py self-test
```

The rules: the lens ring's shape is analytic (a band of 5 original px outside the art's transparent circle, both edges filtered by a 2 px tent over 8x8 sub-samples so no stair shows, drawn whole) and its material is polished cast brass: the body tone and the grain come from the kit's approved brass, the HUD ring (`hardware.hud_ring`, #3259) and the socket ring (`hardware.menu_ring`, #3260) unrolled along their circumference and high-passed (the two end to end are longer than the lens ring's circle, so nothing repeats); the light is by rule (a sharp highlight ridge at the top left, a broad sheen, whole-harmonic reflection bands, a copper reflection at the bottom right, a narrow dark chamfer with a thin bright edge on the inner side, no dark outline). `hardware.lens_ring` and `hardware.lens_ring_filled` are 354 x 354 px (the art's square plus 18 px each side, an open size in the manifest); the eight lens arts' rows are the same ring cut to the art's own 318 x 318 square. The corners and the circle are transparent (the filled look keeps art 367's opaque dark fill). A hotkey slot is one inset (the mean of the ten arts, each normalised by its own wood: the shadow as black with the original's depth as alpha, the edge lines opaque, the middle open). The wood of a maintain slot and its plug is a crop of the strip master's plain wood (`board.inttop`, the stretch left of the slot openings; the tool refuses it if it is no longer opaque) in the original art's tone and under its smooth light. The two selection frames are blocks (every original pixel a block of the master), the roller is smooth along its height and blocks across. Gates before a master is written: the manifest size on the 6 px grid, not empty, no translucent pixel inside a solid detail, a wood tile opaque. Records: `provenance/rule/<part>.json` (`rule:hardware_kit:<part>` in the manifest). Tests: `python3 -B -m unittest tools.steamwoven_ui.test_hardware_kit` (also in `tools/local_dev/run_tests.py`).


## Steamwoven schematic wide button as a kit part, pack first (ARC-UI-3271)

The schematic window's wide round button (a brass bar, a ring round the olive dome and two arrow wells, baked into art 365 Schematic_Base) is the kit part `plate.schematic_wide_button`, made by `tools/steamwoven_ui/wide_button_kit.py` PACK FIRST (owner rule 2026-11-25): the old HD pack has a good frame of the art (`local/hd-packs/interface-redraw-4x/interface/schematics/panels/schematic_base-f000.png`), so the part is that frame's brass under an analytic alpha (a stadium united with the ring's disc, minus the dome's and the two wells' holes, edges filtered by the lens ring's tent) with the surfaces' soft shadow baked outside it; nothing is generated. The holes are open: the surface keeps the original's dome face and arrows there. `zone_specs/ctx-schematic.json` lays it with `kit_parts` and three `disc_boxes`; the #3248 `part_boxes` entry for the button is gone.

```sh
python3 -B tools/steamwoven_ui/wide_button_kit.py rule [--pack FILE] [--dry-run] [--out FILE]   # masters/plate.schematic_wide_button.png and provenance/rule/*.json (the gates run first)
python3 -B tools/steamwoven_ui/wide_button_kit.py outline --out FILE                            # the analytic outlines over the pack frame at 3x (a check picture)
python3 -B tools/steamwoven_ui/wide_button_kit.py self-test
```


## Steamwoven square HUD buttons, batch B-BUTTONS slice 1 (ARC-UI-2482)

Production of the kit parts of the five square buttons of the top strip (Character 169, Inventory 186, Logbook 187, Map of the town 193, World map 194; lit arts 561, 559, 560, 558, 195), the first slice of batch B-BUTTONS
(the other slices are issues of their own). Local (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick, no Godot and no `dotnet`, so no launch queue:

```sh
python3 -B tools/steamwoven_ui/button_kit.py refs                      # the references of the plate prompt: the original plate without its symbol (30 x), the brass-edge swatch, the HUD context's buttons
python3 -B tools/steamwoven_ui/prompt_library.py inbox B-BUTTONS       # the inbox
python3 -B tools/steamwoven_ui/codex_batch.py run B-BUTTONS            # the owner's Codex subscription (gpt-6.1-sol): one item, about 90 s
python3 -B tools/steamwoven_ui/button_kit.py flatten                   # the plate as an opaque square (the generator's notched corners take the frame's shadow colour) + its provenance record
for n in char_but invn_but log_but tmap_but wmap_but; do python3 -B tools/steamwoven_ui/fit.py fit plate.$n --source local/steamwoven-ui/work/B-BUTTONS/plate-square-hud-opaque.png --provenance local/steamwoven-ui/work/B-BUTTONS/plate-square-hud-opaque.provenance.json; done
python3 -B tools/steamwoven_ui/button_kit.py build [--look crisp|sharp|cells|smooth] [--dry-run]   # crisp (default) = the pilot's symbol rule   # glyphs, lit glyphs, hover / pressed overlays, lit faces -> masters/ and provenance/rule/
python3 -B tools/steamwoven_ui/button_kit.py sheet --out FILE          # the before / after composite (English labels)
```

`build_kit.py` writes the manifest rows (the five plates and the lit faces from the census, the glyphs, lit glyphs and overlays as new rows of batch B-BUTTONS, all `master`). A glyph or an overlay is an original frame over its base frame: the symbol's cells in their own colours with a smooth sharp outline, the glow or darkening a separate soft layer under it; laid over the plate
it gives the original's frame back (the symbol's cells exactly; the tests check it); the plate is the only generated picture. A glyph layer of high contrast (the lit symbols, the globe) keeps the symbol as the core and lets its soft edge fall off outward as a halo (alpha = distance over the core threshold, no plateau, no rim; constants `CORE_DE`, `CORE_SHARE`, `HALO_*` in `button_kit.py`). Rule E is the glyph rule of the whole #2482 batch, slices included. Tests: `python3 -B -m unittest tools.steamwoven_ui.test_button_kit` (about 7 s, in the batched `tools/local_dev/run_tests.py`).

## Steamwoven small stone and dark buttons, batch B-BUTTONS slice 6 (ARC-UI-3206, ARC-UI-3323)

The kit parts of the small stone and dark buttons (18 arts) in three families ([TRD](../ui/steamwoven-interface-trd.md#asset-kit), owner decisions 2026-11-25): `rect` from the old HD pack (the owner kept only the stone Add / Remove 647, 648 and the follower arrows 501, 502), `dome` by rule (the round skill and slider buttons 624, 625, 667, 668: the accepted round face of #3202 under the brass ring of the sockets, the symbol by rule E) and `codex` (the olive plus and minus 804, 805, the scroll arrows 317, 318, the follow scrolls 506 to 509 and the skill-point plus and minus 257, 258: one generated sheet of cells per family, cut and reduced). Local (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick for the sheet, no Godot and no `dotnet`, so no launch queue:

```sh
python3 -B tools/steamwoven_ui/small_kit.py plan                           # the 18 buttons, their family, states and the manifest size check
python3 -B tools/steamwoven_ui/small_kit.py build [--only NAME,...] [--dry-run] [--pack DIR] [--sheets DIR] [--sp-variant sp-a|sp-b]   # plate / _hover / _pressed / glyph -> masters/ and provenance/rule/ (pack file, sheet and hash in each record)
python3 -B tools/steamwoven_ui/small_kit.py sheet --out FILE [--zoom N] [--only NAME,...]      # the composite original idle, pressed | new idle, hover, pressed (English labels)
python3 -B tools/steamwoven_ui/small_kit.py self-test
```

The pack is `~/Library/Application Support/Arcanum Steamwoven Game/hd-packs/interface-redraw-4x` (`--pack` names another); it is read, never written. The Codex sheets are `local/steamwoven-ui/small-sheets/<id>.png` (`ol`, `sc`, `fs`, `sp-a`, `sp-b`) with the runner's provenance record next to each (`--sheets` names another folder); they are made through `codex_batch.py` (items with the original frames enlarged and the neighbouring pack frames as the style sample; a layout of at most about 2:1 per sheet, because the generator returns a wrong aspect for a wide one) and the raw outputs are archived under `ARC-UI-3323`. The scroll arrows are a triangle on a flat magenta ground, cut from it (the ground and 3 px round it are transparent). `sp-a` (grey symbol) and `sp-b` (red checkered symbol) are two looks of the skill-point plus and minus; `--sp-variant` picks one (default `sp-b`, until the owner chooses). Tests: `python3 -B -m unittest tools.steamwoven_ui.test_small_kit` (about 1 s; also in `tools/local_dev/run_tests.py`).

## Steamwoven gauges and readouts, batch B-GAUGES (ARC-UI-2483)

Production of the 56 gauge parts (vials, bars, the clock, experience pips, scroll thumbs, ammunition counters, the load watch, pointers; TRD SWI-KIT-6). Local (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick, no Godot and no `dotnet`, so no launch queue:

```sh
python3 -B tools/steamwoven_ui/gauge_kit.py plan                         # every part of the batch and how it is made (codex collage / composite / copy / rule); fails when a part is missing
python3 -B tools/steamwoven_ui/gauge_kit.py refs [--only GROUP,...]      # the eight collages (refs/<id>-original.png) and their layouts (refs/<id>-layout.json)
python3 -B tools/steamwoven_ui/prompt_library.py inbox B-GAUGES          # the inbox: prompts, collages
python3 -B tools/steamwoven_ui/codex_batch.py run B-GAUGES               # the owner's Codex subscription (gpt-6.1-sol): eight items, about 1 min each
python3 -B tools/steamwoven_ui/gauge_kit.py cut [--only GROUP,...] [--dry-run]   # the results -> cells -> fit.py masters (+ the moon and counter composites, the joined chain)
python3 -B tools/steamwoven_ui/gauge_kit.py rule [--only PART,...]       # the 16 rule parts -> masters/ and provenance/rule/
python3 -B tools/steamwoven_ui/gauge_kit.py sheet --out FILE             # the before / after composite (English labels)
```

A collage puts every part of a family on a flat magenta ground (`FF00FF`), each cell the original's frame enlarged to the cell size, as the layout truth. The generator often returns another canvas than asked (another aspect, the art centred and scaled, or a "refused" run that still saved a
picture): `cut` maps the layout onto the result by the bounding box of what is not ground (against the collage's own), finds each cell by its content, keys the ground with the fit tool's keying (a magenta-leaning pixel is ground; an opaque cell has its outer lines that show the ground peeled off, and one more), brings the cell to its master aspect (an opaque cell is stretched within 10 %,
a cut-out is centred on a transparent canvas within 15 %) and fits it; a result the generator refused for its aspect is read from its run folder and the record says so (`accepted`). A moon disc and a counter's picture are layers: scaled and placed by their content box on the original's (the moons by
one scale, the left edge and the bottom of the whole disc, because the frame cuts the disc's top) and laid over the base master with `over`. A keyed cut-out that the standard halo gate refuses (a bumpy brass rim, a needle a few pixels wide, measured before the safety net) is fitted with the least of the recorded
overrides `--halo-limits 8,20` or `16,40` (each is under the cap 20,45; the safety net recolours the band as always; the override is in the part's fit record under `limits.halo_override`). The rule parts: the rows (or columns) of one band are replaced by their per-column median and the profile is resampled by the fit tool's
premultiplied Catmull-Rom; the four small detailed parts the owner wanted free of generated tone shift (`scrllslidem1`, `timepnts`, `mt_apt`, `amo_rol`, axis `smooth`) are the original's frame resampled and nothing else (their cells stay in the collages' layouts but are not used); `gauge.item_dam` is the last of its nine linear-ramp frames.
The halo override has a ceiling: `fit.py --halo-limits` takes finite values up to 20,45 and refuses more (the ceiling and the used pair are in `limits.halo_override` of every fit record that used it, 6 parts of this batch); the gate's final edge check is a safety net that recolours the band and always passes, so the ceiling is the real limit (#3073 tracks the gate itself). `kit_check.py` looks at the masters on disk and fails a master that has a visible pixel of the generation ground's colour (magenta): transparency is the alpha channel alone (`--no-masters` skips it). The two chain strips are the same picture. Tests: `python3 -B -m unittest tools.steamwoven_ui.test_gauge_kit` (about 1 s; also in `tools/local_dev/run_tests.py`).


## Steamwoven round HUD buttons and the ball, batch B-BUTTONS slice 2 (ARC-UI-3202)

The kit parts of the four round buttons of the bottom strip (Combat 470, Schematics 471, Skills 472, Spells 473) and the Fate Points / Sleep ball (art 137), the second slice of B-BUTTONS (same rules as [slice 1](#steamwoven-square-hud-buttons-batch-b-buttons-slice-1-arc-ui-2482)).
Local (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick, no Godot and no `dotnet`, so no launch queue:

```sh
python3 -B tools/steamwoven_ui/round_kit.py refs                       # references of the two plate prompts: the original face without its symbol (26 x), the ball (44 x), the brass-edge swatch, a round-button cut-out
python3 -B tools/steamwoven_ui/prompt_library.py inbox B-BUTTONS       # the inbox (three items; the square one is skipped by the runner while its outbox file exists)
python3 -B tools/steamwoven_ui/codex_batch.py run B-BUTTONS            # two items: plate-round-hud, plate-ball-hud (gpt-6.1-sol)
for n in combat_button schematics_button skills_button spells_button; do python3 -B tools/steamwoven_ui/fit.py fit plate.$n --masters local/steamwoven-ui/work/B-BUTTONS/fitted --source local/steamwoven-ui/outbox/B-BUTTONS/plate-round-hud.png --provenance local/steamwoven-ui/outbox/B-BUTTONS/plate-round-hud.provenance.json; done
python3 -B tools/steamwoven_ui/fit.py fit plate.lilgrnbut --masters local/steamwoven-ui/work/B-BUTTONS/fitted --source local/steamwoven-ui/outbox/B-BUTTONS/plate-ball-hud.png --provenance local/steamwoven-ui/outbox/B-BUTTONS/plate-ball-hud.provenance.json
python3 -B tools/steamwoven_ui/round_kit.py gloss [--dry-run]                                        # the original's gloss arc over the five fitted plates -> masters/, noted in each fit record
python3 -B tools/steamwoven_ui/round_kit.py build [--dry-run] [--only combat_button,...,lilgrnbut]   # glyphs, overlays, the empty ball glyphs -> masters/ and provenance/rule/
python3 -B tools/steamwoven_ui/round_kit.py sheet --out FILE [--zoom N] [--plate-dir DIR]          # the before / after composite (English labels)
```

The fit tool's halo gate ([#3073](https://github.com/Simarglok/arcanum-steamwoven/issues/3073), open) fails the plates' real rim shading, so the fit above is run through a wrapper that relaxes the limits (the repository's constants stay); the safety net leaves p95 under 3.
The glyph's outline is the pilot's glyph mask of the lit frames; the gloss arc of the dome is read from the template as a smooth curve of the original's shape and peak brightness. The plate template of a glyph is the flat dome colour inside radius 16 cells and the voted ring outside it; a pressed overlay is a soft black shade layer (alpha = the brightness the original loses outside the symbol's footprint) plus the lit symbol, clipped to the plate's alpha; art 137 has no symbol, so `glyph.fate_but` and `glyph.sleep_but` are empty layers.
Tests: `python3 -B -m unittest tools.steamwoven_ui.test_round_kit` (about 7 s, in the batched `tools/local_dev/run_tests.py`).

## Steamwoven dark-dome buttons, batch B-BUTTONS slice 3 (ARC-UI-3203)

Part one of the third slice of B-BUTTONS (the rest of #3203 is split into issues by plate family): the character sheet's three skill-group buttons (arts 34, 35, 36), which stand on the round plate of [slice 2](#steamwoven-round-hud-buttons-and-the-ball-batch-b-buttons-slice-2-arc-ui-3202), and the crescent family, dark domes with a brass crescent at the lower right in frames that are not square (Cancel / Done 32 and 33 with their twins 202 and 203, Clear 201: 38 x 37 units, 138 x 132 px; Big Green Left / Right 283 and 284: 34 x 33 units, 120 x 120 px).
Local (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick, no Godot and no `dotnet`, so no launch queue:

```sh
for n in char_common_skills char_tech_skills char_spells_skills; do python3 -B tools/steamwoven_ui/fit.py fit plate.$n --masters local/steamwoven-ui/work/B-BUTTONS/fitted --source local/steamwoven-ui/outbox/B-BUTTONS/plate-round-hud.png --provenance local/steamwoven-ui/outbox/B-BUTTONS/plate-round-hud.provenance.json; done   # the slice-2 item again, through the relaxed-limit wrapper
python3 -B tools/steamwoven_ui/disc_kit.py refs                                # references of the two dome prompts: the original's dome alone (its brass cleared), shown stretched to a square
python3 -B tools/steamwoven_ui/prompt_library.py inbox B-BUTTONS               # the inbox (five items; the three of earlier slices are skipped by the runner while their outbox files exist)
python3 -B tools/steamwoven_ui/codex_batch.py run B-BUTTONS                    # plate-dome-hud, plate-dome-small-hud (gpt-6.1-sol)
python3 -B tools/steamwoven_ui/disc_kit.py place                               # the generated dome -> the master's canvas where the original's disc stands, with the derived provenance record
fit.py fit plate.cncl_big|done_big|cler_big  --source work/B-BUTTONS/plate-dome-hud-placed.png --provenance ...-placed.provenance.json     # as slice 2, through the relaxed-limit wrapper
fit.py fit plate.big_grn_l|big_grn_r         --source work/B-BUTTONS/plate-dome-small-hud-placed.png --provenance ...-placed.provenance.json
python3 -B tools/steamwoven_ui/round_kit.py gloss && python3 -B tools/steamwoven_ui/round_kit.py build --only char_common_skills,char_tech_skills,char_spells_skills
python3 -B tools/steamwoven_ui/disc_kit.py gloss [--dry-run] [--ring FILE]      # the original's gloss arc and the brass crescent (the strip's ring, masters/hardware.hud_ring.png) over the five fitted domes -> masters/, noted in each fit record
python3 -B tools/steamwoven_ui/disc_kit.py build [--dry-run] [--family crescent-big|crescent-small] [--only cncl_big,...]   # glyphs and overlays -> masters/ and provenance/rule/
python3 -B tools/steamwoven_ui/disc_kit.py sheet --out FILE [--zoom N] [--family KEY] [--only NAME,...]                 # the before / after composite (English labels)
```

The frame functions (`button_kit.render_crisp`, `resample_plane`, `outside_cells`, `round_kit.gloss_layer`) take a frame of any width and height (a `(width, height)` pair for the master); the square results are byte for byte the slice-1 and slice-2 masters. The plate template of a family is voted per cell from the idle and hover frames of all its buttons (two buttons must agree, a colour that is no dome colour is not voted in the middle of the dome, a cell fewer than two buttons show plain is filled from its neighbours, a trace of a symbol's edge in the middle of the dome takes the local median; all symbols of these families lie in that middle, `SPECK_SHARE` of the radius); the dome's middle and radius of a family are constants fitted once to the outline of its frames (`FAMILIES`). The brass crescent is made by rule, not generated: its inner and outer radius per 5 degrees of angle are read from the original's brass cells (smooth, tapering to points), its surface is cut from the strip's brass ring master at its lit side (owner 2026-11-25: thin as the original, warm ring brass), and a dark gap of one cell lies between it and the dome; no overlay takes the original's brass cells. A twin art is refused unless its frames are byte for byte the same. The halo gate of the fit tool ([#3073](https://github.com/Simarglok/arcanum-steamwoven/issues/3073)) is relaxed as in slice 2 (mean 15 / p95 30; after the safety net p95 under 3); the limits are in each plate's fit record.
Tests: `python3 -B -m unittest tools.steamwoven_ui.test_disc_kit` (about 4 s, in the batched `tools/local_dev/run_tests.py`).

## Steamwoven parchment boxes, batch B-BUTTONS slice 5 (ARC-UI-3205)

Part one of the fifth slice of B-BUTTONS (the rest of #3205 is split into #3309, #3310 and #3311 by plate family), by the owner's pack-first rule ([TRD SWI-KIT-5](../ui/steamwoven-interface-trd.md)): the three oval boxes of the inventory, loot and barter windows (Drop 224, Use 342, Gamble 345 and their lit 225, 343, 344 and refused 348, 347, 346 frames; 80 x 43 units, 288 x 156 px) are the old generated HD pack's frames (`local/hd-packs/interface-redraw-4x`, 320 x 172) resampled to the master with an analytic alpha: the ring's ellipse united with the four corner triangles (the corner ellipse of ARC-UI-3290), everything else (the old wood between the ring and the corners) clear. No Codex. Local (`local/`, never in git), standard-library Python plus ImageMagick, no Godot and no `dotnet`, so no launch queue:

```sh
python3 -B tools/steamwoven_ui/parchment_kit.py build [--dry-run] [--only drop_off,...]   # the nine parts -> masters/ and provenance/rule/ (a missing or odd-sized pack frame is an error)
python3 -B tools/steamwoven_ui/parchment_kit.py sheet --out FILE [--zoom N]                # original | the new part on a wood stand-in (English labels)
python3 -B tools/steamwoven_ui/build_kit.py && python3 -B tools/steamwoven_ui/kit_check.py
python3 -B tools/steamwoven_ui/surface_compose.py build --original refs/ctx-inventory-master-2880x2160.png --a compare/surface-study/method-a.png --out OUT.png --work DIR --spec tools/steamwoven_ui/zone_specs/ctx-inventory.json --kit-base masters   # the inventory surface with the two ovals laid (`kit_parts`)
```

The inventory surface lays `plate.use_box_off` (the hand oval) and `plate.drop_off` (the arrow oval) with `kit_parts` at master pixels (x 2530, y 1191 and 1385); the interim analytic corner triangles of ARC-UI-3290 (`plate_boxes`) are gone from `surface_compose.py` (a spec with `plate_boxes` is refused). The oval's soft shadow stays the surface's (`disc_boxes`, #3249). Tests: `python3 -B -m unittest tools.steamwoven_ui.test_parchment_kit` (about 1 s, in the batched `tools/local_dev/run_tests.py`).
## Steamwoven college tabs and skill-school buttons, batch B-BUTTONS slice 4 (ARC-UI-3204)

The kit parts of the 16 college tabs of the spell window (arts 11, 38 to 122 step 6, 24 x 24 units) and the 12 skill-school buttons of the character sheet (arts 302 to 313): the player's own HD-pack frames as they are, cut to the ring's circle by an analytic round alpha (owner decision 2026-11-25; [TRD](../ui/steamwoven-interface-trd.md), the 3204 paragraph).
Local (`local/steamwoven-ui/`, never in git), standard-library Python plus ImageMagick for `sheet`, no Godot and no `dotnet`, so no launch queue. The pack is `local/hd-packs/interface-redraw-4x` (read only, found through its `index.tsv`).

```sh
python3 -B tools/steamwoven_ui/school_kit.py check [--only NAME,...]                                   # every button has its three pack frames; the circles lie inside the arts
python3 -B tools/steamwoven_ui/school_kit.py build [--only NAME,...] [--out DIR] [--dry-run]           # the 112 parts -> masters/ and provenance/rule/
python3 -B tools/steamwoven_ui/school_kit.py codex-inbox --stage idle|states [--only NAME,...]        # Codex batch B-BUTTONS-FIX (Nature, Water, Forc): prompts, refs (pack frame, original x32, idle sample) -> inbox/
python3 -B tools/steamwoven_ui/codex_batch.py run B-BUTTONS-FIX                                        # idle first; then `codex-inbox --stage states` and run again (hover, pressed)
python3 -B tools/steamwoven_ui/school_kit.py codex-place [--only NAME,...]                             # the generated images registered onto the pack frames' geometry -> work/B-BUTTONS-FIX/placed (`build` uses them)
python3 -B tools/steamwoven_ui/school_kit.py sheet --out FILE [--only NAME,...] [--zoom N] [--columns N]   # original | idle | hover | pressed over a wood ground with the run-time shadow (English labels)
```

`plate.<name>` is the pack's idle frame, `plate.<name>_hover` and `_pressed` the hover and pressed frames in full, `glyph.<name>` an empty layer (for the three Codex-redrawn buttons the frames are the generated images registered onto the pack frames' geometry, all three states or none). The circle of every art (the ring's gold circle and the cut's half unit) and the shadow's numbers are read from the viewer's `tools/terrain-viewer/Core/AreaKitRoundParts.cs`, one source for the part's edge and the run-time shadow; the alpha is analytic (one pixel wide), the colour outside the circle is carried inward before the stretch to the master's size, so no old background reaches the edge. Each part's provenance record (`provenance/rule/<id>.json`) names the pack frame's sha256 and the index's sha12; the three symbols the pack reads wrongly (art 74, 86, 56: `CODEX_SYMBOLS`) are redrawn by Codex on the pack frame (source `codex` in the record, with the generation record and the placed frame's hash).
Tests: `python3 -B -m unittest tools.steamwoven_ui.test_school_kit` (in the batched `tools/local_dev/run_tests.py`).

## Controller test evidence summariser (ARC-GPAD-1376)

[`tools/input/evidence_summary.py`](../../tools/input/evidence_summary.py): standard-library Python that reads the opt-in evidence files the
viewer writes (format and recording: [controller schemes, evidence log](../input/controller-schemes.md#evidence-log-arc-gpad-1376-merged)).
It runs no Godot and no `dotnet`, so it does not use the launch queue; it reads only the files named on the command line.

```sh
python3 -B tools/input/evidence_summary.py --self-test    # task check: a synthetic session, no game file
python3 -B tools/input/evidence_summary.py evidence-*.jsonl [--json]
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

## Movie decoder (ARC-RENDER-1170)

The original's Bink movies play through an external `ffmpeg` (owner decision 2026-11-12, [Bink movies](../research/bink-movies.md) option O1): nothing of FFmpeg is linked, shipped or committed, and no decoded frame or sound is written to disk. The viewer finds `ffmpeg` and `ffprobe` through `ARC_FFMPEG` (the path of the `ffmpeg` program, its `ffprobe` is looked for beside it), then the search path, then `/opt/homebrew/bin`, `/usr/local/bin`, `/usr/bin`, `/snap/bin`. Install it with `brew install ffmpeg` on a Mac. Without it the main menu hides View Intro with the reason, a new game starts without its two movies and the viewer says so in one log line; nothing waits and nothing blacks out.

- **Where movies play:** only in the game build (`./arcanum package mac|win`: an assembly stamped with its package commit) and in the smoke stage `movies`; the source viewer (`./arcanum area|map`) and every other automated run play none, and the main menu then hides View Intro with the reason "movies play in the game build only" (owner 2026-11-12, ARC-RENDER-1242). In the game build the calls are the original's: the start-up before the first menu (the Sierra and the Troika logo by path, then movie 8), View Intro and a new game's start (movies 1 and 7). A key press ends a movie. There is no switch for the source viewer; to see a movie while developing, run the `movies` stage with `ARC_AREA_MOVIE_CAPTURE` or build the package.
- **Checks:** the player's unit tests (`MoviePlaybackTests`, `FfmpegMovieSourceTests`; the ffmpeg ones generate a pattern file with ffmpeg itself and report `Inconclusive`, not a pass, without it), the opt-in real-data tests (`DAT_ROOT`, category `RealData`: `AreaMovieTableTests` finds the eight files, `FfmpegMovieSourceTests` opens them with the header numbers of the research), and the smoke stage `movies` (`./arcanum smoke pr --stages movies`, with `--hero` by default): it plays two generated test movies on the movie screen and checks the covering, the frozen clocks, the key skip, the finish and the failures that must not hang; it prints `NONE` without ffmpeg. `--env ARC_AREA_MOVIE_CAPTURE=FOLDER` saves a `before.png` and an `after.png` of the installed movie 8 for a graphics report (never committed). The smoke stage `slides` (ARC-RENDER-1879, ARC-RENDER-2197, ARC-DEV-3116; `./arcanum smoke pr --stages slides`, its own `--main-menu true` process, about 90 s with each movie ended by the key skip after its checks; `--all` or the complete run plays every movie to its end, about 230 s, and asserts it ended by itself) walks four endings on the installed data, each from a new game of its own: Kerghan's dying script 2863 through the dying run, its movies 2 and 5 through the real decoder (the movies are passed over without ffmpeg, said in the log), the end-game show of script 997's queue with the installed pictures, then the reset and the menu fade-in; the Vendigroth underground's use script 2862 used with the item it names (movies 3 and 5, flag 2053: slide 3); and the end game asked for with flag 2632 (slide 1) and flag 2050 (slide 4); `--env ARC_AREA_SLIDES_CAPTURE=FOLDER` saves a PNG of each movie, slide and the reopened menu (never committed); the opt-in tests `AreaEndingScriptsRealDataTests` (`DAT_ROOT`, terrain viewer tests, `FullyQualifiedName~AreaEndingScriptsRealDataTests`: the dying scripts 2863 and 3061 and the use script 2862 without a window) and `AreaEndingQueuesRealDataTests` (the slides script 997 and the scripts under it queue for fifteen flag and quest-state sets) pin the queues.

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
| Executable interpreter | `ARCANUM_EXE`; the replays also the oracle files of 0x4A6980 and 0x4A68B0 (`ARCANUM_ORACLE_DIR`, default `research-private/oracle`) | Executable project, `TestCategory=OriginalExe`; [contract](../project/components/Arcanum.Executable.md#verification) |
| Prototype builder | `ARCANUM_EXE` and `ARCANUM_GAME_ROOT` (the installed game folder; inconclusive without it); the comparison also `ARCANUM_ORIGINAL_PROTO` (a folder of the 1,423 files the original wrote); `ARCANUM_PROTO_OUT` writes the built files for a look (refused inside a git repository) | Executable project, `FullyQualifiedName~PrototypeBuilderRealData`, `TestCategory=OriginalExe`; about one minute in Release (`-c Release`: Debug is several times slower), the builds run in parallel; [contract](../project/components/Arcanum.Executable.md#the-prototype-builder) |
| Attack ART census (ARC-COMBAT-1932) | `DAT_ROOT` = the installed game folder | World project, `FullyQualifiedName~AttackArtCensusRealDataTests&TestCategory=RealData` (seconds); the installed critter, monster and unique NPC body ARTs by the attack animations (20, 21, throw 14) their files name, per weapon slot; names only; a failure means the counts changed: update [ledger VCG11](../research/ledger/combat.md) |
| NPC art armour part census (ARC-SCRIPT-2351) | `DAT_ROOT` = the installed game folder | Terrain viewer tests, `FullyQualifiedName~AreaBodyArmorArtCensusTests&TestCategory=RealData` (about 3 min); the installed NPC rows by the armour part of their stored art against the body armour their map data lists; a failure prints the changed counts to paste into the test; [ledger AR8](../research/ledger/scripts.md#get-armor-and-the-group-in-list-conditions-arc-script-2326) |
| Script trigger refusal baseline (ARC-DEV-2816, epic [#2814](https://github.com/Simarglok/arcanum-steamwoven/issues/2814)) | `DAT_ROOT` = the installed game folder; `ARC_SCRIPT_REFUSAL_BASELINE_WRITE` = a file the current rows are written to (to refresh the baseline) | Terrain viewer tests, `FullyQualifiedName~Refused_installed_scripts_per_trigger_kind&TestCategory=RealData` (about 2 min); the installed scripts of every hosted trigger kind (object points the hosts run, tile, sector) read against their host's admission set; the scripts refused are pinned in `tests/Arcanum.TerrainViewer.Tests/ScriptTriggerRefusalBaseline.txt` (kind, script, first refusal; [#2835](https://github.com/Simarglok/arcanum-steamwoven/issues/2835) shrinks it); a script refused at a kind the file does not name, or admitted now while the file names it, fails the test naming the kind and the script: fix the host or add the row with its gap issue, remove rows a fix frees. Its smoke half is the stage `script-triggers` (`./arcanum smoke pr --stages script-triggers`, `--hero` by default, the Crash Site, its own process, about 20 s): Virgil's opening heartbeat and talk, the window's tile scripts the placed set admits (up to twelve scripts for the hero, four of them for one NPC) and the written items' use scripts must run, none Refused, EffectsRejected or ContentUnavailable; [conditions TQ1–TQ28](../research/script-trigger-conditions.md) |
| Call script at census (ARC-SCRIPT-2824) | `DAT_ROOT` = the installed game folder; `ARC_CALL_AT_REPORT` = a file the rows are written to | Terrain viewer tests, `FullyQualifiedName~CallScriptAtCensusRealDataTests&TestCategory=RealData` (about 20 s); the installed lines of call script at (55; one, script 1405 line 4) and call script in (54; 304); a failure means the counts changed: update [ledger TQ17](../research/ledger/scripts.md) |
| Transform then dialog census (ARC-SCRIPT-2507) | `DAT_ROOT` = the installed game folder; `ARC_TRANSFORM_DIALOG_REPORT` = a text file the rows are written to (optional) | Terrain viewer tests, `FullyQualifiedName~TransformThenDialogCensusRealData&TestCategory=RealData` (about 10 s); the control flow of every installed script: the 21 scripts with a transform (40), and whether any reaches a dialog action (4) after it in its own lines, through a call script (7) or in a caller after a callee's transform (none); a failure means the counts changed: a script now does it, model the conversation with the made critter (issue #2507) and update [ledger TI5e](../research/ledger/technology.md) |
| Toggle state census (ARC-SCRIPT-2324) | `DAT_ROOT` = the installed game folder; `ARC_TOGGLE_STATE_REPORT` = a text file the rows are written to (optional) | Terrain viewer tests, `FullyQualifiedName~ToggleStateCensusRealData&TestCategory=RealData` (about 4 min); every installed script that runs toggle state by the focus kind of its object operand, the placed attachments of the 79 maps' windows that run one, and the static rows the map data stores off; [ledger OT8–OT9](../research/ledger/scripts.md#toggle-state-in-the-use-set-arc-script-2248-the-ground-rune-tile-script-2221) |
| Toggled scenery lights survey (ARC-SCRIPT-2585) | `DAT_ROOT` = the installed game folder; `ARC_SCENERY_LIGHT_REPORT` = a text file the rows are written to (optional) | Terrain viewer tests, `FullyQualifiedName~SceneryToggleLightCensusRealData&TestCategory=RealData` (about 2 min); the sceneries of the 79 maps that carry a light, and the ones the scripts 1179 and 2407 toggle (named 1609 and 1668); [ledger OT10](../research/ledger/scripts.md#toggle-state-in-the-use-set-arc-script-2248-the-ground-rune-tile-script-2221) |
| Armour critical points census (ARC-SCRIPT-2828) | `DAT_ROOT` = the installed game folder | Terrain viewer tests, `FullyQualifiedName~The_installed_critical_points_are_counted&TestCategory=RealData` (about 2 min); the installed critical-hit (34) and critical-miss (35) attachments by object type over the placed rows of the 81 maps and over every item prototype (2 weapon and 2 armour rows at 34, 2 weapon rows at 35, no prototype), and the armour scripts read against the combat-point set; script numbers only; `ARC_ARMOUR_CRITICAL_CENSUS` = a text file the report is written to (optional); a failure means the counts changed: update [trigger conditions TQ23](../research/script-trigger-conditions.md) |
| Self-hit scripts census (ARC-SCRIPT-2841) | `DAT_ROOT` = the installed game folder; `ARC_SELF_HIT_CENSUS` = a text file the rows are written to (optional) | Terrain viewer tests, `FullyQualifiedName~The_installed_weapons_with_hit_and_critical_hit_scripts&TestCategory=RealData` (about 4 min); the installed hit (7), critical-hit (34) and critter-hits (26) attachments by object type over the placed rows of the 81 maps and every item prototype (26 placed weapon rows at 7, 2 at 34, 4 weapon prototypes at 7, 18 placed critter rows and 9 critter prototypes at 26); [ledger TQ31](../research/ledger/scripts.md) |
| Generator held items census (ARC-WORLD-1707) | `DAT_ROOT` = the installed game folder | World project, `FullyQualifiedName~GeneratorItemCensusRealDataTests&TestCategory=RealData` (seconds); the 83 items of the 27 installed monster generators against their prototypes (instance overrides, the item and object flags the session reads); counts only; a failure means the counts changed: update [ledger GS5](../research/ledger/npc-ai.md) |
| Art number census (ARC-SCRIPT-1780) | `DAT_ROOT` = the installed game folder | World project, `FullyQualifiedName~ObjectArtCensusRealData&TestCategory=RealData` (about 1 min); the art classes of the NPC prototypes and placed rows (787 placed unique NPCs) and the object each use of change art number names (none an NPC); [ledger AN16–AN17](../research/ledger/scripts.md#change-art-number-arc-script-1735-e5-a-tail) |
| Hero sight and dialog scripts (ARC-SCRIPT-1863) | `DAT_ROOT` = the installed game folder | Scripting project, `FullyQualifiedName~ScrHeroSightCensusRealData&TestCategory=RealData` (the installed clauses of conditions 26, 28, 29); terrain viewer tests, `FullyQualifiedName~AreaHeroSightScriptRealData&TestCategory=RealData` (scripts 2602 and 1130 run under the use host; under a second each); [ledger CS17](../research/ledger/scripts.md) |
| Bed census (ARC-WORLD-1162) | `DAT_ROOT` = the installed game folder | World project, `FullyQualifiedName~BedCensusRealData&TestCategory=RealData` (about 1 min); the placed beds of the 80 whole maps with their use scripts, pinned (191, 19 scripted), and the main map's, read sector by sector (ARC-WORLD-1262: 275 beds, 32 scripted, 1556 NPC rows with a route, 150 on a bed); [sleep window](../world/sleep-window-runtime.md#the-bed-arc-world-1162-ledger-swb1-swb3-swb5-swb14swb16) |
| DAT R1 | `DAT_ROOT`, eight documented candidates | Data project, `FullyQualifiedName~DatArchiveTests&TestCategory=RealData`; [DAT API/evidence](../project/components/Arcanum.Data.md) |
| Resource precedence | `DAT_ROOT` = the installed game folder (with `modules/` and `data/`) | Data project, `FullyQualifiedName~ContentStackRealData&TestCategory=RealData`; probe `tools/research/Arcanum.ContentProbe` (`--self-test`, `--game-root`); [research/API](../research/content-precedence.md) |
| GOG installer unpack (ARC-DEV-074) | `ARC_GOG_INSTALLER` = the GOG offline installer; `innoextract` installed (`PATH` or a Homebrew folder); unpacks into a temporary folder, about 1.2 GB | Session project, `FullyQualifiedName~GogInstallerUnpackerTests&TestCategory=RealData`; [research](packaging.md) |
| Map aggregate builder (ARC-DEV-082) | `ARCANUM_EXE` and `ARCANUM_GAME_ROOT` (an installation with the module and the prototype files); the comparison also `ARCANUM_ORIGINAL_MAPS` (the aggregates the original wrote, such as `modules/Arcanum/maps`); `ARCANUM_AGGREGATE_OUT` writes the built files for a look | Executable project, `FullyQualifiedName~MapAggregateBuilderRealData`, `TestCategory=OriginalExe`, `-c Release` (about 10 s); [contract](../project/components/Arcanum.Executable.md#the-map-aggregate-builder) |
| Prototype provision (ARC-DEV-069) | `ARCANUM_GAME_ROOT` = an installation the original ran (its `Arcanum.exe` is read; a copy made of links without `data/proto` and `modules/Arcanum/maps` is built from, in a temporary settings folder (prototypes and the map aggregates); `ARCANUM_ORIGINAL_PROTO` is not needed: the installation's own `data/proto` is compared by name and length) | Session project, `FullyQualifiedName~A_fresh_copy&TestCategory=OriginalExe`, `-c Release` (about 30 s); [research](../research/prototype-extraction.md#e4-results-arc-dev-069) |
| Technology census (ARC-DEV-363) | `DAT_ROOT` = the installed game folder | Session project, `FullyQualifiedName~TechCensusRealDataTests&TestCategory=RealData` (a few seconds); one state per technology record 140..222 (RUNS, HERO, PART, INERT) and the not-ready components on the hero and an NPC; a failure prints the changed rows to paste into the test; [technology audit](../research/technology-audit-res-104.md#7-smoke-and-test-coverage) |
| Spell census (ARC-DEV-356) | `DAT_ROOT` = the installed game folder | Session project, `FullyQualifiedName~SpellCensusRealDataTests&TestCategory=RealData` (a few seconds); a failure prints the changed rows to paste into the test; [census](../research/magic-audit-res-103.md#spell-census-ma-0) |
| `wa` census (ARC-SCRIPT-1136) | `DAT_ROOT` | Scripting project, `FullyQualifiedName~DialogWaCensusRealDataTests&TestCategory=RealData` (a few seconds); the 47 installed `wa` replies, the 20 a static walk reaches and the 5 a free NPC can be offered from its script's talk; [ledger CF10](../research/ledger/scripts.md#dialog-companion-state-and-actions-arc-dev-195) |
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
