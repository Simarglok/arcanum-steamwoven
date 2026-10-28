# Packaged builds (ARC-DEV-047, Windows ARC-DEV-048)

A packaged build is the game for a player: the terrain viewer project exported with its own player name and user
folder, started without inputs, so it opens the launcher ([ARC-UI-014](../rendering/terrain-viewer.md#launcher-screen-arc-ui-014))
and the original main menu. It is separate from the developer builds of the export endpoints (`artifacts/world-builds`,
the build registry, `./arcanum area`/`map`), which it neither reads nor changes. The player instruction is
[docs/player/alpha-build.md](../player/alpha-build.md). Platforms: **macOS** (universal `.app`, ad-hoc signed, smoked on
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
    artifacts/packages/<platform>/BUILD.txt       commit, date, product, smoke result
    artifacts/packages/<platform>/README.md       the player instruction of that commit
    artifacts/packages/<platform>/previous/       the build before, with its BUILD.txt and README.md
    artifacts/packages/logs/<platform>/           the last run's step logs

The next successful package replaces the current build and moves it to `previous/` (the older `previous/` goes).
`./arcanum clean` lists each platform's current package as KEEP (never removed) and `previous/`, with an interrupted
install's `.incoming-*`, as DISPOSABLE with its size; `./arcanum clean --apply` removes those, keeping no previous build,
and refuses while a package run is going (ARC-DEV-055, [build retention](build-retention.md)). Packages
are local: the tool never commits, uploads or publishes them; giving a copy to a tester is the owner's own private act.

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
