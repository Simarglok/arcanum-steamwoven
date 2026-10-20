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

A run is evidence only when it ran what was intended. After starting any Godot or launcher run (viewer mode, native smoke, self-test, lab, export, `./arcanum ...`), check within the first minute that it started the intended mode, and stop it if it did not:

- **Started:** the log shows the mode's own startup and stage lines (a viewer prints `ARC-RENDER-004 load stage=begin` and a native smoke prints its stages as it goes; an export or a headless self-test prints its own progress lines), and a windowed run shows the viewer or lab window with its HUD. When the log is written only at exit (output redirected to a file can be buffered), check the process instead: its command line (`ps -o pid,args -p PID`) and its window.
- **Not started:** the Godot project manager or an empty editor window (Godot did not get `--path` or its arguments); an argument failure on the viewer's HUD (`failure stage=Arguments code=Usage`, drawn on the window and not printed to the log, so the stage lines above never appear); "unrecognized arguments" from the launcher; a log without the expected mode's lines and no viewer window. An interactive viewer that has finished loading may idle at low CPU; that is not a failed start.
- **Then:** stop only the process you started (check its PID and working directory first), fix the command and run it again. Never leave a stuck or wrong window on the owner's screen, and never report a result, PASS or failure, from a run that did not start the intended mode.

Known pitfalls:

- zsh, the default shell on the owner's Mac, does not split an unquoted `$ARGS` string into words: pass arguments as separate words or an array (or run the line under `bash`), and check the started command line.
- The exterior AREA mode rejects `--game-root` (a map-mode argument) with a Usage failure on its HUD and leaves its window open.
- A windowed native smoke needs its window visible: a hidden or covered window is one cause of a stop at the first pixel stage (`AREA_CLASS_PIXEL_NOT_OBSERVED`). Make the window visible and rerun; a stop there with the window visible and unobstructed is a result. Before ARC-RENDER-029 the exterior smoke also stopped there whenever its window held the focus (the camera follow moved the sampled view back to the actor); its samplers now hold the camera and refuse a moved view with a `..._CAMERA_MOVED` code ([map mode](../rendering/area-map-mode.md#camera-held-during-smoke-samples-arc-render-023)).
- A command given to the owner is one you ran yourself, or one checked with the launcher's `--dry-run` (on `area`, `world` and `map`) or `--help` and [the launcher docs](local-development.md#project-launcher). A dry run checks the launcher's command and build, not the viewer's own arguments: a viewer argument is checked by a run.

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
