# Packaged builds (ARC-DEV-047, Windows ARC-DEV-048, daily ARC-DEV-062)

A packaged build is the game for a player: the terrain viewer project exported with its own player name and user
folder, started without inputs, so it opens the launcher ([ARC-UI-014](../rendering/terrain-viewer.md#launcher-screen-arc-ui-014))
and the original main menu. It is separate from the developer builds of the export endpoints (`artifacts/world-builds`,
the build registry, `./arcanum area`/`map`), which it neither reads nor changes. The owner's build instruction
(prerequisites, `main` or a chosen commit, outputs) is the [README](../../README.md#how-to-build-the-alpha-for-a-player); the player
instruction is [docs/player/alpha-build.md](../player/alpha-build.md). Platforms: **macOS** (universal `.app`, ad-hoc signed, smoked on
the Mac) and **Windows 11 x64** (exported on the Mac from the installed Godot 4.7.2 .NET Windows templates, unsigned,
checked by its files only, delivered as one zip).

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
   tree, so uncommitted or untracked files cannot enter a package and the checkout is not touched.
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

## Daily package build (ARC-DEV-062)

The full map smoke chain runs only here, and only when `main` changed since the last package
([smoke scope](tasks.md#smoke-scope)). One command does the whole day:

    ./arcanum daily               # fetch origin main; skip if both packages hold it; else checks, smoke chain, packages
    ./arcanum daily --dry-run     # fetch, say whether it would build and print the steps; run nothing else
    ./arcanum daily --force       # build although the packages hold origin/main or the last day failed on it

It is `tools/local_dev/daily.py` (tests `tools/local_dev/test_daily.py`). It is not itself a queued run: each of its
steps takes its own place in the [launch queue](toolchain.md#launch-queue-arc-dev-033), so it refuses to run inside
`./arcanum run`, and one `daily` runs at a time per repository (a lock in the git folder; `--dry-run` takes none).

**Any failure blocks the packages** (owner decision 2026-10-29): a failed check or smoke ends the day before packaging,
the packages stay at their last commit, and the report names each failed step with its run log and first error, so a
thread to find and fix it starts from the report; the first day after the fix packages.

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
4. `./arcanum smoke full` ([smoke runner](toolchain.md#smoke-runner-arc-dev-061)): its viewer build and import, the
   Crash Site smoke and the map batteries. A failed, not run or stopped step ends the day with no package.
5. `./arcanum package mac`, then `win`, of that same commit (`--ref COMMIT --no-fetch`), each told the chain's result as
   `--map-smoke`, which `BUILD.txt` keeps as its `map-smoke` line (`PASS (crash-site, main, town, wild, ruins)`); a
   plain `./arcanum package` writes `map-smoke NOT RUN (...)`. Each platform is tried even when the other failed.

Every step's whole output goes to `artifacts/packages/logs/daily/STEP.log` (the queue's own run logs as well), and the
report, one line per step, for each failed step (and each failed smoke) its run log, first error with up to 8 lines
of context and last error, and a `RESULT` line, to
`artifacts/packages/logs/daily/report.txt` (the next run replaces the folder); stdout shows the same `DAILY` lines.
Nothing else under `artifacts/packages` is written by `daily` itself, so the bug reports beside a package
(`artifacts/packages/macos/reports/`) are never touched. Exit codes: 0 `RESULT PASS` or `SKIP` of a packaged commit;
1 `RESULT FAIL` (a check, the smoke chain or a package failed; a package that passed is still installed and named),
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
  ([launcher research](../research/launcher-settings.md)), and source runs and the endpoint exports keep Godot's
  default `app_userdata/Arcanum Terrain Viewer`.
- **Build line**: the package step sets the MSBuild properties `ArcanumPackageCommit` and `ArcanumPackageDate` (as
  environment variables); the viewer project turns them into assembly metadata, and `ViewerBuildInfo` shows
  `Arcanum Steamwoven alpha · main <first 10 digits> · <date>` in the bottom right corner of the launcher and the main
  menu (a source run or an endpoint export shows `not a packaged build`). Every run prints
  `ARC-DEV-047 build <that line> user=<user folder>` at start.

## Not done

- A start of the Windows build on a Windows 11 PC: the launcher, the GOG detection (registry and default folders, so far
  synthetic tests only), the main menu, the SmartScreen steps of the player instruction, a new game reaching the map and a
  save and load are still to be confirmed by the owner (map files are read on Windows since ARC-DEV-049, tested on macOS
  only); the Windows build is not signed (a free signature would need a free certificate route, none chosen).
- An app icon of the project's own (Godot's default icon is used), notarization (needs a paid Apple Developer ID),
  automatic updates.
- Starting a downloaded (quarantined) copy was not tried: the instruction's "Open Anyway" steps follow Apple's
  documentation.
