# Watching the packaged game's performance on a Mac (ARC-DEV-3378)

How to read fps, frame time, GPU time and memory of the packaged game with tools outside it (the game's own panel is the
[performance overlay](../rendering/terrain-viewer.md#performance-overlay-arc-dev-3389), Ctrl+F). Part of the performance epic [#3377](https://github.com/Simarglok/arcanum-steamwoven/issues/3377).

**What was checked.** 2026-11-26, M4 Pro 24 GB, macOS 27.2.1, Xcode 27.2, display 2560x1440 at 180 Hz, the
`./arcanum package mac` build of main `27d9c280c`, Developer mode off; only `powermetrics` used `sudo`. The runs stayed in the launcher and
main menu (no map was loaded) on a copy of the app, and the Mac was busy with other work, so this page says what each
tool **does**; its numbers are not a baseline. Tools marked *not run* were not proven.

## Before reading any fps

The viewer caps its own frame rate: 60 fps when idle, the display's refresh rate while input is held or recent, the
camera moves or the hero walks (`FramePacingPolicy`). A steady 60 while standing is that cap, not the limit of the
machine. Read **frame time p95** and **GPU time**, and compare runs doing the same actions.

## Setup

    APP="$HOME/Git/arcanum-steamwoven/artifacts/packages/macos/Arcanum Steamwoven.app"
    EXE="$APP/Contents/MacOS/Arcanum Steamwoven"
    GAME='Arcanum Steamwoven'

The tools below find the running game with `pgrep -nx "$GAME"` (the newest process of that name). A game started from
Terminal uses the same user folder (saves, settings, logs): its start line names it. While a game from `artifacts/packages/macos`
runs, the [launch queue](toolchain.md#launch-queue-arc-dev-033) holds the agents' window and solo runs. An agent's
measurement is queued with `./arcanum run --kind solo`, so nothing else runs beside it ([solo runs](toolchain.md#solo-runs-arc-dev-3443)).

## Which tool for what

| Question | Tool |
|---|---|
| Live fps, GPU time, frame graph on screen | Metal Performance HUD |
| Numbers for a session that already happened, nothing set up before | `metalperftrace collect` |
| Per-frame times for a p95 | HUD log |
| CPU time in macOS libraries (the game's own code is unnamed) | Instruments (`xctrace`) |
| Allocations in Instruments, the Godot editor's Monitors and Visual Profiler | the [profiling package](#profiling-package-arc-dev-3379) |
| Memory of the process | `footprint`, `vmmap` |
| .NET garbage collections | the `Resources` line of the Ctrl+B report, `ARC_PERF_PROBE=1` ([timing probe](toolchain.md#timing-probe-and-the-perf-stages-arc-dev-367)) |

## Metal Performance HUD (works)

In the game itself **Ctrl+F** switches it on and off: it is the third step of the
[performance overlay](../rendering/terrain-viewer.md#performance-overlay-arc-dev-3389), after the game's own panel. To have
it from the first frame, start from Terminal:

    MTL_HUD_ENABLED=1 "$EXE"

Where `metalperftrace` exists (macOS 27) the game then puts the HUD where its own panel stands (the right edge, below the top
bar), whichever way it was started.

**You see** an overlay at the top right: chip, resolution, Metal and app memory, FPS, GPU ms, frame interval and its
graph. More lines (thermal state, present delay, encoder CPU time):

    MTL_HUD_ENABLED=1 MTL_HUD_ELEMENTS=device,layersize,fps,frameinterval,frameintervalgraph,gputime,memory,thermal,presentdelay,metalcpu "$EXE"

`open` passes the variable too (`open --env` exists on macOS 27):

    open -n --env MTL_HUD_ENABLED=1 "$APP"

For a game that is already running (tried on one started without the variable):

    metalperftrace setup --enable hud --pid "$(pgrep -nx "$GAME")"
    metalperftrace setup --disable hud --pid "$(pgrep -nx "$GAME")"

### HUD log: per-frame times

    MTL_HUD_ENABLED=1 MTL_HUD_LOG_ENABLED=1 "$EXE" 2> hud.log

About once a second the game writes a `metal-HUD:` line to stderr: a frame number, two memory figures in MB, then one
pair per frame (interval ms, GPU ms). The same lines in the system log (`log show`) are cut to `<private>`, so keep
stderr. Summary of a whole run, loading frames included:

    grep -a 'metal-HUD: ' hud.log | sed 's/.*metal-HUD: //' | awk -F, '{for (i = 4; i < NF; i += 2) print $i, $(i+1)}' | sort -n | awk '{f[NR]=$1; s+=$1; g+=$2} END {printf "frames %d, interval avg %.2f p95 %.2f max %.2f ms, GPU avg %.2f ms\n", NR, s/NR, f[int(NR*0.95)], f[NR], g/NR}'

**You see** one line such as `frames 777, interval avg 16.83 p95 22.24 max 83.39 ms, GPU avg 1.87 ms`.

## metalperftrace (works, no setup in the game)

Live, in Terminal:

    metalperftrace listen --pid "$(pgrep -nx "$GAME")" -i 1

**You see** every second: FPS, frame time avg/min/max, CPU begin-to-present, GPU time and memory. Redirected to a
file, nothing arrived until the tool ended; `--output FILE` keeps a copy (its help: rewritten as a JSON array on exit).

Afterwards (macOS records these figures all the time, also with the HUD off):

    metalperftrace collect --last 5m ./perftraces
    metalperftrace overview --predicate "processName == 'Arcanum Terrain Viewer'" "$(ls -t ./perftraces/*.atrc | head -1)"

**You see** for each game process of that window: FPS, frame count, skipped frames, frame time and GPU time avg/min/max,
memory with peak. The trace names the process **Arcanum Terrain Viewer** (`listen` and `pgrep` say Arcanum Steamwoven).
Without `--predicate` it also lists every other Metal process of the Mac. `--json` exists (*not run* on `overview`).

## Godot flags

The game log is `~/Library/Application Support/Arcanum Steamwoven Game/logs/godot.log`. `--log-file PATH` writes a
test run's log elsewhere, so it does not rotate the game's own logs (works).

| Flag | Result |
|---|---|
| `--print-fps` | **Works**, with `NSUnbufferedIO=YES` (command below). Piped without it, the lines came late in blocks (none in the first 18-25 s of two runs) and buffered ones were lost when the process was killed. |
| `--disable-vsync` | **Not determined**: about 60 fps standing, with and without. The viewer's own cap targets 60 there, so the run cannot show whether the flag lifts Metal's vsync. |
| `--max-fps N` | **Does not work**: `--max-fps 30` still gave about 60, the viewer sets the cap itself every frame. |
| `--gpu-profile` | **Does not work** on Metal: only empty `GPU PROFILE (total 0.0ms):` blocks. |

    NSUnbufferedIO=YES "$EXE" --print-fps 2>&1 | tee fps.txt

**You see** `Project FPS: 59 (16.94 mspf)` each second. `MTL_HUD_ENABLED=1` can be added in front.

The Godot editor's remote Monitors and Visual Profiler need a debug build: the player package is a release export and,
given `--remote-debug`, **stops at once** (`ERROR: `--remote-debug` was specified on the command line, but this Godot
binary was compiled without debug. Aborting.`). Use the [profiling package](#profiling-package-arc-dev-3379).

## Instruments (`xctrace`)

Attaching to the ad-hoc signed release app worked with Developer mode off and no password prompt:

    xctrace record --template 'Time Profiler' --attach "$(pgrep -nx "$GAME")" --time-limit 10s --output time.trace
    xctrace record --template 'Metal System Trace' --attach "$(pgrep -nx "$GAME")" --time-limit 5s --no-prompt --output metal.trace
    xctrace record --template 'Game Performance' --attach "$(pgrep -nx "$GAME")" --time-limit 5s --no-prompt --output game.trace
    open time.trace

**You see** `Output file saved as: ...`, then the timeline in Instruments. Sizes: Time Profiler 17 MB for 10 s; Metal
System Trace 60 MB and Game Performance 30 MB for 5 s. Limits:

- The game's **own code has no names** in Time Profiler: nearly every frame of the app binary (the Godot engine) was a
  bare address, and so were the frames without a binary (likely the C# just-in-time code); no C# method was named.
  macOS frameworks and the .NET runtime libraries were named. Use `ARC_PERF_PROBE=1` for the C# side.
- **Game Memory does not work** as a short attach: a 5 s recording wrote 10 GB and was still saving after 90 s, when it
  was stopped (90 s counted from the start of the command). Delete such a `.trace`.
- **Allocations on the player package asks for an administrator password** (ARC-DEV-3379, 2026-11-26, Developer mode off,
  Mac locked): macOS requested the right `com.apple.dt.instruments.process.analysis` (system log, process `authd`), and
  with nobody to answer `xctrace` recorded nothing until it was stopped. The log showed an older password entry as
  expired after 10 hours, so it may pass without a question after a recent one (*not run*). On the
  [profiling package](#profiling-package-arc-dev-3379) it records without a password.

## Profiling package (ARC-DEV-3379)

A second copy of the game for measuring, built on this Mac and **never distributed**:

    ./arcanum package mac --profiling        # fetch origin main, build that commit, smoke it, install it

It lands in `artifacts/packages/macos-profiling/Arcanum Steamwoven Profiling.app` of the main checkout; the player
package is not touched ([packaging](packaging.md#profiling-package-arc-dev-3379)). Same commit and same game code,
compiled as for the player (optimized, ReadyToRun). The differences:

- the Godot **debug** engine template (with the engine's debug checks, and the debug flavour of the C# engine API), so
  it is expected to run slower than the player package: take **fps and frame-time baselines from the player package**,
  and use this one to find where the time and memory go;
- the entitlement `get-task-allow`, so Instruments may inspect the process without a password;
- its own name, bundle id and user folder `~/Library/Application Support/Arcanum Steamwoven Profiling/`, so its saves,
  settings, logs and the HD pack folder inside it (`hd-packs`) are separate: before comparing memory or GPU figures,
  check that both games use the same look and pack. The map cache is the player's (`Arcanum Steamwoven Game/map-cache`,
  a fixed folder for every build).
- its Ctrl+B bug reports go beside it, to `artifacts/packages/macos-profiling/reports/`; `./arcanum qa` reads only the
  player package's folder unless given `--reports-dir`.

To measure with your own saves and settings, quit both games and copy the player's folder over the profiling one (an
APFS clone; it overwrites the profiling build's files of the same name):

    mkdir -p "$HOME/Library/Application Support/Arcanum Steamwoven Profiling"
    cp -cRp "$HOME/Library/Application Support/Arcanum Steamwoven Game/." "$HOME/Library/Application Support/Arcanum Steamwoven Profiling/"

While it runs from `artifacts/packages/macos-profiling`, the [launch queue](toolchain.md#launch-queue-arc-dev-033) of a
checkout that has this change holds its window and solo runs, as for the player package. A checkout from before it (an older
worktree) does not see this folder, and a menu-bar app started before it must be restarted: for a measurement run
`./arcanum queue --playing` first (and `--not-playing` after), which every checkout honours (it holds a queued
[solo run](toolchain.md#solo-runs-arc-dev-3443) too, so this is for a measurement started by hand).

### Instruments

The commands above, with `GAME='Arcanum Steamwoven Profiling'`. Checked on the package of this change (2026-11-26,
Developer mode off, Mac locked, launcher screen, 5 s each): Time Profiler, **Allocations** (statistics for 348
categories, no password asked) and Game Performance attached and saved their traces. Time Profiler still leaves most of
the engine's and the game's own frames unnamed ([#3391](https://github.com/Simarglok/arcanum-steamwoven/issues/3391)).

    xctrace record --template 'Allocations' --attach "$(pgrep -nx 'Arcanum Steamwoven Profiling')" --time-limit 10s --no-prompt --output alloc.trace
    open alloc.trace

### Godot editor: Monitors and Visual Profiler

The editor listens, the game connects to it. Open the editor on an empty project of its own, **not** on the repository's
project (the editor writes into the project it opens). The project needs a main scene, or `--debug-server` starts no
server (with an empty project and no main scene nothing listened on port 6007). Once:

    D="$HOME/arcanum-profiling-debugger"; mkdir -p "$D"
    printf 'config_version=5\n\n[application]\n\nconfig/name="Arcanum profiling debugger"\nrun/main_scene="res://empty.tscn"\n' > "$D/project.godot"
    printf '[gd_scene format=3]\n\n[node name="Empty" type="Node"]\n' > "$D/empty.tscn"

Then, in one Terminal, the editor:

    /Applications/Godot_mono.app/Contents/MacOS/Godot --editor --path "$HOME/arcanum-profiling-debugger" --debug-server tcp://127.0.0.1:6007

and, in another, the game:

    "$HOME/Git/arcanum-steamwoven/artifacts/packages/macos-profiling/Arcanum Steamwoven Profiling.app/Contents/MacOS/Arcanum Steamwoven Profiling" --remote-debug tcp://127.0.0.1:6007

In the editor's bottom **Debugger** panel open **Monitors**, and **Visual Profiler** (press **Start**). Not the
**Profiler** tab: it does not support C#.

Checked 2026-11-26: the editor listened on 6007 and the game held a connection to it; a test listener in the editor's
place received the monitor frames (about one a second) and, after asking for the visual profiler, its frames with the
`Render Viewport` and `Render Canvas` areas. Reading the two tabs in the editor window was *not run* (locked Mac).

## Memory (works)

    footprint "$(pgrep -nx "$GAME")"
    vmmap -summary "$(pgrep -nx "$GAME")"

**You see** `footprint`: the process footprint and its categories (IOSurface, graphics, malloc). `vmmap -summary`:
`Physical footprint` with its peak and the per-region table. The HUD's app memory and `footprint` differed in one run
(720 MB on the overlay, 470 MB from `footprint` seconds later), so compare like with like.

## The game's garbage collector (ARC-RENDER-3414)

The exported Mac game runs the .NET **server** collector with 4 heaps (the viewer project; written into the app's
`Arcanum.TerrainViewer.runtimeconfig.json`). A source run through the Godot editor binary keeps the defaults, and so
does the Windows game (not measured there).

Why: .NET 8 on macOS counts only free pages as available memory. On a 24 GB Mac with `sysctl kern.memorystatus_level`
at 63 % free, `GC.GetGCMemoryInfo().MemoryLoadBytes` read 97-99 % of it, above the 90 % high-memory threshold, and the
workstation collector ran blocking full collections of 50-115 ms while a map window loaded. A higher threshold added
nothing to the server collector in the same measurement.

Measured 2026-11-27 (M4 Pro, 2560x1440, HD, release package, 110 s of walking across map windows, medians of two runs,
before to after this change and the held art archives): the frame of a window swap 379 to 93 ms, frames over 50 ms while
a window loads about 41 to 2 per run, collection pauses about 51 to 14.5 ms per second, the window load about 1.4 to
0.6 s; the working set at the end of the walk grew from about 1.5 to 1.7-2.0 GB. To compare another setting on one
package, start it with `DOTNET_gcServer`, `DOTNET_GCHeapCount` or `DOTNET_GCHighMemPercent` (values in hexadecimal).

## .NET counters (does not work)

`dotnet-counters` 9.0.661903 listed the packaged game (`ps`) and opened a session on it, but received **no values**:
`monitor` stayed on `Waiting for initial payload...` (8 s) and `collect` wrote only the CSV header (20 s). The same tool
read 135 values in 5 s from a plain `dotnet fsi` process. The cause was not examined. For GC rates use the `Resources`
line of the Ctrl+B report and `ARC_PERF_PROBE=1`. To repeat (the tool needs `DOTNET_ROOT`, or it says
`You must install .NET`):

    export DOTNET_ROOT="$HOME/.local/share/dotnet"
    "$DOTNET_ROOT/dotnet" tool install dotnet-counters --version 9.0.661903 --tool-path "$HOME/.arcanum-tools"
    "$HOME/.arcanum-tools/dotnet-counters" monitor -p "$(pgrep -nx "$GAME")" --counters System.Runtime

## powermetrics (GPU works, needs your password)

    sudo powermetrics --samplers gpu_power -i 1000 -n 5

**You see** five one-second samples with `GPU HW active frequency`, `GPU HW active residency`, `GPU idle residency` and
`GPU Power` (run by the owner on 2026-11-26: 338 MHz, 2-4 % active, 20-43 mW, a nearly idle GPU). The figures are for the
whole Mac, not for the game. `powermetrics -h` also lists `cpu_power` and `thermal`
(`--samplers cpu_power,gpu_power,thermal`; those two *not run*).

## Activity Monitor (*partly checked*)

It opens, and its resources contain a **GPU History** window; look for it and CPU History in its Window menu. The menu
and its shortcuts were not checked (no accessibility access).
