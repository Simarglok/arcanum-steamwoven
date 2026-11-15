# Terrain, static, actor and integrated world diagnostic viewer

`tools/terrain-viewer` is a standalone macOS Godot diagnostic viewer for exactly
one fixed Crash Site terrain sector. It is not a game renderer: 4,096 resolved
terrain records (3,033 tiles and 1,063 diagnostic facades) are terrain-only
coverage, not the full Crash Site and not original visual parity.

## Inputs and launch

`tools/arcanum-local` can retain caller-selected local input roles and prepare an
ordinary diagnostic launch without exposing those paths in repository data. Its
terrain actor defaults are explicitly labelled zero anchors/camera/offset,
`normal-uncached`, scale 100, shrunk false, Idle/facing 0/frame 0; these are
not inferred world state. See [local development](../workflow/local-development.md).

Use the `godot_canonical` function from the [shared toolchain](../workflow/toolchain.md), after restoring/building the viewer solution below. Launch the Godot project through the engine; its C# project is not a `dotnet run` executable.

With arguments the viewer has no picker, archive discovery, precedence, PATCH/dif/loose-file
policy, or extraction behavior (a start without arguments reads the stored game folder, [below](#start-without-inputs-arc-dev-046)).
It requires caller-selected inputs and a safe, non-path source label:

    godot_canonical --path tools/terrain-viewer/Godot -- \
      --mode terrain-only --module PATH --resource PATH --source-id LABEL

Omitting `--mode` is terrain-only compatibility. `--mode combined` requires
exactly one additional caller-selected `--prototypes PATH`; terrain-only rejects
that option. Combined is a finite project diagnostic: it validates the fixed
original-data profile, admits only source indexes 0 through 4, draws terrain
tiles then diagnostic facades then the five static commands sorted by
`(anchor.y, anchor.x, sourceIndex)`, and reports the separate 317/5/312 census.
It does not establish original object order, occlusion, facade/wall/roof,
lighting, palette, or overall visual parity.

`--mode actor-diagnostic` is a separate, mutually exclusive fixed-profile
diagnostic. It retains terrain but never adds the five static commands. It
requires the terrain `--resource`, a separate `--actor-resource`, `--prototypes`,
and every caller-selected actor field:
`--actor-map-x`, `--actor-map-y`, `--actor-camera-origin-x`,
`--actor-camera-origin-y`, `--actor-offset-x`, `--actor-offset-y`,
`--actor-render-branch normal-uncached`, `--actor-scale-percent 100`,
`--actor-shrunk false`, `--actor-action idle|walk`, `--actor-facing 0..7`, and
`--actor-frame`. These fields are diagnostic caller policy, not recovered
original state. Missing, duplicate, cached, scaled, shrunk, unknown, or
out-of-range values fail closed. The actor route uses only the bounded
source-derived normal branch and draws terrain, then actor, then its magenta
rectangle and yellow anchor cross (only in the run's debug mode, ARC-DEV-064, or
for a self-test raster oracle that asks for them). That is diagnostic layering,
not original occlusion or placement parity.

`--mode world-diagnostic` (ARC-WORLD-003 grid bridge) combines the same terrain, exactly
five admitted statics and the fixed-profile actor. It requires explicit
`--module`, `--resource`, `--actor-resource`, `--prototypes` (an authenticated
directory) and `--source-id`; it rejects all actor preview overrides. These
roles never imply archive precedence. See [WORLD policy](#integrated-world-diagnostic-policy).

The two resource paths have fixed non-interchangeable roles: `--resource` is
the authenticated terrain archive and `--actor-resource` is the authenticated
actor archive. The latter is required exactly once in actor and world diagnostic
modes; `--expect-actor-resource-sha256` is an optional additive
constraint. Terrain-only and combined reject both actor-resource options. The
viewer has no archive list, lookup precedence, or fallback: actor ordinals are
never attempted in the terrain archive, and terrain resolution never consults
the actor archive.

It validates the fixed Crash Site profile before and after its read: fixed
module/resource sizes and SHA-256 values, sector selector, all 4,096 outcomes,
zero issues, the 3033/1063 kind split, and the 457 recovered cells / 86
recovered lookup IDs. `--expect-module-sha256` and `--expect-resource-sha256`
are optional additional equality constraints, not replacements. Errors expose
only stable categories and aggregate counts; paths, entry names, exception text,
archive bytes, and sampled colours are deliberately not shown.

### Start without inputs (ARC-DEV-046)

Started with no viewer arguments (a player's build; `--native-smoke`, its screenshot option and `--launcher` alone also count), the
viewer reads the launcher settings, the `launcher` line of `user://arcanum.cfg` (the one settings file, [below](#the-one-settings-file-arc-ui-1130); research (deleted);
`LAUNCHER_SETTINGS_TEST_PATH` names another file, and an automated run without it has no file). It opens the stored
game folder while `GameFolderLocator.Check` finds it ready, else the first ready fixed candidate (Windows: the GOG
registry key `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1207658829` and its 64-bit twin, `C:\GOG Games\Arcanum`, the GOG
Galaxy folder; macOS: `/Applications/Arcanum`, `~/Applications/Arcanum`, the GOG folders of each CrossOver bottle) for
that start only. It writes nothing but the identity of a stored folder that had none: a detected folder, a changed
installation and a stored folder that is gone are left for the launcher screen to confirm, so the player's stored folder
is never replaced and each such start says so again. A folder is ready when the content stack mounts the
`Arcanum` module and the module archive, `arcanum1.dat` and `arcanum2.dat` are there; any other result is a typed
`GameFolderStatus` with the missing name. The start is map mode on `Arcanum1-024-fixed` with the main menu and the
folder's own inputs, the same as `./arcanum area` (`test_launcher_sync.py` keeps the two in step). The log prints
`ARC-DEV-046` lines: the settings read, each folder checked, the save and the folder used (GOG marker, verified
1.0.7.4 executable, changed identity, whether the launcher screen shows). Not built: applying the stored display at
start (window or fullscreen, monitor, UI scale; stored, not edited or applied); the candidate paths are F0 until a
packaging task checks them on Windows and in Wine wrappers.

### Launcher screen (ARC-UI-014)

Before the original main menu, a start without inputs shows the launcher screen (owner decision 2026-10-28,
research (deleted) option A, question 2 A) when
`LauncherScreen.Decide` gives a reason, in this order: on request (`--launcher`, alone or with `--native-smoke`; or
Shift pressed at the start or during the 0.75 s an ordinary start that would skip the screen waits for it, with the HUD
saying so; only while the game's window has the focus), the first run (no stored folder, also when the settings file is one this build cannot use), a stored folder
that is no longer a game folder, a changed installation (the stored identity differs) and "show at start". Without a
reason the start opens the stored folder at once. `--launcher` with other viewer arguments is a usage error. The developer launcher
(`./arcanum`) always passes its inputs, so its runs never show the screen; an exported build started with no arguments
(a double click) or `--launcher`, or a source run `godot_canonical --path tools/terrain-viewer/Godot -- --launcher`, does.

- **What it shows.** Why it opened; the settings file's state (none yet, read, damaged, a newer build's, unreadable);
  the game folder with its check in words (ready: GOG installation or not, version, the verified 1.0.7.4 executable or a
  warning that only that release is checked; otherwise what is missing, for example "no arcanum2.dat here"); a button
  for each other ready fixed candidate; "Choose folder…" (the system's folder dialog; `GameFolderLocator.Choose` also
  looks inside a chosen Wine wrapper `.app`, bottle or `drive_c`); the rules Steamwoven or Original (custom mixes
  after alpha); three HD lines (ARC-UI-143, below); "Show this screen at every start"; "Debug mode" (ARC-DEV-050, off by default:
  [debug mode](area-map-mode.md#debug-mode-arc-dev-050)); Play, Reset settings and Quit. The project's own look, not a
  window of the original's menu.
- **HD lines (ARC-UI-143, ARC-RENDER-2076).** After the rules the screen has three HD lines (the Game page of the options window had them until ARC-UI-146), one row of choices each: **HD graphics**
  Off/On (the visual profile, the `visual profile` line of `arcanum.cfg`; ARC-RENDER-2076: the relit night look, [relit look](area-lighting.md#relit-look-arc-render-2060), is a part of it,
  on exactly while HD graphics is On and not a line of its own, so HD graphics Off, the H key included, is Legacy byte for byte; a `relit look` line that an older file has is ignored and left where it is), **HD interface** Off/On/Generated (experimental) and **HD pack** Auto/Game/Custom
  (the `hd interface`, `generated interface`, `hd pack` and `hd pack path` lines of `arcanum.cfg`, [HD pack source](../workflow/hd-pack.md#hd-pack-source-arc-dev-350)).
  They are the only saved-settings screen for them (the options window has no HD line since ARC-UI-146; the `H` key and the palette still
  switch the visual profile in play, HD interface and HD pack change only here, with Shift held at the start): a choice is saved at once in the settings file (not at Play) and the game that Play
  starts reads them like any start. The pack source applies at the next start; the note under the rows says what that start will read (an automated run: that pack folders are not searched). Custom opens the
  system folder dialog, with the same fallback note where there is none. The rows are off while the screen is busy; **Reset settings**
  resets the `launcher` line only and leaves the HD lines (and every other setting) as they are. A run with no settings file (an automated run) keeps the choices
  for the screen only: its start reads the file and finds the defaults. A choice that could not be written is named under the rows. Checked by the launcher self-test (defaults, each choice saved in the files, shown again by the next screen, read by
  the start, Custom's folder, Reset) and by the launcher native smoke's HD lines and screenshot (`--native-smoke-screenshot PATH`
  also writes `NAME-launcher.png` beside it, the screen before Play, where `PATH` is `NAME.png`).
- **Install from GOG installer…** (ARC-DEV-074, [research](../workflow/packaging.md) option A) is for a player
  without Windows who has the GOG offline installer (`setup_arcanum_….exe`, Inno Setup 5.6.2, one file). It first looks for
  the free `innoextract` tool (zlib licence; `PATH`, then `/opt/homebrew/bin`, `/usr/local/bin`, `/opt/local/bin`,
  `/usr/bin`; `innoextract.exe` on `PATH` on Windows); without it the screen says how to get it (`brew install
  innoextract`) and nothing else happens. Then the system's file dialog picks the installer and the folder dialog a new or
  empty folder (a folder with anything in it is refused, so the installer's own folder is never a target; the installer file is never changed). `GogInstallerUnpacker`
  (Godot-free, `src/Arcanum.Session/Launcher`) runs the tool as a child process, arguments as a list and never a shell, off the main
  thread: the installer must name GOG game id 1207658829 (`--gog-game-id`); the listing (`--list --list-sizes --gog
  --exclude-temp`) gives the files and the bytes, and the free space (listed size plus 64 MB) is checked on the target's volume;
  `--test` verifies the installer's checksums before anything is written; then the files are written (`--extract --gog
  --exclude-temp --output-dir`); then `GameFolderLocator.Check` must find a ready game folder whose `Arcanum.exe` in the root (not
  `app/`) has the verified 1.0.7.4 fingerprint. The screen shows the stage with files and megabytes done (the folder, Play and Reset
  are off while it runs; "Cancel unpacking" and Quit stop the tool) and the tool's lines go to the run log (`ARC-DEV-074 installer
  unpack …`). On success the folder is chosen as if the player had picked it (Play stores it, with the GOG marker in its identity);
  every failure (not Arcanum's installer, missing `.bin` parts, a failed checksum, not enough space, a tool error with its first
  error line, the wrong executable, a cancel) is named in words and leaves the chosen folder as it was; files a failed or cancelled
  run wrote stay in the target (the player empties it or picks another). It builds no prototypes: they come at the first Play
  (ARC-DEV-069, next bullet).
- **The game data at Play (ARC-DEV-069, [research](../research/prototype-extraction.md#e4-results-arc-dev-069)).** Before the game
  starts on a folder (Play, or the start from a stored folder) `PrototypeProvisioner` checks that the folder has its prototype
  files: an installation the original ran has them (nothing is done); otherwise the files are built once from the player's own
  verified `Arcanum.exe` by the E3 builder, off the main thread, and kept in `prototypes/` beside `arcanum.cfg`, keyed by the
  executable and verified against the data they were built from at every start. The screen is busy like during an unpack (the folder,
  Play and Reset are off, the line shows `Preparing the game data from your Arcanum.exe (first start only, about half a minute)… n/1423`,
  Cancel and Quit stop it); a stored folder's start shows the same line on the status line. Success mounts the cache for every reader
  (`PrototypeCacheMount`, a base source above the archives and under `data/`) and starts the game; a failure (an executable that is not
  the 1.0.7.4 one, no `Arcanum.exe`, a build fault, a settings folder that cannot be written, a cancel) is named in words, leaves
  the screen open (opened for a stored folder) and keeps Play offered. The screen never tells the player to run the original. Log lines:
  `ARC-DEV-069 prototypes stage=… / status=… files=… mapFiles=… cache=… detail=…`. ARC-DEV-082: an installation whose module has no aggregate files (`modules/<module>/maps/<name>`, which the original writes at its first start) also gets them built the same way (stage `BuildingMaps`, line `Preparing the maps from your game files (first start only)… n/81`, kept in the same cache under `maps/`; [E5 results](../research/prototype-extraction.md#e5-results-arc-dev-082)).
- **Play** is offered only for a ready folder. It stores the folder with its source and identity (the start leaves a
  detected folder and a changed installation unwritten, so Play is where they are confirmed), the rule mode, "show
  at start" and the debug mode (`debugMode`, which turns the run's debug mode on, as `--debug-mode` does), then starts map mode on the main map with the main menu from that folder, as a start without the screen
  does. A save the file refuses (a newer build's or an unreadable file) does not stop the start: the choices apply to
  it and the file is kept.
- **Reset settings** writes the defaults over the file (`LauncherSettingsFile.Reset`, the one write that replaces a
  newer build's or an unreadable file); the shown folder stays, for Play to store again.
- **Rule mode.** Every ordinary run, with inputs or without, starts in `rules.active` of the `launcher` line of `arcanum.cfg`
  (`LauncherSettings.Rules`); the options window's Rules line writes it there through the same store (one owner per
  file), reading the file again and changing only `rules.active`, so a folder another run stored meanwhile is kept. An
  automated run without `LAUNCHER_SETTINGS_TEST_PATH` has no file and starts in Steamwoven. A start that fails after the
  screen (an unexpected error) shows `failure stage=GameFolder code=StartFailed` and a native smoke exits with 1.

Self-test `ARC-UI-014 self-test PASS synthetic launcher` (synthetic folders, a temporary settings file, a stand-in for
the start): the first run without a folder and Play off, a non-game folder named in words, Play storing the chosen
folder with its identity, Original and "show at start", that setting reopening the screen, a stored folder going
straight to the start, a request opening it, the Rules line writing the file, Play never overwriting a newer build's
file and Reset replacing it, and (ARC-DEV-074) the install button over a stand-in tool: no tool named in words, the
folder and Play off while it runs, the unpacked folder chosen and started by Play, a used folder refused, a cancel named, and (ARC-DEV-069)
the game data prepared on Play over a stand-in: the progress and Cancel on the screen, a cancel and a refusal named, a prepared folder starting with its
cache mounted, a stored folder opening the screen on a failure.
The real tool runs only in the opt-in `The_real_installer_unpacks_into_the_verified_game_folder` test
([opt-in data](../workflow/toolchain.md#opt-in-original-data)). Native smoke on the original data: `LAUNCHER_SETTINGS_TEST_PATH=FILE
ARC_AREA_MAP_SMOKE_ONLY=menu` and `godot_canonical --path tools/terrain-viewer/Godot -- --native-smoke` (a first run
with `FILE` absent, or with `--launcher`) prints `ARC-UI-014 native-smoke launcher PASS` (the detected or stored folder
ready, an empty folder refused, Original chosen, Play storing it and starting under Original), then
`ARC-UI-011 native-smoke main menu PASS`; with `FILE` holding a stored folder and no `--launcher` the log says
`launcher reason=None` and the menu smoke runs at once. Not done: applying the display at start, keyboard and
controller use of the screen (mouse only), the candidate paths and the folder dialog on Windows and with wrappers (F0).

ARC-DEV-047: the launcher and the main menu show the build in their bottom right corner (`ViewerBuildInfo`):
`Arcanum Steamwoven alpha · main <commit> · <date>` in a package of `./arcanum package`, `not a packaged build`
otherwise; every run prints `ARC-DEV-047 build <line> user=<user folder>` at start. A package runs under the
`steamwoven_package` feature: player name `Arcanum Steamwoven` and its own user folder `Arcanum Steamwoven Game`
([packaging](../workflow/packaging.md)).

## Diagnostic rendering contract

Core is Godot-independent. Each coordinator request creates one caller-owned,
idempotently disposable archive operation owning two seek-based `DatArchive`
objects normally and, in actor/world modes, the separately authenticated third
actor `DatArchive`. The coordinator serializes inspection, selected-frame decoding,
pre/post fingerprints, and per-load success/failure disposal before releasing
its gate, and records maximum observed archive concurrency.
It atomically publishes only after all records and deduplicated selected frames
succeed. The exact selected-frame identity is the source label, DAT ordinal,
flags, declared size, normalized logical-name hash, group, frame, and requested
palette.
The cache preflights terrain frames against 4,096 identities, static frames
against 5 identities (4,101 combined total), or actor frames against exactly
184 identities for the monster profile (the human AREA profile `HumanFixedArea` 195 since ARC-COMBAT-005; caps 195, 4,291 actor-mode total, 4,296 world-mode total; each profile's exact key count is still enforced by its load), and the unchanged 256 MiB
of packed RGBA before retaining it. Camera actions never access archives or grow
that cache. After successful upload, the app retains only the safe source label
and scalar decoded-count/load-time HUD metrics: neither the completed load
result nor its decoded RGBA dictionary is scene-lifetime app state. The
scene-lifetime texture dictionary is the sole cache. Reload, failure, and exit
clear textures exactly once. The graphical self-test injects an upload failure,
tracks per-texture reload/failure/exit disposal, and deterministically inspects
the app field types to prove they cannot retain decoded texture data; it makes
no nondeterministic GC claim. Focused barrier tests prove per-load archive
lifetime, serialization, and delayed stale success/failure cannot mutate a
newer scene or HUD.
Actor archive opening is a separate disposable production operation. It maps
only DAT-format/limit hierarchy failures, I/O hierarchy failures, and access
denials at that boundary to the path-free actor fingerprint category; caller
cancellation and unrelated programmer failures remain control flow/errors.

`TerrainGeometry.Place` is the only owner of projection, the one-pixel X anchor,
tile 78x40 / facade 80x40 footprint, crop, and raw tile flip. The adapter draws
only its returned integer source and destination rectangles with nearest,
straight RGBA8 and ordinary alpha blending. There is no Y flip, inferred facade
flip, ART hotspot/offset application, object-suffix interpretation, lighting,
shadows, grading, mipmaps, or HD treatment. Palette index zero remains
transparent. Paint order is explicitly **Editor diagnostic order**: tiles before
facades, then local Y, local X, and ordinal; later commands win. That is a
project diagnostic policy, not a claim about original facade/scenery occlusion.

## Controls, window behavior, and diagnostic HUD

The world canvas is viewport-independent under `Camera2D`; `TerrainDiagnosticHud`
is a separate top-anchored `CanvasLayer` with 16px margins. Its sticky summary
always says `DIAGNOSTIC — not gameplay/original parity` (and self-tests visibly
say `AUTOMATED TEST MODE`); wrapped WordSmart detail scrolls within a sensible
110–360px/~35%-viewport bound. The ready HUD displays diagnostic mode,
aggregate counts, safe source label, current bounded zoom, decoded count,
explicit cache count, and load milliseconds; it refreshes after pan, zoom,
fit/reset, and resize. The initial view fits the complete diagnostic bounds
with 32-pixel margins. Zoom is bounded to 0.125–8.0 with nearest 1/1024 fit
rounding. Arrow/WASD pans, wheel and `+`/`-` zoom, and left drag pans. `R`
resets initial Fit, `F` recomputes Fit, and `L` reloads through the same
generation-aware load route. Resize changes HUD layout only; the camera's
current Fit or manual pan/zoom transform survives resize. F11 toggles normal fullscreen and
Escape restores windowed mode. The resizable default is 1280x800. These are the standalone-mode controls; WORLD has the distinct controls below.
A responsive synthetic HUD matrix checks containment,
sticky limitations, wrapped/scrollable detail and content at 480x320, 640x360,
800x500, and 1280x800; resizing affects HUD layout only, not world state.

In `area` mode, the original left click on an NPC whose effective SAP_DIALOG
attachment names a script talks to it (ARC-SCRIPT-005): since ARC-DEV-169 the click orders the hero's goal 21 (`ag_talk`), which
talks from less than 5 cells at once and otherwise walks the hero next to the NPC first (the original's range, 0x42AE10; no project reach). The modal
conversation panel closes the actor gate; replies are clicked (controller menu navigates and
confirms), keyboard keys stay reserved and are not redirected, and a diagnostic Leave button
closes the dialog. Since ARC-SCRIPT-009 the talk request and the script's continuation after a
"return to script" end run through the [SCR dispatcher](../scripting/scr-dispatcher-runtime.md#dialogue-arc-script-009-slice-s3):
a further dialog action opens the next conversation, anything else is a HUD notice. The HUD says whether a dialog NPC is in reach and what the dialogs left in
the game session (quests, rumors, followers; ARC-SCRIPT-006). Rules, state profile and
limits are in the [conversation runtime](../scripting/dialog-conversation-runtime.md); the
self-test covers a synthetic panel, an off-NPC click, keyboard events and polled levels, and the
palette guard in AREA mode (the positive NPC hit test is an opt-in original-data test). ARC-UI-071 (B29): the panel's texts (header, NPC
line, replies, outcome, effects, hint, Leave) are drawn in the original's font art 229 with the MW31 blend (`AreaArtParagraph`, broken at the
panel's content width by `AreaTextFontLayout.Place`) once the viewer has read the text fonts (ARC-DEV-156), and in the viewer's font
without them; the self-test also covers the art-text mode on synthetic glyphs and the `wreck` smoke checks it on the installed fonts. The reply band of the original's own talk window is built with font art 229 too
(0x4C9280, F1), the one font of the dialog box and the message window; the diagnostic panel is not the band and keeps its own look.
ARC-UI-2019 (a Steamwoven addition, owner request; switch `InterfaceRuleProfile.DialogRepliesUnnumbered`, [rule modes](../world/rule-modes.md)): under the Steamwoven
setting each reply line carries its number (`1. …`, up to 5) and the digit keys 1–5 (row or keypad, no modifier) choose the reply with that number while the
panel awaits a choice; a blocked or absent number, a finished dialog and every other key do nothing. Under the Original setting the reply is drawn as its text
alone (the original shows no number) and no digit chooses anything. The self-test covers both settings on the panel and the digit routing in the app.

ARC-UI-2025 (ledger DH1–DH7; rule `Arcanum.Presentation.Hud.DialogReplyBand`, switch `InterfaceRuleProfile.DialogReplyRowsDrift`, [rule modes](../world/rule-modes.md)): in both
settings a reply is chosen on the mouse press, not on the release, as in the original. Under the Original setting `AreaConversationPanel.OriginalReplyRows` lays the
reply stack out as the original's band (100 px tall, five 16 px rows every 19 px starting 3 px down) and resolves a left press by the band's rules:
the row is y / 20 (so a press in a gap picks a row and the fifth row's first pixel picks the fourth), an empty slot, the release and a blocked reply choose
nothing; the hover lights a row as the original's move records do (DH5) and, ARC-UI-2052, again after every rebuild of the rows against the live pointer (DH9). The release that follows a closing press reaches the viewer with no panel under it and sets no route and opens nothing (DH8, DH10; the routing self-test pushes it; the windows a reply opens take it in both settings, ARC-UI-2071 (no switch: the original's design, not an exe bug): the viewer keeps the release of a press that began while the dialog was open and hands it to the identify or repair window (an item under it is quoted as a click), the round view of the identify and repair windows, the NPC sheet and the schematic window (they close), DH10, SW12; only their original look takes it, the diagnostic lists do not). Under the Steamwoven setting each button is its own hit area. The self-test (`TerrainViewerConversationTest`) covers the press mode and the band's refusals.

ARC-UI-2022 (ledger DH11–DH14): under the Original setting the reply stack sits in `AreaConversationPanel`'s band over the game view, not in the framed panel: a 100 px tall strip, 400 px wide
(a row's text plus 13 px that is wider widens it by that and re-centres it; the press x-bound is the band's width), centred, its top at 299 in a 400 px view else the view height minus 181, the view being the part the bars leave free (the original's iso window), so the band lies above the bottom bar. The strip darkens what is under it by a subtract blend of (18, 18, 18), as the original's fill (a saturating subtract, not an alpha mix). Rows
are white, the hovered row yellow (the original's two fonts of art 229), each with a drop shadow one pixel right and down in (10, 10, 10); the text is broken at the box width minus 12 and only the first line fits the
16 px row; a word wider than the row draws nothing. The diagnostic chrome (header, outcome, effects, hint, Leave) is hidden under the Original setting by ARC-UI-2023; the framed panel then holds nothing and is not drawn, so the band alone is on screen (with the chrome shown, as in Steamwoven or for a message-only panel, the panel stands above the band). The self-test covers the rectangle,
the shade, the colours, the shadow and the press bound. ARC-UI-2083 (DH13, DH15): the `wreck` smoke stage, on the installed font art, lays out a reply of 388 px (the widest line the original's break at 388 allows) after and before short replies and requires the band 401 px wide and centred, not shrinking within the set and back at 400 for the next set, and checks as a sanity test that the reply stack holds at most the band's five slots (the source builder keeps five, DH15; its cap is covered by the Scripting tests).

## Integrated world diagnostic policy

Merged [PR #44](https://github.com/Simarglok/arcanum-steamwoven/pull/44)
replaces WORLD001's free diagnostic integrator with WORLD003; historical parent
artifacts retain their original behavior. There
is no free-movement mode or admission-failure fallback. This is a bounded
original-data admission diagnostic, **not** gameplay or original-executable
collision parity. It uses the existing [headless movement runtime](../world/grid-movement-runtime.md)
and [cardinal Data assessment](../world/cardinal-traversal-assessment.md), never
Godot physics, navigation or a second frame-based simulator.

The fixed sector is `(1452,1290)`, admitted zone `x=4..8,y=0..3`, under declared
isolated-PC/current-CRITTER-ART/non-polymorph context. Diagnostic spawn/reset is
`(6,1)`, facing map `XPlus`; this is **not** recovered original placement.
Each new runtime-generated opportunity gets an exact-key assessment from the
owned snapshot. Only Supported/Open permits a start. Blocked, Unknown and
Unsupported leave the settled cell unchanged and show their concrete reason;
the full contributor/gap evidence is retained outside the bounded permit codes.
The adapter replays the earliest missing assessment against the same immutable
previous state, then publishes one final result; invalid/capacity failures are
atomic, not partial movement.

Live adjacent duration is the explicit positive rational **1/2 second** per
cell, with entry at **1/4 second**. It is project diagnostic policy, independent
of ART FPS, analog magnitude, camera delta and original movement speed. Neutral
before entry smoothly returns to the committed origin; equality or later
commits/finishes the destination. Turning while accepted execution is active
does not rotate that segment or queue intermediate turns. Returning and
post-entry finish remain Walk under neutral; only real center arrival selects
Idle. Held rejected intent does not repeatedly create opportunities.

The HUD separates exact committed cell, rational continuous anchor, phase,
clock, step/opportunity IDs and assessment. Full exact admission/input keys are
also retained in numeric debug receipts. Projection remains
`(40*(Y-X),20*(Y+X))`; rendering-only rounding cannot commit a cell. Actor
callbacks use monotonic host microseconds converted to rational seconds, one
ordered complete batch per timestamp. The newest tick remains open until a
strictly newer observation proves it complete; no late-input retagging.

The rendering catalogue retains project diagnostic octants, not original
compass semantics. WORLD uses the bounded eight-sector screen mapping below and
the closed `HumanFixed` profile:
single-layer fixed type2/body0 female underwear0, palette0, mapping-only (no
module/NPC/player appearance association). Idle has one frame at raw 6 FPS;
Walk has ten frames at raw 17 FPS. These positive ART rates drive the diagnostic
clock, not an original animation scheduler or movement-speed claim. The legacy
standalone `actor-diagnostic` keeps `MonsterRegression`, 15/8 frames and the
6/10 FPS override (raw 8/15). Both ratified action facing tables use physical
groups `[0,7,6,5,4,5,6,7]` and mirror only slots 1..3 (the AREA run ART of ARC-WORLD-020 reuses them, not
independently verified). Action changes reset frame/interval;
same-action facing changes preserve the interval. Runtime motion events and
phase determine facing, including reversed facing on early cancellation;
denial does not rotate the actor. Cell floor and ToEven fractional projected pixel offsets
bridge to the checked source-derived normal placement; camera origin stays 0.
Follow targets continuous projected baseline plus `(0,20)`, not rounded sprite
position (in `world-diagnostic`; `area` uses `(40,20)` since
[ARC-RENDER-011](#actor-anchor-and-wall-fade-arc-render-011)). No sprite repositioning/visual nudge substitutes for source geometry.

Since ARC-INPUT-001 the WORLD keyboard has no actor route (see
[diagnostic palette](../input/diagnostic-palette.md)); the left stick requests
**screen sectors** (the table keeps the former W/A/S/D names as sector labels). The reducer applies circular deadzone
and linear sensitivity once; WORLD then applies separate actor X/Y inversion
(defaults false/false) once. Retain each nonzero component whose magnitude is at
least half the largest (inclusive ties), then map the screen sector to one
neighbor. Camera inversion remains independent and authored v1 camera settings
are preserved when migrating files without actor fields. Sensitivity affects
magnitude, not the fixed diagnostic half-second step duration. The literal mapping follows
`(40*(Y-X),20*(Y+X))`, not a compass alias or original-executable claim:

| Sector (former key) / stick | Map direction | Unzoomed projected cell delta | Human octant / physical group / flip |
| --- | --- | --- | --- |
| W / stick N | XMinusYMinus | `(0,-40)` | `0 / 0 / no` |
| W+D / stick NE | XMinus | `(40,-20)` | `1 / 7 / yes` |
| D / stick E | XMinusYPlus | `(80,0)` | `2 / 6 / yes` |
| S+D / stick SE | YPlus | `(40,20)` | `3 / 5 / yes` |
| S / stick S | XPlusYPlus | `(0,40)` | `4 / 4 / no` |
| S+A / stick SW | XPlus | `(-40,20)` | `5 / 5 / no` |
| A / stick W | XPlusYMinus | `(-80,0)` | `6 / 6 / no` |
| W+A / stick NW | YMinus | `(-40,-20)` | `7 / 7 / no` |

From spawn, S+A admits `(6,1)->(7,1)`; release/finish and W+D return to `(6,1)`.
W+D at spawn requests `(5,1)` and is blocked by the already admitted scenery
index 1 (one of the five, no extra/invented blocker). Its diagnostic anchor is
`(-163,134)`, rectangle `(-276,-128,204,267)`. Destination evidence is
asymmetric; general occupancy/reachability/occlusion are not inferred.

WORLD opts into `BASE_SECTOR_1452_1290_ADJACENT_V1`, still only x4..8/y0..3.
Data's default `CardinalOnly` policy remains unchanged. Straight screen axes are
map diagonals: final terrain is assessed once, with four role-labelled object
constraints for both shoulders and both final legs. No shoulder terrain, route
search, four runtime permits, staircase or corner cutting is inferred. At spawn,
W and D legitimately deny because the XMinus shoulder is the existing `(5,1)`
blocker. S can reach `(7,2)` where all eight directions have original-data positive
coverage; holding still renews each admitted segment and stops honestly at the
bounded zone or obstacle. Native callback/frame and original-pixel tests cover
all eight sectors, four actor inversion states, keyboard/stick simulation and
three actual viewports. They do not replace required ordinary physical observation
or independent review of the frozen candidate.

Arrows/right stick pans the world image and
immediately selects Manual, even at zero dt or a clamped camera. Simultaneous
actor/camera intent remains independent. Releasing pan while actor remains
held stays Manual; both domains neutral arms return without snapping; the next
actor-only activation selects Follow. Pan cancels stale return. Held opposing
keys count as activity even when their net vector is zero. The double camera
center clamps to a stable envelope of terrain, five statics and **all** actor
corner/frame/mirror extents, including rounding guard; small maps center at
their midpoint. Movement never changes the envelope/cache or reads archives.

Arrows keep one fixed pan direction whatever the camera inversion (KM-05);
inversion applies to the right stick. LMB, drag, wheel and `+`/`-` have no world
effect (ARC-INPUT-001). HUD/palette consumes pointer input;
UI keyboard focus blocks world input. Resize clamps the center to viewport
bounds but never derives actor state from `Camera2D`. Palette **Fit** fits and remains
Manual/unarmed; **Reset scene** requests explicit safe scene/Follow reset without resetting
preferences; **Reload current source** requests generation-aware reload. Both enqueue neutral and
wait for accepted return/finish to reach Settled before replacing the session.
The successor has a fresh input queue/sequence and snapshot epoch binding;
old pending lifecycle flags cannot execute in it. Missing reload arguments
fail closed, never fall back to reset. Failure/exit disposes the session,
distinct from (and without manufacturing) CenterArrived. F11 is an ordinary
window control; Escape is the reserved original game-menu key. Focus loss/gain, modal transitions and active disconnect/acquisition
enqueue timestamped neutral and quarantine **new input**, not accepted motion. Inactive
disconnect preserves scene. Rearm requires two complete observed-neutral frame
samples of every mapped keyboard and active-controller level; cached/event or
opposing-key cancellation is not neutral proof. Live sampling uses pinned
Godot APIs, not simulated levels. An incomplete, missing-active-controller or
invalid observation quarantines **before** reducer timing or UI actions:
cleared drag/navigation and Manual-return arm, with bounded camera center
preserved. Already accepted return/finish and its Walk clock continue under the
closed gate. Held input on recovery cannot
substitute for those two complete neutral frames.

Ctrl+Shift+F9, Start or the HUD button opens the bounded diagnostic palette
([contract](../input/diagnostic-palette.md)): Fit, Reset scene, Reload, exact zoom,
the read-only [passability overlay](passability-overlay.md) checkbox (ARC-RENDER-008), independent camera X/Y and
actor screen X/Y inversion, independent actor/camera deadzones and sensitivities,
Reset preferences (scene kept) and close.
WORLD first launch and Reset use **false/false**: stick right/down moves the
camera right/down (world image left/up). True reverses only the selected
camera axis; actor and menu directions are unchanged. Explicit saved values,
including true/true and both mixed combinations, survive reload. The standalone
lab retains its historical true/true default. Common engine-free v1 settings
codec supports the host fallback, but WORLD stores only
`user://world_diagnostic_input_v1.json`, never the lab file. Missing, unreadable,
corrupt or unsupported settings fall back to WORLD defaults; isolated automated
restart tests do not overwrite ordinary preferences, and since ARC-DEV-032 no automated run reads or writes
them ([automated runs](#automated-runs-and-the-players-settings-arc-dev-032)).
Modal digital navigation is single-press edge driven, not render/event-rate
repeat; held keys, D-pad, echo and unrelated mouse events do not advance it.
Focused numeric editors retain native digit/edit/Enter/Tab input. Mouse clicks
belong to native GUI dispatch, not semantic Confirm on the previously focused
control. Keyboard/controller activation and navigation are consumed once,
while new movement and Follow activation are gated (accepted motion continues).
The bounded scrollable panel uses an opaque background; bounded-grid/context/parity limitations remain
visible in a fixed header even when the settings rows scroll.

Paint is terrain → diagnostic facades → five sorted statics → actor → guides
→ HUD. The persistent label says bounded adjacent grid, original Data admission,
not gameplay and original parity NOT VERIFIED, including loading/failure/modal.
Shared `src/Arcanum.Input` owns project input/Follow/Vec2; historical
`GamepadLab.Core` names and type forwarding are compatibility only. The viewer
does not reference the experiment assembly, targeting or obstacle algorithms.

WORLD self-tests exercise the actual integrated app with public synthetic
pixels, viewport-dispatched modal/numeric and native checkbox press/release
events (all four axis combinations, one persisted change per click), native
keyboard/controller activation and Reset, and complete-level
**SIMULATION** lifecycle frames, including incomplete-observation recovery.
Engine-free wire fixtures execute the production integrated WORLD load with
real DAT/ART/sector/static-chain parsers under a narrow identity authenticator:
per-domain/union/exact-byte retention limits, static-to-actor and partial-actor
cancellation, all-role post-load mutation, stale success/failure, serialized
disposal and exit drain. These are not original payload or physical-device
observations. Non-headless settings checks independently read literal opaque
background pixels over two contrasting underlays and rendered header text at
actual 480x320, 800x600, 1280x800 and 1440x900 viewports. Isolated process-restart
checks cover missing-file first launch, each explicit axis combination and
Reset. Headless containment checks do not establish opacity. Original graphical
native checks separately authenticate immutable module/terrain/actor/prototype
roles, terrain 4096/3033/1063 with zero issues and normalization 457/86,
static census 317/5/312, all 55 HumanFixed texture keys (457676 RGBA bytes),
264 catalogue selections, actual grid early/equality/late/open-reverse/blocker
traces and phase-specific actor pixels with hidden-actor/static controls,
requested=actual 1280x800,1440x900,800x600 viewports and texture disposal.
Headless checks are structural, not pixels. Source, exported original checks,
isolated settings restart and later ordinary unchanged-artifact observation
are separate evidence. Physical Xbox/Bluetooth comfort and original placement,
movement, occlusion/collision parity remain NOT READY/NOT VERIFIED; historical
lab/five-static owner observations do not verify this integration.

After source corrections and a clean committed candidate, use the closed
WORLD endpoint (not the historical manual recipe below):

    ARCANUM_LOCAL_HOME=OWNED_CONFIG ARCANUM_LOCAL_ROOT_ID=WORLD_ROOT_ID \
      ARCANUM_TERRAIN_EXPORT_TASK=ARC-WORLD-003 \
      tools/terrain-viewer/Godot/tools/validate-macos.sh

Configure WORLD separately at ignored `artifacts/world-builds`; keep legacy
roots/pointers. Use effective TMPDIR and a verified same-tool complete payload
estimate per [toolchain](../workflow/toolchain.md). The endpoint leases one
final head through gates/export/codesign/observation freeze/v2 finalization and
success-only registration. Select `WORLD_ROOT_ID:ARC-WORLD-003:<head>` and run
`tools/arcanum-local launch terrain --mode world-diagnostic` for ordinary use.
Default mode remains terrain-only. No test/capture/input/fullscreen automation
belongs in ordinary launch. Later observation logs/digest bindings stay outside
sealed evidence; never edit a finalized package or rerun an unchanged endpoint.

WORLD003 retains all sixteen parent terrain/WORLD/GPAD/Human gates and adds
source/exported `grid-original` structured reports. Both manifest writing and
registration require eighteen PASS actual-exit receipts bound to task, full
head, timestamp and log digest; registration also requires the gate logs and
receipts in the immutable evidence inventory. Independent private original
pixel reference stays local/ignored, never in the app, manifest or PR.

The production bridge/readback tests live in
[Core tests](../../tests/Arcanum.TerrainViewer.Tests/DiagnosticGridSessionTests.cs),
[original bridge](../../tests/Arcanum.TerrainViewer.Tests/OriginalGridBridgeTests.cs),
[grid notifications](../../tools/terrain-viewer/Godot/TerrainViewerWorldGridTest.cs),
[retirement/ownership](../../tools/terrain-viewer/Godot/TerrainViewerWorldRetirementTest.cs)
and [original graphical readback](../../tools/terrain-viewer/Godot/TerrainViewerWorldOriginalGridValidation.cs).
Native automated readback is software evidence, not a physical Xbox comfort
measurement or ordinary-launch observation. A source pass is not an exported
artifact, independent review, publication or merge claim.

### Whole interior map (ARC-RENDER-017)

`area` with `--game-root GAME --area-map NAME` (no `--prototypes` or Crash
Site inputs) shows one whole interior of the game folder's module content stack
instead of the pinned exterior; see [map mode](area-map-mode.md). Since ARC-RENDER-024 the main world map opens there
as a window that moves with the actor (`--area-map-sector X,Y` picks where it opens;
[the main world map](area-map-mode.md#the-main-world-map-arc-render-024)).

### Crash Site cave and return (ARC-WORLD-009)

`area` takes an optional `--area-cave-mobile PATH`, the cave's
game-generated object file (6,698 bytes, next to `areaMobile`; see the
[cave research](../research/cave-transition-evidence.md)). Without it the mode is
unchanged and the HUD says the cave is not loaded. With it the load also builds the
cave under its pinned profile (`OpenCaveFiles`), its scene plan on its own chart
(origin of sector (32,31), start at the entrance's authored arrival (2087,2018))
and its step evidence with wall edges.

- **Use.** A left press whose [hover pick](#hover-tooltip-and-action-cursor-arc-ui-005) is the
  entrance or exit object is the original use action. It arrives through the ground-click path
  (`_UnhandledInput`, so a HUD or palette click never reaches it) under the same
  focus, modal, safety-gate and reset/reload guards as a click-to-move goal. `AreaTransitionCoordinator` accepts it
  within Chebyshev distance 2 of the object when the object's tile is not
  terrain-blocking and one step towards it is not blocked by anything but the
  object; otherwise the HUD says why. A click from farther away sets the
  click-to-move route (ARC-WORLD-007) to the nearest standable cell within range,
  as the source's use goal walks near first, and requests the same use once the
  route has arrived and the actor is settled. A ground click or a stick step
  cancels that walk-to-use at once; a quarantine (palette, focus loss) stops its
  route, and the use is dropped when the actor settles. Other clicks keep their routing.
- **Transfer.** An accepted use stops any click route and is queued; it is applied
  once, when the actor is settled (a started step ends at its cell centre). While
  it is pending the stick starts no step, ground clicks are ignored and a second
  use is refused. It waits while the palette is open; a reset or reload drops it. The destination must be loaded and
  the arrival admitted, or the use is refused as unavailable. The whole area scene,
  step evidence and chart are swapped, the actor starts on the authored arrival
  cell with a fresh authoritative session and input queue, and the camera centres
  on it before the scene is published. Reset in the cave returns the actor to the
  cave arrival cell; the session map stays the cave. The exit leads back to (92913,82598), three cells east of the entrance.
- **Session.** A `GameSessionOwner` (ARC-WORLD-008) is created per load, with or
  without the cave, with the area lighting clock profile, a project module id (the
  first 16 bytes of the module SHA-256, because the object file's GUID is empty) and the
  original new-game GF domain (0..3199 known false, ARC-SCRIPT-006). Dialogs keep their
  consequences in it ([conversation runtime](../scripting/dialog-conversation-runtime.md#consequences-in-the-game-session-arc-script-006)),
  so they hold after the cave trip. Each transfer records the actor's map and
  arrival cell; the HUD shows it. Since ARC-DEV-113 the transfer is the session's
  [change of place](../architecture/change-of-place.md#3-what-the-session-does-s2)
  (a use script's teleport, flags 0): the session writes the actor, the companions
  and the move list in one revision, and the open switches the coordinator's map. Both maps stay loaded, so the
  exterior is exactly as it was on return. The session clock does not drive the
  day/night clock yet.
- **Cave view.** The cave is lit by its own map-default ambient scheme and its four stationary sector lights ([ARC-RENDER-012](area-lighting.md#cave-ambient-and-stationary-sector-lights-arc-render-012)); walls are drawn with the wall hotspot rule
  and fade in front of the actor by the original wall check
  ([ARC-RENDER-011](#actor-anchor-and-wall-fade-arc-render-011)).

The native smoke (`--native-smoke` with the cave input) adds a transition phase: it
stands the actor next to each trigger, clicks an opaque trigger pixel outside the
HUD through `_UnhandledInput` (simulated focus and test time, as the route matrix),
lets a frame apply the transfer and checks the arrival cell and the
session, then clicks the entrance from the return cell three cells away, checks that a
ground click cancels that walk-to-use, clicks again and lets the route walk there
before the use, then in the cave accepts a use and resets before the next frame,
checking that the reset drops the transfer; it prints `ARC-WORLD-009 native-smoke transition PASS`.
A clock phase follows (ARC-WORLD-010, [area lighting](area-lighting.md#verification)): the session clock flows to the
next hour at literal host times, keeps flowing while the palette and a dialog are open (only the Esc menu pauses it), waits +1 hour on top of the flow
and round-trips through a snapshot; it prints `ARC-WORLD-010 native-smoke clock PASS`.
`ARC_AREA_TRANSITION_CAPTURE` saves views of both maps. The real-data Core test
`AreaTransitionOriginalTests` walks to each trigger over the step evidence and
checks the same transfers. The ordinary launcher passes the cave input only to an
ARC-WORLD-009 build (which `./arcanum area` preferred until ARC-RENDER-028 made it map mode on the main world map); exported AREA builds that predate
it reject the flag ([launcher](../workflow/local-development.md#cave-and-return-candidate-arc-world-009)).

### Actor anchor and wall fade (ARC-RENDER-011)

**Actor anchor.** The original places a map location at `40(y-x-1), 20(y+x)` (the
location-to-screen function 0x4B8680, source reading, F0) and
draws an object at that point plus its offset plus `(40,20)`, so every object, the
player included, stands on the centre of its tile diamond. The AREA plan projects
terrain and bodies from `40(y-x)`, 40 px to the right of that location point, while the
actor placement restates that location point itself; before this change the actor was
therefore drawn 40 px left of its cell, on the diamond's left vertex. In
`area` the placement now takes the chart's location origin `(40,0)`
(`AreaGridSession.LocationOriginX`), so the actor anchor and the followed foot point
are the diamond centre `(40(y-x)+40, 20(y+x)+20)`, the point `AreaCellPicking` picks
and AREA scenery uses. Depth order is unchanged: the same-cell ranks come from the
offsets, not from the drawn point. The ARC-RENDER-008 passability overlay draws its
AREA diamonds from the same origin. `world-diagnostic` keeps its retained origin and
its human/grid references; its terrain uses the same projection, so its actor and
overlay probably sit 40 px left of the tiles as well (inferred, not changed or checked
here). The synthetic moving-overlap oracle moved with it (actor
rectangles +40; the fixture canopy's hotspot is now `(-20,80)` so its opaque half still
covers the witnesses; ranks unchanged).

**Wall check.** The original fades walls in front of the player: the wall check
recalculation (0x437E50) with its block regions, corner offsets and edge table, the
wall-flag updates (0x438570, 0x4386B0, 0x438720, 0x4387C0, 0x438830), the object
draw's wall skip under a faded roof (0x43B390), the render alpha choice (0x442520),
the tile object lists (0x4407C0) and the ART alpha lerp; a source reading (F0) except
what the ledger lists (BL2, RF8, RF9). `AreaWallCheck` (viewer Core) restates it:

- Walls stand on a lattice of corners every four tiles. The player's committed tile
  is resolved to the 4×4 block with corner `(x - (x-2) mod 4, y - (y-2) mod 4)`; on
  the block's low-X line a wall on the player's tile with rotation 4/5 moves the player
  to the previous block along X (rotation 2/3 along Y), and on the low-Y line a
  rotation 2/3 wall moves it along Y. The block's four diagonal triangles are its
  regions; a tile on a diagonal is in two.
- 34 lattice edges around the block (the three tiles between two corners), in a fixed
  order, are checked: 22 can hide others when complete (a wall on all three tiles),
  eight of them only test; 26 fade, 12 of them only fade. A hidden edge hides nothing. Some hides apply only
  while the player is in a given region. An edge that is not hidden makes the first
  wall of each of its tiles see-through (alpha 64) and marks its corners: a corner
  wall fades on the side towards the edge (alpha 128 there, 255 on the other side,
  linear across the drawn width). Walls with `OWAF_TRANS_DISALLOW` stay opaque.
- While the player stays in the same block and shares a region with the position of
  the last calculation, the previous set is kept. A map change starts empty.

The edge list is the original's data (decoded from a source reading of its table, F0)
re-expressed as lattice geometry. A literal research port of the source-read
function gave the same fades as `AreaWallCheck` for every tile of the cave sector
and a 4,000-step random walk (stateful); the port is not part of the repository.
The viewer recalculates when the actor's committed cell changes and draws each faded
wall through the same shader with the alpha at its left and right edges. The HUD
shows the faded count and block. The walls are validated at load (`AreaLoadResult.Walls`):
authored wall translucency flags, or a core wall the plan does not draw although the
source would still find it on its tile, fail the load. A quick load (ARC-WORLD-011)
starts the check afresh, as the source's flush on map load does. An additive wall would
never fade (the source drops alpha modes for additive objects); the cave has none.

Evidence: synthetic `AreaWallCheckTests` (regions, front edge and corners, X-edge
corners by rotation, a complete back edge hiding, region-only hides, the same-block
rule, the block shift, disallowed walls, shared tiles); real-data
`AreaTransitionOriginalTests` (125 walls; at the arrival (2087,2018) block
(2086,2018), region LowY, 11 faded: the corridor's front row y 2022 x 2087..2092 and
the row y 2026 x 2078..2082 with fading corners; no admitted cave tile probes a fade
tile holding two walls); the native cave smoke prints
`ARC-RENDER-011 native-smoke wall fade PASS` after comparing the arrival view drawn
with the fade set and with opaque walls (pixels differ, only on opaque texels of faded
walls).

NOT VERIFIED: original pixel parity (per-pixel alpha rounding, the `>>8` blend, the
lerp of a mirrored wall, stippled blending when the original's translucency setting
is not constant alpha), the original's head-of-list order for a tile holding two
walls (the area's same-tile order is used; it does not occur in the cave), walls of
the exterior (there are none in the admitted core), roofs (the original also hides a
faded wall under a faded roof; the cave has no roofs), click-through of faded walls,
and whether the original recalculates on the committed tile or at another point of a
step.

### Quick save and quick load (ARC-WORLD-011)

While `area` has a game session (every published area since ARC-SCRIPT-006),
the original **F7** saves and **F8** loads one quick-save slot,
`user://saves/quicksave.json` (Godot user data of the viewer; an automated run keeps its slots in its own
folder, [ARC-DEV-032](#automated-runs-and-the-players-settings-arc-dev-032)). Since ARC-UI-010 this slot is the
[save list](#save-list-arc-ui-010)'s auto-save: F7 writes it with its details and a preview, and F8 loads the
**newest** save of the list by file date, which is the auto-save only when nothing newer was saved, as the
original's F8 (ledger SL1, SL2). Elsewhere F7/F8 stay reserved. Map mode
has its own slot and also loads a save of another map
([ARC-WORLD-038](area-map-mode.md#quick-save-arc-world-038)).

- **Keys.** Both act on key release, as the source does
  ([keyboard requirements](../requirements/original-keyboard-mouse.md)). A key acts
  only when its fresh press and its release both came with the window focused and
  no palette or UI focus; otherwise it is ignored and the HUD says so. An open dialog
  takes every key first, so F7/F8 do nothing during a conversation. Echo repeats do
  nothing; modifiers are not checked (the source's handling of modified F7/F8 is not
  verified). The gate is `QuickKeyGate` (Core, unit-tested).
- **Save.** Refused while a reset/reload or a map transfer is pending. Otherwise the
  actor's committed cell and facing on the current map go into the session
  (`PlaceActor`), the whole session is captured and written atomically by
  `GameSessionSaveSlot` ([save slot](../world/game-session-runtime.md#save-slot-arc-world-011)).
  A started step, click route and walk-to-use/talk are host state and are not saved:
  a save during a step records the cell the actor is committed to. If the write then
  fails, the previous save is kept; the placement stays recorded in the session (it
  only mirrors where the actor stands).
- **Load.** Refused during a reset/reload. The slot is read and decoded, the saved map
  must be loaded in this run and the cell admitted on its chart, and `Restore` must
  accept it: same module id and exact clock profile as the running session (the
  world id is taken from the save, which continues that playthrough). Every check
  runs before anything changes, so a missing, damaged, newer-schema or foreign save
  leaves the running session and scene as they were and the HUD names the reason.
  On success the new session replaces the old one, the route, approaches and any
  queued transfer are dropped, and the actor stands settled on the saved map and
  cell (the scene is swapped as a transfer swaps it). The session is replaced in one
  place (`ReplaceAreaSession`), which disposes the previous owner: code that uses the
  session reads it when needed and keeps no owner, clock or flags reference across
  calls; a component that mirrors session state rebinds there. The dialog host
  (ARC-SCRIPT-006) is rebuilt over the loaded session there, so flags, quests, rumors
  and NPC dialog state (LF, reaction, follow consent) come back with the save; a save
  whose dialog values it cannot read is refused before anything changes. Snapshot sections other domains
  add later are restored with the session; the day/night driver (ARC-WORLD-010) rebinds
  there to the loaded clock, so a load restores the saved time, pause and applied scheme.
- **HUD.** The area status line shows the save folder and the last F7/F8 result, a load naming the save it read.

The native smoke (`--native-smoke` with the cave input) runs a quick-save phase after
the transition phase, on a temporary slot, never the player's: F7 through the
production key route next to the entrance, F7 ignored under the palette with the file
unchanged, the entrance into the cave, F8 back to the saved exterior cell under a new
session, then a damaged file makes F8 fail without changing the session or map; it
prints `ARC-WORLD-011 native-smoke quicksave PASS`. Core rules are covered by
`AreaQuickSaveTests` (terrain viewer tests) and `SaveSlotTests` (session tests).

### Hover tooltip and action cursor (ARC-UI-005)

In `area` the pointer names the object under it and the cursor shows what a
left click would do there (FR-UI-005). Nothing is clicked or changed by hovering.

- **Pick.** The object under the pointer is the frontmost drawn body with an opaque pixel
  there, skipping bodies whose `OBJ_F_FLAGS` has `OF_CLICK_THROUGH` (0x800), as the
  original's screen pick does (the screen pick 0x43D9F0 and the object rectangle 0x43DFD0).
  On the installed Crash Site exterior 419 of the 426 drawn scenery bodies (trees, plants,
  blood) are click-through, so 80 bodies can be hovered: 2 containers, 7 scenery, 21 food,
  16 generic items and 34 NPCs. A wall drawn faded near the actor (ARC-RENDER-011) is
  see-through for the pick, as the original skips walls flagged `OWAF_TRANS_LEFT`/`RIGHT`.
  The talk click and the cave use click take the same pick, so a click acts on the object
  the tooltip names. Before ARC-UI-005 the talk click took the frontmost opaque non-additive
  body, click-through ones included, and the use click took any opaque trigger pixel
  whatever was drawn in front; now click-through bodies never block and additive bodies do,
  as in the original. Not modelled: the original's 5×5 pixel fallback and the
  objects-on-the-tile fallback, the roof hit test, and the diagnostic actor, which is not a
  hover target and does not hide objects behind it.
- **Tooltip.** The name is `ObjectDisplayName` ([MES reader](../formats/mes-reader.md#which-name-is-shown))
  decoded as Windows-1252, with a second line `Left click: <action>`. As in the original's
  message window, walls and projectiles are picked but show nothing, and an empty or
  missing description shows nothing. An NPC the hero has not met is shown by its
  not-yet-met description (for Virgil a generic one, not his name); one that remembers the hero is shown by its own
  name (ARC-DEV-172, the examine function's recognition 0x45F970: the NPC's reaction memory holds the hero,
  "met before" 0x4C0C40, [reaction research R6](../research/npc-reaction-reputation-evidence.md)). The viewer reads the
  session's reaction record at every hover (`AreaHoverIndex.Recognition`), so the name changes right after the first talk
  ends (the talk itself still shows the old name, as the game's "met before" is false during the first conversation);
  the window of an object already described keeps its name until the pointer moves to another object, as the original
  describes only a newly hovered object. The barter window's merchant name follows the same rule (ARC-DEV-297: the window makes its name with the examine function for the PC at the open, so a merchant that remembers the hero is named by its own description); the original draws no NPC name in the pick-pocket and loot windows, and the viewer's pick-pocket status line uses the same recognition. The tooltip sits below
  right of the pointer and flips inside the window; it is recomputed when the pointer, the
  camera, the map, the actor's cell or the input guards change, so it never stays on an
  old position. The area status line repeats it as `hover <name> -> <action>`. Since ARC-UI-057
  the tooltip is shown only while the interface bars are not drawn (the diagnostic viewer): with the bars the
  object is described in the message window as in the original ([below](#hovered-object-window-and-highlight-arc-ui-057)).
- **Names.** The description tables come from the content stack of the game folder that
  holds the area's module archive (`<game>/modules/<Name>.dat`, the installed layout; the
  mounted module archive must be that same file). They load once in the background
  (about 0.2 s on the owner's installation) and are kept across reloads and the cave.
  Without them (another layout, a mount or parse failure) the tooltip names the object
  type, e.g. `[NPC: name unavailable]`, and the status line gives the status (never a path).
- **Action cursor.** Project policy, not original behaviour: the original main-mode cursor
  changes with weapon and called-shot keys, not with the target
  (the cursor refresh, 0x553990). Since ARC-UI-056 the player sees the original's cursor art
  ([below](#original-cursor-art-arc-ui-056)) and these system shapes only while that art is not read (the self-test, an
  unreadable game folder); the action is still decided, named in the tooltip's second line and the status line.
  The decision follows `WorldRouteClick` in its order and guards without running it:
  a dialog NPC is *talk* within the talk range (less than 5 cells, goal 21 node 4; ARC-DEV-169), else *walk to talk*; the cave
  entrance or exit is *use* within use range 2, else *walk to use*; anything else is a
  ground goal: *no route* when the route planner refuses it without a search (the cell or
  the actor outside the area, or no neighbour step entering the cell), else *walk here*;
  since ARC-WORLD-042 a goal more than 32 cells from the actor on an axis is *walk here*
  too (the follower walks it along a corridor), and since ARC-WORLD-072 *no route* under the
  Original rule mode (its far click does not move); the *walk to …* actions of far objects do
  not read the mode yet (hover and use reach are ARC-WORLD-068's). The search itself is not run on hover, so
  a *walk here* goal can still end without a route, or short of it (`ROUTE_PARTIAL`). Since ARC-UI-004 an item on the ground
  is *pick up* (since ARC-DEV-175 the hero must stand on the item's tile: goal 15 walks onto it with range 0) else *walk to pick up*, and a dead NPC (since ARC-WORLD-101 also a standing container) is *loot* within reach 1 or *walk to
  loot* (checked after talk and use, before the ground). System cursor shapes: cross for walk,
  forbidden for no route, help (question arrow) for talk, pointing hand for use, pickup and
  loot, arrow otherwise. A use or talk that is then refused (blocked step, no standable cell) is
  reported by the click as before; the cursor shows the intent.
- **Guards.** Over HUD or palette controls, outside the window, without window focus or
  with the palette or a dialog open there is no tooltip and the arrow cursor. While the
  safety gate is closed, a reset/reload is pending or a map transfer is queued, the name
  stays and no action is shown, because the click would be ignored.

Checks: `AreaHoverTests` (pick order and click-through, name chain and fallbacks,
Windows-1252, the planner refusals and goal pre-check, game-folder rules), the opt-in `AreaHoverOriginalTests`
(counts above, every pickable body named, Virgil's unknown description), the
self-test `ARC-UI-005 self-test PASS` over the synthetic owner (tooltip, both ground
outcomes, the left click on the hovered body sets that cell as goal, GUI under the pointer,
modal, closed gate, focus and window guards, injected names) and, with the cave input, the native smoke
`ARC-UI-005 native-smoke hover PASS` and, in the cave, `ARC-UI-005 native-smoke faded-wall
pick PASS` (a faded wall is never picked, the same wall drawn opaque is) (Virgil walk to talk with his unknown description, the
entrance walk to use, click-through sprites passed through to the body behind, the press
does what the cursor showed). Not
verified: original message-window parity (the cursor art follows the executable since ARC-UI-056), hover art (the animated underlay), a
human on a physical mouse and display.

### Hovered object window and highlight (ARC-UI-057)

The owner's bug reports of 2026-10-29: an item under the pointer was not highlighted, and its name floated at the pointer
instead of the message window. Both follow the executable now (ledger [OH1–OH9](../research/ledger/ui-audio.md#hovered-object-arc-ui-057)),
on the pick of ARC-UI-005.

- **Highlight.** The hovered portal, container, scenery or item (types 1–3 and 5–14, without `OF_DONTDRAW` or `OF_INVISIBLE`)
  is drawn a second time over itself (0x43C690): after every body and before the roofs, its frame additive, the source tinted
  by the constant colour (200, 200, 200), unlit, without its alpha modes and its wall fade (`TerrainCanvas.AreaHighlight`,
  rule `AreaHoverWindow.Highlighted`). The item brightens by about 78 % of its own colour. A wall, a projectile, a critter or a
  trap gets no second draw (their rings: next item). NOT READY: the detect-invisible exception, the wading object's two-part draw (the same blit
  for both parts, so no visible difference).
- **Rings (ARC-UI-1117, ledger [OH21](../research/ledger/ui-audio.md#hovered-object-arc-ui-057)).** A hovered critter or trap (types 15–17) with a hero in the
  session gets two rings: art 467 `hoverunder` under every body (after the terrain, before the flat and upright bodies) and art 468 `hoverover` over every
  body (before the roofs), both additive and tinted by the ring colour (`TerrainCanvasArea.AreaRing`, rules in `AreaHoverRingRules`, the frames read from
  `art\interface` through `interface.mes`). The colour is white; an NPC's is its reaction category's row of `rules\reaction_colors.mes` (seven rows read at
  start, the category of the AI host's standard reaction; Neutral reads yellow in the installed data). While the hero is in combat mode an NPC's two arts are 555
  `hoverattack` (a PC or a trap keeps 467 and 468). Arts and colour are decided when the pointer enters the creature (a combat toggle under the pointer changes them at the next hover, as the
  original's setter does nothing for the same handle). The ring is the frame placed at the body's anchor plus the frame's delta less its centre (15 pixels lower
  for a wading body) and follows a moving body; each ring steps to the next frame of its art every 1000 / fps ms of art 467 or 468 (the periods the original fixes at start; 15 frames at installed data). Deviations:
  the frame stamps start at the hover; an NPC with no reaction (no AI host: an automated run leaves it off, or no combat values) is white; HD does not apply
  (the interface art stays Legacy); a body with no hotspot (a seam moved it without placing it from its frame) shows no ring; a missing art or colour file
  degrades to no ring or white and is said once in the log (`ARC-UI-1117 hover-ring`).
- **Window.** With the interface bars drawn the object is described in the message window (0x553BE0) and the floating
  tooltip is not shown: an item by its item window (its type icon and its name in its colour, as the hotkey bar's,
  ARC-UI-035, and since ARC-UI-108 the item window's lines, see the next item); a portal (`Door_Icon` 436 or `IconWindow` 786 by its art), a container (by its description) and scenery (437)
  by their image at (217, 510) and their name, scenery's in its three-line block, and (ARC-UI-107, [OH10](../research/ledger/ui-audio.md#hovered-object-arc-ui-057)) on the fourth line (290, 564, 291 × 18) in the text font, **red** (fixed, not by the hit-point ratio):
  a portal's and container's lock state at the left (`intgame.mes` 26 jammed, 27 magically held, else 9 locked or 10 not, by the
  session's lock flags and hour, `AreaHoverWindow.LockMessage`) and the hit points `current/maximum` at the right edge
  (`AreaHoverWindow.HitPointText`: the maximum less the damage over `hp_pts + hp_adj`, no clamp; a portal's always, a container's
  and scenery's only without object flag 0x400000), read from the session when the pointer enters the object (the original's draw functions read the values when they run; a hit taken while the pointer stays shows at the next hover, redraw on a change of the same hovered object not traced); a critter by its name, the bars of
  ARC-UI-020 on the third and fourth lines (291 px, the viewer's colours: the bar art 463–466 is not drawn; they follow the
  critter's life while it stays hovered) and its image (a follower's portrait, ARC-UI-024, else by its flags or race as the
  loot window's; for a hero who detects alignment, spell flag 0x80, the alignment image of six bands instead) and, since ARC-UI-109 ([OH11](../research/ledger/ui-audio.md#hovered-object-arc-ui-057)), a second line (290, 528, 291 × 18): "Level: N" right-aligned and, for an NPC, at the left "Sneak: state" while the hero is concealed (the AI host's sight and hearing checks), else "Alignment: N" for a hero who detects alignment, else "Reaction: R (category)"; a dead critter gets its dead line in red and no bars (`AreaHoverWindow.CritterWindow`, read when the pointer enters the object); and, since ARC-UI-124 ([OH19](../research/ledger/ui-audio.md#hovered-object-arc-ui-057)), with the hero in combat mode and the critter alive, the **combat panel** (0x554830) in place of the image, read when the pointer enters (`AreaCombatPanelLayout`, `AttackChanceRules.Preview`): the bullseye art 582 at (207, 498) and the hit chance of the wielded weapon against the critter, printed `N%` (not clamped above; font art 230, white, centred in a 64-wide rectangle), the effectiveness of its skill less the difficulty of the shared check from where the hero stands (the tile distance and, on the ranged path, the line of fire the attack reads), less the weapon's magick or technology cancel share, 0 for an impossible shot; with a term that made the check harder the number is at the top (215, 507) and one icon of 20 px each stands over the bullseye in six places (weapon too heavy `Pen_MSR` 586, out of range `Pen_Range` 588, distance `Pen_Perception` 587, cover `Pen_Cover` 583, darkness `Pen_Light` 585, injury `Pen_Injury` 584, blocked line `BlockedShot` 845, cancel chance above 20 `Magic-Tech-Penalty` 846, in that order, at most six); without one the label "To Hit:" (`intgame.mes` 20) is centred at (215, 526) and the number under it at (215, 544); a trap's name as a hover text. In a pane mode only the name goes into the pane's text area. A scenery with
  an empty name, a wall and a projectile draw nothing. Leaving the object shows the viewed entry again (Steamwoven's ML7; the
  original keeps its drawing); an entry stored while the object is hovered takes the window, and the object's window comes
  back only when the pointer moves to another object. NOT READY: the combat panel's art placement is the top left (inferred from the number's centre, the hot-spot handling of 0x554B00 is not read), its offsets with the interface hidden (0x5578C0) and a redraw while the pointer stays (the panel is read when the pointer enters, as the original draws once; a hero who toggles combat mode over an object keeps the old window until the pointer moves), the target's awareness and light (as the attack: an NPC always sees the hero, every position is lit, so the unaware and darkness terms never show), a weapon that default-throws (the wielded skill is used), a follower carried onto a map whose home map is not held (it has no combat values there, so no panel; the original draws it for any critter but the PC), the PC's own portrait; a portal or container with an empty name draws no window in the viewer (in the original
  only scenery checks its name, the others draw their image and lines); a trap's hover text stays after the pointer leaves
  it until the quiet time's next entry (the original's clearing of it there was not read).
- **Item window lines (ARC-UI-108).** An item's window (the world's hover, the hotkey bar's slot, the inventory's still pointer) draws
  after its type icon and name (ledger [OH12–OH18](../research/ledger/ui-audio.md#hovered-object-arc-ui-057), read in `Arcanum.exe`
  0x554F10): a weapon's speed in red right-aligned on the title's line; the second line (290, 528): the effects text of
  `item_effect.mes` (`AreaItemIcons.EffectsText`) when it has one and the item is not magickal or is identified, else a weapon's
  damage line (`D:1-6  FT:1-2  TH:+3  RNG:5  FD:2-3  `: the original's short labels from `intgame.mes` 43–49, the magic adjustments in
  brackets for an identified magickal item), an armour's line (`AC`, `DR`, `PR`, `FR`, `ER`, `MR`, `NP`, gauntlets' unarmed bonus),
  an ammunition's or gold's `Quantity`, a key ring's `Keys`, a generic item's usage bonus; the third line (290, 546): the magick
  power percent or the tech adjustment of an identified weapon, armour or scroll at the left, the charges (`Uses: ??` unidentified),
  a generic item's uses or a weapon's ammunition per shot at the right; the fourth line (290, 564) in red: the weight
  (`Weight: N stone`) at the left and a weapon's, armour's or flagged generic item's hit points `current/maximum` at the right
  (not for a broken one). The builder is `AreaItemWindowLines` (Godot-free); the local PC's aptitude, weapon training and key count
  come from `GameSessionOwner.QueryItemViewer`, an item's units, charges and condition from the session by its entity (the fields'
  own for an item no map entry is held by the session for). NOT READY: a key ring's own key count (the PC's is shown for every ring), the turn-based action-point preview (0x557370, which also moves the window while a preview is
  up) and the observer other than the local PC (an item worn by another holder reads its holder). A line longer than its
  291 × 18 rectangle is drawn as the original's 0x550A10 does with the original's font art (ARC-UI-1115, OH20; the label stand-in without the game folder is not checked): broken at the rectangle's width, only the lines
  its height holds drawn (one for a font 10 to 18 rows high), the rest not drawn, no cut of characters and no second layout; a
  right-aligned text wider than its rectangle starts at the left.
- **The plane wreck at the Crash Site** is one scenery object named "Something Big" among 16 wreckage pieces that carry
  `OF_CLICK_THROUGH`. The original's pick skips them (OH1), so only that one part is hovered, highlighted and named: this is
  the original's behaviour, not a Steamwoven bug (OH8).

Checks: `AreaHoverWindowTests` (the dispatch by type, the highlighted types and flags, the images, the window's layout and
its show and leave), `AreaHoverRingTests` (ARC-UI-1117: which types, the arts by combat mode, the colour by category and its table's reading, the placement and wading shift, the frame clock, the files' reading), `AreaHoverCritterTests` (ARC-UI-109: the second line's parts and order, the sneak states, the alignment bands and image, the dead line), `AreaCombatPanelLayoutTests` and `AttackChanceTests` (Simulation) and `AttackChanceStateTests` (Session) (ARC-UI-124: the icon table, the two layouts, the chance's terms, the cancel share, the zero cases), `AreaHoverConditionTests` (ARC-UI-107: the hit-point text, which types show hit points and a lock line, the
lock message order, the fourth line's rectangle, red colour and right edge, the condition read from a session after damage and
lock writes), `AreaItemWindowLinesTests` (ARC-UI-108: every line of the item window over invented items, the draw's rectangles, fonts and colours), `AreaItemWindowWrapTests` (ARC-UI-1115: a long line broken at its rectangle's width and cut by its height, no wide layout or cut of characters, the right-aligned long text, the bounds check), `AreaMessageLogTests`; the native smoke stage `hover` (`./arcanum smoke pr --stages hover --hero
human-male`, marker `ARC-UI-057 native-smoke hover-window PASS`: with the bars drawn it looks for an item, a container, a
portal and a scenery on screen (on the start map the first, second and fourth are found; at least one kind must be) and
hovers each found one (a body a roof covers or that is hidden is not a candidate, since it is picked but not drawn; of the first six
candidates the one with the most picked pixels, or the first with 16, is taken; a window with no hoverable body, as the generated
wilderness, prints `ARC-UI-057 native-smoke hover-window NONE` instead of failing, so a map battery passes while `--stages hover` alone reports the stage as not run; ARC-DEV-083): the highlight is the body's frame
(an animated scenery shows the frame its animation shows now, not its index command's; still bodies are tried first) drawn additive, none of its picked pixels
gets darker and some get brighter, the window shows the expected kind, image and name without the tooltip, and since ARC-UI-107 the
fourth line the session's facts give (a container's lock line and the `current/maximum` hit points, none for an invulnerable
scenery; the line `ARC-UI-107 native-smoke hover-window fourth-line` prints each), leaving takes both away; since ARC-UI-1281 it also joins a free companion of the map and observes his portrait's hover (`ARC-UI-1281 native-smoke hover-portrait`, [follower portraits' hover](../world/companion-orders-runtime.md#follower-portraits-hover-arc-ui-1281)); since ARC-UI-1117 the same stage hovers an NPC (the AI on for that pass) and checks the rings: arts by combat mode, the colour of its reaction category, additive pixels in the ring's rectangle, a frame step, removal on leaving; `ARC-UI-1117 native-smoke hover-ring` prints it;
`ARC_AREA_HOVER_WINDOW_CAPTURE` saves readbacks) and the stage `life` (`ARC-UI-057 native-smoke hover-window critter PASS`: a
follower's and another NPC's window with their name, image or portrait and bars; since ARC-UI-124 `ARC-UI-124 native-smoke hover combat-panel PASS`: in combat mode the other NPC's window shows the bullseye and the chance, out of it the image; `./arcanum smoke pr --stages life --map "bates mansion lev 2" --hero human-male`). Not verified: the exact pixel of the
original's 16-bit additive blit (its pixel loop is not read), a portal's window on screen (the start map has none).

### Original cursor art (ARC-UI-056)

In `area` (the exterior, the cave and `--area-map`) the pointer is the original's cursor art, picked as the 1.0.7.4
executable picks it ([ledger CU1–CU6](../research/ledger/ui-audio.md#cursor-art-arc-ui-056), FR-UI-005). It follows the
game's state, never the object under the pointer: what a click would do there stays in the tooltip (ARC-UI-005).

- **Which art** (`AreaCursorArt.Art`, CU1–CU3). The spell's targeting cursor open, or armed lockpicks (item use, mode 16, SA41):
  `spellcur.art` (21); the armed pick pocket (skill target, mode 2): `skillcur.art` (352). A window over the game
  (inventory, sheet, logbook, schematics, barter, reading, world map, save list, main menu, palette, dialog, bug report, the
  death cover): the gauntlet `cursor.art` (0). In the game out of combat mode: the gauntlet; in combat mode: `battlecur.art`
  (353), or with a called-shot key held (`,` first, then `.`, then `/`, the physical keys, as the attack reads them, FA4) its
  called-shot cursor (818 head, 819 leg picture, 820 arm picture); a portrait's pending order (mode 18) keeps the game cursor.
  ARC-DEV-109 (MIG16 S2) and ARC-DEV-110 (S3): the mode is read from the viewer's interface mode stack alone
  (`AreaCursorArt.Mode(stack.Cursor)`, SA41; [interface modes](../architecture/interface-modes.md)); the panels outside the stack
  (save list, main menu, palette, bug report, death cover) cover the game with the gauntlet through the modal test.
- **Rule modes** (CUA1). The original shows the leg picture for `.`, which aims at the arm, and the arm picture for `/`,
  which aims at the leg. Original keeps that; Steamwoven (default) shows the aimed location's picture
  (`InterfaceRuleProfile.CalledShotCursorsSwapped`, [rule modes](../world/rule-modes.md)). A mode switch shows at the next frame.
- **Drawing** (CU4, CU5). Each art's frames are read once a run from the game folder of the area's module (as the interface
  bars' art) and shown at their own pixel size as Godot's custom arrow cursor with the frame's hot point (the ART frame
  header's centre) on the pointer. `spellcur.art` steps its 7 frames every 100 ms (10 fps) from the moment the art was set,
  wrapping; the others have one frame. The same cursor serves the world, the bars and the windows, as in the original;
  controls with a cursor shape of their own (a text field's I-beam) keep it. The log prints `ARC-UI-056 cursor art ready=…`
  once and `ARC-UI-056 cursor art=N mode=M` at each change.
- **Across a map window move** (ARC-UI-1357, FR-UI-002). While a window loads (`MapStreamInFlight`) the cursor art is not released
  even if the scene's ready mode is momentarily not `area`, and the first frame after the window swap hands the art to Godot
  again (`ARC-UI-056 window swap …` logs the swap's main-thread time and Godot's shape). The `stream` smoke stage checks (when the game's cursor art reads) that the art is shown, that no
  move released it and that every swap re-applied it. `ClearPublishedScene` does set the ready mode to `TerrainOnly` for that span, so the hold is hardening, not a proven cure. The swap itself is one synchronous step of about 0.2-0.4 s, with no frame
  between the clear and the publish, so the state never showed the system shape in the smoke; the owner's report of a system
  cursor for a moment at a window move is kept on the owner recheck list.
- **Without the art** (`AreaCursorContent.Ready` false: the self-test, another layout, a missing or unreadable file, a frame
  larger than 256 × 256 or with its hot point outside it) the ARC-UI-005 system shapes stay, as before.

Not built (F0 rows of CU6): the cursor arts `Cursor_UP`/`Cursor_DWN` over two interface buttons not yet identified, the
spell cursor of the world map's state 3, a held item's art on the cursor (the viewer's inventory drag keeps its own image),
the skill cursor, and scaling the art with the window (it stays at the art's pixel size). The animation counts from the
elapsed time, so after a long frame hitch it jumps to the frame of that time where the original steps one frame (CU5).

Checks: `AreaCursorTests` (the art by mode, combat mode and key in both rule modes, the frame steps, content readiness; the
opt-in `Original_cursor_art_reads` checks the installed arts' names, frames, rate and hot points), `RuleModeTests`, the
ARC-UI-005 self-test with injected synthetic art (the gauntlet over a walk target and under the palette, the system shapes
back without the art), the ARC-COMBAT-004 combat-mode smoke (`battlecur` in combat mode, each called-shot key in both rule
modes, the gauntlet after leaving) and the ARC-MAGIC-015 cast smoke (`spellcur` frame 0, frame 1 after 100 ms, still shown
after the cast, as the targeting cursor stays open, SU9). Not verified: a human on a physical mouse and display, original parity of the cursor's on-screen size.

### Health and fatigue bars (ARC-UI-020)

Slice 3 of the [combat HUD plan](../research/combat-hud.md#slices) (FR-UI-002), in `area` with plain Godot
controls (no original interface art). The rules are `LifeBarRules` in `Arcanum.Presentation.Hud`, read in the 1.0.7.4
executable ([ledger CH7–CH9](../research/ledger/ui-audio.md#combat-hud-arc-ui-017)); every percent is current × 100 /
maximum truncated toward zero.

- **The PC's gauges** (CH7). With a hero, the health and fatigue gauges, each 88 px high and filled from the bottom over
  the percent of its height (half full at a maximum of 0, the original's guard), with the current value under it as the
  original prints it (C's `%03d`: `030`, `-05`). While the hero is poisoned the health gauge takes the poisoned colour
  (green) and the poison level is printed over it. While the [interface bars](#interface-bars-arc-ui-018) are shown the
  gauges fill the bottom strip's vials at the original's places (gauges at 14, 472 and 754, 473 of the 800 × 600 screen,
  the numbers at 15, 578 and 755, 578, the poison at 15, 500), scaled with the bars and drawn over them, since ARC-UI-024
  with the original's vial art ([panel art](#panel-art-arc-ui-024)); they stay while a window the bars reach is open.
  Without the bars the original's compact panel stands at the screen's bottom right (ARC-UI-024), hidden while a window
  or the palette is open. Without a hero there are none. Automated runs draw them only when their smoke asks or the bars
  are drawn.
- **Follower portraits** (CH9). In each frame of the [follower portraits](../world/companion-orders-runtime.md#area-viewer)
  (ARC-WORLD-046; the original's frames since ARC-UI-024) a health bar (its poisoned art while poisoned) and a fatigue bar,
  each cropped to the follower's percent (nothing at 0 % or less, capped at 100 %); below 20 % health red is added over
  the portrait, as the original does. A follower's values are its combat values by identity (ARC-DEV-076, MIG3: the record resolver finds them wherever it stands, a visiting companion's too); a follower
  without combat values shows empty bars.
- **Hovered critter** (CH8). The [tooltip](#hover-tooltip-and-action-cursor-arc-ui-005) of a critter with combat values
  (an NPC of a covered map or a companion) adds its health and fatigue bars under the name, each filled to the
  percent clamped to 0–100 plus one pixel unless full (the original's bar), with the text `current/maximum` for the
  PC's followers and the unclamped percent (`100%`, `-3%`) for anyone else; the health bar is green while it is
  poisoned. The original draws them side by side on its description line; the tooltip stacks them.
- **Technical fix.** The original divides by the maximum without a guard in the hovered-critter and portrait paths
  (a crash at a maximum of 0); the rules read that percent as 0 in both rule modes (no switch).

Checks: `LifeBarsTests` (percent, gauge fill and numbers, hovered fill and text, portrait crop and tint) and the native
smoke stage `life` (map mode with `--hero`, only when named: `ARC_AREA_MAP_SMOKE_ONLY=life`, observed on `bates mansion
lev 2`; `ARC_AREA_LIFE_CAPTURE=<ignored path prefix>` saves readbacks): the gauges match the rules before and after a
poison packet, a speaker that joins shows its portrait bars and turns red after a hit to 15 % of its maximum, hovering it
reads `current/maximum` (`15/100` on that map), hovering another living NPC reads a percent, with the bars drawn the gauges
sit in the vials at the original's places and stay there with the inventory open, and the palette hides the gauges; it prints `ARC-UI-020 native-smoke life PASS`
(the stage leaves the hero poisoned and the NPC joined and wounded). Not done: the hovered panel's level line and its
place on the bars' description line, the PC as a hover target (the viewer's actor is not pickable), action points and the
turn cue (slice 2), floating text (slice 4, [ARC-UI-021](#floating-text-arc-ui-021)). The art (vials, compact panel, frames, portraits) is ARC-UI-024's. Original
visual parity NOT VERIFIED.

### Panel art (ARC-UI-024)

FR-UI-002: the in-game panel's art after the [interface bars](#interface-bars-arc-ui-018) and the
[health and fatigue bars](#health-and-fatigue-bars-arc-ui-020), read in the 1.0.7.4 executable (ledger
[PA1–PA6](../research/ledger/ui-audio.md#panel-art-arc-ui-024) and CH9). The rules are `LifeBarRules.GaugeArt`,
`CompactFill` and `CompactNumber`; the layouts and the portrait reading are `AreaPanelArt.cs` in the viewer's Core.

- **Vials** (PA1–PA3). While the bars are drawn each gauge draws, as the original, the empty vial (art 20) over its
  empty rows from the art's top left, then the fill art (18 health, 17 while poisoned, 19 fatigue; frame 0 of the 15)
  placed with its top 8 rows, the liquid's surface, above the fill line and clipped to the gauge, so a full gauge shows
  the art's rows 8 to 95 and an empty one only the empty vial. The art comes with the bars' own art read. A fill beyond
  the gauge (a value above its maximum, or a dead PC's negative one), which the original draws from a lower art row or
  with the empty art past the gauge, is clamped to the gauge in both rule modes (a technical drawing overflow).
- **Compact panel** (PA4–PA5). The original draws, instead of the bars, a 49 × 102 "Health/Fatigue Bar" panel at the
  screen's bottom right when the interface is hidden (only with its `-fullscreen` start option). The viewer draws it
  whenever its bars are not drawn (it has no hide toggle): a (9, 9, 9) outline and divider, the health column (red, green
  while poisoned) and the fatigue column (blue), each filled from row d + 1 to row 100 where d is the damage percent
  (damage × 100 / maximum, so 2 of 3 hit points fill from row 34, not by the life percent), and each current value
  printed `%02d` in white at the fill's top, the poison level 40 rows lower; nothing filled at 100 % damage. The original
  divides by a zero maximum there (a crash): the column reads fully damaged in both rule modes; and it shifts a poisoned
  PC's health number off centre by reusing the poison text's rectangle, which the viewer centres in both modes (both
  technical). The original's font (art 229) is not drawn; Godot's default font stands in.
- **Follower frames** (CH9, PA6). The [follower portraits](../world/companion-orders-runtime.md#area-viewer) are the
  original's frames: up to six 40 × 49 frames (art 503) at 11, 50 + 62 × slot of the 800 × 600 screen, scaled by the
  bars' whole factor with the screen centred and its top at the window's top (with or without the bars), each with the
  follower's portrait at 4, 4 scaled to 32 × 32, the health bar (505, 616 while poisoned) at 3, 39 and the fatigue bar
  (504) at 3, 44, cropped to the percent, and pure red added over the portrait below 20 % health. A follower's portrait
  is its stored field 0xE7 (critter slot 49 of its map record), its image the portrait catalogue's small `.bmp`
  (ARC-DATA-045), read when a follower first needs it. A press on a frame opens the order menu as before; the name is
  the frame's tooltip. The diagnostic HUD starts right of the frames while any is shown. Without the art (no game
  folder) a frame is a dark rectangle with the name's first letters and plain bars.
- **Hovered portrait** (PA6). The original has no fixed PC portrait or name area in its bars: its hovered-object panel
  in the message window shows the hovered object's image at 217, 510, the portrait (at its own 64 × 64 size) for the PC
  and for the PC's followers with one. While the bars are drawn the viewer shows there a hovered follower's portrait (when
  the PC leads it and it has one) as part of the [hovered object's window](#hovered-object-window-and-highlight-arc-ui-057),
  which the [message log](#message-log-arc-ui-023) shows as its hover entry (ARC-UI-057; before it the log gave way to the
  portrait). The portrait shows when the pointer moves over the follower, not when the bars come back under a pointer that
  has not moved.

Checks: `LifeBarsTests` (gauge art placement, compact fill and numbers), `AreaPanelArtTests` (layouts, the stored portrait
of a critter record; with `DAT_ROOT` the installed frame and vial art and a portrait image read) and the native smoke stage
`life` (map mode, `bates mansion lev 2` with `--hero`): without the bars the compact panel's columns, numbers and place,
the frame's art, place and portrait, with the bars the vials' art numbers (17 while poisoned) and the hovered follower's
portrait and name in the message window when it has one (since ARC-UI-057 also the window's bars and another NPC's
window). NOT READY: frames past six (the original scrolls them with its arrow buttons), the 50 % draw of the critter art
the window shows for a follower without a portrait (the art by race or flags is drawn since ARC-UI-057), the PC's
portrait (it is not session state, and the PC is not a hover target), the rule that gives an NPC sharing the PC's
portrait another one (0x4CEB80, it needs the PC's), the hovered panel's art for anyone else, and the original's hide
toggle. Original visual parity NOT VERIFIED.

### Combat action points and turn order (ARC-UI-1114)

Slice 2 of the [combat HUD plan](../research/combat-hud.md#slices) (FR-COMBAT-011, FR-COMBAT-003, FR-UI-005), in `area` with plain Godot
controls (the lamp panel took the original's art in ARC-UI-1888, see **Lamp panel art** below), read in the 1.0.7.4 executable ([ledger CH2–CH6 and CH14](../research/ledger/ui-audio.md#combat-hud-arc-ui-017)).
The rules are `ActionPointRules` and `TurnOrderRules` in `Arcanum.Presentation.Hud`, the cost preview is `AreaActionPointPreview` in
`Arcanum.World`; the view is `AreaCombatHud` and its host `TerrainViewerAreaCombatHud.cs`. Shown only while a turn-based round runs (a combat
mode with a round), with a hero, while no window the bars do not reach is open.

- **Lamps** (CH2). A panel of 20 lamps in four groups of five at the original's place just above the interface bar (the lamp panel at 222, 412 of the
  800 × 600 screen, scaled with the bars; the plain rectangles stand for the art when the game's art did not read, see **Lamp panel art**). On the hero's subturn they
  light from the left in three shares: the points left after the previewed action (green), the action's cost (orange) and the part of it the points do not
  cover (red), the colours of the original's hidden-interface bar (CH3); more than 20 show what fits, no points light nothing. On another participant's
  subturn three green lights run along the lamps, seven apart, one step every 100 ms (CH5); the lights' clock runs only while another participant acts and starts again with each round, as the original's.
- **Pending cost** (CH4, CH14). The lamps preview the last action-point check's cost. In a round the action under the pointer previews its cost
  (`AreaActionPointPreview.Cost`: walk 2 a step or 1 for an always-running hero, use or loot 2 plus the approach, pick up 1 plus the walk, attack with the weapon's
  cost plus the approach for a weapon that reaches one cell or less, cast 4); an action that runs no check (a talk, no route, nothing under the pointer) leaves it as
  it was. The hero's own charges (a step, an attack, a pickup, a use) store their cost too, and a charge does not clear it. Each subturn start, the end of the round and a new session
  reset it (the original's subturn start runs the check with 0). The approach is counted in straight-line cells, the original asks its pathfinder for the path (a limit; the charge of each step stays exact).
- **Turn order** (CH6; a Steamwoven addition approved by the owner on 2026-10-28). A list at the top right under the interface strip (the follower portraits stand at the left edge): `Round N`, then the
  participants in round order, the one whose subturn it is marked with `>` (the hero as `Hero`, an NPC by the name the hover gives it). More than 8 participants
  show 8 from the current one on and `+N more`. It hides behind any open window. The list is shown in the Steamwoven rule mode and hidden in Original
  (`InterfaceRuleProfile.TurnOrderListHidden`, [rule modes](../world/rule-modes.md)); the lamps show in both.
- **Lamp panel art** (ARC-UI-1888, CH1, CH2, CH5, CT5). With the game's art read (arts 294, 295, 296, 315, 297 come with the bars' art) the panel is the original's
  (`AreaCombatLampLayout`): the panel art 294 (`ap_bar`, 356 × 30) at (222, 412) of the screen, a lit lamp as the art 295 (the points left and the running lights), 296 (the
  cost) or 315 (the shortfall), 11 × 12 at the x of the table 0x5CAA48 (22, 35, 48, 61, 74, then 93 .. 145, 164 .. 216, 235 .. 287) and y 10 of the panel, an unlit lamp showing the
  socket of the panel art; the end-turn button is the art 297 (31 × 15 at (320, 9) of the panel, frames idle and pressed as the other bar buttons): its release over it ends the hero's
  subturn as E does (CT5: the hero must be idle, a window over the world refuses it), with the bar's press and release sounds. The panel takes no pointer (the art is not a hit area, as the
  plain panel was); only the button does. Without the art (no game folder, the automated runs) the plain controls above draw. In the Generated interface the panel (294) and the end-turn button (297)
  are generated chrome ([S11](hd-interface-density.md#viewer-slice-s11-arc-ui-1821), ARC-UI-1821); the lamps stay pictures.
- **Not done.** The 25-point bar of the hidden-interface layout (the viewer has no hide toggle), the item and skill cursors' previews, the Alt variants of the original's preview (attack check on a portal, container or party member), a throw's own cost in the preview (it shows the attack's), the hero's name in the list, a created critter's name in the list (it shows its entity id), the lamps for a negative point count (the original lights the shortfall, the viewer nothing; points are never negative in a round). Visual parity NOT VERIFIED.

Checks: `CombatHudTests` (shares, the three cases of the 20-lamp cap, the cue, the list window), `AreaActionPointPreviewTests`, `RuleModeTests`, `AreaCombatLampLayoutTests` (the panel's tables and the shares' arts, ARC-UI-1888); the native smoke
stage `turn-based` reads the HUD in a real round on `Arcanum1-024-fixed`: three running lights and a marked NPC on the NPC's subturn, the hero's points as lamps and
the marked hero on its own, the walk preview's 2 shown as the cost, the same cost kept after the step, the attack preview equal to the weapon's cost, and the HUD gone
with the round; with synthetic art the art panel's lamps stand at the table's places in the shares' arts and the end-turn button ends the hero's subturn (`ARC_AREA_HUD_BARS=1` draws the interface bars of the game's art so the panel draws the game's own, `ARC_AREA_HUD_ART=plain` keeps the plain controls for the "before" frame, `ARC_AREA_WINDOW_ART=legacy|hd` checks the five arts in that interface mode; `ARC_AREA_HUD_CAPTURE=<ignored path prefix> saves the readbacks the graphics composite is made from).

### Floating text (ARC-UI-021)

Slice 4 of the [combat HUD plan](../research/combat-hud.md#slices) (FR-UI-005), in `area`, drawn with the
original's font art 229 under every interface layer (ARC-DEV-156, B28: the glyphs with the original's blend, the floater's colour as the tint and the fade as the blit's opacity; the line height is the font's tallest glyph + 2; the viewer's font without a game folder). The rules are `FloaterRules`, `FloaterField` and
`CombatFloaters` in `Arcanum.Presentation.Hud`, read in the 1.0.7.4 executable ([ledger
CH10–CH12](../research/ledger/ui-audio.md#combat-hud-arc-ui-017)); `AreaCombatFloaters` turns the session's attack
results into them.

- **What floats.** Every attack the viewer runs through the session: the hero's on an NPC or an object (also a
  projectile's strike, and a wall the line of fire struck, which floats only a critical self-hit's lines), and an NPC's
  or a companion's (the AI host). In the original's order, over the critter the
  step is about (the target, or the attacker of a critical self-hit): the critical miss or hit, its multiplier, the
  stun (whenever the stun bit meets a critter that was not stunned, without a knockdown bit), unconsciousness or the
  knockdown, the scar (at the scar limit too), blinding, the crippled arm with the weapon and shield it drops, crippled
  legs, the armour damaged or broken, the dropped helmet, the weapon damaged, broken or destroyed, the lost ammunition and
  the dropped weapon (a drop floats whether it lands or is refused, as the original's message comes before the drop's
  answer); a dodge floats over the target. The attacker's weapon worn on a hard target floats its damage or break over
  it at every level. At "text floaters" 2 also the damage line (`N HT`, ` N FT` added for fatigue, only the fatigue part
  without hit-point damage), the poison increase (`Poison level +N`), an object's damage (`HT N`, never below 0) and a
  bare-handed attacker's own loss. The numbers are the step's amounts after resistance, difficulty and the multiplier,
  before the life rules adjust them (a poison an undead ignores still floats), as the original prints them. A target dead before the hit, a refused attack, a plain miss and a prevented hit float
  nothing; healing never floats (the original's healing makes no floater). The texts are the player's
  `mes\combat.mes` 0–24 (read once a module, at game start as the original's combat init; the project's English text
  where a line does not read).
- **Colours.** Red for the PC, yellow for an NPC the PC leads, white for anyone else, by the critter the step is about;
  a dodge and an object's damage white, poison green, the attacker's own wear red.
- **Motion.** A line starts 4 line heights below the top of a 200 px box (line height = the font's text height + 2), or
  one height below the object's newest line while that has risen less than a height, so lines stack. At most every
  50 ms of wall time every line rises by the Float Speed + 1 px (60 px a second at the default 2) and is removed above
  the top. A line fades in from 4h to 2.5h, is opaque to 1.5h and fades out to the top (the original's integer
  formula). The box is centred over the object, its bottom 90 px (at the view's zoom) above the tile centre; the text
  keeps the screen's pixels at any zoom. The hero's box follows its committed cell, a companion's and a walking NPC's
  their live cells, an object's its map cell (no draw offset). A new scene drops every line.
- **Settings.** The [options window](#options-window-arc-ui-013)'s Video page offers Floats (Never, Minimal, Verbose:
  0–2, default 1) and Float Speed (0–10, default 2). Level 0 draws nothing (the lines are still made and rise, as the
  original's). Both are saved at once as the `text floaters` and `float speed` lines of `user://arcanum.cfg` (a missing line reads
  as their defaults).

Checks: `FloatersTests` (colours, start offsets and stacking, the fade at its edges, the box and line positions, the
field's 50 ms step and removal, the attack's lines in order at levels 1 and 2, the damage-line formats, dodges, objects
and the attacker's lines), `AreaCombatFloatersTests` (the message conditions from session results, the texts) and the
native smoke stage `floaters` (map mode with `--hero`, only when named: `ARC_AREA_MAP_SMOKE_ONLY=floaters`, observed on
`bates mansion lev 2`; `ARC_AREA_FLOATERS_CAPTURE=<ignored path prefix>` saves readbacks): at level 2 the hero's
critical hit (a granted fate) on the living NPC with the most hit points floats the rules' lines with all 25 texts
from the game's file, the critical-hit message first, stacked from 4h one height apart, the damage line in white; the
smoke's clock raises them 3 px a step and the drawn opacity is the fade's; the NPC then attacks the hero until a hit
lands (red lines over the hero) or is dodged (a white dodge); at level 1 the numbers go, at level 0 nothing is drawn;
it prints `ARC-UI-021 native-smoke floaters PASS`. Since ARC-COMBAT-1138 the stage also checks a script's damage on a map object (a door, container or scenery with hit points to spare): `HT N` in white over it, N what the
object lost after its material (`ARC-COMBAT-1138 floaters object-damage`; its capture hides the diagnostic HUD). Not done: spell blinding, the draw offsets of the boxes' objects (the hero's while walking), the
original's font art 229. Original visual parity NOT VERIFIED. The text bubbles over objects (Text Duration) are [below](#text-bubbles-arc-ui-1997). ARC-AI-1527: the PC's followers' comment lines (the combat taunt at the end of the hero's hit, the near-death line of a hurt follower, the complaint's upset line) show as text bubbles (ARC-UI-1997) over the follower through the script float queue, with a random line of the player's generated NPC files (no text is stored).

### Text bubbles (ARC-UI-1997)

The original's text bubbles ("TB" module, [ledger TB1–TB22](../research/ledger/ui-audio.md#text-bubbles-arc-ui-1997)): `TextBubbleRules` and
`TextBubbleField` in `Arcanum.Presentation.Hud` (the rules and the table of eight), drawn by `AreaFloaterLayer` (a surface under the
floating lines, so above the objects and below the numbers) in the original's font art 229 with its drop shadow (the shadow's
(10, 10, 10) copy first at +1,+1; the viewer's font with an outline without a game folder).

- **What makes a bubble.** The lines the viewer already queued as floats: a dialog script's float line (action 23), a tile script's, the
  followers' comment lines (ARC-AI-1527) and a greeting that opens no window (ARC-DEV-324): white for anyone, green for a PC speaker
  (path 0x568430). They no longer rise as floating lines. A new line over the same object replaces its bubble. **The NPC's line in an open dialog** (ARC-UI-2027, ledger TB23–TB25) is a white
  persistent bubble over the NPC (life −2: the Text Duration does not age it while the window is open); the conversation panel lists only the PC's
  replies and draws no NPC line (both rule modes). A processed reply removes the bubble and its answer is a new one; a reply that ends the
  dialog leaves no persistent bubble, and the end of the dialog (`EndConversation`) gives the last bubble its default life from then.
- **Life.** The Text Duration line of the Video page (0–10, default 6) × 1000 ms on the **real** clock: pause, the Esc menu, turn-based mode
  and a dialog do not freeze a bubble. A bubble goes when its age is above its life (0: after the next frame). Changing the line sets
  the live bubbles' life at once (from their own start; the original's 1,000,000 factor in that callback is a unit slip, fixed in both rule
  modes, TB17). The "text floaters" level does not hide a bubble (only Text Duration is read).
- **Layout.** The text wraps at 200 px (at most 200 px of lines, the rest cut), each line centred in the tight box; the line height is the
  font's text height. The anchor is the owner's tile centre + (40, 20) on the screen (no draw offset yet); the box takes one of eight
  slots around it (above, then clockwise): the first of the anchor's region order whose box lies in the view, chosen once when first
  drawn and kept (the region constants of the original's 800 × 600 view are taken from the view's centre here, so a larger window
  behaves as the original does at its centre). The table holds eight bubbles; a ninth takes the oldest non-persistent one. A new scene
  drops every bubble. The text keeps the screen's pixels at any zoom (the original has no zoom either).
- **Removal by the object's end** (ARC-UI-2034, ledger TB16, TB27). A script's fade (action 103) empties the table (every fade run, whatever its operands, as the original's clear-all runs first); an object the session destroyed or switched off (off flag 0x2), or a created critter the session no longer holds, loses its bubble (`TextBubbleGoneWatch` reads the state each frame; a bubble made over an object that is already gone stays, as the original removes only at the change). Death, moves and the hero's own bubble are untouched. Limits (TB27): a created critter's off flag is not read, a companion carried from another map is never read as gone, and the state is polled once a frame.
- **Not done.** The final-line close of a dialog (#2028), the speaker's turn toward the listener
  and the speech voice, the objects' draw offsets, the exact word-break rules (TB22).

Checks: `TextBubblesTests` (colours, life, wrap, slots and slot choice, the table's reuse and eviction, life rules, the setting change) and
the `map` stage's talk smoke (the NPC line of every started conversation is a persistent bubble, replaced on each reply, not persistent after the end; no `npc` text in the panel); `ARC_AREA_TALK_CAPTURE=PATH` saves the first open dialog as `PATH.png` and the `floaters` smoke stage's bubble part (a PC's and an NPC's bubble over their objects, green and white, wrapped within 200 px,
alive at 6000 ms and gone after it, a Text Duration change to 2 shortening a live bubble at once; `ARC-UI-1997 bubbles over objects PASS`; then the off flag written on the NPC and the NPC destroyed by a script each remove its bubble and not the hero's, a bubble made over an object that is off stays, `ARC-UI-2034 bubbles removed by off flag and teardown PASS` (the fade's clear-all: `AreaScriptHostTests` and `AreaUseScriptHostTests` count the fade runs, `TextBubblesTests` the gone watch);
`ARC_AREA_FLOATERS_CAPTURE` saves `bubble-before` (the old white rising line) and `bubble-after`) and the `spirit-talk` stage (the greeting over the corpse is a bubble). Original visual parity NOT VERIFIED.

### Inventory window, pickup and loot (ARC-UI-004)

In `area` the [session inventory](../world/inventory-runtime.md) (ARC-WORLD-014)
is wired to the viewer: items can be picked up from the ground and taken from dead NPCs (since ARC-WORLD-101 also from containers), and
the original Inventory key shows what the PC took (FR-ITEM-001, FR-UI-004). Code:
[`AreaInventoryHost`](../../src/Arcanum.World/AreaInventoryHost.cs) (Godot-free) and
the `TerrainViewerAreaInventory` partial with `AreaInventoryPanel`.
Since ARC-WORLD-066 ammunition can be picked up and looted too: it merges into the hero's stack of its prototype, and the
inventory and loot lines show a stack's units (`×N`; [ammunition](../world/inventory-runtime.md#ammunition-arc-world-066)).

- **Seeds.** When the area session is created, the exterior (and, with `--area-cave-mobile`,
  the cave) seed is built from the [T0 inventory view](../world/area-inventory-view.md) and
  life from the [step rules](../world/area-step-assessment.md): an NPC is dead only when a
  step into its cell meets its `CRITTER_DEAD` evidence; no evidence counts as alive, so it is
  never looted (the build counts NPCs on admitted cells without such evidence; 0 on the
  installed exterior). Installed original: exterior 53 items, 45 holders, 4 dead NPCs; cave 28
  items, 11 holders, 1 dead NPC (empty). A refused build or attach leaves the inventory
  unavailable (the status line says why) and the rest of the area unchanged. Every F8 quick
  load attaches the seeds to the restored session before anything changes; a save whose
  taken items disagree with them is refused (`F8 quick load REFUSED: the save's inventory
  does not match the loaded maps`), the running session kept.
- **Pickup and loot clicks.** A left click arrives through the guarded ground-click path
  (`WorldRouteClick`) after talk and cave use. On an item lying on the ground it picks the
  item up; on a dead NPC it opens the body in the inventory window. Both act at once within
  Chebyshev reach 1 and otherwise walk to a standable cell within reach first and act on
  arrival, like walk-to-use (a ground click, the stick, another talk/use/item click,
  reset/reload or a quick load cancel the walk; the guards drop it on arrival). Reach 1 is
  the original's range for opening a container, PC or NPC
  (the use-range helper, 0x428930);
  the pickup goal's own move-near range is not pinned, so pickup uses the same reach (project
  choice). Living NPCs and items inside holders are not targets. Since ARC-WORLD-101 a standing container opens its loot
  window after its admission, lid and first-open generation ([container loot window](../world/barter-runtime.md#container-loot-window-arc-world-101);
  native smoke stage `container`). A ground item the session refuses (gold, keys,
  a pickup or insert script; none on the exterior core) is still shown as *pick up* and the
  click reports the refusal. Not modelled: the pickup animation and action-point cost.
- **Use on self (ARC-SKILLS-005).** A healing item in the PC's list (the kit's from the hero load's prototype fields, a taken
  item's from its object row) gets a *Use on self* button under the list: the hero heals itself through the session's heal
  check ([heal runtime](../world/heal-runtime.md)), and the message line names the amount, the cure and the uses left or the item
  used up. No pre-made kit and nothing on the Crash Site exterior is a healing item; the synthetic self-test covers the button.
- **Repair (ARC-SKILLS-007).** A damaged or broken item in the PC's list (its condition: the session's record over the hero
  load's or the object row's baseline) gets a *Repair* button: the hero repairs it with its Repair skill
  ([repair runtime](../world/repair-runtime.md)), and the message line names the hit points repaired, "nothing to repair" or
  "the repair restored nothing". The native smoke damages an original hero's kit item and repairs it through the button.
- **Taken items.** An item the PC took is neither drawn nor picked any more (the canvas
  skips its body; the hover pick passes over it), also after a quick load. The resource
  sweep of the native smoke counts those bodies as taken.
- **Window.** `I` (the original Inventory key, logical key down) toggles it while the area
  inventory exists; I, Escape, the Close button or a controller cancel close it. It is
  modal like the dialog panel (actor gate closed, the clock keeps flowing, ARC-DEV-126, palette chord refused,
  every key owned by the window). Left: the PC's list in the order items entered it, each
  with its display name (the hover rule: the examine function's (0x441B60) order from the description
  tables, type fallback without them), type and weight. When looting, right: the dead NPC's
  current list with a *Take* button per item and *Take all*; a refusal stays on the body and
  is named in the message line. ARC-SCRIPT-013: an item with its own `SAP_INSERT_ITEM`
  script runs it through the SCR dispatcher with the game's script content (on the installed
  exterior the amulet: script 1341 prints line 1 of its DLG, shown after the item's name in the
  message and status line, until global flag 1085 is set); without the content, or for a script
  that does more than the item set admits, it stays: "the item runs a script on this transfer
  that is not supported here". ARC-SCRIPT-041: the get, transfer and remove-item scripts run the
  same way ([item events](../world/inventory-runtime.md#item-events-arc-script-041)); an item a get
  or transfer script declines stays, named "the item's get script declined it" or "the PC's
  transfer script declined it" with any line it printed, and the log lists every run as
  `scripts=point:script:stop`. Project
  diagnostic window: no stacks (ARC-UI-015 added the paper doll and the weight line, ARC-UI-019 the grid and the drag, ARC-UI-025 the drop and the use); the
  host's insert check accepts every item (the room for an item entering from outside is NOT READY; the session answers it for the
  window's moves, ARC-UI-019); since ARC-WORLD-069 the session refuses a technological item for a Technophobia hero
  and since ARC-WORLD-085 an item past the hero's carry weight ([weight](../world/inventory-runtime.md#weight-and-encumbrance-arc-world-085)).
- **Paper doll, wear and take-off (ARC-UI-015).** Above the PC's list the window shows the hero's nine wear slots in slot order
  (helmet, ring 1, ring 2, medallion, weapon, shield, armor, gauntlets, boots; the original's locations 1000..1008) with the worn
  item's name, a *Take off* button per worn item and a *Wear (slot)* button per slot an item of the PC's list can go to (its wear
  location; a ring offers both ring slots). The buttons stand in for the original's drag of a held item onto a doll slot or onto the
  grid ([ledger IW1–IW9](../research/ledger/ui-audio.md#inventory-window-arc-ui-015)): *Wear* runs the session's `Equip`
  ([equipment](../world/equipment-runtime.md)) with the wear check, the occupant's no-drop and insert checks, and an occupant goes
  back to the pack (in the original it lands in the grid and is then held on the cursor, IW3); *Take off* runs `Unequip`. A refusal
  is named in the message line (the wear rule, the no-drop flag, a Technophobia hero's technological occupant, an item script or
  item spell that cannot run; a move between the pack and a slot changes no carried weight, so weight never refuses it). Moving, wearing and taking off cost no action points, in a turn-based round too (IW8). The viewer has one diagnostic
  body art, so it answers the wear check's art lookup with yes (as the best-items start does). The item's wield and insert/remove
  scripts run through the item script host; its item spells through the shown map's cast host (`SessionItemTriggers`), whose item
  lookup knows the hero's kit, taken and made items (ARC-UI-016, ARC-DEV-305) and the shown map's NPCs' (ARC-UI-031); the same triggers fire the worn
  items of the cast host's own spell damage, payments and passes (ARC-UI-031). Wear values come from the hero load's kit (the best-items start's), a
  taken item's object row and, for an item the session made (a `made:` entity, no row in any map), its prototype's fields (ARC-DEV-305, B36, B01:
  `AreaInventoryHost` gets the prototype reader, `AreaPrototypeItems.Fields`; its use, healing, lockpick, item-spell and condition facts too, and the window's line offers
  the same actions as a taken item's). A made item's writes (condition, charges, scripts) stay the session's; a copy an NPC was given keeps the prototype only (an instance's own fields are not carried). ARC-DEV-315 (B36, B01): the readers that looked an object row up by entity read a made item's prototype too, through one pure helper (`AreaMadeItems`, in `Arcanum.World`: the saved kind of a hero's made item, of a made or created item lying on the ground, and, when the holder is named, of a made item added to that NPC; the row is tried first): the art of an NPC-thrown made item (`AiLaunchThrown`: the row's art when the item has a row, else a made item's prototype current art), the hero's throw art (`HeldThrowArt`: a made weapon's missile art and, for any made item, its own art), the audio cues (`EntitySounds`: a made, created or placed made item's prototype sounds, for the pickup, the weapon's use sound and the other item cues) and the NPC armour a hit sounds on (`AreaCombat.WornArmorEntity`: the armour's entity, row or none, then `EntitySounds` with the NPC as holder, so a made armour and a starting-kit armour given to the NPC sound through their prototypes too; `WornArmor` still returns the row). The hover reads (`DescribeItem`, `HotkeyItemIcon`) take the hero's kit, taken and made items' kinds from `HeroItemKind` and fall back to the kind's prototype for the icon, name and colour when no row is found (`AreaItemIcons.Describe` and `Icon`; checked by reading, no change); NOT READY: the hover's examine-script step runs only for an item with a row, so a made item's examine script (attached from its prototype) is not run, and a made item held by an NPC has no hover kind (a made or kit item the hero dropped on the map has one since ARC-UI-1451, from the session's placed items). A made no-drop flag and the thrown item's spells are read here. The header adds the original's weight lines (IW9): the carried
  weight and the encumbrance level with the weight up to which it holds (`CarryRules.Threshold`). Without a hero the doll reads
  "no hero: nothing can be worn". Stack splitting is built for the drop and a dead NPC's take (ARC-UI-029, below); the hero's gold pile moves in the grid since ARC-UI-090, drops since ARC-UI-091 and throws since ARC-UI-094 (below). NOT READY with a worn taken
  (non-kit) item: the hero's attack wears none of its items (`AreaCombatHost.HeroWorn` knows only kit durability values), and a
  taken ranged weapon has no missile art or throw weight (`WieldedKitNumber`).
- **Grid and drag (ARC-UI-019).** Above the lists the window draws the doll's nine slots and the hero's 10 × 12 grid in the original
  window's frame ([ledger IW14](../research/ledger/ui-audio.md#inventory-grid-arc-ui-019); `AreaInventoryDrag`, plain shapes and
  names, no original art), scaled to the window. The grid is the session's (`QueryHeroGrid`,
  [hero grid](../world/inventory-runtime.md#hero-inventory-grid-arc-ui-019)): every item takes its size in cells (its inventory art's
  frame in 32-pixel cells, ARC-UI-055; 1 × 1 without the game's art) at the first free place, and the items without room are named in the hint line. A left
  press on an item of the grid or the doll holds it (it stays in its place and is not drawn there, IW2); the next left release puts it
  where the pointer is: on a cell, the target is the cell minus the grab's offset in grid locations (IW10), a grid item moves there
  (`MoveHeroItem`) and a worn item comes off into it (`Unequip` with the slot); on a slot, `Equip` runs with its checks and an
  occupant lands in the first free cell and is then held on the cursor (IW11); a ring goes to the other ring slot (IW13); anywhere
  else the item goes back. Every refusal leaves the item where it was and the cursor empty, named in the message line ("no room for
  it at row r, column c", "no free room in the grid", the wear rule, the no-drop flag). A press while an item is held waits for its
  release and a right click does nothing (IW15); closing the window puts a held item back (IW1). The buttons stay for the keyboard and
  the controller. The log line `ARC-UI-019 drop kind=… disposition=… failure=… verdict=… displaced=…` names each release's outcome. A
  kit item's no-drop flag and scripts come from the hero load (`AreaHeroLoad.KitGridMove`), a taken item's from its object row; a made
  item has none here (NOT READY). The native smoke drags a grid item to a free cell and back and the first worn item off into a free
  cell and back onto its slot (`ARC-UI-019 native-smoke drag PASS dragged=4`).
- **Drop and use (ARC-UI-025).** Right of the grid the window draws the original's drop and use icons at their corners (703, 385 and
  703, 331 on the window; plain 32-pixel squares, the icons' art size is not read; [ledger IW16–IW19](../research/ledger/ui-audio.md#inventory-drop-and-use-arc-ui-025)).
  A held item released on *Drop* (or a *Drop on the ground* button) lies on the hero's tile through the session's `DropHeroItem`
  ([hero drop](../world/inventory-runtime.md#hero-drop-and-inventory-use-arc-ui-025)): its drop script, the no-drop flag, its removal
  events and the placement into a junk pile or loose with its decay; an ammunition stack goes the stack path after the quantity window
  (ARC-UI-029, below). A right click on an item, on its release as in the original (mouse subtype 3, ARC-UI-032; the grid takes the release only after a right press on it, where the original takes any right release over the window), (or its *Use* button, shown for an item with `OIF_CAN_USE_BOX` that is not worn and has no
  wear location) uses it with the hero as target; a worn or wearable item does nothing (IW5). ARC-DEV-306 (B32): the use, wear and move facts of an item the hero took on another map are read from its origin map's row through the host's record resolver (before, the use ended at "use facts do not read here"). An item pressed in the grid and released over a hotkey slot goes on that entry (B31: the window's own area keeps the mouse, so the bar reports the release of a held item to the app, 0x57DC60). A held item released on *Use* is used with
  the hero as target while Shift is held and with no target otherwise; an item whose use is a throw takes the map drop onto the game
  cursor there (IW18, ARC-UI-063, [below](#inventory-throw-from-the-game-cursor-arc-ui-063); in a loot or steal window it is NOT
  READY). In a turn-based round both need the hero's turn. The use is the original's 0x462CC0
  (`AiExecutorRules.UseItemNow`) over the window's world: a scroll the hero is not clever enough for stops; an item that needs a target
  and has none opens the targeting cursor (lockpicks arm the lock pick, as *Pick a lock* does; any other item's cursor is NOT READY);
  a trap device's use is NOT READY; the item's use script (a taken item's) may stop the use; an item with spell charges or the magical
  flag casts its slot-0 spell through the map's cast host (`SessionItemTriggers.UseItem`); otherwise a written item opens the reading
  window, a healing kit heals the hero, food and scrolls are spent; 4 action points on the hero's turn of a round. Log lines
  `ARC-UI-025 use item=… source=… step=…` and `ARC-UI-025 ground-drop item=… disposition=… cell=… pile=…`; the native smoke right-clicks
  a worn item (nothing), uses the first usable grid item when the hero holds one, drags a taken item onto *Drop* and takes it back
  (`ARC-UI-025 native-smoke drop-use PASS`).
- **Quantity window (ARC-UI-029).** A dead NPC's gold pile or ammunition stack of more than one unit taken with its *Take* button,
  and an ammunition stack of the hero's dropped (the *Drop* icon or button), first ask how many units move: a row above the doll
  shows "How many of NAME? COUNT of MAXIMUM" with *All*, *+1*, *-1*, *Accept* and *Cancel* (the original's bar mode 9,
  [ledger QW1–QW6](../research/ledger/ui-audio.md#quantity-window-arc-ui-029)). It starts at 0; the keypad digits append (capped
  at the stack), Backspace and Delete erase the last digit, Enter accepts, Escape and I cancel; the main-row digits do nothing.
  While it asks the rest of the window, the doll and the grid do nothing (QW6); closing the window cancels it. Accept with 0 moves
  nothing. The moves are the session's `TakeGoldFromNpc`, `TakeAmmoFromNpc`, (a junk pile's, ARC-UI-1328) `TakeGoldFromJunkPile`, `TakeAmmoFromJunkPile` and `DropHeroAmmo`
  ([stack drop](../world/inventory-runtime.md#stack-drop-and-the-quantity-window-arc-ui-029)). *Take all* moves whole stacks
  without asking, a gold pile too (QW1; a session without a hero refuses the gold, as it has none to hold it); a stack of one unit moves at once by the same stack path. The new stack keeps the moved
  stack's kind (the original makes the type's base prototype, QW5; every installed stack is one). A dead NPC with two gold piles
  answers with its first pile (the session's `TakeGoldFromNpc`, NT1), so a take from the second asks with its coins but moves from
  the first (no installed NPC holds two). The loot list
  shows a gold pile's coins (×N) and the header the hero's gold and its four ammunition counts (arrows, bullets, charges, fuel; IW9).
  A junk pile's stacks ask the same way (ARC-UI-1328): the session moves the count and leaves the rest in the pile, which keeps its items and its dissolve rules (a window that shows it keeps it standing; closing it dissolves one item or none); a container's stacks asked since ARC-WORLD-101. NOT READY: the question in the barter window. **Art pane (ARC-UI-1884).** With the interface bars up and art 298 read, the question is the original's pane in the bottom strip
  instead of the diagnostic row ([QW3a](../research/ledger/ui-audio.md#quantity-window-arc-ui-029); `QuantityPaneRules`, `AreaQuantityPaneView`): the background art 298 over the message
  window's place, the five buttons (All 299, up 805, down 804, Accept 33, Cancel 32; the bar's push buttons, which act on the release over them), the kind's icon at (269, 534) (an item art of
  place 2, `item_paper.mes`: gold, or the stack's ammunition type; none for a type the pane leaves unset) and the count in the roller font (art 25, black) at (404, 545), six
  cells without leading zeros. The diagnostic row stays focusable (keyboard and pad) but is neither drawn nor clicked while the pane shows; without the bars, or when the background, any of the five buttons or the count's font did not read, it is the question as before.
  The pane draws the Legacy art and, with the HD profile, the HD frames of the arts that have them; with the Generated profile the background and the three buttons 299, 33 and 32 are generated chrome ([S15](hd-interface-density.md#viewer-slice-s15-arc-ui-1818), ARC-UI-1818); the icon, the + and − buttons and the count stay pictures. The self-test takes 2 of 12 synthetic arrows by keypad and buttons, cancels with Escape, and drops 1 of the 2
  (`ARC-UI-029 self-test quantity`); the native smoke drops one unit of an ammunition stack of the hero's
  through its *Drop* button, keypad 1 and Enter, and takes it back (`ARC-UI-029 native-smoke quantity PASS`; the crash site holds no
  stack, so 5 arrows of the installed prototype are lent to the hero through an NPC there first and stay with it).
- **Original look (ARC-UI-051).** With the game's art the player's own window (no loot, steal or barter partner) shows the
  original window instead of the diagnostic panel ([plan](../ui/original-windows.md#inventory-window-done-arc-ui-051),
  [ledger IW20–IW25](../research/ledger/ui-audio.md#inventory-window-art-arc-ui-051); `AreaInventoryArtLayout`, `AreaInventoryArtContent`):
  the 800 × 400 frame at its place between the interface bars, scaled to the free part, with the paper doll (223) and pack (221)
  arts, a cover art on every empty wear slot (241–249), each item's inventory art (field 0x5D) centred in its slot or cells
  (scaled down only when larger than its place; a grid item's cells fit its art since ARC-UI-055; a worn item on the cursor
  takes its art's cells), the drop and use boxes (224/225, 342/343, "on" under a held item; their hit
  areas are now the arts' 80 × 43), the weight, encumbrance and speed lines (`inven_ui.mes` 9, 6, 10, 11 and `critter.mes`
  18 + level) and the gold and ammunition counts in the project's font. Every action of the diagnostic window stays: the doll, the
  grid, the boxes and a right click act as before, and a *Diagnostic view* switch below the frame shows the diagnostic panel (its
  buttons) and back. The synthetic self-tests have no game folder and keep the diagnostic look. Grid items take their art's cells
  (ARC-UI-055, `ARC-UI-055 native-smoke item sizes PASS` in the same stage). NOT READY: the paper-doll art of worn weapons and armour (fields 0x71, 0x97), the original fonts, the other modes' art and the doll
  toggle (plan). The native smoke checks it in the inventory stage (`ARC-UI-051 native-smoke inventory-art PASS`: every window
  art read, every grid and worn item's art read, three lines).
- **Loot window art (ARC-UI-052,** [plan](../ui/original-windows.md#loot-window-done-arc-ui-052),
  [ledger IW26–IW32](../research/ledger/ui-audio.md#loot-window-art-arc-ui-052); `AreaLootArtLayout`): with the game's art a dead
  NPC's, junk pile's or container's window (ARC-UI-065: and the steal window, below) shows the loot window's look in the same frame: the `Loot` art (234)
  on the left with the body's image at (183, 33) (by race or critter flags, a container by its description), the body's grid of 10 × 8
  cells from (8, 136) with each item's inventory art at its inventory location, over its art's cells (ARC-UI-055, `QueryItemCells`; a junk pile's at its pile slot; PROJECT: an item the
  session keeps at no location at the first free place; worn items left out, as the original's grid view), cut
  to the shown rows and scrolled by the mouse wheel (maximum 4 rows, a container 88), the Take all button (808) at (301, 47), and the
  hero's pack, grid, counts and boxes as in the own window (no doll, no weight lines). A body's item pressed and released on the hero's
  grid is taken through the same path as its Take button (a stack asks how many; PROJECT: into the first free cells); released on a
  box (which shows the refused art 348 / 347) or elsewhere it goes back; ARC-UI-065: a right release on a body's item takes it the same
  way (IW36), on a hero's item it does nothing here. The "Diagnostic view"
  switch keeps the Take buttons. **Body doll view (ARC-UI-072,** [ledger IW29–IW30](../research/ledger/ui-audio.md)**; bug B30):** the loot and steal windows of a
  critter (not a container) show the toggle buttons 338 (doll) and 339 (bag) at (23, 108) and (74, 108), which act on a release over the same button;
  the doll view draws art 349 at (0, 134) and the body's worn items (`AreaInventoryView.BodyDoll`, by the worn item's inventory location) in the nine slot
  rectangles of table 0x5CAE08, a cover (0x5CAF58) on each empty slot, and hides the grid; a worn item pressed on its slot and released on the hero's grid
  is taken through the loot path (PROJECT: its inventory art, and a cover, stretched into the rectangle; the wheel does not scroll the hidden grid). **Doll values (ARC-UI-1121,** [ledger IW41–IW43](../research/ledger/ui-audio.md#doll-values-and-weapon-overlay-arc-ui-1121)**):** the doll view prints the body's displayed damage rating (right-aligned in the box at (105, 354)) and armour rating (from the left of the box at (227, 354)), read by `AreaCombatHost.DollRatings` into `AreaInventoryView.BodyRatings`, and fills the weapon's slot dark red when the weapon needs more strength than the body has (the numbers use the project's font). The hero's own window prints the same two numbers (ARC-UI-1267, ledger IW25: the damage rating at the right edge of the box at (80, 358), the armour rating from the left edge of the box at (261, 358), read by `AreaCombatHost.HeroDollRatings` into `AreaInventoryView.HeroRatings`) and fills the hero's weapon slot dark red when the weapon needs more strength than the hero has. NOT READY: the
  the hero's own doll toggle, the scroll bar's art, a hero's item moved into the body, a follower's portrait. The native smoke checks every Crash Site body's window
  (`ARC-UI-052 native-smoke loot-art PASS`: loot arts and the image read, every grid item's art, one item taken by a drag). The `loot-worn` stage
  (ARC-DEV-202, ARC-UI-072) drives the doll view: toggle, the worn item on its slot, the drag to the hero's grid, back to the bag view
  (`ARC_AREA_LOOT_DOLL_CAPTURE=PATH.png` saves the doll view; ARC-UI-1121 makes it require the body's ratings). **Stat hot spots (ARC-UI-1924,** [ledger IW46](../research/ledger/ui-audio.md#doll-values-and-weapon-overlay-arc-ui-1121)**):** the hero's art buttons 676 and 677 and the spots over the damage and armour numbers (all windows that print them; modes 1 to 4 once the hero's doll toggle is set) and the other party's two 55 × 40 spots (its doll view on) show the weapon or armour stat lines (`AreaStatLines`, `AreaMessageKind.StatLines`) in the bottom strip on a release (the bar goes to the message mode first) or on the pointer entering them (a pane mode gets only the title). The stages `inv-ui` and `loot-worn` drive the windows (`ARC_AREA_STAT_CAPTURE=PREFIX` saves the own window before and after and the strip's lines for a graphics report), `cast` drives the pane-mode case (its skill pane step).
- **Arrange button (ARC-DEV-339,** [ledger IW37–IW40](../research/ledger/ui-audio.md#arrange-button-arc-dev-339)**; bug B54):** every mode of the original look
  (own, loot, steal and barter windows) draws the button 340 `tiny_butt` at (763, 197) of the window (`AreaInventoryArtLayout.ArrangeButton`, drawn with its
  first frame); a press and a release on it (nothing held; sounds 3000 and 3001) lay the hero's items out again with
  `GameSessionOwner.ArrangeHeroGrid`: the items on worn and quick slots stay out, the others are sorted tallest, then widest first and take the first free place
  row by row (the first press after the window opens) or column by column (the next one), alternating. When one item finds no place nothing changes. The original
  runs no insert check, no item event and no action points. NOT READY: the shop NPC's grid (the viewer's wares have no grid slots), the pressed frame.
  The `inv-arrange` stage of the native smoke drives it.
  **The open's own arrangement (ARC-RES-1410,** [ledger XR16 (g)](../research/ledger/spell-runs.md#magic-audit-executable-reads-arc-dev-357)**):** the open of the loot (mode 2), steal (mode 3)
  and See Contents (mode 4) windows lays the shown holder's grid out again before it draws (`GameSessionOwner.ArrangeHolderGrid`, row by row, the same sort; 96 rows
  for a container, 12 for a critter, worn and quick-slot items stay out, no room writes nothing), so a taken item's old cell closes up at the next open. The slots are
  saved with the inventory section (ARC-RES-1441, schema 95), so a restored session draws the arranged layout. The `container-click` stage's loot smoke checks the arranged layout at the open and after a take.
- **Steal window art (ARC-UI-065,** [ledger IW35–IW36](../research/ledger/ui-audio.md#steal-window-arc-ui-065)): the window of a
  pick-pocket victim (mode 3) draws the loot window's look without the Take all button (IW30), with the victim's image by its critter
  flags or race and the round view. A victim's item pressed and released on the hero's grid is one theft attempt, a hero's grid item
  released on the victim's grid one plant attempt (`AreaInventoryDropKind.Plant`), and a right release on either side's item makes the
  same attempt (PROJECT: the item goes to the receiving side's first free place); the boxes refuse the victim's items and drop or use the
  hero's. The "Diagnostic view" switch keeps the Steal and Plant buttons. The `pickpocket` stage drives the theft and the plant by
  drags and the first plain theft by a right release when the original look shows (`Look` and `Dragged` in its PASS line;
  `ARC_AREA_STEAL_CAPTURE=PATH.png` saves the window).
- **Round view (ARC-UI-054,** [ledger IW33–IW34](../research/ledger/ui-audio.md#round-view-arc-ui-054); `AreaInventoryHole`): the
  window's open centres the camera on the PC, and in the original look of the own window and the loot window the 89 × 89 rectangle
  at (11, 9) or (16, 17) shows the screen's square of 178 game pixels around the PC (centred 15 game pixels above its foot point) at
  half size, copied by a screen-texture shader on canvas layer 3 under the window (the background art leaves the square out, as
  the original's copy replaces it; the doll art 223 is opaque there), with art 233 or 232 drawn over it when it reads.
  A left release in the rectangle closes the window (in the loot window a held item goes back first). The message line under the
  frame keeps one line in the original look (cut with an ellipsis), so a message such as `took N item(s)` no longer shrinks the
  frame. ARC-UI-060: the rectangle, its art and the release are a table by the window's mode (`AreaInventoryMode`: own (11, 9) and 233,
  barter (16, 17) and 231, loot and steal (16, 17) and 232; `AreaInventoryHole.Release`: nothing held closes in every mode, a held item
  goes back and the window closes outside the own window); the barter window's round view shows with its original look since
  ARC-UI-064 (art 231, below) and the steal window's since ARC-UI-065; the component is `AreaRoundView`, shared by the inventory and
  barter panels. ARC-UI-063: in the own window a held item released there
  is the map drop of IW18: it goes onto the game cursor and the window closes ([below](#inventory-throw-from-the-game-cursor-arc-ui-063)). The native smoke
  checks the scale and the round view on the Crash Site bodies and the round view in the own window, and the release on the first body
  (`ARC-UI-054 round view release closed the loot window`); the own window's release is covered by the unit test of the rectangle only.
- **Dialog.** Virgil's negative `IN` (the ring) goes to the same session inventory through
  `SessionDialogInventoryPort` (the conversation host's `Inventory` port), so the ring
  appears in the window, is saved by F7 and comes back with F8.
- **HUD.** The status line shows `PC items <n>` and the last pickup/loot result.

### Inventory throw from the game cursor (ARC-UI-063)

The original lifts a held item onto the game cursor and throws it from there (ledger IW18, TT1, TT2, TT6, TT7, CU7); the session's
throw is ARC-COMBAT-023's `ThrowHeroItem` ([hero attack runtime](../world/hero-attack-runtime.md#inventory-throw-arc-combat-023)).

- **Gold pile (ARC-UI-090).** The hero's gold is a grid item: the pile of type 8 the original keeps in the hero's list, named by critter field
  0xE8 and placed like any item (ledger HG1). The window draws it at its cell with the gold art (prototype file 9076) and the name `gold ×N`;
  it appears with the hero's first coin and goes with the last, and a press and release on another free cell moves it (the session's
  `MoveHeroItem` for `gold:pc`, HG2). Released on the drop icon it asks how many coins (the quantity window, QW1), and the count lies on the hero's tile as a gold item
  (a junk pile when items lie there, HG4); a pickup (or the junk pile's take) turns it back into the hero's coins. Released anywhere else it goes back
  with a notice. Released on the round view or lifted by a hotbar key it goes on the game cursor as a stack (ARC-UI-094: the quantity question asks how many coins, the hotkey takes them all), and a left press throws that many coins (session `ThrowHeroItem`); the coins fly as the gold prototype's art and land as a gold pile to pick up. Native smoke: `ARC-UI-094 native-smoke gold-throw PASS`. The coins stay the session's counter (`QueryGold`); the grid record `gold:pc` carries the cell (schema 76).
  Native smoke: `ARC-UI-090 native-smoke gold-pile PASS` and `ARC-UI-091 native-smoke gold-drop PASS` (the Crash Site smoke's inventory part).
- **Lift.** In the own window's original look, a held item released in the round view, or on the use icon when its use is a throw
  (`OIF_USE_IS_THROW`), is the map drop with flag 4: `AreaHeldThrow.Lift` refuses a no-drop item (the original's remove check: the
  item stays and the window says why) and, NOT READY, an item with a throw script together with an insert-item script or a stack with a throw script;
  anything else goes on the game cursor and the window closes (ARC-COMBAT-1955: the item's removal and throw scripts run at the throw through the shown map's script host, their lines go to the message window and their eye candy plays; a throw script that
  keeps the item posts "its throw script kept it" and the item stays in the pack). Steamwoven keeps
  the item in the hero's pack or slot until the throw (the original removes it at the lift), so a put back only empties the cursor.
- **Cursor.** While an item is on the game cursor, the cursor's image is the item's inventory art with its top-left corner 2 pixels
  up and left of the pointer and the cursor art over it on its own hot point (`AreaHeldCursor.Compose`, CU7; the original sets the
  hot point at (2, 2) of the item art, the overlay's exact pixel place was not read). Without the item's art the cursor stays the
  plain art. PROJECT: the inventory window keeps its own drag image (the original's cursor there is the same CU7 image), drawn at the
  window's scale with the grab offset its drop rule reads (IW10–IW13). The status line shows the item and its thrown range
  (`QueryHeroThrowRange`).
- **Press.** A left press in the game view, under the world press's guards (another critter's turn ignores it), throws the item: at
  the tile of the object under the pointer (0x54ED30), else at the pressed tile. A hero that cannot act puts it back (0x575770; the
  original's press on an object then leaves it held). The struck critter is the line of fire's blocker when it is a living NPC with
  combat values, else the living NPC on the tile (TW4); a companion there is NOT READY (the item stays on the cursor). In a turn-based
  round the throw first charges `QueryHeroThrowCost` (Steamwoven 2, the Original setting 0, TT4; a wielded boomerang still away or a
  hero that cannot act puts the item back before the charge, and a throw ends the hero's walk); so does a "default throws" weapon's
  attack, which used to charge the attack's cost. Since ARC-DEV-182 (MIG13 S12b) the press starts the hero's throw goal 37 of the
  [goal table](../architecture/goal-table.md): the points are charged and the throw art (animation 14) starts at the goal's first
  node (the cursor empties there, TT2), the item leaves at the animation's action frame and the goal ends after the rest of it; a
  new click, the stick, a window or the hero's fall cuts the run and leaves the item in the pack. The shots fly on the projectile host of the thrown dagger: a hit or a miss at the
  struck critter as the critter attack's flight, a throw without one (or a miss's scatter) to its destination without a target; the
  art is a weapon's projectile art, else the item's own art as it is (PF14: the original's rotation setter leaves an item art alone, so a
  thrown item shows no rotation), and an item that is not a weapon is drawn on PF11's size arc: `ProjectileRules.ThrownScale` sets
  its size at every tile it leaves (50 % at the ends, 200 % at the middle of its flight), drawn around the art's hotspot
  (`AreaScenePlan.ScaledAboutHotspot`; PROJECT: the original's renderer was not read for the anchor).
  The item lands where its flight ends. A refused throw empties the cursor with the reason (the item never left).
- **Put back.** A right release in the game view (0x54E228), a window opening, a map change, or the item no longer held puts it back.
- **Smoke.** The ranged stage (needs `--hero`) lifts the kit weapon from the round view (straight from the window without the art),
  checks that a window opened puts it back, then throws it three times from the cursor at its target's tile and checks the flight and
  the landing: `ARC-UI-063 native-smoke cursor-throw PASS` (the held cursor art is reported, `cursor=held-art`). Unit tests:
  `AreaHeldThrowTests`.
- **Ammunition stacks (ARC-UI-086, QW1, QW5, IW18).** The lift of one of the hero's ammunition stacks puts it back and reads its quantity as
  the original's move does: one unit lifts at once, more open the quantity window (interface mode 13, over the inventory window; keypad
  digits, Enter, I as for the drop), and 0 lifts nothing. The accepted count rides on the cursor (`GameCursorItem.Count`, the status line
  shows the thrown range of those units), and the press throws it: `SessionHeroThrow.Count` and `SplitKind` make the session split the units
  off into a new made stack at the moment the item leaves (the original at the lift; the stack path runs no remove check and no remove-item
  event, so the no-drop flag and the remove-item script do not refuse it), and that stack flies, lands and lies on its tile with its units. Its
  range, check and damage are those of its units' weight (WE1: the stack's weight is its unit weight times the units / 4). Smoke: the Crash
  Site smoke's quantity step lifts one unit, throws it and takes it back (`ARC-UI-086 native-smoke ammo-throw PASS`).
- **From the hotkey bar (ARC-UI-087, HK15, TT1).** Two ways put an item on the game cursor without the inventory window
  (`HoldItemOnGameCursor`, the lift's refusals, an item held before put back, the window closed): the activation's Hold (a key or click on
  an item entry whose item is neither a weapon, an armour, ammunition, gold nor a use that is not a throw: 0x54FCF0 → 0x573740, which
  runs the same move with flag 4 as the map drop, so a pack item with the throw use is lifted by its key), and the right-drag of an
  item entry released off the bar (0x573630 puts the item on the cursor without a removal and it stays there; the entry itself is
  emptied, the fix of the original's lost hidden entry). The original's throw of an item lifted by the second way never removes it from the
  hero's list (0x4B3C00 skips the removal 0x466E00 for a critter thrower and 0x4415C0 only places the object), so the item would be in the
  pack and on the ground: a technical bug, fixed in both modes by the item leaving the list at the throw, as the inventory's. An ammunition
  stack goes whole (no question: the quantity question belongs to the window's moves). Smoke: the hotkey bar's check in the `cast` stage drags an
  item entry off the bar (`ARC-UI-087 native-smoke hotkey-lift PASS`).
- **Not ready.** The use icon's throw is the own
  window's only (IW6, IW18: the drop path with flag 4 exists in mode 0, the loot and steal windows are other modes where the item
  goes back), so those windows have none to build.

Checks: `AreaInventoryHostTests` (targets, taken items, transfers, window lines, re-attach
after a load and a mismatched save, the dialog port), the opt-in `CrashSiteInventoryTests`
(`ARC_AREA_CONVERSATION_GAME_ROOT`: seeds built by `AreaInventoryMap.Build`, every NPC on an
admitted cell with life evidence, every core ground item and dead NPC a target, Virgil's ring through the viewer's conversation host, 5 of 6 loot
without script content and the amulet's script with it (ARC-SCRIPT-013), 37 pickups, save and load), the self-test `ARC-UI-004 self-test PASS` (synthetic seed over the
production click path: walk-to-pickup hover and press from 2..6 cells, its cancel by a ground
click, pickup next to the item, I/Escape, loot by the take button; ARC-UI-015: two ring wear buttons and a take-off button that run
the app's wear and take-off), `AreaInventoryWearTests` (ARC-UI-015: doll lines, wear slots, swap, take-off, refusals, the weight facts)
and, with the cave input, the native smoke `ARC-UI-004 native-smoke inventory PASS`
(one pickup next to an item, one walk to pick up, the four dead NPCs looted with *Take all*:
5 taken, 1 left, every taken item named in the window; `ARC_AREA_INVENTORY_CAPTURE=PATH.png` saves a
readback of the open window; ARC-UI-015: with `--hero` every worn item comes off by its button and goes back on by its wear
button, `WornCycled` in the PASS line) and the quick-save phase, which now
also checks that F8 brings back the PC's items. Not verified: original inventory UI parity
(grid, drag and drop; ARC-UI-051 draws the own window's art and layout and ARC-UI-055 the items' sizes; the fonts are not the original's), a human on a physical mouse and display.

### HD render path (ARC-RENDER-014)

`area` and `world-diagnostic` can draw characters and objects from an HD pack
([format](../formats/hd-pack.md)) under the rules of the
[HD presentation contract](hd-presentation-contract.md) (FR-PRODUCT-004). Code: Godot-free
[`HdFrameSource`](../../tools/terrain-viewer/Core/HdFrameSource.cs),
[`HdResidency`](../../tools/terrain-viewer/Core/HdResidency.cs),
[`HdPresentation`](../../src/Arcanum.World/HdPresentation.cs) and the diagnostic
[`HdNearestPack`](../../tools/terrain-viewer/Core/HdNearestPack.cs); the canvas partial
`TerrainCanvasHd` and the `AreaDrawItem` crop and fade uniforms.

- **What binds.** AREA bodies, walls (art class 1) included since ARC-RENDER-016, the
  controlled actor and, since ARC-RENDER-016, the composed level-0 terrain chunks (tiles and
  facades); WORLD statics and the actor. Decimated chunks (zoom below 1 with an integer
  origin), per-command terrain and WORLD terrain stay Legacy and are not candidates. The scene
  plan, picking, routes, passability, the wall check, light and Session read Legacy data only;
  HD is substituted when a texture is bound to an already planned draw.
- **Terrain chunks (ARC-RENDER-016).** A chunk item at level 0 asks for the chunk's question
  (`HdFrameQueries.FromTerrainChunk`: coordinates, composed Legacy RGBA hash, tile digest; made
  once per chunk and map from one compose on the main thread, whose bytes also serve the first
  request; a chunk requested again after its pixels were released is composed again there, the
  decode stays off the main thread). Without a pack, with a rejected pack or with a pack that has
  no chunk entries nothing is composed (`NoPack`, `PackRejected`, `FrameMissing`). It binds the `N·512` square HD chunk with the
  scaled crop to the unchanged world square, keeping its ambient channel. Chunks use their own
  residency under the device's terrain budget (`HdDomainBudget.Terrain`: `TerrainBytes` for
  visible and kept chunks, ceiling `N²·192` MiB, and at most 1,024 kept chunks), not the AREA frame
  limits; the two residencies share the frame source and each drops only its own decoded
  pixels. The Legacy chunk textures stay resident under both profiles.
- **AREA.** An HD item keeps its Legacy destination, reflection, phase and order and samples
  the crop `(N·c, N·m)` with the exact nearest rule (`AreaNearestAxis`, no new mode); the
  actor's crop is its whole Legacy frame. The wall-fade column is the Legacy column
  `floor((sx − N·c) / N)` over the Legacy width (`fade_scale`, `fade_start`), so a faded
  item gets exactly its Legacy alpha. Index-row keys include the crop, so Legacy and HD rows
  never collide.
- **WORLD.** `DrawTextureRectRegion` with the HD texture, the same destination and mirror
  transform, and the source rectangle of the whole Legacy frame times N. The source of both
  profiles now comes from the frame size, not the destination size (equal at the admitted
  scale 100).
- **Frame questions.** AREA frames use their `AreaFrameKey` (ART payload hash, view length,
  group, frame, effective palette, entry name hash, Legacy RGBA hash). The actor and WORLD
  statics, decoded outside the AREA owner, record the SHA-256 of the whole ART entry, its
  length as the view, the entry name hash and their Legacy RGBA hash at load. For an ART with
  bytes after its last frame (ARC-DATA-016) the AREA view is shorter than the entry, so such
  an ART has two different keys; a pack built from one draws the other as `FrameMissing`.
- **Per-frame fallback.** Every outcome other than a valid HD frame draws Legacy with its
  reason: `NoPack`, `PackRejected`, `FrameMissing`, `SourceMismatch`, `FrameRejected`,
  `Invalid` (the pack reader's checks, including the mask check against this device's Legacy
  alpha), `HdAxisLimit` (`N·(c + m) > 65534`), `HdBudget` and `Pending`. Nothing fails the
  view, the map load or the session. The Legacy visible set stays resident under both
  profiles, so any fallback is immediate.
- **Decoding and uploads.** Frames are resolved off the main thread (half the processors);
  decoded pixels are held only until their texture is uploaded, at most 64 frames or 64 MiB
  per residency and submission (in AREA the frame and the terrain residency each) on the main thread; a frame that becomes visible later is decoded lazily and
  draws Legacy (`Pending`) until it is ready; a frame whose decode fails for any reason is
  `Invalid`. There is no whole-area HD CPU repository. The texture cache keeps the Legacy RGBA
  of the actor and WORLD static frames (a few MB, counted at load) in both profiles, because
  the mask check of an HD frame needs this device's Legacy alpha.
- **Budgets.** One residency per domain (AREA frames, AREA terrain chunks since ARC-RENDER-016, WORLD), keyed by the pack question inside one pack (a new
  pack or budget releases every HD texture). Admission is in draw order on the Legacy frame
  size: a frame whose `N²·w·h·4` bytes would exceed the visible limit is `HdBudget` before it
  is decoded; a frame already known to be refused takes no budget. AREA: visible bytes, retained frames and bytes from the device budget
  (ceilings `N²` times the Legacy ones); WORLD: one HD byte limit (`N²/4 · 256` MiB) for
  visible and kept frames and the Legacy key caps (184 + 5). Frames that left the view are
  kept within the retain limits, least recently used evicted first, before any new upload. A
  switch to Legacy keeps them down to the retain limit and drops pending decodes; a map change, reload, failure, pack
  change or exit releases every HD texture and drops pending decodes. A WORLD actor step
  republishes the plan and keeps them.
- **HUD.** The status line shows `visual HD pack <id> <N>x <review>: HD <n>, Legacy <m>
  (<reason> <count>, …), HD textures <count>/<bytes> bytes, decoding <n>` (chunks and walls
  counted with the frames; `; terrain Legacy below zoom 1` while the chunks are decimated),
  per submission; under Legacy `visual Legacy`, or `visual Legacy (HD pack <id>
  <N>x loaded)` when a pack is selected. In AREA it is refreshed with the status line.
- **Selecting a pack and the profile.** Since ARC-UI-006 the profile is a saved setting with an
  on-the-fly toggle ([below](#visual-profile-setting-and-toggle-arc-ui-006)). A diagnostic run may
  still override it for that run with environment variables: `ARC_HD_PACK=DIR` (one pack directory
  outside the repository; implies profile Hd), `ARC_VISUAL_PROFILE=hd|legacy` (`hd` without a pack
  draws everything Legacy as `NoPack`) and `ARC_HD_BUDGET_PATH=FILE` (default
  `user://hd_budget_v1.json`, which an automated run does not read (ARC-DEV-032); missing or invalid means the
  ceilings). The start line
  `ARC-RENDER-014 visual profile=…` names the pack id, scale and open status, never a path.

Checks: `HdPresentationTests` (scaled crop and axis limit; frame questions; the nearest pack at
2× and 4×; every per-frame reason from the frame source, reset and release; residency in draw
order, shared textures, deterministic budget refusal before decoding, budget 0, LRU retention,
the WORLD total limit, upload batches; since ARC-RENDER-016 composed chunks through a nearest
4× pack and the frame source, `FrameMissing`/`SourceMismatch` per chunk, tiles in plan order,
and two residencies sharing a source); `HdPackTests` (chunk binding across map slices, the
file, size and soft/legacy alpha checks on chunks, the tile digest); the self-test
`ARC-RENDER-014 self-test AREA HD PASS` (production AREA compositor with a synthetic nearest
4× pack, one body a wall since ARC-RENDER-016: the HD raster equals Legacy at
zoom 1, 2, 0.5, 1.5 with a fractional origin and 4, also with wall fades; a tinted pack keeps
Legacy alpha and changes colour at magnification 2; `FrameMissing`, `NoPack`,
`PackRejected`, `FrameRejected`, `HdBudget` and `Invalid` each draw the Legacy raster and are
counted; switching back and clearing release HD) and `ARC-RENDER-014 self-test WORLD HD
PASS` (statics and actor, identity at zoom 1, 2, 3; a pack recoloured per parent texel
equals a Legacy draw of the recoloured frames, which also catches a wrong crop on the uniform
statics; republish keeps HD); and the native
smoke on the installed original, which writes a nearest 4× pack of every character and object
frame to a temporary directory outside the repository, deletes it afterwards, and requires
the whole window to equal Legacy with no fallback:
`ARC-RENDER-014 native-smoke AREA HD identity PASS` (Crash Site exterior: 114 frames, 59
body frames and 55 actor frames (105 since ARC-WORLD-020 adds the run ART: 164 frames), 0.86 MB of PNG written in 0.4 s; four views at zoom 1, 2,
0.5 and on the widest body, 4.1 M pixels; up to 100 HD draws per view, 30 HD textures and
22 MB resident at 4×, first bind 90 ms; since ARC-RENDER-016 also every level-0 chunk the four
views can show: 53 chunks, 262 pack frames and 20 MB of PNG written in 5.7 s, no fallback in any view, up to 100 HD draws per view, 45 HD textures and 274 MB resident at 4× (15 terrain chunks, 252 MB, under the terrain budget), first bind 0.6–0.8 s; the smoke also requires HD chunks to be bound in every level-0 chunked view) and `ARC-RENDER-014 native-smoke WORLD HD identity
PASS` (57 frames: the 2 distinct static frames of the 5 statics and 55 actor frames; zoom 1, 2 and 4 on the actor, 1.4 M pixels; 6 HD draws per view, 3.9 MB resident). Not verified: other GPUs and handhelds, human review (ARC-RENDER-015). A real upscaled pack
of the Crash Site (generative characters and objects, x4plus terrain chunks and walls) was
built and looked at locally (ARC-RES-038, ARC-RENDER-016), outside the repository; the
exterior has no walls, so an HD wall is checked by the synthetic self-test only.

### Visual profile setting and toggle (ARC-UI-006)

The visual profile (Legacy or HD) is a saved setting that can be switched at any moment in WORLD and
`area`, following the toggle protocol of the
[HD presentation contract](hd-presentation-contract.md#toggle-protocol). It changes textures only:
plans, picking, routes, the wall check, light, animation, the clock and the Session never read it, and
it is not part of a snapshot or quick save.

**Interface art (ARC-UI-089, ARC-UI-095).** The HD redraws of the interface (the pack's `interface` slice, ARC-DEV-242) have their own
switch: the launcher screen has three project lines after Rules (ARC-UI-143; until ARC-UI-146 they were the options window's Game page lines, which no longer has them, the page ends with Rules). **HD graphics: Off/On** requests the Legacy or HD
profile exactly as the key does (saved, the same toggle protocol) and switches the sprites and terrain of an HD pack only.
**HD interface: Off/On/Generated (experimental)** (ARC-UI-095, default Off) is a separate saved setting (the `hd interface` line of `arcanum.cfg`; a missing line reads it as Off); it draws the interface art alone and does not touch the visual profile, so the two can be
combined freely (sprites HD with a Legacy interface, or the reverse). The third value (ARC-UI-140, `generatedInterface`, file version 5;
version 1–4 files read it as off) is the experimental [generated interface](hd-interface-density.md#viewer-slice-s1-arc-ui-140): it keeps the
HD interface on and draws the chrome of the two HUD strips and the bar buttons by code, with the pictures from the HD redraw (else from the
Legacy frame); only the drawing changes, every frame keeps its Legacy size, place, alpha test and hit area. ARC-UI-141 paints the chrome at
the window's exact whole device scale on a worker (the first draw no longer stalls), see [S2](hd-interface-density.md#viewer-slice-s2-arc-ui-141); ARC-UI-148 adds the inventory window's two frames at that window's own (fractional) device scale, see [S3](hd-interface-density.md#viewer-slice-s3-arc-ui-148) and the [plan for the other windows](hd-interface-density.md#generated-windows-plan-arc-ui-148155); ARC-UI-1194 adds the loot, barter and steal windows and the three lenses, see [S4](hd-interface-density.md#viewer-slice-s4-arc-ui-1194); ARC-UI-1195 adds the character sheet, see [S5](hd-interface-density.md#viewer-slice-s5-arc-ui-1195); ARC-UI-1197 adds the world map and town map window's frame, see [S6](hd-interface-density.md#viewer-slice-s6-arc-ui-1197); ARC-UI-1196 adds the logbook, see [S7](hd-interface-density.md#viewer-slice-s7-arc-ui-1196); ARC-UI-1199 adds the schematic window, see [S8](hd-interface-density.md#viewer-slice-s8-arc-ui-1199). ARC-UI-1200 adds the message window, the spell and skill panes, the Fate window and the Sleep window, see [S9](hd-interface-density.md#viewer-slice-s9-arc-ui-1200); ARC-UI-1819 adds the common dialog box, see [S10](hd-interface-density.md#viewer-slice-s10-arc-ui-1819); ARC-UI-1817 adds the follower panel and the follower order menu, see [S12](hd-interface-density.md#viewer-slice-s12-arc-ui-1817). **HD pack: Auto/Game/Custom** (ARC-DEV-350, default Auto) is the
third project line: where the active pack is read from, saved in the same file (`hdPack`, `hdPackPath`; file version 4, version 1–3 files
read it as Auto). Auto prefers the player's iCloud pack folder (macOS, only when that folder exists on this device) when the pack in it
is whole, and falls back to the installed copy in the game's own `user://hd-packs`; Game reads the installed copy alone; Custom opens the
system folder dialog (a pack folder or a folder of packs) and reads that folder, then the installed copy (the launcher screen's line, ARC-UI-143). The choice applies at the next
start (the pack is chosen once at startup); the launcher screen's note says now what the next start will read, or why a source was skipped. The
rules, the file check and the log line are in [HD pack source](../workflow/hd-pack.md#hd-pack-source-arc-dev-350). `InterfaceHd` holds the active pack and the HD interface setting
(synced each frame); every interface frame with a key (`AreaInterfaceFrame.Hd`, built in
`AreaInterfaceArt.ReadFile`) is an `HdInterfaceTexture`: it reports its **Legacy** size and Legacy pixels to every reader (layout,
hit tests and window sizes never change) and draws the HD image (decoded on its first draw, once per pack, bound by `ResolveSprite`
against the Legacy pixels) when HD interface is on and the pack has the frame; source rectangles are scaled by the pack's factor; sampling is
trilinear with mipmaps (**ARC-UI-112**: the HD image is built with mipmaps and drawn through its own `CanvasTexture` whose
`LinearWithMipmaps` filter overrides the parent node's Nearest, so a 4× redraw shown at 200 % (2×) or 150 % is filtered down instead of
sampled nearest; the Legacy texture, the tile case, bitmap fonts and Legacy frames keep the parent's Nearest, since the filter is per
texture, not per node). **ARC-DEV-376:** a plain frame (not premultiplied or grey) is taken from the device-local optimized cache when it holds the frame's PNG (`HdOptimizedStore.TryBind`,
the pack's own checks run on the cached level 0; a missing or damaged entry takes the path above) and a background worker converts every interface PNG that has no
entry ([HD pack](../workflow/hd-pack.md#optimized-textures-arc-dev-376)); self-test line `ARC-DEV-376 self-test optimized cache PASS`. Off, without a pack, or without an entry, a frame draws exactly the Legacy pixels it drew before. On a screen
wider than 4:3 the strips stay centred at the factor of the 800 × 600 screen. **ARC-UI-096:** the side fill ARC-UI-089 drew there (a piece
of the strip's own art tiled outwards, `AreaStripSides`, drawn by `AreaStripSidesView`; the top strip's wood x 186 width 32, the bottom
strip's end rail, ARC-RES-094 U4) is **off** (`AreaStripSides.SideArtReady = false`), whatever the HD profile and the HD interface line say: the tiles looked like
repeated chain and bar fragments and the bottom pieces did not match, and there is no art made for the sides yet. The margins beside a
strip show the game world instead (the world is drawn over the whole window; only the strips' 800-unit rectangles cover it). The fill's
code and arithmetic stay for the day side art exists. Not part of this task: other pack kinds, the motif sets still open in ARC-RES-094 (HD minification below 4× is ARC-UI-112, above).
Checks: `ARC-UI-089
self-test interface HD switch PASS` (a synthetic 4× pack: Legacy size always, Legacy pixels off, HD pixels on, the side fill off by default and drawing only when enabled),
`ARC-UI-112 self-test interface HD filter PASS` (synthetic one-texel stripes at 1.5× under a Nearest parent: Legacy stays hard, HD is filtered; a region
draw of a white part of a half-black frame stays white within the edge bleed measured in that synthetic probe) and
the options smoke's HD line.

**Bitmap-font text (ARC-DEV-361).** `AreaArtFontFace` (the one source of every art-font glyph texture, drawn by `AreaArtTextDraw`) draws a glyph from
its HD coverage frame when HD interface is on, the active pack holds an `interface` entry for that glyph's frame (a traced 4× coverage frame, `masked`
entry, [format](../formats/hd-pack.md#interface-art-arc-dev-242)) and the blend is **Coverage**: the HD pixels go through the same layer maths as the
Legacy ones (`AreaArtTextRules.LayerPixels`, cover as (w, w, w, w) on the premultiplied canvas), are drawn through their own
`CanvasTexture` (bilinear, **no mipmaps**, ARC-UI-145: the mip chain blurred the edge at the windows' scales; interface frames stay trilinear, ARC-UI-112) into the glyph's **Legacy-size** rectangle at the same integer position, so the layout, the integer
advances, the colour and the blend are the original's and only the glyph edge differs. A glyph without an entry, a frame without a key, the Additive and
Multiply blends and the switch off or no pack draw the Legacy frame exactly as before; the decoded HD glyphs are dropped when the pack or the switch
changes. Check: `ARC-DEV-361 self-test HD font glyphs PASS` (synthetic 6 × 5 glyph: HD only with pack and switch on, never for another blend or a
glyph without an entry, 254 against 199 on a black canvas at 1:1; the pixel checks need a window).

- **Toggle.** `H` (no modifier; Cmd+H stays the system's) or the palette action **Visual Legacy/HD
  (H)** requests the other profile. `H` acts on a fresh press while the world owns input; under the
  palette or UI focus it is ignored and the HUD says so, and an open dialog or inventory window owns
  every key (the switch itself is safe then too: the native smoke requests it during a dialog).
  A request during a pending switch toggles back to the active profile, which cancels the switch.
- **Timed commit.** The active profile keeps drawing while the requested one is prepared for the
  visible set (decodes off the main thread, at most 64 uploads per submission). The switch commits
  between two submissions as soon as every visible frame is ready or refused, so one frame draws every
  item with the new profile, or after 500 ms (`VisualProfileSwitch.DefaultCommitWaitMilliseconds`; the
  first bind of the whole Crash Site view from a 4× pack took about 0.1 s), when frames still decoding
  draw Legacy as `Pending` and switch one by one. A switch to Legacy commits in the next submission and
  keeps HD textures within the retain limits, so switching back is cheap. A map change during a
  pending switch keeps the request; it commits on the new view. With nothing published (also while a
  reload or failure has cleared the scene) a pending request commits at once, and the next view's
  frames then load like any frame that becomes visible (`Pending`, then HD one by one).
- **Persistence.** The request is saved at once as the `visual profile` line (`legacy` or `hd`) of `user://arcanum.cfg`
  (ARC-UI-1130; `VisualProfileCodec` still reads the old `visual_profile_v1.json` for the one-time move): a missing or unknown
  value loads as Legacy; a failed save is shown in the HUD and does not block the switch. Startup applies
  the saved profile before the first submission (an environment override above wins for that run and is
  not saved). `VISUAL_PROFILE_TEST_PATH` isolates tests; an automated run (self-test, native smoke)
  without it neither reads nor writes the file and starts Legacy.
- **Active pack.** `ARC_HD_PACK` when set; otherwise the first pack that opened and is not rejected in
  the pack folder `user://hd-packs/<id>/` (on macOS
  `~/Library/Application Support/Godot/app_userdata/Arcanum Terrain Viewer/hd-packs/<id>/`, for a source
  run and the exported app alike; an exported app such as `./arcanum area` looks only there) and, in a
  source run, the checkout's ignored `local/hd-packs/<id>/` (locations in that order, packs in ordinal folder order; a rejected pack is
  used only when it is the only one, every frame `PackRejected`). Automated runs do not look at these
  folders. Without a pack, HD draws every frame Legacy as `NoPack`.
- **HUD and palette.** The status line adds `H visual Legacy/HD: <active>[, switching to <profile>];
  pack <id> <N>x (<how it was found>) | no HD pack (…); startup from <saved setting|environment|default>`
  and any save failure, after the ARC-RENDER-014 counters, which add `switching to HD (generation n,
  decoding k)` while a switch is pending. The palette shows the same line under its action.

Checks: `VisualProfileSwitchTests` (codec round trip and every malformed or unknown value as Legacy;
no-op requests, generations, commit only when settled or after the bounded wait, cancel, set; the
active pack choice), the self-tests `ARC-UI-006 self-test store PASS`, `ARC-UI-006 self-test toggle
PASS` (production AREA compositor with a tinted 4× pack at magnification 2: preparing frames are
wholly Legacy, the commit frame binds every candidate HD and no read-back frame mixes the two; Legacy
commits in the next submission and keeps HD retained; cancel drops decodes; the bounded wait commits
with every frame pending; a map change keeps the request; NoPack and an unpublished canvas commit at
once) and `ARC-UI-006 self-test app PASS` (the production app over the synthetic area owner: H, Ctrl+H,
H under the palette, the palette action, cancel by a second H, the saved request, and two walks replayed
from one quick save under Legacy twice, HD and with switches before and in the middle of each step:
byte-identical Session snapshots and drawn destinations; a drawn item's destination is its plan
command's, which HD never changes, and ARC-RENDER-014's pixel identity shows the HD draw covers exactly
it), `ARC-UI-006 self-test WORLD toggle PASS` (the same protocol on the WORLD canvas, whose submissions
are its draws, with a pack recoloured per parent texel), and the native smoke on the installed original
`ARC-UI-006 native-smoke Session invariance PASS` (a nearest 4× pack of every character and object frame
of the exterior and the cave in a temporary directory; from one quick save next to Virgil: talk to him
through his replies, walk, wait one game hour, enter the cave; under Legacy twice, HD and with switches
by H and, during the dialog, by the setting: byte-identical Session snapshots and drawn destinations).
Not verified: a real upscaled pack, the exported app (`./arcanum area` gets the toggle with its next
export), switching on other GPUs and handhelds.

### Virgil follows the PC (ARC-WORLD-016)

In `area` Virgil talks through the [session party](../world/party-runtime.md): his `JO` joins it, and his
follower entry (node 385) offers its `WA`/`UW` replies. While he follows he walks after the PC on his own grid session
over the same step evidence (start beyond 4 cells, stop within 2, stand on the PC beyond 30 then step off); the PC and
Virgil block each other where they stand (since ARC-WORLD-074 the PC steps through him outside combat mode, and since ARC-WORLD-075
he walks through the PC and other critters while the session has him out of combat mode, which nothing in the viewer changes yet), and his T0 cell no longer blocks. His T0 body frame is drawn where he stands
(since ARC-WORLD-111 with his own walk or run cycle, turned to his step, and standing on frame 0 of his stand ART when he
stops: [drawing the walk](../world/companion-follow-runtime.md#drawing-the-walk-arc-world-111)), talk reach uses his current cell, a waiting Virgil stays, and his cell is written to the
session on F7 and when the PC leaves the map. HUD: `companion script 1324 <standing> at
(x,y) …`. Contract and checks: [companion follow runtime](../world/companion-follow-runtime.md); native smoke
`ARC-WORLD-016 native-smoke follow PASS` (with the cave input, after the quick-save phase;
`ARC_AREA_FOLLOW_CAPTURE=PATH.png` saves the frame).

ARC-WORLD-017: a following Virgil goes with the PC through the cave entrance and exit onto the authored arrival cell
and steps off it; in the cave he is drawn (his exterior T0 frame, lit by the cave where he stands), picked, talked to
and followed like outside, while the exterior shows no T0 Virgil. A waiting Virgil stays on the map where he waits.
Contract: [map change](../world/companion-follow-runtime.md#map-change-arc-world-017); native smoke
`ARC-WORLD-017 native-smoke cave PASS` (after the follow stage; `ARC_AREA_CAVE_FOLLOW_CAPTURE=PATH.png`).

The AREA smoke stages drive the actor on test time, which can run ahead of the wall clock. Each stage restores the
clock it found unless that is older than the actor's session or the stage's last observation; then the clock stays at
the later time (ARC-WORLD-025), possibly for the rest of the smoke. Restoring the wall clock regardless made the first observation after the follow stage
fail as late (`GRID_OBSERVATION_LATE_OR_INVALID`) whenever the run reached it in less real time than it had simulated.

### Walking and running (ARC-WORLD-020)

In `area` the PC walks or runs ([walking and running](../world/gait-runtime.md)): each step takes the
human ART's average time for its gait (walk 547/773 ms, run 168/235 ms per axis/diagonal step), and a run step
draws the run ART (animation 6, loaded only in this mode). A ground click walks; Ctrl+click runs once (walks once
with Always Run on); Num Lock flips the always-run lock; the palette's **Always Run on/off** is the original option
(the `always run` line of `user://arcanum.cfg`, `ALWAYS_RUN_TEST_PATH`); clicking an object always runs the approach. A following
Virgil runs while he is farther than range + 2. Since ARC-SKILLS-008 the palette's **Prowl (stealth) on/off** toggles the
hero's and its followers' concealment ([stealth](../world/stealth-runtime.md)); while concealed, a run becomes a walk unless the
hero is a master of prowling with fatigue left (the crouch and the stealth walk animations are not drawn). HUD: `Gait: Always Run …, Num Lock run …; next ground click …;
current gait …` (with `prowling: walks only` or `prowling: a master, runs allowed` while concealed) (in `world-diagnostic` the palette row says it is unavailable and Num Lock stays a reserved key). On macOS a physical
Ctrl+click arrives as a right press with Ctrl held and counts as Ctrl+LMB. Native smoke `ARC-WORLD-020 native-smoke run PASS` (after the quick-save phase, before the follow
stage; `ARC_AREA_RUN_CAPTURE=PATH.png`). ARC-WORLD-021 adds the window vault (a window step takes 1.125 s and draws the
vault ART); the Crash Site maps have no jumpable window, so the map-mode smoke observes it (`ARC-WORLD-021 native-smoke vault PASS`,
[gait runtime](../world/gait-runtime.md#window-jump-arc-world-021)).

### Starting hero (ARC-WORLD-027)

`--hero NAME` (area, exterior or `--area-map`) starts the area session as one of the twelve original pre-made
heroes. `NAME` is the project's race-gender name in main-menu order: `halfling-male`, `gnome-male`, `half-elf-male`,
`half-ogre-male`, `elf-male`, `human-male`, `half-orc-male`, `dwarf-male`, `human-female`, `elf-female`,
`half-orc-female`, `half-elf-female` (basic prototypes 16067..16078). Without it the session has no hero and dialogs use
the diagnostic PC profile, as before. The background load reads the hero from the game folder that holds the module
archive (`<game>/modules/<Name>.dat`, the description tables' rule; no extra argument): the hero prototype from the
content stack's base sources (`ContentPrototypes`, ARC-DATA-026), `rules\effect.mes`, `rules\InvenSource.mes`, `rules\InvenSourceBuy.mes` and the hero's flag number from
`mes\MainMenu.mes` message 551 + index. Any refusal fails the load (`AREA_HERO_<status>`); there is no silent fallback to
the diagnostic profile.

The session's player character is that hero ([starting hero runtime](../world/starting-hero-runtime.md#viewer-start-arc-world-027)):
level 1, its race and gender effects, gold and starting kit, and its main-menu global flag (2350..2361)
is true. Dialogs gate replies on the hero's own gender, effective intelligence and race (also after F8, which follows the
loaded save's hero). The inventory window lists the kit first (`(starting kit)`, named from the item's description
number; an item the session's equipment wears is marked `(worn)`) and shows the gold; the HUD adds `hero: NAME level L (U points unspent), gold G, kit K, worn W` or `hero: none
(diagnostic PC profile)`. ARC-WORLD-034: the start wields the kit's best items before the game begins, as the original's
pre-made menu does ([best items](../world/equipment-runtime.md#best-items-arc-world-034)). ARC-WORLD-036: the load also reads
`rules\gamelevel.mes`, `rules\userlevel.mes`, `rules\spelllist.mes` and the module's `rules\gameeffect.mes`, so the hero's
auto-level scheme spends its 5 starting points at the start and each level's points as it is reached, and after every
experience award (a dialog's quest completion, a hit, a kill) the followers and the NPCs waiting on this map follow the
hero's level step with their own schemes ([auto-level schemes](../world/advancement-runtime.md#viewer-host); the log prints
`ARC-WORLD-036 follower-level`). The actor is still drawn with the fixed human body of the AREA profile, not the hero's own
race/gender ART. Launcher: `./arcanum area --hero NAME` (since ARC-RENDER-028 the main world map in map mode), `./arcanum map NAME --hero HERO`
([local development](../workflow/local-development.md#project-launcher)). Native smoke with `--hero` (after the
inventory stage, and in map mode after the map stages): `ARC-WORLD-027 native-smoke hero PASS`.

### Combat mode (ARC-COMBAT-004)

With `--hero`, the original Ready/Unready key **R** (fresh press, world focused, no palette or window) toggles the hero's
combat mode in real time through the session ([real-time combat runtime](../world/real-time-combat-runtime.md#viewer-area)).
The HUD shows `combat mode: ON (real time)` or `off`, the hero's fatigue and the last notice; an unconscious, stunned or
dead hero is refused with the reason. Without `--hero` R reports that combat mode needs a hero. A dialog start leaves
combat mode first (the exit gate closes while an active NPC near the hero fights it or its followers, ARC-COMBAT-1935, or a swing or throw is playing, and a dialog start then opens none; an NPC's own talk stays pending until the gate lets go; the world map's opener `W` leaves combat mode the same way, or shows "cannot leave combat mode when under attack (message 600)" and opens no map, ARC-UI-1940), and the cave transfer leaves it as the
original's teleport does. Running in combat mode costs one fatigue every fifth run step of a move; a hero out of fatigue
walks, and so does a hero at encumbrance level 4 or more (ARC-WORLD-087, ledger WE9), and the viewer does not run the session's event queue, so that fatigue only returns with a quick load. In combat
mode a left click on a living NPC attacks it (below). Native smoke with `--hero` (after the run stage):
`ARC-COMBAT-004 native-smoke combat-mode PASS`. Since ARC-COMBAT-006 its run goal is a cell the PC can walk to 12 cells
from its start (a search over open steps; else the farthest such cell at least 8 away) and its held-stick run takes the first direction that runs at least one
charge of steps, so the smoke works on any map; before, a fixed offset and direction fell in a wall on `tulla interior`
and stopped the map smoke there. The roof smoke after it starts the roofs again from the map's stored state, since the
run may end under a roof.

### Hero attack (ARC-COMBAT-005)

In combat mode (R, `--hero`) a left click on a living NPC attacks it, on the exterior, in the cave and in `--area-map`
([hero attack runtime](../world/hero-attack-runtime.md#viewer-area), ledger
[FA1–FA10](../research/ledger/combat.md#hero-attack-arc-combat-005)): the hero walks (running) within its
weapon's reach, turns to the target and plays its bare-handed attack ART at the rate of its Speed; the frame that shows the
action frame resolves the hit through the session, and the attack repeats (auto attack) until the target dies. Shift makes
it the attempt-attack goal 19 with run flag 0x100 (ARC-DEV-270: from where the hero stands, no approach walk); a follower is attacked only with left Alt held; held `,` `.` `/` aim at the
head, an arm or a leg; a corpse is looted only with Alt, a plain click on it does nothing. The cursor over a living NPC is the
closed hand (`attack`, `walk to attack`). The status line names the outcome, damage, the target's hit points, a knockout or
kill and the experience paid. A killed or unconscious NPC plays its dying or fall-down animation, read on demand from the
archive its body came from, and keeps its last frame (ARC-RENDER-040; a body already lying when the map is shown holds it); out of combat mode a plain click loots the corpse. The stick, another
click, a dialog or window, a map change or a hero that cannot act stop the attack. Native smoke with `--hero` (after the
combat-mode stage, exterior and map mode): `ARC-COMBAT-005 native-smoke attack PASS`, or `NO-TARGET` on a map without a
living NPC with combat values.

### Character sheet (ARC-UI-007)

The original Character sheet key `C` (logical key down) toggles a diagnostic sheet of the session's hero in
area (exterior, cave and `--area-map`). It shows what the original sheet of the PC shows
(`charedit` in active mode, checked in the original executable, ledger rows
[CS1–CS7](../research/ledger/character.md#character-sheet-arc-ui-007)), every value a session query:

- the hero's project name (no PC name in the session), race and gender;
- level, experience still needed for the next level (`last level` at 51; `next level unknown` without experience data),
  unspent points, and the experience itself (a project addition: the original sheet does not print it);
- the hero's stored alignment divided by ten (truncated toward zero), the magick/tech aptitude split into a magick and a
  tech value (a positive aptitude is magick, a negative one tech, the other side 0); a dialog's `AL` action writes the
  hero's alignment since ARC-WORLD-067, so the sheet follows it. **Partial:** the aptitude has no sector adjustment (not
  modelled);
- hit points and fatigue against their maxima;
- the eight primary stats at their effective value, in the sheet's order, marked "raised by effects" or "lowered by
  effects" when the base value adjusted by the effects that are not innate (race, background, class, gender) differs
  from the base value, as the original draws them green or red;
- the eight derived stats (carry weight, damage bonus, AC adjustment, speed, heal rate, poison recovery, reaction
  modifier, max followers) at their effective value;
- the five resistances (normal, magick, fire, poison, electrical) clamped to 0..95: worn armour and the constitution
  bonus to poison `5 × (CN − 4)` only. **Partial:** the session has no base resistance field, effect changes of
  resistances (a half-orc's +10 poison, a half-ogre's +10 normal) are inert in the session, and every worn item counts as
  identified.

The sheet opens from the world only (no palette, dialog, inventory window or UI focus), never for a dead hero (the
original refuses it) and only with `--hero` (without a hero the status line says so). Like the inventory window it is
modal: the actor gate closes, the clock pauses, a walk stops (the original interrupts the PC's animation when the sheet
opens; it does not pause the world, the viewer's modal windows do). While it is open it owns every key: `C`, `Escape` or
a controller cancel closes it, the close button is the only control. It rebuilds whenever the session changes and closes
when the session ends or its hero dies. The HUD adds `C character sheet` and the last refusal.

With a hero the load now also reads `rules\xp_level.mes`, `rules\xp_quest.mes` and `rules\gamequest.mes` and gives the
session its [experience data](../world/experience-runtime.md) (kept over F8): the sheet knows the next level, and a
dialog that completes a quest now gives the quest's experience as the original does (the dialog `XP` action is still not
wired). A table that is missing or
not admitted fails the load (`AREA_HERO_TableUnavailable`, `AREA_HERO_ExperienceInvalid`).

#### Skills page (ARC-UI-009)

Since ARC-UI-009 (task S6 of the skills plan) the sheet has the original's skills page, checked
in the original executable (ledger rows [SP1–SP8](../research/ledger/skills.md#skills-page-arc-ui-009)):

- the sixteen skills in the original's four groups of four (three of basic skills, the fourth the tech skills), all four
  groups at once instead of the original's tabs;
- each row the skill's name with its training name in brackets (none untrained), from the player's `mes\skill.mes`, loaded
  in the background through the module's game folder when the area binds and adopted at the first open after it finished (texts
  that arrive while the sheet is open appear at the next open) (the project's labels without it, for example a module outside
  `<game>/modules/`), then, as project text, the effective level out of 20 (the original draws a bar of level / 20 and
  prints no number), the stored points and the key stat the next point needs (`MinimumKeyStat(4 × points + 4)`, "no
  further point" when no stat value allows it);
- on hover the original's hint: the name, the description and the key stat with its minimum;
- `−` and `+` buttons on each row. Opening the sheet opens the hero's character editor in the session
  (`OpenHeroEditor`, its floor is the hero's sheet at that moment, as the original's snapshot), closing it closes the editor:
  points bought while the sheet was open can be refunded until it closes, never below the floor. `+` spends one point
  (`SpendHeroPoint`); `−` uses the original's click (`RefundHeroPoint` with `UntilLevelChanges`: points come back one at a
  time until the skill's level changes, stopping at the floor). The owner has not chosen the alternative "one point per
  click" (skills plan S5), so the page uses the original's default. Every press reports its outcome on the page: the points
  spent or refunded, or the refusal in project words (not enough unspent points, at the maximum, the key stat too low,
  nothing to refund since the sheet opened, no points); the original shows its `charedit.mes` texts with sound 3004 instead.

Differences from the original, all of the page's presentation: no tabs (all groups shown), the level as a number instead
of a bar, the hint's lines also always visible on the row, project refusal texts and no refusal sound, and names and
training texts redrawn on every change (the original redraws them only at the open and on a tab press, SP2), the hint only over
the row text (the original also over its + and −, whose tooltips here are the project's), a success reported as the points
spent or refunded (the original clears the text bar, SP7) and no 3 s hint delay after a refusal (SP5). The view-only
mode of the original (mode 2, + and − hidden) has no caller in the viewer.

Since ARC-UI-034 the sheet also has a stats page (its first tab): hit points, fatigue and the eight primary stats in the
original's order (strength, constitution, dexterity, beauty, intelligence, willpower, perception, charisma; ledger
[CP3, CP4, CM8](../research/ledger/character.md#character-editor-in-creation-mode-arc-ui-034)), each with `−` and `+`
through the same pool (`AreaStatsPage`, `SpendHeroPoint`/`RefundHeroPoint`): the row shows the effective value, the base
when it differs and the cost of the next point (the original's hint; "at its maximum" when the base has reached the race's
maximum), a pool its maximum and the points bought. Refusals in project words (not enough points, at its maximum or
minimum, nothing to refund since the sheet opened, a skill, spell or degree that needs the stat).

**Not implemented:** the original's refusal texts and sound for the stats page, tech disciplines and schematics panels, age (not session state), the portrait and PC lens,
followers' sheets (passive mode from dialog or the follower menu), switching straight from one window to another by
key. Self-test `ARC-UI-007 self-test PASS` (synthetic hero session: no-hero refusal, `C`/close/`Escape`, keys owned
while open, rebuild on change, the skills page's `+` and `−` with the editor's floor over a close and reopen, dead-hero close
and refusal); native smoke with `--hero` after the hero stage: `ARC-UI-009 native-smoke skills page PASS` (the original skill
texts describe all sixteen rows; a `+` the pool accepts, its `−` and a refused second `−`, or with no unspent points a refused
`+`) and `ARC-UI-007 native-smoke character sheet PASS`.

#### Spells page (ARC-MAGIC-008)

Requirements: [FR-UI-004](../requirements/ui-media-lifecycle.md#fr-ui-004) (the character sheet's spell page),
[FR-MAGIC-002](../requirements/combat-magic-tech.md#fr-magic-002) (learning colleges in order with the editor).

Since ARC-MAGIC-008 (slice MT19 of the [magic implementation plan](../research/magic-implementation-plan.md)) the sheet has
the original's college page as a second tab beside the skills, checked in the original executable (ledger rows
[SG1–SG8](../research/ledger/magic.md#spell-page-arc-magic-008)). Code: `AreaSpellsPage` (Core, Godot-free)
and the panel's "Spells" tab; tests `AreaSpellsPageTests`.

- **Colleges.** Sixteen college tabs, one shown at a time. The shown college is kept between opens, as the original keeps
  it (initially the first).
- **The shown college's five spells**, in rank order. Each row has the spell's name and the minimum level it needs, as the
  original prints it beside the name. Known ranks are drawn white, the next rank gold and the ranks above it grey, as the
  original draws the names; the original also greys the icons of every rank not yet known.
- **Hover hints, as the original's text bar shows them.**
  - Over a spell, or over `+` (the next rank) and `−` (the highest known rank):
    - the name;
    - the minimum willpower;
    - the fatigue cost the hero would pay (a dwarf twice, the mastered college half);
    - for a maintained spell, the maintenance per second or per period (a dwarf twice; the mastered college doubles the
      period);
    - the description.
  - Over a college: its name, then its description — or the mastery description for the college the hero mastered.
  - Names and descriptions come from the player's `mes\spell.mes`, costs from `rules\spelllist.mes`. Both load in the
    background through the module's game folder, with `rules\magictech.mes`, as the original's magic init reads them;
    without them the project's labels are shown and no costs.
  - The willpower is the minimum the session's pool checks (the spell list's `IQ:` loaded with the hero, else the
    defaults).
- **`+` learns the college's next rank.** It is enabled below five ranks. It goes through the session's point pool
  (`SpendHeroPoint`, track College: the [spell book](../world/spell-book-runtime.md)'s lane). Unspent points are tested
  first, then the level, intelligence and willpower. A refusal names the failing check in project words, matching the
  original's `charedit.mes` messages 18, 30, 26 and 36.
- **`−` unlearns the highest rank** (`RefundHeroPoint`). It is enabled only above the rank the hero had when the sheet
  opened (the editor's floor), as the original's in-game sheet shows its `−`. A press at the floor is refused with the
  original's meaning (message 27): a spell learned before the sheet opened cannot be unlearned.
- **Feedback.** Each press reports on the page: the spell learned or unlearned, or the refusal.

Differences from the original, all of the page's presentation:

- the college names as tab texts instead of the college icons;
- no spell icons; the look of each rank is given as a word and a colour;
- the hint as a tooltip; the original's 3-second hint delay after a refusal is not kept (SG6, SP5);
- project refusal texts, with no refusal sound (the original plays 3004, SG6, SP7);
- a success reported in words; the original clears its text bar.

The view-only mode of the original sheet has no caller in the viewer. The character-creation mode is the
[character editor](#character-editor-in-creation-mode-arc-ui-034) (ARC-UI-034); like the original's (CM8) it refuses a `−`
at the floor, which is the new character's start.

**Not implemented:**

- clearing an unlearned spell from the spell bar (SB10): the spell bar is MT20's;
- casting;
- followers' pages.

**Checks:**

- The self-test inside `ARC-UI-007 self-test PASS` uses a synthetic hero with willpower 8. A college tab shows its spells;
  `+` learns rank 1; a second `+` is refused for willpower; `−` unlearns the rank and is then disabled at the floor. The
  shown college is kept over a close and reopen.
- The native smoke with `--hero`, after the skills page, prints `ARC-MAGIC-008 native-smoke spells page PASS`. It checks
  that the original data names the sixteen colleges and all eighty spells and gives every spell a description and a cost.
  Then a `+` on the first college either learns the next rank (`−` then unlearns it) or is refused with the failing check.


### Character sheet original look (ARC-UI-075)

With the player's game folder (`--game-root`, the arts of [CHW1–CHW9](../research/ledger/character.md#character-sheet-original-look-arc-ui-075)
read) the sheet's main page is the original's window: art 22 `char_maint` (800 × 400) between the strips (`AreaGameBarPanel.FittedWindow`, ARC-UI-132: with the HD pack the whole free height at the exact, fractional scale, aspect ratio kept, side margins at 16:9; without it the largest whole scale that fits), standing on the bottom strip (`AreaGameBarPanel.DockedWindowTop`, ARC-UI-127; in the original's 800 × 600 that is also directly under the top strip, in a larger window the free height lies above the sheet, not as a gap above the HUD),
the hero's portrait in its slot (141, 21), every number in the original's fonts and colours (`Core/AreaCharacterSheetArt.cs`,
`AreaCharacterSheetArtLayout.Texts`), the alignment gauge (art 254, frame by hot point) and the aptitude marker (art 255), the ten + / − art
buttons (art 24 / 23: the knobs at either end of each number's oval, which is part of art 22) and the four tabs (arts 34, 35, 36, 568). Texts come
from the player's `charedit.mes`, `stat.mes` and `MainMenu.mes`; a missing number falls back to a project label. The pointer over a button
or one of the 32 regions writes that part's `charedit.mes` text to the message window (a stat's with the cost of its next point, PROJECT);
a refusal of a + or − press goes there too; a mouse-up inside the window's close rectangle (12, 10, 89, 89) closes the sheet, as does `C`,
Escape or a controller cancel (PROJECT). The first region of the table that contains the pointer answers, so 104 "Level" never does:
the original's flaw, kept in Original mode.

**Skills and tech pages (ARC-UI-076, ledger [CHW10–CHW16](../research/ledger/character.md#character-sheet-original-look-arc-ui-075)):**
the right-hand panel (503, 63, 235 × 325) shows the skills page (art 29: four group tabs, the shown group kept between opens; four rows of a
skill's name and training, a level gauge of art 623 six pixels a level, and the − and + art buttons) and the tech page (art 30: eight
discipline buttons, the header with the discipline's and degree's names and the expertise, seven degree rows named by their schematic in white,
yellow or grey, the learned-degree icons and a + and a − in the icon slots; the − is shown only above the editor's snapshot) from
`Core/AreaCharacterSheetPages.cs` and `Godot/AreaCharacterSheetPageView.cs`. The pointer over a skill writes its type-7 hint (name,
description, key stat with the minimum for the next point), over a tab, the header, a discipline or a degree its `charedit.mes` / `tech.mes`
text (PROJECT: the type 10 and 11 layouts are not drawn, the text goes as a hover entry); a + or − goes through the session's point pool
(skills as before, the tech page through the technology lane, TD7, TD8) and a refusal to the message window. The tech page does not draw the
components line of a degree yet (the items' names); without the pages' arts the diagnostic skills page stays.

**Spells page (ARC-UI-077, ledger [CHW17–CHW22](../research/ledger/character.md#character-sheet-original-look-arc-ui-075)):** the right-hand
panel shows art 31 with sixteen college tabs of 24 × 24 in two rows (the shown college's tab pressed; kept between opens), and the college's
five spells: a 32 × 32 icon (the arts that follow the college's tab art), the spell's name in Morph15 and "Minimum Level: n" in Pork12 (art 28),
white for the known spells, gold / yellow for the next one and grey above it (all white at five), the icons of the next spell and the ones
above it greyed. A + (art 647) sits over the next spell's icon below five known spells and a − (art 648) over the highest known one when it is
above the editor's snapshot. There is no college header, points line or mastery mark. The pointer over a tab writes the college's hint
(its large icon, name and description; PROJECT: the type 9 layout is not read), over a spell, the + or the − the spell hint the hotkey bar
shows (icon, name, willpower, cost, description). A tab, + and − act when released over themselves (PROJECT); + and − go through the session's
point pool (spell book lane) and a refusal to the message window. From `Core/AreaCharacterSheetSpells.cs` and
`Godot/AreaCharacterSheetSpellsView.cs`; without the page's arts (80 icons, 16 tabs, arts 31 and 28) the diagnostic spells page stays. The
icons' grey is the usual luminance (the exe's weights are F0).

**Refusal messages (ARC-UI-078, ledger [CHW23](../research/ledger/character.md#character-sheet-original-look-arc-ui-075)):** in the original's
look a refused + or − writes the original's `charedit.mes` text (17..39) to the message window (a message entry: sound 3004, hovers held back for 3 s),
chosen from the page and the refusal by `src/Arcanum.World/AreaSheetRefusals.cs`: a stat's + 17 (maximum) or 18 (points), its − 19 (below the last accepted level),
20 (minimum) or the cause, 28 (a skill), 29 / 39 (a spell of intelligence / willpower), 31 (a tech degree); a skill's + 18, 21, or the key stat's
"raise <stat> first" (32..38, 26 for intelligence, 22 otherwise), its − 24, 23 or 25; a spell's + 18, 30, 26 or 36, its − 27; a degree's + 18 or 26, its −
nothing. A success shows no message (the window keeps the last entry: the original's clear of the text bar after a success, 0x550720, is F0). The diagnostic window keeps the project's words. The original picks a stat − cause from the value minus two, not
the effective value, so it can name another cause or none (its flaw, CHW23); Steamwoven names the cause its pool found, in both rule modes for now.
The character points follow the editor's snapshot (taken when the sheet opens, so points spent before closing it cannot be taken back after a level-up;
the creation editor keeps its draft's snapshot until the game starts).

**NPC sheet (ARC-UI-088, ledger [CHW24](../research/ledger/character.md#character-sheet-original-look-arc-ui-075)):** the dialog reply action c-e ends the talk and
opens the speaker's character sheet in the original's mode 2 (`Godot/TerrainViewerAreaNpcSheet.cs`, `src/Arcanum.World/AreaNpcSheet.cs`): the same window over the NPC's
values (name, level, points, experience to the next level from its stored experience, the eight stats with the colour of its effects, derived stats from
`AutoLevelRules.DerivedStat`, stored resistances with the constitution bonus under the NPC's effect list (the session's, else the map data's; the Magick line shows the `resistmagic` changes too, ARC-UI-1145), hit points and fatigue from its life, skills, spell book and tech degrees), no +
or − on any page (`ReadOnly` of the art view and pages), the next spell grey, no editor snapshot, no pause; a monstrous NPC shows no race or gender. The sheet is the
talk's moment: the session's later changes do not rebuild it. A release in the close rectangle or `C` closes it, the dialog does not resume, and the next `C`
opens the hero's sheet. The scheme page is built (ARC-UI-1742, below): the NPC's scheme name shows and the list scrolls, the rows answer nothing (ALP7). The portrait slot is built (ARC-UI-1791, CHW25): a follower the hero leads shows its stored portrait, any other NPC its icon (critter flags, else its race), the hero's slot is its portrait and the age line stands under the race and gender for the hero only. Not built: the follower arrows, the NPC's worn
armour in its resistances; the item windows `ii` and `ri` of the same reply family stay blocked. Known gaps: a stat's colour counts every effect of the NPC (the
hero's excludes its innate race, background, class and gender causes, which the session's NPC record does not keep); the experience to the next level uses the
experience stored in the map file against the session's level (a follower the session levelled after the file was written shows an inflated figure); the
spell hover texts read the hero's spell book; without the player's game folder the plain fallback panel draws its + and − inert; a stored primary stat above the
racial range shows clamped.

**Identify sheet, heal another critter and lockpicks on containers (ARC-UI-1127, slice E8-g of [the plan](../project/missing-systems-plan.md)).** (1) The cast host's `IdentifyCritter` event for an NPC
(Read Aura, spell 7, ledger XR2) opens the same read-only NPC sheet as the dialog's c-e reply (`TakeIdentifyEvent` and `TakeIdentifySheet` in `Godot/TerrainViewerAreaNpcSheet.cs`; the NPC's map row comes from the
cast host's id; the sheet waits until no modal window owns input, and a hero as the target keeps its message line). (2) A healing kit with the target flag aimed with the item cursor at another critter walks
next to it (goal 39, range 1) and the session's heal runs on it (`HealCritter`, [Heal](../world/heal-runtime.md#viewer)). (3) Armed lockpicks on a standing container start the skill goal 33 with the loot
target string, and the pick is the session's on the container's lock (`TerrainViewerAreaContainerPick.cs`, [Pick locks](../world/pick-locks-runtime.md#containers-in-the-viewer-arc-ui-1127)). Smoke: stage `item-aim`
(steps 6 to 8, run on the town sector). NOT READY: the container window of See Contents and the item description window (their interaction is not read), the question window for a junk pile's and a container's
stacks, poison sources.

**Scheme page (ARC-UI-1742, ledger [ALP1–ALP15](../research/ledger/character.md#character-sheet-scheme-page-arc-ui-1742)):** the fourth tab shows the original's page (art 567):
the shown critter's scheme name centred in blue at the top, then fifteen rows of the schemes whose name exists in `mes/userlevelname.mes` and `mes/gamelevelname.mes` (scheme 0
first, 59 in the installed game) in white, the hovered row in blue, and the original's scroll bar (arts 317, 318, 238, 787, 240; arrows by 1, track by 5, wheel by 3, a dragged thumb)
when the list is longer than fifteen. A release on a row writes the hero's scheme through `AreaSchemesPage.Choose` (no cost, no sound; the next level step runs it, AL12) and the
list keeps its place while the sheet stays open. The NPC sheet (mode 2) names the NPC's scheme and scrolls the list; its rows answer nothing. Model: `AreaSchemesPage` (World),
`AreaCharacterSheetSchemes`/`AreaSheetScrollBar` (Core, Godot-free), `AreaCharacterSheetSchemesView` (Godot). F0: the thumb arts are stretched from 11 to the bar's 17 pixels.
The creation editor (window 14) shows the same tab in the original look (ARC-UI-1792, ALP8): a release on a row writes the scheme into the draft hero (`AreaCreationEditor.PressScheme`), the choice is kept in `AreaNewCharacter.Scheme` and written into the started session by `ApplyChoices` (no cost, no points spent: the original ran its scheme before the editor, so it counts from the next level step); the diagnostic panel of an editor without the game's art keeps the NOT READY note. The age line and the race icon are built since ARC-UI-1791 (the creation editor's sheet gets the age line when the draft stores one). Native smoke: the sheet art stage (`--stages map`), the
item-aim stage (NPC page) and the menu stage (the creation editor's page) print `PASS` lines for the page.

**Not yet in the original's look:** the age is the stored base (no effect on stat 25 is modelled), the NPC icon reads the race the sheet names with no effects and the clash rule of an NPC portrait (0x4CEB80) is not built; a button takes the pointer on its rectangle, not on its opaque pixels. The creation editor (window 14) in the diagnostic look (its art did not read) shows the schemes tab's NOT READY note. Without a game folder (the synthetic self-tests) or when an art does not read, the whole sheet is the diagnostic panel above.
Native smoke: `./arcanum smoke pr --stages map --map "elven ruins" --hero elf-female` prints `ARC-UI-075 native-smoke sheet art PASS`
(`ARC_AREA_SHEET_CAPTURE=PATH.png` saves readbacks `PATH-skills.png`, `PATH-tech.png`, `PATH-spells.png`).

### Level-up messages (ARC-UI-046)

Since ARC-WORLD-087 the same window also shows a change of the hero's encumbrance level: line `11 + level` of
`mes\critter.mes` (loaded with the level texts) for the latest change the session records (one entry when several came between
two frames, where the original shows one per recalculation)
([encumbrance consequences](../world/inventory-runtime.md#encumbrance-consequences-arc-world-087)). The viewer attaches effect 158
from the hero's effect tables to every new or loaded session. Native smoke (after the level message one):
`ARC-WORLD-087 native-smoke encumbrance PASS` (the installed rule of effect 158 changes the speed, the seven message lines are
the player's texts, an entry is shown).

Each level the hero's recalculation writes in its normal branch posts one entry to the viewer's message window, as the
original posts one per level for the local PC (checked in the 1.0.7.4 executable, rows
[OL27–OL28](../research/companion-orders-loyalty-evidence.md#4-level-up-message-q4-xp5)); the last-level branch and a
follower's level update post none. Every award passes the same hook (hits, kills and the dialogs' queued awards), before the
step goes to the followers. The entry is drawn as the original draws it:

- the title (`mes\intgame.mes` 21), message 11 formatted with the new level;
- for a hero without an auto-level scheme, message 12 formatted with the points: under the Original rules the stored unspent
  points plus the level's points before the cap of 56 (OL28, it can name points the hero does not have), under Steamwoven the
  capped value it holds (owner decision D6, 2026-10-21; rule switch
  `ExperienceRuleProfile.LevelMessagePointsBeforeCap`);
- for a hero with a scheme number, the scheme's text (0x4A7030): `mes\level.mes` 2, then message 4 when the number has no
  rule (or the session has no scheme data), message 3 when the level bought nothing, or each purchase as its name, a space and
  its value (a stat's effective value, a skill's points, the bought spell's name with the college's level, a tech
  discipline with its degree's name, the hit-point or fatigue maximum with `level.mes` 5 or 6), joined by ", " with the last
  separator turned into ". "; a space and message 1 follow when the rule completed. Purchases are logged by their name text:
  a repeated name keeps its place with its last value, and at most ten names are kept (0x4A7B90). Names come from
  `mes\stat.mes`, `mes\skill.mes` (tech skills from 12), `mes\tech.mes` (degree names from 8) and `mes\spell.mes`.

Drawing each entry plays sound 3005 through the interface sound path (the per-revision level-up sound of ARC-UI-044 is gone).
The texts are read from the player's installation at run time (in the background when the area binds, through the module's
game folder like the skill texts); a file that is missing falls back to the project's labels for its lines. An award waits at most
a second for a load still running; then its entries wait in a queue that the next frames post once the texts arrive. The two
formats take their one number at the first `%d` or `%i` (`%%` is a percent sign; another conversion stays as text).

Since ARC-UI-023 the entries go to the original's [message log](#message-log-arc-ui-023) in the bottom strip (the diagnostic
`AreaMessageWindow` is gone): a level-up entry is drawn with its icon, the title, the level line and the points line (or the
scheme text on two lines), and its drawing plays 3005. A different session (a quick load, a new game) empties the log and the
queue, and so does a session that ends without one (a failed reload); a map transfer, the main map's window move or world-map
travel carries the session and keeps them.

Self-test inside `ARC-UI-007 self-test PASS` (synthetic hero: an award to level 3 posts two entries with the project's labels,
eleven more keep the last ten); native smoke `ARC-UI-046 native-smoke level message PASS` on the original installation (all six
files load; an award built in the smoke, without changing the session, posts a points entry and a scheme entry that read the
player's title, formats and names, each with the level-up sound; no text is printed). Not ready: the original's other messages
and its three interface callbacks after the entry (button state, two refreshes, not traced).

### Save list (ARC-UI-010)

While `area` has a game session, the palette's **Save game (save list)** and **Load game (save list)**
open the save and load windows over the session's save folder (`user://saves`, map mode `user://saves/map`, an
automated run its own folder). The original opens these windows from its in-game menu, which is a later task; the
rules are the original's ([ledger SL1–SL8](../research/ledger/ui-audio.md#save-list-arc-ui-010)); since ARC-UI-062 the
window's frame, buttons and list are the original's with the game's art (below), and since ARC-UI-067 its picture and info too.

- **Folder.** `GameSaveLibrary` ([save list](../world/game-session-runtime.md#save-list-arc-ui-010)): the auto-save
  (`quicksave`, the original's `SlotAuto` "Auto-Save" that F7 writes) and numbered saves `slot0000`..`slot9998`,
  each a snapshot file with a details file (description, hero and level, place, game date and time, the hero's
  portrait and the story state) and a PNG preview of the screen fitted into the original's fixed 468×300.
- **List.** The auto-save first, then the other saves newest first by file date. A row shows the description
  (the slot name when it is empty); the side shows the details and the preview; a save without details (a quick
  save from before ARC-UI-010) or with details from another snapshot is listed and marked.
- **Save window.** A "new save" row first, then the saves; the first save is selected on opening (as the original).
  (In an empty window the original selects nothing; here the new-save row is selected so Return saves.)
  The description field has the keyboard on the new-save row only. Return or **Save** there writes a new
  numbered save (the highest number plus one; refused at 9999) with the typed description (empty or only
  spaces: the slot name); on a listed save it asks first, Return or **Yes** overwrites it keeping that save's
  own description (as the original, which puts it in the field before asking) and Escape or **No** keeps it.
  A double click on a row confirms it, but never answers an open question. A save that succeeds closes the
  window, as the original; a failure keeps it open with the reason. The save is the game as it was when the window opened (the window is modal: the gate
  closes, the clock pauses, a walk stops), with the preview taken when the palette had left the screen.
- **Load window.** The saves; Return or **Load** loads the selected save through the quick load's path (every check
  before anything changes; a save of another map loads that map in map mode) and closes the window; a failed load
  keeps the window and the session and names the reason.
- **Delete.** Delete or Backspace on a selected save or **Delete** asks first, then removes the save, its details
  and preview and clears the selection, as the original; the auto-save is never deleted.
- **Keys.** The open window owns every key except the typing in its description field: Up/Down select, Return
  confirms or answers yes, Escape answers no or closes; F7, F8 and the window letters do nothing under it. A
  controller confirm, cancel and menu direction do the same.

- **Original look (ARC-UI-062,** [ledger MW11–MW16](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058)): with the
  game's art both windows draw over the 800 × 600 screen (the bars' whole factor) the strips 336 and 335 and the background 745
  between them; Save or Load (321) and Previous Screen (323) on the bottom strip, Delete (32) under the list, each idle, lit under
  the pointer and down while held, acting on a release over it with 3000 / 3001; the list's eleven rows in the art font 27 from the
  scroll top (Up/Down keep the selected row shown, the wheel over the list scrolls, a press on a row selects it; no double press),
  each the save's description, "..." when too wide, the selected one gold; the save window's first row "New" (MainMenu.mes 5055) or
  the description being typed into it (only spaces: "New"; a description wider than the row shows its start and "...", and
  characters the font lacks are not drawn, though they are saved). PROJECT: the selected save's details, a notice and the Yes/No
  question stand in the bottom strip's middle box (a question without the details), and while a question is open the art buttons,
  the toggle and the rows do nothing. Not drawn yet: what the lens shows, the scroll bar and the hints. The synthetic self-tests (no game folder) keep the
  diagnostic window. The menu stage (`ARC-UI-062 native-smoke save window art PASS`, Load Game from the single-player window with
  the F7 auto-save listed and its details in the box, a press on its row selecting it, Previous Screen clicked back to the menu) and the scene stage (the in-play Save Game window, row "New") check it:
  the art read, the rows as drawn, the selected row shown, the pointer lighting the confirm button; `ARC_AREA_SAVE_CAPTURE=PATH`
  saves each window's picture to an ignored path (`-load` / `-save` added to the name).
- **Picture and info (ARC-UI-067,** [ledger MW22–MW25](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058)): the selected
  save's picture in the preview (281, 96, 468 × 300; PROJECT: centred, as the stored picture keeps the screen's shape); over it, while the info display is on, the hero's name, the
  small portrait at (484, 139), "Level N" (MainMenu.mes 5051), the description (the auto-save's "Auto-Save"), the place (5050 when
  it has no name), the date ("%d/%d/%d" in the locale's order) and time ("%02d:%02d:%02d") ending at x 448, and the story state's
  text from `mes/storystate.mes` broken at spaces over four narrowing lines, all in art 27 near-white at the exe's places; then the
  cover 748 over the preview, whatever it shows. No save selected (or the save window's new-save row): black. A save without
  readable details: black with 5005 in red. The toggle 747 at the screen's (499, 82) is a toggle button: a press turns it on (down,
  3000) and hides the info, the next turns it off (3001) and shows it; the display's state outlives the window while the button opens
  up, as the original. The lens 746 at the screen's (84, 51) is drawn without what the original puts in it (the hero's location,
  not a portrait; F0). The menu stage checks it after the row press (`ARC-UI-067 native-smoke save info PASS`: the texts at their
  places, the portrait drawn, the toggle hiding and showing them; `-info` capture); the scene stage's save window checks the black
  preview of the new-save row.

Self-test `ARC-UI-010 self-test PASS synthetic save list` (the production app over the synthetic area owner, on a
temporary folder): a new save closing the window, an overwrite asked (a row activation leaves the question open,
Escape refuses, Return accepts) keeping the save's description, keys owned, the F7 auto-save's details (and the
preview when a frame can be read), the auto-save first and not deletable, a load by row under a new session, F8
loading the newest save (the auto-save, then a numbered save made newer; the HUD names the loaded save), a delete
asked first that clears the selection. The native quick-save smoke checks the auto-save's
details on the original data. Not done: the in-game menu that opens the windows (FR-UI-006, the next tasks), the
lens's content, the scroll bar's art and the hints (next slices), sorting or paging options, a text cursor of the original's
own, and the original `.gsi` format.

### Bug report (ARC-DEV-051)

**Ctrl+B** opens the bug report in the WORLD and AREA modes, in play, in the main menu and over any window (the
death screen included), in both rules modes; it is no key of the original (B has no binding and a Ctrl letter is
silenced, ARC-INPUT-004), and a function key would need Fn on a Mac. The key's moment is kept first: the frame on
screen as PNG (before the report window is drawn; none in a headless run), the last 120 s of the log, and in a game a
save taken as F7 takes it, also under an open window that F7 waits for (the window's own state, such as an open dialog, is not
in the save; none in the main menu before a game, during a map change, a cast or a missile). The control that had the
keyboard under the report gets it back when the report closes.
Then the modal window opens above every other window (canvas layer 20): one text field, Send (Ctrl+Enter) and Cancel
(Escape); the gate closes, the game clock pauses, a walk stops, the shade takes every click; after Send it shows the
folder and Close. What the viewer steps on the wall clock under any modal window (NPC goal runs, flights in progress)
keeps going under it too.

- **Log.** `BugReportLog.Process`, an in-memory ring buffer of the last 120 s (at most 20,000 lines and 4 million
  characters; a burst drops its oldest lines early and the report counts them), filled from the app's first line by
  an engine logger (`BugReportLogger`, every engine message and error) and by session-event lines: the map, the
  hero's cell and phase, combat mode and round, the open windows (one line per change), stored message-log entries
  and fired queue events.
- **Folder.** `BugReportWriter` writes `reports/YYYY-MM-DD_HH-MM-SS/` (local time, `_2`… in the same second) with
  `report.md`, `log.txt`, `screenshot.png` and `save/slot0000.json` with its details and preview (a one-save save list;
  `GameSaveLibrary` lists it and the quick load's path loads it). A packaged game (`steamwoven_package`) writes beside
  the folder it was launched from (the folder of the `.app`, or of the exe); a translocated app, or a folder that cannot
  take a written file, falls back to the user folder's `reports` with the reason. A source run uses its user folder;
  an automated run its isolated folder. No upload.
- **Smoke.** Stage `report` (last in the map battery, or `ARC_AREA_MAP_SMOKE_ONLY=report`): Ctrl+B opens the modal
  report, the moment holds a save, the log line before the key and a screenshot; Send writes the folder whose
  `report.md` names the build, rules, map, hero and combat, whose log holds the line and whose save loads; Escape
  closes it; Ctrl+B over the logbook and Escape close only the report; a cancelled report writes nothing. Marker
  `ARC-DEV-051 native-smoke bug report PASS`. Player instruction: [alpha build](../player/alpha-build.md#9-reporting-a-bug-ctrlb).

### Main menu (ARC-UI-011)

With `--main-menu true` (launcher: `./arcanum area`, which opens it by default since ARC-UI-012 unless `--no-main-menu` or `--hero`, or `./arcanum map NAME --main-menu`; refused with
`--hero`, as the menu picks the hero) the AREA viewer opens the original's main menu over the scene once it is ready,
the first time in a run (when that first scene has no game session, for example a map outside the map list, the menu
does not open and the log says so: `ARC-UI-011 main menu not opened`). The scene's session is the placeholder the original also has before its menu (its sound is not the scene's: the original's game init opens the shopping map and its sector pair 26/29 plays `arcanum.mp3`, ARC-DEV-332, [SN38](../research/audio-evidence.md); the viewer plays that pair through a scheme player of its own, without the session or the scene, from the menu's opening, ARC-UI-139, [scheme player](../audio/scheme-player.md)); the menu
replaces it. The windows, buttons, texts, keys and back moves are the original's
([ledger MM1–MM7](../research/ledger/ui-audio.md#main-menu-arc-ui-011)); since ARC-UI-058 the button windows have the original's art
and layout (below), the other windows the project's.

- **Early menu (ARC-DEV-381, O1 of the [load-time diagnosis](../project/status/tools.md)).** The first opening of the run
  does not wait for the start map: the viewer starts the map's load on its worker and opens the menu as soon as the menu art is read
  (`ARC-DEV-381 early main menu open`; the probe's `start-to-menu` in the packaged build went from the whole map's publication to about
  1.6 s on a loaded Mac, see the diagnosis). The menu is the same one: its windows, texts, art, keys and back moves are unchanged. No scene
  exists yet, so what opens a window the menu itself draws works at once (Single Player, New Game, the pre-made browser, Options, Exit
  Game), and a choice that needs the scene is **held**: Start in the browser and New Character (Load Game and Last Save were held too until
  ARC-DEV-387, below). A held choice shows
  a note, the menu takes no other input, Escape (or the browser's Back arrow) takes the choice back, and the scene's publication attaches the menu to
  it (`ARC-DEV-381 early main menu attached to the scene`): the menu takes the scene's answer for a new game (`NewGameRefusal`), plays the
  shop map's pair and runs the held choice as if it were pressed then. A start scene that fails to load closes the menu and shows the
  failure's own screen (a later successful publication of it opens the menu as before); a first scene with no game session closes the menu again, as one
  that would not have opened. Later openings (after Quit, after a death, in play) open over a published scene, as before. Smoke: the `menu`
  stage checks that the menu opened before the publication and attached to it, then plays the waiting again on the published scene
  (a held choice, no other input, Escape taking it back, the attach running a held Last Save, a held Start starting nothing).
- **The start map loads on demand (ARC-DEV-387, follow-up of O1).** In map mode (`--area-map`) with the menu the start map is not loaded at
  the start at all: the menu opens (`ARC-DEV-381 early main menu open ... is not loaded yet`) and the map's load begins when the player
  enters the New Game window, the way of a new game (`AreaMainMenu.NeedsStartScene`: New Game, the pre-made browser, New Character, the
  editor, the shop; `ARC-DEV-387 start map load begins`). Single Player, Options, Load Game and Last Save need no start map. **Last Save**, and
  **a save chosen in the load window**, load their own map at once instead: `AreaSaveLaunch` reads the save for the map it was made on (its
  runtime number, through the game's map list) and that map loads as the scene the load runs on (the saved cell as the arrival tile, a map
  too large to load whole opening its window around it; no hero, default data rules), then the save loads into it as a load on a published scene
  does (`SceneFlowKind.LoadSave`, `ARC-DEV-387 save chosen before any scene`, one map load instead of the start map's and then the save's). While it
  loads the screen is black with the original's clock in the top-right corner (ARC-UI-1362, the change of place's load screen), the menu under it
  takes no input and has no wait note; it goes when the save is in, and the menu comes back with the reason when the save does not load. A save chosen while the start map is already loading replaces that load. The start map loads when a new game's
  way begins; a choice that needs it is held as above. **The save folder follows the launch map (ARC-DEV-407):** the load window and Last Save read
  `saves/map` from the start, not only once a scene stands (`AreaSaveFolder`; the lazy start used to read the Crash Site folder `saves`, so the Load Game list was
  empty until the start map had loaded, bug reports 2026-11-11). Not changed: the exterior mode (no `--area-map`) loads the start scene at once and
  holds every choice that needs it (ARC-DEV-381); a smoke takes the player's route (Single Player, New Game) itself to start the load, as its
  stages run on the published scene. Known difference: the menu's own music is played through the start scene's session, so it starts when the start map is
  loaded (at New Game) and the main window is silent before; a follow-up is needed to play it without a scene. Smoke: the `menu` stage checks the
  held choices as above and then Last Save pressed while no scene stands (`last-save-before-scene`).
- **Original look (ARC-UI-058,** [plan](../ui/original-windows.md#main-menu-done-arc-ui-058),
  [ledger MW1–MW6](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058); `AreaMenuArtLayout`, `AreaArtFont`, `AreaMenuArtView`):
  with the game's art the main, single-player, new-game and in-play windows draw their background (329 `MainMenuBack`, 331
  `SinglePlayerBack`) over the 800 × 600 screen at the bars' whole factor, centred and black around, and their labels in the
  `Morph30Font` art font centred on (410, 143 + 50 × i): black at (−1, −1), brown at (+1, +1), the dark red face on top, bright red
  under the pointer. The pointer over a label plays 3026, a press 3027, a release over it chooses it; letters, Escape and the
  controller as before (the labels show no letters, as the original's; the focused label is lit at the window's opening and after a
  focus move, PROJECT; the pointer leaving the screen darkens it). PROJECT: hidden buttons leave no gap; a line at the top
  names them and the window's notice. ARC-UI-059: the options window too (below); ARC-UI-062 the save and load windows; ARC-UI-066 the
  pre-made browser (under New game); ARC-UI-068 the new-character window (its section); ARC-UI-069 the opening shop (its section). The editor window keeps
  the project's panel (plan); ARC-DEV-334: the in-play quit question is the original's dialog box (below, with the death load box); the synthetic self-tests (no game folder) keep the panel for every window. The menu stage checks it first
  (`ARC-UI-058 native-smoke menu art PASS`: the art read, the labels at their places, the pointer lighting Single Player and a click
  opening it); `ARC_AREA_MENU_CAPTURE=PATH` saves the main window's picture to an ignored path.
- **Art-font text blend (ARC-UI-070,** [ledger MW31](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058);
  `AreaArtTextRules`, `AreaArtFontFace`, `AreaArtTextDraw`): every art-font text of the menus, the options controls, the save and
  load windows, the pre-made and new-character windows and the dialog box goes through one draw that blends as the original's
  software art renderer: a font without flags 0x80 and 0x100 mixes its colour into the screen by the glyph pixel's red level (smooth
  glyph edges), flag 0x100 (the menu labels' brown pass) adds glyph × colour, flag 0x80 writes it opaque. A canvas is made ready by
  `AreaArtTextDraw.Prepare` (premultiplied alpha; its other draws must be opaque or fully transparent with black transparent pixels,
  as decoded art is) and keeps the order of its draws; the message panel and hover window (art 27/229), the floaters (art 229) and
  the conversation window in the bottom panel are planned to use it next. The menu stage checks it after the art (`ARC-UI-070 native-smoke menu text blend PASS`: the main
  window's labels, read back, within two levels a channel of the background with the three passes rendered by `AreaArtTextRules`, and
  the font holding partly covering edge pixels).

- **Windows.** Main (Single Player, Options, Exit Game), Single Player (New Game, Load Game, Last Save, Cancel) and New Game
  (Pick Character, New Character, Cancel), in the original's order; Options opens the [options window](#options-window-arc-ui-013),
  New Character the [new-character window](#new-character-window-arc-ui-030) (ARC-UI-030).
  The original's MultiPlayer, Credits and View Intro are hidden, as Steamwoven does not have them yet
  (network play, credits, movies); each window names its hidden buttons and why. A button opens its
  window over the current one; Cancel and Escape go back; Escape on the main window does nothing (as at the
  original's first opening; when the original's menu opens again later, Escape there exits the game).
- **Texts and keys.** The labels and hotkey letters are the game's `mes\MainMenu.mes` (the first character of the
  label's message + 10, upper-cased), read through the game folder's content stack in the game-start phase; without it the
  project's English text is used and the window says why. A typed letter chooses its button (Shift does not
  matter); a click does too. A controller moves over the buttons, confirms and goes back.
- **New game.** Pick Character opens the pre-made browser: the twelve heroes in main-menu order from the first,
  Previous and Next cycling, each shown with its prototype, race, gender, level, gold and kit as read (each hero is
  read once a run, as `--hero` reads it). **Start** makes a new session as the scene's launch made its own (the
  module's clock, the map's start cell, its reputations) with that hero (its stats, gold, kit, best items worn,
  main-menu flag; since ARC-UI-068 its background's description number, field 0x102, its story) and adopts it as a load adopts its session; the menu closes. The hero's effect rules and kit facts
  come with it, and the scene's arguments name the hero from then on, so a map transfer reads the hero again with the
  map (as `--hero` does). A new game needs the start map at the map list's start, as the original's new game puts the
  PC there: the AREA exterior, or map mode on runtime map 1 (`./arcanum area`'s `Arcanum1-024-fixed`) opened without
  `--sector`; on another map, in the cave, with `--sector` or with a session a transfer carried, Start first loads the
  start scene ([scene flow](#scene-flow-arc-ui-012), ARC-UI-012). The original's start movies, fade, newspaper event and
  random-encounter schedule are not modelled.
- **Browser's original look (ARC-UI-066,** [ledger MW17–MW21, MW26](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058)): with the
  game's art the browser is window 13's screen (`AreaPremadeBrowserArtView`): the strips, the background 766 and the browsed hero
  as the original draws it: the large portrait at (95, 69), the player name (`Cloister18Font`, red), the gender, race and
  background names (`morph15font`, red) centred in their fields, the hero's story from `gameback.mes` (its background description,
  field 0x102: 1039..1050), "Initial Stats" and fifteen stats "ST: 8" of the start's effective values (the stored stats under the
  race and gender effects), each field filled black first. Start and Back stand in the bottom strip, Previous and Next beside the
  portrait; they light under the pointer, play 3000 on the press and 3001 on the release and act on a release over themselves.
  The story breaks into lines as the original's text draw does (MW26) and shows its first 11, as the original's: every installed
  story is longer (14–18 lines). PROJECT: the hints are not shown; a portrait that does not read leaves its field black. A hero that does not read, and every self-test
  without a game folder, keeps the diagnostic window. The menu stage checks it after opening the browser
  (`ARC-UI-066 native-smoke browser art PASS`: the art read, the first hero's texts, its portrait, Next lit under the pointer, a
  click on Next showing the second hero and on Previous the first again, Back to the new-game window, each story's line count
  in the PASS line; the menu smoke's new game then starts by a click on Start); `ARC_AREA_PREMADE_CAPTURE=PATH` saves the browser's
  picture to an ignored path.
- **Last Save.** Loads the newest save of the [save list](#save-list-arc-ui-010) (F8's choice) through the load path
  and closes the menu; with no save the window stays (the original says nothing; here the window says "no save to
  continue from"); a load refused before anything changes keeps the menu and names the reason. A save of another map
  (map mode) loads that map: the menu ends as the scene is taken down, and if that map's load then fails the shown map
  comes back without the menu (and keeps the loaded hero's tables and name in its arguments, so a reload starts as that hero).
- **Hero tables of a load.** The placeholder session has no hero, so a hero save loaded from the menu (Last Save or
  Load Game) takes the game-wide experience and auto-level data from the pre-made heroes' tables, and the loaded
  pre-made hero's effect rules and kit facts are adopted with it (the scene's arguments name that hero). F8 and the
  palette's load window keep the running session's tables, as before.
- **Load Game.** Opens the save list's load window with the menu waiting under it: a load closes both; closing the
  window unused brings the Single Player window back.
- **Exit Game** quits the viewer. The menu is modal like the logbook (the gate closes, the clock pauses) and owns
  every key; F7, F8 and the window letters do nothing under it. Escape on the main window does nothing the first time it
  opens and exits the game once it opens again after a quit ([scene flow](#scene-flow-arc-ui-012)).

Self-test `ARC-UI-011 self-test PASS synthetic main menu` (the production app over the synthetic area owner with
`--main-menu true`, a temporary save folder and synthetic heroes): opened modal with the offered buttons, letters and
Escape (nothing at the root), Last Save with no save staying, the browser cycling, Start beginning a new game as the
browsed hero under a new session, Last Save loading the newest save, Load Game's window over the waiting menu (back
when closed unused, ending the menu on a load), a controller's Exit. Native smoke on the original data, map mode on
the start map: `ARC_AREA_MAP_SMOKE_ONLY=menu` with `--main-menu true` prints `ARC-UI-011 native-smoke main menu PASS`
(the game's texts, Last Save with no save, the real first hero read and started at the map list's start
(92958, 82592) with its prototype, gold and flag, then F7 and Last Save loading it); the `menu` stage runs only when
named, as the open menu blocks the other stages. Not done: the character editor (the new-character window is
[ARC-UI-030](#new-character-window-arc-ui-030)), credits, movies and network play, the original art of the menu windows other than the
button windows ([plan](../ui/original-windows.md#menu-windows-plan-arc-ui-058)); the scene flow is
[ARC-UI-012](#scene-flow-arc-ui-012).

### Scene flow (ARC-UI-012)

The way between the main menu and play in area (exterior, cave and map mode), as the original's
([ledger SF1–SF5](../research/ledger/ui-audio.md#scene-flow-arc-ui-012)); the art and layout are the project's.

- **In-play menu.** In play, Escape (no palette, dialog or window open, the window focused, no load in progress)
  opens the same menu at the original's in-play window: Save Game, Load Game, Quit and Continue, with the game's
  `MainMenu.mes` labels and letters (messages 30–34); Options opens the [options window](#options-window-arc-ui-013). In a
  turn-based round on another combatant's turn it opens the original's short window (Options, Quit and Continue, messages
  70–72; the original also opens it over its dialog and world map, which here are modal windows that take Escape). Escape or Continue closes it and play goes on; the menu is modal like the main menu.
- **Save Game and Load Game** open the save list's windows over the waiting menu (the save window once the game is
  drawn without the menu, for its preview). A save closes both, as the original's; a load closes both and keeps the
  running session's hero tables, as F8; closing the window unused brings the menu back.
- **Quit** asks first with the original's question (message 5100; Y, Return or Yes quits, N, No or Escape keeps playing). On
  yes the game ends as the original resets its game and loads its module again: the start scene loads from the
  launch arguments (the exterior, or in map mode the map list's runtime map 1, at its map-list start, without
  `--sector`, a hero or a carried session) with a fresh session as a launch makes it, and the main menu opens over
  it again. Saves are not touched and nothing is saved. Escape on that main window now exits the game (MM6).
- **New game from anywhere.** Start in the pre-made browser on a scene that is not the start scene (another map, the
  cave, a `--sector` start, a session a transfer or a load brought) loads the start scene the same way and starts the
  new game there with the picked hero; the menu closes as the load begins. When the start map is not known on the
  shown map (map mode without the map list's runtime map 1 among its destinations) Pick Character is hidden with that
  reason.
- **A failed start scene.** When the start scene does not load, its failure stays on screen with its stage and
  nothing runs on a later scene (the original stops with a fatal error when it cannot load its module again). A
  palette Reload or Reset started while the start scene loads supersedes it: that scene then opens with its fresh
  session and no menu, and the quit or new game is dropped (logged).

Self-test `ARC-UI-012 self-test PASS synthetic scene flow` (the production app over the synthetic area owner
launched without the menu, a temporary save folder and synthetic heroes; the start scene is the synthetic scene
published again): Escape opening the in-play window, Escape and Continue closing it, Save Game saving and ending it,
Load Game coming back unused, Quit's box (N and Escape keeping play, Y loading the start scene with a fresh session
and the main menu reopened, the save kept), a new game off the start scene (a `--sector` start) loading the start
scene and starting there, the reopened root's Escape exiting. Native smoke on the original data, map mode on another
map than the start map with `--main-menu true` (for example `crematorium-lev 1`): `ARC_AREA_MAP_SMOKE_ONLY=scene`
prints `ARC-UI-012 native-smoke scene flow PASS` (the game's in-play texts and quit question, Start loading the start
map `arcanum1-024-fixed` and beginning the game there as the first hero at the map list's start, Escape's in-play
window, Save Game saving, Quit asking in the original's dialog box (ARC-DEV-334: a release over No answering no and the window staying), Y loading the start map again with a fresh session and the main menu reopened, the save
kept, its root Escape exiting). The short window 4 is covered by the core tests only (no turn-based round on another's
turn in the self-test or the smoke). Not done: the start movies, the menu after the game's end (the original restarts
it there; after the hero's death see [Hero death](#hero-death-arc-ui-026)), the original menu art and button sounds.

### Hero death (ARC-UI-026)

What follows the hero's death in area (exterior, cave and map mode), as the original's in single player
([ledger PD1–PD9](../research/ledger/ui-audio.md#hero-death-arc-ui-026)); the core is
[`AreaHeroDeath`](../../src/Arcanum.World/AreaHeroDeath.cs), the art and timing the original's.

- **The wait.** Five seconds after the session's hero dies (real time; the original counts from the end of the PC's dying
  animation, which the viewer does not play for the hero) the death comes. Play goes on meanwhile. When it comes the
  hero is checked again (one brought back to life meanwhile is not reset), the inventory and barter windows close, and an
  open main-menu window (the in-play menu, its save and load windows, the options) closes and the death comes again
  0.3 s later. A session whose hero is dead already when it comes (a load) starts nothing: only a death does.
- **The slides.** The screen fades to black (2 s) and owns every key and mouse button from then on; the world stands still
  under it (the game clock, the walk, the scene's sounds). The default death slide of the installed `rules\slide.mes`
  (message 0, read at module load: its first word names the picture in `slide\`, its second the voice in
  `sound\speech\slide\`; the installed one is `death.bmp` with `DeathSound.mp3`) fades in (2 s) while the slides' music
  `sound\music\DwarvenMusic.mp3` loops, then its voice plays. The slide ends when the voice ends (at once without one),
  on a key's release or a left, right or middle button release (presses do nothing); Escape ends the whole show. Input
  during a fade does nothing. The slide fades out (2 s). The fades take their 2 s in both rule modes; the original's
  flash by, a technical bug (PD9). A slide whose message names no picture and voice is skipped; one whose picture cannot be read shows
  nothing and fades out. Without the table (no game folder) no slide shows: the fade to black leads straight to the reset.
  Scripts that queue their own slides do not run (the installed data has none for a death).
- **The reset.** The game ends as a quit does ([scene flow](#scene-flow-arc-ui-012)): the start scene loads from the launch
  arguments with a fresh session and the main menu reopens over it (its root Escape exits), fading in from black (2 s).
  Saves are not touched; a load is the main menu's (Single Player, Load Game or Last Save). There is no window with a
  choice in the original (the Steamwoven load box below comes first). A reset refused (another map or the start scene loading, a reset in progress) is tried again every 0.3 s with
  the screen black; when no start scene is known (no launch arguments, a start map the shown map does not know), or no
  menu has come 2 minutes after the first try, the screen fades in over the game as it is (the original's reset cannot
  be refused). A start scene that fails shows its failure.
- **The load box (ARC-UI-028, Steamwoven only; look and save rule ARC-WORLD-120).** A project addition the owner asked for
  ([decisions](../architecture/decisions.md), switch `LifecycleRuleProfile.DeathResetsToMainMenu`, which keeps the reset
  above under the Original setting, read from the session's rules). When the dead hero has a save (the newest save whose
  details name the same hero and, when both record one, the same portrait; a save of another hero, or with missing or
  stale details, is not offered), the box asks as soon as the death slide has faded in, over the slide, whose voice and
  music go on (the slide then ends neither on a key nor at the voice's end); when no slide showed, at the point of the
  reset over the black screen. It is the original's common dialog box of its quit question ([ledger
  DB1–DB4](../research/ledger/ui-audio.md#dialog-box-arc-world-120)): `DialogueBox` 325 × 136 at the 800 × 600 screen's
  (237, 232), the 800 × 600 screen fitted to the window as the slide's picture is (PROJECT), the project's question "You
  have died. Load this hero's last save?" (the game's message files hold no such question) and the save's description,
  a line each, in white `Flare12Font`, the green check `Lil_Accept` (Yes) and the red cross `Lil_Cancel` (No), lit under
  the pointer, down while held, acting on the release over them and silent. Y, Return, the check or the controller's
  confirm loads the save as F8 does (on the shown map at once, a save on another map or beyond the main map's shown window
  after that map or window loads) while the slide fades to black (2 s); then the loaded game fades in (2 s, or after 2
  minutes without the map) and play goes on. A refused load (a damaged save, another map loading) keeps the box with the
  reason in place of the description, and so does a load that brings no living hero (the save's map failed to load and the
  old game came back, or the save's hero is dead; the slide has faded out by then, so this box stands over the black
  screen and its No resets at once); a window stream held under the screen is dropped first, as the load replaces
  the scene. N, Escape, the cross or the controller's cancel fades the slide out and ends the game with the reset above.
  Without a save of this hero the show and the reset are the original's. The box acts on key presses (the slides on
  releases); its buttons take the mouse, nothing under the screen does. Without the game's art (the synthetic
  self-tests) the box is the diagnostic parchment box with the question, the save, Yes and No.

Automated runs (self-tests, native smokes) keep the death off unless their own check turns it on, as many of them kill
or wound the hero for other checks. Self-test `ARC-UI-026 self-test PASS synthetic hero death` (the production app over
the synthetic area owner, a synthetic slide table, picture and voice, the start scene published again): a new game's
hero dies, the in-play menu opened meanwhile closes after 5 s and the death comes 0.3 s later, the fade to black owns
the input (Escape opens nothing), the 800 × 600 picture fades in, its voice starts, a key ends it, the reset loads the
start scene with a fresh session and the reopened main menu, which fades in; a hero brought back to life before the
death comes is not reset; a voice's end ends the slide; a start scene that does not publish clears the screen over
the game as it is. ARC-UI-028 and ARC-WORLD-120 in the same self-test: with the hero's save in the list the Steamwoven
setting asks over the faded-in slide (the diagnostic box: the question, the save "Auto-Save (quicksave)", Yes and No;
another key and the voice's end do nothing), Y loads the save with its living hero and no reset while the slide fades to
black, and it fades in; a damaged save keeps the box with the reason and Escape then fades the slide out and resets; the
No button and the N key do the same; under Original the death resets without the box though a save is there; with only
another hero's save the box does not ask. Native smoke on the original data, map mode on the start map
with `--main-menu true`: `ARC_AREA_MAP_SMOKE_ONLY=death` prints `ARC-UI-026 native-smoke death PASS` and
`ARC-UI-028 native-smoke death load PASS` (a new game as the first pre-made hero saved into a temporary save folder,
the hero killed through the session, the death after 5 s on the wall clock, the installed default slide's 800 × 600
picture with the slides' music and its voice; ARC-WORLD-120: a newer save of another hero is written first, and the
original's dialog box asks over the slide with the hero's own save, the pointer over the check lights it and a left click
on it loads the save, which fades in with its hero alive; the loaded hero killed under Original,
Escape ending the show, the start map loaded again with a fresh session and the main menu reopened, without the box). `ARC_AREA_DEATH_CAPTURE=PATH.png` saves the shown
slide with the box (original art: never into git). Not done: script-queued slides, the end-game and credits slides, the hero in a monster body plays 17–19 only where its species has the file (ARC-COMBAT-1865; the human bodies play them by the killing blow, ARC-COMBAT-1793). Since ARC-COMBAT-1741 the dying goal (31) lasts while the hero's fall plays and the death's 5 s count from its end (`IAreaDeathHost.HeroDying`).


### Slide shows: end game and credits (ARC-RENDER-1171)

The death's show loop (`AreaHeroDeath`, which keeps its name) serves the other two slide show types of the original too ([ledger SS1–SS6](../research/ledger/ui-audio.md#slide-shows-end-game-credits-and-the-queue-arc-render-1171)).
`AreaSlideScripts.Run` runs the show type's script (997 end game, 998 death, 999 credits) with `ScrAdmissionSet.SlideScripts`, the hero as the player and the session's flags and quest log; its queue slide
actions (131) give the slide numbers (up to 100), which the table `rules\slide.mes` turns into a picture and a voice as for the death. A death with nothing queued still shows slide 0 (PD4).

- **Credits:** the main menu's Credits button (shown when the game's slide table was read, else hidden with the reason) fades the screen to black over the menu, shows the slides of script 999 (45 to 55 in the
  installed game) and fades back in over the menu, whose own song stops for the show and plays again after it. A key or a mouse button's release ends a slide, Escape the show.
- **End game:** the script action end game and play slides (132; Kerghan's dialog script 1105 and the movie scripts 2862, 2863 and 3061 run it) asks the viewer, which waits for the movies of the same run and any other
  show, then starts the show 50 ms later (a main-menu window open closes and retries 300 ms later): script 997 and the ending scripts it calls queue the slides by flags and quest states; no default slide; then the
  game resets to the main menu as after a death (no load box). The smoke stage `slides` (ARC-RENDER-1879; start map, `--main-menu true`, ffmpeg for the movies, wall clock) walks the whole ending on
  the installed data: after a new game the installed dying script of Kerghan (2863: movies 2 and 5, then 132) runs through the use-script host on a creature row standing for Kerghan on the shown map
  (the dying run the kill makes, 0x45DA57); both movies play from the installed table through the real decoder (800 × 400) with the session clocks standing still and no slide beginning under them; then the
  show starts and shows script 997's queue for the game's flags (46, 20, 33, 39 and 44 for a new game), each installed 800 × 600 picture fading in over about 2 s with the slides' music and its voice, a key
  release ending it and a 2 s fade-out; then the start map loads again with a fresh session without a hero and the main menu fades in. Without ffmpeg the movies are passed over (said in the log) and
  the rest runs. `ARC_AREA_SLIDES_CAPTURE=FOLDER` saves a frame of each movie, each slide and the reopened menu as PNG for a graphics report (never committed).

### Options window (ARC-UI-013)

The original's options window (its window 6, [ledger OP1–OP8](../research/ledger/ui-audio.md#options-window-arc-ui-013))
in area, opened by Options in the main menu and in both in-play windows; the art and layout are the
project's, except as below.

- **Original look (ARC-UI-059,** [ledger MW7–MW8](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058)): with the game's
  art the window draws its background 556 `OptionsMenuBack` over the 800 × 600 screen as the main menu's windows do, and its page
  buttons Game, Video, Sound and Done as art-font labels centred on (130, 221 + 40 × i), lit under the pointer, sounding and
  choosing as the main menu's labels (the focused one lit while the focus is on a page button). ARC-UI-061
  ([ledger MW9–MW10](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058)): each control in its own art at its slot (MW8):
  its background (688 `OptionSlider` or 671 `OptionMultiChoice`, 213 × 56), its label centred on it in `morph15font` (white), the
  arrows 667 and 668 at (+6, +21) and (+180, +21), a slider's bar 623 `SkilGauge` at (+47, +24) as wide as value × 120 / top, a
  list's value centred in red `Flare12Font` at (+47, +30). An arrow is a push button: the pointer over it shows its lit frame, a
  press its down frame with sound 3000, the release over it plays 3001 and changes the value. PROJECT: the shown controls take the
  page's first slots in order (the Violence Filter (ARC-UI-1737) is the Game page's second line and the Rules line its eighth (ARC-COMBAT-1777 put Follower Skills before it); ARC-UI-1737 moved the Controls line (ARC-GPAD-1463), ARC-GPAD-1374's Prompt style line and the evidence log's two lines (ARC-GPAD-1376) to the Video page, which held Brightness, Floats, Float Speed, Combat Taunts, Controls, Prompt style, Evidence Log and Test Task, the eight slots of a page (ARC-UI-1797 made the last two one line, Off, On, Task 1 to 8, so the page holds Brightness, Text Duration, Floats, Float Speed, Combat Taunts, Controls, Prompt style and Evidence Log), so the Game page keeps its two free slots for Auto Switch Weapons (shown since ARC-COMBAT-1738, the Game page's fifth line) and Follower Skills (issues 1776 and 1777); ARC-UI-146 removed the HD graphics, HD interface and HD pack lines that stood after Rules, they are the [launcher screen](#launcher-screen-arc-ui-014)'s); the control with the keyboard or controller
  focus has its label lit in the page buttons' hover red; a text is one line. The line at the top names the hidden controls and the
  notice. Without the game's art (the synthetic self-tests) the whole window is the project's panel.

- **Pages.** The buttons Game, Video, Sound and Done with the game's `MainMenu.mes` labels and letters (messages 4000–4003,
  letters G, V, S, D); the window opens at the Game page each time. The letters act as in the other menu windows, in both
  rule modes; the original's options window ignores them (an original bug: its hotkey path runs such buttons only in
  the in-play windows, OP7), which the owner chose to fix everywhere without a switch (2026-10-28). The Video page (ARC-UI-021) shows Floats and
  Float Speed, the [floating text](#floating-text-arc-ui-021)'s level and speed, and Combat Taunts (ARC-AI-1527, default on, the settings file's `combat taunts` line: the followers' comment lines); ARC-UI-1737 adds Brightness first (0–9, default 0, the `brightness` line; the final colour multiply 1.0 + 0.1 × the value over the whole picture, interface and fades included, drawn by `AreaBrightnessLayer` and hidden at 0 so the picture is today's byte for byte; ledger OH1) and the Game page's Violence Filter (off/on, default off, the `violence filter` line; ledger OH2: no pool of blood, a dying critter falls down plainly, the hit's blood effect index is the clean one); ARC-UI-1797 shows Text Duration as the page's second line (a slider 0–10, default 6, the `text duration` line, the original's setting; the text bubbles that would live that many seconds are not built, so nothing reads it yet).
- **Controls.** Each control shows its `options_ui.mes` label (message 100 + 100 × page + slot) and its value: a
  list's text from its value file (`OptionsDifficulty.mes`, `OptionsCombatMode.mes`, `OptionsOffOn.mes`), a slider as
  `N/10`. Game: Difficulty (Easy, Moderate, Hard), Violence Filter, Default Combat Mode (Real Time, Turn Based), Auto Attack, Auto Switch Weapons (Off, On; default Off, ARC-COMBAT-1738), Always
  Run, Follower Skills (Off, On; default On, ARC-COMBAT-1777: the lockpick use and the skill bar's Disarm Traps go to the best follower, [below](#follower-skills-arc-combat-1777)) and the project's Rules line (ARC-UI-014, below). Video: Brightness (0–9), Floats (Never, Minimal, Verbose) and Float Speed (0–10). Sound: Effects, Voice and Music (0–10). The arrows (or Left and Right on the focused control, which Up and Down or
  a click on an arrow choose, or a controller's confirm) change the value at once, as the original's setters run on each step: a list wraps around at either end, a
  slider stops there. Hidden with their reasons: Module (one module) and the third combat mode Fast Turn Based (the list cycles Real Time and Turn Based).
- **What each setting does.** Difficulty is the session's difficulty (its skill and experience readers, ARC-WORLD-030;
  a new game or a load takes the setting). Default Combat Mode is the turn-based setting Space also toggles: with a
  hero it goes through the hero's turn-based switch (a hero in combat mode starts or ends a round; a refusal keeps the
  value and names why, where the original's setting keeps the new value, OP5), without one the setting changes alone;
  a quick load that brings a round turns it on and saves it. Auto Attack is the real-time attack repeat's setting
  (ARC-COMBAT-005). Floats and Float Speed are the floating text's level and step (ARC-UI-021). Always Run and the three
  volumes are the palette's settings (ARC-WORLD-020, ARC-UI-044).
- **Rules (ARC-UI-014).** A line the original's table does not have, after its eight Game slots, with project texts
  only (the label and values never come from the game's files): Steamwoven (default) or Original, the two built-in
  [rule modes](../world/rule-modes.md); custom mixes come after alpha (owner, 2026-10-28). A change switches the
  session at once (`GameSessionOwner.SelectRules`; the contract makes a switch in play safe), and every session the
  viewer then makes, loads or brings (start, new game, quick load, save load, map transfer) takes the chosen mode,
  together with the difficulty. It is stored in the launcher's settings (the `launcher` line of `arcanum.cfg`, `rules.active`),
  which the [launcher screen](#launcher-screen-arc-ui-014) writes too, and every ordinary run starts in it.
- **Storage.** User preferences, never part of a save, as lines of the one settings file `user://arcanum.cfg`
  ([below](#the-one-settings-file-arc-ui-1130)): `difficulty` 0–2 (default 1), `turn-based` (0), `auto attack` (1), `text floaters` 0–2 (1) and `float speed` 0–10 (2)
  with the original's names and defaults, and the project's `hd interface`, `generated interface`, `hd pack` (`auto`, `game`, `custom`) and
  `hd pack path`; Always Run and the volumes are lines of the same file. A missing line or a value out of its range reads as that setting's
  default and no other setting changes. Each change is saved at
  once (the original writes `arcanum.cfg` when the game exits). Space's toggle also saves the turn-based setting now. An
  automated run (self-test, native smoke) reads and writes no file; `GAME_SETTINGS_TEST_PATH` names a settings file for a test.
- **Opening by O (ARC-UI-2026, OP9).** The release of the O key opens this window at its Game page in play, over any window and over the dialog (the press only arms it; Escape's route is the same). It raises no pause (the Esc menu does) and refuses only while a text entry or a menu is open; the inventory family closes under it as under Escape. The dialog stays and goes on after Done. The key is the physical O key (the scan code, as in the options window's own O), whatever the layout types on it; Ctrl+O and Alt+O do nothing. A hero's death behind the open window closes it, and the death screen comes 0.3 s later (ARC-UI-2066).
- **Closing.** Done goes back to the main menu, or from play closes the menu and play goes on; from play the O key
  does the same. Escape does nothing in this window, as in the original's key table.

Self-test `ARC-UI-013 self-test PASS synthetic options window` (the production app over the synthetic area owner
launched without the menu, no preference file): in-play Options opening the window at its Game page, each arrow
applying the difficulty (on the session), the turn-based setting, auto attack, Always Run, the floaters' level and
speed on the Video page (Text Duration, ARC-UI-1797, is shown) and a volume at once, lists wrapping and sliders stopping, Escape doing nothing, Done and O closing the menu
from play, Done going back from the main menu, the Rules line switching the session to Original and back. Native smoke on the original data, map mode on the start map with
`--main-menu true`: `ARC_AREA_MAP_SMOKE_ONLY=options` prints `ARC-UI-013 native-smoke options PASS` (all 27 option
texts from the game, the original's defaults shown, Original chosen on the Rules line, a music step moving the channel,
Done back to the main menu, a new game as the first hero beginning under the Original rules, then in play a difficulty
step reaching the session, the default combat mode going through the hero's turn-based switch and back, Rules back to
Steamwoven on the session, the O key closing the menu; ARC-UI-059: the art read, the page labels at their places, none lit
while a control has the focus, the pointer lighting Sound and a click on it showing the Sound page; `ARC_AREA_OPTIONS_CAPTURE=PATH`
saves the Video page's picture to an ignored path (the Game page's next to it with `-game`); ARC-UI-061: the controls at the first slots on the Game and Sound pages, the
pointer lighting Music's left arrow, a press showing it down with 3000 and the release over it playing 3001 and lowering the music, a release away from it changing nothing;
the capture also saves the Sound page next to it with `-sound` before the extension). ARC-UI-1737: the Violence Filter line is saved at once, and the Brightness pixel check reads the same frame at 0, 5 and 9 and requires every steady sample to equal the original's ramp (`BrightnessRules.Apply`) and the frame at 0 again to be the first one; the `critter-fall` stage checks no pool and the fall-down with `ARC_CRITTER_FALL_FILTER=1` (and `ARC_CRITTER_FALL_BRIGHTNESS=N`, `ARC_CRITTER_FALL_CAPTURE=PATH` for the capture series). Not done: Text Duration,
Module and Fast Turn Based (no function yet).

### New-character window (ARC-UI-030)

N10b, slice 1 (FR-CHAR-001, FR-CHAR-003, FR-CHAR-005): the original's window 12, which New Character opens from the New Game
window ([ledger NW1–NW6](../research/ledger/character.md#new-character-window-arc-ui-030)); the art and layout are the project's
until ARC-UI-068 (the original look, below).

- **The character.** Opening the window makes the new character from the generic PC prototype 16066 (human, male, background 0,
  level 1 with every primary stat 8 and 5 points unspent), read once a run as the pre-made heroes are
  (`AreaStartingHero.NewCharacter`, which also reads the background table, ARC-DATA-044); a start that does not read keeps
  the New Game window with the reason. Leaving the window (Cancel or Escape) drops the character; the next opening makes a new one.
- **Rows.** Gender, Race and Background, each with the original's previous and next arrows. The race arrows step through the
  eight player races and wrap; a woman steps only through human, elf, half-elf and half-orc (NW2, NW5). Both gender arrows
  toggle the gender where the race allows a woman (NW4). A race or gender change clears the background (BD10); under the
  Original rule setting a gender press on a race without women clears it too and changes nothing else (NW4, the setting of
  the launcher and options window, read at each press). The background arrows step through the blocks the race and gender
  may take and wrap (BD4), and each press sets the landed block with its description number and effect. The background
  row shows the description's name and the text below it, the game's `mes\gameback.mes`; before any background press (or after
  a clear) the window shows the default text 1000 and the character keeps background 0 without its effect, as in the
  original. Race and gender names are the project's in the project's panel (the game's in the original look, below).
- **Portrait** (ARC-UI-033, FR-CHAR-004, [NW9](../research/ledger/character.md#new-character-name-and-portrait-arc-ui-033)):
  a row with the original's arrows over the portraits the race and gender may take (`PortraitCatalogue.Press`, PO9: next
  past the last takes the first, previous before the first takes the last); a race or gender change takes the first
  portrait of the new pair, a refused press keeps it. The window starts with the generic prototype's portrait 1005 and
  shows the portrait's image name and number in the project's panel (the large image in the original look, below). The catalogue is read with the generic start
  (`AreaHeroLoad.Portraits`); without it the arrows say so and a race change keeps the portrait.
- **Name** (ARC-UI-033, FR-CHAR-002, [NW6–NW8](../research/ledger/character.md#new-character-name-and-portrait-arc-ui-033)):
  the field starts with main-menu message 500 (in the game's encoding, the project's text without the game). A click on it
  (or the controller's confirm on its row) clears it and starts the typing, marked by a caret: typed characters the game's
  encoding (code page 1252) has go to its end up to 22 bytes, Backspace removes the last, Return ends the typing (an empty
  field takes message 500), and a press on any other button ends it first (an empty field takes back its old text). The
  cursor keys, Delete and Insert of the original's text edit are not offered, and each opening of the window starts the
  field with message 500 (the original keeps a name typed before Cancel, NW7). While it is typed into, letters go to the
  field; Escape still leaves the window, as in the original.
- **Accept** starts a new game as that character, as Start does for a pre-made hero (the same session recipe and scene
  flow): the session's hero is the generic prototype with the chosen race and gender, whose race effect (64 + race) and
  female effect (330) are its innate effects (NW3), then the chosen background is set with its effect (cause 1,
  [ARC-WORLD-097](../world/equipment-runtime.md#setting-and-clearing-the-background-arc-world-097)); no main-menu flag is
  set. ARC-UI-033: Accept first checks the name (NW6): a name equal to message 500 (an empty field takes it) is refused with
  message 506 as the window's notice; otherwise the session's hero starts with the chosen portrait and the name is stored as
  its player name (`SetHeroPlayerName`, snapshot schema 56), which the status lines then show. The new character's tables
  (effect rules, no kit) are adopted, and the scene's arguments name it, so a map
  transfer reads the generic start again; a load that makes a fresh session instead of carrying it (the palette's reload, an
  exterior scene load without a carried session) starts the generic human man with background 0, as it starts a pre-made hero
  afresh.
- **Keys.** The window's buttons are art only in the original: no letters. Escape and Cancel go back. A controller moves
  over the rows (gender, race, background, portrait, name), Accept and Cancel, and its confirm presses a row's next arrow,
  begins or ends the name's typing, or presses the button.

- **Original look (ARC-UI-068,** [ledger MW27–MW29](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058)): with the game's
  art the window is window 12's screen (`AreaNewCharacterArtView`, read with the browser's art): the strips, the background 765 and
  the character as the browser draws its hero (the shared character draw, `AreaCharacterArtDraw`): the large portrait, the gender,
  race and background names, "Initial Stats" and fifteen stats of the character's effective values (its stored stats under its
  race, gender and chosen background's effects, as the original's object carries them), and the description field: the
  background's story when its description number is above 1000, else "Race" and the race's text. The pointer over the gender's
  band (the gender row) shows the gender's text there instead, over the race's band the race's text (MW28). The name field
  (`Cloister18Font`, red) shows the typed name with a closing "|" while it is typed, centred, or its end when it is too wide
  (MW29). Accept and Cancel stand in the bottom strip, the portrait's arrows beside the portrait and the gender's, race's and
  background's arrows (757 / 758) beside their fields; they light under the pointer, play 3000 and 3001 and act on a release
  over themselves; the name field is an art-less button (3026 entering it, 3027 on its press, a release over it starts the
  typing), and a press elsewhere ends the typing first (NW8). PROJECT: the controller's focus row lights its buttons until the pointer moves; the hints
  are not shown; Accept's refusal (506) is the line at the top. Without the art (the self-tests) the window keeps the project's
  panel. The menu stage checks it after opening the window (`ARC-UI-068 native-smoke new character art PASS`: the art read, the
  generic human man's texts and portrait, the gender's and race's bands, a click on the race's right and left arrows, the name
  field's click and a press beside it); `ARC_AREA_NEWCHAR_CAPTURE=PATH` saves the window's picture to an ignored path.

Since ARC-UI-034 Accept opens the [character editor](#character-editor-in-creation-mode-arc-ui-034) instead of starting the
game, and since ARC-UI-036 the editor's Accept opens the [opening shop](#opening-shop-arc-ui-036), whose kit (the background's
money and items, the clothes, lockpicks and bandages) the new character starts with. Not built yet (later N10b slices): the
body type and art by race and gender are N15. Self-test `ARC-UI-011 self-test PASS synthetic main menu` covers the window (its rows, arrows, Escape
back, the default name refused, a name typed through the keys and an Accept that opens the editor and, through it, starts a
dwarf with a background and its effect, the name and the portrait); native smoke on the original data: the `menu` stage
(`ARC_AREA_MAP_SMOKE_ONLY=menu`) also opens New Character, presses gender on a dwarf man (refused; the background stays under Steamwoven) and makes an elf woman with a background,
steps her portraits, has Accept refuse the default name, types a name through the window's keys, accepts, spends in the
editor and checks the session (`ARC-UI-030 native-smoke new character PASS`, `ARC-UI-034 native-smoke character editor PASS`,
then `ARC-UI-033 native-smoke name and portrait PASS`). Checks: `AreaNewCharacterTests`, `CharacterNameRulesTests`,
`HeroNameTests`, `AreaStatsPageTests`, `AreaNewCharacterArtTests`.

### Character editor in creation mode (ARC-UI-034)

N10b, slice 3 (FR-CHAR-001, FR-CHAR-015, FR-CHAR-016, FR-CHAR-017, FR-CHAR-020): the original's window 14, which the
new-character window's Accept opens once the name passes ([ledger CM1–CM8](../research/ledger/character.md#character-editor-in-creation-mode-arc-ui-034)).
In the original it is the ordinary character editor (`charedit`) opened on the new PC in creation mode; its opening takes the
snapshot every refund stops at, so the five starting points can be spent and taken back while the window stays open.

- **The draft.** Steamwoven makes the new character's session only when the game starts (possibly after the start scene loads),
  so the editor works on a draft session made from the window's character exactly as the start makes it (the scene's
  new-game recipe, the race, gender, portrait, name and background with its effect; `AreaCreationEditor`), with its
  character editor open: the floor is the character's start (eight stats 8, nothing bought), as in the original (CM4). The
  draft is disposed when the window goes back to the new-character window or the menu ends (the opening shop keeps it). A scene without its new-game recipe (its session came from
  a transfer or a load) cannot open the editor and names the reason.
- **Pages.** The character sheet in its creation mode above the main menu's backdrop: the sheet's values, the stats page
  (hit points, fatigue, the eight stats), the skills page and the spells page with their `−` and `+` (the in-game sheet's
  rules and texts, [skills page](#skills-page-arc-ui-009)) and, since ARC-MAGIC-043, a tech page with the eight disciplines'
  `−` and `+` (the original's degree page of the same editor, CM8; the pool's technology lane, refused below the snapshot the
  editor opened on, TC10; project labels; the original look of the page since ARC-UI-147, below), and Cancel and
  Accept. Each press that changes the draft is recorded (the replay at Accept is by track, so degrees replay like the other
  points); a refused one is not.
- **Accept** (or Return, CM5) opens the [opening shop](#opening-shop-arc-ui-036) (window 15, CM2), whose Accept starts the new
  game as Start does for a pre-made hero, then replays the recorded presses on the new session inside an editor of its own
  opened on the same start (`AreaNewCharacter.Apply`), and closes that editor: the game starts with nothing refundable. A press
  the new session refuses fails the start and names it. The draft and its editor stay, hidden, while the shop is open.
- **Cancel** (or Escape, CM5) goes back to the new-character window, which, as in the original, starts a new character (the
  generic human man; CM6). The name field starts with message 500 again (the original keeps the typed name, NW7: a later
  slice).
- **Keys.** The menu owns the keys: Return accepts, Escape cancels, no letters (CM7). A controller moves over Accept and Cancel
  and confirms; the pages' buttons take the pointer.
- **Original look (ARC-UI-147).** With the game's art the editor is the in-game character sheet's original look
  ([sheet](../ui/original-windows.md#character-sheet), CHW1–CHW9, the pages ARC-UI-076/077) over the draft: art 22 with the draft hero's
  portrait, name, level, the five points, the stats, the alignment gauge and the skills, tech or spells page, and the ten `−` and `+` and
  the four tabs by the pointer, between the strips and the shop's two art buttons Accept (675, 496) and Back (69, 496) of the 800 × 600 screen
  (window 14 has no background of its own and the same buttons as window 15, MW1, MW30, CM2; `AreaShopArtView`). The sheet's window stands at the
  menu screen's (0, 41) at the bars' whole factor (`AreaCharacterSheetArtView.MenuScreen`, as the shop's barter window). Differences from
  the in-game sheet, all original: a release in the portrait's close rectangle does not close the editor (the rectangle is the interface
  modes 1 and 2's, CM3); the pages' presses are the same ones the diagnostic panel records on the draft (`AreaCreationEditor`). PROJECT: the
  original's text bar belongs to the game, so the hover hints are not shown and a refused press writes the original's `charedit.mes` text
  (ARC-UI-078) into the menu's note line at the top of the screen; the controller's focus row lights Accept or Back as in the shop. Without the
  game's art (the synthetic self-tests, a game folder whose sheet or strips art does not read) the diagnostic panel above is shown as before.
  Native smoke (`menu` stage): `ARC-UI-147 native-smoke character editor art PASS` (the draft's sheet and portrait, the window's place, a refused
  `−` written as the note, the close rectangle that does not close, the spells tab, willpower's `+` by the pointer);
  `ARC_AREA_EDITOR_CAPTURE=PATH.png` saves readbacks of the skills and spells pages (PATH-skills.png, PATH-spells.png, ignored local paths).

Self-test inside `ARC-UI-011 self-test PASS synthetic main menu`: Accept opens the editor on the dwarf's draft, `+` on
strength, Cancel back to a new human, then Accept again, strength, a hit point, constitution `+` and `−`, the first skill, a
refused `−` at the floor, and Return starts the dwarf with the presses replayed and the editor closed. Native smoke (`menu`
stage, original data): the elf woman's willpower, a hit point, the first skill and a college 1 spell, the hit point taken
back and a second `−` refused, then Return; `ARC-UI-034 native-smoke character editor PASS` with the started session's values.
Checks: `AreaNewCharacterTests` (draft, record, replay, a refused replay, the menu's window 14), `AreaStatsPageTests`.

### Opening shop (ARC-UI-036)

N10b, slice 4 (FR-CHAR-007, FR-CHAR-015): the original's window 15, which the character editor's Accept opens
([ledger OS1–OS6](../research/ledger/character.md#opening-shop-arc-ui-036)). In the original its opening makes the new character's
kit (`item_generate_inventory`) and opens barter with the first NPC of the shop map the new game runs on behind the menu; its
Accept starts the game, its Back returns to the editor.

- **The kit** (`AreaOpeningKit.Make`, `OpeningKitRules`, OS3, OS4), made from the editor's draft each time the shop opens: the money
  and items of the background the character holds (block 0, 400 gold installed, when no background button was pressed: the shop
  reads the PC's field), then the clothes for its gender and armour size (men 8065, 8049, 8069; women 8134, 8133, 8135), kept
  when the draft's wear check allows them in the body-armour slot (`GameSessionOwner.CheckHeroWear`; the art lookup answered
  yes, as the viewer's start answers it), else the size's jacket (8118, 8110, 8126); then crude lockpicks (15178) for any
  pick-locks points and bandages (15179) for any heal points the editor bought. An item whose prototype is missing is skipped,
  as the original's failed creation; an item with a get or insert-item script, which the transfer to the PC runs, refuses the
  shop with the reason, as a pre-made kit refuses it. The items keep their prototypes' item flags (the original marks the
  background's items identified, OS4: not modelled). The generic start reads the prototype files the kit can make once
  (`AreaHeroLoad.KitPrototypes`: every background's items, the clothes, jackets, lockpicks and bandages).
- **The window** lists the kit: the gold, each item's name (the game's text when the names loaded) with its prototype, the
  clothes chosen and the items skipped; Back (left) and Accept (right), the controller's rows Accept and Back. No letters;
  Return accepts, Escape goes back (CM5, MM6).
- **Accept** (or Return) starts the game with the kit: the session's hero is the new character with the kit's gold and items
  (`AreaNewCharacter.StartHero`, the kit's tables with it), the window's choices and the editor's presses are applied, then the
  start wields the best weapon and the best body armour, each only into an empty slot (`AreaNewCharacter.WieldStart`,
  `GameSessionOwner.WieldStartBest`, the original's 0x5412E0, OS5; the art lookup and insert check answered yes); no other
  slot, unlike a pre-made hero's start, which wields every slot (WB1–WB8). The suit is worn unless it has a move script or item
  spells (then it stays in the pack, as the best-items policy keeps such an item).
- **Back** (or Escape) returns to the editor with the same draft: every point spent before the shop can still be taken back
  down to the character's start (the original reopens the editor on the snapshot it took when the character was made, CM4, OS6;
  owner decision 2026-10-28: points stay free until the game starts, in both rule modes). The kit goes; the next opening makes
  it anew, so it is never doubled.
- **Map transfers and loads.** A new character's map transfer or load reads the generic start again (its kit is empty), so the
  viewer rebuilds the kit's tables (wear, healing, lockpicks, durability, art, item spells, grid, use) from the session's own
  kit items by kit number (`AreaStartingHero.WithSessionKit`).

The merchant's barter on the shop map: [opening shop merchant](#opening-shop-merchant-arc-ui-038) (ARC-UI-038).

Self-test inside `ARC-UI-011 self-test PASS synthetic main menu`: Return in the editor opens the shop on the dwarf's kit (120
gold, the clothes skipped: no prototype reads in the fixture), Back returns to the editor where the strength bought before the
shop is taken back and bought again, Return opens the shop again and Accept starts the dwarf with the gold. Native smoke (`menu`
stage, original data): the elf woman's shop with her background's money and items and the clothes, Back, her willpower taken
back and bought again, Return twice; `ARC-UI-036 native-smoke opening shop PASS` with the kit, the worn armour and the started
session's gold. Checks: `AreaOpeningShopTests`, `OpeningKitRulesTests`, `EquipBestTests`, `PremadeHeroTests`.

### Opening shop merchant (ARC-UI-038)

N10b, slice 5 (FR-CHAR-007, FR-ITEM-014): the opening shop's barter with the shop map's merchant
([ledger OS7–OS10](../research/ledger/character.md#opening-shop-merchant-arc-ui-038)). The original's new game runs on the map list's
`SHOPPING_MAP` behind the menu (the PC stands at its start); each refresh of window 15 makes the kit, then the first NPC of that map
forgets the PC, its inventory and its substitute container's regenerate, and the inventory window opens in barter mode; the start
teleports the PC to the start map.

- **The shop map** (`AreaShopMap.Load`): the map list's type (`MapList.OfType`, OS9: installed 5013 `SHOPPING_MAP`, ShopMap, runtime
  map 14, start 31, 29) names the map, which loads in the background through the viewer's area load once a run, from the moment
  New Character opens; its inventory map gives the seed, the rows and the merchant: the first NPC of the seed whose object flags
  have neither destroyed nor off (OS7; installed: one NPC, price multiplier 58, no source of its own, its chest 2 tiles away with
  source 19). A module without a shop map or a shop map without an NPC starts the game at once, as the original (OS2); a map the
  project cannot load (or whose NPC before the merchant has fields that do not read) leaves the shop with the kit alone and the reason in the window's note (`AreaMainMenu.ShopUnavailable`); while
  it loads the note says so (`AreaMainMenu.ShopLoading`) and Accept starts the game with the kit.
- **The shop's session** (`AreaShopSession.Open`): when the shop opens with its map and the barter data loaded, the new character's
  session is made as the start makes it (this scene's new-game recipe, the kit's hero, the window's choices and the editor's
  presses) but with its actor at the shop map's start, as the original's PC stands there. On it the shop map's seed is attached, the
  merchant forgets the PC (`ForgetReaction`, R6), its inventory regenerates (`GameSessionOwner.RegenerateInventory`, 0x463E20 asked
  directly, OS8: a merchant without a source is only cleared, its persistent items stay) and then its substitute container's
  (installed: 2000 coins and 59 items), and the barter opens as a conversation's does (`AreaBarter.Open`: the container's coins move
  to the merchant, both lists are identified; then the container's restock schedule). The merchant's reaction is its effective
  reaction with the diagnostic starting reaction 50, as the dialog host's (its name from the game's descriptions).
- **The window**: the barter window sits at the top, above the shop's window, which moves to the bottom with a one-line summary
  (the merchant, the map, the hero's gold) and keeps Back and Accept; the barter window has no Close (the shop's Back closes it,
  OS1). The menu keeps the keys (Return accepts, Escape goes back) and a controller moves over Accept and Back; the wares and the
  hero's goods take the pointer. Trades are the barter window's ([barter](#barter-window-arc-ui-050)).
- **Accept** starts the game on the shop's session: the best weapon and body armour into empty slots (a ware bought in the shop
  counts: its wear facts come from its shop row, or for an item the regeneration made from its prototype), then the PC is teleported
  to the start map's tile (`AreaShopSession.Start`, OS5, OS10); the scene's seeds attach and the session is adopted as the start
  adopts a new one. On a scene that is not the start scene the start scene loads first and the session it carries starts there. A
  refused start drops the shop's session with its trades and the shop opens anew.
- **Back** drops the shop's session with its trades (the kit goes too): the next opening makes the kit, the merchant's clear and the
  chest anew, as the original's next refresh redoes them; every point bought before the shop is still free (OS6).
- **Not built.** The bought wares' facts for the start's wield come from the shop scene's own read, and the shop scene's inventory host has no prototype reader (NOT READY there): in play a ware the regeneration made has its wear and use facts from its prototype in the viewer's host (ARC-DEV-305), and one from a shop row has them only while the shop map's rows are
  held (not after the start). The restock event the barter open schedules on the shop map's chest does nothing when it comes due in play
  (the start scene's restock host does not hold the shop map); after a load, whose session has no shop map seed, it postpones a
  game minute at a time, as for any merchant left behind. The window's drop on the map outside it, which the original skips while the menu is open
  (0x574577), does not exist in the viewer's window.

Self-test inside `ARC-UI-011 self-test PASS synthetic main menu`: the synthetic menu has no shop map, so the shop names the reason and
is the kit alone. Native smoke (`menu` stage, original data): the elf woman's shop waits for the shop map, the barter opens with the
merchant's coins and wares, the cheapest affordable ware is bought, Back returns to the editor with the shop's session dropped, the shop
opens anew with the kit's gold, a ware is bought again and Return starts the game on the shop's session on the start map with the ware
and the gold left; `ARC-UI-038 native-smoke shop merchant PASS`. Checks: `AreaShopMerchantTests` (with the opt-in
`Original_shop_map_and_its_merchant`), `RestockStateTests`, `MapTransitionsTests`.

### Opening shop in the original's look (ARC-UI-069)

Menu windows plan item 5, the shop's half (FR-UI-001, FR-CHAR-007): window 15 in the original's look
([ledger MW30](../research/ledger/ui-audio.md#menu-window-art-arc-ui-058)). The window has no background of its own (the strips 336
and 335 only, MW1) and two art buttons in the bottom strip, Accept 321 `NextBut` at the screen's (675, 496) and Back 323 `BackBut` at
(69, 496) (`AreaShopArtLayout`, `AreaShopArtView`), with their frames, push behaviour and sounds as window 12's (GB3, GB4, GB7).
The barter window shows the barter window's original look ([barter window](../ui/original-windows.md#barter-window-done-arc-ui-064))
at the screen's (0, 41), the place of every game window, on the menu screen's factor: the shop session's hero grid and the merchant's
shown object (its substitute container) from the shop map's inventory host with the kit's tables, the items' inventory art (the shop
session's items lay out by their art, as the game's), the merchant's image, name and quote line; a drag buys or sells, as in play.
With the main menu up the original turns on no round view (IW33) and tests no release in its square (IW34): the viewer draws neither
the round view nor its art there, and a release in the square acts as anywhere else. The menu keeps the keys (Return, Escape) and the
controller's focus row lights Accept or Back until the pointer moves (PROJECT); the hints (7003, 7002) are not shown. While the shop
map loads, when the shop is the kit alone, without the game's art (the self-tests) or with the barter window's diagnostic view chosen
in play, the shop keeps its diagnostic window and the docked barter window above it.

Native smoke (`menu` stage, original data): the first shop shows the art with the barter window's original look docked at the
screen's (0, 41), its merchant's wares in the grid and no round view; the pointer over Back lights it and a click on it returns to
the editor; the ware bought in the second shop stands on the hero's side of the original look; `ARC-UI-069 native-smoke shop art
PASS` (`ARC_AREA_SHOP_CAPTURE` saves a readback of the window to the caller's ignored path). Checks: `AreaShopArtTests`.

### Interface bars (ARC-UI-018)

In area (exterior, cave and map mode) the original's in-game interface bars are drawn over a session with the
game's own art (`IntTop`, `IntBotom` and the button art, read through `art\interface\interface.mes`, palette slot 0), as
the 1.0.7.4 executable lays them out (ledger [GB1–GB7](../research/ledger/ui-audio.md#interface-bars-arc-ui-018)): the
top strip at the top of the window and the bottom strip at its bottom, each centred and scaled by the largest whole factor
at which the original's 800 × 600 screen fits the window. A strip takes every press on it, so the world never gets a press
under the bars. Buttons:

| Button | Strip | Does in the viewer |
| --- | --- | --- |
| Character, Logbook, Inventory, Schematics | top, top, top, bottom | Opens its window as `C`, `L`, `I`, `T` do, or closes it when open. Over another of these windows it closes that one first and opens its own (the original's interface mode change). |
| Map | top | The world map window (`W`, map mode on the start map), which does not fit between the strips, so the bars hide while it is open (`W` or Escape closes it); the town map is not built. |
| Combat | bottom | Toggles the hero's combat mode (`R`). |
| Spells | bottom | A toggle: on shows the spell pane (`M`), off hides it; `M` moves the button with the pane. |
| Skills | bottom | A toggle (Spells goes up when it goes down; `K` does the same without the sound): shows the [skill pane](../world/spell-cast-host.md#the-skill-pane-in-the-bottom-strip-arc-ui-1113) (bar mode 2: the pane's art and the four skill-bar buttons) in the message window. |
| Fate Points | top | Opens the [Fate Point window](#fate-point-window-arc-ui-1112) as the key `F` does, or closes it when open. |
| Sleep | top | The sleep window (`S`). |

A button shows its idle, down and lit frames as the original's (GB4). It plays 3000 on the press and 3001 on the release
over it, and acts on that release; a toggle plays 3000 turning on and 3001 turning off (GB3, GB6). The pointer over a
button writes its name from `mes\intgame.mes` (1000–1009) where the original's message window is; there is no hover sound
on the bars. The main menu's, in-play menu's and options window's buttons play 3026 when the pointer enters one and 3027 on
the press (GB7). The bars are drawn over the windows they open, and those windows (character sheet, inventory, logbook,
schematics) lay themselves out in the part the bars leave free; while a conversation, barter, reading, the save list,
the palette, the menu or the world map is open the bars are hidden, and they come back with no button lit or held (a conversation hides them under
Steamwoven only: since ARC-UI-2024 the Original setting `DialogBarStays` keeps them drawn under the dialog, as the original does, lays the conversation panel in the part
they leave free and shows the speaker's description of the dialog start in the description area; the bar's buttons stay without effect while the dialog is open, and
a click on a hotkey slot or a recent-action button, and the A key, end the dialog (an item, skill or spell entry that acts, ARC-UI-2068; the repair button and a spell with the inventory bit too, their window then opens in its place, ARC-UI-2086) and then act as without one, an empty entry or a hero who cannot act leaves it open, the follower portraits and their gauges stay drawn and take no input (ARC-UI-2069, DB5), [ledger DB1-DB5](../research/ledger/ui-audio.md#the-bars-under-the-dialog-arc-ui-2024)). The
diagnostic HUD moves below the top strip and the follower portraits and spell pane above the bottom strip. Since ARC-UI-020 the health and fatigue vials show the PC's
[gauges](#health-and-fatigue-bars-arc-ui-020), and since ARC-UI-023 the bottom strip's message window is the
[message log](#message-log-arc-ui-023), where the button names appear. Since ARC-UI-027 the hot-key slots, the recent-action buttons and
the light button are the [hotkey bar](../world/spell-cast-host.md#the-hotkey-bars-art-and-drag-arc-ui-027) (icons, counts, clicks and the
right-button drag). Since ARC-MAGIC-026 the top strip's five slots are the
[maintain bar](../world/spell-cast-host.md#the-maintain-bar-arc-magic-026): they show the hero's maintained spells and a click on one
ends its spell. Since ARC-UI-1111 the top strip's clock is the original's ([clock](#clock-and-date-arc-ui-1111)). Automated
runs draw no bars unless their smoke asks.

Tests `AreaGameBarTests` (layout, frames, button states and sounds, texts, interface art names; with `DAT_ROOT` the
installed art and texts read). Native smoke on the original data, map mode on the start map with `--main-menu true`:
`ARC_AREA_MAP_SMOKE_ONLY=bar` prints `ARC-UI-018 native-smoke bar PASS` (through the window's GUI: a menu button's 3026 and
3027, a new game as the first pre-made hero, the bars with all 11 arts and 10 texts from the game, Character's hover
name and lit frame, its down frame with 3000, the release with 3001 opening the sheet, Inventory switching from the sheet,
Logbook and Schematics opening and closing their windows, the logbook lying between the strips and taking a press inside
it, Map opening the world map with the bars hidden and back with no hover, Combat in and out of combat mode, Spells showing and hiding
the pane, Fate Points opening and closing its window (and, in the same smoke, the [Fate Point window](#fate-point-window-arc-ui-1112)'s own checks), a press on the strip's clock not reaching the world, every sound started, and the
[clock](#clock-and-date-arc-ui-1111)). Not done: the
hot-key bar (later slices; the vials since ARC-UI-020, the message log since ARC-UI-023, the vials' art, the follower frames
and the hovered portrait since [ARC-UI-024](#panel-art-arc-ui-024); the skill pane since ARC-UI-1113; the buttons' lit art since
[ARC-UI-1364](#bar-buttons-lit-by-game-events-arc-ui-1364)), the town map, the toggle to the compact interface (the viewer shows the
compact panel whenever its bars are not drawn).

### Bar buttons lit by game events (ARC-UI-1364)

The top strip's Character, Logbook, Inventory and Map buttons take their lit art from game events, and the Map button's face follows the hero's
sector, as the 1.0.7.4 executable's light callbacks do (0x556A90, 0x556B70; ledger [GB15–GB17](../research/ledger/ui-audio.md#interface-bars-arc-ui-018)).
`AreaBarLights` (Godot-free, `AreaGameBar.Lights`) keeps the original's five art words and map mode; the viewer applies the events to it each frame
(`TerrainViewerBarLights.cs`, before the bars show or hide, so a hidden strip keeps its state):

| Button | Lit art | Lit by | Put out by |
| --- | --- | --- | --- |
| Character | 561 `Char_ON` | a level-up of the hero (the level-up hook, as the message is posted) | the character sheet open |
| Logbook | 560 | a change in the hero's quests, blessings, curses or reputations | the logbook open |
| Inventory | 559 | an object entering or leaving the hero's held list (not an ammunition merge, not a move inside the list) | the inventory or barter window open |
| Map | 195 (world face; the town face 558 is lit by no single player event) | a newly known world-map area | the map window open |

A light is not stored while the button's own window is open. The session answers `QueryHeroInterfaceStamp` (three process-local stamps over the logbook,
held list and known areas) and the viewer lights on a difference after each revision, because the session's writes are many and spread; a quest request not above the quest's state changes nothing in the session and
lights nothing, as in the original (its public setter passes such a request on to no write: ledger [SQ16](../research/ledger/scripts.md#story-and-time-commands-arc-script-1165-e5-c), ARC-WORLD-1554); a botched mark on a quest the PC never held lights nothing either. A light that waits (a world map light while the town face shows) is drawn when the face changes, as the original's redraw does. A new session (a new game or a load) takes its first stamps as the baseline (nothing lights for what it already holds) and starts from the saved lights, or idle without any; a map transfer keeps the session, so it keeps the lights too. The character sheet of an NPC or a companion is not the hero's and puts nothing out.
The Map button shows art 193 (the town face) in a sector with a town map and art 194 (the world map, the owner's "blue globe") in any other, the
original's rule at sector entry (0x564F60: the sector's town map number, 0 for none); the sector is read per frame, which gives the same face.
**The original blocks nothing by this mode:** the world map gate never reads it (ledger GB17), so no entry is gated here either. `ARC_AREA_MAP_SMOKE_ONLY=bar`
ends with `ARC-UI-1364 native-smoke bar-lights PASS` (a level-up, a quest, a taken object and a newly known area each light their button with the lit art
on the button; the window opens put them out; a light with its window open is not stored). **Saved (ARC-UI-1542, schema 98):** the five lit states and the map face are part of the session snapshot (`barLights`; the original writes them with the interface state, 0x54A1A0, and restores them at 0x54A220). The viewer records them in the session at each change (`RecordBarLights`) and restores them when a loaded session first shows; the Map face then holds until the sector's town map answer changes, a save from an older build loads idle. The quick save smoke (`ARC-WORLD-011 native-smoke quicksave PASS`) lights the Logbook button, turns the face, saves with F7, loads with F8 and finds both again. **Not done:** the reputation and blessing events of other players (no network).

### Clock and date (ARC-UI-1111)

The top strip's clock window, as the 1.0.7.4 executable draws and answers it (ledger
[GB8–GB12](../research/ledger/ui-audio.md#interface-bars-arc-ui-018)); `AreaGameClock.cs` (Core, Godot-free: `AreaClockLayout`,
`AreaGameClock`, `AreaClockText`) and `AreaClockView` on the bar panel.

- **The window.** 128 × 30 at (648, 5) of the top strip, black behind a sky of 512 pixels that repeats the day strip
  (interface art 207) and the day's moon piece (208–215), scrolled so that its centre column is the game time (one pass a game day,
  the sun in the middle at noon), and the 5 pixel needle (216) at (708, 6). The moon piece is chosen by the day's phase (28
  phases, the day turning at noon, GB9). The arts are read from the player's game with the bars' other art; without them the window
  stays black.
- **When it moves.** The window is drawn when the session begins and then an hour of game time after each refresh (the Clock event's
  period, counted from the last refresh and not on the hour; a late refresh happens once and a jump of many hours refreshes once). A
  new session (a new game, a load, a quick load) starts the chain with a refresh (for a load this follows the interface being made again, an inference, GB10). The chain runs under the session also while the bars
  are hidden. The face is not saved.
- **The click.** The window is an invisible silent push button (no press or release sound). Its release over it writes a hover entry of
  the [message log](#message-log-arc-ui-023) (never stored, shown only in the message mode and when no entry was stored in the last
  3 s): `<message 22>: hh:mm:ss   <message 23>: d/m/y` (the installed texts read "Current Time" and "Current Date"; the project's "Time"
  and "Date" stand in without the game's file), the date with no padding, in the order of the system's short date pattern (the
  original asks the system's date order; month/day/year when it is unknown). Leaving the window takes the entry away (the original clears it only in the bottom strip's modes 0, 1, 2 and 8, GB11; the viewer always does); entering it
  writes nothing, and the clock has no name of its own. It opens no menu.
- **Not modelled.** The original ignores every mouse event on another combatant's turn of a turn-based round, the clock's press
  too (GB12); the viewer gates none of the bar buttons so. A window made again with the offset it last drew stays blank in the
  original (GB8); the viewer redraws. The viewer's compact panel (shown while the bars are not drawn) has no clock; whether the original's compact interface keeps one was not read.

Tests `AreaGameClockTests` (layout, moon phases, offsets over a day, slices, the refresh chain, the silent button, the text and date
orders; with `DAT_ROOT` the arts' sizes in `AreaGameBarTests`). Native smoke, part of `ARC_AREA_MAP_SMOKE_ONLY=bar` (prints
`ARC-UI-1111 clock smoke`): the window draws its slices and the needle at the right place and size and is not dark, the pointer over
it writes no name, a press writes nothing and the release writes the line with the game's two labels and the day/month/year order,
no sound plays, leaving it clears the entry, and an hour of game time later the window refreshes once. `ARC_AREA_BAR_CAPTURE=PREFIX`
also saves the strip with and without the window and at three later hours for a graphics report (local files, never committed).

### Experience strip (ARC-UI-1365)

The experience progress strip of the bottom strip, under the hot-key bar, as the 1.0.7.4 executable draws it (ledger
[GB13–GB14](../research/ledger/ui-audio.md#interface-bars-arc-ui-018), the draw 0x556C20); `AreaExperienceStrip.cs` (Core,
Godot-free: `AreaExperienceStripLayout`) and `AreaExperienceStripView` on the bar panel.

- **The pieces.** The hero's progress toward the next level in thousandths (the session's `QueryHeroExperience().Progress`, the rule of
  0x4A6900) is scaled by 11/10; each full hundred draws one whole piece of the original's table in order (interface art 773–782 at their
  places in the strip, from (211, 37) to (555, 37), at the art's own size), and a remainder above 0 draws the table's last row (art 772 at
  (216, 47)) cut to the remainder's percent of its width. Nothing draws at the start of a level or without experience data. The art under
  it is the strip's own (art 184), so nothing is repainted first (the original repaints the rectangle (211, 37) 384 × 14 from it).
- **When it moves.** The view follows the session every frame. The original repaints at its bar refresh events and after an experience write
  only when the character sheet is open (GB14); the viewer redraws at once. The arts are read from the player's game with the bars' other art;
  without them the strip stays empty.

Tests `AreaExperienceStripTests` (the table, the scaling, the whole and cut pieces, the edges, the 32-bit wrap) and `AreaGameBarTests`
(the art numbers). Native smoke, part of `ARC_AREA_MAP_SMOKE_ONLY=bar` (prints `ARC-UI-1365 experience strip PASS`): a new hero starts at
progress 0 with no pieces, an award of half the step draws the pieces the table gives for it with the widths the art says and shows on the
pixels, and a second award of three tenths of the remainder draws more pieces without reaching the level.
`ARC_AREA_BAR_CAPTURE=PREFIX` also saves the bottom strip before the strip and at the two progress values for a graphics report (local
files, never committed).

### Fate Point window (ARC-UI-1112)

The window the Fate Points button and the key `F` open, as the 1.0.7.4 executable builds and answers it (ledger
[FW1–FW6](../research/ledger/ui-audio.md#fate-point-window-arc-ui-1112)); `AreaFateWindow.cs` (Core, Godot-free: `AreaFateLayout`, `AreaFateTexts`,
`AreaFateContent`, `AreaFateButtons`, `AreaFateWindowRules`), `AreaFateView` (the drawing and the pointer) and `TerrainViewerAreaFate.cs` (the opening,
the session calls and the key). The rules and the session calls exist since ARC-WORLD-054 ([fate points](../world/fate-points-runtime.md)).

- **The window.** The interface art 292 at (0, 41) of the original's screen (the bars' whole factor, standing under the top strip), of its art's size;
  twelve lines of `mes\fate_ui.mes` (0..11, one for each fate kind; the project's English names stand in when the file does not read) in the
  white font art 229 at x 11, y 3 + 18 × kind, wrapped at the window's width, and twelve toggle buttons of art 293 at x 223, y 4 + 18 × kind
  (frames idle, down, lit as the bar's, GB4). The window is no interface mode: the world keeps running and clicks outside it reach the world; a press
  over it never does.
- **Opening.** The button and the key `F` call one function: an open window closes; otherwise it opens for a living hero (a dead hero, or none, gets
  a notice in the bar's status line). Neither opens it while another interface window (sheet, inventory, logbook, schematics, sleep), the dialog,
  barter, reading, the world map, the save list, the menu or the palette owns the screen (the key is also ignored while a UI control owns input, as `S`
  is), and a window opened by its own button or key closes it (the original's interface close paths close it with the others, 0x541710, 0x541D40 and
  0x542200, F0 for which events call them). Space closes it and goes no further. PROJECT: it also closes when any of those covers the screen, when
  the hero dies and when the session is replaced.
- **The buttons.** A press flips a button (sound 3000 turning on, 3001 turning off; the release does nothing) and asks the session: on arms the kind
  (`ArmHeroFate`: one point is spent, a kind already armed or no point left is refused; the full heal is applied at once) and off undoes it
  (`UndoHeroFate`: the point comes back, up to 100). After each answer and each frame the buttons are set from the hero's armed kinds, so a refused
  press pops back (lit under the pointer, as the original's pop, FW4), the full heal pops up at once and a kind an event used (a critical hit, a
  skill check, a conversation start, a spell) pops its button up while the window is open (FW6). A kind armed when the window opens shows down.
- **Technical bug, fixed (FW3).** The original makes an already armed kind's button without the toggle flag, so it works as a push button that
  cannot stay armed until the window is opened again. Every button toggles here, in both rule modes (the technical bug rule).
- **Not modelled.** The multiplayer network messages of the buttons (FT3, FT4); a button takes the pointer on its art's rectangle, not its opaque
  pixels; the controller has no focus row in the window; the fate points left are not shown (the original window shows no count either).

Tests `AreaFateWindowTests` (layout, hit areas, the lines, the toggle sounds and looks, the buttons following the armed kinds and the rules' answers,
the open decision). Native smoke, a step of `ARC_AREA_MAP_SMOKE_ONLY=bar` (prints `ARC-UI-1112 native-smoke fate PASS`): after a new game the button
opens the window at (0, 41), every button starts idle, the pointer lights a button, a press arms critical hit and gambling (points spent, 3000), the full
heal is used at once and pops up, a press with no point left is refused and pops back, a second press undoes (the point back, 3001), an event's use of
an armed kind pops its button, Space closes the window and `F` opens and closes it.

### Message log (ARC-UI-023)

The bottom strip's message window shows the original's message log, as the 1.0.7.4 executable keeps and draws it (ledger
[ML1–ML8](../research/ledger/ui-audio.md#message-log-arc-ui-023)); the log itself (`AreaMessageLog`, Core) exists in every area
session, the window only while the [interface bars](#interface-bars-arc-ui-018) are drawn.

- **Entries.** The last ten stored entries are kept; a new one becomes the newest and the shown one, and drawing it plays its
  sound: a level-up entry 3005, a message 3004 (the hero's encumbrance changes, `mes\critter.mes` 11–17). A message equal to the
  newest entry's text (A–Z compared without case) is not stored again: the newest is shown and plays its sound once more. Texts (a level-up entry's
  scheme text) keep at most 199 characters. The viewer's spell lines are a plain text entry (no sound) titled with the project's label "Magic"; the original's
  own spell messages are not built.
- **Drawing.** In the message mode the mode's background art (354) lies at (196, 492) of the 800 × 600 screen; an entry fills
  the text area (211, 503, 383 × 82) with black and draws its icon (438 level up, 442 message, 443 plain, 444 hover) at (217, 510)
  and its texts in the original's rectangles: a level-up entry its title, level and points lines (the scheme text on two
  lines); a text entry splits at its first line break into a title line and a body, or fills the whole column (290, 504, 291 × 78).
  A text entry whose last text does not fit is drawn again without the icon in the wide rectangles (from x 217, 364 wide), cut
  by characters until it fits. ARC-DEV-156 (B27): the texts are drawn in the original's font art (27 for titles, 229 for text, both
  white, tinted to the entry's colour) with its blend, broken at the rectangle's width, drawn while the height takes a line, a one-line
  text flagged right at the rectangle's edge, a pane's hover name centred both ways; the floating tooltip (without the bars) uses
  font 229 too. Without a game folder the viewer's font stands in.
- **Scrolling.** The window's upper half (210, 504, 382 × 41) shows the next older entry, as far back as the log holds, and its
  lower half (210, 545) the next newer one; both are invisible push areas with no sound that act on a release over the area
  pressed.
- **Button names.** The pointer entering a bar button and every move while it is there write the button's name
  (`mes\intgame.mes` 1000–1009) as a hover entry with icon 444, which is never stored and shows only in the message mode and when
  no entry was stored in the last 3 s (real time). Leaving the button takes the name away.
- **Panes.** While Spells (the spell pane, `M`) or Skills is down the window belongs to that pane and draws nothing of the log
  (since ARC-UI-053 the spell pane is drawn there as the original's bar mode 1, with its art, college buttons and spell icons:
  [spell pane](../world/spell-cast-host.md#the-spell-pane-in-the-bottom-strip-arc-ui-053), and since ARC-UI-1113 the skill pane as bar mode 2: [skill pane](../world/spell-cast-host.md#the-skill-pane-in-the-bottom-strip-arc-ui-1113)). A new stored entry puts the
  window back in the message mode, and the next pointer move over the bottom strip (x 10–790, y 441–590) gives it back to the
  pane whose button is still down; a pointer move above the strip gives the spells or skills pane's window to the log (not the
  weapon's spells), as the original's panes own the window only while the pointer is over the strip; the button going up
  returns the window to the log. The viewer shows the weapon button and the weapon's spells in the diagnostic pane above the strip
  with Spells down, and treats them as the original's separate weapon mode while they are open (no return above the strip).
- **Steamwoven fix (both rule modes).** The original leaves the window blank after a button name is taken away and after the
  window returns from a pane, until the next entry or scroll click; the viewer shows the scroll position's entry again there
  (a technical display bug, ML7).

Not ready: entry types 1–3 and 7–13 (listed in ML5), the original's other messages, a held inventory item released over the
scroll areas (the original drops it through 0x575770), the compact interface's message window (F10). Since ARC-UI-024 a
hovered follower's portrait and name take the window while shown ([panel art](#panel-art-arc-ui-024)): the log, its background
included, was hidden until the hover ended. Since ARC-UI-057 the hovered object's window is the log's hover entry
([hovered object](#hovered-object-window-and-highlight-arc-ui-057)): an entry stored meanwhile takes the window at once (the
original's order there was not read). Tests `AreaMessageLogTests` (ring, repeats, the 199-character copy, the quiet
time, scrolling, panes, sounds, layout), `AreaLevelMessagesTests`. Native smoke, part of `ARC_AREA_MAP_SMOKE_ONLY=bar`:
`ARC-UI-023 native-smoke message-log PASS` (three encumbrance entries with the player's texts through the production post, each
drawn with the background, its icon and 3004; a repeat not stored and sounding again; the halves clicked through the window's
GUI; Character's name withheld within 3 s and drawn after, leaving it showing the entry again; Spells taking the window, an entry
taking it back, a move over the strip returning it, a move above the strip giving it to the log, Spells up showing the entry; a
long text drawn wide).

### Hotkey bar (ARC-UI-022)

With an AREA session that has a hero, the keys `1`..`0` activate the session's ten hotkey entries (on the press, not during another critter's turn of a turn-based round) and `A` the latest
recent action ([contract](../world/spell-cast-host.md#the-hotkey-bar-arc-ui-022),
ledger [HK rows](../research/ledger/spell-runs.md#hotkey-bar-arc-ui-022)). Since ARC-UI-027 the bar's slots and recent-action buttons
are drawn on the bottom strip and work with the mouse ([hotkey bar art and drag](../world/spell-cast-host.md#the-hotkey-bars-art-and-drag-arc-ui-027)):
a left click on a slot activates it, a right drag from a slot or the spell pane puts its entry on another slot or off the bar, and a
recent-action button activates its action. Keys stay as stand-ins: `Ctrl`+`1`..`0` puts the latest recent action (a spell pressed in the
spell pane, or the palette's Prowl action; the only way to a skill-bar button, as the skill bar is not drawn) on that entry, `Ctrl`+`Shift`+`1`..`0` empties it, and a key `1`..`0` while the
inventory window holds an item drops the item on the entry (it leaves the grid). The status line lists the entries and the latest
action. Since ARC-UI-032 an item entry shows its inventory art up to 32 × 32 or its type icon ([item icons](../world/spell-cast-host.md#the-hotkey-bars-item-icons-arc-ui-032)).
The cast smoke prints `ARC-UI-022 native-smoke hotkeys PASS`, then `ARC-UI-027 native-smoke hotkey-bar PASS` and
`ARC-UI-032 native-smoke hotkey-item-icons PASS items=… inventory-art=… type-icon=…` (every item the hero holds has an icon).

### Logbook (ARC-UI-008)

The original Logbook key `L` (logical key down; the original's in-game key handler toggles the logbook for the local PC on
`l`/`L`, ledger [LB1](../research/ledger/ui-audio.md#logbook-arc-ui-008)) toggles a read-only diagnostic
logbook of the session's PC in area (exterior, cave and `--area-map`), with the two tabs of the original that
the session can fill: **rumors** and **quests**. What it lists and how each entry looks follow the original logbook
(ledger rows LB2–LB13):

- **Rumors:** every rumor the PC knows, oldest first by the game time it was learned, struck through when the rumor is
  quelled (proven false, the dialog action `RQ`). The text is message 20 × number of `mes\game_rd_npc_m2m.mes`, or of
  `mes\game_rd_npc_m2m_dumb.mes` for a hero of intelligence 4 or less (no fallback to the other file).
- **Quests:** every quest whose state is not Unknown, oldest first by the time of its last change, with its state label;
  accepted and achieved in blue, completed struck through in green, completed by someone else and botched struck through
  in red, mentioned in black. The description is the low-intelligence one of `mes\gamequestlogdumb.mes` when it exists and
  the hero's intelligence is 4 or less, otherwise the normal one of `mes\gamequestlog.mes`; both are read only when the
  module's `rules\gamequest.mes` loads (a file that cannot be read or parsed refuses the texts, project policy).
- Each entry starts with the original's date line (month, day, year, 12-hour time, `January 1, 1885  3:00pm`), the
  quest's state label right after it, and the text on the next line. Equal times are ordered by number (the original's
  order for them is its unstable sort's).

The texts come from the module's own data through the game folder's content stack at the module-load phase
(`LogbookTexts`, loaded once per module in the background, kept over the cave, map transfers and F8); every module and
map works the same, nothing is location-specific. A missing text shows an empty line; the header names what is missing
(no hero: normal texts; no texts loaded; no quest table; texts not decodable as Windows-1252). Month names and state
labels are the project's English words, not the player's `mes\logbk_ui.mes`.

The logbook opens from the world only (no palette, dialog, inventory window, character sheet or UI focus), with or without
a hero (without one the normal texts are read), and never for a dead hero (the original refuses a dead local PC). Like the
character sheet it is modal: the actor gate closes, the clock pauses, a walk stops (the original does not pause; the
viewer's modal windows do). While it is open it owns every key: `L`, `Escape` or a controller cancel closes it, `Tab` or a
controller confirm switches the tab, and the tab buttons switch it with the mouse. The tab is kept across close and open
and starts at the rumors for a new session (a quick load), as the original keeps it until a game reset. It rebuilds
whenever the session, the tab or the loaded texts change and closes when the session ends or its hero dies. The HUD adds
`L logbook` and the last refusal.

The session holds the quelled rumors (game-wide, [story domain](../world/game-session-runtime.md#story-domain), snapshot
schema 19) and the conversation's `RQ` action writes them (`IDialogRumorPort`).

**Not implemented:** no tab (all seven are built: reputations and background since ARC-UI-079, blessings and curses since ARC-UI-080, keys since ARC-UI-081, kills and injuries since ARC-UI-082, see below), the developer quotes page, the logbook button's highlight, scripts' rumor actions (`RU`/`RQ` as reply conditions since ARC-DEV-171, [conversation runtime](../scripting/dialog-conversation-runtime.md#rumor-conditions-arc-dev-171)). Self-test `ARC-UI-008 self-test PASS`
(synthetic texts: open without a hero, order and styles, `Tab`/controller tab switch, keys owned, tab kept and reset,
low-intelligence texts, rebuild on change, dead-hero close and refusal); native smoke after the hero stage (exterior and
map mode, with or without `--hero`): `ARC-UI-008 native-smoke logbook PASS` (the module's texts load; `L`, `Tab`, `Escape`
on the live session; on a detached copy every quest and rumor that has a text is listed with it).

#### Original look (ARC-UI-074)

With the game's art and the message window's fonts read (`TextFonts`, ARC-DEV-156) the logbook shows the original's book instead of the list
(`AreaLogbookArtLayout`, `AreaLogbookArtView`; ledger [LB14–LB17](../research/ledger/ui-audio.md#logbook-arc-ui-008), plan
[original windows](../ui/original-windows.md#logbook-all-seven-tabs-the-pc-lens-and-the-tab-hint-done-arc-ui-074-arc-ui-079082-arc-ui-098)): the side panel and the book with the page art, the seven tab
buttons (the shown tab pressed), the previous and next page buttons, the title and the page numbers in the two page halves, and the entries of
the tab laid out on the spread by the original's rules (one text line between entries, an entry that does not fit the column goes whole to the
next column or spread, only the first entry of a fresh column is cut at the bottom) in the colours of their state with a strike-through for a
quelled rumor, a completed and a failed quest. The window is docked at the screen's (0, 41) at the bars' whole factor. The month names, the quest
state labels and the tab titles come from the player's `mes\logbk_ui.mes` when it has it (otherwise the project's English words). PROJECT: the arrow
and page keys, the mouse wheel and the controller's previous / next turn pages too; a word wider than the column ends its entry there (the art text
break of the dialog windows, MW26); `Tab` and the controller confirm switch between the built tabs; a tab not built (none now) would show an empty
page and a note; a "Diagnostic view" button at the side panel's foot shows the earlier list (and "Original look" returns); the synthetic self-tests, which
have no game folder, keep the list. ARC-UI-098 adds the **PC lens** and the **tab hover hint** (ledger LB23): the open centres the game view on the hero, a round view of it (`AreaRoundView`, the component the inventory window shares) shows in the side panel's rectangle (25, 24, 89 × 89) under the window, the side art leaving that square out and art 198 drawn over it (art 198 is read with the others but is no reason for the list); a left release in the square closes the window with the close sound (a press does nothing); entering a tab button writes that tab's message of `logbk_ui.mes` to the message window as a hover entry (shown only 3 s after the last stored message, WM61) and leaving it takes it away (PROJECT: closing the window with the pointer on a tab clears it too). Self-test `ARC-UI-008 self-test PASS` checks the hints, the round view, the click and its edges; the native `bar` stage checks them with the player's art.

ARC-UI-079 adds the **reputation** tab (the PC's reputations in the order the session stores them, which is the order they were gained: a date line over the
reputation's name in `mes\gamereplog.mes`, plain black; ledger LB18) and the **background** tab (the hero's description text, the part of `mes\gameback.mes`
after the first line feed, cut by the page layout into column-sized pieces at the last space or line feed that fits, no date line; a hero with no description text
or no hero shows an empty page, where the original's code would fault on a missing text). `LogbookTexts` loads the two tables with the other texts; one that does
not load leaves its tab without text and a note names it.

ARC-UI-080 adds the **blessings and curses** tab (ledger LB19): the PC's two lists (`QueryHeroBlessings`) merged oldest first, a blessing before a curse of the
same game time, each entry a date line over the name from `mes\gamebless.mes` or `mes\gamecurse.mes` (message 10 × number; none for a number below 50 or a
missing message); blessings blue, curses red, neither struck (new styles `Blessing` and `Curse`). `LogbookTexts` loads the two tables with the other texts;
without a hero the page is empty with a note. The native smoke holds every blessing and curse of the module's tables on the detached copy and checks the
tab lists them all (up to 64 numbers per table), oldest first.

ARC-UI-081 adds the **keys** tab (ledger LB20): the PC's key numbers (`QueryHeroKeys`, the session's list in the order they were added, repeats kept), each entry
only the key's name from `mes\gamekey.mes` (message = the number; an empty line for a missing message, where the original would fault), plain black, no date line.
The session keeps the list in the story section (`story.heroKeys`, snapshot schema 77, upgrade step 76 adds the empty list; `AddHeroKey` appends, at most 3000 keys).
Since ARC-WORLD-123 a key item the hero takes (pickup, loot, a dialog's item) is folded into the list and the list feeds the open attempt of doors and containers (ledger KY1–KY5, [lock state](../world/lock-state-runtime.md)). NOT READY: the key-ring item and the key that stays loose (the session's hero always holds the ring), the followers' keys and every other source, so in play the page fills from the keys the hero picks up; the native smoke carries
the module's keys on the detached copy and checks the tab lists them in that order.

ARC-UI-082 adds the **kills and injuries** tab (ledger LB21): nine fixed plain lines (the kill total, the victims of the highest and lowest level, of the most positive and
most negative alignment and of the highest and lowest aptitude, each as "label: name" with "-----" for none; a blank line, which ends the left column; the injuries'
head), then the PC's injuries oldest first, each the kind's text and the causer's name, struck through unless current (the hero's life record: blinded, crippled legs,
the crippled arms by the original's shared search, the newest scars by the scar count). Labels come from messages 26..37 of `logbk_ui.mes` (project words without
them), names from the description files (`LogbookTexts.Descriptions`). The session keeps the log (`story.killLog`, snapshot schema 78, upgrade step 77 adds the empty log;
`RecordHeroKill` and `RecordHeroInjury` apply the original's slot rules). ARC-UI-097 feeds the log from the attack steps (ledger LB22): the hero's and an NPC's attack record the kill of an NPC dealt by the hero or a critter it leads, and the scars, blindings and cripplings a hit leaves the hero; NOT READY: kills and injuries by spell damage (not wired to the log), burning, traps and script damage (no attacker), and the dying script's veto. The native smoke carries a few
kills and injuries on the detached copy.

Checks: unit tests `AreaLogbookArtTests` (layout table, spread rules, tab order, labels); the logbook self-test adds a synthetic art set (tab and page
buttons by mouse, spreads, an unbuilt tab, the diagnostic switch); the native smoke prints `ARC-UI-074 native-smoke logbook book` (the installed game's
162 quests and 340 rumors land on 22 and 67 spreads, none cut) and `ARC_AREA_LOGBOOK_CAPTURE=<ignored path prefix>` saves readbacks of the open book
(it adds some rumors and quests to the live session: a look only).

### Schematic window (ARC-TECHNOLOGY-005)

The original Technical schematics key `T` (logical key down; the original's in-game key handler opens the window for the local
PC as crafter and viewer, ledger [SW1](../research/ledger/technology.md#schematic-window-arc-technology-005)) toggles
the schematic window of the session's hero (ARC-UI-142: in the original's look, art 365 with the page buttons, the discipline tabs, the record's art and
text, the two component slots, the assemble button and the lens over the game view, when its art reads; else a diagnostic layout) in area (exterior, cave and `--area-map`), with the learned and
found tabs, the eight discipline tabs, Previous and Next, the record's name and description, the two components (found or
missing, the expertise they need), the product and quantity, and the Assemble button with its state. Contract:
[schematic window runtime](../world/schematic-window-runtime.md).

It opens from the world only (no palette, dialog, other window or UI focus), only with a hero and never for a dead one. Open
plays 3008, the player's close 3009, a discipline or page 3010, a tab 3011 (only from the other tab), a successful assembly
3018 + the current discipline with `schematic_text.mes` 2; a refused button shows `schematic_text.mes` 0 or 1. Like the
character sheet it is modal (gate closed, clock paused, a walk stopped) and owns every key while open: `T`, `Escape` or a
controller cancel closes it, `Tab` or a controller confirm assembles. Each tab keeps its discipline and page across close and
open; a new session (a quick load) starts at the learned tab. Script action 129 of a dialog script opens the window for the PC
once nothing else owns input; for an NPC with a PC leader, and after a conversation ended by the dialog action `su`
(ARC-TECHNOLOGY-006), it opens with that NPC as crafter: the NPC's degrees and own list, an empty found tab, the products into its
list ([NPC crafter](../world/schematic-window-runtime.md#npc-crafter-arc-technology-006)). The data
(`schematic.mes`, `schematic_text.mes`, `tech.mes` and the prototypes the records name) load once per module in the background.
The HUD adds `T schematics` and the last refusal.

**Not implemented:** the original window art, slot pictures and overlays, the interface button, the slot item description box,
the tab and discipline hover texts, an NPC's weight and room for its products. Self-test `ARC-TECHNOLOGY-005 self-test PASS` (synthetic
records and hero: refusal without a hero, `T`, controls and their sounds and bounds, keys owned, refused texts, an assembly
through the session, `Escape`, position kept, controller cancel, script 129 for the PC and an NPC without data, an NPC crafter's
window with its reset, empty found tab and assembly into its list, dead-hero close); native smoke `ARC-TECHNOLOGY-006 native-smoke npc
crafter PASS` (exterior with the cave and `--hero`: a Crash Site speaker opened through the window path `su` takes after its talk ended, one assembly into its list on
a detached copy; the reply pick is covered by `AreaSchematicConversationTests`);
native smoke after the logbook stage: `ARC-TECHNOLOGY-005 native-smoke schematics PASS` (the module's schematic data load; with
`--hero`, `T` opens over the live session, the controls move, `Escape` closes, nothing changes; on a detached synthetic hero
holding a learned record's components one assembly makes its product).

### Barter window (ARC-UI-050)

A conversation reply `b:` ends the talk and opens a diagnostic barter window with the speaker as merchant once nothing else owns input
(area: exterior, cave and `--area-map`; it needs `--hero`). Contract: [barter runtime](../world/barter-runtime.md), ledger
[BT1–BT10](../research/ledger/items.md#barter-arc-ui-050). The header shows the hero's and the merchant's gold, the hero's haggle
effectiveness and training, the merchant's reaction, price multiplier and buy list (`Listed`, `Everything`, `None`, `Emptied`), a fence and
a buy-object script; the left column has a `Sell:` button per hero item with its sale quote, the right column a `Buy:` button per
merchant ware (not worn, not coins) with its buy quote; a quote of 0 shows its reason (`NotDealt`, `WontSell`, `Stolen`, ...) and its
button is disabled; a follower's window is free. A stack trades whole at the unit price × units. A trade runs through the session,
prints `sold …`/`bought … for N` with a reaction change, or `not enough money`, `won't buy (…)`, `the merchant cannot pay`, and requests
the trade sound 8158. Like the schematic window it is modal (gate closed, clock paused, a walk stopped) and owns every key: `Escape`, a
controller cancel or `Close` closes it. A quick load, a map change or a dead hero closes it. The inventory sources and every item
prototype load once per module in the background. The HUD adds `barter open: Escape closes` and the last refusal. ARC-WORLD-090: a
merchant with a substitute container within 20 tiles of where it talked (no leader) shows the container's items (`(stock chest)` in
the right header, the container's buy list in brackets after the merchant's); the open moves the container's coins onto the merchant
and identifies the goods (log line `ARC-WORLD-090 barter stock`); a broken container leaves the merchant's own list, another refused
open shows no window.

**Original look (ARC-UI-064,** [ledger BW1–BW5](../research/ledger/ui-audio.md#barter-window-art-arc-ui-064), plan
[original windows](../ui/original-windows.md#barter-window-done-arc-ui-064); `AreaBarterArtLayout`): with the game's art (every barter
art of `AreaBarterArtLayout.ArtNumbers` read, log `ARC-UI-064 barter art ready=True`) the window in the world draws the original barter
window, the inventory window's 800 × 400 frame in interface mode 1 at its place between the bars: background 235 `Barter` (825
`Barter_Follower` in a follower's window while the hero leads another follower too), the merchant's image at (183, 33) (its race or
flags, as the loot window's; the merchant's even when a substitute container is shown), its name centred on x 215, the quote line at
(89, 128), the shown object's grid (the substitute container's, else the merchant's; worn items left out) of 10 × 7 rows of 32 from
(8, 168) scrolled by the mouse wheel (largest scroll 5, or 89 for a container), the hero's grid, counts and drop and use boxes as in its
own window, the gamble box 345 at (703, 236), and the round view at (16, 17) with art 231 (the open centres the camera on the PC). A
merchant's item pressed and released on the hero's grid is bought, a hero's item released on the merchant's grid sold, both through the
trades above (trade sound, messages on the quote line; PROJECT: a bought item goes to the hero's first free cells, a stack trades whole);
a right release on an item moves it to the other side the same way (IW36); anything else goes back. A pointer resting on an item puts its quote on the quote line (PROJECT text `Buy NAME: N` / `Sell NAME: N` /
`… no price (REASON)`; the original prints a generated phrase of the merchant's); the next quote replaces a trade message, as the
original's hover rewrites its line. A left release in the round view closes the window. ARC-UI-1120 (HK25, BW6): a pointer resting on an item first runs the item's examine script for the hero and draws its window while the bars show (the quote follows either way), and the cycle buttons 757 / 758 of the follower background switch the window to the previous or next follower ([barter runtime](../world/barter-runtime.md#follower-cycle-and-examine-arc-ui-1120)). The `Diagnostic view` switch below the frame
shows the diagnostic window and back (the choice holds until it is switched again, as in the inventory window); a controller reaches
only that switch in the original look (PROJECT: trades without a mouse go through the diagnostic window's buttons); since ARC-UI-069 the opening shop's docked window shows
the original look too ([its section](#opening-shop-in-the-originals-look-arc-ui-069)). Since ARC-UI-1740 the gamble box works ([barter](../world/barter-runtime.md#gambling-arc-ui-1740)): the art follows the pointer (345 rest, 344 when the gate 0x579840 passes for a held ware, 346 when it refuses), the merchant's line shows in the quote line, a release on it bets. NOT READY: the hero's grid moves and its drop and use boxes inside the barter window (the item goes back), the count dialog, the
generated quote phrases, the follower's weight lines, the hero's item released on a slot of the merchant's doll (issue #2138), staking a worn item (issue #2139), the scroll bar's art, a follower's portrait as the image, the original fonts.
**Merchant's doll view (ARC-UI-1917,** [ledger IW29, IW30](../research/ledger/ui-audio.md#loot-window-art-arc-ui-052)**):** the doll and bag buttons 338 and 339 at (8, 123) and (46, 123) are drawn whenever the window shows a merchant or a follower (the shown grid may be a substitute container's); the doll button shows art 337 `Barter_PD` at (0, 157) with the merchant's worn items by inventory art in the nine rectangles of table 0x5CAD78 (the loot windows' rectangles), the covers of the empty slots, the doll's two values and the weapon slot's fill (only where the combat host covers the map: no values in the opening shop, and a body the session's lists do not show as an NPC, such as a follower held elsewhere, draws covers only), and hides the grid; the bag button shows the grid again. The toggle starts clear at every open. ARC-UI-1926 (ledger IW29, IW36): a slot's rectangle hits the location 1000 + slot, a press lifts the merchant's worn item, the resting pointer quotes it (`Buy NAME (worn): no price (Worn)`), and a release on the hero's grid or a right release buys it through `AreaBarter.Buy` (the model's `WornWares`, taken from the merchant itself): below Master haggle the price is 0 and the buy is refused, a follower's window gives it away. NOT READY: a hero's item released on a slot of the merchant's doll goes back (issue #2138). The native smoke stage `barter` checks the buttons, the doll view, the worn vest's lift, quote and refused buy, and its frame in the Legacy, HD and (ARC-UI-1750, recipe `barter_pd.art`) Generated runs (`ARC_AREA_WINDOW_ART`).

**Not implemented in the diagnostic window:** grids, paperdoll and drag and drop, the count dialog, the quote lines, the gamble slot,
the follower menu and cycling ([barter runtime](../world/barter-runtime.md#not-modelled-not-ready)). ARC-WORLD-096: every open schedules
the restock of the window's container (else the merchant), logged as `ARC-WORLD-096 restock schedule`, and a loot open schedules the
looted holder's; the event world runs the restock through the viewer's restock host ([barter runtime](../world/barter-runtime.md#restock-in-the-viewer-arc-world-096)). Self-test
`ARC-UI-050 self-test PASS` (synthetic hero and merchant: modal open with both quotes, keys owned, a sale and a buy through the session
with the trade sound, `Escape`, a replaced session closes it; ARC-UI-064 over a synthetic art set: the original look in mode 1 with
background 235, a resting pointer's quote, a sale and a buy by drag with their coins and sounds, a sale by a right release, a ware
released on its own grid changing nothing, the switch and back, a release on the round view closing the window); native smoke `ARC-UI-050 native-smoke barter PASS` (stage `barter` of
`ARC_AREA_MAP_SMOKE_ONLY`, `--area-map` with `--hero`: a speaker opened through the path a `b:` reply takes, a merchant buying through
its substitute container when no speaker buys itself, its quotes, the open is at most two revisions (the coins and identification, the
restock schedule), `Escape` closes with nothing changed; one sale and one buy on a detached copy, through the container when there is
one; ARC-UI-1809: `ARC-UI-1740 native-smoke barter-gamble PASS` bets the first ware the gate lets through on two fresh copies of the live session, a merchant of Gambling level 0 and one of level 20, and checks that each bet uses the saved roll the live open threw, wins or loses by `roll + difficulty <= effectiveness`, costs nothing on a win and the quote on a loss, and saves the check's own draw as the next roll; a merchant with no Gambling points refuses every ware); with it `ARC-UI-064 native-smoke barter-art PASS` on the live window (mode 1, the background by the follower rule, the merchant's
image and every shown item's art read, the round view shown, the first ware's quote, that ware released on its own grid going back;
`ARC_AREA_BARTER_CAPTURE=PATH.png` saves the window); then `ARC-WORLD-096 native-smoke restock PASS` on the live session: a jump to the container's restock event with the container on
screen schedules it again, with the camera panned until it is off screen the next event regenerates it through its merchant, and the
window opened again shows the new wares; then `ARC-WORLD-103 native-smoke redress PASS`: the first living NPC of the map with an
inventory source of its own whose set the project admits and combat values is scheduled through the window open's path, its event regenerates it and it wields its
best items through the production event world, and the session's list shows the slots its record names (SKIP on a map without such an
NPC; [NPC best items](../world/barter-runtime.md#npc-best-items-after-a-regeneration-arc-world-103)).


### Item identify and repair windows (ARC-UI-1731)

A dialog reply `ii` or `ri` ends the talk (the original's interface-mode change, ledger WN1, WN2) and the viewer opens the **item identify window** (the interface mode `InventoryKind5`) or the
**item repair window** (`InventoryKind6`) over the area session once nothing else owns input and the barter data is loaded
([contract](../world/item-service-runtime.md), ledger IS1–IS7). The window is a PROJECT diagnostic list (`AreaItemServicePanel`: the hero's items, one button each with its quote, the hero's
gold, the NPC's name and the last message); the first click on an item selects and quotes it, the next click on the same item pays and does the service through the model
(`AreaItemService`, [`TerrainViewerAreaItemService.cs`](../../tools/terrain-viewer/Godot/TerrainViewerAreaItemService.cs)). Like the barter window it is modal (gate closed, clock paused, a walk stopped),
is the inventory family at Escape (the limited in-play menu opens and closes it), and closes with a replaced session, a dead hero or a map change; the NPC is held as the talk partner while it is open. The viewer's
self-test stage `AreaItemService` covers both windows over a synthetic hero and merchant (identify, two clicks, Escape, repair, a replaced session); `ARC_UI_ITEM_SERVICE_CAPTURE` (a folder) saves a PNG
of each step for a graphics comparison. ARC-UI-1765: with the game's art the window draws the **original look** (ledger IS8–IS11): the inventory window's frame of modes 5 and 6 (`AreaInventoryGridArea` in its service look, `AreaServiceArtLayout`): background 223, the hero's
grid and wear slots, counts, arrange button and round view as in the own window, without the weight lines and the drop and use boxes, the NPC's image (`HolderImage`, the loot window's rules) at (141, 36) at half size,
the NPC's generated quote line (`AreaConversationHost.PickLine` / `PickClassLine`; the price in the quote's `%d`) in the rectangle (180, 29, 155 × 65), and the remembered item's cells filled dark red. A press on an item
is the click (the first quotes, the next on the same item pays: PROJECT substitute for IS3's undecided event); the switch below the frame ("Diagnostic view") shows the list and back; a release on the round view closes it.
Positions, sizes and hit areas stay the Legacy ones. Without the art (self-tests) or behind the switch the diagnostic list stays. A pad or the keyboard: the D-pad steps over the hero's items and quotes the one it lands on, confirm clicks the remembered item (the second click pays); the switch is the mouse's. `ARC_AREA_ITEM_SERVICE_PROBE=1` runs the self-test stage alone; the native `barter` stage also opens both windows on the real data and checks the original look (`ARC_AREA_SERVICE_CAPTURE=PREFIX` saves PREFIX-identify.png and PREFIX-repair.png).
### Reading window (ARC-UI-049)

The inventory window shows a `Read:` button for each written item the hero holds; the session decides the use
([reading window runtime](../world/reading-window-runtime.md), ledger
[WR1–WR16](../research/ledger/ui-audio.md#reading-window-arc-ui-049)). A schematic document registers and the
inventory message shows `tech.mes` 89 with its name (88 when known); any other kind replaces the inventory with a modal reading
window, as the original's mode change pops the inventory. Script actions 64, 65 and 114 of the dialog, use, tile and sector scripts
open it for a PC viewer once nothing else owns input. The window owns every key: `Escape` or a controller cancel closes it, a book's
`Previous`/`Next`, `Left`/`Right` or `Page Up`/`Page Down` turn two pages; sounds 3008, 3010, 3009. The texts and the written
prototypes load once per module in the background; the HUD adds `reading: Escape closes` while open and the last refusal.

**Not implemented:** the original background, frame and page art, fonts, pixel paging (the panel pages by 700 characters, two pages
a spread), newspaper columns and picture drawing; an item's use script (refused), the item-spell path. Self-test `ARC-UI-049
self-test PASS` (synthetic texts and hero: Read buttons, a schematic document registered and gone, a book replacing the inventory,
page turns with sounds and bounds, keys owned, `Escape`, a script plaque waiting for the palette, controller cancel, a picture art
refused, a dead hero refused); native smoke after the schematic stage: `ARC-UI-049 native-smoke reading PASS` (the texts and the 68
written prototypes load, every readable one builds a view; on a detached synthetic hero one readable item opens in the window and
one schematic document registers).

### Reserved-key hint (ARC-INPUT-003)

An original key that is reserved, not implemented ([keyboard routing](../input/diagnostic-palette.md#keyboard-routing)),
is named in the HUD's sticky header ("Reserved: F10 = Compact interface (source reading only) (NOT IMPLEMENTED)") until the
next key press, so it is visible without scrolling; the detail text keeps its "last reserved key" entry. Native smoke
after the logbook stage (exterior and map mode): `ARC-INPUT-003 native-smoke reserved-key hint PASS` (F10, F1 before
ARC-WORLD-046 made F1–F6 [follower orders](../world/companion-orders-runtime.md#area-viewer), pushed through the viewport's input
path; the notice is in the sticky header, every header line shown inside the panel and the window;
Z, an unmapped key, removes it).

### Sound output (ARC-UI-040)

The viewer plays the original's sounds through the module's [sound catalogue](../audio/sound-catalog.md)
on the original's three channels (effects, voice, music) at the original's volumes
([contract](../audio/viewer-audio.md)); music and ambient play since ARC-UI-041 (below). The palette action **Sound test: next
interface sound** plays the next fixed interface sound by id and reports the outcome; the palette's sound
line shows the output device (or "no output device" in a headless run), the three settings (0–10, default
5, the volume lines of `user://arcanum.cfg`; automated runs use no file unless `AUDIO_VOLUMES_TEST_PATH` is set),
the catalogue and the last result. Self-test marker `ARC-UI-040 self-test audio PASS`; native smoke
marker `ARC-UI-040 native-smoke audio PASS` (exterior AREA and `--area-map`).

### Music and ambient (ARC-UI-041)

The session's music and ambient schemes play in area (exterior, cave, `--area-map`) as in the original:
the map's default pair when a map opens, a sector's pair when the PC enters it, songs by their hour windows, ambient
sounds by chance every 250 ms, combat music when an NPC enters combat mode ([contract](../audio/scheme-player.md)). The
status line shows the slots and voices. Native smoke marker `ARC-UI-041 native-smoke schemes PASS` (map mode).

### Positional event sounds (ARC-UI-042)

The hero's footsteps, a pickup from the ground and the hero's attacks (use, attacking bark, hit, critical, miss, the
target's death) play on their tiles with the original's attenuation and pan around the tile at the centre of the view
([contract](../audio/positional-sound.md)); the status line counts them. Native smoke marker
`ARC-UI-042 native-smoke cues PASS` (exterior AREA and `--area-map`).

### Dialogue voice (ARC-UI-043)

An NPC line in a conversation plays its recorded voice once on the voice channel, by the PC's gender with the female → male
fallback; a picked reply stops it ([contract](../audio/dialogue-voice.md)). The status line names the last voice. The talk
smoke (`--area-map`) checks that a voiced first line starts its voice.

### Interface sounds and volumes (ARC-UI-044)

The logbook plays the original's book sounds (open, tab press, the player's close), a level gained and a quest completed play theirs, and an
item taken from a body plays its pickup sound; the palette has the three volume settings 0–10, applied and saved at once
([contract](../audio/interface-sounds.md)). Native smoke marker `ARC-UI-044 native-smoke interface PASS`.

### Window art warm-up (ARC-DEV-369)

The first opening of the inventory, the character sheet and the logbook cost 0.25–2.2 s because the window's art was read from the game folder and its HD
redraws were resolved on the main thread ([diagnosis O2](../project/status/tools.md)). Since ARC-DEV-369 the viewer prepares that art in
the background after the map is published and the first 30 frames have drawn, one window at a time (inventory, sheet, logbook), so the first opening is a warm one:

- **Worker.** The window's art content is read by its own loader through a `Lazy`, so a window the player opens meanwhile waits for the worker's read instead
  of making a second one, and a window opened first reads on demand as before. With the HD interface on, the worker then resolves every HD frame the window will
  draw (`InterfaceHd.Prepare`: pack file, SHA-256 of the Legacy pixels, PNG decode, size and mask checks through `HdPack.ResolveSprite`, then premultiplication for
  art-font canvases and mipmaps, the same steps `HdInterfaceTexture` takes at its first draw) up to half the processors in parallel.
- **Main thread.** It builds the window's textures as the opening would (the sheet's look is now kept per module, not per panel) and creates the prepared HD
  textures in slices of at most about 3 ms a frame (`InterfaceHd.WarmSlice`). A frame that is not prepared, was prepared for another pack, or was dropped is
  resolved at its first draw exactly as before, and a prepared image nobody took is dropped when the window's step ends.
- **Hero portrait.** Once the session has a hero, its portrait (the sheet's slot) is read on a worker and adopted into the portrait content the way `LoadPortraits`
  would, when that content did not change meanwhile; a different hero is warmed again.
- **Nothing visual changes.** The textures, the pixels, the sizes and the order of drawing are the opening's own; only the time of the work moves. Without a game
  folder (the synthetic self-tests) nothing runs. `ARC_WINDOW_ART_WARMUP=0` turns the warm-up off, to measure the cold opening. The log prints
  `ARC-DEV-369 window art warm WINDOW: worker content … ms, hd N frames … ms; main build … ms, upload … slices … ms (worst … ms)` per window and
  `ARC-DEV-369 portrait warm ID: STATUS`.

Measured on the owner's Mac (packaged-like run of the source viewer, HD interface on with the installed 4× pack, one run each on a loaded machine, so a ranking and
not a contract): first opening to the next drawn frame, cold against warmed-up, inventory 1069 → 85 ms, sheet 2124 → 119 ms, logbook 1764 → 70 ms; the worker takes
0.4–0.6 s of reading and about 1 s of HD resolving per window, the main thread 2–9 ms of building and 15–35 ms of upload in 3–8 slices (worst slice 9 ms). The warm
opening is the panel's own cost (inventory 39 ms, sheet 50 ms, logbook 27 ms inside the opening call). The self-test case `ARC-DEV-369 self-test prepared HD frames`
checks that a prepared frame draws the pixels of a lazily resolved one, that refusals stay refusals, and that a stale or dropped frame resolves at the draw.

## Verification and evidence status

### WORLD camera zoom (ARC-GPAD-004)

WORLD starts in Follow at preferred zoom 2.00 when no valid preference exists.
This is a hypothesis for an assessable actor, not a QHD comfort measurement.
LT out / RT in are admitted complete-frame levels with deadzone 0.10 and full
drive one octave/second. The ratio remains 0.125..8 with nearest filtering (on a map loaded as a window the
smallest is its zoom floor, ARC-RENDER-032, or 1/8 again over its terrain overview since ARC-RENDER-034, [map mode](area-map-mode.md#the-main-world-map-arc-render-024));
camera zoom never changes simulation, asset geometry, UI scale or display DPI.
Numerical 1.00 does not prove physical 1:1 on a HiDPI framebuffer.

The palette (Ctrl+Shift+F9/Start/HUD button) holds the World zoom numeric editor (keyboard text/Enter/
Tab; controller shoulders adjust by 0.05), exact 100% (1.00), 200% (2.00),
300% (3.00), and Fit. Native clicks activate only the clicked control once;
controller A activates the selected action. Keyboard/controller navigation and
native Tab automatically scroll the focused control (including numeric editors)
into view; navigation alone never edits zoom. Modal triggers do not zoom. Since
ARC-INPUT-001 the world wheel and `+`/`-` do not zoom in WORLD (they did, with
factors 1.25/0.8 stopping exactly at 1.00); exact zoom is a palette action.
Standalone preview controls remain unchanged.

Separate `user://world_diagnostic_zoom_v1.json` (an automated run uses its own file,
[ARC-DEV-032](#automated-runs-and-the-players-settings-arc-dev-032)) stores numeric
`{"version":1,"zoom":2.0}` with finite inclusive 0.125..8. Missing, unreadable,
malformed/type/version/range-invalid files fall back to 2.00. Reset scene/Reload retain
the preferred value and reset actor/Follow; Reset preferences persists
input false/false plus zoom 2.00 and does not reset the scene (ARC-INPUT-001). Fit is a session-only Manual/unarmed view and
never overwrites the preferred startup ratio; the next explicit zoom does.
Discrete edits save once, held changes coalesce with 0.25s settled debounce and
shutdown flushing. Pending values survive quarantine/reload; failures are shown
as session-only, not a false persistence success. Resize preserves zoom and only
clamps center. HUD reports wanted/bounded center and bound limitation.

For the GPAD-004 candidate use the same closed endpoint above with literal
`ARCANUM_TERRAIN_EXPORT_TASK=ARC-GPAD-004`. Its package is
`artifacts/world-builds/ARC-GPAD-004/<full-head>/ArcanumTerrainViewer.app` and
selection is `WORLD_ROOT_ID:ARC-GPAD-004:<full-head>`, not a lab or relabelled
WORLD package. In addition to seven terrain gates it requires focused tests,
source/exported original WORLD, source/exported persistence and dual hostfxr.
Lease/capacity/frozen observation/v2 inventory/signature/success-only pointer
rules remain mandatory. Unknown tasks and incomplete/failed gates fail closed.
Explicit WORLD/GPAD task selection permits their historical current pointers
to coexist without deleting either; duplicate authorities within a task,
unsafe sibling pointers and wrong task/head/root bindings still reject.
Every validation child uses owned isolated input and zoom preference paths
before initialization; ordinary validation launches must likewise set both
`WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH` and `WORLD_DIAGNOSTIC_ZOOM_TEST_PATH`
to owned files. Never point checks at owner preferences. Full-sector historical
pixel checks explicitly Fit; default-2 actor framing is a separate assertion.

`--self-test` creates a public in-memory 82x42 asymmetric 8-bpp ART fixture, decodes
it through `ArtDecoder`, uploads it through the production RGBA8 texture path,
and constructs the canvas plan through the same `TerrainScenePlan` placement and
ordering path used for loaded terrain records. Its hand-authored pixels cover
transparent palette zero, row-major orientation, source crop, the one-pixel
anchor destination, 78x40 tile and 80x40 facade footprints, tile-only X flip,
unflipped facade command, borders, and separate layer/Y/X/ordinal winners. The independently stated geometry landmarks include all sector
corners and local `(30,32) -> (80,1240)` before the separate X anchor.
A non-headless Godot run reads the `SubViewport` pixels; the dummy headless
renderer cannot read a `ViewportTexture`, so its output explicitly records
`graphical readback NOT_VERIFIED` rather than claiming rendered pixels.

The same self-test also builds two public synthetic asymmetric actor ART inputs
(Idle and Walk), decodes all 184 group/frame textures through `ArtDecoder`,
then bootstraps a second real `TerrainViewerApp` in a fixed synthetic
`SubViewport` and publishes them through the exact ready-publication method
used by loads. It drives `1`/`2`, facing selection, step, play/pause and
timeline advance for Idle and Walk across all eight requested facings. The
non-headless matrix uses fixture-literal placement and two asymmetric pixels
per reflection state, plus literal terrain and guide colors; it verifies HUD,
cache, camera-transform and stationary caller-input policy. It also republishes
and exercises shared load-failure clearing, a deterministic partial actor-upload
rollback, and live `_ExitTree` through the normal production paths, proving
ownership of exactly 185 textures (one terrain plus 184 actor), exact
replacement disposal, and no double-clear/double-exit disposal. The headless run executes the same
structural matrix but reports pixels `NOT_VERIFIED`; it never turns the
structural result into a pixel claim.

`--native-smoke` uses explicit original inputs, validates seven fixed
representative ordinals through isolated production canvases, then drives the production scene at
1280x800 and 1920x1080, injects keyboard/wheel/drag/reset/fit/F11/Escape events,
checks Camera2D readback, command geometry/cache count, HUD global coordinates,
fullscreen restoration where supported, manual-resize persistence, reload, and
100 input-driven pan/zoom/reset/resize cycles. It requires each viewport readback to have exactly the requested
dimensions and validates a terrain-texture landmark transformed through the
active camera. In combined mode the same exact-size frame must additionally
match an opaque pixel from the reverse-order selected static command; a
terrain-only comparison with the static pass hidden is retained only as a
supplementary control. An opaque clear colour or HUD alone cannot pass. A display clamp
or unreadable terrain landmark is NOT_VERIFIED, never a size PASS. Fullscreen
which cannot transition is also NOT_VERIFIED; failed restoration is a failure.
With
`--native-smoke-screenshot PATH`, it writes filenames containing both requested
and actual dimensions. These game-graphics screenshots
remain local and ignored.

The previously retained blank source/export PNGs are failed historical evidence,
not smoke passes. Current exact-head ARC-RENDER-005 local build evidence lives below
`artifacts/builds/ARC-RENDER-005/<commit>/` with the app and `manifest.json`;
an optional local screenshot directory is created only when a smoke invocation
requests it. `artifacts/builds/ARC-RENDER-005/current.json` points to the exact
commit-bound manifest/app. The artifact tree is ignored and must not be committed
or attached. Evidence distinguishes synthetic checks, original automated checks,
original visual inspection, and physical/readability. Only the first two are
automatable here; local visual and physical/readability observations remain
explicitly NOT_VERIFIED unless a human records them.

## ARC-RENDER-006 closed human validation and export

Use literal `ARCANUM_TERRAIN_EXPORT_TASK=ARC-RENDER-006` at the same DEV001
endpoint. The immutable v2 package is
`artifacts/world-builds/ARC-RENDER-006/<full-head>/ArcanumTerrainViewer.app`.
No new manifest schema or manual registration path is introduced. The thirteen
inherited gates remain mandatory; `human-metadata`, `source-human-original`
and `exported-human-original` add three mandatory structured reports. Current
task authorities can coexist; unknown tasks, unsafe siblings, altered payloads,
duplicate same-task pointers and missing/failed/NOT_VERIFIED gates fail closed.

`world_validate.py --kind human` requires a local independent private original
wire oracle through `RENDER006_HUMAN_ORACLE`. Keep it ignored; it is not shipped
in the app. The non-headless source and exported validators compare all55 used
RGBA buffers and numeric geometry, then the actual combined main viewport for
Idle and all ten Walk frames, all eight facings/mirrors at1280x800,1440x900,
800x600 (264 selections). Comparisons use independently stated foot-anchor and
mirror geometry and exclude transparent/guide/actual HUD regions. Actual
hidden-actor and wrong-facing main-canvas controls must fail comparison.
`--kind human-metadata` emits the separate source metadata gate after the same
independent comparison. No oracle/no graphical renderer yields no human PASS.
Reports contain task, full head, host, profile, authenticated original identity,
coverage and actual viewports; old monster/WORLD PASS lines cannot substitute.

Public `--self-test` retains the standalone184-key monster fixture and adds a
distinct55-key human WORLD fixture through the real ready publication/canvas
and lifecycle. It checks static ordering, control/zoom/modal/focus behavior,
corner envelopes, reload and Nth-upload rollback/disposal without original assets.
Synthetic, headless, automated original graphical and ordinary observations are
separate. Ordinary `tools/arcanum-local launch terrain --mode world-diagnostic`
must use the selected sealed RENDER006 manifest, no automation flags, and owned
isolated preference paths; actual Idle/Walk observation and post-run signature
and complete payload integrity are still mandatory outside the sealed bundle.

## Automated runs and the player's settings (ARC-DEV-032)

A self-test (`--self-test`), a native smoke (`--native-smoke`) or the WORLD settings validation (`--world-settings-test`)
never reads, writes or deletes the player's saved
viewer files: the app marks the process automated before it reads any setting (`ViewerAutomatedStorage`). Each store
then picks its path by `ViewerStoragePaths.Choose` (Core): an explicit test path wins
(`WORLD_DIAGNOSTIC_SETTINGS_TEST_PATH`, `WORLD_DIAGNOSTIC_ZOOM_TEST_PATH`, and as before `ALWAYS_RUN_TEST_PATH`,
`VISUAL_PROFILE_TEST_PATH`, `AUDIO_VOLUMES_TEST_PATH`, `GAME_SETTINGS_TEST_PATH`, `ARC_HD_BUDGET_PATH`); otherwise an automated run uses a fresh
temporary folder of its own process (`arcanum-viewer-automated-<pid>-<id>` under the system temporary folder, deleted when
the viewer app exits); only an ordinary run uses the `user://` file. Per setting:

| Setting | Ordinary run | Automated run without a test path |
| --- | --- | --- |
| Input preferences `world_diagnostic_input_v1.json` | `user://` | the run's folder (starts at the WORLD defaults) |
| Zoom `world_diagnostic_zoom_v1.json` | `user://` | the run's folder (starts at 2.00) |
| Quick saves `saves/`, `saves/map/` | `user://` | the run's folder (stages that test saving use their own slot as before) |
| HD budget `hd_budget_v1.json` | `user://` | none (the contract ceilings) |
| The one settings file `arcanum.cfg` (always run, visual profile, volumes, game settings, launcher; ARC-UI-013, ARC-UI-1130) | `user://` | none, as before (off, Legacy, defaults) |
| HD pack folders | discovered | not searched, as before |

So a zoom the player saved (for example 1.70) no longer changes a self-test, and a self-test's own zoom steps no longer
land in the player's file. The self-test checks it first and fails with `SELF_TEST_SETTINGS_NOT_ISOLATED` when the zoom or
input store would use a `user://` path; the log prints `ARC-DEV-032 self-test PASS settings isolated (the zoom and input stores use no user:// file)` and
`ARC-DEV-032 automated run: settings and saves are isolated from the player's files` when the folder is first used. The
WORLD settings validation launch still requires both explicit test paths. Checks: `ViewerStoragePathsTests`.

## The one settings file (ARC-UI-1130)

The player's settings are one file, `user://arcanum.cfg`, in the original's format (ledger OP8, OP9): `name=value` lines, one line a
setting (`ArcanumConfig`, `Arcanum.Session.Settings`, Godot-free). The settings the original registers keep its names and value shape
(`difficulty`, `turn-based`, `auto attack`, `always run`, `text floaters`, `float speed`, `effects volume`, `voice volume`, `music volume`;
`ViewerSettingsConfig`), the project's own have project names (`hd interface`, `generated interface`, `hd pack`, `hd pack path`,
`visual profile`, and `launcher`, the launcher object of [the launcher screen](#launcher-screen-arc-ui-014) as compact JSON on one line).
A save changes only the lines of its own group and keeps every other line (a newer build's included) in its place, through a temporary file
in the same folder and a rename; a line that is not a usable pair is dropped by the next write (as the original's rewrite does); a
missing line, or a value outside its range, reads as that setting's default and no other setting changes. Settings stay saved at once, not
at exit (the original writes at exit; owner decision, FR-LIFECYCLE-004). The file is the project's own: the original's `arcanum.cfg` in the
installation is never read or written, and the file never holds saves or game state.
A byte-order mark is ignored and a line with a byte that is not UTF-8 is dropped alone, so one bad byte never costs another line; a file
longer than 1 MiB is replaced by the next write, and a file that exists but cannot be read is never replaced by an ordinary write (only
the launcher's Reset replaces it, holding the launcher line alone). A write that was cut off leaves a temporary file, removed by a later write
once it is an hour old. The settings are for one running game: two instances saving different groups at the same moment can lose one change
(no lock). A folder path the file cannot hold (white space at an end, a control character) is refused by the launcher's Custom choice.

The first ordinary start after this change moves the settings of the files the viewer kept before (`game_settings_v1.json`,
`audio_volumes_v1.json`, `always_run_v1.json`, `visual_profile_v1.json`, `launcher_v1.json`, in `user://`) into `arcanum.cfg`, once, only
when `arcanum.cfg` does not exist yet (`ViewerSettingsFile.UserFile`, `ViewerSettingsMigration`): each old file is read by its own old codec,
an invalid one is skipped, and the old files stay where they are, unread and unchanged (the player may delete them). The test variables
`GAME_SETTINGS_TEST_PATH`, `AUDIO_VOLUMES_TEST_PATH`, `ALWAYS_RUN_TEST_PATH`, `VISUAL_PROFILE_TEST_PATH` and `LAUNCHER_SETTINGS_TEST_PATH`
keep their names and each now names a settings file; an automated run without one has no file. **Not in the file** (own files, own
owners): the WORLD diagnostic's input and zoom preferences (`world_diagnostic_*_v1.json`, development diagnostics with their own validation),
the HD device budget (`hd_budget_v1.json`, a measurement, not a setting) and the labs' files. Checks: `ArcanumConfigTests`,
`LauncherSettingsTests`, `ViewerSettingsConfigTests`, the self-test's store sections.

The same automated runs keep drawing, at full speed, while the window cannot draw (minimized): see
[minimized window draw](../workflow/toolchain.md#minimized-window-draw-arc-dev-045) (ARC-DEV-045), and take no system
pointer events: see [system pointer in automated runs](../workflow/toolchain.md#system-pointer-in-automated-runs-arc-dev-060)
(ARC-DEV-060).

## Reproducible local verification

Initialize `DOTNET`, `GODOT` and `godot_canonical` from the shared toolchain, and provide the
caller-selected `MODULE`, terrain `RESOURCE`, actor `ACTOR_RESOURCE`, and safe `SOURCE_ID` only in the local
shell. Do not place those paths in a manifest, commit, or issue.

    "$DOTNET" restore tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln --locked-mode
    "$DOTNET" build tools/terrain-viewer/Godot/Arcanum.TerrainViewer.sln --no-restore
    "$DOTNET" test tests/Arcanum.TerrainViewer.Tests/Arcanum.TerrainViewer.Tests.csproj --no-build
    godot_canonical --headless --path tools/terrain-viewer/Godot -- --self-test
    godot_canonical --path tools/terrain-viewer/Godot -- --self-test
    godot_canonical --path tools/terrain-viewer/Godot -- --native-smoke --module "$MODULE" --resource "$RESOURCE" --source-id "$SOURCE_ID"
    godot_canonical --path tools/terrain-viewer/Godot -- --native-smoke --mode actor-diagnostic --module "$MODULE" --resource "$RESOURCE" --actor-resource "$ACTOR_RESOURCE" --prototypes "$PROTOTYPES" --source-id "$SOURCE_ID" --actor-map-x 0 --actor-map-y 0 --actor-camera-origin-x 0 --actor-camera-origin-y 0 --actor-offset-x 0 --actor-offset-y 0 --actor-render-branch normal-uncached --actor-scale-percent 100 --actor-shrunk false --actor-action idle --actor-facing 0 --actor-frame 0

After committing the source candidate, configure the ignored artifact root with `tools/arcanum-local configure add-artifact-root --id ID PATH` and use `ARCANUM_LOCAL_ROOT_ID=ID tools/terrain-viewer/Godot/tools/validate-macos.sh` with effective `TMPDIR` for the bounded exact-head ARC-DEV-001 endpoint. It proves/checks actual export/temp/build/cache devices, leases the candidate, validates source/export/runtime/codesign WITHOUT a manifest, freezes sampled observation, then runs manifest-only finalization and success-only registration/publication. Budget uses verified accepted/current output +10% or one complete configured payload estimate (`ARCANUM_OUTPUT_BYTES`/`ARCANUM_ESTIMATE_MANIFEST`), never aggregate history/source size. Godot/.NET temporary cache coverage is incomplete: `observedTemporaryBytes=null`/unavailable is distinct from both fallback estimates and measured zero. Finalized/ambiguous partial same-head candidates reject intact; review their artifacts read-only. See [local workflow](../workflow/local-development.md). ART remains next-export-only; this endpoint is Terrain-only. The manual ARC-RENDER-005 commands below remain historical diagnostic detail, not a substitute for that registration path. They export to the ignored
`artifacts/builds/ARC-RENDER-005/<commit>/ArcanumTerrainViewer.app`, run its
headless and non-headless `--self-test`, then each terrain-only, combined, and
actor-diagnostic `--native-smoke` plus the ordinary actor launch with the same
local arguments, then run
`codesign --verify --deep --strict --verbose=2` on that app. Record source and
export results, requested/actual dimensions, measured load milliseconds,
distinct texture count, checked RGBA bytes, maximum concurrency, and pre/post
fingerprint equality in the ignored schema-v1 manifest. `current.json` may be
updated only to that exact commit-bound directory.

For the macOS export, keep the SDK selection process-local. This avoids an
editor-configured alternate SDK without changing global Godot or .NET settings:

    SOURCE_HEAD=$(git rev-parse HEAD)
    BUILD_ROOT="artifacts/builds/ARC-RENDER-005/$SOURCE_HEAD"
    env -i HOME="$HOME" USER="$USER" TMPDIR="$TMPDIR" LANG="${LANG:-en_US.UTF-8}" \
      PATH="$HOME/.local/share/dotnet:/usr/bin:/bin:/usr/sbin:/sbin" \
      DOTNET_ROOT="$HOME/.local/share/dotnet" DOTNET_HOST_PATH="$HOME/.local/share/dotnet/dotnet" \
      DOTNET_MULTILEVEL_LOOKUP=0 __GODOT_SHELL_ENV_SET=1 "$GODOT" --headless \
      --path tools/terrain-viewer/Godot --export-release "macOS" "$BUILD_ROOT/ArcanumTerrainViewer.app"
    # (ARC-RENDER-005 used --export-debug "macOS debug"; the preset is now the release export.)
    "$BUILD_ROOT/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer" --headless -- --self-test
    "$BUILD_ROOT/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer" -- --self-test
    "$BUILD_ROOT/ArcanumTerrainViewer.app/Contents/MacOS/Arcanum Terrain Viewer" -- \
      --native-smoke --native-smoke-screenshot "$BUILD_ROOT/evidence/export-native-smoke.png" \
      --module "$MODULE" --resource "$RESOURCE" --source-id "$SOURCE_ID"
    codesign --verify --deep --strict --verbose=2 "$BUILD_ROOT/ArcanumTerrainViewer.app"
    python3 -m json.tool "$BUILD_ROOT/manifest.json"
    python3 -m json.tool artifacts/builds/ARC-RENDER-004/current.json

Before treating an export as usable, inspect its Godot export log for canonical
SDK `8.0.425` and no Homebrew SDK path, and verify `libhostfxr.dylib` exists in
both exported architecture payloads. Run source and exported native smoke with
separate ignored screenshot bases and record requested and actual readback
dimensions. A clamped display remains `NOT_VERIFIED`, not a 1920x1080 pass.

## Provenance and limitations

On 2026-09-29 the owner said the combined terrain/five-object image looked
normal. This is a bounded human observation only; it does not establish
original parity, exhaustive stability, controller behavior, Bluetooth
reconnect, drift, or all axes/settings.

This uses the project's independently authored Data APIs (`DatArchive`,
`TerrainLoader`, `TerrainGeometry`, and `ArtDecoder`). The documented diagnostic
placement conventions are source-reading evidence (F0); no mechanical
translation is included, and this makes no clean-room or independent-corroboration claim.
Original inputs, extracted frames, archive paths, screenshots, and toolkit
payloads remain local. The viewer implements its authenticated finite
five-object and fixed-profile actor/WORLD diagnostic policies, not general
scenery, roofs, collision, navigation, gameplay, dialogue, archive precedence,
or original scene/visual parity. WORLD controller integration is diagnostic
only; physical usability is not established by automation.

The actor diagnostic uses the owner-authorized fixed Direct/SingleLayer
Idle/Walk profile described by [actor visual evidence](../research/actor-visual-evidence.md).
Raw ART FPS is only a preview clock; it does not establish a source scheduler,
movement speed, world movement, collision, passability, or gameplay. Its HUD
labels the caller-selected anchor/render policy and **Original placement parity:
NOT VERIFIED**. This authorized presentation subset does not revise the
historical NOT READY conclusion for original-runtime parity.

## Follower Skills (ARC-COMBAT-1777)

Follower Skills slice 2 (ledger PL5, SP3, OH4; the chooser 0x4C9050, its setting getter 0x4C8FA0). The Game page's seventh row (`AreaOption.FollowerSkills`, settings-file line `follower skills`, default on, `AreaGameSettings.FollowerSkills`) switches it. With it on and no Ctrl key held
(`FollowerSkillsAllowed`: the viewer cannot tell the left Ctrl key, which the lockpick use tests, from the right one, which the Disarm Traps press also tests), two presses go to the follower with the strictly best effectiveness minus difficulty (`SkillCheckRules.Score` of the request
the use would run, `GameSessionOwner.QueryPickLockScore` and `QueryDisarmScore`; the hero scored first with its local-PC term, `AreaAiHost.ChooseSkillUser` over the hero's followers the host runs, `FollowerSkillRules.Choose`: a tie keeps the hero or the earlier follower):

- **The lockpick use** on a door, window or container (`HandOverLockpick`, called by the two armed-lockpick presses before the hero's own skill goal): the follower's skill goal 33 with skill 14 starts (`AreaAiHost.StartPickLocks`, its own lockpicks if it holds any, `FirstLockpicks`, else none), so it walks within range 1 and picks at its attempt node with
  `PickLockByNpc`; the hero does not move, the pick's cost is the follower's goal's.
- **The skill bar's Disarm Traps** (`DisarmByFollower`, in `PerformDisarm`): the hero's press pays its 4 action points in a round (`ChargeHeroAction`, the original's 0x57A320), then `AreaTraps.DisarmByNpc` rolls the follower's own check at once from where it stands (`RollNpcDisarmTrap`), a sprung native trap takes effect with the follower as
  the triggerer and an ordinary trap script runs with it.

The hand-over line (random line 4700..4799 of the follower's dialog override or of the gender pair's gd_npc file, 0x4139A0) floats over the follower through the follower-comment float (`SayFollowerLine`, with its can-speak head test). NOT READY: the lockpicks the candidates are scored with are the ones the hero armed (the original takes the PC's first lockpicks in inventory order); the original's list recurses into followers of followers (the world holds the hero's own);
the original starts the use-item goal (0x28, no animation) for a follower holding lockpicks and the timed goal for one that does not (both run as goal 33 here); an expert follower's critical-success trap device lands in the hero's pack, not its own; the open parts are [#1878](https://github.com/Simarglok/arcanum-steamwoven/issues/1878); a pick by a follower has no witnesses ([#1860](https://github.com/Simarglok/arcanum-steamwoven/issues/1860)).
Smoke: the `options` stage checks the row and its saved line, the `disarm` stage's last step joins a following NPC, forces it to score best (`FollowerSkillsScoreForSelfTest`) and checks the hand-over, Ctrl and the option off; unit tests `FollowerSkillRulesTests`, `FollowerSkillsStateTests`, `AreaAiFollowerTests`.
