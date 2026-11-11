# Arcanum Steamwoven

Research-first, independent reimplementation of **Arcanum: Of Steamworks and Magick Obscura** in C# and Godot 4, using
the player's original game data.

**Status.** An early alpha for testing, not a finished game: every component is deliberately bounded, its original
behaviour is read from the retail executable component by component, and parity with the original runtime as a whole is
not verified. The [component catalogue](docs/project/status.md) lists each component's implemented scope, evidence and
source links; the [coverage matrix](docs/requirements/coverage.md) gives each functional requirement one status (its
summary table has the counts; requirements differ in size, so they are not a percentage of the game).

- **Packaged alpha** for macOS and Windows 11 ([how to build it](docs/workflow/packaging.md)): a launcher that
  finds the player's GOG Arcanum and picks the *Steamwoven* or *Original* rules, then the original main menu, character
  creation and the opening shop.
- **Map mode** on every map of the game: the main world map streamed by window around the hero, the interiors and caves,
  map transitions and world-map travel (W), day/night and lights, roofs, doors and windows, sounds, music and voices, and
  an optional local [HD pack](docs/formats/hd-pack.md) for characters and objects.
- **Play**: click-to-move, walk and run, hover names and action cursor; inventory, pickup, loot and barter; NPC
  conversations; companions who follow and take orders; real-time and turn-based combat with the hero's and NPCs'
  attacks; spells and schematics; the character sheet, the logbook, experience and levels; skills such as picking locks;
  quick save and the save list. Every key: [Hotkeys](docs/player/hotkeys.md).
- **Testing aids**: the [debug mode](docs/player/alpha-build.md#7-debug-mode) (teleport to any area), the in-game bug
  report (Ctrl+B) and the diagnostic palette.
- **Headless rules** (Godot-free `Arcanum.Data`, `Arcanum.Simulation`, `Arcanum.Scripting`, `Arcanum.Session`, `Arcanum.World`): the
  original data formats, the game session and its save snapshot, character rules, combat, magic, technology, skills,
  NPC reaction and AI, and the SCR script dispatcher. `Arcanum.Executable` interprets the player's own `Arcanum.exe`
  (an integer x86 subset), the base of building the prototypes a fresh installation lacks.

Not there yet, among others: much of the NPC AI and the full SCR script runtime, traps that are spotted and fire, resting
(S) and random encounters, the original look of several windows (character sheet, logbook, world map, save and load), and
a run of the Windows build on a Windows 11 PC.
The requirement rows name what each part still lacks.

## Direction

- C# data/compatibility and simulation code independent of Godot.
- Godot 4.7.2 .NET for the bounded viewer presentation boundary; `Arcanum.Data` remains Godot-independent. See [Arcanum.Godot viewer](docs/project/components/Arcanum.Godot.md) for the shared-toolchain commands and known limits.
- Preserve original gameplay; distinguish original behavior, bug fixes and optional mod profiles.
- Modern resolutions, scalable UI and high-DPI from the early Legacy frontend.
- Keyboard/mouse and first-class gamepad interaction.
- Legacy graphics first. HD is an optional local pack: existing frames of characters and objects upscaled (4× soft-edge by default, 2× supported) with the same directions, frames, anchors and timing. Each player builds the pack from their own installation or imports it from another of their devices (the build and import tooling, ARC-DEV-003, is deferred by the owner); it stays outside the repository and is never distributed ([HD pack format](docs/formats/hd-pack.md), [HD presentation contract](docs/rendering/hd-presentation-contract.md)). Additional frames/directions and 3D are future options, not commitments.

Players provide their own legally acquired copy. Original game assets, third-party binaries and extracted content are not distributed here. MIT applies to independently authored project code and documentation, not to Arcanum or archived third-party material. This unofficial fan project is not affiliated with the game's rights holders.

## The `./arcanum` launcher

`./arcanum` in the repository root is the project's developer launcher, a Python script over `tools/arcanum-local` and
`tools/local_dev` ([project launcher](docs/workflow/local-development.md#project-launcher)). It starts the viewer modes
from the exported developer builds, builds the player's alpha and puts every Godot and `dotnet` run into one queue.
`./arcanum --help` and `./arcanum COMMAND --help` list every option.

```sh
./arcanum setup                   # find the installed game (default /Applications/Arcanum, --game-root DIR) and configure the launcher
./arcanum status                  # which modes have every input configured, and their builds
./arcanum area                    # the Crash Site on the main world map (map mode), main menu first; --hero NAME, --sector X,Y, --no-main-menu
./arcanum map "crash site cave"   # one whole map by its folder name; --main-menu, --hero NAME, --sector X,Y
./arcanum map Arcanum1-024-fixed  # the main world map, streamed by window from the new-game start
./arcanum world                   # the WORLD diagnostic
./arcanum package mac|win         # the player's alpha (packaged builds, below)
./arcanum run --kind window|headless|export|dotnet -- COMMAND   # any Godot run, export or dotnet build/test through the queue
./arcanum queue                   # the launch queue: running and waiting runs (--history N: the last finished ones)
./arcanum smoke pr --stages S,... # the per-PR smoke: only the touched stages (--crash-site when related); `--auto`: the zones the diff selects (ARC-DEV-396); `smoke full`: the batteries chain, `smoke complete`: the nightly run, every stage
./arcanum oracle                  # run original functions of the installed Arcanum.exe in the emulator oracle, then the OriginalExe tests
./arcanum clean                   # which builds are protected and which may go (dry run; --apply removes only those)
```

`area` and `map` also take `--debug-mode`, `--dry-run` (print the command) and `--no-queue`. They run the current build
exported by ARC-DEV-015 (the universal map-mode build, preferred) or ARC-RENDER-008, so a feature on `main` shows only
after that build is exported again ([universal map-mode export](docs/workflow/local-development.md#universal-map-mode-candidate-arc-dev-015)).
Runs are quiet: the whole output goes to a log file, the terminal keeps `PASS` or `FAIL` with the first error
([launch queue](docs/workflow/toolchain.md#launch-queue-arc-dev-033)). A Godot run checks its own start: it waits up to
60 s for the command's start line and fails a run that showed a usage error or did not start
([checking that a run started](docs/workflow/toolchain.md#checking-that-a-run-started-arc-dev-029)).

Never delete `artifacts/` or a build in it by hand: the launcher and the export need the current builds. See
[which builds may be deleted](docs/workflow/build-retention.md).

## Building and installing the alpha

`./arcanum package mac|win` builds the player's alpha from a fresh `origin/main` (or `--ref COMMIT`), and `./arcanum daily`
does it for both platforms when `main` changed. Prerequisites, options, where the build lands and the daily chain are in
[packaged builds](docs/workflow/packaging.md); installing and starting a build, the debug mode and the bug report
(Ctrl+B) are in [the player instruction](docs/player/alpha-build.md), which ships beside every build as its `README.md`.
Every key of the game is in [Hotkeys](docs/player/hotkeys.md).

## Start here

- [Documentation index](docs/README.md), [component catalogue](docs/project/status.md) and [vision](docs/project/vision.md)
- [Toolchain and checks](docs/workflow/toolchain.md), [task workflow](docs/workflow/tasks.md), [task naming](docs/workflow/task-naming.md) and [backlog](docs/workflow/backlog.md)
- [Architecture decisions](docs/architecture/decisions.md) and [research method](docs/research/method.md)
- [Agent rules](AGENTS.md), [the Claude session guide](CLAUDE.md) and [contributor guidance](CONTRIBUTING.md)

Tasks and acceptance contracts come from the owner's explicit requests ([task workflow](docs/workflow/tasks.md)): a direct
instruction, a Claude project thread or a named `ARC-TYPE-NNN` work item. The local `toolkit/` archive is intentionally
ignored. No task starts merely because a plan or research note exists.
