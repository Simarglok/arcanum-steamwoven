# Packaged builds (ARC-DEV-047, Windows ARC-DEV-048, daily ARC-DEV-062)

A packaged build is the game for a player: the terrain viewer project exported with its own player name and user
folder, started without inputs, so it opens the launcher ([ARC-UI-014](../rendering/terrain-viewer.md#launcher-screen-arc-ui-014))
and the original main menu. It is separate from the developer builds of the export endpoints (`artifacts/world-builds`,
the build registry, `./arcanum area`/`map`), which it neither reads nor changes. The owner's build instruction
(prerequisites, `main` or a chosen commit, outputs) is this page; the player
instruction is [docs/player/alpha-build.md](../player/alpha-build.md). Platforms: **macOS** (universal `.app`, ad-hoc signed, smoked on
the Mac) and **Windows 11 x64** (exported on the Mac from the installed Godot 4.7.2 .NET Windows templates, unsigned,
checked by its files only, delivered as one zip).

## Prerequisites on the Mac

The build runs on a Mac from a committed state of the repository, never from local changes: `git archive` of one commit
into a fresh temporary folder, so uncommitted or untracked files cannot enter it.

- .NET SDK **8.0.425** (pinned by `global.json`) in `~/.local/share/dotnet` or the folder `DOTNET_ROOT` names;
- Godot **4.7.2 .NET** at `/Applications/Godot_mono.app`;
- its export templates `4.7.2.stable.mono` (Godot: Editor → Manage Export Templates), which hold both the macOS and the
  Windows templates;
- this repository cloned with the `origin` remote (the command fetches `origin main` itself);
- for the macOS smoke only: the game in `/Applications/Arcanum`, `~/Applications/Arcanum` or a CrossOver bottle (without
  it the smoke is skipped and the run says so).

## Command

    ./arcanum package mac                 # fetch origin main, build that commit, smoke it, install it
    ./arcanum package win                 # the same commit flow for Windows 11 x64: file checks, zip, install
    ./arcanum package mac --ref COMMIT    # another commit (still fetched first unless --no-fetch)
    ./arcanum package mac --verbose       # the step lines here instead of the quiet run log

It runs as an `export` in the [launch queue](toolchain.md#launch-queue-arc-dev-033) (quiet by default: the run log holds
every line, stdout keeps PASS or FAIL). The builder is `tools/local_dev/package.py` (tests
`tools/local_dev/test_package.py`). Steps, each with its log in `artifacts/packages/logs/<platform>/`:

1. `git fetch origin main` in the main checkout, then the commit of `--ref` and its commit date.
2. `git archive` of that commit into a fresh temporary folder: only the commit's tracked files, never the working
   tree, so uncommitted or untracked files cannot enter a package and the checkout is not touched. The commit's
   [app icon](../../assets/app-icon/README.md) is then copied into the extracted viewer project (the
   [icon](#what-makes-the-package-different-from-a-developer-build) below).
3. `restore` (the viewer solution, `--locked-mode`), `build`, `import` (Godot `--headless --import`), `export`
   (`--export-release "macOS package"` or `"Windows package"`), with the canonical toolchain of [toolchain.md](toolchain.md) (the pinned SDK first
   on `PATH`, the Godot shell sentinel). The export log must show the pinned SDK, as for the export endpoints.
4. macOS: the bundle has its executable, `codesign --verify --deep --strict` passes and the signature is ad-hoc.
   Windows (no signing step): `Arcanum Steamwoven.exe` is a PE x86-64 GUI executable whose version resource carries the
   product name (the preset's `application/modify_resources`, which Godot 4.7 writes itself without rcedit),
   `Arcanum Steamwoven.pck` is a Godot pack exported with the `steamwoven_package` feature, and one
   `data_*_windows_x86_64` folder holds the game assembly, `GodotSharp.dll` and the .NET runtime (`coreclr.dll`,
   `hostfxr.dll`).
5. Smoke (macOS only: a Windows build cannot start on the Mac, so `BUILD.txt` says `smoke NOT RUN`), when the game is in `/Applications/Arcanum`, `~/Applications/Arcanum` or a CrossOver bottle (the launcher's
   own macOS candidates): the exported app is started with
   `-- --native-smoke`, `ARC_AREA_MAP_SMOKE_ONLY=menu` and a fresh `LAUNCHER_SETTINGS_TEST_PATH` (a player's first
   start): it must print `ARC-DEV-047 build Arcanum Steamwoven alpha · main <commit>` with its user folder
   `Arcanum Steamwoven Game`, then `ARC-UI-014 native-smoke launcher PASS` and `ARC-UI-011 native-smoke main menu PASS`.
   Without the game the smoke is skipped and the run says so; `--no-smoke` skips it on purpose. `BUILD.txt` and the
   final `PASS` line record whether the smoke passed or was skipped.
6. Windows: the export folder `Arcanum Steamwoven/` is zipped as `Arcanum Steamwoven Windows x64.zip` (the folder is the
   zip's top entry), the one file a player copies to the PC.
7. Only then the build is installed (below). A failed step installs nothing and prints the step, its exit code, its log
   and the log's last 20 lines. The temporary folder is removed, also when the queue stops the run (`--keep-work` keeps
   it). The owner's opt-in variables (`DAT_ROOT`, `ARC_*`, `ARCANUM_*` but the queue's own, `LAUNCHER_SETTINGS_*`,
   `WORLD_DIAGNOSTIC_*`) are dropped from the build and smoke environment.

### Building another commit

`--ref` is resolved in the main checkout with `git rev-parse` after `git fetch origin main`, so every commit of `main`
(also an older one) works directly, and so do a tag or `origin/BRANCH`. A commit that is only on another branch must be in
the checkout first: `git fetch origin BRANCH`, then `--ref origin/BRANCH` or the hash. Only committed files of that commit
are built; the checkout itself is not touched. `--no-fetch` skips the fetch (offline, or a commit already present).
Other options (`./arcanum package --help`): `--no-smoke` skips the macOS smoke, `--keep-work` keeps the temporary source and
export folder for a look, `--verbose` streams the step lines instead of the quiet run log.

Two limits: the build line in the game always reads `main <commit>`, also for a commit that is not on `main` (check
`BUILD.txt` for the real commit), and the player instruction copied beside the build is the one of that commit, so an
older commit ships its older instruction.

## Where the build lands

In the **main checkout** (also when the command runs from a worktree):

    artifacts/packages/macos/Arcanum Steamwoven.app
    artifacts/packages/windows/Arcanum Steamwoven Windows x64.zip
    artifacts/packages/<platform>/BUILD.txt       commit, date, product, smoke result, map-smoke result
    artifacts/packages/<platform>/README.md       the player instruction of that commit
    artifacts/packages/<platform>/previous/       the build before, with its BUILD.txt and README.md
    artifacts/packages/logs/<platform>/           the last run's step logs
    artifacts/packages/logs/daily/                the last ./arcanum daily run: report.txt and each step's log

The next successful package replaces the current build and moves it to `previous/` (the older `previous/` goes).
`./arcanum clean` lists each platform's current package as KEEP (never removed) and `previous/`, with an interrupted
install's `.incoming-*`, as DISPOSABLE with its size; `./arcanum clean --apply` removes those, keeping no previous build,
and refuses while a package run is going (ARC-DEV-055, [build retention](build-retention.md)). Packages
are local: the tool never commits, uploads or publishes them; giving a copy to a tester is the owner's own private act.

## Profiling package (ARC-DEV-3379)

    ./arcanum package mac --profiling     # for the owner's own measurements; never distributed

The same flow as `package mac` (fetch, `git archive`, restore, build, import, export, checks, menu smoke, install), with
these differences. Use: [Mac performance monitoring](mac-performance-monitoring.md#profiling-package-arc-dev-3379).

| Step | Player package | Profiling package |
|---|---|---|
| Export | `--export-release "macOS package"` | `--export-debug "macOS profiling"`: the same custom feature `steamwoven_package`, so the game behaves as the player's |
| C# code | `ExportRelease`: optimized, ReadyToRun | `ExportDebug` with the MSBuild property `ArcanumProfiling` (set only by this option; removed from the caller's environment): optimized, ReadyToRun too |
| Name and user folder | `Arcanum Steamwoven`, `Arcanum Steamwoven Game` | `Arcanum Steamwoven Profiling` for both: the two settings are rewritten in the extracted `project.godot` only (the build fails if either line is not there exactly once) |
| Bundle id | `org.arcanumsteamwoven.game` | `org.arcanumsteamwoven.profiling` |
| Signature | ad-hoc | ad-hoc with `com.apple.security.get-task-allow` (checked; without it nothing is installed) |
| Installed to | `artifacts/packages/macos/` | `artifacts/packages/macos-profiling/`, with `BUILD.txt` (a `profiling` line), this build's monitoring page as `README.md` and its own `previous/`; logs in `artifacts/packages/logs/macos-profiling/` |

A commit older than this option has no `macOS profiling` preset and is refused. `--profiling` is for `mac` only.
Only this command builds it: `./arcanum daily` and a player package run leave it as it is. So it cannot go stale
unnoticed (ARC-DEV-3478), a successful `./arcanum package mac` or `win` prints, and `./arcanum daily` puts into its
report after packaging, `WARNING the profiling package is out of date: ...` with both commits and this command, when the
installed profiling package holds neither the packaged commit nor a later one; the exit code and result do not change.
`./arcanum clean` treats its folder like a platform's
([build retention](build-retention.md)), and the launch queue of a checkout with this change treats a game running from
it like the player package ([details](mac-performance-monitoring.md#profiling-package-arc-dev-3379)).

## Daily package build (ARC-DEV-062)

The complete smoke run (the Crash Site smoke, the map batteries and every stage) runs only here, and only when `main` changed since the last package
([smoke scope](tasks.md#smoke-scope)). One command does the whole day:

    ./arcanum daily               # fetch origin main; skip if both packages hold it; else checks, smoke chain, packages
    ./arcanum daily --dry-run     # fetch, say whether it would build and print the steps; run nothing else
    ./arcanum daily --force       # build although the packages hold origin/main or the last day failed on it

It is `tools/local_dev/daily.py` (tests `tools/local_dev/test_daily.py`). It is not itself a queued run: each of its
steps takes its own place in the [launch queue](toolchain.md#launch-queue-arc-dev-033), so it refuses to run inside
`./arcanum run`, and one `daily` runs at a time per repository (a lock in the git folder; `--dry-run` takes none).

**Any failure blocks the packages** (owner decision 2026-10-29): a failed check or smoke ends the day with no package,
the packages stay at their last commit, and the report names each failed step with its run log and first error, so a
thread to find and fix it starts from the report; the first day after the fix packages. **The smoke runs even when
the root checks failed or timed out** (owner decision 2026-11-15, ARC-DEV-2224: the daily of 11-15 ran no smoke after a
dotnet timeout of 2700 s, which hid a day of smoke regressions): the checks and then the smoke always run, the report
shows both results, and a red check or a red smoke alone blocks the package (no green checks, no package). Only a
check that was *stopped* (a signal, exit 128 and above) ends the day without a smoke.

1. `git fetch origin main` in the main checkout (`--no-fetch` skips it). `SKIP` (exit 0, nothing written) when the
   `BUILD.txt` of both `artifacts/packages/macos` and `artifacts/packages/windows` holds that commit with a
   `map-smoke PASS` line, the mark of a package `daily` made: a package made by hand (`map-smoke NOT RUN`) does not
   count, so the chain still runs for it. `SKIP` with exit 1 when the last `daily` run found that same commit failing
   its root checks or smoke (its report's `failed commit` line): a failing `main` is not run again every day; the fix's
   commit runs, or `--force`. A stopped run, a cut-off smoke chain or a failed package is not the commit's failure and
   is tried again.
2. A fresh detached `git worktree` of that commit in an `arcanum-daily-*` folder of the system temporary folder: the
   steps run the commit's own commands there, never the owner's checkout or working tree. The worktree is removed at
   the end, also when the run is stopped; one kept by `--keep-work` or left by a killed run is removed by the next
   `daily` (under its lock, so never a running one's).
3. The root checks (CLAUDE.md §2): the solution's locked restore, build and tests (`./arcanum run --kind dotnet`), then
   the `dat_probe.py` and `oracle.py` self-tests (30 minutes each at most). They run without the owner's opt-in
   variables (`DAT_ROOT`, `ARC_*`, `ARCANUM_*` but the launcher's home), so no real-data test turns on. The oracle
   self-test uses the main checkout's `local/original-oracle/venv` (or `ARCANUM_ORACLE_VENV`), its cache of the locked
   Unicorn, rather than installing it into the fresh worktree each day; one that cannot load Unicorn is a failed check.
   A failed or timed-out check does not stop the day: the remaining checks and the smoke (step 4) still run. A step that
   ends with the timeout exit (124) gets a report line that names the cause from its own log: the run time hit the
   queue's limit (the dotnet step's 45 minutes, counted from the start of the run, with the seconds it waited for its
   place first; in the smoke chain's log the last timeout and its own ticket), the daily's own limit of an unqueued step
   (30 minutes), or, with no queue line in the log, "cause unknown" (queue wait cannot be told from run time).
4. `./arcanum smoke complete` ([smoke runner](toolchain.md#smoke-runner-arc-dev-061), ARC-DEV-396): its viewer build and
   import, the Crash Site smoke, the map batteries and then every stage the viewer accepts (about twenty processes; about 35
   minutes of slot with the two long stages). A failed, not run or stopped step means no package (it runs also after red root checks, which block the package too); a group of stages that fails only after other stages of its process
   and whose every stage passes alone in the retry (order-dependent, ARC-DEV-396) is reported but does not end the day.
5. `./arcanum package mac`, then `win`, of that same commit (`--ref COMMIT --no-fetch`), each told the chain's result as
   `--map-smoke`, which `BUILD.txt` keeps as its `map-smoke` line (`PASS (crash-site, main, town, wild, ruins, stages-main, ...)`: the steps that ran); a
   plain `./arcanum package` writes `map-smoke NOT RUN (...)`. Each platform is tried even when the other failed.

Every step's whole output goes to `artifacts/packages/logs/daily/STEP.log` (the queue's own run logs as well), and the
report, one line per step, for each failed step (and each failed smoke) its run log, first error with up to 8 lines
of context and last error, and a `RESULT` line, to
`artifacts/packages/logs/daily/report.txt` (the next run replaces the folder); stdout shows the same `DAILY` lines.
Nothing else under `artifacts/packages` is written by `daily` itself, so the bug reports beside a package
(`artifacts/packages/macos/reports/`) are never touched. Exit codes: 0 `RESULT PASS` or `SKIP` of a packaged commit;
1 `RESULT FAIL` (a check, the smoke chain or a package failed; the line names the failed checks and the smoke result together; a package that passed is still installed and named),
`RESULT STOPPED` (a stop signal; the worktree is still removed) or `SKIP` of a commit that already failed.

## What makes the package different from a developer build

- **Preset** `Windows package` in the same file: Windows Desktop, `x86_64`, the same custom feature, the `.pck` beside
  the `.exe` (not embedded), no console wrapper, `codesign/enable` off, product name `Arcanum Steamwoven` written into the
  executable's version resource, ANGLE exported on Godot's default (auto) and no D3D12 Agility SDK (the project renders
  with the Compatibility renderer).
- **Preset** `macOS package` in `tools/terrain-viewer/Godot/export_presets.cfg`: bundle id `org.arcanumsteamwoven.game`,
  custom feature `steamwoven_package`, Godot's default universal binary and built-in ad-hoc signature.
- **Feature overrides** in `project.godot`, active only in a build with that feature: `config/name` "Arcanum Steamwoven"
  (the app and its window), and a custom user folder "Arcanum Steamwoven Game", so `user://` is
  `~/Library/Application Support/Arcanum Steamwoven Game/` on macOS and `%APPDATA%\Arcanum Steamwoven Game\` on Windows.
  It is not the developer launcher's home `~/Library/Application Support/Arcanum Steamwoven`
  ([launcher research](../project/status/session.md)), and source runs and the endpoint exports keep Godot's
  default `app_userdata/Arcanum Terrain Viewer`.
- **App icon** (ARC-DEV-3457): `project.godot` sets `config/icon.steamwoven_package="res://app-icon.png"`. The build
  copies `assets/app-icon/steamwoven-icon-1024.png` of the packaged commit to that name inside the extracted project
  only (the checkout gets no second copy), and fails if the commit sets the icon but lacks the picture; a commit from
  before the icon sets none and keeps Godot's. Godot converts it itself: `icon.icns` of the macOS bundles, the icon
  resources of the Windows `.exe` (16 to 256 px); both were read back from the built player packages. The same setting
  is the one Godot reads at start for the running program's window, Dock and taskbar icon (not yet seen on screen,
  [#3469](https://github.com/Simarglok/arcanum-steamwoven/issues/3469)). The presets leave `application/icon` empty,
  since a preset's icon would win. The icon macOS 27 reports for the `.app` (the one Finder shows) has a grey rounded
  tile, drawn by the system behind the transparent ground (other versions not checked;
  [#3468](https://github.com/Simarglok/arcanum-steamwoven/issues/3468)).
  The copy is made by the builder of the running checkout, so a checkout from before ARC-DEV-3457 must be updated before
  it packages a newer commit (`./arcanum daily` runs the packaged commit's own builder).
  Source runs, the endpoint exports and the start-up splash keep Godot's logo
  ([#3467](https://github.com/Simarglok/arcanum-steamwoven/issues/3467)).
- **Build line**: the package step sets the MSBuild properties `ArcanumPackageCommit` and `ArcanumPackageDate` (as
  environment variables); the viewer project turns them into assembly metadata, and `ViewerBuildInfo` shows
  `Arcanum Steamwoven alpha · main <first 10 digits> · <date>` in the bottom right corner of the launcher and the main
  menu (a source run or an endpoint export shows `not a packaged build`). Every run prints
  `ARC-DEV-047 build <that line> user=<user folder>` at start.
- **Precompiled code** (ARC-UI-3419): the release export publishes the viewer's assemblies ReadyToRun
  (`PublishReadyToRun` in the viewer project for the `ExportRelease` configuration), so the first use of a window or a
  rule does not wait for the JIT compiler. It makes the macOS universal app about 80 MB larger and the first export
  downloads the matching `Microsoft.NETCore.App.Crossgen2` package from NuGet; a source run is unchanged.

## Not done

- A start of the Windows build on a Windows 11 PC: the launcher, the GOG detection (registry and default folders, so far
  synthetic tests only), the main menu, the SmartScreen steps of the player instruction, a new game reaching the map and a
  save and load are still to be confirmed by the owner (map files are read on Windows since ARC-DEV-049, tested on macOS
  only); the Windows build is not signed (a free signature would need a free certificate route, none chosen).
- An import of a real Steam download (**Install from Steam…**, ARC-DEV-3515, synthetic tests only) on macOS or Windows,
  including whether its `Arcanum.exe` lets the first Play prepare the game data, is the owner's check ([#3516](https://github.com/Simarglok/arcanum-steamwoven/issues/3516)).
- Bundling `innoextract` into the package, so the player needs no separate install (the "A+" option of the GOG installer research, ARC-RES-087), is not built.
- The icon on a Windows 11 PC (Explorer, taskbar) and in the Dock of a running macOS package, still to be confirmed by
  the owner ([#3469](https://github.com/Simarglok/arcanum-steamwoven/issues/3469)); notarization (needs a paid Apple Developer ID), automatic updates.
- Starting a downloaded (quarantined) copy was not tried: the instruction's "Open Anyway" steps follow Apple's
  documentation.
