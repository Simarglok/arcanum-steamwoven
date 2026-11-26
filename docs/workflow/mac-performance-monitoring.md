# Watching the packaged game's performance on a Mac (ARC-DEV-3378)

How to read fps, frame time, GPU time and memory of the packaged game with tools outside it. Nothing is built into the
game. Part of the performance epic [#3377](https://github.com/Simarglok/arcanum-steamwoven/issues/3377).

**What was checked.** 2026-11-26, M4 Pro 24 GB, macOS 27.2.1, Xcode 27.2, display 2560x1440 at 180 Hz, the
`./arcanum package mac` build of main `27d9c280c`, Developer mode off, no `sudo`. The runs stayed in the launcher and
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
runs, the [launch queue](toolchain.md#launch-queue-arc-dev-033) holds the agents' window runs.

## Which tool for what

| Question | Tool |
|---|---|
| Live fps, GPU time, frame graph on screen | Metal Performance HUD |
| Numbers for a session that already happened, nothing set up before | `metalperftrace collect` |
| Per-frame times for a p95 | HUD log |
| CPU time in macOS libraries (the game's own code is unnamed) | Instruments (`xctrace`) |
| Memory of the process | `footprint`, `vmmap` |
| .NET garbage collections | the `Resources` line of the Ctrl+B report, `ARC_PERF_PROBE=1` ([timing probe](toolchain.md#timing-probe-and-the-perf-stages-arc-dev-367)) |

## Metal Performance HUD (works)

Start from Terminal:

    MTL_HUD_ENABLED=1 "$EXE"

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

The Godot editor's remote Monitors and Visual Profiler need a debug build; the package is a release export
([#3379](https://github.com/Simarglok/arcanum-steamwoven/issues/3379); *not run*).

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

## Memory (works)

    footprint "$(pgrep -nx "$GAME")"
    vmmap -summary "$(pgrep -nx "$GAME")"

**You see** `footprint`: the process footprint and its categories (IOSurface, graphics, malloc). `vmmap -summary`:
`Physical footprint` with its peak and the per-region table. The HUD's app memory and `footprint` differed in one run
(720 MB on the overlay, 470 MB from `footprint` seconds later), so compare like with like.

## .NET counters (does not work)

`dotnet-counters` 9.0.661903 listed the packaged game (`ps`) and opened a session on it, but received **no values**:
`monitor` stayed on `Waiting for initial payload...` (8 s) and `collect` wrote only the CSV header (20 s). The same tool
read 135 values in 5 s from a plain `dotnet fsi` process. The cause was not examined. For GC rates use the `Resources`
line of the Ctrl+B report and `ARC_PERF_PROBE=1`. To repeat (the tool needs `DOTNET_ROOT`, or it says
`You must install .NET`):

    export DOTNET_ROOT="$HOME/.local/share/dotnet"
    "$DOTNET_ROOT/dotnet" tool install dotnet-counters --version 9.0.661903 --tool-path "$HOME/.arcanum-tools"
    "$HOME/.arcanum-tools/dotnet-counters" monitor -p "$(pgrep -nx "$GAME")" --counters System.Runtime

## powermetrics (*not run*: needs your password)

`powermetrics -h` on macOS 27.2.1 lists the samplers `cpu_power`, `gpu_power` and `thermal`.

    sudo powermetrics --samplers cpu_power,gpu_power,thermal -i 1000

Expected, not seen: CPU and GPU power, GPU frequency and residency, thermal pressure each second. Ctrl+C stops it.

## Activity Monitor (*partly checked*)

It opens, and its resources contain a **GPU History** window; look for it and CPU History in its Window menu. The menu
and its shortcuts were not checked (no accessibility access).
