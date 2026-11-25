# Mac crashes during play: how to read them (ARC-DEV-2274)

The owner's Mac (M4 Pro, 24 GB, macOS 27.2) has four WindowServer watchdog
reports (2026-11-08 about 13:32, 2026-11-12 about 20:21, 2026-11-15 about
18:00 and 2026-11-21 about 14:13 local time); the last three happened while the packaged game was open and
the 11-12 one ended in a panic. This page keeps the diagnosis so the next incident is read in
minutes. The mitigation work is [#2273](https://github.com/Simarglok/arcanum-steamwoven/issues/2273)
(viewer cap, "owner is playing" mode, panic forensics); the investigation is
[#2274](https://github.com/Simarglok/arcanum-steamwoven/issues/2274); the fourth incident is
[#3115](https://github.com/Simarglok/arcanum-steamwoven/issues/3115), and
[`tools/local_dev/windowserver_report.py`](../../tools/local_dev/windowserver_report.py) reads a report's wait chain
(see "How to read the next incident").

## Finding

The game is **not the proximate cause**; it is the victim of a hang in the
system. Our process-start rate may be a trigger (see the context below). In the first
three reports (the fourth, 2026-11-21, has the same signature: see "Fourth incident" below):

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

### Resource line in the log and the bug report (ARC-DEV-1398)

Every viewer run except a self-test prints one line every 30 s (the soak's interval when `ARC_SOAK_INTERVAL` is set) with the counters above in
a form a person reads, `grep "ARC-DEV-1398 resources"` in the log:

```
ARC-DEV-1398 resources t=90s fps=59.9 texture=40.0MiB video=56.3MiB buffer=0.0MiB objects=3781 resources=22 nodes=378 orphans=0 godotStatic=44.0MiB gcLive=283.0MiB gcHeap=300.1MiB gc=8/3/2 workingSet=1200.0MiB threads=34 openFiles=61 frameMs=17.2/21.5/84.0 gcRate=0.40/0.13/0.07/s over=30s
```

`fps` is Godot's count of the last second, so one slow second can read 23 while the typical frame is 47 fps (ARC-DEV-3128). The tail gives the true frame cost over
the last 30 s: `frameMs=avg/p95/max` is the gap between frames in ms (what the player sees; the idle cap of 60 fps counts as a 16.7 ms frame), and
`gcRate=g0/g1/g2/s` is the .NET collections per second of generation 0, 1 and 2 over the same span (`over=`; `frameMs=?` before two frames are held).
The window is a fixed ring filled once per frame without allocation (`ViewerFrameWindow`); the soak CSV keeps its columns.

`openFiles` is the number of file descriptors the process holds (the listing of `/dev/fd`, which counts its own descriptor: a constant one more; `?` on
Windows, where there is no such folder). The bug report (Ctrl+B) carries the log's last 120 s, so four of these lines, and a fresh reading as its
`Resources` fact. The CSV of the soak has the same numbers as the column `open_files` (the last one). A leak that the 2274 idle run could not see
(a window that is opened and closed, a save and load, a map change) is looked for by the `soak` smoke stage
([toolchain](toolchain.md)): it plays those steps in a loop for a chosen number of minutes and fails when a counter grows past its allowance.

### Soak results

**2026-11-15, 15 minutes (stopped by hand to free the queue for another run).** Viewer from the
checkout's source (Debug, Godot 4.7.2 .NET) on the Crash Site start map, `ARC_SOAK_WINDOW=editor`, 30 s rows, sound on (this run
predates the mute). The editor was open from the first minute to the 15th. After the first two minutes every counter was flat:
render textures 40.0 MiB, video memory 56.3 MiB, objects 3,781, nodes 378, resources 22, orphan nodes 0, Godot static memory 44 MiB,
threads 34 to 36, live .NET heap 283 MiB (the full-collection survivors, 296.7 MB, unchanged to the byte from minute 2 to minute 15), 17 to 19
full collections. The .NET heap including garbage saw-toothed between 298 and 304 MiB with 8 to 9 gen0 collections a minute: an
idle editor allocates short-lived garbage, not retained memory. The working set came down from 848 MiB (minute 2) to 530 MiB
(minute 8) as the OS reclaimed pages, and stayed there. The 1-minute trial earlier showed the same plateau within 12 s of the
window opening (texture 40 MiB, nodes 378). Not a leak in this window for 15 minutes.

**2026-11-16, 30 minutes (the documented command, sound muted).** Same setup, 61 rows over 1,827 s, the editor open from the first
minute to the last. Render textures 40.0 MiB, Godot static memory 44.4 MiB, objects 3,800, resources 23, nodes 387 and orphan nodes 0
did not move at all after the first three minutes; threads 35 to 36. Two things moved, both as one step and not as a climb:
video memory rose from 58.3 to 76.6 MiB (+18.3 MiB) between 587 s and 620 s and stayed there to the end, and the live .NET heap
(survivors of the last full collection) rose from 265.5 to 285.5 MiB in the same window (278.4 MB at 188 s, 299.4 MB from 741 s,
unchanged to the megabyte for the last 18 minutes). The working set fell from 495 to 442 MiB over the run (the OS reclaimed pages; it
ranged 247 to 518 MiB). The frame rate was about 30 instead of 60 because other threads' smoke runs shared the machine. Reading:
**no leak in the creation editor over 30 minutes**: a counter that leaks climbs steadily, and here the last 18 minutes were flat
in every counter. The single step at about 10 minutes was not traced to its cause (one late first-use allocation, not a per-frame
one; it did not repeat in 20 minutes). A run longer than 30 minutes, the New Character window alone and the Crash Site in play are
not covered. The viewer's exit prints three Godot `Texture with GL ID ...: leaked` lines at shutdown, as in the 1-minute trial: not a
runtime counter, and the soak does not look into them.

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

## Fourth incident, 2026-11-21 (ARC-DEV-3115)

Black screen, no input, forced power-button reboot (about 14:14; new boot 14:15:01); local time +04:00. Same signature as the
three before; **the game is a victim, not the cause**.

- 13:49:14 the packaged game started (Steamwoven rules, Crash Site area); 9 Ctrl+B reports 13:50-14:09. Its resource counters
  stayed bounded (texture 206-430 MiB, video 261-476 MiB, working set 1.4-1.7 GB, 130 fds, no climb).
- About 14:12:25 a freshly forked `Python` child (resident 49200 bytes, forked and not yet executed) entered an uninterruptible
  kernel wait (`TH_WAIT|TH_UNINT`). Ancestry: `Python <- Python <- bash <- codex <- Codex (Aperitif) <- ChatGPT`, a shell
  command of the Codex app, **not** our launch queue.
- `runningboardd` (assertion call-out thread), `ArcanumQueue-*` (our menu-bar app), `com.docker.backend` and `spindump` sat in
  the same uninterruptible wait on the **same wait event** as that child. The WindowServer main thread (`ws_main_thread`)
  blocked on `runningboardd` (turnstile, 3 hops).
- 14:13:19 watchdogd: "unresponsive work processor(s): WindowServer main thread, 40 seconds since last successful checkin" ->
  `/Library/Logs/DiagnosticReports/.WindowServer-2026-11-21-141319.ips` (bug_type 409, displayState OFF).
- 14:13:20 the game aborted: an Objective-C exception in `-[NSCGLSurface flushRect:]` under `NSOpenGLContext flushBuffer` (its
  window-server connection went away), `Arcanum Steamwoven-2026-11-21-141322.ips`, SIGABRT; the same consequence as 11-12 and 11-15.
- About 14:14:00 WindowServer was relaunched (pid 93747) but the system stayed wedged (airportd: "gRunLoop Health check NOT
  alive"); the unified log stops at 14:14:18; no panic file (the forced reboot came before the userspace-watchdog panic).
- The 14:09 Ctrl+B report ("periodic freeze with a location refresh, music restarts, camera re-centres") is game-side and
  nothing ties it to the hang; it goes to the usual Ctrl+B triage.

### Cross-incident comparison

| Incident | Stuck forked child (rss 49200) and its origin | Fellow waiters | Game | Godot/dotnet | VM |
|---|---|---|---|---|---|
| 11-08 13:32 | zsh <- claude (Claude Code) | runningboardd | **not running** | 154 dotnet | 1.9 GB VM |
| 11-12 20:21 | Python <- zsh <- claude | runningboardd, sysmond, spindump, replayd | yes | 16 dotnet | 1.5 GB VM |
| 11-15 18:00 | python3.14 <- ... <- claude | runningboardd | yes | 0 | none |
| 11-21 14:13 | Python <- bash <- codex (ChatGPT app) | runningboardd, ArcanumQueue, docker backend, spindump | yes | **0 (queue idle)** | 4.8 + 2.5 GB VMs |

The first eight kernel frames of the stuck child are **identical in all four reports** (frame offsets 599200, 593904, 6429656,
6429112, 6428256, 6506048, 746152, 160372), and `runningboardd`'s are identical too (..., 746152, 339328). Process counts
616-874; 10-39 Claude Code CLI processes each time.

### Hypotheses, ranked

1. **macOS 27.2 kernel or RunningBoard deadlock around process creation (most likely).** A fork from an agent shell (Claude Code
   or Codex) gets stuck before exec; the `runningboardd` assertion call-out waits on the same kernel event; WindowServer waits
   on `runningboardd`; the watchdog kills WindowServer and the relaunched one wedges too. Evidence: 4 of 4 identical kernel
   stacks, different spawning programs, one incident with no game at all, no GPU fault. The trigger probably scales with the
   process-spawn rate (many agent threads running shells).
2. **Memory pressure as an aggravator (possible, not proven).** 11-21 had two Virtualization VMs (7.3 GB resident), the Claude
   renderer 2 GB and the game 2.1 GB: summed resident sizes 33 GB on a 24 GB machine (overcounts shared pages). No Jetsam
   report near 14:13, so not a memory-exhaustion kill. 11-15 had 21 GB summed and no VM, so pressure is not required.
3. **Parallel Godot smoke runs (ruled out as necessary).** On 11-21 the queue was idle (no Godot or dotnet process in the
   stackshot), so the "owner is playing" mode of #2273 would not have helped.
4. **GPU, Metal or OpenGL driver hang (unlikely).** WindowServer's main thread waits on `runningboardd`, not on IOGPU; no
   GPU-restart report; the game's OpenGL abort comes after the watchdog fired.
5. **Our game leaking resources (ruled out for 11-21).** Counters bounded over 20 minutes; 11-08 happened without the game.

### Owner note, 2026-11-24

No hang has happened while the agents (Claude Code, Codex) were off. macOS 27.2.1 has not yet been tested with agents on, so a
fix there is neither confirmed nor ruled out.

### Next steps (owner side unless noted)

- At the next hang do **not** press the power button at once; wait about 3 minutes. The userspace watchdog then panics and
  writes `panic-full-*.panic` with the kernel stack (as on 11-12), which Apple needs.
- Right after the reboot: `sudo sysdiagnose -u -b -f ~/Desktop`, then a Feedback Assistant report (macOS, "system hang / WindowServer
  watchdog") with the `.WindowServer-*.ips` files, the panic file and the sysdiagnose.
- Install any macOS 27.2.x update (`softwareupdate --list` shows them, no sudo), then play with agents on and note the result here.
- Optional: symbolicate the kernel frames with the Kernel Debug Kit for build 26B123 (free Apple developer download) to name the
  kernel function.
- Mitigation to decide (owner): hold shell-heavy agent work (Claude Code and Codex threads) while the owner plays, not only the
  queue's own runs; the queue's playing mode covers only our queue (#3115 AC3, open).

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

- Read the wait chain with the tool instead of by hand (it only reads the file and writes nothing; it repairs the cut-off JSON,
  prints the WindowServer main-thread chain, every `TH_UNINT` thread with its ancestry, resident size, wait event and first kernel
  frames, and marks the threads that share the wait event of the chain's last thread; `--frames N` sets how many kernel frames):

  ```sh
  python3 -B tools/local_dev/windowserver_report.py /Library/Logs/DiagnosticReports/.WindowServer-DATE.ips
  ```

  Its unit test uses a synthetic fixture only (`test_windowserver_report.py`); no real report is stored in git. The field names it
  reads were taken by hand from the four reports; a missing field prints `?`. Check the tool's output against the raw report the
  first time a new macOS version writes one.
- By hand, in the `.WindowServer` stackshot, `stackshot.processByPid` holds each
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
- The owner can send the four `.WindowServer` stackshots and `sysdiagnose` to Apple through
  Feedback Assistant (macOS 27.2, RunningBoard / process creation hang); this
  is an owner action.
