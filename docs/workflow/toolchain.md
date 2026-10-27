# Toolchain and verification entry points

Configuration is authoritative: [global.json](../../global.json) pins .NET SDK `8.0.425` with roll-forward disabled; [Directory.Build.props](../../Directory.Build.props) targets `net8.0`; [Directory.Packages.props](../../Directory.Packages.props) centralizes package versions. Godot adapters/labs use `Godot.NET.Sdk/4.7.2`, Godot **4.7.2 .NET** and matching .NET export templates. Development currently targets macOS. Version changes require a separate scoped decision; do not replace retained shared installations implicitly.

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

Known pitfalls:

- zsh, the default shell on the owner's Mac, does not split an unquoted `$ARGS` string into words: pass arguments as separate words or an array (or run the line under `bash`), and check the started command line.
- The exterior AREA mode rejects `--game-root` (a map-mode argument) with a Usage failure on its HUD and leaves its window open.
- A windowed native smoke needs its window visible: a hidden or covered window is one cause of a stop at the first pixel stage (`AREA_CLASS_PIXEL_NOT_OBSERVED`). Make the window visible and rerun; a stop there with the window visible and unobstructed is a result. Before ARC-RENDER-029 the exterior smoke also stopped there with its window focus simulated on (the camera follow moved the sampled view back to the actor); its samplers now hold the camera and refuse a moved view with a `..._CAMERA_MOVED` code ([map mode](../rendering/area-map-mode.md#camera-held-during-smoke-samples-arc-render-023)). A run with real window focus was not observed; if the stop persists with a visible window, compare with `main` before blaming the change.
- A command given to the owner is one you ran yourself, or one checked with the launcher's `--dry-run` (on `area`, `world` and `map`) or `--help` and [the launcher docs](local-development.md#project-launcher). A dry run checks the launcher's command and build, not the viewer's own arguments: a viewer argument is checked by a run.

## Launch queue (ARC-DEV-033)

Several threads work on the owner's Mac at once. Every Godot run, every export and every `dotnet` build or test goes
through one first-in, first-out queue, so Godot windows never open over each other, timing-sensitive runs are not
starved, and an export gets a quiet Mac in its turn instead of waiting for one. It replaces the earlier
`lockf /tmp/arcanum-godot-window.lock` rule, which was not first-in, first-out and covered windows only.

    ./arcanum run --kind window|headless|export|dotnet [--timeout MIN] [--label TEXT] [--verbose] -- COMMAND [ARGS...]
    ./arcanum queue                 # the runs holding a place, then the waiters in order
    ./arcanum queue --history N     # the last N finished runs: waited, ran, exit

| Kind | Use it for | Rule | Default limit |
|---|---|---|---|
| `window` | any Godot run with a window: native smokes, the viewer self-test, labs | the single Godot slot, in arrival order | 30 min |
| `headless` | `Godot --headless`, a project import | the same Godot slot | 30 min |
| `export` | an export endpoint (`tools/arcanum-local ... export`, the validation commands that export) | the Godot slot; when it is next, new `dotnet` runs wait and running ones finish, then it runs alone | 180 min |
| `dotnet` | `dotnet restore/build/test`; give the root checks as one command (`sh -c '... && ... && ...'`) | runs alongside other `dotnet` and Godot runs; waits only for an export that is running or next | 45 min |

- **Order and gaps.** Places are numbered as runs arrive; a waiter checks the queue every 0.25 s, so the next run
  starts within that of the previous one ending. An export has no priority: it waits for its turn like any run, and
  its turn always comes. Other commands (git, Python self-tests, reading logs) are not queued.
- **Start check.** The wrapper prints `ARCANUM_QUEUE queued ticket=N kind=K ahead=A waiting-for=#M ...` while it waits
  (again when its position changes), `ARCANUM_QUEUE start ticket=N kind=K waited=Ss` when the command starts, and
  `ARCANUM_QUEUE end ticket=N exit=E ran=Ss` at the end, on stderr; a [quiet](#quiet-output-arc-dev-037) run also
  prints `ARCANUM_QUEUE log ticket=N PATH` before it waits. Within the first minute after starting, check that
  one of the `queued` or `start` lines is there; after `start`, check the command itself as [above](#checking-that-a-run-started-arc-dev-029),
  in its log file for a quiet run.
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
  parent's place when that place covers it: an export covers every kind, a `window` or `headless` run covers Godot
  and `dotnet` runs, a `dotnet` run covers `dotnet` runs. Any other nesting (an export inside a Godot run, a Godot
  run inside a `dotnet` run) would wait for its own parent and is refused: queue it on its own.
- **The owner's viewer.** `./arcanum area`, `world` and `map` take the Godot slot first in line (after the run holding
  it), with no time limit, and keep it until the viewer window closes; the command stays in the terminal until then.
  Ctrl-C there, or closing the terminal, gives the place up while the viewer stays open. `--no-queue` starts at
  once, and `--dry-run` never queues.
- **Lanes still on lockf.** While some threads still use the lockf rule, a run holding the Godot slot also takes
  `/tmp/arcanum-godot-window.lock`, so the two never overlap (a queued run then prints that it waits for a lockf user).

Tests: `python3 -B -m unittest tools.local_dev.test_run_queue` (order, gaps, export quiet, crashed and killed runs,
limits, lockf, nesting, locales); they use their own `ARCANUM_LOCAL_HOME` and lock file.

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
- **`--verbose`** streams the command's output to the terminal as before, with no log file and no summary. Use it only
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

The root solution includes Data, Presentation, Scripting, [headless Simulation movement](../world/grid-movement-runtime.md), [Session state](../world/game-session-runtime.md), the ART viewer and terrain-inspect. It does **not** include the standalone terrain viewer/tests, research C# probes, or display/gamepad labs. Use their specific commands: [ART viewer](../Arcanum.Godot.md), [terrain viewer](../rendering/terrain-viewer.md), [display lab](../../experiments/display-ui-lab/README.md), [gamepad lab](../../experiments/gamepad-lab/README.md). Launch Godot projects with Godot; `dotnet run` applies to executable CLIs/harnesses, not the Godot adapter projects.

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

## Function oracle

ARC-DEV-007, [`tools/original-oracle`](../../tools/original-oracle/README.md), method in the
[original executable check](../research/original-executable-check.md#function-emulation-oracle).

| Tool | Version | Use |
| --- | --- | --- |
| [Unicorn](https://pypi.org/project/unicorn/) | 2.1.4, pinned with wheel hashes in `tools/original-oracle/requirements.lock` | Emulates single x86-32 functions; installed by `oracle.py` into `local/original-oracle/venv` (or `ARCANUM_ORACLE_VENV`) on first use. Bindings BSD, engine GPLv2; never vendored or referenced by a C# assembly. |
| [Ghidra](https://github.com/NationalSecurityAgency/ghidra/releases) | 12.1.4 (Apache-2.0), with JDK 21 | Optional headless reading and function discovery; its project stays in `research-private/`. Not needed by the oracle. |

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
| DAT R1 | `DAT_ROOT`, eight documented candidates | Data project, `FullyQualifiedName~DatArchiveTests&TestCategory=RealData`; [DAT API/evidence](../Arcanum.Data.md) |
| Resource precedence | `DAT_ROOT` = the installed game folder (with `modules/` and `data/`) | Data project, `FullyQualifiedName~ContentStackRealData&TestCategory=RealData`; probe `tools/research/Arcanum.ContentProbe` (`--self-test`, `--game-root`); [research/API](../research/content-precedence.md) |
| DLG reader | `DAT_ROOT` | Data project, `TestCategory=DlgRealData`; [DLG reader](../formats/dlg-reader.md) |
| Dialog transition | `DAT_ROOT` | Scripting project, `TestCategory=DlgRuntimeRealData`; [runtime](../scripting/dialog-transition-runtime.md) |
| Terrain | `ARCANUM_TERRAIN_MODULE`, `ARCANUM_TERRAIN_RESOURCE` | Data project, `FullyQualifiedName~TerrainLoaderTests&TestCategory=RealData`; [loader](../world/terrain-loader.md) |
| Static objects | `ARCANUM_STATIC_MODULE`, `ARCANUM_STATIC_RESOURCE`, `ARCANUM_STATIC_PROTOTYPES` | Data project, `FullyQualifiedName~StaticObjectLoaderTests&TestCategory=RealData`; [loader](../world/static-object-loader.md) |
| Complete exterior area | `ARC_AREA_MODULE`, `ARC_AREA_RESOURCE1`, `ARC_AREA_RESOURCE2`, `ARC_AREA_MOBILE`, `ARC_AREA_PROTOTYPES`, `ARC_AREA_ORACLE` | Data project, `FullyQualifiedName~AreaOriginalTests&TestCategory=RealData`; [profile, independent oracle and measurements](../world/crash-site-area-loader.md) |
| Crash Site cave | `ARC_AREA_MODULE`, `ARC_AREA_RESOURCE1`, `ARC_AREA_RESOURCE2`, `ARC_AREA_PROTOTYPES`, `ARC_CAVE_MOBILE` (the cave's game-generated object file next to `areaMobile`) | Data project, `FullyQualifiedName~AreaCaveOriginalTests&TestCategory=RealData`; [cave profile](../world/crash-site-area-loader.md#crash-site-cave-profile) |
| Whole-core step assessment | The Area variables except the oracle, plus `ARC_AREA_EFFECTS`, `ARC_AREA_PATCH` | Data project, `FullyQualifiedName~AreaStepOriginalDataTests&TestCategory=RealData`; [API and observations](../world/area-step-assessment.md) |
| Function oracle | `ARCANUM_EXE` (the installed `Arcanum.exe`) and the oracle's files from `./arcanum oracle` (`ARCANUM_ORACLE_DIR`, default `research-private/oracle/`) | Simulation project, `TestCategory=OriginalExe`; [function oracle](#function-oracle) |
| Click route on the whole core | `ARC_AREA_ORIGINAL=1` and the area coordinator roles (module, resource1/2, mobile, prototypes, effects, patch) | Terrain Viewer tests, `FullyQualifiedName~AreaRouteOriginalTests&TestCategory=RealData`; [route core](../world/click-route-runtime.md) |

Example, with `DAT_ROOT` already set locally:

```sh
"$DOTNET" test tests/Arcanum.Data.Tests/Arcanum.Data.Tests.csproj --no-build \
  --filter "FullyQualifiedName~DatArchiveTests&TestCategory=RealData"
```

A solution-wide `TestCategory=RealData` run selects all these input families; `DAT_ROOT` alone is insufficient. Viewers also require their documented explicit arguments and original-data fingerprint gates. Synthetic tests, original-data structural checks, graphical readback, human visual observations and physical device evidence are separate results. Headless dummy rendering cannot establish graphical pixel readback. Documentation refreshes validate links/configuration, not runtime behavior.
