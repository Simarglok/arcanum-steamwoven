# Launcher and settings: Original/Steamwoven rules, game folder and packaged builds

Status: **Research**, work item ARC-RES-074 (2026-10-28). The owner asked for a way to manage the settings of the
original and of Steamwoven, and judged that a separate launcher with parameters and configs is simpler than changing the
original's in-game menu. This note lists every switch and settings file the project has today and where it lives,
compares the ways to build a launcher and package it for macOS and Windows, proposes the configuration file, its
location on each OS and the rule profiles (Original, Steamwoven, custom), describes how to find a GOG installation, and
ends with a recommendation, a task breakdown and questions for the owner. It changes no code, viewer, session schema,
save, launcher or requirement coverage. Docs-only: no checks were run.

Requirements touched (none changes coverage here): [FR-UI-006](../requirements/ui-media-lifecycle.md#fr-ui-006)
(game, video and audio settings), [FR-LIFECYCLE-001](../requirements/ui-media-lifecycle.md#fr-lifecycle-001)
(start-up), [FR-LIFECYCLE-004](../requirements/ui-media-lifecycle.md#fr-lifecycle-004)
(preferences outside saves), [FR-CONTENT-001](../requirements/content-product.md#fr-content-001)
(the player's own installation), [FR-PRODUCT-005](../requirements/content-product.md#fr-product-005)
(platforms) and [FR-PRODUCT-006](../requirements/content-product.md#fr-product-006) (compatibility vs intended fixes).

Related work: the in-game Options window [ARC-UI-013](../rendering/terrain-viewer.md#options-window-arc-ui-013)
(PR #441, step N9 of the owner's alpha plan, which is outside the repository) and the alpha's final task, packaged builds for macOS and Windows 11 x64 with a player instruction for pointing
the app at the original GOG files (owner, 2026-10-28; outside the repository).

## Sources and evidence

- **Code and docs on `origin/main`** (2026-10-28, after PR #440): `src/Arcanum.Session/GameRules.cs`, the domain profiles
  under `src/Arcanum.Simulation`, `src/Arcanum.Data`, `src/Arcanum.Scripting`, the viewer's settings stores under
  `tools/terrain-viewer/Godot`, the local launcher `tools/local_dev` (`config.py`, `launch.py`), the export presets, the
  [rule modes contract](../world/rule-modes.md), [resource precedence](content-precedence.md) and
  [local development](../workflow/local-development.md).
- **Platform facts** (public sources, level F0 for this project until a packaging task checks them on a machine):
  [gogdb, Arcanum product 1207658829](https://www.gogdb.org/product/1207658829/build/2081) (GOG ships a Windows build
  only, install folder `Arcanum/`); [GOG forum, Arcanum on macOS through a Wineskin wrapper](https://www.gog.com/forum/arcanum_of_steamworks_and_magick_obscura/perfect_arcanum_on_macos_m1_m2_m3_systems)
  (the GOG installer runs inside the wrapper and installs under its `drive_c`);
  [GOG forum, install path in the registry](https://gog.com/forum/general/querying_gog_install_path)
  (`HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\<id>`); Godot 4 project settings `application/config/use_custom_user_dir` and
  `application/config/custom_user_dir_name`, and the native file dialog of `FileDialog`.
- No original executable was read: the launcher is a project feature, not original behaviour. The original's options
  (`arcanum.cfg`) are read by ARC-UI-013, not here.

## Inventory: what can be set today and where it lives

### Rule switches (Original vs Steamwoven)

The mechanism is the [rule modes contract](../world/rule-modes.md) (ARC-WORLD-023, owner decision D8 extended to every
domain on 2026-10-17): `RuleMode` (`Steamwoven`, `Original`), one immutable profile per domain, and `GameRules`
composing them. The registry on main lists **72 implemented switches in 15 profiles**; the Original value of each
switch is `true`, Steamwoven `false`:

| `GameRules` member | Profile (assembly) | Switches | Examples |
| --- | --- | ---: | --- |
| `Combat` | `CombatAnomalyProfile` (Simulation) | 22 | A2 bed modifier counted twice, A7 critical self-hit rolled twice, EX12 spread-out sets no-flee |
| `Magic` | `MagicAnomalyProfile` (Simulation) | 17 | MG4 save subtracts resistance, MG3 maintenance pays both, MG35 difficulty scales kill damage |
| `Data` | `DataFixProfile` (Data) | 5 | DF1 stone examine text lost, MP7 map patch script 0 ignored |
| `Orders` | `CompanionOrderRuleProfile` (Simulation) | 4 | OL6 follow order never unwaits |
| `Travel` | `TravelRuleProfile` (Simulation) | 4 | WT3 discovery uses the best radius |
| `Experience` | `ExperienceRuleProfile` (Simulation) | 3 | XP3 progress product wraps |
| `Scripts` | `ScrRuleProfile` (Scripting) | 3 | X10 global flag write spills, TE1 call delay wraps |
| `Sound` | `SoundRuleProfile` (Session) | 3 | SN18 a combat save loses the schemes |
| `Events` | `GameEventQueueProfile` (Simulation) | 2 | OQ2 rebuild on load, TE10 poison recovery twice |
| `Items` | `ItemRuleProfile` (Simulation) | 2 | TC7 repair quote counts damage twice |
| `Technology` | `TechnologyRuleProfile` (Simulation) | 2 | TC1 item cells favour magick |
| `Navigation` | `NavigationRuleProfile` (Simulation) | 2 | LR4/LR7 a far click does not move |
| `Reaction` | `ReactionRuleProfile` (Simulation) | 1 | reputation A2 a follower hit raises reaction |
| `Dialog` | `DialogRuleProfile` (Scripting) | 1 | GD-A1 generated line pick spans gaps |
| `Written` | `WrittenRuleProfile` (Data) | 1 | WRD3 unprefixed message loses its first character |

More switches are reserved (for example MG9, MG10, MR13, P3, SP11, SP15, SP17, EX15, SU17) and land with their slices,
so the list grows with every domain; any settings screen must be generated from the profiles, not typed by hand.

How the mode is chosen today:

- `GameSessionDescriptor.Rules` / `GameSessionRestoreOptions.Rules` start a session in a mode (default Steamwoven);
  `GameSessionOwner.SelectRules(mode)` switches during play. The contract's safety rules (read at the decision, no state
  depends on the mode, not saved) make a switch without a restart safe, and any save loads under either mode.
- `RuleModeSettingCodec` stores the player's choice as `{"schema":1,"rules":"steamwoven"|"original"}`, but **nothing
  writes or reads that file yet**: the viewer has no mode control and every session starts in Steamwoven. The
  contract's [Not ready](../world/rule-modes.md#not-ready) section says so.
- Only the two whole modes exist. The contract allows a profile changed with `with` only as a named custom mix for
  comparison runs, and a test requires every profile in a `GameRules` to carry the mode's name, so a per-switch custom
  mix is **not** supported by the session today.

### Player preferences in the AREA viewer (the current game host)

The JSON stores keep an automated run away from the player's files (ARC-DEV-032): a test path variable wins; without
one an automated run either uses an isolated folder (`ViewerStoragePaths`: zoom, input, saves) or has no file at all
(Always Run, volumes, visual profile, game settings); only an ordinary run reads and writes the player's file. The files carry a
`version` field (game settings, volumes, zoom, input) or a `schema` field (visual profile) and are overwritten in place. Under Godot's
`user://`:

| File | Owner task | Content |
| --- | --- | --- |
| `user://game_settings_v1.json` | ARC-UI-013 | the Options window's difficulty, default combat mode, auto attack (`version` 1) |
| `user://always_run_v1.json` | ARC-WORLD-020 | Always Run option |
| `user://audio_volumes_v1.json` | ARC-UI-040 | effects, voice, music volumes (0–10, default 5) |
| `user://visual_profile_v1.json` | ARC-UI-006 | Legacy or HD visual profile |
| `user://hd_budget_v1.json` | ARC-UI-006 | device memory budget for HD packs |
| `user://hd-packs/` (folder) | ARC-UI-006 | the player's local HD pack collection ([format](../formats/hd-pack.md)); always read, no isolation |
| `user://world_diagnostic_input_v1.json` | ARC-DEV-032 | joystick deadzone, sensitivity, camera inversion (`InputSettings`) |
| `user://world_diagnostic_zoom_v1.json` | ARC-DEV-032 | WORLD camera zoom |
| `user://saves/`, `user://saves/map` (folders) | ARC-UI-010 | saves and the map quick save |

Outside the AREA viewer, `src/Arcanum.Godot`'s HD review capture writes `user://hd-review` (diagnostic only).
ARC-UI-013 opens the original's Options window (window 6, ledger OP1–OP8; the original keeps its 16 controls in
`arcanum.cfg`) from the main menu and the in-play menus. It shows the Game and Sound pages (difficulty, default combat
mode, auto attack, Always Run, the three volumes) and hides the Video page and the controls without a function, each
with its reason. It stores its own three values in `game_settings_v1.json` and leaves Always Run and the volumes in
their files. It has **no rule-mode control**. The viewer's project is
still named `Arcanum Terrain Viewer` and has no custom user folder, so `user://` is Godot's default
`app_userdata/Arcanum Terrain Viewer` folder on each OS.

### Original game location (developer launcher only)

The game folder is known only to the developer launcher, a Python tool for the owner's Mac:

- `./arcanum setup [--game-root PATH]` examines one folder (the caller's, default `/Applications/Arcanum`; it never
  scans the machine), maps the
  `GAME_ROLES` it finds (`modules/Arcanum.dat`, `arcanum1.dat`–`arcanum3.dat`, `modules/Arcanum.PATCH0`, two map
  files, `data/proto`, the game folder itself) and records an `arcanum-installation-v1` record with the pinned
  identities (`pinnedVerified`).
- The record lives in `config-v1.json` under `~/Library/Application Support/Arcanum Steamwoven` (or
  `ARCANUM_LOCAL_HOME`), next to `build-registry-v1.json`. The tool's locking uses `fcntl`, so it does not run on
  Windows, and a player's build cannot depend on Python.
- The viewer itself takes the inputs as command-line flags (`--module`, `--resource`, `--game-root`, …). Map mode
  (ARC-RENDER-017, any installation since ARC-DEV-015) takes the module, `arcanum1.dat`, `arcanum2.dat` and
  `--game-root`, all inside the game folder; the content stack (`ContentMountPlan.Discover(gameRoot)`,
  ARC-DATA-011) validates a folder with typed results (`Ready`, `MissingGameRoot`, `MissingTig`, `MissingModule`, …).
  So **one setting, the game folder, is enough** for a packaged build: every role and the mount order can be derived
  from it.

### Other parameters

Command-line and environment switches (`--main-menu`/`--no-main-menu`, `--hero`, `--area-map`, `DAT_ROOT`,
`ARCANUM_*`, `ARC_*` test variables, `*_TEST_PATH`) are for development and tests, not for players. The export presets
(`tools/terrain-viewer/Godot/export_presets.cfg`, `src/Arcanum.Godot/export_presets.cfg`) contain macOS presets only.

## Launcher options

| | A. Launcher screen in the game app | B. Separate Godot launcher app | C. Separate native or web app |
| --- | --- | --- | --- |
| What | A scene of the same Godot project, shown before the main menu | A second Godot .NET project that writes the config and starts the game binary | Avalonia/.NET, Tauri/Electron, or a Python/Tk tool |
| Code reuse | Full: `GameRules`, codecs, `ContentMountPlan.Discover`, the switch catalogue, the viewer's UI theme | Full C#, but a second project, export and test setup | C#-only with Avalonia; none with Tauri/Electron/Python |
| Packaging | One `.app`, one `.exe` folder; nothing new | Two Godot .NET exports per platform (each carries the .NET runtime, roughly doubling size); on macOS a nested `.app` complicates signing | A second toolchain and installer per platform; Python is not on players' machines |
| Start the game | Same process: change the scene | `OS.CreateProcess`; two processes, two crash surfaces | Process start; path to the game binary per OS |
| Apply settings at once | Yes before the main menu (same process); after content is loaded, the game folder needs a restart, the rule mode can change in play | Only at the next start | Only at the next start |
| Risk to the original menu | None: the original main menu stays as it is | None | None |
| New dependencies | None | None | New UI framework and its licences, lock files, CI |

The owner's goal (do not rework the original menu) is met by all three; A meets it without a second program. A launcher
screen is not part of the original's menu: it comes before it, has its own look, and can be skipped.

### Recommended shape (option A)

- **When it shows.** On the first run, whenever the saved game folder is missing or no longer valid, and when the player
  asks for it (holding a modifier key at start, or `--launcher`). Otherwise the app goes straight to the original main
  menu; a "show the launcher at start" checkbox keeps it on every start for players who like it.
- **What it holds.** Settings that need a restart or belong to the project rather than to the original game:
  1. game folder: detected candidates, "Choose folder…" (native dialog), validation result in words, the detected
     version and source (GOG, other);
  2. rules: Steamwoven (default), Original, or a custom profile; for each switch its domain, name, ledger ID and a
     one-line description of the Original and Steamwoven behaviour (a player/designer text, see the switch catalogue
     below);
  3. display at start: window or fullscreen, monitor, UI scale (FR-PRODUCT-001);
  4. visuals: Legacy/HD, the HD pack folder and budget (today's `visual_profile_v1`/`hd_budget_v1` stores move here or
     stay shared);
  5. utilities: open the saves folder, open the logs folder, reset all settings;
  6. later: module choice (FR-CONTENT-003), language.
- **Play** writes the config and enters the main menu in the same process.

### Split with the in-game Options window (ARC-UI-013)

| Setting | In-game Options (original menu) | Launcher |
| --- | --- | --- |
| The original's options (difficulty, default combat mode, auto attack, Always Run, volumes; later the Video page) and their files | **Owner** (ARC-UI-013, done) | not shown |
| Rule mode Steamwoven/Original | optional line or toggle, not built by ARC-UI-013 (D8 says the settings menu may switch it in play; alpha plan N9 lists "rule mode"); the Rules line added by ARC-UI-014 | **Owner**: full choice incl. custom profiles |
| Per-switch custom profile | shows the active profile's name only | **Owner** (editor) |
| Game folder, module | — | **Owner** (applies at once before the main menu; a change after content is loaded needs a restart) |
| Display mode, monitor, UI scale | — (the original has none) | **Owner** |
| Legacy/HD, HD packs | the viewer's existing hot-key stays | **Owner** of the stored value |
| Key and gamepad bindings | later, wherever FR-UI-006 puts it | — |

One owner per setting and per file means the launcher and the Options window never write the same file. If the
in-game window shows the rule mode, it writes the launcher's file through one shared store class, not a copy of it.

## Configuration file

**Format.** Versioned JSON with a `schema` field, as `RuleModeSettingCodec` already is. Requirements for the new
file (today's stores do not all meet them: they overwrite in place, and the input store's save can throw): missing,
unreadable or unknown content loads the defaults; reading and writing never throw at the boundary but return a typed
result; the file is written atomically (temporary file and rename); unknown keys are kept on rewrite so an older build
does not erase a newer build's settings.

**Files.** One file per owner, so nothing is migrated now:

- `launcher_v1.json` (new): `gameRoot`, `gameSource` (`gog`, `other`), `showAtStart`, `rules` (below), `display`, and the
  last validation result's identity (`Version.txt`, file sizes) so a changed installation is noticed at start.
- the existing preference files above and ARC-UI-013's options store, as that task leaves them.
- the original's own `arcanum.cfg` in the game folder is **never written**: the installation stays read-only.
  Importing its values once on the first run is an option for a later Options task, not for the launcher.

**Location.** Godot's `user://`. A packaged build should set `application/config/use_custom_user_dir` with a project
name instead of the default `Godot/app_userdata/<project>` folder:

| OS | `user://` with custom user dir `<name>` |
| --- | --- |
| macOS | `~/Library/Application Support/<name>/` |
| Windows | `%APPDATA%\<name>\` |
| Linux / handheld | `~/.local/share/<name>/` |

**Collision to avoid.** The developer launcher already owns `~/Library/Application Support/Arcanum Steamwoven`
(`config-v1.json`, `build-registry-v1.json`). The player's folder must not be the same folder, or a packaged build on the
owner's Mac would sit inside the developer authority that `./arcanum clean` and the build registry manage. Either name
the game's folder differently (for example `Arcanum Steamwoven Game`) or move the developer home; the first touches no
existing tool. The developer runs keep the isolation of `ViewerStoragePaths` either way.

### Rule profiles

- **Built-in:** `steamwoven` and `original`, exactly today's `GameRules.Steamwoven` and `GameRules.Original`.
- **Custom:** a named profile = a base mode plus overrides of single switches:

  ```json
  {"schema":1,"active":"my-mix","custom":[{"name":"my-mix","base":"steamwoven",
    "overrides":{"Combat.InvocationModifierCountedTwice":true,"Magic.SaveSubtractsResistance":true}}]}
  ```

  An override names `<GameRules member>.<switch>`; an unknown name (a switch renamed or removed) is ignored and shown
  as "no longer exists", so an old file never blocks the start. A profile whose overrides all equal its base is that
  base.
- **What it needs in code** (a contract and session API change, owner's call):
  1. a **switch catalogue**: for every boolean of every profile its member, name, ledger ID, Original/Steamwoven text
     for players, generated from the profiles by reflection plus a text table, with a test that every switch has a
     text and every text a switch;
  2. `GameRules` able to carry a custom name: `GameRules.Custom(name, base, overrides)` building each domain profile with
     `with`, and the test "every profile carries the mode's name" relaxed to "the base's name, or the custom name";
  3. the session API: today `GameSessionDescriptor.Rules`, `GameSessionRestoreOptions.Rules` and
     `GameSessionOwner.SelectRules` take the `RuleMode` enum, `GameRules` has a private constructor and derives `Name`
     from `Mode`, and each profile's `FromName` knows only the two built-in names. They must accept a `GameRules` value
     (or a profile id resolved through a registry) instead of the enum; `Mode` of a custom mix stays its base mode,
     `Name` becomes the custom name, and `FromName` of a custom name fails as an unknown name (custom mixes are built
     from overrides, never looked up by name);
  4. a codec for the block above, extending `RuleModeSettingCodec` (schema 2) and still reading schema 1;
  5. traces, bug reports and smoke output name the profile and list its overrides, so a report from a custom mix is
     never read as Steamwoven or Original behaviour.
- The safety rules of the contract hold unchanged: profiles stay stateless, nothing is saved in snapshots, a switch
  takes effect at the next decision. A future network game must take the host's rules (FR-NET), which a named profile
  plus overrides makes easy to send.

## Finding the game folder

The launcher looks only at a short fixed list of candidates and at the folder the player chooses; it never scans a
disk (the same policy as `./arcanum setup`). Each candidate is checked with `ContentMountPlan.Discover`; a `Ready`
result is accepted, any other result is shown in words (for example "no `modules/Arcanum.dat` here"). The identity
check then reads `Version.txt` and, when present, the SHA-256 of `Arcanum.exe` (GOG 1.0.7.4:
`9bd80511f8663a41a25a20a701959e394e0065e4ffc68a8dc69fe19c23678d52`, [original executable check](original-executable-check.md));
another version is allowed with a warning, since only the GOG 1.0.7.4 data is verified. A `goggame-1207658829.info`
file marks a GOG install.

All facts in this section except the `Discover` check and the 1.0.7.4 hash are level F0: common layouts and public
reports (GOG product 1207658829 and its `goggame-<id>.info` marker, the registry key, default folders, wrapper layouts),
not checked on a machine. The packaging task checks the ones it supports.

**Windows 11 x64** (GOG ships Windows only):

1. Registry: `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1207658829`, value `path` (and, unverified, the same key
   without `WOW6432Node`). Read through `Microsoft.Win32.Registry` behind `OperatingSystem.IsWindows()`
   so the platform analyzer passes under `TreatWarningsAsErrors`.
2. Default folders: `C:\GOG Games\Arcanum` (offline installer), `C:\Program Files (x86)\GOG Galaxy\Games\Arcanum`
   (GOG Galaxy).
3. The player's choice.

**macOS** (no native build; players either copy or extract the Windows files, or run them in a Wine wrapper):

1. `/Applications/Arcanum` and `~/Applications/Arcanum` (a plain copied or extracted folder; the developer launcher's default is
   `/Applications/Arcanum`).
2. CrossOver bottles: `~/Library/Application Support/CrossOver/Bottles/*/drive_c/GOG Games/Arcanum` and
   `…/drive_c/Program Files (x86)/GOG Galaxy/Games/Arcanum`.
3. Wineskin wrappers chosen by the player: when the chosen path is an `.app`, look inside it for
   `Contents/SharedSupport/prefix/drive_c/…` and `Contents/Resources/drive_c/…` (the GOG installer installs under the
   wrapper's `drive_c`). Whisky bottles live in another app's container
   (`~/Library/Containers/com.isaacmarovitz.Whisky/…`); probing there would make macOS ask the player for access to
   another app's data, so it is only looked at when the player picks it.
4. The player's choice through the native dialog.

The simplest instruction (F0, to be tried by the packaging task) for macOS players is to extract the GOG offline installer with `innoextract` and
point the launcher at the extracted folder; the alpha's player instruction should say so.

## Packaging (input to the alpha's final task)

Godot export, signing and SmartScreen facts below are level F0 (Godot and platform documentation, not tried in this
project yet).

- **Godot 4.7.2 .NET export** for macOS (`.app`, universal or arm64) and Windows x64 (a folder with the `.exe`, the
  .NET runtime and the `.pck`), both exported on the owner's Mac through the run queue
  (`./arcanum run --kind export`). The game project needs a player-facing name, the custom user folder above, an icon
  and Windows/macOS export presets; the terrain viewer's presets are macOS-only today.
- **No original content in the package**: the build carries code only and reads the player's folder at run time; HD
  packs stay local (hard rules in [CLAUDE.md](../../CLAUDE.md)).
- **macOS signing**: Godot's built-in ad-hoc signing is enough to run, but an unnotarized app downloaded from the
  internet is blocked by Gatekeeper until the player allows it in System Settings; notarization needs a paid Apple
  Developer ID. **Windows**: an unsigned `.exe` shows a SmartScreen warning; signing needs a code-signing certificate.
- **Launcher first**: a packaged build cannot be given command-line inputs by the developer launcher, so the game-folder
  setting (task 1–2 below) must exist before the packaging task.

## Recommendation

1. Build the launcher as **a screen inside the game app** (option A), shown on the first run, when the game folder is
   invalid, and on request; it owns the game folder, the rule profile, display-at-start and visuals.
2. Keep the **original's options in the in-game Options window** (ARC-UI-013) and add there at most one rule-mode
   line that switches between the built-in modes, as D8 already allows.
3. Store the launcher's settings in **one new versioned JSON file** in a player folder that is not the developer
   launcher's home.
4. Ship **Steamwoven and Original first**; add **custom profiles** as a separate task after alpha, because they need the
   switch catalogue with a player text for each of the 72+ switches and a change to the rule modes contract.
5. Detect **GOG on Windows through the registry and default folders, on macOS through a few fixed folders**, always with
   "Choose folder…" and a typed validation; never write into the installation.

### Task breakdown (codes are taken from the coordinator when each task starts)

| # | Task | Type | Where | FR groups | Depends on |
| --- | --- | --- | --- | --- | --- |
| 1 | Launcher settings store and game-folder locator: `launcher_v1.json` codec (Godot-independent), OS folders, Windows registry and fixed candidates, `Discover` + identity check, typed results, synthetic tests | DEV | Mac (dotnet) | FR-CONTENT-001, FR-LIFECYCLE-004, FR-PRODUCT-005 | — |
| 2 | Launcher screen in the game app: first-run and invalid-folder flows, native folder dialog, rule mode (built-in), display at start, Play; the app reads the folder instead of command-line roles when started without them; launcher sync in `tools/local_dev` and docs | UI | Mac (Godot) | FR-LIFECYCLE-001, FR-UI-006, FR-LIFECYCLE-004 | 1 |
| 3 | Rule mode in play: wire the stored mode to session start and `SelectRules`, and the in-game Options line (ARC-UI-013 did not add one); close the "no settings menu" item of the rule modes contract. ARC-UI-014 added the Options line and the session wiring; the stored mode is left | UI | Mac | FR-PRODUCT-006, FR-UI-006 | 1, ARC-UI-013 |
| 4 | Switch catalogue and custom profiles: catalogue with player texts, `GameRules.Custom`, codec schema 2, contract change, launcher editor | WORLD + UI (two tasks) | Mac | FR-PRODUCT-006 | 2, owner's decision |
| 5 | Packaged builds (the alpha's final task): macOS `.app` and Windows x64 presets, player folder name, icon, signing level, smoke on both OSes, player instruction for GOG on macOS and Windows | DEV | Mac (+ a Windows 11 machine for the smoke) | FR-PRODUCT-005, FR-LIFECYCLE-001 | 1, 2 |

Task 1 is done as ARC-DEV-046 (the settings store, the locator and a viewer start without inputs from the stored
folder; [viewer start](../rendering/terrain-viewer.md#start-without-inputs-arc-dev-046)). Tasks 2 and 3 are done as
ARC-UI-014 ([launcher screen](../rendering/terrain-viewer.md#launcher-screen-arc-ui-014), the options window's Rules
line and the stored mode), except applying the display at start. Tasks 1, 2 and 5 are on the alpha's path; 3 is small and can join 2; 4 can wait until after alpha.

## Questions for the owner

1. **Form of the launcher.** A: a screen inside the game app before the main menu (recommended: one program, settings
   apply at once, nothing new to package); B: a separate Godot launcher app (two programs, double size); C: a separate
   native app (new toolchain).
2. **When the launcher shows.** A: on the first run, when the game folder is invalid, and on request (a held key or
   `--launcher`), with a checkbox to show it every time (recommended); B: on every start; C: only from a new button in
   the original main menu (changes the original menu).
3. **Custom profiles (per-switch mixes).** A: alpha ships only Steamwoven and Original, custom mixes after alpha
   (recommended); B: custom mixes in alpha too (needs the player text for all 72+ switches and a contract change).
4. **Rule mode inside the game.** A: the launcher chooses, and the in-game Options also switch Steamwoven/Original during
   play, as D8 allows (recommended); B: only the launcher chooses, the game shows the mode read-only.
5. **macOS signing for test builds.** A: ad-hoc signed, the instruction explains "Open anyway" in System Settings
   (recommended for alpha testers); B: Developer ID with notarization (needs a paid Apple Developer account).
