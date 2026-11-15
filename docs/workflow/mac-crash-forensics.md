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
quit message, cause not found) was flat after the
first two minutes: physical footprint 366 MB, IOAccelerator 13 MB, IOSurface
16 MB, threads 33 to 34. A read-only code review of the creation editor and art
path found no unbounded cache and no per-frame texture allocation (the font,
chunk and mapping caches are capped or weak-keyed). Not covered: a soak inside
the character-creation window itself (no launch flag opens it; it needs a click
path) and runs longer than 10 minutes.

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
