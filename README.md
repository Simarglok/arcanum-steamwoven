# Arcanum Steamwoven

Research-first, independent reimplementation of **Arcanum: Of Steamworks and Magick Obscura** in C# and Godot 4, using
the player's original game data.

**Status (main, 2026-10-28, through PR #525).** An early alpha for testing, not a finished game: every component is
deliberately bounded, its original behaviour is read from the retail executable component by component, and parity with
the original runtime as a whole is not verified. The [component catalogue](docs/project/status.md) lists each
component's implemented scope, evidence and source links; the [coverage matrix](docs/requirements/coverage.md) gives
each of the 191 functional requirements one status (4 implemented, 150 partly in the viewer, 13 rules without a host
yet, 15 researched only, 9 not started, as its summary counts them; requirements differ in size, so this is not a
percentage of the game).

- **Packaged alpha** for macOS and Windows 11 ([how to build it](#how-to-build-the-alpha-for-a-player)): a launcher that
  finds the player's GOG Arcanum and picks the *Steamwoven* or *Original* rules, then the original main menu, character
  creation and the opening shop.
- **Map mode** on every map of the game: the main world map streamed by window around the hero, the interiors and caves,
  map transitions and world-map travel (W), day/night and lights, roofs, doors and windows, sounds, music and voices, and
  an optional local [HD pack](docs/formats/hd-pack.md) for characters and objects.
- **Play**: click-to-move, walk and run, hover names and action cursor; inventory, pickup, loot and barter; NPC
  conversations; companions who follow and take orders; real-time and turn-based combat with the hero's and NPCs'
  attacks; spells and schematics; the character sheet, the logbook, experience and levels; skills such as picking locks;
  quick save and the save list. Every key: [Hotkeys](#hotkeys).
- **Testing aids**: the [debug mode](docs/player/alpha-build.md#7-debug-mode) (teleport to any area), the in-game bug
  report (Ctrl+B) and the diagnostic palette.
- **Headless rules** (Godot-free `Arcanum.Data`, `Arcanum.Simulation`, `Arcanum.Scripting`, `Arcanum.Session`): the
  original data formats, the game session and its save snapshot, character rules, combat, magic, technology, skills,
  NPC reaction and AI, and the SCR script dispatcher. `Arcanum.Executable` interprets the player's own `Arcanum.exe`
  (an integer x86 subset), the base of building the prototypes a fresh installation lacks.

Not there yet, among others: much of the NPC AI and the full SCR script runtime, traps that are spotted and fire, resting
(S) and random encounters, the original look of several windows (character sheet, logbook, world map, save and load), and
a run of the Windows build on a Windows 11 PC.
The requirement rows name what each part still lacks.

## Direction

- C# data/compatibility and simulation code independent of Godot.
- Godot 4.7.2 .NET for the bounded viewer presentation boundary; `Arcanum.Data` remains Godot-independent. See [Arcanum.Godot viewer](docs/Arcanum.Godot.md) for the shared-toolchain commands and known limits.
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
./arcanum package mac|win         # the player's alpha (below)
./arcanum run --kind window|headless|export|dotnet -- COMMAND   # any Godot run, export or dotnet build/test through the queue
./arcanum queue                   # the launch queue: running and waiting runs (--history N: the last finished ones)
./arcanum smoke pr --stages S,... # the per-PR smoke: only the touched stages (--crash-site when related); `smoke full`: the full map chain (ARC-DEV-061)
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

## How to build the alpha for a player

This section is the one instruction for building the packaged alpha (the launcher and the original main menu, reading
the player's own GOG Arcanum). The build runs on a Mac from a committed state of the repository, never from local
changes: `git archive` of one commit into a fresh temporary folder, so uncommitted or untracked files cannot enter it.
The steps, checks and package presets are in [packaging](docs/workflow/packaging.md); installing and starting a build is
in [the player instruction](docs/player/alpha-build.md), which ships beside every build as its `README.md`.

### Prerequisites on the Mac

- .NET SDK **8.0.425** (pinned by `global.json`) in `~/.local/share/dotnet` or the folder `DOTNET_ROOT` names;
- Godot **4.7.2 .NET** at `/Applications/Godot_mono.app`;
- its export templates `4.7.2.stable.mono` (Godot: Editor → Manage Export Templates), which hold both the macOS and the
  Windows templates;
- this repository cloned with the `origin` remote (the command fetches `origin main` itself);
- for the macOS smoke only: the game in `/Applications/Arcanum`, `~/Applications/Arcanum` or a CrossOver bottle (without
  it the smoke is skipped and the run says so).

### Build from `main`

```sh
./arcanum package mac    # Arcanum Steamwoven.app (universal, ad-hoc signed), launcher and main menu smoke
./arcanum package win    # Windows 11 x64: the Arcanum Steamwoven.exe folder as one zip (unsigned, file checks only)
```

Each fetches `origin main` and builds its newest commit (`--ref` defaults to `origin/main`). The run goes through the
[launch queue](docs/workflow/toolchain.md#launch-queue-arc-dev-033) as an `export` and prints `PASS` or `FAIL` with the
failed step, its log and the log's last lines.

### The daily build

```sh
./arcanum daily      # when main changed since the last package: root checks, full map smoke chain, both packages
```

It skips when both packages already hold the newest `main` commit (their `BUILD.txt`), builds from a fresh worktree of
that commit, and writes `map-smoke PASS (...)` into each `BUILD.txt`. Any failed check or smoke stops it before packaging:
the packages stay at their last commit and the report, `artifacts/packages/logs/daily/report.txt`, names each failed step
with its log and first error ([daily package build](docs/workflow/packaging.md#daily-package-build-arc-dev-062)).

### Build from a specific commit

```sh
./arcanum package mac --ref 1a2b3c4d5e   # a commit hash, full or an unambiguous prefix
./arcanum package win --ref 1a2b3c4d5e
./arcanum package mac --ref origin/BRANCH   # or anything else `git rev-parse` names as a commit, e.g. a tag
```

`--ref` is resolved in the main checkout with `git rev-parse` after `git fetch origin main`, so every commit of `main`
(also an older one) works directly. A commit that is only on another branch must be in the checkout first:
`git fetch origin BRANCH`, then `--ref origin/BRANCH` or the hash. Only committed files of that commit are built; the
checkout itself is not touched. `--no-fetch` skips the fetch (offline, or a commit already present).

Two limits of the current packager: the build line in the game always reads `main <commit>`, also for a commit that is not
on `main` (check `BUILD.txt` for the real commit), and the player instruction copied beside the build is the one of that
commit, so an older commit ships its older instruction.

Other options (`./arcanum package --help`): `--no-smoke` skips the macOS smoke, `--keep-work` keeps the temporary source
and export folder for a look, `--verbose` streams the step lines instead of the quiet run log.

### Where the build lands

In the main checkout (also when the command runs from a worktree):

    artifacts/packages/macos/Arcanum Steamwoven.app
    artifacts/packages/windows/Arcanum Steamwoven Windows x64.zip
    artifacts/packages/<platform>/BUILD.txt     the full commit, its date, the product, the smoke and map smoke results
    artifacts/packages/<platform>/README.md     the player instruction of that commit
    artifacts/packages/<platform>/previous/     the build before (the next build replaces it)
    artifacts/packages/logs/<platform>/         the last run's step logs

Only a build that passed every step replaces the current one. `artifacts/` is ignored by git, so these files are local
copies, not the source of any instruction. Do not delete them by hand ([build retention](docs/workflow/build-retention.md)).

### Which build is it

The launcher and the main menu show `Arcanum Steamwoven alpha · main <first 10 digits> · <date>` in the bottom right
corner; `BUILD.txt` beside the build has the full commit. While the debug mode is on, the main menu's line ends in
`DEBUG MODE`. A source run or a developer export (`./arcanum area`, `map`) says
`not a packaged build`. Every run also prints
`ARC-DEV-047 build <that line>` at start.

### Giving it to a player: signing

Both builds are signed for free only, so the first start of a copy that came from the internet asks once:

- **macOS** (ad-hoc signed, no notarization): a build made or copied locally opens with a double click. A downloaded,
  AirDropped or mailed copy is refused first; then **System Settings → Privacy & Security → Open Anyway**
  ([player steps](docs/player/alpha-build.md#2-first-start-on-macos-open-anyway)).
- **Windows 11** (unsigned): unpack the zip first, then on the blue SmartScreen window press **More info → Run anyway**,
  or tick **Unblock** in the zip's Properties before unpacking. Smart App Control, when on, may block the build outright
  ([player steps](docs/player/alpha-build.md#3-first-start-on-windows-11-unzip-then-run-anyway)).

### User data, debug mode and bug reports

- **User folder** (settings `launcher_v1.json`, options, `saves/`, `logs/godot.log`, fallback `reports/`), separate from
  any developer build: `~/Library/Application Support/Arcanum Steamwoven Game/` on macOS,
  `%APPDATA%\Arcanum Steamwoven Game\` on Windows. Deleting it starts the game as on its first run
  ([details](docs/player/alpha-build.md#5-where-your-settings-and-saves-live)).
- **Debug mode** (testing aid, off by default): hold **Shift** at start to open the launcher, tick **Debug mode**, **Play**
  (stored until unticked); or for one start pass `-- --debug-mode` to the executable. The world map (**W**) then lists
  every area with **Teleport**, the diagnostic panel shows, and saves carry `[DEBUG]`
  ([details](docs/player/alpha-build.md#7-debug-mode)).
- **Bug report**: **Ctrl+B** in play, in the main menu or over any window writes `report.md`, `log.txt` (the last 120
  seconds), `screenshot.png` and, when one can be taken, a save into a `reports/<date_time>/` folder next to the app
  (macOS) or the `.exe` (Windows), or into the user folder's `reports/` when that place cannot be written. It holds
  content of the player's own copy of Arcanum: never commit it
  ([details](docs/player/alpha-build.md#9-reporting-a-bug-ctrlb)). A handled report (its fix thread started, or called
  not a bug) is moved, never deleted, into `reports/archive/` with one line in `reports/archive/INDEX.md` (ARC-DEV-056).

## Hotkeys

The keyboard and mouse controls of the packaged alpha and of the map mode (`./arcanum area`, `./arcanum map`), as the
code on `main` routes them (ARC-DEV-053). Keys follow the original game where it has them; the rows marked *Steamwoven*
are this project's own.

- **Mac:** on a keyboard whose F keys control brightness or volume, hold **fn** with every F key (**fn+F7**,
  **fn+Ctrl+Shift+F10**). **Ctrl** is the Control key on a Mac too, never Command.
- A, C, F, I, L, R, S, T and W with Ctrl, Alt or Command held do nothing in play, as in the original.
- An open window, conversation, menu or the diagnostic palette takes the keys first: the *In play* keys act only while
  none is open.

**In play**

| Key | Action |
| --- | --- |
| Escape | In-play menu (save, load, options, quit) |
| F7 / F8 | Quick save / quick load (one slot) |
| I | Inventory |
| C | Character sheet |
| L | Logbook |
| T | Technological schematics |
| M | Spell pane, shown or hidden (on the key's release, while the hero can cast) |
| W | World map; in the [debug mode](docs/player/alpha-build.md#7-debug-mode) it also lists every area with **Teleport** |
| Num Lock | Run by default on or off (the original's Num Lock lock; on a keyboard that has the key) |
| Arrow keys | Pan the camera |
| H | Legacy or HD graphics (needs a local [HD pack](docs/formats/hd-pack.md)); *Steamwoven* |
| F11 | Fullscreen on or off; *Steamwoven* |

**Combat**

| Key | Action |
| --- | --- |
| R | Combat mode (ready / unready) |
| Space | Turn-based on or off (also the default combat mode of the options) |
| E | End the hero's turn in a turn-based round (on the key's release) |
| `,` `.` `/` held while clicking a foe | Called shot at the head, the arms, the legs |
| Alt + left click (combat mode) | Attack an object (a door, a window, a container, scenery) or loot a corpse |

**Quick bar**

| Key | Action |
| --- | --- |
| 1 … 9, 0 | Use quick slot 1 … 10 |
| A | Repeat the last action |
| Ctrl+1 … Ctrl+0 | Put the last action on that slot |
| Ctrl+Shift+1 … Ctrl+Shift+0 | Clear that slot |
| 1 … 0 with an item held in the inventory | Put the held item on that slot |

**Orders to companions** (on the key's release, to every follower within 10 tiles)

| Key | Order |
| --- | --- |
| F1 | Walk to the tile under the pointer |
| F2 | Attack |
| F3 | Stay close |
| F4 | Spread out |
| F5 | Back off (stop attacking) |
| F6 | Follow in the *Steamwoven* rules; nothing in the *Original* rules (the original's preset names nobody) |

**Mouse**

| Input | Action |
| --- | --- |
| Left click | Walk there, or use, talk to, pick up or attack (combat mode) what is under the pointer |
| Ctrl + left click on the ground | Run there; walk there instead while the Always run setting is on. On a Mac, Ctrl+click works the same |
| Shift while using an item or casting a spell | Use it on the hero, cast it at the hero |
| Alt held at a spell's target cursor | Let an aggressive spell pick the hero's party too |
| Right click | Cancel a spell's target cursor |
| Left click on a quick bar slot or recent-action button | Use it (with an item held in the inventory: put the item on the slot) |
| Left press on a college's button in the spell pane; left click on a spell's icon there | Show that college's known spells; use the spell (as the hotkey does) |
| Right press and drag from a quick bar slot, a spell of the spell pane or a slot of the weapon pane | Move it on the bar, put it on a slot, or drag it off the bar to empty the slot |
| Click on a follower's portrait | That follower's order menu (a walk order takes the next left click as its tile) |
| Left click on a spell icon in the five top slots | End that maintained spell (hovering it shows the spell) |

**Inside windows** (the open window takes every key)

| Window | Keys |
| --- | --- |
| Inventory | I or Escape closes; 1 … 0 puts the held item on a quick slot |
| Quantity box | keypad digits type (the main-row digits do not), Backspace or Delete erases, Enter accepts, Escape or I cancels |
| Character sheet | C or Escape closes |
| Logbook | L or Escape closes; Tab switches the tab |
| Technological schematics | T or Escape closes; Tab assembles |
| World map | W or Escape closes (or ends the journey); Enter travels |
| Book or note | Escape closes; Left or Page Up, Right or Page Down turns a page |
| Barter | Escape closes |
| Conversation | click a reply; keys do nothing |
| Save and load windows | Up / Down select; Enter saves or loads; Delete or Backspace deletes (while no description is typed); Escape closes; typing writes the save's description |
| Main menu and in-play menu | the letter of a button presses it; Escape goes back; Enter answers yes in the quit box and accepts the character editor and the opening shop |
| Options | Up / Down move the focus, Left / Right change the focused control; O closes the menu opened in play |
| Death screen | a key or a click ends a slide, Escape skips the rest; then, in the *Steamwoven* rules with a save there, Enter or Y loads the last save and Escape or N declines (the *Original* rules return to the main menu) |

**Tools** (*Steamwoven*, not part of the original game)

| Key | Action |
| --- | --- |
| Ctrl+B | [Bug report](docs/player/alpha-build.md#9-reporting-a-bug-ctrlb) over anything: Ctrl+Enter (or Command+Enter) sends, Escape cancels |
| Ctrl+Shift+F9 | Diagnostic palette |
| Ctrl+Shift+F10 | [Diagnostic panel](docs/player/alpha-build.md#8-showing-and-hiding-the-diagnostic-panel) shown or hidden |
| Shift held while the packaged game starts | Opens the launcher (debug mode, settings) |

**Not there yet.** These original keys do nothing yet (the diagnostic panel, when shown, names them
`Reserved: … (NOT IMPLEMENTED)`): F (fate points), S (sleep or wait), K (skills pane), O (options; Escape opens them),
V (version), Home (centre on the hero), Enter (message), F10 (compact interface), F12 (screenshot). Attacking a follower
(left Alt + click in combat mode) is refused as not ready yet. `,` `.` `/` pressed alone also show the reserved notice;
held during an attack click they aim as listed under *Combat*.

## Start here

- [Documentation index](docs/README.md)
- [Project vision](docs/project/vision.md) and [dependency roadmap](docs/project/roadmap.md)
- [Full-game functional requirements and bounded implementation coverage (draft)](docs/requirements/README.md)
- [Toolchain and checks](docs/workflow/toolchain.md)
- [Private local diagnostic launch and build retention](docs/workflow/local-development.md)
- [Architecture decisions](docs/architecture/decisions.md)
- [Research method and R1 boundary](docs/research/method.md)
- [DAT format boundary](docs/formats/dat.md)
- [ART format boundary](docs/formats/art.md)
- [Arcanum.Data API, limits and commands](docs/Arcanum.Data.md)
- [Arcanum.Godot viewer](docs/Arcanum.Godot.md)
- [Task workflow](docs/workflow/tasks.md) and [task naming](docs/workflow/task-naming.md)
- [Agent rules](AGENTS.md) and [the Claude session guide](CLAUDE.md)
- [Contributor guidance](CONTRIBUTING.md)

**Tasks and acceptance contracts come from the owner's explicit requests ([task workflow](docs/workflow/tasks.md)):** a direct instruction, a Claude project thread or a named `ARC-TYPE-NNN` work item. Each task is one PR, reviewed independently and merged by the agent that did it unless the owner asked to review it. Obsidian stores research and discussions; it is not a task queue. Repository documents describe product and engineering policy, not runnable tickets.

The local `toolkit/` archive is intentionally ignored. No task starts merely because a roadmap or research note exists.
