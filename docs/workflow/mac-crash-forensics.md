# Mac crashes during play: how to read them (ARC-DEV-2274)

The owner's Mac (M4 Pro, 24 GB, macOS 27.2) has three WindowServer watchdog
reports (2026-11-08 about 13:32, 2026-11-12 about 20:21 and 2026-11-15 about
18:00 local time); the last two happened while the packaged game was open and
the 11-12 one ended in a panic. This page keeps the diagnosis so the next incident is read in
minutes. The mitigation work is [#2273](https://github.com/Simarglok/arcanum-steamwoven/issues/2273)
(viewer cap, "owner is playing" mode, panic forensics); the investigation is
[#2274](https://github.com/Simarglok/arcanum-steamwoven/issues/2274).

## Finding

The game is **not the proximate cause**; it is the victim of a hang in the
system. Our process-start rate may be a trigger (see the context below). In all
three reports:

1. `/Library/Logs/DiagnosticReports/.WindowServer-DATE.ips` (a hidden file; the
   JSON is cut off, so append the missing closing braces to parse it) says the watchdog found the
   WindowServer main thread unresponsive for 40 seconds. On 2026-11-12 the Mac
   then panicked (`panic-full-*.panic`: "userspace watchdog timeout: no
   successful checkins from WindowServer"); on the other two WindowServer was
   restarted.
2. In the stackshot, the WindowServer main thread is blocked on a
   `runningboardd` thread (`client-workloop`), which waits on its
   `RBAssertionManagerCallOut` thread, which sits in an **uninterruptible kernel
   wait** (`TH_UNINT`) on the same wait event as a **freshly forked child**
   (`zsh` or `python3`, resident size 49200 bytes or about 48 KB, which suggests
   forked and not yet executed) that had been stuck for 36 to 144 seconds
   (2026-11-08: `zsh`, 11-12 and 11-15: `python3`). powerd, launchservicesd,
   mDNSResponder and others queue behind the same thread.
3. The kernel stack of the stuck child is identical in the 2026-11-12 and
   2026-11-15 reports (not compared for 11-08). This points to a macOS kernel or
   RunningBoard hang around process creation rather than a leak or a GPU fault
   in our code; it is not proven, and no kernel symbols were available.
4. The game then aborts (`abort()` in `CGLFlushDrawable`, probably an
   Objective-C exception when its window server connection goes away). The
   `Arcanum Steamwoven-DATE.ips` in `~/Library/Logs/DiagnosticReports` is the
   consequence.

Context at the time: the game had been running only 7, 16 and 14 minutes
(resident size 1.2 to 1.4 GB); 2026-11-08 had about 150 `dotnet` processes, and
2026-11-15 about 60 `bash`/`sh`/`zsh` starts per minute for half an hour while
other threads ran smoke viewers (several short-lived Godot windows opening per
minute). The Jetsam reports (`JetsamEvent-*.ips`) only show per-process-limit
kills of unrelated daemons.

## Game memory check

A 10-minute idle run of the packaged build at its main menu (launched with
`open -g -n`, sampled every 30 s with `ps` and `footprint`; the planned 20
minutes ended when the game process disappeared without a crash report or a
quit message; see "A game that vanished without a crash report" below) was flat after the
first two minutes: physical footprint 366 MB, IOAccelerator 13 MB, IOSurface
16 MB, threads 33 to 34. A read-only code review of the creation editor and art
path found no unbounded cache and no per-frame texture allocation (the font,
chunk and mapping caches are capped or weak-keyed). Not covered by that run: a soak inside
the character-creation window itself and runs longer than 10 minutes; see "Soak in the
creation window" below ([#2279](https://github.com/Simarglok/arcanum-steamwoven/issues/2279)).

## Soak in the creation window (ARC-DEV-2279)

The viewer has a development soak, switched on by environment variables (no argument, so the launchers and the
player's build do not change):

| Variable | Meaning |
|---|---|
| `ARC_SOAK_LOG` | Turns the soak on: a CSV file (rewritten at the start) that gets one row of counters at the start and every interval. |
| `ARC_SOAK_WINDOW` | `new-character` (menu window 12) or `editor` (window 14, the character editor in creation mode). The viewer takes the player's route over the main menu (Single Player, New Game, New Character, and for the editor its Accept, held until the start map is loaded) and leaves the window idle. Without it the counters run wherever the viewer stands. |
| `ARC_SOAK_INTERVAL` | Seconds between rows, 1 to 3600, default 30. |
| `ARC_SOAK_MINUTES` | Optional: the viewer ends itself after that many minutes (exit 0), after a last `# ARC-DEV-2279 soak summary` row in the log; without it the run goes on until the window is closed. |

Columns: `elapsed_s`, `window` (the menu window at the reading, `none` while no menu stands), `fps`; Godot's
`texture_bytes`, `video_bytes`, `buffer_bytes` (`RenderTextureMemUsed`, `RenderVideoMemUsed`, `RenderBufferMemUsed`),
`objects`, `resources`, `nodes`, `orphan_nodes` (`ObjectCount`, `ObjectResourceCount`, `ObjectNodeCount`,
`ObjectOrphanNodeCount`) and `godot_static_bytes` (`MemoryStatic`); the .NET side's `gc_heap_bytes` (includes garbage not yet
collected, so it saw-tooths), `gc_live_bytes` (what survived the last full collection: the leak signal), `gc0`, `gc1`, `gc2`
(collection counts); the process's `working_set_bytes` and `threads`. A leak is a counter that only climbs after the first
two minutes (the summary leaves those out); `gc_heap_bytes` rising between two `gc2` steps is not one.

The documented command runs the viewer from the checkout's source through the launch queue (one `window` run; the build step comes
first; the game folder is the one `./arcanum setup` recorded, `/Applications/Arcanum` here; the `*_TEST_PATH` variables keep the
run's settings away from the owner's files, as a smoke does). The `./arcanum area` build is not used because it needs a fresh
ARC-DEV-015 export of the same commit.

```sh
./arcanum run --kind dotnet -- sh -c 'dotnet restore tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln --locked-mode -v q && dotnet build tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln --no-restore -v q --nologo'
```

```sh
G=/Applications/Arcanum; D=$(mktemp -d); ./arcanum run --kind window --estimate 2200 --timeout 40 --expect '^ARC-DEV-059 viewer start mode=area' --start-within 120 -- env __GODOT_SHELL_ENV_SET=1 DOTNET_ROOT=$HOME/.local/share/dotnet PATH=$HOME/.local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin ARC_SOAK_LOG=$D/soak.csv ARC_SOAK_WINDOW=editor ARC_SOAK_MINUTES=30 GAME_SETTINGS_TEST_PATH=$D/game_settings.cfg AUDIO_VOLUMES_TEST_PATH=$D/audio.cfg ALWAYS_RUN_TEST_PATH=$D/always.cfg LAUNCHER_SETTINGS_TEST_PATH=$D/launcher.cfg VISUAL_PROFILE_TEST_PATH=$D/visual.cfg /Applications/Godot_mono.app/Contents/MacOS/Godot --path tools/terrain-viewer/Godot -- --mode area --module $G/modules/Arcanum.dat --resource $G/arcanum2.dat --source-id local-original --actor-resource $G/arcanum1.dat --game-root $G --area-map Arcanum1-024-fixed --main-menu true
```

The soak is an automated run: the Master audio bus is muted as in a smoke (ARC-DEV-403; the sounds still play into the muted bus, so the audio memory is still measured), and `ARC_SMOKE_SOUND=1` lets it be heard.

Limits: the end check runs at the interval's ticks, so the viewer ends up to one interval after `ARC_SOAK_MINUTES` (keep the queue
`--timeout` at least ten minutes above it). The counters `godot_static_bytes` and `orphan_nodes` read 0 in a release template
(the command above uses the editor binary). The window opens only on the start map the New Game route accepts (the Crash Site start
map of the command); when the route is refused or the menu is busy the viewer logs `ARC-DEV-2279 soak: ...` or the log's `window`
column stays `Main`, and the run is no soak of the creation window. (The first line of the run log says `ARC-DEV-2279 soak start pid=...`; `footprint <pid>` beside it gives the physical footprint and
the IOAccelerator size the 2274 idle run quoted, which the in-process counters cannot read.) The unit tests of the settings and the
summary are `ViewerSoakTests`; a one-minute trial with `ARC_SOAK_INTERVAL=5 ARC_SOAK_MINUTES=1` shows the window opening within seconds.

### Soak results

**2026-11-15, 15 minutes (stopped by hand to free the queue for another run; the 30-minute run follows).** Viewer from the
checkout's source (Debug, Godot 4.7.2 .NET) on the Crash Site start map, `ARC_SOAK_WINDOW=editor`, 30 s rows, sound on (this run
predates the mute). The editor was open from the first minute to the 15th. After the first two minutes every counter was flat:
render textures 40.0 MiB, video memory 56.3 MiB, objects 3,781, nodes 378, resources 22, orphan nodes 0, Godot static memory 44 MiB,
threads 34 to 36, live .NET heap 283 MiB (the full-collection survivors, 296.7 MB, unchanged to the byte from minute 2 to minute 15), 17 to 19
full collections. The .NET heap including garbage saw-toothed between 298 and 304 MiB with 8 to 9 gen0 collections a minute: an
idle editor allocates short-lived garbage, not retained memory. The working set came down from 848 MiB (minute 2) to 530 MiB
(minute 8) as the OS reclaimed pages, and stayed there. The 1-minute trial earlier showed the same plateau within 12 s of the
window opening (texture 40 MiB, nodes 378). Not a leak in this window for 15 minutes; the 30-minute result is below when it is in.

## A game that vanished without a crash report (ARC-DEV-2280)

In the 2274 idle run the game (pid 8604, opened with `open -g -n` at 18:14:44, outside every queue
process group) disappeared at 18:25:34 on 2026-11-15. The unified log shows it was **not killed**:

- `runningboardd`: `termination reported by launchd (0, 0, 0)` and `Firing exit handlers ... <RBSProcessExitContext | voluntary>`:
  exit status 0 and no signal; a SIGKILL, a Jetsam kill or a crash reads differently (a signal, a Jetsam reason or an `.ips` report).
- `launchservicesd`: `SETFRONT: pid=8604 ... foreground=1` at 18:25:32.93 (the window was activated; the previous front
  process, 758, is `SystemUIServer`, the "nothing in front" process), then `QUITTING: pid=8604` at 18:25:34.60, 1.7 s later,
  and Music became the front app at once. The first line of the run's start has the same shape (activation from 758).
- No `osascript`, `pkill`, `killall`, quit or kill line in the log between 18:25:30 and the exit, and the game's render loop was
  running normally 0.6 s before it.

Reading: a normal exit with status 0 shortly after the window was activated, most likely a quit by hand (Cmd+Q, the close
button, Dock Quit or the main menu Exit). The log cannot tell those apart, and it also fits a quit the code itself makes with
status 0 (a smoke or self-test end, the HD pack problem box "close game"); those are flag-driven and not expected at an idle
main menu, but they are not ruled out by the log. The activation shows that a window was brought to the front, not who did it.

No tool in the repository can have done it: no script under `tools/` or `./arcanum` (and no C# under `tools/` or `src/`)
kills by name or sends an Apple event, and the queue signals only the process group of a command it started with
`start_new_session`; a game opened with `open` belongs to launchd and shares no group. (Ad-hoc commands typed by a Claude
session or the owner are not covered by any scan.)
[`test_foreign_kill_guard.py`](../../tools/local_dev/test_foreign_kill_guard.py) keeps it so, and `_signal_group` now refuses a
pid that does not lead its own group and the queue's own group. The viewer logs `ARC-DEV-2280 window close request at HH:MM:SS`
when the window gets a close request (not yet seen with a real Cmd+Q, close button or Dock Quit in a built game; the first
time one is pressed tells which arrive; since ARC-DEV-2270 an interactive window asks the quit question after it, and the line ends "the quit question follows" with `ARC-DEV-2270 quit yes` or `quit held key` when the game then exits), and the main menu Exit logs `ARC-UI-011 main menu exit`, so the next incident can be
read from the game's own log (`~/Library/Application Support/Godot/app_userdata/Arcanum Terrain Viewer/logs/godot.log`). Copy
that log before the next smoke run: every smoke viewer starts a new log in the same folder and only the last five are kept
([#2285](https://github.com/Simarglok/arcanum-steamwoven/issues/2285) gives the game its own log).

Recipe for any vanished process (replace the pid; `YYYY-MM-DD HH:MM:SS` is a few seconds either side of the exit):

```sh
/usr/bin/log show --start 'YYYY-MM-DD HH:MM:SS' --end 'YYYY-MM-DD HH:MM:SS' --info --debug --predicate 'eventMessage CONTAINS "PID" AND (process == "runningboardd" OR process == "launchservicesd" OR process == "launchd")'
```

## How to read the next incident

System reports (`.WindowServer-*.ips` is hidden, so `-A`; also `panic-full-*`
and `ResetCounter-*`), then the game's own crash report, then the unified log.
Use `/usr/bin/log`, because zsh has a builtin named `log`.

```sh
ls -At /Library/Logs/DiagnosticReports | head
```

```sh
ls -At ~/Library/Logs/DiagnosticReports | head
```

```sh
/usr/bin/log show --start 'YYYY-MM-DD HH:MM:SS' --end 'YYYY-MM-DD HH:MM:SS' --style compact --predicate 'process == "WindowServer"'
```

- In the `.WindowServer` stackshot, `stackshot.processByPid` holds each
  process with `threadById`, `turnstileInfo` (who blocks on whom) and
  `waitEvent`. Follow `blocked on` from the WindowServer main thread until the
  thread has no turnstile, then list every thread with the same `waitEvent`.
- A kernel log line `process_is_plugin_host: running binary "bash"` is written
  for every shell start; counting them per minute gives the start rate.
- Count the Godot and `dotnet` processes alive and the packaged game's age at
  the time.

## What we can do

- Keep the number of simultaneous viewers and the process start rate low while
  the owner plays (#2273).
- The owner can send the three `.WindowServer` stackshots and `sysdiagnose` to Apple through
  Feedback Assistant (macOS 27.2, RunningBoard / process creation hang); this
  is an owner action.
